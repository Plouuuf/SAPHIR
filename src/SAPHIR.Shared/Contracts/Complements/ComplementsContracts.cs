namespace Saphir.Shared.Contracts.Complements;

// =====================================================================================
// Applications complémentaires (phase 8) : NEO, PVe, Annuaire, Messagerie, Notes, MRZ, Bodycam, Tchap.
// Routes et permissions : docs/API-COMPLEMENTS.md.
// =====================================================================================

/// <summary>
/// Types des messages temps réel du module. Le texte ne porte qu'un identifiant (le poste recharge ses données) ;
/// les bulles de notification utilisent <see cref="RealtimeTypes.Notify"/>.
/// </summary>
public static class ComplementsRealtime
{
    /// <summary>Nouveau message reçu (Text = identifiant du message).</summary>
    public const string MessageRecu = "messagerie-recu";

    /// <summary>Accusé de lecture pour l'expéditeur (Text = identifiant du message).</summary>
    public const string MessageLu = "messagerie-lu";

    /// <summary>Nouveau message dans un salon Tchap (Text = identifiant du salon).</summary>
    public const string TchapMessage = "tchap-message";

    /// <summary>Liste des salons modifiée (création, membres, archivage).</summary>
    public const string TchapSalons = "tchap-salons";

    /// <summary>Registre bodycam modifié (Text = identifiant de l'enregistrement).</summary>
    public const string Bodycam = "bodycam-change";

    /// <summary>Note partagée modifiée (Text = identifiant de la note).</summary>
    public const string Note = "notes-change";

    /// <summary>PVe créé, modifié ou validé (Text = identifiant).</summary>
    public const string Pve = "pve-change";

    /// <summary>Annuaire des services ou coordonnées modifiés.</summary>
    public const string Annuaire = "annuaire-change";
}

/// <summary>Fichier joint (messagerie, justificatif de PVe, document bodycam) : métadonnées seules.</summary>
public sealed record PieceJointeDto(long Id, string Nom, string TypeMime, long Taille);

/// <summary>Fichier envoyé par le poste : <paramref name="Donnees"/> en base64 (10 Mo au plus une fois décodé).</summary>
public sealed record PieceJointeInput(string Nom, string? TypeMime, string Donnees);

/// <summary>Contenu d'un fichier joint (base64).</summary>
public sealed record PieceJointeContenuDto(long Id, string Nom, string TypeMime, long Taille, string Donnees);

/// <summary>Règles communes des fichiers joints (contrôle des fichiers téléchargés, cahier § 22).</summary>
public static class PiecesJointes
{
    public const long TailleMax = 10L * 1024 * 1024;
    public const int NombreMaxParMessage = 10;

    /// <summary>Extensions autorisées (en minuscules, sans point).</summary>
    public static readonly IReadOnlyList<string> Extensions =
        ["pdf", "png", "jpg", "jpeg", "gif", "webp", "txt", "csv", "docx", "xlsx", "odt", "ods", "mp3", "wav"];

    public static string Extension(string? nom)
    {
        var e = Path.GetExtension(nom ?? string.Empty);
        return string.IsNullOrEmpty(e) ? string.Empty : e[1..].ToLowerInvariant();
    }

    public static bool ExtensionAutorisee(string? nom) => Extensions.Contains(Extension(nom));

    public static string TypeMime(string? nom) => Extension(nom) switch
    {
        "pdf" => "application/pdf",
        "png" => "image/png",
        "jpg" or "jpeg" => "image/jpeg",
        "gif" => "image/gif",
        "webp" => "image/webp",
        "txt" => "text/plain",
        "csv" => "text/csv",
        "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "odt" => "application/vnd.oasis.opendocument.text",
        "ods" => "application/vnd.oasis.opendocument.spreadsheet",
        "mp3" => "audio/mpeg",
        "wav" => "audio/wav",
        "mp4" or "m4v" => "video/mp4",
        "mov" => "video/quicktime",
        "webm" => "video/webm",
        "mkv" => "video/x-matroska",
        "avi" => "video/x-msvideo",
        _ => "application/octet-stream"
    };

    /// <summary>« 1,2 Mo », « 350 Ko ».</summary>
    public static string TailleLisible(long octets) => octets switch
    {
        >= 1024L * 1024 * 1024 => (octets / 1024d / 1024 / 1024).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + " Go",
        >= 1024 * 1024 => (octets / 1024d / 1024).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + " Mo",
        >= 1024 => (octets / 1024) + " Ko",
        _ => octets + " o"
    };
}

// =====================================================================================
// ANNUAIRE (cahier § 18.1)
// =====================================================================================

/// <summary>Agent de l'annuaire réduit aux colonnes du socle (repli quand /api/referentiels/agents est indisponible).</summary>
public sealed record AnnuaireAgentDto(long Id, string Reference, string Nom, string Prenom, string? Rio, string Statut);

/// <summary>Coordonnées professionnelles fictives d'un agent (téléphone de service, indicatif radio, poste, adresse interne).</summary>
public sealed record CoordonneesDto(long AgentId, string? Telephone, string? Indicatif, string? Poste, string? Email, DateTimeOffset MajLe);

public sealed record CoordonneesInput(string? Telephone, string? Indicatif, string? Poste, string? Email);

/// <summary>Service de l'annuaire (numéros et indicatifs fictifs gérés par l'administration).</summary>
public sealed record ServiceAnnuaireDto(
    long Id,
    string Nom,
    string? Categorie,
    string? Indicatif,
    string? Telephone,
    string? Frequence,
    string? Adresse,
    string? Horaires,
    string? Observations,
    int Ordre,
    bool Actif);

public sealed record ServiceAnnuaireInput(
    string Nom,
    string? Categorie,
    string? Indicatif,
    string? Telephone,
    string? Frequence,
    string? Adresse,
    string? Horaires,
    string? Observations,
    int Ordre,
    bool Actif);

// =====================================================================================
// MESSAGERIE (cahier § 18.2)
// =====================================================================================

public static class MessagerieDossiers
{
    public const string Reception = "reception";
    public const string Envoyes = "envoyes";
    public const string Brouillons = "brouillons";
    public const string Corbeille = "corbeille";
}

public static class MessagerieTypes
{
    public const string A = "A";
    public const string Cc = "Cc";
}

/// <summary>Ligne d'une boîte (réception, envoyés, brouillons, corbeille).</summary>
public sealed record MessageResumeDto(
    long Id,
    string Reference,
    long ConversationId,
    string Sujet,
    long ExpediteurId,
    string Expediteur,
    string Destinataires,
    DateTimeOffset Date,
    bool Lu,
    bool Brouillon,
    bool Important,
    int NombrePieces);

/// <summary>Destinataire d'un message ; <paramref name="LuLe"/> = accusé de lecture.</summary>
public sealed record DestinataireDto(long AgentId, string Libelle, string Type, DateTimeOffset? LuLe);

public sealed record MessageDto(
    long Id,
    string Reference,
    long ConversationId,
    long? ReponseA,
    string Sujet,
    string Corps,
    long ExpediteurId,
    string Expediteur,
    string? ListeDiffusion,
    IReadOnlyList<DestinataireDto> Destinataires,
    IReadOnlyList<PieceJointeDto> Pieces,
    IReadOnlyList<string> Liens,
    bool Brouillon,
    bool Important,
    DateTimeOffset CreeLe,
    DateTimeOffset? EnvoyeLe,
    int Version);

/// <summary>
/// Création ou modification d'un message. <paramref name="Envoyer"/> = faux : brouillon enregistré.
/// <paramref name="PiecesConservees"/> : pièces déjà jointes au brouillon (ou au message transféré) à garder.
/// <paramref name="Liens"/> : références SAPHIR citées (« PER-000012 », « PVE-2026-000004 »…).
/// </summary>
public sealed record MessageInput(
    string Sujet,
    string Corps,
    IReadOnlyList<long>? Destinataires,
    IReadOnlyList<long>? Copies,
    string? ListeDiffusion,
    IReadOnlyList<string>? Liens,
    IReadOnlyList<PieceJointeInput>? NouvellesPieces,
    IReadOnlyList<long>? PiecesConservees,
    long? ReponseA,
    bool Important,
    bool Envoyer,
    int Version);

public sealed record MessagerieCompteursDto(int NonLus, int Brouillons);

/// <summary>Référence SAPHIR citée dans un message, et application qui sait l'ouvrir (permissions vérifiées à l'ouverture).</summary>
public static class LiensSaphir
{
    /// <summary>Application capable d'ouvrir la référence, ou null si le préfixe est inconnu.</summary>
    public static string? Application(string? reference)
    {
        var r = (reference ?? string.Empty).Trim().ToUpperInvariant();
        if (r.StartsWith("PER-", StringComparison.Ordinal) || r.StartsWith("VEH-", StringComparison.Ordinal)) return SaphirApps.Cheops;
        if (r.StartsWith("INT-", StringComparison.Ordinal) || r.StartsWith("APP-", StringComparison.Ordinal)) return SaphirApps.Pegase;
        if (r.StartsWith("MC-", StringComparison.Ordinal) || r.StartsWith("VAC-", StringComparison.Ordinal)) return SaphirApps.MainCourante;
        if (r.StartsWith("PROC-", StringComparison.Ordinal) || r.StartsWith("PV-", StringComparison.Ordinal)) return SaphirApps.Lrppn;
        if (r.StartsWith("PVE-", StringComparison.Ordinal)) return SaphirApps.Pve;
        if (r.StartsWith("BC-", StringComparison.Ordinal)) return SaphirApps.Bodycam;
        if (r.StartsWith("AG-", StringComparison.Ordinal)) return SaphirApps.Annuaire;
        return null;
    }

    /// <summary>Référence acceptée : lettres, chiffres et tirets, 3 à 40 caractères.</summary>
    public static bool Valide(string? reference)
    {
        var r = (reference ?? string.Empty).Trim();
        return r.Length is >= 3 and <= 40 && r.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    }
}

// =====================================================================================
// NOTES (cahier § 18.3)
// =====================================================================================

public sealed record NotePartageDto(long AgentId, string Libelle, bool PeutModifier);

public sealed record NoteDto(
    long Id,
    string Reference,
    string Titre,
    string Contenu,
    string? Categorie,
    bool Epinglee,
    long AuteurId,
    string Auteur,
    bool EstAuteur,
    bool PeutModifier,
    IReadOnlyList<NotePartageDto> Partages,
    DateTimeOffset CreeLe,
    DateTimeOffset MajLe,
    int Version);

public sealed record NotePartageInput(long AgentId, bool PeutModifier);

/// <summary><paramref name="Version"/> : version lue (détection des modifications concurrentes) ; 0 à la création.</summary>
public sealed record NoteInput(
    string Titre,
    string Contenu,
    string? Categorie,
    bool Epinglee,
    IReadOnlyList<NotePartageInput>? Partages,
    int Version);

// =====================================================================================
// MRZ (cahier § 18.4)
// =====================================================================================

public sealed record MrzAnalyseInput(string Texte);

/// <summary>Contrôle d'un chiffre de contrôle (7-3-1).</summary>
public sealed record MrzControleDto(string Libelle, bool Ok, string Attendu, string Lu);

/// <summary>Résultat de l'analyse d'une bande MRZ.</summary>
public sealed record MrzResultatDto(
    bool Reconnu,
    bool ControlesOk,
    string Format,
    string? TypeDocument,
    string? PaysEmetteur,
    string? NumeroDocument,
    string? Nom,
    string? Prenoms,
    string? Nationalite,
    DateOnly? DateNaissance,
    string? Sexe,
    DateOnly? DateExpiration,
    string? DonneesOptionnelles,
    IReadOnlyList<MrzControleDto> Controles,
    IReadOnlyList<string> Erreurs,
    IReadOnlyList<string> Lignes);

/// <summary>Lecture confirmée par l'agent (champs éventuellement corrigés) et liée ou non à une personne SAPHIR.</summary>
public sealed record MrzLectureInput(
    string Texte,
    string Nom,
    string Prenoms,
    DateOnly? DateNaissance,
    string? Sexe,
    string? NumeroDocument,
    string? TypeDocument,
    string? Nationalite,
    DateOnly? DateExpiration,
    bool ConfirmationManuelle,
    long? PersonneId,
    string? PersonneReference,
    string? PersonneLibelle,
    string? Contexte);

public sealed record MrzLectureDto(
    long Id,
    string Reference,
    DateTimeOffset Date,
    string Agent,
    string Format,
    bool ControlesOk,
    string Nom,
    string Prenoms,
    DateOnly? DateNaissance,
    string? Sexe,
    string? NumeroDocument,
    string? TypeDocument,
    string? Nationalite,
    DateOnly? DateExpiration,
    long? PersonneId,
    string? PersonneReference,
    string? PersonneLibelle,
    string? Contexte);

// =====================================================================================
// PVe (cahier § 17)
// =====================================================================================

public static class PveStatuts
{
    public const string Brouillon = "Brouillon";
    public const string Valide = "Validé";
    public const string Abandonne = "Abandonné";

    public static readonly IReadOnlyList<string> All = [Brouillon, Valide, Abandonne];
}

/// <summary>Infraction du référentiel (NATINF fictif). <paramref name="Classe"/> : « 1 » à « 5 » ou « AFD » (amende forfaitaire délictuelle).</summary>
public sealed record InfractionDto(
    long Id,
    string Natinf,
    string Libelle,
    string? TexteReference,
    string Categorie,
    string Classe,
    decimal MontantForfaitaire,
    decimal? MontantMinore,
    decimal? MontantMajore,
    int Points,
    bool Actif,
    int Ordre);

public sealed record InfractionInput(
    string Natinf,
    string Libelle,
    string? TexteReference,
    string Categorie,
    string Classe,
    decimal MontantForfaitaire,
    decimal? MontantMinore,
    decimal? MontantMajore,
    int Points,
    bool Actif,
    int Ordre);

public sealed record PveResumeDto(
    long Id,
    string Reference,
    string? NumeroAvis,
    string Statut,
    DateTimeOffset DateInfraction,
    string Natinf,
    string Infraction,
    string? Contrevenant,
    string? Immatriculation,
    string? Commune,
    string Agent,
    decimal MontantForfaitaire);

public sealed record PveMentionDto(long Id, DateTimeOffset Date, string Auteur, string Texte);

public sealed record PveDto(
    long Id,
    string Reference,
    string? NumeroAvis,
    string Statut,
    long InfractionId,
    string Natinf,
    string Infraction,
    string? TexteReference,
    string Classe,
    decimal MontantForfaitaire,
    decimal? MontantMinore,
    decimal? MontantMajore,
    int Points,
    DateTimeOffset DateInfraction,
    string Lieu,
    string? Commune,
    long? PersonneId,
    string? PersonneReference,
    string? ContrevenantNom,
    string? ContrevenantPrenom,
    DateOnly? ContrevenantNaissance,
    string? ContrevenantLieuNaissance,
    string? ContrevenantAdresse,
    long? VehiculeId,
    string? Immatriculation,
    string? VehiculeMarque,
    string? VehiculeModele,
    string? VehiculeCouleur,
    string? Circonstances,
    string? Observations,
    string? InterventionNumero,
    long AgentId,
    string Agent,
    string? AgentRio,
    string? AgentGrade,
    string? AgentService,
    string? AgentUnite,
    DateTimeOffset CreeLe,
    DateTimeOffset MajLe,
    DateTimeOffset? ValideLe,
    string? MotifAbandon,
    int Version,
    IReadOnlyList<PieceJointeDto> Justificatifs,
    IReadOnlyList<PveMentionDto> Mentions);

/// <summary>Brouillon de PVe. Il faut un contrevenant (nom) ou une immatriculation. <paramref name="Version"/> : 0 à la création.</summary>
public sealed record PveInput(
    long InfractionId,
    DateTimeOffset DateInfraction,
    string Lieu,
    string? Commune,
    long? PersonneId,
    string? PersonneReference,
    string? ContrevenantNom,
    string? ContrevenantPrenom,
    DateOnly? ContrevenantNaissance,
    string? ContrevenantLieuNaissance,
    string? ContrevenantAdresse,
    long? VehiculeId,
    string? Immatriculation,
    string? VehiculeMarque,
    string? VehiculeModele,
    string? VehiculeCouleur,
    string? Circonstances,
    string? Observations,
    string? InterventionNumero,
    int Version);

public sealed record PveValidationInput(int Version, bool Certifie);

public sealed record PveAbandonInput(string Motif, int Version);

public sealed record PveMentionInput(string Texte);

/// <summary>Export d'un PVe : document HTML autonome (impression / enregistrement).</summary>
public sealed record PveExportDto(string NomFichier, string Contenu);

// =====================================================================================
// BODYCAM (cahier § 18.5)
// =====================================================================================

public static class BodycamStatuts
{
    public const string EnAttente = "En attente du fichier";
    public const string Disponible = "Disponible";
    public const string Archive = "Archivé";
}

public static class BodycamSources
{
    public const string Url = "url";
    public const string Fichier = "fichier";
}

public static class BodycamFichiers
{
    public const long TailleMax = 200L * 1024 * 1024;

    /// <summary>Taille d'un morceau d'envoi ou de lecture (octets bruts, avant base64).</summary>
    public const int TailleMorceau = 4 * 1024 * 1024;

    public static readonly IReadOnlyList<string> Extensions = ["mp4", "m4v", "mov", "webm", "mkv", "avi"];
}

public sealed record BodycamResumeDto(
    long Id,
    string Numero,
    string Agent,
    DateTimeOffset EnregistreLe,
    int DureeSecondes,
    string? InterventionNumero,
    string Source,
    string Statut,
    string? Description);

public sealed record BodycamDto(
    long Id,
    string Numero,
    long AgentId,
    string Agent,
    DateTimeOffset EnregistreLe,
    int DureeSecondes,
    string? InterventionNumero,
    string? Description,
    string? Provenance,
    string Source,
    string? Url,
    string? FichierNom,
    long? FichierTaille,
    string? FichierType,
    string? FichierSha256,
    long FichierRecu,
    string Statut,
    string CreePar,
    DateTimeOffset CreeLe,
    DateTimeOffset MajLe,
    string? MotifArchivage,
    int Version,
    IReadOnlyList<PieceJointeDto> Documents);

/// <summary><paramref name="AgentId"/> : porteur de la caméra. <paramref name="Source"/> : « url » ou « fichier ».</summary>
public sealed record BodycamInput(
    long AgentId,
    DateTimeOffset EnregistreLe,
    int DureeSecondes,
    string? InterventionNumero,
    string? Description,
    string? Provenance,
    string Source,
    string? Url,
    int Version);

public sealed record BodycamEnvoiDebutInput(string Nom, long Taille, string? TypeMime);

public sealed record BodycamMorceauInput(long Offset, string Donnees);

public sealed record BodycamEnvoiEtatDto(long Recu, long Attendu, bool Termine, string Statut);

/// <summary>Réponse à « visionner » (accès journalisé) : adresse de la vidéo ou fichier à télécharger par morceaux.</summary>
public sealed record BodycamVisionnageDto(string Source, string? Url, string? FichierNom, long? Taille, string? TypeMime);

public sealed record BodycamMorceauDto(long Offset, long Total, string Donnees);

public sealed record BodycamAccesDto(long Id, DateTimeOffset Date, string Agent, string Action);

public sealed record BodycamArchivageInput(string Motif, int Version);

// =====================================================================================
// TCHAP (cahier § 18.6)
// =====================================================================================

public static class TchapTypesSalon
{
    /// <summary>Salon de service : ouvert à tous les agents ayant Tchap, créé par l'administration.</summary>
    public const string Service = "service";

    /// <summary>Salon créé par un agent : réservé à ses membres.</summary>
    public const string Groupe = "groupe";
}

public sealed record SalonDto(
    long Id,
    string Nom,
    string? Description,
    string Type,
    bool EstMembre,
    bool EstAdmin,
    int NombreMembres,
    int NonLus,
    DateTimeOffset? DernierMessageLe,
    bool Archive);

public sealed record SalonMembreDto(long AgentId, string Libelle, string Role, DateTimeOffset AjouteLe);

public sealed record SalonInput(string Nom, string? Description, string Type, IReadOnlyList<long>? Membres);

public sealed record SalonMembresInput(IReadOnlyList<long>? Ajouter, IReadOnlyList<long>? Retirer);

public sealed record TchapMessageDto(long Id, long SalonId, long AuteurId, string Auteur, string Texte, DateTimeOffset Date, bool Supprime);

public sealed record TchapMessageInput(string Texte);
