using MySqlConnector;

namespace Saphir.Server.Data;

/// <summary>Petites aides autour de MySqlConnector (MariaDB), sans ORM.</summary>
public static class Sql
{
    public static async Task<MySqlConnection> OpenAsync(string connectionString, CancellationToken ct)
    {
        var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    public static MySqlCommand Command(MySqlConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    public static async Task<List<T>> QueryAsync<T>(
        MySqlConnection connection, string sql, Func<MySqlDataReader, T> map, CancellationToken ct,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<T>();
        while (await reader.ReadAsync(ct))
            rows.Add(map(reader));
        return rows;
    }

    public static async Task<int> ExecuteAsync(
        MySqlConnection connection, string sql, CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(connection, sql, parameters);
        return await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Exécute un INSERT et renvoie l'identifiant créé.</summary>
    public static async Task<long> InsertAsync(
        MySqlConnection connection, string sql, CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(connection, sql, parameters);
        await command.ExecuteNonQueryAsync(ct);
        return command.LastInsertedId;
    }

    public static async Task<object?> ScalarAsync(
        MySqlConnection connection, string sql, CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(connection, sql, parameters);
        var value = await command.ExecuteScalarAsync(ct);
        return value is DBNull ? null : value;
    }

    // ===== Lecture de colonnes, tolérante aux types =====

    public static string? Str(this MySqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return null;
        var value = reader.GetValue(ordinal);
        var text = value switch
        {
            byte[] bytes => System.Text.Encoding.UTF8.GetString(bytes),
            DateTime date => date.ToString("dd/MM/yyyy"),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
        };
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    public static string Text(this MySqlDataReader reader, string column) => reader.Str(column) ?? string.Empty;

    public static long Long(this MySqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt64(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);
    }

    public static long? NullableLong(this MySqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : Convert.ToInt64(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);
    }

    public static bool Bool(this MySqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return !reader.IsDBNull(ordinal) && Convert.ToBoolean(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Les dates SAPHIR sont enregistrées en UTC.</summary>
    public static DateTimeOffset Utc(this MySqlDataReader reader, string column)
        => reader.NullableUtc(column) ?? DateTimeOffset.MinValue;

    public static DateTimeOffset? NullableUtc(this MySqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return null;
        var date = reader.GetDateTime(ordinal);
        return new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc));
    }

    /// <summary>Texte saisi : espaces retirés, chaîne vide → NULL, longueur bornée.</summary>
    public static string? Clean(string? value, int maxLength = 255)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var text = value.Trim();
        return text.Length > maxLength ? text[..maxLength] : text;
    }

    /// <summary>Échappe les jokers d'un LIKE et entoure de %.</summary>
    public static string Like(string term)
        => "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    /// <summary>Plaque comparable : majuscules, sans espace ni tiret.</summary>
    public static string NormalizePlate(string? plate)
        => new string((plate ?? string.Empty).ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
}
