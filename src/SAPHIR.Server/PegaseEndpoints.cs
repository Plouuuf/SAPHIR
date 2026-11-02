using Saphir.Server.Data;
using Saphir.Shared.Contracts;

namespace Saphir.Server;

/// <summary>API de PEGASE (/api/pegase/…). Jeton d'application et accès PEGASE (rôle Discord) requis.</summary>
public static class PegaseEndpoints
{
    public static void MapPegase(this WebApplication app)
    {
        var api = app.MapGroup("/api/pegase");

        api.MapGet("/etat", (HttpContext http) =>
            RunAsync(http, (store, agent, ct) => Api.Ok(store.GetStateAsync(ct))));

        // ----- Appels -----

        api.MapPost("/appels", (HttpContext http) =>
            RunAsync(http, (store, agent, ct) => Api.Ok(store.CreateManualCallAsync(agent, ct))));

        api.MapGet("/appel/{id:long}", (HttpContext http, long id) =>
            RunAsync(http, async (store, agent, ct) =>
                await store.GetCallAsync(id, ct) is { } call ? Results.Ok(call) : Results.NotFound(new { error = "Appel introuvable." })));

        api.MapPost("/appel/{id:long}/evenement", (HttpContext http, long id, EvenementInput input) =>
            RunAsync(http, async (store, agent, ct) =>
                await store.CreateEventFromCallAsync(id, input, agent, ct) is { } eventId
                    ? Results.Ok(new { id = eventId })
                    : Results.NotFound(new { error = "Appel introuvable." })));

        api.MapPost("/appel/{id:long}/sans-suite", (HttpContext http, long id, SansSuiteInput input) =>
            RunAsync(http, async (store, agent, ct) =>
                await store.CloseCallWithoutEventAsync(id, input, agent, ct)
                    ? Results.NoContent()
                    : Results.NotFound(new { error = "Appel introuvable ou déjà traité." })));

        // ----- Événements -----

        api.MapGet("/evenement/{id:long}", (HttpContext http, long id) =>
            RunAsync(http, async (store, agent, ct) =>
                await store.GetEventAsync(id, ct) is { } ev ? Results.Ok(ev) : Results.NotFound(new { error = "Événement introuvable." })));

        api.MapPut("/evenement/{id:long}", (HttpContext http, long id, EvenementInput input) =>
            RunAsync(http, async (store, agent, ct) =>
                await store.UpdateEventAsync(id, input, agent, ct) ? Results.NoContent() : NotFoundEvent()));

        api.MapPost("/evenement/{id:long}/message", (HttpContext http, long id, MessageInput input) =>
            RunAsync(http, async (store, agent, ct) =>
                await store.AddMessageAsync(id, input.Texte, agent, ct) ? Results.NoContent() : NotFoundEvent()));

        api.MapPost("/evenement/{id:long}/engager", (HttpContext http, long id, EngagementInput input) =>
            RunAsync(http, async (store, agent, ct) =>
                await store.EngageAsync(id, input.EquipageId, agent, ct)
                    ? Results.NoContent()
                    : Results.NotFound(new { error = "Événement ou équipage introuvable." })));

        api.MapPost("/evenement/{id:long}/clore", (HttpContext http, long id, ClotureInput input) =>
            RunAsync(http, async (store, agent, ct) =>
                await store.CloseEventAsync(id, input.CompteRendu, agent, ct)
                    ? Results.NoContent()
                    : Results.NotFound(new { error = "Événement introuvable ou déjà terminé." })));

        // ----- Équipages -----

        api.MapPost("/equipages", (HttpContext http, EquipageInput input) =>
            RunAsync(http, async (store, agent, ct) => Results.Ok(new { id = await store.CreateCrewAsync(input, ct) })));

        api.MapPut("/equipage/{id:long}", (HttpContext http, long id, EquipageInput input) =>
            RunAsync(http, async (store, agent, ct) =>
                await store.UpdateCrewAsync(id, input, ct) ? Results.NoContent() : NotFoundCrew()));

        api.MapDelete("/equipage/{id:long}", (HttpContext http, long id) =>
            RunAsync(http, async (store, agent, ct) =>
                await store.DeleteCrewAsync(id, ct) ? Results.NoContent() : NotFoundCrew()));

        api.MapPost("/equipage/{id:long}/statut", (HttpContext http, long id, StatutInput input) =>
            RunAsync(http, async (store, agent, ct) =>
                await store.SetCrewStatusAsync(id, input.Statut, agent, ct) ? Results.NoContent() : NotFoundCrew()));
    }

    private static IResult NotFoundEvent() => Results.NotFound(new { error = "Événement introuvable." });
    private static IResult NotFoundCrew() => Results.NotFound(new { error = "Équipage introuvable." });

    private static Task<IResult> RunAsync(HttpContext http, Func<PegaseStore, AgentContext, CancellationToken, Task<IResult>> work)
        => Api.RunAsync(http,
            user => AccessLevels.CanOpen(user.Levels, SaphirApps.Pegase),
            "Accès PEGASE non autorisé pour ton rôle Discord.",
            (agent, ct) => work(http.RequestServices.GetRequiredService<PegaseStore>(), agent, ct));
}
