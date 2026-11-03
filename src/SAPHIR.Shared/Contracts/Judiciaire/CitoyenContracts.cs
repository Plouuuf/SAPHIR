namespace Saphir.Shared.Contracts.Judiciaire;

// =====================================================================================
// Déclaration d'identité par le joueur, depuis le jeu (ressource FiveM saphir-identite).
// Routes d'intégration serveur à serveur : /api/integrations/identite/… (en-tête X-Saphir-Key).
// Contrat : docs/API-CITOYEN.md. Données fictives (jeu de rôle VDMS).
// =====================================================================================

/// <summary>Paramètres centraux du portail de déclaration.</summary>
public static class CitoyenParametres
{
    /// <summary>« oui » : déclarations acceptées ; toute autre valeur (« non ») : portail fermé.</summary>
    public const string PortailActif = "citoyen.portail.actif";

    /// <summary>Nombre maximal de personnages actifs par compte Discord (5 par défaut).</summary>
    public const string PersonnagesMax = "citoyen.personnages.max";
}

/// <summary>Joueur à l'origine de la demande : compte Discord, personnage FiveM (citizenid / identifier) et nom affiché.</summary>
public sealed record CitoyenJoueurInput(string DiscordId, string PersonnageId, string? NomJoueur);

/// <summary>
/// Déclaration ou mise à jour du personnage joué. NOM, prénom(s), naissance, sexe, nationalité, téléphone et taille viennent
/// du framework (lus côté serveur FiveM) ; lieu de naissance, adresse, profession et signalement sont saisis en jeu.
/// <paramref name="DateNaissance"/> : « jj/mm/aaaa » ou « aaaa-mm-jj ».
/// </summary>
public sealed record CitoyenIdentiteInput(
    string DiscordId,
    string PersonnageId,
    string? NomJoueur,
    string Nom,
    string Prenom,
    string? DateNaissance,
    string? LieuNaissance,
    string? Sexe,
    string? Nationalite,
    string? Adresse,
    string? Telephone,
    string? Profession,
    int? TailleCm,
    string? Corpulence,
    string? SignesParticuliers);

/// <summary>Véhicule possédé en jeu. Assurance facultative (assureur + numéro de contrat + date de fin).</summary>
public sealed record CitoyenVehiculeInput(
    string Plaque,
    string? Marque,
    string? Modele,
    string? Couleur,
    string? Assureur,
    string? Contrat,
    string? FinAssurance);

public sealed record CitoyenVehiculesInput(string DiscordId, string PersonnageId, string? NomJoueur, IReadOnlyList<CitoyenVehiculeInput> Vehicules);

public sealed record CitoyenVehiculeRetraitInput(string DiscordId, string PersonnageId, string? NomJoueur, string Plaque);

/// <summary>Permis : catégories (« B », « A2 »…) et date d'obtention. Points et situation sont fixés par SAPHIR.</summary>
public sealed record CitoyenPermisInput(string DiscordId, string PersonnageId, string? NomJoueur, IReadOnlyList<string> Categories, string? DateObtention);

public sealed record CitoyenPermisDto(string Reference, string Categories, string? DatesObtention, string Situation, int Points);

public sealed record CitoyenAssuranceDto(string Assureur, string Contrat, DateOnly? Fin, bool Couvert);

public sealed record CitoyenVehiculeDto(string Immatriculation, string? Marque, string? Modele, string? Couleur, CitoyenAssuranceDto? Assurance);

/// <summary>Fiche vue par le joueur (jamais les fichiers de police : ni TAJ, ni FPR, ni FOVeS).</summary>
public sealed record CitoyenPersonnageDto(
    string Reference,
    string PersonnageId,
    string Nom,
    string Prenom,
    DateOnly? DateNaissance,
    string? LieuNaissance,
    string? Sexe,
    string? Nationalite,
    string? Adresse,
    string? Telephone,
    string? Profession,
    int? TailleCm,
    string? Corpulence,
    string? SignesParticuliers,
    bool IdentiteFigee,
    CitoyenPermisDto? Permis,
    IReadOnlyList<CitoyenVehiculeDto> Vehicules);

/// <summary>État du compte : personnages actifs du compte Discord et, s'il est déclaré, le personnage joué.</summary>
public sealed record CitoyenEtatDto(
    bool PortailActif,
    int Maximum,
    IReadOnlyList<CitoyenPersonnageResumeDto> Personnages,
    CitoyenPersonnageDto? Personnage);

public sealed record CitoyenPersonnageResumeDto(string Reference, string PersonnageId, string Nom, string Prenom, DateOnly? DateNaissance);

/// <summary>Résultat d'une déclaration : fiche à jour, création ou mise à jour, avertissements (identité figée…).</summary>
public sealed record CitoyenResultatDto(bool Cree, string Message, CitoyenPersonnageDto Personnage, IReadOnlyList<string> Avertissements);

public sealed record CitoyenVehiculeResultatDto(string Plaque, string Statut, string Message);

public sealed record CitoyenVehiculesResultatDto(IReadOnlyList<CitoyenVehiculeResultatDto> Resultats, CitoyenPersonnageDto Personnage);

/// <summary>Statuts d'un véhicule déclaré.</summary>
public static class CitoyenVehiculeStatuts
{
    public const string Cree = "Déclaré";
    public const string MisAJour = "Mis à jour";
    public const string Refuse = "Refusé";
}
