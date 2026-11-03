using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Operations;
using Saphir.ViewModels.Apps.Operations;
using Saphir.Views.Apps.Operations;

namespace Saphir.Screenshots;

/// <summary>Captures et données fictives du module Operations (MAIN COURANTE, PÉGASE, GÉOOPS) — Créteil, Val-de-Marne.</summary>
public static class OperationsCaptures
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 41, 0, TimeSpan.FromHours(1));
    private static DateTimeOffset Il(int minutes) => Now.AddMinutes(-minutes);

    private static readonly List<AgentResumeDto> Agents =
    [
        new(1, "AG-000001", "DURAND", "Jean-Pierre", "1234567", "Brigadier-chef", "BC", "CSP de Créteil", "BPS / BAC", "Chef de bord", AgentStatuts.Actif),
        new(3, "AG-000003", "TABOULOT", "Louis", "3301458", "Major", "MAJ", "CSP de Créteil", "BPS / BAC", "Chef d'unité", AgentStatuts.Actif),
        new(4, "AG-000004", "BORDINI", "Nolan", "4410256", "Major", "MAJ", "CSP de Créteil", "CSI", "Chef d'unité", AgentStatuts.Actif),
        new(7, "AG-000007", "LEROY", "Camille", "5520871", "Gardien de la paix", "GPX", "CSP de Créteil", "Police-secours", "Équipier", AgentStatuts.Actif),
        new(8, "AG-000008", "BENALI", "Sofiane", "6630412", "Brigadier", "BG", "CSP de Créteil", "Police-secours", "Chef de bord", AgentStatuts.Actif),
        new(9, "AG-000009", "PETIT", "Manon", "7741025", "Gardien de la paix", "GPX", "CSP de Créteil", "Police-secours", "Équipière", AgentStatuts.Actif),
        new(10, "AG-000010", "FONTAINE", "Hugo", "8852310", "Gardien de la paix", "GPX", "CSP de Créteil", "BPS / BAC", "Équipier", AgentStatuts.Actif),
    ];

    private static readonly List<VacationResumeDto> Vacations =
    [
        new(3, "VAC-2026-000003", "CSP de Créteil — Police-secours (jour)", Il(160), Il(160).AddHours(8), null, "MAJ TABOULOT Louis", VacationStatuts.Ouverte, 6, 2),
        new(2, "VAC-2026-000002", "CSP de Créteil — BAC (nuit)", Il(940), Il(940).AddHours(10), Il(170), "BC DURAND Jean-Pierre", VacationStatuts.Cloturee, 4, 1),
    ];

    private static readonly List<EquipageDto> Equipages =
    [
        new(11, 3, "VAC-2026-000003", "PS 12", "Police-secours", "CSP de Créteil", 8, "BG BENALI Sofiane",
            [new(8, "BG BENALI Sofiane", "Chef de bord"), new(7, "GPX LEROY Camille", "Conducteur"), new(9, "GPX PETIT Manon", "Équipier")],
            21, "GE-418-PN (Peugeot 5008)", EquipageStatuts.SurPlace, 42, "INT-2026-000042", true, Il(150), Il(12)),
        new(12, 3, "VAC-2026-000003", "BAC 94", "BAC", "CSP de Créteil", 1, "BC DURAND Jean-Pierre",
            [new(1, "BC DURAND Jean-Pierre", "Chef de bord"), new(10, "GPX FONTAINE Hugo", "Conducteur")],
            22, "FX-207-QL (Renault Mégane)", EquipageStatuts.Disponible, null, null, true, Il(140), Il(30)),
        new(13, 3, "VAC-2026-000003", "PS 14", "Police-secours", "CSP de Créteil", 3, "MAJ TABOULOT Louis",
            [new(3, "MAJ TABOULOT Louis", "Chef de bord"), new(4, "MAJ BORDINI Nolan", "Équipier")],
            null, null, EquipageStatuts.EnRoute, 43, "INT-2026-000043", true, Il(120), Il(4)),
    ];

    private static readonly List<VehiculeServiceDto> Vehicules =
    [
        new(21, "VS-2026-000021", "GE-418-PN", "Peugeot", "5008", "Véhicule sérigraphié", "CSP de Créteil", "PS 12", 48213, "Bon état", VehiculeDisponibilites.Sorti, null, 11, "PS 12", Il(150), 4),
        new(22, "VS-2026-000022", "FX-207-QL", "Renault", "Mégane", "Véhicule banalisé", "CSP de Créteil", "BAC 94", 91540, "Rayure aile avant gauche", VehiculeDisponibilites.Disponible, null, 12, "BAC 94", Il(30), 7),
        new(23, "VS-2026-000023", "GA-552-RT", "Renault", "Master", "Fourgon", "CSP de Créteil", "TV 3", 120874, "Pneu arrière à remplacer", VehiculeDisponibilites.Indisponible, "Garage le 03/11", null, null, Il(600), 3),
        new(24, "VS-2026-000024", "GH-093-KB", "Yamaha", "FJR 1300", "Motocyclette", "CSP de Créteil", "MOTO 2", 30215, "Bon état", VehiculeDisponibilites.Disponible, null, null, null, Il(900), 2),
    ];

    private static readonly List<MentionResumeDto> Mentions =
    [
        new(17, "MC-2026-000017", MentionTypes.Declaration, "Différend de voisinage", "Nuisances sonores répétées", "MARCHAND Élise", "LEROY Camille", Il(25), MentionStatuts.Brouillon, null, null, null),
        new(16, "MC-2026-000016", MentionTypes.Mention, "Compte rendu d'intervention", "Différend familial", null, "BENALI Sofiane", Il(70), MentionStatuts.Validee, "INT-2026-000040", null, null),
        new(15, "MC-2026-000015", MentionTypes.Declaration, "Perte de documents", "Perte de carte d'identité", "ROUSSEL Karim", "LEROY Camille", Il(190), MentionStatuts.Validee, null, null, null),
        new(18, "MC-2026-000018", MentionTypes.Rectificative, "Compte rendu d'intervention", "Additif à la mention MC-2026-000016", null, "BENALI Sofiane", Il(15), MentionStatuts.Brouillon, "INT-2026-000040", 16, "MC-2026-000016"),
    ];

    private static readonly List<JournalEntreeDto> Journal =
    [
        new(91, Il(4), JournalTypes.Engagement, "Engagement de PS 14 sur INT-2026-000043 — Accident, Maisons-Alfort", "Équipage : MAJ TABOULOT Louis, MAJ BORDINI Nolan.", "PÉGASE", "BORDINI Nolan (AG-000004)", 3, 13, null, 43, "INT-2026-000043", null),
        new(90, Il(9), JournalTypes.InterventionCreee, "Intervention INT-2026-000043 créée — Accident, Maisons-Alfort", "P2 — Urgente. Appel APL-2026-000118.", "PÉGASE", "BORDINI Nolan (AG-000004)", null, null, null, 43, "INT-2026-000043", null),
        new(89, Il(12), JournalTypes.ArriveeSurPlace, "PS 12 : sur place — INT-2026-000042", "Violences intrafamiliales, Créteil", "PÉGASE", "BENALI Sofiane (AG-000008)", 3, 11, null, 42, "INT-2026-000042", null),
        new(88, Il(21), JournalTypes.Engagement, "Engagement de PS 12 sur INT-2026-000042 — Violences intrafamiliales, Créteil", null, "PÉGASE", "BORDINI Nolan (AG-000004)", 3, 11, 21, 42, "INT-2026-000042", null),
        new(87, Il(70), JournalTypes.MentionValidee, "Mention MC-2026-000016 validée — Compte rendu d'intervention", null, "Main courante", "TABOULOT Louis (AG-000003)", null, null, null, 40, "INT-2026-000040", 16),
        new(86, Il(148), JournalTypes.SortieVehicule, "Sortie du véhicule GE-418-PN — PS 12", "Kilométrage : 48190. Carburant : Plein. Conducteur : GPX LEROY Camille.", "Main courante", "TABOULOT Louis (AG-000003)", 3, 11, 21, null, null, null),
        new(85, Il(150), JournalTypes.ConstitutionEquipage, "Équipage PS 12 constitué (Police-secours)", "Chef de bord : BG BENALI Sofiane.", "Main courante", "TABOULOT Louis (AG-000003)", 3, 11, 21, null, null, null),
        new(84, Il(160), JournalTypes.PriseService, "Ouverture de la vacation VAC-2026-000003 — CSP de Créteil — Police-secours (jour)", "Responsable : MAJ TABOULOT Louis. Agents présents : 6, absents : 0.", "Main courante", "TABOULOT Louis (AG-000003)", 3, null, null, null, null, null),
    ];

    private static readonly List<InterventionLigneDto> Interventions =
    [
        new(42, "INT-2026-000042", "Violences intrafamiliales", "Cris et coups entendus", "P1", "12 rue du Général-Leclerc, 94000 Créteil", "Créteil", Il(24), InterventionStatuts.EnCours, ["PS 12 (Sur place)"], 2, Il(12)),
        new(43, "INT-2026-000043", "Accident", "Collision VL / deux-roues", "P2", "Avenue du Général-de-Gaulle, 94700 Maisons-Alfort", "Maisons-Alfort", Il(9), InterventionStatuts.EnCours, ["PS 14 (En route)"], 1, Il(4)),
        new(44, "INT-2026-000044", "Tapage", null, "P3", "Résidence du Port, 94140 Alfortville", "Alfortville", Il(5), InterventionStatuts.EnAttente, [], 1, Il(5)),
        new(41, "INT-2026-000041", "Individu suspect signalé", "Rôde autour des véhicules", "P2", "Parking Créteil Soleil, 94000 Créteil", "Créteil", Il(55), InterventionStatuts.CompteRenduAttendu, [], 1, Il(30)),
    ];

    private static readonly List<AppelResumeDto> Appels =
    [
        new(119, "APL-2026-000119", AppelEtats.EnCours, Il(1), "17", null, null, "Saint-Maur-des-Fossés", "BORDINI Nolan", null, null),
        new(117, "APL-2026-000117", AppelEtats.EnAttente, Il(14), "Téléphone direct", "Différend", "P3", "Bonneuil-sur-Marne", "BORDINI Nolan", null, null),
    ];

    private static AppelDto Appel(long id) => new(
        id, "APL-2026-000119", AppelEtats.EnCours, Il(1), null, "BORDINI Nolan", 4, "CSP de Créteil", "simcall-8f21", null, null, null,
        new AppelFormulaire("17", "Demande d'intervention",
            new AppelRequerant("Témoin", true, null, null, "LEMOINE", "Sandrine", "06 12 34 56 78", null, "8 avenue Foch, 94100 Saint-Maur-des-Fossés", null,
                "Voisine", "Français", false, "Appelle depuis son balcon"),
            new AppelLocalisation("Saint-Maur-des-Fossés", "94100", "8", "avenue Foch", null, "Les Tilleuls", "B", "3", "32", "Digicode 4512B",
                "Immeuble / parties communes", "Face à la pharmacie", 412.5, -1203.8, "Déclarée par le requérant"),
            new AppelMotif("Différend", "Dispute violente entre voisins", "Deux hommes se disputent sur le palier, bruits de chute.", Il(3), true, "P2",
                "Moyenne", 2, false, false, null),
            [new AppelPersonne("Mis en cause", false, null, null, null, null, "Homme, 40 ans environ, sweat rouge", "Palier du 3e", "Agité", null, "Requérante")],
            [new AppelVehicule(null, "FR-512-ZT", "France", "Peugeot", "208", "Blanche", "Véhicule léger", "Garée devant l'immeuble", null, "Véhicule du mis en cause")],
            new AppelSecurite(false, null, true, false, null, false, false, false, null, "Ne pas monter seul", false),
            new AppelEngagement(true, 1, "Police-secours", null)),
        Il(1), 1);

    private static InterventionDto Intervention(long id) => new(
        id, "INT-2026-000042", "Violences intrafamiliales", "Cris et coups entendus", "P1",
        "Une voisine signale des cris et des bruits de coups dans l'appartement du dessus depuis 10 minutes. Présence possible d'un enfant.",
        "12 rue du Général-Leclerc, 94000 Créteil", "Créteil", "94000", -215.0, -890.0, "Vérifiée", "Témoin (identité non communiquée)",
        InterventionStatuts.EnCours, Il(24), null, null, 1, "Police-secours", false, null, null, null, "BORDINI Nolan (AG-000004)",
        new AppelSecurite(false, null, true, false, null, true, false, false, null, "Enfant présent dans le logement", true),
        [new(115, "APL-2026-000115", AppelEtats.InterventionCreee, Il(24), "17", "Violences intrafamiliales", "P1", "Créteil", "BORDINI Nolan", id, "INT-2026-000042"),
         new(116, "APL-2026-000116", AppelEtats.Rattache, Il(19), "17", "Violences intrafamiliales", "P1", "Créteil", "BORDINI Nolan", id, "INT-2026-000042")],
        [new AppelPersonne("Victime", false, null, null, null, null, "Femme, 30 ans environ", "Appartement 4e gauche", "Inconnu", null, "Voisine"),
         new AppelPersonne("Auteur présumé", true, 5, "PER-000005", "VASSEUR", "Mickaël", "Homme, 1m85", "Appartement 4e gauche", "Alcoolisé selon la voisine", "Alertes : TAJ", "Répertoire SAPHIR")],
        [],
        [new(301, 11, "PS 12", "BG BENALI Sofiane", "BG BENALI Sofiane, GPX LEROY Camille, GPX PETIT Manon", "GE-418-PN (Peugeot 5008)",
            EquipageStatuts.SurPlace, EquipageStatuts.SurPlace, false, true, Il(21), Il(21), "BORDINI Nolan (AG-000004)", Il(20), Il(12), null)],
        [new(1, Il(24), "Création", "Intervention créée depuis l'appel APL-2026-000115 (17) — Violences intrafamiliales, P1 — Immédiate.", "BORDINI Nolan (AG-000004)"),
         new(2, Il(24), "Sécurité", "Attention : menace signalée.", "BORDINI Nolan (AG-000004)"),
         new(3, Il(21), "Engagement", "Engagement : PS 12 — chef de bord BG BENALI Sofiane, véhicule GE-418-PN (Peugeot 5008).", "BORDINI Nolan (AG-000004)"),
         new(4, Il(20), "Statut équipage", "PS 12 : En route.", "BENALI Sofiane (AG-000008)"),
         new(5, Il(19), "Appel", "Appel APL-2026-000116 rattaché (17).", "BORDINI Nolan (AG-000004)"),
         new(6, Il(12), "Statut équipage", "PS 12 : Sur place.", "BENALI Sofiane (AG-000008)"),
         new(7, Il(8), "Message", "PS 12 : situation calme, victime prise en charge, demande des sapeurs-pompiers pour bilan.", "BENALI Sofiane (AG-000008)")],
        [], [], 6);

    private static readonly GeoopsCalibrationDto Calibration = new(-4140, 4860, -4300, 8700, false, 60);

    private static GeoopsSituationDto Situation() => new(Calibration,
        [
            new(11, "PS 12", EquipageStatuts.SurPlace, 42, "INT-2026-000042", -210, -880, 90, 0, "GE-418-PN", "Simulation FiveM", "BG BENALI Sofiane", Il(0)),
            new(12, "BAC 94", EquipageStatuts.Disponible, null, null, 380, -1420, 180, 12, "FX-207-QL", "Simulation FiveM", "BC DURAND Jean-Pierre", Il(0)),
            new(13, "PS 14", EquipageStatuts.EnRoute, 43, "INT-2026-000043", 1150, -620, 45, 24, null, "Déclarée", null, Il(3)),
        ],
        [new GeoopsAgentDto("GPX ROCHE Inès", -820, 180, Il(0))],
        [
            new(42, "INT-2026-000042", "Violences intrafamiliales", "P1", InterventionStatuts.EnCours, "12 rue du Général-Leclerc, Créteil", -215, -890),
            new(43, "INT-2026-000043", "Accident", "P2", InterventionStatuts.EnCours, "Avenue du Général-de-Gaulle, Maisons-Alfort", 1290, -510),
        ],
        [new(44, "INT-2026-000044", "Tapage", "P3", InterventionStatuts.EnAttente, "Résidence du Port, Alfortville", 0, 0)],
        Now);

    private static MentionDto Mention(long id) => new(
        id, "MC-2026-000017", MentionTypes.Declaration, "Différend de voisinage", "Accueil du public", MentionStatuts.Brouillon, Il(25), 7,
        "LEROY Camille", "5520871", "Gardien de la paix", "CSP de Créteil", null, null, null, null,
        new MentionDeclarant("Déclarant", null, null, "MARCHAND", "Élise", new DateOnly(1979, 4, 18), "Créteil (94)", "5 allée des Lilas, 94000 Créteil",
            "06 98 76 54 32", null, null),
        new MentionFaits(Il(1440), "5 allée des Lilas, 94000 Créteil", "Nuisances sonores répétées",
            "La déclarante indique subir depuis trois semaines des nuisances sonores nocturnes (musique, cris) provenant de l'appartement situé au-dessus du sien. " +
            "Elle précise avoir tenté une démarche amiable restée sans effet et souhaite que les faits soient consignés.",
            "Voisin du 2e étage (identité non connue)", null, null, null, "Déclarante calme, demande une simple consignation."),
        new MentionSuites("Information sur la procédure de plainte et la médiation de voisinage.", "Orientation vers une association", null, null),
        null, null, [], 2);

    /// <summary>Réponse fictive à une requête du poste (null = route inconnue de ce module).</summary>
    public static object? Sample(string method, string path, string query, string? body)
    {
        if (method != "GET")
            return null;
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        long? Id(int index) => segments.Length > index && long.TryParse(segments[index], out var v) ? v : null;

        return path switch
        {
            "api/referentiels/agents" => Agents,
            "api/main-courante/tableau-de-bord" => new McTableauDeBordDto(Vacations.Where(v => v.Statut == VacationStatuts.Ouverte).ToList(), 3, 2, 2, 1, 1, 2, 3,
                Journal.Take(6).ToList(), Mentions.Where(m => m.Statut == MentionStatuts.Brouillon).ToList()),
            "api/main-courante/vacations" => query.Contains("Cl%C3%B4tur", StringComparison.Ordinal) ? Vacations.Where(v => v.Statut == VacationStatuts.Cloturee).ToList()
                : query.Contains("statut=", StringComparison.Ordinal) ? Vacations.Where(v => v.Statut == VacationStatuts.Ouverte).ToList() : Vacations,
            "api/main-courante/equipages" => Equipages,
            "api/main-courante/vehicules" => Vehicules,
            "api/main-courante/mentions" => Mentions,
            "api/main-courante/journal" => Journal,
            "api/main-courante/historique" or "api/pegase/historique" => Historique(),
            "api/pegase/tableau-de-bord" => new PegaseTableauDeBordDto(2, 0, 1, 2, 1, 1, 2,
                new TnDto(4, 4, "BORDINI Nolan", "CSI", Il(200), null, TnStatuts.EnCours), Interventions, Appels, Equipages),
            "api/pegase/appels" => Appels,
            "api/pegase/interventions" => query.Contains("terminees", StringComparison.Ordinal)
                ? new List<InterventionLigneDto> { new(40, "INT-2026-000040", "Différend", "Différend familial", "P2", "3 rue Viet, Créteil", "Créteil", Il(180), InterventionStatuts.Cloturee, [], 1, Il(80)) }
                : Interventions,
            "api/pegase/tn" => new List<TnDto>
            {
                new(4, 4, "BORDINI Nolan", "CSI", Il(200), null, TnStatuts.EnCours),
                new(3, 2, "GIRARD Valentin", "Direction", Il(900), Il(200), TnStatuts.Terminee),
            },
            "api/pegase/communications" => Intervention(42).Chronologie.Reverse()
                .Select(c => new CommunicationDto(c.Id, c.Date, 42, "INT-2026-000042", c.Type, c.Texte, c.Auteur)).ToList(),
            "api/pegase/statistiques" => new PegaseStatistiquesDto(Now.AddDays(-30), Now, 412, 268, 241, 11, 6.4, 13.8,
                [new("Différend", 54), new("Tapage", 41), new("Vol", 37), new("Accident", 29), new("Violences", 24), new("Violences intrafamiliales", 19), new("Individu suspect signalé", 17)],
                [new("P1", 38), new("P2", 151), new("P3", 79)],
                [new(InterventionStatuts.Cloturee, 241), new(InterventionStatuts.Annulee, 11), new(InterventionStatuts.EnCours, 2), new(InterventionStatuts.EnAttente, 1)],
                [new("17", 301), new("Téléphone direct", 52), new("112 transmis", 31), new("Présentation au commissariat", 18), new("Radio", 10)],
                [new("Créteil", 96), new("Maisons-Alfort", 41), new("Alfortville", 37), new("Saint-Maur-des-Fossés", 33), new("Bonneuil-sur-Marne", 21)]),
            "api/geoops/situation" => Situation(),
            "api/geoops/calibration" => Calibration,
            _ when path.StartsWith("api/main-courante/vacations/", StringComparison.Ordinal) && Id(3) is { } id => new VacationDto(
                id, "VAC-2026-000003", "CSP de Créteil — Police-secours (jour)", Il(160), Il(160).AddHours(8), null, 3, "MAJ TABOULOT Louis",
                VacationStatuts.Ouverte, "Patrouilles renforcées secteur Créteil Soleil (vols à la roulotte).", null,
                [new(8, "BG BENALI Sofiane", PresencesAgent.Present, null), new(7, "GPX LEROY Camille", PresencesAgent.Present, null),
                 new(9, "GPX PETIT Manon", PresencesAgent.Retard, "Arrivée 7 h 20"), new(1, "BC DURAND Jean-Pierre", PresencesAgent.Present, null),
                 new(10, "GPX FONTAINE Hugo", PresencesAgent.Present, null), new(3, "MAJ TABOULOT Louis", PresencesAgent.Present, null)],
                Equipages.Take(2).ToList(), "TABOULOT Louis (AG-000003)", Il(160), 3),
            _ when path.StartsWith("api/main-courante/vehicules/", StringComparison.Ordinal) && Id(3) is { } id => new VehiculeFicheDto(
                Vehicules.First(v => v.Id == id),
                [new(5, "Sortie", Il(148), "PS 12", "GPX LEROY Camille", 48190, "Plein", "Bon état", null, null, VehiculeDisponibilites.Sorti, "TABOULOT Louis (AG-000003)"),
                 new(4, "Retour", Il(980), "BAC 94", null, 48190, "1/4", "Bon état", null, "RAS", VehiculeDisponibilites.Disponible, "DURAND Jean-Pierre (AG-000001)")],
                Historique()),
            _ when path.StartsWith("api/main-courante/mentions/", StringComparison.Ordinal) && Id(3) is { } id => Mention(id),
            _ when path.StartsWith("api/pegase/appels/", StringComparison.Ordinal) && Id(3) is { } id => Appel(id),
            _ when path.StartsWith("api/pegase/interventions/", StringComparison.Ordinal) && Id(3) is { } id =>
                segments.Length > 4 && segments[4] == "resume"
                    ? (object)new InterventionResumeDto(id, "INT-2026-000042", "Violences intrafamiliales", "12 rue du Général-Leclerc", "Créteil", Il(24), InterventionStatuts.EnCours, ["PS 12"])
                    : Intervention(id),
            _ => null
        };
    }

    private static List<HistoriqueDto> Historique() =>
    [
        new(301, Il(4), "BORDINI Nolan (AG-000004)", "pg-intervention", "INT-2026-000043", "Engagement", "equipages", null, "PS 14", null),
        new(300, Il(12), "BENALI Sofiane (AG-000008)", "mc-equipage", "PS 12 #11", "Statut", "statut", "En route", "Sur place", null),
        new(299, Il(18), "BORDINI Nolan (AG-000004)", "pg-intervention", "INT-2026-000042", "Modification", "Priorite", "P2", "P1", "Enfant présent dans le logement"),
        new(298, Il(148), "TABOULOT Louis (AG-000003)", "mc-vehicule", "VS-2026-000021", "Sortie", "disponibilite", "Disponible", "Sorti", "Km 48190, équipage PS 12"),
    ];

    /// <summary>Captures du module (utiliser Shots.Capture). Les fichiers sont numérotés à partir de 40.</summary>
    public static void Run()
    {
        // 40-43. MAIN COURANTE
        {
            var vm = new MainCouranteViewModel();
            Shots.Pump();
            Shots.Capture(new OperationsAppView { DataContext = vm }, 1280, 820, "40-main-courante-tableau.png");

            vm.OuvrirMention(17);
            Shots.Pump();
            Shots.Capture(new OperationsAppView { DataContext = vm }, 1280, 820, "41-main-courante-mention.png");

            var vacations = vm.Afficher<McVacationsSection>("vacations");
            Shots.Pump();
            vacations.Selection = vacations.Lignes.FirstOrDefault();
            Shots.Pump();
            Shots.Capture(new OperationsAppView { DataContext = vm }, 1280, 820, "42-main-courante-vacations.png");

            var vehicules = vm.Afficher<McVehiculesSection>("vehicules");
            Shots.Pump();
            vehicules.Selection = vehicules.Lignes.FirstOrDefault();
            Shots.Pump();
            Shots.Capture(new OperationsAppView { DataContext = vm }, 1280, 820, "43-main-courante-vehicules.png");
        }

        // 44-48. PÉGASE
        {
            var vm = new PegaseViewModel();
            Shots.Pump();
            Shots.Capture(new OperationsAppView { DataContext = vm }, 1280, 820, "44-pegase-tableau.png");

            vm.OuvrirAppel(119);
            Shots.Pump();
            Shots.Capture(new OperationsAppView { DataContext = vm }, 1280, 820, "45-pegase-nouvel-appel.png");

            vm.OuvrirIntervention(42);
            Shots.Pump();
            Shots.Capture(new OperationsAppView { DataContext = vm }, 1280, 820, "46-pegase-intervention.png");

            vm.Afficher("geoops");
            Shots.Pump();
            Shots.Capture(new OperationsAppView { DataContext = vm }, 1280, 820, "47-pegase-geoops.png");

            vm.Afficher("statistiques");
            Shots.Pump();
            Shots.Capture(new OperationsAppView { DataContext = vm }, 1280, 820, "48-pegase-statistiques.png");
        }

        // 49. Téléphone : PÉGASE en affichage compact
        {
            var vm = new PegaseViewModel();
            Shots.Pump();
            Shots.Capture(new OperationsAppView { DataContext = vm }, 390, 844, "49-pegase-telephone.png");
        }
    }
}
