using System.Globalization;
using System.Text;
using System.Text.Json;
using Saphir.Server.Data;
using Saphir.Server.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Judiciaire;

namespace Saphir.Server.Modules.Judiciaire;

/// <summary>Contenu modifiable d'un acte (JSON dans lr_actes.contenu).</summary>
internal sealed record ContenuActe(
    Dictionary<string, string?> Champs,
    List<ActeAgentDto> Agents,
    List<QuestionReponseDto> QuestionsReponses,
    List<long> PiecesIds);

/// <summary>
/// Actes LRPPN (14, 15) : création préremplie, brouillons versionnés, relecture, correction, validation (acte figé, règle 10),
/// mentions, texte, PDF, indexation ANADOC (20.5) et inscription TAJ du mis en cause (20.4).
/// </summary>
public sealed class ActesStore(SaphirDb saphirDb, ParametresService parametres)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static DateTime Maintenant => DateTime.UtcNow;

    internal const string ColonnesLigne =
        """
        a.id, a.procedure_id, pr.numero AS procedure_numero, a.numero, a.type, a.titre, a.statut, a.redacteur, a.maj_le, a.valide_le, a.valide_par,
        p.nom AS p_nom, p.prenom AS p_prenom
        """;

    internal const string JointuresLigne =
        "FROM lr_actes a JOIN lr_procedures pr ON pr.id = a.procedure_id LEFT JOIN per_personnes p ON p.id = a.personne_id";

    internal static ActeLigneDto LireLigne(SqlRow r) => new(
        r.Long("id"), r.Long("procedure_id"), r.Text("procedure_numero"), r.Text("numero"), r.Text("type"), ActesCatalogue.Libelle(r.Text("type")),
        r.Text("titre"), r.Text("statut"), r.Text("redacteur"),
        r.NullableText("p_nom") is { } nom ? $"{nom} {r.Text("p_prenom")}" : null,
        r.Date("maj_le"), r.NullableDate("valide_le"), r.NullableText("valide_par"));

    private static ContenuActe LireContenu(string? json)
    {
        ContenuActe? contenu = null;
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                contenu = JsonSerializer.Deserialize<ContenuActe>(json, Json);
            }
            catch (JsonException)
            {
                contenu = null;
            }
        }
        return new ContenuActe(
            contenu?.Champs ?? new Dictionary<string, string?>(),
            contenu?.Agents ?? new List<ActeAgentDto>(),
            contenu?.QuestionsReponses ?? new List<QuestionReponseDto>(),
            contenu?.PiecesIds ?? new List<long>());
    }

    private static string Ecrire(ContenuActe contenu) => JsonSerializer.Serialize(contenu, Json);

    private static string LibelleAgent(AgentDto agent) => $"{agent.Nom} {agent.Prenom}";

    // =====================================================================================
    // Lecture
    // =====================================================================================

    public async Task<List<ActeLigneDto>> ListerAsync(string? statut, long? redacteurId, string? q, CancellationToken ct)
    {
        var sql = new StringBuilder($"SELECT {ColonnesLigne} {JointuresLigne} WHERE 1 = 1");
        var parametresSql = new List<(string Name, object? Value)>();
        if (!string.IsNullOrWhiteSpace(statut))
        {
            sql.Append(" AND a.statut = @statut");
            parametresSql.Add(("@statut", statut.Trim()));
        }
        if (redacteurId is { } redacteur)
        {
            sql.Append(" AND a.redacteur_id = @redacteur");
            parametresSql.Add(("@redacteur", redacteur));
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            sql.Append(" AND (a.numero LIKE @q OR a.titre LIKE @q OR pr.numero LIKE @q OR p.nom LIKE @q)");
            parametresSql.Add(("@q", Sqlj.Like(q)));
        }
        sql.Append(" ORDER BY a.maj_le DESC LIMIT 300");
        await using var db = await saphirDb.OpenAsync(ct);
        return await db.QueryAsync(sql.ToString(), LireLigne, ct, parametresSql.ToArray());
    }

    public async Task<ActeDto> ActeAsync(long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await LireActeAsync(db, id, ct);
    }

    private static async Task<ActeDto> LireActeAsync(SqlSession db, long id, CancellationToken ct)
    {
        var acte = await db.FirstOrDefaultAsync(
            """
            SELECT a.id, a.procedure_id, pr.numero AS procedure_numero, pr.intervention_id, pr.intervention_numero, a.numero, a.type, a.titre,
                   a.statut, a.personne_id, a.mis_en_cause_id, a.redacteur_id, a.redacteur, a.redacteur_rio, a.redacteur_grade, a.redacteur_service,
                   a.contenu, a.version, a.cree_le, a.maj_le, a.valide_le, a.valide_par, a.empreinte, a.texte_fige,
                   p.nom AS p_nom, p.prenom AS p_prenom, p.reference AS p_reference,
                   m.nom AS m_nom, m.prenom AS m_prenom, m.reference AS m_reference
            FROM lr_actes a
            JOIN lr_procedures pr ON pr.id = a.procedure_id
            LEFT JOIN per_personnes p ON p.id = a.personne_id
            LEFT JOIN per_personnes m ON m.id = a.mis_en_cause_id
            WHERE a.id = @id
            """,
            r =>
            {
                var contenu = LireContenu(r.NullableText("contenu"));
                return new ActeDto(
                    r.Long("id"), r.Long("procedure_id"), r.Text("procedure_numero"), r.Text("numero"), r.Text("type"), r.Text("titre"), r.Text("statut"),
                    r.NullableLong("personne_id"),
                    r.NullableText("p_nom") is { } pn ? $"{pn} {r.Text("p_prenom")} ({r.Text("p_reference")})" : null,
                    r.NullableLong("mis_en_cause_id"),
                    r.NullableText("m_nom") is { } mn ? $"{mn} {r.Text("m_prenom")} ({r.Text("m_reference")})" : null,
                    r.NullableLong("redacteur_id"), r.Text("redacteur"), r.NullableText("redacteur_rio"), r.NullableText("redacteur_grade"),
                    r.NullableText("redacteur_service"), contenu.Champs, contenu.Agents, contenu.QuestionsReponses, contenu.PiecesIds,
                    r.Int("version"), r.Date("cree_le"), r.Date("maj_le"), r.NullableDate("valide_le"), r.NullableText("valide_par"),
                    r.NullableText("empreinte"), r.NullableText("texte_fige"), Array.Empty<MentionDto>(),
                    r.NullableLong("intervention_id"), r.NullableText("intervention_numero"));
            }, ct, ("@id", id)) ?? throw ApiException.Introuvable("Acte introuvable.");
        var mentions = await db.QueryAsync("SELECT id, texte, auteur, cree_le FROM lr_actes_mentions WHERE acte_id = @id ORDER BY cree_le",
            r => new MentionDto(r.Long("id"), r.Text("texte"), r.Text("auteur"), r.Date("cree_le")), ct, ("@id", id));
        return acte with { Mentions = mentions };
    }

    private sealed record EtatActe(long ProcedureId, string Numero, string Type, string Statut, int Version, long? RedacteurId, string Contenu);

    private static async Task<EtatActe> VerrouillerAsync(SqlSession db, long id, CancellationToken ct)
        => await db.FirstOrDefaultAsync(
            "SELECT procedure_id, numero, type, statut, version, redacteur_id, contenu FROM lr_actes WHERE id = @id FOR UPDATE",
            r => new EtatActe(r.Long("procedure_id"), r.Text("numero"), r.Text("type"), r.Text("statut"), r.Int("version"),
                r.NullableLong("redacteur_id"), r.Text("contenu")), ct, ("@id", id))
           ?? throw ApiException.Introuvable("Acte introuvable.");

    private sealed record ProcedureActe(long Id, string Numero, string Intitule, string? Service, string? Qualification, DateTimeOffset? FaitsLe,
        string? Commune, string? Adresse, string Cadre, string Responsable, string? InterventionNumero);

    private static async Task<ProcedureActe> LireProcedureAsync(SqlSession db, long id, CancellationToken ct)
        => await db.FirstOrDefaultAsync(
            "SELECT id, numero, intitule, service, qualification, faits_le, commune, adresse, cadre, responsable, intervention_numero FROM lr_procedures WHERE id = @id",
            r => new ProcedureActe(r.Long("id"), r.Text("numero"), r.Text("intitule"), r.NullableText("service"), r.NullableText("qualification"),
                r.NullableDate("faits_le"), r.NullableText("commune"), r.NullableText("adresse"), r.Text("cadre"), r.Text("responsable"),
                r.NullableText("intervention_numero")), ct, ("@id", id))
           ?? throw ApiException.Introuvable("Procédure introuvable.");

    // =====================================================================================
    // Création (14.1) et brouillons
    // =====================================================================================

    public async Task<ActeDto> CreerAsync(AgentContext ctx, long procedureId, ActeCreationInput input, CancellationToken ct)
    {
        var type = ActesCatalogue.Trouver(input.Type) ?? throw ApiException.Saisie("Type d'acte inconnu.");
        long id;
        await using (var db = await saphirDb.OpenAsync(ct))
        {
            await db.BeginAsync(ct);
            await LrppnStore.ProcedureOuverteAsync(db, procedureId, ct);
            var procedure = await LireProcedureAsync(db, procedureId, ct);

            var champs = PreremplirDepuisProcedure(type, procedure);
            if (input.PersonneId is { } personneId)
            {
                var fiche = await PersonnesStore.LireFicheAsync(db, personneId, ctx.Droits, ct) ?? throw ApiException.Introuvable("Personne introuvable.");
                foreach (var (cle, valeur) in ActesCatalogue.ChampsIdentite(type.Code, fiche))
                    if (!string.IsNullOrWhiteSpace(valeur))
                        champs[cle] = valeur;
            }

            var agent = ctx.Agent;
            var agents = type.SectionAgents is null
                ? new List<ActeAgentDto>()
                : [new ActeAgentDto(agent.Id, "Rédacteur", LibelleAgent(agent), agent.Rio, agent.Grade, agent.Service)];
            var contenu = Ecrire(new ContenuActe(champs, agents, [], []));

            var ordre = (int)await db.LongAsync("SELECT COALESCE(MAX(ordre), 0) + 1 FROM lr_actes WHERE procedure_id = @id", ct, ("@id", procedureId));
            var numero = JudiciaireRegles.NumeroActe(procedure.Numero, ordre);
            id = await db.InsertAsync(
                """
                INSERT INTO lr_actes (procedure_id, ordre, numero, type, titre, statut, personne_id, redacteur_id, redacteur, redacteur_rio,
                    redacteur_grade, redacteur_service, contenu, version, cree_le, maj_le)
                VALUES (@procedure, @ordre, @numero, @type, @titre, 'Brouillon', @personne, @redacteurId, @redacteur, @rio, @grade, @service,
                    @contenu, 1, @maintenant, @maintenant)
                """, ct,
                ("@procedure", procedureId), ("@ordre", ordre), ("@numero", numero), ("@type", type.Code), ("@titre", type.Libelle),
                ("@personne", input.PersonneId), ("@redacteurId", agent.Id), ("@redacteur", LibelleAgent(agent)), ("@rio", agent.Rio),
                ("@grade", agent.Grade), ("@service", agent.Service), ("@contenu", contenu), ("@maintenant", Maintenant));
            await EcrireVersionAsync(db, id, 1, ActeStatuts.Brouillon, contenu, ctx.Auteur, "Création", ct);
            if (input.PersonneId is { } p)
                await LrppnStore.AssocierPersonneAsync(db, ctx.Auteur, procedureId, p, JudiciaireRegles.QualitePersonneActe(type.Code), null, ct);
            await Historique.EcrireAsync(db, ctx.Auteur, ObjetsJudiciaire.Acte, numero, "Création", "statut", null, ActeStatuts.Brouillon, type.Libelle, ct);
            await db.ExecuteAsync("UPDATE lr_procedures SET maj_le = @maintenant WHERE id = @id", ct, ("@maintenant", Maintenant), ("@id", procedureId));
            await db.CommitAsync(ct);
        }
        return await ActeAsync(id, ct);
    }

    /// <summary>Champs repris de la procédure (les données proposées doivent être vérifiées par le rédacteur).</summary>
    private static Dictionary<string, string?> PreremplirDepuisProcedure(TypeActeInfo type, ProcedureActe p)
    {
        var maintenant = DateTimeOffset.UtcNow;
        var faits = p.FaitsLe ?? maintenant;
        var lieu = string.Join(", ", new[] { p.Adresse, p.Commune }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var valeurs = new Dictionary<string, string?>
        {
            ["c_date"] = HeureLocale.Date(faits),
            ["c_heure"] = HeureLocale.Heure(faits),
            ["c_commune"] = p.Commune,
            ["c_adresse"] = p.Adresse,
            ["c_cadre"] = p.Cadre,
            ["c_motif"] = p.Qualification ?? p.Intitule,
            ["b_intervention"] = p.InterventionNumero,
            ["f_service_saisi"] = p.Service,
            ["f_responsable"] = p.Responsable,
            ["e_effectue"] = "Non",
            ["p_date_faits"] = p.FaitsLe is { } f ? HeureLocale.Date(f) : null,
            ["p_heure_faits"] = p.FaitsLe is { } h ? HeureLocale.Heure(h) : null,
            ["p_lieu_faits"] = lieu.Length == 0 ? null : lieu,
            ["p_nature"] = p.Qualification,
            ["a_date"] = HeureLocale.Date(maintenant),
            ["a_heure_debut"] = HeureLocale.Heure(maintenant),
            ["k_date"] = HeureLocale.Date(maintenant),
            ["k_heure"] = HeureLocale.Heure(maintenant),
            ["k_lieu"] = lieu.Length == 0 ? null : lieu,
            ["s_date"] = HeureLocale.Date(maintenant),
            ["s_heure"] = HeureLocale.Heure(maintenant),
            ["s_lieu"] = lieu.Length == 0 ? null : lieu,
            ["o_objet"] = p.Intitule
        };
        return valeurs
            .Where(v => !string.IsNullOrWhiteSpace(v.Value) && type.Champs.Any(c => c.Code == v.Key))
            .ToDictionary(v => v.Key, v => v.Value);
    }

    private static Task EcrireVersionAsync(SqlSession db, long acteId, int version, string statut, string contenu, Auteur auteur, string? motif, CancellationToken ct)
        => db.ExecuteAsync(
            "INSERT INTO lr_actes_versions (acte_id, version, statut, contenu, auteur, motif, enregistre_le) VALUES (@acte, @version, @statut, @contenu, @auteur, @motif, @maintenant)", ct,
            ("@acte", acteId), ("@version", version), ("@statut", statut), ("@contenu", contenu), ("@auteur", auteur.Nom),
            ("@motif", SocleRegles.Tronquer(motif, 500)), ("@maintenant", Maintenant));

    public async Task<ActeDto> EnregistrerAsync(AgentContext ctx, long id, ActeInput input, CancellationToken ct)
    {
        await using (var db = await saphirDb.OpenAsync(ct))
        {
            await db.BeginAsync(ct);
            var etat = await VerrouillerAsync(db, id, ct);
            if (!JudiciaireRegles.ActeModifiable(etat.Statut))
                throw ApiException.Conflit("Cet acte est validé : il est figé. Ajoute une mention si nécessaire.");
            await LrppnStore.ProcedureOuverteAsync(db, etat.ProcedureId, ct);
            if (input.Version != etat.Version)
                throw ApiException.Conflit("L'acte a été modifié entre-temps par un autre agent. Recharge-le avant d'enregistrer.");
            var type = ActesCatalogue.Trouver(etat.Type) ?? throw ApiException.Saisie("Type d'acte inconnu.");

            if (input.PersonneId is { } personneId)
                _ = await PersonnesStore.ReferenceAsync(db, personneId, ct);
            if (input.MisEnCauseId is { } mecId)
                _ = await PersonnesStore.ReferenceAsync(db, mecId, ct);

            var agents = new List<ActeAgentDto>();
            foreach (var a in (input.Agents ?? Array.Empty<ActeAgentDto>()).Take(30))
            {
                if (!ActesCatalogue.RolesAgents.Contains(a.Role))
                    throw ApiException.Saisie($"Rôle d'agent inconnu : {a.Role}.");
                var libelle = SocleRegles.Tronquer(a.Libelle, 200);
                string? rio = SocleRegles.Tronquer(a.Rio, 32);
                if (a.AgentId is { } agentId && await AgentStore.TrouverAsync(db, agentId, false, ct) is { } connu)
                {
                    libelle = LibelleAgent(connu);
                    rio = connu.Rio;
                }
                if (libelle is null)
                    continue;
                agents.Add(new ActeAgentDto(a.AgentId, a.Role, libelle, rio, SocleRegles.Tronquer(a.Grade, 80), SocleRegles.Tronquer(a.Service, 120)));
            }

            var questions = (input.QuestionsReponses ?? Array.Empty<QuestionReponseDto>())
                .Select(q => new QuestionReponseDto(SocleRegles.Tronquer(q.Question, 4000) ?? string.Empty, SocleRegles.Tronquer(q.Reponse, 8000) ?? string.Empty))
                .Where(q => q.Question.Length > 0 || q.Reponse.Length > 0)
                .Take(300)
                .ToList();

            var piecesIds = new List<long>();
            if (input.PiecesIds is { Count: > 0 } demandees)
            {
                var (liste, ids) = Sqlj.In("pc", demandees);
                piecesIds = await db.QueryAsync($"SELECT id FROM lr_pieces WHERE procedure_id = @procedure AND id IN ({liste}) ORDER BY ordre",
                    r => r.Long("id"), ct, [.. ids, ("@procedure", etat.ProcedureId)]);
            }

            var contenu = Ecrire(new ContenuActe(JudiciaireRegles.NettoyerChamps(type, input.Champs), agents, questions, piecesIds));
            var titre = SocleRegles.Tronquer(input.Titre, 255) ?? type.Libelle;
            var version = etat.Version + 1;
            var rafraichirRedacteur = etat.RedacteurId == ctx.Agent.Id;
            await db.ExecuteAsync(
                $"""
                UPDATE lr_actes SET titre = @titre, personne_id = @personne, mis_en_cause_id = @mec, contenu = @contenu, version = @version,
                    maj_le = @maintenant{(rafraichirRedacteur ? ", redacteur = @redacteur, redacteur_rio = @rio, redacteur_grade = @grade, redacteur_service = @service" : string.Empty)}
                WHERE id = @id
                """, ct,
                ("@titre", titre), ("@personne", input.PersonneId), ("@mec", input.MisEnCauseId), ("@contenu", contenu), ("@version", version),
                ("@maintenant", Maintenant), ("@redacteur", LibelleAgent(ctx.Agent)), ("@rio", ctx.Agent.Rio), ("@grade", ctx.Agent.Grade),
                ("@service", ctx.Agent.Service), ("@id", id));
            await EcrireVersionAsync(db, id, version, etat.Statut, contenu, ctx.Auteur, "Enregistrement du brouillon", ct);
            if (input.PersonneId is { } p)
                await LrppnStore.AssocierPersonneAsync(db, ctx.Auteur, etat.ProcedureId, p, JudiciaireRegles.QualitePersonneActe(type.Code), null, ct);
            if (input.MisEnCauseId is { } m)
                await LrppnStore.AssocierPersonneAsync(db, ctx.Auteur, etat.ProcedureId, m, "Mis en cause", null, ct);
            await Historique.EcrireAsync(db, ctx.Auteur, ObjetsJudiciaire.Acte, etat.Numero, "Enregistrement", "version",
                etat.Version.ToString(CultureInfo.InvariantCulture), version.ToString(CultureInfo.InvariantCulture), null, ct);
            await db.CommitAsync(ct);
        }
        return await ActeAsync(id, ct);
    }

    public async Task<ActeDto> DemanderRelectureAsync(AgentContext ctx, long id, CancellationToken ct)
        => await ChangerStatutAsync(ctx, id, ActeStatuts.Brouillon, ActeStatuts.EnRelecture, "Relecture demandée", null, ct);

    public async Task<ActeDto> CorrigerAsync(AgentContext ctx, long id, string? motif, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(motif))
            throw ApiException.Saisie("Indique les corrections attendues (motif obligatoire).");
        return await ChangerStatutAsync(ctx, id, ActeStatuts.EnRelecture, ActeStatuts.Brouillon, "Renvoyé pour correction", motif, ct);
    }

    private async Task<ActeDto> ChangerStatutAsync(AgentContext ctx, long id, string attendu, string nouveau, string action, string? motif, CancellationToken ct)
    {
        await using (var db = await saphirDb.OpenAsync(ct))
        {
            await db.BeginAsync(ct);
            var etat = await VerrouillerAsync(db, id, ct);
            if (etat.Statut != attendu)
                throw ApiException.Conflit($"Action impossible : l'acte est « {etat.Statut} ».");
            await LrppnStore.ProcedureOuverteAsync(db, etat.ProcedureId, ct);
            await db.ExecuteAsync(
                $"UPDATE lr_actes SET statut = @statut, version = version + 1, maj_le = @maintenant{(nouveau == ActeStatuts.EnRelecture ? ", relecture_le = @maintenant" : string.Empty)} WHERE id = @id", ct,
                ("@statut", nouveau), ("@maintenant", Maintenant), ("@id", id));
            await Historique.EcrireAsync(db, ctx.Auteur, ObjetsJudiciaire.Acte, etat.Numero, action, "statut", etat.Statut, nouveau, motif, ct);
            await db.CommitAsync(ct);
        }
        return await ActeAsync(id, ct);
    }

    public async Task SupprimerBrouillonAsync(AgentContext ctx, long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var etat = await VerrouillerAsync(db, id, ct);
        if (etat.Statut != ActeStatuts.Brouillon)
            throw ApiException.Conflit("Seul un brouillon peut être supprimé.");
        if (etat.RedacteurId != ctx.Agent.Id && !ctx.Droits.Possede(Permissions.LrppnValider))
            throw ApiException.Conflit("Seul le rédacteur (ou un agent habilité à valider) peut supprimer ce brouillon.");
        await LrppnStore.ProcedureOuverteAsync(db, etat.ProcedureId, ct);
        await db.ExecuteAsync("DELETE FROM lr_actes_versions WHERE acte_id = @id", ct, ("@id", id));
        await db.ExecuteAsync("DELETE FROM lr_actes_mentions WHERE acte_id = @id", ct, ("@id", id));
        await db.ExecuteAsync("UPDATE lr_pieces SET acte_id = NULL WHERE acte_id = @id", ct, ("@id", id));
        await db.ExecuteAsync("DELETE FROM lr_actes WHERE id = @id", ct, ("@id", id));
        await Historique.EcrireAsync(db, ctx.Auteur, ObjetsJudiciaire.Acte, etat.Numero, "Brouillon supprimé", "statut", etat.Statut, null, null, ct);
        await db.CommitAsync(ct);
    }

    // =====================================================================================
    // Texte, validation, PDF
    // =====================================================================================

    private async Task<ActeRenduContexte> ContexteAsync(SqlSession db, long id, string? validePar, DateTimeOffset? valideLe, CancellationToken ct)
    {
        var acte = await LireActeAsync(db, id, ct);
        var type = ActesCatalogue.Trouver(acte.Type) ?? throw ApiException.Saisie("Type d'acte inconnu.");
        var procedure = await LireProcedureAsync(db, acte.ProcedureId, ct);
        var pieces = new List<string>();
        if (acte.PiecesIds.Count > 0)
        {
            var (liste, ids) = Sqlj.In("pc", acte.PiecesIds);
            pieces = await db.QueryAsync($"SELECT numero, categorie, libelle, numero_scelle FROM lr_pieces WHERE id IN ({liste}) ORDER BY ordre",
                r => $"{r.Text("numero")} — {r.Text("categorie")} : {r.Text("libelle")}" +
                     (r.NullableText("numero_scelle") is { } s ? $" (scellé {s})" : string.Empty), ct, ids);
        }
        var organisation = await parametres.ValeurAsync(ParametresCles.NomOrganisation, ct);
        return new ActeRenduContexte(type, procedure.Numero, procedure.Intitule, acte.Numero, acte.Titre, acte.Redacteur, acte.RedacteurGrade,
            acte.RedacteurRio, acte.RedacteurService ?? procedure.Service, acte.CreeLe, await LrppnStore.ModeleTexteAsync(db, type, ct),
            acte.Personne, acte.MisEnCause, acte.Champs, acte.Agents, acte.QuestionsReponses, pieces, validePar, valideLe,
            string.IsNullOrWhiteSpace(organisation) ? SocleRegles.NomOrganisationParDefaut : organisation);
    }

    public async Task<ApercuDto> ApercuAsync(long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var acte = await LireActeAsync(db, id, ct);
        if (acte.TexteFige is { } fige)
            return new ApercuDto(acte.Titre, fige, true);
        return new ApercuDto(acte.Titre, ActeRendu.Rendre(await ContexteAsync(db, id, null, null, ct)), false);
    }

    private static byte[] Pdf(string texte, string titre, string numero, string organisation, bool projet)
        => new PdfSimple
        {
            EnTete = $"{organisation} — {titre} n° {numero}",
            PiedDePage = "SAPHIR — document de simulation (données fictives)" + (projet ? " — PROJET non validé" : string.Empty),
            Filigrane = projet ? "PROJET" : null
        }.TexteBalise(texte).Generer();

    /// <summary>
    /// Valide l'acte (règles 10 et 17) : contrôles, texte et PDF figés avec empreinte SHA-256, historique de la validation,
    /// indexation ANADOC et, pour un PVI ou une plainte, inscription TAJ du mis en cause.
    /// </summary>
    public async Task<(ActeDto Acte, string? Taj)> ValiderAsync(AgentContext ctx, long id, CancellationToken ct)
    {
        string? referenceTaj = null;
        await using (var db = await saphirDb.OpenAsync(ct))
        {
            await db.BeginAsync(ct);
            var etat = await VerrouillerAsync(db, id, ct);
            if (!JudiciaireRegles.ActeModifiable(etat.Statut))
                throw ApiException.Conflit("Cet acte est déjà validé.");
            await LrppnStore.ProcedureOuverteAsync(db, etat.ProcedureId, ct);
            var type = ActesCatalogue.Trouver(etat.Type) ?? throw ApiException.Saisie("Type d'acte inconnu.");
            var acte = await LireActeAsync(db, id, ct);
            var erreurs = JudiciaireRegles.ControlerActe(type, acte.Champs, acte.PersonneId);
            if (erreurs.Count > 0)
                throw ApiException.Saisie("Validation impossible :\n• " + string.Join("\n• ", erreurs));

            var valideLe = DateTimeOffset.UtcNow;
            var validePar = $"{LibelleAgent(ctx.Agent)}" + (ctx.Agent.Grade is null ? string.Empty : $", {ctx.Agent.Grade}") +
                            (ctx.Agent.Rio is null ? string.Empty : $", RIO {ctx.Agent.Rio}");
            var contexte = await ContexteAsync(db, id, validePar, valideLe, ct);
            var texte = ActeRendu.Rendre(contexte);
            var empreinte = JudiciaireRegles.Empreinte(texte);
            var pdf = Pdf(texte, acte.Titre, acte.Numero, contexte.Organisation, false);

            await db.ExecuteAsync(
                """
                UPDATE lr_actes SET statut = 'Validé', texte_fige = @texte, pdf = @pdf, empreinte = @empreinte, valide_le = @valideLe,
                    valide_par_id = @valideParId, valide_par = @validePar, version = version + 1, maj_le = @valideLe
                WHERE id = @id
                """, ct,
                ("@texte", texte), ("@pdf", pdf), ("@empreinte", empreinte), ("@valideLe", valideLe.UtcDateTime), ("@valideParId", ctx.Agent.Id),
                ("@validePar", validePar), ("@id", id));
            await EcrireVersionAsync(db, id, etat.Version + 1, ActeStatuts.Valide, etat.Contenu, ctx.Auteur, "Validation — SHA-256 " + empreinte, ct);
            await Historique.EcrireAsync(db, ctx.Auteur, ObjetsJudiciaire.Acte, etat.Numero, "Validation", "statut", etat.Statut, ActeStatuts.Valide,
                "Empreinte SHA-256 : " + empreinte, ct);

            // 20.5 : indexation ANADOC des métadonnées et du texte validé.
            var procedure = await LireProcedureAsync(db, etat.ProcedureId, ct);
            var personnes = new[] { acte.Personne, acte.MisEnCause }.Where(p => p is not null).ToList();
            var personnesIds = new[] { acte.PersonneId, acte.MisEnCauseId }.Where(p => p is not null).Select(p => p!.Value).Distinct().ToList();
            await db.ExecuteAsync(
                """
                INSERT INTO ch_anadoc (source, acte_id, procedure_id, procedure_numero, numero_pv, type_acte, titre, `date`, service, redacteur,
                    personnes, personnes_ids, texte, indexe_le)
                VALUES ('Acte', @acte, @procedure, @procedureNumero, @numero, @type, @titre, @date, @service, @redacteur, @personnes, @personnesIds,
                    @texte, @date)
                ON DUPLICATE KEY UPDATE texte = VALUES(texte), titre = VALUES(titre), indexe_le = VALUES(indexe_le)
                """, ct,
                ("@acte", id), ("@procedure", etat.ProcedureId), ("@procedureNumero", procedure.Numero), ("@numero", acte.Numero),
                ("@type", type.Libelle), ("@titre", acte.Titre), ("@date", valideLe.UtcDateTime), ("@service", acte.RedacteurService ?? procedure.Service),
                ("@redacteur", acte.Redacteur), ("@personnes", SocleRegles.Tronquer(string.Join(" ; ", personnes), 1000)),
                ("@personnesIds", personnesIds.Count == 0 ? null : "," + string.Join(',', personnesIds) + ","),
                ("@texte", ActeRendu.SansBalises(texte)));

            // 20.4 : inscription TAJ de la personne mise en cause (jamais FIJAIS ni casier depuis une interpellation).
            if (type.InscriptionTaj)
            {
                var misEnCause = type.Code == ActesCatalogue.Pvi ? acte.PersonneId : acte.MisEnCauseId;
                if (misEnCause is { } mec)
                {
                    acte.Champs.TryGetValue("c_motif", out var motif);
                    acte.Champs.TryGetValue("p_nature", out var nature);
                    var faits = procedure.Qualification ?? motif ?? nature ?? procedure.Intitule;
                    var lieu = string.Join(", ", new[] { procedure.Adresse, procedure.Commune }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    referenceTaj = await CheopsStore.InscrireTajAsync(db, ctx.Auteur, mec, TajQualites.MisEnCause, faits, null,
                        procedure.FaitsLe ?? valideLe, lieu.Length == 0 ? null : lieu, acte.RedacteurService ?? procedure.Service, procedure.Numero,
                        procedure.Id, id, null, "LRPPN — " + type.Abrege + " " + acte.Numero, ct);
                }
            }
            await db.ExecuteAsync("UPDATE lr_procedures SET maj_le = @maintenant WHERE id = @id", ct, ("@maintenant", Maintenant), ("@id", etat.ProcedureId));
            await db.CommitAsync(ct);
        }
        return (await ActeAsync(id, ct), referenceTaj);
    }

    public async Task<ActeDto> AjouterMentionAsync(AgentContext ctx, long id, MentionInput input, CancellationToken ct)
    {
        var texte = SocleRegles.Tronquer(input.Texte, 4000) ?? throw ApiException.Saisie("Le texte de la mention est obligatoire.");
        await using (var db = await saphirDb.OpenAsync(ct))
        {
            await db.BeginAsync(ct);
            var etat = await VerrouillerAsync(db, id, ct);
            if (etat.Statut != ActeStatuts.Valide)
                throw ApiException.Conflit("Les mentions complètent un acte validé ; un brouillon se modifie directement.");
            await db.ExecuteAsync("INSERT INTO lr_actes_mentions (acte_id, texte, auteur_id, auteur, cree_le) VALUES (@acte, @texte, @auteurId, @auteur, @maintenant)", ct,
                ("@acte", id), ("@texte", texte), ("@auteurId", ctx.Agent.Id), ("@auteur", ctx.Auteur.Nom), ("@maintenant", Maintenant));
            await Historique.EcrireAsync(db, ctx.Auteur, ObjetsJudiciaire.Acte, etat.Numero, "Mention ajoutée", "mentions", null, texte, null, ct);
            await db.CommitAsync(ct);
        }
        return await ActeAsync(id, ct);
    }

    /// <summary>PDF d'un acte : celui figé à la validation, sinon un projet portant le filigrane « PROJET ».</summary>
    public async Task<(byte[] Contenu, string Nom)> PdfAsync(long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var fige = await db.FirstOrDefaultAsync("SELECT numero, pdf FROM lr_actes WHERE id = @id",
            r => (Numero: r.Text("numero"), Pdf: r.Value("pdf") as byte[]).ToTuple(), ct, ("@id", id)) ?? throw ApiException.Introuvable("Acte introuvable.");
        var nom = JudiciaireRegles.NomFichierSur("PV " + fige.Item1.Replace('/', '-')) + ".pdf";
        if (fige.Item2 is { Length: > 0 } pdf)
            return (pdf, nom);
        var contexte = await ContexteAsync(db, id, null, null, ct);
        return (Pdf(ActeRendu.Rendre(contexte), contexte.Titre, contexte.Numero, contexte.Organisation, true),
            JudiciaireRegles.NomFichierSur("PROJET " + fige.Item1.Replace('/', '-')) + ".pdf");
    }

    /// <summary>Export PDF d'une procédure : synthèse, personnes, véhicules, pièces, puis le texte de chaque acte validé.</summary>
    public async Task<(byte[] Contenu, string Nom)> ProcedurePdfAsync(long procedureId, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var p = await LrppnStore.LireProcedureAsync(db, procedureId, null, ct);
        var organisation = await parametres.ValeurAsync(ParametresCles.NomOrganisation, ct);
        if (string.IsNullOrWhiteSpace(organisation))
            organisation = SocleRegles.NomOrganisationParDefaut;
        var pdf = new PdfSimple
        {
            EnTete = $"{organisation} — Procédure n° {p.Numero}",
            PiedDePage = "SAPHIR — export de procédure (données fictives)"
        };
        pdf.Titre("PROCÉDURE N° " + p.Numero).Paragraphe(p.Intitule, true, 11).Espace();
        pdf.Intertitre("Synthèse")
            .Paragraphe($"Statut : {p.Statut}")
            .Paragraphe($"Service : {p.Service ?? "—"}   Unité : {p.Unite ?? "—"}")
            .Paragraphe($"Agent responsable : {p.Responsable}" + (p.ResponsableGrade is null ? string.Empty : $", {p.ResponsableGrade}") +
                        (p.ResponsableRio is null ? string.Empty : $", RIO {p.ResponsableRio}") + (p.QualiteFonctionnelle is null ? string.Empty : $" ({p.QualiteFonctionnelle})"))
            .Paragraphe($"Ouverte le {HeureLocale.DateHeure(p.OuverteLe)} — {p.Cadre} — {p.TypeAffaire}")
            .Paragraphe($"Qualification envisagée : {p.Qualification ?? "—"}")
            .Paragraphe($"Faits : {(p.FaitsLe is { } f ? HeureLocale.DateHeure(f) : "date non précisée")} — {p.Adresse ?? string.Empty} {p.Commune ?? string.Empty}")
            .Paragraphe($"Origine : {p.Origine ?? "—"}   Intervention PÉGASE : {p.InterventionNumero ?? "—"}   Main courante : {p.MainCouranteReference ?? "—"}");
        if (!string.IsNullOrWhiteSpace(p.Description))
            pdf.Espace(4).Paragraphe(p.Description);

        pdf.Intertitre("Personnes");
        if (p.Personnes.Count == 0) pdf.Paragraphe("Aucune personne.");
        foreach (var pp in p.Personnes)
            pdf.Paragraphe($"• {pp.Personne.Libelle} — {pp.Personne.Reference} — {pp.Qualite}");
        pdf.Intertitre("Véhicules");
        if (p.Vehicules.Count == 0) pdf.Paragraphe("Aucun véhicule.");
        foreach (var v in p.Vehicules)
            pdf.Paragraphe($"• {v.Vehicule.Immatriculation} — {v.Vehicule.Marque} {v.Vehicule.Modele} — {v.Role}");
        pdf.Intertitre("Pièces et scellés");
        if (p.Pieces.Count == 0) pdf.Paragraphe("Aucune pièce.");
        foreach (var piece in p.Pieces)
            pdf.Paragraphe($"• {piece.Numero} — {piece.Categorie} : {piece.Libelle}" + (piece.NumeroScelle is null ? string.Empty : $" (scellé {piece.NumeroScelle})") + $" — {piece.Statut}");
        pdf.Intertitre("Actes");
        foreach (var a in p.Actes)
            pdf.Paragraphe($"• {a.Numero} — {a.Titre} — {a.Statut}" + (a.ValideLe is { } v ? $" le {HeureLocale.DateHeure(v)} par {a.ValidePar}" : string.Empty));
        foreach (var validation in p.Validations)
            pdf.Paragraphe($"   Empreinte {validation.Numero} : {validation.Empreinte}", false, 7.5);

        var textes = await db.QueryAsync("SELECT texte_fige FROM lr_actes WHERE procedure_id = @id AND statut = 'Validé' ORDER BY ordre",
            r => r.Text("texte_fige"), ct, ("@id", procedureId));
        foreach (var texte in textes)
            pdf.Espace(18).TexteBalise(texte);

        return (pdf.Generer(), JudiciaireRegles.NomFichierSur("Procedure " + p.Numero.Replace('/', '-')) + ".pdf");
    }

    public async Task<List<HistoriqueDto>> HistoriqueAsync(long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var numero = await db.ScalarAsync("SELECT numero FROM lr_actes WHERE id = @id", ct, ("@id", id)) as string
                     ?? throw ApiException.Introuvable("Acte introuvable.");
        return await Sqlj.HistoriqueAsync(db, ObjetsJudiciaire.Acte, numero, ct);
    }
}
