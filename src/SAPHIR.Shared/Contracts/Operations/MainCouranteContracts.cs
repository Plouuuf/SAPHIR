namespace Saphir.Shared.Contracts.Operations;

// =====================================================================================
// MAIN COURANTE (phase 4) : vacations, équipages, véhicules de service, mentions, journal de service.
// Routes : /api/main-courante/… (voir docs/API-OPERATIONS.md).
// Dates : DateTimeOffset (stockées en UTC). Version : contrôle des modifications concurrentes.
// =====================================================================================

// ----- Vacations (10.1) -----

public sealed record VacationAgentDto(long AgentId, string Libelle, string Presence, string? Observation);

/// <summary>Agent de la vacation : <paramref name="Libelle"/> = libellé du répertoire RH (« BC DURAND Jean-Pierre »).</summary>
public sealed record VacationAgentInput(long AgentId, string? Libelle, string Presence, string? Observation);

public sealed record VacationResumeDto(
    long Id,
    string Reference,
    string Service,
    DateTimeOffset Debut,
    DateTimeOffset? FinPrevue,
    DateTimeOffset? Fin,
    string? Responsable,
    string Statut,
    int NombreAgents,
    int NombreEquipages);

public sealed record VacationDto(
    long Id,
    string Reference,
    string Service,
    DateTimeOffset Debut,
    DateTimeOffset? FinPrevue,
    DateTimeOffset? Fin,
    long? ResponsableId,
    string? Responsable,
    string Statut,
    string? Consignes,
    string? Observations,
    IReadOnlyList<VacationAgentDto> Agents,
    IReadOnlyList<EquipageDto> Equipages,
    string CreePar,
    DateTimeOffset CreeLe,
    long Version);

public sealed record VacationInput(
    string Service,
    DateTimeOffset Debut,
    DateTimeOffset? FinPrevue,
    long? ResponsableId,
    string? ResponsableLibelle,
    string? Consignes,
    string? Observations,
    IReadOnlyList<VacationAgentInput> Agents,
    long? Version);

public sealed record ClotureVacationInput(DateTimeOffset? Fin, string? Observations);

// ----- Équipages (10.2) -----

public sealed record EquipageMembreDto(long AgentId, string Libelle, string Role);

public sealed record EquipageMembreInput(long AgentId, string? Libelle, string Role);

public sealed record EquipageDto(
    long Id,
    long VacationId,
    string VacationReference,
    string Indicatif,
    string Type,
    string? Service,
    long? ChefAgentId,
    string? Chef,
    IReadOnlyList<EquipageMembreDto> Membres,
    long? VehiculeId,
    string? Vehicule,
    string Statut,
    long? InterventionId,
    string? InterventionNumero,
    bool Actif,
    DateTimeOffset CreeLe,
    DateTimeOffset MajLe);

/// <summary>Constitution ou modification d'un équipage : un seul « Chef de bord » parmi les membres.</summary>
public sealed record EquipageInput(
    long VacationId,
    string Indicatif,
    string Type,
    string? Service,
    long? VehiculeId,
    IReadOnlyList<EquipageMembreInput> Membres);

// ----- Véhicules de service (10.3 à 10.5) -----

public sealed record VehiculeServiceDto(
    long Id,
    string Reference,
    string Immatriculation,
    string? Marque,
    string? Modele,
    string Type,
    string? Service,
    string? IndicatifHabituel,
    int Kilometrage,
    string? Etat,
    string Disponibilite,
    string? Observations,
    long? EquipageId,
    string? EquipageIndicatif,
    DateTimeOffset MajLe,
    long Version);

public sealed record VehiculeServiceInput(
    string Immatriculation,
    string? Marque,
    string? Modele,
    string Type,
    string? Service,
    string? IndicatifHabituel,
    int Kilometrage,
    string? Etat,
    string? Observations,
    long? Version);

public sealed record VehiculeMouvementDto(
    long Id,
    string Type,
    DateTimeOffset Date,
    string? Equipage,
    string? Conducteur,
    int Kilometrage,
    string? Carburant,
    string? Etat,
    string? Anomalies,
    string? Observations,
    string? Disponibilite,
    string Responsable);

public sealed record VehiculeFicheDto(VehiculeServiceDto Vehicule, IReadOnlyList<VehiculeMouvementDto> Mouvements, IReadOnlyList<HistoriqueDto> Historique);

public sealed record SortieVehiculeInput(
    long? EquipageId,
    long? ConducteurId,
    string? ConducteurLibelle,
    DateTimeOffset? Heure,
    int Kilometrage,
    string? Carburant,
    string? Etat,
    string? Anomalies);

public sealed record RetourVehiculeInput(
    DateTimeOffset? Heure,
    int Kilometrage,
    string? Carburant,
    string? Etat,
    string? Degradations,
    string? Observations,
    bool Disponible);

public sealed record DisponibiliteVehiculeInput(string Disponibilite, string? Motif);

// ----- Mentions et déclarations (9.3) -----

public sealed record MentionDeclarant(
    string? Qualite,
    long? PersonneId,
    string? PersonneReference,
    string? Nom,
    string? Prenom,
    DateOnly? DateNaissance,
    string? LieuNaissance,
    string? Adresse,
    string? Telephone,
    string? Email,
    string? AutresPersonnes);

public sealed record MentionFaits(
    DateTimeOffset? Date,
    string? Lieu,
    string? Nature,
    string? Declaration,
    string? PersonnesCitees,
    string? Vehicules,
    string? Objets,
    string? Documents,
    string? Observations);

public sealed record MentionSuites(string? Mesures, string? Orientation, string? ServiceInforme, string? ProcedureLrppn);

/// <summary>
/// Création ou modification d'une mention (brouillon). <paramref name="MentionParentId"/> renseigné : mention
/// rectificative (additif) d'une mention validée.
/// </summary>
public sealed record MentionInput(
    string Type,
    string? Categorie,
    string? Origine,
    long? InterventionId,
    long? MentionParentId,
    MentionDeclarant Declarant,
    MentionFaits Faits,
    MentionSuites Suites,
    long? Version);

public sealed record MentionResumeDto(
    long Id,
    string Numero,
    string Type,
    string? Categorie,
    string? Nature,
    string? Declarant,
    string Redacteur,
    DateTimeOffset Date,
    string Statut,
    string? InterventionNumero,
    long? MentionParentId,
    string? ParentNumero);

public sealed record MentionDto(
    long Id,
    string Numero,
    string Type,
    string? Categorie,
    string? Origine,
    string Statut,
    DateTimeOffset Date,
    long? RedacteurId,
    string Redacteur,
    string? RedacteurRio,
    string? RedacteurGrade,
    string? RedacteurService,
    long? InterventionId,
    string? InterventionNumero,
    long? MentionParentId,
    string? ParentNumero,
    MentionDeclarant Declarant,
    MentionFaits Faits,
    MentionSuites Suites,
    string? ValidePar,
    DateTimeOffset? ValideLe,
    IReadOnlyList<MentionResumeDto> Rectificatives,
    long Version);

// ----- Journal de service (9.4) -----

public sealed record JournalEntreeDto(
    long Id,
    DateTimeOffset Date,
    string Type,
    string Libelle,
    string? Details,
    string Source,
    string Auteur,
    long? VacationId,
    long? EquipageId,
    long? VehiculeId,
    long? InterventionId,
    string? InterventionNumero,
    long? MentionId);

/// <summary>Événement saisi à la main dans le journal de service.</summary>
public sealed record JournalInput(string Libelle, string? Details, long? VacationId);

// ----- Tableau de bord -----

public sealed record McTableauDeBordDto(
    IReadOnlyList<VacationResumeDto> VacationsOuvertes,
    int EquipagesActifs,
    int EquipagesEngages,
    int VehiculesDisponibles,
    int VehiculesSortis,
    int VehiculesIndisponibles,
    int MentionsAValider,
    int MentionsDuJour,
    IReadOnlyList<JournalEntreeDto> DerniersEvenements,
    IReadOnlyList<MentionResumeDto> MentionsBrouillon);
