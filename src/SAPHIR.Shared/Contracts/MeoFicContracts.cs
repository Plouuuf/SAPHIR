namespace Saphir.Shared.Contracts;

// =====================================================================================
// MEO-FIC : consultation des fichiers (TAJ, FPR, SNPC pour les personnes ; FOVeS, SIV, FVA
// pour les véhicules). Les identités et véhicules viennent de la base FiveM (lecture seule)
// et de la base SAPHIR (fiches créées par les policiers) ; les inscriptions sont dans SAPHIR.
//
// Clé d'une personne : « s:<id> » (fiche SAPHIR) ou « f:<identifiant FiveM> ».
// Les dates saisies sont du texte « jj/mm/aaaa » (comme dans la base FiveM).
// =====================================================================================

/// <summary>Identité d'une personne (état civil et signalement).</summary>
public sealed record IdentityDto(
    string Nom,
    string Prenom,
    string? DateNaissance = null,
    string? LieuNaissance = null,
    string? Sexe = null,
    string? Nationalite = null,
    string? Adresse = null,
    string? Telephone = null,
    string? Taille = null,
    string? Signalement = null,
    string? Alias = null);

/// <summary>Ligne de résultat d'une recherche de personne.</summary>
public sealed record PersonSummaryDto(
    string Key,
    string Nom,
    string Prenom,
    string? DateNaissance,
    string? Sexe,
    string Source,
    bool FprActif,
    int TajCount,
    string? PermisStatut)
{
    public bool HasTaj => TajCount > 0;
}

/// <summary>Fiche complète d'une personne.</summary>
public sealed record PersonDetailDto(
    string Key,
    long? SaphirId,
    string? FivemId,
    string Source,
    IdentityDto Identite,
    IReadOnlyList<TajEntryDto> Taj,
    IReadOnlyList<FprEntryDto> Fpr,
    PermisDto? Permis,
    IReadOnlyList<VehicleSummaryDto> Vehicules);

/// <summary>Création ou correction d'une fiche personne SAPHIR.</summary>
public sealed record PersonInput(IdentityDto Identite);

// ----- TAJ : traitement des antécédents judiciaires -----

public sealed record TajEntryDto(
    long Id,
    string Qualite,
    string Infraction,
    string? Natinf,
    string DateFaits,
    string? Lieu,
    string? Service,
    string? Procedure,
    string? Details,
    string Auteur,
    DateTimeOffset CreeLe);

public sealed record TajInput(
    string Qualite,
    string Infraction,
    string? Natinf,
    string DateFaits,
    string? Lieu,
    string? Service,
    string? Procedure,
    string? Details);

// ----- FPR : fichier des personnes recherchées -----

public sealed record FprEntryDto(
    long Id,
    string Motif,
    string ConduiteATenir,
    string? Autorite,
    string? Details,
    bool Dangereux,
    bool Arme,
    bool Actif,
    string Auteur,
    DateTimeOffset CreeLe,
    string? ClosPar,
    DateTimeOffset? ClosLe);

public sealed record FprInput(
    string Motif,
    string ConduiteATenir,
    string? Autorite,
    string? Details,
    bool Dangereux,
    bool Arme);

// ----- SNPC : système national des permis de conduire -----

public sealed record PermisDto(
    string? Numero,
    string Categories,
    int Points,
    string Statut,
    string? DateObtention,
    string? Restrictions,
    string? MisAJourPar,
    DateTimeOffset? MisAJourLe);

public sealed record PermisInput(
    string? Numero,
    string Categories,
    int Points,
    string Statut,
    string? DateObtention,
    string? Restrictions);

// ----- Véhicules : SIV (immatriculation), FVA (assurance), FOVeS (signalements) -----

public sealed record VehicleSummaryDto(
    string Plaque,
    string? Marque,
    string? Modele,
    string? Couleur,
    string? Titulaire,
    string Source,
    bool Signale);

public sealed record SivDto(
    string? Marque,
    string? Modele,
    string? Couleur,
    string? Vin,
    string? DateMiseEnCirculation,
    string? Titulaire,
    string? TitulaireKey,
    string? ControleTechnique,
    string? Opposition,
    string Source);

public sealed record FvaDto(
    string Statut,
    string? Assureur,
    string? NumeroContrat,
    string? ValideJusquau);

public sealed record FovesEntryDto(
    long Id,
    string Motif,
    string ConduiteATenir,
    string? Details,
    bool Actif,
    string Auteur,
    DateTimeOffset CreeLe,
    string? ClosPar,
    DateTimeOffset? ClosLe);

public sealed record VehicleDetailDto(
    string Plaque,
    SivDto Siv,
    FvaDto Fva,
    IReadOnlyList<FovesEntryDto> Foves);

/// <summary>Création ou correction d'un véhicule SAPHIR (SIV + FVA). Complète les données FiveM.</summary>
public sealed record VehicleInput(
    string Plaque,
    string? Marque,
    string? Modele,
    string? Couleur,
    string? Vin,
    string? DateMiseEnCirculation,
    string? TitulaireKey,
    string? TitulaireNom,
    string? ControleTechnique,
    string? Opposition,
    string? Assureur,
    string? NumeroContrat,
    string? AssuranceValideJusquau);

public sealed record FovesInput(string Motif, string ConduiteATenir, string? Details);

/// <summary>Consultation ou modification d'un fichier (journal des accès, visible par l'administration).</summary>
public sealed record AuditEntryDto(
    DateTimeOffset Date,
    string Agent,
    string Action,
    string Cible);

/// <summary>Valeurs proposées dans les formulaires MEO-FIC.</summary>
public static class MeoFicReferentiel
{
    public static readonly IReadOnlyList<string> TajQualites = ["Mis en cause", "Victime"];

    /// <summary>Catégories de fiches FPR.</summary>
    public static readonly IReadOnlyList<string> FprMotifs =
    [
        "PJ — Recherche judiciaire",
        "J — Mandat (arrêt, amener, recherche)",
        "V — Évadé",
        "M — Mineur en fugue",
        "S — Sûreté de l'État",
        "E — Police générale",
        "IT — Interdiction du territoire",
        "T — Débiteur envers le Trésor",
        "R — Opposition à résidence",
        "D — Disparition inquiétante"
    ];

    public static readonly IReadOnlyList<string> ConduitesATenir =
    [
        "Interpeller et aviser l'autorité",
        "Conduire au service",
        "Retenir et aviser",
        "Signaler sans attirer l'attention",
        "Notifier la décision",
        "Aviser la famille"
    ];

    public static readonly IReadOnlyList<string> PermisStatuts =
        ["Valide", "Suspendu", "Annulé", "Invalidé (solde nul)", "Probatoire", "Sans permis"];

    public static readonly IReadOnlyList<string> FovesMotifs =
    [
        "Véhicule volé",
        "Plaques volées",
        "Recherche judiciaire",
        "Surveillance",
        "Immobilisation / fourrière",
        "Défaut d'assurance"
    ];
}
