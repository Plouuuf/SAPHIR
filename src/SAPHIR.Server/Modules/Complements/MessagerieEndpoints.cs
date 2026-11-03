using System.Globalization;
using Saphir.Server.Data;
using Saphir.Server.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Complements;

namespace Saphir.Server.Modules.Complements;

/// <summary>
/// MESSAGERIE interne (cahier § 18.2) : boîte de réception, envoyés, brouillons, corbeille, conversations,
/// destinataires multiples, accusés de lecture, pièces jointes (MEDIUMBLOB, 10 Mo), références SAPHIR,
/// notification temps réel du destinataire. Un message envoyé n'est plus modifiable.
/// État d'un destinataire (colonne supprime) : 0 = boîte de réception, 1 = corbeille, 2 = effacé.
/// </summary>
internal sealed class MessagerieStore(SaphirDb saphirDb, RealtimeHub hub)
{
    private const int MaxDestinataires = 200;
    private const int MaxLiens = 20;
    private const int MaxCorps = 100_000;

    private const string ColonnesResume =
        """
        m.id, m.reference, m.conversation_id, m.sujet, m.expediteur_id, m.expediteur, m.important, m.brouillon,
        COALESCE(m.envoye_le, m.maj_le) AS date_tri,
        (SELECT GROUP_CONCAT(x.agent ORDER BY x.type, x.agent SEPARATOR ', ') FROM app_message_destinataires x WHERE x.message_id = m.id) AS liste,
        (SELECT COUNT(*) FROM app_message_pieces p WHERE p.message_id = m.id) AS nb_pieces
        """;

    public async Task<List<MessageResumeDto>> ListerAsync(AgentContext ctx, string? dossier, string? q, CancellationToken ct)
    {
        var moi = ctx.Agent.Id;
        var parametres = new List<(string, object?)> { ("@moi", moi) };
        string sql;
        switch (dossier ?? MessagerieDossiers.Reception)
        {
            case MessagerieDossiers.Reception:
            case MessagerieDossiers.Corbeille:
                sql = $"""
                    SELECT {ColonnesResume}, d.non_lu
                    FROM app_messages m
                    JOIN app_message_destinataires d ON d.message_id = m.id AND d.agent_id = @moi
                    WHERE m.brouillon = 0 AND d.supprime = @etat
                    """;
                parametres.Add(("@etat", dossier == MessagerieDossiers.Corbeille ? 1 : 0));
                break;
            case MessagerieDossiers.Envoyes:
                sql = $"SELECT {ColonnesResume}, 0 AS non_lu FROM app_messages m WHERE m.expediteur_id = @moi AND m.brouillon = 0 AND m.masque_expediteur = 0";
                break;
            case MessagerieDossiers.Brouillons:
                sql = $"SELECT {ColonnesResume}, 0 AS non_lu FROM app_messages m WHERE m.expediteur_id = @moi AND m.brouillon = 1";
                break;
            default:
                throw ApiException.Saisie("Dossier inconnu.");
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            sql += " AND (m.sujet LIKE @q OR m.corps LIKE @q OR m.expediteur LIKE @q OR m.reference LIKE @q)";
            parametres.Add(("@q", "%" + SocleRegles.EchapperLike(q.Trim()) + "%"));
        }
        sql += " ORDER BY date_tri DESC, m.id DESC LIMIT 300";

        await using var db = await saphirDb.OpenAsync(ct);
        return await db.QueryAsync(sql, r => new MessageResumeDto(
            r.Long("id"), r.Text("reference"), r.Long("conversation_id"), r.Text("sujet"), r.Long("expediteur_id"),
            r.Text("expediteur"), r.Text("liste"), r.Date("date_tri"), !r.Bool("non_lu"), r.Bool("brouillon"),
            r.Bool("important"), r.Int("nb_pieces")), ct, parametres.ToArray());
    }

    public async Task<MessagerieCompteursDto> CompteursAsync(AgentContext ctx, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var nonLus = await db.LongAsync(
            """
            SELECT COUNT(*) FROM app_message_destinataires d JOIN app_messages m ON m.id = d.message_id
            WHERE d.agent_id = @moi AND d.supprime = 0 AND d.non_lu = 1 AND m.brouillon = 0
            """, ct, ("@moi", ctx.Agent.Id));
        var brouillons = await db.LongAsync(
            "SELECT COUNT(*) FROM app_messages WHERE expediteur_id = @moi AND brouillon = 1", ct, ("@moi", ctx.Agent.Id));
        return new MessagerieCompteursDto((int)nonLus, (int)brouillons);
    }

    /// <summary>Lecture d'un message ; pour un destinataire, marque lu et envoie l'accusé de lecture à l'expéditeur.</summary>
    public async Task<MessageDto> LireAsync(AgentContext ctx, long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var message = await ChargerAsync(db, ctx.Agent.Id, id, ct) ?? throw ApiException.Introuvable("Message introuvable.");

        var destinataire = message.Destinataires.FirstOrDefault(d => d.AgentId == ctx.Agent.Id);
        if (!message.Brouillon && destinataire is not null)
        {
            var premiereLecture = destinataire.LuLe is null;
            await db.ExecuteAsync(
                """
                UPDATE app_message_destinataires SET non_lu = 0, lu_le = COALESCE(lu_le, @maintenant)
                WHERE message_id = @id AND agent_id = @moi
                """, ct, ("@id", id), ("@moi", ctx.Agent.Id), ("@maintenant", DateTime.UtcNow));
            if (premiereLecture)
            {
                message = await ChargerAsync(db, ctx.Agent.Id, id, ct) ?? message;
                if (message.ExpediteurId != ctx.Agent.Id)
                    foreach (var discord in await ComplementsOutils.DiscordIdsAsync(db, [message.ExpediteurId], ct))
                        await hub.SendToUserAsync(discord, new RealtimeMessage(ComplementsRealtime.MessageLu,
                            Text: id.ToString(CultureInfo.InvariantCulture), AppId: SaphirApps.Messagerie), CancellationToken.None);
            }
        }
        return message;
    }

    public async Task<List<MessageDto>> ConversationAsync(AgentContext ctx, long conversationId, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var ids = await db.QueryAsync(
            """
            SELECT m.id FROM app_messages m
            WHERE m.conversation_id = @c AND (
                (m.expediteur_id = @moi AND m.masque_expediteur = 0)
                OR (m.brouillon = 0 AND EXISTS (SELECT 1 FROM app_message_destinataires d
                                                WHERE d.message_id = m.id AND d.agent_id = @moi AND d.supprime < 2)))
            ORDER BY COALESCE(m.envoye_le, m.maj_le), m.id
            LIMIT 200
            """, r => r.Long("id"), ct, ("@c", conversationId), ("@moi", ctx.Agent.Id));
        var liste = new List<MessageDto>();
        foreach (var id in ids)
            if (await ChargerAsync(db, ctx.Agent.Id, id, ct) is { } m)
                liste.Add(m);
        if (liste.Count == 0)
            throw ApiException.Introuvable("Conversation introuvable.");
        return liste;
    }

    public async Task<PieceJointeContenuDto> PieceAsync(AgentContext ctx, long pieceId, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var messageId = await db.ScalarAsync("SELECT message_id FROM app_message_pieces WHERE id = @id", ct, ("@id", pieceId));
        if (messageId is null || !await PeutLireAsync(db, ctx.Agent.Id, Convert.ToInt64(messageId, CultureInfo.InvariantCulture), ct))
            throw ApiException.Introuvable("Pièce jointe introuvable.");
        return await db.FirstOrDefaultAsync(
                   "SELECT id, nom, type_mime, taille, contenu FROM app_message_pieces WHERE id = @id",
                   ComplementsOutils.LirePieceContenu, ct, ("@id", pieceId))
               ?? throw ApiException.Introuvable("Pièce jointe introuvable.");
    }

    /// <summary>Création (id null) ou modification d'un brouillon ; envoi si <see cref="MessageInput.Envoyer"/>.</summary>
    public async Task<MessageDto> EnregistrerAsync(AgentContext ctx, long? id, MessageInput input, CancellationToken ct)
    {
        var moi = ctx.Agent;
        var sujet = ComplementsOutils.Obligatoire(input.Sujet, 200, "Objet");
        var corps = ComplementsOutils.Long(input.Corps, MaxCorps, "Message", obligatoire: input.Envoyer);
        var liste = ComplementsOutils.Facultatif(input.ListeDiffusion, 200);
        var liens = (input.Liens ?? Array.Empty<string>())
            .Select(l => (l ?? string.Empty).Trim().ToUpperInvariant())
            .Where(l => l.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (liens.Count > MaxLiens)
            throw ApiException.Saisie($"{MaxLiens} références SAPHIR au plus par message.");
        if (liens.FirstOrDefault(l => !LiensSaphir.Valide(l)) is { } lienInvalide)
            throw ApiException.Saisie($"Référence SAPHIR invalide : « {lienInvalide} ».");

        var a = (input.Destinataires ?? Array.Empty<long>()).Distinct().ToList();
        var cc = (input.Copies ?? Array.Empty<long>()).Distinct().Where(x => !a.Contains(x)).ToList();
        if (a.Count + cc.Count > MaxDestinataires)
            throw ApiException.Saisie($"{MaxDestinataires} destinataires au plus.");
        if (input.Envoyer && a.Count + cc.Count == 0)
            throw ApiException.Saisie("Choisis au moins un destinataire.");

        var nouvelles = (input.NouvellesPieces ?? Array.Empty<PieceJointeInput>()).Select(ComplementsOutils.DecoderPiece).ToList();
        var conservees = (input.PiecesConservees ?? Array.Empty<long>()).Distinct().ToList();
        if (nouvelles.Count + conservees.Count > PiecesJointes.NombreMaxParMessage)
            throw ApiException.Saisie($"{PiecesJointes.NombreMaxParMessage} pièces jointes au plus par message.");

        var maintenant = DateTime.UtcNow;
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);

        var agents = await ComplementsOutils.AgentsAsync(db, a.Concat(cc), ct);
        var inconnus = a.Concat(cc).Where(x => agents.All(ag => ag.Id != x)).ToList();
        if (inconnus.Count > 0)
            throw ApiException.Saisie("Un destinataire n'existe plus dans SAPHIR. Actualise la liste des agents.");
        var inactifs = agents.Where(ag => !AgentStatuts.PeutSeConnecter(ag.Statut)).ToList();
        if (input.Envoyer && inactifs.Count > 0)
            throw ApiException.Saisie($"Destinataire hors service : {string.Join(", ", inactifs.Select(i => i.Nom + " " + i.Prenom))}.");

        long conversation;
        long? reponseA = null;
        if (input.ReponseA is { } parent)
        {
            if (!await PeutLireAsync(db, moi.Id, parent, ct))
                throw ApiException.Introuvable("Le message auquel tu réponds est introuvable.");
            reponseA = parent;
        }

        if (id is null)
        {
            id = await db.InsertAsync(
                """
                INSERT INTO app_messages (expediteur_id, expediteur, sujet, corps, liste_diffusion, liens, important, brouillon,
                                          reponse_a, version, cree_le, maj_le)
                VALUES (@moi, @expediteur, @sujet, @corps, @liste, @liens, @important, 1, @reponse, 1, @maintenant, @maintenant)
                """, ct,
                ("@moi", moi.Id), ("@expediteur", $"{moi.Nom} {moi.Prenom}"), ("@sujet", sujet), ("@corps", corps),
                ("@liste", liste), ("@liens", liens.Count == 0 ? null : string.Join('\n', liens)), ("@important", input.Important),
                ("@reponse", reponseA), ("@maintenant", maintenant));
            conversation = id.Value;
            if (reponseA is { } p)
                conversation = Convert.ToInt64(await db.ScalarAsync(
                    "SELECT COALESCE(conversation_id, id) FROM app_messages WHERE id = @p", ct, ("@p", p)) ?? id.Value, CultureInfo.InvariantCulture);
            await db.ExecuteAsync("UPDATE app_messages SET reference = @reference, conversation_id = @c WHERE id = @id", ct,
                ("@reference", ComplementsOutils.Reference("MSG", id.Value)), ("@c", conversation), ("@id", id.Value));
        }
        else
        {
            var etat = await db.FirstOrDefaultAsync(
                           "SELECT expediteur_id, brouillon, version FROM app_messages WHERE id = @id FOR UPDATE",
                           r => new { Expediteur = r.Long("expediteur_id"), Brouillon = r.Bool("brouillon"), Version = r.Int("version") },
                           ct, ("@id", id.Value))
                       ?? throw ApiException.Introuvable("Brouillon introuvable.");
            if (etat.Expediteur != moi.Id)
                throw ApiException.Introuvable("Brouillon introuvable.");
            if (!etat.Brouillon)
                throw ApiException.Conflit("Ce message a déjà été envoyé : il n'est plus modifiable.");
            ComplementsOutils.VerifierVersion(input.Version, etat.Version);
            await db.ExecuteAsync(
                """
                UPDATE app_messages SET sujet = @sujet, corps = @corps, liste_diffusion = @liste, liens = @liens,
                    important = @important, version = version + 1, maj_le = @maintenant
                WHERE id = @id
                """, ct,
                ("@sujet", sujet), ("@corps", corps), ("@liste", liste), ("@liens", liens.Count == 0 ? null : string.Join('\n', liens)),
                ("@important", input.Important), ("@maintenant", maintenant), ("@id", id.Value));
            await db.ExecuteAsync("DELETE FROM app_message_destinataires WHERE message_id = @id", ct, ("@id", id.Value));
        }
        var messageId = id.Value;

        // Pièces : celles du brouillon non conservées sont retirées ; celles d'un autre message lisible (transfert) sont copiées.
        var existantes = await db.QueryAsync("SELECT id FROM app_message_pieces WHERE message_id = @id", r => r.Long("id"), ct, ("@id", messageId));
        foreach (var retiree in existantes.Where(e => !conservees.Contains(e)))
            await db.ExecuteAsync("DELETE FROM app_message_pieces WHERE id = @p", ct, ("@p", retiree));
        foreach (var pieceId in conservees.Where(c => !existantes.Contains(c)))
        {
            var source = await db.ScalarAsync("SELECT message_id FROM app_message_pieces WHERE id = @p", ct, ("@p", pieceId));
            if (source is null || !await PeutLireAsync(db, moi.Id, Convert.ToInt64(source, CultureInfo.InvariantCulture), ct))
                throw ApiException.Saisie("Une pièce jointe à transférer est introuvable.");
            await db.ExecuteAsync(
                """
                INSERT INTO app_message_pieces (message_id, nom, type_mime, taille, sha256, contenu, cree_le)
                SELECT @id, nom, type_mime, taille, sha256, contenu, @maintenant FROM app_message_pieces WHERE id = @p
                """, ct, ("@id", messageId), ("@maintenant", maintenant), ("@p", pieceId));
        }
        foreach (var piece in nouvelles)
            await db.ExecuteAsync(
                """
                INSERT INTO app_message_pieces (message_id, nom, type_mime, taille, sha256, contenu, cree_le)
                VALUES (@id, @nom, @type, @taille, @sha, @contenu, @maintenant)
                """, ct,
                ("@id", messageId), ("@nom", piece.Nom), ("@type", piece.TypeMime), ("@taille", piece.Contenu.LongLength),
                ("@sha", piece.Sha256), ("@contenu", piece.Contenu), ("@maintenant", maintenant));

        foreach (var agent in agents)
            await db.ExecuteAsync(
                """
                INSERT INTO app_message_destinataires (message_id, agent_id, agent, type, non_lu, supprime)
                VALUES (@id, @agent, @libelle, @type, 1, 0)
                """, ct,
                ("@id", messageId), ("@agent", agent.Id), ("@libelle", $"{agent.Nom} {agent.Prenom}"),
                ("@type", a.Contains(agent.Id) ? MessagerieTypes.A : MessagerieTypes.Cc));

        var reference = ComplementsOutils.Reference("MSG", messageId);
        if (input.Envoyer)
        {
            await db.ExecuteAsync("UPDATE app_messages SET brouillon = 0, envoye_le = @maintenant WHERE id = @id", ct,
                ("@maintenant", maintenant), ("@id", messageId));
            await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Message, reference, "Envoi", "destinataires", null,
                SocleRegles.Liste(agents.Select(ag => ag.Libelle)), sujet, ct);
        }

        var dto = await ChargerAsync(db, moi.Id, messageId, ct) ?? throw new InvalidOperationException("Message enregistré introuvable.");
        await db.CommitAsync(ct);

        if (input.Envoyer)
        {
            foreach (var agent in agents.Where(ag => ag.DiscordId.Length > 0))
            {
                await hub.SendToUserAsync(agent.DiscordId, new RealtimeMessage(ComplementsRealtime.MessageRecu,
                    Text: messageId.ToString(CultureInfo.InvariantCulture), AppId: SaphirApps.Messagerie), CancellationToken.None);
                await hub.SendToUserAsync(agent.DiscordId, new RealtimeMessage(RealtimeTypes.Notify,
                    Title: input.Important ? "Messagerie — message important" : "Messagerie",
                    Text: $"Nouveau message de {moi.Nom} {moi.Prenom}", AppId: SaphirApps.Messagerie), CancellationToken.None);
            }
        }
        return dto;
    }

    /// <summary>Suppression : brouillon effacé ; message reçu mis à la corbeille puis effacé ; message envoyé retiré des envoyés.</summary>
    public async Task SupprimerAsync(AgentContext ctx, long id, CancellationToken ct)
    {
        var moi = ctx.Agent.Id;
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var message = await db.FirstOrDefaultAsync(
            "SELECT expediteur_id, brouillon, reference, sujet FROM app_messages WHERE id = @id FOR UPDATE",
            r => new { Expediteur = r.Long("expediteur_id"), Brouillon = r.Bool("brouillon"), Reference = r.Text("reference"), Sujet = r.Text("sujet") },
            ct, ("@id", id)) ?? throw ApiException.Introuvable("Message introuvable.");

        var etat = await db.ScalarAsync(
            "SELECT supprime FROM app_message_destinataires WHERE message_id = @id AND agent_id = @moi", ct, ("@id", id), ("@moi", moi));
        var touche = false;
        if (message.Expediteur == moi && message.Brouillon)
        {
            await db.ExecuteAsync("DELETE FROM app_messages WHERE id = @id", ct, ("@id", id));
            await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Message, message.Reference, "Suppression du brouillon",
                null, message.Sujet, null, null, ct);
            touche = true;
        }
        else
        {
            if (!message.Brouillon && etat is not null)
            {
                var nouvelEtat = Convert.ToInt32(etat, CultureInfo.InvariantCulture) == 0 ? 1 : 2;
                await db.ExecuteAsync("UPDATE app_message_destinataires SET supprime = @etat WHERE message_id = @id AND agent_id = @moi", ct,
                    ("@etat", nouvelEtat), ("@id", id), ("@moi", moi));
                touche = true;
            }
            if (message.Expediteur == moi && !message.Brouillon)
            {
                await db.ExecuteAsync("UPDATE app_messages SET masque_expediteur = 1 WHERE id = @id", ct, ("@id", id));
                touche = true;
            }
        }
        if (!touche)
            throw ApiException.Introuvable("Message introuvable.");
        await db.CommitAsync(ct);
    }

    public async Task RestaurerAsync(AgentContext ctx, long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var n = await db.ExecuteAsync(
            "UPDATE app_message_destinataires SET supprime = 0 WHERE message_id = @id AND agent_id = @moi AND supprime = 1", ct,
            ("@id", id), ("@moi", ctx.Agent.Id));
        if (n == 0)
            throw ApiException.Introuvable("Message introuvable dans la corbeille.");
    }

    public async Task MarquerNonLuAsync(AgentContext ctx, long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var n = await db.ExecuteAsync(
            "UPDATE app_message_destinataires SET non_lu = 1 WHERE message_id = @id AND agent_id = @moi", ct,
            ("@id", id), ("@moi", ctx.Agent.Id));
        if (n == 0)
            throw ApiException.Introuvable("Message introuvable.");
    }

    private static async Task<bool> PeutLireAsync(SqlSession db, long moi, long messageId, CancellationToken ct)
        => await db.LongAsync(
            """
            SELECT COUNT(*) FROM app_messages m
            WHERE m.id = @id AND (m.expediteur_id = @moi
                OR (m.brouillon = 0 AND EXISTS (SELECT 1 FROM app_message_destinataires d WHERE d.message_id = m.id AND d.agent_id = @moi)))
            """, ct, ("@id", messageId), ("@moi", moi)) > 0;

    /// <summary>Message complet, ou null s'il n'existe pas ou n'est pas lisible par l'agent.</summary>
    private static async Task<MessageDto?> ChargerAsync(SqlSession db, long moi, long id, CancellationToken ct)
    {
        if (!await PeutLireAsync(db, moi, id, ct))
            return null;
        var destinataires = await db.QueryAsync(
            "SELECT agent_id, agent, type, lu_le FROM app_message_destinataires WHERE message_id = @id ORDER BY type, agent",
            r => new DestinataireDto(r.Long("agent_id"), r.Text("agent"), r.Text("type"), r.NullableDate("lu_le")), ct, ("@id", id));
        var pieces = await db.QueryAsync(
            "SELECT id, nom, type_mime, taille FROM app_message_pieces WHERE message_id = @id ORDER BY id",
            ComplementsOutils.LirePiece, ct, ("@id", id));
        return await db.FirstOrDefaultAsync(
            "SELECT * FROM app_messages WHERE id = @id",
            r =>
            {
                var expediteur = r.Long("expediteur_id");
                // Accusés de lecture visibles par l'expéditeur seulement.
                var liste = expediteur == moi ? destinataires : destinataires.Select(d => d with { LuLe = d.AgentId == moi ? d.LuLe : null }).ToList();
                return new MessageDto(
                    r.Long("id"), r.Text("reference"), r.Long("conversation_id", r.Long("id")), r.NullableLong("reponse_a"),
                    r.Text("sujet"), r.Text("corps"), expediteur, r.Text("expediteur"), r.NullableText("liste_diffusion"),
                    liste, pieces,
                    (r.NullableText("liens") ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries),
                    r.Bool("brouillon"), r.Bool("important"), r.Date("cree_le"), r.NullableDate("envoye_le"), r.Int("version"));
            }, ct, ("@id", id));
    }
}

internal static class MessagerieEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/messagerie");
        string[] p = [Permissions.MessagerieUtiliser];

        g.MapGet("/messages", (HttpContext http, MessagerieStore store, string? dossier, string? q) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.ListerAsync(ctx, dossier, q, ct))));

        g.MapGet("/compteurs", (HttpContext http, MessagerieStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.CompteursAsync(ctx, ct))));

        g.MapGet("/messages/{id:long}", (HttpContext http, long id, MessagerieStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.LireAsync(ctx, id, ct))));

        g.MapGet("/conversations/{id:long}", (HttpContext http, long id, MessagerieStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.ConversationAsync(ctx, id, ct))));

        g.MapPost("/messages", (HttpContext http, MessageInput input, MessagerieStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.EnregistrerAsync(ctx, null, input, ct))));

        g.MapPut("/messages/{id:long}", (HttpContext http, long id, MessageInput input, MessagerieStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.EnregistrerAsync(ctx, id, input, ct))));

        g.MapDelete("/messages/{id:long}", (HttpContext http, long id, MessagerieStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) =>
            {
                await store.SupprimerAsync(ctx, id, ct);
                return Results.NoContent();
            }));

        g.MapPost("/messages/{id:long}/restaurer", (HttpContext http, long id, MessagerieStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) =>
            {
                await store.RestaurerAsync(ctx, id, ct);
                return Results.NoContent();
            }));

        g.MapPost("/messages/{id:long}/non-lu", (HttpContext http, long id, MessagerieStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) =>
            {
                await store.MarquerNonLuAsync(ctx, id, ct);
                return Results.NoContent();
            }));

        g.MapGet("/pieces/{id:long}", (HttpContext http, long id, MessagerieStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.PieceAsync(ctx, id, ct))));
    }
}
