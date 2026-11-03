using System.Globalization;
using System.Text;
using System.Text.Json;
using Saphir.Server.Data;
using Saphir.Server.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Operations;

namespace Saphir.Server.Modules.Operations;

/// <summary>Aides SQL communes au module (dates UTC, listes IN, lectures tolérantes).</summary>
internal static class OpSql
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static DateTime Utc(DateTimeOffset date) => date.UtcDateTime;

    public static DateTime? Utc(DateTimeOffset? date) => date?.UtcDateTime;

    public static string? T(string? texte, int max) => SocleRegles.Tronquer(texte, max);

    public static object? Param(DateOnly? date) => date?.ToDateTime(TimeOnly.MinValue);

    public static DateOnly? DateOnlyDe(SqlRow r, string colonne) => r.Value(colonne) switch
    {
        DateTime d => DateOnly.FromDateTime(d),
        DateOnly d => d,
        _ => null
    };

    public static double? Double(SqlRow r, string colonne) => r.Value(colonne) switch
    {
        null => null,
        var v => Convert.ToDouble(v, CultureInfo.InvariantCulture)
    };

    public static int? NullableInt(SqlRow r, string colonne) => r.Value(colonne) switch
    {
        null => null,
        var v => Convert.ToInt32(v, CultureInfo.InvariantCulture)
    };

    /// <summary>« @p0, @p1 » pour une clause IN, paramètres ajoutés à <paramref name="parametres"/>.</summary>
    public static string In(string prefixe, IReadOnlyList<long> ids, List<(string Name, object? Value)> parametres)
    {
        if (ids.Count == 0)
            return "NULL";
        var noms = new StringBuilder();
        for (var i = 0; i < ids.Count; i++)
        {
            var nom = "@" + prefixe + i.ToString(CultureInfo.InvariantCulture);
            if (i > 0) noms.Append(", ");
            noms.Append(nom);
            parametres.Add((nom, ids[i]));
        }
        return noms.ToString();
    }

    public static string Serialiser<T>(T valeur) => JsonSerializer.Serialize(valeur, Json);

    public static T? Lire<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Filtre LIKE « %texte% » échappé.</summary>
    public static string Like(string texte) => "%" + SocleRegles.EchapperLike(texte.Trim()) + "%";

    /// <summary>Contrôle de modification concurrente : la version lue par le poste doit être la version en base.</summary>
    public static void VerifierVersion(long? attendue, long actuelle)
    {
        if (attendue is { } v && v != actuelle)
            throw ApiException.Conflit("Cette fiche a été modifiée entre-temps par un autre agent : recharge-la avant d'enregistrer.");
    }

    /// <summary>Écrit dans l'historique chaque différence entre deux états.</summary>
    public static async Task HistoriserDifferencesAsync(SqlSession db, Auteur auteur, string objet, string reference,
        string prefixe, object? ancien, object? nouveau, string? motif, CancellationToken ct)
    {
        foreach (var (champ, a, n) in OperationsRegles.Differences(prefixe, ancien, nouveau))
            await Historique.EcrireAsync(db, auteur, objet, reference, "Modification", champ, a, n, motif, ct);
    }

    /// <summary>Historique d'un ensemble d'objets du module.</summary>
    public static async Task<List<HistoriqueDto>> HistoriqueAsync(SqlSession db, IReadOnlyList<string> objets, string? reference,
        string? q, int limite, CancellationToken ct)
    {
        var parametres = new List<(string Name, object? Value)>();
        var noms = new StringBuilder();
        for (var i = 0; i < objets.Count; i++)
        {
            if (i > 0) noms.Append(", ");
            noms.Append("@o").Append(i.ToString(CultureInfo.InvariantCulture));
            parametres.Add(("@o" + i.ToString(CultureInfo.InvariantCulture), objets[i]));
        }
        var sql = new StringBuilder(
            $"SELECT id, `date`, auteur, objet, reference, `action`, champ, ancienne, nouvelle, motif FROM sys_historique WHERE objet IN ({noms})");
        if (!string.IsNullOrWhiteSpace(reference))
        {
            sql.Append(" AND reference = @reference");
            parametres.Add(("@reference", reference.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            sql.Append(" AND (reference LIKE @q OR auteur LIKE @q OR `action` LIKE @q OR champ LIKE @q)");
            parametres.Add(("@q", Like(q)));
        }
        sql.Append(" ORDER BY `date` DESC, id DESC LIMIT @limite");
        parametres.Add(("@limite", limite));
        return await db.QueryAsync(sql.ToString(), r => new HistoriqueDto(
            r.Long("id"), r.Date("date"), r.Text("auteur"), r.Text("objet"), r.Text("reference"), r.Text("action"),
            r.NullableText("champ"), r.NullableText("ancienne"), r.NullableText("nouvelle"), r.NullableText("motif")),
            ct, parametres.ToArray());
    }
}

/// <summary>Agent du répertoire RH (lecture seule de rh_agents).</summary>
internal sealed record AgentRh(long Id, string Reference, string DiscordId, string Nom, string Prenom, string? Rio, string Statut)
{
    public string Libelle => $"{Nom} {Prenom}";

    /// <summary>Libellé proposé par le poste (avec grade) s'il désigne bien cet agent, sinon « NOM Prénom ».</summary>
    public string LibelleDepuis(string? propose)
    {
        var p = SocleRegles.Tronquer(propose, 200);
        return p is not null && p.Contains(Nom, StringComparison.OrdinalIgnoreCase) ? p : Libelle;
    }

    public static async Task<Dictionary<long, AgentRh>> LireAsync(SqlSession db, IEnumerable<long> ids, CancellationToken ct)
    {
        var liste = ids.Distinct().ToList();
        if (liste.Count == 0)
            return new Dictionary<long, AgentRh>();
        var parametres = new List<(string Name, object? Value)>();
        var clause = OpSql.In("ag", liste, parametres);
        var agents = await db.QueryAsync(
            $"SELECT id, reference, discord_id, nom, prenom, rio, statut FROM rh_agents WHERE id IN ({clause})",
            r => new AgentRh(r.Long("id"), r.Text("reference"), r.Text("discord_id"), r.Text("nom"), r.Text("prenom"),
                r.NullableText("rio"), r.Text("statut")), ct, parametres.ToArray());
        return agents.ToDictionary(a => a.Id);
    }
}

/// <summary>Événement du journal de service (9.4), écrit dans la transaction de l'action qui le provoque.</summary>
internal sealed record EvenementService(string Type, string Libelle)
{
    public string? Details { get; init; }
    public string Source { get; init; } = "Main courante";
    public long? VacationId { get; init; }
    public long? EquipageId { get; init; }
    public long? VehiculeId { get; init; }
    public long? InterventionId { get; init; }
    public string? InterventionNumero { get; init; }
    public long? MentionId { get; init; }

    public Task EcrireAsync(SqlSession db, Auteur auteur, CancellationToken ct)
        => db.ExecuteAsync(
            """
            INSERT INTO mc_journal (`date`, type, libelle, details, source, auteur_id, auteur, vacation_id, equipage_id,
                                    vehicule_id, intervention_id, intervention_numero, mention_id)
            VALUES (@date, @type, @libelle, @details, @source, @auteur_id, @auteur, @vacation, @equipage,
                    @vehicule, @intervention, @numero, @mention)
            """, ct,
            ("@date", DateTime.UtcNow),
            ("@type", Type),
            ("@libelle", OpSql.T(Libelle, 300) ?? Type),
            ("@details", OpSql.T(Details, 4000)),
            ("@source", Source),
            ("@auteur_id", auteur.Id),
            ("@auteur", OpSql.T(auteur.Nom, 200) ?? "?"),
            ("@vacation", VacationId),
            ("@equipage", EquipageId),
            ("@vehicule", VehiculeId),
            ("@intervention", InterventionId),
            ("@numero", InterventionNumero),
            ("@mention", MentionId));
}

/// <summary>
/// Notifications du module : messages temps réel aux postes (identifiants seulement) et webhook Discord PÉGASE.
/// Les envois sont faits après validation de la transaction et n'échouent jamais l'action de l'agent.
/// </summary>
public sealed class OperationsNotifier(
    RealtimeHub hub,
    ParametresService parametres,
    IHttpClientFactory httpClients,
    ILogger<OperationsNotifier> logger)
{
    public const string ClientDiscord = "saphir-pegase-discord";

    public Task MainCouranteAsync(string objet) => DiffuserAsync(new RealtimeMessage(OperationsTypes.MainCouranteChanged, Text: objet, AppId: SaphirApps.MainCourante));

    public Task PegaseAsync(string objet) => DiffuserAsync(new RealtimeMessage(OperationsTypes.PegaseChanged, Text: objet, AppId: SaphirApps.Pegase));

    public Task PositionsAsync(int nombre) => DiffuserAsync(new RealtimeMessage(OperationsTypes.GeoopsPositions,
        Text: nombre.ToString(CultureInfo.InvariantCulture), AppId: SaphirApps.Pegase));

    /// <summary>Bulle de notification sur les postes des agents désignés (titre et texte sans donnée personnelle).</summary>
    public async Task AgentsAsync(IEnumerable<string> discordIds, string titre, string texte, string appId)
    {
        foreach (var discordId in discordIds.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct(StringComparer.Ordinal))
        {
            try
            {
                await hub.SendToUserAsync(discordId, new RealtimeMessage(RealtimeTypes.Notify, Title: titre, Text: texte, AppId: appId), CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Notification d'un poste impossible");
            }
        }
    }

    /// <summary>Ouvre une application sur les postes d'un agent (SIMCALL : prise d'appel).</summary>
    public async Task<int> OuvrirApplicationAsync(string discordId, string appId, string argument)
    {
        try
        {
            return await hub.SendToUserAsync(discordId, new RealtimeMessage(OperationsTypes.OuvrirApplication, Text: argument, AppId: appId), CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Ouverture d'application sur le poste impossible");
            return 0;
        }
    }

    /// <summary>Webhook Discord d'une nouvelle intervention (8.3), envoyé en arrière-plan.</summary>
    public void DiscordNouvelleIntervention(string numero, DateTimeOffset debut, string nature, string? commune, string priorite, int? moyens, string statut)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var url = (await parametres.ValeurAsync(OperationsRegles.ParamWebhook, CancellationToken.None)).Trim();
                if (!OperationsRegles.WebhookValide(url))
                    return;
                var organisation = await parametres.ValeurAsync(ParametresCles.NomOrganisation, CancellationToken.None);
                var contenu = OperationsRegles.MessageDiscord(numero, debut, nature, commune, priorite, moyens, statut,
                    string.IsNullOrWhiteSpace(organisation) ? "SAPHIR" : organisation);
                using var client = httpClients.CreateClient(ClientDiscord);
                client.Timeout = TimeSpan.FromSeconds(10);
                using var corps = new StringContent(contenu, Encoding.UTF8, "application/json");
                using var reponse = await client.PostAsync(url, corps);
                if (!reponse.IsSuccessStatusCode)
                    logger.LogWarning("Webhook Discord PÉGASE : réponse {Code} pour {Numero}", (int)reponse.StatusCode, numero);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Webhook Discord PÉGASE : envoi impossible pour {Numero}", numero);
            }
        });
    }

    private async Task DiffuserAsync(RealtimeMessage message)
    {
        try
        {
            await hub.BroadcastAsync(message, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Diffusion temps réel impossible");
        }
    }
}
