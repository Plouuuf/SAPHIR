namespace Saphir.Shared.Contracts;

/// <summary>
/// Niveaux d'accès SAPHIR, calculés par le serveur à partir des rôles Discord VDMS.
/// « agent » correspond au rôle obligatoire (whitelist Police). Chaque application a son propre niveau
/// (identifiant de <see cref="SaphirApps"/>) ; s'y ajoutent « superviseur » (bodycams en direct)
/// et « administration » (tout, y compris la suppression de fiches).
/// </summary>
public static class AccessLevels
{
    public const string Agent = "agent";
    public const string Superviseur = "superviseur";
    public const string Administration = "administration";

    /// <summary>Du plus élevé au plus bas, pour afficher le niveau principal.</summary>
    public static readonly IReadOnlyList<string> ByRank = [Administration, Superviseur, Agent];

    public static string Label(string level) => level switch
    {
        Administration => "Administration",
        Superviseur => "Superviseur",
        Agent => "Agent",
        _ => level
    };

    public static string HighestLabel(IReadOnlyCollection<string>? levels)
    {
        if (levels is null || levels.Count == 0)
            return Label(Agent);
        foreach (var level in ByRank)
            if (levels.Contains(level))
                return Label(level);
        return Label(Agent);
    }

    /// <summary>Le joueur peut-il ouvrir cette application ?</summary>
    public static bool CanOpen(IReadOnlyCollection<string>? levels, string appId)
        => levels is not null && (levels.Contains(Administration) || levels.Contains(appId));
}

/// <summary>Joueur connecté, tel que le serveur le renvoie à l'application.</summary>
public sealed record SaphirUserDto(
    string UserId,
    string DisplayName,
    string? AvatarUrl,
    IReadOnlyList<string> Levels);

/// <summary>Réponse à POST /auth/discord/session.</summary>
public sealed record AuthSessionStartResponse(string SessionId, string AuthorizeUrl, DateTimeOffset ExpiresUtc);

/// <summary>Réponse à GET /auth/discord/status/{sessionId}. Status : pending, approved, denied, expired.</summary>
public sealed record AuthStatusResponse(string Status, string? Error, string? Token, SaphirUserDto? User);

/// <summary>Corps de POST /auth/discord/validate.</summary>
public sealed record TokenValidationRequest(string Token);

/// <summary>Réponse à POST /auth/discord/validate.</summary>
public sealed record TokenValidationResponse(bool Valid, bool Reauthenticate, string? Error, SaphirUserDto? User);
