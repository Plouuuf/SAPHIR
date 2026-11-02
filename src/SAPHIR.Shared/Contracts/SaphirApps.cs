namespace Saphir.Shared.Contracts;

/// <summary>Applications du bureau SAPHIR.</summary>
public static class SaphirApps
{
    // Modules principaux
    public const string Cheops = "cheops";
    public const string Pegase = "pegase";
    public const string MainCourante = "main-courante";
    public const string Lrppn = "lrppn";
    public const string Rh = "rh";

    // Applications complémentaires
    public const string Neo = "neo";
    public const string Pve = "pve";
    public const string Tchap = "tchap";
    public const string Annuaire = "annuaire";
    public const string Messagerie = "messagerie";
    public const string Mrz = "mrz";
    public const string Notes = "notes";
    public const string Bodycam = "bodycam";

    // Système
    public const string Administration = "administration";
    public const string Parametres = "parametres";

    public static readonly IReadOnlyList<string> All =
    [
        Cheops, Pegase, MainCourante, Lrppn, Rh,
        Neo, Pve, Tchap, Annuaire, Messagerie, Mrz, Notes, Bodycam,
        Administration, Parametres
    ];

    /// <summary>
    /// Une application apparaît sur le bureau si l'agent a au moins une permission de son module.
    /// RH exige en plus le rôle Discord RH ; Paramètres est ouvert à tous.
    /// </summary>
    public static bool CanOpen(string appId, IReadOnlyCollection<string> permissions, bool roleRh)
    {
        if (appId == Parametres)
            return true;
        if (appId == Rh && !roleRh)
            return false;
        var module = appId switch
        {
            Administration => "admin.",
            _ => appId + "."
        };
        return permissions.Any(p => p.StartsWith(module, StringComparison.Ordinal));
    }
}
