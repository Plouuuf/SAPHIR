using System.Globalization;
using System.Text;

namespace Saphir.Shared.Contracts.Complements;

/// <summary>
/// Lecture des bandes MRZ (zone de lecture automatique) de documents fictifs :
/// TD1 (carte d'identité, 3 × 30), TD3 (passeport, 2 × 44) et ancienne carte nationale d'identité française (2 × 36).
/// Chiffres de contrôle : pondération 7-3-1, modulo 10 (chiffres = valeur, A-Z = 10-35, « &lt; » = 0).
/// Classe pure (aucun accès réseau ni base) : utilisée par le serveur et testée sans base.
/// </summary>
public static class MrzParser
{
    public const string FormatTd1 = "TD1 (carte d'identité, 3 × 30)";
    public const string FormatTd3 = "TD3 (passeport, 2 × 44)";
    public const string FormatCniFr = "Ancienne CNI française (2 × 36)";
    public const string FormatInconnu = "Format non reconnu";

    private static readonly int[] Poids = [7, 3, 1];

    /// <summary>Valeur d'un caractère MRZ (−1 si invalide).</summary>
    public static int Valeur(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'A' and <= 'Z' => c - 'A' + 10,
        '<' => 0,
        _ => -1
    };

    /// <summary>Chiffre de contrôle 7-3-1 d'une donnée (−1 si un caractère est invalide).</summary>
    public static int ChiffreControle(string donnee)
    {
        var somme = 0;
        for (var i = 0; i < donnee.Length; i++)
        {
            var v = Valeur(donnee[i]);
            if (v < 0)
                return -1;
            somme += v * Poids[i % 3];
        }
        return somme % 10;
    }

    /// <summary>
    /// Nettoie la saisie : majuscules, « « » et espaces internes convertis, lignes vides ignorées ;
    /// une bande collée sur une seule ligne (72, 88 ou 90 caractères) est redécoupée.
    /// </summary>
    public static IReadOnlyList<string> Normaliser(string? texte)
    {
        if (string.IsNullOrWhiteSpace(texte))
            return Array.Empty<string>();

        var lignes = texte.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n')
            .Select(NettoyerLigne)
            .Where(l => l.Length > 0)
            .ToList();

        if (lignes.Count == 1)
        {
            var l = lignes[0];
            if (l.Length == 90) return [l[..30], l[30..60], l[60..]];
            if (l.Length == 88) return [l[..44], l[44..]];
            if (l.Length == 72) return [l[..36], l[36..]];
        }
        return lignes;
    }

    private static string NettoyerLigne(string ligne)
    {
        var sb = new StringBuilder(ligne.Length);
        foreach (var c in ligne.Trim().ToUpperInvariant())
        {
            if (c is ' ' or '\t')
                continue;
            sb.Append(c is '«' or '‹' or '≪' ? '<' : c);
        }
        return sb.ToString();
    }

    /// <summary>Analyse une bande MRZ. <paramref name="aujourdhui"/> sert à fixer le siècle des dates (tests).</summary>
    public static MrzResultatDto Analyser(string? texte, DateOnly? aujourdhui = null)
    {
        var jour = aujourdhui ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var lignes = Normaliser(texte);
        var erreurs = new List<string>();

        if (lignes.Count == 0)
            return Echec(lignes, "Aucune bande MRZ saisie.");

        foreach (var l in lignes)
            if (l.Any(c => Valeur(c) < 0))
                return Echec(lignes, "Caractère non autorisé dans la bande : seuls A-Z, 0-9 et « < » sont admis.");

        if (lignes.Count == 3 && lignes.All(l => l.Length == 30))
            return Td1(lignes, jour);
        if (lignes.Count == 2 && lignes.All(l => l.Length == 44))
            return Td3(lignes, jour);
        if (lignes.Count == 2 && lignes.All(l => l.Length == 36))
            return CniFr(lignes, jour);

        erreurs.Add($"Longueurs lues : {string.Join(" / ", lignes.Select(l => l.Length))}. Attendu : 3 lignes de 30 (TD1), 2 lignes de 44 (TD3) ou 2 lignes de 36 (CNI française).");
        return new MrzResultatDto(false, false, FormatInconnu, null, null, null, null, null, null, null, null, null, null,
            Array.Empty<MrzControleDto>(), erreurs, lignes);
    }

    private static MrzResultatDto Echec(IReadOnlyList<string> lignes, string erreur)
        => new(false, false, FormatInconnu, null, null, null, null, null, null, null, null, null, null,
            Array.Empty<MrzControleDto>(), [erreur], lignes);

    // ----- TD1 : 3 × 30 -----
    private static MrzResultatDto Td1(IReadOnlyList<string> l, DateOnly jour)
    {
        string l1 = l[0], l2 = l[1], l3 = l[2];
        var controles = new List<MrzControleDto>();
        var erreurs = new List<string>();

        var type = Champ(l1[..2]);
        var pays = Champ(l1[2..5]);
        var numero = l1[5..14];
        var optionnel1 = l1[15..30];
        // Numéro long (plus de 9 caractères) : la suite est dans la zone optionnelle, terminée par le chiffre de contrôle.
        if (l1[14] == '<' && Champ(optionnel1) is { Length: > 1 } suite)
        {
            var reste = suite.Split('<')[0];
            numero += reste[..^1];
            controles.Add(Controle("Numéro du document", numero, reste[^1]));
        }
        else
        {
            controles.Add(Controle("Numéro du document", numero, l1[14]));
        }

        var naissance = l2[..6];
        controles.Add(Controle("Date de naissance", naissance, l2[6]));
        var sexe = Sexe(l2[7]);
        var expiration = l2[8..14];
        controles.Add(Controle("Date d'expiration", expiration, l2[14]));
        var nationalite = Champ(l2[15..18]);
        var optionnel2 = l2[18..29];
        controles.Add(Controle("Contrôle global", l1[5..30] + l2[..7] + l2[8..15] + l2[18..29], l2[29]));

        var (nom, prenoms) = Noms(l3);
        var dateNaissance = Date(naissance, jour, naissance: true, erreurs, "Date de naissance");
        var dateExpiration = Date(expiration, jour, naissance: false, erreurs, "Date d'expiration");

        var optionnel = string.Join(" ", new[] { Champ(optionnel1), Champ(optionnel2) }.Where(s => s.Length > 0));
        return Resultat(FormatTd1, type, pays, Champ(numero), nom, prenoms, nationalite, dateNaissance, sexe, dateExpiration,
            optionnel, controles, erreurs, l);
    }

    // ----- TD3 : 2 × 44 -----
    private static MrzResultatDto Td3(IReadOnlyList<string> l, DateOnly jour)
    {
        string l1 = l[0], l2 = l[1];
        var controles = new List<MrzControleDto>();
        var erreurs = new List<string>();

        var type = Champ(l1[..2]);
        var pays = Champ(l1[2..5]);
        var (nom, prenoms) = Noms(l1[5..]);

        var numero = l2[..9];
        controles.Add(Controle("Numéro du document", numero, l2[9]));
        var nationalite = Champ(l2[10..13]);
        var naissance = l2[13..19];
        controles.Add(Controle("Date de naissance", naissance, l2[19]));
        var sexe = Sexe(l2[20]);
        var expiration = l2[21..27];
        controles.Add(Controle("Date d'expiration", expiration, l2[27]));
        var personnel = l2[28..42];
        // Numéro personnel absent : le chiffre de contrôle peut être « < » (équivalent à 0).
        controles.Add(Controle("Numéro personnel", personnel, l2[42], videTolere: true));
        controles.Add(Controle("Contrôle global", l2[..10] + l2[13..20] + l2[21..43], l2[43]));

        var dateNaissance = Date(naissance, jour, naissance: true, erreurs, "Date de naissance");
        var dateExpiration = Date(expiration, jour, naissance: false, erreurs, "Date d'expiration");

        return Resultat(FormatTd3, type, pays, Champ(numero), nom, prenoms, nationalite, dateNaissance, sexe, dateExpiration,
            Champ(personnel), controles, erreurs, l);
    }

    // ----- Ancienne CNI française : 2 × 36 -----
    private static MrzResultatDto CniFr(IReadOnlyList<string> l, DateOnly jour)
    {
        string l1 = l[0], l2 = l[1];
        var controles = new List<MrzControleDto>();
        var erreurs = new List<string>();

        var type = Champ(l1[..2]);
        var pays = Champ(l1[2..5]);
        if (type != "ID" || pays != "FRA")
            erreurs.Add("Une ancienne CNI française commence par « IDFRA ».");
        var nom = Champ(l1[5..30]).Replace('<', ' ').Trim();
        var bureau = Champ(l1[30..36]);

        var numero = l2[..12];
        controles.Add(Controle("Numéro de la carte", numero, l2[12]));
        var prenoms = string.Join(", ", l2[13..27].Split("<<", StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Replace('<', ' ').Trim()).Where(p => p.Length > 0));
        var naissance = l2[27..33];
        controles.Add(Controle("Date de naissance", naissance, l2[33]));
        var sexe = Sexe(l2[34]);
        controles.Add(Controle("Contrôle global", l1 + l2[..35], l2[35]));

        var dateNaissance = Date(naissance, jour, naissance: true, erreurs, "Date de naissance");

        // Date de délivrance (AAMM) en tête du numéro : validité 15 ans pour une carte délivrée majeur depuis 2004 (indicatif).
        DateOnly? expiration = null;
        if (int.TryParse(numero[..2], NumberStyles.None, CultureInfo.InvariantCulture, out var aa) &&
            int.TryParse(numero[2..4], NumberStyles.None, CultureInfo.InvariantCulture, out var mm) && mm is >= 1 and <= 12)
        {
            var annee = 2000 + aa > jour.Year ? 1900 + aa : 2000 + aa;
            expiration = new DateOnly(annee, mm, 1).AddYears(annee >= 2004 ? 15 : 10).AddDays(-1);
        }

        return Resultat(FormatCniFr, type, pays, Champ(numero), nom, prenoms, "FRA", dateNaissance, sexe, expiration,
            bureau.Length > 0 ? "Bureau " + bureau : null, controles, erreurs, l);
    }

    private static MrzResultatDto Resultat(string format, string type, string pays, string numero, string nom, string prenoms,
        string nationalite, DateOnly? naissance, string? sexe, DateOnly? expiration, string? optionnel,
        List<MrzControleDto> controles, List<string> erreurs, IReadOnlyList<string> lignes)
    {
        foreach (var c in controles.Where(c => !c.Ok))
            erreurs.Add($"Chiffre de contrôle incorrect : {c.Libelle} (attendu {c.Attendu}, lu {c.Lu}).");
        if (nom.Length == 0)
            erreurs.Add("Nom absent de la bande.");
        var ok = erreurs.Count == 0;
        return new MrzResultatDto(true, ok, format, Vide(type), Vide(pays), Vide(numero), Vide(nom), Vide(prenoms), Vide(nationalite),
            naissance, sexe, expiration, Vide(optionnel), controles, erreurs, lignes);
    }

    private static MrzControleDto Controle(string libelle, string donnee, char lu, bool videTolere = false)
    {
        var attendu = ChiffreControle(donnee);
        var ok = attendu >= 0 && (Valeur(lu) == attendu && lu != '<' || (lu == '<' && attendu == 0 && (videTolere || donnee.All(c => c == '<'))));
        return new MrzControleDto(libelle, ok, attendu < 0 ? "?" : attendu.ToString(CultureInfo.InvariantCulture), lu.ToString());
    }

    /// <summary>« DURAND&lt;&lt;JEAN&lt;PIERRE » → (« DURAND », « JEAN PIERRE »).</summary>
    public static (string Nom, string Prenoms) Noms(string zone)
    {
        var z = zone.TrimEnd('<');
        var separation = z.IndexOf("<<", StringComparison.Ordinal);
        if (separation < 0)
            return (z.Replace('<', ' ').Trim(), string.Empty);
        var nom = z[..separation].Replace('<', ' ').Trim();
        var prenoms = z[(separation + 2)..].Replace('<', ' ').Trim();
        while (prenoms.Contains("  ", StringComparison.Ordinal))
            prenoms = prenoms.Replace("  ", " ", StringComparison.Ordinal);
        return (nom, prenoms);
    }

    private static string? Sexe(char c) => c switch
    {
        'M' => "M",
        'F' => "F",
        _ => null
    };

    private static DateOnly? Date(string aammjj, DateOnly jour, bool naissance, List<string> erreurs, string libelle)
    {
        if (aammjj.Contains('<'))
            return null;
        if (!int.TryParse(aammjj[..2], NumberStyles.None, CultureInfo.InvariantCulture, out var aa) ||
            !int.TryParse(aammjj[2..4], NumberStyles.None, CultureInfo.InvariantCulture, out var mm) ||
            !int.TryParse(aammjj[4..6], NumberStyles.None, CultureInfo.InvariantCulture, out var jj))
        {
            erreurs.Add($"{libelle} illisible ({aammjj}).");
            return null;
        }
        // Naissance : jamais dans le futur. Expiration : 20xx sauf années très anciennes.
        var annee = naissance
            ? (2000 + aa > jour.Year ? 1900 + aa : 2000 + aa)
            : (aa >= 70 ? 1900 + aa : 2000 + aa);
        if (mm is < 1 or > 12 || jj < 1 || jj > DateTime.DaysInMonth(annee, mm))
        {
            erreurs.Add($"{libelle} invalide ({aammjj}).");
            return null;
        }
        return new DateOnly(annee, mm, jj);
    }

    private static string Champ(string s) => s.Trim('<');

    private static string? Vide(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
