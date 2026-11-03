namespace Saphir.Shared.Contracts;

/// <summary>
/// Message échangé sur la connexion temps réel (/ws), en JSON.
/// Premier message du client : { "type": "auth", "token": "…" }.
/// Le serveur répond « welcome » (ou « denied »), puis pousse ses messages.
/// </summary>
public sealed record RealtimeMessage(
    string Type,
    string? Token = null,
    string? Title = null,
    string? Text = null,
    string? AppId = null);

public static class RealtimeTypes
{
    public const string Auth = "auth";
    public const string Welcome = "welcome";
    public const string Denied = "denied";
    public const string Ping = "ping";
    public const string Pong = "pong";

    /// <summary>Notification à afficher (bulle de la zone de notification, bandeau sur téléphone).</summary>
    public const string Notify = "notify";

    /// <summary>Profils, permissions ou fiche de l'agent modifiés : le poste recharge sa session (GET /api/session).</summary>
    public const string SessionChanged = "session-changed";

    /// <summary>Paramètres centraux modifiés (version…).</summary>
    public const string ParametresChanged = "parametres-changed";

    /// <summary>Nouvelle version des postes disponible sur le serveur (GET /api/updates/latest).</summary>
    public const string UpdateAvailable = "update-available";
}
