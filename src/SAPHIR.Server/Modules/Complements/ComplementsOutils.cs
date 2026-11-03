using System.Globalization;
using System.Security.Cryptography;
using Saphir.Server.Data;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Complements;

namespace Saphir.Server.Modules.Complements;

/// <summary>Agent lu dans rh_agents (colonnes du socle uniquement).</summary>
internal sealed record AgentCourt(long Id, string Reference, string DiscordId, string Nom, string Prenom, string? Rio, string Statut)
{
    public string Libelle => $"{Nom} {Prenom} ({Reference})";
}

/// <summary>Objets suivis dans l'historique (sys_historique) par les applications complémentaires.</summary>
internal static class ObjetsComplements
{
    public const string Message = "message";
    public const string Note = "note";
    public const string Mrz = "mrz";
    public const string Pve = "pve";
    public const string Infraction = "pve-infraction";
    public const string Bodycam = "bodycam";
    public const string Salon = "tchap-salon";
    public const string ServiceAnnuaire = "annuaire-service";
    public const string Coordonnees = "annuaire-coordonnees";
}

/// <summary>Outils communs du module : agents, listes IN paramétrées, fichiers joints, références.</summary>
public static class ComplementsOutils
{
    public static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>« PVE-2026-000012 ».</summary>
    public static string Reference(string prefixe, DateTime date, long id)
        => $"{prefixe}-{date.Year.ToString(CultureInfo.InvariantCulture)}-{id.ToString("D6", CultureInfo.InvariantCulture)}";

    /// <summary>« MSG-000012 ».</summary>
    public static string Reference(string prefixe, long id)
        => $"{prefixe}-{id.ToString("D6", CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Liste « @p0, @p1… » pour une clause IN (noms de paramètres générés, valeurs toujours passées en paramètre).
    /// </summary>
    public static (string Sql, (string Name, object? Value)[] Parametres) ListeIn(string prefixe, IReadOnlyList<long> ids)
    {
        if (ids.Count == 0)
            return ("NULL", Array.Empty<(string, object?)>());
        var noms = new string[ids.Count];
        var parametres = new (string, object?)[ids.Count];
        for (var i = 0; i < ids.Count; i++)
        {
            noms[i] = "@" + prefixe + i.ToString(CultureInfo.InvariantCulture);
            parametres[i] = (noms[i], ids[i]);
        }
        return (string.Join(", ", noms), parametres);
    }

    /// <summary>Agents existants parmi <paramref name="ids"/> (dans l'ordre de la base).</summary>
    internal static async Task<List<AgentCourt>> AgentsAsync(SqlSession db, IEnumerable<long> ids, CancellationToken ct)
    {
        var liste = ids.Distinct().Take(500).ToList();
        if (liste.Count == 0)
            return [];
        var (sql, parametres) = ListeIn("ag", liste);
        return await db.QueryAsync(
            $"SELECT id, reference, discord_id, nom, prenom, rio, statut FROM rh_agents WHERE id IN ({sql})",
            LireAgent, ct, parametres);
    }

    internal static async Task<AgentCourt?> AgentAsync(SqlSession db, long id, CancellationToken ct)
        => (await AgentsAsync(db, [id], ct)).FirstOrDefault();

    internal static AgentCourt LireAgent(SqlRow r) => new(
        r.Long("id"), r.Text("reference"), r.Text("discord_id"), r.Text("nom"), r.Text("prenom"), r.NullableText("rio"), r.Text("statut"));

    /// <summary>Identifiants Discord des agents (notifications temps réel).</summary>
    public static async Task<List<string>> DiscordIdsAsync(SqlSession db, IEnumerable<long> agentIds, CancellationToken ct)
        => (await AgentsAsync(db, agentIds, ct)).Select(a => a.DiscordId).Where(d => d.Length > 0).Distinct().ToList();

    /// <summary>Texte obligatoire nettoyé, sinon erreur de saisie.</summary>
    public static string Obligatoire(string? texte, int maximum, string libelle)
        => SocleRegles.Tronquer(texte, maximum) ?? throw ApiException.Saisie($"{libelle} : champ obligatoire.");

    public static string? Facultatif(string? texte, int maximum) => SocleRegles.Tronquer(texte, maximum);

    /// <summary>Texte long (corps de message, note) : espaces de fin retirés, longueur bornée.</summary>
    public static string Long(string? texte, int maximum, string libelle, bool obligatoire)
    {
        var t = (texte ?? string.Empty).TrimEnd();
        if (obligatoire && t.Trim().Length == 0)
            throw ApiException.Saisie($"{libelle} : champ obligatoire.");
        if (t.Length > maximum)
            throw ApiException.Saisie($"{libelle} : {maximum} caractères au plus.");
        return t;
    }

    /// <summary>Fichier joint décodé et contrôlé (taille, extension, signature).</summary>
    public sealed record FichierRecu(string Nom, string TypeMime, byte[] Contenu, string Sha256);

    public static FichierRecu DecoderPiece(PieceJointeInput piece)
    {
        var nom = NomFichier(piece.Nom);
        if (!PiecesJointes.ExtensionAutorisee(nom))
            throw ApiException.Saisie($"Type de fichier non autorisé : « {nom} ». Autorisés : {string.Join(", ", PiecesJointes.Extensions)}.");
        byte[] contenu;
        try
        {
            contenu = Convert.FromBase64String(piece.Donnees ?? string.Empty);
        }
        catch (FormatException)
        {
            throw ApiException.Saisie($"Fichier « {nom} » illisible.");
        }
        if (contenu.Length == 0)
            throw ApiException.Saisie($"Le fichier « {nom} » est vide.");
        if (contenu.Length > PiecesJointes.TailleMax)
            throw ApiException.Saisie($"Le fichier « {nom} » dépasse {PiecesJointes.TailleLisible(PiecesJointes.TailleMax)}.");
        if (!SignatureCoherente(PiecesJointes.Extension(nom), contenu))
            throw ApiException.Saisie($"Le contenu du fichier « {nom} » ne correspond pas à son extension.");
        return new FichierRecu(nom, PiecesJointes.TypeMime(nom), contenu, Sha256(contenu));
    }

    public static string Sha256(byte[] contenu) => Convert.ToHexString(SHA256.HashData(contenu)).ToLowerInvariant();

    /// <summary>Nom de fichier sans chemin ni caractère de contrôle, 200 caractères au plus.</summary>
    public static string NomFichier(string? nom)
    {
        var n = Path.GetFileName((nom ?? string.Empty).Replace('\\', '/'));
        n = new string(n.Where(c => !char.IsControl(c) && c is not ('/' or ':' or '*' or '?' or '"' or '<' or '>' or '|')).ToArray()).Trim();
        if (n.Length == 0 || n.Trim('.').Length == 0)
            throw ApiException.Saisie("Nom de fichier invalide.");
        if (n.Length > 200)
        {
            var extension = Path.GetExtension(n);
            n = n[..(200 - extension.Length)] + extension;
        }
        return n;
    }

    /// <summary>Contrôle des premiers octets (signature) pour les formats binaires courants.</summary>
    public static bool SignatureCoherente(string extension, ReadOnlySpan<byte> debut)
    {
        static bool Commence(ReadOnlySpan<byte> d, params byte[] s) => d.Length >= s.Length && d[..s.Length].SequenceEqual(s);
        return extension switch
        {
            "pdf" => Commence(debut, 0x25, 0x50, 0x44, 0x46),
            "png" => Commence(debut, 0x89, 0x50, 0x4E, 0x47),
            "jpg" or "jpeg" => Commence(debut, 0xFF, 0xD8, 0xFF),
            "gif" => Commence(debut, 0x47, 0x49, 0x46, 0x38),
            "webp" => Commence(debut, 0x52, 0x49, 0x46, 0x46) && debut.Length >= 12 && debut[8..12].SequenceEqual("WEBP"u8),
            "docx" or "xlsx" or "odt" or "ods" => Commence(debut, 0x50, 0x4B, 0x03, 0x04),
            "wav" => Commence(debut, 0x52, 0x49, 0x46, 0x46),
            "mp3" => Commence(debut, 0x49, 0x44, 0x33) || (debut.Length >= 2 && debut[0] == 0xFF && (debut[1] & 0xE0) == 0xE0),
            "mp4" or "m4v" or "mov" => debut.Length >= 8 && debut[4..8].SequenceEqual("ftyp"u8)
                                       || debut.Length >= 8 && (debut[4..8].SequenceEqual("moov"u8) || debut[4..8].SequenceEqual("mdat"u8) || debut[4..8].SequenceEqual("wide"u8)),
            "webm" or "mkv" => Commence(debut, 0x1A, 0x45, 0xDF, 0xA3),
            "avi" => Commence(debut, 0x52, 0x49, 0x46, 0x46) && debut.Length >= 12 && debut[8..12].SequenceEqual("AVI "u8),
            // Texte : pas d'octet nul dans le début du fichier.
            "txt" or "csv" => !debut[..Math.Min(debut.Length, 4096)].Contains((byte)0),
            _ => false
        };
    }

    public static PieceJointeDto LirePiece(SqlRow r)
        => new(r.Long("id"), r.Text("nom"), r.Text("type_mime"), r.Long("taille"));

    public static PieceJointeContenuDto LirePieceContenu(SqlRow r)
    {
        var contenu = r.Value("contenu") as byte[] ?? [];
        return new PieceJointeContenuDto(r.Long("id"), r.Text("nom"), r.Text("type_mime"), r.Long("taille"), Convert.ToBase64String(contenu));
    }

    /// <summary>Date facultative (DATE) → DateOnly.</summary>
    public static DateOnly? DateSeule(SqlRow r, string colonne) => r.Value(colonne) switch
    {
        DateTime d => DateOnly.FromDateTime(d),
        DateOnly d => d,
        _ => null
    };

    /// <summary>DateOnly → DateTime pour un paramètre DATE.</summary>
    public static object? Parametre(DateOnly? date) => date?.ToDateTime(TimeOnly.MinValue);

    /// <summary>Contrôle de version (modification concurrente).</summary>
    public static void VerifierVersion(int lue, int actuelle)
    {
        if (lue != actuelle)
            throw ApiException.Conflit("Cet élément a été modifié entre-temps par un autre poste. Recharge-le avant d'enregistrer.");
    }
}
