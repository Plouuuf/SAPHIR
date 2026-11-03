using Saphir.Server.Data;
using Saphir.Server.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Rh;

namespace Saphir.Server.Modules.Rh;

/// <summary>
/// Routes du module RH (contrat : docs/API-RH.md).
/// /api/referentiels/** : toute session d'agent. /api/rh/** : rôle Discord RH obligatoire + permission rh.*.
/// </summary>
public static class RhEndpoints
{
    private static readonly string[] Consulter = [Permissions.RhConsulter];
    private static readonly string[] LectureDocuments = [Permissions.RhConsulter, Permissions.RhDocuments, Permissions.RhDocumentsValider];

    public static void Map(WebApplication app)
    {
        MapReferentielsPublics(app);

        var rh = app.MapGroup("/api/rh");
        MapPersonnel(rh);
        MapReferentiels(rh);
        MapDocuments(rh);
    }

    // =====================================================================================
    // Référentiels publics (tous les modules)
    // =====================================================================================

    private static void MapReferentielsPublics(WebApplication app)
    {
        app.MapGet("/api/referentiels/agents", (HttpContext http, RhStore store, string? q, bool? tous)
            => Api.RunAsync(http, Array.Empty<string>(), async (_, ct) => Results.Ok(await store.ReferentielAgentsAsync(q, tous ?? false, ct))));

        foreach (var type in RhReferentielTypes.All)
        {
            var t = type;
            app.MapGet("/api/referentiels/" + t, (HttpContext http, RhStore store, bool? tous)
                => Api.RunAsync(http, Array.Empty<string>(), async (_, ct) => Results.Ok(await store.ReferentielPublicAsync(t, tous ?? false, ct))));
        }
    }

    // =====================================================================================
    // Personnel
    // =====================================================================================

    private static void MapPersonnel(RouteGroupBuilder rh)
    {
        rh.MapGet("/tableau-de-bord", (HttpContext http, RhStore store)
            => Api.RunAsync(http, Consulter, async (_, ct) => Results.Ok(await store.TableauBordAsync(ct)), requireRhRole: true));

        rh.MapGet("/parametres", (HttpContext http, RhStore store)
            => Api.RunAsync(http, LectureDocuments, async (_, ct) => Results.Ok(await store.ParametresAsync(ct)), requireRhRole: true));

        rh.MapGet("/agents", (HttpContext http, RhStore store, string? q, string? statut, long? serviceId, long? uniteId, long? gradeId)
            => Api.RunAsync(http, Consulter,
                async (_, ct) => Results.Ok(await store.ListerAgentsAsync(q, statut, serviceId, uniteId, gradeId, ct)), requireRhRole: true));

        rh.MapGet("/agents/{id:long}", (HttpContext http, long id, RhStore store, JournalAcces journal)
            => Api.RunAsync(http, Consulter, async (ctx, ct) =>
            {
                var dossier = await store.LireDossierAsync(id, VoirConfidentiel(ctx), ct);
                await journal.EcrireAsync(ctx.Agent.Id, ctx.Auteur.Nom, "Consultation dossier RH", dossier.Agent.Reference, "Autorisée");
                return Results.Ok(dossier);
            }, requireRhRole: true));

        rh.MapPut("/agents/{id:long}/identite", (HttpContext http, long id, RhIdentiteInput input, RhStore store, RealtimeHub hub)
            => Modifier(http, Permissions.RhAgentModifier, id, hub, (ctx, ct) => store.ModifierIdentiteAsync(ctx.Auteur, id, input, ct)));

        rh.MapPut("/agents/{id:long}/matricule", (HttpContext http, long id, RhMatriculeInput input, RhStore store, RealtimeHub hub)
            => Modifier(http, Permissions.RhAgentModifier, id, hub, (ctx, ct) => store.ModifierMatriculeAsync(ctx.Auteur, id, input, ct)));

        rh.MapPut("/agents/{id:long}/integration", (HttpContext http, long id, RhIntegrationInput input, RhStore store, RealtimeHub hub)
            => Modifier(http, Permissions.RhAgentModifier, id, hub, (ctx, ct) => store.ModifierIntegrationAsync(ctx.Auteur, id, input, ct)));

        rh.MapPut("/agents/{id:long}/rio", (HttpContext http, long id, RhRioInput input, RhStore store, RealtimeHub hub)
            => Modifier(http, Permissions.RhRioAttribuer, id, hub, (ctx, ct) => store.AttribuerRioAsync(ctx.Auteur, id, input, ct)));

        rh.MapPut("/agents/{id:long}/grade", (HttpContext http, long id, RhGradeInput input, RhStore store, RealtimeHub hub)
            => Modifier(http, Permissions.RhCarriere, id, hub, (ctx, ct) => store.ChangerGradeAsync(ctx.Auteur, id, input, ct)));

        rh.MapPost("/agents/{id:long}/affectations", (HttpContext http, long id, RhAffectationInput input, RhStore store, RealtimeHub hub)
            => Modifier(http, Permissions.RhCarriere, id, hub, (ctx, ct) => store.AffecterAsync(ctx.Auteur, id, input, ct)));

        rh.MapPut("/agents/{id:long}/statut", (HttpContext http, long id, RhStatutInput input, RhStore store, RealtimeHub hub)
            => Modifier(http, Permissions.RhStatut, id, hub, (ctx, ct) => store.ChangerStatutAsync(ctx.Auteur, id, input, ct)));

        rh.MapGet("/affectations", (HttpContext http, RhStore store, bool? enCours)
            => Api.RunAsync(http, Consulter,
                async (_, ct) => Results.Ok(await store.ListerAffectationsAsync(enCours ?? false, ct)), requireRhRole: true));

        rh.MapGet("/rio/historique", (HttpContext http, RhStore store, int? limit)
            => Api.RunAsync(http, Consulter,
                async (_, ct) => Results.Ok(await store.ListerRioAsync(SocleRegles.Limite(limit, 300, 2000), ct)), requireRhRole: true));

        rh.MapGet("/historique", (HttpContext http, RhStore store, string? objet, string? reference, int? limit)
            => Api.RunAsync(http, Consulter,
                async (_, ct) => Results.Ok(await store.HistoriqueAsync(objet, reference, SocleRegles.Limite(limit, 300, 2000), ct)),
                requireRhRole: true));
    }

    /// <summary>
    /// Modification d'un agent : le poste de l'agent recharge sa session (SessionChanged) et les fenêtres
    /// qui affichent des agents se mettent à jour (rh-agent-modifie, identifiant seulement).
    /// </summary>
    private static Task<IResult> Modifier(HttpContext http, string permission, long id, RealtimeHub hub,
        Func<AgentContext, CancellationToken, Task<string>> modification)
        => Api.RunAsync(http, [permission], async (ctx, ct) =>
        {
            var discordId = await modification(ctx, ct);
            await hub.SendToUserAsync(discordId, new RealtimeMessage(RealtimeTypes.SessionChanged), CancellationToken.None);
            await hub.BroadcastAsync(new RealtimeMessage(RhRealtimeTypes.AgentModifie, Text: id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                CancellationToken.None);
            return Results.NoContent();
        }, requireRhRole: true);

    private static bool VoirConfidentiel(AgentContext ctx)
        => RhRegles.PeutVoirConfidentiel(ctx.Droits.Possede(Permissions.RhDocuments), ctx.Droits.Possede(Permissions.RhDocumentsValider));

    // =====================================================================================
    // Référentiels RH (gestion)
    // =====================================================================================

    private static void MapReferentiels(RouteGroupBuilder rh)
    {
        rh.MapGet("/referentiels/{type}", (HttpContext http, string type, RhStore store, bool? inactifs)
            => Api.RunAsync(http, [Permissions.RhConsulter, Permissions.RhReferentiels], async (_, ct) =>
            {
                if (!RhReferentielTypes.Existe(type))
                    return Api.Erreur(StatusCodes.Status404NotFound, "Référentiel inconnu.");
                return Results.Ok(await store.ListerReferentielAsync(type, inactifs ?? true, ct));
            }, requireRhRole: true));

        rh.MapPost("/referentiels/{type}", (HttpContext http, string type, RhReferentielInput input, RhStore store, RealtimeHub hub)
            => Api.RunAsync(http, [Permissions.RhReferentiels], async (ctx, ct) =>
            {
                if (!RhReferentielTypes.Existe(type))
                    return Api.Erreur(StatusCodes.Status404NotFound, "Référentiel inconnu.");
                var element = await store.CreerReferentielAsync(ctx.Auteur, type, input, ct);
                await hub.BroadcastAsync(new RealtimeMessage(RhRealtimeTypes.ReferentielsModifies, Text: type), CancellationToken.None);
                return Results.Ok(element);
            }, requireRhRole: true));

        rh.MapPut("/referentiels/{type}/{id:long}", (HttpContext http, string type, long id, RhReferentielInput input, RhStore store, RealtimeHub hub)
            => Api.RunAsync(http, [Permissions.RhReferentiels], async (ctx, ct) =>
            {
                if (!RhReferentielTypes.Existe(type))
                    return Api.Erreur(StatusCodes.Status404NotFound, "Référentiel inconnu.");
                var (element, discordIds) = await store.ModifierReferentielAsync(ctx.Auteur, type, id, input, ct);
                foreach (var discordId in discordIds.Distinct(StringComparer.Ordinal))
                    await hub.SendToUserAsync(discordId, new RealtimeMessage(RealtimeTypes.SessionChanged), CancellationToken.None);
                await hub.BroadcastAsync(new RealtimeMessage(RhRealtimeTypes.ReferentielsModifies, Text: type), CancellationToken.None);
                return Results.Ok(element);
            }, requireRhRole: true));
    }

    // =====================================================================================
    // Documents, dossiers, catégories, modèles
    // =====================================================================================

    private static void MapDocuments(RouteGroupBuilder rh)
    {
        rh.MapGet("/documents", (HttpContext http, RhDocumentsStore store, string? q, long? categorieId, long? dossierId, long? agentId,
                long? serviceId, string? statut, bool? archives)
            => Api.RunAsync(http, LectureDocuments, async (ctx, ct) =>
            {
                var filtre = new FiltreDocuments(q, categorieId, dossierId, agentId, serviceId, statut,
                    ArchivesSeulement: archives == true, InclureArchives: false);
                return Results.Ok(await store.ListerAsync(filtre, VoirConfidentiel(ctx), ct));
            }, requireRhRole: true));

        rh.MapGet("/documents/{id:long}", (HttpContext http, long id, RhDocumentsStore store)
            => Api.RunAsync(http, LectureDocuments,
                async (ctx, ct) => Results.Ok(await store.LireDetailAsync(id, VoirConfidentiel(ctx), ct)), requireRhRole: true));

        rh.MapGet("/documents/{id:long}/versions/{numero:int}/fichier", (HttpContext http, long id, int numero, RhDocumentsStore store, JournalAcces journal)
            => Api.RunAsync(http, LectureDocuments, async (ctx, ct) =>
            {
                var (document, fichier) = await store.FichierAsync(id, numero, VoirConfidentiel(ctx), ct);
                await journal.EcrireAsync(ctx.Agent.Id, ctx.Auteur.Nom, "Téléchargement document RH",
                    $"{document.Reference} v{(numero <= 0 ? document.Version : numero)}", "Autorisé");
                return Results.Ok(fichier);
            }, requireRhRole: true));

        rh.MapPost("/documents", (HttpContext http, RhDocumentCreationInput input, RhDocumentsStore store, RealtimeHub hub)
            => Documents(http, Permissions.RhDocuments, hub, (ctx, ct) => store.CreerAsync(ctx.Auteur, input, ct)));

        rh.MapPut("/documents/{id:long}", (HttpContext http, long id, RhDocumentMajInput input, RhDocumentsStore store, RealtimeHub hub)
            => Documents(http, Permissions.RhDocuments, hub, (ctx, ct) => store.ModifierAsync(ctx.Auteur, id, input, VoirConfidentiel(ctx), ct)));

        rh.MapPost("/documents/{id:long}/versions", (HttpContext http, long id, RhVersionInput input, RhDocumentsStore store, RealtimeHub hub)
            => Documents(http, Permissions.RhDocuments, hub, (ctx, ct) => store.NouvelleVersionAsync(ctx.Auteur, id, input, VoirConfidentiel(ctx), ct)));

        rh.MapPost("/documents/{id:long}/soumettre", (HttpContext http, long id, RhDocumentsStore store, RealtimeHub hub)
            => Documents(http, Permissions.RhDocuments, hub, (ctx, ct) => store.SoumettreAsync(ctx.Auteur, id, VoirConfidentiel(ctx), ct)));

        rh.MapPost("/documents/{id:long}/valider", (HttpContext http, long id, RhDocumentsStore store, RealtimeHub hub)
            => Documents(http, Permissions.RhDocumentsValider, hub, (ctx, ct) => store.ValiderAsync(ctx.Auteur, id, VoirConfidentiel(ctx), ct)));

        rh.MapPost("/documents/{id:long}/refuser", (HttpContext http, long id, RhMotifInput input, RhDocumentsStore store, RealtimeHub hub)
            => Documents(http, Permissions.RhDocumentsValider, hub, (ctx, ct) => store.RefuserAsync(ctx.Auteur, id, input.Motif, VoirConfidentiel(ctx), ct)));

        rh.MapPost("/documents/{id:long}/archiver", (HttpContext http, long id, RhMotifInput input, RhDocumentsStore store, RealtimeHub hub)
            => Documents(http, Permissions.RhDocuments, hub, (ctx, ct) => store.ArchiverAsync(ctx.Auteur, id, input.Motif, VoirConfidentiel(ctx), ct)));

        rh.MapPost("/documents/{id:long}/restaurer", (HttpContext http, long id, RhDocumentsStore store, RealtimeHub hub)
            => Documents(http, Permissions.RhDocuments, hub, (ctx, ct) => store.RestaurerAsync(ctx.Auteur, id, VoirConfidentiel(ctx), ct)));

        // ----- Dossiers -----

        rh.MapGet("/dossiers", (HttpContext http, RhDocumentsStore store)
            => Api.RunAsync(http, LectureDocuments, async (_, ct) => Results.Ok(await store.ListerDossiersAsync(ct)), requireRhRole: true));

        rh.MapPost("/dossiers", (HttpContext http, RhDossierDocInput input, RhDocumentsStore store, RealtimeHub hub)
            => Documents(http, Permissions.RhDocuments, hub, (ctx, ct) => store.CreerDossierAsync(ctx.Auteur, input, ct)));

        rh.MapPut("/dossiers/{id:long}", (HttpContext http, long id, RhDossierDocInput input, RhDocumentsStore store, RealtimeHub hub)
            => Documents(http, Permissions.RhDocuments, hub, (ctx, ct) => store.ModifierDossierAsync(ctx.Auteur, id, input, ct)));

        rh.MapDelete("/dossiers/{id:long}", (HttpContext http, long id, RhDocumentsStore store, RealtimeHub hub)
            => Api.RunAsync(http, [Permissions.RhDocuments], async (ctx, ct) =>
            {
                await store.SupprimerDossierAsync(ctx.Auteur, id, ct);
                await hub.BroadcastAsync(new RealtimeMessage(RhRealtimeTypes.DocumentsModifies), CancellationToken.None);
                return Results.NoContent();
            }, requireRhRole: true));

        // ----- Catégories -----

        rh.MapGet("/categories", (HttpContext http, RhDocumentsStore store)
            => Api.RunAsync(http, LectureDocuments, async (_, ct) => Results.Ok(await store.ListerCategoriesAsync(ct)), requireRhRole: true));

        rh.MapPost("/categories", (HttpContext http, RhCategorieInput input, RhDocumentsStore store, RealtimeHub hub)
            => Documents(http, Permissions.RhDocuments, hub, (ctx, ct) => store.EnregistrerCategorieAsync(ctx.Auteur, null, input, ct)));

        rh.MapPut("/categories/{id:long}", (HttpContext http, long id, RhCategorieInput input, RhDocumentsStore store, RealtimeHub hub)
            => Documents(http, Permissions.RhDocuments, hub, (ctx, ct) => store.EnregistrerCategorieAsync(ctx.Auteur, id, input, ct)));

        // ----- Modèles -----

        rh.MapGet("/modeles", (HttpContext http, RhDocumentsStore store)
            => Api.RunAsync(http, [Permissions.RhConsulter, Permissions.RhDocuments],
                async (_, ct) => Results.Ok(await store.ListerModelesAsync(ct)), requireRhRole: true));

        rh.MapPost("/modeles", (HttpContext http, RhModeleInput input, RhDocumentsStore store, RealtimeHub hub)
            => Documents(http, Permissions.RhDocuments, hub, (ctx, ct) => store.EnregistrerModeleAsync(ctx.Auteur, null, input, ct)));

        rh.MapPut("/modeles/{id:long}", (HttpContext http, long id, RhModeleInput input, RhDocumentsStore store, RealtimeHub hub)
            => Documents(http, Permissions.RhDocuments, hub, (ctx, ct) => store.EnregistrerModeleAsync(ctx.Auteur, id, input, ct)));

        rh.MapPost("/modeles/{id:long}/generer", (HttpContext http, long id, RhGenerationInput input, RhDocumentsStore store, RealtimeHub hub)
            => Api.RunAsync(http, [Permissions.RhDocuments], async (ctx, ct) =>
            {
                var resultat = await store.GenererAsync(ctx.Auteur, id, input, ct);
                if (resultat.Document is not null)
                    await hub.BroadcastAsync(new RealtimeMessage(RhRealtimeTypes.DocumentsModifies), CancellationToken.None);
                return Results.Ok(resultat);
            }, requireRhRole: true));
    }

    /// <summary>Modification documentaire : réponse JSON + diffusion « rh-documents-modifies » (sans donnée).</summary>
    private static Task<IResult> Documents<T>(HttpContext http, string permission, RealtimeHub hub, Func<AgentContext, CancellationToken, Task<T>> action)
        => Api.RunAsync(http, [permission], async (ctx, ct) =>
        {
            var resultat = await action(ctx, ct);
            await hub.BroadcastAsync(new RealtimeMessage(RhRealtimeTypes.DocumentsModifies), CancellationToken.None);
            return Results.Ok(resultat);
        }, requireRhRole: true);
}
