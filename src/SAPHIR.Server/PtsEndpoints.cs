using Saphir.Server.Data;
using Saphir.Shared.Contracts;

namespace Saphir.Server;

/// <summary>
/// API de PTS (/api/pts/…). Accès « scientifique » (rôle Discord) requis ; la recherche et la fiche d'identité
/// passent par l'API MEO-FIC, ouverte aussi à ce rôle.
/// </summary>
public static class PtsEndpoints
{
    public static void MapPts(this WebApplication app)
    {
        var api = app.MapGroup("/api/pts");

        api.MapGet("/personne", (HttpContext http, string key) =>
            RunAsync(http, false, (store, agent, ct) => Api.Ok(store.GetPersonAsync(key, agent, ct))));

        api.MapPost("/personne/signalisation", (HttpContext http, string key, SignalisationInput input) =>
            RunAsync(http, false, async (store, agent, ct) =>
                await store.AddSignalisationAsync(key, input, agent, ct) ? Results.NoContent() : NotFoundPerson()));

        api.MapPost("/personne/inscription", (HttpContext http, string key, InscriptionInput input) =>
            RunAsync(http, false, async (store, agent, ct) =>
                await store.AddInscriptionAsync(key, input, agent, ct) ? Results.NoContent() : NotFoundPerson()));

        api.MapGet("/traces", (HttpContext http, bool? toutes) =>
            RunAsync(http, false, (store, agent, ct) => Api.Ok(store.GetTracesAsync(toutes != true, ct))));

        api.MapPost("/traces", (HttpContext http, TraceInput input) =>
            RunAsync(http, false, async (store, agent, ct) => Results.Ok(new { id = await store.AddTraceAsync(input, agent, ct) })));

        api.MapPost("/trace/{id:long}/rapprocher", (HttpContext http, long id, RapprochementInput input) =>
            RunAsync(http, false, async (store, agent, ct) =>
                await store.MatchTraceAsync(id, input.PersonneKey, agent, ct) ? Results.NoContent() : NotFoundTrace()));

        api.MapPost("/trace/{id:long}/dissocier", (HttpContext http, long id) =>
            RunAsync(http, false, async (store, agent, ct) =>
                await store.UnmatchTraceAsync(id, agent, ct) ? Results.NoContent() : NotFoundTrace()));

        api.MapDelete("/{kind}/{id:long}", (HttpContext http, string kind, long id) =>
            RunAsync(http, true, async (store, agent, ct) =>
                await store.DeleteAsync(kind, id, agent, ct) ? Results.NoContent() : Results.NotFound(new { error = "Élément introuvable." })));
    }

    private static IResult NotFoundPerson() => Results.NotFound(new { error = "Personne introuvable." });
    private static IResult NotFoundTrace() => Results.NotFound(new { error = "Trace introuvable." });

    private static Task<IResult> RunAsync(
        HttpContext http, bool administrationOnly, Func<PtsStore, AgentContext, CancellationToken, Task<IResult>> work)
        => Api.RunAsync(http,
            user => administrationOnly
                ? user.Levels.Contains(AccessLevels.Administration)
                : AccessLevels.CanOpen(user.Levels, SaphirApps.Scientifique),
            "Accès PTS non autorisé pour ton rôle Discord.",
            (agent, ct) => work(http.RequestServices.GetRequiredService<PtsStore>(), agent, ct));
}
