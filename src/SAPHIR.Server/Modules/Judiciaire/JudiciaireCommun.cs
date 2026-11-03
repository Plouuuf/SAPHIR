using Saphir.Server.Data;
using Saphir.Server.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Judiciaire;

namespace Saphir.Server.Modules.Judiciaire;

/// <summary>Listes de permissions utilisées par les routes du module.</summary>
public static class JudiciaireAcces
{
    public static readonly string[] Cheops =
    [
        Permissions.CheopsTaj, Permissions.CheopsFpr, Permissions.CheopsFprInscrire, Permissions.CheopsVehicules,
        Permissions.CheopsSnpc, Permissions.CheopsScientifique, Permissions.CheopsFijais, Permissions.CheopsAnadoc
    ];

    /// <summary>Routes partagées personnes / véhicules (Referentiels.cs) : n'importe laquelle de ces permissions.</summary>
    public static readonly string[] Partage =
    [
        .. Cheops, Permissions.LrppnConsulter, Permissions.LrppnRediger, Permissions.PegaseAppelCreer,
        Permissions.PegaseConsulter, Permissions.NeoUtiliser, Permissions.MrzUtiliser, Permissions.PveRediger
    ];

    /// <summary>Fiche d'identité complète : CHEOPS ou LRPPN.</summary>
    public static readonly string[] FichePersonne = [.. Cheops, Permissions.LrppnConsulter, Permissions.LrppnRediger];

    /// <summary>Mise à jour des référentiels fictifs (identités, véhicules, permis, assurances).</summary>
    public static readonly string[] EcritureReferentiel = [Permissions.LrppnRediger, Permissions.CheopsFprInscrire];

    /// <summary>Casier de simulation : lecture.</summary>
    public static readonly string[] CasierLecture = [Permissions.CheopsFijais, Permissions.LrppnValider];

    public static readonly string[] LrppnLecture = [Permissions.LrppnConsulter, Permissions.LrppnRediger, Permissions.LrppnValider];
}

/// <summary>Objets suivis dans l'historique (sys_historique.objet).</summary>
public static class ObjetsJudiciaire
{
    public const string Personne = "personne";
    public const string Vehicule = "vehicule";
    public const string Assurance = "assurance";
    public const string Taj = "taj";
    public const string Fpr = "fpr";
    public const string Foves = "foves";
    public const string Permis = "permis";
    public const string Scientifique = "scientifique";
    public const string Fijais = "fijais";
    public const string Decision = "decision";
    public const string Procedure = "procedure";
    public const string Acte = "acte";
    public const string Piece = "piece";
    public const string Modele = "modele";
}

/// <summary>Petits outils de lecture/écriture SQL partagés par les magasins du module.</summary>
public static class Sqlj
{
    public static DateOnly? Jour(SqlRow r, string colonne) => r.Value(colonne) switch
    {
        DateTime d => DateOnly.FromDateTime(d),
        DateOnly d => d,
        _ => null
    };

    public static object? Jour(DateOnly? jour) => jour?.ToDateTime(TimeOnly.MinValue);

    public static object? Utc(DateTimeOffset? date) => date?.UtcDateTime;

    public static string Like(string terme) => "%" + SocleRegles.EchapperLike(terme.Trim()) + "%";

    /// <summary>« @i0, @i1… » et les paramètres correspondants (liste non vide).</summary>
    public static (string Liste, (string Name, object? Value)[] Parametres) In(string prefixe, IEnumerable<long> ids)
    {
        var liste = ids.Distinct().ToList();
        if (liste.Count == 0)
            liste.Add(-1);
        var noms = liste.Select((_, i) => "@" + prefixe + i).ToList();
        return (string.Join(", ", noms), liste.Select((id, i) => (noms[i], (object?)id)).ToArray());
    }

    public const string ColonnesPersonne = "p.id, p.reference, p.nom, p.prenom, p.date_naissance, p.lieu_naissance, p.sexe";

    public static PersonneResumeDto LirePersonne(SqlRow r, IReadOnlyList<string>? alertes = null) => new(
        r.Long("id"),
        r.Text("reference"),
        r.Text("nom"),
        r.Text("prenom"),
        Jour(r, "date_naissance"),
        r.NullableText("lieu_naissance"),
        r.NullableText("sexe"),
        alertes ?? Array.Empty<string>());

    /// <summary>Historique d'un objet (le plus récent d'abord).</summary>
    public static Task<List<HistoriqueDto>> HistoriqueAsync(SqlSession db, string objet, string reference, CancellationToken ct)
        => Historique.ListerAsync(db, objet, reference, 200, ct);

    /// <summary>Historise chaque champ modifié (ancienne → nouvelle valeur).</summary>
    public static async Task HistoriserChangementsAsync(SqlSession db, Auteur auteur, string objet, string reference,
        IEnumerable<(string Champ, string? Ancienne, string? Nouvelle)> changements, string? motif, CancellationToken ct)
    {
        foreach (var (champ, ancienne, nouvelle) in changements)
            if (!string.Equals(ancienne ?? string.Empty, nouvelle ?? string.Empty, StringComparison.Ordinal))
                await Historique.EcrireAsync(db, auteur, objet, reference, "Modification", champ, ancienne, nouvelle, motif, ct);
    }

    public static string? Texte(DateOnly? jour) => jour?.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Codes d'alerte (selon les droits de l'agent) et bandeaux rouges FPR / FOVeS.</summary>
public static class AlertesJudiciaires
{
    public static async Task<Dictionary<long, List<string>>> PersonnesAsync(SqlSession db, IReadOnlyCollection<long> ids, AgentDroits droits, CancellationToken ct)
    {
        var resultat = ids.Distinct().ToDictionary(id => id, _ => new List<string>());
        if (resultat.Count == 0)
            return resultat;
        var (liste, parametres) = Sqlj.In("i", resultat.Keys);

        async Task AjouterAsync(string sql, Func<SqlRow, string> code)
        {
            var lignes = await db.QueryAsync(sql, r => (Id: r.Long("personne_id"), Code: code(r)), ct, parametres);
            foreach (var (id, c) in lignes)
                if (resultat.TryGetValue(id, out var codes) && !codes.Contains(c))
                    codes.Add(c);
        }

        if (droits.Possede(Permissions.CheopsFpr))
            await AjouterAsync($"SELECT DISTINCT personne_id FROM ch_fpr WHERE statut = 'Active' AND personne_id IN ({liste})", _ => FichiersCheops.Fpr);
        if (droits.Possede(Permissions.CheopsTaj))
            await AjouterAsync($"SELECT DISTINCT personne_id FROM ch_taj WHERE personne_id IN ({liste})", _ => FichiersCheops.Taj);
        if (droits.Possede(Permissions.CheopsFijais))
            await AjouterAsync($"SELECT DISTINCT personne_id, fichier FROM ch_fijais WHERE statut <> 'Effacé' AND personne_id IN ({liste})", r => r.Text("fichier"));
        if (droits.Possede(Permissions.CheopsScientifique))
            await AjouterAsync($"SELECT DISTINCT personne_id, fichier FROM ch_scientifique WHERE personne_id IN ({liste})", r => r.Text("fichier"));
        if (droits.Possede(Permissions.CheopsSnpc))
            await AjouterAsync(
                $"SELECT DISTINCT personne_id FROM ch_permis WHERE situation IN ('Suspendu', 'Annulé', 'Invalidé (solde nul)') AND personne_id IN ({liste})",
                _ => FichiersCheops.Snpc);
        return resultat;
    }

    public static async Task<List<string>> PersonneAsync(SqlSession db, long id, AgentDroits droits, CancellationToken ct)
        => (await PersonnesAsync(db, [id], droits, ct))[id];

    public static async Task<Dictionary<long, List<string>>> VehiculesAsync(SqlSession db, IReadOnlyCollection<long> ids, AgentDroits droits, CancellationToken ct)
    {
        var resultat = ids.Distinct().ToDictionary(id => id, _ => new List<string>());
        if (resultat.Count == 0)
            return resultat;
        var (liste, parametres) = Sqlj.In("v", resultat.Keys);

        async Task AjouterAsync(string sql, string code)
        {
            var lignes = await db.QueryAsync(sql, r => r.Long("id"), ct, parametres);
            foreach (var id in lignes)
                if (resultat.TryGetValue(id, out var codes) && !codes.Contains(code))
                    codes.Add(code);
        }

        if (droits.Possede(Permissions.CheopsVehicules))
        {
            await AjouterAsync(
                $"""
                SELECT DISTINCT v.id FROM veh_vehicules v
                JOIN ch_foves f ON (f.vehicule_id = v.id OR f.identifiant_norm = v.immatriculation) AND f.statut = 'Signalement actif'
                WHERE v.id IN ({liste})
                """, FichiersCheops.Foves);
            await AjouterAsync($"SELECT id FROM veh_vehicules WHERE situation <> 'Normale' AND id IN ({liste})", FichiersCheops.Siv);
            await AjouterAsync(
                $"""
                SELECT v.id FROM veh_vehicules v
                WHERE v.id IN ({liste}) AND NOT EXISTS (
                    SELECT 1 FROM veh_assurances a WHERE a.vehicule_id = v.id AND a.statut = 'Valide'
                    AND a.debut <= UTC_DATE() AND (a.fin IS NULL OR a.fin >= UTC_DATE()))
                """, FichiersCheops.Fva);
        }
        if (droits.Possede(Permissions.CheopsFpr))
            await AjouterAsync(
                $"""
                SELECT DISTINCT v.id FROM veh_vehicules v
                JOIN ch_fpr f ON f.personne_id = v.titulaire_id AND f.statut = 'Active'
                WHERE v.id IN ({liste})
                """, FichiersCheops.Fpr);
        return resultat;
    }

    /// <summary>Bandeaux rouges d'une personne : fiches FPR actives (si l'agent consulte le FPR).</summary>
    public static async Task<List<AlerteBandeauDto>> BandeauxPersonneAsync(SqlSession db, long personneId, AgentDroits droits, CancellationToken ct)
    {
        if (!droits.Possede(Permissions.CheopsFpr))
            return [];
        return await db.QueryAsync(
            "SELECT numero, categorie, motif, conduite, dangereux, arme FROM ch_fpr WHERE personne_id = @id AND statut = 'Active' ORDER BY cree_le DESC",
            r => new AlerteBandeauDto(
                FichiersCheops.Fpr,
                r.Text("numero"),
                "PERSONNE INSCRITE AU FPR — " + r.Text("categorie") + " — " + r.Text("motif") +
                (r.Bool("dangereux") ? " — PERSONNE DANGEREUSE" : string.Empty) + (r.Bool("arme") ? " — SUSCEPTIBLE D'ÊTRE ARMÉE" : string.Empty),
                r.Text("conduite")), ct, ("@id", personneId));
    }

    /// <summary>Bandeaux rouges d'un véhicule : signalements FOVeS actifs et titulaire inscrit au FPR.</summary>
    public static async Task<List<AlerteBandeauDto>> BandeauxVehiculeAsync(SqlSession db, long vehiculeId, AgentDroits droits, CancellationToken ct)
    {
        var bandeaux = new List<AlerteBandeauDto>();
        if (droits.Possede(Permissions.CheopsVehicules))
            bandeaux.AddRange(await db.QueryAsync(
                """
                SELECT f.reference, f.nature, f.conduite, f.signale_le FROM ch_foves f
                JOIN veh_vehicules v ON v.id = @id
                WHERE (f.vehicule_id = v.id OR f.identifiant_norm = v.immatriculation) AND f.statut = 'Signalement actif'
                ORDER BY f.signale_le DESC
                """,
                r => new AlerteBandeauDto(FichiersCheops.Foves, r.Text("reference"),
                    "VÉHICULE SIGNALÉ AU FOVeS — " + r.Text("nature") + " — signalé le " + r.Date("signale_le").ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture),
                    r.NullableText("conduite") ?? "Aviser le CIC."), ct, ("@id", vehiculeId)));
        if (droits.Possede(Permissions.CheopsFpr))
            bandeaux.AddRange(await db.QueryAsync(
                """
                SELECT f.numero, f.categorie, f.conduite FROM ch_fpr f
                JOIN veh_vehicules v ON v.titulaire_id = f.personne_id
                WHERE v.id = @id AND f.statut = 'Active'
                """,
                r => new AlerteBandeauDto(FichiersCheops.Fpr, r.Text("numero"),
                    "TITULAIRE INSCRIT AU FPR — " + r.Text("categorie"), r.Text("conduite")), ct, ("@id", vehiculeId)));
        return bandeaux;
    }
}
