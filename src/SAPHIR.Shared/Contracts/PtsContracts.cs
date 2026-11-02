namespace Saphir.Shared.Contracts;

// =====================================================================================
// PTS (police technique et scientifique) : FAED (empreintes), FNAEG (ADN), FIJAIS (auteurs
// d'infractions sexuelles ou violentes), FIJAIT (auteurs d'infractions terroristes), et traces
// relevées sur les scènes, rapprochées ensuite d'une personne.
// Les personnes sont les mêmes que dans MEO-FIC (clés « s:… » ou « f:… »).
// =====================================================================================

public static class PtsFichiers
{
    public const string Faed = "FAED";
    public const string Fnaeg = "FNAEG";
    public const string Fijais = "FIJAIS";
    public const string Fijait = "FIJAIT";
}

/// <summary>Signalisation d'une personne au FAED (empreintes) ou au FNAEG (profil ADN).</summary>
public sealed record SignalisationDto(
    long Id,
    string Fichier,
    string Reference,
    string DateSignalisation,
    string? Service,
    string? Motif,
    string? Details,
    string Auteur,
    DateTimeOffset CreeLe);

public sealed record SignalisationInput(
    string Fichier,
    string Reference,
    string DateSignalisation,
    string? Service,
    string? Motif,
    string? Details);

/// <summary>Inscription au FIJAIS ou au FIJAIT.</summary>
public sealed record InscriptionDto(
    long Id,
    string Fichier,
    string Infraction,
    string Decision,
    string? DateDecision,
    string? Juridiction,
    string? Obligations,
    string? FinInscription,
    string? Details,
    string Auteur,
    DateTimeOffset CreeLe);

public sealed record InscriptionInput(
    string Fichier,
    string Infraction,
    string Decision,
    string? DateDecision,
    string? Juridiction,
    string? Obligations,
    string? FinInscription,
    string? Details);

/// <summary>Trace papillaire (FAED) ou génétique (FNAEG) relevée sur une scène.</summary>
public sealed record TraceDto(
    long Id,
    string Fichier,
    string Reference,
    string? Affaire,
    string? Lieu,
    string? DatePrelevement,
    string? Description,
    string? PersonneKey,
    string? PersonneNom,
    string? RapprochePar,
    DateTimeOffset? RapprocheLe,
    string Auteur,
    DateTimeOffset CreeLe)
{
    public bool EstIdentifiee => PersonneKey is not null;
}

public sealed record TraceInput(
    string Fichier,
    string Reference,
    string? Affaire,
    string? Lieu,
    string? DatePrelevement,
    string? Description);

public sealed record RapprochementInput(string PersonneKey);

/// <summary>Données PTS d'une personne (en plus de sa fiche d'identité MEO-FIC).</summary>
public sealed record PtsPersonDto(
    string Key,
    IReadOnlyList<SignalisationDto> Signalisations,
    IReadOnlyList<InscriptionDto> Inscriptions,
    IReadOnlyList<TraceDto> Traces);

public static class PtsReferentiel
{
    public static readonly IReadOnlyList<string> FichiersSignalisation = [PtsFichiers.Faed, PtsFichiers.Fnaeg];
    public static readonly IReadOnlyList<string> FichiersInscription = [PtsFichiers.Fijais, PtsFichiers.Fijait];

    public static readonly IReadOnlyList<string> Decisions =
    [
        "Condamnation définitive",
        "Condamnation non définitive",
        "Mise en examen",
        "Composition pénale",
        "Décision d'irresponsabilité pénale",
        "Décision étrangère"
    ];

    public static readonly IReadOnlyList<string> Obligations =
    [
        "Justifier de son adresse tous les ans",
        "Justifier de son adresse tous les 6 mois",
        "Justifier de son adresse tous les 3 mois",
        "Se présenter tous les mois au service",
        "Déclarer tout déplacement à l'étranger",
        "Aucune obligation de présentation"
    ];
}
