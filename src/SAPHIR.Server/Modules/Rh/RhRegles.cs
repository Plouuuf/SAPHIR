using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Rh;

namespace Saphir.Server.Modules.Rh;

/// <summary>Objets RH suivis dans l'historique (sys_historique.objet). Les agents gardent l'objet « agent » du socle.</summary>
public static class ObjetsRh
{
    public const string Grade = "rh-grade";
    public const string Service = "rh-service";
    public const string Unite = "rh-unite";
    public const string Fonction = "rh-fonction";
    public const string Document = "rh-document";
    public const string Dossier = "rh-dossier";
    public const string Categorie = "rh-categorie";
    public const string Modele = "rh-modele";

    /// <summary>Objets affichés dans l'historique RH (le socle y ajoute « agent »).</summary>
    public static readonly IReadOnlyList<string> All = [Grade, Service, Unite, Fonction, Document, Dossier, Categorie, Modele];

    public static string DepuisType(string type) => type switch
    {
        RhReferentielTypes.Grades => Grade,
        RhReferentielTypes.Services => Service,
        RhReferentielTypes.Unites => Unite,
        _ => Fonction
    };
}

/// <summary>Valeurs disponibles pour remplir un modèle de document.</summary>
public sealed record ValeursModele(
    string Nom,
    string Prenom,
    string? Rio,
    string? Grade,
    string? Service,
    string? Unite,
    string? Fonction,
    string? Matricule,
    string Reference,
    DateOnly Date,
    string? Responsable,
    string? Numero,
    string? Organisation);

/// <summary>Résultat du contrôle d'un fichier envoyé.</summary>
public sealed record FichierControle(string NomFichier, string Extension, string TypeMime, string Sha256);

/// <summary>Règles pures du module RH (sans base) : testées dans SAPHIR.Tests/RhTests.cs.</summary>
public static partial class RhRegles
{
    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Texte affiché à la place d'une valeur absente dans un document généré.</summary>
    public const string ValeurAbsente = "—";

    [GeneratedRegex(@"\{\{\s*([A-Za-z_]+)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex VariableRegex();

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9\-]{1,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex MatriculeRegex();

    /// <summary>
    /// Un agent « Profil à compléter » devient « Actif » dès que RIO, grade, service et unité sont renseignés.
    /// </summary>
    public static bool ProfilComplet(string? rio, long? gradeId, long? serviceId, long? uniteId)
        => !string.IsNullOrWhiteSpace(rio) && gradeId is not null && serviceId is not null && uniteId is not null;

    /// <summary>Statut qui suit une modification de la fiche : passage automatique à Actif si le profil est complet.</summary>
    public static string StatutApresMiseAJour(string statut, bool complet)
        => statut == AgentStatuts.ProfilACompleter && complet ? AgentStatuts.Actif : statut;

    /// <summary>Statut d'une réactivation : Actif si le profil est complet, sinon Profil à compléter.</summary>
    public static string StatutReactivation(bool complet) => complet ? AgentStatuts.Actif : AgentStatuts.ProfilACompleter;

    /// <summary>Le motif est obligatoire pour bloquer un compte (suspension, inactivité, archivage).</summary>
    public static bool MotifObligatoirePourStatut(string statut) => !AgentStatuts.PeutSeConnecter(statut);

    /// <summary>Matricule saisi par le RH : 2 à 32 caractères, lettres majuscules, chiffres et tirets.</summary>
    public static string? NormaliserMatricule(string? matricule)
    {
        var m = (matricule ?? string.Empty).Trim().ToUpperInvariant();
        return MatriculeRegex().IsMatch(m) ? m : null;
    }

    /// <summary>Code de référentiel tiré du libellé : « BPS / BAC » → « BPS-BAC » (32 caractères au plus).</summary>
    public static string CodeReferentiel(string libelle)
    {
        var code = SocleRegles.CodeDepuisNom(libelle).ToUpperInvariant();
        return code.Length > 32 ? code[..32].TrimEnd('-') : code;
    }

    /// <summary>Référence d'un document RH : « DOC-RH-000042 ».</summary>
    public static string ReferenceDocument(long id) => "DOC-RH-" + id.ToString("D6", CultureInfo.InvariantCulture);

    /// <summary>« Confidentiel RH » n'est visible qu'avec une permission documentaire.</summary>
    public static bool PeutVoirConfidentiel(bool gereDocuments, bool valideDocuments) => gereDocuments || valideDocuments;

    public static string NormaliserConfidentialite(string? valeur)
        => RhConfidentialites.All.FirstOrDefault(c => string.Equals(c, valeur?.Trim(), StringComparison.OrdinalIgnoreCase))
           ?? RhConfidentialites.Normal;

    /// <summary>Date au format français « 02/11/2026 ».</summary>
    public static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", Francais);

    public static string Date(DateOnly? date) => date is { } d ? Date(d) : ValeurAbsente;

    // =====================================================================================
    // Modèles
    // =====================================================================================

    /// <summary>Dictionnaire des variables (codes du cahier « agent_nom » et alias courts « NOM »), insensible à la casse.</summary>
    public static Dictionary<string, string> Variables(ValeursModele v)
    {
        static string Val(string? s) => string.IsNullOrWhiteSpace(s) ? ValeurAbsente : s.Trim();
        var valeurs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Ajouter(string code, string alias, string? valeur)
        {
            valeurs[code] = Val(valeur);
            valeurs[alias] = Val(valeur);
        }

        Ajouter("agent_nom", "NOM", v.Nom);
        Ajouter("agent_prenom", "PRENOM", v.Prenom);
        Ajouter("agent_rio", "RIO", v.Rio);
        Ajouter("agent_grade", "GRADE", v.Grade);
        Ajouter("agent_service", "SERVICE", v.Service);
        Ajouter("agent_unite", "UNITE", v.Unite);
        Ajouter("agent_fonction", "FONCTION", v.Fonction);
        Ajouter("agent_matricule", "MATRICULE", v.Matricule);
        Ajouter("agent_reference", "REFERENCE", v.Reference);
        Ajouter("date_document", "DATE", Date(v.Date));
        Ajouter("responsable_nom", "RESPONSABLE", v.Responsable);
        Ajouter("numero_document", "NUMERO", v.Numero);
        Ajouter("organisation", "ORGANISATION", v.Organisation);
        return valeurs;
    }

    /// <summary>
    /// Remplace les variables {{…}} du modèle. Les variables inconnues restent telles quelles et sont signalées.
    /// </summary>
    public static (string Texte, List<string> Inconnues) RemplirModele(string modele, IReadOnlyDictionary<string, string> valeurs)
    {
        var inconnues = new List<string>();
        var texte = VariableRegex().Replace(modele ?? string.Empty, m =>
        {
            var nom = m.Groups[1].Value;
            if (valeurs.TryGetValue(nom, out var valeur))
                return valeur;
            if (!inconnues.Contains(nom, StringComparer.OrdinalIgnoreCase))
                inconnues.Add(nom);
            return m.Value;
        });
        return (texte, inconnues);
    }

    // =====================================================================================
    // Fichiers
    // =====================================================================================

    /// <summary>Nom de fichier sûr : sans chemin, sans caractères de contrôle ni réservés, 150 caractères au plus.</summary>
    public static string NettoyerNomFichier(string? nom)
    {
        var brut = (nom ?? string.Empty).Replace('\\', '/');
        var dernier = brut.LastIndexOf('/');
        if (dernier >= 0)
            brut = brut[(dernier + 1)..];
        var sb = new StringBuilder(brut.Length);
        foreach (var c in brut.Trim())
            sb.Append(char.IsControl(c) || "<>:\"/\\|?*".Contains(c) ? '_' : c);
        var resultat = sb.ToString().Trim(' ', '.');
        if (resultat.Length > 150)
        {
            var ext = Path.GetExtension(resultat);
            resultat = resultat[..(150 - ext.Length)] + ext;
        }
        return resultat.Length == 0 ? "document" : resultat;
    }

    /// <summary>
    /// Contrôle d'un fichier envoyé : taille (1 octet à 10 Mo), extension autorisée et signature du contenu cohérente.
    /// Retourne le message d'erreur, ou <c>null</c> et le contrôle si le fichier est accepté.
    /// </summary>
    public static (FichierControle? Controle, string? Erreur) ControlerFichier(string? nomFichier, byte[]? contenu)
    {
        if (contenu is null || contenu.Length == 0)
            return (null, "Le fichier est vide.");
        if (contenu.Length > RhFichiers.TailleMax)
            return (null, "Le fichier dépasse 10 Mo.");

        var nom = NettoyerNomFichier(nomFichier);
        var extension = Path.GetExtension(nom).ToLowerInvariant();
        if (!RhFichiers.Extensions.Contains(extension))
            return (null, $"Type de fichier refusé. Types acceptés : {string.Join(", ", RhFichiers.Extensions)}.");

        var signatureOk = extension switch
        {
            ".pdf" => Commence(contenu, "%PDF-"u8),
            ".png" => Commence(contenu, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
            ".jpg" or ".jpeg" => Commence(contenu, [0xFF, 0xD8, 0xFF]),
            ".docx" => Commence(contenu, [0x50, 0x4B, 0x03, 0x04]),
            ".txt" => TexteValide(contenu),
            _ => false
        };
        if (!signatureOk)
            return (null, "Le contenu du fichier ne correspond pas à son extension.");

        return (new FichierControle(nom, extension, RhFichiers.TypeMime(extension), Sha256(contenu)), null);
    }

    public static string Sha256(byte[] contenu) => Convert.ToHexString(SHA256.HashData(contenu)).ToLowerInvariant();

    private static bool Commence(byte[] contenu, ReadOnlySpan<byte> signature)
        => contenu.Length >= signature.Length && contenu.AsSpan(0, signature.Length).SequenceEqual(signature);

    private static bool TexteValide(byte[] contenu)
    {
        if (Array.IndexOf(contenu, (byte)0) >= 0)
            return false;
        try
        {
            _ = new UTF8Encoding(false, true).GetString(contenu);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>Nom de fichier PDF tiré d'un titre : « Attestation administrative » → « Attestation administrative.pdf ».</summary>
    public static string NomFichierPdf(string titre, string reference)
    {
        var nom = NettoyerNomFichier($"{reference} - {titre}");
        return nom.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? nom : nom + ".pdf";
    }
}
