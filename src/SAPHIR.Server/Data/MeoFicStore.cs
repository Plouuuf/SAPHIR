using System.Globalization;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Saphir.Server.Services;
using Saphir.Shared.Contracts;

namespace Saphir.Server.Data;

/// <summary>
/// Données de MEO-FIC : fiches SAPHIR (personnes, TAJ, FPR, permis, véhicules, FOVeS) dans la base SAPHIR,
/// complétées par les identités et véhicules lus dans la base FiveM.
/// </summary>
public sealed class MeoFicStore(IOptionsMonitor<DatabaseOptions> options, FiveMReader fivem, ILogger<MeoFicStore> logger)
{
    private const string FivemPrefix = "f:";
    private const string SaphirPrefix = "s:";

    private string ConnectionString => options.CurrentValue.Saphir;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);

    private Task<MySqlConnection> OpenAsync(CancellationToken ct) => Sql.OpenAsync(ConnectionString, ct);

    public async Task InitializeAsync(CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        foreach (var statement in SaphirSchema.Statements)
            await Sql.ExecuteAsync(connection, statement, ct);
        logger.LogInformation("Base SAPHIR prête (FiveM : {State}).", fivem.IsEnabled ? "lecture activée" : "non configurée");
    }

    // =====================================================================
    // Personnes
    // =====================================================================

    public async Task<List<PersonSummaryDto>> SearchPeopleAsync(string query, CancellationToken ct)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= 2).Take(4).ToList();
        if (terms.Count == 0)
            return [];

        await using var connection = await OpenAsync(ct);

        var parameters = new List<(string, object?)>();
        var clauses = new List<string>();
        for (var i = 0; i < terms.Count; i++)
        {
            parameters.Add(("@t" + i, Sql.Like(terms[i])));
            clauses.Add($"(p.nom LIKE @t{i} OR p.prenom LIKE @t{i} OR p.alias LIKE @t{i} OR p.date_naissance LIKE @t{i})");
        }

        var results = new Dictionary<string, PersonSummaryDto>(StringComparer.Ordinal);
        foreach (var row in await QuerySummariesAsync(connection, string.Join(" AND ", clauses), parameters, ct))
            results.TryAdd(row.Key, row);

        // Personnages FiveM : on ajoute ceux qui n'ont pas encore de fiche SAPHIR trouvée par la recherche.
        var fivemPeople = await fivem.SearchPeopleAsync(terms, ct);
        var missing = fivemPeople.Where(p => !results.ContainsKey(FivemPrefix + p.Id)).ToList();
        if (missing.Count > 0)
        {
            // Une fiche SAPHIR peut exister sous un ancien nom : on récupère ses indicateurs (TAJ, FPR, permis).
            var (inClause, inParameters) = InList("@f", missing.Select(p => (object?)p.Id));
            var linked = (await QuerySummariesAsync(connection, $"p.fivem_id IN ({inClause})", inParameters, ct))
                .ToDictionary(r => r.Key, StringComparer.Ordinal);

            foreach (var person in missing)
            {
                var key = FivemPrefix + person.Id;
                linked.TryGetValue(key, out var flags);
                results.TryAdd(key, new PersonSummaryDto(key, person.Nom, person.Prenom, person.DateNaissance, person.Sexe, "FiveM",
                    flags?.FprActif ?? false, flags?.TajCount ?? 0, flags?.PermisStatut));
            }
        }

        return results.Values
            .OrderByDescending(p => p.FprActif)
            .ThenBy(p => p.Nom, StringComparer.Create(CultureInfo.GetCultureInfo("fr-FR"), true))
            .ThenBy(p => p.Prenom, StringComparer.Create(CultureInfo.GetCultureInfo("fr-FR"), true))
            .Take(50)
            .ToList();
    }

    private static Task<List<PersonSummaryDto>> QuerySummariesAsync(
        MySqlConnection connection, string where, IReadOnlyList<(string, object?)> parameters, CancellationToken ct)
        => Sql.QueryAsync(connection,
            $"""
             SELECT p.id, p.fivem_id, p.nom, p.prenom, p.date_naissance, p.sexe,
                    (SELECT COUNT(*) FROM saphir_taj t WHERE t.personne_id = p.id) AS taj,
                    EXISTS(SELECT 1 FROM saphir_fpr f WHERE f.personne_id = p.id AND f.actif = 1) AS fpr,
                    (SELECT m.statut FROM saphir_permis m WHERE m.personne_id = p.id) AS permis
             FROM saphir_personnes p
             WHERE {where}
             ORDER BY p.nom, p.prenom
             LIMIT 50
             """,
            r =>
            {
                var fivemId = r.Str("fivem_id");
                return new PersonSummaryDto(
                    fivemId is null ? SaphirPrefix + r.Long("id") : FivemPrefix + fivemId,
                    r.Text("nom"), r.Text("prenom"), r.Str("date_naissance"), r.Str("sexe"),
                    fivemId is null ? "SAPHIR" : "FiveM",
                    r.Bool("fpr"), (int)r.Long("taj"), r.Str("permis"));
            }, ct, parameters.ToArray());

    public async Task<PersonDetailDto?> GetPersonAsync(string key, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        var (row, fivemPerson) = await LoadPersonAsync(connection, key, ct);
        if (row is null && fivemPerson is null)
            return null;

        var fivemId = fivemPerson?.Id ?? row?.FivemId;
        var publicKey = fivemId is not null ? FivemPrefix + fivemId : SaphirPrefix + row!.Id;

        var identity = MergeIdentity(row, fivemPerson);

        var taj = new List<TajEntryDto>();
        var fpr = new List<FprEntryDto>();
        PermisDto? permis = null;
        if (row is not null)
        {
            taj = await Sql.QueryAsync(connection,
                "SELECT * FROM saphir_taj WHERE personne_id = @id ORDER BY cree_le DESC",
                r => new TajEntryDto(r.Long("id"), r.Text("qualite"), r.Text("infraction"), r.Str("natinf"), r.Text("date_faits"),
                    r.Str("lieu"), r.Str("service"), r.Str("procedure_ref"), r.Str("details"), r.Text("auteur"), r.Utc("cree_le")),
                ct, ("@id", row.Id));

            fpr = await Sql.QueryAsync(connection,
                "SELECT * FROM saphir_fpr WHERE personne_id = @id ORDER BY actif DESC, cree_le DESC",
                ReadFpr, ct, ("@id", row.Id));

            permis = (await Sql.QueryAsync(connection,
                "SELECT * FROM saphir_permis WHERE personne_id = @id",
                r => new PermisDto(r.Str("numero"), r.Text("categories"), (int)r.Long("points"), r.Text("statut"),
                    r.Str("date_obtention"), r.Str("restrictions"), r.Str("maj_par"), r.NullableUtc("maj_le")),
                ct, ("@id", row.Id))).FirstOrDefault();
        }

        // Sans fiche SNPC dans SAPHIR, on reprend les permis connus du serveur FiveM.
        if (permis is null && fivemId is not null)
        {
            var categories = await fivem.GetLicenceCategoriesAsync(fivemId, ct);
            if (categories is not null)
                permis = new PermisDto(null, categories, 12, "Valide", null, null, "Serveur FiveM", null);
        }

        var vehicles = await VehiclesOfAsync(connection, publicKey, row, fivemId, ct);
        var source = row is not null && fivemPerson is not null ? "FiveM + SAPHIR" : fivemPerson is not null ? "FiveM" : "SAPHIR";

        return new PersonDetailDto(publicKey, row?.Id, fivemId, source, identity, taj, fpr, permis, vehicles);
    }

    /// <summary>Nouvelle fiche personne (personne inconnue du serveur FiveM).</summary>
    public async Task<string> CreatePersonAsync(IdentityDto identity, AgentContext agent, CancellationToken ct)
    {
        ValidateIdentity(identity);
        await using var connection = await OpenAsync(ct);
        var id = await InsertPersonAsync(connection, null, identity, agent, ct);
        await LogAsync(connection, agent, "Création fiche personne", Describe(identity), ct);
        return SaphirPrefix + id;
    }

    /// <summary>Correction de l'identité ou du signalement (pour un personnage FiveM, complète ses données).</summary>
    public async Task<bool> UpdateIdentityAsync(string key, IdentityDto identity, AgentContext agent, CancellationToken ct)
    {
        ValidateIdentity(identity);
        await using var connection = await OpenAsync(ct);
        var personId = await EnsurePersonIdAsync(connection, key, agent, ct);
        if (personId is null)
            return false;

        await Sql.ExecuteAsync(connection,
            """
            UPDATE saphir_personnes SET nom = @nom, prenom = @prenom, date_naissance = @naissance, lieu_naissance = @lieu,
                sexe = @sexe, nationalite = @nationalite, adresse = @adresse, telephone = @telephone, taille = @taille,
                signalement = @signalement, alias = @alias, maj_le = @now
            WHERE id = @id
            """, ct, [.. IdentityParameters(identity), ("@now", DateTime.UtcNow), ("@id", personId.Value)]);
        await LogAsync(connection, agent, "Modification identité", Describe(identity), ct);
        return true;
    }

    // ----- TAJ -----

    public async Task<bool> AddTajAsync(string key, TajInput input, AgentContext agent, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Infraction) || string.IsNullOrWhiteSpace(input.DateFaits))
            throw new ArgumentException("L'infraction et la date des faits sont obligatoires.");

        await using var connection = await OpenAsync(ct);
        var personId = await EnsurePersonIdAsync(connection, key, agent, ct);
        if (personId is null)
            return false;

        await Sql.InsertAsync(connection,
            """
            INSERT INTO saphir_taj (personne_id, qualite, infraction, natinf, date_faits, lieu, service, procedure_ref, details, auteur, auteur_id, cree_le)
            VALUES (@p, @qualite, @infraction, @natinf, @date, @lieu, @service, @procedure, @details, @auteur, @auteurId, @now)
            """, ct,
            ("@p", personId.Value),
            ("@qualite", Sql.Clean(input.Qualite, 40) ?? MeoFicReferentiel.TajQualites[0]),
            ("@infraction", Sql.Clean(input.Infraction)),
            ("@natinf", Sql.Clean(input.Natinf, 20)),
            ("@date", Sql.Clean(input.DateFaits, 20)),
            ("@lieu", Sql.Clean(input.Lieu)),
            ("@service", Sql.Clean(input.Service, 120)),
            ("@procedure", Sql.Clean(input.Procedure, 60)),
            ("@details", Sql.Clean(input.Details, 8000)),
            ("@auteur", agent.Name), ("@auteurId", agent.Id), ("@now", DateTime.UtcNow));
        await LogAsync(connection, agent, "Inscription TAJ", $"{await PersonLabelAsync(connection, personId.Value, ct)} — {input.Infraction.Trim()}", ct);
        return true;
    }

    // ----- FPR -----

    public async Task<bool> AddFprAsync(string key, FprInput input, AgentContext agent, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Motif) || string.IsNullOrWhiteSpace(input.ConduiteATenir))
            throw new ArgumentException("Le motif et la conduite à tenir sont obligatoires.");

        await using var connection = await OpenAsync(ct);
        var personId = await EnsurePersonIdAsync(connection, key, agent, ct);
        if (personId is null)
            return false;

        await Sql.InsertAsync(connection,
            """
            INSERT INTO saphir_fpr (personne_id, motif, conduite, autorite, details, dangereux, arme, actif, auteur, auteur_id, cree_le)
            VALUES (@p, @motif, @conduite, @autorite, @details, @dangereux, @arme, 1, @auteur, @auteurId, @now)
            """, ct,
            ("@p", personId.Value),
            ("@motif", Sql.Clean(input.Motif, 80)),
            ("@conduite", Sql.Clean(input.ConduiteATenir)),
            ("@autorite", Sql.Clean(input.Autorite, 160)),
            ("@details", Sql.Clean(input.Details, 8000)),
            ("@dangereux", input.Dangereux), ("@arme", input.Arme),
            ("@auteur", agent.Name), ("@auteurId", agent.Id), ("@now", DateTime.UtcNow));
        await LogAsync(connection, agent, "Inscription FPR", $"{await PersonLabelAsync(connection, personId.Value, ct)} — {input.Motif.Trim()}", ct);
        return true;
    }

    public async Task<bool> CloseFprAsync(long id, AgentContext agent, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        var changed = await Sql.ExecuteAsync(connection,
            "UPDATE saphir_fpr SET actif = 0, clos_par = @agent, clos_le = @now WHERE id = @id AND actif = 1", ct,
            ("@agent", agent.Name), ("@now", DateTime.UtcNow), ("@id", id));
        if (changed > 0)
            await LogAsync(connection, agent, "Clôture FPR", "Fiche FPR n° " + id, ct);
        return changed > 0;
    }

    // ----- SNPC -----

    public async Task<bool> SetPermisAsync(string key, PermisInput input, AgentContext agent, CancellationToken ct)
    {
        if (input.Points is < 0 or > 15)
            throw new ArgumentException("Le solde de points doit être compris entre 0 et 15.");
        if (string.IsNullOrWhiteSpace(input.Statut))
            throw new ArgumentException("Le statut du permis est obligatoire.");

        await using var connection = await OpenAsync(ct);
        var personId = await EnsurePersonIdAsync(connection, key, agent, ct);
        if (personId is null)
            return false;

        await Sql.ExecuteAsync(connection,
            """
            INSERT INTO saphir_permis (personne_id, numero, categories, points, statut, date_obtention, restrictions, maj_par, maj_le)
            VALUES (@p, @numero, @categories, @points, @statut, @obtention, @restrictions, @agent, @now)
            ON DUPLICATE KEY UPDATE numero = VALUES(numero), categories = VALUES(categories), points = VALUES(points),
                statut = VALUES(statut), date_obtention = VALUES(date_obtention), restrictions = VALUES(restrictions),
                maj_par = VALUES(maj_par), maj_le = VALUES(maj_le)
            """, ct,
            ("@p", personId.Value),
            ("@numero", Sql.Clean(input.Numero, 40)),
            ("@categories", Sql.Clean(input.Categories, 60) ?? "—"),
            ("@points", input.Points),
            ("@statut", Sql.Clean(input.Statut, 40)),
            ("@obtention", Sql.Clean(input.DateObtention, 20)),
            ("@restrictions", Sql.Clean(input.Restrictions)),
            ("@agent", agent.Name), ("@now", DateTime.UtcNow));
        await LogAsync(connection, agent, "Mise à jour SNPC", $"{await PersonLabelAsync(connection, personId.Value, ct)} — {input.Statut}, {input.Points} pts", ct);
        return true;
    }

    // =====================================================================
    // Véhicules
    // =====================================================================

    public async Task<List<VehicleSummaryDto>> SearchVehiclesAsync(string query, CancellationToken ct)
    {
        var plate = Sql.NormalizePlate(query);
        if (plate.Length < 2)
            return [];

        await using var connection = await OpenAsync(ct);
        var results = new Dictionary<string, VehicleSummaryDto>(StringComparer.Ordinal);

        foreach (var row in await Sql.QueryAsync(connection,
                     "SELECT * FROM saphir_vehicules WHERE plaque_norm LIKE @p ORDER BY plaque_norm LIMIT 30",
                     ReadVehicleRow, ct, ("@p", Sql.Like(plate))))
            results[row.PlaqueNorm] = new VehicleSummaryDto(row.Plaque, row.Marque, row.Modele, row.Couleur, row.TitulaireNom, "SAPHIR", false);

        foreach (var vehicle in await fivem.SearchVehiclesAsync(plate, ct))
        {
            var norm = Sql.NormalizePlate(vehicle.Plaque);
            if (results.TryGetValue(norm, out var existing))
            {
                results[norm] = existing with
                {
                    Modele = existing.Modele ?? vehicle.Modele,
                    Titulaire = existing.Titulaire ?? vehicle.OwnerName,
                    Source = "FiveM + SAPHIR"
                };
            }
            else
            {
                results[norm] = new VehicleSummaryDto(vehicle.Plaque.Trim(), null, vehicle.Modele, null, vehicle.OwnerName, "FiveM", false);
            }
        }

        // Plaques signalées au FOVeS, y compris inconnues du SIV (plaques volées, véhicule non immatriculé…).
        var flagged = await Sql.QueryAsync(connection,
            "SELECT DISTINCT plaque_norm FROM saphir_foves WHERE actif = 1 AND plaque_norm LIKE @p LIMIT 30",
            r => r.Text("plaque_norm"), ct, ("@p", Sql.Like(plate)));
        foreach (var norm in flagged)
        {
            results[norm] = results.TryGetValue(norm, out var existing)
                ? existing with { Signale = true }
                : new VehicleSummaryDto(norm, null, null, null, null, "FOVeS", true);
        }

        return results.Values.OrderByDescending(v => v.Signale).ThenBy(v => v.Plaque, StringComparer.Ordinal).ToList();
    }

    public async Task<VehicleDetailDto?> GetVehicleAsync(string plate, CancellationToken ct)
    {
        var norm = Sql.NormalizePlate(plate);
        if (norm.Length == 0)
            return null;

        await using var connection = await OpenAsync(ct);
        var row = (await Sql.QueryAsync(connection, "SELECT * FROM saphir_vehicules WHERE plaque_norm = @p", ReadVehicleRow, ct, ("@p", norm)))
            .FirstOrDefault();
        var fivemVehicle = await fivem.GetVehicleAsync(norm, ct);
        var foves = await Sql.QueryAsync(connection,
            "SELECT * FROM saphir_foves WHERE plaque_norm = @p ORDER BY actif DESC, cree_le DESC", ReadFoves, ct, ("@p", norm));

        if (row is null && fivemVehicle is null && foves.Count == 0)
            return null;

        var source = (row, fivemVehicle) switch
        {
            (not null, not null) => "FiveM + SAPHIR",
            (not null, null) => "SAPHIR",
            (null, not null) => "FiveM",
            _ => "Inconnu au SIV"
        };

        var siv = new SivDto(
            row?.Marque,
            row?.Modele ?? fivemVehicle?.Modele,
            row?.Couleur,
            row?.Vin,
            row?.DateMec,
            row?.TitulaireNom ?? fivemVehicle?.OwnerName,
            row?.TitulaireKey ?? (fivemVehicle?.OwnerId is { } owner ? FivemPrefix + owner : null),
            row?.ControleTechnique,
            row?.Opposition,
            source);

        var fva = BuildFva(row);
        var display = row?.Plaque ?? fivemVehicle?.Plaque.Trim() ?? norm;
        return new VehicleDetailDto(display, siv, fva, foves);
    }

    public async Task SaveVehicleAsync(VehicleInput input, AgentContext agent, CancellationToken ct)
    {
        var norm = Sql.NormalizePlate(input.Plaque);
        if (norm.Length is < 2 or > 16)
            throw new ArgumentException("Plaque d'immatriculation invalide.");

        await using var connection = await OpenAsync(ct);
        await Sql.ExecuteAsync(connection,
            """
            INSERT INTO saphir_vehicules (plaque_norm, plaque, marque, modele, couleur, vin, date_mec, titulaire_key, titulaire_nom,
                controle_technique, opposition, assureur, numero_contrat, assurance_jusquau, maj_par, maj_le)
            VALUES (@norm, @plaque, @marque, @modele, @couleur, @vin, @mec, @tkey, @tnom, @ct, @opposition, @assureur, @contrat, @jusquau, @agent, @now)
            ON DUPLICATE KEY UPDATE plaque = VALUES(plaque), marque = VALUES(marque), modele = VALUES(modele), couleur = VALUES(couleur),
                vin = VALUES(vin), date_mec = VALUES(date_mec), titulaire_key = VALUES(titulaire_key), titulaire_nom = VALUES(titulaire_nom),
                controle_technique = VALUES(controle_technique), opposition = VALUES(opposition), assureur = VALUES(assureur),
                numero_contrat = VALUES(numero_contrat), assurance_jusquau = VALUES(assurance_jusquau), maj_par = VALUES(maj_par), maj_le = VALUES(maj_le)
            """, ct,
            ("@norm", norm),
            ("@plaque", Sql.Clean(input.Plaque, 20)!.ToUpperInvariant()),
            ("@marque", Sql.Clean(input.Marque, 60)),
            ("@modele", Sql.Clean(input.Modele, 60)),
            ("@couleur", Sql.Clean(input.Couleur, 40)),
            ("@vin", Sql.Clean(input.Vin, 40)?.ToUpperInvariant()),
            ("@mec", Sql.Clean(input.DateMiseEnCirculation, 20)),
            ("@tkey", Sql.Clean(input.TitulaireKey, 140)),
            ("@tnom", Sql.Clean(input.TitulaireNom, 200)),
            ("@ct", Sql.Clean(input.ControleTechnique, 60)),
            ("@opposition", Sql.Clean(input.Opposition)),
            ("@assureur", Sql.Clean(input.Assureur, 80)),
            ("@contrat", Sql.Clean(input.NumeroContrat, 60)),
            ("@jusquau", Sql.Clean(input.AssuranceValideJusquau, 20)),
            ("@agent", agent.Name), ("@now", DateTime.UtcNow));
        await LogAsync(connection, agent, "Mise à jour SIV / FVA", input.Plaque.Trim().ToUpperInvariant(), ct);
    }

    public async Task AddFovesAsync(string plate, FovesInput input, AgentContext agent, CancellationToken ct)
    {
        var norm = Sql.NormalizePlate(plate);
        if (norm.Length is < 2 or > 16)
            throw new ArgumentException("Plaque d'immatriculation invalide.");
        if (string.IsNullOrWhiteSpace(input.Motif) || string.IsNullOrWhiteSpace(input.ConduiteATenir))
            throw new ArgumentException("Le motif et la conduite à tenir sont obligatoires.");

        await using var connection = await OpenAsync(ct);
        await Sql.InsertAsync(connection,
            """
            INSERT INTO saphir_foves (plaque_norm, motif, conduite, details, actif, auteur, auteur_id, cree_le)
            VALUES (@p, @motif, @conduite, @details, 1, @auteur, @auteurId, @now)
            """, ct,
            ("@p", norm),
            ("@motif", Sql.Clean(input.Motif, 80)),
            ("@conduite", Sql.Clean(input.ConduiteATenir)),
            ("@details", Sql.Clean(input.Details, 8000)),
            ("@auteur", agent.Name), ("@auteurId", agent.Id), ("@now", DateTime.UtcNow));
        await LogAsync(connection, agent, "Signalement FOVeS", $"{norm} — {input.Motif.Trim()}", ct);
    }

    public async Task<bool> CloseFovesAsync(long id, AgentContext agent, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        var changed = await Sql.ExecuteAsync(connection,
            "UPDATE saphir_foves SET actif = 0, clos_par = @agent, clos_le = @now WHERE id = @id AND actif = 1", ct,
            ("@agent", agent.Name), ("@now", DateTime.UtcNow), ("@id", id));
        if (changed > 0)
            await LogAsync(connection, agent, "Clôture FOVeS", "Signalement n° " + id, ct);
        return changed > 0;
    }

    // =====================================================================
    // Suppression (administration) et journal
    // =====================================================================

    /// <summary>Supprime une inscription TAJ, FPR ou FOVeS (erreur de saisie). Réservé à l'administration.</summary>
    public async Task<bool> DeleteEntryAsync(string kind, long id, AgentContext agent, CancellationToken ct)
    {
        var table = kind switch
        {
            "taj" => "saphir_taj",
            "fpr" => "saphir_fpr",
            "foves" => "saphir_foves",
            _ => null
        };
        if (table is null)
            return false;

        await using var connection = await OpenAsync(ct);
        var deleted = await Sql.ExecuteAsync(connection, $"DELETE FROM {table} WHERE id = @id", ct, ("@id", id));
        if (deleted > 0)
            await LogAsync(connection, agent, "Suppression " + kind.ToUpperInvariant(), "Inscription n° " + id, ct);
        return deleted > 0;
    }

    public async Task<List<AuditEntryDto>> JournalAsync(int limit, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        return await Sql.QueryAsync(connection,
            "SELECT date_utc, agent, action, cible FROM saphir_journal ORDER BY date_utc DESC, id DESC LIMIT @limit",
            r => new AuditEntryDto(r.Utc("date_utc"), r.Text("agent"), r.Text("action"), r.Text("cible")),
            ct, ("@limit", Math.Clamp(limit, 1, 500)));
    }

    /// <summary>Trace une consultation (appelé par l'API pour chaque fiche ouverte).</summary>
    public async Task LogConsultationAsync(AgentContext agent, string action, string target, CancellationToken ct)
    {
        try
        {
            await using var connection = await OpenAsync(ct);
            await LogAsync(connection, agent, action, target, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Journal SAPHIR : écriture impossible");
        }
    }

    private static Task LogAsync(MySqlConnection connection, AgentContext agent, string action, string target, CancellationToken ct)
        => Sql.ExecuteAsync(connection,
            "INSERT INTO saphir_journal (date_utc, agent_id, agent, action, cible) VALUES (@now, @id, @agent, @action, @cible)", ct,
            ("@now", DateTime.UtcNow), ("@id", agent.Id), ("@agent", agent.Name),
            ("@action", action.Length > 80 ? action[..80] : action),
            ("@cible", target.Length > 255 ? target[..255] : target));

    // =====================================================================
    // Aides internes
    // =====================================================================

    private sealed record PersonRow(long Id, string? FivemId, IdentityDto Identity);

    private sealed record VehicleRow(
        string PlaqueNorm, string Plaque, string? Marque, string? Modele, string? Couleur, string? Vin, string? DateMec,
        string? TitulaireKey, string? TitulaireNom, string? ControleTechnique, string? Opposition,
        string? Assureur, string? NumeroContrat, string? AssuranceJusquau);

    private static PersonRow ReadPersonRow(MySqlDataReader r) => new(
        r.Long("id"),
        r.Str("fivem_id"),
        new IdentityDto(r.Text("nom"), r.Text("prenom"), r.Str("date_naissance"), r.Str("lieu_naissance"), r.Str("sexe"),
            r.Str("nationalite"), r.Str("adresse"), r.Str("telephone"), r.Str("taille"), r.Str("signalement"), r.Str("alias")));

    private static VehicleRow ReadVehicleRow(MySqlDataReader r) => new(
        r.Text("plaque_norm"), r.Text("plaque"), r.Str("marque"), r.Str("modele"), r.Str("couleur"), r.Str("vin"), r.Str("date_mec"),
        r.Str("titulaire_key"), r.Str("titulaire_nom"), r.Str("controle_technique"), r.Str("opposition"),
        r.Str("assureur"), r.Str("numero_contrat"), r.Str("assurance_jusquau"));

    private static FprEntryDto ReadFpr(MySqlDataReader r) => new(
        r.Long("id"), r.Text("motif"), r.Text("conduite"), r.Str("autorite"), r.Str("details"),
        r.Bool("dangereux"), r.Bool("arme"), r.Bool("actif"), r.Text("auteur"), r.Utc("cree_le"),
        r.Str("clos_par"), r.NullableUtc("clos_le"));

    private static FovesEntryDto ReadFoves(MySqlDataReader r) => new(
        r.Long("id"), r.Text("motif"), r.Text("conduite"), r.Str("details"), r.Bool("actif"), r.Text("auteur"),
        r.Utc("cree_le"), r.Str("clos_par"), r.NullableUtc("clos_le"));

    /// <summary>Fiche SAPHIR et personnage FiveM correspondant à une clé « s:… » ou « f:… ».</summary>
    private async Task<(PersonRow? Row, FiveMPerson? FiveM)> LoadPersonAsync(MySqlConnection connection, string key, CancellationToken ct)
    {
        if (key.StartsWith(SaphirPrefix, StringComparison.Ordinal) && long.TryParse(key[SaphirPrefix.Length..], out var id))
        {
            var row = (await Sql.QueryAsync(connection, "SELECT * FROM saphir_personnes WHERE id = @id", ReadPersonRow, ct, ("@id", id)))
                .FirstOrDefault();
            var linked = row?.FivemId is { } linkedId ? await fivem.GetPersonAsync(linkedId, ct) : null;
            return (row, linked);
        }

        if (key.StartsWith(FivemPrefix, StringComparison.Ordinal) && key.Length > FivemPrefix.Length)
        {
            var fivemId = key[FivemPrefix.Length..];
            var row = (await Sql.QueryAsync(connection, "SELECT * FROM saphir_personnes WHERE fivem_id = @f", ReadPersonRow, ct, ("@f", fivemId)))
                .FirstOrDefault();
            return (row, await fivem.GetPersonAsync(fivemId, ct));
        }

        return (null, null);
    }

    /// <summary>Identifiant SAPHIR de la personne ; pour un personnage FiveM, crée sa fiche au premier enregistrement.</summary>
    private async Task<long?> EnsurePersonIdAsync(MySqlConnection connection, string key, AgentContext agent, CancellationToken ct)
    {
        var (row, fivemPerson) = await LoadPersonAsync(connection, key, ct);
        if (row is not null)
            return row.Id;
        if (fivemPerson is null)
            return null;

        var identity = new IdentityDto(fivemPerson.Nom, fivemPerson.Prenom, fivemPerson.DateNaissance, null, fivemPerson.Sexe,
            fivemPerson.Nationalite, null, fivemPerson.Telephone, fivemPerson.Taille);
        return await InsertPersonAsync(connection, fivemPerson.Id, identity, agent, ct);
    }

    private static Task<long> InsertPersonAsync(MySqlConnection connection, string? fivemId, IdentityDto identity, AgentContext agent, CancellationToken ct)
        => Sql.InsertAsync(connection,
            """
            INSERT INTO saphir_personnes (fivem_id, nom, prenom, date_naissance, lieu_naissance, sexe, nationalite, adresse, telephone,
                taille, signalement, alias, cree_par, cree_le, maj_le)
            VALUES (@fivem, @nom, @prenom, @naissance, @lieu, @sexe, @nationalite, @adresse, @telephone, @taille, @signalement, @alias,
                @agent, @now, @now)
            """, ct,
            [("@fivem", fivemId), .. IdentityParameters(identity), ("@agent", agent.Name), ("@now", DateTime.UtcNow)]);

    private static (string, object?)[] IdentityParameters(IdentityDto identity) =>
    [
        ("@nom", Sql.Clean(identity.Nom, 100)!.ToUpperInvariant()),
        ("@prenom", Sql.Clean(identity.Prenom, 100)),
        ("@naissance", Sql.Clean(identity.DateNaissance, 20)),
        ("@lieu", Sql.Clean(identity.LieuNaissance, 120)),
        ("@sexe", Sql.Clean(identity.Sexe, 10)),
        ("@nationalite", Sql.Clean(identity.Nationalite, 60)),
        ("@adresse", Sql.Clean(identity.Adresse)),
        ("@telephone", Sql.Clean(identity.Telephone, 40)),
        ("@taille", Sql.Clean(identity.Taille, 20)),
        ("@signalement", Sql.Clean(identity.Signalement, 4000)),
        ("@alias", Sql.Clean(identity.Alias))
    ];

    private static void ValidateIdentity(IdentityDto identity)
    {
        if (string.IsNullOrWhiteSpace(identity.Nom) || string.IsNullOrWhiteSpace(identity.Prenom))
            throw new ArgumentException("Le nom et le prénom sont obligatoires.");
    }

    /// <summary>État civil FiveM prioritaire ; les compléments saisis dans SAPHIR priment pour le reste.</summary>
    private static IdentityDto MergeIdentity(PersonRow? row, FiveMPerson? fivemPerson)
    {
        var saphir = row?.Identity;
        if (fivemPerson is null)
            return saphir!;

        return new IdentityDto(
            fivemPerson.Nom.Length > 0 ? fivemPerson.Nom : saphir?.Nom ?? string.Empty,
            fivemPerson.Prenom.Length > 0 ? fivemPerson.Prenom : saphir?.Prenom ?? string.Empty,
            fivemPerson.DateNaissance ?? saphir?.DateNaissance,
            saphir?.LieuNaissance,
            fivemPerson.Sexe ?? saphir?.Sexe,
            saphir?.Nationalite ?? fivemPerson.Nationalite,
            saphir?.Adresse,
            saphir?.Telephone ?? fivemPerson.Telephone,
            saphir?.Taille ?? fivemPerson.Taille,
            saphir?.Signalement,
            saphir?.Alias);
    }

    private async Task<List<VehicleSummaryDto>> VehiclesOfAsync(
        MySqlConnection connection, string publicKey, PersonRow? row, string? fivemId, CancellationToken ct)
    {
        var keys = new List<object?> { publicKey };
        if (row is not null) keys.Add(SaphirPrefix + row.Id);
        if (fivemId is not null) keys.Add(FivemPrefix + fivemId);

        var (inClause, inParameters) = InList("@k", keys.Distinct());
        var result = new Dictionary<string, VehicleSummaryDto>(StringComparer.Ordinal);
        foreach (var v in await Sql.QueryAsync(connection, $"SELECT * FROM saphir_vehicules WHERE titulaire_key IN ({inClause})",
                     ReadVehicleRow, ct, inParameters.ToArray()))
            result[v.PlaqueNorm] = new VehicleSummaryDto(v.Plaque, v.Marque, v.Modele, v.Couleur, v.TitulaireNom, "SAPHIR", false);

        if (fivemId is not null)
        {
            foreach (var v in await fivem.VehiclesOfAsync(fivemId, ct))
            {
                var norm = Sql.NormalizePlate(v.Plaque);
                if (!result.ContainsKey(norm))
                    result[norm] = new VehicleSummaryDto(v.Plaque.Trim(), null, v.Modele, null, v.OwnerName, "FiveM", false);
            }
        }

        if (result.Count > 0)
        {
            var (plates, plateParameters) = InList("@v", result.Keys.Select(k => (object?)k));
            var flagged = await Sql.QueryAsync(connection,
                $"SELECT DISTINCT plaque_norm FROM saphir_foves WHERE actif = 1 AND plaque_norm IN ({plates})",
                r => r.Text("plaque_norm"), ct, plateParameters.ToArray());
            foreach (var norm in flagged)
                if (result.TryGetValue(norm, out var vehicle))
                    result[norm] = vehicle with { Signale = true };
        }

        return result.Values.OrderBy(v => v.Plaque, StringComparer.Ordinal).ToList();
    }

    private static FvaDto BuildFva(VehicleRow? row)
    {
        if (row?.Assureur is null)
            return new FvaDto("Non renseigné", null, null, null);

        var status = "Assuré";
        if (row.AssuranceJusquau is { } until &&
            DateTime.TryParseExact(until, "dd/MM/yyyy", CultureInfo.GetCultureInfo("fr-FR"), DateTimeStyles.None, out var date) &&
            date.Date < DateTime.Today)
            status = "Contrat expiré";
        return new FvaDto(status, row.Assureur, row.NumeroContrat, row.AssuranceJusquau);
    }

    private static async Task<string> PersonLabelAsync(MySqlConnection connection, long personId, CancellationToken ct)
    {
        var rows = await Sql.QueryAsync(connection, "SELECT nom, prenom FROM saphir_personnes WHERE id = @id",
            r => $"{r.Text("nom")} {r.Text("prenom")}", ct, ("@id", personId));
        return rows.FirstOrDefault() ?? "Personne n° " + personId;
    }

    private static string Describe(IdentityDto identity) => $"{identity.Nom.Trim().ToUpperInvariant()} {identity.Prenom.Trim()}";

    private static (string Clause, List<(string, object?)> Parameters) InList(string prefix, IEnumerable<object?> values)
    {
        var parameters = values.Select((value, index) => (prefix + index, value)).ToList();
        return (string.Join(", ", parameters.Select(p => p.Item1)), parameters);
    }
}

/// <summary>Agent connecté qui consulte ou modifie un fichier.</summary>
public sealed record AgentContext(string Id, string Name);
