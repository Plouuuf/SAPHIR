namespace Saphir.Shared.Contracts;

// =====================================================================================
// PEGASE : prise des appels 17, événements et engagement des équipages.
// Un appel décroché sur SIMCALL ouvre la prise d'appel sur le poste SAPHIR de l'opérateur ;
// l'appel débouche sur un événement (qualifié, localisé, priorisé) ou est clos sans suite.
// =====================================================================================

// ----- Intégration SIMCALL (serveur à serveur, en-tête X-Saphir-Key) -----

/// <summary>SIMCALL → SAPHIR : un opérateur vient de décrocher un appel au 17.</summary>
public sealed record SimcallCallAnswered(
    string SimcallCallId,
    string DiscordUserId,
    string OperatorName,
    string? CallerNumber,
    DateTimeOffset AnsweredAt);

/// <summary>SIMCALL → SAPHIR : l'appel est terminé (raccroché).</summary>
public sealed record SimcallCallEnded(string SimcallCallId, DateTimeOffset EndedAt);

// ----- Appels -----

public sealed record AppelDto(
    long Id,
    string? SimcallId,
    string? NumeroRequerant,
    string Operateur,
    string OperateurId,
    DateTimeOffset DecrocheLe,
    DateTimeOffset? RaccrocheLe,
    string Statut,
    string? Issue,
    long? EvenementId);

public static class AppelStatuts
{
    public const string EnCours = "En cours";
    public const string Evenement = "Événement créé";
    public const string SansSuite = "Sans suite";
}

public sealed record SansSuiteInput(string Motif, string? Observations);

// ----- Événements -----

public sealed record EvenementDto(
    long Id,
    string Numero,
    string Nature,
    int Priorite,
    string Adresse,
    string? Commune,
    string? Complement,
    string? RequerantNom,
    string? RequerantTel,
    string? Description,
    string Statut,
    string CreePar,
    DateTimeOffset CreeLe,
    string? ClosPar,
    DateTimeOffset? ClosLe,
    string? CompteRendu,
    IReadOnlyList<string> Equipages);

public sealed record EvenementInput(
    string Nature,
    int Priorite,
    string Adresse,
    string? Commune,
    string? Complement,
    string? RequerantNom,
    string? RequerantTel,
    string? Description);

/// <summary>Ligne de la chronologie d'un événement.</summary>
public sealed record PegaseMessageDto(long Id, DateTimeOffset Date, string Auteur, string Type, string Texte);

public sealed record EvenementDetailDto(
    EvenementDto Evenement,
    IReadOnlyList<PegaseMessageDto> Messages,
    AppelDto? Appel);

public sealed record MessageInput(string Texte);
public sealed record EngagementInput(long EquipageId);
public sealed record ClotureInput(string CompteRendu);

public static class EvenementStatuts
{
    public const string Attente = "En attente";
    public const string Engage = "Engagé";
    public const string SurPlace = "Sur place";
    public const string Termine = "Terminé";
}

// ----- Équipages -----

public sealed record EquipageDto(
    long Id,
    string Indicatif,
    string Type,
    string? Effectif,
    string Statut,
    long? EvenementId,
    string? EvenementNumero,
    DateTimeOffset MajLe);

public sealed record EquipageInput(string Indicatif, string Type, string? Effectif);
public sealed record StatutInput(string Statut);

public static class EquipageStatuts
{
    public const string Disponible = "Disponible";
    public const string Engage = "Engagé";
    public const string SurPlace = "Sur place";
    public const string Indisponible = "Indisponible";
    public const string HorsService = "Hors service";

    public static readonly IReadOnlyList<string> All = [Disponible, Engage, SurPlace, Indisponible, HorsService];
}

/// <summary>État complet de PEGASE pour un poste (rechargé à chaque changement).</summary>
public sealed record PegaseStateDto(
    IReadOnlyList<EvenementDto> Evenements,
    IReadOnlyList<EquipageDto> Equipages,
    IReadOnlyList<AppelDto> AppelsEnCours);

public static class PegaseReferentiel
{
    public static readonly IReadOnlyList<string> Natures =
    [
        "Violences volontaires",
        "Violences intrafamiliales",
        "Rixe",
        "Individu armé",
        "Coups de feu",
        "Vol avec violence / à l'arraché",
        "Cambriolage en cours",
        "Vol à l'étalage",
        "Vol de véhicule",
        "Accident de la circulation",
        "Délit de fuite",
        "Conduite dangereuse",
        "Refus d'obtempérer",
        "Personne suspecte",
        "Trafic de stupéfiants",
        "Dégradations",
        "Différend",
        "Tapage / nuisances",
        "Ivresse publique manifeste",
        "Disparition inquiétante",
        "Personne en détresse",
        "Alarme",
        "Prise d'otage / retranché",
        "Autre"
    ];

    /// <summary>Priorités : index 0 = P1.</summary>
    public static readonly IReadOnlyList<string> Priorites =
    [
        "P1 — Urgence absolue",
        "P2 — Urgence",
        "P3 — Différé"
    ];

    public static readonly IReadOnlyList<string> MotifsSansSuite =
    [
        "Renseignement donné",
        "Réorientation (15, 18, autre service)",
        "Appel abandonné / silencieux",
        "Appel malveillant",
        "Erreur de numéro",
        "Doublon d'un événement en cours"
    ];

    public static readonly IReadOnlyList<string> TypesEquipage =
    [
        "Police secours",
        "BAC",
        "Brigade motocycliste",
        "Unité cynophile",
        "Brigade de nuit",
        "Officier / chef de poste",
        "Renfort (CRS, CDI…)",
        "Autre"
    ];

    public static string PrioriteLabel(int priorite)
        => priorite is >= 1 and <= 3 ? Priorites[priorite - 1] : "P" + priorite;
}
