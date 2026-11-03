namespace Saphir.Shared.Contracts.Judiciaire;

// =====================================================================================
// LRPPN — procédures judiciaires de simulation (phase 7). Contrat des routes : docs/API-JUDICIAIRE.md.
// =====================================================================================

public static class ProcedureStatuts
{
    public const string EnCours = "En cours";
    public const string Transmise = "Transmise au parquet";
    public const string Cloturee = "Clôturée";
    public const string Archivee = "Archivée";
    public static readonly IReadOnlyList<string> All = [EnCours, Transmise, Cloturee, Archivee];
}

public static class ProcedureReferentiels
{
    public static readonly IReadOnlyList<string> Cadres =
    [
        "Flagrant délit", "Enquête préliminaire", "Commission rogatoire", "Recherche des causes de la mort",
        "Recherche des causes d'une disparition", "Enquête administrative"
    ];

    public static readonly IReadOnlyList<string> TypesAffaire =
    [
        "Atteintes aux personnes", "Atteintes aux biens", "Stupéfiants", "Infractions routières",
        "Ordre public", "Escroqueries et fraudes", "Armes", "Autre"
    ];

    public static readonly IReadOnlyList<string> Origines =
    [
        "Intervention PÉGASE", "Plainte", "Initiative du service", "Instruction du parquet", "Signalement", "Autre"
    ];

    public static readonly IReadOnlyList<string> QualitesPersonne =
    [
        "Mis en cause", "Victime", "Plaignant", "Témoin", "Personne entendue librement", "Requérant", "Autre"
    ];

    public static readonly IReadOnlyList<string> RolesVehicule =
        ["Véhicule mis en cause", "Véhicule victime", "Véhicule saisi", "Véhicule de service", "Autre"];

    public static readonly IReadOnlyList<string> QualitesFonctionnelles =
        ["OPJ", "APJ", "APJA", "Enquêteur"];
}

/// <summary>Création ou modification d'une procédure (13.3). <see cref="Version"/> : détection des conflits.</summary>
public sealed record ProcedureInput(
    string? Service,
    string? Unite,
    long ResponsableId,
    string? ResponsableGrade,
    string? QualiteFonctionnelle,
    DateTimeOffset OuverteLe,
    string Cadre,
    string TypeAffaire,
    string Intitule,
    long? InterventionId,
    string? InterventionNumero,
    string? MainCouranteReference,
    string? Qualification,
    DateTimeOffset? FaitsLe,
    string? Commune,
    string? Adresse,
    string? Description,
    string? Origine,
    int? Version,
    string? Motif);

public sealed record ProcedureLigneDto(
    long Id,
    string Numero,
    string Intitule,
    string TypeAffaire,
    string Cadre,
    string Statut,
    string? Service,
    string Responsable,
    DateTimeOffset OuverteLe,
    int NombreActes,
    int ActesAValider,
    string? InterventionNumero);

public sealed record ProcedurePersonneDto(long Id, PersonneResumeDto Personne, string Qualite, string? Observations, DateTimeOffset AjouteLe);

public sealed record ProcedurePersonneInput(long PersonneId, string Qualite, string? Observations);

public sealed record ProcedureVehiculeDto(long Id, VehiculeResumeDto Vehicule, string Role, string? Observations);

public sealed record ProcedureVehiculeInput(long VehiculeId, string Role, string? Observations);

public sealed record ProcedureLienDto(long Id, long ProcedureId, string Numero, string Intitule);

/// <summary>Lien vers une autre procédure, par son numéro.</summary>
public sealed record ProcedureLienInput(string Numero);

/// <summary>Validation d'un acte (journal des validations, règle 17).</summary>
public sealed record ValidationDto(long ActeId, string Numero, string Titre, DateTimeOffset ValideLe, string ValidePar, string Empreinte);

public sealed record ProcedureDto(
    long Id,
    string Numero,
    string Statut,
    string? Service,
    string? Unite,
    long? ResponsableId,
    string Responsable,
    string? ResponsableRio,
    string? ResponsableGrade,
    string? QualiteFonctionnelle,
    DateTimeOffset OuverteLe,
    string Cadre,
    string TypeAffaire,
    string Intitule,
    long? InterventionId,
    string? InterventionNumero,
    string? MainCouranteReference,
    string? Qualification,
    DateTimeOffset? FaitsLe,
    string? Commune,
    string? Adresse,
    string? Description,
    string? Origine,
    int Version,
    DateTimeOffset CreeLe,
    string CreePar,
    DateTimeOffset MajLe,
    IReadOnlyList<ProcedurePersonneDto> Personnes,
    IReadOnlyList<ProcedureVehiculeDto> Vehicules,
    IReadOnlyList<ActeLigneDto> Actes,
    IReadOnlyList<PieceDto> Pieces,
    IReadOnlyList<ProcedureLienDto> Liens,
    IReadOnlyList<ValidationDto> Validations);

// ===== Actes =====

public static class ActeStatuts
{
    public const string Brouillon = "Brouillon";
    public const string EnRelecture = "En relecture";
    public const string Valide = "Validé";
    public static readonly IReadOnlyList<string> All = [Brouillon, EnRelecture, Valide];
}

public sealed record ActeLigneDto(
    long Id,
    long ProcedureId,
    string ProcedureNumero,
    string Numero,
    string Type,
    string TypeLibelle,
    string Titre,
    string Statut,
    string Redacteur,
    string? Personne,
    DateTimeOffset MajLe,
    DateTimeOffset? ValideLe,
    string? ValidePar);

/// <summary>Agent cité dans un acte (rédacteur, interpellateur, intervenant…). Valeurs figées à la validation.</summary>
public sealed record ActeAgentDto(long? AgentId, string Role, string Libelle, string? Rio, string? Grade, string? Service);

public sealed record QuestionReponseDto(string Question, string Reponse);

/// <summary>Mention ajoutée après validation (l'acte validé n'est jamais réécrit).</summary>
public sealed record MentionDto(long Id, string Texte, string Auteur, DateTimeOffset CreeLe);

public sealed record ActeDto(
    long Id,
    long ProcedureId,
    string ProcedureNumero,
    string Numero,
    string Type,
    string Titre,
    string Statut,
    long? PersonneId,
    string? Personne,
    long? MisEnCauseId,
    string? MisEnCause,
    long? RedacteurId,
    string Redacteur,
    string? RedacteurRio,
    string? RedacteurGrade,
    string? RedacteurService,
    IReadOnlyDictionary<string, string?> Champs,
    IReadOnlyList<ActeAgentDto> Agents,
    IReadOnlyList<QuestionReponseDto> QuestionsReponses,
    IReadOnlyList<long> PiecesIds,
    int Version,
    DateTimeOffset CreeLe,
    DateTimeOffset MajLe,
    DateTimeOffset? ValideLe,
    string? ValidePar,
    string? Empreinte,
    string? TexteFige,
    IReadOnlyList<MentionDto> Mentions,
    long? InterventionId,
    string? InterventionNumero);

/// <summary>Nouvel acte dans une procédure (14.1) : le serveur préremplit rédacteur, procédure, intervention et identité.</summary>
public sealed record ActeCreationInput(string Type, long? PersonneId);

/// <summary>Enregistrement d'un brouillon. <see cref="Version"/> : détection des conflits.</summary>
public sealed record ActeInput(
    string? Titre,
    long? PersonneId,
    long? MisEnCauseId,
    IReadOnlyDictionary<string, string?> Champs,
    IReadOnlyList<ActeAgentDto> Agents,
    IReadOnlyList<QuestionReponseDto> QuestionsReponses,
    IReadOnlyList<long> PiecesIds,
    int Version);

public sealed record MotifInput(string? Motif);

public sealed record MentionInput(string Texte);

/// <summary>Texte de l'acte tel qu'il sera généré (prévisualisation) ou figé (acte validé).</summary>
public sealed record ApercuDto(string Titre, string Texte, bool Fige);

// ===== Pièces et scellés =====

public static class PieceCategories
{
    public const string Piece = "Pièce";
    public const string Scelle = "Scellé";
    public const string Objet = "Objet";
    public const string Document = "Document";
    public const string Photographie = "Photographie";
    public const string Video = "Vidéo";
    public static readonly IReadOnlyList<string> All = [Piece, Scelle, Objet, Document, Photographie, Video];
}

public static class PieceMouvements
{
    public static readonly IReadOnlyList<string> All =
        ["Dépôt au greffe", "Sortie pour expertise", "Retour d'expertise", "Transfert de service", "Restitution", "Destruction"];
}

public sealed record MouvementPieceDto(long Id, DateTimeOffset Date, string Mouvement, string? Destination, string? Observations, string Auteur);

public sealed record PieceDto(
    long Id,
    long ProcedureId,
    string ProcedureNumero,
    string Numero,
    string Categorie,
    string Libelle,
    string? Description,
    string? NumeroScelle,
    string Statut,
    string? NomFichier,
    string? TypeMime,
    long Taille,
    string? Empreinte,
    long? ActeId,
    string? Localisation,
    DateTimeOffset CreeLe,
    string CreePar,
    IReadOnlyList<MouvementPieceDto> Mouvements);

/// <summary>Nouvelle pièce ; le fichier facultatif est envoyé en base64 (10 Mo au plus une fois décodé).</summary>
public sealed record PieceInput(
    string Categorie,
    string Libelle,
    string? Description,
    string? NumeroScelle,
    long? ActeId,
    string? Localisation,
    string? NomFichier,
    string? TypeMime,
    string? ContenuBase64);

public sealed record MouvementInput(string Mouvement, string? Destination, string? Observations);

/// <summary>Fichier transmis au poste (PDF, pièce jointe) : contenu en base64.</summary>
public sealed record FichierDto(string NomFichier, string TypeMime, string ContenuBase64);

// ===== Dossiers associés (14.3) =====

/// <summary>
/// Dossiers d'une personne selon les habilitations : une liste <c>null</c> = non autorisé pour l'agent
/// (le libellé du fichier figure alors dans <see cref="NonAutorises"/>).
/// </summary>
public sealed record DossiersAssociesDto(
    PersonneFicheDto Personne,
    IReadOnlyList<TajFaitDto>? Taj,
    IReadOnlyList<FprFicheDto>? Fpr,
    IReadOnlyList<ProcedureLigneDto>? Procedures,
    IReadOnlyList<DecisionDto>? Casier,
    IReadOnlyList<string> NonAutorises);

// ===== Modèles (15.5 : versionnés et administrables) =====

/// <summary>Trame d'introduction d'un type d'acte. Variables : {date}, {heure}, {redacteur}, {grade}, {rio}, {service}, {procedure}.</summary>
public sealed record ModeleDto(string Type, string Libelle, string Texte, int Version, DateTimeOffset MajLe, string MajPar);

public sealed record ModeleInput(string Texte, string? Motif);
