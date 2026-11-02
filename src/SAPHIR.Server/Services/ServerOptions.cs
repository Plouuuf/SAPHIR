namespace Saphir.Server.Services;

public sealed class SaphirServerOptions
{
    /// <summary>Secret du serveur : chiffre les sessions Discord enregistrées sur disque.</summary>
    public string AccessKey { get; set; } = string.Empty;
}

public sealed class DiscordOAuthOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Adresse publique du serveur SAPHIR ; le retour Discord arrive sur {PublicBaseUrl}/auth/discord/callback.</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>Serveur Discord VDMS.</summary>
    public string GuildId { get; set; } = string.Empty;

    /// <summary>Rôle obligatoire pour ouvrir une session SAPHIR (whitelist Police). Donne le niveau « agent ».</summary>
    public string RequiredRoleId { get; set; } = string.Empty;

    /// <summary>
    /// Rôles Discord donnant un niveau supplémentaire : clé = niveau (identifiant d'application : meo-fic, pegase…,
    /// ou superviseur, administration),
    /// valeur = ID du rôle, ou plusieurs ID séparés par des virgules. Valeur vide = niveau attribué à personne.
    /// </summary>
    public Dictionary<string, string> LevelRoles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Bases de données : SAPHIR (lecture/écriture) et FiveM (lecture seule).</summary>
public sealed class DatabaseOptions
{
    /// <summary>Base SAPHIR (MariaDB) : fiches TAJ, FPR, FOVeS… Les tables sont créées au démarrage.</summary>
    public string Saphir { get; set; } = string.Empty;

    /// <summary>Base du serveur FiveM, lue en lecture seule (utilisateur MariaDB avec SELECT uniquement conseillé).</summary>
    public string FiveM { get; set; } = string.Empty;

    /// <summary>Framework FiveM : « esx », « qbcore » ou « aucun » (pas de lecture FiveM).</summary>
    public string Framework { get; set; } = "esx";
}
