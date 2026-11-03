using System.Globalization;
using Saphir.Server.Data;
using Saphir.Server.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Judiciaire;
using P = Saphir.Shared.Contracts.Permissions;

namespace Saphir.Server.Modules.Judiciaire;

/// <summary>Routes du module Judiciaire : personnes/véhicules partagés, CHEOPS et LRPPN. Contrat : docs/API-JUDICIAIRE.md.</summary>
public static class JudiciaireEndpoints
{
    private static string Id(long id) => id.ToString(CultureInfo.InvariantCulture);

    /// <summary>Consultation sensible écrite au journal des accès (22).</summary>
    private static Task JournaliserAsync(HttpContext http, AgentContext ctx, string action, string cible)
        => http.RequestServices.GetRequiredService<JournalAcces>()
            .EcrireAsync(ctx.Agent.Id, SocleRegles.LibelleAgent(ctx.Agent), action, cible, "Autorisé");

    private static Task CheopsModifieAsync(RealtimeHub hub, long id)
        => hub.BroadcastAsync(new RealtimeMessage(JudiciaireRealtime.CheopsChanged, Text: Id(id), AppId: SaphirApps.Cheops), CancellationToken.None);

    private static Task LrppnModifieAsync(RealtimeHub hub, long procedureId)
        => hub.BroadcastAsync(new RealtimeMessage(JudiciaireRealtime.LrppnChanged, Text: Id(procedureId), AppId: SaphirApps.Lrppn), CancellationToken.None);

    /// <summary>Notification au rédacteur d'un acte (numéro d'acte seulement, aucune donnée personnelle).</summary>
    private static async Task PrevenirRedacteurAsync(HttpContext http, long? redacteurId, string texte)
    {
        if (redacteurId is null)
            return;
        try
        {
            await using var db = await http.RequestServices.GetRequiredService<SaphirDb>().OpenAsync(CancellationToken.None);
            if (await db.ScalarAsync("SELECT discord_id FROM rh_agents WHERE id = @id", CancellationToken.None, ("@id", redacteurId.Value)) is string discordId)
                await http.RequestServices.GetRequiredService<RealtimeHub>().SendToUserAsync(discordId,
                    new RealtimeMessage(RealtimeTypes.Notify, Title: "LRPPN", Text: texte, AppId: SaphirApps.Lrppn), CancellationToken.None);
        }
        catch (Exception ex)
        {
            http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("SAPHIR.Judiciaire").LogWarning(ex, "Notification du rédacteur impossible");
        }
    }

    private static IResult Fichier(byte[] contenu, string mime, string nom, string? format)
        => string.Equals(format, "base64", StringComparison.OrdinalIgnoreCase)
            ? Results.Ok(new FichierDto(nom, mime, Convert.ToBase64String(contenu)))
            : Results.File(contenu, mime, nom);

    private static DateOnly? Jour(string? texte)
        => string.IsNullOrWhiteSpace(texte) ? null : JudiciaireRegles.LireDate(texte) ?? throw ApiException.Saisie("Date attendue au format jj/mm/aaaa.");

    private static IResult Refus(string message) => Api.Erreur(StatusCodes.Status403Forbidden, message);

    public static void Map(WebApplication app)
    {
        MapPartage(app);
        MapCheops(app);
        MapLrppn(app);
    }

    // =====================================================================================
    // Routes partagées (Referentiels.cs)
    // =====================================================================================

    private static void MapPartage(WebApplication app)
    {
        app.MapGet("/api/personnes/recherche", (HttpContext http, PersonnesStore store, string? q)
            => Api.RunAsync(http, JudiciaireAcces.Partage, async (ctx, ct) => Results.Ok(await store.RechercherAsync(q, ctx.Droits, ct))));

        app.MapGet("/api/personnes/{id:long}/resume", (HttpContext http, PersonnesStore store, long id)
            => Api.RunAsync(http, JudiciaireAcces.Partage, async (ctx, ct) => Results.Ok(await store.ResumeAsync(id, ctx.Droits, ct))));

        app.MapPost("/api/personnes", (HttpContext http, PersonnesStore store, RealtimeHub hub, PersonneCreationInput input)
            => Api.RunAsync(http, JudiciaireAcces.Partage, async (ctx, ct) =>
            {
                var personne = await store.CreerAsync(ctx.Auteur, input, ctx.Droits, ct);
                await CheopsModifieAsync(hub, personne.Id);
                return Results.Ok(personne);
            }));

        app.MapGet("/api/vehicules/recherche", (HttpContext http, PersonnesStore store, string? q)
            => Api.RunAsync(http, JudiciaireAcces.Partage, async (ctx, ct) => Results.Ok(await store.RechercherVehiculesAsync(q, ctx.Droits, ct))));

        app.MapGet("/api/vehicules/{id:long}/resume", (HttpContext http, PersonnesStore store, long id)
            => Api.RunAsync(http, JudiciaireAcces.Partage, async (ctx, ct) => Results.Ok(await store.VehiculeResumeAsync(id, ctx.Droits, ct))));
    }

    // =====================================================================================
    // CHEOPS
    // =====================================================================================

    private static void MapCheops(WebApplication app)
    {
        var g = app.MapGroup("/api/cheops");

        // ----- Recherche générale et identités -----

        g.MapGet("/recherche", (HttpContext http, CheopsStore store, string? nom, string? prenom, string? alias, string? naissance, string? lieu,
                string? identifiant, string? dossier, string? immatriculation)
            => Api.RunAsync(http, JudiciaireAcces.Cheops, async (ctx, ct) =>
            {
                var resultat = await store.RechercheGeneraleAsync(nom, prenom, alias, naissance, lieu, identifiant, dossier, immatriculation, ctx.Droits, ct);
                await JournaliserAsync(http, ctx, "Recherche générale CHEOPS",
                    string.Join(" ", new[] { nom, prenom, alias, naissance, identifiant, dossier, immatriculation }.Where(s => !string.IsNullOrWhiteSpace(s))));
                return Results.Ok(resultat);
            }));

        g.MapGet("/personnes/{id:long}", (HttpContext http, PersonnesStore store, long id)
            => Api.RunAsync(http, JudiciaireAcces.FichePersonne, async (ctx, ct) =>
            {
                var fiche = await store.FicheAsync(id, ctx.Droits, ct);
                await JournaliserAsync(http, ctx, "Consultation fiche d'identité", fiche.Reference);
                return Results.Ok(fiche);
            }));

        g.MapPut("/personnes/{id:long}", (HttpContext http, PersonnesStore store, RealtimeHub hub, long id, PersonneModificationInput input)
            => Api.RunAsync(http, JudiciaireAcces.EcritureReferentiel, async (ctx, ct) =>
            {
                var fiche = await store.ModifierAsync(ctx.Auteur, id, input, ctx.Droits, ct);
                await CheopsModifieAsync(hub, id);
                return Results.Ok(fiche);
            }));

        g.MapPost("/personnes/{id:long}/documents", (HttpContext http, PersonnesStore store, RealtimeHub hub, long id, DocumentIdentiteInput input)
            => Api.RunAsync(http, JudiciaireAcces.EcritureReferentiel, async (ctx, ct) =>
            {
                await store.AjouterDocumentAsync(ctx.Auteur, id, input, ct);
                await CheopsModifieAsync(hub, id);
                return Results.Ok(await store.FicheAsync(id, ctx.Droits, ct));
            }));

        // ----- TAJ -----

        g.MapGet("/taj", (HttpContext http, CheopsStore store, string? q, string? naissance, string? reference, string? procedure)
            => Api.RunAsync(http, [P.CheopsTaj], async (ctx, ct) =>
            {
                var resultat = await store.RechercherTajAsync(q, naissance, reference, procedure, ct);
                await JournaliserAsync(http, ctx, "Recherche TAJ", string.Join(" ", new[] { q, naissance, reference, procedure }.Where(s => !string.IsNullOrWhiteSpace(s))));
                return Results.Ok(resultat);
            }));

        g.MapGet("/taj/personnes/{id:long}", (HttpContext http, CheopsStore store, long id)
            => Api.RunAsync(http, [P.CheopsTaj], async (ctx, ct) =>
            {
                var fiche = await store.TajFicheAsync(id, ctx.Droits, ct);
                await JournaliserAsync(http, ctx, "Consultation TAJ", fiche.Personne.Reference);
                return Results.Ok(fiche);
            }));

        g.MapPost("/taj", (HttpContext http, CheopsStore store, RealtimeHub hub, TajFaitInput input)
            => Api.RunAsync(http, [P.LrppnValider], async (ctx, ct) =>
            {
                if (!ctx.Droits.Possede(P.CheopsTaj))
                    return Refus("Accès refusé : consultation du TAJ requise.");
                var reference = await store.AjouterTajAsync(ctx.Auteur, input, ct);
                await CheopsModifieAsync(hub, input.PersonneId);
                return Results.Ok(new { reference });
            }));

        g.MapPut("/taj/{id:long}/suite", (HttpContext http, CheopsStore store, RealtimeHub hub, long id, TajSuiteInput input)
            => Api.RunAsync(http, [P.LrppnValider], async (ctx, ct) =>
            {
                if (!ctx.Droits.Possede(P.CheopsTaj))
                    return Refus("Accès refusé : consultation du TAJ requise.");
                await store.ModifierSuiteTajAsync(ctx.Auteur, id, input, ct);
                await CheopsModifieAsync(hub, id);
                return Results.NoContent();
            }));

        // ----- FPR -----

        g.MapGet("/fpr", (HttpContext http, CheopsStore store, string? q, string? statut)
            => Api.RunAsync(http, [P.CheopsFpr], async (ctx, ct) =>
            {
                var resultat = await store.RechercherFprAsync(q, statut, ct);
                if (!string.IsNullOrWhiteSpace(q))
                    await JournaliserAsync(http, ctx, "Recherche FPR", q);
                return Results.Ok(resultat);
            }));

        g.MapGet("/fpr/{id:long}", (HttpContext http, CheopsStore store, long id)
            => Api.RunAsync(http, [P.CheopsFpr], async (ctx, ct) =>
            {
                var fiche = await store.FprAsync(id, ct);
                await JournaliserAsync(http, ctx, "Consultation FPR", fiche.Numero);
                return Results.Ok(fiche);
            }));

        g.MapPost("/fpr", (HttpContext http, CheopsStore store, RealtimeHub hub, FprInput input)
            => Api.RunAsync(http, [P.CheopsFprInscrire], async (ctx, ct) =>
            {
                var fiche = await store.InscrireFprAsync(ctx.Auteur, input, ct);
                await CheopsModifieAsync(hub, fiche.Id);
                return Results.Ok(fiche);
            }));

        g.MapPut("/fpr/{id:long}/statut", (HttpContext http, CheopsStore store, RealtimeHub hub, long id, StatutInput input)
            => Api.RunAsync(http, [P.CheopsFprInscrire], async (ctx, ct) =>
            {
                var fiche = await store.ChangerStatutFprAsync(ctx.Auteur, id, input, ct);
                await CheopsModifieAsync(hub, id);
                return Results.Ok(fiche);
            }));

        // ----- FOVeS -----

        g.MapGet("/foves", (HttpContext http, CheopsStore store, string? q, string? statut)
            => Api.RunAsync(http, [P.CheopsVehicules], async (_, ct) => Results.Ok(await store.RechercherFovesAsync(q, statut, ct))));

        g.MapGet("/foves/{id:long}", (HttpContext http, CheopsStore store, long id)
            => Api.RunAsync(http, [P.CheopsVehicules], async (ctx, ct) =>
            {
                var fiche = await store.FovesAsync(id, ct);
                await JournaliserAsync(http, ctx, "Consultation FOVeS", fiche.Reference);
                return Results.Ok(fiche);
            }));

        g.MapPost("/foves", (HttpContext http, CheopsStore store, RealtimeHub hub, FovesInput input)
            => Api.RunAsync(http, [P.CheopsFprInscrire, P.LrppnRediger], async (ctx, ct) =>
            {
                var fiche = await store.SignalerFovesAsync(ctx.Auteur, input, ct);
                await CheopsModifieAsync(hub, fiche.Id);
                return Results.Ok(fiche);
            }));

        g.MapPut("/foves/{id:long}/statut", (HttpContext http, CheopsStore store, RealtimeHub hub, long id, FovesStatutInput input)
            => Api.RunAsync(http, [P.CheopsFprInscrire, P.LrppnRediger], async (ctx, ct) =>
            {
                var fiche = await store.ChangerStatutFovesAsync(ctx.Auteur, id, input, ct);
                await CheopsModifieAsync(hub, id);
                return Results.Ok(fiche);
            }));

        // ----- SIV -----

        g.MapGet("/siv", (HttpContext http, PersonnesStore store, string? q)
            => Api.RunAsync(http, [P.CheopsVehicules], async (ctx, ct) =>
            {
                var resultat = await store.RechercherVehiculesAsync(q, ctx.Droits, ct);
                await JournaliserAsync(http, ctx, "Recherche SIV", q ?? string.Empty);
                return Results.Ok(resultat);
            }));

        g.MapGet("/siv/{id:long}", (HttpContext http, PersonnesStore store, long id)
            => Api.RunAsync(http, [P.CheopsVehicules], async (ctx, ct) =>
            {
                var fiche = await store.VehiculeFicheAsync(id, ctx.Droits, ct);
                await JournaliserAsync(http, ctx, "Consultation SIV", fiche.Immatriculation);
                return Results.Ok(fiche);
            }));

        g.MapPost("/siv", (HttpContext http, PersonnesStore store, RealtimeHub hub, VehiculeInput input)
            => Api.RunAsync(http, JudiciaireAcces.EcritureReferentiel, async (ctx, ct) =>
            {
                var id = await store.EnregistrerVehiculeAsync(ctx.Auteur, null, input, ct);
                await CheopsModifieAsync(hub, id);
                return Results.Ok(await store.VehiculeFicheAsync(id, ctx.Droits, ct));
            }));

        g.MapPut("/siv/{id:long}", (HttpContext http, PersonnesStore store, RealtimeHub hub, long id, VehiculeInput input)
            => Api.RunAsync(http, JudiciaireAcces.EcritureReferentiel, async (ctx, ct) =>
            {
                await store.EnregistrerVehiculeAsync(ctx.Auteur, id, input, ct);
                await CheopsModifieAsync(hub, id);
                return Results.Ok(await store.VehiculeFicheAsync(id, ctx.Droits, ct));
            }));

        // ----- FVA -----

        g.MapGet("/fva", (HttpContext http, CheopsStore store, string? q)
            => Api.RunAsync(http, [P.CheopsVehicules], async (ctx, ct) =>
            {
                var resultat = await store.RechercherFvaAsync(q, ctx.Droits, ct);
                await JournaliserAsync(http, ctx, "Recherche FVA", q ?? string.Empty);
                return Results.Ok(resultat);
            }));

        g.MapPost("/fva", (HttpContext http, CheopsStore store, RealtimeHub hub, AssuranceInput input)
            => Api.RunAsync(http, JudiciaireAcces.EcritureReferentiel, async (ctx, ct) =>
            {
                await store.AjouterAssuranceAsync(ctx.Auteur, input, ct);
                await CheopsModifieAsync(hub, input.VehiculeId);
                return Results.NoContent();
            }));

        g.MapPost("/fva/{id:long}/verifier", (HttpContext http, CheopsStore store, long id)
            => Api.RunAsync(http, [P.CheopsVehicules], async (ctx, ct) =>
            {
                await store.VerifierAssuranceAsync(ctx.Auteur, id, ct);
                return Results.NoContent();
            }));

        // ----- SNPC -----

        g.MapGet("/snpc", (HttpContext http, CheopsStore store, string? q)
            => Api.RunAsync(http, [P.CheopsSnpc], async (ctx, ct) =>
            {
                var resultat = await store.RechercherPermisAsync(q, ct);
                await JournaliserAsync(http, ctx, "Recherche SNPC", q ?? string.Empty);
                return Results.Ok(resultat);
            }));

        g.MapGet("/snpc/personnes/{id:long}", (HttpContext http, CheopsStore store, long id)
            => Api.RunAsync(http, [P.CheopsSnpc], async (ctx, ct) =>
            {
                var permis = await store.PermisAsync(id, ct);
                await JournaliserAsync(http, ctx, "Consultation SNPC", permis.Personne.Reference);
                return Results.Ok(permis);
            }));

        g.MapPut("/snpc/personnes/{id:long}", (HttpContext http, CheopsStore store, RealtimeHub hub, long id, PermisInput input)
            => Api.RunAsync(http, JudiciaireAcces.EcritureReferentiel, async (ctx, ct) =>
            {
                var permis = await store.EnregistrerPermisAsync(ctx.Auteur, id, input, ct);
                await CheopsModifieAsync(hub, id);
                return Results.Ok(permis);
            }));

        g.MapPost("/snpc/personnes/{id:long}/points", (HttpContext http, CheopsStore store, RealtimeHub hub, long id, PointsInput input)
            => Api.RunAsync(http, JudiciaireAcces.EcritureReferentiel, async (ctx, ct) =>
            {
                var permis = await store.ModifierPointsAsync(ctx.Auteur, id, input, ct);
                await CheopsModifieAsync(hub, id);
                return Results.Ok(permis);
            }));

        // ----- FAED / FNAEG -----

        g.MapGet("/scientifique", (HttpContext http, CheopsStore store, string? fichier, string? q)
            => Api.RunAsync(http, [P.CheopsScientifique], async (ctx, ct) =>
            {
                var resultat = await store.RechercherScientifiqueAsync(fichier ?? FichiersCheops.Faed, q, ct);
                await JournaliserAsync(http, ctx, "Recherche " + (fichier ?? FichiersCheops.Faed), q ?? string.Empty);
                return Results.Ok(resultat);
            }));

        g.MapGet("/scientifique/{id:long}", (HttpContext http, CheopsStore store, long id)
            => Api.RunAsync(http, [P.CheopsScientifique], async (ctx, ct) =>
            {
                var fiche = await store.ScientifiqueAsync(id, ct);
                await JournaliserAsync(http, ctx, "Consultation " + fiche.Fichier, fiche.Reference);
                return Results.Ok(fiche);
            }));

        g.MapPost("/scientifique", (HttpContext http, CheopsStore store, RealtimeHub hub, ScientifiqueInput input)
            => Api.RunAsync(http, [P.CheopsScientifique], async (ctx, ct) =>
            {
                var fiche = await store.CreerScientifiqueAsync(ctx.Auteur, input, ct);
                await CheopsModifieAsync(hub, fiche.Id);
                return Results.Ok(fiche);
            }));

        g.MapPut("/scientifique/{id:long}/resultat", (HttpContext http, CheopsStore store, RealtimeHub hub, long id, ResultatInput input)
            => Api.RunAsync(http, [P.CheopsScientifique], async (ctx, ct) =>
            {
                var fiche = await store.ResultatScientifiqueAsync(ctx.Auteur, id, input, ct);
                await CheopsModifieAsync(hub, id);
                return Results.Ok(fiche);
            }));

        // ----- FIJAISV / FIJAIT (consultations toutes journalisées ; inscription manuelle par un OPJ habilité) -----

        g.MapGet("/fijais", (HttpContext http, CheopsStore store, string? fichier, string? q)
            => Api.RunAsync(http, [P.CheopsFijais], async (ctx, ct) =>
            {
                var resultat = await store.RechercherFijaisAsync(fichier ?? FichiersCheops.Fijaisv, q, ct);
                await JournaliserAsync(http, ctx, "Recherche " + (fichier ?? FichiersCheops.Fijaisv), q ?? string.Empty);
                return Results.Ok(resultat);
            }));

        g.MapGet("/fijais/{id:long}", (HttpContext http, CheopsStore store, long id)
            => Api.RunAsync(http, [P.CheopsFijais], async (ctx, ct) =>
            {
                var fiche = await store.FijaisAsync(id, ct);
                await JournaliserAsync(http, ctx, "Consultation " + fiche.Fichier, fiche.Reference);
                return Results.Ok(fiche);
            }));

        g.MapPost("/fijais", (HttpContext http, CheopsStore store, RealtimeHub hub, FijaisInput input)
            => Api.RunAsync(http, [P.CheopsFijais], async (ctx, ct) =>
            {
                if (!ctx.Droits.Possede(P.LrppnValider))
                    return Refus("Accès refusé : l'inscription est réservée aux OPJ habilités (validation LRPPN).");
                var fiche = await store.InscrireFijaisAsync(ctx.Auteur, input, ct);
                await CheopsModifieAsync(hub, fiche.Id);
                return Results.Ok(fiche);
            }));

        g.MapPut("/fijais/{id:long}/statut", (HttpContext http, CheopsStore store, RealtimeHub hub, long id, StatutInput input)
            => Api.RunAsync(http, [P.CheopsFijais], async (ctx, ct) =>
            {
                if (!ctx.Droits.Possede(P.LrppnValider))
                    return Refus("Accès refusé : modification réservée aux OPJ habilités (validation LRPPN).");
                var fiche = await store.ChangerStatutFijaisAsync(ctx.Auteur, id, input, ct);
                await CheopsModifieAsync(hub, id);
                return Results.Ok(fiche);
            }));

        // ----- Casier de simulation -----

        g.MapGet("/casier/{personneId:long}", (HttpContext http, CheopsStore store, long personneId)
            => Api.RunAsync(http, JudiciaireAcces.CasierLecture, async (ctx, ct) =>
            {
                var decisions = await store.DecisionsAsync(personneId, ct);
                await JournaliserAsync(http, ctx, "Consultation casier de simulation", "personne " + Id(personneId));
                return Results.Ok(decisions);
            }));

        g.MapPost("/casier", (HttpContext http, CheopsStore store, RealtimeHub hub, DecisionInput input)
            => Api.RunAsync(http, [P.LrppnValider], async (ctx, ct) =>
            {
                var decision = await store.AjouterDecisionAsync(ctx.Auteur, input, ct);
                await CheopsModifieAsync(hub, input.PersonneId);
                return Results.Ok(decision);
            }));

        // ----- ANADOC -----

        g.MapGet("/anadoc", (HttpContext http, CheopsStore store, string? procedure, string? pv, string? type, string? du, string? au,
                string? service, string? redacteur, string? personne, string? motsCles)
            => Api.RunAsync(http, [P.CheopsAnadoc], async (ctx, ct) =>
            {
                var resultat = await store.RechercherAnadocAsync(procedure, pv, type, Jour(du), Jour(au), service, redacteur, personne, motsCles, ct);
                await JournaliserAsync(http, ctx, "Recherche ANADOC",
                    string.Join(" ", new[] { procedure, pv, type, personne, motsCles }.Where(s => !string.IsNullOrWhiteSpace(s))));
                return Results.Ok(resultat);
            }));

        g.MapGet("/anadoc/{id:long}", (HttpContext http, CheopsStore store, long id)
            => Api.RunAsync(http, [P.CheopsAnadoc], async (ctx, ct) =>
            {
                var document = await store.AnadocDocumentAsync(id, ct);
                await JournaliserAsync(http, ctx, "Consultation ANADOC", document.Resultat.NumeroPv ?? document.Resultat.ProcedureNumero);
                return Results.Ok(document);
            }));
    }

    // =====================================================================================
    // LRPPN
    // =====================================================================================

    private static void MapLrppn(WebApplication app)
    {
        var g = app.MapGroup("/api/lrppn");
        var lecture = JudiciaireAcces.LrppnLecture;

        // ----- Procédures -----

        g.MapGet("/procedures", (HttpContext http, LrppnStore store, string? q, string? statut, bool? miennes, bool? archives, long? interventionId)
            => Api.RunAsync(http, lecture, async (ctx, ct) =>
                Results.Ok(await store.ListerAsync(q, statut, miennes == true ? ctx.Agent.Id : null, archives == true, interventionId, ct))));

        g.MapPost("/procedures", (HttpContext http, LrppnStore store, RealtimeHub hub, ProcedureInput input)
            => Api.RunAsync(http, [P.LrppnRediger], async (ctx, ct) =>
            {
                var procedure = await store.CreerAsync(ctx, input, ct);
                await LrppnModifieAsync(hub, procedure.Id);
                return Results.Ok(procedure);
            }));

        g.MapGet("/procedures/{id:long}", (HttpContext http, LrppnStore store, long id)
            => Api.RunAsync(http, lecture, async (ctx, ct) => Results.Ok(await store.ProcedureAsync(id, ct, ctx.Droits))));

        g.MapPut("/procedures/{id:long}", (HttpContext http, LrppnStore store, RealtimeHub hub, long id, ProcedureInput input)
            => Api.RunAsync(http, [P.LrppnRediger], async (ctx, ct) =>
            {
                var procedure = await store.ModifierAsync(ctx.Auteur, id, input, ct);
                await LrppnModifieAsync(hub, id);
                return Results.Ok(procedure);
            }));

        g.MapPut("/procedures/{id:long}/statut", (HttpContext http, LrppnStore store, RealtimeHub hub, long id, StatutInput input)
            => Api.RunAsync(http, [P.LrppnValider], async (ctx, ct) =>
            {
                var procedure = await store.ChangerStatutAsync(ctx.Auteur, id, input, ct);
                await LrppnModifieAsync(hub, id);
                return Results.Ok(procedure);
            }));

        g.MapPost("/procedures/{id:long}/personnes", (HttpContext http, LrppnStore store, RealtimeHub hub, long id, ProcedurePersonneInput input)
            => Api.RunAsync(http, [P.LrppnRediger], async (ctx, ct) =>
            {
                await store.AjouterPersonneAsync(ctx.Auteur, id, input, ct);
                await LrppnModifieAsync(hub, id);
                return Results.Ok(await store.ProcedureAsync(id, ct, ctx.Droits));
            }));

        g.MapDelete("/procedures/{id:long}/personnes/{lienId:long}", (HttpContext http, LrppnStore store, RealtimeHub hub, long id, long lienId, string? motif)
            => Api.RunAsync(http, [P.LrppnRediger], async (ctx, ct) =>
            {
                await store.RetirerPersonneAsync(ctx.Auteur, id, lienId, motif, ct);
                await LrppnModifieAsync(hub, id);
                return Results.Ok(await store.ProcedureAsync(id, ct, ctx.Droits));
            }));

        g.MapPost("/procedures/{id:long}/vehicules", (HttpContext http, LrppnStore store, RealtimeHub hub, long id, ProcedureVehiculeInput input)
            => Api.RunAsync(http, [P.LrppnRediger], async (ctx, ct) =>
            {
                await store.AjouterVehiculeAsync(ctx.Auteur, id, input, ct);
                await LrppnModifieAsync(hub, id);
                return Results.Ok(await store.ProcedureAsync(id, ct, ctx.Droits));
            }));

        g.MapDelete("/procedures/{id:long}/vehicules/{lienId:long}", (HttpContext http, LrppnStore store, RealtimeHub hub, long id, long lienId, string? motif)
            => Api.RunAsync(http, [P.LrppnRediger], async (ctx, ct) =>
            {
                await store.RetirerVehiculeAsync(ctx.Auteur, id, lienId, motif, ct);
                await LrppnModifieAsync(hub, id);
                return Results.Ok(await store.ProcedureAsync(id, ct, ctx.Droits));
            }));

        g.MapPost("/procedures/{id:long}/liens", (HttpContext http, LrppnStore store, RealtimeHub hub, long id, ProcedureLienInput input)
            => Api.RunAsync(http, [P.LrppnRediger], async (ctx, ct) =>
            {
                await store.LierAsync(ctx.Auteur, id, input, ct);
                await LrppnModifieAsync(hub, id);
                return Results.Ok(await store.ProcedureAsync(id, ct, ctx.Droits));
            }));

        g.MapDelete("/procedures/{id:long}/liens/{lienId:long}", (HttpContext http, LrppnStore store, RealtimeHub hub, long id, long lienId)
            => Api.RunAsync(http, [P.LrppnRediger], async (ctx, ct) =>
            {
                await store.DelierAsync(ctx.Auteur, id, lienId, ct);
                await LrppnModifieAsync(hub, id);
                return Results.Ok(await store.ProcedureAsync(id, ct, ctx.Droits));
            }));

        g.MapGet("/procedures/{id:long}/historique", (HttpContext http, LrppnStore store, long id)
            => Api.RunAsync(http, lecture, async (_, ct) => Results.Ok(await store.HistoriqueProcedureAsync(id, ct))));

        g.MapGet("/procedures/{id:long}/pdf", (HttpContext http, ActesStore store, long id, string? format)
            => Api.RunAsync(http, lecture, async (ctx, ct) =>
            {
                var (contenu, nom) = await store.ProcedurePdfAsync(id, ct);
                await JournaliserAsync(http, ctx, "Export PDF de procédure", nom);
                return Fichier(contenu, "application/pdf", nom, format);
            }));

        // ----- Actes -----

        g.MapPost("/procedures/{id:long}/actes", (HttpContext http, ActesStore store, RealtimeHub hub, long id, ActeCreationInput input)
            => Api.RunAsync(http, [P.LrppnRediger], async (ctx, ct) =>
            {
                var acte = await store.CreerAsync(ctx, id, input, ct);
                await LrppnModifieAsync(hub, id);
                return Results.Ok(acte);
            }));

        g.MapGet("/actes", (HttpContext http, ActesStore store, string? statut, bool? miens, string? q)
            => Api.RunAsync(http, lecture, async (ctx, ct) => Results.Ok(await store.ListerAsync(statut, miens == true ? ctx.Agent.Id : null, q, ct))));

        g.MapGet("/actes/{id:long}", (HttpContext http, ActesStore store, long id)
            => Api.RunAsync(http, lecture, async (_, ct) => Results.Ok(await store.ActeAsync(id, ct))));

        g.MapPut("/actes/{id:long}", (HttpContext http, ActesStore store, RealtimeHub hub, long id, ActeInput input)
            => Api.RunAsync(http, [P.LrppnRediger], async (ctx, ct) =>
            {
                var acte = await store.EnregistrerAsync(ctx, id, input, ct);
                await LrppnModifieAsync(hub, acte.ProcedureId);
                return Results.Ok(acte);
            }));

        g.MapDelete("/actes/{id:long}", (HttpContext http, ActesStore store, RealtimeHub hub, long id)
            => Api.RunAsync(http, [P.LrppnRediger], async (ctx, ct) =>
            {
                var procedureId = (await store.ActeAsync(id, ct)).ProcedureId;
                await store.SupprimerBrouillonAsync(ctx, id, ct);
                await LrppnModifieAsync(hub, procedureId);
                return Results.NoContent();
            }));

        g.MapPost("/actes/{id:long}/relecture", (HttpContext http, ActesStore store, RealtimeHub hub, long id)
            => Api.RunAsync(http, [P.LrppnRediger], async (ctx, ct) =>
            {
                var acte = await store.DemanderRelectureAsync(ctx, id, ct);
                await LrppnModifieAsync(hub, acte.ProcedureId);
                return Results.Ok(acte);
            }));

        g.MapPost("/actes/{id:long}/correction", (HttpContext http, ActesStore store, RealtimeHub hub, long id, MotifInput input)
            => Api.RunAsync(http, [P.LrppnValider], async (ctx, ct) =>
            {
                var acte = await store.CorrigerAsync(ctx, id, input.Motif, ct);
                await LrppnModifieAsync(hub, acte.ProcedureId);
                await PrevenirRedacteurAsync(http, acte.RedacteurId, $"Acte {acte.Numero} renvoyé pour correction.");
                return Results.Ok(acte);
            }));

        g.MapPost("/actes/{id:long}/valider", (HttpContext http, ActesStore store, RealtimeHub hub, long id)
            => Api.RunAsync(http, [P.LrppnValider], async (ctx, ct) =>
            {
                var (acte, taj) = await store.ValiderAsync(ctx, id, ct);
                await LrppnModifieAsync(hub, acte.ProcedureId);
                if (taj is not null && acte.PersonneId is { } personneId)
                    await CheopsModifieAsync(hub, personneId);
                if (acte.RedacteurId != ctx.Agent.Id)
                    await PrevenirRedacteurAsync(http, acte.RedacteurId, $"Acte {acte.Numero} validé.");
                return Results.Ok(acte);
            }));

        g.MapPost("/actes/{id:long}/mentions", (HttpContext http, ActesStore store, RealtimeHub hub, long id, MentionInput input)
            => Api.RunAsync(http, [P.LrppnRediger, P.LrppnValider], async (ctx, ct) =>
            {
                var acte = await store.AjouterMentionAsync(ctx, id, input, ct);
                await LrppnModifieAsync(hub, acte.ProcedureId);
                return Results.Ok(acte);
            }));

        g.MapGet("/actes/{id:long}/apercu", (HttpContext http, ActesStore store, long id)
            => Api.RunAsync(http, lecture, async (_, ct) => Results.Ok(await store.ApercuAsync(id, ct))));

        g.MapGet("/actes/{id:long}/pdf", (HttpContext http, ActesStore store, long id, string? format)
            => Api.RunAsync(http, lecture, async (ctx, ct) =>
            {
                var (contenu, nom) = await store.PdfAsync(id, ct);
                await JournaliserAsync(http, ctx, "Export PDF d'acte", nom);
                return Fichier(contenu, "application/pdf", nom, format);
            }));

        g.MapGet("/actes/{id:long}/historique", (HttpContext http, ActesStore store, long id)
            => Api.RunAsync(http, lecture, async (_, ct) => Results.Ok(await store.HistoriqueAsync(id, ct))));

        // ----- Pièces et scellés -----

        g.MapPost("/procedures/{id:long}/pieces", (HttpContext http, LrppnStore store, RealtimeHub hub, long id, PieceInput input)
            => Api.RunAsync(http, [P.LrppnPieces], async (ctx, ct) =>
            {
                var piece = await store.AjouterPieceAsync(ctx.Auteur, id, input, ct);
                await LrppnModifieAsync(hub, id);
                return Results.Ok(piece);
            }));

        g.MapGet("/pieces", (HttpContext http, LrppnStore store, string? q, string? categorie)
            => Api.RunAsync(http, [.. lecture, P.LrppnPieces], async (_, ct) => Results.Ok(await store.RechercherPiecesAsync(q, categorie, ct))));

        g.MapGet("/pieces/{id:long}/fichier", (HttpContext http, LrppnStore store, long id, string? format)
            => Api.RunAsync(http, [.. lecture, P.LrppnPieces], async (ctx, ct) =>
            {
                var (contenu, mime, nom, numero) = await store.FichierPieceAsync(id, ct);
                await JournaliserAsync(http, ctx, "Téléchargement de pièce", numero);
                return Fichier(contenu, mime, nom, format);
            }));

        g.MapPost("/pieces/{id:long}/mouvements", (HttpContext http, LrppnStore store, RealtimeHub hub, long id, MouvementInput input)
            => Api.RunAsync(http, [P.LrppnPieces], async (ctx, ct) =>
            {
                var piece = await store.MouvementAsync(ctx.Auteur, id, input, ct);
                await LrppnModifieAsync(hub, piece.ProcedureId);
                return Results.Ok(piece);
            }));

        // ----- Personnes (14.2, 14.3), historique, modèles -----

        g.MapGet("/personnes/{id:long}/dossiers", (HttpContext http, LrppnStore store, long id)
            => Api.RunAsync(http, [.. lecture, .. JudiciaireAcces.Cheops], async (ctx, ct) =>
            {
                var dossiers = await store.DossiersAsync(id, ctx.Droits, ct);
                await JournaliserAsync(http, ctx, "Consultation des dossiers associés", dossiers.Personne.Reference);
                return Results.Ok(dossiers);
            }));

        g.MapGet("/historique", (HttpContext http, LrppnStore store, int? limit)
            => Api.RunAsync(http, lecture, async (_, ct) => Results.Ok(await store.HistoriqueGeneralAsync(SocleRegles.Limite(limit), ct))));

        g.MapGet("/modeles", (HttpContext http, LrppnStore store)
            => Api.RunAsync(http, lecture, async (_, ct) => Results.Ok(await store.ModelesAsync(ct))));

        g.MapPut("/modeles/{type}", (HttpContext http, LrppnStore store, string type, ModeleInput input)
            => Api.RunAsync(http, [P.LrppnValider], async (ctx, ct) => Results.Ok(await store.ModifierModeleAsync(ctx.Auteur, type, input, ct))));
    }
}
