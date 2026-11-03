using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Judiciaire;

namespace Saphir.Server.Modules.Judiciaire;

/// <summary>Règles pures du module Judiciaire (sans base) : références, formats, transitions. Testées dans SAPHIR.Tests.</summary>
public static class JudiciaireRegles
{
    /// <summary>Taille maximale d'un fichier joint (pièce, photographie, vidéo de simulation).</summary>
    public const int TailleMaxFichier = 10 * 1024 * 1024;

    public const int PointsMax = 12;

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly Regex PlaqueSiv = new("^[A-Z]{2}[0-9]{3}[A-Z]{2}$", RegexOptions.CultureInvariant);
    private static readonly Regex Heure = new("^([01][0-9]|2[0-3])[:h]([0-5][0-9])$", RegexOptions.CultureInvariant);

    /// <summary>Identifiant interne stable d'une personne (règle 11) : « PER-000001 ».</summary>
    public static string ReferencePersonne(long id) => Reference("PER", id);

    /// <summary>Référence d'une fiche : « TAJ-000001 », « FPR-000012 »…</summary>
    public static string Reference(string prefixe, long id) => prefixe + "-" + id.ToString("D6", CultureInfo.InvariantCulture);

    /// <summary>Numéro de procédure : « 2026/000123 » (année d'ouverture + identifiant).</summary>
    public static string NumeroProcedure(int annee, long id)
        => annee.ToString("D4", CultureInfo.InvariantCulture) + "/" + id.ToString("D6", CultureInfo.InvariantCulture);

    /// <summary>Numéro d'acte dans sa procédure : « 2026/000123-PV004 ».</summary>
    public static string NumeroActe(string numeroProcedure, int ordre) => $"{numeroProcedure}-PV{ordre.ToString("D3", CultureInfo.InvariantCulture)}";

    /// <summary>Numéro de pièce dans sa procédure : « 2026/000123-P002 ».</summary>
    public static string NumeroPiece(string numeroProcedure, int ordre) => $"{numeroProcedure}-P{ordre.ToString("D3", CultureInfo.InvariantCulture)}";

    /// <summary>Plaque ou numéro de série sans espaces, tirets ni points, en majuscules.</summary>
    public static string Normaliser(string? valeur)
    {
        var builder = new StringBuilder();
        foreach (var c in (valeur ?? string.Empty).Trim().ToUpperInvariant())
            if (char.IsLetterOrDigit(c))
                builder.Append(c);
        return builder.ToString();
    }

    /// <summary>Plaque saisie valable : 2 à 12 lettres ou chiffres une fois normalisée.</summary>
    public static bool ImmatriculationValide(string? valeur) => Normaliser(valeur).Length is >= 2 and <= 12;

    /// <summary>« AB123CD » → « AB-123-CD » (format SIV) ; autres plaques inchangées.</summary>
    public static string FormaterImmatriculation(string? valeur)
    {
        var plaque = Normaliser(valeur);
        return PlaqueSiv.IsMatch(plaque) ? $"{plaque[..2]}-{plaque[2..5]}-{plaque[5..]}" : plaque;
    }

    /// <summary>Date saisie « jj/mm/aaaa » ou « aaaa-mm-jj » (ESX, QBCore) ; null si illisible.</summary>
    public static DateOnly? LireDate(string? texte)
    {
        if (string.IsNullOrWhiteSpace(texte))
            return null;
        var t = texte.Trim();
        string[] formats = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yyyy", "yyyy/MM/dd"];
        return DateOnly.TryParseExact(t, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }

    public static bool HeureValide(string? texte) => !string.IsNullOrWhiteSpace(texte) && Heure.IsMatch(texte.Trim());

    /// <summary>Découpe une saisie de recherche en termes (3 au plus pris en compte par champ).</summary>
    public static IReadOnlyList<string> Termes(string? q)
        => (q ?? string.Empty)
            .Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

    // ===== SNPC =====

    /// <summary>Nouveau solde après retrait (négatif) ou restitution (positif), borné à [0, 12].</summary>
    public static int AppliquerPoints(int solde, int variation) => Math.Clamp(solde + variation, 0, PointsMax);

    /// <summary>Situation du titre après un changement de solde : solde nul → invalidé ; solde rétabli → valide.</summary>
    public static string SituationApresPoints(string situation, int points)
    {
        if (points <= 0)
            return PermisSituations.Invalide;
        return situation == PermisSituations.Invalide ? PermisSituations.Valide : situation;
    }

    // ===== FVA =====

    public static bool Couvert(string statut, DateOnly debut, DateOnly? fin, DateOnly jour)
        => statut == AssuranceStatuts.Valide && debut <= jour && (fin is null || fin.Value >= jour);

    // ===== Transitions =====

    /// <summary>Message d'erreur si le changement de statut FPR est interdit, sinon null.</summary>
    public static string? ControlerTransitionFpr(string ancien, string nouveau, string? motif)
    {
        if (!FprStatuts.All.Contains(nouveau))
            return "Statut FPR inconnu.";
        if (ancien == nouveau)
            return "La fiche a déjà ce statut.";
        var permis = ancien switch
        {
            FprStatuts.Active => nouveau is FprStatuts.Suspendue or FprStatuts.Levee,
            FprStatuts.Suspendue => nouveau is FprStatuts.Active or FprStatuts.Levee,
            FprStatuts.Levee => nouveau is FprStatuts.Archivee,
            _ => false
        };
        if (!permis)
            return $"Passage de « {ancien} » à « {nouveau} » impossible.";
        if (nouveau is FprStatuts.Levee or FprStatuts.Suspendue && string.IsNullOrWhiteSpace(motif))
            return "Le motif est obligatoire pour suspendre ou lever une fiche.";
        return null;
    }

    /// <summary>Un acte validé est figé (règle 10) : plus aucune modification, seulement des mentions.</summary>
    public static bool ActeModifiable(string statut) => statut is ActeStatuts.Brouillon or ActeStatuts.EnRelecture;

    /// <summary>Message d'erreur si la procédure ne peut pas recevoir de nouvel acte ou de modification.</summary>
    public static string? ControlerProcedureOuverte(string statut)
        => statut is ProcedureStatuts.Archivee or ProcedureStatuts.Cloturee
            ? $"La procédure est « {statut} » : elle ne peut plus être modifiée."
            : null;

    /// <summary>Changement de statut d'une procédure ; l'archivage n'est possible qu'après clôture.</summary>
    public static string? ControlerTransitionProcedure(string ancien, string nouveau)
    {
        if (!ProcedureStatuts.All.Contains(nouveau))
            return "Statut de procédure inconnu.";
        if (ancien == nouveau)
            return "La procédure a déjà ce statut.";
        if (ancien == ProcedureStatuts.Archivee)
            return "Une procédure archivée ne change plus de statut.";
        if (nouveau == ProcedureStatuts.Archivee && ancien != ProcedureStatuts.Cloturee)
            return "Clôture d'abord la procédure avant de l'archiver.";
        return null;
    }

    public const string PieceAuService = "Au service";

    /// <summary>Statut d'une pièce après un mouvement ; null si le mouvement est impossible (pièce restituée ou détruite).</summary>
    public static string? StatutApresMouvement(string statutActuel, string mouvement)
    {
        if (statutActuel is "Restituée" or "Détruite")
            return null;
        return mouvement switch
        {
            "Dépôt au greffe" => "Au greffe",
            "Sortie pour expertise" => "En expertise",
            "Retour d'expertise" => statutActuel == "En expertise" ? PieceAuService : null,
            "Transfert de service" => "Transférée",
            "Restitution" => "Restituée",
            "Destruction" => "Détruite",
            _ => null
        };
    }

    /// <summary>Qualité dans la procédure de la personne principale d'un acte.</summary>
    public static string QualitePersonneActe(string type) => type switch
    {
        ActesCatalogue.Pvi => "Mis en cause",
        ActesCatalogue.Plainte => "Plaignant",
        ActesCatalogue.Audition => "Personne entendue librement",
        _ => "Autre"
    };

    /// <summary>Types d'actes dont la personne principale est obligatoire.</summary>
    public static bool PersonneObligatoire(string type)
        => type is ActesCatalogue.Pvi or ActesCatalogue.Plainte or ActesCatalogue.Audition or ActesCatalogue.Convocation;

    /// <summary>
    /// Contrôles avant validation (champs obligatoires, formats de date et d'heure). Liste vide = acte validable.
    /// </summary>
    public static List<string> ControlerActe(TypeActeInfo type, IReadOnlyDictionary<string, string?> champs, long? personneId)
    {
        var erreurs = new List<string>();
        if (PersonneObligatoire(type.Code) && personneId is null)
            erreurs.Add($"{type.LibellePersonne ?? "Personne"} : sélectionne une identité.");
        foreach (var champ in type.Champs)
        {
            champs.TryGetValue(champ.Code, out var valeur);
            var vide = string.IsNullOrWhiteSpace(valeur);
            if (champ.Obligatoire && vide)
            {
                erreurs.Add($"{champ.Section} — {champ.Libelle} : champ obligatoire.");
                continue;
            }
            if (vide)
                continue;
            if (champ.Type == ChampTypes.Date && LireDate(valeur) is null)
                erreurs.Add($"{champ.Section} — {champ.Libelle} : date attendue au format jj/mm/aaaa.");
            if (champ.Type == ChampTypes.Heure && !HeureValide(valeur))
                erreurs.Add($"{champ.Section} — {champ.Libelle} : heure attendue au format hh:mm.");
            if (champ.Choix is { Count: > 0 } choix && champ.Type is ChampTypes.Choix or ChampTypes.OuiNon && !choix.Contains(valeur!.Trim()))
                erreurs.Add($"{champ.Section} — {champ.Libelle} : valeur non prévue.");
        }
        return erreurs;
    }

    /// <summary>Champs d'un acte nettoyés : seuls les codes du type sont conservés, textes tronqués à 8000 caractères.</summary>
    public static Dictionary<string, string?> NettoyerChamps(TypeActeInfo type, IReadOnlyDictionary<string, string?>? champs)
    {
        var resultat = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (champs is null)
            return resultat;
        foreach (var champ in type.Champs)
            if (champs.TryGetValue(champ.Code, out var valeur) && !string.IsNullOrWhiteSpace(valeur))
                resultat[champ.Code] = valeur.Trim().Length > 8000 ? valeur.Trim()[..8000] : valeur.Trim();
        return resultat;
    }

    /// <summary>Remplace les variables d'un modèle ({date}, {redacteur}…) ; variables inconnues laissées telles quelles.</summary>
    public static string RemplacerVariables(string modele, IReadOnlyDictionary<string, string?> valeurs)
    {
        var texte = modele ?? string.Empty;
        foreach (var (cle, valeur) in valeurs)
            texte = texte.Replace("{" + cle + "}", string.IsNullOrWhiteSpace(valeur) ? "—" : valeur, StringComparison.Ordinal);
        return texte;
    }

    /// <summary>Année en toutes lettres pour l'en-tête des procès-verbaux (« deux mille vingt-six »).</summary>
    public static string AnneeEnLettres(int annee)
    {
        if (annee is < 2000 or > 2099)
            return annee.ToString(CultureInfo.InvariantCulture);
        var reste = annee - 2000;
        return reste == 0 ? "deux mille" : "deux mille " + NombreEnLettres(reste);
    }

    private static string NombreEnLettres(int n)
    {
        string[] unites = ["zéro", "un", "deux", "trois", "quatre", "cinq", "six", "sept", "huit", "neuf", "dix", "onze", "douze",
            "treize", "quatorze", "quinze", "seize", "dix-sept", "dix-huit", "dix-neuf"];
        string[] dizaines = ["", "", "vingt", "trente", "quarante", "cinquante", "soixante", "soixante", "quatre-vingt", "quatre-vingt"];
        if (n < 20)
            return unites[n];
        var d = n / 10;
        var u = n % 10;
        if (d is 7 or 9)
            return dizaines[d] + (u == 1 && d == 7 ? "-et-" : "-") + unites[10 + u];
        if (u == 0)
            return d == 8 ? "quatre-vingts" : dizaines[d];
        return dizaines[d] + (u == 1 && d != 8 ? "-et-un" : "-" + unites[u]);
    }

    public static string DateLongue(DateTimeOffset date) => date.ToString("dddd d MMMM yyyy", Fr);

    // ===== Fichiers =====

    private static readonly IReadOnlyDictionary<string, string> TypesAutorises = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".txt"] = "text/plain",
        [".mp4"] = "video/mp4",
        [".webm"] = "video/webm",
        [".mov"] = "video/quicktime",
        [".odt"] = "application/vnd.oasis.opendocument.text",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    };

    /// <summary>
    /// Contrôle d'un fichier téléversé (22) : nom, extension autorisée, taille ≤ 10 Mo, signature des PDF et images.
    /// Retourne le contenu décodé et le type MIME retenu, ou lève <see cref="ArgumentException"/> avec un message lisible.
    /// </summary>
    public static (byte[] Contenu, string TypeMime, string Nom) ControlerFichier(string? nom, string? contenuBase64)
    {
        var nomPropre = Path.GetFileName((nom ?? string.Empty).Trim());
        if (string.IsNullOrWhiteSpace(nomPropre) || nomPropre.Length > 200)
            throw new ArgumentException("Nom de fichier invalide.");
        var extension = Path.GetExtension(nomPropre);
        if (!TypesAutorises.TryGetValue(extension, out var mime))
            throw new ArgumentException("Type de fichier non autorisé (PDF, images, vidéos, texte, ODT ou DOCX uniquement).");
        if (string.IsNullOrWhiteSpace(contenuBase64))
            throw new ArgumentException("Le fichier est vide.");
        if (contenuBase64.Length > (TailleMaxFichier / 3 + 2) * 4)
            throw new ArgumentException("Le fichier dépasse 10 Mo.");

        byte[] contenu;
        try
        {
            contenu = Convert.FromBase64String(contenuBase64);
        }
        catch (FormatException)
        {
            throw new ArgumentException("Le contenu du fichier est illisible.");
        }
        if (contenu.Length == 0)
            throw new ArgumentException("Le fichier est vide.");
        if (contenu.Length > TailleMaxFichier)
            throw new ArgumentException("Le fichier dépasse 10 Mo.");
        if (!SignatureCompatible(mime, contenu))
            throw new ArgumentException("Le contenu du fichier ne correspond pas à son extension.");
        return (contenu, mime, nomPropre);
    }

    private static bool SignatureCompatible(string mime, byte[] c) => mime switch
    {
        "application/pdf" => c.Length >= 4 && c[0] == '%' && c[1] == 'P' && c[2] == 'D' && c[3] == 'F',
        "image/png" => c.Length >= 4 && c[0] == 0x89 && c[1] == 'P' && c[2] == 'N' && c[3] == 'G',
        "image/jpeg" => c.Length >= 3 && c[0] == 0xFF && c[1] == 0xD8 && c[2] == 0xFF,
        "image/gif" => c.Length >= 3 && c[0] == 'G' && c[1] == 'I' && c[2] == 'F',
        _ => true
    };

    /// <summary>Empreinte SHA-256 en hexadécimal (contrôle d'intégrité des actes validés et des pièces).</summary>
    public static string Empreinte(byte[] contenu) => Convert.ToHexString(SHA256.HashData(contenu)).ToLowerInvariant();

    public static string Empreinte(string texte) => Empreinte(Encoding.UTF8.GetBytes(texte));

    /// <summary>Nom de fichier sûr pour un téléchargement : « PV 2026-000123-PV001.pdf ».</summary>
    public static string NomFichierSur(string texte)
    {
        var builder = new StringBuilder();
        foreach (var c in texte)
            builder.Append(char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.' ? c : '-');
        var resultat = builder.ToString().Trim();
        return resultat.Length == 0 ? "document" : resultat;
    }
}
