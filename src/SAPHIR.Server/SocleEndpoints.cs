using Saphir.Server.Data;
using Saphir.Server.Services;
using Saphir.Shared.Contracts;

namespace Saphir.Server;

/// <summary>Routes du socle (phase 2) : session, première connexion et Administration. Contrat : API-SOCLE.md.</summary>
public static class SocleEndpoints
{
    public static void MapSocle(this WebApplication app)
    {
        // ===== Version (publique) =====

        app.MapGet("/api/version", async (ParametresService parametres, CancellationToken ct)
            => Results.Ok(new { version = await parametres.VersionAffichageAsync(ct) }));

        // ===== Session =====

        app.MapGet("/api/session", (HttpContext http, SessionService sessions)
            => Api.RunUserAsync(http, async (user, ct) => Results.Ok(await sessions.LireAsync(user, ct))));

        app.MapPost("/api/session/premiere-connexion", (HttpContext http, PremiereConnexionInput input, SessionService sessions)
            => Api.RunUserAsync(http, async (user, ct) => Results.Ok(await sessions.PremiereConnexionAsync(user, input, ct))));

        // ===== Administration : comptes =====

        var admin = app.MapGroup("/api/admin");

        admin.MapGet("/comptes", (HttpContext http, AdminStore store, string? q, string? statut)
            => Api.RunAsync(http, [Permissions.AdminComptes],
                async (_, ct) => Results.Ok(await store.ListerComptesAsync(q, statut, ct))));

        admin.MapPut("/comptes/{id:long}/profils", (HttpContext http, long id, ProfilsAgentInput input, AdminStore store, RealtimeHub hub)
            => Api.RunAsync(http, [Permissions.AdminComptes], async (ctx, ct) =>
            {
                var discordId = await store.DefinirProfilsAsync(ctx.Auteur, id, input, ct);
                if (discordId is not null)
                    await PrevenirAsync(hub, [discordId]);
                return Results.NoContent();
            }));

        admin.MapPut("/comptes/{id:long}/statut", (HttpContext http, long id, StatutAgentInput input, AdminStore store, RealtimeHub hub)
            => Api.RunAsync(http, [Permissions.AdminComptes], async (ctx, ct) =>
            {
                var discordId = await store.DefinirStatutAsync(ctx.Auteur, id, input, ct);
                if (discordId is not null)
                    await PrevenirAsync(hub, [discordId]);
                return Results.NoContent();
            }));

        // ===== Administration : profils =====

        admin.MapGet("/profils", (HttpContext http, AdminStore store)
            => Api.RunAsync(http, [Permissions.AdminProfils, Permissions.AdminComptes],
                async (_, ct) => Results.Ok(await store.ListerProfilsAsync(ct))));

        admin.MapPost("/profils", (HttpContext http, ProfilInput input, AdminStore store)
            => Api.RunAsync(http, [Permissions.AdminProfils],
                async (ctx, ct) => Results.Ok(await store.CreerProfilAsync(ctx.Auteur, input, ct))));

        admin.MapPut("/profils/{id:long}", (HttpContext http, long id, ProfilInput input, AdminStore store, RealtimeHub hub)
            => Api.RunAsync(http, [Permissions.AdminProfils], async (ctx, ct) =>
            {
                var (profil, discordIds) = await store.ModifierProfilAsync(ctx.Auteur, id, input, ct);
                await PrevenirAsync(hub, discordIds);
                return Results.Ok(profil);
            }));

        admin.MapDelete("/profils/{id:long}", (HttpContext http, long id, AdminStore store)
            => Api.RunAsync(http, [Permissions.AdminProfils], async (ctx, ct) =>
            {
                await store.SupprimerProfilAsync(ctx.Auteur, id, ct);
                return Results.NoContent();
            }));

        // ===== Administration : paramètres =====

        admin.MapGet("/parametres", (HttpContext http, AdminStore store)
            => Api.RunAsync(http, [Permissions.AdminParametres],
                async (_, ct) => Results.Ok(await store.ListerParametresAsync(ct))));

        admin.MapPut("/parametres/{cle}", (HttpContext http, string cle, ParametreInput input, AdminStore store, RealtimeHub hub)
            => Api.RunAsync(http, [Permissions.AdminParametres], async (ctx, ct) =>
            {
                if (await store.ModifierParametreAsync(ctx.Auteur, cle, input, ct))
                    await hub.BroadcastAsync(new RealtimeMessage(RealtimeTypes.ParametresChanged, Text: cle), CancellationToken.None);
                return Results.NoContent();
            }));

        // ===== Administration : journaux =====

        admin.MapGet("/historique", (HttpContext http, SaphirDb db, string? objet, string? reference, int? limit)
            => Api.RunAsync(http, [Permissions.AdminJournaux], async (_, ct) =>
            {
                await using var session = await db.OpenAsync(ct);
                return Results.Ok(await Historique.ListerAsync(session, objet, reference, SocleRegles.Limite(limit), ct));
            }));

        admin.MapGet("/journal", (HttpContext http, JournalAcces journal, int? limit)
            => Api.RunAsync(http, [Permissions.AdminJournaux],
                async (_, ct) => Results.Ok(await journal.ListerAsync(SocleRegles.Limite(limit), ct))));
    }

    /// <summary>Profils, permissions ou statut modifiés : les postes concernés rechargent leur session.</summary>
    private static async Task PrevenirAsync(RealtimeHub hub, IEnumerable<string> discordIds)
    {
        foreach (var discordId in discordIds.Distinct(StringComparer.Ordinal))
            await hub.SendToUserAsync(discordId, new RealtimeMessage(RealtimeTypes.SessionChanged), CancellationToken.None);
    }
}
