using Saphir.Server.Data;
using Saphir.Server.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Complements;

namespace Saphir.Server.Modules.Complements;

/// <summary>
/// ANNUAIRE (cahier § 18.1). Les informations professionnelles des agents (grade, service, unité, fonction) viennent du
/// module RH (GET /api/referentiels/agents, appelé par le poste) ; ce module gère les coordonnées professionnelles
/// fictives et l'annuaire des services. Repli : GET /api/annuaire/agents lit les colonnes du socle.
/// </summary>
internal static class AnnuaireEndpoints
{
    /// <summary>Permissions des applications qui ont besoin de choisir un agent (destinataire, membre, porteur…).</summary>
    public static readonly string[] ChoixAgents =
    [
        Permissions.AnnuaireConsulter, Permissions.MessagerieUtiliser, Permissions.TchapUtiliser, Permissions.NotesUtiliser,
        Permissions.BodycamGerer, Permissions.NeoUtiliser
    ];

    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/annuaire");

        g.MapGet("/agents", (HttpContext http, SaphirDb saphirDb, string? q) =>
            Api.RunAsync(http, ChoixAgents, async (_, ct) =>
            {
                await using var db = await saphirDb.OpenAsync(ct);
                var sql = "SELECT id, reference, nom, prenom, rio, statut FROM rh_agents WHERE statut = @actif";
                var parametres = new List<(string, object?)> { ("@actif", AgentStatuts.Actif) };
                if (!string.IsNullOrWhiteSpace(q))
                {
                    sql += " AND (nom LIKE @q OR prenom LIKE @q OR rio LIKE @q OR reference LIKE @q)";
                    parametres.Add(("@q", "%" + SocleRegles.EchapperLike(q.Trim()) + "%"));
                }
                sql += " ORDER BY nom, prenom LIMIT 1000";
                return Results.Ok(await db.QueryAsync(sql, r => new AnnuaireAgentDto(
                    r.Long("id"), r.Text("reference"), r.Text("nom"), r.Text("prenom"), r.NullableText("rio"), r.Text("statut")),
                    ct, parametres.ToArray()));
            }));

        // ----- Coordonnées professionnelles -----

        g.MapGet("/coordonnees", (HttpContext http, SaphirDb saphirDb) =>
            Api.RunAsync(http, ChoixAgents, async (_, ct) =>
            {
                await using var db = await saphirDb.OpenAsync(ct);
                return Results.Ok(await db.QueryAsync(
                    "SELECT agent_id, telephone, indicatif, poste, email, maj_le FROM app_annuaire_coordonnees",
                    LireCoordonnees, ct));
            }));

        g.MapPut("/coordonnees/moi", (HttpContext http, CoordonneesInput input, SaphirDb saphirDb, RealtimeHub hub) =>
            Api.RunAsync(http, [Permissions.AnnuaireConsulter], async (ctx, ct) =>
            {
                var dto = await EnregistrerCoordonneesAsync(saphirDb, ctx, ctx.Agent.Id, input, ct);
                await hub.BroadcastAsync(new RealtimeMessage(ComplementsRealtime.Annuaire, Text: "coordonnees"), CancellationToken.None);
                return Results.Ok(dto);
            }));

        g.MapPut("/coordonnees/{agentId:long}", (HttpContext http, long agentId, CoordonneesInput input, SaphirDb saphirDb, RealtimeHub hub) =>
            Api.RunAsync(http, [Permissions.AdminComptes], async (ctx, ct) =>
            {
                var dto = await EnregistrerCoordonneesAsync(saphirDb, ctx, agentId, input, ct);
                await hub.BroadcastAsync(new RealtimeMessage(ComplementsRealtime.Annuaire, Text: "coordonnees"), CancellationToken.None);
                return Results.Ok(dto);
            }));

        // ----- Annuaire des services -----

        g.MapGet("/services", (HttpContext http, SaphirDb saphirDb, bool? tous) =>
            Api.RunAsync(http, ChoixAgents, async (ctx, ct) =>
            {
                await using var db = await saphirDb.OpenAsync(ct);
                var inactifs = tous == true && ctx.Droits.Possede(Permissions.AdminParametres);
                return Results.Ok(await db.QueryAsync(
                    "SELECT * FROM app_annuaire_services" + (inactifs ? string.Empty : " WHERE actif = 1") + " ORDER BY ordre, nom",
                    LireService, ct));
            }));

        g.MapPost("/services", (HttpContext http, ServiceAnnuaireInput input, SaphirDb saphirDb, RealtimeHub hub) =>
            Api.RunAsync(http, [Permissions.AdminParametres], async (ctx, ct) =>
            {
                var dto = await EnregistrerServiceAsync(saphirDb, ctx, null, input, ct);
                await hub.BroadcastAsync(new RealtimeMessage(ComplementsRealtime.Annuaire, Text: "services"), CancellationToken.None);
                return Results.Ok(dto);
            }));

        g.MapPut("/services/{id:long}", (HttpContext http, long id, ServiceAnnuaireInput input, SaphirDb saphirDb, RealtimeHub hub) =>
            Api.RunAsync(http, [Permissions.AdminParametres], async (ctx, ct) =>
            {
                var dto = await EnregistrerServiceAsync(saphirDb, ctx, id, input, ct);
                await hub.BroadcastAsync(new RealtimeMessage(ComplementsRealtime.Annuaire, Text: "services"), CancellationToken.None);
                return Results.Ok(dto);
            }));
    }

    private static CoordonneesDto LireCoordonnees(SqlRow r) => new(
        r.Long("agent_id"), r.NullableText("telephone"), r.NullableText("indicatif"), r.NullableText("poste"),
        r.NullableText("email"), r.Date("maj_le"));

    private static ServiceAnnuaireDto LireService(SqlRow r) => new(
        r.Long("id"), r.Text("nom"), r.NullableText("categorie"), r.NullableText("indicatif"), r.NullableText("telephone"),
        r.NullableText("frequence"), r.NullableText("adresse"), r.NullableText("horaires"), r.NullableText("observations"),
        r.Int("ordre"), r.Bool("actif"));

    private static async Task<CoordonneesDto> EnregistrerCoordonneesAsync(SaphirDb saphirDb, AgentContext ctx, long agentId,
        CoordonneesInput input, CancellationToken ct)
    {
        var telephone = ComplementsOutils.Facultatif(input.Telephone, 40);
        var indicatif = ComplementsOutils.Facultatif(input.Indicatif, 40);
        var poste = ComplementsOutils.Facultatif(input.Poste, 80);
        var email = ComplementsOutils.Facultatif(input.Email, 120);
        if (telephone is not null && !telephone.All(c => char.IsAsciiDigit(c) || c is ' ' or '+' or '.' or '-' or '(' or ')'))
            throw ApiException.Saisie("Le téléphone ne contient que des chiffres, espaces et + . - ( ).");
        if (email is not null && (!email.Contains('@') || email.Contains(' ')))
            throw ApiException.Saisie("Adresse de messagerie professionnelle invalide.");

        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var agent = await ComplementsOutils.AgentAsync(db, agentId, ct) ?? throw ApiException.Introuvable("Agent introuvable.");
        var avant = await db.FirstOrDefaultAsync(
            "SELECT agent_id, telephone, indicatif, poste, email, maj_le FROM app_annuaire_coordonnees WHERE agent_id = @id FOR UPDATE",
            LireCoordonnees, ct, ("@id", agentId));
        await db.ExecuteAsync(
            """
            INSERT INTO app_annuaire_coordonnees (agent_id, telephone, indicatif, poste, email, maj_le)
            VALUES (@id, @telephone, @indicatif, @poste, @email, @maintenant)
            ON DUPLICATE KEY UPDATE telephone = @telephone, indicatif = @indicatif, poste = @poste, email = @email, maj_le = @maintenant
            """, ct,
            ("@id", agentId), ("@telephone", telephone), ("@indicatif", indicatif), ("@poste", poste), ("@email", email),
            ("@maintenant", DateTime.UtcNow));

        // Une ligne d'historique par champ modifié.
        foreach (var (champ, ancienne, nouvelle) in new[]
                 {
                     ("telephone", avant?.Telephone, telephone), ("indicatif", avant?.Indicatif, indicatif),
                     ("poste", avant?.Poste, poste), ("email", avant?.Email, email)
                 })
        {
            if (ancienne != nouvelle)
                await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Coordonnees, agent.Reference, "Modification",
                    champ, ancienne, nouvelle, null, ct);
        }
        await db.CommitAsync(ct);
        return new CoordonneesDto(agentId, telephone, indicatif, poste, email, DateTimeOffset.UtcNow);
    }

    private static async Task<ServiceAnnuaireDto> EnregistrerServiceAsync(SaphirDb saphirDb, AgentContext ctx, long? id,
        ServiceAnnuaireInput input, CancellationToken ct)
    {
        var nom = ComplementsOutils.Obligatoire(input.Nom, 120, "Nom du service");
        var valeurs = new (string Champ, string? Valeur)[]
        {
            ("categorie", ComplementsOutils.Facultatif(input.Categorie, 80)),
            ("indicatif", ComplementsOutils.Facultatif(input.Indicatif, 40)),
            ("telephone", ComplementsOutils.Facultatif(input.Telephone, 40)),
            ("frequence", ComplementsOutils.Facultatif(input.Frequence, 40)),
            ("adresse", ComplementsOutils.Facultatif(input.Adresse, 250)),
            ("horaires", ComplementsOutils.Facultatif(input.Horaires, 120)),
            ("observations", ComplementsOutils.Facultatif(input.Observations, 500)),
        };
        var maintenant = DateTime.UtcNow;

        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var doublon = await db.LongAsync(
            "SELECT COUNT(*) FROM app_annuaire_services WHERE nom = @nom AND id <> @id", ct, ("@nom", nom), ("@id", id ?? 0));
        if (doublon > 0)
            throw ApiException.Conflit("Un service porte déjà ce nom.");

        var parametres = new List<(string, object?)>
        {
            ("@nom", nom), ("@ordre", input.Ordre), ("@actif", input.Actif), ("@maintenant", maintenant)
        };
        parametres.AddRange(valeurs.Select(v => ("@" + v.Champ, (object?)v.Valeur)));

        if (id is null)
        {
            var nouveau = await db.InsertAsync(
                """
                INSERT INTO app_annuaire_services (nom, categorie, indicatif, telephone, frequence, adresse, horaires, observations, ordre, actif, cree_le, maj_le)
                VALUES (@nom, @categorie, @indicatif, @telephone, @frequence, @adresse, @horaires, @observations, @ordre, @actif, @maintenant, @maintenant)
                """, ct, parametres.ToArray());
            await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.ServiceAnnuaire, nouveau.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "Création", null, null, nom, null, ct);
            id = nouveau;
        }
        else
        {
            var avant = await db.FirstOrDefaultAsync("SELECT * FROM app_annuaire_services WHERE id = @id FOR UPDATE", LireService, ct, ("@id", id.Value))
                        ?? throw ApiException.Introuvable("Service introuvable.");
            parametres.Add(("@id", id.Value));
            await db.ExecuteAsync(
                """
                UPDATE app_annuaire_services SET nom = @nom, categorie = @categorie, indicatif = @indicatif, telephone = @telephone,
                    frequence = @frequence, adresse = @adresse, horaires = @horaires, observations = @observations,
                    ordre = @ordre, actif = @actif, maj_le = @maintenant
                WHERE id = @id
                """, ct, parametres.ToArray());
            var reference = id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var anciennes = new Dictionary<string, string?>
            {
                ["nom"] = avant.Nom, ["categorie"] = avant.Categorie, ["indicatif"] = avant.Indicatif, ["telephone"] = avant.Telephone,
                ["frequence"] = avant.Frequence, ["adresse"] = avant.Adresse, ["horaires"] = avant.Horaires,
                ["observations"] = avant.Observations, ["ordre"] = avant.Ordre.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["actif"] = avant.Actif ? "oui" : "non"
            };
            var nouvelles = valeurs.ToDictionary(v => v.Champ, v => v.Valeur);
            nouvelles["nom"] = nom;
            nouvelles["ordre"] = input.Ordre.ToString(System.Globalization.CultureInfo.InvariantCulture);
            nouvelles["actif"] = input.Actif ? "oui" : "non";
            foreach (var (champ, ancienne) in anciennes)
                if (ancienne != nouvelles[champ])
                    await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.ServiceAnnuaire, reference, "Modification",
                        champ, ancienne, nouvelles[champ], null, ct);
        }

        var dto = await db.FirstOrDefaultAsync("SELECT * FROM app_annuaire_services WHERE id = @id", LireService, ct, ("@id", id.Value))
                  ?? throw new InvalidOperationException("Service enregistré introuvable.");
        await db.CommitAsync(ct);
        return dto;
    }
}
