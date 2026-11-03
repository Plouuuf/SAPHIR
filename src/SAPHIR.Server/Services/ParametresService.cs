using MySqlConnector;
using Saphir.Server.Data;
using Saphir.Shared.Contracts;

namespace Saphir.Server.Services;

/// <summary>
/// Lecture des paramètres centraux (sys_parametres), mise en cache jusqu'à la prochaine modification.
/// Base indisponible : valeurs par défaut (la version reste affichable).
/// </summary>
public sealed class ParametresService(SaphirDb db, ILogger<ParametresService> logger)
{
    private volatile IReadOnlyDictionary<string, ParametreDto>? _cache;

    public async Task<string> ValeurAsync(string cle, CancellationToken ct)
    {
        var parametres = await ChargerAsync(ct);
        if (parametres.TryGetValue(cle, out var parametre))
            return parametre.Valeur;
        return SocleRegles.ParametresParDefaut.FirstOrDefault(p => p.Cle == cle)?.Valeur ?? string.Empty;
    }

    /// <summary>« SAPHIR - v1.0.0.0 ».</summary>
    public async Task<string> VersionAffichageAsync(CancellationToken ct)
        => SocleRegles.AffichageVersion(await ValeurAsync(ParametresCles.Version, ct));

    public async Task<List<ParametreDto>> ListerAsync(CancellationToken ct)
    {
        await using var session = await db.OpenAsync(ct);
        return await LireAsync(session, ct);
    }

    public void Invalider() => _cache = null;

    internal static Task<List<ParametreDto>> LireAsync(SqlSession session, CancellationToken ct)
        => session.QueryAsync(
            "SELECT cle, valeur, libelle, aide, modifiable FROM sys_parametres ORDER BY cle",
            r => new ParametreDto(
                r.Text("cle"),
                r.Text("valeur"),
                r.Text("libelle"),
                r.NullableText("aide"),
                r.Bool("modifiable", true)), ct);

    private async Task<IReadOnlyDictionary<string, ParametreDto>> ChargerAsync(CancellationToken ct)
    {
        var cache = _cache;
        if (cache is not null)
            return cache;

        if (!db.IsReady)
            return ParDefaut();

        try
        {
            var liste = await ListerAsync(ct);
            var dictionnaire = liste.ToDictionary(p => p.Cle, StringComparer.Ordinal);
            _cache = dictionnaire;
            return dictionnaire;
        }
        catch (MySqlException ex)
        {
            logger.LogWarning(ex, "Paramètres SAPHIR illisibles : valeurs par défaut utilisées");
            return ParDefaut();
        }
    }

    private static Dictionary<string, ParametreDto> ParDefaut()
        => SocleRegles.ParametresParDefaut.ToDictionary(p => p.Cle, StringComparer.Ordinal);
}
