using System.Globalization;
using Saphir.Server.Data;
using Saphir.Server.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Complements;

namespace Saphir.Server.Modules.Complements;

/// <summary>
/// NOTES (cahier § 18.3) : notes personnelles, partagées (lecture ou modification), catégories, épinglage, recherche,
/// historique des modifications et détection des modifications concurrentes (version).
/// </summary>
internal sealed class NotesStore(SaphirDb saphirDb, RealtimeHub hub)
{
    private const int MaxContenu = 200_000;
    private const int MaxPartages = 50;

    public async Task<List<NoteDto>> ListerAsync(AgentContext ctx, string? q, string? categorie, string? filtre, CancellationToken ct)
    {
        var sql = """
            SELECT n.id FROM app_notes n
            WHERE n.supprimee = 0 AND (n.auteur_id = @moi OR EXISTS (SELECT 1 FROM app_note_partages s WHERE s.note_id = n.id AND s.agent_id = @moi))
            """;
        var parametres = new List<(string, object?)> { ("@moi", ctx.Agent.Id) };
        switch (filtre)
        {
            case "epinglees":
                sql += " AND n.epinglee = 1";
                break;
            case "partagees":
                sql += " AND n.auteur_id <> @moi";
                break;
            case "miennes":
                sql += " AND n.auteur_id = @moi";
                break;
        }
        if (!string.IsNullOrWhiteSpace(categorie))
        {
            sql += " AND n.categorie = @categorie";
            parametres.Add(("@categorie", categorie.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            sql += " AND (n.titre LIKE @q OR n.contenu LIKE @q OR n.categorie LIKE @q)";
            parametres.Add(("@q", "%" + SocleRegles.EchapperLike(q.Trim()) + "%"));
        }
        sql += " ORDER BY n.epinglee DESC, n.maj_le DESC LIMIT 500";

        await using var db = await saphirDb.OpenAsync(ct);
        var ids = await db.QueryAsync(sql, r => r.Long("id"), ct, parametres.ToArray());
        var notes = new List<NoteDto>();
        foreach (var id in ids)
            if (await ChargerAsync(db, ctx.Agent.Id, id, ct) is { } n)
                notes.Add(n);
        return notes;
    }

    public async Task<List<string>> CategoriesAsync(AgentContext ctx, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await db.QueryAsync(
            """
            SELECT DISTINCT n.categorie FROM app_notes n
            WHERE n.supprimee = 0 AND n.categorie IS NOT NULL
              AND (n.auteur_id = @moi OR EXISTS (SELECT 1 FROM app_note_partages s WHERE s.note_id = n.id AND s.agent_id = @moi))
            ORDER BY n.categorie
            """, r => r.Text("categorie"), ct, ("@moi", ctx.Agent.Id));
    }

    public async Task<NoteDto> LireAsync(AgentContext ctx, long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await ChargerAsync(db, ctx.Agent.Id, id, ct) ?? throw ApiException.Introuvable("Note introuvable.");
    }

    public async Task<List<HistoriqueDto>> HistoriqueAsync(AgentContext ctx, long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var note = await ChargerAsync(db, ctx.Agent.Id, id, ct) ?? throw ApiException.Introuvable("Note introuvable.");
        return await Historique.ListerAsync(db, ObjetsComplements.Note, note.Reference, 200, ct);
    }

    public async Task<NoteDto> EnregistrerAsync(AgentContext ctx, long? id, NoteInput input, CancellationToken ct)
    {
        var titre = ComplementsOutils.Obligatoire(input.Titre, 200, "Titre");
        var contenu = ComplementsOutils.Long(input.Contenu, MaxContenu, "Contenu", obligatoire: false);
        var categorie = ComplementsOutils.Facultatif(input.Categorie, 60);
        var partages = (input.Partages ?? Array.Empty<NotePartageInput>())
            .Where(s => s.AgentId != ctx.Agent.Id)
            .GroupBy(s => s.AgentId).Select(g => g.Last()).ToList();
        if (partages.Count > MaxPartages)
            throw ApiException.Saisie($"Une note se partage avec {MaxPartages} agents au plus.");

        var maintenant = DateTime.UtcNow;
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var agents = await ComplementsOutils.AgentsAsync(db, partages.Select(s => s.AgentId), ct);
        if (agents.Count != partages.Count)
            throw ApiException.Saisie("Un agent choisi pour le partage n'existe plus.");

        string reference;
        var destinatairesAvant = new List<long>();
        if (id is null)
        {
            id = await db.InsertAsync(
                """
                INSERT INTO app_notes (auteur_id, auteur, titre, contenu, categorie, epinglee, supprimee, version, cree_le, maj_le)
                VALUES (@moi, @auteur, @titre, @contenu, @categorie, @epinglee, 0, 1, @maintenant, @maintenant)
                """, ct,
                ("@moi", ctx.Agent.Id), ("@auteur", $"{ctx.Agent.Nom} {ctx.Agent.Prenom}"), ("@titre", titre), ("@contenu", contenu),
                ("@categorie", categorie), ("@epinglee", input.Epinglee), ("@maintenant", maintenant));
            reference = ComplementsOutils.Reference("NOTE", id.Value);
            await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Note, reference, "Création", null, null, titre, null, ct);
        }
        else
        {
            var avant = await ChargerAsync(db, ctx.Agent.Id, id.Value, ct, verrouiller: true)
                        ?? throw ApiException.Introuvable("Note introuvable.");
            if (!avant.PeutModifier)
                throw ApiException.Saisie("Cette note t'est partagée en lecture seule.");
            ComplementsOutils.VerifierVersion(input.Version, avant.Version);
            reference = avant.Reference;
            destinatairesAvant = avant.Partages.Select(s => s.AgentId).Append(avant.AuteurId).ToList();

            // Seul l'auteur gère l'épinglage et les partages.
            var epinglee = avant.EstAuteur ? input.Epinglee : avant.Epinglee;
            await db.ExecuteAsync(
                """
                UPDATE app_notes SET titre = @titre, contenu = @contenu, categorie = @categorie, epinglee = @epinglee,
                    version = version + 1, maj_le = @maintenant
                WHERE id = @id
                """, ct,
                ("@titre", titre), ("@contenu", contenu), ("@categorie", categorie), ("@epinglee", epinglee),
                ("@maintenant", maintenant), ("@id", id.Value));

            if (avant.Titre != titre)
                await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Note, reference, "Modification", "titre", avant.Titre, titre, null, ct);
            if (avant.Contenu != contenu)
                await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Note, reference, "Modification", "contenu",
                    Apercu(avant.Contenu), Apercu(contenu), null, ct);
            if (avant.Categorie != categorie)
                await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Note, reference, "Modification", "catégorie", avant.Categorie, categorie, null, ct);
            if (avant.Epinglee != epinglee)
                await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Note, reference, epinglee ? "Épinglage" : "Désépinglage", null, null, null, null, ct);

            if (!avant.EstAuteur)
            {
                var dtoPartage = await ChargerAsync(db, ctx.Agent.Id, id.Value, ct) ?? throw new InvalidOperationException("Note introuvable.");
                await db.CommitAsync(ct);
                await PrevenirAsync(destinatairesAvant, id.Value, ctx.Agent.Id);
                return dtoPartage;
            }

            var anciens = string.Join(", ", avant.Partages.OrderBy(s => s.Libelle).Select(s => s.Libelle + (s.PeutModifier ? " (modification)" : string.Empty)));
            var nouveaux = string.Join(", ", partages
                .Select(s => (Agent: agents.First(ag => ag.Id == s.AgentId), s.PeutModifier))
                .OrderBy(x => $"{x.Agent.Nom} {x.Agent.Prenom}")
                .Select(x => $"{x.Agent.Nom} {x.Agent.Prenom}" + (x.PeutModifier ? " (modification)" : string.Empty)));
            if (anciens != nouveaux)
                await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Note, reference, "Partage", "partages",
                    anciens.Length == 0 ? null : anciens, nouveaux.Length == 0 ? null : nouveaux, null, ct);
            await db.ExecuteAsync("DELETE FROM app_note_partages WHERE note_id = @id", ct, ("@id", id.Value));
        }

        foreach (var s in partages)
        {
            var agent = agents.First(ag => ag.Id == s.AgentId);
            await db.ExecuteAsync(
                """
                INSERT INTO app_note_partages (note_id, agent_id, agent, peut_modifier, partage_le)
                VALUES (@id, @agent, @libelle, @modifier, @maintenant)
                """, ct,
                ("@id", id.Value), ("@agent", agent.Id), ("@libelle", $"{agent.Nom} {agent.Prenom}"), ("@modifier", s.PeutModifier),
                ("@maintenant", maintenant));
        }

        var dto = await ChargerAsync(db, ctx.Agent.Id, id.Value, ct) ?? throw new InvalidOperationException("Note enregistrée introuvable.");
        await db.CommitAsync(ct);
        await PrevenirAsync(destinatairesAvant.Concat(partages.Select(s => s.AgentId)), id.Value, ctx.Agent.Id);
        return dto;
    }

    public async Task SupprimerAsync(AgentContext ctx, long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var note = await ChargerAsync(db, ctx.Agent.Id, id, ct, verrouiller: true) ?? throw ApiException.Introuvable("Note introuvable.");
        if (!note.EstAuteur)
        {
            // Un destinataire retire la note de sa liste (fin du partage pour lui).
            await db.ExecuteAsync("DELETE FROM app_note_partages WHERE note_id = @id AND agent_id = @moi", ct, ("@id", id), ("@moi", ctx.Agent.Id));
            await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Note, note.Reference, "Retrait du partage", null, null, null, null, ct);
        }
        else
        {
            await db.ExecuteAsync("UPDATE app_notes SET supprimee = 1, maj_le = @maintenant, version = version + 1 WHERE id = @id", ct,
                ("@maintenant", DateTime.UtcNow), ("@id", id));
            await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Note, note.Reference, "Suppression", null, note.Titre, null, null, ct);
        }
        await db.CommitAsync(ct);
        await PrevenirAsync(note.Partages.Select(s => s.AgentId).Append(note.AuteurId), id, ctx.Agent.Id);
    }

    private async Task PrevenirAsync(IEnumerable<long> agentIds, long noteId, long moi)
    {
        var cibles = agentIds.Where(a => a != moi).Distinct().ToList();
        if (cibles.Count == 0)
            return;
        await using var db = await saphirDb.OpenAsync(CancellationToken.None);
        foreach (var discord in await ComplementsOutils.DiscordIdsAsync(db, cibles, CancellationToken.None))
            await hub.SendToUserAsync(discord, new RealtimeMessage(ComplementsRealtime.Note,
                Text: noteId.ToString(CultureInfo.InvariantCulture), AppId: SaphirApps.Notes), CancellationToken.None);
    }

    private static string Apercu(string texte) => texte.Length <= 300 ? texte : texte[..300] + "…";

    private static async Task<NoteDto?> ChargerAsync(SqlSession db, long moi, long id, CancellationToken ct, bool verrouiller = false)
    {
        var partages = await db.QueryAsync(
            "SELECT agent_id, agent, peut_modifier FROM app_note_partages WHERE note_id = @id ORDER BY agent",
            r => new NotePartageDto(r.Long("agent_id"), r.Text("agent"), r.Bool("peut_modifier")), ct, ("@id", id));
        var note = await db.FirstOrDefaultAsync(
            "SELECT * FROM app_notes WHERE id = @id AND supprimee = 0" + (verrouiller ? " FOR UPDATE" : string.Empty),
            r =>
            {
                var auteur = r.Long("auteur_id");
                var estAuteur = auteur == moi;
                var partage = partages.FirstOrDefault(s => s.AgentId == moi);
                return new NoteDto(
                    r.Long("id"), ComplementsOutils.Reference("NOTE", r.Long("id")), r.Text("titre"), r.Text("contenu"),
                    r.NullableText("categorie"), r.Bool("epinglee"), auteur, r.Text("auteur"), estAuteur,
                    estAuteur || partage?.PeutModifier == true,
                    estAuteur ? partages : partages.Where(s => s.AgentId == moi).ToList(),
                    r.Date("cree_le"), r.Date("maj_le"), r.Int("version"));
            }, ct, ("@id", id));
        if (note is null || (!note.EstAuteur && note.Partages.Count == 0))
            return null;
        return note;
    }
}

internal static class NotesEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/notes");
        string[] p = [Permissions.NotesUtiliser];

        g.MapGet("", (HttpContext http, NotesStore store, string? q, string? categorie, string? filtre) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.ListerAsync(ctx, q, categorie, filtre, ct))));

        g.MapGet("/categories", (HttpContext http, NotesStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.CategoriesAsync(ctx, ct))));

        g.MapGet("/{id:long}", (HttpContext http, long id, NotesStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.LireAsync(ctx, id, ct))));

        g.MapGet("/{id:long}/historique", (HttpContext http, long id, NotesStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.HistoriqueAsync(ctx, id, ct))));

        g.MapPost("", (HttpContext http, NoteInput input, NotesStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.EnregistrerAsync(ctx, null, input, ct))));

        g.MapPut("/{id:long}", (HttpContext http, long id, NoteInput input, NotesStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.EnregistrerAsync(ctx, id, input, ct))));

        g.MapDelete("/{id:long}", (HttpContext http, long id, NotesStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) =>
            {
                await store.SupprimerAsync(ctx, id, ct);
                return Results.NoContent();
            }));
    }
}
