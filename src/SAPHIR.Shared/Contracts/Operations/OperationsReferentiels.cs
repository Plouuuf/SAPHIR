namespace Saphir.Shared.Contracts.Operations;

// =====================================================================================
// Module Opérations (MAIN COURANTE, PÉGASE, GÉOOPS) : listes de valeurs partagées serveur / poste.
// Les libellés sont stockés tels quels en base (texte lisible dans l'historique).
// =====================================================================================

/// <summary>Types des messages temps réel du module (Text = « objet:id », jamais de donnée personnelle).</summary>
public static class OperationsTypes
{
    /// <summary>Main courante modifiée (vacation, équipage, véhicule, mention, journal). Text : « vacation:12 »…</summary>
    public const string MainCouranteChanged = "main-courante-changed";

    /// <summary>PÉGASE modifié (appel, intervention, TN). Text : « intervention:12 », « appel:4 », « tn ».</summary>
    public const string PegaseChanged = "pegase-changed";

    /// <summary>Nouvelles positions GÉOOPS reçues du serveur FiveM. Text : nombre de positions.</summary>
    public const string GeoopsPositions = "geoops-positions";

    /// <summary>Ouverture d'une application sur le poste (même valeur que SaphirSession.OpenAppType).</summary>
    public const string OuvrirApplication = "open-app";
}

/// <summary>Statuts d'une intervention (7.2).</summary>
public static class InterventionStatuts
{
    public const string AQualifier = "À qualifier";
    public const string EnAttente = "En attente d'engagement";
    public const string MoyensAffectes = "Moyens affectés";
    public const string EnCours = "En cours";
    public const string CompteRenduAttendu = "Compte rendu attendu";
    public const string Cloturee = "Clôturée";
    public const string Annulee = "Annulée";

    public static readonly IReadOnlyList<string> All =
        [AQualifier, EnAttente, MoyensAffectes, EnCours, CompteRenduAttendu, Cloturee, Annulee];

    /// <summary>Intervention terminée (plus d'engagement possible).</summary>
    public static bool EstTerminee(string statut) => statut is Cloturee or Annulee;

    /// <summary>Interventions « en attente » (menu 6.2.4) : pas encore de moyen engagé.</summary>
    public static bool EstEnAttente(string statut) => statut is AQualifier or EnAttente;
}

/// <summary>Statuts d'un équipage (7.2), indépendants du statut de l'intervention.</summary>
public static class EquipageStatuts
{
    public const string Disponible = "Disponible";
    public const string Affecte = "Affecté";
    public const string EnRoute = "En route";
    public const string SurPlace = "Sur place";
    public const string Transport = "Transport en cours";
    public const string AuService = "Au service destinataire";
    public const string MissionTerminee = "Mission terminée";
    public const string Indisponible = "Indisponible";
    public const string HorsService = "Hors service";

    public static readonly IReadOnlyList<string> All =
        [Disponible, Affecte, EnRoute, SurPlace, Transport, AuService, MissionTerminee, Indisponible, HorsService];

    /// <summary>Statuts d'un équipage engagé sur une intervention.</summary>
    public static bool EstEngage(string statut) => statut is Affecte or EnRoute or SurPlace or Transport or AuService;

    /// <summary>Un équipage peut recevoir un nouvel engagement.</summary>
    public static bool PeutEtreEngage(string statut) => statut is Disponible or MissionTerminee;
}

/// <summary>États de traitement d'un appel (6.3 A).</summary>
public static class AppelEtats
{
    public const string EnCours = "En cours de saisie";
    public const string Brouillon = "Brouillon";
    public const string EnAttente = "En attente";
    public const string TransmisTn = "Transmis au TN";
    public const string InterventionCreee = "Intervention créée";
    public const string Rattache = "Rattaché à une intervention";
    public const string ClosSansSuite = "Clos sans suite";

    public static readonly IReadOnlyList<string> All =
        [EnCours, Brouillon, EnAttente, TransmisTn, InterventionCreee, Rattache, ClosSansSuite];

    /// <summary>Appel encore à traiter (sans intervention ni clôture).</summary>
    public static bool EstOuvert(string etat) => etat is EnCours or Brouillon or EnAttente or TransmisTn;
}

/// <summary>Listes de choix des formulaires PÉGASE.</summary>
public static class PegaseListes
{
    public static readonly IReadOnlyList<string> Canaux =
    [
        "17", "112 transmis", "Téléphone direct", "Radio", "Autre service", "Présentation au commissariat", "Initiative", "Autre"
    ];

    public static readonly IReadOnlyList<string> NaturesAppel =
    [
        "Demande d'intervention", "Signalement", "Demande de renseignement", "Rappel d'un requérant", "Appel abusif", "Erreur / appel perdu", "Autre"
    ];

    public static readonly IReadOnlyList<string> Motifs =
    [
        "Vol", "Cambriolage", "Violences", "Rixe", "Violences intrafamiliales", "Accident", "Tapage", "Différend", "Menaces",
        "Personne disparue", "Secours à personne", "Véhicule signalé", "Dégradations", "Individu suspect signalé",
        "Infraction routière", "Troubles à l'ordre public", "Autre"
    ];

    /// <summary>Priorités : code stocké (P1…P3).</summary>
    public static readonly IReadOnlyList<string> Priorites = ["P1", "P2", "P3"];

    public static string LibellePriorite(string? code) => code switch
    {
        "P1" => "P1 — Immédiate",
        "P2" => "P2 — Urgente",
        "P3" => "P3 — Différée",
        _ => code ?? "—"
    };

    public static readonly IReadOnlyList<string> Gravites = ["Non précisée", "Faible", "Moyenne", "Élevée", "Critique"];

    public static readonly IReadOnlyList<string> QualitesRequerant = ["Victime", "Témoin", "Tiers", "Autre"];

    public static readonly IReadOnlyList<string> TypesLieu =
    [
        "Voie publique", "Domicile", "Immeuble / parties communes", "Commerce", "Établissement scolaire", "Transport en commun",
        "Parking", "Bâtiment public", "Établissement de santé", "Débit de boissons", "Zone industrielle", "Espace vert", "Autre"
    ];

    public static readonly IReadOnlyList<string> Fiabilites =
        ["Vérifiée", "Déclarée par le requérant", "Approximative", "Inconnue"];

    public static readonly IReadOnlyList<string> RolesPersonne =
        ["Victime", "Auteur présumé", "Témoin", "Mis en cause", "Personne vulnérable", "Requérant", "Autre"];

    public static readonly IReadOnlyList<string> CategoriesVehicule =
        ["Véhicule léger", "Deux-roues", "Utilitaire", "Poids lourd", "Transport en commun", "Autre"];

    public static readonly IReadOnlyList<string> CategoriesMoyen =
        ["Police-secours", "BAC", "Brigade motocycliste", "Brigade cynophile", "Équipage VTT", "Unité spécialisée", "Autre"];

    public static readonly IReadOnlyList<string> ResultatsIntervention =
    [
        "Faits constatés", "Néant à l'arrivée", "Interpellation", "Assistance apportée", "Différend réglé sur place",
        "Orientation vers un autre service", "Fausse alerte", "Autre"
    ];
}

/// <summary>Listes de choix de la MAIN COURANTE.</summary>
public static class MainCouranteListes
{
    public static readonly IReadOnlyList<string> TypesEquipage = PegaseListes.CategoriesMoyen;

    public static readonly IReadOnlyList<string> RolesMembre = ["Chef de bord", "Conducteur", "Équipier", "Stagiaire"];

    public const string ChefDeBord = "Chef de bord";

    public static readonly IReadOnlyList<string> TypesVehicule =
        ["Véhicule sérigraphié", "Véhicule banalisé", "Fourgon", "Motocyclette", "VTT", "Autre"];

    public static readonly IReadOnlyList<string> NiveauxCarburant = ["Plein", "3/4", "1/2", "1/4", "Réserve"];

    public static readonly IReadOnlyList<string> CategoriesMention =
    [
        "Différend familial", "Différend de voisinage", "Perte de documents", "Départ du domicile", "Non-représentation d'enfant",
        "Information du service", "Incident", "Dépôt d'objet trouvé", "Compte rendu d'intervention", "Autre"
    ];

    public static readonly IReadOnlyList<string> OriginesMention =
        ["Accueil du public", "Intervention PÉGASE", "Appel téléphonique", "Initiative du service", "Autre service", "Autre"];

    public static readonly IReadOnlyList<string> QualitesDeclarant = ["Déclarant", "Victime", "Témoin", "Mis en cause", "Représentant légal", "Autre"];

    public static readonly IReadOnlyList<string> Orientations =
        ["Aucune", "Dépôt de plainte proposé", "Orientation vers une association", "Orientation vers un autre service", "Information du parquet", "Autre"];
}

public static class MentionTypes
{
    public const string Mention = "Mention";
    public const string Declaration = "Déclaration";
    public const string Rectificative = "Mention rectificative";

    public static readonly IReadOnlyList<string> All = [Mention, Declaration, Rectificative];
}

public static class MentionStatuts
{
    public const string Brouillon = "Brouillon";
    public const string Validee = "Validée";
}

public static class VacationStatuts
{
    public const string Ouverte = "Ouverte";
    public const string Cloturee = "Clôturée";
}

public static class PresencesAgent
{
    public const string Present = "Présent";
    public const string Absent = "Absent";
    public const string Retard = "En retard";

    public static readonly IReadOnlyList<string> All = [Present, Absent, Retard];
}

public static class VehiculeDisponibilites
{
    public const string Disponible = "Disponible";
    public const string Sorti = "Sorti";
    public const string Indisponible = "Indisponible";

    public static readonly IReadOnlyList<string> All = [Disponible, Sorti, Indisponible];
}

public static class CompteRenduStatuts
{
    public const string Brouillon = "Brouillon";
    public const string Valide = "Validé";
}

public static class TnStatuts
{
    public const string EnCours = "En cours";
    public const string Terminee = "Terminée";
}

/// <summary>Événements du journal de service (9.4).</summary>
public static class JournalTypes
{
    public const string PriseService = "Prise de service";
    public const string ConstitutionEquipage = "Constitution d'équipage";
    public const string ModificationEquipage = "Modification d'équipage";
    public const string DissolutionEquipage = "Dissolution d'équipage";
    public const string SortieVehicule = "Sortie de véhicule";
    public const string RetourVehicule = "Retour de véhicule";
    public const string InterventionCreee = "Intervention créée";
    public const string Engagement = "Engagement";
    public const string ArriveeSurPlace = "Arrivée sur place";
    public const string StatutEquipage = "Statut d'équipage";
    public const string RetourMission = "Retour de mission";
    public const string InterventionTerminee = "Intervention terminée";
    public const string MentionValidee = "Mention validée";
    public const string FinVacation = "Fin de vacation";
    public const string Evenement = "Événement";

    public static readonly IReadOnlyList<string> All =
    [
        PriseService, ConstitutionEquipage, ModificationEquipage, DissolutionEquipage, SortieVehicule, RetourVehicule,
        InterventionCreee, Engagement, ArriveeSurPlace, StatutEquipage, RetourMission, InterventionTerminee, MentionValidee,
        FinVacation, Evenement
    ];
}
