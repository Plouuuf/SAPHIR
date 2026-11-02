using MySqlConnector;
using Saphir.Server.Data;
using Saphir.Server.Services;
using Saphir.Shared.Contracts;

namespace Saphir.Server;

/// <summary>
/// Exécution commune des appels d'API : jeton d'application, droit d'accès (rôle Discord),
/// base SAPHIR configurée, erreurs de saisie (400) et base injoignable (503).
/// </summary>
public static class Api
{
    public static async Task<IResult> RunAsync(
        HttpContext http,
        Func<SaphirUserDto, bool> allowed,
        string deniedMessage,
        Func<AgentContext, CancellationToken, Task<IResult>> work)
    {
        var user = await ApiAuth.AuthenticateAsync(http);
        if (user is null)
            return Results.Unauthorized();
        if (!allowed(user))
            return Results.Json(new { error = deniedMessage }, statusCode: StatusCodes.Status403Forbidden);

        var options = http.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<DatabaseOptions>>();
        if (string.IsNullOrWhiteSpace(options.CurrentValue.Saphir))
            return Results.Json(new { error = "La base SAPHIR n'est pas configurée sur le serveur." }, statusCode: StatusCodes.Status503ServiceUnavailable);

        try
        {
            return await work(new AgentContext(user.UserId, user.DisplayName), http.RequestAborted);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (MySqlException ex)
        {
            http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("SAPHIR.Api").LogError(ex, "Base SAPHIR");
            return Results.Json(new { error = "La base SAPHIR est injoignable pour le moment." }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    public static async Task<IResult> Ok<T>(Task<T> task) => Results.Ok(await task);
}

/// <summary>Vérification du jeton d'application envoyé par le poste (Authorization: Bearer …).</summary>
public static class ApiAuth
{
    public static async Task<SaphirUserDto?> AuthenticateAsync(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;
        var oauth = context.RequestServices.GetRequiredService<DiscordOAuthService>();
        var result = await oauth.ValidateAppTokenAsync(header["Bearer ".Length..].Trim(), forceRoleCheck: false);
        return result.Valid ? result.User : null;
    }
}
