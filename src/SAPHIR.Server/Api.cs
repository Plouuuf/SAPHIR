using MySqlConnector;
using Saphir.Server.Data;
using Saphir.Server.Services;
using Saphir.Shared.Contracts;

namespace Saphir.Server;

/// <summary>Agent authentifié et autorisé, transmis au traitement d'une route.</summary>
public sealed record AgentContext(SaphirUserDto User, AgentDto Agent, AgentDroits Droits)
{
    /// <summary>Auteur des modifications (historique).</summary>
    public Auteur Auteur => new(Agent.Id, SocleRegles.LibelleAgent(Agent));
}

/// <summary>
/// Exécution commune des appels d'API : jeton de session Discord (401), base SAPHIR prête (503),
/// agent existant et autorisé (403), permissions (403), erreurs métier (<see cref="ApiException"/>)
/// et base injoignable (503). Erreurs au format <c>{ "error": "…" }</c>.
/// </summary>
public static class Api
{
    /// <summary>Session Discord valide et base prête ; pas encore besoin d'agent (session, première connexion).</summary>
    public static async Task<IResult> RunUserAsync(HttpContext http, Func<SaphirUserDto, CancellationToken, Task<IResult>> work)
    {
        var validation = await ApiAuth.AuthenticateAsync(http);
        if (!validation.Valid || validation.User is null)
            return Erreur(StatusCodes.Status401Unauthorized, validation.Error ?? "Connexion Discord requise.");

        var db = http.RequestServices.GetRequiredService<SaphirDb>();
        if (!db.IsConfigured)
            return Erreur(StatusCodes.Status503ServiceUnavailable, "La base SAPHIR n'est pas configurée sur le serveur.");
        if (!await db.EnsureReadyAsync(http.RequestAborted))
            return Erreur(StatusCodes.Status503ServiceUnavailable, "La base SAPHIR est injoignable pour le moment. Réessaie dans quelques instants.");

        try
        {
            return await work(validation.User, http.RequestAborted);
        }
        catch (ApiException ex)
        {
            return Erreur(ex.StatusCode, ex.Message);
        }
        catch (MySqlException ex)
        {
            http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("SAPHIR.Api").LogError(ex, "Base SAPHIR");
            return Erreur(StatusCodes.Status503ServiceUnavailable, "La base SAPHIR est injoignable pour le moment. Réessaie dans quelques instants.");
        }
    }

    /// <summary>
    /// Route protégée : agent existant, statut autorisé, au moins une des <paramref name="permissions"/>
    /// (liste vide = toute session d'agent), et rôle Discord RH si <paramref name="requireRhRole"/>.
    /// </summary>
    public static Task<IResult> RunAsync(
        HttpContext http,
        IReadOnlyList<string> permissions,
        Func<AgentContext, CancellationToken, Task<IResult>> work,
        bool requireRhRole = false)
        => RunUserAsync(http, async (user, ct) =>
        {
            var services = http.RequestServices;
            var agent = await services.GetRequiredService<AgentStore>().TrouverParDiscordAsync(user.UserId, ct);
            if (agent is null)
                return Erreur(StatusCodes.Status403Forbidden, "Accès refusé : termine d'abord ta première connexion à SAPHIR.");

            var journal = services.GetRequiredService<JournalAcces>();
            var cible = $"{http.Request.Method} {http.Request.Path}";
            if (!AgentStatuts.PeutSeConnecter(agent.Statut))
            {
                await journal.EcrireAsync(agent.Id, SocleRegles.LibelleAgent(agent), "Accès refusé", cible, agent.Statut);
                return Erreur(StatusCodes.Status403Forbidden, SocleRegles.MessageRefus(agent.Statut));
            }

            if (requireRhRole && !user.RoleRh)
            {
                await journal.EcrireAsync(agent.Id, SocleRegles.LibelleAgent(agent), "Accès refusé", cible, "Rôle Discord RH absent");
                return Erreur(StatusCodes.Status403Forbidden, "Accès refusé : le rôle Discord RH est requis.");
            }

            var droits = await services.GetRequiredService<PermissionService>().ObtenirAsync(agent.Id, ct);
            if (permissions.Count > 0 && !permissions.Any(droits.Possede))
            {
                await journal.EcrireAsync(agent.Id, SocleRegles.LibelleAgent(agent), "Accès refusé", cible, "Permission manquante");
                return Erreur(StatusCodes.Status403Forbidden, "Accès refusé : tu n'as pas la permission nécessaire pour cette action.");
            }

            return await work(new AgentContext(user, agent, droits), ct);
        });

    public static IResult Erreur(int statusCode, string message)
        => Results.Json(new { error = message }, statusCode: statusCode);
}

/// <summary>Vérification du jeton d'application envoyé par le poste (Authorization: Bearer …).</summary>
public static class ApiAuth
{
    public static async Task<DiscordOAuthService.AppTokenValidationResult> AuthenticateAsync(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return new DiscordOAuthService.AppTokenValidationResult(false, true, "Connexion Discord requise.", null);
        var oauth = context.RequestServices.GetRequiredService<DiscordOAuthService>();
        return await oauth.ValidateAppTokenAsync(header["Bearer ".Length..].Trim(), forceRoleCheck: false);
    }
}
