using System.Security.Cryptography;
using System.Text;
using Saphir.Server.Data;
using Saphir.Server.Services;
using Saphir.Shared.Contracts;

namespace Saphir.Server;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            await RunServerAsync(args);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static async Task RunServerAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });

        // Secrets locaux (non versionnés) : appsettings.Local.json à côté de l'exécutable.
        builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

        builder.Services.Configure<SaphirServerOptions>(builder.Configuration.GetSection("SAPHIR"));
        builder.Services.Configure<DiscordOAuthOptions>(builder.Configuration.GetSection("Discord"));
        builder.Services.AddSingleton<AuthSessionStore>();
        builder.Services.AddSingleton<PersistentDiscordSessionStore>();
        builder.Services.AddSingleton<DiscordOAuthService>();
        builder.Services.AddSingleton<RealtimeHub>();
        builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection("Database"));
        builder.Services.AddSingleton<FiveMReader>();
        builder.Services.AddSingleton<MeoFicStore>();
        builder.Services.AddSingleton<PegaseStore>();
        builder.Services.AddSingleton<PtsStore>();

        var app = builder.Build();

        var accessKey = app.Configuration["SAPHIR:AccessKey"];
        if (string.IsNullOrWhiteSpace(accessKey) || accessKey.StartsWith("CHANGE_ME", StringComparison.Ordinal))
            throw new InvalidOperationException("La clé du serveur (SAPHIR:AccessKey) doit être configurée avant le démarrage.");

        if (!app.Services.GetRequiredService<DiscordOAuthService>().IsConfigured)
            app.Logger.LogWarning("Connexion Discord non configurée : renseigner Discord:ClientId, ClientSecret, PublicBaseUrl, GuildId et RequiredRoleId.");

        var meoFic = app.Services.GetRequiredService<MeoFicStore>();
        if (!meoFic.IsConfigured)
        {
            app.Logger.LogWarning("Base SAPHIR non configurée : renseigner Database:Saphir (MEO-FIC indisponible).");
        }
        else
        {
            try
            {
                await meoFic.InitializeAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Base SAPHIR injoignable au démarrage : MEO-FIC répondra « base injoignable » tant qu'elle ne l'est pas.");
            }
        }

        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });

        app.MapGet("/", () => Results.Ok(new { service = "SAPHIR Server", status = "online" }));
        app.MapGet("/health", (RealtimeHub hub) => Results.Ok(new { status = "ok", utc = DateTimeOffset.UtcNow, connections = hub.ConnectionCount }));

        // ===== Connexion Discord =====

        app.MapPost("/auth/discord/session", (DiscordOAuthService oauth, AuthSessionStore sessions) =>
        {
            if (!oauth.IsConfigured)
                return Results.Problem("La connexion Discord n'est pas configurée sur le serveur SAPHIR.", statusCode: 503);
            var session = sessions.CreateSession();
            return Results.Ok(new AuthSessionStartResponse(session.Id, oauth.BuildAuthorizeUrl(session.Id), session.ExpiresAt));
        });

        app.MapGet("/auth/discord/status/{sessionId}", (string sessionId, AuthSessionStore sessions) =>
        {
            if (!sessions.TryGetSession(sessionId, out var session))
                return Results.NotFound(new AuthStatusResponse("expired", "Session expirée.", null, null));
            return Results.Ok(new AuthStatusResponse(
                session.Status,
                session.Error,
                session.Status == "approved" ? session.AppToken : null,
                session.User));
        });

        app.MapPost("/auth/discord/validate", async (TokenValidationRequest request, DiscordOAuthService oauth) =>
        {
            var result = await oauth.ValidateAppTokenAsync(request.Token, forceRoleCheck: true);
            return Results.Ok(new TokenValidationResponse(result.Valid, result.Reauthenticate, result.Error, result.Valid ? result.User : null));
        });

        app.MapGet("/auth/discord/callback", async (HttpContext context, DiscordOAuthService oauth, AuthSessionStore sessions) =>
        {
            context.Response.Headers["Cache-Control"] = "no-store, max-age=0";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";

            var code = context.Request.Query["code"].ToString();
            var state = context.Request.Query["state"].ToString();
            var error = context.Request.Query["error"].ToString();

            if (!string.IsNullOrWhiteSpace(error))
            {
                if (!string.IsNullOrWhiteSpace(state)) sessions.Deny(state, "Connexion Discord annulée.");
                return Html(OAuthPage.Build("Connexion annulée", "La connexion Discord a été annulée. Tu peux revenir dans SAPHIR.", false, null));
            }

            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
                return Html(OAuthPage.Build("Connexion impossible", "La demande est invalide ou expirée. Relance la connexion depuis SAPHIR.", false, null), 400);

            var result = await oauth.CompleteAsync(code, state);
            return Html(OAuthPage.Build(result.Success ? "Accès autorisé" : "Accès refusé", result.Message, result.Success, result.DisplayName));
        });

        // ===== Temps réel =====

        app.Map("/ws", branch => branch.Run(context => context.RequestServices.GetRequiredService<RealtimeHub>().HandleAsync(context)));

        // ===== Données (jeton d'application requis) =====

        app.MapGet("/api/me", async (HttpContext context) =>
        {
            var user = await ApiAuth.AuthenticateAsync(context);
            return user is null ? Results.Unauthorized() : Results.Ok(user);
        });

        app.MapMeoFic();
        app.MapPegase();
        app.MapPts();

        // ===== Intégrations serveur à serveur (clé SAPHIR:AccessKey dans l'en-tête X-Saphir-Key) =====

        // Notification de test, et base des futurs liens (SIMCALL pour PEGASE, scripts FiveM).
        // Corps : { "type": "notify", "title": "…", "text": "…", "appId": "…" } ; ?userId=… pour un seul joueur.
        app.MapPost("/api/integrations/notify", async (HttpContext context, RealtimeMessage message, RealtimeHub hub) =>
        {
            if (!HasIntegrationKey(context, accessKey))
                return Results.Unauthorized();

            var notification = message with { Type = RealtimeTypes.Notify, Token = null };
            var userId = context.Request.Query["userId"].ToString();
            var sent = string.IsNullOrWhiteSpace(userId)
                ? await hub.BroadcastAsync(notification, context.RequestAborted)
                : await hub.SendToUserAsync(userId, notification, context.RequestAborted);
            return Results.Ok(new { sent });
        });

        // SIMCALL : décroché d'un 17 → fiche d'appel PEGASE ouverte sur le poste de l'opérateur.
        app.MapPost("/api/integrations/simcall/answered", async (HttpContext context, SimcallCallAnswered message, PegaseStore pegase) =>
        {
            if (!HasIntegrationKey(context, accessKey))
                return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(message.SimcallCallId) || string.IsNullOrWhiteSpace(message.DiscordUserId))
                return Results.BadRequest(new { error = "Appel SIMCALL incomplet." });
            if (!meoFic.IsConfigured)
                return Results.Json(new { error = "Base SAPHIR non configurée." }, statusCode: StatusCodes.Status503ServiceUnavailable);
            var call = await pegase.SimcallAnsweredAsync(message, context.RequestAborted);
            return Results.Ok(new { id = call.Id });
        });

        // SIMCALL : raccroché → durée de l'appel figée.
        app.MapPost("/api/integrations/simcall/ended", async (HttpContext context, SimcallCallEnded message, PegaseStore pegase) =>
        {
            if (!HasIntegrationKey(context, accessKey))
                return Results.Unauthorized();
            if (!meoFic.IsConfigured)
                return Results.Json(new { error = "Base SAPHIR non configurée." }, statusCode: StatusCodes.Status503ServiceUnavailable);
            await pegase.SimcallEndedAsync(message, context.RequestAborted);
            return Results.NoContent();
        });

        await app.RunAsync();
    }

    private static IResult Html(string html, int statusCode = 200)
        => Results.Content(html, "text/html; charset=utf-8", Encoding.UTF8, statusCode);

    private static bool HasIntegrationKey(HttpContext context, string expected)
    {
        var provided = context.Request.Headers["X-Saphir-Key"].ToString();
        if (string.IsNullOrEmpty(provided))
            return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));
    }
}
