using System.Collections.Concurrent;
using Saphir.Server.Data;
using Saphir.Shared.Contracts;

namespace Saphir.Server.Services;

/// <summary>Profils et permissions effectives d'un agent (union des permissions de ses profils).</summary>
public sealed class AgentDroits(IReadOnlyList<string> profils, IReadOnlyList<string> permissions)
{
    private readonly HashSet<string> _permissions = new(permissions, StringComparer.Ordinal);

    /// <summary>Codes des profils attribués.</summary>
    public IReadOnlyList<string> Profils { get; } = profils;

    /// <summary>Codes des permissions (triés).</summary>
    public IReadOnlyList<string> Codes { get; } = permissions;

    public bool Possede(string permission) => _permissions.Contains(permission);
}

/// <summary>
/// Calcule les droits d'un agent, avec un cache court (30 s) vidé à chaque modification de profils,
/// d'attributions ou de statut (<see cref="Invalider"/>).
/// </summary>
public sealed class PermissionService(SaphirDb db)
{
    private static readonly TimeSpan DureeCache = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<long, Entree> _cache = new();
    private long _generation;

    public async Task<AgentDroits> ObtenirAsync(long agentId, CancellationToken ct)
    {
        if (_cache.TryGetValue(agentId, out var entree) && entree.Expire > DateTime.UtcNow)
            return entree.Droits;

        var generation = Interlocked.Read(ref _generation);
        await using var session = await db.OpenAsync(ct);
        var droits = await CalculerAsync(session, agentId, ct);

        _cache[agentId] = new Entree(droits, DateTime.UtcNow + DureeCache);
        // Une modification pendant le calcul rend le résultat douteux : on ne le garde pas.
        if (Interlocked.Read(ref _generation) != generation)
            _cache.TryRemove(agentId, out _);
        return droits;
    }

    /// <summary>Calcul sans cache, sur une connexion existante (éventuellement dans une transaction).</summary>
    public static async Task<AgentDroits> CalculerAsync(SqlSession session, long agentId, CancellationToken ct)
    {
        var profils = await session.QueryAsync(
            """
            SELECT p.code
            FROM auth_agent_profils ap
            JOIN auth_profils p ON p.id = ap.profil_id
            WHERE ap.agent_id = @agent
            ORDER BY p.code
            """, r => r.Text("code"), ct, ("@agent", agentId));

        var permissions = await session.QueryAsync(
            """
            SELECT DISTINCT pp.permission
            FROM auth_agent_profils ap
            JOIN auth_profil_permissions pp ON pp.profil_id = ap.profil_id
            WHERE ap.agent_id = @agent
            """, r => r.Text("permission"), ct, ("@agent", agentId));

        // Une permission retirée du catalogue ne donne plus rien, même si elle traîne en base.
        var codes = permissions
            .Where(Permissions.Exists)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        return new AgentDroits(profils, codes);
    }

    public void Invalider()
    {
        Interlocked.Increment(ref _generation);
        _cache.Clear();
    }

    private sealed record Entree(AgentDroits Droits, DateTime Expire);
}
