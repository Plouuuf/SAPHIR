using System.Globalization;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Rh;

namespace Saphir.ViewModels.Apps.Rh;

/// <summary>Choix d'une liste déroulante (référentiel, agent, dossier…). Égalité par valeur : la sélection survit aux rechargements.</summary>
public sealed record RhOption(long? Id, string Libelle, long? ParentId = null)
{
    public override string ToString() => Libelle;
}

/// <summary>Entrée du menu latéral RH (section 4.2).</summary>
public sealed record RhMenuEntree(string Cle, string Numero, string Libelle);

/// <summary>Fichier choisi par l'agent dans le sélecteur d'ouverture.</summary>
public sealed record RhFichierChoisi(string Nom, byte[] Contenu);

/// <summary>Barre d'une répartition (tableau de bord).</summary>
public sealed record RhBarre(string Libelle, int Nombre, double Largeur);

/// <summary>Ligne d'historique affichée.</summary>
public sealed record RhHistoriqueLigne(string Date, string Auteur, string Objet, string Reference, string Action, string Detail, string Motif)
{
    public static RhHistoriqueLigne Depuis(HistoriqueDto h) => new(
        RhFormat.DateHeure(h.Date), h.Auteur, RhFormat.Objet(h.Objet), h.Reference, h.Action,
        h.Champ is null ? (h.Nouvelle ?? string.Empty) : $"{h.Champ} : {h.Ancienne ?? "—"} → {h.Nouvelle ?? "—"}",
        h.Motif ?? string.Empty);
}

/// <summary>Mise en forme commune (français).</summary>
public static class RhFormat
{
    public static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");

    public static string DateHeure(DateTimeOffset date) => date.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Francais);

    public static string DateHeure(DateTimeOffset? date) => date is { } d ? DateHeure(d) : "—";

    public static string Jour(DateOnly? jour) => jour?.ToString("dd/MM/yyyy", Francais) ?? "—";

    public static string Texte(string? texte) => string.IsNullOrWhiteSpace(texte) ? "—" : texte;

    public static DateOnly? VersJour(DateTime? date) => date is { } d ? DateOnly.FromDateTime(d) : null;

    public static DateTime? VersDate(DateOnly? jour) => jour?.ToDateTime(TimeOnly.MinValue);

    public static string Taille(long octets) => octets switch
    {
        < 1024 => $"{octets} o",
        < 1024 * 1024 => (octets / 1024.0).ToString("0.#", Francais) + " Ko",
        _ => (octets / (1024.0 * 1024.0)).ToString("0.#", Francais) + " Mo"
    };

    public static string Objet(string objet) => objet switch
    {
        "agent" => "Agent",
        "rh-grade" => "Grade",
        "rh-service" => "Service",
        "rh-unite" => "Unité",
        "rh-fonction" => "Fonction",
        "rh-document" => "Document",
        "rh-dossier" => "Dossier",
        "rh-categorie" => "Catégorie",
        "rh-modele" => "Modèle",
        _ => objet
    };

    /// <summary>Entier saisi dans une zone de texte (0 si vide ou invalide).</summary>
    public static int Entier(string? texte)
        => int.TryParse((texte ?? string.Empty).Trim(), NumberStyles.Integer, Francais, out var n) ? n : 0;
}

/// <summary>Ligne de la gestion du personnel.</summary>
public sealed class RhAgentRow(RhAgentLigneDto dto)
{
    public RhAgentLigneDto Dto { get; } = dto;
    public long Id => Dto.Id;
    public string Reference => Dto.Reference;
    public string Nom => Dto.Nom;
    public string Prenom => Dto.Prenom;
    public string Rio => RhFormat.Texte(Dto.Rio);
    public string Matricule => RhFormat.Texte(Dto.Matricule);
    public string Grade => RhFormat.Texte(Dto.Grade);
    public string Service => RhFormat.Texte(Dto.Service);
    public string Unite => RhFormat.Texte(Dto.Unite);
    public string Fonction => RhFormat.Texte(Dto.Fonction);
    public string Statut => Dto.Statut;
    public string Discord => Dto.DiscordId;
    public string MajLe => RhFormat.DateHeure(Dto.MajLe);
    public string CreeLe => RhFormat.DateHeure(Dto.CreeLe);
    public string Libelle => Dto.Libelle;
}

public sealed class RhAffectationRow(RhAffectationDto dto)
{
    public RhAffectationDto Dto { get; } = dto;
    public long AgentId => Dto.AgentId;
    public string Agent => Dto.Agent;
    public string Service => RhFormat.Texte(Dto.Service);
    public string Unite => RhFormat.Texte(Dto.Unite);
    public string Fonction => RhFormat.Texte(Dto.Fonction);
    public string Responsable => RhFormat.Texte(Dto.Responsable);
    public string Debut => RhFormat.Jour(Dto.Debut);
    public string Fin => Dto.Fin is null ? "En cours" : RhFormat.Jour(Dto.Fin);
    public string Motif => Dto.Motif ?? string.Empty;
    public string Auteur => Dto.Auteur;
}

public sealed class RhDocumentRow(RhDocumentDto dto)
{
    public RhDocumentDto Dto { get; } = dto;
    public long Id => Dto.Id;
    public string Reference => Dto.Reference;
    public string Titre => Dto.Titre;
    public string Categorie => RhFormat.Texte(Dto.Categorie);
    public string Dossier => RhFormat.Texte(Dto.Dossier);
    public string Agent => RhFormat.Texte(Dto.Agent);
    public string Statut => Dto.Statut;
    public string Version => "v" + Dto.Version.ToString(CultureInfo.InvariantCulture);
    public string Fichier => Dto.NomFichier is null ? "—" : $"{Dto.NomFichier} ({RhFormat.Taille(Dto.Taille)})";
    public string Confidentialite => Dto.Confidentialite;
    public string MajLe => RhFormat.DateHeure(Dto.MajLe);
    public string Auteur => Dto.Auteur;
}

public sealed class RhVersionRow(RhDocumentVersionDto dto)
{
    public RhDocumentVersionDto Dto { get; } = dto;
    public int Numero => Dto.Numero;
    public string Titre => $"v{Dto.Numero} — {Dto.NomFichier}";
    public string Detail => $"{RhFormat.Taille(Dto.Taille)} · {Dto.Statut} · {Dto.Auteur} · {RhFormat.DateHeure(Dto.CreeLe)}"
                            + (Dto.ValidePar is null ? string.Empty : $" · validée par {Dto.ValidePar} le {RhFormat.DateHeure(Dto.ValideLe)}");
    public string Commentaire => Dto.Commentaire ?? string.Empty;
    public bool HasCommentaire => !string.IsNullOrWhiteSpace(Dto.Commentaire);
    public string Empreinte => "SHA-256 " + Dto.Sha256;
}

/// <summary>Accès commun aux permissions RH du poste (le serveur revérifie tout).</summary>
public static class RhDroits
{
    public static bool Consulter => SaphirSession.Has(Permissions.RhConsulter);
    public static bool ModifierAgent => SaphirSession.Has(Permissions.RhAgentModifier);
    public static bool Rio => SaphirSession.Has(Permissions.RhRioAttribuer);
    public static bool Carriere => SaphirSession.Has(Permissions.RhCarriere);
    public static bool Referentiels => SaphirSession.Has(Permissions.RhReferentiels);
    public static bool Statut => SaphirSession.Has(Permissions.RhStatut);
    public static bool Documents => SaphirSession.Has(Permissions.RhDocuments);
    public static bool ValiderDocuments => SaphirSession.Has(Permissions.RhDocumentsValider);
    public static bool LireDocuments => Consulter || Documents || ValiderDocuments;
    public static bool AdminComptes => SaphirSession.Has(Permissions.AdminComptes);
    public static bool AdminParametres => SaphirSession.Has(Permissions.AdminParametres);
}
