using Microsoft.Extensions.Options;
using MySqlConnector;
using Saphir.Server.Services;
using Saphir.Shared.Contracts;

namespace Saphir.Server.Data;

/// <summary>
/// Données PTS : signalisations FAED / FNAEG et inscriptions FIJAIS / FIJAIT d'une personne, traces relevées
/// sur les scènes et leurs rapprochements. Tout est tracé dans le journal SAPHIR.
/// </summary>
public sealed class PtsStore(IOptionsMonitor<DatabaseOptions> options, MeoFicStore people, RealtimeHub hub, ILogger<PtsStore> logger)
{
    private Task<MySqlConnection> OpenAsync(CancellationToken ct) => Sql.OpenAsync(options.CurrentValue.Saphir, ct);

    // =====================================================================
    // Personne
    // =====================================================================

    /// <summary>Fichiers PTS d'une personne (vides si elle n'a pas encore de fiche SAPHIR).</summary>
    public async Task<PtsPersonDto> GetPersonAsync(string key, AgentContext agent, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        var personId = await people.FindPersonIdAsync(connection, key, ct);
        if (personId is null)
            return new PtsPersonDto(key, [], [], []);

        var signalisations = await Sql.QueryAsync(connection,
            "SELECT * FROM pts_signalisations WHERE personne_id = @p ORDER BY cree_le DESC",
            r => new SignalisationDto(r.Long("id"), r.Text("fichier"), r.Text("reference"), r.Text("date_signalisation"),
                r.Str("service"), r.Str("motif"), r.Str("details"), r.Text("auteur"), r.Utc("cree_le")),
            ct, ("@p", personId.Value));

        var inscriptions = await Sql.QueryAsync(connection,
            "SELECT * FROM pts_inscriptions WHERE personne_id = @p ORDER BY cree_le DESC",
            r => new InscriptionDto(r.Long("id"), r.Text("fichier"), r.Text("infraction"), r.Text("decision"), r.Str("date_decision"),
                r.Str("juridiction"), r.Str("obligations"), r.Str("fin_inscription"), r.Str("details"), r.Text("auteur"), r.Utc("cree_le")),
            ct, ("@p", personId.Value));

        var traces = await QueryTracesAsync(connection, "t.personne_id = @p", ct, ("@p", personId.Value));

        await MeoFicStore.WriteJournalAsync(connection, agent, "Consultation PTS",
            await MeoFicStore.PersonNameAsync(connection, personId.Value, ct), ct);
        return new PtsPersonDto(key, signalisations, inscriptions, traces);
    }

    public async Task<bool> AddSignalisationAsync(string key, SignalisationInput input, AgentContext agent, CancellationToken ct)
    {
        if (!PtsReferentiel.FichiersSignalisation.Contains(input.Fichier))
            throw new ArgumentException("Fichier inconnu (FAED ou FNAEG).");
        if (string.IsNullOrWhiteSpace(input.Reference) || string.IsNullOrWhiteSpace(input.DateSignalisation))
            throw new ArgumentException("La référence et la date de signalisation sont obligatoires.");

        await using var connection = await OpenAsync(ct);
        var personId = await people.ResolvePersonIdAsync(connection, key, agent, ct);
        if (personId is null)
            return false;

        await Sql.InsertAsync(connection,
            """
            INSERT INTO pts_signalisations (personne_id, fichier, reference, date_signalisation, service, motif, details, auteur, auteur_id, cree_le)
            VALUES (@p, @fichier, @reference, @date, @service, @motif, @details, @auteur, @auteurId, @now)
            """, ct,
            ("@p", personId.Value), ("@fichier", input.Fichier), ("@reference", Sql.Clean(input.Reference, 60)!.ToUpperInvariant()),
            ("@date", Sql.Clean(input.DateSignalisation, 20)), ("@service", Sql.Clean(input.Service, 120)),
            ("@motif", Sql.Clean(input.Motif)), ("@details", Sql.Clean(input.Details, 8000)),
            ("@auteur", agent.Name), ("@auteurId", agent.Id), ("@now", DateTime.UtcNow));
        await MeoFicStore.WriteJournalAsync(connection, agent, "Signalisation " + input.Fichier,
            $"{await MeoFicStore.PersonNameAsync(connection, personId.Value, ct)} — {input.Reference.Trim()}", ct);
        return true;
    }

    public async Task<bool> AddInscriptionAsync(string key, InscriptionInput input, AgentContext agent, CancellationToken ct)
    {
        if (!PtsReferentiel.FichiersInscription.Contains(input.Fichier))
            throw new ArgumentException("Fichier inconnu (FIJAIS ou FIJAIT).");
        if (string.IsNullOrWhiteSpace(input.Infraction) || string.IsNullOrWhiteSpace(input.Decision))
            throw new ArgumentException("L'infraction et la décision sont obligatoires.");

        await using var connection = await OpenAsync(ct);
        var personId = await people.ResolvePersonIdAsync(connection, key, agent, ct);
        if (personId is null)
            return false;

        await Sql.InsertAsync(connection,
            """
            INSERT INTO pts_inscriptions (personne_id, fichier, infraction, decision, date_decision, juridiction, obligations,
                fin_inscription, details, auteur, auteur_id, cree_le)
            VALUES (@p, @fichier, @infraction, @decision, @date, @juridiction, @obligations, @fin, @details, @auteur, @auteurId, @now)
            """, ct,
            ("@p", personId.Value), ("@fichier", input.Fichier), ("@infraction", Sql.Clean(input.Infraction)),
            ("@decision", Sql.Clean(input.Decision, 80)), ("@date", Sql.Clean(input.DateDecision, 20)),
            ("@juridiction", Sql.Clean(input.Juridiction, 160)), ("@obligations", Sql.Clean(input.Obligations)),
            ("@fin", Sql.Clean(input.FinInscription, 20)), ("@details", Sql.Clean(input.Details, 8000)),
            ("@auteur", agent.Name), ("@auteurId", agent.Id), ("@now", DateTime.UtcNow));
        await MeoFicStore.WriteJournalAsync(connection, agent, "Inscription " + input.Fichier,
            $"{await MeoFicStore.PersonNameAsync(connection, personId.Value, ct)} — {input.Infraction.Trim()}", ct);
        return true;
    }

    // =====================================================================
    // Traces
    // =====================================================================

    /// <summary>Traces relevées : non identifiées seulement, ou toutes (les 300 plus récentes).</summary>
    public async Task<List<TraceDto>> GetTracesAsync(bool onlyUnidentified, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        return await QueryTracesAsync(connection, onlyUnidentified ? "t.personne_id IS NULL" : "1 = 1", ct);
    }

    public async Task<long> AddTraceAsync(TraceInput input, AgentContext agent, CancellationToken ct)
    {
        if (!PtsReferentiel.FichiersSignalisation.Contains(input.Fichier))
            throw new ArgumentException("Fichier inconnu (FAED ou FNAEG).");
        if (string.IsNullOrWhiteSpace(input.Reference))
            throw new ArgumentException("La référence de la trace est obligatoire.");

        await using var connection = await OpenAsync(ct);
        var id = await Sql.InsertAsync(connection,
            """
            INSERT INTO pts_traces (fichier, reference, affaire, lieu, date_prelevement, description, auteur, auteur_id, cree_le)
            VALUES (@fichier, @reference, @affaire, @lieu, @date, @description, @auteur, @auteurId, @now)
            """, ct,
            ("@fichier", input.Fichier), ("@reference", Sql.Clean(input.Reference, 60)!.ToUpperInvariant()),
            ("@affaire", Sql.Clean(input.Affaire, 160)), ("@lieu", Sql.Clean(input.Lieu)), ("@date", Sql.Clean(input.DatePrelevement, 20)),
            ("@description", Sql.Clean(input.Description, 8000)),
            ("@auteur", agent.Name), ("@auteurId", agent.Id), ("@now", DateTime.UtcNow));
        await MeoFicStore.WriteJournalAsync(connection, agent, "Trace " + input.Fichier, input.Reference.Trim().ToUpperInvariant(), ct);
        await NotifyChangedAsync(ct);
        return id;
    }

    /// <summary>Rapprochement : la trace est attribuée à une personne (identification).</summary>
    public async Task<bool> MatchTraceAsync(long traceId, string personKey, AgentContext agent, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        var personId = await people.ResolvePersonIdAsync(connection, personKey, agent, ct);
        if (personId is null)
            throw new ArgumentException("Personne introuvable.");

        var changed = await Sql.ExecuteAsync(connection,
            "UPDATE pts_traces SET personne_id = @p, rapproche_par = @agent, rapproche_le = @now WHERE id = @id", ct,
            ("@p", personId.Value), ("@agent", agent.Name), ("@now", DateTime.UtcNow), ("@id", traceId));
        if (changed == 0)
            return false;

        await MeoFicStore.WriteJournalAsync(connection, agent, "Rapprochement de trace",
            $"Trace n° {traceId} → {await MeoFicStore.PersonNameAsync(connection, personId.Value, ct)}", ct);
        await NotifyChangedAsync(ct);
        return true;
    }

    public async Task<bool> UnmatchTraceAsync(long traceId, AgentContext agent, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        var changed = await Sql.ExecuteAsync(connection,
            "UPDATE pts_traces SET personne_id = NULL, rapproche_par = NULL, rapproche_le = NULL WHERE id = @id AND personne_id IS NOT NULL", ct,
            ("@id", traceId));
        if (changed == 0)
            return false;
        await MeoFicStore.WriteJournalAsync(connection, agent, "Annulation de rapprochement", "Trace n° " + traceId, ct);
        await NotifyChangedAsync(ct);
        return true;
    }

    /// <summary>Suppression d'une signalisation, inscription ou trace (administration).</summary>
    public async Task<bool> DeleteAsync(string kind, long id, AgentContext agent, CancellationToken ct)
    {
        var table = kind switch
        {
            "signalisation" => "pts_signalisations",
            "inscription" => "pts_inscriptions",
            "trace" => "pts_traces",
            _ => null
        };
        if (table is null)
            return false;

        await using var connection = await OpenAsync(ct);
        var deleted = await Sql.ExecuteAsync(connection, $"DELETE FROM {table} WHERE id = @id", ct, ("@id", id));
        if (deleted > 0)
        {
            await MeoFicStore.WriteJournalAsync(connection, agent, "Suppression PTS (" + kind + ")", "N° " + id, ct);
            if (kind == "trace")
                await NotifyChangedAsync(ct);
        }
        return deleted > 0;
    }

    // =====================================================================
    // Aides internes
    // =====================================================================

    private static Task<List<TraceDto>> QueryTracesAsync(
        MySqlConnection connection, string where, CancellationToken ct, params (string, object?)[] parameters)
        => Sql.QueryAsync(connection,
            $"""
             SELECT t.*, p.fivem_id, p.nom, p.prenom FROM pts_traces t
             LEFT JOIN saphir_personnes p ON p.id = t.personne_id
             WHERE {where}
             ORDER BY t.cree_le DESC
             LIMIT 300
             """,
            r =>
            {
                var personId = r.NullableLong("personne_id");
                return new TraceDto(
                    r.Long("id"), r.Text("fichier"), r.Text("reference"), r.Str("affaire"), r.Str("lieu"), r.Str("date_prelevement"),
                    r.Str("description"),
                    personId is { } pid ? MeoFicStore.PublicKey(pid, r.Str("fivem_id")) : null,
                    personId is null ? null : $"{r.Text("nom")} {r.Text("prenom")}",
                    r.Str("rapproche_par"), r.NullableUtc("rapproche_le"), r.Text("auteur"), r.Utc("cree_le"));
            }, ct, parameters);

    private async Task NotifyChangedAsync(CancellationToken ct)
    {
        try
        {
            await hub.BroadcastAsync(new RealtimeMessage(RealtimeTypes.PtsChanged), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PTS : diffusion du changement impossible");
        }
    }
}
