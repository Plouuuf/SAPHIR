using System.Globalization;
using System.Text;
using Saphir.Server.Data;
using Saphir.Server.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Judiciaire;

namespace Saphir.Server.Modules.Judiciaire;

/// <summary>
/// Fichiers CHEOPS fictifs (12) : TAJ, FPR, FOVeS, FVA, SNPC, FAED/FNAEG, FIJAISV/FIJAIT, casier de simulation, ANADOC
/// et recherche générale (11.2). Toute création ou modification est historisée dans sa transaction.
/// </summary>
public sealed class CheopsStore(SaphirDb saphirDb, PersonnesStore personnes)
{
    private const int Limite = 100;
    private static DateTime Maintenant => DateTime.UtcNow;

    internal const string ColonnesPersonneP =
        "p.id AS p_id, p.reference AS p_reference, p.nom AS p_nom, p.prenom AS p_prenom, p.date_naissance AS p_date_naissance, " +
        "p.lieu_naissance AS p_lieu_naissance, p.sexe AS p_sexe";

    internal static PersonneResumeDto LirePersonneP(SqlRow r) => new(
        r.Long("p_id"), r.Text("p_reference"), r.Text("p_nom"), r.Text("p_prenom"), Sqlj.Jour(r, "p_date_naissance"),
        r.NullableText("p_lieu_naissance"), r.NullableText("p_sexe"), Array.Empty<string>());

    /// <summary>Conditions « chaque terme dans NOM, prénom ou alias » sur la table p.</summary>
    private static void AjouterTermes(StringBuilder sql, List<(string Name, object? Value)> parametres, string? q)
    {
        var termes = JudiciaireRegles.Termes(q);
        for (var i = 0; i < termes.Count; i++)
        {
            if (JudiciaireRegles.LireDate(termes[i]) is { } date)
            {
                sql.Append($" AND p.date_naissance = @qd{i}");
                parametres.Add(($"@qd{i}", Sqlj.Jour(date)));
                continue;
            }
            sql.Append($" AND (p.nom LIKE @q{i} OR p.prenom LIKE @q{i} OR p.reference = @qr{i} OR EXISTS (SELECT 1 FROM per_alias a WHERE a.personne_id = p.id AND a.alias LIKE @q{i}))");
            parametres.Add(($"@q{i}", Sqlj.Like(termes[i])));
            parametres.Add(($"@qr{i}", termes[i].ToUpperInvariant()));
        }
    }

    private static async Task<long> PersonneExistanteAsync(SqlSession db, long personneId, CancellationToken ct)
    {
        _ = await PersonnesStore.ReferenceAsync(db, personneId, ct);
        return personneId;
    }

    // =====================================================================================
    // TAJ (12.1)
    // =====================================================================================

    private const string ColonnesTaj =
        "t.id, t.reference, t.personne_id, t.qualite, t.faits, t.natinf, t.faits_le, t.lieu, t.service, t.procedure_numero, t.procedure_id, t.suite, t.source, t.cree_le, t.cree_par";

    internal static TajFaitDto LireTaj(SqlRow r) => new(
        r.Long("id"), r.Text("reference"), r.Long("personne_id"), r.Text("qualite"), r.Text("faits"), r.NullableText("natinf"),
        r.NullableDate("faits_le"), r.NullableText("lieu"), r.NullableText("service"), r.NullableText("procedure_numero"),
        r.NullableLong("procedure_id"), r.NullableText("suite"), r.Text("source"), r.Date("cree_le"), r.Text("cree_par"));

    public async Task<List<TajLigneDto>> RechercherTajAsync(string? q, string? naissance, string? reference, string? procedure, CancellationToken ct)
    {
        var sql = new StringBuilder(
            $"""
            SELECT {ColonnesPersonneP}, COUNT(t.id) AS nombre, MAX(t.faits_le) AS dernier
            FROM ch_taj t JOIN per_personnes p ON p.id = t.personne_id WHERE 1 = 1
            """);
        var parametres = new List<(string Name, object? Value)>();
        AjouterTermes(sql, parametres, q);
        if (!string.IsNullOrWhiteSpace(naissance))
        {
            sql.Append(" AND p.date_naissance = @naissance");
            parametres.Add(("@naissance", Sqlj.Jour(JudiciaireRegles.LireDate(naissance) ?? throw ApiException.Saisie("Date de naissance attendue au format jj/mm/aaaa."))));
        }
        if (!string.IsNullOrWhiteSpace(reference))
        {
            sql.Append(" AND t.reference = @reference");
            parametres.Add(("@reference", reference.Trim().ToUpperInvariant()));
        }
        if (!string.IsNullOrWhiteSpace(procedure))
        {
            sql.Append(" AND t.procedure_numero LIKE @procedure");
            parametres.Add(("@procedure", Sqlj.Like(procedure)));
        }
        if (parametres.Count == 0)
            return [];
        sql.Append($" GROUP BY p.id, p.reference, p.nom, p.prenom, p.date_naissance, p.lieu_naissance, p.sexe ORDER BY p.nom, p.prenom LIMIT {Limite}");

        await using var db = await saphirDb.OpenAsync(ct);
        return await db.QueryAsync(sql.ToString(),
            r => new TajLigneDto(LirePersonneP(r), r.Int("nombre"), r.NullableDate("dernier")), ct, parametres.ToArray());
    }

    public async Task<TajFicheDto> TajFicheAsync(long personneId, AgentDroits droits, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var fiche = await PersonnesStore.LireFicheAsync(db, personneId, droits, ct) ?? throw ApiException.Introuvable("Personne introuvable.");
        var faits = await db.QueryAsync($"SELECT {ColonnesTaj} FROM ch_taj t WHERE t.personne_id = @id ORDER BY t.faits_le DESC, t.id DESC",
            LireTaj, ct, ("@id", personneId));
        return new TajFicheDto(fiche, faits, await Sqlj.HistoriqueAsync(db, ObjetsJudiciaire.Taj, fiche.Reference, ct));
    }

    /// <summary>
    /// Inscription au TAJ (saisie manuelle ou validation d'un acte LRPPN, 20.4). Une même personne n'est inscrite qu'une fois par acte.
    /// Retourne la référence créée, ou null si l'inscription existait déjà.
    /// </summary>
    internal static async Task<string?> InscrireTajAsync(SqlSession db, Auteur auteur, long personneId, string qualite, string faits,
        string? natinf, DateTimeOffset? faitsLe, string? lieu, string? service, string? procedureNumero, long? procedureId, long? acteId,
        string? suite, string source, CancellationToken ct)
    {
        var id = await db.InsertAsync(
            """
            INSERT IGNORE INTO ch_taj (personne_id, qualite, faits, natinf, faits_le, lieu, service, procedure_numero, procedure_id, acte_id,
                suite, source, cree_par, cree_le)
            VALUES (@personne, @qualite, @faits, @natinf, @faitsLe, @lieu, @service, @procedure, @procedureId, @acte, @suite, @source, @auteur, @maintenant)
            """, ct,
            ("@personne", personneId), ("@qualite", qualite), ("@faits", SocleRegles.Tronquer(faits, 255) ?? "Faits non précisés"),
            ("@natinf", SocleRegles.Tronquer(natinf, 20)), ("@faitsLe", Sqlj.Utc(faitsLe)), ("@lieu", SocleRegles.Tronquer(lieu, 255)),
            ("@service", SocleRegles.Tronquer(service, 120)), ("@procedure", SocleRegles.Tronquer(procedureNumero, 40)),
            ("@procedureId", procedureId), ("@acte", acteId), ("@suite", SocleRegles.Tronquer(suite, 500)), ("@source", source),
            ("@auteur", auteur.Nom), ("@maintenant", Maintenant));
        if (id <= 0)
            return null;
        var reference = JudiciaireRegles.Reference("TAJ", id);
        await db.ExecuteAsync("UPDATE ch_taj SET reference = @reference WHERE id = @id", ct, ("@reference", reference), ("@id", id));
        var personneRef = await PersonnesStore.ReferenceAsync(db, personneId, ct);
        await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Taj, personneRef, "Inscription", reference, null,
            $"{qualite} — {faits}" + (procedureNumero is null ? string.Empty : $" (procédure {procedureNumero})"), source, ct);
        return reference;
    }

    public async Task<string> AjouterTajAsync(Auteur auteur, TajFaitInput input, CancellationToken ct)
    {
        if (!TajQualites.All.Contains(input.Qualite))
            throw ApiException.Saisie("Qualité attendue : mis en cause ou victime.");
        if (string.IsNullOrWhiteSpace(input.Faits))
            throw ApiException.Saisie("Les faits sont obligatoires.");
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        await PersonneExistanteAsync(db, input.PersonneId, ct);
        var reference = await InscrireTajAsync(db, auteur, input.PersonneId, input.Qualite, input.Faits, input.Natinf, input.FaitsLe,
            input.Lieu, input.Service, input.ProcedureNumero, null, null, input.Suite, "Saisie manuelle", ct)
            ?? throw ApiException.Conflit("Inscription déjà existante.");
        await db.CommitAsync(ct);
        return reference;
    }

    public async Task ModifierSuiteTajAsync(Auteur auteur, long id, TajSuiteInput input, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var avant = await db.FirstOrDefaultAsync(
            "SELECT t.reference, t.suite, p.reference AS personne FROM ch_taj t JOIN per_personnes p ON p.id = t.personne_id WHERE t.id = @id FOR UPDATE",
            r => new[] { r.Text("reference"), r.NullableText("suite"), r.Text("personne") }, ct, ("@id", id))
            ?? throw ApiException.Introuvable("Inscription TAJ introuvable.");
        var suite = SocleRegles.Tronquer(input.Suite, 500);
        await db.ExecuteAsync("UPDATE ch_taj SET suite = @suite WHERE id = @id", ct, ("@suite", suite), ("@id", id));
        await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Taj, avant[2]!, "Modification", "Suite " + avant[0], avant[1], suite, input.Motif, ct);
        await db.CommitAsync(ct);
    }

    // =====================================================================================
    // FPR (12.2)
    // =====================================================================================

    private const string ColonnesFpr =
        "f.id, f.numero, f.categorie, f.motif, f.conduite, f.procedure_numero, f.service, f.cree_le, f.reexamen_le, f.statut, f.informations, f.dangereux, f.arme, f.cree_par";

    internal static FprFicheDto LireFpr(SqlRow r) => new(
        r.Long("id"), r.Text("numero"), LirePersonneP(r), r.Text("categorie"), r.Text("motif"), r.Text("conduite"),
        r.NullableText("procedure_numero"), r.NullableText("service"), r.Date("cree_le"), Sqlj.Jour(r, "reexamen_le"), r.Text("statut"),
        r.NullableText("informations"), r.Bool("dangereux"), r.Bool("arme"), r.Text("cree_par"), Array.Empty<HistoriqueDto>());

    public async Task<List<FprFicheDto>> RechercherFprAsync(string? q, string? statut, CancellationToken ct)
    {
        var sql = new StringBuilder($"SELECT {ColonnesFpr}, {ColonnesPersonneP} FROM ch_fpr f JOIN per_personnes p ON p.id = f.personne_id WHERE 1 = 1");
        var parametres = new List<(string Name, object? Value)>();
        AjouterTermes(sql, parametres, q);
        if (!string.IsNullOrWhiteSpace(q) && q.Trim().StartsWith("FPR-", StringComparison.OrdinalIgnoreCase))
        {
            sql = new StringBuilder($"SELECT {ColonnesFpr}, {ColonnesPersonneP} FROM ch_fpr f JOIN per_personnes p ON p.id = f.personne_id WHERE f.numero = @numero");
            parametres = [("@numero", q.Trim().ToUpperInvariant())];
        }
        if (!string.IsNullOrWhiteSpace(statut))
        {
            sql.Append(" AND f.statut = @statut");
            parametres.Add(("@statut", statut.Trim()));
        }
        sql.Append($" ORDER BY f.statut = 'Active' DESC, f.cree_le DESC LIMIT {Limite}");
        await using var db = await saphirDb.OpenAsync(ct);
        return await db.QueryAsync(sql.ToString(), LireFpr, ct, parametres.ToArray());
    }

    public async Task<FprFicheDto> FprAsync(long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var fiche = await db.FirstOrDefaultAsync(
            $"SELECT {ColonnesFpr}, {ColonnesPersonneP} FROM ch_fpr f JOIN per_personnes p ON p.id = f.personne_id WHERE f.id = @id",
            LireFpr, ct, ("@id", id)) ?? throw ApiException.Introuvable("Fiche FPR introuvable.");
        return fiche with { Historique = await Sqlj.HistoriqueAsync(db, ObjetsJudiciaire.Fpr, fiche.Numero, ct) };
    }

    public async Task<FprFicheDto> InscrireFprAsync(Auteur auteur, FprInput input, CancellationToken ct)
    {
        if (!FprCategories.All.Contains(input.Categorie))
            throw ApiException.Saisie("Catégorie de recherche inconnue.");
        var motif = SocleRegles.Tronquer(input.Motif, 255) ?? throw ApiException.Saisie("Le motif est obligatoire.");
        var conduite = SocleRegles.Tronquer(input.ConduiteATenir, 500) ?? throw ApiException.Saisie("La conduite à tenir est obligatoire.");
        if (input.ReexamenLe is { } reexamen && reexamen < DateOnly.FromDateTime(DateTime.UtcNow))
            throw ApiException.Saisie("La date de réexamen doit être future.");

        long id;
        await using (var db = await saphirDb.OpenAsync(ct))
        {
            await db.BeginAsync(ct);
            await PersonneExistanteAsync(db, input.PersonneId, ct);
            id = await db.InsertAsync(
                """
                INSERT INTO ch_fpr (personne_id, categorie, motif, conduite, procedure_numero, service, reexamen_le, statut, informations,
                    dangereux, arme, cree_par, cree_le, maj_le)
                VALUES (@personne, @categorie, @motif, @conduite, @procedure, @service, @reexamen, 'Active', @informations, @dangereux, @arme,
                    @auteur, @maintenant, @maintenant)
                """, ct,
                ("@personne", input.PersonneId), ("@categorie", input.Categorie), ("@motif", motif), ("@conduite", conduite),
                ("@procedure", SocleRegles.Tronquer(input.ProcedureNumero, 40)), ("@service", SocleRegles.Tronquer(input.Service, 120)),
                ("@reexamen", Sqlj.Jour(input.ReexamenLe)), ("@informations", SocleRegles.Tronquer(input.Informations, 4000)),
                ("@dangereux", input.Dangereux), ("@arme", input.Arme), ("@auteur", auteur.Nom), ("@maintenant", Maintenant));
            var numero = JudiciaireRegles.Reference("FPR", id);
            await db.ExecuteAsync("UPDATE ch_fpr SET numero = @numero WHERE id = @id", ct, ("@numero", numero), ("@id", id));
            await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Fpr, numero, "Inscription", "statut", null, FprStatuts.Active, motif, ct);
            await db.CommitAsync(ct);
        }
        return await FprAsync(id, ct);
    }

    public async Task<FprFicheDto> ChangerStatutFprAsync(Auteur auteur, long id, StatutInput input, CancellationToken ct)
    {
        await using (var db = await saphirDb.OpenAsync(ct))
        {
            await db.BeginAsync(ct);
            var avant = await db.FirstOrDefaultAsync("SELECT numero, statut FROM ch_fpr WHERE id = @id FOR UPDATE",
                r => new[] { r.Text("numero"), r.Text("statut") }, ct, ("@id", id)) ?? throw ApiException.Introuvable("Fiche FPR introuvable.");
            if (JudiciaireRegles.ControlerTransitionFpr(avant[1], input.Statut, input.Motif) is { } erreur)
                throw ApiException.Saisie(erreur);
            await db.ExecuteAsync("UPDATE ch_fpr SET statut = @statut, maj_le = @maintenant WHERE id = @id", ct,
                ("@statut", input.Statut), ("@maintenant", Maintenant), ("@id", id));
            await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Fpr, avant[0], "Statut", "statut", avant[1], input.Statut, input.Motif, ct);
            await db.CommitAsync(ct);
        }
        return await FprAsync(id, ct);
    }

    // =====================================================================================
    // FOVeS (12.3)
    // =====================================================================================

    public const string ColonnesFoves =
        "f.id, f.reference, f.type_objet, f.identifiant, f.vehicule_id, f.description, f.nature, f.signale_le, f.service, f.procedure_numero, " +
        "f.statut, f.decouvert_le, f.lieu_decouverte, f.conduite, f.cree_par";

    public static FovesFicheDto LireFoves(SqlRow r) => new(
        r.Long("id"), r.Text("reference"), r.Text("type_objet"), r.Text("identifiant"), r.NullableLong("vehicule_id"),
        r.NullableText("description"), r.Text("nature"), r.Date("signale_le"), r.NullableText("service"), r.NullableText("procedure_numero"),
        r.Text("statut"), r.NullableDate("decouvert_le"), r.NullableText("lieu_decouverte"), r.NullableText("conduite"), r.Text("cree_par"),
        Array.Empty<HistoriqueDto>());

    public async Task<List<FovesFicheDto>> RechercherFovesAsync(string? q, string? statut, CancellationToken ct)
    {
        var sql = new StringBuilder($"SELECT {ColonnesFoves} FROM ch_foves f WHERE 1 = 1");
        var parametres = new List<(string Name, object? Value)>();
        if (!string.IsNullOrWhiteSpace(q))
        {
            sql.Append(" AND (f.identifiant_norm LIKE @norm OR f.reference = @reference OR f.description LIKE @texte OR f.procedure_numero LIKE @texte)");
            parametres.Add(("@norm", Sqlj.Like(JudiciaireRegles.Normaliser(q).Length == 0 ? q : JudiciaireRegles.Normaliser(q))));
            parametres.Add(("@reference", q.Trim().ToUpperInvariant()));
            parametres.Add(("@texte", Sqlj.Like(q)));
        }
        if (!string.IsNullOrWhiteSpace(statut))
        {
            sql.Append(" AND f.statut = @statut");
            parametres.Add(("@statut", statut.Trim()));
        }
        sql.Append($" ORDER BY f.signale_le DESC LIMIT {Limite}");
        await using var db = await saphirDb.OpenAsync(ct);
        return await db.QueryAsync(sql.ToString(), LireFoves, ct, parametres.ToArray());
    }

    public async Task<FovesFicheDto> FovesAsync(long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var fiche = await db.FirstOrDefaultAsync($"SELECT {ColonnesFoves} FROM ch_foves f WHERE f.id = @id", LireFoves, ct, ("@id", id))
                    ?? throw ApiException.Introuvable("Signalement FOVeS introuvable.");
        return fiche with { Historique = await Sqlj.HistoriqueAsync(db, ObjetsJudiciaire.Foves, fiche.Reference, ct) };
    }

    public async Task<FovesFicheDto> SignalerFovesAsync(Auteur auteur, FovesInput input, CancellationToken ct)
    {
        if (!FovesTypes.All.Contains(input.TypeObjet))
            throw ApiException.Saisie("Type d'objet inconnu.");
        if (!FovesTypes.Natures.Contains(input.Nature))
            throw ApiException.Saisie("Nature du signalement inconnue.");
        var identifiant = SocleRegles.Tronquer(input.Identifiant, 60) ?? throw ApiException.Saisie("Immatriculation ou numéro de série obligatoire.");
        var norm = JudiciaireRegles.Normaliser(identifiant);
        if (norm.Length < 2)
            throw ApiException.Saisie("Immatriculation ou numéro de série invalide.");
        var vehicule = input.TypeObjet is "Véhicule" or "Plaque d'immatriculation";
        if (vehicule)
            identifiant = JudiciaireRegles.FormaterImmatriculation(identifiant);

        long id;
        await using (var db = await saphirDb.OpenAsync(ct))
        {
            await db.BeginAsync(ct);
            var doublon = await db.ScalarAsync("SELECT reference FROM ch_foves WHERE identifiant_norm = @norm AND statut = 'Signalement actif' LIMIT 1", ct, ("@norm", norm));
            if (doublon is string refExistante)
                throw ApiException.Conflit($"Un signalement actif existe déjà pour cet objet ({refExistante}).");
            long? vehiculeId = null;
            if (vehicule && await db.ScalarAsync("SELECT id FROM veh_vehicules WHERE immatriculation = @norm", ct, ("@norm", norm)) is { } v)
                vehiculeId = Convert.ToInt64(v, CultureInfo.InvariantCulture);
            id = await db.InsertAsync(
                """
                INSERT INTO ch_foves (type_objet, identifiant, identifiant_norm, vehicule_id, description, nature, signale_le, service,
                    procedure_numero, statut, conduite, cree_par, cree_le, maj_le)
                VALUES (@type, @identifiant, @norm, @vehicule, @description, @nature, @signale, @service, @procedure, 'Signalement actif',
                    @conduite, @auteur, @maintenant, @maintenant)
                """, ct,
                ("@type", input.TypeObjet), ("@identifiant", identifiant), ("@norm", norm), ("@vehicule", vehiculeId),
                ("@description", SocleRegles.Tronquer(input.Description, 4000)), ("@nature", input.Nature),
                ("@signale", (input.SignaleLe ?? DateTimeOffset.UtcNow).UtcDateTime), ("@service", SocleRegles.Tronquer(input.Service, 120)),
                ("@procedure", SocleRegles.Tronquer(input.ProcedureNumero, 40)), ("@conduite", SocleRegles.Tronquer(input.ConduiteATenir, 500)),
                ("@auteur", auteur.Nom), ("@maintenant", Maintenant));
            var reference = JudiciaireRegles.Reference("FOV", id);
            await db.ExecuteAsync("UPDATE ch_foves SET reference = @reference WHERE id = @id", ct, ("@reference", reference), ("@id", id));
            await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Foves, reference, "Signalement", "statut", null, FovesStatuts.Actif,
                $"{input.Nature} — {identifiant}", ct);
            await db.CommitAsync(ct);
        }
        return await FovesAsync(id, ct);
    }

    public async Task<FovesFicheDto> ChangerStatutFovesAsync(Auteur auteur, long id, FovesStatutInput input, CancellationToken ct)
    {
        if (!FovesStatuts.All.Contains(input.Statut))
            throw ApiException.Saisie("Statut inconnu.");
        await using (var db = await saphirDb.OpenAsync(ct))
        {
            await db.BeginAsync(ct);
            var avant = await db.FirstOrDefaultAsync("SELECT reference, statut FROM ch_foves WHERE id = @id FOR UPDATE",
                r => new[] { r.Text("reference"), r.Text("statut") }, ct, ("@id", id)) ?? throw ApiException.Introuvable("Signalement FOVeS introuvable.");
            var autorise = avant[1] switch
            {
                FovesStatuts.Actif => input.Statut is FovesStatuts.Decouvert or FovesStatuts.Leve,
                FovesStatuts.Decouvert or FovesStatuts.Leve => input.Statut == FovesStatuts.Archive,
                _ => false
            };
            if (!autorise)
                throw ApiException.Saisie($"Passage de « {avant[1]} » à « {input.Statut} » impossible.");
            if (input.Statut == FovesStatuts.Decouvert && (input.DecouvertLe is null || string.IsNullOrWhiteSpace(input.LieuDecouverte)))
                throw ApiException.Saisie("Date et lieu de découverte obligatoires.");
            if (input.Statut == FovesStatuts.Leve && string.IsNullOrWhiteSpace(input.Motif))
                throw ApiException.Saisie("Le motif de levée est obligatoire.");
            await db.ExecuteAsync(
                """
                UPDATE ch_foves SET statut = @statut, decouvert_le = COALESCE(@decouvert, decouvert_le),
                    lieu_decouverte = COALESCE(@lieu, lieu_decouverte), maj_le = @maintenant
                WHERE id = @id
                """, ct,
                ("@statut", input.Statut), ("@decouvert", Sqlj.Utc(input.DecouvertLe)), ("@lieu", SocleRegles.Tronquer(input.LieuDecouverte, 255)),
                ("@maintenant", Maintenant), ("@id", id));
            await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Foves, avant[0], "Statut", "statut", avant[1], input.Statut,
                input.Motif ?? input.LieuDecouverte, ct);
            await db.CommitAsync(ct);
        }
        return await FovesAsync(id, ct);
    }

    // =====================================================================================
    // FVA (12.5)
    // =====================================================================================

    public async Task<List<FvaLigneDto>> RechercherFvaAsync(string? plaque, AgentDroits droits, CancellationToken ct)
    {
        var vehicules = await personnes.RechercherParPlaqueAsync(plaque, droits, ct);
        if (vehicules.Count == 0)
            return [];
        await using var db = await saphirDb.OpenAsync(ct);
        var (liste, parametres) = Sqlj.In("v", vehicules.Select(v => v.Id));
        var contrats = await db.QueryAsync(
            $"SELECT {PersonnesStore.ColonnesAssurance} FROM veh_assurances a JOIN veh_vehicules v ON v.id = a.vehicule_id WHERE a.vehicule_id IN ({liste}) ORDER BY a.debut DESC",
            PersonnesStore.LireAssurance, ct, parametres);
        return vehicules.Select(v =>
        {
            var duVehicule = contrats.Where(c => c.VehiculeId == v.Id).ToList();
            var courant = duVehicule.FirstOrDefault(c => c.Couvert);
            var situation = courant is not null ? "Assuré" : duVehicule.Count == 0 ? "Aucun contrat connu" : "Non assuré (contrat expiré ou résilié)";
            return new FvaLigneDto(v, situation, courant ?? duVehicule.FirstOrDefault());
        }).ToList();
    }

    public async Task AjouterAssuranceAsync(Auteur auteur, AssuranceInput input, CancellationToken ct)
    {
        var assureur = SocleRegles.Tronquer(input.Assureur, 100) ?? throw ApiException.Saisie("Assureur obligatoire.");
        var contrat = SocleRegles.Tronquer(input.Contrat, 60) ?? throw ApiException.Saisie("Numéro de contrat obligatoire.");
        if (!AssuranceStatuts.All.Contains(input.Statut))
            throw ApiException.Saisie("Statut de contrat inconnu.");
        if (input.Fin is { } fin && fin < input.Debut)
            throw ApiException.Saisie("La fin de couverture précède le début.");
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var immat = await db.ScalarAsync("SELECT affichage FROM veh_vehicules WHERE id = @id", ct, ("@id", input.VehiculeId)) as string
                    ?? throw ApiException.Introuvable("Véhicule introuvable.");
        await db.ExecuteAsync(
            """
            INSERT INTO veh_assurances (vehicule_id, assureur, contrat, debut, fin, statut, cree_le)
            VALUES (@vehicule, @assureur, @contrat, @debut, @fin, @statut, @maintenant)
            """, ct,
            ("@vehicule", input.VehiculeId), ("@assureur", assureur), ("@contrat", contrat), ("@debut", Sqlj.Jour(input.Debut)),
            ("@fin", Sqlj.Jour(input.Fin)), ("@statut", input.Statut), ("@maintenant", Maintenant));
        await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Vehicule, immat, "Assurance ajoutée", "FVA", null,
            $"{assureur} — {contrat} ({input.Statut})", null, ct);
        await db.CommitAsync(ct);
    }

    /// <summary>Vérification d'un contrat (date de vérification FVA), historisée sur le véhicule.</summary>
    public async Task VerifierAssuranceAsync(Auteur auteur, long assuranceId, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var immat = await db.ScalarAsync(
            "SELECT v.affichage FROM veh_assurances a JOIN veh_vehicules v ON v.id = a.vehicule_id WHERE a.id = @id", ct, ("@id", assuranceId)) as string
            ?? throw ApiException.Introuvable("Contrat introuvable.");
        await db.ExecuteAsync("UPDATE veh_assurances SET verifie_le = @maintenant WHERE id = @id", ct, ("@maintenant", Maintenant), ("@id", assuranceId));
        await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Vehicule, immat, "Vérification FVA", "FVA", null, "Contrat vérifié", null, ct);
        await db.CommitAsync(ct);
    }

    // =====================================================================================
    // SNPC (12.6)
    // =====================================================================================

    private const string ColonnesPermis = "c.id, c.reference, c.categories, c.dates_obtention, c.validite, c.situation, c.points";

    private static PermisDto LirePermis(SqlRow r) => new(
        r.Long("id"), LirePersonneP(r), r.Text("reference"), r.Text("categories"), r.NullableText("dates_obtention"),
        Sqlj.Jour(r, "validite"), r.Text("situation"), r.Int("points"), JudiciaireRegles.PointsMax, Array.Empty<HistoriqueDto>());

    public async Task<List<PermisDto>> RechercherPermisAsync(string? q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q))
            return [];
        var sql = new StringBuilder($"SELECT {ColonnesPermis}, {ColonnesPersonneP} FROM ch_permis c JOIN per_personnes p ON p.id = c.personne_id WHERE p.retire_le IS NULL AND ");
        var parametres = new List<(string Name, object? Value)> { ("@permis", q.Trim().ToUpperInvariant()) };
        sql.Append("(c.reference = @permis OR (1 = 1");
        AjouterTermes(sql, parametres, q);
        sql.Append($")) ORDER BY p.nom, p.prenom LIMIT {Limite}");
        await using var db = await saphirDb.OpenAsync(ct);
        return await db.QueryAsync(sql.ToString(), LirePermis, ct, parametres.ToArray());
    }

    public async Task<PermisDto> PermisAsync(long personneId, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var permis = await db.FirstOrDefaultAsync(
            $"SELECT {ColonnesPermis}, {ColonnesPersonneP} FROM ch_permis c JOIN per_personnes p ON p.id = c.personne_id WHERE c.personne_id = @id",
            LirePermis, ct, ("@id", personneId)) ?? throw ApiException.Introuvable("Aucun permis de conduire enregistré pour cette personne.");
        return permis with { Historique = await Sqlj.HistoriqueAsync(db, ObjetsJudiciaire.Permis, permis.Reference, ct) };
    }

    public async Task<PermisDto> EnregistrerPermisAsync(Auteur auteur, long personneId, PermisInput input, CancellationToken ct)
    {
        var categories = SocleRegles.Tronquer(input.Categories, 60) ?? throw ApiException.Saisie("Catégories obligatoires (ex. « B » ou « A2, B »).");
        if (!PermisSituations.All.Contains(input.Situation))
            throw ApiException.Saisie("Situation du titre inconnue.");
        if (input.Points is < 0 or > JudiciaireRegles.PointsMax)
            throw ApiException.Saisie("Le solde de points doit être compris entre 0 et 12.");
        var situation = input.Points == 0 ? PermisSituations.Invalide : input.Situation;

        await using (var db = await saphirDb.OpenAsync(ct))
        {
            await db.BeginAsync(ct);
            await PersonneExistanteAsync(db, personneId, ct);
            var avant = await db.FirstOrDefaultAsync(
                "SELECT id, reference, categories, dates_obtention, validite, situation, points FROM ch_permis WHERE personne_id = @id FOR UPDATE",
                r => new Dictionary<string, string?>
                {
                    ["id"] = r.Long("id").ToString(CultureInfo.InvariantCulture),
                    ["reference"] = r.Text("reference"),
                    ["Catégories"] = r.Text("categories"),
                    ["Dates d'obtention"] = r.NullableText("dates_obtention"),
                    ["Validité"] = Sqlj.Texte(Sqlj.Jour(r, "validite")),
                    ["Situation"] = r.Text("situation"),
                    ["Points"] = r.Int("points").ToString(CultureInfo.InvariantCulture)
                }, ct, ("@id", personneId));
            var apres = new Dictionary<string, string?>
            {
                ["Catégories"] = categories,
                ["Dates d'obtention"] = SocleRegles.Tronquer(input.DatesObtention, 255),
                ["Validité"] = Sqlj.Texte(input.ValiditeJusquAu),
                ["Situation"] = situation,
                ["Points"] = input.Points.ToString(CultureInfo.InvariantCulture)
            };
            var parametres = new (string Name, object? Value)[]
            {
                ("@personne", personneId), ("@categories", categories), ("@dates", apres["Dates d'obtention"]),
                ("@validite", Sqlj.Jour(input.ValiditeJusquAu)), ("@situation", situation), ("@points", input.Points), ("@maintenant", Maintenant)
            };
            if (avant is null)
            {
                var id = await db.InsertAsync(
                    """
                    INSERT INTO ch_permis (personne_id, categories, dates_obtention, validite, situation, points, cree_le, maj_le)
                    VALUES (@personne, @categories, @dates, @validite, @situation, @points, @maintenant, @maintenant)
                    """, ct, parametres);
                var reference = JudiciaireRegles.Reference("PC", id);
                await db.ExecuteAsync("UPDATE ch_permis SET reference = @reference WHERE id = @id", ct, ("@reference", reference), ("@id", id));
                await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Permis, reference, "Création", null, null, $"{categories} — {input.Points} point(s)", input.Motif, ct);
            }
            else
            {
                await db.ExecuteAsync(
                    """
                    UPDATE ch_permis SET categories = @categories, dates_obtention = @dates, validite = @validite, situation = @situation,
                        points = @points, maj_le = @maintenant
                    WHERE personne_id = @personne
                    """, ct, parametres);
                await Sqlj.HistoriserChangementsAsync(db, auteur, ObjetsJudiciaire.Permis, avant["reference"]!,
                    apres.Select(kv => (kv.Key, avant[kv.Key], kv.Value)), input.Motif, ct);
            }
            await db.CommitAsync(ct);
        }
        return await PermisAsync(personneId, ct);
    }

    public async Task<PermisDto> ModifierPointsAsync(Auteur auteur, long personneId, PointsInput input, CancellationToken ct)
    {
        if (input.Variation == 0 || Math.Abs(input.Variation) > JudiciaireRegles.PointsMax)
            throw ApiException.Saisie("Indique un retrait ou une restitution de 1 à 12 points.");
        var motif = SocleRegles.Tronquer(input.Motif, 500) ?? throw ApiException.Saisie("Le motif est obligatoire (infraction ou stage).");
        await using (var db = await saphirDb.OpenAsync(ct))
        {
            await db.BeginAsync(ct);
            var avant = await db.FirstOrDefaultAsync("SELECT reference, situation, points FROM ch_permis WHERE personne_id = @id FOR UPDATE",
                r => (Reference: r.Text("reference"), Situation: r.Text("situation"), Points: r.Int("points")).ToTuple(), ct, ("@id", personneId))
                ?? throw ApiException.Introuvable("Aucun permis de conduire enregistré pour cette personne.");
            if (avant.Item2 == PermisSituations.Annule)
                throw ApiException.Saisie("Le permis est annulé : le solde de points ne peut plus évoluer.");
            var points = JudiciaireRegles.AppliquerPoints(avant.Item3, input.Variation);
            var situation = JudiciaireRegles.SituationApresPoints(avant.Item2, points);
            await db.ExecuteAsync("UPDATE ch_permis SET points = @points, situation = @situation, maj_le = @maintenant WHERE personne_id = @id", ct,
                ("@points", points), ("@situation", situation), ("@maintenant", Maintenant), ("@id", personneId));
            await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Permis, avant.Item1, input.Variation < 0 ? "Retrait de points" : "Restitution de points",
                "points", avant.Item3.ToString(CultureInfo.InvariantCulture), points.ToString(CultureInfo.InvariantCulture), motif, ct);
            if (situation != avant.Item2)
                await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Permis, avant.Item1, "Situation", "situation", avant.Item2, situation, motif, ct);
            await db.CommitAsync(ct);
        }
        return await PermisAsync(personneId, ct);
    }

    // =====================================================================================
    // FAED / FNAEG (12.7, 12.8)
    // =====================================================================================

    private const string ColonnesScientifique =
        "s.id, s.fichier, s.reference, s.reference_prelevement, s.origine, s.procedure_numero, s.type, s.preleve_le, s.personne_id, " +
        "s.resultat, s.rapport, s.cree_par, p.nom AS p_nom, p.prenom AS p_prenom, p.reference AS p_reference";

    private static ScientifiqueDto LireScientifique(SqlRow r) => new(
        r.Long("id"), r.Text("fichier"), r.Text("reference"), r.NullableText("reference_prelevement"), r.Text("origine"),
        r.NullableText("procedure_numero"), r.Text("type"), r.Date("preleve_le"), r.NullableLong("personne_id"),
        r.NullableText("p_nom") is { } nom ? $"{nom} {r.Text("p_prenom")} ({r.Text("p_reference")})" : null,
        r.Text("resultat"), r.NullableText("rapport"), r.Text("cree_par"), Array.Empty<HistoriqueDto>());

    private static string ControlerFichierScientifique(string? fichier)
        => fichier is FichiersCheops.Faed or FichiersCheops.Fnaeg ? fichier : throw ApiException.Saisie("Fichier attendu : FAED ou FNAEG.");

    public async Task<List<ScientifiqueDto>> RechercherScientifiqueAsync(string fichier, string? q, CancellationToken ct)
    {
        ControlerFichierScientifique(fichier);
        var sql = new StringBuilder($"SELECT {ColonnesScientifique} FROM ch_scientifique s LEFT JOIN per_personnes p ON p.id = s.personne_id WHERE s.fichier = @fichier");
        var parametres = new List<(string Name, object? Value)> { ("@fichier", fichier) };
        if (!string.IsNullOrWhiteSpace(q))
        {
            sql.Append(" AND (s.reference = @exact OR s.reference_prelevement = @exact OR s.procedure_numero LIKE @texte OR s.origine LIKE @texte OR p.nom LIKE @texte OR p.reference = @exact)");
            parametres.Add(("@exact", q.Trim().ToUpperInvariant()));
            parametres.Add(("@texte", Sqlj.Like(q)));
        }
        sql.Append($" ORDER BY s.preleve_le DESC LIMIT {Limite}");
        await using var db = await saphirDb.OpenAsync(ct);
        return await db.QueryAsync(sql.ToString(), LireScientifique, ct, parametres.ToArray());
    }

    public async Task<ScientifiqueDto> ScientifiqueAsync(long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var fiche = await db.FirstOrDefaultAsync(
            $"SELECT {ColonnesScientifique} FROM ch_scientifique s LEFT JOIN per_personnes p ON p.id = s.personne_id WHERE s.id = @id",
            LireScientifique, ct, ("@id", id)) ?? throw ApiException.Introuvable("Enregistrement introuvable.");
        return fiche with { Historique = await Sqlj.HistoriqueAsync(db, ObjetsJudiciaire.Scientifique, fiche.Reference, ct) };
    }

    public async Task<ScientifiqueDto> CreerScientifiqueAsync(Auteur auteur, ScientifiqueInput input, CancellationToken ct)
    {
        var fichier = ControlerFichierScientifique(input.Fichier);
        var types = fichier == FichiersCheops.Faed ? ScientifiqueResultats.TypesFaed : ScientifiqueResultats.TypesFnaeg;
        if (!types.Contains(input.Type))
            throw ApiException.Saisie("Type inconnu pour ce fichier.");
        var origine = SocleRegles.Tronquer(input.Origine, 255) ?? throw ApiException.Saisie("L'origine (affaire, lieu) est obligatoire.");
        long id;
        await using (var db = await saphirDb.OpenAsync(ct))
        {
            await db.BeginAsync(ct);
            if (input.PersonneId is { } personneId)
                await PersonneExistanteAsync(db, personneId, ct);
            id = await db.InsertAsync(
                """
                INSERT INTO ch_scientifique (fichier, reference_prelevement, origine, procedure_numero, type, preleve_le, personne_id, resultat,
                    rapport, cree_par, cree_le, maj_le)
                VALUES (@fichier, @prelevement, @origine, @procedure, @type, @preleve, @personne, 'En attente', @rapport, @auteur, @maintenant, @maintenant)
                """, ct,
                ("@fichier", fichier), ("@prelevement", SocleRegles.Tronquer(input.ReferencePrelevement, 60)), ("@origine", origine),
                ("@procedure", SocleRegles.Tronquer(input.ProcedureNumero, 40)), ("@type", input.Type),
                ("@preleve", (input.PreleveLe ?? DateTimeOffset.UtcNow).UtcDateTime), ("@personne", input.PersonneId),
                ("@rapport", SocleRegles.Tronquer(input.Rapport, 8000)), ("@auteur", auteur.Nom), ("@maintenant", Maintenant));
            var reference = JudiciaireRegles.Reference(fichier, id);
            await db.ExecuteAsync("UPDATE ch_scientifique SET reference = @reference WHERE id = @id", ct, ("@reference", reference), ("@id", id));
            await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Scientifique, reference, "Création", null, null, $"{input.Type} — {origine}", null, ct);
            await db.CommitAsync(ct);
        }
        return await ScientifiqueAsync(id, ct);
    }

    public async Task<ScientifiqueDto> ResultatScientifiqueAsync(Auteur auteur, long id, ResultatInput input, CancellationToken ct)
    {
        if (!ScientifiqueResultats.All.Contains(input.Resultat))
            throw ApiException.Saisie("Résultat inconnu.");
        if (input.Resultat == "Concordance" && input.PersonneId is null)
            throw ApiException.Saisie("Une concordance doit désigner la personne rapprochée.");
        await using (var db = await saphirDb.OpenAsync(ct))
        {
            await db.BeginAsync(ct);
            var avant = await db.FirstOrDefaultAsync("SELECT reference, resultat, personne_id FROM ch_scientifique WHERE id = @id FOR UPDATE",
                r => new[] { r.Text("reference"), r.Text("resultat"), r.NullableLong("personne_id")?.ToString(CultureInfo.InvariantCulture) }, ct, ("@id", id))
                ?? throw ApiException.Introuvable("Enregistrement introuvable.");
            if (input.PersonneId is { } personneId)
                await PersonneExistanteAsync(db, personneId, ct);
            await db.ExecuteAsync(
                "UPDATE ch_scientifique SET resultat = @resultat, personne_id = @personne, rapport = COALESCE(@rapport, rapport), maj_le = @maintenant WHERE id = @id", ct,
                ("@resultat", input.Resultat), ("@personne", input.PersonneId), ("@rapport", SocleRegles.Tronquer(input.Rapport, 8000)),
                ("@maintenant", Maintenant), ("@id", id));
            await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Scientifique, avant[0]!, "Résultat de comparaison", "resultat", avant[1], input.Resultat, input.Motif, ct);
            if (avant[2] != input.PersonneId?.ToString(CultureInfo.InvariantCulture))
                await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Scientifique, avant[0]!, "Rapprochement", "personne", avant[2],
                    input.PersonneId?.ToString(CultureInfo.InvariantCulture), input.Motif, ct);
            await db.CommitAsync(ct);
        }
        return await ScientifiqueAsync(id, ct);
    }

    // =====================================================================================
    // FIJAISV / FIJAIT (12.9) — inscription manuelle uniquement, jamais depuis une interpellation
    // =====================================================================================

    private const string ColonnesFijais =
        "j.id, j.fichier, j.reference, j.reference_decision, j.juridiction, j.infraction, j.decision_le, j.fin_inscription, j.statut, " +
        "j.service_gestionnaire, j.obligations, j.restrictions";

    private static FijaisDto LireFijais(SqlRow r) => new(
        r.Long("id"), r.Text("fichier"), r.Text("reference"), LirePersonneP(r), r.Text("reference_decision"), r.NullableText("juridiction"),
        r.NullableText("infraction"), Sqlj.Jour(r, "decision_le"), Sqlj.Jour(r, "fin_inscription"), r.Text("statut"),
        r.NullableText("service_gestionnaire"), r.NullableText("obligations"), r.NullableText("restrictions"), Array.Empty<HistoriqueDto>());

    private static string ControlerFichierFijais(string? fichier)
        => fichier is FichiersCheops.Fijaisv or FichiersCheops.Fijait ? fichier : throw ApiException.Saisie("Fichier attendu : FIJAISV ou FIJAIT.");

    public async Task<List<FijaisDto>> RechercherFijaisAsync(string fichier, string? q, CancellationToken ct)
    {
        ControlerFichierFijais(fichier);
        var sql = new StringBuilder($"SELECT {ColonnesFijais}, {ColonnesPersonneP} FROM ch_fijais j JOIN per_personnes p ON p.id = j.personne_id WHERE j.fichier = @fichier");
        var parametres = new List<(string Name, object? Value)> { ("@fichier", fichier) };
        if (!string.IsNullOrWhiteSpace(q) && (q.Trim().StartsWith("FIJ-", StringComparison.OrdinalIgnoreCase)))
        {
            sql.Append(" AND j.reference = @reference");
            parametres.Add(("@reference", q.Trim().ToUpperInvariant()));
        }
        else
        {
            AjouterTermes(sql, parametres, q);
        }
        sql.Append($" ORDER BY p.nom, p.prenom LIMIT {Limite}");
        await using var db = await saphirDb.OpenAsync(ct);
        return await db.QueryAsync(sql.ToString(), LireFijais, ct, parametres.ToArray());
    }

    public async Task<FijaisDto> FijaisAsync(long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var fiche = await db.FirstOrDefaultAsync(
            $"SELECT {ColonnesFijais}, {ColonnesPersonneP} FROM ch_fijais j JOIN per_personnes p ON p.id = j.personne_id WHERE j.id = @id",
            LireFijais, ct, ("@id", id)) ?? throw ApiException.Introuvable("Inscription introuvable.");
        return fiche with { Historique = await Sqlj.HistoriqueAsync(db, ObjetsJudiciaire.Fijais, fiche.Reference, ct) };
    }

    public async Task<FijaisDto> InscrireFijaisAsync(Auteur auteur, FijaisInput input, CancellationToken ct)
    {
        var fichier = ControlerFichierFijais(input.Fichier);
        var decision = SocleRegles.Tronquer(input.ReferenceDecision, 60)
                       ?? throw ApiException.Saisie("La référence de la décision judiciaire fictive est obligatoire.");
        long id;
        await using (var db = await saphirDb.OpenAsync(ct))
        {
            await db.BeginAsync(ct);
            await PersonneExistanteAsync(db, input.PersonneId, ct);
            id = await db.InsertAsync(
                """
                INSERT INTO ch_fijais (fichier, personne_id, reference_decision, juridiction, infraction, decision_le, fin_inscription, statut,
                    service_gestionnaire, obligations, restrictions, cree_par, cree_le, maj_le)
                VALUES (@fichier, @personne, @decision, @juridiction, @infraction, @decisionLe, @fin, 'Inscrit', @service, @obligations, @restrictions,
                    @auteur, @maintenant, @maintenant)
                """, ct,
                ("@fichier", fichier), ("@personne", input.PersonneId), ("@decision", decision),
                ("@juridiction", SocleRegles.Tronquer(input.Juridiction, 160)), ("@infraction", SocleRegles.Tronquer(input.Infraction, 255)),
                ("@decisionLe", Sqlj.Jour(input.DecisionLe)), ("@fin", Sqlj.Jour(input.FinInscription)),
                ("@service", SocleRegles.Tronquer(input.ServiceGestionnaire, 160)), ("@obligations", SocleRegles.Tronquer(input.Obligations, 4000)),
                ("@restrictions", SocleRegles.Tronquer(input.Restrictions, 4000)), ("@auteur", auteur.Nom), ("@maintenant", Maintenant));
            var reference = JudiciaireRegles.Reference("FIJ", id);
            await db.ExecuteAsync("UPDATE ch_fijais SET reference = @reference WHERE id = @id", ct, ("@reference", reference), ("@id", id));
            await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Fijais, reference, "Inscription", "statut", null, FijaisStatuts.Inscrit, decision, ct);
            await db.CommitAsync(ct);
        }
        return await FijaisAsync(id, ct);
    }

    public async Task<FijaisDto> ChangerStatutFijaisAsync(Auteur auteur, long id, StatutInput input, CancellationToken ct)
    {
        if (!FijaisStatuts.All.Contains(input.Statut))
            throw ApiException.Saisie("Statut inconnu.");
        if (string.IsNullOrWhiteSpace(input.Motif))
            throw ApiException.Saisie("Le motif est obligatoire.");
        await using (var db = await saphirDb.OpenAsync(ct))
        {
            await db.BeginAsync(ct);
            var avant = await db.FirstOrDefaultAsync("SELECT reference, statut FROM ch_fijais WHERE id = @id FOR UPDATE",
                r => new[] { r.Text("reference"), r.Text("statut") }, ct, ("@id", id)) ?? throw ApiException.Introuvable("Inscription introuvable.");
            if (avant[1] == input.Statut)
                throw ApiException.Saisie("L'inscription a déjà ce statut.");
            if (avant[1] == "Effacé")
                throw ApiException.Saisie("Une inscription effacée ne change plus de statut.");
            await db.ExecuteAsync("UPDATE ch_fijais SET statut = @statut, maj_le = @maintenant WHERE id = @id", ct,
                ("@statut", input.Statut), ("@maintenant", Maintenant), ("@id", id));
            await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Fijais, avant[0], "Statut", "statut", avant[1], input.Statut, input.Motif, ct);
            await db.CommitAsync(ct);
        }
        return await FijaisAsync(id, ct);
    }

    // =====================================================================================
    // Casier de simulation : décisions judiciaires fictives validées (14.3)
    // =====================================================================================

    internal static DecisionDto LireDecision(SqlRow r) => new(
        r.Long("id"), r.Long("personne_id"), r.Text("reference"), r.Text("juridiction"), Sqlj.Jour(r, "decision_le") ?? DateOnly.MinValue,
        r.Text("nature"), r.Text("infraction"), r.NullableText("peine"), r.Date("cree_le"), r.Text("cree_par"));

    internal const string ColonnesDecision = "d.id, d.personne_id, d.reference, d.juridiction, d.decision_le, d.nature, d.infraction, d.peine, d.cree_le, d.cree_par";

    public async Task<List<DecisionDto>> DecisionsAsync(long personneId, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await db.QueryAsync($"SELECT {ColonnesDecision} FROM ch_decisions d WHERE d.personne_id = @id ORDER BY d.decision_le DESC",
            LireDecision, ct, ("@id", personneId));
    }

    public async Task<DecisionDto> AjouterDecisionAsync(Auteur auteur, DecisionInput input, CancellationToken ct)
    {
        var juridiction = SocleRegles.Tronquer(input.Juridiction, 160) ?? throw ApiException.Saisie("Juridiction obligatoire.");
        var nature = SocleRegles.Tronquer(input.Nature, 80) ?? throw ApiException.Saisie("Nature de la décision obligatoire.");
        var infraction = SocleRegles.Tronquer(input.Infraction, 255) ?? throw ApiException.Saisie("Infraction obligatoire.");
        if (input.DecisionLe > DateOnly.FromDateTime(DateTime.UtcNow))
            throw ApiException.Saisie("La date de décision ne peut pas être future.");
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var personneRef = await PersonnesStore.ReferenceAsync(db, input.PersonneId, ct);
        var id = await db.InsertAsync(
            """
            INSERT INTO ch_decisions (personne_id, juridiction, decision_le, nature, infraction, peine, cree_par, cree_le)
            VALUES (@personne, @juridiction, @decision, @nature, @infraction, @peine, @auteur, @maintenant)
            """, ct,
            ("@personne", input.PersonneId), ("@juridiction", juridiction), ("@decision", Sqlj.Jour(input.DecisionLe)), ("@nature", nature),
            ("@infraction", infraction), ("@peine", SocleRegles.Tronquer(input.Peine, 500)), ("@auteur", auteur.Nom), ("@maintenant", Maintenant));
        var reference = JudiciaireRegles.Reference("DEC", id);
        await db.ExecuteAsync("UPDATE ch_decisions SET reference = @reference WHERE id = @id", ct, ("@reference", reference), ("@id", id));
        await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Decision, personneRef, "Décision enregistrée", reference, null,
            $"{juridiction} — {infraction}", null, ct);
        await db.CommitAsync(ct);
        return await db.FirstOrDefaultAsync($"SELECT {ColonnesDecision} FROM ch_decisions d WHERE d.id = @id", LireDecision, ct, ("@id", id))
               ?? throw ApiException.Introuvable("Décision introuvable.");
    }

    // =====================================================================================
    // ANADOC (12.10)
    // =====================================================================================

    private const string ColonnesAnadoc =
        "x.id, x.source, x.acte_id, x.piece_id, x.procedure_id, x.procedure_numero, x.numero_pv, x.type_acte, x.titre, x.`date`, x.service, x.redacteur, x.personnes";

    private static AnadocResultatDto LireAnadoc(SqlRow r, string? motCle)
    {
        var texte = r.Text("texte");
        return new AnadocResultatDto(r.Long("id"), r.Text("source"), r.Long("procedure_id"), r.Text("procedure_numero"), r.NullableText("numero_pv"),
            r.Text("type_acte"), r.Text("titre"), r.Date("date"), r.NullableText("service"), r.NullableText("redacteur"), r.NullableText("personnes"),
            Extrait(texte, motCle));
    }

    /// <summary>Extrait de 260 caractères autour du premier mot-clé trouvé (ou début du texte).</summary>
    public static string Extrait(string texte, string? motCle)
    {
        var plat = texte.Replace('\n', ' ').Replace('\r', ' ');
        var position = string.IsNullOrWhiteSpace(motCle) ? -1 : plat.IndexOf(motCle.Trim(), StringComparison.OrdinalIgnoreCase);
        var debut = Math.Max(0, position - 80);
        var longueur = Math.Min(260, plat.Length - debut);
        var extrait = plat.Substring(debut, longueur).Trim();
        return (debut > 0 ? "… " : string.Empty) + extrait + (debut + longueur < plat.Length ? " …" : string.Empty);
    }

    public async Task<List<AnadocResultatDto>> RechercherAnadocAsync(string? procedure, string? pv, string? type, DateOnly? du, DateOnly? au,
        string? service, string? redacteur, string? personne, string? motsCles, CancellationToken ct)
    {
        var sql = new StringBuilder($"SELECT {ColonnesAnadoc}, x.texte FROM ch_anadoc x WHERE 1 = 1");
        var parametres = new List<(string Name, object? Value)>();
        void Like(string colonne, string nom, string? valeur)
        {
            if (string.IsNullOrWhiteSpace(valeur))
                return;
            sql.Append($" AND {colonne} LIKE {nom}");
            parametres.Add((nom, Sqlj.Like(valeur)));
        }
        Like("x.procedure_numero", "@procedure", procedure);
        Like("x.numero_pv", "@pv", pv);
        Like("x.type_acte", "@type", type);
        Like("x.service", "@service", service);
        Like("x.redacteur", "@redacteur", redacteur);
        if (!string.IsNullOrWhiteSpace(personne))
        {
            sql.Append(" AND (x.personnes LIKE @personne OR x.personnes_ids LIKE @personneId)");
            parametres.Add(("@personne", Sqlj.Like(personne)));
            parametres.Add(("@personneId", "%," + SocleRegles.EchapperLike(personne.Trim()) + ",%"));
        }
        if (du is { } d1)
        {
            sql.Append(" AND x.`date` >= @du");
            parametres.Add(("@du", Sqlj.Jour(d1)));
        }
        if (au is { } d2)
        {
            sql.Append(" AND x.`date` < @au");
            parametres.Add(("@au", Sqlj.Jour(d2.AddDays(1))));
        }
        var termes = JudiciaireRegles.Termes(motsCles);
        for (var i = 0; i < termes.Count; i++)
        {
            sql.Append($" AND (x.texte LIKE @m{i} OR x.titre LIKE @m{i})");
            parametres.Add(($"@m{i}", Sqlj.Like(termes[i])));
        }
        sql.Append($" ORDER BY x.`date` DESC LIMIT {Limite}");
        await using var db = await saphirDb.OpenAsync(ct);
        return await db.QueryAsync(sql.ToString(), r => LireAnadoc(r, termes.FirstOrDefault()), ct, parametres.ToArray());
    }

    public async Task<AnadocDocumentDto> AnadocDocumentAsync(long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await db.FirstOrDefaultAsync($"SELECT {ColonnesAnadoc}, x.texte FROM ch_anadoc x WHERE x.id = @id",
            r => new AnadocDocumentDto(LireAnadoc(r, null), r.Text("texte"), r.NullableLong("acte_id"), r.NullableLong("piece_id")), ct, ("@id", id))
            ?? throw ApiException.Introuvable("Document introuvable.");
    }

    // =====================================================================================
    // Recherche générale (11.2)
    // =====================================================================================

    public async Task<RechercheGeneraleDto> RechercheGeneraleAsync(string? nom, string? prenom, string? alias, string? naissance, string? lieu,
        string? identifiant, string? dossier, string? immatriculation, AgentDroits droits, CancellationToken ct)
    {
        var trouvees = await personnes.RechercherCriteresAsync(nom, prenom, alias, naissance, lieu, identifiant, droits, ct);
        List<VehiculeResumeDto> vehicules = droits.Possede(Permissions.CheopsVehicules) && !string.IsNullOrWhiteSpace(immatriculation)
            ? await personnes.RechercherParPlaqueAsync(immatriculation, droits, ct)
            : new List<VehiculeResumeDto>();
        var dossiers = new List<CorrespondanceDto>();
        if (!string.IsNullOrWhiteSpace(dossier))
        {
            var exact = dossier.Trim().ToUpperInvariant();
            var like = Sqlj.Like(dossier);
            await using var db = await saphirDb.OpenAsync(ct);
            async Task ChercherAsync(string sql, Func<SqlRow, CorrespondanceDto> lire)
                => dossiers.AddRange(await db.QueryAsync(sql, lire, ct, ("@exact", exact), ("@like", like)));

            if (droits.Possede(Permissions.CheopsTaj))
                await ChercherAsync(
                    "SELECT t.id, t.reference, t.faits, t.procedure_numero, t.personne_id, p.nom, p.prenom FROM ch_taj t JOIN per_personnes p ON p.id = t.personne_id WHERE t.reference = @exact OR t.procedure_numero LIKE @like LIMIT 50",
                    r => new CorrespondanceDto(FichiersCheops.Taj, r.Long("id"), r.Text("reference"),
                        $"{r.Text("nom")} {r.Text("prenom")} — {r.Text("faits")} ({r.NullableText("procedure_numero") ?? "sans procédure"})", r.Long("personne_id"), null));
            if (droits.Possede(Permissions.CheopsFpr))
                await ChercherAsync(
                    "SELECT f.id, f.numero, f.motif, f.statut, f.personne_id, p.nom, p.prenom FROM ch_fpr f JOIN per_personnes p ON p.id = f.personne_id WHERE f.numero = @exact OR f.procedure_numero LIKE @like LIMIT 50",
                    r => new CorrespondanceDto(FichiersCheops.Fpr, r.Long("id"), r.Text("numero"),
                        $"{r.Text("nom")} {r.Text("prenom")} — {r.Text("motif")} ({r.Text("statut")})", r.Long("personne_id"), null));
            if (droits.Possede(Permissions.CheopsVehicules))
                await ChercherAsync(
                    "SELECT f.id, f.reference, f.identifiant, f.nature, f.statut, f.vehicule_id FROM ch_foves f WHERE f.reference = @exact OR f.procedure_numero LIKE @like LIMIT 50",
                    r => new CorrespondanceDto(FichiersCheops.Foves, r.Long("id"), r.Text("reference"),
                        $"{r.Text("identifiant")} — {r.Text("nature")} ({r.Text("statut")})", null, r.NullableLong("vehicule_id")));
            if (droits.Possede(Permissions.CheopsScientifique))
                await ChercherAsync(
                    "SELECT s.id, s.fichier, s.reference, s.type, s.personne_id FROM ch_scientifique s WHERE s.reference = @exact OR s.reference_prelevement = @exact OR s.procedure_numero LIKE @like LIMIT 50",
                    r => new CorrespondanceDto(r.Text("fichier"), r.Long("id"), r.Text("reference"), r.Text("type"), r.NullableLong("personne_id"), null));
            if (droits.Possede(Permissions.CheopsFijais))
                await ChercherAsync(
                    "SELECT j.id, j.fichier, j.reference, j.reference_decision, j.personne_id, p.nom, p.prenom FROM ch_fijais j JOIN per_personnes p ON p.id = j.personne_id WHERE j.reference = @exact OR j.reference_decision = @exact LIMIT 50",
                    r => new CorrespondanceDto(r.Text("fichier"), r.Long("id"), r.Text("reference"),
                        $"{r.Text("nom")} {r.Text("prenom")} — décision {r.Text("reference_decision")}", r.Long("personne_id"), null));
            if (droits.Possede(Permissions.LrppnConsulter))
                await ChercherAsync(
                    "SELECT id, numero, intitule, statut FROM lr_procedures WHERE numero LIKE @like OR main_courante_ref = @exact OR intervention_numero = @exact LIMIT 50",
                    r => new CorrespondanceDto("LRPPN", r.Long("id"), r.Text("numero"), $"{r.Text("intitule")} ({r.Text("statut")})", null, null));
        }
        List<long> declarees = [];
        if (trouvees.Count > 0)
        {
            var (liste, parametres) = Sqlj.In("p", trouvees.Select(p => p.Id));
            await using var db = await saphirDb.OpenAsync(ct);
            declarees = await db.QueryAsync($"SELECT id FROM per_personnes WHERE discord_proprietaire IS NOT NULL AND id IN ({liste})",
                r => r.Long("id"), ct, parametres);
        }
        return new RechercheGeneraleDto(trouvees, vehicules, dossiers, declarees);
    }
}
