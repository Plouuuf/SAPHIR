using System.Security.Cryptography;
using System.Text;
using MySqlConnector;
using Saphir.Server.Data;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Operations;

namespace Saphir.Server.Modules.Operations;

/// <summary>Routes du module Opérations. Contrat détaillé : docs/API-OPERATIONS.md.</summary>
public static class OperationsEndpoints
{
    /// <summary>Toute permission de la Main courante (consultation).</summary>
    private static readonly string[] McTous =
    [
        Permissions.McConsulter, Permissions.McMentionRediger, Permissions.McMentionValider, Permissions.McVacationGerer,
        Permissions.McEquipageGerer, Permissions.McVehiculeGerer
    ];

    /// <summary>Toute permission PÉGASE (consultation).</summary>
    private static readonly string[] PgTous =
    [
        Permissions.PegaseConsulter, Permissions.PegaseAppelCreer, Permissions.PegaseInterventionCreer, Permissions.PegaseAffecter,
        Permissions.PegaseStatut, Permissions.PegaseCompteRendu, Permissions.PegaseCloturer, Permissions.PegaseTn
    ];

    /// <summary>Acteurs opérationnels PÉGASE (messages sur une intervention).</summary>
    private static readonly string[] PgActeurs =
    [
        Permissions.PegaseAppelCreer, Permissions.PegaseInterventionCreer, Permissions.PegaseAffecter, Permissions.PegaseStatut,
        Permissions.PegaseCompteRendu, Permissions.PegaseCloturer, Permissions.PegaseTn
    ];

    public static void MapMainCourante(this WebApplication app)
    {
        var mc = app.MapGroup("/api/main-courante");

        mc.MapGet("/tableau-de-bord", (HttpContext http, MainCouranteStore store)
            => Api.RunAsync(http, McTous, async (_, ct) => Results.Ok(await store.TableauDeBordAsync(ct))));

        // ----- Vacations -----
        mc.MapGet("/vacations", (HttpContext http, MainCouranteStore store, string? statut, int? limit)
            => Api.RunAsync(http, McTous, async (_, ct) => Results.Ok(await store.ListerVacationsAsync(statut, SocleRegles.Limite(limit, 100, 500), ct))));
        mc.MapGet("/vacations/{id:long}", (HttpContext http, long id, MainCouranteStore store)
            => Api.RunAsync(http, McTous, async (_, ct) => Results.Ok(await store.LireVacationAsync(id, ct))));
        mc.MapPost("/vacations", (HttpContext http, VacationInput input, MainCouranteStore store)
            => Api.RunAsync(http, [Permissions.McVacationGerer], async (ctx, ct) => Results.Ok(await store.CreerVacationAsync(ctx, input, ct))));
        mc.MapPut("/vacations/{id:long}", (HttpContext http, long id, VacationInput input, MainCouranteStore store)
            => Api.RunAsync(http, [Permissions.McVacationGerer], async (ctx, ct) => Results.Ok(await store.ModifierVacationAsync(ctx, id, input, ct))));
        mc.MapPost("/vacations/{id:long}/cloturer", (HttpContext http, long id, ClotureVacationInput input, MainCouranteStore store)
            => Api.RunAsync(http, [Permissions.McVacationGerer], async (ctx, ct) => Results.Ok(await store.CloturerVacationAsync(ctx, id, input, ct))));

        // ----- Équipages -----
        mc.MapGet("/equipages", (HttpContext http, MainCouranteStore store, long? vacationId, bool? actifs)
            => Api.RunAsync(http, [.. McTous, .. PgTous],
                async (_, ct) => Results.Ok(await store.ListerEquipagesAsync(vacationId, actifs ?? true, ct))));
        mc.MapPost("/equipages", (HttpContext http, EquipageInput input, MainCouranteStore store)
            => Api.RunAsync(http, [Permissions.McEquipageGerer], async (ctx, ct) => Results.Ok(await store.CreerEquipageAsync(ctx, input, ct))));
        mc.MapPut("/equipages/{id:long}", (HttpContext http, long id, EquipageInput input, MainCouranteStore store)
            => Api.RunAsync(http, [Permissions.McEquipageGerer], async (ctx, ct) => Results.Ok(await store.ModifierEquipageAsync(ctx, id, input, ct))));
        mc.MapPost("/equipages/{id:long}/dissoudre", (HttpContext http, long id, MotifInput input, MainCouranteStore store)
            => Api.RunAsync(http, [Permissions.McEquipageGerer], async (ctx, ct) =>
            {
                await store.DissoudreEquipageAsync(ctx, id, input, ct);
                return Results.NoContent();
            }));

        // ----- Véhicules de service -----
        mc.MapGet("/vehicules", (HttpContext http, MainCouranteStore store)
            => Api.RunAsync(http, [.. McTous, .. PgTous], async (_, ct) => Results.Ok(await store.ListerVehiculesAsync(ct))));
        mc.MapGet("/vehicules/{id:long}", (HttpContext http, long id, MainCouranteStore store)
            => Api.RunAsync(http, McTous, async (_, ct) => Results.Ok(await store.FicheVehiculeAsync(id, ct))));
        mc.MapPost("/vehicules", (HttpContext http, VehiculeServiceInput input, MainCouranteStore store)
            => Api.RunAsync(http, [Permissions.McVehiculeGerer], async (ctx, ct) => Results.Ok(await store.EnregistrerVehiculeAsync(ctx, null, input, ct))));
        mc.MapPut("/vehicules/{id:long}", (HttpContext http, long id, VehiculeServiceInput input, MainCouranteStore store)
            => Api.RunAsync(http, [Permissions.McVehiculeGerer], async (ctx, ct) => Results.Ok(await store.EnregistrerVehiculeAsync(ctx, id, input, ct))));
        mc.MapPost("/vehicules/{id:long}/sortie", (HttpContext http, long id, SortieVehiculeInput input, MainCouranteStore store)
            => Api.RunAsync(http, [Permissions.McVehiculeGerer], async (ctx, ct) => Results.Ok(await store.SortieVehiculeAsync(ctx, id, input, ct))));
        mc.MapPost("/vehicules/{id:long}/retour", (HttpContext http, long id, RetourVehiculeInput input, MainCouranteStore store)
            => Api.RunAsync(http, [Permissions.McVehiculeGerer], async (ctx, ct) => Results.Ok(await store.RetourVehiculeAsync(ctx, id, input, ct))));
        mc.MapPost("/vehicules/{id:long}/disponibilite", (HttpContext http, long id, DisponibiliteVehiculeInput input, MainCouranteStore store)
            => Api.RunAsync(http, [Permissions.McVehiculeGerer], async (ctx, ct) => Results.Ok(await store.DisponibiliteVehiculeAsync(ctx, id, input, ct))));

        // ----- Mentions et déclarations -----
        string[] lecteursMentions = [Permissions.McConsulter, Permissions.McMentionRediger, Permissions.McMentionValider];
        mc.MapGet("/mentions", (HttpContext http, MainCouranteStore store, string? q, string? type, string? statut, DateTimeOffset? du,
                DateTimeOffset? au, long? interventionId, int? limit)
            => Api.RunAsync(http, lecteursMentions, async (_, ct) =>
                Results.Ok(await store.ListerMentionsAsync(q, type, statut, du, au, interventionId, SocleRegles.Limite(limit, 200, 1000), ct))));
        mc.MapGet("/mentions/projet", (HttpContext http, MainCouranteStore store, long interventionId)
            => Api.RunAsync(http, [Permissions.McMentionRediger], async (_, ct) => Results.Ok(await store.ProjetMentionAsync(interventionId, ct))));
        mc.MapGet("/mentions/{id:long}", (HttpContext http, long id, MainCouranteStore store, JournalAcces journal)
            => Api.RunAsync(http, lecteursMentions, async (ctx, ct) =>
            {
                var mention = await store.LireMentionAsync(id, ct);
                await journal.EcrireAsync(ctx.Agent.Id, ctx.Auteur.Nom, "Consultation main courante", "Mention " + mention.Numero, "Autorisé");
                return Results.Ok(mention);
            }));
        mc.MapPost("/mentions", (HttpContext http, MentionInput input, MainCouranteStore store)
            => Api.RunAsync(http, [Permissions.McMentionRediger], async (ctx, ct) => Results.Ok(await store.CreerMentionAsync(ctx, input, ct))));
        mc.MapPut("/mentions/{id:long}", (HttpContext http, long id, MentionInput input, MainCouranteStore store)
            => Api.RunAsync(http, [Permissions.McMentionRediger, Permissions.McMentionValider],
                async (ctx, ct) => Results.Ok(await store.ModifierMentionAsync(ctx, id, input, ct))));
        mc.MapPost("/mentions/{id:long}/valider", (HttpContext http, long id, MainCouranteStore store)
            => Api.RunAsync(http, [Permissions.McMentionValider], async (ctx, ct) => Results.Ok(await store.ValiderMentionAsync(ctx, id, ct))));

        // ----- Journal de service, historique -----
        mc.MapGet("/journal", (HttpContext http, MainCouranteStore store, DateTimeOffset? du, DateTimeOffset? au, long? vacationId, string? type,
                string? q, int? limit)
            => Api.RunAsync(http, McTous, async (_, ct) =>
                Results.Ok(await store.ListerJournalAsync(du, au, vacationId, type, q, SocleRegles.Limite(limit, 300, 2000), ct))));
        mc.MapPost("/journal", (HttpContext http, JournalInput input, MainCouranteStore store)
            => Api.RunAsync(http, [Permissions.McMentionRediger, Permissions.McVacationGerer], async (ctx, ct) =>
            {
                await store.AjouterJournalAsync(ctx, input, ct);
                return Results.NoContent();
            }));
        mc.MapGet("/historique", (HttpContext http, MainCouranteStore store, string? reference, string? q, int? limit)
            => Api.RunAsync(http, McTous, async (_, ct) => Results.Ok(await store.HistoriqueAsync(reference, q, SocleRegles.Limite(limit, 300, 1000), ct))));
    }

    public static void MapPegase(this WebApplication app)
    {
        var pg = app.MapGroup("/api/pegase");

        pg.MapGet("/tableau-de-bord", (HttpContext http, PegaseStore store)
            => Api.RunAsync(http, PgTous, async (_, ct) => Results.Ok(await store.TableauDeBordAsync(ct))));

        // ----- Appels -----
        pg.MapGet("/appels", (HttpContext http, PegaseStore store, string? etat, string? q, int? limit)
            => Api.RunAsync(http, PgTous, async (_, ct) => Results.Ok(await store.ListerAppelsAsync(etat, q, SocleRegles.Limite(limit, 100, 500), ct))));
        pg.MapGet("/appels/{id:long}", (HttpContext http, long id, PegaseStore store, JournalAcces journal)
            => Api.RunAsync(http, PgTous, async (ctx, ct) =>
            {
                var appel = await store.LireAppelAsync(id, ct);
                await journal.EcrireAsync(ctx.Agent.Id, ctx.Auteur.Nom, "Consultation PÉGASE", "Appel " + appel.Numero, "Autorisé");
                return Results.Ok(appel);
            }));
        pg.MapPost("/appels", (HttpContext http, AppelCreationInput input, PegaseStore store)
            => Api.RunAsync(http, [Permissions.PegaseAppelCreer], async (ctx, ct) => Results.Ok(await store.CreerAppelAsync(ctx, input, ct))));
        pg.MapPut("/appels/{id:long}", (HttpContext http, long id, AppelActionInput input, PegaseStore store)
            => Api.RunAsync(http, [Permissions.PegaseAppelCreer], async (ctx, ct) => Results.Ok(await store.EnregistrerAppelAsync(ctx, id, input, ct))));
        pg.MapPost("/appels/{id:long}/attente", (HttpContext http, long id, AppelActionInput input, PegaseStore store)
            => Api.RunAsync(http, [Permissions.PegaseAppelCreer], async (ctx, ct) => Results.Ok(await store.AttenteAppelAsync(ctx, id, input, false, ct))));
        pg.MapPost("/appels/{id:long}/transmettre-tn", (HttpContext http, long id, AppelActionInput input, PegaseStore store)
            => Api.RunAsync(http, [Permissions.PegaseAppelCreer], async (ctx, ct) => Results.Ok(await store.AttenteAppelAsync(ctx, id, input, true, ct))));
        pg.MapPost("/appels/{id:long}/clore", (HttpContext http, long id, AppelActionInput input, PegaseStore store)
            => Api.RunAsync(http, [Permissions.PegaseAppelCreer], async (ctx, ct) => Results.Ok(await store.CloreAppelAsync(ctx, id, input, ct))));
        pg.MapPost("/appels/{id:long}/rattacher", (HttpContext http, long id, AppelActionInput input, PegaseStore store)
            => Api.RunAsync(http, [Permissions.PegaseAppelCreer], async (ctx, ct) => Results.Ok(await store.RattacherAppelAsync(ctx, id, input, ct))));
        pg.MapPost("/appels/{id:long}/intervention", (HttpContext http, long id, AppelActionInput input, PegaseStore store)
            => Api.RunAsync(http, [Permissions.PegaseInterventionCreer], async (ctx, ct) => Results.Ok(await store.CreerInterventionAsync(ctx, id, input, ct))));

        // ----- Interventions -----
        pg.MapGet("/interventions", (HttpContext http, PegaseStore store, string? vue, string? q, DateTimeOffset? du, DateTimeOffset? au, int? limit)
            => Api.RunAsync(http, PgTous, async (_, ct) =>
                Results.Ok(await store.ListerInterventionsAsync(vue, q, du, au, SocleRegles.Limite(limit, 200, 1000), ct))));
        pg.MapGet("/interventions/{id:long}", (HttpContext http, long id, PegaseStore store, JournalAcces journal)
            => Api.RunAsync(http, PgTous, async (ctx, ct) =>
            {
                var intervention = await store.LireInterventionAsync(id, ct);
                await journal.EcrireAsync(ctx.Agent.Id, ctx.Auteur.Nom, "Consultation PÉGASE", "Intervention " + intervention.Numero, "Autorisé");
                return Results.Ok(intervention);
            }));
        pg.MapGet("/interventions/{id:long}/resume", (HttpContext http, long id, PegaseStore store)
            => Api.RunAsync(http, [Permissions.PegaseConsulter, Permissions.LrppnConsulter], async (_, ct) => Results.Ok(await store.ResumeAsync(id, ct))));
        pg.MapPut("/interventions/{id:long}", (HttpContext http, long id, InterventionModificationInput input, PegaseStore store)
            => Api.RunAsync(http, [Permissions.PegaseInterventionCreer, Permissions.PegaseAffecter],
                async (ctx, ct) => Results.Ok(await store.ModifierInterventionAsync(ctx, id, input, ct))));
        pg.MapPut("/interventions/{id:long}/position", (HttpContext http, long id, PositionInput input, PegaseStore store)
            => Api.RunAsync(http, [Permissions.PegaseInterventionCreer, Permissions.PegaseAffecter],
                async (ctx, ct) => Results.Ok(await store.PositionInterventionAsync(ctx, id, input, ct))));
        pg.MapPost("/interventions/{id:long}/statut", (HttpContext http, long id, InterventionStatutInput input, PegaseStore store)
            => Api.RunAsync(http, [Permissions.PegaseAffecter, Permissions.PegaseCloturer, Permissions.PegaseInterventionCreer],
                async (ctx, ct) => Results.Ok(await store.ChangerStatutInterventionAsync(ctx, id, input, ct))));
        pg.MapPost("/interventions/{id:long}/engagements", (HttpContext http, long id, EngagementInput input, PegaseStore store)
            => Api.RunAsync(http, [Permissions.PegaseAffecter], async (ctx, ct) => Results.Ok(await store.EngagerAsync(ctx, id, input, ct))));
        pg.MapPost("/interventions/{id:long}/engagements/{engagementId:long}/desengager",
            (HttpContext http, long id, long engagementId, MotifInput input, PegaseStore store)
                => Api.RunAsync(http, [Permissions.PegaseAffecter], async (ctx, ct) => Results.Ok(await store.DesengagerAsync(ctx, id, engagementId, input, ct))));
        pg.MapPost("/interventions/{id:long}/messages", (HttpContext http, long id, MessageInput input, PegaseStore store)
            => Api.RunAsync(http, PgActeurs, async (ctx, ct) => Results.Ok(await store.MessageAsync(ctx, id, input, ct))));
        pg.MapPut("/interventions/{id:long}/references", (HttpContext http, long id, ReferencesInput input, PegaseStore store)
            => Api.RunAsync(http, [Permissions.PegaseCompteRendu, Permissions.PegaseCloturer, Permissions.PegaseAffecter, Permissions.LrppnRediger],
                async (ctx, ct) => Results.Ok(await store.ReferencesAsync(ctx, id, input, ct))));

        // ----- Comptes rendus -----
        pg.MapPost("/interventions/{id:long}/comptes-rendus", (HttpContext http, long id, CompteRenduInput input, PegaseStore store)
            => Api.RunAsync(http, [Permissions.PegaseCompteRendu], async (ctx, ct) => Results.Ok(await store.CreerCompteRenduAsync(ctx, id, input, ct))));
        pg.MapPut("/comptes-rendus/{id:long}", (HttpContext http, long id, CompteRenduInput input, PegaseStore store)
            => Api.RunAsync(http, [Permissions.PegaseCompteRendu], async (ctx, ct) => Results.Ok(await store.ModifierCompteRenduAsync(ctx, id, input, ct))));
        pg.MapPost("/comptes-rendus/{id:long}/valider", (HttpContext http, long id, PegaseStore store)
            => Api.RunAsync(http, [Permissions.PegaseCompteRendu, Permissions.PegaseCloturer],
                async (ctx, ct) => Results.Ok(await store.ValiderCompteRenduAsync(ctx, id, ct))));

        // ----- Équipages (statuts 7.2) -----
        pg.MapPost("/equipages/{id:long}/statut", (HttpContext http, long id, EquipageStatutInput input, PegaseStore store)
            => Api.RunAsync(http, [Permissions.PegaseStatut, Permissions.PegaseAffecter],
                async (ctx, ct) => Results.Ok(await store.StatutEquipageAsync(ctx, id, input, ct))));

        // ----- Communications, TN, statistiques, historique -----
        pg.MapGet("/communications", (HttpContext http, PegaseStore store, bool? messages, int? limit)
            => Api.RunAsync(http, PgTous, async (_, ct) =>
                Results.Ok(await store.CommunicationsAsync(messages ?? false, SocleRegles.Limite(limit, 200, 1000), ct))));
        pg.MapGet("/tn", (HttpContext http, PegaseStore store, int? limit)
            => Api.RunAsync(http, PgTous, async (_, ct) => Results.Ok(await store.ListerTnAsync(SocleRegles.Limite(limit, 30, 500), ct))));
        pg.MapPost("/tn/prendre", (HttpContext http, TnInput input, PegaseStore store)
            => Api.RunAsync(http, [Permissions.PegaseTn], async (ctx, ct) => Results.Ok(await store.PrendreTnAsync(ctx, input, ct))));
        pg.MapPost("/tn/{id:long}/terminer", (HttpContext http, long id, PegaseStore store)
            => Api.RunAsync(http, [Permissions.PegaseTn], async (ctx, ct) =>
            {
                await store.TerminerTnAsync(ctx, id, ct);
                return Results.NoContent();
            }));
        pg.MapGet("/statistiques", (HttpContext http, PegaseStore store, DateTimeOffset? du, DateTimeOffset? au)
            => Api.RunAsync(http, PgTous, async (_, ct) => Results.Ok(await store.StatistiquesAsync(du, au, ct))));
        pg.MapGet("/historique", (HttpContext http, PegaseStore store, string? reference, string? q, int? limit)
            => Api.RunAsync(http, PgTous, async (_, ct) => Results.Ok(await store.HistoriqueAsync(reference, q, SocleRegles.Limite(limit, 300, 1000), ct))));
    }

    public static void MapGeoops(this WebApplication app)
    {
        var geo = app.MapGroup("/api/geoops");

        // Image brute (navigateur, outils) ; le poste utilise /carte/donnees (même contenu en base64).
        // Deux paramètres : un délégué (HttpContext) seul serait pris pour un RequestDelegate et son résultat ignoré.
        geo.MapGet("/carte", (HttpContext http, GeoopsStore _)
            => Api.RunAsync(http, PgTous, (_, _) =>
            {
                var chemin = GeoopsStore.CheminCarte();
                var resultat = chemin is null
                    ? Api.Erreur(StatusCodes.Status404NotFound, "Aucune carte GÉOOPS : dépose data/geoops/carte.jpg à côté du serveur.")
                    : Results.File(chemin, GeoopsStore.TypeContenu(chemin), lastModified: File.GetLastWriteTimeUtc(chemin));
                return Task.FromResult(resultat);
            }));
        geo.MapGet("/carte/donnees", (HttpContext http, GeoopsStore store)
            => Api.RunAsync(http, PgTous, async (_, ct) => await store.CarteAsync(ct) is { } carte
                ? Results.Ok(carte)
                : Api.Erreur(StatusCodes.Status404NotFound, "Aucune carte GÉOOPS : dépose data/geoops/carte.jpg à côté du serveur.")));
        geo.MapGet("/calibration", (HttpContext http, GeoopsStore store)
            => Api.RunAsync(http, PgTous, async (_, ct) => Results.Ok(await store.CalibrationAsync(ct))));
        geo.MapGet("/situation", (HttpContext http, GeoopsStore store)
            => Api.RunAsync(http, PgTous, async (_, ct) => Results.Ok(await store.SituationAsync(ct))));
        geo.MapPost("/equipages/{id:long}/position", (HttpContext http, long id, PositionInput input, GeoopsStore store)
            => Api.RunAsync(http, [Permissions.PegaseAffecter], async (ctx, ct) =>
            {
                await store.DeclarerPositionAsync(ctx, id, input, ct);
                return Results.NoContent();
            }));
    }

    /// <summary>Intégrations serveur à serveur (clé SAPHIR:AccessKey dans l'en-tête X-Saphir-Key) : SIMCALL et script FiveM GÉOOPS.</summary>
    public static void MapIntegrationsOperations(this WebApplication app)
    {
        // SIMCALL 0.7.3 : décroché d'un 17 → prise d'appel PÉGASE ouverte sur le poste de l'opérateur (contrat de SAPHIR 0.1).
        app.MapPost("/api/integrations/simcall/answered", (HttpContext http, SimcallCallAnswered message, PegaseStore store)
            => IntegrationAsync(http, async ct =>
            {
                if (string.IsNullOrWhiteSpace(message.SimcallCallId) || string.IsNullOrWhiteSpace(message.DiscordUserId))
                    return Results.BadRequest(new { error = "Appel SIMCALL incomplet." });
                var id = await store.SimcallDecrocheAsync(message, ct);
                return Results.Ok(new { id });
            }));

        // SIMCALL : raccroché → heure de fin figée.
        app.MapPost("/api/integrations/simcall/ended", (HttpContext http, SimcallCallEnded message, PegaseStore store)
            => IntegrationAsync(http, async ct =>
            {
                await store.SimcallRaccrocheAsync(message, ct);
                return Results.NoContent();
            }));

        // Script FiveM saphir-geoops : positions des joueurs en service police, toutes les 5 secondes.
        app.MapPost("/api/integrations/geoops/positions", (HttpContext http, List<GeoopsPositionInput> positions, GeoopsStore store)
            => IntegrationAsync(http, async ct => Results.Ok(new { recues = positions.Count, enregistrees = await store.RecevoirPositionsAsync(positions, ct) })));
    }

    /// <summary>Clé d'intégration (comparaison à temps constant), base prête, erreurs métier et base injoignable.</summary>
    private static async Task<IResult> IntegrationAsync(HttpContext http, Func<CancellationToken, Task<IResult>> work)
    {
        var attendue = http.RequestServices.GetRequiredService<IConfiguration>()["SAPHIR:AccessKey"];
        var fournie = http.Request.Headers["X-Saphir-Key"].ToString();
        if (string.IsNullOrEmpty(attendue) || string.IsNullOrEmpty(fournie) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(fournie), Encoding.UTF8.GetBytes(attendue)))
            return Results.Unauthorized();

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
            http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("SAPHIR.Operations").LogError(ex, "Base SAPHIR (intégration)");
            return Api.Erreur(StatusCodes.Status503ServiceUnavailable, "Base SAPHIR injoignable pour le moment.");
        }
    }
}
