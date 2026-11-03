using System.Globalization;
using System.Text;

namespace Saphir.Server.Modules.Rh;

/// <summary>
/// Générateur PDF minimal (texte seul, A4, Helvetica, encodage WinAnsi) pour les documents RH générés depuis un modèle.
/// Aucune dépendance externe : le fichier produit s'ouvre dans tout lecteur PDF.
/// </summary>
public static class RhPdf
{
    private const int LargeurPage = 595;
    private const int HauteurPage = 842;
    private const int Marge = 56;
    private const double TailleTexte = 10.5;
    private const double Interligne = 14;
    private const int CaracteresParLigne = 92;

    /// <summary>Produit un PDF : en-tête (organisation), titre, texte (sauts de ligne conservés), pied de page avec la référence.</summary>
    public static byte[] Generer(string organisation, string titre, string texte, string reference)
    {
        var lignes = Couper(texte ?? string.Empty);
        var hauteurUtile = HauteurPage - 2 * Marge - 70;
        var lignesParPage = Math.Max(1, (int)(hauteurUtile / Interligne));
        var pages = new List<List<string>>();
        for (var i = 0; i < lignes.Count; i += lignesParPage)
            pages.Add(lignes.Skip(i).Take(lignesParPage).ToList());
        if (pages.Count == 0)
            pages.Add([]);

        var objets = new List<byte[]>();
        // 1 catalogue, 2 pages, 3 police normale, 4 police grasse, puis (page, contenu) par page.
        var kids = string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => $"{5 + 2 * i} 0 R"));
        objets.Add(Ascii("<< /Type /Catalog /Pages 2 0 R >>"));
        objets.Add(Ascii($"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>"));
        objets.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
        objets.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"));

        for (var p = 0; p < pages.Count; p++)
        {
            var contenu = Contenu(organisation, titre, reference, pages[p], p + 1, pages.Count);
            objets.Add(Ascii(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {LargeurPage} {HauteurPage}] " +
                $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {6 + 2 * p} 0 R >>"));
            using var flux = new MemoryStream();
            flux.Write(Ascii($"<< /Length {contenu.Length} >>\nstream\n"));
            flux.Write(contenu);
            flux.Write(Ascii("\nendstream"));
            objets.Add(flux.ToArray());
        }

        using var sortie = new MemoryStream();
        sortie.Write(Ascii("%PDF-1.4\n"));
        sortie.Write([0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A]);
        var positions = new List<long>();
        for (var i = 0; i < objets.Count; i++)
        {
            positions.Add(sortie.Position);
            sortie.Write(Ascii($"{i + 1} 0 obj\n"));
            sortie.Write(objets[i]);
            sortie.Write(Ascii("\nendobj\n"));
        }

        var xref = sortie.Position;
        var table = new StringBuilder();
        table.Append(CultureInfo.InvariantCulture, $"xref\n0 {objets.Count + 1}\n");
        table.Append("0000000000 65535 f \n");
        foreach (var position in positions)
            table.Append(position.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        table.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        sortie.Write(Ascii(table.ToString()));
        return sortie.ToArray();
    }

    private static byte[] Contenu(string organisation, string titre, string reference, List<string> lignes, int page, int pages)
    {
        using var flux = new MemoryStream();
        void Texte(string police, double taille, double x, double y, string texte)
        {
            flux.Write(Ascii(string.Create(CultureInfo.InvariantCulture, $"BT /{police} {taille:0.##} Tf {x:0.##} {y:0.##} Td (")));
            flux.Write(Encoder(texte));
            flux.Write(Ascii(") Tj ET\n"));
        }

        var haut = HauteurPage - Marge;
        Texte("F2", 9, Marge, haut, organisation.ToUpperInvariant());
        Texte("F1", 8, Marge, haut - 12, "Document de simulation — Val-de-Marne Simulation (données fictives)");
        flux.Write(Ascii(string.Create(CultureInfo.InvariantCulture,
            $"0.6 w {Marge} {haut - 20} m {LargeurPage - Marge} {haut - 20} l S\n")));
        if (page == 1)
            Texte("F2", 14, Marge, haut - 48, Tronquer(titre, 70));

        double y = haut - (page == 1 ? 74 : 44);
        foreach (var ligne in lignes)
        {
            if (ligne.Length > 0)
                Texte("F1", TailleTexte, Marge, y, ligne);
            y -= Interligne;
        }

        flux.Write(Ascii(string.Create(CultureInfo.InvariantCulture,
            $"0.4 w {Marge} {Marge - 6} m {LargeurPage - Marge} {Marge - 6} l S\n")));
        Texte("F1", 8, Marge, Marge - 18, $"{reference} — page {page}/{pages}");
        return flux.ToArray();
    }

    /// <summary>Découpe le texte en lignes d'au plus <see cref="CaracteresParLigne"/> caractères (coupure aux espaces).</summary>
    public static List<string> Couper(string texte)
    {
        var resultat = new List<string>();
        foreach (var brute in texte.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var ligne = brute.Replace('\t', ' ').TrimEnd();
            while (ligne.Length > CaracteresParLigne)
            {
                var coupure = ligne.LastIndexOf(' ', CaracteresParLigne);
                if (coupure <= 0)
                    coupure = CaracteresParLigne;
                resultat.Add(ligne[..coupure].TrimEnd());
                ligne = ligne[coupure..].TrimStart();
            }
            resultat.Add(ligne);
        }
        return resultat;
    }

    private static string Tronquer(string texte, int max) => texte.Length <= max ? texte : texte[..(max - 1)] + "…";

    private static byte[] Ascii(string texte) => Encoding.ASCII.GetBytes(texte);

    /// <summary>Chaîne PDF en WinAnsi : parenthèses et barres obliques inverses échappées, caractères hors table → « ? ».</summary>
    private static byte[] Encoder(string texte)
    {
        var octets = new List<byte>(texte.Length + 8);
        foreach (var c in texte)
        {
            int code = c switch
            {
                '\u20AC' => 0x80, // €
                '\u201A' => 0x82,
                '\u201E' => 0x84,
                '\u2026' => 0x85, // …
                '\u2039' => 0x8B,
                '\u0152' => 0x8C, // Œ
                '\u2018' => 0x91,
                '\u2019' => 0x92, // ’
                '\u201C' => 0x93,
                '\u201D' => 0x94,
                '\u2022' => 0x95, // •
                '\u2013' => 0x96, // –
                '\u2014' => 0x97, // —
                '\u203A' => 0x9B,
                '\u0153' => 0x9C, // œ
                '\u0178' => 0x9F, // Ÿ
                '\u202F' or '\u2009' => 0x20,
                _ when c is >= ' ' and <= '~' => c,
                _ when c is >= '\u00A0' and <= '\u00FF' => c,
                _ => '?'
            };
            var b = (byte)code;
            if (b is (byte)'(' or (byte)')' or (byte)'\\')
                octets.Add((byte)'\\');
            octets.Add(b);
        }
        return octets.ToArray();
    }
}
