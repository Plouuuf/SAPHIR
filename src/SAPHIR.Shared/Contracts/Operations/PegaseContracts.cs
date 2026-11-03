namespace Saphir.Shared.Contracts.Operations;

// =====================================================================================
// PÉGASE (phase 5) : appels CIC, interventions, engagements, comptes rendus, TN, statistiques.
// Un appel et une intervention sont deux objets distincts (règle 13) ; une intervention peut mobiliser
// plusieurs équipages (règle 14). Routes : /api/pegase/… (voir docs/API-OPERATIONS.md).
// =====================================================================================

// ----- Formulaire « Nouvel appel CIC » (6.3) -----

/// <summary>B — Requérant.</summary>
public sealed record AppelRequerant(
    string? Qualite,
    bool IdentiteConnue,
    long? PersonneId,
    string? PersonneReference,
    string? Nom,
    string? Prenom,
    string? Telephone,
    string? TelephoneRappel,
    string? Adresse,
    DateOnly? DateNaissance,
    string? LienFaits,
    string? Langue,
    bool Interprete,
    string? Observations);

/// <summary>C — Localisation. X/Y : position sur la carte GÉOOPS (coordonnées du jeu, position déclarée).</summary>
public sealed record AppelLocalisation(
    string? Commune,
    string? CodePostal,
    string? Numero,
    string? Rue,
    string? AdresseComplete,
    string? Residence,
    string? Batiment,
    string? Etage,
    string? Appartement,
    string? Acces,
    string? TypeLieu,
    string? Repere,
    double? X,
    double? Y,
    string? Fiabilite);

/// <summary>D — Motif.</summary>
public sealed record AppelMotif(
    string? Motif,
    string? SousMotif,
    string? Description,
    DateTimeOffset? FaitsLe,
    bool FaitsEnCours,
    string? Priorite,
    string? Gravite,
    int? NombrePersonnes,
    bool BlessesSignales,
    bool DangerImmediat,
    string? Observations);

/// <summary>E — Personne concernée (identifiant interne <paramref name="PersonneId"/> si trouvée dans SAPHIR).</summary>
public sealed record AppelPersonne(
    string? Role,
    bool IdentiteConnue,
    long? PersonneId,
    string? PersonneReference,
    string? Nom,
    string? Prenom,
    string? Description,
    string? Localisation,
    string? EtatApparent,
    string? Informations,
    string? Source);

/// <summary>F — Véhicule concerné.</summary>
public sealed record AppelVehicule(
    long? VehiculeId,
    string? Immatriculation,
    string? Pays,
    string? Marque,
    string? Modele,
    string? Couleur,
    string? Categorie,
    string? Description,
    string? Direction,
    string? LienFaits);

/// <summary>G — Sécurité.</summary>
public sealed record AppelSecurite(
    bool Blesses,
    int? NombreBlesses,
    bool Menace,
    bool Arme,
    string? ArmePrecision,
    bool Mineurs,
    bool PersonneAssistance,
    bool SecoursDemandes,
    string? ServicesSurPlace,
    string? Consignes,
    bool RenfortEnvisage);

/// <summary>H — Engagement demandé (les équipages réellement affectés sont les engagements de l'intervention).</summary>
public sealed record AppelEngagement(bool Necessaire, int? NombreEquipages, string? CategorieMoyen, string? Observations);

/// <summary>Contenu saisissable d'un appel (sections A à H). Aucun champ n'est obligatoire (urgence).</summary>
public sealed record AppelFormulaire(
    string? Canal,
    string? Nature,
    AppelRequerant Requerant,
    AppelLocalisation Localisation,
    AppelMotif Motif,
    IReadOnlyList<AppelPersonne> Personnes,
    IReadOnlyList<AppelVehicule> Vehicules,
    AppelSecurite Securite,
    AppelEngagement Engagement);

public sealed record AppelDto(
    long Id,
    string Numero,
    string Etat,
    DateTimeOffset RecuLe,
    DateTimeOffset? RaccrocheLe,
    string Operateur,
    long? OperateurId,
    string? Service,
    string? SimcallId,
    long? InterventionId,
    string? InterventionNumero,
    string? MotifCloture,
    AppelFormulaire Formulaire,
    DateTimeOffset MajLe,
    long Version);

public sealed record AppelResumeDto(
    long Id,
    string Numero,
    string Etat,
    DateTimeOffset RecuLe,
    string? Canal,
    string? Motif,
    string? Priorite,
    string? Commune,
    string Operateur,
    long? InterventionId,
    string? InterventionNumero);

/// <summary>Corps de POST /api/pegase/appels : nouvel appel vierge.</summary>
public sealed record AppelCreationInput(string? Canal);

/// <summary>
/// Actions sur un appel : le formulaire (s'il est fourni) est enregistré d'abord.
/// <paramref name="InterventionId"/> : rattachement ; <paramref name="EquipageId"/> : engagement immédiat à la création
/// (permission pegase.intervention.affecter) ; <paramref name="Motif"/> : mise en attente ou clôture sans suite.
/// </summary>
public sealed record AppelActionInput(AppelFormulaire? Formulaire, long? Version, string? Motif, long? InterventionId, long? EquipageId);

// ----- Interventions (7.1, 7.2) -----

public sealed record InterventionLigneDto(
    long Id,
    string Numero,
    string Nature,
    string? SousMotif,
    string Priorite,
    string? Adresse,
    string? Commune,
    DateTimeOffset DebutLe,
    string Statut,
    IReadOnlyList<string> Equipages,
    int NombreAppels,
    DateTimeOffset MajLe);

public sealed record EngagementDto(
    long Id,
    long EquipageId,
    string Indicatif,
    string? Chef,
    string? Agents,
    string? Vehicule,
    string Statut,
    string StatutEquipage,
    bool Renfort,
    bool Actif,
    DateTimeOffset DemandeLe,
    DateTimeOffset? AffecteLe,
    string? AffectePar,
    DateTimeOffset? EnRouteLe,
    DateTimeOffset? SurPlaceLe,
    DateTimeOffset? TermineLe);

public sealed record ChronologieDto(long Id, DateTimeOffset Date, string Type, string Texte, string Auteur);

public sealed record CompteRenduInput(
    long? EquipageId,
    string? Indicatif,
    string? ChefDeBord,
    DateTimeOffset? ArriveeLe,
    DateTimeOffset? DepartLe,
    DateTimeOffset? FinLe,
    string? Resultat,
    string? Faits,
    string? Mesures,
    string? PersonnesControlees,
    string? Interpellations,
    string? Victimes,
    string? Vehicules,
    string? Secours,
    string? Transport,
    string? Destination,
    string? Documents,
    bool SuiteJudiciaire,
    string? Observations,
    long? Version);

public sealed record CompteRenduDto(
    long Id,
    long InterventionId,
    string InterventionNumero,
    long? EquipageId,
    string? Indicatif,
    string? ChefDeBord,
    DateTimeOffset? ArriveeLe,
    DateTimeOffset? DepartLe,
    DateTimeOffset? FinLe,
    string? Resultat,
    string? Faits,
    string? Mesures,
    string? PersonnesControlees,
    string? Interpellations,
    string? Victimes,
    string? Vehicules,
    string? Secours,
    string? Transport,
    string? Destination,
    string? Documents,
    bool SuiteJudiciaire,
    string? Observations,
    string Statut,
    string Redacteur,
    string? ValidePar,
    DateTimeOffset? ValideLe,
    DateTimeOffset CreeLe,
    long Version);

public sealed record InterventionDto(
    long Id,
    string Numero,
    string Nature,
    string? SousMotif,
    string Priorite,
    string? Description,
    string? Adresse,
    string? Commune,
    string? CodePostal,
    double? X,
    double? Y,
    string? Fiabilite,
    string? Requerant,
    string Statut,
    DateTimeOffset DebutLe,
    DateTimeOffset? ClotureLe,
    string? MotifStatut,
    int? EquipagesDemandes,
    string? CategorieMoyen,
    bool SuiteJudiciaire,
    string? ProcedureLrppn,
    string? Suites,
    string? Documents,
    string CreePar,
    AppelSecurite? Securite,
    IReadOnlyList<AppelResumeDto> Appels,
    IReadOnlyList<AppelPersonne> Personnes,
    IReadOnlyList<AppelVehicule> Vehicules,
    IReadOnlyList<EngagementDto> Engagements,
    IReadOnlyList<ChronologieDto> Chronologie,
    IReadOnlyList<CompteRenduDto> ComptesRendus,
    IReadOnlyList<MentionResumeDto> Mentions,
    long Version);

/// <summary>Modification de la qualification ; <paramref name="Justification"/> obligatoire si la priorité change (8.4).</summary>
public sealed record InterventionModificationInput(
    string Nature,
    string? SousMotif,
    string Priorite,
    string? Description,
    string? Adresse,
    string? Commune,
    string? CodePostal,
    double? X,
    double? Y,
    int? EquipagesDemandes,
    string? CategorieMoyen,
    string? Justification,
    long Version);

public sealed record InterventionStatutInput(string Statut, string? Motif);

public sealed record EngagementInput(long EquipageId, bool Renfort);

public sealed record EquipageStatutInput(string Statut, string? Motif);

public sealed record MessageInput(string Texte);

public sealed record MotifInput(string? Motif);

/// <summary>Références de l'intervention (procédure LRPPN, suites, documents).</summary>
public sealed record ReferencesInput(string? ProcedureLrppn, string? Suites, string? Documents);

/// <summary>Message ou événement de la chronologie, pour le menu « Communications ».</summary>
public sealed record CommunicationDto(long Id, DateTimeOffset Date, long InterventionId, string InterventionNumero, string Type, string Texte, string Auteur);

// ----- TN du jour (8.2) -----

public sealed record TnDto(long Id, long AgentId, string Agent, string? Service, DateTimeOffset Debut, DateTimeOffset? Fin, string Statut);

public sealed record TnInput(string? Service);

// ----- Tableau de bord et statistiques -----

public sealed record PegaseTableauDeBordDto(
    int AppelsOuverts,
    int InterventionsAQualifier,
    int InterventionsEnAttente,
    int InterventionsEnCours,
    int ComptesRendusAttendus,
    int EquipagesDisponibles,
    int EquipagesEngages,
    TnDto? Tn,
    IReadOnlyList<InterventionLigneDto> Interventions,
    IReadOnlyList<AppelResumeDto> Appels,
    IReadOnlyList<EquipageDto> Equipages);

public sealed record StatistiqueLigneDto(string Libelle, int Nombre);

public sealed record PegaseStatistiquesDto(
    DateTimeOffset Du,
    DateTimeOffset Au,
    int Appels,
    int Interventions,
    int Cloturees,
    int Annulees,
    double? DelaiAffectationMinutes,
    double? DelaiArriveeMinutes,
    IReadOnlyList<StatistiqueLigneDto> ParMotif,
    IReadOnlyList<StatistiqueLigneDto> ParPriorite,
    IReadOnlyList<StatistiqueLigneDto> ParStatut,
    IReadOnlyList<StatistiqueLigneDto> ParCanal,
    IReadOnlyList<StatistiqueLigneDto> ParCommune);

// ----- Intégration SIMCALL (serveur à serveur, en-tête X-Saphir-Key) — contrat inchangé depuis SAPHIR 0.1 -----

/// <summary>SIMCALL → SAPHIR : un opérateur vient de décrocher un appel au 17 (POST /api/integrations/simcall/answered).</summary>
public sealed record SimcallCallAnswered(
    string SimcallCallId,
    string DiscordUserId,
    string OperatorName,
    string? CallerNumber,
    DateTimeOffset AnsweredAt);

/// <summary>SIMCALL → SAPHIR : l'appel est terminé (POST /api/integrations/simcall/ended).</summary>
public sealed record SimcallCallEnded(string SimcallCallId, DateTimeOffset EndedAt);

// ----- GÉOOPS (8.1) -----

/// <summary>Position envoyée par le script FiveM (coordonnées du jeu). Identifiant : Discord sinon numéro de joueur.</summary>
public sealed record GeoopsPositionInput(
    string? DiscordId,
    int? ServerId,
    string? Nom,
    double X,
    double Y,
    double Z,
    double? Cap,
    double? Vitesse,
    string? Vehicule);

/// <summary>Calibration de l'image : coordonnées du jeu des bords gauche (XMin), droit (XMax), bas (YMin) et haut (YMax).</summary>
public sealed record GeoopsCalibrationDto(double XMin, double XMax, double YMin, double YMax, bool CarteDisponible, int PeremptionSecondes);

/// <summary>Image de fond pour le poste (base64), <paramref name="Version"/> change quand le fichier change.</summary>
public sealed record GeoopsCarteDto(string ContentType, string Donnees, string Version);

/// <summary>
/// Équipage positionné. <paramref name="Source"/> : « Simulation FiveM » (position du jeu) ou « Déclarée »
/// (saisie par un opérateur) : jamais une position GPS mesurée.
/// </summary>
public sealed record GeoopsEquipageDto(
    long EquipageId,
    string Indicatif,
    string Statut,
    long? InterventionId,
    string? InterventionNumero,
    double X,
    double Y,
    double? Cap,
    double? Vitesse,
    string? Vehicule,
    string Source,
    string? Agent,
    DateTimeOffset Le);

/// <summary>Agent en service sans équipage constitué.</summary>
public sealed record GeoopsAgentDto(string Nom, double X, double Y, DateTimeOffset Le);

public sealed record GeoopsInterventionDto(long Id, string Numero, string Nature, string Priorite, string Statut, string? Adresse, double X, double Y);

public sealed record GeoopsSituationDto(
    GeoopsCalibrationDto Calibration,
    IReadOnlyList<GeoopsEquipageDto> Equipages,
    IReadOnlyList<GeoopsAgentDto> AutresAgents,
    IReadOnlyList<GeoopsInterventionDto> Interventions,
    IReadOnlyList<GeoopsInterventionDto> NonPlacees,
    DateTimeOffset Le);

/// <summary>Position déclarée (équipage) ou position d'une intervention, en coordonnées du jeu.</summary>
public sealed record PositionInput(double X, double Y);
