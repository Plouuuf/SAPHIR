namespace Saphir.Server.Services;

/// <summary>Section « SAPHIR » de la configuration.</summary>
public sealed class SaphirServerOptions
{
    /// <summary>Administrateurs désignés quand la configuration n'en donne aucun.</summary>
    public static readonly IReadOnlyList<string> AdminDiscordIdsParDefaut = ["560037927695024139"];

    /// <summary>Secret du serveur : chiffre les sessions Discord enregistrées sur disque et protège les intégrations (X-Saphir-Key).</summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>
    /// Comptes Discord qui reçoivent le profil « administrateur » à leur première connexion.
    /// Vide = <see cref="AdminDiscordIdsParDefaut"/>.
    /// </summary>
    public string[] AdminDiscordIds { get; set; } = [];

    /// <summary>Liste effective des administrateurs désignés (configuration, sinon valeur par défaut).</summary>
    public IReadOnlyList<string> ObtenirAdministrateursDiscord()
    {
        var configures = (AdminDiscordIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return configures.Count > 0 ? configures : AdminDiscordIdsParDefaut;
    }
}

/// <summary>Section « Discord » de la configuration.</summary>
public sealed class DiscordOAuthOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Adresse publique du serveur SAPHIR ; le retour Discord arrive sur {PublicBaseUrl}/auth/discord/callback.</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>Serveur Discord VDMS.</summary>
    public string GuildId { get; set; } = string.Empty;

    /// <summary>Rôle obligatoire pour ouvrir une session SAPHIR (whitelist SAPHIR).</summary>
    public string RequiredRoleId { get; set; } = string.Empty;

    /// <summary>Rôle Discord RH : donne <c>SaphirUserDto.RoleRh</c>, exigé par le module RH. Vide = personne.</summary>
    public string RhRoleId { get; set; } = string.Empty;
}

/// <summary>Section « Database » de la configuration.</summary>
public sealed class DatabaseOptions
{
    /// <summary>Chaîne de connexion MariaDB de la base SAPHIR. Les tables sont créées par les migrations au démarrage.</summary>
    public string Saphir { get; set; } = string.Empty;
}
