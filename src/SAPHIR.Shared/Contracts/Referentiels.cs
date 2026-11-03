namespace Saphir.Shared.Contracts;

// =====================================================================================
// Contrats partagés entre modules (lecture seule pour les modules qui ne les possèdent pas).
// Propriétaire de chaque route indiqué en commentaire ; les autres modules l'appellent depuis le poste.
// =====================================================================================

/// <summary>
/// Agent résumé pour les listes de choix (équipages, PVI, annuaire…).
/// Route (module RH) : GET /api/referentiels/agents?q=&amp;tous=false — toute session d'agent ; par défaut agents Actifs.
/// </summary>
public sealed record AgentResumeDto(
    long Id,
    string Reference,
    string Nom,
    string Prenom,
    string? Rio,
    string? Grade,
    string? GradeAbrege,
    string? Service,
    string? Unite,
    string? Fonction,
    string Statut)
{
    /// <summary>« BC DURAND Jean-Pierre » (grade abrégé s'il est connu).</summary>
    public string Libelle => string.IsNullOrWhiteSpace(GradeAbrege) ? $"{Nom} {Prenom}" : $"{GradeAbrege} {Nom} {Prenom}";
}

/// <summary>
/// Élément de référentiel RH (grade, service, unité, fonction).
/// Routes (module RH) : GET /api/referentiels/grades | services | unites | fonctions — toute session d'agent.
/// <paramref name="ParentId"/> : service d'une unité ; <paramref name="Ordre"/> : rang (grades).
/// </summary>
public sealed record ReferentielDto(long Id, string Code, string Libelle, string? Abrege, long? ParentId, int Ordre, bool Actif);

/// <summary>
/// Personne fictive (identifiant interne stable « PER-000001 »), résumé pour les recherches des autres modules.
/// <paramref name="Alertes"/> : codes courts uniquement (« FPR », « TAJ », « FIJAIS »…), selon les droits de l'agent.
/// Routes (module Judiciaire) : GET /api/personnes/recherche?q= ; GET /api/personnes/{id}/resume ;
/// POST /api/personnes (corps <see cref="PersonneCreationInput"/>).
/// </summary>
public sealed record PersonneResumeDto(
    long Id,
    string Reference,
    string Nom,
    string Prenom,
    DateOnly? DateNaissance,
    string? LieuNaissance,
    string? Sexe,
    IReadOnlyList<string> Alertes)
{
    public string Libelle => DateNaissance is { } d ? $"{Nom} {Prenom} (né(e) le {d:dd/MM/yyyy})" : $"{Nom} {Prenom}";
}

public sealed record PersonneCreationInput(string Nom, string Prenom, DateOnly? DateNaissance, string? LieuNaissance, string? Sexe, string? Nationalite);

/// <summary>
/// Véhicule fictif, résumé pour les recherches des autres modules.
/// Routes (module Judiciaire) : GET /api/vehicules/recherche?q= ; GET /api/vehicules/{id}/resume.
/// </summary>
public sealed record VehiculeResumeDto(
    long Id,
    string Immatriculation,
    string? Marque,
    string? Modele,
    string? Couleur,
    long? TitulaireId,
    string? Titulaire,
    IReadOnlyList<string> Alertes);

/// <summary>
/// Intervention PÉGASE résumée (pour LRPPN, Main courante, NEO).
/// Route (module Opérations) : GET /api/pegase/interventions/{id}/resume — permissions pegase.consulter ou lrppn.consulter.
/// </summary>
public sealed record InterventionResumeDto(
    long Id,
    string Numero,
    string Nature,
    string? Adresse,
    string? Commune,
    DateTimeOffset DebutLe,
    string Statut,
    IReadOnlyList<string> Equipages);
