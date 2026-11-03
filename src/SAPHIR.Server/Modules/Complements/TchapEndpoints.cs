using System.Globalization;
using Saphir.Server.Data;
using Saphir.Server.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Complements;

namespace Saphir.Server.Modules.Complements;

/// <summary>
/// TCHAP (cahier § 18.6) : messagerie instantanée simplifiée, interne à SAPHIR (aucune liaison avec le Tchap réel).
/// Salons de service ouverts à tous les agents ayant Tchap (créés par l'administration) et salons de groupe créés par les
/// agents (réservés à leurs membres). Messages poussés en temps réel ; non-lus suivis par agent.
/// </summary>
internal sealed class TchapStore(SaphirDb saphirDb, RealtimeHub hub)
{
    private const int MaxTexte = 4000;
    private const int MaxMembres = 200;

    public async Task<List<SalonDto>> SalonsAsync(AgentContext ctx, bool archives, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await db.QueryAsync(
            $"""
            SELECT s.*,
                (SELECT m.role FROM app_tchap_membres m WHERE m.salon_id = s.id AND m.agent_id = @moi) AS mon_role,
                (SELECT COUNT(*) FROM app_tchap_membres m WHERE m.salon_id = s.id) AS nb_membres,
                (SELECT COUNT(*) FROM app_tchap_messages x WHERE x.salon_id = s.id AND x.supprime = 0 AND x.auteur_id <> @moi
                    AND x.id > COALESCE((SELECT l.dernier_lu_id FROM app_tchap_lectures l WHERE l.salon_id = s.id AND l.agent_id = @moi), 0)) AS non_lus
            FROM app_tchap_salons s
            WHERE (s.type = @service OR EXISTS (SELECT 1 FROM app_tchap_membres m WHERE m.salon_id = s.id AND m.agent_id = @moi))
            {(archives ? string.Empty : "AND s.archive = 0")}
            ORDER BY s.type DESC, COALESCE(s.dernier_message_le, s.cree_le) DESC
            """,
            r => LireSalon(r, ctx), ct, ("@moi", ctx.Agent.Id), ("@service", TchapTypesSalon.Service));
    }

    private static SalonDto LireSalon(SqlRow r, AgentContext ctx)
    {
        var type = r.Text("type");
        var role = r.NullableText("mon_role");
        var estAdmin = type == TchapTypesSalon.Service ? ctx.Droits.Possede(Permissions.AdminParametres) : role == "admin";
        return new SalonDto(r.Long("id"), r.Text("nom"), r.NullableText("description"), type,
            type == TchapTypesSalon.Service || role is not null, estAdmin, r.Int("nb_membres"), r.Int("non_lus"),
            r.NullableDate("dernier_message_le"), r.Bool("archive"));
    }

    public async Task<SalonDto> CreerSalonAsync(AgentContext ctx, SalonInput input, CancellationToken ct)
    {
        var nom = ComplementsOutils.Obligatoire(input.Nom, 100, "Nom du salon");
        var description = ComplementsOutils.Facultatif(input.Description, 300);
        if (input.Type is not (TchapTypesSalon.Service or TchapTypesSalon.Groupe))
            throw ApiException.Saisie("Type de salon inconnu.");
        if (input.Type == TchapTypesSalon.Service && !ctx.Droits.Possede(Permissions.AdminParametres))
            throw ApiException.Saisie("Seule l'administration crée des salons de service.");
        var membres = (input.Membres ?? Array.Empty<long>()).Where(m => m != ctx.Agent.Id).Distinct().ToList();
        if (membres.Count > MaxMembres)
            throw ApiException.Saisie($"{MaxMembres} membres au plus.");

        var maintenant = DateTime.UtcNow;
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var agents = await ComplementsOutils.AgentsAsync(db, membres, ct);
        if (agents.Count != membres.Count)
            throw ApiException.Saisie("Un membre choisi n'existe plus.");
        var id = await db.InsertAsync(
            """
            INSERT INTO app_tchap_salons (nom, description, type, archive, cree_par_id, cree_par, cree_le, maj_le)
            VALUES (@nom, @description, @type, 0, @moi, @auteur, @maintenant, @maintenant)
            """, ct,
            ("@nom", nom), ("@description", description), ("@type", input.Type), ("@moi", ctx.Agent.Id), ("@auteur", ctx.Auteur.Nom),
            ("@maintenant", maintenant));
        if (input.Type == TchapTypesSalon.Groupe)
        {
            await AjouterMembreAsync(db, id, ctx.Agent.Id, $"{ctx.Agent.Nom} {ctx.Agent.Prenom}", "admin", maintenant, ct);
            foreach (var a in agents)
                await AjouterMembreAsync(db, id, a.Id, $"{a.Nom} {a.Prenom}", "membre", maintenant, ct);
        }
        await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Salon, Reference(id), "Création", "membres", null,
            SocleRegles.Liste(agents.Select(a => a.Libelle)), nom, ct);
        await db.CommitAsync(ct);

        await PrevenirSalonsAsync(input.Type, agents.Select(a => a.Id).Append(ctx.Agent.Id));
        return (await SalonsAsync(ctx, true, ct)).First(s => s.Id == id);
    }

    public async Task<SalonDto> ModifierSalonAsync(AgentContext ctx, long id, SalonInput input, CancellationToken ct)
    {
        var nom = ComplementsOutils.Obligatoire(input.Nom, 100, "Nom du salon");
        var description = ComplementsOutils.Facultatif(input.Description, 300);
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var salon = await SalonPourAdminAsync(db, ctx, id, ct);
        await db.ExecuteAsync("UPDATE app_tchap_salons SET nom = @nom, description = @description, maj_le = @maintenant WHERE id = @id", ct,
            ("@nom", nom), ("@description", description), ("@maintenant", DateTime.UtcNow), ("@id", id));
        if (salon.Nom != nom)
            await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Salon, Reference(id), "Modification", "nom", salon.Nom, nom, null, ct);
        if (salon.Description != description)
            await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Salon, Reference(id), "Modification", "description", salon.Description, description, null, ct);
        await db.CommitAsync(ct);
        await PrevenirSalonsAsync(salon.Type, await MembresIdsAsync(id, ct));
        return (await SalonsAsync(ctx, true, ct)).First(s => s.Id == id);
    }

    public async Task ArchiverSalonAsync(AgentContext ctx, long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var salon = await SalonPourAdminAsync(db, ctx, id, ct);
        await db.ExecuteAsync("UPDATE app_tchap_salons SET archive = 1, maj_le = @maintenant WHERE id = @id", ct,
            ("@maintenant", DateTime.UtcNow), ("@id", id));
        await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Salon, Reference(id), "Archivage", null, salon.Nom, null, null, ct);
        await db.CommitAsync(ct);
        await PrevenirSalonsAsync(salon.Type, await MembresIdsAsync(id, ct));
    }

    public async Task<List<SalonMembreDto>> MembresAsync(AgentContext ctx, long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        await SalonLisibleAsync(db, ctx, id, ct);
        return await db.QueryAsync(
            "SELECT agent_id, agent, role, ajoute_le FROM app_tchap_membres WHERE salon_id = @id ORDER BY role, agent",
            r => new SalonMembreDto(r.Long("agent_id"), r.Text("agent"), r.Text("role"), r.Date("ajoute_le")), ct, ("@id", id));
    }

    public async Task<List<SalonMembreDto>> ModifierMembresAsync(AgentContext ctx, long id, SalonMembresInput input, CancellationToken ct)
    {
        var ajouter = (input.Ajouter ?? Array.Empty<long>()).Distinct().ToList();
        var retirer = (input.Retirer ?? Array.Empty<long>()).Distinct().Where(r => r != ctx.Agent.Id).ToList();
        var maintenant = DateTime.UtcNow;
        await using (var db = await saphirDb.OpenAsync(ct))
        {
            await db.BeginAsync(ct);
            var salon = await SalonPourAdminAsync(db, ctx, id, ct);
            if (salon.Type != TchapTypesSalon.Groupe)
                throw ApiException.Saisie("Un salon de service est ouvert à tous : il n'a pas de liste de membres.");
            var agents = await ComplementsOutils.AgentsAsync(db, ajouter, ct);
            if (agents.Count != ajouter.Count)
                throw ApiException.Saisie("Un agent choisi n'existe plus.");
            foreach (var a in agents)
                await AjouterMembreAsync(db, id, a.Id, $"{a.Nom} {a.Prenom}", "membre", maintenant, ct);
            var retires = await ComplementsOutils.AgentsAsync(db, retirer, ct);
            foreach (var r in retirer)
                await db.ExecuteAsync("DELETE FROM app_tchap_membres WHERE salon_id = @id AND agent_id = @agent", ct, ("@id", id), ("@agent", r));
            if (await db.LongAsync("SELECT COUNT(*) FROM app_tchap_membres WHERE salon_id = @id", ct, ("@id", id)) > MaxMembres)
                throw ApiException.Saisie($"{MaxMembres} membres au plus.");
            if (agents.Count > 0)
                await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Salon, Reference(id), "Ajout de membres", "membres", null,
                    SocleRegles.Liste(agents.Select(a => a.Libelle)), null, ct);
            if (retires.Count > 0)
                await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Salon, Reference(id), "Retrait de membres", "membres",
                    SocleRegles.Liste(retires.Select(a => a.Libelle)), null, null, ct);
            await db.CommitAsync(ct);
        }
        await PrevenirSalonsAsync(TchapTypesSalon.Groupe, (await MembresIdsAsync(id, ct)).Concat(retirer));
        return await MembresAsync(ctx, id, ct);
    }

    public async Task QuitterAsync(AgentContext ctx, long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var n = await db.ExecuteAsync("DELETE FROM app_tchap_membres WHERE salon_id = @id AND agent_id = @moi", ct, ("@id", id), ("@moi", ctx.Agent.Id));
        if (n == 0)
            throw ApiException.Introuvable("Tu n'es pas membre de ce salon.");
        // Le dernier administrateur qui part transmet le rôle au membre le plus ancien.
        if (await db.LongAsync("SELECT COUNT(*) FROM app_tchap_membres WHERE salon_id = @id AND role = 'admin'", ct, ("@id", id)) == 0)
            await db.ExecuteAsync(
                "UPDATE app_tchap_membres SET role = 'admin' WHERE salon_id = @id ORDER BY ajoute_le, agent_id LIMIT 1", ct, ("@id", id));
        await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Salon, Reference(id), "Départ du salon", "membres", ctx.Auteur.Nom, null, null, ct);
        await db.CommitAsync(ct);
        await PrevenirSalonsAsync(TchapTypesSalon.Groupe, (await MembresIdsAsync(id, ct)).Append(ctx.Agent.Id));
    }

    // ===== Messages =====

    public async Task<List<TchapMessageDto>> MessagesAsync(AgentContext ctx, long id, long? avant, int? limite, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        await SalonLisibleAsync(db, ctx, id, ct);
        var messages = await db.QueryAsync(
            """
            SELECT * FROM (
                SELECT id, salon_id, auteur_id, auteur, texte, supprime, cree_le FROM app_tchap_messages
                WHERE salon_id = @id AND (@avant IS NULL OR id < @avant)
                ORDER BY id DESC LIMIT @limite) dernier
            ORDER BY id
            """,
            LireMessage, ct, ("@id", id), ("@avant", avant), ("@limite", SocleRegles.Limite(limite, 100, 300)));
        if (avant is null && messages.Count > 0)
            await db.ExecuteAsync(
                """
                INSERT INTO app_tchap_lectures (salon_id, agent_id, dernier_lu_id) VALUES (@id, @moi, @dernier)
                ON DUPLICATE KEY UPDATE dernier_lu_id = GREATEST(dernier_lu_id, @dernier)
                """, ct, ("@id", id), ("@moi", ctx.Agent.Id), ("@dernier", messages[^1].Id));
        return messages;
    }

    public async Task<TchapMessageDto> EnvoyerAsync(AgentContext ctx, long id, TchapMessageInput input, CancellationToken ct)
    {
        var texte = ComplementsOutils.Long(input.Texte, MaxTexte, "Message", obligatoire: true).Trim();
        var maintenant = DateTime.UtcNow;
        TchapMessageDto message;
        Salon salon;
        await using (var db = await saphirDb.OpenAsync(ct))
        {
            await db.BeginAsync(ct);
            // Verrou du salon d'abord : sans lui, deux envois simultanés (verrou partagé de la clé étrangère à l'insertion,
            // puis mise à jour du salon) se bloquent mutuellement (interblocage InnoDB).
            await db.ScalarAsync("SELECT id FROM app_tchap_salons WHERE id = @id FOR UPDATE", ct, ("@id", id));
            salon = await SalonLisibleAsync(db, ctx, id, ct);
            if (salon.Archive)
                throw ApiException.Conflit("Ce salon est archivé : il est en lecture seule.");
            var messageId = await db.InsertAsync(
                "INSERT INTO app_tchap_messages (salon_id, auteur_id, auteur, texte, supprime, cree_le) VALUES (@id, @moi, @auteur, @texte, 0, @maintenant)",
                ct, ("@id", id), ("@moi", ctx.Agent.Id), ("@auteur", $"{ctx.Agent.Nom} {ctx.Agent.Prenom}"), ("@texte", texte), ("@maintenant", maintenant));
            await db.ExecuteAsync("UPDATE app_tchap_salons SET dernier_message_id = @m, dernier_message_le = @maintenant WHERE id = @id", ct,
                ("@m", messageId), ("@maintenant", maintenant), ("@id", id));
            await db.ExecuteAsync(
                """
                INSERT INTO app_tchap_lectures (salon_id, agent_id, dernier_lu_id) VALUES (@id, @moi, @m)
                ON DUPLICATE KEY UPDATE dernier_lu_id = GREATEST(dernier_lu_id, @m)
                """, ct, ("@id", id), ("@moi", ctx.Agent.Id), ("@m", messageId));
            message = await db.FirstOrDefaultAsync(
                          "SELECT id, salon_id, auteur_id, auteur, texte, supprime, cree_le FROM app_tchap_messages WHERE id = @m",
                          LireMessage, ct, ("@m", messageId))
                      ?? throw new InvalidOperationException("Message introuvable.");
            await db.CommitAsync(ct);
        }

        var signal = new RealtimeMessage(ComplementsRealtime.TchapMessage, Text: id.ToString(CultureInfo.InvariantCulture), AppId: SaphirApps.Tchap);
        if (salon.Type == TchapTypesSalon.Service)
        {
            await hub.BroadcastAsync(signal, CancellationToken.None);
        }
        else
        {
            await using var db = await saphirDb.OpenAsync(CancellationToken.None);
            foreach (var discord in await ComplementsOutils.DiscordIdsAsync(db, await MembresIdsAsync(id, CancellationToken.None), CancellationToken.None))
            {
                await hub.SendToUserAsync(discord, signal, CancellationToken.None);
                if (discord != ctx.User.UserId)
                    await hub.SendToUserAsync(discord, new RealtimeMessage(RealtimeTypes.Notify, Title: "TCHAP — " + salon.Nom,
                        Text: $"Nouveau message de {ctx.Agent.Nom} {ctx.Agent.Prenom}", AppId: SaphirApps.Tchap), CancellationToken.None);
            }
        }
        return message;
    }

    public async Task SupprimerMessageAsync(AgentContext ctx, long messageId, CancellationToken ct)
    {
        long salonId;
        await using (var db = await saphirDb.OpenAsync(ct))
        {
            await db.BeginAsync(ct);
            var m = await db.FirstOrDefaultAsync(
                        "SELECT id, salon_id, auteur_id, auteur, texte, supprime, cree_le FROM app_tchap_messages WHERE id = @m FOR UPDATE",
                        LireMessage, ct, ("@m", messageId))
                    ?? throw ApiException.Introuvable("Message introuvable.");
            var salon = await SalonLisibleAsync(db, ctx, m.SalonId, ct);
            if (m.AuteurId != ctx.Agent.Id && !salon.EstAdmin)
                throw ApiException.Saisie("Seul l'auteur ou un administrateur du salon peut retirer ce message.");
            if (m.Supprime)
                throw ApiException.Conflit("Message déjà retiré.");
            await db.ExecuteAsync("UPDATE app_tchap_messages SET supprime = 1 WHERE id = @m", ct, ("@m", messageId));
            await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Salon, Reference(m.SalonId), "Message retiré", "message",
                m.Texte.Length > 300 ? m.Texte[..300] + "…" : m.Texte, null, $"Message de {m.Auteur} du {m.Date:u}", ct);
            await db.CommitAsync(ct);
            salonId = m.SalonId;
        }
        await hub.BroadcastAsync(new RealtimeMessage(ComplementsRealtime.TchapMessage, Text: salonId.ToString(CultureInfo.InvariantCulture),
            AppId: SaphirApps.Tchap), CancellationToken.None);
    }

    // ===== Outils =====

    private sealed record Salon(long Id, string Nom, string? Description, string Type, bool Archive, bool EstMembre, bool EstAdmin);

    /// <summary>Salon de service, ou salon de groupe dont l'agent est membre (sinon 404).</summary>
    private static async Task<Salon> SalonLisibleAsync(SqlSession db, AgentContext ctx, long id, CancellationToken ct)
    {
        var salon = await db.FirstOrDefaultAsync(
            """
            SELECT s.id, s.nom, s.description, s.type, s.archive,
                (SELECT m.role FROM app_tchap_membres m WHERE m.salon_id = s.id AND m.agent_id = @moi) AS mon_role
            FROM app_tchap_salons s WHERE s.id = @id
            """,
            r =>
            {
                var type = r.Text("type");
                var role = r.NullableText("mon_role");
                return new Salon(r.Long("id"), r.Text("nom"), r.NullableText("description"), type, r.Bool("archive"),
                    type == TchapTypesSalon.Service || role is not null,
                    type == TchapTypesSalon.Service ? ctx.Droits.Possede(Permissions.AdminParametres) : role == "admin");
            }, ct, ("@moi", ctx.Agent.Id), ("@id", id));
        if (salon is null || !salon.EstMembre)
            throw ApiException.Introuvable("Salon introuvable.");
        return salon;
    }

    private static async Task<Salon> SalonPourAdminAsync(SqlSession db, AgentContext ctx, long id, CancellationToken ct)
    {
        var salon = await SalonLisibleAsync(db, ctx, id, ct);
        if (!salon.EstAdmin)
            throw ApiException.Saisie("Seul un administrateur du salon peut faire cette action.");
        if (salon.Archive)
            throw ApiException.Conflit("Ce salon est archivé.");
        return salon;
    }

    private static Task AjouterMembreAsync(SqlSession db, long salon, long agent, string libelle, string role, DateTime maintenant, CancellationToken ct)
        => db.ExecuteAsync(
            "INSERT IGNORE INTO app_tchap_membres (salon_id, agent_id, agent, role, ajoute_le) VALUES (@s, @a, @libelle, @role, @maintenant)",
            ct, ("@s", salon), ("@a", agent), ("@libelle", libelle), ("@role", role), ("@maintenant", maintenant));

    private async Task<List<long>> MembresIdsAsync(long salon, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await db.QueryAsync("SELECT agent_id FROM app_tchap_membres WHERE salon_id = @s", r => r.Long("agent_id"), ct, ("@s", salon));
    }

    private async Task PrevenirSalonsAsync(string type, IEnumerable<long> agents)
    {
        var signal = new RealtimeMessage(ComplementsRealtime.TchapSalons, AppId: SaphirApps.Tchap);
        if (type == TchapTypesSalon.Service)
        {
            await hub.BroadcastAsync(signal, CancellationToken.None);
            return;
        }
        await using var db = await saphirDb.OpenAsync(CancellationToken.None);
        foreach (var discord in await ComplementsOutils.DiscordIdsAsync(db, agents, CancellationToken.None))
            await hub.SendToUserAsync(discord, signal, CancellationToken.None);
    }

    private static TchapMessageDto LireMessage(SqlRow r)
    {
        var supprime = r.Bool("supprime");
        return new TchapMessageDto(r.Long("id"), r.Long("salon_id"), r.Long("auteur_id"), r.Text("auteur"),
            supprime ? "Message retiré." : r.Text("texte"), r.Date("cree_le"), supprime);
    }

    private static string Reference(long id) => ComplementsOutils.Reference("SALON", id);
}

internal static class TchapEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/tchap");
        string[] p = [Permissions.TchapUtiliser];

        g.MapGet("/salons", (HttpContext http, TchapStore store, bool? archives) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.SalonsAsync(ctx, archives == true, ct))));

        g.MapPost("/salons", (HttpContext http, SalonInput input, TchapStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.CreerSalonAsync(ctx, input, ct))));

        g.MapPut("/salons/{id:long}", (HttpContext http, long id, SalonInput input, TchapStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.ModifierSalonAsync(ctx, id, input, ct))));

        g.MapPost("/salons/{id:long}/archiver", (HttpContext http, long id, TchapStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) =>
            {
                await store.ArchiverSalonAsync(ctx, id, ct);
                return Results.NoContent();
            }));

        g.MapGet("/salons/{id:long}/membres", (HttpContext http, long id, TchapStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.MembresAsync(ctx, id, ct))));

        g.MapPut("/salons/{id:long}/membres", (HttpContext http, long id, SalonMembresInput input, TchapStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.ModifierMembresAsync(ctx, id, input, ct))));

        g.MapPost("/salons/{id:long}/quitter", (HttpContext http, long id, TchapStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) =>
            {
                await store.QuitterAsync(ctx, id, ct);
                return Results.NoContent();
            }));

        g.MapGet("/salons/{id:long}/messages", (HttpContext http, long id, long? avant, int? limit, TchapStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.MessagesAsync(ctx, id, avant, limit, ct))));

        g.MapPost("/salons/{id:long}/messages", (HttpContext http, long id, TchapMessageInput input, TchapStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.EnvoyerAsync(ctx, id, input, ct))));

        g.MapDelete("/messages/{id:long}", (HttpContext http, long id, TchapStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) =>
            {
                await store.SupprimerMessageAsync(ctx, id, ct);
                return Results.NoContent();
            }));
    }
}
