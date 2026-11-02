using System.Text.Json;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Saphir.Server.Services;

namespace Saphir.Server.Data;

public sealed record FiveMPerson(
    string Id,
    string Nom,
    string Prenom,
    string? DateNaissance,
    string? Sexe,
    string? Taille,
    string? Telephone,
    string? Nationalite);

public sealed record FiveMVehicle(string Plaque, string? Modele, string? OwnerId, string? OwnerName);

/// <summary>
/// Lecture seule de la base du serveur FiveM : identités (personnages), véhicules et permis.
/// Compatible ESX (users, owned_vehicles, user_licenses, vehicles) et QBCore (players, player_vehicles).
/// Les colonnes facultatives sont détectées au démarrage ; une base FiveM injoignable n'empêche pas
/// SAPHIR de fonctionner (seules les fiches SAPHIR s'affichent alors).
/// </summary>
public sealed class FiveMReader(IOptionsMonitor<DatabaseOptions> options, ILogger<FiveMReader> logger)
{
    private const int MaxResults = 30;
    private static readonly TimeSpan SchemaLifetime = TimeSpan.FromMinutes(10);

    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private HashSet<string> _columns = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<uint, string> _modelNames = [];
    private DateTimeOffset _schemaLoadedAt = DateTimeOffset.MinValue;

    private string ConnectionString => options.CurrentValue.FiveM;
    private string Framework => (options.CurrentValue.Framework ?? string.Empty).Trim().ToLowerInvariant();

    public bool IsEnabled => !string.IsNullOrWhiteSpace(ConnectionString) && (Framework is "esx" or "qbcore");
    private bool IsQb => Framework == "qbcore";

    // ===== Personnes =====

    public Task<List<FiveMPerson>> SearchPeopleAsync(IReadOnlyList<string> terms, CancellationToken ct)
        => SafeAsync<FiveMPerson>("recherche de personnes", async connection =>
        {
            if (terms.Count == 0)
                return [];

            var parameters = new List<(string, object?)>();
            var clauses = new List<string>();
            for (var i = 0; i < terms.Count; i++)
            {
                var name = "@t" + i;
                parameters.Add((name, Sql.Like(terms[i])));
                clauses.Add(IsQb
                    ? $"(JSON_UNQUOTE(JSON_EXTRACT(charinfo, '$.firstname')) LIKE {name} OR JSON_UNQUOTE(JSON_EXTRACT(charinfo, '$.lastname')) LIKE {name})"
                    : $"(firstname LIKE {name} OR lastname LIKE {name})");
            }

            var where = string.Join(" AND ", clauses);
            var people = IsQb
                ? await Sql.QueryAsync(connection, $"SELECT citizenid, charinfo FROM players WHERE {where} LIMIT {MaxResults}",
                    ReadQbPerson, ct, parameters.ToArray())
                : await Sql.QueryAsync(connection, $"SELECT {EsxPersonColumns()} FROM users WHERE {where} ORDER BY lastname, firstname LIMIT {MaxResults}",
                    ReadEsxPerson, ct, parameters.ToArray());
            return people.Where(p => p is not null).Select(p => p!).ToList();
        }, ct);

    public async Task<FiveMPerson?> GetPersonAsync(string id, CancellationToken ct)
    {
        var rows = await SafeAsync<FiveMPerson>("lecture d'une personne", async connection =>
        {
            var people = IsQb
                ? await Sql.QueryAsync(connection, "SELECT citizenid, charinfo FROM players WHERE citizenid = @id LIMIT 1",
                    ReadQbPerson, ct, ("@id", id))
                : await Sql.QueryAsync(connection, $"SELECT {EsxPersonColumns()} FROM users WHERE identifier = @id LIMIT 1",
                    ReadEsxPerson, ct, ("@id", id));
            return people.Where(p => p is not null).Select(p => p!).ToList();
        }, ct);
        return rows.FirstOrDefault();
    }

    /// <summary>Catégories de permis connues du serveur FiveM (ESX : user_licenses ; QBCore : metadata.licences).</summary>
    public async Task<string?> GetLicenceCategoriesAsync(string id, CancellationToken ct)
    {
        var rows = await SafeAsync<string>("lecture des permis", async connection =>
        {
            if (IsQb)
            {
                if (!_columns.Contains("players.metadata"))
                    return [];
                var json = await Sql.ScalarAsync(connection, "SELECT metadata FROM players WHERE citizenid = @id LIMIT 1", ct, ("@id", id));
                return QbLicences(json as string);
            }

            if (!_columns.Contains("user_licenses.type"))
                return [];
            var types = await Sql.QueryAsync(connection, "SELECT type FROM user_licenses WHERE owner = @id", r => r.Text("type"), ct, ("@id", id));
            return types.Select(EsxLicence).Where(c => c is not null).Select(c => c!).Distinct().ToList();
        }, ct);
        return rows.Count == 0 ? null : string.Join(", ", rows.OrderBy(c => c, StringComparer.Ordinal));
    }

    // ===== Véhicules =====

    public Task<List<FiveMVehicle>> SearchVehiclesAsync(string plateNorm, CancellationToken ct)
        => QueryVehiclesAsync($"{PlateExpression("v.plate")} LIKE @p", ct, ("@p", Sql.Like(plateNorm)));

    public async Task<FiveMVehicle?> GetVehicleAsync(string plateNorm, CancellationToken ct)
        => (await QueryVehiclesAsync($"{PlateExpression("v.plate")} = @p", ct, ("@p", plateNorm))).FirstOrDefault();

    public Task<List<FiveMVehicle>> VehiclesOfAsync(string ownerId, CancellationToken ct)
        => QueryVehiclesAsync(IsQb ? "v.citizenid = @o" : "v.owner = @o", ct, ("@o", ownerId));

    private Task<List<FiveMVehicle>> QueryVehiclesAsync(string where, CancellationToken ct, params (string, object?)[] parameters)
        => SafeAsync<FiveMVehicle>("lecture des véhicules", async connection =>
        {
            if (IsQb)
            {
                return await Sql.QueryAsync(connection,
                    $"SELECT v.plate, v.vehicle, v.citizenid AS owner, p.charinfo FROM player_vehicles v " +
                    $"LEFT JOIN players p ON p.citizenid = v.citizenid WHERE {where} LIMIT {MaxResults}",
                    r =>
                    {
                        var owner = ParseCharinfo(r.Str("charinfo"));
                        return new FiveMVehicle(r.Text("plate"), Prettify(r.Str("vehicle")), r.Str("owner"),
                            owner is null ? null : $"{owner.Value.Nom} {owner.Value.Prenom}");
                    }, ct, parameters);
            }

            return await Sql.QueryAsync(connection,
                $"SELECT v.plate, v.vehicle, v.owner, u.firstname, u.lastname FROM owned_vehicles v " +
                $"LEFT JOIN users u ON u.identifier = v.owner WHERE {where} LIMIT {MaxResults}",
                r =>
                {
                    var last = r.Str("lastname");
                    var first = r.Str("firstname");
                    var ownerName = last is null && first is null ? null : $"{last?.ToUpperInvariant()} {first}".Trim();
                    return new FiveMVehicle(r.Text("plate"), EsxModelName(r.Str("vehicle")), r.Str("owner"), ownerName);
                }, ct, parameters);
        }, ct);

    // ===== Lecture des lignes =====

    private string EsxPersonColumns()
    {
        var columns = new List<string> { "identifier", "firstname", "lastname" };
        foreach (var optional in new[] { "dateofbirth", "sex", "height", "phone_number" })
            if (_columns.Contains("users." + optional))
                columns.Add(optional);
        return string.Join(", ", columns);
    }

    private static FiveMPerson? ReadEsxPerson(MySqlDataReader r)
    {
        var last = r.Str("lastname");
        var first = r.Str("firstname");
        if (last is null && first is null)
            return null; // Compte sans personnage créé.

        return new FiveMPerson(
            r.Text("identifier"),
            (last ?? string.Empty).ToUpperInvariant(),
            first ?? string.Empty,
            Optional(r, "dateofbirth"),
            Optional(r, "sex")?.ToLowerInvariant() switch { "m" => "M", "f" => "F", var other => other?.ToUpperInvariant() },
            Optional(r, "height") is { } height ? height + " cm" : null,
            Optional(r, "phone_number"),
            null);
    }

    private static FiveMPerson? ReadQbPerson(MySqlDataReader r)
    {
        var info = ParseCharinfo(r.Str("charinfo"));
        return info is null
            ? null
            : new FiveMPerson(r.Text("citizenid"), info.Value.Nom, info.Value.Prenom, info.Value.Naissance, info.Value.Sexe,
                null, info.Value.Telephone, info.Value.Nationalite);
    }

    private static string? Optional(MySqlDataReader r, string column)
    {
        for (var i = 0; i < r.FieldCount; i++)
            if (string.Equals(r.GetName(i), column, StringComparison.OrdinalIgnoreCase))
                return r.Str(column);
        return null;
    }

    private static (string Nom, string Prenom, string? Naissance, string? Sexe, string? Telephone, string? Nationalite)? ParseCharinfo(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            string? Get(string name) => root.TryGetProperty(name, out var value)
                ? value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number => value.GetRawText(),
                    _ => null
                }
                : null;

            var gender = Get("gender") switch { "0" => "M", "1" => "F", var other => other };
            var birth = Get("birthdate");
            if (birth is not null && DateTime.TryParseExact(birth, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var date))
                birth = date.ToString("dd/MM/yyyy");
            return ((Get("lastname") ?? string.Empty).ToUpperInvariant(), Get("firstname") ?? string.Empty, birth, gender, Get("phone"), Get("nationality"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<string> QbLicences(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
            return [];
        try
        {
            using var document = JsonDocument.Parse(metadata);
            if (!document.RootElement.TryGetProperty("licences", out var licences) || licences.ValueKind != JsonValueKind.Object)
                return [];
            var categories = new List<string>();
            foreach (var licence in licences.EnumerateObject())
            {
                if (licence.Value.ValueKind != JsonValueKind.True)
                    continue;
                if (licence.Name == "driver") categories.Add("B");
            }
            return categories;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? EsxLicence(string type) => type switch
    {
        "drive" => "B",
        "drive_bike" => "A",
        "drive_truck" => "C",
        _ => null
    };

    /// <summary>ESX enregistre le modèle sous forme de hachage GTA ; on le retrouve grâce à la table « vehicles ».</summary>
    private string? EsxModelName(string? vehicleJson)
    {
        if (string.IsNullOrWhiteSpace(vehicleJson))
            return null;
        try
        {
            using var document = JsonDocument.Parse(vehicleJson);
            if (!document.RootElement.TryGetProperty("model", out var model))
                return null;
            if (model.ValueKind == JsonValueKind.String)
                return Prettify(model.GetString());
            if (model.ValueKind == JsonValueKind.Number && model.TryGetInt64(out var hash))
                return _modelNames.TryGetValue(unchecked((uint)hash), out var name) ? name : null;
        }
        catch (JsonException)
        {
            // Données véhicule illisibles : modèle inconnu.
        }
        return null;
    }

    private static string? Prettify(string? model)
        => string.IsNullOrWhiteSpace(model) ? null : char.ToUpperInvariant(model[0]) + model[1..];

    private string PlateExpression(string column)
        => $"REPLACE(REPLACE(UPPER({column}), ' ', ''), '-', '')";

    /// <summary>Hachage « joaat » utilisé par GTA V pour les noms de modèles.</summary>
    private static uint Joaat(string text)
    {
        unchecked
        {
            uint hash = 0;
            foreach (var c in text.ToLowerInvariant())
            {
                hash += c;
                hash += hash << 10;
                hash ^= hash >> 6;
            }
            hash += hash << 3;
            hash ^= hash >> 11;
            hash += hash << 15;
            return hash;
        }
    }

    // ===== Connexion et schéma =====

    private async Task<List<T>> SafeAsync<T>(string what, Func<MySqlConnection, Task<List<T>>> work, CancellationToken ct)
    {
        if (!IsEnabled)
            return [];
        try
        {
            await using var connection = await Sql.OpenAsync(ConnectionString, ct);
            await EnsureSchemaAsync(connection, ct);
            return await work(connection);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Base FiveM : échec de la {What}", what);
            return [];
        }
    }

    private async Task EnsureSchemaAsync(MySqlConnection connection, CancellationToken ct)
    {
        if (DateTimeOffset.UtcNow - _schemaLoadedAt < SchemaLifetime)
            return;

        await _schemaLock.WaitAsync(ct);
        try
        {
            if (DateTimeOffset.UtcNow - _schemaLoadedAt < SchemaLifetime)
                return;

            var columns = await Sql.QueryAsync(connection,
                "SELECT TABLE_NAME, COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE()",
                r => r.Text("TABLE_NAME") + "." + r.Text("COLUMN_NAME"), ct);
            var set = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);

            var models = new Dictionary<uint, string>();
            if (!IsQb && set.Contains("vehicles.model") && set.Contains("vehicles.name"))
            {
                var rows = await Sql.QueryAsync(connection, "SELECT model, name FROM vehicles",
                    r => (Model: r.Str("model"), Name: r.Str("name")), ct);
                foreach (var (model, name) in rows)
                    if (model is not null)
                        models[Joaat(model)] = name ?? Prettify(model)!;
            }

            _columns = set;
            _modelNames = models;
            _schemaLoadedAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            _schemaLock.Release();
        }
    }
}
