using System.Data.Common;
using System.Globalization;
using MySqlConnector;

namespace Saphir.Server.Data;

/// <summary>
/// Connexion ouverte à la base SAPHIR, avec une transaction facultative.
/// Toutes les requêtes sont paramétrées : <c>("@nom", valeur)</c>.
/// </summary>
public sealed class SqlSession : IAsyncDisposable
{
    private readonly MySqlConnection _connection;
    private MySqlTransaction? _transaction;

    private SqlSession(MySqlConnection connection) => _connection = connection;

    public static async Task<SqlSession> OpenAsync(string connectionString, CancellationToken ct)
    {
        var connection = new MySqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
        return new SqlSession(connection);
    }

    /// <summary>Ouvre une transaction : les commandes suivantes y participent jusqu'à <see cref="CommitAsync"/>.</summary>
    public async Task BeginAsync(CancellationToken ct)
    {
        if (_transaction is not null)
            throw new InvalidOperationException("Une transaction est déjà ouverte sur cette connexion.");
        _transaction = await _connection.BeginTransactionAsync(ct);
    }

    public async Task CommitAsync(CancellationToken ct)
    {
        if (_transaction is null)
            return;
        await _transaction.CommitAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackAsync()
    {
        if (_transaction is null)
            return;
        try
        {
            await _transaction.RollbackAsync(CancellationToken.None);
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task<List<T>> QueryAsync<T>(string sql, Func<SqlRow, T> map, CancellationToken ct,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = CreateCommand(sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var row = new SqlRow(reader);
        var list = new List<T>();
        while (await reader.ReadAsync(ct))
            list.Add(map(row));
        return list;
    }

    public async Task<T?> FirstOrDefaultAsync<T>(string sql, Func<SqlRow, T> map, CancellationToken ct,
        params (string Name, object? Value)[] parameters) where T : class
    {
        await using var command = CreateCommand(sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return map(new SqlRow(reader));
    }

    /// <summary>Première colonne de la première ligne ; <c>null</c> si aucune ligne ou valeur NULL.</summary>
    public async Task<object?> ScalarAsync(string sql, CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        await using var command = CreateCommand(sql, parameters);
        var value = await command.ExecuteScalarAsync(ct);
        return value is DBNull ? null : value;
    }

    /// <summary>Valeur entière (COUNT…) ; 0 si aucune ligne.</summary>
    public async Task<long> LongAsync(string sql, CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        var value = await ScalarAsync(sql, ct, parameters);
        return value is null ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    /// <summary>Exécute une commande ; retourne le nombre de lignes touchées.</summary>
    public async Task<int> ExecuteAsync(string sql, CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        await using var command = CreateCommand(sql, parameters);
        return await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Exécute un INSERT ; retourne l'identifiant AUTO_INCREMENT créé.</summary>
    public async Task<long> InsertAsync(string sql, CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        await using var command = CreateCommand(sql, parameters);
        await command.ExecuteNonQueryAsync(ct);
        return command.LastInsertedId;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            try
            {
                // Une transaction non validée est annulée.
                await _transaction.DisposeAsync();
            }
            catch
            {
                // Connexion déjà perdue : rien à annuler.
            }
            _transaction = null;
        }
        await _connection.DisposeAsync();
    }

    private MySqlCommand CreateCommand(string sql, (string Name, object? Value)[] parameters)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = _transaction;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
}

/// <summary>
/// Lecture tolérante d'une ligne : colonne absente ou NULL → valeur de repli, conversions souples
/// (TINYINT/BIT → bool, DATETIME → DateTimeOffset UTC).
/// </summary>
public sealed class SqlRow
{
    private readonly DbDataReader _reader;
    private readonly Dictionary<string, int> _ordinals = new(StringComparer.OrdinalIgnoreCase);

    public SqlRow(DbDataReader reader)
    {
        _reader = reader;
        for (var i = 0; i < reader.FieldCount; i++)
            _ordinals.TryAdd(reader.GetName(i), i);
    }

    public bool Has(string column) => _ordinals.ContainsKey(column);

    public object? Value(string column)
    {
        if (!_ordinals.TryGetValue(column, out var ordinal) || _reader.IsDBNull(ordinal))
            return null;
        return _reader.GetValue(ordinal);
    }

    public string? NullableText(string column) => Value(column) switch
    {
        null => (string?)null,
        string text => text,
        byte[] bytes => System.Text.Encoding.UTF8.GetString(bytes),
        var other => Convert.ToString(other, CultureInfo.InvariantCulture)
    };

    public string Text(string column, string fallback = "") => NullableText(column) ?? fallback;

    public long? NullableLong(string column) => Value(column) switch
    {
        null => (long?)null,
        var other => (long?)Convert.ToInt64(other, CultureInfo.InvariantCulture)
    };

    public long Long(string column, long fallback = 0) => NullableLong(column) ?? fallback;

    public int Int(string column, int fallback = 0) => Value(column) switch
    {
        null => fallback,
        var other => Convert.ToInt32(other, CultureInfo.InvariantCulture)
    };

    public bool Bool(string column, bool fallback = false) => Value(column) switch
    {
        null => fallback,
        bool flag => flag,
        var other => Convert.ToInt64(other, CultureInfo.InvariantCulture) != 0
    };

    /// <summary>DATETIME stocké en UTC → DateTimeOffset (décalage 0).</summary>
    public DateTimeOffset? NullableDate(string column) => Value(column) switch
    {
        DateTime date => (DateTimeOffset?)new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)),
        DateTimeOffset offset => (DateTimeOffset?)offset.ToUniversalTime(),
        _ => (DateTimeOffset?)null
    };

    public DateTimeOffset Date(string column) => NullableDate(column) ?? DateTimeOffset.UnixEpoch;
}

/// <summary>Erreur métier renvoyée telle quelle au poste : code HTTP + message en français.</summary>
public sealed class ApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public static ApiException Saisie(string message) => new(StatusCodes.Status400BadRequest, message);
    public static ApiException Introuvable(string message) => new(StatusCodes.Status404NotFound, message);
    public static ApiException Conflit(string message) => new(StatusCodes.Status409Conflict, message);
}
