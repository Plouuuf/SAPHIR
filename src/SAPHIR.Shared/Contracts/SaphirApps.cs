namespace Saphir.Shared.Contracts;

/// <summary>
/// Identifiants des applications du bureau SAPHIR. Chaque identifiant est aussi le nom du niveau d'accès
/// à configurer dans Discord:LevelRoles (un rôle Discord par application).
/// </summary>
public static class SaphirApps
{
    // Fichiers (MEO-FIC : TAJ, FPR, SNPC, FOVeS, SIV, FVA)
    public const string MeoFic = "meo-fic";

    // Police technique et scientifique (FAED, FNAEG, FIJAIS, FIJAIT)
    public const string Scientifique = "scientifique";

    // Opérations
    public const string Pegase = "pegase";
    public const string GeoOps = "geoops";
    public const string MainCourante = "main-courante";

    // Judiciaire
    public const string Lrppn = "lrppn";

    // Terminal NEO (Tchap, annuaire, messagerie, MRZ, notes)
    public const string Neo = "neo";

    // Moyens
    public const string Bodycam = "bodycam";
    public const string Pve = "pve";

    // Applications auxiliaires
    public const string Mugshot = "mugshot";
    public const string GardeAVue = "garde-a-vue";
    public const string Armurerie = "armurerie";

    public static readonly IReadOnlyList<string> All =
    [
        MeoFic, Scientifique, Pegase, GeoOps, MainCourante, Lrppn, Neo, Bodycam, Pve, Mugshot, GardeAVue, Armurerie
    ];
}
