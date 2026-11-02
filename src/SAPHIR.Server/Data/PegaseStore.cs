using Microsoft.Extensions.Options;
using MySqlConnector;
using Saphir.Server.Services;
using Saphir.Shared.Contracts;

namespace Saphir.Server.Data;

/// <summary>
/// Données de PEGASE (appels 17, événements, chronologies, équipages) dans la base SAPHIR.
/// Chaque modification prévient tous les postes (« pegase-changed ») pour qu'ils rechargent l'état.
/// </summary>
public sealed class PegaseStore(IOptionsMonitor<DatabaseOptions> options, RealtimeHub hub, ILogger<PegaseStore> logger)
{
    private static readonly TimeSpan ClosedVisibleFor = TimeSpan.FromHours(12);
    private static readonly TimeSpan PendingCallsVisibleFor = TimeSpan.FromHours(2);

    private Task<MySqlConnection> OpenAsync(CancellationToken ct) => Sql.OpenAsync(options.CurrentValue.Saphir, ct);

    // =====================================================================
    // Lecture
    // =====================================================================

    public async Task<PegaseStateDto> GetStateAsync(CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        var since = DateTime.UtcNow - ClosedVisibleFor;
        var events = await QueryEventsAsync(connection,
            "e.statut <> @termine OR e.clos_le >= @since", ct,
            ("@termine", EvenementStatuts.Termine), ("@since", since));
        var crews = await QueryCrewsAsync(connection, ct);
        var calls = await Sql.QueryAsync(connection,
            "SELECT * FROM pegase_appels WHERE statut = @statut AND decroche_le >= @since ORDER BY decroche_le DESC",
            ReadCall, ct, ("@statut", AppelStatuts.EnCours), ("@since", DateTime.UtcNow - PendingCallsVisibleFor));
        return new PegaseStateDto(events, crews, calls);
    }

    public async Task<EvenementDetailDto?> GetEventAsync(long id, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        var ev = (await QueryEventsAsync(connection, "e.id = @id", ct, ("@id", id))).FirstOrDefault();
        if (ev is null)
            return null;
        var messages = await Sql.QueryAsync(connection,
            "SELECT * FROM pegase_messages WHERE evenement_id = @id ORDER BY date_utc, id",
            r => new PegaseMessageDto(r.Long("id"), r.Utc("date_utc"), r.Text("auteur"), r.Text("type"), r.Text("texte")),
            ct, ("@id", id));
        var call = (await Sql.QueryAsync(connection, "SELECT * FROM pegase_appels WHERE evenement_id = @id ORDER BY id LIMIT 1",
            ReadCall, ct, ("@id", id))).FirstOrDefault();
        return new EvenementDetailDto(ev, messages, call);
    }

    public async Task<AppelDto?> GetCallAsync(long id, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        return (await Sql.QueryAsync(connection, "SELECT * FROM pegase_appels WHERE id = @id", ReadCall, ct, ("@id", id))).FirstOrDefault();
    }

    // =====================================================================
    // Appels
    // =====================================================================

    /// <summary>Décroché d'un 17 sur SIMCALL : fiche d'appel créée et ouverte sur le poste de l'opérateur.</summary>
    public async Task<AppelDto> SimcallAnsweredAsync(SimcallCallAnswered message, CancellationToken ct)
    {
        // Identifiant tel qu'enregistré (espaces retirés, 64 caractères) : sert aussi à relire la fiche.
        var simcallId = Sql.Clean(message.SimcallCallId, 64)!;
        // Heure absente du message (valeur par défaut) : heure de réception.
        var answeredAt = message.AnsweredAt == default ? DateTime.UtcNow : message.AnsweredAt.UtcDateTime;

        await using var connection = await OpenAsync(ct);
        await Sql.ExecuteAsync(connection,
            """
            INSERT INTO pegase_appels (simcall_id, numero_requerant, operateur, operateur_id, decroche_le, statut)
            VALUES (@simcall, @numero, @operateur, @operateurId, @decroche, @statut)
            ON DUPLICATE KEY UPDATE operateur = VALUES(operateur), operateur_id = VALUES(operateur_id)
            """, ct,
            ("@simcall", simcallId),
            ("@numero", Sql.Clean(message.CallerNumber, 40)),
            ("@operateur", Sql.Clean(message.OperatorName, 100) ?? "Opérateur"),
            ("@operateurId", Sql.Clean(message.DiscordUserId, 32) ?? string.Empty),
            ("@decroche", answeredAt),
            ("@statut", AppelStatuts.EnCours));

        var call = (await Sql.QueryAsync(connection, "SELECT * FROM pegase_appels WHERE simcall_id = @simcall",
            ReadCall, ct, ("@simcall", simcallId))).First();

        await hub.SendToUserAsync(message.DiscordUserId,
            new RealtimeMessage(RealtimeTypes.OpenCall, Text: call.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), AppId: SaphirApps.Pegase), ct);
        await NotifyChangedAsync(ct);
        return call;
    }

    /// <summary>Raccroché sur SIMCALL : la durée de l'appel est figée.</summary>
    public async Task<bool> SimcallEndedAsync(SimcallCallEnded message, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        var changed = await Sql.ExecuteAsync(connection,
            "UPDATE pegase_appels SET raccroche_le = @fin WHERE simcall_id = @simcall AND raccroche_le IS NULL", ct,
            ("@fin", message.EndedAt == default ? DateTime.UtcNow : message.EndedAt.UtcDateTime),
            ("@simcall", Sql.Clean(message.SimcallCallId, 64)));
        if (changed > 0)
            await NotifyChangedAsync(ct);
        return changed > 0;
    }

    /// <summary>Appel saisi à la main (sans SIMCALL) : ouvre une prise d'appel vierge.</summary>
    public async Task<AppelDto> CreateManualCallAsync(AgentContext agent, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        var id = await Sql.InsertAsync(connection,
            """
            INSERT INTO pegase_appels (numero_requerant, operateur, operateur_id, decroche_le, statut)
            VALUES (NULL, @operateur, @operateurId, @now, @statut)
            """, ct,
            ("@operateur", agent.Name), ("@operateurId", agent.Id), ("@now", DateTime.UtcNow), ("@statut", AppelStatuts.EnCours));
        var call = (await Sql.QueryAsync(connection, "SELECT * FROM pegase_appels WHERE id = @id", ReadCall, ct, ("@id", id))).First();
        await NotifyChangedAsync(ct);
        return call;
    }

    /// <summary>Qualification de l'appel en événement. Renvoie l'identifiant de l'événement.</summary>
    public async Task<long?> CreateEventFromCallAsync(long callId, EvenementInput input, AgentContext agent, CancellationToken ct)
    {
        ValidateEvent(input);
        await using var connection = await OpenAsync(ct);
        var call = (await Sql.QueryAsync(connection, "SELECT * FROM pegase_appels WHERE id = @id", ReadCall, ct, ("@id", callId))).FirstOrDefault();
        if (call is null)
            return null;
        if (call.Statut != AppelStatuts.EnCours)
            throw new ArgumentException("Cet appel a déjà été traité.");

        var eventId = await Sql.InsertAsync(connection,
            """
            INSERT INTO pegase_evenements (nature, priorite, adresse, commune, complement, requerant_nom, requerant_tel, description,
                statut, cree_par, cree_le)
            VALUES (@nature, @priorite, @adresse, @commune, @complement, @nom, @tel, @description, @statut, @agent, @now)
            """, ct,
            [.. EventParameters(input, call.NumeroRequerant), ("@statut", EvenementStatuts.Attente), ("@agent", agent.Name), ("@now", DateTime.UtcNow)]);

        await Sql.ExecuteAsync(connection,
            "UPDATE pegase_appels SET statut = @statut, evenement_id = @ev WHERE id = @id", ct,
            ("@statut", AppelStatuts.Evenement), ("@ev", eventId), ("@id", callId));

        await AddMessageAsync(connection, eventId, agent.Name, "creation",
            $"Création de l'événement — {input.Nature.Trim()} ({PegaseReferentiel.PrioriteLabel(input.Priorite)}), " +
            $"appel n° {callId}" + (call.NumeroRequerant is { } number ? $" du {number}" : string.Empty) + ".", ct);

        if (input.Priorite == 1)
            await hub.BroadcastAsync(new RealtimeMessage(RealtimeTypes.Notify, Title: "PEGASE — P1",
                Text: $"{input.Nature.Trim()} · {input.Adresse.Trim()}", AppId: SaphirApps.Pegase), ct);
        await NotifyChangedAsync(ct);
        return eventId;
    }

    public async Task<bool> CloseCallWithoutEventAsync(long callId, SansSuiteInput input, AgentContext agent, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Motif))
            throw new ArgumentException("Le motif de clôture est obligatoire.");

        await using var connection = await OpenAsync(ct);
        var issue = input.Motif.Trim() + (string.IsNullOrWhiteSpace(input.Observations) ? string.Empty : " — " + input.Observations.Trim());
        var changed = await Sql.ExecuteAsync(connection,
            "UPDATE pegase_appels SET statut = @statut, issue = @issue WHERE id = @id AND statut = @enCours", ct,
            ("@statut", AppelStatuts.SansSuite), ("@issue", Sql.Clean(issue, 500)), ("@id", callId), ("@enCours", AppelStatuts.EnCours));
        if (changed > 0)
            await NotifyChangedAsync(ct);
        return changed > 0;
    }

    // =====================================================================
    // Événements
    // =====================================================================

    public async Task<bool> UpdateEventAsync(long id, EvenementInput input, AgentContext agent, CancellationToken ct)
    {
        ValidateEvent(input);
        await using var connection = await OpenAsync(ct);
        var changed = await Sql.ExecuteAsync(connection,
            """
            UPDATE pegase_evenements SET nature = @nature, priorite = @priorite, adresse = @adresse, commune = @commune,
                complement = @complement, requerant_nom = @nom, requerant_tel = @tel, description = @description
            WHERE id = @id
            """, ct, [.. EventParameters(input, null), ("@id", id)]);
        if (changed == 0)
            return false;
        await AddMessageAsync(connection, id, agent.Name, "maj",
            $"Mise à jour : {input.Nature.Trim()}, {PegaseReferentiel.PrioriteLabel(input.Priorite)}, {input.Adresse.Trim()}.", ct);
        await NotifyChangedAsync(ct);
        return true;
    }

    public async Task<bool> AddMessageAsync(long eventId, string text, AgentContext agent, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Le message est vide.");
        await using var connection = await OpenAsync(ct);
        if (!await EventExistsAsync(connection, eventId, ct))
            return false;
        await AddMessageAsync(connection, eventId, agent.Name, "info", text.Trim(), ct);
        await NotifyChangedAsync(ct);
        return true;
    }

    public async Task<bool> EngageAsync(long eventId, long crewId, AgentContext agent, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        var ev = (await QueryEventsAsync(connection, "e.id = @id", ct, ("@id", eventId))).FirstOrDefault();
        var crew = (await QueryCrewsAsync(connection, ct)).FirstOrDefault(c => c.Id == crewId);
        if (ev is null || crew is null)
            return false;
        if (ev.Statut == EvenementStatuts.Termine)
            throw new ArgumentException("L'événement est terminé.");
        if (crew.Statut is EquipageStatuts.HorsService or EquipageStatuts.Indisponible)
            throw new ArgumentException($"L'équipage {crew.Indicatif} est {crew.Statut.ToLowerInvariant()}.");

        // Un équipage déjà engagé ailleurs est réaffecté : on le note sur l'ancien événement.
        if (crew.EvenementId is { } previous && previous != eventId)
            await AddMessageAsync(connection, previous, agent.Name, "engagement", $"{crew.Indicatif} désengagé (réaffecté sur {ev.Numero}).", ct);

        await Sql.ExecuteAsync(connection,
            "UPDATE pegase_equipages SET statut = @statut, evenement_id = @ev, maj_le = @now WHERE id = @id", ct,
            ("@statut", EquipageStatuts.Engage), ("@ev", eventId), ("@now", DateTime.UtcNow), ("@id", crewId));
        if (ev.Statut == EvenementStatuts.Attente)
            await SetEventStatusAsync(connection, eventId, EvenementStatuts.Engage, ct);
        await AddMessageAsync(connection, eventId, agent.Name, "engagement", $"Engagement de {crew.Indicatif} ({crew.Type}).", ct);
        await NotifyChangedAsync(ct);
        return true;
    }

    public async Task<bool> CloseEventAsync(long id, string report, AgentContext agent, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(report))
            throw new ArgumentException("Le compte rendu est obligatoire pour clore l'événement.");

        await using var connection = await OpenAsync(ct);
        var changed = await Sql.ExecuteAsync(connection,
            "UPDATE pegase_evenements SET statut = @termine, clos_par = @agent, clos_le = @now, compte_rendu = @cr WHERE id = @id AND statut <> @termine", ct,
            ("@termine", EvenementStatuts.Termine), ("@agent", agent.Name), ("@now", DateTime.UtcNow), ("@cr", Sql.Clean(report, 8000)), ("@id", id));
        if (changed == 0)
            return false;

        await Sql.ExecuteAsync(connection,
            "UPDATE pegase_equipages SET statut = @dispo, evenement_id = NULL, maj_le = @now WHERE evenement_id = @id", ct,
            ("@dispo", EquipageStatuts.Disponible), ("@now", DateTime.UtcNow), ("@id", id));
        await AddMessageAsync(connection, id, agent.Name, "cloture", "Événement terminé : " + report.Trim(), ct);
        await NotifyChangedAsync(ct);
        return true;
    }

    // =====================================================================
    // Équipages
    // =====================================================================

    public async Task<long> CreateCrewAsync(EquipageInput input, CancellationToken ct)
    {
        ValidateCrew(input);
        await using var connection = await OpenAsync(ct);
        try
        {
            var id = await Sql.InsertAsync(connection,
                "INSERT INTO pegase_equipages (indicatif, type, effectif, statut, maj_le) VALUES (@indicatif, @type, @effectif, @statut, @now)", ct,
                ("@indicatif", Sql.Clean(input.Indicatif, 40)!.ToUpperInvariant()), ("@type", Sql.Clean(input.Type, 60)),
                ("@effectif", Sql.Clean(input.Effectif)), ("@statut", EquipageStatuts.Disponible), ("@now", DateTime.UtcNow));
            await NotifyChangedAsync(ct);
            return id;
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            throw new ArgumentException($"L'indicatif {input.Indicatif.Trim().ToUpperInvariant()} existe déjà.");
        }
    }

    public async Task<bool> UpdateCrewAsync(long id, EquipageInput input, CancellationToken ct)
    {
        ValidateCrew(input);
        await using var connection = await OpenAsync(ct);
        try
        {
            var changed = await Sql.ExecuteAsync(connection,
                "UPDATE pegase_equipages SET indicatif = @indicatif, type = @type, effectif = @effectif, maj_le = @now WHERE id = @id", ct,
                ("@indicatif", Sql.Clean(input.Indicatif, 40)!.ToUpperInvariant()), ("@type", Sql.Clean(input.Type, 60)),
                ("@effectif", Sql.Clean(input.Effectif)), ("@now", DateTime.UtcNow), ("@id", id));
            if (changed > 0)
                await NotifyChangedAsync(ct);
            return changed > 0;
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            throw new ArgumentException($"L'indicatif {input.Indicatif.Trim().ToUpperInvariant()} existe déjà.");
        }
    }

    public async Task<bool> DeleteCrewAsync(long id, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        var changed = await Sql.ExecuteAsync(connection, "DELETE FROM pegase_equipages WHERE id = @id", ct, ("@id", id));
        if (changed > 0)
            await NotifyChangedAsync(ct);
        return changed > 0;
    }

    /// <summary>Statut d'un équipage. « Sur place » fait passer l'événement sur place ; un statut libre le désengage.</summary>
    public async Task<bool> SetCrewStatusAsync(long id, string status, AgentContext agent, CancellationToken ct)
    {
        if (!EquipageStatuts.All.Contains(status))
            throw new ArgumentException("Statut d'équipage inconnu.");

        await using var connection = await OpenAsync(ct);
        var crew = (await QueryCrewsAsync(connection, ct)).FirstOrDefault(c => c.Id == id);
        if (crew is null)
            return false;

        var eventId = crew.EvenementId;
        var keepsEvent = status is EquipageStatuts.Engage or EquipageStatuts.SurPlace;
        if (keepsEvent && eventId is null)
            throw new ArgumentException("Engage d'abord l'équipage sur un événement.");

        await Sql.ExecuteAsync(connection,
            "UPDATE pegase_equipages SET statut = @statut, evenement_id = @ev, maj_le = @now WHERE id = @id", ct,
            ("@statut", status), ("@ev", keepsEvent ? eventId : null), ("@now", DateTime.UtcNow), ("@id", id));

        if (eventId is { } ev)
        {
            await AddMessageAsync(connection, ev, agent.Name, "statut",
                keepsEvent ? $"{crew.Indicatif} : {status.ToLowerInvariant()}." : $"{crew.Indicatif} libéré ({status.ToLowerInvariant()}).", ct);
            if (status == EquipageStatuts.SurPlace)
                await SetEventStatusAsync(connection, ev, EvenementStatuts.SurPlace, ct, onlyFrom: [EvenementStatuts.Attente, EvenementStatuts.Engage]);
        }

        await NotifyChangedAsync(ct);
        return true;
    }

    // =====================================================================
    // Aides internes
    // =====================================================================

    private static async Task<List<EvenementDto>> QueryEventsAsync(
        MySqlConnection connection, string where, CancellationToken ct, params (string, object?)[] parameters)
    {
        var events = await Sql.QueryAsync(connection,
            $"""
             SELECT e.*, (SELECT GROUP_CONCAT(q.indicatif ORDER BY q.indicatif SEPARATOR ',') FROM pegase_equipages q WHERE q.evenement_id = e.id) AS equipages
             FROM pegase_evenements e
             WHERE {where}
             ORDER BY (e.statut = '{EvenementStatuts.Termine}'), e.priorite, e.cree_le DESC
             LIMIT 200
             """,
            r =>
            {
                var created = r.Utc("cree_le");
                var crews = r.Str("equipages");
                return new EvenementDto(
                    r.Long("id"),
                    $"{created.ToLocalTime():yyyy}-{r.Long("id"):D5}",
                    r.Text("nature"), (int)r.Long("priorite"), r.Text("adresse"), r.Str("commune"), r.Str("complement"),
                    r.Str("requerant_nom"), r.Str("requerant_tel"), r.Str("description"), r.Text("statut"),
                    r.Text("cree_par"), created, r.Str("clos_par"), r.NullableUtc("clos_le"), r.Str("compte_rendu"),
                    crews is null ? Array.Empty<string>() : crews.Split(',', StringSplitOptions.RemoveEmptyEntries));
            }, ct, parameters);
        return events;
    }

    private static Task<List<EquipageDto>> QueryCrewsAsync(MySqlConnection connection, CancellationToken ct)
        => Sql.QueryAsync(connection,
            """
            SELECT q.*, e.cree_le AS ev_cree_le FROM pegase_equipages q
            LEFT JOIN pegase_evenements e ON e.id = q.evenement_id
            ORDER BY q.indicatif
            """,
            r =>
            {
                var eventId = r.NullableLong("evenement_id");
                var eventCreated = r.NullableUtc("ev_cree_le");
                return new EquipageDto(r.Long("id"), r.Text("indicatif"), r.Text("type"), r.Str("effectif"), r.Text("statut"),
                    eventId, eventId is { } ev && eventCreated is { } at ? $"{at.ToLocalTime():yyyy}-{ev:D5}" : null, r.Utc("maj_le"));
            }, ct);

    private static AppelDto ReadCall(MySqlDataReader r) => new(
        r.Long("id"), r.Str("simcall_id"), r.Str("numero_requerant"), r.Text("operateur"), r.Text("operateur_id"),
        r.Utc("decroche_le"), r.NullableUtc("raccroche_le"), r.Text("statut"), r.Str("issue"), r.NullableLong("evenement_id"));

    private static (string, object?)[] EventParameters(EvenementInput input, string? fallbackPhone) =>
    [
        ("@nature", Sql.Clean(input.Nature, 80)),
        ("@priorite", Math.Clamp(input.Priorite, 1, 3)),
        ("@adresse", Sql.Clean(input.Adresse)),
        ("@commune", Sql.Clean(input.Commune, 80)),
        ("@complement", Sql.Clean(input.Complement)),
        ("@nom", Sql.Clean(input.RequerantNom, 120)),
        ("@tel", Sql.Clean(input.RequerantTel, 40) ?? fallbackPhone),
        ("@description", Sql.Clean(input.Description, 8000))
    ];

    private static void ValidateEvent(EvenementInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Nature))
            throw new ArgumentException("La nature de l'événement est obligatoire.");
        if (string.IsNullOrWhiteSpace(input.Adresse))
            throw new ArgumentException("L'adresse de l'événement est obligatoire.");
    }

    private static void ValidateCrew(EquipageInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Indicatif) || string.IsNullOrWhiteSpace(input.Type))
            throw new ArgumentException("L'indicatif et le type d'équipage sont obligatoires.");
    }

    private static async Task<bool> EventExistsAsync(MySqlConnection connection, long id, CancellationToken ct)
        => await Sql.ScalarAsync(connection, "SELECT 1 FROM pegase_evenements WHERE id = @id", ct, ("@id", id)) is not null;

    private static async Task SetEventStatusAsync(MySqlConnection connection, long id, string status, CancellationToken ct, string[]? onlyFrom = null)
    {
        if (onlyFrom is null)
        {
            await Sql.ExecuteAsync(connection, "UPDATE pegase_evenements SET statut = @s WHERE id = @id AND statut <> @termine", ct,
                ("@s", status), ("@id", id), ("@termine", EvenementStatuts.Termine));
            return;
        }
        var (clause, parameters) = (string.Join(", ", onlyFrom.Select((_, i) => "@f" + i)), onlyFrom.Select((v, i) => ("@f" + i, (object?)v)));
        await Sql.ExecuteAsync(connection, $"UPDATE pegase_evenements SET statut = @s WHERE id = @id AND statut IN ({clause})", ct,
            [("@s", status), ("@id", id), .. parameters]);
    }

    private static Task AddMessageAsync(MySqlConnection connection, long eventId, string author, string type, string text, CancellationToken ct)
        => Sql.ExecuteAsync(connection,
            "INSERT INTO pegase_messages (evenement_id, date_utc, auteur, type, texte) VALUES (@ev, @now, @auteur, @type, @texte)", ct,
            ("@ev", eventId), ("@now", DateTime.UtcNow), ("@auteur", author), ("@type", type),
            ("@texte", text.Length > 4000 ? text[..4000] : text));

    private async Task NotifyChangedAsync(CancellationToken ct)
    {
        try
        {
            await hub.BroadcastAsync(new RealtimeMessage(RealtimeTypes.PegaseChanged), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PEGASE : diffusion du changement impossible");
        }
    }
}
