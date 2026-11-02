using Saphir.Shared.Contracts;

namespace Saphir.Server.Data;

/// <summary>Auteur d'une modification : agent SAPHIR (identifiant + « NOM Prénom (AG-…) »).</summary>
public sealed record Auteur(long? Id, string Nom);

/// <summary>Objets suivis dans l'historique.</summary>
public static class ObjetsHistorique
{
    public const string Agent = "agent";
    public const string Profil = "profil";
    public const string Parametre = "parametre";
}

/// <summary>Historique des modifications (sys_historique) : écrit dans la transaction de la modification.</summary>
public static class Historique
{
    public static Task EcrireAsync(
        SqlSession db,
        Auteur auteur,
        string objet,
        string reference,
        string action,
        string? champ,
        string? ancienne,
        string? nouvelle,
        string? motif,
        CancellationToken ct)
        => db.ExecuteAsync(
            """
            INSERT INTO sys_historique (`date`, auteur_id, auteur, objet, reference, `action`, champ, ancienne, nouvelle, motif)
            VALUES (@date, @auteur_id, @auteur, @objet, @reference, @action, @champ, @ancienne, @nouvelle, @motif)
            """, ct,
            ("@date", DateTime.UtcNow),
            ("@auteur_id", auteur.Id),
            ("@auteur", SocleRegles.Tronquer(auteur.Nom, 200) ?? "?"),
            ("@objet", objet),
            ("@reference", SocleRegles.Tronquer(reference, 100) ?? "?"),
            ("@action", action),
            ("@champ", champ),
            ("@ancienne", ancienne),
            ("@nouvelle", nouvelle),
            ("@motif", SocleRegles.Tronquer(motif, 500)));

    public static async Task<List<HistoriqueDto>> ListerAsync(SqlSession db, string? objet, string? reference, int limite, CancellationToken ct)
    {
        var sql = new System.Text.StringBuilder(
            "SELECT id, `date`, auteur, objet, reference, `action`, champ, ancienne, nouvelle, motif FROM sys_historique WHERE 1 = 1");
        var parametres = new List<(string Name, object? Value)>();
        if (!string.IsNullOrWhiteSpace(objet))
        {
            sql.Append(" AND objet = @objet");
            parametres.Add(("@objet", objet.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(reference))
        {
            sql.Append(" AND reference = @reference");
            parametres.Add(("@reference", reference.Trim()));
        }
        sql.Append(" ORDER BY `date` DESC, id DESC LIMIT @limite");
        parametres.Add(("@limite", limite));

        return await db.QueryAsync(sql.ToString(), r => new HistoriqueDto(
            r.Long("id"),
            r.Date("date"),
            r.Text("auteur"),
            r.Text("objet"),
            r.Text("reference"),
            r.Text("action"),
            r.NullableText("champ"),
            r.NullableText("ancienne"),
            r.NullableText("nouvelle"),
            r.NullableText("motif")), ct, parametres.ToArray());
    }
}

/// <summary>
/// Journal des accès (sys_journal_acces) : connexions, refus. Écriture « au mieux » sur sa propre connexion :
/// une panne du journal ne bloque jamais l'agent.
/// </summary>
public sealed class JournalAcces(SaphirDb db, ILogger<JournalAcces> logger)
{
    public async Task EcrireAsync(long? agentId, string agent, string action, string cible, string resultat)
    {
        if (!db.IsReady)
            return;
        try
        {
            await using var session = await db.OpenAsync(CancellationToken.None);
            await session.ExecuteAsync(
                """
                INSERT INTO sys_journal_acces (`date`, agent_id, agent, `action`, cible, resultat)
                VALUES (@date, @agent_id, @agent, @action, @cible, @resultat)
                """, CancellationToken.None,
                ("@date", DateTime.UtcNow),
                ("@agent_id", agentId),
                ("@agent", SocleRegles.Tronquer(agent, 200) ?? "?"),
                ("@action", SocleRegles.Tronquer(action, 100) ?? "?"),
                ("@cible", SocleRegles.Tronquer(cible, 200) ?? "?"),
                ("@resultat", SocleRegles.Tronquer(resultat, 100) ?? "?"));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Journal des accès : écriture impossible");
        }
    }

    public async Task<List<JournalAccesDto>> ListerAsync(int limite, CancellationToken ct)
    {
        await using var session = await db.OpenAsync(ct);
        return await session.QueryAsync(
            "SELECT id, `date`, agent, `action`, cible, resultat FROM sys_journal_acces ORDER BY `date` DESC, id DESC LIMIT @limite",
            r => new JournalAccesDto(
                r.Long("id"),
                r.Date("date"),
                r.Text("agent"),
                r.Text("action"),
                r.Text("cible"),
                r.Text("resultat")), ct, ("@limite", limite));
    }
}
