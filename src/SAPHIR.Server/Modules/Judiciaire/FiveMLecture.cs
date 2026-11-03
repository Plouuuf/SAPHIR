using System.Text.Json;
using Microsoft.Extensions.Options;
using Saphir.Server.Data;

namespace Saphir.Server.Modules.Judiciaire;

/// <summary>
/// Section « FiveM » de la configuration : base du serveur FiveM lue en lecture seule (utilisateur MariaDB avec SELECT seulement).
/// Framework : « esx », « qbcore » ou vide (pas de lecture).
/// </summary>
public sealed class FiveMOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string Framework { get; set; } = string.Empty;
}

public sealed record FiveMPersonne(string Id, string Nom, string Prenom, DateOnly? DateNaissance, string? Sexe, string? Telephone, string? Nationalite);

public sealed record FiveMVehicule(string Plaque, string? Modele, string? ProprietaireId);

/// <summary>
/// Lecture seule des identités et véhicules des personnages FiveM (ESX : users, owned_vehicles ; QBCore : players, player_vehicles).
/// Les personnes trouvées sont importées dans SAPHIR avec un identifiant interne stable (règle 11).
/// Une base FiveM absente ou injoignable n'empêche jamais SAPHIR de fonctionner.
/// </summary>
public sealed class FiveMLecture(IOptionsMonitor<FiveMOptions> options, ILogger<FiveMLecture> logger)
{
    private const int Max = 25;
    private static readonly TimeSpan DureeSchema = TimeSpan.FromMinutes(10);

    private readonly SemaphoreSlim _verrou = new(1, 1);
    private HashSet<string> _colonnes = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<uint, string> _modeles = [];
    private DateTimeOffset _schemaLu = DateTimeOffset.MinValue;

    private string Framework => (options.CurrentValue.Framework ?? string.Empty).Trim().ToLowerInvariant();
    private bool Qb => Framework == "qbcore";

    public bool Active => !string.IsNullOrWhiteSpace(options.CurrentValue.ConnectionString) && Framework is "esx" or "qbcore";

    public Task<List<FiveMPersonne>> ChercherPersonnesAsync(IReadOnlyList<string> termes, CancellationToken ct)
        => SansErreurAsync<FiveMPersonne>("recherche de personnes", async db =>
        {
            var lettres = termes.Where(t => t.Length >= 2 && t.All(c => char.IsLetter(c) || c is '-' or '\'')).Take(3).ToList();
            if (lettres.Count == 0)
                return [];
            var parametres = new List<(string Name, object? Value)>();
            var clauses = new List<string>();
            for (var i = 0; i < lettres.Count; i++)
            {
                var nom = "@t" + i;
                parametres.Add((nom, Sqlj.Like(lettres[i])));
                clauses.Add(Qb
                    ? $"(JSON_UNQUOTE(JSON_EXTRACT(charinfo, '$.firstname')) LIKE {nom} OR JSON_UNQUOTE(JSON_EXTRACT(charinfo, '$.lastname')) LIKE {nom})"
                    : $"(firstname LIKE {nom} OR lastname LIKE {nom})");
            }
            var where = string.Join(" AND ", clauses);
            var lignes = Qb
                ? await db.QueryAsync($"SELECT citizenid, charinfo FROM players WHERE {where} LIMIT {Max}", LireQb, ct, parametres.ToArray())
                : await db.QueryAsync($"SELECT {ColonnesEsx()} FROM users WHERE {where} LIMIT {Max}", LireEsx, ct, parametres.ToArray());
            return lignes.Where(p => p is not null).Select(p => p!).ToList();
        }, ct);

    public async Task<FiveMPersonne?> LirePersonneAsync(string id, CancellationToken ct)
        => (await SansErreurAsync<FiveMPersonne>("lecture d'une personne", async db =>
        {
            var lignes = Qb
                ? await db.QueryAsync("SELECT citizenid, charinfo FROM players WHERE citizenid = @id LIMIT 1", LireQb, ct, ("@id", id))
                : await db.QueryAsync($"SELECT {ColonnesEsx()} FROM users WHERE identifier = @id LIMIT 1", LireEsx, ct, ("@id", id));
            return lignes.Where(p => p is not null).Select(p => p!).ToList();
        }, ct)).FirstOrDefault();

    public Task<List<FiveMVehicule>> ChercherVehiculesAsync(string plaqueNormalisee, CancellationToken ct)
        => SansErreurAsync<FiveMVehicule>("recherche de véhicules", async db =>
        {
            if (plaqueNormalisee.Length < 2)
                return [];
            var plaque = "REPLACE(REPLACE(UPPER(v.plate), ' ', ''), '-', '')";
            return Qb
                ? await db.QueryAsync($"SELECT v.plate, v.vehicle, v.citizenid AS owner FROM player_vehicles v WHERE {plaque} LIKE @p LIMIT {Max}",
                    r => new FiveMVehicule(r.Text("plate").Trim(), Joli(r.NullableText("vehicle")), r.NullableText("owner")), ct,
                    ("@p", Sqlj.Like(plaqueNormalisee)))
                : await db.QueryAsync($"SELECT v.plate, v.vehicle, v.owner FROM owned_vehicles v WHERE {plaque} LIKE @p LIMIT {Max}",
                    r => new FiveMVehicule(r.Text("plate").Trim(), ModeleEsx(r.NullableText("vehicle")), r.NullableText("owner")), ct,
                    ("@p", Sqlj.Like(plaqueNormalisee)));
        }, ct);

    // ===== Lecture des lignes =====

    private string ColonnesEsx()
    {
        var colonnes = new List<string> { "identifier", "firstname", "lastname" };
        foreach (var facultative in new[] { "dateofbirth", "sex", "phone_number" })
            if (_colonnes.Contains("users." + facultative))
                colonnes.Add(facultative);
        return string.Join(", ", colonnes);
    }

    private static FiveMPersonne? LireEsx(SqlRow r)
    {
        var nom = r.NullableText("lastname");
        var prenom = r.NullableText("firstname");
        if (string.IsNullOrWhiteSpace(nom) && string.IsNullOrWhiteSpace(prenom))
            return null; // compte sans personnage
        var sexe = r.NullableText("sex")?.Trim().ToLowerInvariant() switch
        {
            "m" => "M",
            "f" => "F",
            var autre => autre?.ToUpperInvariant()
        };
        return new FiveMPersonne(r.Text("identifier"), (nom ?? string.Empty).Trim().ToUpperInvariant(), (prenom ?? string.Empty).Trim(),
            JudiciaireRegles.LireDate(r.NullableText("dateofbirth")), sexe, r.NullableText("phone_number"), null);
    }

    private static FiveMPersonne? LireQb(SqlRow r)
    {
        var json = r.NullableText("charinfo");
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var racine = document.RootElement;
            string? Lire(string nom) => racine.TryGetProperty(nom, out var v)
                ? v.ValueKind switch
                {
                    JsonValueKind.String => v.GetString(),
                    JsonValueKind.Number => v.GetRawText(),
                    _ => null
                }
                : null;
            var sexe = Lire("gender") switch { "0" => "M", "1" => "F", var autre => autre };
            return new FiveMPersonne(r.Text("citizenid"), (Lire("lastname") ?? string.Empty).Trim().ToUpperInvariant(),
                (Lire("firstname") ?? string.Empty).Trim(), JudiciaireRegles.LireDate(Lire("birthdate")), sexe, Lire("phone"), Lire("nationality"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string? ModeleEsx(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("model", out var modele))
                return null;
            if (modele.ValueKind == JsonValueKind.String)
                return Joli(modele.GetString());
            if (modele.ValueKind == JsonValueKind.Number && modele.TryGetInt64(out var hachage))
                return _modeles.TryGetValue(unchecked((uint)hachage), out var nom) ? nom : null;
        }
        catch (JsonException)
        {
            // Données véhicule illisibles : modèle inconnu.
        }
        return null;
    }

    private static string? Joli(string? modele)
        => string.IsNullOrWhiteSpace(modele) ? null : char.ToUpperInvariant(modele[0]) + modele[1..];

    /// <summary>Hachage « joaat » utilisé par GTA V pour les noms de modèles.</summary>
    private static uint Joaat(string texte)
    {
        unchecked
        {
            uint h = 0;
            foreach (var c in texte.ToLowerInvariant())
            {
                h += c;
                h += h << 10;
                h ^= h >> 6;
            }
            h += h << 3;
            h ^= h >> 11;
            h += h << 15;
            return h;
        }
    }

    // ===== Connexion et schéma =====

    private async Task<List<T>> SansErreurAsync<T>(string quoi, Func<SqlSession, Task<List<T>>> travail, CancellationToken ct)
    {
        if (!Active)
            return [];
        try
        {
            await using var db = await SqlSession.OpenAsync(options.CurrentValue.ConnectionString, ct);
            await LireSchemaAsync(db, ct);
            return await travail(db);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Base FiveM : échec de la {Quoi}", quoi);
            return [];
        }
    }

    private async Task LireSchemaAsync(SqlSession db, CancellationToken ct)
    {
        if (DateTimeOffset.UtcNow - _schemaLu < DureeSchema)
            return;
        await _verrou.WaitAsync(ct);
        try
        {
            if (DateTimeOffset.UtcNow - _schemaLu < DureeSchema)
                return;
            var colonnes = await db.QueryAsync(
                "SELECT TABLE_NAME, COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE()",
                r => r.Text("TABLE_NAME") + "." + r.Text("COLUMN_NAME"), ct);
            var ensemble = new HashSet<string>(colonnes, StringComparer.OrdinalIgnoreCase);
            var modeles = new Dictionary<uint, string>();
            if (!Qb && ensemble.Contains("vehicles.model") && ensemble.Contains("vehicles.name"))
            {
                var lignes = await db.QueryAsync("SELECT model, name FROM vehicles",
                    r => (Modele: r.NullableText("model"), Nom: r.NullableText("name")), ct);
                foreach (var (modele, nom) in lignes)
                    if (!string.IsNullOrWhiteSpace(modele))
                        modeles[Joaat(modele)] = nom ?? Joli(modele)!;
            }
            _colonnes = ensemble;
            _modeles = modeles;
            _schemaLu = DateTimeOffset.UtcNow;
        }
        finally
        {
            _verrou.Release();
        }
    }
}
