using Saphir.Shared.Contracts;

namespace Saphir.Services;

/// <summary>Application du bureau SAPHIR.</summary>
public sealed record AppDefinition(string Id, string Title, string Subtitle, string IconPath);

/// <summary>Les applications du bureau, dans l'ordre des icônes (fichiers, opérations, judiciaire, moyens, auxiliaires).</summary>
public static class AppCatalog
{
    public static readonly IReadOnlyList<AppDefinition> All =
    [
        new(SaphirApps.MeoFic, "MEO-FIC", "TAJ · FPR · SNPC · FOVeS · SIV · FVA", "Icons/meo-fic.png"),
        new(SaphirApps.Scientifique, "PTS", "FAED · FNAEG · FIJAIS · FIJAIT", "Icons/scientifique.png"),
        new(SaphirApps.Pegase, "PEGASE", "Prise d'appels 17", "Icons/pegase.png"),
        new(SaphirApps.GeoOps, "GÉOOPS", "Localisation des équipages", "Icons/geoops.png"),
        new(SaphirApps.MainCourante, "Main courante", "Véhicules et équipages en service", "Icons/main-courante.png"),
        new(SaphirApps.Lrppn, "LRPPN", "Rédaction des procédures", "Icons/lrppn.png"),
        new(SaphirApps.Neo, "NEO", "Tchap · Annuaire · Messagerie · MRZ · Notes", "Icons/neo.png"),
        new(SaphirApps.Bodycam, "Video Manager", "Caméras piétons", "Icons/bodycam.png"),
        new(SaphirApps.Pve, "PVe", "Procès-verbal électronique", "Icons/pve.png"),
        new(SaphirApps.Mugshot, "Mugshot", "Photos signalétiques", "Icons/mugshot.png"),
        new(SaphirApps.GardeAVue, "Garde à vue", "Registre des gardes à vue", "Icons/garde-a-vue.png"),
        new(SaphirApps.Armurerie, "Armurerie", "Armes, munitions et stocks", "Icons/armurerie.png")
    ];

    public static AppDefinition? Find(string id) => All.FirstOrDefault(a => a.Id == id);
}
