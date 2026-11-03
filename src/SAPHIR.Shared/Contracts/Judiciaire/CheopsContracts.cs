namespace Saphir.Shared.Contracts.Judiciaire;

// =====================================================================================
// CHEOPS — fichiers de police fictifs (phase 6). Contrat des routes : docs/API-JUDICIAIRE.md.
// Toutes les données sont fictives (jeu de rôle VDMS).
// =====================================================================================

/// <summary>Messages temps réel du module (Text = identifiant de l'objet modifié, jamais de donnée personnelle).</summary>
public static class JudiciaireRealtime
{
    /// <summary>Fiche CHEOPS créée ou modifiée (personne, véhicule, FPR, FOVeS…).</summary>
    public const string CheopsChanged = "cheops-changed";

    /// <summary>Procédure, acte ou pièce LRPPN modifié (Text = identifiant de la procédure).</summary>
    public const string LrppnChanged = "lrppn-changed";
}

/// <summary>Fichier consultable depuis le portail CHEOPS, avec la permission qui l'ouvre.</summary>
public sealed record FichierCheopsInfo(string Code, string Libelle, string Description, string Permission);

/// <summary>Les fichiers du portail (11.1) et leurs permissions.</summary>
public static class FichiersCheops
{
    public const string Taj = "TAJ";
    public const string Fpr = "FPR";
    public const string Foves = "FOVeS";
    public const string Siv = "SIV";
    public const string Fva = "FVA";
    public const string Snpc = "SNPC";
    public const string Faed = "FAED";
    public const string Fnaeg = "FNAEG";
    public const string Fijaisv = "FIJAISV";
    public const string Fijait = "FIJAIT";
    public const string Anadoc = "ANADOC";

    public static readonly IReadOnlyList<FichierCheopsInfo> All =
    [
        new(Taj, "TAJ", "Traitement des antécédents judiciaires (fictif)", Permissions.CheopsTaj),
        new(Fpr, "FPR", "Fichier des personnes recherchées (fictif)", Permissions.CheopsFpr),
        new(Foves, "FOVeS", "Objets et véhicules signalés (fictif)", Permissions.CheopsVehicules),
        new(Siv, "SIV", "Immatriculation des véhicules (fictif)", Permissions.CheopsVehicules),
        new(Fva, "FVA", "Véhicules assurés (fictif)", Permissions.CheopsVehicules),
        new(Snpc, "SNPC", "Permis de conduire (fictif)", Permissions.CheopsSnpc),
        new(Faed, "FAED", "Empreintes digitales (fictif)", Permissions.CheopsScientifique),
        new(Fnaeg, "FNAEG", "Empreintes génétiques (fictif)", Permissions.CheopsScientifique),
        new(Fijaisv, "FIJAISV", "Auteurs d'infractions sexuelles ou violentes (fictif)", Permissions.CheopsFijais),
        new(Fijait, "FIJAIT", "Auteurs d'infractions terroristes (fictif)", Permissions.CheopsFijais),
        new(Anadoc, "ANADOC", "Recherche documentaire dans les actes LRPPN validés", Permissions.CheopsAnadoc)
    ];

    public static string? Permission(string fichier) => All.FirstOrDefault(f => f.Code == fichier)?.Permission;
}

/// <summary>Bandeau d'alerte (rouge) affiché en tête de toute fiche : FPR actif, FOVeS actif.</summary>
public sealed record AlerteBandeauDto(string Fichier, string Reference, string Titre, string ConduiteATenir);

// ===== Personnes =====

public sealed record AliasDto(long Id, string Alias);

public sealed record DocumentIdentiteDto(long Id, string Type, string Numero, DateOnly? DelivreLe, DateOnly? ExpireLe, string? Autorite);

public sealed record DocumentIdentiteInput(string Type, string Numero, DateOnly? DelivreLe, DateOnly? ExpireLe, string? Autorite);

/// <summary>Fiche d'identité complète (CHEOPS, LRPPN). <see cref="Source"/> : « SAPHIR » ou « FiveM ».</summary>
public sealed record PersonneFicheDto(
    long Id,
    string Reference,
    string Nom,
    string Prenom,
    DateOnly? DateNaissance,
    string? LieuNaissance,
    string? Sexe,
    string? Nationalite,
    string? Adresse,
    string? Telephone,
    string? Signalement,
    string Source,
    IReadOnlyList<AliasDto> Alias,
    IReadOnlyList<DocumentIdentiteDto> Documents,
    IReadOnlyList<string> Alertes,
    IReadOnlyList<AlerteBandeauDto> Bandeaux,
    DateTimeOffset CreeLe,
    DateTimeOffset MajLe)
{
    public string Libelle => DateNaissance is { } d ? $"{Nom} {Prenom} (né(e) le {d:dd/MM/yyyy})" : $"{Nom} {Prenom}";
}

/// <summary>Modification d'une identité (historisée, motif conseillé).</summary>
public sealed record PersonneModificationInput(
    string Nom,
    string Prenom,
    DateOnly? DateNaissance,
    string? LieuNaissance,
    string? Sexe,
    string? Nationalite,
    string? Adresse,
    string? Telephone,
    string? Signalement,
    IReadOnlyList<string>? Alias,
    string? Motif);

// ===== Recherche générale (11.2) =====

/// <summary>Correspondance dans un fichier (numéro de dossier, référence, procédure).</summary>
public sealed record CorrespondanceDto(string Fichier, long Id, string Reference, string Libelle, long? PersonneId, long? VehiculeId);

public sealed record RechercheGeneraleDto(
    IReadOnlyList<PersonneResumeDto> Personnes,
    IReadOnlyList<VehiculeResumeDto> Vehicules,
    IReadOnlyList<CorrespondanceDto> Dossiers);

// ===== TAJ (12.1) =====

public static class TajQualites
{
    public const string MisEnCause = "Mis en cause";
    public const string Victime = "Victime";
    public static readonly IReadOnlyList<string> All = [MisEnCause, Victime];
}

public sealed record TajFaitDto(
    long Id,
    string Reference,
    long PersonneId,
    string Qualite,
    string Faits,
    string? Natinf,
    DateTimeOffset? FaitsLe,
    string? Lieu,
    string? Service,
    string? ProcedureNumero,
    long? ProcedureId,
    string? Suite,
    string Source,
    DateTimeOffset CreeLe,
    string CreePar);

/// <summary>Ligne de résultat TAJ : une personne et ses faits.</summary>
public sealed record TajLigneDto(PersonneResumeDto Personne, int NombreFaits, DateTimeOffset? DernierFait);

public sealed record TajFicheDto(PersonneFicheDto Personne, IReadOnlyList<TajFaitDto> Faits, IReadOnlyList<HistoriqueDto> Historique);

public sealed record TajFaitInput(
    long PersonneId,
    string Qualite,
    string Faits,
    string? Natinf,
    DateTimeOffset? FaitsLe,
    string? Lieu,
    string? Service,
    string? ProcedureNumero,
    string? Suite);

public sealed record TajSuiteInput(string? Suite, string? Motif);

// ===== FPR (12.2) =====

public static class FprStatuts
{
    public const string Active = "Active";
    public const string Suspendue = "Suspendue";
    public const string Levee = "Levée";
    public const string Archivee = "Archivée";
    public static readonly IReadOnlyList<string> All = [Active, Suspendue, Levee, Archivee];
}

public static class FprCategories
{
    public static readonly IReadOnlyList<string> All =
    [
        "PJ — Recherche de police judiciaire",
        "J — Exécution d'une décision judiciaire",
        "V — Évadé",
        "M — Mineur fugueur",
        "D — Disparition inquiétante",
        "IT — Interdiction du territoire",
        "E — Police administrative des étrangers",
        "S — Atteinte à la sûreté (signalement)"
    ];
}

public sealed record FprFicheDto(
    long Id,
    string Numero,
    PersonneResumeDto Personne,
    string Categorie,
    string Motif,
    string ConduiteATenir,
    string? ProcedureNumero,
    string? Service,
    DateTimeOffset CreeLe,
    DateOnly? ReexamenLe,
    string Statut,
    string? Informations,
    bool Dangereux,
    bool Arme,
    string CreePar,
    IReadOnlyList<HistoriqueDto> Historique);

public sealed record FprInput(
    long PersonneId,
    string Categorie,
    string Motif,
    string ConduiteATenir,
    string? ProcedureNumero,
    string? Service,
    DateOnly? ReexamenLe,
    string? Informations,
    bool Dangereux,
    bool Arme);

/// <summary>Changement de statut (FPR, FIJAIS, procédure…) : motif obligatoire selon le cas.</summary>
public sealed record StatutInput(string Statut, string? Motif);

// ===== FOVeS (12.3) =====

public static class FovesStatuts
{
    public const string Actif = "Signalement actif";
    public const string Decouvert = "Découvert";
    public const string Leve = "Levé";
    public const string Archive = "Archivé";
    public static readonly IReadOnlyList<string> All = [Actif, Decouvert, Leve, Archive];
}

public static class FovesTypes
{
    public static readonly IReadOnlyList<string> All =
        ["Véhicule", "Plaque d'immatriculation", "Arme", "Téléphone", "Document", "Objet d'art", "Autre objet"];

    public static readonly IReadOnlyList<string> Natures =
        ["Vol", "Vol avec violence", "Détournement", "Perte", "Surveillance", "Usurpation de plaques"];
}

public sealed record FovesFicheDto(
    long Id,
    string Reference,
    string TypeObjet,
    string Identifiant,
    long? VehiculeId,
    string? Description,
    string Nature,
    DateTimeOffset SignaleLe,
    string? Service,
    string? ProcedureNumero,
    string Statut,
    DateTimeOffset? DecouvertLe,
    string? LieuDecouverte,
    string? ConduiteATenir,
    string CreePar,
    IReadOnlyList<HistoriqueDto> Historique);

public sealed record FovesInput(
    string TypeObjet,
    string Identifiant,
    string? Description,
    string Nature,
    DateTimeOffset? SignaleLe,
    string? Service,
    string? ProcedureNumero,
    string? ConduiteATenir);

public sealed record FovesStatutInput(string Statut, DateTimeOffset? DecouvertLe, string? LieuDecouverte, string? Motif);

// ===== SIV (12.4) et FVA (12.5) =====

public static class SivSituations
{
    public const string Normale = "Normale";
    public static readonly IReadOnlyList<string> All =
        [Normale, "Opposition (OTCI)", "Gage", "Véhicule volé", "Immobilisation", "Véhicule détruit"];

    public static readonly IReadOnlyList<string> Categories = ["VP", "VU", "Moto", "Cyclomoteur", "Poids lourd", "Remorque"];
}

public static class AssuranceStatuts
{
    public const string Valide = "Valide";
    public const string Resilie = "Résilié";
    public const string Suspendu = "Suspendu";
    public static readonly IReadOnlyList<string> All = [Valide, Suspendu, Resilie];
}

public sealed record AssuranceDto(
    long Id,
    long VehiculeId,
    string Immatriculation,
    string Assureur,
    string Contrat,
    DateOnly Debut,
    DateOnly? Fin,
    string Statut,
    bool Couvert,
    DateTimeOffset? VerifieLe);

public sealed record AssuranceInput(long VehiculeId, string Assureur, string Contrat, DateOnly Debut, DateOnly? Fin, string Statut);

/// <summary>Résultat FVA : véhicule et contrat en cours (null = aucun contrat couvrant aujourd'hui).</summary>
public sealed record FvaLigneDto(VehiculeResumeDto Vehicule, string Situation, AssuranceDto? Contrat);

public sealed record VehiculeFicheDto(
    long Id,
    string Immatriculation,
    string? Vin,
    string? Marque,
    string? Modele,
    string? Version,
    string? Categorie,
    string? Couleur,
    DateOnly? MiseEnCirculation,
    long? TitulaireId,
    string? Titulaire,
    string? Adresse,
    string Situation,
    string Source,
    IReadOnlyList<string> Alertes,
    IReadOnlyList<AlerteBandeauDto> Bandeaux,
    IReadOnlyList<AssuranceDto> Assurances,
    IReadOnlyList<FovesFicheDto> Signalements,
    IReadOnlyList<HistoriqueDto> Historique);

public sealed record VehiculeInput(
    string Immatriculation,
    string? Vin,
    string? Marque,
    string? Modele,
    string? Version,
    string? Categorie,
    string? Couleur,
    DateOnly? MiseEnCirculation,
    long? TitulaireId,
    string? TitulaireLibre,
    string? Adresse,
    string? Situation,
    string? Motif);

// ===== SNPC (12.6) =====

public static class PermisSituations
{
    public const string Valide = "Valide";
    public const string Probatoire = "Probatoire";
    public const string Suspendu = "Suspendu";
    public const string Annule = "Annulé";
    public const string Invalide = "Invalidé (solde nul)";
    public static readonly IReadOnlyList<string> All = [Valide, Probatoire, Suspendu, Annule, Invalide];
}

public sealed record PermisDto(
    long Id,
    PersonneResumeDto Personne,
    string Reference,
    string Categories,
    string? DatesObtention,
    DateOnly? ValiditeJusquAu,
    string Situation,
    int Points,
    int PointsMax,
    IReadOnlyList<HistoriqueDto> Historique);

public sealed record PermisInput(
    string Categories,
    string? DatesObtention,
    DateOnly? ValiditeJusquAu,
    string Situation,
    int Points,
    string? Motif);

/// <summary>Retrait (négatif) ou restitution (positif) de points simulés.</summary>
public sealed record PointsInput(int Variation, string Motif);

// ===== FAED / FNAEG (12.7, 12.8) =====

public static class ScientifiqueResultats
{
    public const string EnAttente = "En attente";
    public static readonly IReadOnlyList<string> All = [EnAttente, "Concordance", "Pas de concordance", "Inexploitable"];
    public static readonly IReadOnlyList<string> TypesFaed = ["Empreintes décadactylaires", "Trace papillaire", "Empreinte palmaire"];
    public static readonly IReadOnlyList<string> TypesFnaeg = ["Prélèvement biologique (simulé)", "Trace de sang", "Trace de contact", "Trace salivaire"];
}

/// <summary>Enregistrement FAED ou FNAEG fictif (aucune donnée biométrique réelle).</summary>
public sealed record ScientifiqueDto(
    long Id,
    string Fichier,
    string Reference,
    string? ReferencePrelevement,
    string Origine,
    string? ProcedureNumero,
    string Type,
    DateTimeOffset PreleveLe,
    long? PersonneId,
    string? Personne,
    string Resultat,
    string? Rapport,
    string CreePar,
    IReadOnlyList<HistoriqueDto> Historique);

public sealed record ScientifiqueInput(
    string Fichier,
    string? ReferencePrelevement,
    string Origine,
    string? ProcedureNumero,
    string Type,
    DateTimeOffset? PreleveLe,
    long? PersonneId,
    string? Rapport);

public sealed record ResultatInput(string Resultat, long? PersonneId, string? Rapport, string? Motif);

// ===== FIJAISV / FIJAIT (12.9) et casier de simulation =====

public static class FijaisStatuts
{
    public const string Inscrit = "Inscrit";
    public static readonly IReadOnlyList<string> All = [Inscrit, "Obligations suspendues", "Effacé"];
}

public sealed record FijaisDto(
    long Id,
    string Fichier,
    string Reference,
    PersonneResumeDto Personne,
    string ReferenceDecision,
    string? Juridiction,
    string? Infraction,
    DateOnly? DecisionLe,
    DateOnly? FinInscription,
    string Statut,
    string? ServiceGestionnaire,
    string? Obligations,
    string? Restrictions,
    IReadOnlyList<HistoriqueDto> Historique);

public sealed record FijaisInput(
    string Fichier,
    long PersonneId,
    string ReferenceDecision,
    string? Juridiction,
    string? Infraction,
    DateOnly? DecisionLe,
    DateOnly? FinInscription,
    string? ServiceGestionnaire,
    string? Obligations,
    string? Restrictions);

/// <summary>Décision judiciaire fictive validée : seule source du casier de simulation (14.3).</summary>
public sealed record DecisionDto(
    long Id,
    long PersonneId,
    string Reference,
    string Juridiction,
    DateOnly DecisionLe,
    string Nature,
    string Infraction,
    string? Peine,
    DateTimeOffset CreeLe,
    string CreePar);

public sealed record DecisionInput(long PersonneId, string Juridiction, DateOnly DecisionLe, string Nature, string Infraction, string? Peine);

// ===== ANADOC (12.10) =====

public sealed record AnadocResultatDto(
    long Id,
    string Source,
    long ProcedureId,
    string ProcedureNumero,
    string? NumeroPv,
    string TypeActe,
    string Titre,
    DateTimeOffset Date,
    string? Service,
    string? Redacteur,
    string? Personnes,
    string Extrait);

public sealed record AnadocDocumentDto(AnadocResultatDto Resultat, string Texte, long? ActeId, long? PieceId);
