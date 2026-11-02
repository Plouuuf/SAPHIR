using Saphir.Shared.Contracts;

namespace Saphir.Services;

/// <summary>Application du bureau SAPHIR. <see cref="Phase"/> = phase du plan où elle arrive (null = disponible).</summary>
public sealed record AppDefinition(string Id, string Title, string Subtitle, string IconPath, bool Principale, int? Phase);

/// <summary>Applications du bureau, dans l'ordre du menu Démarrer (modules principaux, compléments, système).</summary>
public static class AppCatalog
{
    public static readonly IReadOnlyList<AppDefinition> All =
    [
        new(SaphirApps.Cheops, "CHEOPS", "Fichiers de police", "Icons/cheops.png", true, 6),
        new(SaphirApps.Pegase, "PÉGASE", "Appels et interventions (CIC)", "Icons/pegase.png", true, 5),
        new(SaphirApps.MainCourante, "MAIN COURANTE", "Vacations, équipages, mentions", "Icons/main-courante.png", true, 4),
        new(SaphirApps.Lrppn, "LRPPN", "Procédures et procès-verbaux", "Icons/lrppn.png", true, 7),
        new(SaphirApps.Rh, "RESSOURCES HUMAINES", "Personnel, carrières, documents", "Icons/rh.png", true, 3),

        new(SaphirApps.Neo, "NEO", "Terminal mobile", "Icons/neo.png", false, 8),
        new(SaphirApps.Pve, "PVe", "Procès-verbal électronique", "Icons/pve.png", false, 8),
        new(SaphirApps.Tchap, "TCHAP", "Communication", "Icons/tchap.png", false, 8),
        new(SaphirApps.Annuaire, "ANNUAIRE", "Répertoire du personnel", "Icons/annuaire.png", false, 8),
        new(SaphirApps.Messagerie, "MESSAGERIE", "Communications internes", "Icons/messagerie.png", false, 8),
        new(SaphirApps.Mrz, "MRZ", "Lecture de documents", "Icons/mrz.png", false, 8),
        new(SaphirApps.Notes, "NOTES", "Prise de notes", "Icons/notes.png", false, 8),
        new(SaphirApps.Bodycam, "BODYCAM", "Enregistrements vidéo", "Icons/bodycam.png", false, 8),

        new(SaphirApps.Administration, "ADMINISTRATION", "Comptes, profils, paramètres", "Icons/administration.png", false, null),
        new(SaphirApps.Parametres, "Centre de paramètres", "Mon compte, affichage, à propos", "Icons/parametres.png", false, null)
    ];

    public static AppDefinition? Find(string id) => All.FirstOrDefault(a => a.Id == id);
}
