using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Saphir.Shared.Contracts;

namespace Saphir.Server;

/// <summary>Règles pures du socle (sans base) : identifiants, validations, formats. Testées dans SAPHIR.Tests.</summary>
public static class SocleRegles
{
    public const string VersionParDefaut = "0.2.0.0";
    public const string RioFormatParDefaut = @"^\d{7}$";
    public const string RioExempleParDefaut = "1234567";
    public const string NomOrganisationParDefaut = "Police nationale — Val-de-Marne Simulation";

    /// <summary>Préfixe des matricules générés (département du Val-de-Marne).</summary>
    public const string PrefixeMatricule = "94";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly Regex VersionRegex = new(@"^[0-9]{1,9}(\.[0-9]{1,9}){3}$", RegexOptions.CultureInvariant);

    /// <summary>Paramètres centraux créés au démarrage (la valeur n'est jamais écrasée ensuite).</summary>
    public static readonly IReadOnlyList<ParametreDto> ParametresParDefaut =
    [
        new(ParametresCles.Version, VersionParDefaut, "Version de SAPHIR",
            "Affichée « SAPHIR - v… » sur tous les postes. Format : 1.2.3.4.", true),
        new(ParametresCles.NomOrganisation, NomOrganisationParDefaut, "Nom de l'organisation",
            "Affiché dans les en-têtes et les documents.", true),
        new(ParametresCles.RioFormat, RioFormatParDefaut, "Format du RIO",
            "Expression régulière que doit respecter tout RIO (par défaut : 7 chiffres).", true),
        new(ParametresCles.RioExemple, RioExempleParDefaut, "Exemple de RIO",
            "Exemple affiché dans les formulaires ; il doit respecter le format du RIO.", true)
    ];

    /// <summary>Référence d'un agent : « AG-000123 ».</summary>
    public static string ReferenceAgent(long id) => "AG-" + id.ToString("D6", CultureInfo.InvariantCulture);

    /// <summary>
    /// Matricule généré : « 94 » + identifiant sur 6 chiffres (« 94000123 »). En cas de collision (matricule saisi
    /// à la main ailleurs), <paramref name="tentative"/> &gt; 0 ajoute un suffixe : « 94000123-1 ».
    /// </summary>
    public static string Matricule(long id, int tentative = 0)
    {
        var baseMatricule = PrefixeMatricule + id.ToString("D6", CultureInfo.InvariantCulture);
        return tentative <= 0 ? baseMatricule : $"{baseMatricule}-{tentative.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>Version au format 1.2.3.4.</summary>
    public static bool VersionValide(string? version)
        => !string.IsNullOrWhiteSpace(version) && VersionRegex.IsMatch(version.Trim());

    /// <summary>« SAPHIR - v0.2.0.0 ».</summary>
    public static string AffichageVersion(string? version)
        => "SAPHIR - v" + (VersionValide(version) ? version!.Trim() : VersionParDefaut);

    /// <summary>Vrai si <paramref name="pattern"/> est une expression régulière .NET correcte (et non vide).</summary>
    public static bool RegexValide(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;
        try
        {
            _ = new Regex(pattern, RegexOptions.CultureInvariant, RegexTimeout);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Vrai si le RIO respecte le format (expression régulière du paramètre rh.rio.format).</summary>
    public static bool RioValide(string? rio, string format)
    {
        if (string.IsNullOrWhiteSpace(rio))
            return false;
        try
        {
            return Regex.IsMatch(rio.Trim(), format, RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Contrôle la valeur d'un paramètre central. Retourne le message d'erreur, ou <c>null</c> si la valeur est acceptée.
    /// <paramref name="formatRio"/> = format du RIO en vigueur (pour contrôler l'exemple).
    /// </summary>
    public static string? ValiderParametre(string cle, string valeur, string? formatRio)
    {
        if (string.IsNullOrWhiteSpace(valeur))
            return "La valeur ne peut pas être vide.";
        if (valeur.Length > 2000)
            return "La valeur est trop longue (2000 caractères au plus).";

        return cle switch
        {
            ParametresCles.Version => VersionValide(valeur)
                ? null
                : "Version invalide : elle doit ressembler à 1.2.3.4.",
            ParametresCles.RioFormat => RegexValide(valeur)
                ? null
                : "Format du RIO invalide : l'expression régulière n'est pas correcte.",
            ParametresCles.RioExemple => formatRio is not null && RegexValide(formatRio) && !RioValide(valeur, formatRio)
                ? $"L'exemple ne respecte pas le format du RIO en vigueur ({formatRio})."
                : null,
            ParametresCles.NomOrganisation => valeur.Trim().Length > 200
                ? "Le nom de l'organisation est trop long (200 caractères au plus)."
                : null,
            _ => null
        };
    }

    /// <summary>Code de profil tiré du nom : « Chef d'équipe CIC » → « chef-d-equipe-cic » (64 caractères au plus).</summary>
    public static string CodeDepuisNom(string? nom)
    {
        var decomposed = (nom ?? string.Empty).Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var tiret = false;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(c);
                tiret = false;
            }
            else if (builder.Length > 0 && !tiret)
            {
                builder.Append('-');
                tiret = true;
            }
        }

        var code = builder.ToString().Trim('-');
        if (code.Length > 64)
            code = code[..64].TrimEnd('-');
        return code.Length == 0 ? "profil" : code;
    }

    /// <summary>Code de profil rendu unique : « code », puis « code-2 », « code-3 »…</summary>
    public static string CodeAvecSuffixe(string code, int numero)
    {
        if (numero <= 1)
            return code;
        var suffixe = "-" + numero.ToString(CultureInfo.InvariantCulture);
        var racine = code.Length + suffixe.Length > 64 ? code[..(64 - suffixe.Length)].TrimEnd('-') : code;
        return racine + suffixe;
    }

    /// <summary>Message affiché à un agent qui ne peut plus se connecter.</summary>
    public static string MessageRefus(string statut) => statut switch
    {
        AgentStatuts.Suspendu => "Accès refusé : ton compte SAPHIR est suspendu. Rapproche-toi de ta hiérarchie.",
        AgentStatuts.Inactif => "Accès refusé : ton compte SAPHIR est inactif. Rapproche-toi de ta hiérarchie.",
        AgentStatuts.Archive => "Accès refusé : ton compte SAPHIR est archivé.",
        _ => $"Accès refusé : ton compte SAPHIR est au statut « {statut} »."
    };

    /// <summary>« NOM Prénom (AG-000123) », utilisé comme auteur dans l'historique et le journal.</summary>
    public static string LibelleAgent(AgentDto agent) => $"{agent.Nom} {agent.Prenom} ({agent.Reference})";

    /// <summary>Échappe une saisie pour LIKE (\, % et _).</summary>
    public static string EchapperLike(string texte)
        => texte.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    /// <summary>Nombre de lignes demandé, borné à [1, maximum].</summary>
    public static int Limite(int? demande, int parDefaut = 200, int maximum = 1000)
        => demande is null or <= 0 ? parDefaut : Math.Min(demande.Value, maximum);

    /// <summary>Texte facultatif nettoyé et tronqué ; <c>null</c> si vide.</summary>
    public static string? Tronquer(string? texte, int maximum)
    {
        if (string.IsNullOrWhiteSpace(texte))
            return null;
        var t = texte.Trim();
        return t.Length <= maximum ? t : t[..maximum];
    }

    /// <summary>Liste lisible pour l'historique : « a, b, c » (ou « aucun »).</summary>
    public static string Liste(IEnumerable<string> valeurs)
    {
        var texte = string.Join(", ", valeurs);
        return texte.Length == 0 ? "aucun" : texte;
    }
}
