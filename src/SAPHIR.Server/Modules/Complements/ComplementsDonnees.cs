namespace Saphir.Server.Modules.Complements;

/// <summary>
/// Données de départ fictives (Val-de-Marne Simulation). Numéros de téléphone dans la tranche « 01 99 00 » réservée
/// à la fiction ; codes NATINF et montants indicatifs, propres au jeu de rôle et modifiables par l'administration.
/// </summary>
public static class ComplementsDonnees
{
    public sealed record ServiceDepart(string Nom, string? Categorie, string? Indicatif, string? Telephone, string? Frequence,
        string? Adresse, string? Horaires, string? Observations);

    public sealed record SalonDepart(string Code, string Nom, string Description);

    public sealed record InfractionDepart(string Natinf, string Libelle, string TexteReference, string Categorie, string Classe,
        decimal MontantForfaitaire, decimal? MontantMinore, decimal? MontantMajore, int Points);

    public static readonly IReadOnlyList<ServiceDepart> Services =
    [
        new("CIC 94 — Centre d'information et de commandement", "Commandement", "TN 94", "01 99 00 01 00", "Canal 1 — CIC",
            "Hôtel de police, 1 place du Général-Billotte, Créteil", "24 h / 24", "Engagement des moyens, renseignements opérationnels"),
        new("CSP de Créteil — Accueil", "Commissariat", "Créteil Accueil", "01 99 00 01 10", null,
            "Hôtel de police, 1 place du Général-Billotte, Créteil", "24 h / 24", "Dépôt de plainte, main courante"),
        new("CSP de Créteil — Officier de permanence", "Commissariat", "OPJ Créteil", "01 99 00 01 11", "Canal 2 — District",
            "Hôtel de police, Créteil", "24 h / 24", "Avis à magistrat, gardes à vue"),
        new("BAC 94 de nuit", "Voie publique", "BAC 94", "01 99 00 01 20", "Canal 3 — BAC", "Créteil", "20 h – 6 h", null),
        new("Police secours — Brigade de jour", "Voie publique", "PS Créteil", "01 99 00 01 21", "Canal 2 — District", "Créteil", "6 h – 20 h", null),
        new("Unité de sécurité routière", "Voie publique", "USR 94", "01 99 00 01 30", "Canal 4 — Routière", "Créteil", "7 h – 19 h",
            "Contrôles routiers, PVe, dépistages"),
        new("Service d'investigation et de recherche", "Investigation", "SIR 94", "01 99 00 01 40", null, "Créteil", "9 h – 19 h", "Enquêtes judiciaires"),
        new("Brigade des mineurs et de la famille", "Investigation", "BPF 94", "01 99 00 01 41", null, "Créteil", "9 h – 18 h", null),
        new("Service de l'identité judiciaire", "Investigation", "SIJ 94", "01 99 00 01 42", null, "Créteil", "8 h – 18 h", "Prélèvements, constatations"),
        new("Fourrière de Créteil (fictive)", "Partenaires", null, "01 99 00 01 50", null, "Zone industrielle des Petits-Prés, Créteil", "7 h – 20 h", null),
        new("SAMU 94 (simulation)", "Partenaires", "SAMU 94", "01 99 00 01 60", null, "Hôpital Henri-Mondor, Créteil", "24 h / 24", null),
        new("Sapeurs-pompiers BSPP (simulation)", "Partenaires", "CODIS / BSPP", "01 99 00 01 70", null, "Créteil", "24 h / 24", null),
        new("Parquet de Créteil — Permanence (fictive)", "Justice", null, "01 99 00 01 80", null, "Tribunal judiciaire, Créteil", "24 h / 24", "Avis, réquisitions"),
        new("Service RH — Secrétariat", "Administration", null, "01 99 00 01 90", null, "Hôtel de police, Créteil", "9 h – 17 h", "Affectations, carrières, documents RH"),
    ];

    public static readonly IReadOnlyList<SalonDepart> Salons =
    [
        new("general", "Général — CSP de Créteil", "Informations de service pour tous les agents"),
        new("cic-94", "CIC 94 — Informations", "Diffusions du centre d'information et de commandement"),
        new("bac-94", "BAC 94", "Coordination de la brigade anticriminalité"),
        new("securite-routiere", "Sécurité routière", "Opérations de contrôle, PVe, dépistages"),
        new("investigation", "Investigation", "Échanges du service d'investigation"),
    ];

    /// <summary>Une trentaine d'infractions routières courantes (référentiel fictif à vocation de jeu de rôle).</summary>
    public static readonly IReadOnlyList<InfractionDepart> Infractions =
    [
        new("11298", "Excès de vitesse inférieur à 20 km/h — vitesse maximale autorisée supérieure à 50 km/h", "Art. R413-14 C. route", "Vitesse", "3", 68m, 45m, 180m, 1),
        new("11299", "Excès de vitesse inférieur à 20 km/h — vitesse maximale autorisée inférieure ou égale à 50 km/h", "Art. R413-14 C. route", "Vitesse", "4", 135m, 90m, 375m, 1),
        new("11300", "Excès de vitesse égal ou supérieur à 20 km/h et inférieur à 30 km/h", "Art. R413-14 C. route", "Vitesse", "4", 135m, 90m, 375m, 2),
        new("11301", "Excès de vitesse égal ou supérieur à 30 km/h et inférieur à 40 km/h", "Art. R413-14 C. route", "Vitesse", "4", 135m, 90m, 375m, 3),
        new("11302", "Excès de vitesse égal ou supérieur à 40 km/h et inférieur à 50 km/h", "Art. R413-14 C. route", "Vitesse", "4", 135m, 90m, 375m, 4),
        new("20015", "Inobservation de l'arrêt imposé par un feu rouge fixe ou clignotant", "Art. R412-30 C. route", "Signalisation", "4", 135m, 90m, 375m, 4),
        new("20016", "Inobservation de l'arrêt absolu imposé par un panneau « STOP »", "Art. R415-6 C. route", "Signalisation", "4", 135m, 90m, 375m, 4),
        new("20017", "Circulation en sens interdit", "Art. R412-28 C. route", "Signalisation", "4", 135m, 90m, 375m, 4),
        new("20018", "Franchissement d'une ligne continue", "Art. R412-19 C. route", "Signalisation", "4", 135m, 90m, 375m, 3),
        new("20019", "Chevauchement d'une ligne continue", "Art. R412-19 C. route", "Signalisation", "4", 135m, 90m, 375m, 1),
        new("20020", "Refus de priorité à un piéton régulièrement engagé", "Art. R415-11 C. route", "Priorités", "4", 135m, 90m, 375m, 6),
        new("20021", "Refus de priorité à droite", "Art. R415-5 C. route", "Priorités", "4", 135m, 90m, 375m, 4),
        new("20022", "Dépassement par la droite", "Art. R414-6 C. route", "Circulation", "4", 135m, 90m, 375m, 3),
        new("20023", "Non-respect des distances de sécurité entre deux véhicules", "Art. R412-12 C. route", "Circulation", "4", 135m, 90m, 375m, 3),
        new("20024", "Circulation sur la bande d'arrêt d'urgence", "Art. R412-8 C. route", "Circulation", "4", 135m, 90m, 375m, 3),
        new("20025", "Changement de direction sans avertissement préalable", "Art. R412-10 C. route", "Circulation", "2", 35m, 22m, 75m, 3),
        new("21001", "Usage d'un téléphone tenu en main par le conducteur d'un véhicule en circulation", "Art. R412-6-1 C. route", "Comportement", "4", 135m, 90m, 375m, 3),
        new("21002", "Port à l'oreille d'un dispositif susceptible d'émettre du son par le conducteur", "Art. R412-6-1 C. route", "Comportement", "4", 135m, 90m, 375m, 3),
        new("21003", "Défaut de port de la ceinture de sécurité par le conducteur", "Art. R412-1 C. route", "Comportement", "4", 135m, 90m, 375m, 3),
        new("21004", "Défaut de port du casque par le conducteur d'un deux-roues motorisé", "Art. R431-1 C. route", "Comportement", "4", 135m, 90m, 375m, 3),
        new("21005", "Conduite avec une concentration d'alcool égale ou supérieure à 0,25 mg/l et inférieure à 0,40 mg/l d'air expiré", "Art. R234-1 C. route", "Comportement", "4", 135m, 90m, 375m, 6),
        new("22001", "Stationnement très gênant sur un trottoir", "Art. R417-11 C. route", "Stationnement", "4", 135m, null, 375m, 0),
        new("22002", "Stationnement très gênant sur un emplacement réservé aux personnes handicapées", "Art. R417-11 C. route", "Stationnement", "4", 135m, null, 375m, 0),
        new("22003", "Arrêt ou stationnement gênant sur un passage piéton", "Art. R417-10 C. route", "Stationnement", "2", 35m, null, 75m, 0),
        new("22004", "Stationnement en double file", "Art. R417-10 C. route", "Stationnement", "2", 35m, null, 75m, 0),
        new("23001", "Défaut de présentation immédiate du permis de conduire", "Art. R233-1 C. route", "Documents", "1", 11m, null, 33m, 0),
        new("23002", "Défaut de présentation immédiate du certificat d'immatriculation", "Art. R233-1 C. route", "Documents", "1", 11m, null, 33m, 0),
        new("23003", "Circulation d'un véhicule sans contrôle technique périodique en cours de validité", "Art. R323-1 C. route", "Véhicule", "4", 135m, 90m, 375m, 0),
        new("23004", "Plaque d'immatriculation illisible, non conforme ou absente", "Art. R317-8 C. route", "Véhicule", "4", 135m, 90m, 375m, 0),
        new("23005", "Pneumatique présentant une usure ou un défaut non conforme", "Art. R314-1 C. route", "Véhicule", "3", 68m, 45m, 180m, 0),
        new("23006", "Vitres avant surteintées (transparence inférieure à 70 %)", "Art. R316-3-1 C. route", "Véhicule", "4", 135m, 90m, 375m, 1),
        new("23007", "Émission de bruits gênants par un véhicule (échappement non conforme)", "Art. R318-3 C. route", "Véhicule", "3", 68m, 45m, 180m, 0),
        new("24001", "Conduite d'un véhicule sans assurance (amende forfaitaire délictuelle)", "Art. L324-2 C. route", "Délits forfaitaires", "AFD", 500m, 400m, 1000m, 0),
        new("24002", "Conduite d'un véhicule sans permis (amende forfaitaire délictuelle)", "Art. L221-2 C. route", "Délits forfaitaires", "AFD", 800m, 640m, 1600m, 0),
    ];
}
