using Saphir.Server.Data;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Rh;

namespace Saphir.Server.Modules.Rh;

/// <summary>Aides SQL communes au module RH (dates, requêtes de lecture partagées).</summary>
internal static class RhSql
{
    /// <summary>Agent avec ses libellés de grade, service, unité et fonction (alias « a »).</summary>
    public const string SelectAgent = """
        SELECT a.id, a.reference, a.discord_id, a.nom, a.prenom, a.rio, a.matricule, a.statut, a.cree_le, a.maj_le,
               a.grade_id, g.libelle AS grade, g.abrege AS grade_abrege,
               a.service_id, s.libelle AS service,
               a.unite_id, u.libelle AS unite,
               a.fonction_id, f.libelle AS fonction,
               COALESCE(a.date_integration, DATE(a.cree_le)) AS date_integration, a.grade_effet, a.derniere_connexion
        FROM rh_agents a
        LEFT JOIN rh_grades g ON g.id = a.grade_id
        LEFT JOIN rh_services s ON s.id = a.service_id
        LEFT JOIN rh_unites u ON u.id = a.unite_id
        LEFT JOIN rh_fonctions f ON f.id = a.fonction_id
        """;

    public static RhAgentLigneDto LireAgent(SqlRow r) => new(
        r.Long("id"),
        r.Text("reference"),
        r.Text("discord_id"),
        r.Text("nom"),
        r.Text("prenom"),
        r.NullableText("rio"),
        r.NullableText("matricule"),
        r.NullableLong("grade_id"),
        r.NullableText("grade"),
        r.NullableText("grade_abrege"),
        r.NullableLong("service_id"),
        r.NullableText("service"),
        r.NullableLong("unite_id"),
        r.NullableText("unite"),
        r.NullableLong("fonction_id"),
        r.NullableText("fonction"),
        r.Text("statut"),
        r.Date("cree_le"),
        r.Date("maj_le"));

    /// <summary>Colonne DATE → DateOnly.</summary>
    public static DateOnly? Jour(SqlRow r, string colonne) => r.Value(colonne) switch
    {
        DateTime d => DateOnly.FromDateTime(d),
        DateOnly d => d,
        _ => null
    };

    /// <summary>DateOnly → paramètre DATE.</summary>
    public static object? P(DateOnly? jour) => jour?.ToDateTime(TimeOnly.MinValue);

    /// <summary>Date du jour à Paris (repli : UTC).</summary>
    public static DateOnly Aujourdhui()
    {
        foreach (var id in new[] { "Europe/Paris", "Romance Standard Time" })
        {
            try
            {
                return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, id));
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }
        return DateOnly.FromDateTime(DateTime.UtcNow);
    }

    public static HistoriqueDto LireHistorique(SqlRow r) => new(
        r.Long("id"),
        r.Date("date"),
        r.Text("auteur"),
        r.Text("objet"),
        r.Text("reference"),
        r.Text("action"),
        r.NullableText("champ"),
        r.NullableText("ancienne"),
        r.NullableText("nouvelle"),
        r.NullableText("motif"));

    /// <summary>Historique RH : agents (objet « agent ») et objets rh-*, filtres facultatifs.</summary>
    public static Task<List<HistoriqueDto>> HistoriqueRhAsync(SqlSession db, string? objet, string? reference, int limite, CancellationToken ct)
    {
        var parametres = new List<(string Name, object? Value)> { ("@limite", limite) };
        var objets = new List<string> { ObjetsHistorique.Agent };
        objets.AddRange(ObjetsRh.All);
        string filtreObjet;
        if (!string.IsNullOrWhiteSpace(objet) && objets.Contains(objet.Trim()))
        {
            filtreObjet = "objet = @objet";
            parametres.Add(("@objet", objet.Trim()));
        }
        else
        {
            var noms = objets.Select((_, i) => "@o" + i).ToList();
            filtreObjet = $"objet IN ({string.Join(", ", noms)})";
            for (var i = 0; i < objets.Count; i++)
                parametres.Add(("@o" + i, objets[i]));
        }

        var filtreReference = string.Empty;
        if (!string.IsNullOrWhiteSpace(reference))
        {
            filtreReference = " AND reference = @reference";
            parametres.Add(("@reference", reference.Trim()));
        }

        return db.QueryAsync(
            $"""
            SELECT id, `date`, auteur, objet, reference, `action`, champ, ancienne, nouvelle, motif
            FROM sys_historique
            WHERE {filtreObjet}{filtreReference}
            ORDER BY `date` DESC, id DESC
            LIMIT @limite
            """, LireHistorique, ct, parametres.ToArray());
    }

    /// <summary>Motif obligatoire, nettoyé (500 caractères au plus).</summary>
    public static string MotifObligatoire(string? motif, string message = "Le motif est obligatoire.")
        => SocleRegles.Tronquer(motif, 500) ?? throw ApiException.Saisie(message);
}
