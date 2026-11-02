using MySqlConnector;
using Saphir.Server.Data;
using Saphir.Server.Services;
using Saphir.Shared.Contracts;

namespace Saphir.Server;

/// <summary>
/// API de MEO-FIC (/api/meo/…). Jeton d'application requis (Authorization: Bearer) et accès MEO-FIC
/// (rôle Discord de l'application, ou administration). Chaque fiche ouverte est tracée dans le journal.
/// </summary>
public static class MeoFicEndpoints
{
    public static void MapMeoFic(this WebApplication app)
    {
        var api = app.MapGroup("/api/meo");

        // ----- Personnes -----

        api.MapGet("/personnes", (HttpContext http, string? q) =>
            RunAsync(http, false, (store, agent, ct) => Ok(store.SearchPeopleAsync(q ?? string.Empty, ct)), sharedWithPts: true));

        api.MapGet("/personne", (HttpContext http, string key) =>
            RunAsync(http, false, async (store, agent, ct) =>
            {
                var person = await store.GetPersonAsync(key, ct);
                if (person is null)
                    return Results.NotFound(new { error = "Personne introuvable." });
                await store.LogConsultationAsync(agent, "Consultation personne", $"{person.Identite.Nom} {person.Identite.Prenom}", ct);
                return Results.Ok(person);
            }, sharedWithPts: true));

        api.MapPost("/personnes", (HttpContext http, PersonInput input) =>
            RunAsync(http, false, async (store, agent, ct) =>
                Results.Ok(new { key = await store.CreatePersonAsync(input.Identite, agent, ct) }), sharedWithPts: true));

        api.MapPut("/personne/identite", (HttpContext http, string key, IdentityDto input) =>
            RunAsync(http, false, async (store, agent, ct) =>
                await store.UpdateIdentityAsync(key, input, agent, ct) ? Results.NoContent() : NotFoundPerson()));

        api.MapPost("/personne/taj", (HttpContext http, string key, TajInput input) =>
            RunAsync(http, false, async (store, agent, ct) =>
                await store.AddTajAsync(key, input, agent, ct) ? Results.NoContent() : NotFoundPerson()));

        api.MapPost("/personne/fpr", (HttpContext http, string key, FprInput input) =>
            RunAsync(http, false, async (store, agent, ct) =>
                await store.AddFprAsync(key, input, agent, ct) ? Results.NoContent() : NotFoundPerson()));

        api.MapPut("/personne/permis", (HttpContext http, string key, PermisInput input) =>
            RunAsync(http, false, async (store, agent, ct) =>
                await store.SetPermisAsync(key, input, agent, ct) ? Results.NoContent() : NotFoundPerson()));

        api.MapPost("/fpr/{id:long}/clore", (HttpContext http, long id) =>
            RunAsync(http, false, async (store, agent, ct) =>
                await store.CloseFprAsync(id, agent, ct) ? Results.NoContent() : Results.NotFound(new { error = "Fiche FPR introuvable ou déjà close." })));

        // ----- Véhicules -----

        api.MapGet("/vehicules", (HttpContext http, string? q) =>
            RunAsync(http, false, (store, agent, ct) => Ok(store.SearchVehiclesAsync(q ?? string.Empty, ct))));

        api.MapGet("/vehicule", (HttpContext http, string plaque) =>
            RunAsync(http, false, async (store, agent, ct) =>
            {
                var vehicle = await store.GetVehicleAsync(plaque, ct);
                if (vehicle is null)
                    return Results.NotFound(new { error = "Aucun véhicule ni signalement pour cette plaque." });
                await store.LogConsultationAsync(agent, "Consultation véhicule", vehicle.Plaque, ct);
                return Results.Ok(vehicle);
            }));

        api.MapPut("/vehicule", (HttpContext http, VehicleInput input) =>
            RunAsync(http, false, async (store, agent, ct) =>
            {
                await store.SaveVehicleAsync(input, agent, ct);
                return Results.NoContent();
            }));

        api.MapPost("/vehicule/foves", (HttpContext http, string plaque, FovesInput input) =>
            RunAsync(http, false, async (store, agent, ct) =>
            {
                await store.AddFovesAsync(plaque, input, agent, ct);
                return Results.NoContent();
            }));

        api.MapPost("/foves/{id:long}/clore", (HttpContext http, long id) =>
            RunAsync(http, false, async (store, agent, ct) =>
                await store.CloseFovesAsync(id, agent, ct) ? Results.NoContent() : Results.NotFound(new { error = "Signalement introuvable ou déjà clos." })));

        // ----- Administration -----

        api.MapDelete("/inscription/{kind}/{id:long}", (HttpContext http, string kind, long id) =>
            RunAsync(http, true, async (store, agent, ct) =>
                await store.DeleteEntryAsync(kind, id, agent, ct) ? Results.NoContent() : Results.NotFound(new { error = "Inscription introuvable." })));

        api.MapGet("/journal", (HttpContext http, int? limit) =>
            RunAsync(http, true, (store, agent, ct) => Ok(store.JournalAsync(limit ?? 200, ct))));
    }

    private static IResult NotFoundPerson() => Results.NotFound(new { error = "Personne introuvable." });

    private static async Task<IResult> Ok<T>(Task<T> task) => Results.Ok(await task);

    /// <summary>
    /// <paramref name="sharedWithPts"/> : recherche et fiche d'identité, aussi utilisées par PTS (police scientifique).
    /// </summary>
    private static Task<IResult> RunAsync(
        HttpContext http, bool administrationOnly, Func<MeoFicStore, AgentContext, CancellationToken, Task<IResult>> work,
        bool sharedWithPts = false)
        => Api.RunAsync(http,
            user => administrationOnly
                ? user.Levels.Contains(AccessLevels.Administration)
                : AccessLevels.CanOpen(user.Levels, SaphirApps.MeoFic) ||
                  (sharedWithPts && AccessLevels.CanOpen(user.Levels, SaphirApps.Scientifique)),
            "Accès MEO-FIC non autorisé pour ton rôle Discord.",
            (agent, ct) => work(http.RequestServices.GetRequiredService<MeoFicStore>(), agent, ct));
}
