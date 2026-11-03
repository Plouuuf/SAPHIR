using System.Text;
using Saphir.Server.Data;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Operations;

namespace Saphir.Server.Modules.Operations;

/// <summary>
/// MAIN COURANTE : vacations, équipages, véhicules de service, mentions et journal de service.
/// Toute modification est historisée (sys_historique) et, si utile, inscrite au journal de service (mc_journal).
/// </summary>
public sealed class MainCouranteStore(SaphirDb saphirDb, OperationsNotifier notifier)
{
    // =====================================================================================
    // Vacations (10.1)
    // =====================================================================================

    public async Task<List<VacationResumeDto>> ListerVacationsAsync(string? statut, int limite, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await ListerVacationsAsync(db, statut, limite, ct);
    }

    private static Task<List<VacationResumeDto>> ListerVacationsAsync(SqlSession db, string? statut, int limite, CancellationToken ct)
    {
        var sql = new StringBuilder(
            """
            SELECT v.id, v.reference, v.service, v.debut, v.fin_prevue, v.fin, v.responsable, v.statut,
                   (SELECT COUNT(*) FROM mc_vacation_agents a WHERE a.vacation_id = v.id) AS nb_agents,
                   (SELECT COUNT(*) FROM mc_equipages e WHERE e.vacation_id = v.id AND e.actif = 1) AS nb_equipages
            FROM mc_vacations v
            """);
        var parametres = new List<(string Name, object? Value)>();
        if (!string.IsNullOrWhiteSpace(statut))
        {
            sql.Append(" WHERE v.statut = @statut");
            parametres.Add(("@statut", statut.Trim()));
        }
        sql.Append(" ORDER BY v.debut DESC, v.id DESC LIMIT @limite");
        parametres.Add(("@limite", limite));
        return db.QueryAsync(sql.ToString(), r => new VacationResumeDto(
            r.Long("id"), r.Text("reference"), r.Text("service"), r.Date("debut"), r.NullableDate("fin_prevue"), r.NullableDate("fin"),
            r.NullableText("responsable"), r.Text("statut"), r.Int("nb_agents"), r.Int("nb_equipages")), ct, parametres.ToArray());
    }

    public async Task<VacationDto> LireVacationAsync(long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await LireVacationAsync(db, id, false, ct) ?? throw ApiException.Introuvable("Vacation introuvable.");
    }

    private static async Task<VacationDto?> LireVacationAsync(SqlSession db, long id, bool verrouiller, CancellationToken ct)
    {
        var vacation = await db.FirstOrDefaultAsync(
            "SELECT * FROM mc_vacations WHERE id = @id" + (verrouiller ? " FOR UPDATE" : string.Empty),
            r => new VacationDto(r.Long("id"), r.Text("reference"), r.Text("service"), r.Date("debut"), r.NullableDate("fin_prevue"),
                r.NullableDate("fin"), r.NullableLong("responsable_id"), r.NullableText("responsable"), r.Text("statut"),
                r.NullableText("consignes"), r.NullableText("observations"), [], [], r.Text("cree_par"), r.Date("cree_le"), r.Long("version")),
            ct, ("@id", id));
        if (vacation is null)
            return null;
        var agents = await db.QueryAsync(
            "SELECT agent_id, libelle, presence, observation FROM mc_vacation_agents WHERE vacation_id = @id ORDER BY libelle",
            r => new VacationAgentDto(r.Long("agent_id"), r.Text("libelle"), r.Text("presence"), r.NullableText("observation")), ct, ("@id", id));
        var equipages = await ListerEquipagesAsync(db, id, false, null, ct);
        return vacation with { Agents = agents, Equipages = equipages };
    }

    public async Task<VacationDto> CreerVacationAsync(AgentContext ctx, VacationInput input, CancellationToken ct)
    {
        var (service, debut, finPrevue) = ControlerVacation(input);
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        long id;
        string reference;
        try
        {
            var (responsableId, responsable, agents) = await ResoudreAgentsVacationAsync(db, input, ct);
            await ControlerAgentsLibresAsync(db, agents, null, ct);

            var maintenant = DateTime.UtcNow;
            id = await db.InsertAsync(
                """
                INSERT INTO mc_vacations (service, debut, fin_prevue, responsable_id, responsable, statut, consignes, observations,
                                          cree_par_id, cree_par, cree_le, maj_le, version)
                VALUES (@service, @debut, @fin_prevue, @responsable_id, @responsable, @statut, @consignes, @observations,
                        @auteur_id, @auteur, @maintenant, @maintenant, 1)
                """, ct,
                ("@service", service), ("@debut", OpSql.Utc(debut)), ("@fin_prevue", OpSql.Utc(finPrevue)),
                ("@responsable_id", responsableId), ("@responsable", responsable), ("@statut", VacationStatuts.Ouverte),
                ("@consignes", OpSql.T(input.Consignes, 4000)), ("@observations", OpSql.T(input.Observations, 4000)),
                ("@auteur_id", ctx.Auteur.Id), ("@auteur", ctx.Auteur.Nom), ("@maintenant", maintenant));
            reference = OperationsRegles.Numero(OperationsRegles.PrefixeVacation, debut, id);
            await db.ExecuteAsync("UPDATE mc_vacations SET reference = @reference WHERE id = @id", ct, ("@reference", reference), ("@id", id));
            await EcrireAgentsVacationAsync(db, id, agents, ct);

            await Historique.EcrireAsync(db, ctx.Auteur, OperationsRegles.ObjetVacation, reference, "Ouverture", null, null,
                $"{service} — {agents.Count} agent(s)", null, ct);
            await new EvenementService(JournalTypes.PriseService,
                $"Ouverture de la vacation {reference} — {service}")
            {
                Details = $"Responsable : {responsable ?? "non désigné"}. Agents présents : {agents.Count(a => a.Presence != PresencesAgent.Absent)}, absents : {agents.Count(a => a.Presence == PresencesAgent.Absent)}.",
                VacationId = id
            }.EcrireAsync(db, ctx.Auteur, ct);
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }

        await notifier.MainCouranteAsync("vacation:" + id);
        return (await LireVacationAsync(db, id, false, ct))!;
    }

    public async Task<VacationDto> ModifierVacationAsync(AgentContext ctx, long id, VacationInput input, CancellationToken ct)
    {
        var (service, debut, finPrevue) = ControlerVacation(input);
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        try
        {
            var actuelle = await LireVacationAsync(db, id, true, ct) ?? throw ApiException.Introuvable("Vacation introuvable.");
            if (actuelle.Statut != VacationStatuts.Ouverte)
                throw ApiException.Conflit("La vacation est clôturée : elle ne peut plus être modifiée.");
            OpSql.VerifierVersion(input.Version, actuelle.Version);

            var (responsableId, responsable, agents) = await ResoudreAgentsVacationAsync(db, input, ct);
            await ControlerAgentsLibresAsync(db, agents, id, ct);

            // Un agent retiré de la vacation ne doit plus être membre d'un équipage actif de celle-ci.
            var retires = actuelle.Agents.Select(a => a.AgentId).Except(agents.Select(a => a.AgentId)).ToHashSet();
            foreach (var equipage in actuelle.Equipages.Where(e => e.Actif))
                foreach (var membre in equipage.Membres.Where(m => retires.Contains(m.AgentId)))
                    throw ApiException.Conflit($"{membre.Libelle} est membre de l'équipage {equipage.Indicatif} : retire-le d'abord de l'équipage.");

            await db.ExecuteAsync(
                """
                UPDATE mc_vacations SET service = @service, debut = @debut, fin_prevue = @fin_prevue, responsable_id = @responsable_id,
                       responsable = @responsable, consignes = @consignes, observations = @observations, maj_le = @maintenant,
                       version = version + 1
                WHERE id = @id
                """, ct,
                ("@service", service), ("@debut", OpSql.Utc(debut)), ("@fin_prevue", OpSql.Utc(finPrevue)),
                ("@responsable_id", responsableId), ("@responsable", responsable),
                ("@consignes", OpSql.T(input.Consignes, 4000)), ("@observations", OpSql.T(input.Observations, 4000)),
                ("@maintenant", DateTime.UtcNow), ("@id", id));
            await db.ExecuteAsync("DELETE FROM mc_vacation_agents WHERE vacation_id = @id", ct, ("@id", id));
            await EcrireAgentsVacationAsync(db, id, agents, ct);

            var avant = new { Service = actuelle.Service, Debut = actuelle.Debut, FinPrevue = actuelle.FinPrevue, Responsable = actuelle.Responsable, Consignes = actuelle.Consignes, Observations = actuelle.Observations };
            var apres = new { Service = service, Debut = debut, FinPrevue = finPrevue, Responsable = responsable, Consignes = OpSql.T(input.Consignes, 4000), Observations = OpSql.T(input.Observations, 4000) };
            await OpSql.HistoriserDifferencesAsync(db, ctx.Auteur, OperationsRegles.ObjetVacation, actuelle.Reference, string.Empty, avant, apres, null, ct);
            var agentsAvant = SocleRegles.Liste(actuelle.Agents.OrderBy(a => a.Libelle).Select(a => $"{a.Libelle} ({a.Presence})"));
            var agentsApres = SocleRegles.Liste(agents.OrderBy(a => a.Libelle).Select(a => $"{a.Libelle} ({a.Presence})"));
            if (agentsAvant != agentsApres)
                await Historique.EcrireAsync(db, ctx.Auteur, OperationsRegles.ObjetVacation, actuelle.Reference, "Modification", "agents",
                    agentsAvant, agentsApres, null, ct);
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }

        await notifier.MainCouranteAsync("vacation:" + id);
        return (await LireVacationAsync(db, id, false, ct))!;
    }

    public async Task<VacationDto> CloturerVacationAsync(AgentContext ctx, long id, ClotureVacationInput input, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        try
        {
            var vacation = await LireVacationAsync(db, id, true, ct) ?? throw ApiException.Introuvable("Vacation introuvable.");
            if (vacation.Statut != VacationStatuts.Ouverte)
                throw ApiException.Conflit("La vacation est déjà clôturée.");
            var actifs = vacation.Equipages.Where(e => e.Actif).ToList();
            var engage = actifs.FirstOrDefault(e => EquipageStatuts.EstEngage(e.Statut) || e.InterventionId is not null);
            if (engage is not null)
                throw ApiException.Conflit($"L'équipage {engage.Indicatif} est engagé sur une intervention : termine sa mission avant de clôturer.");
            var sorti = await db.FirstOrDefaultAsync(
                """
                SELECT v.immatriculation FROM mc_vehicules v JOIN mc_equipages e ON e.id = v.equipage_id
                WHERE e.vacation_id = @id AND e.actif = 1 AND v.disponibilite = @sorti LIMIT 1
                """, r => r.Text("immatriculation"), ct, ("@id", id), ("@sorti", VehiculeDisponibilites.Sorti));
            if (sorti is not null)
                throw ApiException.Conflit($"Le véhicule {sorti} est encore sorti : enregistre son retour avant de clôturer la vacation.");

            var fin = input.Fin ?? DateTimeOffset.UtcNow;
            if (fin < vacation.Debut)
                throw ApiException.Saisie("La fin de vacation ne peut pas précéder son début.");
            var maintenant = DateTime.UtcNow;
            foreach (var equipage in actifs)
            {
                await DissoudreAsync(db, equipage.Id, maintenant, ct);
                await Historique.EcrireAsync(db, ctx.Auteur, OperationsRegles.ObjetEquipage, equipage.Indicatif + " #" + equipage.Id,
                    "Dissolution", null, null, null, "Clôture de la vacation " + vacation.Reference, ct);
            }

            var observations = string.IsNullOrWhiteSpace(input.Observations)
                ? vacation.Observations
                : string.IsNullOrWhiteSpace(vacation.Observations) ? input.Observations.Trim() : vacation.Observations + "\n" + input.Observations.Trim();
            await db.ExecuteAsync(
                """
                UPDATE mc_vacations SET statut = @statut, fin = @fin, observations = @observations, maj_le = @maintenant, version = version + 1
                WHERE id = @id
                """, ct,
                ("@statut", VacationStatuts.Cloturee), ("@fin", OpSql.Utc(fin)), ("@observations", OpSql.T(observations, 4000)),
                ("@maintenant", maintenant), ("@id", id));
            await Historique.EcrireAsync(db, ctx.Auteur, OperationsRegles.ObjetVacation, vacation.Reference, "Clôture", "statut",
                VacationStatuts.Ouverte, VacationStatuts.Cloturee, OpSql.T(input.Observations, 500), ct);
            await new EvenementService(JournalTypes.FinVacation, $"Fin de la vacation {vacation.Reference} — {vacation.Service}")
            {
                Details = actifs.Count > 0 ? $"Équipages dissous : {string.Join(", ", actifs.Select(e => e.Indicatif))}." : null,
                VacationId = id
            }.EcrireAsync(db, ctx.Auteur, ct);
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }

        await notifier.MainCouranteAsync("vacation:" + id);
        return (await LireVacationAsync(db, id, false, ct))!;
    }

    private static (string Service, DateTimeOffset Debut, DateTimeOffset? FinPrevue) ControlerVacation(VacationInput input)
    {
        var service = OpSql.T(input.Service, 120) ?? throw ApiException.Saisie("Le service de la vacation est obligatoire.");
        var debut = input.Debut == default ? DateTimeOffset.UtcNow : input.Debut;
        if (input.FinPrevue is { } fin && fin <= debut)
            throw ApiException.Saisie("L'heure de fin prévue doit suivre l'heure de début.");
        if (input.Agents is null)
            throw ApiException.Saisie("Liste des agents absente.");
        if (input.Agents.Select(a => a.AgentId).Distinct().Count() != input.Agents.Count)
            throw ApiException.Saisie("Un agent figure deux fois dans la vacation.");
        if (input.Agents.Any(a => !PresencesAgent.All.Contains(a.Presence)))
            throw ApiException.Saisie("Présence inconnue (Présent, Absent ou En retard).");
        return (service, debut, input.FinPrevue);
    }

    private sealed record AgentVacation(long AgentId, string Libelle, string Presence, string? Observation);

    private static async Task<(long? ResponsableId, string? Responsable, List<AgentVacation> Agents)> ResoudreAgentsVacationAsync(
        SqlSession db, VacationInput input, CancellationToken ct)
    {
        var ids = input.Agents.Select(a => a.AgentId).ToList();
        if (input.ResponsableId is { } rid)
            ids.Add(rid);
        var rh = await AgentRh.LireAsync(db, ids, ct);
        var agents = new List<AgentVacation>();
        foreach (var a in input.Agents)
        {
            if (!rh.TryGetValue(a.AgentId, out var agent))
                throw ApiException.Saisie("Un agent choisi n'existe pas dans le répertoire RH.");
            agents.Add(new AgentVacation(agent.Id, agent.LibelleDepuis(a.Libelle), a.Presence, OpSql.T(a.Observation, 300)));
        }

        string? responsable = null;
        if (input.ResponsableId is { } id)
        {
            if (!rh.TryGetValue(id, out var chef))
                throw ApiException.Saisie("Le responsable choisi n'existe pas dans le répertoire RH.");
            responsable = chef.LibelleDepuis(input.ResponsableLibelle);
        }
        return (input.ResponsableId, responsable, agents);
    }

    /// <summary>Un agent présent ne peut pas figurer dans deux vacations ouvertes.</summary>
    private static async Task ControlerAgentsLibresAsync(SqlSession db, List<AgentVacation> agents, long? vacationId, CancellationToken ct)
    {
        foreach (var agent in agents.Where(a => a.Presence != PresencesAgent.Absent))
        {
            var autre = await db.FirstOrDefaultAsync(
                """
                SELECT v.reference FROM mc_vacation_agents a JOIN mc_vacations v ON v.id = a.vacation_id
                WHERE a.agent_id = @agent AND v.statut = @ouverte AND a.presence <> @absent AND v.id <> @vacation LIMIT 1
                """, r => r.Text("reference"), ct,
                ("@agent", agent.AgentId), ("@ouverte", VacationStatuts.Ouverte), ("@absent", PresencesAgent.Absent),
                ("@vacation", vacationId ?? 0));
            if (autre is not null)
                throw ApiException.Conflit($"{agent.Libelle} est déjà présent dans la vacation ouverte {autre}.");
        }
    }

    private static async Task EcrireAgentsVacationAsync(SqlSession db, long vacationId, List<AgentVacation> agents, CancellationToken ct)
    {
        foreach (var a in agents)
            await db.ExecuteAsync(
                """
                INSERT INTO mc_vacation_agents (vacation_id, agent_id, libelle, presence, observation)
                VALUES (@vacation, @agent, @libelle, @presence, @observation)
                """, ct,
                ("@vacation", vacationId), ("@agent", a.AgentId), ("@libelle", a.Libelle), ("@presence", a.Presence), ("@observation", a.Observation));
    }

    // =====================================================================================
    // Équipages (10.2)
    // =====================================================================================

    public async Task<List<EquipageDto>> ListerEquipagesAsync(long? vacationId, bool actifsSeulement, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await ListerEquipagesAsync(db, vacationId, actifsSeulement, null, ct);
    }

    internal static async Task<List<EquipageDto>> ListerEquipagesAsync(SqlSession db, long? vacationId, bool actifsSeulement, long? equipageId, CancellationToken ct)
    {
        var filtre = new StringBuilder(" WHERE 1 = 1");
        var parametres = new List<(string Name, object? Value)>();
        if (vacationId is { } v)
        {
            filtre.Append(" AND e.vacation_id = @vacation");
            parametres.Add(("@vacation", v));
        }
        if (actifsSeulement)
            filtre.Append(" AND e.actif = 1");
        if (equipageId is { } id)
        {
            filtre.Append(" AND e.id = @equipage");
            parametres.Add(("@equipage", id));
        }

        var membres = (await db.QueryAsync(
                "SELECT m.equipage_id, m.agent_id, m.libelle, m.role FROM mc_equipage_membres m JOIN mc_equipages e ON e.id = m.equipage_id" + filtre,
                r => (EquipageId: r.Long("equipage_id"), Membre: new EquipageMembreDto(r.Long("agent_id"), r.Text("libelle"), r.Text("role"))),
                ct, parametres.ToArray()))
            .GroupBy(m => m.EquipageId)
            .ToDictionary(g => g.Key, g => g.Select(m => m.Membre)
                .OrderBy(m => m.Role == MainCouranteListes.ChefDeBord ? 0 : 1).ThenBy(m => m.Libelle).ToList());

        var equipages = await db.QueryAsync(
            """
            SELECT e.id, e.vacation_id, v.reference AS vacation_reference, e.indicatif, e.type, e.service, e.vehicule_id,
                   vh.immatriculation, vh.marque, vh.modele, e.statut, e.intervention_id, i.numero AS intervention_numero,
                   e.actif, e.cree_le, e.maj_le
            FROM mc_equipages e
            JOIN mc_vacations v ON v.id = e.vacation_id
            LEFT JOIN mc_vehicules vh ON vh.id = e.vehicule_id
            LEFT JOIN pg_interventions i ON i.id = e.intervention_id
            """ + filtre + " ORDER BY e.actif DESC, e.indicatif",
            r =>
            {
                var id = r.Long("id");
                var liste = membres.TryGetValue(id, out var m) ? m : [];
                var chef = liste.FirstOrDefault(x => x.Role == MainCouranteListes.ChefDeBord);
                return new EquipageDto(id, r.Long("vacation_id"), r.Text("vacation_reference"), r.Text("indicatif"), r.Text("type"),
                    r.NullableText("service"), chef?.AgentId, chef?.Libelle, liste, r.NullableLong("vehicule_id"),
                    LibelleVehicule(r.NullableText("immatriculation"), r.NullableText("marque"), r.NullableText("modele")),
                    r.Text("statut"), r.NullableLong("intervention_id"), r.NullableText("intervention_numero"), r.Bool("actif"),
                    r.Date("cree_le"), r.Date("maj_le"));
            }, ct, parametres.ToArray());
        return equipages;
    }

    internal static string? LibelleVehicule(string? immatriculation, string? marque, string? modele)
    {
        if (string.IsNullOrWhiteSpace(immatriculation))
            return null;
        var detail = string.Join(" ", new[] { marque, modele }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return detail.Length == 0 ? immatriculation : $"{immatriculation} ({detail})";
    }

    internal static async Task<EquipageDto?> LireEquipageAsync(SqlSession db, long id, CancellationToken ct)
        => (await ListerEquipagesAsync(db, null, false, id, ct)).FirstOrDefault();

    public async Task<EquipageDto> CreerEquipageAsync(AgentContext ctx, EquipageInput input, CancellationToken ct)
        => await EnregistrerEquipageAsync(ctx, null, input, ct);

    public async Task<EquipageDto> ModifierEquipageAsync(AgentContext ctx, long id, EquipageInput input, CancellationToken ct)
        => await EnregistrerEquipageAsync(ctx, id, input, ct);

    private async Task<EquipageDto> EnregistrerEquipageAsync(AgentContext ctx, long? id, EquipageInput input, CancellationToken ct)
    {
        if (!OperationsRegles.IndicatifValide(input.Indicatif))
            throw ApiException.Saisie("Indicatif radio invalide (2 à 20 caractères : lettres, chiffres, espaces, tirets).");
        var indicatif = input.Indicatif.Trim().ToUpperInvariant();
        if (!MainCouranteListes.TypesEquipage.Contains(input.Type))
            throw ApiException.Saisie("Type d'équipage inconnu.");
        if (OperationsRegles.ControlerMembres(input.Membres) is { } erreurMembres)
            throw ApiException.Saisie(erreurMembres);

        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        long equipageId;
        try
        {
            EquipageDto? actuel = null;
            if (id is { } existant)
            {
                await db.ExecuteAsync("SELECT id FROM mc_equipages WHERE id = @id FOR UPDATE", ct, ("@id", existant));
                actuel = await LireEquipageAsync(db, existant, ct) ?? throw ApiException.Introuvable("Équipage introuvable.");
                if (!actuel.Actif)
                    throw ApiException.Conflit("L'équipage est dissous : il ne peut plus être modifié.");
                if (actuel.VacationId != input.VacationId)
                    throw ApiException.Saisie("Un équipage ne change pas de vacation : dissous-le et constitue-en un nouveau.");
            }

            var vacation = await db.FirstOrDefaultAsync(
                "SELECT id, reference, statut, service FROM mc_vacations WHERE id = @id FOR UPDATE",
                r => new { Id = r.Long("id"), Reference = r.Text("reference"), Statut = r.Text("statut"), Service = r.Text("service") },
                ct, ("@id", input.VacationId)) ?? throw ApiException.Introuvable("Vacation introuvable.");
            if (vacation.Statut != VacationStatuts.Ouverte)
                throw ApiException.Conflit("La vacation est clôturée : impossible d'y constituer un équipage.");

            var doublon = await db.LongAsync(
                "SELECT COUNT(*) FROM mc_equipages WHERE actif = 1 AND indicatif = @indicatif AND id <> @id", ct,
                ("@indicatif", indicatif), ("@id", id ?? 0));
            if (doublon > 0)
                throw ApiException.Conflit($"L'indicatif {indicatif} est déjà utilisé par un équipage actif.");

            // Agents : existants, libres (un seul équipage actif à la fois), ajoutés à la vacation s'ils n'y figurent pas.
            var rh = await AgentRh.LireAsync(db, input.Membres.Select(m => m.AgentId), ct);
            var membres = new List<EquipageMembreDto>();
            foreach (var m in input.Membres)
            {
                if (!rh.TryGetValue(m.AgentId, out var agent))
                    throw ApiException.Saisie("Un agent choisi n'existe pas dans le répertoire RH.");
                var libelle = agent.LibelleDepuis(m.Libelle);
                var autre = await db.FirstOrDefaultAsync(
                    """
                    SELECT e.indicatif FROM mc_equipage_membres m JOIN mc_equipages e ON e.id = m.equipage_id
                    WHERE e.actif = 1 AND m.agent_id = @agent AND e.id <> @id LIMIT 1
                    """, r => r.Text("indicatif"), ct, ("@agent", agent.Id), ("@id", id ?? 0));
                if (autre is not null)
                    throw ApiException.Conflit($"{libelle} est déjà membre de l'équipage actif {autre}.");
                var absent = await db.LongAsync(
                    "SELECT COUNT(*) FROM mc_vacation_agents WHERE vacation_id = @vacation AND agent_id = @agent AND presence = @absent", ct,
                    ("@vacation", vacation.Id), ("@agent", agent.Id), ("@absent", PresencesAgent.Absent));
                if (absent > 0)
                    throw ApiException.Conflit($"{libelle} est noté absent sur la vacation.");
                membres.Add(new EquipageMembreDto(agent.Id, libelle, m.Role));
            }

            // Véhicule : existant, non indisponible, non affecté à un autre équipage actif.
            string? vehiculeLibelle = null;
            if (input.VehiculeId is { } vid)
            {
                var vehicule = await db.FirstOrDefaultAsync(
                    "SELECT id, immatriculation, marque, modele, disponibilite, equipage_id FROM mc_vehicules WHERE id = @id FOR UPDATE",
                    r => new
                    {
                        Immatriculation = r.Text("immatriculation"), Libelle = LibelleVehicule(r.Text("immatriculation"), r.NullableText("marque"), r.NullableText("modele")),
                        Disponibilite = r.Text("disponibilite"), EquipageId = r.NullableLong("equipage_id")
                    }, ct, ("@id", vid)) ?? throw ApiException.Introuvable("Véhicule introuvable.");
                if (vehicule.Disponibilite == VehiculeDisponibilites.Indisponible)
                    throw ApiException.Conflit($"Le véhicule {vehicule.Immatriculation} est indisponible.");
                if (vehicule.EquipageId is { } autreEquipage && autreEquipage != (id ?? 0))
                {
                    var autreActif = await db.FirstOrDefaultAsync("SELECT indicatif FROM mc_equipages WHERE id = @id AND actif = 1",
                        r => r.Text("indicatif"), ct, ("@id", autreEquipage));
                    if (autreActif is not null)
                        throw ApiException.Conflit($"Le véhicule {vehicule.Immatriculation} est affecté à l'équipage {autreActif}.");
                }
                vehiculeLibelle = vehicule.Libelle;
            }

            var maintenant = DateTime.UtcNow;
            var service = OpSql.T(input.Service, 120) ?? vacation.Service;
            if (id is null)
            {
                equipageId = await db.InsertAsync(
                    """
                    INSERT INTO mc_equipages (vacation_id, indicatif, type, service, vehicule_id, statut, intervention_id, actif, cree_le, maj_le)
                    VALUES (@vacation, @indicatif, @type, @service, @vehicule, @statut, NULL, 1, @maintenant, @maintenant)
                    """, ct,
                    ("@vacation", vacation.Id), ("@indicatif", indicatif), ("@type", input.Type), ("@service", service),
                    ("@vehicule", input.VehiculeId), ("@statut", EquipageStatuts.Disponible), ("@maintenant", maintenant));
            }
            else
            {
                equipageId = id.Value;
                await db.ExecuteAsync(
                    "UPDATE mc_equipages SET indicatif = @indicatif, type = @type, service = @service, vehicule_id = @vehicule, maj_le = @maintenant WHERE id = @id",
                    ct, ("@indicatif", indicatif), ("@type", input.Type), ("@service", service), ("@vehicule", input.VehiculeId),
                    ("@maintenant", maintenant), ("@id", equipageId));
                await db.ExecuteAsync("DELETE FROM mc_equipage_membres WHERE equipage_id = @id", ct, ("@id", equipageId));
            }

            // Lien véhicule ↔ équipage (l'ancien véhicule est libéré).
            await db.ExecuteAsync("UPDATE mc_vehicules SET equipage_id = NULL WHERE equipage_id = @id AND (@vehicule IS NULL OR id <> @vehicule)", ct,
                ("@id", equipageId), ("@vehicule", input.VehiculeId));
            if (input.VehiculeId is { } vehiculeId)
                await db.ExecuteAsync("UPDATE mc_vehicules SET equipage_id = @id WHERE id = @vehicule", ct, ("@id", equipageId), ("@vehicule", vehiculeId));

            foreach (var m in membres)
            {
                await db.ExecuteAsync(
                    "INSERT INTO mc_equipage_membres (equipage_id, agent_id, libelle, role) VALUES (@equipage, @agent, @libelle, @role)", ct,
                    ("@equipage", equipageId), ("@agent", m.AgentId), ("@libelle", m.Libelle), ("@role", m.Role));
                // Agent absent de la vacation : ajouté comme présent.
                await db.ExecuteAsync(
                    """
                    INSERT IGNORE INTO mc_vacation_agents (vacation_id, agent_id, libelle, presence, observation)
                    VALUES (@vacation, @agent, @libelle, @present, NULL)
                    """, ct, ("@vacation", vacation.Id), ("@agent", m.AgentId), ("@libelle", m.Libelle), ("@present", PresencesAgent.Present));
            }

            var chef = membres.First(m => m.Role == MainCouranteListes.ChefDeBord);
            var composition = SocleRegles.Liste(membres.Select(m => $"{m.Libelle} ({m.Role})"));
            var reference = indicatif + " #" + equipageId;
            if (actuel is null)
            {
                await Historique.EcrireAsync(db, ctx.Auteur, OperationsRegles.ObjetEquipage, reference, "Constitution", null, null,
                    $"{input.Type} — {composition}" + (vehiculeLibelle is null ? string.Empty : " — " + vehiculeLibelle), vacation.Reference, ct);
                await new EvenementService(JournalTypes.ConstitutionEquipage, $"Équipage {indicatif} constitué ({input.Type})")
                {
                    Details = $"Chef de bord : {chef.Libelle}. Membres : {composition}. Véhicule : {vehiculeLibelle ?? "aucun"}.",
                    VacationId = vacation.Id,
                    EquipageId = equipageId,
                    VehiculeId = input.VehiculeId
                }.EcrireAsync(db, ctx.Auteur, ct);
            }
            else
            {
                var avant = new { Indicatif = actuel.Indicatif, Type = actuel.Type, Service = actuel.Service, Vehicule = actuel.Vehicule,
                    Membres = SocleRegles.Liste(actuel.Membres.Select(m => $"{m.Libelle} ({m.Role})")) };
                var apres = new { Indicatif = indicatif, Type = input.Type, Service = service, Vehicule = vehiculeLibelle, Membres = composition };
                await OpSql.HistoriserDifferencesAsync(db, ctx.Auteur, OperationsRegles.ObjetEquipage, reference, string.Empty, avant, apres, null, ct);
                await new EvenementService(JournalTypes.ModificationEquipage, $"Équipage {indicatif} modifié")
                {
                    Details = $"Membres : {composition}. Véhicule : {vehiculeLibelle ?? "aucun"}.",
                    VacationId = vacation.Id,
                    EquipageId = equipageId,
                    VehiculeId = input.VehiculeId
                }.EcrireAsync(db, ctx.Auteur, ct);
            }
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }

        await notifier.MainCouranteAsync("equipage:" + equipageId);
        await notifier.PegaseAsync("equipage:" + equipageId);
        return (await LireEquipageAsync(db, equipageId, ct))!;
    }

    public async Task DissoudreEquipageAsync(AgentContext ctx, long id, MotifInput input, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        try
        {
            await db.ExecuteAsync("SELECT id FROM mc_equipages WHERE id = @id FOR UPDATE", ct, ("@id", id));
            var equipage = await LireEquipageAsync(db, id, ct) ?? throw ApiException.Introuvable("Équipage introuvable.");
            if (!equipage.Actif)
                throw ApiException.Conflit("L'équipage est déjà dissous.");
            if (EquipageStatuts.EstEngage(equipage.Statut) || equipage.InterventionId is not null)
                throw ApiException.Conflit($"L'équipage {equipage.Indicatif} est engagé sur une intervention : termine sa mission d'abord.");
            await DissoudreAsync(db, id, DateTime.UtcNow, ct);
            await Historique.EcrireAsync(db, ctx.Auteur, OperationsRegles.ObjetEquipage, equipage.Indicatif + " #" + id, "Dissolution",
                "statut", equipage.Statut, EquipageStatuts.HorsService, OpSql.T(input.Motif, 500), ct);
            await new EvenementService(JournalTypes.DissolutionEquipage, $"Équipage {equipage.Indicatif} dissous")
            {
                Details = OpSql.T(input.Motif, 500),
                VacationId = equipage.VacationId,
                EquipageId = id
            }.EcrireAsync(db, ctx.Auteur, ct);
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        await notifier.MainCouranteAsync("equipage:" + id);
        await notifier.PegaseAsync("equipage:" + id);
    }

    private static async Task DissoudreAsync(SqlSession db, long equipageId, DateTime maintenant, CancellationToken ct)
    {
        await db.ExecuteAsync(
            "UPDATE mc_equipages SET actif = 0, statut = @hs, intervention_id = NULL, dissous_le = @maintenant, maj_le = @maintenant WHERE id = @id",
            ct, ("@hs", EquipageStatuts.HorsService), ("@maintenant", maintenant), ("@id", equipageId));
        await db.ExecuteAsync("UPDATE mc_vehicules SET equipage_id = NULL WHERE equipage_id = @id", ct, ("@id", equipageId));
        await db.ExecuteAsync("DELETE FROM geo_positions WHERE equipage_id = @id", ct, ("@id", equipageId));
    }

    // =====================================================================================
    // Véhicules de service (10.3 à 10.5)
    // =====================================================================================

    private const string ColonnesVehicule =
        """
        v.id, v.reference, v.immatriculation, v.marque, v.modele, v.type, v.service, v.indicatif_habituel, v.kilometrage, v.etat,
        v.disponibilite, v.observations, e.id AS equipage_id, e.indicatif AS equipage_indicatif, v.maj_le, v.version
        """;

    private const string JointureVehicule = " FROM mc_vehicules v LEFT JOIN mc_equipages e ON e.id = v.equipage_id AND e.actif = 1";

    private static VehiculeServiceDto LireVehicule(SqlRow r) => new(
        r.Long("id"), r.Text("reference"), r.Text("immatriculation"), r.NullableText("marque"), r.NullableText("modele"), r.Text("type"),
        r.NullableText("service"), r.NullableText("indicatif_habituel"), r.Int("kilometrage"), r.NullableText("etat"), r.Text("disponibilite"),
        r.NullableText("observations"), r.NullableLong("equipage_id"), r.NullableText("equipage_indicatif"), r.Date("maj_le"), r.Long("version"));

    public async Task<List<VehiculeServiceDto>> ListerVehiculesAsync(CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await db.QueryAsync($"SELECT {ColonnesVehicule}{JointureVehicule} ORDER BY v.immatriculation", LireVehicule, ct);
    }

    public async Task<VehiculeFicheDto> FicheVehiculeAsync(long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var vehicule = await db.FirstOrDefaultAsync($"SELECT {ColonnesVehicule}{JointureVehicule} WHERE v.id = @id", LireVehicule, ct, ("@id", id))
                       ?? throw ApiException.Introuvable("Véhicule introuvable.");
        var mouvements = await db.QueryAsync(
            "SELECT * FROM mc_vehicule_mouvements WHERE vehicule_id = @id ORDER BY `date` DESC, id DESC LIMIT 200",
            r => new VehiculeMouvementDto(r.Long("id"), r.Text("type"), r.Date("date"), r.NullableText("equipage"), r.NullableText("conducteur"),
                r.Int("kilometrage"), r.NullableText("carburant"), r.NullableText("etat"), r.NullableText("anomalies"),
                r.NullableText("observations"), r.NullableText("disponibilite"), r.Text("responsable")), ct, ("@id", id));
        var historique = await Historique.ListerAsync(db, OperationsRegles.ObjetVehicule, vehicule.Reference, 200, ct);
        return new VehiculeFicheDto(vehicule, mouvements, historique);
    }

    public async Task<VehiculeServiceDto> EnregistrerVehiculeAsync(AgentContext ctx, long? id, VehiculeServiceInput input, CancellationToken ct)
    {
        var immatriculation = OperationsRegles.NormaliserImmatriculation(input.Immatriculation);
        if (immatriculation.Length is < 4 or > 20)
            throw ApiException.Saisie("Immatriculation invalide.");
        if (!MainCouranteListes.TypesVehicule.Contains(input.Type))
            throw ApiException.Saisie("Type de véhicule inconnu.");
        if (input.Kilometrage is < 0 or > 2_000_000)
            throw ApiException.Saisie("Kilométrage invalide.");
        var apres = new
        {
            Immatriculation = immatriculation, Marque = OpSql.T(input.Marque, 60), Modele = OpSql.T(input.Modele, 60), Type = input.Type,
            Service = OpSql.T(input.Service, 120), IndicatifHabituel = OpSql.T(input.IndicatifHabituel, 20)?.ToUpperInvariant(),
            Kilometrage = input.Kilometrage, Etat = OpSql.T(input.Etat, 300), Observations = OpSql.T(input.Observations, 4000)
        };

        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        long vehiculeId;
        try
        {
            var doublon = await db.LongAsync("SELECT COUNT(*) FROM mc_vehicules WHERE immatriculation = @immat AND id <> @id", ct,
                ("@immat", immatriculation), ("@id", id ?? 0));
            if (doublon > 0)
                throw ApiException.Conflit($"Le véhicule {immatriculation} existe déjà.");
            var maintenant = DateTime.UtcNow;
            if (id is null)
            {
                vehiculeId = await db.InsertAsync(
                    """
                    INSERT INTO mc_vehicules (immatriculation, marque, modele, type, service, indicatif_habituel, kilometrage, etat,
                                              disponibilite, observations, cree_le, maj_le, version)
                    VALUES (@immat, @marque, @modele, @type, @service, @indicatif, @km, @etat, @dispo, @observations, @maintenant, @maintenant, 1)
                    """, ct,
                    ("@immat", apres.Immatriculation), ("@marque", apres.Marque), ("@modele", apres.Modele), ("@type", apres.Type),
                    ("@service", apres.Service), ("@indicatif", apres.IndicatifHabituel), ("@km", apres.Kilometrage), ("@etat", apres.Etat),
                    ("@dispo", VehiculeDisponibilites.Disponible), ("@observations", apres.Observations), ("@maintenant", maintenant));
                var reference = OperationsRegles.Numero(OperationsRegles.PrefixeVehicule, DateTimeOffset.UtcNow, vehiculeId);
                await db.ExecuteAsync("UPDATE mc_vehicules SET reference = @reference WHERE id = @id", ct, ("@reference", reference), ("@id", vehiculeId));
                await Historique.EcrireAsync(db, ctx.Auteur, OperationsRegles.ObjetVehicule, reference, "Création", null, null,
                    $"{apres.Immatriculation} — {apres.Type}", null, ct);
            }
            else
            {
                vehiculeId = id.Value;
                var actuel = await db.FirstOrDefaultAsync($"SELECT {ColonnesVehicule}{JointureVehicule} WHERE v.id = @id FOR UPDATE", LireVehicule, ct, ("@id", vehiculeId))
                             ?? throw ApiException.Introuvable("Véhicule introuvable.");
                OpSql.VerifierVersion(input.Version, actuel.Version);
                await db.ExecuteAsync(
                    """
                    UPDATE mc_vehicules SET immatriculation = @immat, marque = @marque, modele = @modele, type = @type, service = @service,
                           indicatif_habituel = @indicatif, kilometrage = @km, etat = @etat, observations = @observations, maj_le = @maintenant,
                           version = version + 1
                    WHERE id = @id
                    """, ct,
                    ("@immat", apres.Immatriculation), ("@marque", apres.Marque), ("@modele", apres.Modele), ("@type", apres.Type),
                    ("@service", apres.Service), ("@indicatif", apres.IndicatifHabituel), ("@km", apres.Kilometrage), ("@etat", apres.Etat),
                    ("@observations", apres.Observations), ("@maintenant", maintenant), ("@id", vehiculeId));
                var avant = new
                {
                    actuel.Immatriculation, actuel.Marque, actuel.Modele, actuel.Type, actuel.Service, actuel.IndicatifHabituel,
                    actuel.Kilometrage, actuel.Etat, actuel.Observations
                };
                await OpSql.HistoriserDifferencesAsync(db, ctx.Auteur, OperationsRegles.ObjetVehicule, actuel.Reference, string.Empty, avant, apres, null, ct);
            }
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        await notifier.MainCouranteAsync("vehicule:" + vehiculeId);
        return await db.FirstOrDefaultAsync($"SELECT {ColonnesVehicule}{JointureVehicule} WHERE v.id = @id", LireVehicule, ct, ("@id", vehiculeId))
               ?? throw ApiException.Introuvable("Véhicule introuvable.");
    }

    public async Task<VehiculeServiceDto> SortieVehiculeAsync(AgentContext ctx, long id, SortieVehiculeInput input, CancellationToken ct)
    {
        if (input.Carburant is not null && !MainCouranteListes.NiveauxCarburant.Contains(input.Carburant))
            throw ApiException.Saisie("Niveau de carburant inconnu.");
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        try
        {
            var vehicule = await db.FirstOrDefaultAsync($"SELECT {ColonnesVehicule}{JointureVehicule} WHERE v.id = @id FOR UPDATE", LireVehicule, ct, ("@id", id))
                           ?? throw ApiException.Introuvable("Véhicule introuvable.");
            if (vehicule.Disponibilite != VehiculeDisponibilites.Disponible)
                throw ApiException.Conflit($"Le véhicule {vehicule.Immatriculation} n'est pas disponible ({vehicule.Disponibilite}).");
            if (OperationsRegles.ControlerKilometrage(vehicule.Kilometrage, input.Kilometrage) is { } erreurKm)
                throw ApiException.Saisie(erreurKm);

            EquipageDto? equipage = null;
            if (input.EquipageId is { } eid)
            {
                equipage = await LireEquipageAsync(db, eid, ct) ?? throw ApiException.Introuvable("Équipage introuvable.");
                if (!equipage.Actif)
                    throw ApiException.Conflit($"L'équipage {equipage.Indicatif} est dissous.");
                if (vehicule.EquipageId is { } autre && autre != eid)
                    throw ApiException.Conflit($"Le véhicule est affecté à l'équipage {vehicule.EquipageIndicatif}.");
            }
            else if (vehicule.EquipageId is { } affecte)
            {
                equipage = await LireEquipageAsync(db, affecte, ct);
            }

            string? conducteur = null;
            if (input.ConducteurId is { } cid)
            {
                var rh = await AgentRh.LireAsync(db, [cid], ct);
                if (!rh.TryGetValue(cid, out var agent))
                    throw ApiException.Saisie("Le conducteur choisi n'existe pas dans le répertoire RH.");
                conducteur = agent.LibelleDepuis(input.ConducteurLibelle);
            }

            var heure = input.Heure ?? DateTimeOffset.UtcNow;
            var maintenant = DateTime.UtcNow;
            await db.InsertAsync(
                """
                INSERT INTO mc_vehicule_mouvements (vehicule_id, type, `date`, equipage_id, equipage, conducteur_id, conducteur, kilometrage,
                                                    carburant, etat, anomalies, observations, disponibilite, responsable_id, responsable)
                VALUES (@vehicule, 'Sortie', @date, @equipage_id, @equipage, @conducteur_id, @conducteur, @km, @carburant, @etat, @anomalies,
                        NULL, @dispo, @responsable_id, @responsable)
                """, ct,
                ("@vehicule", id), ("@date", OpSql.Utc(heure)), ("@equipage_id", equipage?.Id), ("@equipage", equipage?.Indicatif),
                ("@conducteur_id", input.ConducteurId), ("@conducteur", conducteur), ("@km", input.Kilometrage), ("@carburant", input.Carburant),
                ("@etat", OpSql.T(input.Etat, 300)), ("@anomalies", OpSql.T(input.Anomalies, 4000)), ("@dispo", VehiculeDisponibilites.Sorti),
                ("@responsable_id", ctx.Auteur.Id), ("@responsable", ctx.Auteur.Nom));
            await db.ExecuteAsync(
                """
                UPDATE mc_vehicules SET disponibilite = @sorti, kilometrage = @km, etat = COALESCE(@etat, etat), equipage_id = @equipage,
                       maj_le = @maintenant, version = version + 1
                WHERE id = @id
                """, ct,
                ("@sorti", VehiculeDisponibilites.Sorti), ("@km", input.Kilometrage), ("@etat", OpSql.T(input.Etat, 300)),
                ("@equipage", equipage?.Id), ("@maintenant", maintenant), ("@id", id));
            if (equipage is not null)
            {
                await db.ExecuteAsync("UPDATE mc_vehicules SET equipage_id = NULL WHERE equipage_id = @equipage AND id <> @id", ct,
                    ("@equipage", equipage.Id), ("@id", id));
                await db.ExecuteAsync("UPDATE mc_equipages SET vehicule_id = @id, maj_le = @maintenant WHERE id = @equipage", ct,
                    ("@id", id), ("@maintenant", maintenant), ("@equipage", equipage.Id));
            }

            await Historique.EcrireAsync(db, ctx.Auteur, OperationsRegles.ObjetVehicule, vehicule.Reference, "Sortie", "disponibilite",
                vehicule.Disponibilite, VehiculeDisponibilites.Sorti,
                $"Km {input.Kilometrage}" + (equipage is null ? string.Empty : $", équipage {equipage.Indicatif}"), ct);
            await new EvenementService(JournalTypes.SortieVehicule, $"Sortie du véhicule {vehicule.Immatriculation}" + (equipage is null ? string.Empty : $" — {equipage.Indicatif}"))
            {
                Details = $"Kilométrage : {input.Kilometrage}. Carburant : {input.Carburant ?? "non relevé"}. Conducteur : {conducteur ?? "non précisé"}." +
                          (string.IsNullOrWhiteSpace(input.Anomalies) ? string.Empty : " Anomalies : " + input.Anomalies.Trim()),
                VacationId = equipage?.VacationId,
                EquipageId = equipage?.Id,
                VehiculeId = id
            }.EcrireAsync(db, ctx.Auteur, ct);
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        await notifier.MainCouranteAsync("vehicule:" + id);
        await notifier.PegaseAsync("vehicule:" + id);
        return await db.FirstOrDefaultAsync($"SELECT {ColonnesVehicule}{JointureVehicule} WHERE v.id = @id", LireVehicule, ct, ("@id", id))
               ?? throw ApiException.Introuvable("Véhicule introuvable.");
    }

    public async Task<VehiculeServiceDto> RetourVehiculeAsync(AgentContext ctx, long id, RetourVehiculeInput input, CancellationToken ct)
    {
        if (input.Carburant is not null && !MainCouranteListes.NiveauxCarburant.Contains(input.Carburant))
            throw ApiException.Saisie("Niveau de carburant inconnu.");
        var disponibilite = input.Disponible ? VehiculeDisponibilites.Disponible : VehiculeDisponibilites.Indisponible;
        if (!input.Disponible && string.IsNullOrWhiteSpace(input.Degradations) && string.IsNullOrWhiteSpace(input.Observations))
            throw ApiException.Saisie("Indique les dégradations ou la raison de l'indisponibilité.");

        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        try
        {
            var vehicule = await db.FirstOrDefaultAsync($"SELECT {ColonnesVehicule}{JointureVehicule} WHERE v.id = @id FOR UPDATE", LireVehicule, ct, ("@id", id))
                           ?? throw ApiException.Introuvable("Véhicule introuvable.");
            if (vehicule.Disponibilite != VehiculeDisponibilites.Sorti)
                throw ApiException.Conflit($"Le véhicule {vehicule.Immatriculation} n'est pas sorti.");
            if (OperationsRegles.ControlerKilometrage(vehicule.Kilometrage, input.Kilometrage) is { } erreurKm)
                throw ApiException.Saisie(erreurKm);
            var sortie = await db.FirstOrDefaultAsync(
                "SELECT `date`, kilometrage FROM mc_vehicule_mouvements WHERE vehicule_id = @id AND type = 'Sortie' ORDER BY `date` DESC, id DESC LIMIT 1",
                r => new { Date = r.Date("date"), Km = r.Int("kilometrage") }, ct, ("@id", id));
            var heure = input.Heure ?? DateTimeOffset.UtcNow;
            if (sortie is not null && heure < sortie.Date)
                throw ApiException.Saisie("L'heure de retour précède l'heure de sortie.");

            var maintenant = DateTime.UtcNow;
            var etat = OpSql.T(input.Etat, 300);
            await db.InsertAsync(
                """
                INSERT INTO mc_vehicule_mouvements (vehicule_id, type, `date`, equipage_id, equipage, conducteur_id, conducteur, kilometrage,
                                                    carburant, etat, anomalies, observations, disponibilite, responsable_id, responsable)
                VALUES (@vehicule, 'Retour', @date, @equipage_id, @equipage, NULL, NULL, @km, @carburant, @etat, @degradations, @observations,
                        @dispo, @responsable_id, @responsable)
                """, ct,
                ("@vehicule", id), ("@date", OpSql.Utc(heure)), ("@equipage_id", vehicule.EquipageId), ("@equipage", vehicule.EquipageIndicatif),
                ("@km", input.Kilometrage), ("@carburant", input.Carburant), ("@etat", etat), ("@degradations", OpSql.T(input.Degradations, 4000)),
                ("@observations", OpSql.T(input.Observations, 4000)), ("@dispo", disponibilite),
                ("@responsable_id", ctx.Auteur.Id), ("@responsable", ctx.Auteur.Nom));
            await db.ExecuteAsync(
                """
                UPDATE mc_vehicules SET disponibilite = @dispo, kilometrage = @km, etat = COALESCE(@etat, etat), maj_le = @maintenant,
                       version = version + 1
                WHERE id = @id
                """, ct, ("@dispo", disponibilite), ("@km", input.Kilometrage), ("@etat", etat), ("@maintenant", maintenant), ("@id", id));
            if (!input.Disponible)
            {
                // Véhicule indisponible : retiré de son équipage.
                await db.ExecuteAsync("UPDATE mc_equipages SET vehicule_id = NULL, maj_le = @maintenant WHERE vehicule_id = @id", ct,
                    ("@maintenant", maintenant), ("@id", id));
                await db.ExecuteAsync("UPDATE mc_vehicules SET equipage_id = NULL WHERE id = @id", ct, ("@id", id));
            }

            var distance = sortie is null ? (int?)null : input.Kilometrage - sortie.Km;
            // Rattache le retour à la vacation de l'équipage (sinon absent du journal filtré par vacation).
            var vacationRetour = vehicule.EquipageId is null ? 0
                : await db.LongAsync("SELECT vacation_id FROM mc_equipages WHERE id = @equipage", ct, ("@equipage", vehicule.EquipageId));
            await Historique.EcrireAsync(db, ctx.Auteur, OperationsRegles.ObjetVehicule, vehicule.Reference, "Retour", "disponibilite",
                VehiculeDisponibilites.Sorti, disponibilite, OpSql.T(input.Degradations, 500), ct);
            await new EvenementService(JournalTypes.RetourVehicule, $"Retour du véhicule {vehicule.Immatriculation} — {disponibilite.ToLowerInvariant()}")
            {
                Details = $"Kilométrage : {input.Kilometrage}" + (distance is null ? "." : $" ({distance} km parcourus).") +
                          $" Carburant : {input.Carburant ?? "non relevé"}." +
                          (string.IsNullOrWhiteSpace(input.Degradations) ? string.Empty : " Dégradations : " + input.Degradations.Trim()),
                VacationId = vacationRetour > 0 ? vacationRetour : null,
                EquipageId = vehicule.EquipageId,
                VehiculeId = id
            }.EcrireAsync(db, ctx.Auteur, ct);
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        await notifier.MainCouranteAsync("vehicule:" + id);
        await notifier.PegaseAsync("vehicule:" + id);
        return await db.FirstOrDefaultAsync($"SELECT {ColonnesVehicule}{JointureVehicule} WHERE v.id = @id", LireVehicule, ct, ("@id", id))
               ?? throw ApiException.Introuvable("Véhicule introuvable.");
    }

    public async Task<VehiculeServiceDto> DisponibiliteVehiculeAsync(AgentContext ctx, long id, DisponibiliteVehiculeInput input, CancellationToken ct)
    {
        if (input.Disponibilite is not (VehiculeDisponibilites.Disponible or VehiculeDisponibilites.Indisponible))
            throw ApiException.Saisie("Disponibilité invalide : utilise la sortie et le retour pour un véhicule en mission.");
        if (input.Disponibilite == VehiculeDisponibilites.Indisponible && string.IsNullOrWhiteSpace(input.Motif))
            throw ApiException.Saisie("Le motif d'indisponibilité est obligatoire.");
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        try
        {
            var vehicule = await db.FirstOrDefaultAsync($"SELECT {ColonnesVehicule}{JointureVehicule} WHERE v.id = @id FOR UPDATE", LireVehicule, ct, ("@id", id))
                           ?? throw ApiException.Introuvable("Véhicule introuvable.");
            if (vehicule.Disponibilite == VehiculeDisponibilites.Sorti)
                throw ApiException.Conflit("Le véhicule est sorti : enregistre d'abord son retour.");
            if (vehicule.Disponibilite == input.Disponibilite)
                throw ApiException.Conflit("Le véhicule a déjà cette disponibilité.");
            var maintenant = DateTime.UtcNow;
            await db.ExecuteAsync("UPDATE mc_vehicules SET disponibilite = @dispo, maj_le = @maintenant, version = version + 1 WHERE id = @id", ct,
                ("@dispo", input.Disponibilite), ("@maintenant", maintenant), ("@id", id));
            if (input.Disponibilite == VehiculeDisponibilites.Indisponible)
            {
                await db.ExecuteAsync("UPDATE mc_equipages SET vehicule_id = NULL, maj_le = @maintenant WHERE vehicule_id = @id", ct,
                    ("@maintenant", maintenant), ("@id", id));
                await db.ExecuteAsync("UPDATE mc_vehicules SET equipage_id = NULL WHERE id = @id", ct, ("@id", id));
            }
            await Historique.EcrireAsync(db, ctx.Auteur, OperationsRegles.ObjetVehicule, vehicule.Reference, "Disponibilité", "disponibilite",
                vehicule.Disponibilite, input.Disponibilite, OpSql.T(input.Motif, 500), ct);
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        await notifier.MainCouranteAsync("vehicule:" + id);
        await notifier.PegaseAsync("vehicule:" + id);
        return await db.FirstOrDefaultAsync($"SELECT {ColonnesVehicule}{JointureVehicule} WHERE v.id = @id", LireVehicule, ct, ("@id", id))
               ?? throw ApiException.Introuvable("Véhicule introuvable.");
    }

    // =====================================================================================
    // Mentions et déclarations (9.3)
    // =====================================================================================

    private const string ColonnesMentionResume =
        """
        m.id, m.numero, m.type, m.categorie, m.nature, m.declarant_nom, m.declarant_prenom, m.redacteur, m.`date`, m.statut,
        m.intervention_numero, m.mention_parent_id, p.numero AS parent_numero
        """;

    private static MentionResumeDto LireMentionResume(SqlRow r) => new(
        r.Long("id"), r.Text("numero"), r.Text("type"), r.NullableText("categorie"), r.NullableText("nature"),
        r.NullableText("declarant_nom") is null && r.NullableText("declarant_prenom") is null
            ? null
            : OperationsRegles.LibellePersonne(r.NullableText("declarant_nom"), r.NullableText("declarant_prenom")),
        r.Text("redacteur"), r.Date("date"), r.Text("statut"), r.NullableText("intervention_numero"), r.NullableLong("mention_parent_id"),
        r.NullableText("parent_numero"));

    public async Task<List<MentionResumeDto>> ListerMentionsAsync(string? q, string? type, string? statut, DateTimeOffset? du, DateTimeOffset? au,
        long? interventionId, int limite, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await ListerMentionsAsync(db, q, type, statut, du, au, interventionId, limite, ct);
    }

    internal static Task<List<MentionResumeDto>> ListerMentionsAsync(SqlSession db, string? q, string? type, string? statut,
        DateTimeOffset? du, DateTimeOffset? au, long? interventionId, int limite, CancellationToken ct)
    {
        var sql = new StringBuilder($"SELECT {ColonnesMentionResume} FROM mc_mentions m LEFT JOIN mc_mentions p ON p.id = m.mention_parent_id WHERE 1 = 1");
        var parametres = new List<(string Name, object? Value)>();
        if (!string.IsNullOrWhiteSpace(q))
        {
            sql.Append(" AND (m.numero LIKE @q OR m.nature LIKE @q OR m.categorie LIKE @q OR m.declarant_nom LIKE @q OR m.declarant_prenom LIKE @q")
               .Append(" OR m.redacteur LIKE @q OR m.intervention_numero LIKE @q OR m.faits_lieu LIKE @q)");
            parametres.Add(("@q", OpSql.Like(q)));
        }
        if (!string.IsNullOrWhiteSpace(type))
        {
            sql.Append(" AND m.type = @type");
            parametres.Add(("@type", type.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(statut))
        {
            sql.Append(" AND m.statut = @statut");
            parametres.Add(("@statut", statut.Trim()));
        }
        if (du is { } d)
        {
            sql.Append(" AND m.`date` >= @du");
            parametres.Add(("@du", OpSql.Utc(d)));
        }
        if (au is { } a)
        {
            sql.Append(" AND m.`date` <= @au");
            parametres.Add(("@au", OpSql.Utc(a)));
        }
        if (interventionId is { } i)
        {
            sql.Append(" AND m.intervention_id = @intervention");
            parametres.Add(("@intervention", i));
        }
        sql.Append(" ORDER BY m.`date` DESC, m.id DESC LIMIT @limite");
        parametres.Add(("@limite", limite));
        return db.QueryAsync(sql.ToString(), LireMentionResume, ct, parametres.ToArray());
    }

    public async Task<MentionDto> LireMentionAsync(long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await LireMentionAsync(db, id, false, ct) ?? throw ApiException.Introuvable("Mention introuvable.");
    }

    private static async Task<MentionDto?> LireMentionAsync(SqlSession db, long id, bool verrouiller, CancellationToken ct)
    {
        var mention = await db.FirstOrDefaultAsync(
            "SELECT m.*, p.numero AS parent_numero FROM mc_mentions m LEFT JOIN mc_mentions p ON p.id = m.mention_parent_id WHERE m.id = @id" +
            (verrouiller ? " FOR UPDATE" : string.Empty),
            r => new MentionDto(
                r.Long("id"), r.Text("numero"), r.Text("type"), r.NullableText("categorie"), r.NullableText("origine"), r.Text("statut"),
                r.Date("date"), r.NullableLong("redacteur_id"), r.Text("redacteur"), r.NullableText("redacteur_rio"),
                r.NullableText("redacteur_grade"), r.NullableText("redacteur_service"), r.NullableLong("intervention_id"),
                r.NullableText("intervention_numero"), r.NullableLong("mention_parent_id"), r.NullableText("parent_numero"),
                new MentionDeclarant(r.NullableText("declarant_qualite"), r.NullableLong("declarant_personne_id"),
                    r.NullableText("declarant_personne_reference"), r.NullableText("declarant_nom"), r.NullableText("declarant_prenom"),
                    OpSql.DateOnlyDe(r, "declarant_naissance"), r.NullableText("declarant_lieu_naissance"), r.NullableText("declarant_adresse"),
                    r.NullableText("declarant_telephone"), r.NullableText("declarant_email"), r.NullableText("autres_personnes")),
                new MentionFaits(r.NullableDate("faits_le"), r.NullableText("faits_lieu"), r.NullableText("nature"), r.NullableText("declaration"),
                    r.NullableText("personnes_citees"), r.NullableText("vehicules"), r.NullableText("objets"), r.NullableText("documents"),
                    r.NullableText("observations")),
                new MentionSuites(r.NullableText("mesures"), r.NullableText("orientation"), r.NullableText("service_informe"),
                    r.NullableText("procedure_lrppn")),
                r.NullableText("valide_par"), r.NullableDate("valide_le"), [], r.Long("version")),
            ct, ("@id", id));
        if (mention is null)
            return null;
        var rectificatives = await db.QueryAsync(
            $"SELECT {ColonnesMentionResume} FROM mc_mentions m LEFT JOIN mc_mentions p ON p.id = m.mention_parent_id WHERE m.mention_parent_id = @id ORDER BY m.`date`",
            LireMentionResume, ct, ("@id", id));
        return mention with { Rectificatives = rectificatives };
    }

    /// <summary>Contenu nettoyé d'une mention (valeurs bornées), pour l'écriture et l'historique.</summary>
    private sealed record ContenuMention(string? Categorie, string? Origine, MentionDeclarant Declarant, MentionFaits Faits, MentionSuites Suites);

    private static ContenuMention Nettoyer(MentionInput input)
    {
        var d = input.Declarant ?? new MentionDeclarant(null, null, null, null, null, null, null, null, null, null, null);
        var f = input.Faits ?? new MentionFaits(null, null, null, null, null, null, null, null, null);
        var s = input.Suites ?? new MentionSuites(null, null, null, null);
        return new ContenuMention(
            OpSql.T(input.Categorie, 80), OpSql.T(input.Origine, 60),
            new MentionDeclarant(OpSql.T(d.Qualite, 40), d.PersonneId, OpSql.T(d.PersonneReference, 20),
                OpSql.T(d.Nom, 60)?.ToUpperInvariant(), OpSql.T(d.Prenom, 60) is { } p ? NomsFormat.Prenom(p) : null,
                d.DateNaissance, OpSql.T(d.LieuNaissance, 120), OpSql.T(d.Adresse, 300), OpSql.T(d.Telephone, 30), OpSql.T(d.Email, 120),
                OpSql.T(d.AutresPersonnes, 4000)),
            new MentionFaits(f.Date, OpSql.T(f.Lieu, 300), OpSql.T(f.Nature, 200), OpSql.T(f.Declaration, 60000),
                OpSql.T(f.PersonnesCitees, 4000), OpSql.T(f.Vehicules, 4000), OpSql.T(f.Objets, 4000), OpSql.T(f.Documents, 4000),
                OpSql.T(f.Observations, 4000)),
            new MentionSuites(OpSql.T(s.Mesures, 4000), OpSql.T(s.Orientation, 120), OpSql.T(s.ServiceInforme, 120), OpSql.T(s.ProcedureLrppn, 60)));
    }

    private static (string, object?)[] ParametresContenu(ContenuMention c) =>
    [
        ("@categorie", c.Categorie), ("@origine", c.Origine),
        ("@d_qualite", c.Declarant.Qualite), ("@d_personne", c.Declarant.PersonneId), ("@d_reference", c.Declarant.PersonneReference),
        ("@d_nom", c.Declarant.Nom), ("@d_prenom", c.Declarant.Prenom), ("@d_naissance", OpSql.Param(c.Declarant.DateNaissance)),
        ("@d_lieu", c.Declarant.LieuNaissance), ("@d_adresse", c.Declarant.Adresse), ("@d_tel", c.Declarant.Telephone),
        ("@d_email", c.Declarant.Email), ("@autres", c.Declarant.AutresPersonnes),
        ("@f_date", OpSql.Utc(c.Faits.Date)), ("@f_lieu", c.Faits.Lieu), ("@nature", c.Faits.Nature), ("@declaration", c.Faits.Declaration),
        ("@citees", c.Faits.PersonnesCitees), ("@vehicules", c.Faits.Vehicules), ("@objets", c.Faits.Objets), ("@documents", c.Faits.Documents),
        ("@observations", c.Faits.Observations),
        ("@mesures", c.Suites.Mesures), ("@orientation", c.Suites.Orientation), ("@service_informe", c.Suites.ServiceInforme),
        ("@procedure", c.Suites.ProcedureLrppn)
    ];

    private const string AffectationsContenu =
        """
        categorie = @categorie, origine = @origine, declarant_qualite = @d_qualite, declarant_personne_id = @d_personne,
        declarant_personne_reference = @d_reference, declarant_nom = @d_nom, declarant_prenom = @d_prenom, declarant_naissance = @d_naissance,
        declarant_lieu_naissance = @d_lieu, declarant_adresse = @d_adresse, declarant_telephone = @d_tel, declarant_email = @d_email,
        autres_personnes = @autres, faits_le = @f_date, faits_lieu = @f_lieu, nature = @nature, declaration = @declaration,
        personnes_citees = @citees, vehicules = @vehicules, objets = @objets, documents = @documents, observations = @observations,
        mesures = @mesures, orientation = @orientation, service_informe = @service_informe, procedure_lrppn = @procedure
        """;

    public async Task<MentionDto> CreerMentionAsync(AgentContext ctx, MentionInput input, CancellationToken ct)
    {
        var contenu = Nettoyer(input);
        if (contenu.Faits.Nature is null && contenu.Faits.Declaration is null)
            throw ApiException.Saisie("Indique au moins la nature ou le texte de la mention.");
        var type = input.MentionParentId is null ? input.Type : MentionTypes.Rectificative;
        if (type is not (MentionTypes.Mention or MentionTypes.Declaration or MentionTypes.Rectificative))
            throw ApiException.Saisie("Type de mention inconnu.");
        if (type == MentionTypes.Rectificative && input.MentionParentId is null)
            throw ApiException.Saisie("Une mention rectificative complète une mention validée : choisis la mention d'origine.");

        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        long id;
        try
        {
            string? parentNumero = null;
            if (input.MentionParentId is { } parentId)
            {
                var parent = await db.FirstOrDefaultAsync("SELECT numero, statut FROM mc_mentions WHERE id = @id FOR UPDATE",
                    r => new { Numero = r.Text("numero"), Statut = r.Text("statut") }, ct, ("@id", parentId))
                    ?? throw ApiException.Introuvable("Mention d'origine introuvable.");
                if (parent.Statut != MentionStatuts.Validee)
                    throw ApiException.Conflit("La mention d'origine n'est pas validée : modifie-la directement.");
                parentNumero = parent.Numero;
            }
            var (interventionId, interventionNumero) = await ResoudreInterventionAsync(db, input.InterventionId, ct);

            var maintenant = DateTime.UtcNow;
            var agent = ctx.Agent;
            var parametres = new List<(string, object?)>(ParametresContenu(contenu))
            {
                ("@type", type), ("@statut", MentionStatuts.Brouillon), ("@date", maintenant), ("@redacteur_id", agent.Id),
                ("@redacteur", $"{agent.Nom} {agent.Prenom}"), ("@rio", agent.Rio), ("@grade", agent.Grade),
                ("@service", agent.Service), ("@intervention", interventionId), ("@intervention_numero", interventionNumero),
                ("@parent", input.MentionParentId), ("@maintenant", maintenant)
            };
            id = await db.InsertAsync(
                $"""
                INSERT INTO mc_mentions (type, statut, `date`, redacteur_id, redacteur, redacteur_rio, redacteur_grade, redacteur_service,
                                         intervention_id, intervention_numero, mention_parent_id, cree_le, maj_le, version)
                VALUES (@type, @statut, @date, @redacteur_id, @redacteur, @rio, @grade, @service, @intervention, @intervention_numero,
                        @parent, @maintenant, @maintenant, 1)
                """, ct, parametres.ToArray());
            var numero = OperationsRegles.Numero(OperationsRegles.PrefixeMention, DateTimeOffset.UtcNow, id);
            parametres.Add(("@id", id));
            parametres.Add(("@numero", numero));
            await db.ExecuteAsync($"UPDATE mc_mentions SET numero = @numero, {AffectationsContenu} WHERE id = @id", ct, parametres.ToArray());
            await Historique.EcrireAsync(db, ctx.Auteur, OperationsRegles.ObjetMention, numero, "Création", null, null,
                $"{type}{(contenu.Categorie is null ? string.Empty : " — " + contenu.Categorie)}",
                parentNumero is null ? null : "Additif à " + parentNumero, ct);
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        await notifier.MainCouranteAsync("mention:" + id);
        return (await LireMentionAsync(db, id, false, ct))!;
    }

    public async Task<MentionDto> ModifierMentionAsync(AgentContext ctx, long id, MentionInput input, CancellationToken ct)
    {
        var contenu = Nettoyer(input);
        if (contenu.Faits.Nature is null && contenu.Faits.Declaration is null)
            throw ApiException.Saisie("Indique au moins la nature ou le texte de la mention.");
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        try
        {
            var actuelle = await LireMentionAsync(db, id, true, ct) ?? throw ApiException.Introuvable("Mention introuvable.");
            if (!OperationsRegles.MentionModifiable(actuelle.Statut))
                throw ApiException.Conflit("Une mention validée ne peut pas être modifiée : rédige une mention rectificative.");
            if (actuelle.RedacteurId != ctx.Agent.Id && !ctx.Droits.Possede(Permissions.McMentionValider))
                throw new ApiException(StatusCodes.Status403Forbidden, "Seul le rédacteur ou un valideur peut modifier ce brouillon.");
            OpSql.VerifierVersion(input.Version, actuelle.Version);
            var type = actuelle.MentionParentId is null
                ? (input.Type is MentionTypes.Mention or MentionTypes.Declaration ? input.Type : actuelle.Type)
                : MentionTypes.Rectificative;
            var (interventionId, interventionNumero) = await ResoudreInterventionAsync(db, input.InterventionId, ct);

            var parametres = new List<(string, object?)>(ParametresContenu(contenu))
            {
                ("@type", type), ("@intervention", interventionId), ("@intervention_numero", interventionNumero),
                ("@maintenant", DateTime.UtcNow), ("@id", id)
            };
            await db.ExecuteAsync(
                $"""
                UPDATE mc_mentions SET type = @type, intervention_id = @intervention, intervention_numero = @intervention_numero,
                       {AffectationsContenu}, maj_le = @maintenant, version = version + 1
                WHERE id = @id
                """, ct, parametres.ToArray());

            var avant = new ContenuMention(actuelle.Categorie, actuelle.Origine, actuelle.Declarant, actuelle.Faits, actuelle.Suites);
            if (actuelle.Type != type)
                await Historique.EcrireAsync(db, ctx.Auteur, OperationsRegles.ObjetMention, actuelle.Numero, "Modification", "type", actuelle.Type, type, null, ct);
            if (actuelle.InterventionNumero != interventionNumero)
                await Historique.EcrireAsync(db, ctx.Auteur, OperationsRegles.ObjetMention, actuelle.Numero, "Modification", "intervention",
                    actuelle.InterventionNumero, interventionNumero, null, ct);
            await OpSql.HistoriserDifferencesAsync(db, ctx.Auteur, OperationsRegles.ObjetMention, actuelle.Numero, string.Empty,
                new { Categorie = actuelle.Categorie, Origine = actuelle.Origine }, new { Categorie = contenu.Categorie, Origine = contenu.Origine }, null, ct);
            await OpSql.HistoriserDifferencesAsync(db, ctx.Auteur, OperationsRegles.ObjetMention, actuelle.Numero, "declarant", avant.Declarant, contenu.Declarant, null, ct);
            await OpSql.HistoriserDifferencesAsync(db, ctx.Auteur, OperationsRegles.ObjetMention, actuelle.Numero, "faits", avant.Faits, contenu.Faits, null, ct);
            await OpSql.HistoriserDifferencesAsync(db, ctx.Auteur, OperationsRegles.ObjetMention, actuelle.Numero, "suites", avant.Suites, contenu.Suites, null, ct);
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        await notifier.MainCouranteAsync("mention:" + id);
        return (await LireMentionAsync(db, id, false, ct))!;
    }

    public async Task<MentionDto> ValiderMentionAsync(AgentContext ctx, long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        MentionDto mention;
        try
        {
            mention = await LireMentionAsync(db, id, true, ct) ?? throw ApiException.Introuvable("Mention introuvable.");
            if (mention.Statut != MentionStatuts.Brouillon)
                throw ApiException.Conflit("La mention est déjà validée.");
            if (string.IsNullOrWhiteSpace(mention.Faits.Declaration))
                throw ApiException.Saisie("Le texte de la mention (déclaration / faits) est obligatoire pour la valider.");
            var maintenant = DateTime.UtcNow;
            await db.ExecuteAsync(
                """
                UPDATE mc_mentions SET statut = @validee, valide_par_id = @agent_id, valide_par = @agent, valide_le = @maintenant,
                       maj_le = @maintenant, version = version + 1
                WHERE id = @id
                """, ct,
                ("@validee", MentionStatuts.Validee), ("@agent_id", ctx.Auteur.Id), ("@agent", ctx.Auteur.Nom), ("@maintenant", maintenant), ("@id", id));
            await Historique.EcrireAsync(db, ctx.Auteur, OperationsRegles.ObjetMention, mention.Numero, "Validation", "statut",
                MentionStatuts.Brouillon, MentionStatuts.Validee, null, ct);
            await new EvenementService(JournalTypes.MentionValidee,
                $"{mention.Type} {mention.Numero} validée" + (mention.Categorie is null ? string.Empty : $" — {mention.Categorie}"))
            {
                Details = mention.ParentNumero is null ? mention.Faits.Nature : $"Additif à la mention {mention.ParentNumero}.",
                InterventionId = mention.InterventionId,
                InterventionNumero = mention.InterventionNumero,
                MentionId = id
            }.EcrireAsync(db, ctx.Auteur, ct);
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        await notifier.MainCouranteAsync("mention:" + id);
        if (mention.InterventionId is { } iid)
            await notifier.PegaseAsync("intervention:" + iid);
        return (await LireMentionAsync(db, id, false, ct))!;
    }

    private static async Task<(long? Id, string? Numero)> ResoudreInterventionAsync(SqlSession db, long? interventionId, CancellationToken ct)
    {
        if (interventionId is not { } iid)
            return (null, null);
        var numero = await db.FirstOrDefaultAsync("SELECT numero FROM pg_interventions WHERE id = @id", r => r.Text("numero"), ct, ("@id", iid))
                     ?? throw ApiException.Introuvable("Intervention PÉGASE introuvable.");
        return (iid, numero);
    }

    /// <summary>Projet de mention prérempli depuis une intervention et ses comptes rendus validés (20.2), à vérifier par le rédacteur.</summary>
    public async Task<MentionInput> ProjetMentionAsync(long interventionId, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var intervention = await db.FirstOrDefaultAsync(
            "SELECT numero, nature, sous_motif, adresse, commune, debut_le, procedure_lrppn FROM pg_interventions WHERE id = @id",
            r => new
            {
                Numero = r.Text("numero"), Nature = r.Text("nature"), SousMotif = r.NullableText("sous_motif"), Adresse = r.NullableText("adresse"),
                Commune = r.NullableText("commune"), Debut = r.Date("debut_le"), Procedure = r.NullableText("procedure_lrppn")
            }, ct, ("@id", interventionId)) ?? throw ApiException.Introuvable("Intervention introuvable.");
        var comptesRendus = await db.QueryAsync(
            "SELECT indicatif, resultat, faits, mesures, interpellations, victimes, documents FROM pg_comptes_rendus WHERE intervention_id = @id AND statut = @valide ORDER BY valide_le",
            r => new
            {
                Indicatif = r.NullableText("indicatif"), Resultat = r.NullableText("resultat"), Faits = r.NullableText("faits"),
                Mesures = r.NullableText("mesures"), Interpellations = r.NullableText("interpellations"), Victimes = r.NullableText("victimes"),
                Documents = r.NullableText("documents")
            }, ct, ("@id", interventionId), ("@valide", CompteRenduStatuts.Valide));
        var personnes = await db.QueryAsync(
            "SELECT role, nom, prenom, identite_connue FROM pg_personnes WHERE intervention_id = @id ORDER BY appel_id, ordre",
            r => $"{(r.Bool("identite_connue") ? OperationsRegles.LibellePersonne(r.NullableText("nom"), r.NullableText("prenom")) : "Identité inconnue")} ({r.NullableText("role") ?? "rôle non précisé"})",
            ct, ("@id", interventionId));
        var vehicules = await db.QueryAsync(
            "SELECT immatriculation, marque, modele FROM pg_vehicules WHERE intervention_id = @id ORDER BY appel_id, ordre",
            r => string.Join(" ", new[] { r.NullableText("immatriculation"), r.NullableText("marque"), r.NullableText("modele") }.Where(s => !string.IsNullOrWhiteSpace(s))),
            ct, ("@id", interventionId));

        var texte = new StringBuilder();
        texte.Append($"Intervention PÉGASE {intervention.Numero} — {intervention.Nature}");
        if (intervention.SousMotif is not null) texte.Append($" ({intervention.SousMotif})");
        texte.Append('.');
        foreach (var cr in comptesRendus)
        {
            texte.Append("\n\nCompte rendu ").Append(cr.Indicatif ?? "de l'équipage").Append(" : ").Append(cr.Resultat ?? "résultat non précisé").Append('.');
            if (cr.Faits is not null) texte.Append("\nFaits constatés : ").Append(cr.Faits);
            if (cr.Interpellations is not null) texte.Append("\nInterpellations : ").Append(cr.Interpellations);
            if (cr.Victimes is not null) texte.Append("\nVictimes prises en charge : ").Append(cr.Victimes);
        }

        var mesures = string.Join("\n", comptesRendus.Select(c => c.Mesures).Where(m => !string.IsNullOrWhiteSpace(m)));
        var documents = string.Join("\n", comptesRendus.Select(c => c.Documents).Where(m => !string.IsNullOrWhiteSpace(m)));
        return new MentionInput(
            MentionTypes.Mention, "Compte rendu d'intervention", "Intervention PÉGASE", interventionId, null,
            new MentionDeclarant(null, null, null, null, null, null, null, null, null, null, null),
            new MentionFaits(intervention.Debut,
                string.Join(", ", new[] { intervention.Adresse, intervention.Commune }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct()),
                intervention.Nature, texte.ToString(),
                personnes.Count == 0 ? null : string.Join("\n", personnes),
                vehicules.Count == 0 ? null : string.Join("\n", vehicules.Where(v => v.Length > 0)),
                null, documents.Length == 0 ? null : documents, null),
            new MentionSuites(mesures.Length == 0 ? null : mesures, null, null, intervention.Procedure),
            null);
    }

    // =====================================================================================
    // Journal de service (9.4)
    // =====================================================================================

    public async Task<List<JournalEntreeDto>> ListerJournalAsync(DateTimeOffset? du, DateTimeOffset? au, long? vacationId, string? type, string? q,
        int limite, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await ListerJournalAsync(db, du, au, vacationId, type, q, limite, ct);
    }

    private static Task<List<JournalEntreeDto>> ListerJournalAsync(SqlSession db, DateTimeOffset? du, DateTimeOffset? au, long? vacationId,
        string? type, string? q, int limite, CancellationToken ct)
    {
        var sql = new StringBuilder("SELECT * FROM mc_journal WHERE 1 = 1");
        var parametres = new List<(string Name, object? Value)>();
        if (du is { } d)
        {
            sql.Append(" AND `date` >= @du");
            parametres.Add(("@du", OpSql.Utc(d)));
        }
        if (au is { } a)
        {
            sql.Append(" AND `date` <= @au");
            parametres.Add(("@au", OpSql.Utc(a)));
        }
        if (vacationId is { } v)
        {
            sql.Append(" AND vacation_id = @vacation");
            parametres.Add(("@vacation", v));
        }
        if (!string.IsNullOrWhiteSpace(type))
        {
            sql.Append(" AND type = @type");
            parametres.Add(("@type", type.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            sql.Append(" AND (libelle LIKE @q OR details LIKE @q OR auteur LIKE @q OR intervention_numero LIKE @q)");
            parametres.Add(("@q", OpSql.Like(q)));
        }
        sql.Append(" ORDER BY `date` DESC, id DESC LIMIT @limite");
        parametres.Add(("@limite", limite));
        return db.QueryAsync(sql.ToString(), r => new JournalEntreeDto(
            r.Long("id"), r.Date("date"), r.Text("type"), r.Text("libelle"), r.NullableText("details"), r.Text("source"), r.Text("auteur"),
            r.NullableLong("vacation_id"), r.NullableLong("equipage_id"), r.NullableLong("vehicule_id"), r.NullableLong("intervention_id"),
            r.NullableText("intervention_numero"), r.NullableLong("mention_id")), ct, parametres.ToArray());
    }

    public async Task AjouterJournalAsync(AgentContext ctx, JournalInput input, CancellationToken ct)
    {
        var libelle = OpSql.T(input.Libelle, 300) ?? throw ApiException.Saisie("Le libellé de l'événement est obligatoire.");
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        try
        {
            if (input.VacationId is { } vid && await db.LongAsync("SELECT COUNT(*) FROM mc_vacations WHERE id = @id", ct, ("@id", vid)) == 0)
                throw ApiException.Introuvable("Vacation introuvable.");
            await new EvenementService(JournalTypes.Evenement, libelle) { Details = OpSql.T(input.Details, 4000), VacationId = input.VacationId }
                .EcrireAsync(db, ctx.Auteur, ct);
            await Historique.EcrireAsync(db, ctx.Auteur, OperationsRegles.ObjetJournal, "journal", "Événement saisi", null, null, libelle, null, ct);
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        await notifier.MainCouranteAsync("journal");
    }

    // =====================================================================================
    // Tableau de bord, historique
    // =====================================================================================

    public async Task<McTableauDeBordDto> TableauDeBordAsync(CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var vacations = await ListerVacationsAsync(db, VacationStatuts.Ouverte, 20, ct);
        var equipages = await db.QueryAsync("SELECT statut FROM mc_equipages WHERE actif = 1", r => r.Text("statut"), ct);
        var vehicules = await db.QueryAsync("SELECT disponibilite, COUNT(*) AS n FROM mc_vehicules GROUP BY disponibilite",
            r => (Dispo: r.Text("disponibilite"), N: r.Int("n")), ct);
        var aValider = (int)await db.LongAsync("SELECT COUNT(*) FROM mc_mentions WHERE statut = @b", ct, ("@b", MentionStatuts.Brouillon));
        var debutJour = DebutDuJour();
        var duJour = (int)await db.LongAsync("SELECT COUNT(*) FROM mc_mentions WHERE `date` >= @d", ct, ("@d", OpSql.Utc(debutJour)));
        var journal = await ListerJournalAsync(db, null, null, null, null, null, 15, ct);
        var brouillons = await ListerMentionsAsync(db, null, null, MentionStatuts.Brouillon, null, null, null, 10, ct);
        int Nombre(string dispo) => vehicules.Where(v => v.Dispo == dispo).Sum(v => v.N);
        return new McTableauDeBordDto(vacations, equipages.Count, equipages.Count(EquipageStatuts.EstEngage),
            Nombre(VehiculeDisponibilites.Disponible), Nombre(VehiculeDisponibilites.Sorti), Nombre(VehiculeDisponibilites.Indisponible),
            aValider, duJour, journal, brouillons);
    }

    /// <summary>Minuit (heure de Paris) du jour courant.</summary>
    internal static DateTimeOffset DebutDuJour()
    {
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, OperationsRegles.Fuseau);
        return new DateTimeOffset(local.Date, local.Offset);
    }

    public async Task<List<HistoriqueDto>> HistoriqueAsync(string? reference, string? q, int limite, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await OpSql.HistoriqueAsync(db, OperationsRegles.ObjetsMainCourante, reference, q, limite, ct);
    }
}
