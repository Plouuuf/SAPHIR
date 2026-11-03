using System.Globalization;
using System.Text;

namespace Saphir.Server.Modules.Judiciaire;

/// <summary>
/// Générateur PDF minimal, sans dépendance externe : pages A4, polices standard Helvetica (normal et gras),
/// encodage WinAnsi (accents français), retour à la ligne automatique, en-tête, pied de page numéroté
/// et filigrane facultatif (« PROJET » pour un acte non validé).
/// </summary>
public sealed class PdfSimple
{
    private const double LargeurPage = 595.28;
    private const double HauteurPage = 841.89;
    private const double Marge = 56;
    private const double HautContenu = HauteurPage - 70;
    private const double BasContenu = 60;

    private sealed record Ligne(string Texte, bool Gras, double Taille, double Retrait, double EspaceAvant, bool Centre, bool Filet);

    private readonly List<Ligne> _lignes = [];

    public string EnTete { get; set; } = string.Empty;
    public string PiedDePage { get; set; } = string.Empty;
    public string? Filigrane { get; set; }

    public PdfSimple Titre(string texte)
    {
        foreach (var morceau in Couper(texte, true, 15, LargeurPage - 2 * Marge))
            _lignes.Add(new Ligne(morceau, true, 15, 0, 4, true, false));
        return this;
    }

    public PdfSimple Intertitre(string texte)
    {
        var premiere = true;
        foreach (var morceau in Couper(texte, true, 11, LargeurPage - 2 * Marge))
        {
            _lignes.Add(new Ligne(morceau, true, 11, 0, premiere ? 12 : 0, false, false));
            premiere = false;
        }
        _lignes.Add(new Ligne(string.Empty, false, 4, 0, 0, false, true));
        return this;
    }

    public PdfSimple Paragraphe(string? texte, bool gras = false, double taille = 10, double retrait = 0)
    {
        if (string.IsNullOrEmpty(texte))
        {
            _lignes.Add(new Ligne(string.Empty, false, taille, 0, 0, false, false));
            return this;
        }
        foreach (var brut in texte.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            foreach (var morceau in Couper(brut, gras, taille, LargeurPage - 2 * Marge - retrait))
                _lignes.Add(new Ligne(morceau, gras, taille, retrait, 0, false, false));
        return this;
    }

    public PdfSimple Espace(double hauteur = 8)
    {
        _lignes.Add(new Ligne(string.Empty, false, 0, 0, hauteur, false, false));
        return this;
    }

    /// <summary>
    /// Texte balisé produit par <see cref="ActeRendu"/> : « # Titre », « ## Intertitre », lignes ordinaires.
    /// </summary>
    public PdfSimple TexteBalise(string texte)
    {
        foreach (var ligne in texte.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (ligne.StartsWith("## ", StringComparison.Ordinal))
                Intertitre(ligne[3..]);
            else if (ligne.StartsWith("# ", StringComparison.Ordinal))
                Titre(ligne[2..]);
            else
                Paragraphe(ligne);
        }
        return this;
    }

    // ===== Mise en page =====

    private static IEnumerable<string> Couper(string texte, bool gras, double taille, double largeur)
    {
        if (texte.Length == 0)
        {
            yield return string.Empty;
            yield break;
        }
        var mots = texte.Split(' ');
        var courant = new StringBuilder();
        foreach (var mot in mots)
        {
            var essai = courant.Length == 0 ? mot : courant + " " + mot;
            if (Largeur(essai, gras, taille) <= largeur)
            {
                courant.Clear().Append(essai);
                continue;
            }
            if (courant.Length > 0)
            {
                yield return courant.ToString();
                courant.Clear();
            }
            // Mot plus long que la ligne : coupé de force.
            var reste = mot;
            while (Largeur(reste, gras, taille) > largeur && reste.Length > 1)
            {
                var n = reste.Length - 1;
                while (n > 1 && Largeur(reste[..n], gras, taille) > largeur)
                    n--;
                yield return reste[..n];
                reste = reste[n..];
            }
            courant.Append(reste);
        }
        if (courant.Length > 0)
            yield return courant.ToString();
    }

    public static double Largeur(string texte, bool gras, double taille)
    {
        double total = 0;
        foreach (var c in texte)
            total += LargeurCaractere(c, gras);
        return total * taille / 1000.0;
    }

    private static readonly int[] Normal =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584
    ];

    private static readonly int[] Gras =
    [
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
        333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
        611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584
    ];

    private static int LargeurCaractere(char c, bool gras)
    {
        var table = gras ? Gras : Normal;
        if (c >= 32 && c <= 126)
            return table[c - 32];
        // Moitié d'un caractère hors BMP (émoji…) : la paire est rendue par un seul « ? » (Chaine), la seconde moitié est vide.
        // Normalize lèverait une exception sur une moitié isolée.
        if (char.IsHighSurrogate(c))
            return table['?' - 32];
        if (char.IsLowSurrogate(c))
            return 0;
        // Lettres accentuées : largeur de la lettre de base.
        var decompose = c.ToString().Normalize(NormalizationForm.FormD);
        if (decompose.Length > 0 && decompose[0] >= 32 && decompose[0] <= 126)
            return table[decompose[0] - 32];
        return c switch
        {
            'œ' => 944,
            'Œ' => 1000,
            'æ' => 889,
            '—' => 1000,
            '–' => 556,
            '…' => 1000,
            '€' => 556,
            '«' or '»' => 556,
            '’' or '‘' => 222,
            '“' or '”' => 333,
            '°' => 400,
            _ => 556
        };
    }

    /// <summary>Caractère → octet WinAnsi (Windows-1252) ; « ? » si absent.</summary>
    private static byte WinAnsi(char c)
    {
        if (c < 128)
            return (byte)c;
        if (c >= 160 && c <= 255)
            return (byte)c;
        return c switch
        {
            '€' => 0x80,
            '‚' => 0x82,
            '„' => 0x84,
            '…' => 0x85,
            '‹' => 0x8B,
            'Œ' => 0x8C,
            '‘' => 0x91,
            '’' => 0x92,
            '“' => 0x93,
            '”' => 0x94,
            '•' => 0x95,
            '–' => 0x96,
            '—' => 0x97,
            '›' => 0x9B,
            'œ' => 0x9C,
            'Ÿ' => 0x9F,
            ' ' or ' ' => 0xA0,
            _ => (byte)'?'
        };
    }

    private static string Chaine(string texte)
    {
        var builder = new StringBuilder("(");
        foreach (var c in texte)
        {
            if (char.IsLowSurrogate(c))
                continue; // paire hors BMP : un seul « ? » (émis pour la première moitié)
            var b = WinAnsi(c);
            switch (b)
            {
                case (byte)'(':
                case (byte)')':
                case (byte)'\\':
                    builder.Append('\\').Append((char)b);
                    break;
                default:
                    if (b < 32 || b > 126)
                        builder.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
                    else
                        builder.Append((char)b);
                    break;
            }
        }
        return builder.Append(')').ToString();
    }

    private static string N(double valeur) => valeur.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Produit le fichier PDF.</summary>
    public byte[] Generer()
    {
        // 1. Répartition des lignes sur les pages.
        var pages = new List<List<(Ligne Ligne, double Y)>>();
        var page = new List<(Ligne, double)>();
        var y = HautContenu;
        foreach (var ligne in _lignes)
        {
            var hauteur = ligne.EspaceAvant + (ligne.Taille <= 0 ? 0 : ligne.Taille * 1.35);
            if (y - hauteur < BasContenu && page.Count > 0)
            {
                pages.Add(page);
                page = [];
                y = HautContenu;
            }
            y -= hauteur;
            page.Add((ligne, y));
        }
        pages.Add(page);

        // 2. Contenu de chaque page.
        var flux = new List<string>();
        for (var i = 0; i < pages.Count; i++)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(Filigrane))
            {
                // Texte gris clair en diagonale (rotation de 45°).
                sb.Append("q 0.88 g BT /F2 90 Tf 0.7071 0.7071 -0.7071 0.7071 150 250 Tm ")
                  .Append(Chaine(Filigrane)).Append(" Tj ET Q\n");
            }
            // En-tête et filet.
            sb.Append("0.25 g BT /F1 8 Tf ").Append(N(Marge)).Append(' ').Append(N(HauteurPage - 40)).Append(" Td ")
              .Append(Chaine(EnTete)).Append(" Tj ET\n");
            sb.Append("0.6 G 0.5 w ").Append(N(Marge)).Append(' ').Append(N(HauteurPage - 46)).Append(" m ")
              .Append(N(LargeurPage - Marge)).Append(' ').Append(N(HauteurPage - 46)).Append(" l S\n");

            foreach (var (ligne, ly) in pages[i])
            {
                if (ligne.Filet)
                {
                    sb.Append("0.75 G 0.4 w ").Append(N(Marge)).Append(' ').Append(N(ly + 2)).Append(" m ")
                      .Append(N(LargeurPage - Marge)).Append(' ').Append(N(ly + 2)).Append(" l S\n");
                    continue;
                }
                if (ligne.Texte.Length == 0 || ligne.Taille <= 0)
                    continue;
                var x = ligne.Centre
                    ? (LargeurPage - Largeur(ligne.Texte, ligne.Gras, ligne.Taille)) / 2
                    : Marge + ligne.Retrait;
                sb.Append("0 g BT /").Append(ligne.Gras ? "F2" : "F1").Append(' ').Append(N(ligne.Taille)).Append(" Tf ")
                  .Append(N(x)).Append(' ').Append(N(ly)).Append(" Td ").Append(Chaine(ligne.Texte)).Append(" Tj ET\n");
            }

            // Pied de page.
            var pied = $"{PiedDePage}    —    Page {i + 1} / {pages.Count}";
            sb.Append("0.6 G 0.5 w ").Append(N(Marge)).Append(" 44 m ").Append(N(LargeurPage - Marge)).Append(" 44 l S\n");
            sb.Append("0.35 g BT /F1 7.5 Tf ").Append(N(Marge)).Append(" 32 Td ").Append(Chaine(pied)).Append(" Tj ET\n");
            flux.Add(sb.ToString());
        }

        // 3. Objets PDF : 1 catalogue, 2 pages, 3 police normale, 4 police grasse, puis (page, contenu) par page.
        var objets = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            string.Empty,
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"
        };
        var kids = new List<string>();
        foreach (var contenu in flux)
        {
            var pageNum = objets.Count + 1;
            var contenuNum = pageNum + 1;
            kids.Add($"{pageNum} 0 R");
            objets.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(LargeurPage)} {N(HauteurPage)}] " +
                       $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contenuNum} 0 R >>");
            objets.Add($"<< /Length {contenu.Length} >>\nstream\n{contenu}endstream");
        }
        objets[1] = $"<< /Type /Pages /Kids [{string.Join(' ', kids)}] /Count {kids.Count} >>";

        // 4. Écriture avec la table des références croisées (contenu ASCII uniquement).
        var sortie = new StringBuilder("%PDF-1.4\n%âãÏÓ\n");
        var positions = new List<int>();
        var octets = Encoding.Latin1;
        for (var i = 0; i < objets.Count; i++)
        {
            positions.Add(octets.GetByteCount(sortie.ToString()));
            sortie.Append(i + 1).Append(" 0 obj\n").Append(objets[i]).Append("\nendobj\n");
        }
        var xref = octets.GetByteCount(sortie.ToString());
        sortie.Append("xref\n0 ").Append(objets.Count + 1).Append("\n0000000000 65535 f \n");
        foreach (var position in positions)
            sortie.Append(position.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        sortie.Append("trailer\n<< /Size ").Append(objets.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return octets.GetBytes(sortie.ToString());
    }
}
