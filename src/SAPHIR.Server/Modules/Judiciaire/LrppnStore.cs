using System.Globalization;
using System.Text;
using Saphir.Server.Data;
using Saphir.Server.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Judiciaire;

namespace Saphir.Server.Modules.Judiciaire;

/// <summary>
/// LRPPN : procédures (13.3), organisation du dossier (13.4), personnes, véhicules, procédures liées, pièces et scellés,
/// dossiers associés d'une personne (14.3), modèles d'actes versionnés (15.5) et historique.
/// </summary>
public sealed class LrppnStore(SaphirDb saphirDb)
{
    private const int Limite = 200;
    private static DateTime Maintenant => DateTime.UtcNow;

    // =====================================================================================
    // Procédures
    // =====================================================================================

    private const string ColonnesLigne =
        """
        pr.id, pr.numero, pr.intitule, pr.type_affaire, pr.cadre, pr.statut, pr.service, pr.responsable, pr.ouverte_le, pr.intervention_numero,
        (SELECT COUNT(*) FROM lr_actes a WHERE a.procedure_id = pr.id) AS nombre_actes,
        (SELECT COUNT(*) FROM lr_actes a WHERE a.procedure_id = pr.id AND a.statut = 'En relecture') AS actes_a_valider
        """;

    internal static ProcedureLigneDto LireLigne(SqlRow r) => new(
        r.Long("id"), r.Text("numero"), r.Text("intitule"), r.Text("type_affaire"), r.Text("cadre"), r.Text("statut"),
        r.NullableText("service"), r.Text("responsable"), r.Date("ouverte_le"), r.Int("nombre_actes"), r.Int("actes_a_valider"),
        r.NullableText("intervention_numero"));

    /// <summary>
    /// Liste des procédures. <paramref name="agentId"/> renseigné = « Mes procédures » (responsable, créateur ou rédacteur d'un acte).
    /// Sans filtre de statut, les procédures archivées sont exclues sauf si <paramref name="archives"/>.
    /// </summary>
    public async Task<List<ProcedureLigneDto>> ListerAsync(string? q, string? statut, long? agentId, bool archives, long? interventionId, CancellationToken ct)
    {
        var sql = new StringBuilder($"SELECT {ColonnesLigne} FROM lr_procedures pr WHERE 1 = 1");
        var parametres = new List<(string Name, object? Value)>();
        if (!string.IsNullOrWhiteSpace(q))
        {
            sql.Append("""
                 AND (pr.numero LIKE @q OR pr.intitule LIKE @q OR pr.qualification LIKE @q OR pr.commune LIKE @q
                      OR pr.intervention_numero = @exact OR pr.main_courante_ref = @exact
                      OR EXISTS (SELECT 1 FROM lr_procedure_personnes pp JOIN per_personnes p ON p.id = pp.personne_id
                                 WHERE pp.procedure_id = pr.id AND (p.nom LIKE @q OR p.reference = @exact)))
                """);
            parametres.Add(("@q", Sqlj.Like(q)));
            parametres.Add(("@exact", q.Trim().ToUpperInvariant()));
        }
        if (!string.IsNullOrWhiteSpace(statut))
        {
            sql.Append(" AND pr.statut = @statut");
            parametres.Add(("@statut", statut.Trim()));
        }
        else
        {
            sql.Append(archives ? " AND pr.statut = 'Archivée'" : " AND pr.statut <> 'Archivée'");
        }
        if (agentId is { } agent)
        {
            sql.Append(" AND (pr.responsable_id = @agent OR pr.cree_par_id = @agent OR EXISTS (SELECT 1 FROM lr_actes a WHERE a.procedure_id = pr.id AND a.redacteur_id = @agent))");
            parametres.Add(("@agent", agent));
        }
        if (interventionId is { } intervention)
        {
            sql.Append(" AND pr.intervention_id = @intervention");
            parametres.Add(("@intervention", intervention));
        }
        sql.Append($" ORDER BY pr.ouverte_le DESC, pr.id DESC LIMIT {Limite}");
        await using var db = await saphirDb.OpenAsync(ct);
        return await db.QueryAsync(sql.ToString(), LireLigne, ct, parametres.ToArray());
    }

    private static void ControlerSaisie(ProcedureInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Intitule))
            throw ApiException.Saisie("L'intitulé de la procédure est obligatoire.");
        if (!ProcedureReferentiels.Cadres.Contains(input.Cadre))
            throw ApiException.Saisie("Cadre procédural inconnu.");
        if (!ProcedureReferentiels.TypesAffaire.Contains(input.TypeAffaire))
            throw ApiException.Saisie("Type d'affaire inconnu.");
        if (input.Origine is not null && !ProcedureReferentiels.Origines.Contains(input.Origine))
            throw ApiException.Saisie("Origine inconnue.");
        if (input.QualiteFonctionnelle is not null && !ProcedureReferentiels.QualitesFonctionnelles.Contains(input.QualiteFonctionnelle))
            throw ApiException.Saisie("Qualité fonctionnelle inconnue.");
        if (input.FaitsLe is { } faits && faits > DateTimeOffset.UtcNow.AddHours(1))
            throw ApiException.Saisie("La date des faits ne peut pas être future.");
    }

    private static Dictionary<string, string?> Valeurs(ProcedureInput input, AgentDto responsable)
        => new()
        {
            ["Service"] = SocleRegles.Tronquer(input.Service, 120),
            ["Unité"] = SocleRegles.Tronquer(input.Unite, 120),
            ["Agent responsable"] = $"{responsable.Nom} {responsable.Prenom}",
            ["RIO"] = responsable.Rio,
            ["Grade"] = SocleRegles.Tronquer(input.ResponsableGrade, 80) ?? responsable.Grade,
            ["Qualité fonctionnelle"] = input.QualiteFonctionnelle,
            ["Date d'ouverture"] = HeureLocale.DateHeure(input.OuverteLe),
            ["Cadre procédural"] = input.Cadre,
            ["Type d'affaire"] = input.TypeAffaire,
            ["Intitulé"] = SocleRegles.Tronquer(input.Intitule, 255),
            ["Intervention PÉGASE"] = SocleRegles.Tronquer(input.InterventionNumero, 40),
            ["Référence main courante"] = SocleRegles.Tronquer(input.MainCouranteReference, 60),
            ["Qualification envisagée"] = SocleRegles.Tronquer(input.Qualification, 255),
            ["Date des faits"] = input.FaitsLe is { } f ? HeureLocale.DateHeure(f) : null,
            ["Commune"] = SocleRegles.Tronquer(input.Commune, 80),
            ["Adresse"] = SocleRegles.Tronquer(input.Adresse, 255),
            ["Description"] = SocleRegles.Tronquer(input.Description, 8000),
            ["Origine"] = input.Origine
        };

    private static (string Name, object? Value)[] Parametres(ProcedureInput input, Dictionary<string, string?> v, AgentDto responsable, long id) =>
    [
        ("@service", v["Service"]), ("@unite", v["Unité"]), ("@responsableId", responsable.Id), ("@responsable", v["Agent responsable"]),
        ("@rio", responsable.Rio), ("@grade", v["Grade"]), ("@qualite", input.QualiteFonctionnelle), ("@ouverte", input.OuverteLe.UtcDateTime),
        ("@cadre", input.Cadre), ("@type", input.TypeAffaire), ("@intitule", v["Intitulé"]), ("@interventionId", input.InterventionId),
        ("@interventionNumero", v["Intervention PÉGASE"]), ("@mc", v["Référence main courante"]), ("@qualification", v["Qualification envisagée"]),
        ("@faits", Sqlj.Utc(input.FaitsLe)), ("@commune", v["Commune"]), ("@adresse", v["Adresse"]), ("@description", v["Description"]),
        ("@origine", input.Origine), ("@maintenant", Maintenant), ("@id", id)
    ];

    public async Task<ProcedureDto> CreerAsync(AgentContext ctx, ProcedureInput input, CancellationToken ct)
    {
        ControlerSaisie(input);
        long id;
        await using (var db = await saphirDb.OpenAsync(ct))
        {
            await db.BeginAsync(ct);
            var responsable = await AgentStore.TrouverAsync(db, input.ResponsableId, false, ct)
                              ?? throw ApiException.Saisie("Agent responsable introuvable.");
            var valeurs = Valeurs(input, responsable);
            var parametres = Parametres(input, valeurs, responsable, 0).Append(("@creeParId", (object?)ctx.Agent.Id)).Append(("@creePar", (object?)ctx.Auteur.Nom)).ToArray();
            id = await db.InsertAsync(
                """
                INSERT INTO lr_procedures (statut, service, unite, responsable_id, responsable, responsable_rio, responsable_grade, qualite_fonctionnelle,
                    ouverte_le, cadre, type_affaire, intitule, intervention_id, intervention_numero, main_courante_ref, qualification, faits_le,
                    commune, adresse, description, origine, version, cree_par_id, cree_par, cree_le, maj_le)
                VALUES ('En cours', @service, @unite, @responsableId, @responsable, @rio, @grade, @qualite, @ouverte, @cadre, @type, @intitule,
                    @interventionId, @interventionNumero, @mc, @qualification, @faits, @commune, @adresse, @description, @origine, 1,
                    @creeParId, @creePar, @maintenant, @maintenant)
                """, ct, parametres);
            var numero = JudiciaireRegles.NumeroProcedure(HeureLocale.Convertir(input.OuverteLe).Year, id);
            await db.ExecuteAsync("UPDATE lr_procedures SET numero = @numero WHERE id = @id", ct, ("@numero", numero), ("@id", id));
            await Historique.EcrireAsync(db, ctx.Auteur, ObjetsJudiciaire.Procedure, numero, "Création", null, null, valeurs["Intitulé"],
                input.InterventionNumero is null ? null : "Ouverte depuis l'intervention PÉGASE " + input.InterventionNumero, ct);
            await db.CommitAsync(ct);
        }
        return await ProcedureAsync(id, ct);
    }

    public async Task<ProcedureDto> ModifierAsync(Auteur auteur, long id, ProcedureInput input, CancellationToken ct)
    {
        ControlerSaisie(input);
        await using (var db = await saphirDb.OpenAsync(ct))
        {
            await db.BeginAsync(ct);
            var etat = await db.FirstOrDefaultAsync("SELECT numero, statut, version FROM lr_procedures WHERE id = @id FOR UPDATE",
                r => (Numero: r.Text("numero"), Statut: r.Text("statut"), Version: r.Int("version")).ToTuple(), ct, ("@id", id))
                ?? throw ApiException.Introuvable("Procédure introuvable.");
            if (JudiciaireRegles.ControlerProcedureOuverte(etat.Item2) is { } erreur)
                throw ApiException.Conflit(erreur);
            if (input.Version is { } version && version != etat.Item3)
                throw ApiException.Conflit("La procédure a été modifiée entre-temps par un autre agent. Recharge-la avant d'enregistrer.");
            var responsable = await AgentStore.TrouverAsync(db, input.ResponsableId, false, ct)
                              ?? throw ApiException.Saisie("Agent responsable introuvable.");
            var avant = await LireValeursAsync(db, id, ct);
            var apres = Valeurs(input, responsable);
            await db.ExecuteAsync(
                """
                UPDATE lr_procedures SET service = @service, unite = @unite, responsable_id = @responsableId, responsable = @responsable,
                    responsable_rio = @rio, responsable_grade = @grade, qualite_fonctionnelle = @qualite, ouverte_le = @ouverte, cadre = @cadre,
                    type_affaire = @type, intitule = @intitule, intervention_id = @interventionId, intervention_numero = @interventionNumero,
                    main_courante_ref = @mc, qualification = @qualification, faits_le = @faits, commune = @commune, adresse = @adresse,
                    description = @description, origine = @origine, version = version + 1, maj_le = @maintenant
                WHERE id = @id
                """, ct, Parametres(input, apres, responsable, id));
            await Sqlj.HistoriserChangementsAsync(db, auteur, ObjetsJudiciaire.Procedure, etat.Item1,
                apres.Select(kv => (kv.Key, avant.GetValueOrDefault(kv.Key), kv.Value)), input.Motif, ct);
            await db.CommitAsync(ct);
        }
        return await ProcedureAsync(id, ct);
    }

    private static async Task<Dictionary<string, string?>> LireValeursAsync(SqlSession db, long id, CancellationToken ct)
        => await db.FirstOrDefaultAsync(
            """
            SELECT service, unite, responsable, responsable_rio, responsable_grade, qualite_fonctionnelle, ouverte_le, cadre, type_affaire,
                   intitule, intervention_numero, main_courante_ref, qualification, faits_le, commune, adresse, description, origine
            FROM lr_procedures WHERE id = @id
            """,
            r => new Dictionary<string, string?>
            {
                ["Service"] = r.NullableText("service"),
                ["Unité"] = r.NullableText("unite"),
                ["Agent responsable"] = r.Text("responsable"),
                ["RIO"] = r.NullableText("responsable_rio"),
                ["Grade"] = r.NullableText("responsable_grade"),
                ["Qualité fonctionnelle"] = r.NullableText("qualite_fonctionnelle"),
                ["Date d'ouverture"] = HeureLocale.DateHeure(r.Date("ouverte_le")),
                ["Cadre procédural"] = r.Text("cadre"),
                ["Type d'affaire"] = r.Text("type_affaire"),
                ["Intitulé"] = r.Text("intitule"),
                ["Intervention PÉGASE"] = r.NullableText("intervention_numero"),
                ["Référence main courante"] = r.NullableText("main_courante_ref"),
                ["Qualification envisagée"] = r.NullableText("qualification"),
                ["Date des faits"] = r.NullableDate("faits_le") is { } f ? HeureLocale.DateHeure(f) : null,
                ["Commune"] = r.NullableText("commune"),
                ["Adresse"] = r.NullableText("adresse"),
                ["Description"] = r.NullableText("description"),
                ["Origine"] = r.NullableText("origine")
            }, ct, ("@id", id)) ?? new Dictionary<string, string?>();

    public async Task<ProcedureDto> ChangerStatutAsync(Auteur auteur, long id, StatutInput input, CancellationToken ct)
    {
        await using (var db = await saphirDb.OpenAsync(ct))
        {
            await db.BeginAsync(ct);
            var avant = await db.FirstOrDefaultAsync("SELECT numero, statut FROM lr_procedures WHERE id = @id FOR UPDATE",
                r => new[] { r.Text("numero"), r.Text("statut") }, ct, ("@id", id)) ?? throw ApiException.Introuvable("Procédure introuvable.");
            if (JudiciaireRegles.ControlerTransitionProcedure(avant[1], input.Statut) is { } erreur)
                throw ApiException.Saisie(erreur);
            if (input.Statut is ProcedureStatuts.Cloturee or ProcedureStatuts.Transmise)
            {
                var brouillons = await db.LongAsync("SELECT COUNT(*) FROM lr_actes WHERE procedure_id = @id AND statut <> 'Validé'", ct, ("@id", id));
                if (brouillons > 0)
                    throw ApiException.Conflit($"{brouillons} acte(s) ne sont pas validés : valide-les ou supprime les brouillons avant de clôturer ou transmettre.");
            }
            await db.ExecuteAsync("UPDATE lr_procedures SET statut = @statut, version = version + 1, maj_le = @maintenant WHERE id = @id", ct,
                ("@statut", input.Statut), ("@maintenant", Maintenant), ("@id", id));
            await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Procedure, avant[0], "Statut", "statut", avant[1], input.Statut, input.Motif, ct);
            await db.CommitAsync(ct);
        }
        return await ProcedureAsync(id, ct);
    }

    public async Task<ProcedureDto> ProcedureAsync(long id, CancellationToken ct, AgentDroits? droits = null)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await LireProcedureAsync(db, id, droits, ct);
    }

    internal static async Task<ProcedureDto> LireProcedureAsync(SqlSession db, long id, AgentDroits? droits, CancellationToken ct)
    {
        var procedure = await db.FirstOrDefaultAsync(
            """
            SELECT id, numero, statut, service, unite, responsable_id, responsable, responsable_rio, responsable_grade, qualite_fonctionnelle,
                   ouverte_le, cadre, type_affaire, intitule, intervention_id, intervention_numero, main_courante_ref, qualification, faits_le,
                   commune, adresse, description, origine, version, cree_le, cree_par, maj_le
            FROM lr_procedures WHERE id = @id
            """,
            r => new ProcedureDto(
                r.Long("id"), r.Text("numero"), r.Text("statut"), r.NullableText("service"), r.NullableText("unite"), r.NullableLong("responsable_id"),
                r.Text("responsable"), r.NullableText("responsable_rio"), r.NullableText("responsable_grade"), r.NullableText("qualite_fonctionnelle"),
                r.Date("ouverte_le"), r.Text("cadre"), r.Text("type_affaire"), r.Text("intitule"), r.NullableLong("intervention_id"),
                r.NullableText("intervention_numero"), r.NullableText("main_courante_ref"), r.NullableText("qualification"), r.NullableDate("faits_le"),
                r.NullableText("commune"), r.NullableText("adresse"), r.NullableText("description"), r.NullableText("origine"), r.Int("version"),
                r.Date("cree_le"), r.Text("cree_par"), r.Date("maj_le"),
                Array.Empty<ProcedurePersonneDto>(), Array.Empty<ProcedureVehiculeDto>(), Array.Empty<ActeLigneDto>(), Array.Empty<PieceDto>(),
                Array.Empty<ProcedureLienDto>(), Array.Empty<ValidationDto>()), ct, ("@id", id))
            ?? throw ApiException.Introuvable("Procédure introuvable.");

        var personnes = await db.QueryAsync(
            $"""
            SELECT pp.id AS lien_id, pp.qualite, pp.observations, pp.ajoute_le, {CheopsStore.ColonnesPersonneP}
            FROM lr_procedure_personnes pp JOIN per_personnes p ON p.id = pp.personne_id
            WHERE pp.procedure_id = @id ORDER BY pp.ajoute_le
            """,
            r => new ProcedurePersonneDto(r.Long("lien_id"), CheopsStore.LirePersonneP(r), r.Text("qualite"), r.NullableText("observations"), r.Date("ajoute_le")),
            ct, ("@id", id));
        if (droits is not null && personnes.Count > 0)
        {
            var alertes = await AlertesJudiciaires.PersonnesAsync(db, personnes.Select(p => p.Personne.Id).ToList(), droits, ct);
            personnes = personnes.Select(p => p with { Personne = p.Personne with { Alertes = alertes[p.Personne.Id] } }).ToList();
        }

        var vehicules = await db.QueryAsync(
            """
            SELECT pv.id AS lien_id, pv.role, pv.observations, v.id, v.affichage, v.marque, v.modele, v.couleur, v.titulaire_id, v.titulaire_libre,
                   tp.nom AS t_nom, tp.prenom AS t_prenom
            FROM lr_procedure_vehicules pv JOIN veh_vehicules v ON v.id = pv.vehicule_id LEFT JOIN per_personnes tp ON tp.id = v.titulaire_id
            WHERE pv.procedure_id = @id ORDER BY pv.ajoute_le
            """,
            r => new ProcedureVehiculeDto(r.Long("lien_id"),
                new VehiculeResumeDto(r.Long("id"), r.Text("affichage"), r.NullableText("marque"), r.NullableText("modele"), r.NullableText("couleur"),
                    r.NullableLong("titulaire_id"), r.NullableText("t_nom") is { } n ? $"{n} {r.Text("t_prenom")}" : r.NullableText("titulaire_libre"),
                    Array.Empty<string>()),
                r.Text("role"), r.NullableText("observations")), ct, ("@id", id));
        if (droits is not null && vehicules.Count > 0)
        {
            var alertes = await AlertesJudiciaires.VehiculesAsync(db, vehicules.Select(v => v.Vehicule.Id).ToList(), droits, ct);
            vehicules = vehicules.Select(v => v with { Vehicule = v.Vehicule with { Alertes = alertes[v.Vehicule.Id] } }).ToList();
        }

        var actes = await db.QueryAsync($"SELECT {ActesStore.ColonnesLigne} {ActesStore.JointuresLigne} WHERE a.procedure_id = @id ORDER BY a.ordre",
            ActesStore.LireLigne, ct, ("@id", id));
        var pieces = await PiecesAsync(db, "pc.procedure_id = @id", ct, ("@id", id));
        var liens = await db.QueryAsync(
            """
            SELECT l.id, o.id AS autre_id, o.numero, o.intitule FROM lr_procedure_liens l
            JOIN lr_procedures o ON o.id = CASE WHEN l.procedure_id = @id THEN l.liee_id ELSE l.procedure_id END
            WHERE l.procedure_id = @id OR l.liee_id = @id ORDER BY o.numero
            """,
            r => new ProcedureLienDto(r.Long("id"), r.Long("autre_id"), r.Text("numero"), r.Text("intitule")), ct, ("@id", id));
        var validations = await db.QueryAsync(
            "SELECT id, numero, titre, valide_le, valide_par, empreinte FROM lr_actes WHERE procedure_id = @id AND valide_le IS NOT NULL ORDER BY valide_le",
            r => new ValidationDto(r.Long("id"), r.Text("numero"), r.Text("titre"), r.Date("valide_le"), r.Text("valide_par"), r.Text("empreinte")),
            ct, ("@id", id));

        return procedure with
        {
            Personnes = personnes,
            Vehicules = vehicules,
            Actes = actes,
            Pieces = pieces,
            Liens = liens,
            Validations = validations
        };
    }

    internal static async Task<(string Numero, string Statut)> ProcedureOuverteAsync(SqlSession db, long id, CancellationToken ct, bool verrouiller = true)
    {
        var etat = await db.FirstOrDefaultAsync(
            "SELECT numero, statut FROM lr_procedures WHERE id = @id" + (verrouiller ? " FOR UPDATE" : string.Empty),
            r => new[] { r.Text("numero"), r.Text("statut") }, ct, ("@id", id)) ?? throw ApiException.Introuvable("Procédure introuvable.");
        if (JudiciaireRegles.ControlerProcedureOuverte(etat[1]) is { } erreur)
            throw ApiException.Conflit(erreur);
        return (etat[0], etat[1]);
    }

    private static Task ToucherAsync(SqlSession db, long id, CancellationToken ct)
        => db.ExecuteAsync("UPDATE lr_procedures SET maj_le = @maintenant WHERE id = @id", ct, ("@maintenant", Maintenant), ("@id", id));

    // ----- Personnes, véhicules, liens -----

    public async Task AjouterPersonneAsync(Auteur auteur, long procedureId, ProcedurePersonneInput input, CancellationToken ct)
    {
        if (!ProcedureReferentiels.QualitesPersonne.Contains(input.Qualite))
            throw ApiException.Saisie("Qualité inconnue.");
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var (numero, _) = await ProcedureOuverteAsync(db, procedureId, ct);
        var reference = await PersonnesStore.ReferenceAsync(db, input.PersonneId, ct);
        var ajoute = await AssocierPersonneAsync(db, auteur, procedureId, input.PersonneId, input.Qualite, input.Observations, ct);
        if (!ajoute)
            throw ApiException.Conflit("Cette personne figure déjà dans la procédure avec cette qualité.");
        await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Procedure, numero, "Personne ajoutée", "personnes", null,
            $"{reference} ({input.Qualite})", input.Observations, ct);
        await ToucherAsync(db, procedureId, ct);
        await db.CommitAsync(ct);
    }

    /// <summary>Associe une personne à la procédure si elle n'y est pas déjà avec cette qualité ; retourne vrai si ajoutée.</summary>
    internal static async Task<bool> AssocierPersonneAsync(SqlSession db, Auteur auteur, long procedureId, long personneId, string qualite,
        string? observations, CancellationToken ct)
        => await db.ExecuteAsync(
            """
            INSERT IGNORE INTO lr_procedure_personnes (procedure_id, personne_id, qualite, observations, ajoute_par, ajoute_le)
            VALUES (@procedure, @personne, @qualite, @observations, @auteur, @maintenant)
            """, ct,
            ("@procedure", procedureId), ("@personne", personneId), ("@qualite", qualite), ("@observations", SocleRegles.Tronquer(observations, 500)),
            ("@auteur", auteur.Nom), ("@maintenant", Maintenant)) > 0;

    public async Task RetirerPersonneAsync(Auteur auteur, long procedureId, long lienId, string? motif, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var (numero, _) = await ProcedureOuverteAsync(db, procedureId, ct);
        var lien = await db.FirstOrDefaultAsync(
            "SELECT pp.personne_id, pp.qualite, p.reference FROM lr_procedure_personnes pp JOIN per_personnes p ON p.id = pp.personne_id WHERE pp.id = @lien AND pp.procedure_id = @procedure",
            r => (Personne: r.Long("personne_id"), Qualite: r.Text("qualite"), Reference: r.Text("reference")).ToTuple(), ct,
            ("@lien", lienId), ("@procedure", procedureId)) ?? throw ApiException.Introuvable("Personne absente de la procédure.");
        var actes = await db.LongAsync(
            "SELECT COUNT(*) FROM lr_actes WHERE procedure_id = @procedure AND statut = 'Validé' AND (personne_id = @personne OR mis_en_cause_id = @personne)", ct,
            ("@procedure", procedureId), ("@personne", lien.Item1));
        if (actes > 0)
            throw ApiException.Conflit("Cette personne est citée dans un acte validé : elle ne peut plus être retirée de la procédure.");
        await db.ExecuteAsync("DELETE FROM lr_procedure_personnes WHERE id = @lien", ct, ("@lien", lienId));
        await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Procedure, numero, "Personne retirée", "personnes", $"{lien.Item3} ({lien.Item2})", null, motif, ct);
        await ToucherAsync(db, procedureId, ct);
        await db.CommitAsync(ct);
    }

    public async Task AjouterVehiculeAsync(Auteur auteur, long procedureId, ProcedureVehiculeInput input, CancellationToken ct)
    {
        if (!ProcedureReferentiels.RolesVehicule.Contains(input.Role))
            throw ApiException.Saisie("Rôle du véhicule inconnu.");
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var (numero, _) = await ProcedureOuverteAsync(db, procedureId, ct);
        var immat = await db.ScalarAsync("SELECT affichage FROM veh_vehicules WHERE id = @id", ct, ("@id", input.VehiculeId)) as string
                    ?? throw ApiException.Introuvable("Véhicule introuvable.");
        var ajoute = await db.ExecuteAsync(
            """
            INSERT IGNORE INTO lr_procedure_vehicules (procedure_id, vehicule_id, role, observations, ajoute_par, ajoute_le)
            VALUES (@procedure, @vehicule, @role, @observations, @auteur, @maintenant)
            """, ct,
            ("@procedure", procedureId), ("@vehicule", input.VehiculeId), ("@role", input.Role),
            ("@observations", SocleRegles.Tronquer(input.Observations, 500)), ("@auteur", auteur.Nom), ("@maintenant", Maintenant));
        if (ajoute == 0)
            throw ApiException.Conflit("Ce véhicule figure déjà dans la procédure avec ce rôle.");
        await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Procedure, numero, "Véhicule ajouté", "vehicules", null, $"{immat} ({input.Role})", input.Observations, ct);
        await ToucherAsync(db, procedureId, ct);
        await db.CommitAsync(ct);
    }

    public async Task RetirerVehiculeAsync(Auteur auteur, long procedureId, long lienId, string? motif, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var (numero, _) = await ProcedureOuverteAsync(db, procedureId, ct);
        var immat = await db.ScalarAsync(
            "SELECT v.affichage FROM lr_procedure_vehicules pv JOIN veh_vehicules v ON v.id = pv.vehicule_id WHERE pv.id = @lien AND pv.procedure_id = @procedure", ct,
            ("@lien", lienId), ("@procedure", procedureId)) as string ?? throw ApiException.Introuvable("Véhicule absent de la procédure.");
        await db.ExecuteAsync("DELETE FROM lr_procedure_vehicules WHERE id = @lien", ct, ("@lien", lienId));
        await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Procedure, numero, "Véhicule retiré", "vehicules", immat, null, motif, ct);
        await ToucherAsync(db, procedureId, ct);
        await db.CommitAsync(ct);
    }

    public async Task LierAsync(Auteur auteur, long procedureId, ProcedureLienInput input, CancellationToken ct)
    {
        var numeroLie = SocleRegles.Tronquer(input.Numero, 30) ?? throw ApiException.Saisie("Numéro de procédure obligatoire.");
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var (numero, _) = await ProcedureOuverteAsync(db, procedureId, ct);
        var lieeId = await db.ScalarAsync("SELECT id FROM lr_procedures WHERE numero = @numero", ct, ("@numero", numeroLie)) is { } v
            ? Convert.ToInt64(v, CultureInfo.InvariantCulture)
            : throw ApiException.Introuvable($"Aucune procédure n° {numeroLie}.");
        if (lieeId == procedureId)
            throw ApiException.Saisie("Une procédure ne peut pas être liée à elle-même.");
        var existe = await db.LongAsync(
            "SELECT COUNT(*) FROM lr_procedure_liens WHERE (procedure_id = @a AND liee_id = @b) OR (procedure_id = @b AND liee_id = @a)", ct,
            ("@a", procedureId), ("@b", lieeId));
        if (existe > 0)
            throw ApiException.Conflit("Ces procédures sont déjà liées.");
        await db.ExecuteAsync("INSERT INTO lr_procedure_liens (procedure_id, liee_id, cree_le) VALUES (@a, @b, @maintenant)", ct,
            ("@a", procedureId), ("@b", lieeId), ("@maintenant", Maintenant));
        await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Procedure, numero, "Procédure liée", "liens", null, numeroLie, null, ct);
        await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Procedure, numeroLie, "Procédure liée", "liens", null, numero, null, ct);
        await db.CommitAsync(ct);
    }

    public async Task DelierAsync(Auteur auteur, long procedureId, long lienId, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var (numero, _) = await ProcedureOuverteAsync(db, procedureId, ct);
        var autre = await db.ScalarAsync(
            """
            SELECT o.numero FROM lr_procedure_liens l
            JOIN lr_procedures o ON o.id = CASE WHEN l.procedure_id = @procedure THEN l.liee_id ELSE l.procedure_id END
            WHERE l.id = @lien AND (l.procedure_id = @procedure OR l.liee_id = @procedure)
            """, ct, ("@lien", lienId), ("@procedure", procedureId)) as string ?? throw ApiException.Introuvable("Lien introuvable.");
        await db.ExecuteAsync("DELETE FROM lr_procedure_liens WHERE id = @lien", ct, ("@lien", lienId));
        await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Procedure, numero, "Lien supprimé", "liens", autre, null, null, ct);
        await db.CommitAsync(ct);
    }

    // =====================================================================================
    // Pièces et scellés
    // =====================================================================================

    private const string ColonnesPiece =
        """
        pc.id, pc.procedure_id, pr.numero AS procedure_numero, pc.numero, pc.categorie, pc.libelle, pc.description, pc.numero_scelle, pc.statut,
        pc.nom_fichier, pc.type_mime, pc.taille, pc.empreinte, pc.acte_id, pc.localisation, pc.cree_le, pc.cree_par
        """;

    private static PieceDto LirePiece(SqlRow r) => new(
        r.Long("id"), r.Long("procedure_id"), r.Text("procedure_numero"), r.Text("numero"), r.Text("categorie"), r.Text("libelle"),
        r.NullableText("description"), r.NullableText("numero_scelle"), r.Text("statut"), r.NullableText("nom_fichier"), r.NullableText("type_mime"),
        r.Long("taille"), r.NullableText("empreinte"), r.NullableLong("acte_id"), r.NullableText("localisation"), r.Date("cree_le"),
        r.Text("cree_par"), Array.Empty<MouvementPieceDto>());

    internal static async Task<List<PieceDto>> PiecesAsync(SqlSession db, string condition, CancellationToken ct, params (string Name, object? Value)[] parametres)
    {
        var pieces = await db.QueryAsync(
            $"SELECT {ColonnesPiece} FROM lr_pieces pc JOIN lr_procedures pr ON pr.id = pc.procedure_id WHERE {condition} ORDER BY pc.procedure_id DESC, pc.ordre LIMIT {Limite}",
            LirePiece, ct, parametres);
        if (pieces.Count == 0)
            return pieces;
        var (liste, ids) = Sqlj.In("m", pieces.Select(p => p.Id));
        var mouvements = await db.QueryAsync(
            $"SELECT id, piece_id, `date`, mouvement, destination, observations, auteur FROM lr_pieces_mouvements WHERE piece_id IN ({liste}) ORDER BY `date`, id",
            r => (Piece: r.Long("piece_id"), Mouvement: new MouvementPieceDto(r.Long("id"), r.Date("date"), r.Text("mouvement"),
                r.NullableText("destination"), r.NullableText("observations"), r.Text("auteur"))), ct, ids);
        return pieces.Select(p => p with { Mouvements = mouvements.Where(m => m.Piece == p.Id).Select(m => m.Mouvement).ToList() }).ToList();
    }

    public async Task<List<PieceDto>> RechercherPiecesAsync(string? q, string? categorie, CancellationToken ct)
    {
        var condition = new StringBuilder("1 = 1");
        var parametres = new List<(string Name, object? Value)>();
        if (!string.IsNullOrWhiteSpace(q))
        {
            condition.Append(" AND (pc.numero LIKE @q OR pc.libelle LIKE @q OR pc.numero_scelle LIKE @q OR pr.numero LIKE @q OR pc.description LIKE @q)");
            parametres.Add(("@q", Sqlj.Like(q)));
        }
        if (!string.IsNullOrWhiteSpace(categorie))
        {
            condition.Append(" AND pc.categorie = @categorie");
            parametres.Add(("@categorie", categorie.Trim()));
        }
        await using var db = await saphirDb.OpenAsync(ct);
        return await PiecesAsync(db, condition.ToString(), ct, parametres.ToArray());
    }

    public async Task<PieceDto> AjouterPieceAsync(Auteur auteur, long procedureId, PieceInput input, CancellationToken ct)
    {
        if (!PieceCategories.All.Contains(input.Categorie))
            throw ApiException.Saisie("Catégorie de pièce inconnue.");
        var libelle = SocleRegles.Tronquer(input.Libelle, 255) ?? throw ApiException.Saisie("Le libellé de la pièce est obligatoire.");
        if (input.Categorie == PieceCategories.Scelle && string.IsNullOrWhiteSpace(input.NumeroScelle))
            throw ApiException.Saisie("Le numéro de scellé est obligatoire pour un scellé.");

        byte[]? contenu = null;
        string? mime = null;
        string? nomFichier = null;
        if (!string.IsNullOrWhiteSpace(input.ContenuBase64))
        {
            try
            {
                (contenu, mime, nomFichier) = JudiciaireRegles.ControlerFichier(input.NomFichier, input.ContenuBase64);
            }
            catch (ArgumentException ex)
            {
                throw ApiException.Saisie(ex.Message);
            }
        }

        long id;
        await using (var db = await saphirDb.OpenAsync(ct))
        {
            await db.BeginAsync(ct);
            var (numeroProcedure, _) = await ProcedureOuverteAsync(db, procedureId, ct);
            if (input.ActeId is { } acteId &&
                await db.LongAsync("SELECT COUNT(*) FROM lr_actes WHERE id = @acte AND procedure_id = @procedure", ct, ("@acte", acteId), ("@procedure", procedureId)) == 0)
                throw ApiException.Saisie("L'acte rattaché n'appartient pas à cette procédure.");
            var ordre = (int)await db.LongAsync("SELECT COALESCE(MAX(ordre), 0) + 1 FROM lr_pieces WHERE procedure_id = @id", ct, ("@id", procedureId));
            var numero = JudiciaireRegles.NumeroPiece(numeroProcedure, ordre);
            id = await db.InsertAsync(
                """
                INSERT INTO lr_pieces (procedure_id, ordre, numero, categorie, libelle, description, numero_scelle, statut, nom_fichier, type_mime,
                    taille, contenu, empreinte, acte_id, localisation, cree_par, cree_le, maj_le)
                VALUES (@procedure, @ordre, @numero, @categorie, @libelle, @description, @scelle, 'Au service', @nom, @mime, @taille, @contenu,
                    @empreinte, @acte, @localisation, @auteur, @maintenant, @maintenant)
                """, ct,
                ("@procedure", procedureId), ("@ordre", ordre), ("@numero", numero), ("@categorie", input.Categorie), ("@libelle", libelle),
                ("@description", SocleRegles.Tronquer(input.Description, 4000)), ("@scelle", SocleRegles.Tronquer(input.NumeroScelle, 60)),
                ("@nom", nomFichier), ("@mime", mime), ("@taille", (long)(contenu?.Length ?? 0)), ("@contenu", contenu),
                ("@empreinte", contenu is null ? null : JudiciaireRegles.Empreinte(contenu)), ("@acte", input.ActeId),
                ("@localisation", SocleRegles.Tronquer(input.Localisation, 160)), ("@auteur", auteur.Nom), ("@maintenant", Maintenant));
            await db.ExecuteAsync(
                "INSERT INTO lr_pieces_mouvements (piece_id, `date`, mouvement, destination, observations, auteur) VALUES (@piece, @maintenant, 'Enregistrement', @destination, NULL, @auteur)", ct,
                ("@piece", id), ("@maintenant", Maintenant), ("@destination", SocleRegles.Tronquer(input.Localisation, 160)), ("@auteur", auteur.Nom));
            await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Piece, numero, "Création", null, null,
                $"{input.Categorie} : {libelle}" + (nomFichier is null ? string.Empty : $" (fichier {nomFichier}, {contenu!.Length} octets)"), null, ct);
            await IndexerPieceAsync(db, id, ct);
            await ToucherAsync(db, procedureId, ct);
            await db.CommitAsync(ct);
        }
        await using var lecture = await saphirDb.OpenAsync(ct);
        return (await PiecesAsync(lecture, "pc.id = @id", ct, ("@id", id))).Single();
    }

    /// <summary>Indexe une pièce dans ANADOC (libellé, description, fichier).</summary>
    private static Task IndexerPieceAsync(SqlSession db, long pieceId, CancellationToken ct)
        => db.ExecuteAsync(
            """
            INSERT INTO ch_anadoc (source, piece_id, procedure_id, procedure_numero, numero_pv, type_acte, titre, `date`, service, redacteur, personnes, personnes_ids, texte, indexe_le)
            SELECT 'Pièce', pc.id, pr.id, pr.numero, pc.numero, pc.categorie, pc.libelle, pc.cree_le, pr.service, pc.cree_par, NULL, NULL,
                   CONCAT_WS('\n', pc.libelle, pc.description, pc.numero_scelle, pc.nom_fichier), UTC_TIMESTAMP()
            FROM lr_pieces pc JOIN lr_procedures pr ON pr.id = pc.procedure_id WHERE pc.id = @id
            ON DUPLICATE KEY UPDATE titre = VALUES(titre), texte = VALUES(texte), indexe_le = VALUES(indexe_le)
            """, ct, ("@id", pieceId));

    public async Task<PieceDto> MouvementAsync(Auteur auteur, long pieceId, MouvementInput input, CancellationToken ct)
    {
        if (!PieceMouvements.All.Contains(input.Mouvement))
            throw ApiException.Saisie("Mouvement inconnu.");
        await using (var db = await saphirDb.OpenAsync(ct))
        {
            await db.BeginAsync(ct);
            var piece = await db.FirstOrDefaultAsync("SELECT numero, statut FROM lr_pieces WHERE id = @id FOR UPDATE",
                r => new[] { r.Text("numero"), r.Text("statut") }, ct, ("@id", pieceId)) ?? throw ApiException.Introuvable("Pièce introuvable.");
            var statut = JudiciaireRegles.StatutApresMouvement(piece[1], input.Mouvement)
                         ?? throw ApiException.Saisie($"Mouvement « {input.Mouvement} » impossible : la pièce est « {piece[1]} ».");
            if (input.Mouvement is "Transfert de service" or "Sortie pour expertise" or "Restitution" && string.IsNullOrWhiteSpace(input.Destination))
                throw ApiException.Saisie("La destination est obligatoire pour ce mouvement.");
            await db.ExecuteAsync(
                "INSERT INTO lr_pieces_mouvements (piece_id, `date`, mouvement, destination, observations, auteur) VALUES (@piece, @maintenant, @mouvement, @destination, @observations, @auteur)", ct,
                ("@piece", pieceId), ("@maintenant", Maintenant), ("@mouvement", input.Mouvement), ("@destination", SocleRegles.Tronquer(input.Destination, 160)),
                ("@observations", SocleRegles.Tronquer(input.Observations, 500)), ("@auteur", auteur.Nom));
            await db.ExecuteAsync("UPDATE lr_pieces SET statut = @statut, localisation = COALESCE(@destination, localisation), maj_le = @maintenant WHERE id = @id", ct,
                ("@statut", statut), ("@destination", SocleRegles.Tronquer(input.Destination, 160)), ("@maintenant", Maintenant), ("@id", pieceId));
            await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Piece, piece[0], input.Mouvement, "statut", piece[1], statut, input.Observations, ct);
            await db.CommitAsync(ct);
        }
        await using var lecture = await saphirDb.OpenAsync(ct);
        return (await PiecesAsync(lecture, "pc.id = @id", ct, ("@id", pieceId))).Single();
    }

    public async Task<(byte[] Contenu, string Mime, string Nom, string Numero)> FichierPieceAsync(long pieceId, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var fichier = await db.FirstOrDefaultAsync("SELECT numero, nom_fichier, type_mime, contenu FROM lr_pieces WHERE id = @id",
            r => (Numero: r.Text("numero"), Nom: r.NullableText("nom_fichier"), Mime: r.NullableText("type_mime"), Contenu: r.Value("contenu") as byte[]).ToTuple(),
            ct, ("@id", pieceId)) ?? throw ApiException.Introuvable("Pièce introuvable.");
        if (fichier.Item4 is null || fichier.Item2 is null)
            throw ApiException.Introuvable("Aucun fichier n'est joint à cette pièce.");
        return (fichier.Item4, fichier.Item3 ?? "application/octet-stream", fichier.Item2, fichier.Item1);
    }

    // =====================================================================================
    // Dossiers associés d'une personne (14.3)
    // =====================================================================================

    public async Task<DossiersAssociesDto> DossiersAsync(long personneId, AgentDroits droits, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var fiche = await PersonnesStore.LireFicheAsync(db, personneId, droits, ct) ?? throw ApiException.Introuvable("Personne introuvable.");
        var refuses = new List<string>();

        List<TajFaitDto>? taj = null;
        if (droits.Possede(Permissions.CheopsTaj))
            taj = await db.QueryAsync(
                "SELECT t.id, t.reference, t.personne_id, t.qualite, t.faits, t.natinf, t.faits_le, t.lieu, t.service, t.procedure_numero, t.procedure_id, t.suite, t.source, t.cree_le, t.cree_par FROM ch_taj t WHERE t.personne_id = @id ORDER BY t.faits_le DESC",
                CheopsStore.LireTaj, ct, ("@id", personneId));
        else
            refuses.Add("TAJ");

        List<FprFicheDto>? fpr = null;
        if (droits.Possede(Permissions.CheopsFpr))
            fpr = await db.QueryAsync(
                $"""
                SELECT f.id, f.numero, f.categorie, f.motif, f.conduite, f.procedure_numero, f.service, f.cree_le, f.reexamen_le, f.statut, f.informations,
                       f.dangereux, f.arme, f.cree_par, {CheopsStore.ColonnesPersonneP}
                FROM ch_fpr f JOIN per_personnes p ON p.id = f.personne_id WHERE f.personne_id = @id ORDER BY f.cree_le DESC
                """, CheopsStore.LireFpr, ct, ("@id", personneId));
        else
            refuses.Add("FPR");

        List<ProcedureLigneDto>? procedures = null;
        if (JudiciaireAcces.LrppnLecture.Any(droits.Possede))
            procedures = await db.QueryAsync(
                $"""
                SELECT {ColonnesLigne} FROM lr_procedures pr
                WHERE EXISTS (SELECT 1 FROM lr_procedure_personnes pp WHERE pp.procedure_id = pr.id AND pp.personne_id = @id)
                   OR EXISTS (SELECT 1 FROM lr_actes a WHERE a.procedure_id = pr.id AND (a.personne_id = @id OR a.mis_en_cause_id = @id))
                ORDER BY pr.ouverte_le DESC
                """, LireLigne, ct, ("@id", personneId));
        else
            refuses.Add("Procédures LRPPN");

        List<DecisionDto>? casier = null;
        if (JudiciaireAcces.CasierLecture.Any(droits.Possede))
            casier = await db.QueryAsync($"SELECT {CheopsStore.ColonnesDecision} FROM ch_decisions d WHERE d.personne_id = @id ORDER BY d.decision_le DESC",
                CheopsStore.LireDecision, ct, ("@id", personneId));
        else
            refuses.Add("Casier de simulation");

        return new DossiersAssociesDto(fiche, taj, fpr, procedures, casier, refuses);
    }

    // =====================================================================================
    // Historique et modèles
    // =====================================================================================

    public async Task<List<HistoriqueDto>> HistoriqueProcedureAsync(long procedureId, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var numero = await db.ScalarAsync("SELECT numero FROM lr_procedures WHERE id = @id", ct, ("@id", procedureId)) as string
                     ?? throw ApiException.Introuvable("Procédure introuvable.");
        return await HistoriqueAsync(db,
            "(objet = 'procedure' AND reference = @numero) OR (objet IN ('acte', 'piece') AND reference LIKE @prefixe)",
            200, ct, ("@numero", numero), ("@prefixe", SocleRegles.EchapperLike(numero) + "-%"));
    }

    public async Task<List<HistoriqueDto>> HistoriqueGeneralAsync(int limite, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await HistoriqueAsync(db, "objet IN ('procedure', 'acte', 'piece', 'modele')", limite, ct);
    }

    private static Task<List<HistoriqueDto>> HistoriqueAsync(SqlSession db, string condition, int limite, CancellationToken ct,
        params (string Name, object? Value)[] parametres)
        => db.QueryAsync(
            $"SELECT id, `date`, auteur, objet, reference, `action`, champ, ancienne, nouvelle, motif FROM sys_historique WHERE {condition} ORDER BY `date` DESC, id DESC LIMIT {limite}",
            r => new HistoriqueDto(r.Long("id"), r.Date("date"), r.Text("auteur"), r.Text("objet"), r.Text("reference"), r.Text("action"),
                r.NullableText("champ"), r.NullableText("ancienne"), r.NullableText("nouvelle"), r.NullableText("motif")), ct, parametres);

    public async Task<List<ModeleDto>> ModelesAsync(CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var enregistres = await db.QueryAsync("SELECT type, texte, version, maj_par, maj_le FROM lr_modeles",
            r => new ModeleDto(r.Text("type"), string.Empty, r.Text("texte"), r.Int("version"), r.Date("maj_le"), r.Text("maj_par")), ct);
        return ActesCatalogue.All.Select(t => enregistres.FirstOrDefault(m => m.Type == t.Code) is { } m
                ? m with { Libelle = t.Libelle }
                : new ModeleDto(t.Code, t.Libelle, t.ModeleParDefaut, 0, DateTimeOffset.UnixEpoch, "SAPHIR (modèle par défaut)"))
            .ToList();
    }

    internal static async Task<string> ModeleTexteAsync(SqlSession db, TypeActeInfo type, CancellationToken ct)
        => await db.ScalarAsync("SELECT texte FROM lr_modeles WHERE type = @type", ct, ("@type", type.Code)) as string ?? type.ModeleParDefaut;

    public async Task<ModeleDto> ModifierModeleAsync(Auteur auteur, string type, ModeleInput input, CancellationToken ct)
    {
        var info = ActesCatalogue.Trouver(type) ?? throw ApiException.Introuvable("Type d'acte inconnu.");
        var texte = SocleRegles.Tronquer(input.Texte, 8000) ?? throw ApiException.Saisie("Le texte du modèle est obligatoire.");
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var avant = await db.FirstOrDefaultAsync("SELECT texte, version FROM lr_modeles WHERE type = @type FOR UPDATE",
            r => (Texte: r.Text("texte"), Version: r.Int("version")).ToTuple(), ct, ("@type", type));
        var version = (avant?.Item2 ?? 0) + 1;
        var maintenant = Maintenant;
        await db.ExecuteAsync(
            """
            INSERT INTO lr_modeles (type, texte, version, maj_par, maj_le) VALUES (@type, @texte, @version, @auteur, @maintenant)
            ON DUPLICATE KEY UPDATE texte = VALUES(texte), version = VALUES(version), maj_par = VALUES(maj_par), maj_le = VALUES(maj_le)
            """, ct, ("@type", type), ("@texte", texte), ("@version", version), ("@auteur", auteur.Nom), ("@maintenant", maintenant));
        await db.ExecuteAsync(
            "INSERT INTO lr_modeles_versions (type, version, texte, maj_par, motif, maj_le) VALUES (@type, @version, @texte, @auteur, @motif, @maintenant)", ct,
            ("@type", type), ("@version", version), ("@texte", texte), ("@auteur", auteur.Nom), ("@motif", SocleRegles.Tronquer(input.Motif, 500)),
            ("@maintenant", maintenant));
        await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Modele, type, "Nouvelle version", "texte", avant?.Item1 ?? info.ModeleParDefaut, texte, input.Motif, ct);
        await db.CommitAsync(ct);
        return new ModeleDto(type, info.Libelle, texte, version, new DateTimeOffset(maintenant, TimeSpan.Zero), auteur.Nom);
    }
}
