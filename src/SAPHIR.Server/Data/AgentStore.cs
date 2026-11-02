using MySqlConnector;
using Saphir.Shared.Contracts;

namespace Saphir.Server.Data;

/// <summary>Agents SAPHIR (rh_agents) : lecture, première connexion, dernière connexion.</summary>
public sealed class AgentStore(SaphirDb db)
{
    /// <summary>Colonnes lues par <see cref="Lire"/> (alias de table « a »).</summary>
    public const string Colonnes =
        "a.id, a.reference, a.discord_id, a.nom, a.prenom, a.rio, a.matricule, a.statut, a.cree_le, a.maj_le, a.derniere_connexion";

    /// <summary>Grade, service, unité et fonction arrivent en phase 3 : null pour l'instant.</summary>
    public static AgentDto Lire(SqlRow r) => new(
        r.Long("id"),
        r.Text("reference"),
        r.Text("discord_id"),
        r.Text("nom"),
        r.Text("prenom"),
        r.NullableText("rio"),
        r.NullableText("matricule"),
        null,
        null,
        null,
        null,
        r.Text("statut"),
        r.Date("cree_le"),
        r.Date("maj_le"));

    public async Task<AgentDto?> TrouverParDiscordAsync(string discordId, CancellationToken ct)
    {
        await using var session = await db.OpenAsync(ct);
        return await session.FirstOrDefaultAsync(
            $"SELECT {Colonnes} FROM rh_agents a WHERE a.discord_id = @discord", Lire, ct, ("@discord", discordId));
    }

    public static Task<AgentDto?> TrouverAsync(SqlSession session, long id, bool verrouiller, CancellationToken ct)
        => session.FirstOrDefaultAsync(
            $"SELECT {Colonnes} FROM rh_agents a WHERE a.id = @id" + (verrouiller ? " FOR UPDATE" : string.Empty),
            Lire, ct, ("@id", id));

    /// <summary>Note la connexion ; retourne la date de la connexion précédente.</summary>
    public async Task<DateTimeOffset?> NoterConnexionAsync(long agentId, CancellationToken ct)
    {
        await using var session = await db.OpenAsync(ct);
        var precedente = await session.FirstOrDefaultAsync(
            "SELECT derniere_connexion FROM rh_agents WHERE id = @id",
            r => new Box<DateTimeOffset?>(r.NullableDate("derniere_connexion")), ct, ("@id", agentId));
        await session.ExecuteAsync(
            "UPDATE rh_agents SET derniere_connexion = @maintenant WHERE id = @id", ct,
            ("@maintenant", DateTime.UtcNow), ("@id", agentId));
        return precedente?.Value;
    }

    /// <summary>
    /// Première connexion : crée l'agent (référence et matricule générés, statut « Profil à compléter »),
    /// lui donne les profils indiqués et historise la création. 409 si ce compte Discord a déjà un agent.
    /// </summary>
    public async Task<AgentDto> CreerAsync(string discordId, string nom, string prenom, IReadOnlyList<string> profils, CancellationToken ct)
    {
        const string DejaCree = "Ce compte Discord a déjà un agent SAPHIR.";
        var maintenant = DateTime.UtcNow;

        await using var session = await db.OpenAsync(ct);
        await session.BeginAsync(ct);

        var existant = await session.ScalarAsync(
            "SELECT id FROM rh_agents WHERE discord_id = @discord FOR UPDATE", ct, ("@discord", discordId));
        if (existant is not null)
            throw ApiException.Conflit(DejaCree);

        long id;
        try
        {
            id = await session.InsertAsync(
                """
                INSERT INTO rh_agents (discord_id, nom, prenom, statut, cree_le, maj_le, derniere_connexion)
                VALUES (@discord, @nom, @prenom, @statut, @maintenant, @maintenant, @maintenant)
                """, ct,
                ("@discord", discordId), ("@nom", nom), ("@prenom", prenom),
                ("@statut", AgentStatuts.ProfilACompleter), ("@maintenant", maintenant));
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            throw ApiException.Conflit(DejaCree);
        }

        var reference = SocleRegles.ReferenceAgent(id);
        var matricule = SocleRegles.Matricule(id);
        for (var tentative = 1; tentative <= 20; tentative++)
        {
            var pris = await session.LongAsync(
                "SELECT COUNT(*) FROM rh_agents WHERE matricule = @matricule", ct, ("@matricule", matricule));
            if (pris == 0)
                break;
            matricule = SocleRegles.Matricule(id, tentative);
        }

        await session.ExecuteAsync(
            "UPDATE rh_agents SET reference = @reference, matricule = @matricule WHERE id = @id", ct,
            ("@reference", reference), ("@matricule", matricule), ("@id", id));

        foreach (var code in profils.Distinct(StringComparer.Ordinal))
            await session.ExecuteAsync(
                """
                INSERT IGNORE INTO auth_agent_profils (agent_id, profil_id, attribue_le)
                SELECT @agent, p.id, @maintenant FROM auth_profils p WHERE p.code = @code
                """, ct,
                ("@agent", id), ("@maintenant", maintenant), ("@code", code));

        var auteur = new Auteur(id, $"{nom} {prenom} ({reference})");
        await Historique.EcrireAsync(session, auteur, ObjetsHistorique.Agent, reference, "Création", null, null,
            $"{nom} {prenom}, matricule {matricule}, statut « {AgentStatuts.ProfilACompleter} », profils : {SocleRegles.Liste(profils)}",
            "Première connexion", ct);

        var agent = await TrouverAsync(session, id, verrouiller: false, ct)
                    ?? throw new InvalidOperationException("Agent créé introuvable.");
        await session.CommitAsync(ct);
        return agent;
    }

    private sealed record Box<T>(T Value);
}
