using System.Security.Cryptography;
using System.Text;
using MySqlConnector;
using Saphir.Server.Data;
using Saphir.Shared.Contracts.Judiciaire;

namespace Saphir.Server.Modules.Judiciaire;

/// <summary>
/// Déclaration d'identité en jeu : routes d'intégration appelées par la ressource FiveM saphir-identite
/// (clé SAPHIR:AccessKey dans l'en-tête X-Saphir-Key, comme GÉOOPS et SIMCALL). Contrat : docs/API-CITOYEN.md.
/// </summary>
public static class CitoyenEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/integrations/identite");

        g.MapGet("/etat", (HttpContext http, string? discordId, string? personnageId, CitoyenStore store)
            => IntegrationAsync(http, async ct => Results.Ok(await store.EtatAsync(discordId, personnageId, ct))));

        g.MapPost("/declarer", (HttpContext http, CitoyenIdentiteInput input, CitoyenStore store)
            => IntegrationAsync(http, async ct => Results.Ok(await store.DeclarerAsync(input, ct))));

        g.MapPost("/retirer", (HttpContext http, CitoyenJoueurInput input, CitoyenStore store)
            => IntegrationAsync(http, async ct => Results.Ok(new { message = await store.RetirerAsync(input, ct) })));

        g.MapPost("/vehicules", (HttpContext http, CitoyenVehiculesInput input, CitoyenStore store)
            => IntegrationAsync(http, async ct => Results.Ok(await store.DeclarerVehiculesAsync(input, ct))));

        g.MapPost("/vehicules/retirer", (HttpContext http, CitoyenVehiculeRetraitInput input, CitoyenStore store)
            => IntegrationAsync(http, async ct => Results.Ok(new { message = await store.RetirerVehiculeAsync(input, ct) })));

        g.MapPost("/permis", (HttpContext http, CitoyenPermisInput input, CitoyenStore store)
            => IntegrationAsync(http, async ct => Results.Ok(await store.DeclarerPermisAsync(input, ct))));
    }

    /// <summary>Clé d'intégration (comparaison à temps constant), base prête, erreurs métier { error } et base injoignable.</summary>
    private static async Task<IResult> IntegrationAsync(HttpContext http, Func<CancellationToken, Task<IResult>> work)
    {
        var attendue = http.RequestServices.GetRequiredService<IConfiguration>()["SAPHIR:AccessKey"];
        var fournie = http.Request.Headers["X-Saphir-Key"].ToString();
        if (string.IsNullOrEmpty(attendue) || string.IsNullOrEmpty(fournie) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(fournie), Encoding.UTF8.GetBytes(attendue)))
            return Api.Erreur(StatusCodes.Status401Unauthorized, "Clé d'intégration SAPHIR invalide.");

        var db = http.RequestServices.GetRequiredService<SaphirDb>();
        if (!db.IsConfigured)
            return Api.Erreur(StatusCodes.Status503ServiceUnavailable, "Base SAPHIR non configurée.");
        if (!await db.EnsureReadyAsync(http.RequestAborted))
            return Api.Erreur(StatusCodes.Status503ServiceUnavailable, "Base SAPHIR injoignable pour le moment.");
        try
        {
            return await work(http.RequestAborted);
        }
        catch (ApiException ex)
        {
            return Api.Erreur(ex.StatusCode, ex.Message);
        }
        catch (MySqlException ex)
        {
            http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("SAPHIR.Citoyen").LogError(ex, "Base SAPHIR (déclaration d'identité)");
            return Api.Erreur(StatusCodes.Status503ServiceUnavailable, "Base SAPHIR injoignable pour le moment.");
        }
    }
}
