namespace Saphir.Shared.Contracts;

/// <summary>
/// Compte Discord connecté, tel que la connexion le renvoie. <see cref="RoleRh"/> = rôle Discord RH présent.
/// Le rôle SAPHIR (whitelist) est obligatoire : sans lui, la connexion est refusée.
/// </summary>
public sealed record SaphirUserDto(
    string UserId,
    string DisplayName,
    string? AvatarUrl,
    bool RoleRh);

/// <summary>Réponse à POST /auth/discord/session.</summary>
public sealed record AuthSessionStartResponse(string SessionId, string AuthorizeUrl, DateTimeOffset ExpiresUtc);

/// <summary>Réponse à GET /auth/discord/status/{sessionId}. Status : pending, approved, denied, expired.</summary>
public sealed record AuthStatusResponse(string Status, string? Error, string? Token, SaphirUserDto? User);

/// <summary>Corps de POST /auth/discord/validate.</summary>
public sealed record TokenValidationRequest(string Token);

/// <summary>Réponse à POST /auth/discord/validate.</summary>
public sealed record TokenValidationResponse(bool Valid, bool Reauthenticate, string? Error, SaphirUserDto? User);

// =====================================================================================
// Agent et session SAPHIR
// =====================================================================================

public static class AgentStatuts
{
    public const string ProfilACompleter = "Profil à compléter";
    public const string Actif = "Actif";
    public const string Suspendu = "Suspendu";
    public const string Inactif = "Inactif";
    public const string Archive = "Archivé";

    public static readonly IReadOnlyList<string> All = [ProfilACompleter, Actif, Suspendu, Inactif, Archive];

    /// <summary>Un agent suspendu, inactif ou archivé ne peut plus ouvrir de session.</summary>
    public static bool PeutSeConnecter(string statut) => statut is ProfilACompleter or Actif;
}

/// <summary>Fiche agent (vue courte, partagée par tous les modules).</summary>
public sealed record AgentDto(
    long Id,
    string Reference,
    string DiscordId,
    string Nom,
    string Prenom,
    string? Rio,
    string? Matricule,
    string? Grade,
    string? Service,
    string? Unite,
    string? Fonction,
    string Statut,
    DateTimeOffset CreeLe,
    DateTimeOffset MajLe)
{
    /// <summary>« NOM Prénom », éventuellement précédé du grade abrégé.</summary>
    public string NomComplet => $"{Nom} {Prenom}";
}

/// <summary>Réponse à GET /api/session : qui est connecté et ce qu'il peut faire.</summary>
public sealed record SessionInfoDto(
    AgentDto? Agent,
    bool PremiereConnexion,
    bool RoleRh,
    IReadOnlyList<string> Profils,
    IReadOnlyList<string> Permissions,
    string Version,
    string? Refus);

/// <summary>Corps de POST /api/session/premiere-connexion.</summary>
public sealed record PremiereConnexionInput(string Nom, string Prenom);

/// <summary>Mise en forme imposée par le cahier des charges : NOM en majuscules, Prénom avec une capitale.</summary>
public static class NomsFormat
{
    public static string Nom(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>« jean-pierre » → « Jean-Pierre » ; espaces et traits d'union conservés.</summary>
    public static string Prenom(string? value)
    {
        var text = (value ?? string.Empty).Trim().ToLowerInvariant();
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (i == 0 || chars[i - 1] is ' ' or '-' or '\'')
                chars[i] = char.ToUpperInvariant(chars[i]);
        return new string(chars);
    }

    public static bool EstValide(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Trim().Length is >= 2 and <= 60 &&
           value.Trim().All(c => char.IsLetter(c) || c is ' ' or '-' or '\'');
}

// =====================================================================================
// Administration
// =====================================================================================

public sealed record ProfilDto(
    long Id,
    string Code,
    string Nom,
    string Description,
    IReadOnlyList<string> Permissions,
    bool Systeme,
    int NombreAgents);

public sealed record ProfilInput(string Nom, string Description, IReadOnlyList<string> Permissions);

/// <summary>Ligne de la liste des comptes (Administration).</summary>
public sealed record CompteDto(
    AgentDto Agent,
    IReadOnlyList<string> Profils,
    DateTimeOffset? DerniereConnexion);

/// <summary>Corps de PUT /api/admin/comptes/{id}/statut : nouveau statut + motif (historisé).</summary>
public sealed record StatutAgentInput(string Statut, string? Motif);

/// <summary>Corps de PUT /api/admin/comptes/{id}/profils : codes des profils + motif (historisé).</summary>
public sealed record ProfilsAgentInput(IReadOnlyList<string> Profils, string? Motif);

public sealed record ParametreDto(string Cle, string Valeur, string Libelle, string? Aide, bool Modifiable);

public sealed record ParametreInput(string Valeur);

/// <summary>Paramètres centraux.</summary>
public static class ParametresCles
{
    public const string Version = "version";
    public const string RioFormat = "rh.rio.format";
    public const string RioExemple = "rh.rio.exemple";
    public const string NomOrganisation = "organisation.nom";
}

/// <summary>Ligne d'historique : toute modification d'un objet, avec son auteur et son motif.</summary>
public sealed record HistoriqueDto(
    long Id,
    DateTimeOffset Date,
    string Auteur,
    string Objet,
    string Reference,
    string Action,
    string? Champ,
    string? Ancienne,
    string? Nouvelle,
    string? Motif);

/// <summary>Journal des accès : connexions, refus, consultations sensibles.</summary>
public sealed record JournalAccesDto(
    long Id,
    DateTimeOffset Date,
    string Agent,
    string Action,
    string Cible,
    string Resultat);

/// <summary>Page de résultats.</summary>
public sealed record PageDto<T>(IReadOnlyList<T> Items, int Total);
