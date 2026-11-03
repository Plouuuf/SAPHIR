using System.Globalization;
using System.Text;
using Saphir.Server.Data;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Operations;

namespace Saphir.Server.Modules.Operations;

/// <summary>
/// PÉGASE : appels CIC (6.3), interventions et engagements (7.1, 7.2, 8.4), comptes rendus (7.3), TN du jour (8.2),
/// tableau de bord et statistiques. Les engagements et statuts alimentent le journal de service (20.2).
/// </summary>
public sealed class PegaseStore(SaphirDb saphirDb, OperationsNotifier notifier)
{
    private const string SourcePegase = "PÉGASE";

    internal static readonly AppelRequerant RequerantVide = new(null, false, null, null, null, null, null, null, null, null, null, null, false, null);
    internal static readonly AppelLocalisation LocalisationVide = new(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
    internal static readonly AppelMotif MotifVide = new(null, null, null, null, false, null, null, null, false, false, null);
    internal static readonly AppelSecurite SecuriteVide = new(false, null, false, false, null, false, false, false, null, null, false);
    internal static readonly AppelEngagement EngagementVide = new(false, null, null, null);

    /// <summary>Actions à mener après validation de la transaction (temps réel, Discord).</summary>
    private sealed class Apres
    {
        private readonly List<Func<Task>> _actions = [];
        public void Ajouter(Func<Task> action) => _actions.Add(action);
        public async Task ExecuterAsync()
        {
            foreach (var action in _actions)
                await action();
        }
    }

    // =====================================================================================
    // Appels
    // =====================================================================================

    private static AppelResumeDto LireAppelResume(SqlRow r) => new(
        r.Long("id"), r.Text("numero"), r.Text("etat"), r.Date("recu_le"), r.NullableText("canal"), r.NullableText("motif"),
        r.NullableText("priorite"), r.NullableText("commune"), r.Text("operateur"), r.NullableLong("intervention_id"),
        r.NullableText("intervention_numero"));

    private const string SelectAppelResume =
        """
        SELECT a.id, a.numero, a.etat, a.recu_le, a.canal, a.motif, a.priorite, a.commune, a.operateur, a.intervention_id,
               i.numero AS intervention_numero
        FROM pg_appels a LEFT JOIN pg_interventions i ON i.id = a.intervention_id
        """;

    public async Task<List<AppelResumeDto>> ListerAppelsAsync(string? etat, string? q, int limite, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await ListerAppelsAsync(db, etat, q, limite, ct);
    }

    private static Task<List<AppelResumeDto>> ListerAppelsAsync(SqlSession db, string? etat, string? q, int limite, CancellationToken ct)
    {
        var sql = new StringBuilder(SelectAppelResume).Append(" WHERE 1 = 1");
        var parametres = new List<(string Name, object? Value)>();
        if (etat == "ouverts")
        {
            sql.Append(" AND a.etat IN (@e1, @e2, @e3, @e4)");
            parametres.Add(("@e1", AppelEtats.EnCours));
            parametres.Add(("@e2", AppelEtats.Brouillon));
            parametres.Add(("@e3", AppelEtats.EnAttente));
            parametres.Add(("@e4", AppelEtats.TransmisTn));
        }
        else if (!string.IsNullOrWhiteSpace(etat))
        {
            sql.Append(" AND a.etat = @etat");
            parametres.Add(("@etat", etat.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            sql.Append(" AND (a.numero LIKE @q OR a.motif LIKE @q OR a.commune LIKE @q OR a.adresse LIKE @q OR a.operateur LIKE @q OR i.numero LIKE @q)");
            parametres.Add(("@q", OpSql.Like(q)));
        }
        sql.Append(" ORDER BY a.recu_le DESC, a.id DESC LIMIT @limite");
        parametres.Add(("@limite", limite));
        return db.QueryAsync(sql.ToString(), LireAppelResume, ct, parametres.ToArray());
    }

    public async Task<AppelDto> LireAppelAsync(long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await LireAppelAsync(db, id, false, ct) ?? throw ApiException.Introuvable("Appel introuvable.");
    }

    private static async Task<AppelDto?> LireAppelAsync(SqlSession db, long id, bool verrouiller, CancellationToken ct)
    {
        var appel = await db.FirstOrDefaultAsync(
            "SELECT a.*, i.numero AS intervention_numero FROM pg_appels a LEFT JOIN pg_interventions i ON i.id = a.intervention_id WHERE a.id = @id" +
            (verrouiller ? " FOR UPDATE" : string.Empty),
            r => new AppelDto(
                r.Long("id"), r.Text("numero"), r.Text("etat"), r.Date("recu_le"), r.NullableDate("raccroche_le"), r.Text("operateur"),
                r.NullableLong("operateur_id"), r.NullableText("service"), r.NullableText("simcall_id"), r.NullableLong("intervention_id"),
                r.NullableText("intervention_numero"), r.NullableText("motif_cloture"),
                new AppelFormulaire(
                    r.NullableText("canal"), r.NullableText("nature"),
                    OpSql.Lire<AppelRequerant>(r.NullableText("requerant_json")) ?? RequerantVide,
                    OpSql.Lire<AppelLocalisation>(r.NullableText("localisation_json")) ?? LocalisationVide,
                    OpSql.Lire<AppelMotif>(r.NullableText("motif_json")) ?? MotifVide,
                    [], [],
                    OpSql.Lire<AppelSecurite>(r.NullableText("securite_json")) ?? SecuriteVide,
                    OpSql.Lire<AppelEngagement>(r.NullableText("engagement_json")) ?? EngagementVide),
                r.Date("maj_le"), r.Long("version")),
            ct, ("@id", id));
        if (appel is null)
            return null;
        var personnes = await db.QueryAsync("SELECT * FROM pg_personnes WHERE appel_id = @id ORDER BY ordre, id", LirePersonne, ct, ("@id", id));
        var vehicules = await db.QueryAsync("SELECT * FROM pg_vehicules WHERE appel_id = @id ORDER BY ordre, id", LireVehicule, ct, ("@id", id));
        return appel with { Formulaire = appel.Formulaire with { Personnes = personnes, Vehicules = vehicules } };
    }

    private static AppelPersonne LirePersonne(SqlRow r) => new(
        r.NullableText("role"), r.Bool("identite_connue"), r.NullableLong("personne_id"), r.NullableText("personne_reference"),
        r.NullableText("nom"), r.NullableText("prenom"), r.NullableText("description"), r.NullableText("localisation"),
        r.NullableText("etat_apparent"), r.NullableText("informations"), r.NullableText("source"));

    private static AppelVehicule LireVehicule(SqlRow r) => new(
        r.NullableLong("vehicule_id"), r.NullableText("immatriculation"), r.NullableText("pays"), r.NullableText("marque"),
        r.NullableText("modele"), r.NullableText("couleur"), r.NullableText("categorie"), r.NullableText("description"),
        r.NullableText("direction"), r.NullableText("lien_faits"));

    /// <summary>Nouvel appel vierge, opérateur = agent connecté.</summary>
    public async Task<AppelDto> CreerAppelAsync(AgentContext ctx, AppelCreationInput input, CancellationToken ct)
    {
        var canal = string.IsNullOrWhiteSpace(input.Canal) ? "17" : input.Canal.Trim();
        if (!PegaseListes.Canaux.Contains(canal))
            throw ApiException.Saisie("Canal de réception inconnu.");
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        long id;
        try
        {
            var maintenant = DateTime.UtcNow;
            id = await db.InsertAsync(
                """
                INSERT INTO pg_appels (etat, recu_le, operateur_id, operateur, operateur_discord, service, canal, cree_le, maj_le, version)
                VALUES (@etat, @maintenant, @operateur_id, @operateur, @discord, @service, @canal, @maintenant, @maintenant, 1)
                """, ct,
                ("@etat", AppelEtats.EnCours), ("@maintenant", maintenant), ("@operateur_id", ctx.Agent.Id),
                ("@operateur", $"{ctx.Agent.Nom} {ctx.Agent.Prenom}"), ("@discord", ctx.Agent.DiscordId), ("@service", ctx.Agent.Service),
                ("@canal", canal));
            var numero = OperationsRegles.Numero(OperationsRegles.PrefixeAppel, DateTimeOffset.UtcNow, id);
            await db.ExecuteAsync("UPDATE pg_appels SET numero = @numero WHERE id = @id", ct, ("@numero", numero), ("@id", id));
            await Historique.EcrireAsync(db, ctx.Auteur, OperationsRegles.ObjetAppel, numero, "Création", "canal", null, canal, null, ct);
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        await notifier.PegaseAsync("appel:" + id);
        return (await LireAppelAsync(db, id, false, ct))!;
    }

    /// <summary>SIMCALL : décroché d'un 17. Crée (ou retrouve) l'appel et ouvre la prise d'appel sur le poste de l'opérateur.</summary>
    public Task<long> SimcallDecrocheAsync(SimcallCallAnswered message, CancellationToken ct)
        => OpSql.ReessayerAsync(() => SimcallDecrocheEssaiAsync(message, ct), doublon: true);

    private async Task<long> SimcallDecrocheEssaiAsync(SimcallCallAnswered message, CancellationToken ct)
    {
        var simcallId = OpSql.T(message.SimcallCallId, 64) ?? throw ApiException.Saisie("Appel SIMCALL incomplet.");
        var discordId = OperationsRegles.NettoyerDiscordId(message.DiscordUserId) ?? throw ApiException.Saisie("Appel SIMCALL incomplet.");
        var decroche = message.AnsweredAt == default ? DateTime.UtcNow : message.AnsweredAt.UtcDateTime;
        var numeroAppelant = OpSql.T(message.CallerNumber, 40);

        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        long id;
        bool nouveau;
        try
        {
            var agent = await db.FirstOrDefaultAsync(
                "SELECT id, nom, prenom FROM rh_agents WHERE discord_id = @discord",
                r => new { Id = r.Long("id"), Libelle = $"{r.Text("nom")} {r.Text("prenom")}" }, ct, ("@discord", discordId));
            var operateur = agent?.Libelle ?? OpSql.T(message.OperatorName, 200) ?? "Opérateur SIMCALL";
            // Pas de FOR UPDATE : sur un appel encore absent, le verrou de plage puis l'insertion provoquaient des interblocages
            // entre décrochés simultanés. La clé unique simcall_id protège des doublons (nouvel essai, voir SimcallDecrocheAsync).
            var existant = await db.FirstOrDefaultAsync("SELECT id FROM pg_appels WHERE simcall_id = @simcall",
                r => (object)r.Long("id"), ct, ("@simcall", simcallId));
            nouveau = existant is null;
            var maintenant = DateTime.UtcNow;
            if (existant is long trouve)
            {
                id = trouve;
                await db.ExecuteAsync(
                    "UPDATE pg_appels SET operateur_id = @operateur_id, operateur = @operateur, operateur_discord = @discord, maj_le = @maintenant WHERE id = @id",
                    ct, ("@operateur_id", agent?.Id), ("@operateur", operateur), ("@discord", discordId), ("@maintenant", maintenant), ("@id", id));
            }
            else
            {
                var requerant = RequerantVide with { Telephone = numeroAppelant };
                id = await db.InsertAsync(
                    """
                    INSERT INTO pg_appels (simcall_id, etat, recu_le, operateur_id, operateur, operateur_discord, canal, telephone,
                                           requerant_json, cree_le, maj_le, version)
                    VALUES (@simcall, @etat, @decroche, @operateur_id, @operateur, @discord, '17', @telephone, @requerant, @maintenant, @maintenant, 1)
                    """, ct,
                    ("@simcall", simcallId), ("@etat", AppelEtats.EnCours), ("@decroche", decroche), ("@operateur_id", agent?.Id),
                    ("@operateur", operateur), ("@discord", discordId), ("@telephone", numeroAppelant),
                    ("@requerant", OpSql.Serialiser(requerant)), ("@maintenant", maintenant));
                var numero = OperationsRegles.Numero(OperationsRegles.PrefixeAppel, DateTimeOffset.UtcNow, id);
                await db.ExecuteAsync("UPDATE pg_appels SET numero = @numero WHERE id = @id", ct, ("@numero", numero), ("@id", id));
                await Historique.EcrireAsync(db, new Auteur(agent?.Id, operateur + " (SIMCALL)"), OperationsRegles.ObjetAppel, numero,
                    "Création", "canal", null, "17 (SIMCALL)", null, ct);
            }
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }

        await notifier.OuvrirApplicationAsync(discordId, SaphirApps.Pegase, "appel:" + id.ToString(CultureInfo.InvariantCulture));
        if (nouveau)
            await notifier.AgentsAsync([discordId], "PÉGASE — appel 17", "Appel décroché sur SIMCALL : prise d'appel ouverte.", SaphirApps.Pegase);
        await notifier.PegaseAsync("appel:" + id);
        return id;
    }

    /// <summary>SIMCALL : raccroché, l'heure de fin est figée.</summary>
    public async Task<bool> SimcallRaccrocheAsync(SimcallCallEnded message, CancellationToken ct)
    {
        var simcallId = OpSql.T(message.SimcallCallId, 64);
        if (simcallId is null)
            return false;
        await using var db = await saphirDb.OpenAsync(ct);
        var id = await db.FirstOrDefaultAsync("SELECT id FROM pg_appels WHERE simcall_id = @simcall", r => (object)r.Long("id"), ct, ("@simcall", simcallId));
        var modifie = await db.ExecuteAsync(
            "UPDATE pg_appels SET raccroche_le = @fin, maj_le = @maintenant WHERE simcall_id = @simcall AND raccroche_le IS NULL", ct,
            ("@fin", message.EndedAt == default ? DateTime.UtcNow : message.EndedAt.UtcDateTime), ("@maintenant", DateTime.UtcNow),
            ("@simcall", simcallId));
        if (modifie > 0 && id is long appelId)
            await notifier.PegaseAsync("appel:" + appelId);
        return modifie > 0;
    }

    /// <summary>Contenu nettoyé et contrôlé du formulaire (les champs vides ne bloquent jamais une urgence).</summary>
    internal static AppelFormulaire Nettoyer(AppelFormulaire? f)
    {
        if (f is null)
            throw ApiException.Saisie("Formulaire d'appel absent.");
        static string? Choix(string? valeur, IReadOnlyList<string> liste, string champ)
        {
            var v = OpSql.T(valeur, 120);
            if (v is not null && !liste.Contains(v))
                throw ApiException.Saisie($"Valeur inconnue pour « {champ} ».");
            return v;
        }

        var q = f.Requerant ?? RequerantVide;
        var l = f.Localisation ?? LocalisationVide;
        var m = f.Motif ?? MotifVide;
        var s = f.Securite ?? SecuriteVide;
        var e = f.Engagement ?? EngagementVide;
        if (l.X is { } x && l.Y is { } y && !OperationsRegles.PositionPlausible(x, y))
            throw ApiException.Saisie("Position sur carte invalide.");
        if ((f.Personnes?.Count ?? 0) > 30 || (f.Vehicules?.Count ?? 0) > 30)
            throw ApiException.Saisie("Trente personnes ou véhicules au plus par appel.");

        return new AppelFormulaire(
            Choix(f.Canal, PegaseListes.Canaux, "Canal"),
            Choix(f.Nature, PegaseListes.NaturesAppel, "Nature de l'appel"),
            new AppelRequerant(Choix(q.Qualite, PegaseListes.QualitesRequerant, "Qualité du requérant"), q.IdentiteConnue, q.PersonneId,
                OpSql.T(q.PersonneReference, 20), OpSql.T(q.Nom, 60)?.ToUpperInvariant(), OpSql.T(q.Prenom, 60) is { } p ? NomsFormat.Prenom(p) : null,
                OpSql.T(q.Telephone, 30), OpSql.T(q.TelephoneRappel, 30), OpSql.T(q.Adresse, 300), q.DateNaissance, OpSql.T(q.LienFaits, 200),
                OpSql.T(q.Langue, 40), q.Interprete, OpSql.T(q.Observations, 2000)),
            new AppelLocalisation(OpSql.T(l.Commune, 100), OpSql.T(l.CodePostal, 10), OpSql.T(l.Numero, 20), OpSql.T(l.Rue, 200),
                OpSql.T(l.AdresseComplete, 300), OpSql.T(l.Residence, 120), OpSql.T(l.Batiment, 40), OpSql.T(l.Etage, 20),
                OpSql.T(l.Appartement, 20), OpSql.T(l.Acces, 500), Choix(l.TypeLieu, PegaseListes.TypesLieu, "Type de lieu"),
                OpSql.T(l.Repere, 300), l.X, l.Y, Choix(l.Fiabilite, PegaseListes.Fiabilites, "Fiabilité de localisation")),
            new AppelMotif(Choix(m.Motif, PegaseListes.Motifs, "Motif"), OpSql.T(m.SousMotif, 120), OpSql.T(m.Description, 8000), m.FaitsLe,
                m.FaitsEnCours, Choix(m.Priorite, PegaseListes.Priorites, "Priorité"), Choix(m.Gravite, PegaseListes.Gravites, "Gravité"),
                m.NombrePersonnes is >= 0 and <= 10000 ? m.NombrePersonnes : null, m.BlessesSignales, m.DangerImmediat, OpSql.T(m.Observations, 2000)),
            (f.Personnes ?? []).Select(x => new AppelPersonne(Choix(x.Role, PegaseListes.RolesPersonne, "Rôle de la personne"), x.IdentiteConnue,
                x.PersonneId, OpSql.T(x.PersonneReference, 20), OpSql.T(x.Nom, 60)?.ToUpperInvariant(),
                OpSql.T(x.Prenom, 60) is { } pp ? NomsFormat.Prenom(pp) : null, OpSql.T(x.Description, 2000), OpSql.T(x.Localisation, 300),
                OpSql.T(x.EtatApparent, 200), OpSql.T(x.Informations, 2000), OpSql.T(x.Source, 120))).ToList(),
            (f.Vehicules ?? []).Select(x => new AppelVehicule(x.VehiculeId,
                x.Immatriculation is null ? null : OpSql.T(OperationsRegles.NormaliserImmatriculation(x.Immatriculation), 20),
                OpSql.T(x.Pays, 40), OpSql.T(x.Marque, 60), OpSql.T(x.Modele, 60), OpSql.T(x.Couleur, 40),
                Choix(x.Categorie, PegaseListes.CategoriesVehicule, "Catégorie de véhicule"), OpSql.T(x.Description, 2000),
                OpSql.T(x.Direction, 200), OpSql.T(x.LienFaits, 200))).ToList(),
            new AppelSecurite(s.Blesses, s.NombreBlesses is >= 0 and <= 1000 ? s.NombreBlesses : null, s.Menace, s.Arme,
                OpSql.T(s.ArmePrecision, 200), s.Mineurs, s.PersonneAssistance, s.SecoursDemandes, OpSql.T(s.ServicesSurPlace, 300),
                OpSql.T(s.Consignes, 2000), s.RenfortEnvisage),
            new AppelEngagement(e.Necessaire, e.NombreEquipages is >= 0 and <= 50 ? e.NombreEquipages : null,
                Choix(e.CategorieMoyen, PegaseListes.CategoriesMoyen, "Catégorie de moyen"), OpSql.T(e.Observations, 2000)));
    }

    /// <summary>Enregistre le formulaire de l'appel (verrou déjà posé) et historise les différences.</summary>
    private static async Task EcrireFormulaireAsync(SqlSession db, Auteur auteur, AppelDto actuel, AppelFormulaire f, CancellationToken ct)
    {
        var maintenant = DateTime.UtcNow;
        await db.ExecuteAsync(
            """
            UPDATE pg_appels SET canal = @canal, nature = @nature, motif = @motif, priorite = @priorite, commune = @commune, adresse = @adresse,
                   telephone = @telephone, requerant_json = @requerant, localisation_json = @localisation, motif_json = @motif_json,
                   securite_json = @securite, engagement_json = @engagement, maj_le = @maintenant, version = version + 1
            WHERE id = @id
            """, ct,
            ("@canal", f.Canal), ("@nature", f.Nature), ("@motif", f.Motif.Motif), ("@priorite", f.Motif.Priorite),
            ("@commune", f.Localisation.Commune), ("@adresse", OpSql.T(OperationsRegles.AdresseAffichee(f.Localisation), 300)),
            ("@telephone", f.Requerant.Telephone), ("@requerant", OpSql.Serialiser(f.Requerant)),
            ("@localisation", OpSql.Serialiser(f.Localisation)), ("@motif_json", OpSql.Serialiser(f.Motif)),
            ("@securite", OpSql.Serialiser(f.Securite)), ("@engagement", OpSql.Serialiser(f.Engagement)),
            ("@maintenant", maintenant), ("@id", actuel.Id));

        await db.ExecuteAsync("DELETE FROM pg_personnes WHERE appel_id = @id", ct, ("@id", actuel.Id));
        var ordre = 0;
        foreach (var p in f.Personnes)
            await db.ExecuteAsync(
                """
                INSERT INTO pg_personnes (appel_id, intervention_id, ordre, role, identite_connue, personne_id, personne_reference, nom, prenom,
                                          description, localisation, etat_apparent, informations, source)
                VALUES (@appel, @intervention, @ordre, @role, @connue, @personne, @reference, @nom, @prenom, @description, @localisation,
                        @etat, @informations, @source)
                """, ct,
                ("@appel", actuel.Id), ("@intervention", actuel.InterventionId), ("@ordre", ordre++), ("@role", p.Role),
                ("@connue", p.IdentiteConnue), ("@personne", p.PersonneId), ("@reference", p.PersonneReference), ("@nom", p.Nom),
                ("@prenom", p.Prenom), ("@description", p.Description), ("@localisation", p.Localisation), ("@etat", p.EtatApparent),
                ("@informations", p.Informations), ("@source", p.Source));

        await db.ExecuteAsync("DELETE FROM pg_vehicules WHERE appel_id = @id", ct, ("@id", actuel.Id));
        ordre = 0;
        foreach (var v in f.Vehicules)
            await db.ExecuteAsync(
                """
                INSERT INTO pg_vehicules (appel_id, intervention_id, ordre, vehicule_id, immatriculation, pays, marque, modele, couleur, categorie,
                                          description, direction, lien_faits)
                VALUES (@appel, @intervention, @ordre, @vehicule, @immat, @pays, @marque, @modele, @couleur, @categorie, @description,
                        @direction, @lien)
                """, ct,
                ("@appel", actuel.Id), ("@intervention", actuel.InterventionId), ("@ordre", ordre++), ("@vehicule", v.VehiculeId),
                ("@immat", v.Immatriculation), ("@pays", v.Pays), ("@marque", v.Marque), ("@modele", v.Modele), ("@couleur", v.Couleur),
                ("@categorie", v.Categorie), ("@description", v.Description), ("@direction", v.Direction), ("@lien", v.LienFaits));

        var a = actuel.Formulaire;
        var objet = OperationsRegles.ObjetAppel;
        await OpSql.HistoriserDifferencesAsync(db, auteur, objet, actuel.Numero, string.Empty,
            new { a.Canal, a.Nature }, new { f.Canal, f.Nature }, null, ct);
        await OpSql.HistoriserDifferencesAsync(db, auteur, objet, actuel.Numero, "requerant", a.Requerant, f.Requerant, null, ct);
        await OpSql.HistoriserDifferencesAsync(db, auteur, objet, actuel.Numero, "localisation", a.Localisation, f.Localisation, null, ct);
        await OpSql.HistoriserDifferencesAsync(db, auteur, objet, actuel.Numero, "motif", a.Motif, f.Motif, null, ct);
        await OpSql.HistoriserDifferencesAsync(db, auteur, objet, actuel.Numero, "securite", a.Securite, f.Securite, null, ct);
        await OpSql.HistoriserDifferencesAsync(db, auteur, objet, actuel.Numero, "engagement", a.Engagement, f.Engagement, null, ct);
        var personnesAvant = SocleRegles.Liste(a.Personnes.Select(DecrirePersonne));
        var personnesApres = SocleRegles.Liste(f.Personnes.Select(DecrirePersonne));
        if (personnesAvant != personnesApres)
            await Historique.EcrireAsync(db, auteur, objet, actuel.Numero, "Modification", "personnes", personnesAvant, personnesApres, null, ct);
        var vehiculesAvant = SocleRegles.Liste(a.Vehicules.Select(v => v.Immatriculation ?? "sans immatriculation"));
        var vehiculesApres = SocleRegles.Liste(f.Vehicules.Select(v => v.Immatriculation ?? "sans immatriculation"));
        if (vehiculesAvant != vehiculesApres)
            await Historique.EcrireAsync(db, auteur, objet, actuel.Numero, "Modification", "vehicules", vehiculesAvant, vehiculesApres, null, ct);
    }

    private static string DecrirePersonne(AppelPersonne p)
        => $"{(p.IdentiteConnue ? OperationsRegles.LibellePersonne(p.Nom, p.Prenom) : "Identité inconnue")} ({p.Role ?? "rôle non précisé"})";

    /// <summary>Verrouille l'appel, vérifie qu'il est encore modifiable, enregistre le formulaire éventuel.</summary>
    private static async Task<AppelDto> PreparerAppelAsync(SqlSession db, Auteur auteur, long id, AppelActionInput input, bool ouvertRequis, CancellationToken ct)
    {
        var appel = await LireAppelAsync(db, id, true, ct) ?? throw ApiException.Introuvable("Appel introuvable.");
        if (appel.Etat == AppelEtats.ClosSansSuite)
            throw ApiException.Conflit("L'appel est clos sans suite : il ne peut plus être modifié.");
        if (ouvertRequis && !AppelEtats.EstOuvert(appel.Etat))
            throw ApiException.Conflit($"L'appel est déjà traité ({appel.Etat}).");
        OpSql.VerifierVersion(input.Version, appel.Version);
        if (input.Formulaire is not null)
        {
            var formulaire = Nettoyer(input.Formulaire);
            await EcrireFormulaireAsync(db, auteur, appel, formulaire, ct);
            appel = (await LireAppelAsync(db, id, false, ct))!;
        }
        return appel;
    }

    private static async Task ChangerEtatAppelAsync(SqlSession db, Auteur auteur, AppelDto appel, string etat, long? interventionId, string? motif, CancellationToken ct)
    {
        await db.ExecuteAsync(
            """
            UPDATE pg_appels SET etat = @etat, intervention_id = COALESCE(@intervention, intervention_id),
                   motif_cloture = COALESCE(@motif, motif_cloture), maj_le = @maintenant, version = version + 1
            WHERE id = @id
            """, ct,
            ("@etat", etat), ("@intervention", interventionId), ("@motif", etat == AppelEtats.ClosSansSuite ? OpSql.T(motif, 500) : null),
            ("@maintenant", DateTime.UtcNow), ("@id", appel.Id));
        if (interventionId is { } iid)
        {
            await db.ExecuteAsync("UPDATE pg_personnes SET intervention_id = @intervention WHERE appel_id = @id", ct, ("@intervention", iid), ("@id", appel.Id));
            await db.ExecuteAsync("UPDATE pg_vehicules SET intervention_id = @intervention WHERE appel_id = @id", ct, ("@intervention", iid), ("@id", appel.Id));
        }
        if (appel.Etat != etat)
            await Historique.EcrireAsync(db, auteur, OperationsRegles.ObjetAppel, appel.Numero, "État", "etat", appel.Etat, etat, OpSql.T(motif, 500), ct);
    }

    /// <summary>Enregistrer le brouillon / Modifier.</summary>
    public async Task<AppelDto> EnregistrerAppelAsync(AgentContext ctx, long id, AppelActionInput input, CancellationToken ct)
    {
        if (input.Formulaire is null)
            throw ApiException.Saisie("Formulaire d'appel absent.");
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        try
        {
            var appel = await PreparerAppelAsync(db, ctx.Auteur, id, input, false, ct);
            if (appel.Etat == AppelEtats.EnCours)
                await ChangerEtatAppelAsync(db, ctx.Auteur, appel, AppelEtats.Brouillon, null, null, ct);
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        await notifier.PegaseAsync("appel:" + id);
        return (await LireAppelAsync(db, id, false, ct))!;
    }

    /// <summary>Mettre en attente (motif facultatif) ou transmettre au TN.</summary>
    public async Task<AppelDto> AttenteAppelAsync(AgentContext ctx, long id, AppelActionInput input, bool transmettreTn, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        AppelDto appel;
        TnDto? tn;
        try
        {
            appel = await PreparerAppelAsync(db, ctx.Auteur, id, input, true, ct);
            await ChangerEtatAppelAsync(db, ctx.Auteur, appel, transmettreTn ? AppelEtats.TransmisTn : AppelEtats.EnAttente, null, input.Motif, ct);
            tn = await TnActuelAsync(db, ct);
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        if (transmettreTn && tn is not null && await DiscordAgentAsync(db, tn.AgentId, ct) is { } discord)
            await notifier.AgentsAsync([discord], "PÉGASE — appel transmis au TN",
                $"{appel.Numero} : {appel.Formulaire.Motif.Motif ?? "motif à qualifier"} — {appel.Formulaire.Localisation.Commune ?? "commune non précisée"}",
                SaphirApps.Pegase);
        await notifier.PegaseAsync("appel:" + id);
        return (await LireAppelAsync(db, id, false, ct))!;
    }

    /// <summary>Clôturer l'appel sans suite (motif obligatoire).</summary>
    public async Task<AppelDto> CloreAppelAsync(AgentContext ctx, long id, AppelActionInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Motif))
            throw ApiException.Saisie("Le motif de clôture sans suite est obligatoire.");
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        try
        {
            var appel = await PreparerAppelAsync(db, ctx.Auteur, id, input, true, ct);
            await ChangerEtatAppelAsync(db, ctx.Auteur, appel, AppelEtats.ClosSansSuite, null, input.Motif, ct);
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        await notifier.PegaseAsync("appel:" + id);
        return (await LireAppelAsync(db, id, false, ct))!;
    }

    /// <summary>Rattacher l'appel à une intervention existante (plusieurs appels pour une même intervention).</summary>
    public async Task<AppelDto> RattacherAppelAsync(AgentContext ctx, long id, AppelActionInput input, CancellationToken ct)
    {
        if (input.InterventionId is not { } interventionId)
            throw ApiException.Saisie("Choisis l'intervention à laquelle rattacher l'appel.");
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        try
        {
            var intervention = await LireInterventionBaseAsync(db, interventionId, true, ct) ?? throw ApiException.Introuvable("Intervention introuvable.");
            if (InterventionStatuts.EstTerminee(intervention.Statut))
                throw ApiException.Conflit($"L'intervention {intervention.Numero} est terminée.");
            var appel = await PreparerAppelAsync(db, ctx.Auteur, id, input, true, ct);
            await ChangerEtatAppelAsync(db, ctx.Auteur, appel, AppelEtats.Rattache, interventionId, null, ct);
            await ChronoAsync(db, interventionId, "Appel", $"Appel {appel.Numero} rattaché ({appel.Formulaire.Canal ?? "canal non précisé"}).", ctx.Auteur, null, ct);
            await TouchInterventionAsync(db, interventionId, ct);
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        await notifier.PegaseAsync("intervention:" + interventionId);
        return (await LireAppelAsync(db, id, false, ct))!;
    }

    /// <summary>Créer l'intervention depuis l'appel ; engagement immédiat d'un équipage si demandé (et permis).</summary>
    public async Task<InterventionDto> CreerInterventionAsync(AgentContext ctx, long appelId, AppelActionInput input, CancellationToken ct)
    {
        if (input.EquipageId is not null && !ctx.Droits.Possede(Permissions.PegaseAffecter))
            throw new ApiException(StatusCodes.Status403Forbidden, "Accès refusé : l'affectation d'un équipage demande la permission « Affecter les équipages ».");
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var apres = new Apres();
        long id;
        try
        {
            var appel = await PreparerAppelAsync(db, ctx.Auteur, appelId, input, true, ct);
            var f = appel.Formulaire;
            var nature = f.Motif.Motif ?? "Non qualifié";
            var priorite = f.Motif.Priorite ?? "P2";
            var statut = OperationsRegles.StatutInitial(f.Motif.Motif, f.Engagement.Necessaire || input.EquipageId is not null);
            var requerant = f.Requerant.IdentiteConnue
                ? $"{OperationsRegles.LibellePersonne(f.Requerant.Nom, f.Requerant.Prenom)} ({f.Requerant.Qualite ?? "requérant"})"
                : f.Requerant.Qualite is null ? "Requérant non identifié" : $"{f.Requerant.Qualite} (identité non communiquée)";
            var maintenant = DateTime.UtcNow;
            id = await db.InsertAsync(
                """
                INSERT INTO pg_interventions (nature, sous_motif, priorite, description, adresse, commune, code_postal, x, y, fiabilite, requerant,
                                              securite_json, statut, debut_le, equipages_demandes, categorie_moyen, suite_judiciaire,
                                              cree_par_id, cree_par, cree_le, maj_le, version)
                VALUES (@nature, @sous_motif, @priorite, @description, @adresse, @commune, @cp, @x, @y, @fiabilite, @requerant,
                        @securite, @statut, @debut, @demandes, @categorie, 0, @auteur_id, @auteur, @maintenant, @maintenant, 1)
                """, ct,
                ("@nature", nature), ("@sous_motif", f.Motif.SousMotif), ("@priorite", priorite), ("@description", f.Motif.Description),
                ("@adresse", OpSql.T(OperationsRegles.AdresseAffichee(f.Localisation), 300)), ("@commune", f.Localisation.Commune),
                ("@cp", f.Localisation.CodePostal), ("@x", f.Localisation.X), ("@y", f.Localisation.Y), ("@fiabilite", f.Localisation.Fiabilite),
                ("@requerant", OpSql.T(requerant, 200)), ("@securite", OpSql.Serialiser(f.Securite)), ("@statut", statut),
                ("@debut", OpSql.Utc(appel.RecuLe)), ("@demandes", f.Engagement.NombreEquipages), ("@categorie", f.Engagement.CategorieMoyen),
                ("@auteur_id", ctx.Auteur.Id), ("@auteur", ctx.Auteur.Nom), ("@maintenant", maintenant));
            var numero = OperationsRegles.Numero(OperationsRegles.PrefixeIntervention, DateTimeOffset.UtcNow, id);
            await db.ExecuteAsync("UPDATE pg_interventions SET numero = @numero WHERE id = @id", ct, ("@numero", numero), ("@id", id));
            await ChangerEtatAppelAsync(db, ctx.Auteur, appel, AppelEtats.InterventionCreee, id, null, ct);

            await Historique.EcrireAsync(db, ctx.Auteur, OperationsRegles.ObjetIntervention, numero, "Création", "statut", null, statut,
                "Depuis l'appel " + appel.Numero, ct);
            await StatutHistoAsync(db, "intervention", id, id, null, statut, null, ctx.Auteur, ct);
            await ChronoAsync(db, id, "Création",
                $"Intervention créée depuis l'appel {appel.Numero} ({appel.Formulaire.Canal ?? "canal non précisé"}) — {nature}, {PegaseListes.LibellePriorite(priorite)}.",
                ctx.Auteur, null, ct);
            if (f.Securite.Arme || f.Securite.Menace || f.Motif.DangerImmediat)
                await ChronoAsync(db, id, "Sécurité",
                    "Attention : " + string.Join(", ", new[] { f.Securite.Arme ? "arme signalée" : null, f.Securite.Menace ? "menace signalée" : null,
                        f.Motif.DangerImmediat ? "danger immédiat" : null }.Where(t => t is not null)) + ".", ctx.Auteur, null, ct);
            await new EvenementService(JournalTypes.InterventionCreee,
                $"Intervention {numero} créée — {nature}" + (f.Localisation.Commune is null ? string.Empty : $", {f.Localisation.Commune}"))
            {
                Details = $"{PegaseListes.LibellePriorite(priorite)}. Appel {appel.Numero}.",
                Source = SourcePegase,
                InterventionId = id,
                InterventionNumero = numero
            }.EcrireAsync(db, ctx.Auteur, ct);

            if (input.EquipageId is { } equipageId)
                await EngagerAsync(db, ctx.Auteur, id, new EngagementInput(equipageId, false), apres, ct);

            var tn = await TnActuelAsync(db, ct);
            var tnDiscord = tn is null ? null : await DiscordAgentAsync(db, tn.AgentId, ct);
            var commune = f.Localisation.Commune;
            var demandes = f.Engagement.NombreEquipages;
            var debut = appel.RecuLe;
            var statutFinal = (await LireInterventionBaseAsync(db, id, false, ct))!.Statut;
            apres.Ajouter(() =>
            {
                notifier.DiscordNouvelleIntervention(numero, debut, nature, commune, priorite, demandes, statutFinal);
                return Task.CompletedTask;
            });
            if (tnDiscord is not null)
                apres.Ajouter(() => notifier.AgentsAsync([tnDiscord], $"PÉGASE — {numero}",
                    $"Nouvelle intervention {PegaseListes.LibellePriorite(priorite)} : {nature}" + (commune is null ? "." : $" — {commune}."), SaphirApps.Pegase));
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        await apres.ExecuterAsync();
        await notifier.PegaseAsync("intervention:" + id);
        await notifier.MainCouranteAsync("journal");
        return (await LireInterventionAsync(db, id, ct))!;
    }

    // =====================================================================================
    // Interventions
    // =====================================================================================

    public async Task<List<InterventionLigneDto>> ListerInterventionsAsync(string? vue, string? q, DateTimeOffset? du, DateTimeOffset? au, int limite, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await ListerInterventionsAsync(db, vue, q, du, au, limite, ct);
    }

    private static async Task<List<InterventionLigneDto>> ListerInterventionsAsync(SqlSession db, string? vue, string? q, DateTimeOffset? du,
        DateTimeOffset? au, int limite, CancellationToken ct)
    {
        var sql = new StringBuilder(
            """
            SELECT i.id, i.numero, i.nature, i.sous_motif, i.priorite, i.adresse, i.commune, i.debut_le, i.statut, i.maj_le,
                   (SELECT COUNT(*) FROM pg_appels a WHERE a.intervention_id = i.id) AS nb_appels
            FROM pg_interventions i WHERE 1 = 1
            """);
        var parametres = new List<(string Name, object? Value)>();
        void Statuts(params string[] statuts)
        {
            sql.Append(" AND i.statut IN (");
            for (var k = 0; k < statuts.Length; k++)
            {
                var nom = "@s" + k.ToString(CultureInfo.InvariantCulture);
                sql.Append(k == 0 ? nom : ", " + nom);
                parametres.Add((nom, statuts[k]));
            }
            sql.Append(')');
        }
        var actives = true;
        switch (vue)
        {
            case "en-cours":
                Statuts(InterventionStatuts.MoyensAffectes, InterventionStatuts.EnCours, InterventionStatuts.CompteRenduAttendu);
                break;
            case "en-attente":
                Statuts(InterventionStatuts.AQualifier, InterventionStatuts.EnAttente);
                break;
            case "actives":
                Statuts(InterventionStatuts.AQualifier, InterventionStatuts.EnAttente, InterventionStatuts.MoyensAffectes,
                    InterventionStatuts.EnCours, InterventionStatuts.CompteRenduAttendu);
                break;
            case "terminees":
                Statuts(InterventionStatuts.Cloturee, InterventionStatuts.Annulee);
                actives = false;
                break;
            default:
                actives = false;
                break;
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            sql.Append(" AND (i.numero LIKE @q OR i.nature LIKE @q OR i.adresse LIKE @q OR i.commune LIKE @q OR i.sous_motif LIKE @q)");
            parametres.Add(("@q", OpSql.Like(q)));
        }
        if (du is { } d)
        {
            sql.Append(" AND i.debut_le >= @du");
            parametres.Add(("@du", OpSql.Utc(d)));
        }
        if (au is { } a)
        {
            sql.Append(" AND i.debut_le <= @au");
            parametres.Add(("@au", OpSql.Utc(a)));
        }
        sql.Append(actives ? " ORDER BY i.priorite, i.debut_le DESC" : " ORDER BY i.debut_le DESC, i.id DESC");
        sql.Append(" LIMIT @limite");
        parametres.Add(("@limite", limite));

        var lignes = await db.QueryAsync(sql.ToString(), r => new InterventionLigneDto(
            r.Long("id"), r.Text("numero"), r.Text("nature"), r.NullableText("sous_motif"), r.Text("priorite"), r.NullableText("adresse"),
            r.NullableText("commune"), r.Date("debut_le"), r.Text("statut"), [], r.Int("nb_appels"), r.Date("maj_le")), ct, parametres.ToArray());
        if (lignes.Count == 0)
            return lignes;

        var p2 = new List<(string Name, object? Value)>();
        var clause = OpSql.In("i", lignes.Select(l => l.Id).ToList(), p2);
        var engagements = (await db.QueryAsync(
                $"SELECT intervention_id, indicatif, statut FROM pg_engagements WHERE actif = 1 AND intervention_id IN ({clause}) ORDER BY demande_le",
                r => (Id: r.Long("intervention_id"), Texte: $"{r.Text("indicatif")} ({r.Text("statut")})"), ct, p2.ToArray()))
            .GroupBy(e => e.Id).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(e => e.Texte).ToList());
        return lignes.Select(l => engagements.TryGetValue(l.Id, out var e) ? l with { Equipages = e } : l).ToList();
    }

    private static InterventionDto LireInterventionBase(SqlRow r) => new(
        r.Long("id"), r.Text("numero"), r.Text("nature"), r.NullableText("sous_motif"), r.Text("priorite"), r.NullableText("description"),
        r.NullableText("adresse"), r.NullableText("commune"), r.NullableText("code_postal"), OpSql.Double(r, "x"), OpSql.Double(r, "y"),
        r.NullableText("fiabilite"), r.NullableText("requerant"), r.Text("statut"), r.Date("debut_le"), r.NullableDate("cloture_le"),
        r.NullableText("motif_statut"), OpSql.NullableInt(r, "equipages_demandes"), r.NullableText("categorie_moyen"),
        r.Bool("suite_judiciaire"), r.NullableText("procedure_lrppn"), r.NullableText("suites"), r.NullableText("documents"),
        r.Text("cree_par"), OpSql.Lire<AppelSecurite>(r.NullableText("securite_json")), [], [], [], [], [], [], [], r.Long("version"));

    private static Task<InterventionDto?> LireInterventionBaseAsync(SqlSession db, long id, bool verrouiller, CancellationToken ct)
        => db.FirstOrDefaultAsync("SELECT * FROM pg_interventions WHERE id = @id" + (verrouiller ? " FOR UPDATE" : string.Empty),
            LireInterventionBase, ct, ("@id", id));

    public async Task<InterventionDto> LireInterventionAsync(long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await LireInterventionAsync(db, id, ct) ?? throw ApiException.Introuvable("Intervention introuvable.");
    }

    private static async Task<InterventionDto?> LireInterventionAsync(SqlSession db, long id, CancellationToken ct)
    {
        var intervention = await LireInterventionBaseAsync(db, id, false, ct);
        if (intervention is null)
            return null;
        var appels = await db.QueryAsync(SelectAppelResume + " WHERE a.intervention_id = @id ORDER BY a.recu_le", LireAppelResume, ct, ("@id", id));
        var personnes = await db.QueryAsync("SELECT * FROM pg_personnes WHERE intervention_id = @id ORDER BY appel_id, ordre", LirePersonne, ct, ("@id", id));
        var vehicules = await db.QueryAsync("SELECT * FROM pg_vehicules WHERE intervention_id = @id ORDER BY appel_id, ordre", LireVehicule, ct, ("@id", id));
        var engagements = await LireEngagementsAsync(db, id, ct);
        var chronologie = await db.QueryAsync(
            "SELECT id, `date`, type, texte, auteur FROM pg_chronologie WHERE intervention_id = @id ORDER BY `date`, id",
            r => new ChronologieDto(r.Long("id"), r.Date("date"), r.Text("type"), r.Text("texte"), r.Text("auteur")), ct, ("@id", id));
        var comptesRendus = await db.QueryAsync(
            "SELECT c.*, i.numero AS intervention_numero FROM pg_comptes_rendus c JOIN pg_interventions i ON i.id = c.intervention_id WHERE c.intervention_id = @id ORDER BY c.cree_le",
            LireCompteRendu, ct, ("@id", id));
        var mentions = await MainCouranteStore.ListerMentionsAsync(db, null, null, null, null, null, id, 50, ct);
        return intervention with
        {
            Appels = appels, Personnes = personnes, Vehicules = vehicules, Engagements = engagements, Chronologie = chronologie,
            ComptesRendus = comptesRendus, Mentions = mentions
        };
    }

    private static Task<List<EngagementDto>> LireEngagementsAsync(SqlSession db, long interventionId, CancellationToken ct)
        => db.QueryAsync(
            """
            SELECT g.*, e.statut AS statut_equipage FROM pg_engagements g LEFT JOIN mc_equipages e ON e.id = g.equipage_id
            WHERE g.intervention_id = @id ORDER BY g.demande_le, g.id
            """,
            r => new EngagementDto(r.Long("id"), r.Long("equipage_id"), r.Text("indicatif"), r.NullableText("chef"), r.NullableText("agents"),
                r.NullableText("vehicule"), r.Text("statut"), r.Text("statut_equipage", EquipageStatuts.HorsService), r.Bool("renfort"),
                r.Bool("actif"), r.Date("demande_le"), r.NullableDate("affecte_le"), r.NullableText("affecte_par"), r.NullableDate("en_route_le"),
                r.NullableDate("sur_place_le"), r.NullableDate("termine_le")), ct, ("@id", interventionId));

    /// <summary>Résumé partagé (LRPPN, Main courante, NEO).</summary>
    public async Task<InterventionResumeDto> ResumeAsync(long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var i = await LireInterventionBaseAsync(db, id, false, ct) ?? throw ApiException.Introuvable("Intervention introuvable.");
        var equipages = await db.QueryAsync("SELECT DISTINCT indicatif FROM pg_engagements WHERE intervention_id = @id ORDER BY indicatif",
            r => r.Text("indicatif"), ct, ("@id", id));
        return new InterventionResumeDto(i.Id, i.Numero, i.Nature, i.Adresse, i.Commune, i.DebutLe, i.Statut, equipages);
    }

    public async Task<InterventionDto> ModifierInterventionAsync(AgentContext ctx, long id, InterventionModificationInput input, CancellationToken ct)
    {
        if (!PegaseListes.Motifs.Contains(input.Nature))
            throw ApiException.Saisie("Motif inconnu.");
        if (!PegaseListes.Priorites.Contains(input.Priorite))
            throw ApiException.Saisie("Priorité inconnue.");
        if (input.CategorieMoyen is not null && !PegaseListes.CategoriesMoyen.Contains(input.CategorieMoyen))
            throw ApiException.Saisie("Catégorie de moyen inconnue.");
        if (input.X is { } x && input.Y is { } y && !OperationsRegles.PositionPlausible(x, y))
            throw ApiException.Saisie("Position invalide.");
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        try
        {
            var actuelle = await LireInterventionBaseAsync(db, id, true, ct) ?? throw ApiException.Introuvable("Intervention introuvable.");
            if (InterventionStatuts.EstTerminee(actuelle.Statut))
                throw ApiException.Conflit("L'intervention est terminée : elle ne peut plus être modifiée.");
            OpSql.VerifierVersion(input.Version, actuelle.Version);
            var justification = OpSql.T(input.Justification, 500);
            if (actuelle.Priorite != input.Priorite && justification is null)
                throw ApiException.Saisie("Le changement de priorité doit être justifié.");

            var apres = new
            {
                Nature = input.Nature, SousMotif = OpSql.T(input.SousMotif, 120), Priorite = input.Priorite, Description = OpSql.T(input.Description, 8000),
                Adresse = OpSql.T(input.Adresse, 300), Commune = OpSql.T(input.Commune, 100), CodePostal = OpSql.T(input.CodePostal, 10),
                X = input.X, Y = input.Y, EquipagesDemandes = input.EquipagesDemandes is >= 0 and <= 50 ? input.EquipagesDemandes : null,
                CategorieMoyen = input.CategorieMoyen
            };
            var statut = actuelle.Statut == InterventionStatuts.AQualifier ? InterventionStatuts.EnAttente : actuelle.Statut;
            await db.ExecuteAsync(
                """
                UPDATE pg_interventions SET nature = @nature, sous_motif = @sous_motif, priorite = @priorite, description = @description,
                       adresse = @adresse, commune = @commune, code_postal = @cp, x = @x, y = @y, equipages_demandes = @demandes,
                       categorie_moyen = @categorie, statut = @statut, maj_le = @maintenant, version = version + 1
                WHERE id = @id
                """, ct,
                ("@nature", apres.Nature), ("@sous_motif", apres.SousMotif), ("@priorite", apres.Priorite), ("@description", apres.Description),
                ("@adresse", apres.Adresse), ("@commune", apres.Commune), ("@cp", apres.CodePostal), ("@x", apres.X), ("@y", apres.Y),
                ("@demandes", apres.EquipagesDemandes), ("@categorie", apres.CategorieMoyen), ("@statut", statut),
                ("@maintenant", DateTime.UtcNow), ("@id", id));
            var avant = new
            {
                actuelle.Nature, actuelle.SousMotif, actuelle.Priorite, actuelle.Description, actuelle.Adresse, actuelle.Commune, actuelle.CodePostal,
                actuelle.X, actuelle.Y, actuelle.EquipagesDemandes, actuelle.CategorieMoyen
            };
            await OpSql.HistoriserDifferencesAsync(db, ctx.Auteur, OperationsRegles.ObjetIntervention, actuelle.Numero, string.Empty, avant, apres, justification, ct);
            if (actuelle.Priorite != input.Priorite)
                await ChronoAsync(db, id, "Priorité", $"Priorité {actuelle.Priorite} → {input.Priorite} : {justification}", ctx.Auteur, null, ct);
            else
                await ChronoAsync(db, id, "Qualification", "Qualification de l'intervention mise à jour.", ctx.Auteur, null, ct);
            if (statut != actuelle.Statut)
            {
                await StatutHistoAsync(db, "intervention", id, id, actuelle.Statut, statut, "Qualification", ctx.Auteur, ct);
                await Historique.EcrireAsync(db, ctx.Auteur, OperationsRegles.ObjetIntervention, actuelle.Numero, "Statut", "statut", actuelle.Statut, statut, "Qualification", ct);
            }
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        await notifier.PegaseAsync("intervention:" + id);
        return (await LireInterventionAsync(db, id, ct))!;
    }

    public async Task<InterventionDto> PositionInterventionAsync(AgentContext ctx, long id, PositionInput input, CancellationToken ct)
    {
        if (!OperationsRegles.PositionPlausible(input.X, input.Y))
            throw ApiException.Saisie("Position invalide.");
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        try
        {
            var actuelle = await LireInterventionBaseAsync(db, id, true, ct) ?? throw ApiException.Introuvable("Intervention introuvable.");
            if (InterventionStatuts.EstTerminee(actuelle.Statut))
                throw ApiException.Conflit("L'intervention est terminée.");
            await db.ExecuteAsync("UPDATE pg_interventions SET x = @x, y = @y, maj_le = @maintenant, version = version + 1 WHERE id = @id", ct,
                ("@x", input.X), ("@y", input.Y), ("@maintenant", DateTime.UtcNow), ("@id", id));
            var texte = FormattableString.Invariant($"{input.X:0.#} ; {input.Y:0.#}");
            await Historique.EcrireAsync(db, ctx.Auteur, OperationsRegles.ObjetIntervention, actuelle.Numero, "Modification", "position",
                actuelle.X is null ? null : FormattableString.Invariant($"{actuelle.X:0.#} ; {actuelle.Y:0.#}"), texte, "GÉOOPS", ct);
            await ChronoAsync(db, id, "GÉOOPS", "Lieu d'intervention placé sur la carte (position déclarée).", ctx.Auteur, null, ct);
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        await notifier.PegaseAsync("intervention:" + id);
        return (await LireInterventionAsync(db, id, ct))!;
    }

    public async Task<InterventionDto> ChangerStatutInterventionAsync(AgentContext ctx, long id, InterventionStatutInput input, CancellationToken ct)
    {
        var nouveau = input.Statut;
        var droits = ctx.Droits;
        if (nouveau == InterventionStatuts.Cloturee && !droits.Possede(Permissions.PegaseCloturer))
            throw new ApiException(StatusCodes.Status403Forbidden, "Accès refusé : la clôture demande la permission « Clôturer une intervention ».");
        if (nouveau == InterventionStatuts.Annulee && !droits.Possede(Permissions.PegaseCloturer) && !droits.Possede(Permissions.PegaseAffecter))
            throw new ApiException(StatusCodes.Status403Forbidden, "Accès refusé : l'annulation demande la permission de clôturer ou d'affecter.");
        if (!InterventionStatuts.EstTerminee(nouveau) && !droits.Possede(Permissions.PegaseAffecter) && !droits.Possede(Permissions.PegaseInterventionCreer))
            throw new ApiException(StatusCodes.Status403Forbidden, "Accès refusé : tu ne peux pas changer le statut de cette intervention.");

        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var equipagesLiberes = new List<long>();
        try
        {
            var actuelle = await LireInterventionBaseAsync(db, id, true, ct) ?? throw ApiException.Introuvable("Intervention introuvable.");
            var crValide = await db.LongAsync("SELECT COUNT(*) FROM pg_comptes_rendus WHERE intervention_id = @id AND statut = @v", ct,
                ("@id", id), ("@v", CompteRenduStatuts.Valide)) > 0;
            if (OperationsRegles.ControlerStatutIntervention(actuelle.Statut, nouveau, input.Motif, crValide) is { } erreur)
                throw ApiException.Saisie(erreur);
            var motif = OpSql.T(input.Motif, 500);
            var maintenant = DateTime.UtcNow;
            var terminee = InterventionStatuts.EstTerminee(nouveau);
            await db.ExecuteAsync(
                """
                UPDATE pg_interventions SET statut = @statut, motif_statut = COALESCE(@motif, motif_statut),
                       cloture_le = CASE WHEN @terminee THEN @maintenant ELSE cloture_le END, maj_le = @maintenant, version = version + 1
                WHERE id = @id
                """, ct, ("@statut", nouveau), ("@motif", motif), ("@terminee", terminee), ("@maintenant", maintenant), ("@id", id));

            if (terminee)
            {
                // Fin de l'intervention : engagements clos, équipages rendus disponibles.
                var actifs = (await LireEngagementsAsync(db, id, ct)).Where(e => e.Actif).ToList();
                foreach (var engagement in actifs)
                {
                    await db.ExecuteAsync(
                        "UPDATE pg_engagements SET actif = 0, statut = @statut, termine_le = COALESCE(termine_le, @maintenant), maj_le = @maintenant WHERE id = @id",
                        ct, ("@statut", EquipageStatuts.MissionTerminee), ("@maintenant", maintenant), ("@id", engagement.Id));
                    var libere = await db.ExecuteAsync(
                        "UPDATE mc_equipages SET statut = @dispo, intervention_id = NULL, maj_le = @maintenant WHERE id = @equipage AND intervention_id = @id AND actif = 1",
                        ct, ("@dispo", EquipageStatuts.Disponible), ("@maintenant", maintenant), ("@equipage", engagement.EquipageId), ("@id", id));
                    if (libere > 0)
                    {
                        equipagesLiberes.Add(engagement.EquipageId);
                        await StatutHistoAsync(db, "equipage", engagement.EquipageId, id, engagement.StatutEquipage, EquipageStatuts.Disponible,
                            $"Intervention {nouveau.ToLowerInvariant()}", ctx.Auteur, ct);
                    }
                }
                await new EvenementService(JournalTypes.InterventionTerminee, $"Intervention {actuelle.Numero} {nouveau.ToLowerInvariant()} — {actuelle.Nature}")
                {
                    Details = motif,
                    Source = SourcePegase,
                    InterventionId = id,
                    InterventionNumero = actuelle.Numero
                }.EcrireAsync(db, ctx.Auteur, ct);
            }
            await StatutHistoAsync(db, "intervention", id, id, actuelle.Statut, nouveau, motif, ctx.Auteur, ct);
            await Historique.EcrireAsync(db, ctx.Auteur, OperationsRegles.ObjetIntervention, actuelle.Numero, "Statut", "statut", actuelle.Statut, nouveau, motif, ct);
            await ChronoAsync(db, id, "Statut", $"Statut : {actuelle.Statut} → {nouveau}" + (motif is null ? "." : $" ({motif})."), ctx.Auteur, null, ct);
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        await notifier.PegaseAsync("intervention:" + id);
        if (equipagesLiberes.Count > 0 || InterventionStatuts.EstTerminee(nouveau))
            await notifier.MainCouranteAsync("journal");
        return (await LireInterventionAsync(db, id, ct))!;
    }

    // ----- Engagements (8.4) -----

    public async Task<InterventionDto> EngagerAsync(AgentContext ctx, long id, EngagementInput input, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var apres = new Apres();
        try
        {
            await EngagerAsync(db, ctx.Auteur, id, input, apres, ct);
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        await apres.ExecuterAsync();
        await notifier.PegaseAsync("intervention:" + id);
        await notifier.MainCouranteAsync("equipage:" + input.EquipageId);
        return (await LireInterventionAsync(db, id, ct))!;
    }

    private async Task EngagerAsync(SqlSession db, Auteur auteur, long interventionId, EngagementInput input, Apres apres, CancellationToken ct)
    {
        var intervention = await LireInterventionBaseAsync(db, interventionId, true, ct) ?? throw ApiException.Introuvable("Intervention introuvable.");
        if (InterventionStatuts.EstTerminee(intervention.Statut))
            throw ApiException.Conflit("L'intervention est terminée : aucun engagement possible.");
        await db.ExecuteAsync("SELECT id FROM mc_equipages WHERE id = @id FOR UPDATE", ct, ("@id", input.EquipageId));
        var equipage = await MainCouranteStore.LireEquipageAsync(db, input.EquipageId, ct) ?? throw ApiException.Introuvable("Équipage introuvable.");
        if (!equipage.Actif)
            throw ApiException.Conflit($"L'équipage {equipage.Indicatif} est dissous.");
        if (equipage.InterventionId is { } autre)
            throw ApiException.Conflit(autre == interventionId
                ? $"L'équipage {equipage.Indicatif} est déjà engagé sur cette intervention."
                : $"L'équipage {equipage.Indicatif} est déjà engagé sur l'intervention {equipage.InterventionNumero}.");
        if (!EquipageStatuts.PeutEtreEngage(equipage.Statut))
            throw ApiException.Conflit($"L'équipage {equipage.Indicatif} n'est pas disponible ({equipage.Statut}).");

        var maintenant = DateTime.UtcNow;
        var dejaEngages = await db.LongAsync("SELECT COUNT(*) FROM pg_engagements WHERE intervention_id = @id", ct, ("@id", interventionId));
        var renfort = input.Renfort || dejaEngages > 0;
        var agents = SocleRegles.Liste(equipage.Membres.Select(m => m.Libelle));
        await db.InsertAsync(
            """
            INSERT INTO pg_engagements (intervention_id, equipage_id, indicatif, chef, agents, vehicule, statut, renfort, actif, demande_le,
                                        affecte_le, affecte_par, maj_le)
            VALUES (@intervention, @equipage, @indicatif, @chef, @agents, @vehicule, @statut, @renfort, 1, @maintenant, @maintenant, @auteur, @maintenant)
            """, ct,
            ("@intervention", interventionId), ("@equipage", equipage.Id), ("@indicatif", equipage.Indicatif), ("@chef", equipage.Chef),
            ("@agents", OpSql.T(agents, 1000)), ("@vehicule", OpSql.T(equipage.Vehicule, 100)), ("@statut", EquipageStatuts.Affecte),
            ("@renfort", renfort), ("@maintenant", maintenant), ("@auteur", auteur.Nom));
        await db.ExecuteAsync("UPDATE mc_equipages SET statut = @statut, intervention_id = @intervention, maj_le = @maintenant WHERE id = @id", ct,
            ("@statut", EquipageStatuts.Affecte), ("@intervention", interventionId), ("@maintenant", maintenant), ("@id", equipage.Id));
        await StatutHistoAsync(db, "equipage", equipage.Id, interventionId, equipage.Statut, EquipageStatuts.Affecte, "Engagement " + intervention.Numero, auteur, ct);
        await Historique.EcrireAsync(db, auteur, OperationsRegles.ObjetIntervention, intervention.Numero, "Engagement", "equipages", null,
            equipage.Indicatif + (renfort ? " (renfort)" : string.Empty), null, ct);
        await ChronoAsync(db, interventionId, "Engagement",
            $"{(renfort ? "Renfort" : "Engagement")} : {equipage.Indicatif} — chef de bord {equipage.Chef ?? "non désigné"}" +
            (equipage.Vehicule is null ? "." : $", véhicule {equipage.Vehicule}."), auteur, equipage.Id, ct);
        await new EvenementService(JournalTypes.Engagement, $"Engagement de {equipage.Indicatif} sur {intervention.Numero} — {intervention.Nature}" +
                                                            (intervention.Commune is null ? string.Empty : $", {intervention.Commune}"))
        {
            Details = $"Équipage : {agents}." + (renfort ? " Renfort." : string.Empty),
            Source = SourcePegase,
            VacationId = equipage.VacationId,
            EquipageId = equipage.Id,
            VehiculeId = equipage.VehiculeId,
            InterventionId = interventionId,
            InterventionNumero = intervention.Numero
        }.EcrireAsync(db, auteur, ct);
        await AppliquerStatutDeduitAsync(db, auteur, intervention, ct);

        // Les agents de l'équipage reçoivent une bulle sur leur poste (référence et motif uniquement).
        var discordIds = await DiscordMembresAsync(db, equipage.Id, ct);
        var titre = $"PÉGASE — engagement {equipage.Indicatif}";
        var texte = $"{intervention.Numero} : {intervention.Nature}" + (intervention.Commune is null ? string.Empty : $" — {intervention.Commune}") +
                    $" ({PegaseListes.LibellePriorite(intervention.Priorite)}).";
        apres.Ajouter(() => notifier.AgentsAsync(discordIds, titre, texte, SaphirApps.Pegase));
    }

    public async Task<InterventionDto> DesengagerAsync(AgentContext ctx, long id, long engagementId, MotifInput input, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        long equipageId;
        try
        {
            var intervention = await LireInterventionBaseAsync(db, id, true, ct) ?? throw ApiException.Introuvable("Intervention introuvable.");
            var engagement = (await LireEngagementsAsync(db, id, ct)).FirstOrDefault(e => e.Id == engagementId)
                             ?? throw ApiException.Introuvable("Engagement introuvable.");
            if (!engagement.Actif)
                throw ApiException.Conflit("Cet engagement est déjà terminé.");
            equipageId = engagement.EquipageId;
            var maintenant = DateTime.UtcNow;
            var motif = OpSql.T(input.Motif, 500);
            await db.ExecuteAsync("UPDATE pg_engagements SET actif = 0, statut = 'Désengagé', termine_le = @maintenant, maj_le = @maintenant WHERE id = @id", ct,
                ("@maintenant", maintenant), ("@id", engagementId));
            await db.ExecuteAsync(
                "UPDATE mc_equipages SET statut = @dispo, intervention_id = NULL, maj_le = @maintenant WHERE id = @equipage AND intervention_id = @intervention",
                ct, ("@dispo", EquipageStatuts.Disponible), ("@maintenant", maintenant), ("@equipage", equipageId), ("@intervention", id));
            await StatutHistoAsync(db, "equipage", equipageId, id, engagement.StatutEquipage, EquipageStatuts.Disponible, motif ?? "Désengagement", ctx.Auteur, ct);
            await Historique.EcrireAsync(db, ctx.Auteur, OperationsRegles.ObjetIntervention, intervention.Numero, "Désengagement", "equipages",
                engagement.Indicatif, null, motif, ct);
            await ChronoAsync(db, id, "Engagement", $"Désengagement de {engagement.Indicatif}" + (motif is null ? "." : $" : {motif}."), ctx.Auteur, equipageId, ct);
            await AppliquerStatutDeduitAsync(db, ctx.Auteur, intervention, ct);
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        await notifier.PegaseAsync("intervention:" + id);
        await notifier.MainCouranteAsync("equipage:" + equipageId);
        return (await LireInterventionAsync(db, id, ct))!;
    }

    /// <summary>Statut d'un équipage (7.2), indépendant du statut global : chef de bord, membre de l'équipage ou affectateur.</summary>
    public async Task<EquipageDto> StatutEquipageAsync(AgentContext ctx, long equipageId, EquipageStatutInput input, CancellationToken ct)
    {
        var nouveau = input.Statut;
        if (!EquipageStatuts.All.Contains(nouveau))
            throw ApiException.Saisie("Statut d'équipage inconnu.");
        if (nouveau == EquipageStatuts.Affecte)
            throw ApiException.Saisie("Le statut « Affecté » est donné par l'engagement sur une intervention.");

        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        long? interventionId;
        try
        {
            await db.ExecuteAsync("SELECT id FROM mc_equipages WHERE id = @id FOR UPDATE", ct, ("@id", equipageId));
            var equipage = await MainCouranteStore.LireEquipageAsync(db, equipageId, ct) ?? throw ApiException.Introuvable("Équipage introuvable.");
            if (!equipage.Actif)
                throw ApiException.Conflit("L'équipage est dissous.");
            if (!ctx.Droits.Possede(Permissions.PegaseAffecter) && equipage.Membres.All(m => m.AgentId != ctx.Agent.Id))
                throw new ApiException(StatusCodes.Status403Forbidden, "Seuls les membres de l'équipage ou un affectateur peuvent changer son statut.");
            if (equipage.Statut == nouveau)
                throw ApiException.Conflit("L'équipage a déjà ce statut.");
            interventionId = equipage.InterventionId;
            var engageApres = EquipageStatuts.EstEngage(nouveau);
            if (interventionId is null && engageApres)
                throw ApiException.Conflit("L'équipage n'est engagé sur aucune intervention.");
            if (interventionId is not null && !engageApres && nouveau != EquipageStatuts.MissionTerminee)
                throw ApiException.Conflit("L'équipage est engagé : passe-le d'abord en « Mission terminée ».");

            var maintenant = DateTime.UtcNow;
            var motif = OpSql.T(input.Motif, 500);
            var finMission = interventionId is not null && nouveau == EquipageStatuts.MissionTerminee;
            await db.ExecuteAsync(
                "UPDATE mc_equipages SET statut = @statut, intervention_id = CASE WHEN @fin THEN NULL ELSE intervention_id END, maj_le = @maintenant WHERE id = @id",
                ct, ("@statut", nouveau), ("@fin", finMission), ("@maintenant", maintenant), ("@id", equipageId));
            await StatutHistoAsync(db, "equipage", equipageId, interventionId, equipage.Statut, nouveau, motif, ctx.Auteur, ct);
            await Historique.EcrireAsync(db, ctx.Auteur, OperationsRegles.ObjetEquipage, equipage.Indicatif + " #" + equipageId, "Statut", "statut",
                equipage.Statut, nouveau, motif, ct);

            if (interventionId is { } iid)
            {
                var intervention = (await LireInterventionBaseAsync(db, iid, true, ct))!;
                await db.ExecuteAsync(
                    """
                    UPDATE pg_engagements SET statut = @statut,
                           en_route_le = CASE WHEN @statut = @en_route AND en_route_le IS NULL THEN @maintenant ELSE en_route_le END,
                           sur_place_le = CASE WHEN @statut = @sur_place AND sur_place_le IS NULL THEN @maintenant ELSE sur_place_le END,
                           termine_le = CASE WHEN @fin THEN @maintenant ELSE termine_le END,
                           actif = CASE WHEN @fin THEN 0 ELSE actif END, maj_le = @maintenant
                    WHERE intervention_id = @intervention AND equipage_id = @equipage AND actif = 1
                    """, ct,
                    ("@statut", nouveau), ("@en_route", EquipageStatuts.EnRoute), ("@sur_place", EquipageStatuts.SurPlace), ("@fin", finMission),
                    ("@maintenant", maintenant), ("@intervention", iid), ("@equipage", equipageId));
                await ChronoAsync(db, iid, "Statut équipage", $"{equipage.Indicatif} : {nouveau}" + (motif is null ? "." : $" ({motif})."), ctx.Auteur, equipageId, ct);
                if (nouveau is EquipageStatuts.SurPlace or EquipageStatuts.MissionTerminee)
                    await new EvenementService(OperationsRegles.TypeJournalPourStatut(nouveau), $"{equipage.Indicatif} : {nouveau.ToLowerInvariant()} — {intervention.Numero}")
                    {
                        Details = intervention.Nature + (intervention.Commune is null ? string.Empty : ", " + intervention.Commune),
                        Source = SourcePegase,
                        VacationId = equipage.VacationId,
                        EquipageId = equipageId,
                        InterventionId = iid,
                        InterventionNumero = intervention.Numero
                    }.EcrireAsync(db, ctx.Auteur, ct);
                await AppliquerStatutDeduitAsync(db, ctx.Auteur, intervention, ct);
            }
            else if (nouveau is EquipageStatuts.Indisponible or EquipageStatuts.HorsService)
            {
                await new EvenementService(JournalTypes.StatutEquipage, $"{equipage.Indicatif} : {nouveau.ToLowerInvariant()}")
                {
                    Details = motif,
                    Source = SourcePegase,
                    VacationId = equipage.VacationId,
                    EquipageId = equipageId
                }.EcrireAsync(db, ctx.Auteur, ct);
            }
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        if (interventionId is { } i)
            await notifier.PegaseAsync("intervention:" + i);
        else
            await notifier.PegaseAsync("equipage:" + equipageId);
        await notifier.MainCouranteAsync("equipage:" + equipageId);
        return (await MainCouranteStore.LireEquipageAsync(db, equipageId, ct))!;
    }

    /// <summary>Applique le statut global déduit des engagements (et le trace).</summary>
    private static async Task AppliquerStatutDeduitAsync(SqlSession db, Auteur auteur, InterventionDto intervention, CancellationToken ct)
    {
        var engagements = await LireEngagementsAsync(db, intervention.Id, ct);
        var actifs = engagements.Where(e => e.Actif).Select(e => e.Statut).ToList();
        var dejaSurLeTerrain = engagements.Any(e => e.EnRouteLe is not null || e.SurPlaceLe is not null);
        var actuel = await db.FirstOrDefaultAsync("SELECT statut FROM pg_interventions WHERE id = @id", r => r.Text("statut"), ct, ("@id", intervention.Id))
                     ?? intervention.Statut;
        if (OperationsRegles.StatutDeduit(actuel, actifs, dejaSurLeTerrain) is not { } deduit)
            return;
        await db.ExecuteAsync("UPDATE pg_interventions SET statut = @statut, maj_le = @maintenant, version = version + 1 WHERE id = @id", ct,
            ("@statut", deduit), ("@maintenant", DateTime.UtcNow), ("@id", intervention.Id));
        await StatutHistoAsync(db, "intervention", intervention.Id, intervention.Id, actuel, deduit, "Automatique (équipages)", auteur, ct);
        await Historique.EcrireAsync(db, auteur, OperationsRegles.ObjetIntervention, intervention.Numero, "Statut", "statut", actuel, deduit, "Automatique (équipages)", ct);
        await ChronoAsync(db, intervention.Id, "Statut", $"Statut : {actuel} → {deduit}.", auteur, null, ct);
    }

    // ----- Communications, références -----

    public async Task<InterventionDto> MessageAsync(AgentContext ctx, long id, MessageInput input, CancellationToken ct)
    {
        var texte = OpSql.T(input.Texte, 2000) ?? throw ApiException.Saisie("Le message est vide.");
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        try
        {
            var intervention = await LireInterventionBaseAsync(db, id, true, ct) ?? throw ApiException.Introuvable("Intervention introuvable.");
            if (InterventionStatuts.EstTerminee(intervention.Statut))
                throw ApiException.Conflit("L'intervention est terminée.");
            await ChronoAsync(db, id, "Message", texte, ctx.Auteur, null, ct);
            await TouchInterventionAsync(db, id, ct);
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        await notifier.PegaseAsync("intervention:" + id);
        return (await LireInterventionAsync(db, id, ct))!;
    }

    public async Task<List<CommunicationDto>> CommunicationsAsync(bool messagesSeulement, int limite, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var sql = new StringBuilder(
            "SELECT c.id, c.`date`, c.intervention_id, i.numero, c.type, c.texte, c.auteur FROM pg_chronologie c JOIN pg_interventions i ON i.id = c.intervention_id");
        if (messagesSeulement)
            sql.Append(" WHERE c.type = 'Message'");
        sql.Append(" ORDER BY c.`date` DESC, c.id DESC LIMIT @limite");
        return await db.QueryAsync(sql.ToString(), r => new CommunicationDto(r.Long("id"), r.Date("date"), r.Long("intervention_id"),
            r.Text("numero"), r.Text("type"), r.Text("texte"), r.Text("auteur")), ct, ("@limite", limite));
    }

    public async Task<InterventionDto> ReferencesAsync(AgentContext ctx, long id, ReferencesInput input, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        try
        {
            var actuelle = await LireInterventionBaseAsync(db, id, true, ct) ?? throw ApiException.Introuvable("Intervention introuvable.");
            var apres = new { ProcedureLrppn = OpSql.T(input.ProcedureLrppn, 60), Suites = OpSql.T(input.Suites, 4000), Documents = OpSql.T(input.Documents, 4000) };
            await db.ExecuteAsync(
                "UPDATE pg_interventions SET procedure_lrppn = @procedure, suites = @suites, documents = @documents, maj_le = @maintenant, version = version + 1 WHERE id = @id",
                ct, ("@procedure", apres.ProcedureLrppn), ("@suites", apres.Suites), ("@documents", apres.Documents), ("@maintenant", DateTime.UtcNow), ("@id", id));
            await OpSql.HistoriserDifferencesAsync(db, ctx.Auteur, OperationsRegles.ObjetIntervention, actuelle.Numero, string.Empty,
                new { actuelle.ProcedureLrppn, actuelle.Suites, actuelle.Documents }, apres, null, ct);
            if (apres.ProcedureLrppn is not null && apres.ProcedureLrppn != actuelle.ProcedureLrppn)
                await ChronoAsync(db, id, "LRPPN", $"Procédure LRPPN liée : {apres.ProcedureLrppn}.", ctx.Auteur, null, ct);
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        await notifier.PegaseAsync("intervention:" + id);
        return (await LireInterventionAsync(db, id, ct))!;
    }

    // =====================================================================================
    // Comptes rendus (7.3)
    // =====================================================================================

    private static CompteRenduDto LireCompteRendu(SqlRow r) => new(
        r.Long("id"), r.Long("intervention_id"), r.Text("intervention_numero"), r.NullableLong("equipage_id"), r.NullableText("indicatif"),
        r.NullableText("chef_de_bord"), r.NullableDate("arrivee_le"), r.NullableDate("depart_le"), r.NullableDate("fin_le"),
        r.NullableText("resultat"), r.NullableText("faits"), r.NullableText("mesures"), r.NullableText("personnes_controlees"),
        r.NullableText("interpellations"), r.NullableText("victimes"), r.NullableText("vehicules"), r.NullableText("secours"),
        r.NullableText("transport"), r.NullableText("destination"), r.NullableText("documents"), r.Bool("suite_judiciaire"),
        r.NullableText("observations"), r.Text("statut"), r.Text("redacteur"), r.NullableText("valide_par"), r.NullableDate("valide_le"),
        r.Date("cree_le"), r.Long("version"));

    private static Task<CompteRenduDto?> LireCompteRenduAsync(SqlSession db, long id, bool verrouiller, CancellationToken ct)
        => db.FirstOrDefaultAsync(
            "SELECT c.*, i.numero AS intervention_numero FROM pg_comptes_rendus c JOIN pg_interventions i ON i.id = c.intervention_id WHERE c.id = @id" +
            (verrouiller ? " FOR UPDATE" : string.Empty), LireCompteRendu, ct, ("@id", id));

    private sealed record ContenuCompteRendu(
        long? EquipageId, string? Indicatif, string? ChefDeBord, DateTimeOffset? ArriveeLe, DateTimeOffset? DepartLe, DateTimeOffset? FinLe,
        string? Resultat, string? Faits, string? Mesures, string? PersonnesControlees, string? Interpellations, string? Victimes, string? Vehicules,
        string? Secours, string? Transport, string? Destination, string? Documents, bool SuiteJudiciaire, string? Observations);

    private static async Task<ContenuCompteRendu> NettoyerCompteRenduAsync(SqlSession db, long interventionId, CompteRenduInput input, CancellationToken ct)
    {
        if (input.Resultat is not null && !PegaseListes.ResultatsIntervention.Contains(input.Resultat))
            throw ApiException.Saisie("Résultat inconnu.");
        if (input.ArriveeLe is { } arrivee && input.FinLe is { } fin && fin < arrivee)
            throw ApiException.Saisie("L'heure de fin précède l'heure d'arrivée.");
        string? indicatif = OpSql.T(input.Indicatif, 20);
        string? chef = OpSql.T(input.ChefDeBord, 200);
        if (input.EquipageId is { } equipageId)
        {
            var engagement = (await LireEngagementsAsync(db, interventionId, ct)).FirstOrDefault(e => e.EquipageId == equipageId)
                             ?? throw ApiException.Saisie("Cet équipage n'a pas été engagé sur l'intervention.");
            indicatif ??= engagement.Indicatif;
            chef ??= engagement.Chef;
        }
        return new ContenuCompteRendu(input.EquipageId, indicatif, chef, input.ArriveeLe, input.DepartLe, input.FinLe, input.Resultat,
            OpSql.T(input.Faits, 60000), OpSql.T(input.Mesures, 4000), OpSql.T(input.PersonnesControlees, 4000), OpSql.T(input.Interpellations, 4000),
            OpSql.T(input.Victimes, 4000), OpSql.T(input.Vehicules, 4000), OpSql.T(input.Secours, 4000), OpSql.T(input.Transport, 4000),
            OpSql.T(input.Destination, 200), OpSql.T(input.Documents, 4000), input.SuiteJudiciaire, OpSql.T(input.Observations, 4000));
    }

    private static (string, object?)[] ParametresCompteRendu(ContenuCompteRendu c) =>
    [
        ("@equipage", c.EquipageId), ("@indicatif", c.Indicatif), ("@chef", c.ChefDeBord), ("@arrivee", OpSql.Utc(c.ArriveeLe)),
        ("@depart", OpSql.Utc(c.DepartLe)), ("@fin", OpSql.Utc(c.FinLe)), ("@resultat", c.Resultat), ("@faits", c.Faits), ("@mesures", c.Mesures),
        ("@controlees", c.PersonnesControlees), ("@interpellations", c.Interpellations), ("@victimes", c.Victimes), ("@vehicules", c.Vehicules),
        ("@secours", c.Secours), ("@transport", c.Transport), ("@destination", c.Destination), ("@documents", c.Documents),
        ("@suite", c.SuiteJudiciaire), ("@observations", c.Observations)
    ];

    private const string AffectationsCompteRendu =
        """
        equipage_id = @equipage, indicatif = @indicatif, chef_de_bord = @chef, arrivee_le = @arrivee, depart_le = @depart, fin_le = @fin,
        resultat = @resultat, faits = @faits, mesures = @mesures, personnes_controlees = @controlees, interpellations = @interpellations,
        victimes = @victimes, vehicules = @vehicules, secours = @secours, transport = @transport, destination = @destination,
        documents = @documents, suite_judiciaire = @suite, observations = @observations
        """;

    public async Task<CompteRenduDto> CreerCompteRenduAsync(AgentContext ctx, long interventionId, CompteRenduInput input, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        long id;
        try
        {
            var intervention = await LireInterventionBaseAsync(db, interventionId, true, ct) ?? throw ApiException.Introuvable("Intervention introuvable.");
            if (InterventionStatuts.EstTerminee(intervention.Statut))
                throw ApiException.Conflit("L'intervention est terminée : le compte rendu ne peut plus être ajouté.");
            var contenu = await NettoyerCompteRenduAsync(db, interventionId, input, ct);
            var maintenant = DateTime.UtcNow;
            var parametres = new List<(string, object?)>(ParametresCompteRendu(contenu))
            {
                ("@intervention", interventionId), ("@statut", CompteRenduStatuts.Brouillon), ("@redacteur_id", ctx.Auteur.Id),
                ("@redacteur", ctx.Auteur.Nom), ("@maintenant", maintenant)
            };
            id = await db.InsertAsync(
                """
                INSERT INTO pg_comptes_rendus (intervention_id, statut, redacteur_id, redacteur, cree_le, maj_le, version)
                VALUES (@intervention, @statut, @redacteur_id, @redacteur, @maintenant, @maintenant, 1)
                """, ct, parametres.ToArray());
            parametres.Add(("@id", id));
            await db.ExecuteAsync($"UPDATE pg_comptes_rendus SET {AffectationsCompteRendu} WHERE id = @id", ct, parametres.ToArray());
            var reference = OperationsRegles.Numero(OperationsRegles.PrefixeCompteRendu, DateTimeOffset.UtcNow, id);
            await Historique.EcrireAsync(db, ctx.Auteur, OperationsRegles.ObjetCompteRendu, reference, "Création", null, null,
                $"{intervention.Numero} — {contenu.Indicatif ?? "équipage non précisé"}", null, ct);
            await ChronoAsync(db, interventionId, "Compte rendu", $"Compte rendu rédigé ({contenu.Indicatif ?? "équipage non précisé"}), en attente de validation.",
                ctx.Auteur, contenu.EquipageId, ct);
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        await notifier.PegaseAsync("intervention:" + interventionId);
        return (await LireCompteRenduAsync(db, id, false, ct))!;
    }

    public async Task<CompteRenduDto> ModifierCompteRenduAsync(AgentContext ctx, long id, CompteRenduInput input, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        CompteRenduDto actuel;
        try
        {
            actuel = await LireCompteRenduAsync(db, id, true, ct) ?? throw ApiException.Introuvable("Compte rendu introuvable.");
            if (actuel.Statut != CompteRenduStatuts.Brouillon)
                throw ApiException.Conflit("Le compte rendu est validé : il ne peut plus être modifié.");
            OpSql.VerifierVersion(input.Version, actuel.Version);
            var contenu = await NettoyerCompteRenduAsync(db, actuel.InterventionId, input, ct);
            var parametres = new List<(string, object?)>(ParametresCompteRendu(contenu)) { ("@maintenant", DateTime.UtcNow), ("@id", id) };
            await db.ExecuteAsync($"UPDATE pg_comptes_rendus SET {AffectationsCompteRendu}, maj_le = @maintenant, version = version + 1 WHERE id = @id",
                ct, parametres.ToArray());
            var avant = new ContenuCompteRendu(actuel.EquipageId, actuel.Indicatif, actuel.ChefDeBord, actuel.ArriveeLe, actuel.DepartLe, actuel.FinLe,
                actuel.Resultat, actuel.Faits, actuel.Mesures, actuel.PersonnesControlees, actuel.Interpellations, actuel.Victimes, actuel.Vehicules,
                actuel.Secours, actuel.Transport, actuel.Destination, actuel.Documents, actuel.SuiteJudiciaire, actuel.Observations);
            await OpSql.HistoriserDifferencesAsync(db, ctx.Auteur, OperationsRegles.ObjetCompteRendu,
                OperationsRegles.Numero(OperationsRegles.PrefixeCompteRendu, actuel.CreeLe, id), string.Empty, avant, contenu, null, ct);
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        await notifier.PegaseAsync("intervention:" + actuel.InterventionId);
        return (await LireCompteRenduAsync(db, id, false, ct))!;
    }

    /// <summary>Validation (7.3) : compte rendu figé, références et statuts des moyens actualisés.</summary>
    public async Task<CompteRenduDto> ValiderCompteRenduAsync(AgentContext ctx, long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        CompteRenduDto cr;
        try
        {
            cr = await LireCompteRenduAsync(db, id, true, ct) ?? throw ApiException.Introuvable("Compte rendu introuvable.");
            if (cr.Statut != CompteRenduStatuts.Brouillon)
                throw ApiException.Conflit("Le compte rendu est déjà validé.");
            if (cr.Resultat is null || string.IsNullOrWhiteSpace(cr.Faits))
                throw ApiException.Saisie("Le résultat et les faits constatés sont obligatoires pour valider le compte rendu.");
            var intervention = await LireInterventionBaseAsync(db, cr.InterventionId, true, ct) ?? throw ApiException.Introuvable("Intervention introuvable.");
            var maintenant = DateTime.UtcNow;
            await db.ExecuteAsync(
                """
                UPDATE pg_comptes_rendus SET statut = @valide, valide_par_id = @agent_id, valide_par = @agent, valide_le = @maintenant,
                       maj_le = @maintenant, version = version + 1
                WHERE id = @id
                """, ct, ("@valide", CompteRenduStatuts.Valide), ("@agent_id", ctx.Auteur.Id), ("@agent", ctx.Auteur.Nom), ("@maintenant", maintenant), ("@id", id));
            if (cr.SuiteJudiciaire && !intervention.SuiteJudiciaire)
                await db.ExecuteAsync("UPDATE pg_interventions SET suite_judiciaire = 1, maj_le = @maintenant, version = version + 1 WHERE id = @id", ct,
                    ("@maintenant", maintenant), ("@id", intervention.Id));
            var reference = OperationsRegles.Numero(OperationsRegles.PrefixeCompteRendu, cr.CreeLe, id);
            await Historique.EcrireAsync(db, ctx.Auteur, OperationsRegles.ObjetCompteRendu, reference, "Validation", "statut",
                CompteRenduStatuts.Brouillon, CompteRenduStatuts.Valide, null, ct);
            await ChronoAsync(db, intervention.Id, "Compte rendu",
                $"Compte rendu {cr.Indicatif ?? string.Empty} validé : {cr.Resultat}." + (cr.SuiteJudiciaire ? " Suite judiciaire nécessaire." : string.Empty),
                ctx.Auteur, cr.EquipageId, ct);

            // Actualisation des moyens : l'équipage du compte rendu encore engagé termine sa mission.
            if (cr.EquipageId is { } equipageId)
            {
                var engagement = (await LireEngagementsAsync(db, intervention.Id, ct)).FirstOrDefault(e => e.EquipageId == equipageId && e.Actif);
                if (engagement is not null)
                {
                    await db.ExecuteAsync(
                        "UPDATE pg_engagements SET actif = 0, statut = @fin, termine_le = @maintenant, maj_le = @maintenant WHERE id = @id", ct,
                        ("@fin", EquipageStatuts.MissionTerminee), ("@maintenant", maintenant), ("@id", engagement.Id));
                    await db.ExecuteAsync(
                        "UPDATE mc_equipages SET statut = @fin, intervention_id = NULL, maj_le = @maintenant WHERE id = @equipage AND intervention_id = @intervention",
                        ct, ("@fin", EquipageStatuts.MissionTerminee), ("@maintenant", maintenant), ("@equipage", equipageId), ("@intervention", intervention.Id));
                    await StatutHistoAsync(db, "equipage", equipageId, intervention.Id, engagement.StatutEquipage, EquipageStatuts.MissionTerminee,
                        "Compte rendu validé", ctx.Auteur, ct);
                    await new EvenementService(JournalTypes.RetourMission, $"{engagement.Indicatif} : mission terminée — {intervention.Numero}")
                    {
                        Details = "Compte rendu validé : " + cr.Resultat,
                        Source = SourcePegase,
                        EquipageId = equipageId,
                        InterventionId = intervention.Id,
                        InterventionNumero = intervention.Numero
                    }.EcrireAsync(db, ctx.Auteur, ct);
                    await AppliquerStatutDeduitAsync(db, ctx.Auteur, intervention, ct);
                }
            }
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        await notifier.PegaseAsync("intervention:" + cr.InterventionId);
        await notifier.MainCouranteAsync("journal");
        return (await LireCompteRenduAsync(db, id, false, ct))!;
    }

    // =====================================================================================
    // TN du jour (8.2)
    // =====================================================================================

    private static TnDto LireTn(SqlRow r) => new(r.Long("id"), r.Long("agent_id"), r.Text("agent"), r.NullableText("service"), r.Date("debut"),
        r.NullableDate("fin"), r.Text("statut"));

    private static Task<TnDto?> TnActuelAsync(SqlSession db, CancellationToken ct)
        => db.FirstOrDefaultAsync("SELECT * FROM pg_tn WHERE statut = @encours ORDER BY debut DESC, id DESC LIMIT 1", LireTn, ct, ("@encours", TnStatuts.EnCours));

    private static Task<string?> DiscordAgentAsync(SqlSession db, long agentId, CancellationToken ct)
        => db.FirstOrDefaultAsync("SELECT discord_id FROM rh_agents WHERE id = @id", r => r.Text("discord_id"), ct, ("@id", agentId));

    private static Task<List<string>> DiscordMembresAsync(SqlSession db, long equipageId, CancellationToken ct)
        => db.QueryAsync(
            "SELECT a.discord_id FROM mc_equipage_membres m JOIN rh_agents a ON a.id = m.agent_id WHERE m.equipage_id = @id",
            r => r.Text("discord_id"), ct, ("@id", equipageId));

    public async Task<List<TnDto>> ListerTnAsync(int limite, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await db.QueryAsync("SELECT * FROM pg_tn ORDER BY debut DESC, id DESC LIMIT @limite", LireTn, ct, ("@limite", limite));
    }

    /// <summary>L'agent connecté prend la permanence TN ; la permanence en cours est close.</summary>
    public Task<TnDto> PrendreTnAsync(AgentContext ctx, TnInput input, CancellationToken ct)
        => OpSql.ReessayerAsync(() => PrendreTnEssaiAsync(ctx, input, ct));

    private async Task<TnDto> PrendreTnEssaiAsync(AgentContext ctx, TnInput input, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        long id;
        try
        {
            var maintenant = DateTime.UtcNow;
            var actuel = await db.FirstOrDefaultAsync("SELECT * FROM pg_tn WHERE statut = @encours ORDER BY debut DESC LIMIT 1 FOR UPDATE", LireTn, ct,
                ("@encours", TnStatuts.EnCours));
            if (actuel is not null && actuel.AgentId == ctx.Agent.Id)
                throw ApiException.Conflit("Tu assures déjà la permanence TN.");
            await db.ExecuteAsync("UPDATE pg_tn SET statut = @terminee, fin = @maintenant WHERE statut = @encours", ct,
                ("@terminee", TnStatuts.Terminee), ("@maintenant", maintenant), ("@encours", TnStatuts.EnCours));
            var agent = $"{ctx.Agent.Nom} {ctx.Agent.Prenom}";
            id = await db.InsertAsync(
                "INSERT INTO pg_tn (agent_id, agent, service, debut, fin, statut) VALUES (@agent_id, @agent, @service, @maintenant, NULL, @encours)", ct,
                ("@agent_id", ctx.Agent.Id), ("@agent", agent), ("@service", OpSql.T(input.Service, 120) ?? ctx.Agent.Service),
                ("@maintenant", maintenant), ("@encours", TnStatuts.EnCours));
            await Historique.EcrireAsync(db, ctx.Auteur, OperationsRegles.ObjetTn, "TN-" + id, "Prise de permanence", "tn", actuel?.Agent, agent, null, ct);
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        await notifier.PegaseAsync("tn");
        return (await db.FirstOrDefaultAsync("SELECT * FROM pg_tn WHERE id = @id", LireTn, ct, ("@id", id)))!;
    }

    public async Task TerminerTnAsync(AgentContext ctx, long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        try
        {
            var tn = await db.FirstOrDefaultAsync("SELECT * FROM pg_tn WHERE id = @id FOR UPDATE", LireTn, ct, ("@id", id))
                     ?? throw ApiException.Introuvable("Permanence introuvable.");
            if (tn.Statut != TnStatuts.EnCours)
                throw ApiException.Conflit("Cette permanence est déjà terminée.");
            await db.ExecuteAsync("UPDATE pg_tn SET statut = @terminee, fin = @maintenant WHERE id = @id", ct,
                ("@terminee", TnStatuts.Terminee), ("@maintenant", DateTime.UtcNow), ("@id", id));
            await Historique.EcrireAsync(db, ctx.Auteur, OperationsRegles.ObjetTn, "TN-" + id, "Fin de permanence", "statut", TnStatuts.EnCours, TnStatuts.Terminee, null, ct);
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        await notifier.PegaseAsync("tn");
    }

    // =====================================================================================
    // Tableau de bord, statistiques, historique
    // =====================================================================================

    public async Task<PegaseTableauDeBordDto> TableauDeBordAsync(CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var interventions = await ListerInterventionsAsync(db, "actives", null, null, null, 100, ct);
        var appels = await ListerAppelsAsync(db, "ouverts", null, 50, ct);
        var equipages = await MainCouranteStore.ListerEquipagesAsync(db, null, true, null, ct);
        var tn = await TnActuelAsync(db, ct);
        return new PegaseTableauDeBordDto(
            appels.Count,
            interventions.Count(i => i.Statut == InterventionStatuts.AQualifier),
            interventions.Count(i => i.Statut == InterventionStatuts.EnAttente),
            interventions.Count(i => i.Statut is InterventionStatuts.MoyensAffectes or InterventionStatuts.EnCours),
            interventions.Count(i => i.Statut == InterventionStatuts.CompteRenduAttendu),
            equipages.Count(e => EquipageStatuts.PeutEtreEngage(e.Statut)),
            equipages.Count(e => EquipageStatuts.EstEngage(e.Statut)),
            tn, interventions, appels, equipages);
    }

    public async Task<PegaseStatistiquesDto> StatistiquesAsync(DateTimeOffset? du, DateTimeOffset? au, CancellationToken ct)
    {
        var fin = au ?? DateTimeOffset.UtcNow;
        var debut = du ?? fin.AddDays(-30);
        if (debut > fin)
            throw ApiException.Saisie("La période est inversée.");
        await using var db = await saphirDb.OpenAsync(ct);
        (string, object?)[] P() => [("@du", OpSql.Utc(debut)), ("@au", OpSql.Utc(fin))];
        const string Periode = " WHERE debut_le BETWEEN @du AND @au";

        var appels = (int)await db.LongAsync("SELECT COUNT(*) FROM pg_appels WHERE recu_le BETWEEN @du AND @au", ct, P());
        var interventions = (int)await db.LongAsync("SELECT COUNT(*) FROM pg_interventions" + Periode, ct, P());
        var cloturees = (int)await db.LongAsync("SELECT COUNT(*) FROM pg_interventions" + Periode + " AND statut = @statut", ct,
            [.. P(), ("@statut", InterventionStatuts.Cloturee)]);
        var annulees = (int)await db.LongAsync("SELECT COUNT(*) FROM pg_interventions" + Periode + " AND statut = @statut", ct,
            [.. P(), ("@statut", InterventionStatuts.Annulee)]);

        async Task<double?> DelaiAsync(string colonne)
        {
            var valeur = await db.ScalarAsync(
                $"""
                SELECT AVG(TIMESTAMPDIFF(SECOND, i.debut_le, g.premier)) FROM pg_interventions i
                JOIN (SELECT intervention_id, MIN({colonne}) AS premier FROM pg_engagements WHERE {colonne} IS NOT NULL GROUP BY intervention_id) g
                  ON g.intervention_id = i.id
                WHERE i.debut_le BETWEEN @du AND @au
                """, ct, P());
            return valeur is null ? null : Math.Round(Convert.ToDouble(valeur, CultureInfo.InvariantCulture) / 60.0, 1);
        }

        Task<List<StatistiqueLigneDto>> GrouperAsync(string table, string colonne, string periode, int limite = 20)
            => db.QueryAsync(
                $"SELECT COALESCE({colonne}, 'Non précisé') AS libelle, COUNT(*) AS n FROM {table} WHERE {periode} BETWEEN @du AND @au GROUP BY libelle ORDER BY n DESC LIMIT {limite}",
                r => new StatistiqueLigneDto(r.Text("libelle"), r.Int("n")), ct, P());

        return new PegaseStatistiquesDto(debut, fin, appels, interventions, cloturees, annulees,
            await DelaiAsync("affecte_le"), await DelaiAsync("sur_place_le"),
            await GrouperAsync("pg_interventions", "nature", "debut_le"),
            await GrouperAsync("pg_interventions", "priorite", "debut_le"),
            await GrouperAsync("pg_interventions", "statut", "debut_le"),
            await GrouperAsync("pg_appels", "canal", "recu_le"),
            await GrouperAsync("pg_interventions", "commune", "debut_le", 10));
    }

    public async Task<List<HistoriqueDto>> HistoriqueAsync(string? reference, string? q, int limite, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await OpSql.HistoriqueAsync(db, OperationsRegles.ObjetsPegase, reference, q, limite, ct);
    }

    // =====================================================================================
    // Aides
    // =====================================================================================

    private static Task ChronoAsync(SqlSession db, long interventionId, string type, string texte, Auteur auteur, long? equipageId, CancellationToken ct)
        => db.ExecuteAsync(
            """
            INSERT INTO pg_chronologie (intervention_id, `date`, type, texte, auteur_id, auteur, equipage_id)
            VALUES (@intervention, @date, @type, @texte, @auteur_id, @auteur, @equipage)
            """, ct,
            ("@intervention", interventionId), ("@date", DateTime.UtcNow), ("@type", type), ("@texte", OpSql.T(texte, 4000) ?? type),
            ("@auteur_id", auteur.Id), ("@auteur", OpSql.T(auteur.Nom, 200) ?? "?"), ("@equipage", equipageId));

    private static Task StatutHistoAsync(SqlSession db, string objet, long objetId, long? interventionId, string? ancien, string nouveau, string? motif,
        Auteur auteur, CancellationToken ct)
        => db.ExecuteAsync(
            """
            INSERT INTO pg_statuts (objet, objet_id, intervention_id, ancien, nouveau, motif, `date`, auteur_id, auteur)
            VALUES (@objet, @objet_id, @intervention, @ancien, @nouveau, @motif, @date, @auteur_id, @auteur)
            """, ct,
            ("@objet", objet), ("@objet_id", objetId), ("@intervention", interventionId), ("@ancien", ancien), ("@nouveau", nouveau),
            ("@motif", OpSql.T(motif, 500)), ("@date", DateTime.UtcNow), ("@auteur_id", auteur.Id), ("@auteur", OpSql.T(auteur.Nom, 200) ?? "?"));

    private static Task TouchInterventionAsync(SqlSession db, long id, CancellationToken ct)
        => db.ExecuteAsync("UPDATE pg_interventions SET maj_le = @maintenant WHERE id = @id", ct, ("@maintenant", DateTime.UtcNow), ("@id", id));
}
