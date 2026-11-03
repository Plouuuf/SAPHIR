namespace Saphir.Shared.Contracts.Rh;

// =====================================================================================
// RESSOURCES HUMAINES (phase 3) — contrats échangés entre le serveur et le poste.
// Routes : docs/API-RH.md. Toutes les routes /api/rh/** exigent le rôle Discord RH (vérifié côté serveur).
// =====================================================================================

/// <summary>Messages temps réel du module RH (Text = identifiant concerné, jamais de donnée personnelle).</summary>
public static class RhRealtimeTypes
{
    /// <summary>Fiche d'un agent modifiée (Text = id de l'agent) : annuaire, listes d'agents, fenêtres RH ouvertes.</summary>
    public const string AgentModifie = "rh-agent-modifie";

    /// <summary>Documents, dossiers, catégories ou modèles RH modifiés (Text = id éventuel).</summary>
    public const string DocumentsModifies = "rh-documents-modifies";

    /// <summary>Grades, services, unités ou fonctions modifiés (Text = type de référentiel).</summary>
    public const string ReferentielsModifies = "rh-referentiels-modifies";
}

/// <summary>Types de référentiels RH (segment d'URL).</summary>
public static class RhReferentielTypes
{
    public const string Grades = "grades";
    public const string Services = "services";
    public const string Unites = "unites";
    public const string Fonctions = "fonctions";

    public static readonly IReadOnlyList<string> All = [Grades, Services, Unites, Fonctions];

    public static bool Existe(string? type) => type is not null && All.Contains(type);

    public static string Libelle(string type) => type switch
    {
        Grades => "Grades",
        Services => "Services",
        Unites => "Unités",
        Fonctions => "Fonctions",
        _ => type
    };

    public static string LibelleSingulier(string type) => type switch
    {
        Grades => "grade",
        Services => "service",
        Unites => "unité",
        Fonctions => "fonction",
        _ => type
    };
}

/// <summary>Catégories de grades (référentiel des grades).</summary>
public static class RhGradeCategories
{
    public const string EncadrementApplication = "Corps d'encadrement et d'application";
    public const string Commandement = "Corps de commandement";
    public const string ConceptionDirection = "Corps de conception et de direction";
    public const string Contractuels = "Adjoints et contractuels";
    public const string Eleves = "Élèves";
    public const string Reserve = "Réserve opérationnelle";

    public static readonly IReadOnlyList<string> All =
        [EncadrementApplication, Commandement, ConceptionDirection, Contractuels, Eleves, Reserve];
}

/// <summary>Statuts d'un document RH (et de chacune de ses versions).</summary>
public static class RhDocumentStatuts
{
    public const string Brouillon = "Brouillon";
    public const string AValider = "À valider";
    public const string Valide = "Validé";
    public const string Archive = "Archivé";

    public static readonly IReadOnlyList<string> All = [Brouillon, AValider, Valide, Archive];
}

/// <summary>Niveaux de confidentialité. « Confidentiel RH » : visible seulement avec rh.documents ou rh.documents.valider.</summary>
public static class RhConfidentialites
{
    public const string Normal = "Normal";
    public const string Restreint = "Diffusion restreinte";
    public const string Confidentiel = "Confidentiel RH";

    public static readonly IReadOnlyList<string> All = [Normal, Restreint, Confidentiel];
}

/// <summary>Groupes de catégories documentaires (section 5.3).</summary>
public static class RhCategorieGroupes
{
    public const string Personnel = "Personnel";
    public const string Service = "Service";
    public const string Generaux = "Documents généraux";

    public static readonly IReadOnlyList<string> All = [Personnel, Service, Generaux];
}

/// <summary>Types de modèles (section 5.6).</summary>
public static class RhModeleTypes
{
    public static readonly IReadOnlyList<string> All =
    [
        "Fiche individuelle",
        "Note de service",
        "Document d'affectation",
        "Document de changement de grade",
        "Attestation administrative",
        "Document de nomination",
        "Document de changement de fonction",
        "Formulaire administratif libre"
    ];
}

/// <summary>Fichiers acceptés par la gestion documentaire RH.</summary>
public static class RhFichiers
{
    /// <summary>10 Mo au plus par fichier.</summary>
    public const int TailleMax = 10 * 1024 * 1024;

    public static readonly IReadOnlyList<string> Extensions = [".pdf", ".png", ".jpg", ".jpeg", ".docx", ".txt"];

    public static string TypeMime(string extension) => extension.ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".txt" => "text/plain",
        _ => "application/octet-stream"
    };

    public static bool EstImage(string? typeMime) => typeMime is "image/png" or "image/jpeg";
}

/// <summary>Variables reconnues dans les modèles : {{agent_nom}} ou {{NOM}}, etc.</summary>
public sealed record RhVariableModele(string Code, string Alias, string Description);

public static class RhModeleVariables
{
    public static readonly IReadOnlyList<RhVariableModele> All =
    [
        new("agent_nom", "NOM", "NOM de l'agent"),
        new("agent_prenom", "PRENOM", "Prénom de l'agent"),
        new("agent_rio", "RIO", "RIO actuel"),
        new("agent_grade", "GRADE", "Grade actuel"),
        new("agent_service", "SERVICE", "Service d'affectation"),
        new("agent_unite", "UNITE", "Unité actuelle"),
        new("agent_fonction", "FONCTION", "Fonction exercée"),
        new("agent_matricule", "MATRICULE", "Matricule de simulation"),
        new("agent_reference", "REFERENCE", "Identifiant SAPHIR (AG-…)"),
        new("date_document", "DATE", "Date du document (jj/mm/aaaa)"),
        new("responsable_nom", "RESPONSABLE", "Responsable hiérarchique"),
        new("numero_document", "NUMERO", "Numéro du document généré"),
        new("organisation", "ORGANISATION", "Nom de l'organisation")
    ];
}

// =====================================================================================
// Agents
// =====================================================================================

/// <summary>Ligne de la gestion du personnel (section 4.4).</summary>
public sealed record RhAgentLigneDto(
    long Id,
    string Reference,
    string DiscordId,
    string Nom,
    string Prenom,
    string? Rio,
    string? Matricule,
    long? GradeId,
    string? Grade,
    string? GradeAbrege,
    long? ServiceId,
    string? Service,
    long? UniteId,
    string? Unite,
    long? FonctionId,
    string? Fonction,
    string Statut,
    DateTimeOffset CreeLe,
    DateTimeOffset MajLe)
{
    public string Libelle => string.IsNullOrWhiteSpace(GradeAbrege) ? $"{Nom} {Prenom}" : $"{GradeAbrege} {Nom} {Prenom}";
}

public sealed record RhAffectationDto(
    long Id,
    long AgentId,
    string Agent,
    long? ServiceId,
    string? Service,
    long? UniteId,
    string? Unite,
    long? FonctionId,
    string? Fonction,
    long? ResponsableId,
    string? Responsable,
    DateOnly Debut,
    DateOnly? Fin,
    string? Motif,
    string? Observations,
    string Auteur,
    DateTimeOffset CreeLe)
{
    public bool EnCours => Fin is null;
}

/// <summary>Changement de grade (historique de carrière).</summary>
public sealed record RhCarriereDto(
    long Id,
    long AgentId,
    string Agent,
    string? AncienGrade,
    string Grade,
    DateOnly Effet,
    string? Motif,
    string? Observations,
    long? DocumentId,
    string Auteur,
    DateTimeOffset CreeLe);

public sealed record RhRioHistoriqueDto(
    long Id,
    long AgentId,
    string Agent,
    string? Ancien,
    string Nouveau,
    DateOnly Effet,
    string? Motif,
    string? Observations,
    string Auteur,
    DateTimeOffset CreeLe);

public sealed record RhIdentiteHistoriqueDto(
    long Id,
    string AncienNom,
    string AncienPrenom,
    string NouveauNom,
    string NouveauPrenom,
    DateOnly Effet,
    string? Motif,
    string? Observations,
    string Auteur,
    DateTimeOffset CreeLe);

/// <summary>Habilitations d'un agent (lecture seule dans RH ; modification dans Administration).</summary>
public sealed record RhHabilitationsDto(
    IReadOnlyList<string> Profils,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> Applications,
    IReadOnlyList<HistoriqueDto> Historique);

/// <summary>Dossier individuel complet (section 4.5).</summary>
public sealed record RhDossierDto(
    RhAgentLigneDto Agent,
    DateOnly? DateIntegration,
    DateOnly? GradeEffet,
    DateTimeOffset? DerniereConnexion,
    RhAffectationDto? AffectationCourante,
    IReadOnlyList<RhAffectationDto> Affectations,
    IReadOnlyList<RhCarriereDto> Carriere,
    IReadOnlyList<RhRioHistoriqueDto> Rio,
    IReadOnlyList<RhIdentiteHistoriqueDto> Identites,
    IReadOnlyList<RhDocumentDto> Documents,
    RhHabilitationsDto Habilitations,
    IReadOnlyList<HistoriqueDto> Historique);

/// <summary>Modification du NOM et du prénom (section 4.7). Motif obligatoire.</summary>
public sealed record RhIdentiteInput(string Nom, string Prenom, DateOnly? DateEffet, string? Motif, string? Observations);

public sealed record RhMatriculeInput(string Matricule, string? Motif);

public sealed record RhIntegrationInput(DateOnly? DateIntegration, string? Motif);

/// <summary>Attribution ou changement de RIO (section 4.6). Motif obligatoire pour un changement.</summary>
public sealed record RhRioInput(string Rio, DateOnly? DateEffet, string? Motif, string? Observations);

/// <summary>Nouveau grade (section 4.8). Motif obligatoire.</summary>
public sealed record RhGradeInput(long GradeId, DateOnly? DateEffet, string? Motif, string? Observations, long? DocumentId);

/// <summary>Nouvelle affectation (section 4.10). Motif obligatoire.</summary>
public sealed record RhAffectationInput(
    long ServiceId,
    long UniteId,
    long? FonctionId,
    long? ResponsableId,
    DateOnly? DateEffet,
    string? Motif,
    string? Observations);

/// <summary>Changement de statut administratif (section 4.11). Motif obligatoire pour suspendre, désactiver ou archiver.</summary>
public sealed record RhStatutInput(string Statut, string? Motif);

// =====================================================================================
// Référentiels
// =====================================================================================

/// <summary>Élément de référentiel vu par le RH (avec effectif et éléments liés).</summary>
public sealed record RhReferentielDto(
    long Id,
    string Type,
    string Code,
    string Libelle,
    string? Abrege,
    string? Categorie,
    long? ParentId,
    string? Parent,
    long? ResponsableId,
    string? Responsable,
    int Ordre,
    bool Actif,
    int Effectif);

/// <summary>
/// Création ou modification d'un élément de référentiel. <paramref name="Categorie"/> : grades ;
/// <paramref name="ParentId"/> : service d'une unité ; <paramref name="ResponsableId"/> : responsable d'un service.
/// </summary>
public sealed record RhReferentielInput(
    string Libelle,
    string? Abrege,
    string? Categorie,
    long? ParentId,
    long? ResponsableId,
    int Ordre,
    bool Actif);

// =====================================================================================
// Tableau de bord, paramètres
// =====================================================================================

public sealed record RhCompteurDto(string Libelle, int Nombre);

public sealed record RhTableauBordDto(
    int Total,
    int Actifs,
    int Suspendus,
    int Inactifs,
    int Archives,
    int ProfilsACompleter,
    int SansRio,
    int DocumentsAValider,
    IReadOnlyList<RhCompteurDto> ParStatut,
    IReadOnlyList<RhCompteurDto> ParGrade,
    IReadOnlyList<RhCompteurDto> ParService,
    IReadOnlyList<RhCompteurDto> ParUnite,
    IReadOnlyList<HistoriqueDto> DernieresModifications,
    IReadOnlyList<RhAgentLigneDto> DernieresCreations);

public sealed record RhParametresDto(
    string RioFormat,
    string RioExemple,
    string Organisation,
    int TailleMaxFichier,
    IReadOnlyList<string> Extensions);

// =====================================================================================
// Gestion documentaire (section 5)
// =====================================================================================

public sealed record RhDossierDocDto(long Id, long? ParentId, string Nom, string Chemin, int NombreDocuments);

public sealed record RhDossierDocInput(string Nom, long? ParentId);

public sealed record RhCategorieDto(long Id, string Code, string Libelle, string Groupe, int Ordre, bool Actif);

public sealed record RhCategorieInput(string Libelle, string Groupe, int Ordre, bool Actif);

public sealed record RhDocumentDto(
    long Id,
    string Reference,
    string Titre,
    string? Type,
    long? CategorieId,
    string? Categorie,
    long? DossierId,
    string? Dossier,
    string? Description,
    long? AgentId,
    string? Agent,
    long? ServiceId,
    string? Service,
    DateOnly? DateDocument,
    DateOnly? DateEffet,
    DateOnly? DateExpiration,
    string Confidentialite,
    string Statut,
    string? Observations,
    int Version,
    string? NomFichier,
    string? TypeMime,
    long Taille,
    string Auteur,
    DateTimeOffset CreeLe,
    DateTimeOffset MajLe,
    string? ValidePar,
    DateTimeOffset? ValideLe,
    long? ModeleId)
{
    /// <summary>Validé : figé (seule une nouvelle version, un classement ou l'archivage restent possibles).</summary>
    public bool EstFige => Statut == RhDocumentStatuts.Valide;
}

public sealed record RhDocumentVersionDto(
    long Id,
    int Numero,
    string NomFichier,
    string TypeMime,
    long Taille,
    string Sha256,
    string? Commentaire,
    string Statut,
    string Auteur,
    DateTimeOffset CreeLe,
    string? ValidePar,
    DateTimeOffset? ValideLe,
    bool ATexte);

public sealed record RhDocumentDetailDto(
    RhDocumentDto Document,
    IReadOnlyList<RhDocumentVersionDto> Versions,
    IReadOnlyList<HistoriqueDto> Historique);

/// <summary>Import d'un document (section 5.4) : fichier en base64 (byte[] JSON), 10 Mo au plus.</summary>
public sealed record RhDocumentCreationInput(
    string Titre,
    string? Type,
    long? CategorieId,
    long? DossierId,
    string? Description,
    long? AgentId,
    long? ServiceId,
    DateOnly? DateDocument,
    DateOnly? DateEffet,
    DateOnly? DateExpiration,
    string? Confidentialite,
    string? Observations,
    bool Soumettre,
    string NomFichier,
    byte[] Contenu);

/// <summary>Modification des métadonnées (renommer, déplacer, classer…). Document validé : seul le dossier peut changer.</summary>
public sealed record RhDocumentMajInput(
    string Titre,
    string? Type,
    long? CategorieId,
    long? DossierId,
    string? Description,
    long? AgentId,
    long? ServiceId,
    DateOnly? DateDocument,
    DateOnly? DateEffet,
    DateOnly? DateExpiration,
    string? Confidentialite,
    string? Observations,
    string? Motif);

/// <summary>Nouvelle version d'un document (l'ancienne est conservée).</summary>
public sealed record RhVersionInput(string NomFichier, byte[] Contenu, string? Commentaire);

public sealed record RhMotifInput(string? Motif);

/// <summary>Fichier téléchargé (contenu en base64) ; <paramref name="Texte"/> : texte d'un document généré depuis un modèle.</summary>
public sealed record RhFichierDto(string NomFichier, string TypeMime, byte[] Contenu, string? Texte);

// =====================================================================================
// Modèles RH (section 5.6)
// =====================================================================================

public sealed record RhModeleDto(
    long Id,
    string Code,
    string Libelle,
    string Type,
    long? CategorieId,
    string? Categorie,
    string Contenu,
    bool Actif,
    DateTimeOffset MajLe);

public sealed record RhModeleInput(string Libelle, string Type, long? CategorieId, string Contenu, bool Actif);

/// <summary>Génération d'un document pour un agent. <paramref name="Apercu"/> : texte seulement, rien n'est enregistré.</summary>
public sealed record RhGenerationInput(
    long AgentId,
    string? Titre,
    long? DossierId,
    long? CategorieId,
    string? Confidentialite,
    bool Apercu,
    bool Soumettre);

public sealed record RhGenerationDto(
    string Titre,
    string Texte,
    IReadOnlyList<string> VariablesInconnues,
    RhDocumentDto? Document);
