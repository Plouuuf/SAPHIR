using System.Text;
using MySqlConnector;
using Saphir.Server.Data;
using Saphir.Server.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Rh;

namespace Saphir.Server.Modules.Rh;

/// <summary>
/// Personnel : fiches agents, RIO, identité, matricule, grades, affectations, statuts, tableau de bord et référentiels.
/// Toute modification est faite dans une transaction (ligne de l'agent verrouillée) et historisée.
/// Les méthodes de modification retournent l'ID Discord de l'agent concerné (à prévenir : SessionChanged).
/// </summary>
public sealed class RhStore(SaphirDb db, PermissionService droits, ParametresService parametres)
{
    private const int LimiteAgents = 2000;

    // =====================================================================================
    // Lecture du personnel
    // =====================================================================================

    public async Task<List<RhAgentLigneDto>> ListerAgentsAsync(
        string? q, string? statut, long? serviceId, long? uniteId, long? gradeId, CancellationToken ct)
    {
        var sql = new StringBuilder(RhSql.SelectAgent).Append(" WHERE 1 = 1");
        var p = new List<(string Name, object? Value)>();
        AjouterRecherche(sql, p, q);
        if (!string.IsNullOrWhiteSpace(statut))
        {
            sql.Append(" AND a.statut = @statut");
            p.Add(("@statut", statut.Trim()));
        }
        if (serviceId is not null)
        {
            sql.Append(" AND a.service_id = @service");
            p.Add(("@service", serviceId));
        }
        if (uniteId is not null)
        {
            sql.Append(" AND a.unite_id = @unite");
            p.Add(("@unite", uniteId));
        }
        if (gradeId is not null)
        {
            sql.Append(" AND a.grade_id = @grade");
            p.Add(("@grade", gradeId));
        }
        sql.Append(" ORDER BY a.nom, a.prenom, a.id LIMIT @limite");
        p.Add(("@limite", LimiteAgents));

        await using var session = await db.OpenAsync(ct);
        return await session.QueryAsync(sql.ToString(), RhSql.LireAgent, ct, p.ToArray());
    }

    /// <summary>Liste de choix pour tous les modules (GET /api/referentiels/agents) : agents Actifs par défaut.</summary>
    public async Task<List<AgentResumeDto>> ReferentielAgentsAsync(string? q, bool tous, CancellationToken ct)
    {
        var sql = new StringBuilder(RhSql.SelectAgent).Append(" WHERE 1 = 1");
        var p = new List<(string Name, object? Value)>();
        AjouterRecherche(sql, p, q);
        if (!tous)
        {
            sql.Append(" AND a.statut = @actif");
            p.Add(("@actif", AgentStatuts.Actif));
        }
        sql.Append(" ORDER BY a.nom, a.prenom, a.id LIMIT @limite");
        p.Add(("@limite", LimiteAgents));

        await using var session = await db.OpenAsync(ct);
        var lignes = await session.QueryAsync(sql.ToString(), RhSql.LireAgent, ct, p.ToArray());
        return lignes.Select(a => new AgentResumeDto(a.Id, a.Reference, a.Nom, a.Prenom, a.Rio, a.Grade, a.GradeAbrege,
            a.Service, a.Unite, a.Fonction, a.Statut)).ToList();
    }

    private static void AjouterRecherche(StringBuilder sql, List<(string Name, object? Value)> p, string? q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return;
        sql.Append("""
             AND (a.nom LIKE @q OR a.prenom LIKE @q OR a.reference LIKE @q OR a.rio LIKE @q OR a.matricule LIKE @q
                  OR a.discord_id LIKE @q OR CONCAT(a.nom, ' ', a.prenom) LIKE @q OR CONCAT(a.prenom, ' ', a.nom) LIKE @q
                  OR g.libelle LIKE @q OR g.abrege LIKE @q OR s.libelle LIKE @q OR u.libelle LIKE @q OR f.libelle LIKE @q)
            """);
        p.Add(("@q", "%" + SocleRegles.EchapperLike(q.Trim()) + "%"));
    }

    /// <summary>Dossier individuel complet (section 4.5).</summary>
    public async Task<RhDossierDto> LireDossierAsync(long id, bool voirConfidentiel, CancellationToken ct)
    {
        await using var session = await db.OpenAsync(ct);
        var ligne = await session.FirstOrDefaultAsync(RhSql.SelectAgent + " WHERE a.id = @id",
            r => new LigneDossier(RhSql.LireAgent(r), RhSql.Jour(r, "date_integration"), RhSql.Jour(r, "grade_effet"),
                r.NullableDate("derniere_connexion")), ct, ("@id", id))
            ?? throw ApiException.Introuvable("Agent introuvable.");
        var agent = ligne.Agent;

        var affectations = await ListerAffectationsAsync(session, id, false, ct);
        var carriere = await session.QueryAsync(
            """
            SELECT c.*, CONCAT(a.nom, ' ', a.prenom) AS agent_nom
            FROM rh_carriere c JOIN rh_agents a ON a.id = c.agent_id
            WHERE c.agent_id = @id ORDER BY c.effet DESC, c.id DESC
            """, LireCarriere, ct, ("@id", id));
        var rio = await ListerRioAsync(session, id, 500, ct);
        var identites = await session.QueryAsync(
            "SELECT * FROM rh_identites WHERE agent_id = @id ORDER BY effet DESC, id DESC",
            r => new RhIdentiteHistoriqueDto(r.Long("id"), r.Text("ancien_nom"), r.Text("ancien_prenom"), r.Text("nouveau_nom"),
                r.Text("nouveau_prenom"), RhSql.Jour(r, "effet") ?? default, r.NullableText("motif"), r.NullableText("observations"),
                r.Text("auteur"), r.Date("cree_le")), ct, ("@id", id));
        var documents = await RhDocumentsStore.ListerAsync(session,
            new FiltreDocuments(null, null, null, id, null, null, ArchivesSeulement: false, InclureArchives: true), voirConfidentiel, ct);
        var historique = await Historique.ListerAsync(session, ObjetsHistorique.Agent, agent.Reference, 500, ct);

        var calcul = await PermissionService.CalculerAsync(session, id, ct);
        var nomsProfils = await session.QueryAsync(
            "SELECT p.code, p.nom FROM auth_agent_profils ap JOIN auth_profils p ON p.id = ap.profil_id WHERE ap.agent_id = @id ORDER BY p.nom",
            r => r.Text("nom"), ct, ("@id", id));
        var applications = ApplicationsAccessibles(calcul.Codes);
        var habilitations = new RhHabilitationsDto(
            nomsProfils,
            calcul.Codes,
            applications,
            historique.Where(h => h.Champ == "profils" || h.Action.Contains("profil", StringComparison.OrdinalIgnoreCase)
                                  || h.Champ == "statut").ToList());

        return new RhDossierDto(agent, ligne.DateIntegration, ligne.GradeEffet, ligne.DerniereConnexion,
            affectations.FirstOrDefault(a => a.EnCours), affectations, carriere, rio, identites, documents, habilitations, historique);
    }

    /// <summary>Applications ouvertes par les permissions (RESSOURCES HUMAINES exige aussi le rôle Discord RH).</summary>
    public static List<string> ApplicationsAccessibles(IReadOnlyCollection<string> permissions)
    {
        var liste = new List<string>();
        foreach (var app in SaphirApps.All)
        {
            if (app == SaphirApps.Rh)
            {
                if (SaphirApps.CanOpen(app, permissions, roleRh: true))
                    liste.Add(LibelleApplication(app) + " (si rôle Discord RH)");
            }
            else if (SaphirApps.CanOpen(app, permissions, roleRh: false))
                liste.Add(LibelleApplication(app));
        }
        return liste;
    }

    public static string LibelleApplication(string app) => app switch
    {
        SaphirApps.Cheops => "CHEOPS",
        SaphirApps.Pegase => "PÉGASE",
        SaphirApps.MainCourante => "MAIN COURANTE",
        SaphirApps.Lrppn => "LRPPN",
        SaphirApps.Rh => "RESSOURCES HUMAINES",
        SaphirApps.Neo => "NEO",
        SaphirApps.Pve => "PVe",
        SaphirApps.Tchap => "Tchap",
        SaphirApps.Annuaire => "Annuaire",
        SaphirApps.Messagerie => "Messagerie",
        SaphirApps.Mrz => "MRZ",
        SaphirApps.Notes => "Notes",
        SaphirApps.Bodycam => "Bodycam",
        SaphirApps.Administration => "Administration",
        SaphirApps.Parametres => "Centre de paramètres",
        _ => app
    };

    public async Task<List<RhAffectationDto>> ListerAffectationsAsync(bool enCoursSeulement, CancellationToken ct)
    {
        await using var session = await db.OpenAsync(ct);
        return await ListerAffectationsAsync(session, null, enCoursSeulement, ct);
    }

    private static Task<List<RhAffectationDto>> ListerAffectationsAsync(SqlSession session, long? agentId, bool enCours, CancellationToken ct)
    {
        var sql = new StringBuilder("""
            SELECT af.*, CONCAT(a.nom, ' ', a.prenom) AS agent_nom,
                   s.libelle AS service, u.libelle AS unite, f.libelle AS fonction,
                   CONCAT(r.nom, ' ', r.prenom) AS responsable
            FROM rh_affectations af
            JOIN rh_agents a ON a.id = af.agent_id
            LEFT JOIN rh_services s ON s.id = af.service_id
            LEFT JOIN rh_unites u ON u.id = af.unite_id
            LEFT JOIN rh_fonctions f ON f.id = af.fonction_id
            LEFT JOIN rh_agents r ON r.id = af.responsable_id
            WHERE 1 = 1
            """);
        var p = new List<(string Name, object? Value)>();
        if (agentId is not null)
        {
            sql.Append(" AND af.agent_id = @agent");
            p.Add(("@agent", agentId));
        }
        if (enCours)
            sql.Append(" AND af.fin IS NULL");
        sql.Append(" ORDER BY af.debut DESC, af.id DESC LIMIT 2000");
        return session.QueryAsync(sql.ToString(), r => new RhAffectationDto(
            r.Long("id"), r.Long("agent_id"), r.Text("agent_nom"),
            r.NullableLong("service_id"), r.NullableText("service"),
            r.NullableLong("unite_id"), r.NullableText("unite"),
            r.NullableLong("fonction_id"), r.NullableText("fonction"),
            r.NullableLong("responsable_id"), r.NullableText("responsable"),
            RhSql.Jour(r, "debut") ?? default, RhSql.Jour(r, "fin"),
            r.NullableText("motif"), r.NullableText("observations"), r.Text("auteur"), r.Date("cree_le")), ct, p.ToArray());
    }

    private static RhCarriereDto LireCarriere(SqlRow r) => new(
        r.Long("id"), r.Long("agent_id"), r.Text("agent_nom"), r.NullableText("ancien_grade"), r.Text("grade"),
        RhSql.Jour(r, "effet") ?? default, r.NullableText("motif"), r.NullableText("observations"), r.NullableLong("document_id"),
        r.Text("auteur"), r.Date("cree_le"));

    public async Task<List<RhRioHistoriqueDto>> ListerRioAsync(int limite, CancellationToken ct)
    {
        await using var session = await db.OpenAsync(ct);
        return await ListerRioAsync(session, null, limite, ct);
    }

    private static Task<List<RhRioHistoriqueDto>> ListerRioAsync(SqlSession session, long? agentId, int limite, CancellationToken ct)
        => session.QueryAsync(
            """
            SELECT h.*, CONCAT(a.nom, ' ', a.prenom) AS agent_nom
            FROM rh_rio_historique h JOIN rh_agents a ON a.id = h.agent_id
            WHERE (@agent IS NULL OR h.agent_id = @agent)
            ORDER BY h.cree_le DESC, h.id DESC
            LIMIT @limite
            """,
            r => new RhRioHistoriqueDto(r.Long("id"), r.Long("agent_id"), r.Text("agent_nom"), r.NullableText("ancien"),
                r.Text("nouveau"), RhSql.Jour(r, "effet") ?? default, r.NullableText("motif"), r.NullableText("observations"),
                r.Text("auteur"), r.Date("cree_le")), ct, ("@agent", agentId), ("@limite", limite));

    public async Task<List<HistoriqueDto>> HistoriqueAsync(string? objet, string? reference, int limite, CancellationToken ct)
    {
        await using var session = await db.OpenAsync(ct);
        return await RhSql.HistoriqueRhAsync(session, objet, reference, limite, ct);
    }

    // =====================================================================================
    // Tableau de bord (4.3)
    // =====================================================================================

    public async Task<RhTableauBordDto> TableauBordAsync(CancellationToken ct)
    {
        await using var session = await db.OpenAsync(ct);
        var parStatut = await session.QueryAsync("SELECT statut AS libelle, COUNT(*) AS nombre FROM rh_agents GROUP BY statut",
            LireCompteur, ct);
        int Statut(string s) => parStatut.FirstOrDefault(c => c.Libelle == s)?.Nombre ?? 0;

        var archive = ("@archive", (object?)AgentStatuts.Archive);
        var sansRio = (int)await session.LongAsync(
            "SELECT COUNT(*) FROM rh_agents WHERE (rio IS NULL OR rio = '') AND statut <> @archive", ct, archive);
        var parGrade = await session.QueryAsync(
            """
            SELECT COALESCE(g.libelle, 'Sans grade') AS libelle, COUNT(*) AS nombre
            FROM rh_agents a LEFT JOIN rh_grades g ON g.id = a.grade_id
            WHERE a.statut <> @archive
            GROUP BY COALESCE(g.libelle, 'Sans grade')
            ORDER BY MIN(COALESCE(g.ordre, 999999)), libelle
            """, LireCompteur, ct, archive);
        var parService = await session.QueryAsync(
            """
            SELECT COALESCE(s.libelle, 'Sans service') AS libelle, COUNT(*) AS nombre
            FROM rh_agents a LEFT JOIN rh_services s ON s.id = a.service_id
            WHERE a.statut <> @archive
            GROUP BY COALESCE(s.libelle, 'Sans service')
            ORDER BY MIN(COALESCE(s.ordre, 999999)), libelle
            """, LireCompteur, ct, archive);
        var parUnite = await session.QueryAsync(
            """
            SELECT COALESCE(u.libelle, 'Sans unité') AS libelle, COUNT(*) AS nombre
            FROM rh_agents a LEFT JOIN rh_unites u ON u.id = a.unite_id
            WHERE a.statut <> @archive
            GROUP BY COALESCE(u.libelle, 'Sans unité')
            ORDER BY MIN(COALESCE(u.ordre, 999999)), libelle
            """, LireCompteur, ct, archive);
        var aValider = (int)await session.LongAsync("SELECT COUNT(*) FROM rh_documents WHERE statut = @statut", ct,
            ("@statut", RhDocumentStatuts.AValider));
        var modifications = await RhSql.HistoriqueRhAsync(session, null, null, 12, ct);
        var creations = await session.QueryAsync(RhSql.SelectAgent + " ORDER BY a.cree_le DESC, a.id DESC LIMIT 8", RhSql.LireAgent, ct);

        var ordreStatuts = AgentStatuts.All.ToList();
        return new RhTableauBordDto(
            parStatut.Sum(c => c.Nombre),
            Statut(AgentStatuts.Actif),
            Statut(AgentStatuts.Suspendu),
            Statut(AgentStatuts.Inactif),
            Statut(AgentStatuts.Archive),
            Statut(AgentStatuts.ProfilACompleter),
            sansRio,
            aValider,
            ordreStatuts.Select(s => new RhCompteurDto(s, Statut(s))).ToList(),
            parGrade, parService, parUnite, modifications, creations);
    }

    private static RhCompteurDto LireCompteur(SqlRow r) => new(r.Text("libelle"), r.Int("nombre"));

    public async Task<RhParametresDto> ParametresAsync(CancellationToken ct)
        => new(
            await parametres.ValeurAsync(ParametresCles.RioFormat, ct),
            await parametres.ValeurAsync(ParametresCles.RioExemple, ct),
            await parametres.ValeurAsync(ParametresCles.NomOrganisation, ct),
            RhFichiers.TailleMax,
            RhFichiers.Extensions);

    // =====================================================================================
    // Modifications d'un agent
    // =====================================================================================

    /// <summary>Modification du NOM et du prénom (section 4.7) : motif obligatoire, ancienne identité conservée.</summary>
    public Task<string> ModifierIdentiteAsync(Auteur auteur, long id, RhIdentiteInput input, CancellationToken ct)
        => ModifierAsync(id, ct, async (session, agent) =>
        {
            if (!NomsFormat.EstValide(input.Nom))
                throw ApiException.Saisie("NOM invalide : 2 à 60 caractères, lettres, espaces, traits d'union ou apostrophes.");
            if (!NomsFormat.EstValide(input.Prenom))
                throw ApiException.Saisie("Prénom invalide : 2 à 60 caractères, lettres, espaces, traits d'union ou apostrophes.");
            var nom = NomsFormat.Nom(input.Nom);
            var prenom = NomsFormat.Prenom(input.Prenom);
            if (nom == agent.Nom && prenom == agent.Prenom)
                throw ApiException.Saisie("Le NOM et le prénom sont inchangés.");
            var motif = RhSql.MotifObligatoire(input.Motif);
            var effet = input.DateEffet ?? RhSql.Aujourdhui();

            await session.ExecuteAsync("UPDATE rh_agents SET nom = @nom, prenom = @prenom WHERE id = @id", ct,
                ("@nom", nom), ("@prenom", prenom), ("@id", agent.Id));
            await session.ExecuteAsync(
                """
                INSERT INTO rh_identites (agent_id, ancien_nom, ancien_prenom, nouveau_nom, nouveau_prenom, effet, motif, observations, auteur_id, auteur, cree_le)
                VALUES (@agent, @ancien_nom, @ancien_prenom, @nom, @prenom, @effet, @motif, @obs, @auteur_id, @auteur, @maintenant)
                """, ct,
                ("@agent", agent.Id), ("@ancien_nom", agent.Nom), ("@ancien_prenom", agent.Prenom), ("@nom", nom), ("@prenom", prenom),
                ("@effet", RhSql.P(effet)), ("@motif", motif), ("@obs", SocleRegles.Tronquer(input.Observations, 2000)),
                ("@auteur_id", auteur.Id), ("@auteur", auteur.Nom), ("@maintenant", DateTime.UtcNow));
            await Historique.EcrireAsync(session, auteur, ObjetsHistorique.Agent, agent.Reference, "Modification de l'identité",
                "identite", $"{agent.Nom} {agent.Prenom}", $"{nom} {prenom} (effet {RhRegles.Date(effet)})", motif, ct);
        }, auteur);

    public Task<string> ModifierMatriculeAsync(Auteur auteur, long id, RhMatriculeInput input, CancellationToken ct)
        => ModifierAsync(id, ct, async (session, agent) =>
        {
            var matricule = RhRegles.NormaliserMatricule(input.Matricule)
                            ?? throw ApiException.Saisie("Matricule invalide : 2 à 32 caractères (lettres majuscules, chiffres, tirets).");
            if (matricule == agent.Matricule)
                throw ApiException.Saisie("Le matricule est inchangé.");
            var motif = RhSql.MotifObligatoire(input.Motif);
            var pris = await session.LongAsync("SELECT COUNT(*) FROM rh_agents WHERE matricule = @m AND id <> @id", ct,
                ("@m", matricule), ("@id", agent.Id));
            if (pris > 0)
                throw ApiException.Conflit("Ce matricule est déjà attribué à un autre agent.");
            await ExecuterUniqueAsync(() => session.ExecuteAsync("UPDATE rh_agents SET matricule = @m WHERE id = @id", ct,
                ("@m", matricule), ("@id", agent.Id)), "Ce matricule est déjà attribué à un autre agent.");
            await Historique.EcrireAsync(session, auteur, ObjetsHistorique.Agent, agent.Reference, "Modification du matricule",
                "matricule", agent.Matricule, matricule, motif, ct);
        }, auteur);

    public Task<string> ModifierIntegrationAsync(Auteur auteur, long id, RhIntegrationInput input, CancellationToken ct)
        => ModifierAsync(id, ct, async (session, agent) =>
        {
            var actuelle = await session.FirstOrDefaultAsync(
                "SELECT COALESCE(date_integration, DATE(cree_le)) AS d FROM rh_agents WHERE id = @id",
                r => new Boite<DateOnly?>(RhSql.Jour(r, "d")), ct, ("@id", agent.Id));
            if (input.DateIntegration is null)
                throw ApiException.Saisie("La date d'intégration est obligatoire.");
            if (actuelle?.Valeur == input.DateIntegration)
                throw ApiException.Saisie("La date d'intégration est inchangée.");
            await session.ExecuteAsync("UPDATE rh_agents SET date_integration = @d WHERE id = @id", ct,
                ("@d", RhSql.P(input.DateIntegration)), ("@id", agent.Id));
            await Historique.EcrireAsync(session, auteur, ObjetsHistorique.Agent, agent.Reference, "Date d'intégration",
                "date_integration", RhRegles.Date(actuelle?.Valeur), RhRegles.Date(input.DateIntegration), SocleRegles.Tronquer(input.Motif, 500), ct);
        }, auteur);

    /// <summary>
    /// Attribution ou changement de RIO (4.6) : format du paramètre rh.rio.format, unicité, jamais de réattribution
    /// d'un RIO déjà porté par un autre agent ; ancien RIO conservé dans l'historique.
    /// </summary>
    public Task<string> AttribuerRioAsync(Auteur auteur, long id, RhRioInput input, CancellationToken ct)
        => ModifierAsync(id, ct, async (session, agent) =>
        {
            var rio = (input.Rio ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
            var format = await session.ScalarAsync("SELECT valeur FROM sys_parametres WHERE cle = @cle", ct,
                ("@cle", ParametresCles.RioFormat)) as string ?? SocleRegles.RioFormatParDefaut;
            if (!SocleRegles.RioValide(rio, format))
                throw ApiException.Saisie($"RIO invalide : il ne respecte pas le format en vigueur ({format}).");
            if (rio == agent.Rio)
                throw ApiException.Saisie("C'est déjà le RIO de cet agent.");
            var changement = !string.IsNullOrWhiteSpace(agent.Rio);
            var motif = changement
                ? RhSql.MotifObligatoire(input.Motif, "Le motif est obligatoire pour changer un RIO.")
                : SocleRegles.Tronquer(input.Motif, 500) ?? "Attribution initiale";

            var porteur = await session.FirstOrDefaultAsync(
                "SELECT CONCAT(nom, ' ', prenom, ' (', COALESCE(reference, ''), ')') AS libelle FROM rh_agents WHERE rio = @rio AND id <> @id",
                r => r.Text("libelle"), ct, ("@rio", rio), ("@id", agent.Id));
            if (porteur is not null)
                throw ApiException.Conflit($"Ce RIO est déjà attribué à {porteur}.");
            var dejaAttribue = await session.LongAsync(
                "SELECT COUNT(*) FROM rh_rio_historique WHERE (nouveau = @rio OR ancien = @rio) AND agent_id <> @id", ct,
                ("@rio", rio), ("@id", agent.Id));
            if (dejaAttribue > 0)
                throw ApiException.Conflit("Ce RIO a déjà été attribué à un autre agent : un RIO n'est jamais réattribué.");

            var effet = input.DateEffet ?? RhSql.Aujourdhui();
            await ExecuterUniqueAsync(() => session.ExecuteAsync("UPDATE rh_agents SET rio = @rio WHERE id = @id", ct,
                ("@rio", rio), ("@id", agent.Id)), "Ce RIO est déjà attribué à un autre agent.");
            await session.ExecuteAsync(
                """
                INSERT INTO rh_rio_historique (agent_id, ancien, nouveau, effet, motif, observations, auteur_id, auteur, cree_le)
                VALUES (@agent, @ancien, @nouveau, @effet, @motif, @obs, @auteur_id, @auteur, @maintenant)
                """, ct,
                ("@agent", agent.Id), ("@ancien", agent.Rio), ("@nouveau", rio), ("@effet", RhSql.P(effet)), ("@motif", motif),
                ("@obs", SocleRegles.Tronquer(input.Observations, 2000)), ("@auteur_id", auteur.Id), ("@auteur", auteur.Nom),
                ("@maintenant", DateTime.UtcNow));
            await Historique.EcrireAsync(session, auteur, ObjetsHistorique.Agent, agent.Reference,
                changement ? "Changement de RIO" : "Attribution du RIO", "rio", agent.Rio, $"{rio} (effet {RhRegles.Date(effet)})", motif, ct);
        }, auteur);

    /// <summary>Nouveau grade (4.8) : historique de carrière (libellés figés au moment du changement).</summary>
    public Task<string> ChangerGradeAsync(Auteur auteur, long id, RhGradeInput input, CancellationToken ct)
        => ModifierAsync(id, ct, async (session, agent) =>
        {
            var grade = await LireElementAsync(session, RhReferentielTypes.Grades, input.GradeId, ct)
                        ?? throw ApiException.Saisie("Grade introuvable.");
            if (!grade.Actif)
                throw ApiException.Saisie("Ce grade est désactivé.");
            if (agent.GradeId == grade.Id)
                throw ApiException.Saisie("L'agent a déjà ce grade.");
            var motif = RhSql.MotifObligatoire(input.Motif);
            if (input.DocumentId is { } documentId &&
                await session.LongAsync("SELECT COUNT(*) FROM rh_documents WHERE id = @d", ct, ("@d", documentId)) == 0)
                throw ApiException.Saisie("Document justificatif introuvable.");
            var ancien = agent.GradeId is { } ancienId ? await LireElementAsync(session, RhReferentielTypes.Grades, ancienId, ct) : null;
            var effet = input.DateEffet ?? RhSql.Aujourdhui();

            await session.ExecuteAsync("UPDATE rh_agents SET grade_id = @g, grade_effet = @effet WHERE id = @id", ct,
                ("@g", grade.Id), ("@effet", RhSql.P(effet)), ("@id", agent.Id));
            await session.ExecuteAsync(
                """
                INSERT INTO rh_carriere (agent_id, ancien_grade_id, ancien_grade, grade_id, grade, effet, motif, observations, document_id, auteur_id, auteur, cree_le)
                VALUES (@agent, @ancien_id, @ancien, @grade_id, @grade, @effet, @motif, @obs, @doc, @auteur_id, @auteur, @maintenant)
                """, ct,
                ("@agent", agent.Id), ("@ancien_id", ancien?.Id), ("@ancien", ancien?.Libelle), ("@grade_id", grade.Id),
                ("@grade", grade.Libelle), ("@effet", RhSql.P(effet)), ("@motif", motif),
                ("@obs", SocleRegles.Tronquer(input.Observations, 2000)), ("@doc", input.DocumentId),
                ("@auteur_id", auteur.Id), ("@auteur", auteur.Nom), ("@maintenant", DateTime.UtcNow));
            await Historique.EcrireAsync(session, auteur, ObjetsHistorique.Agent, agent.Reference, "Changement de grade",
                "grade", ancien?.Libelle, $"{grade.Libelle} (effet {RhRegles.Date(effet)})", motif, ct);
        }, auteur);

    /// <summary>
    /// Nouvelle affectation (4.10) : l'affectation en cours est close à la date d'effet, la nouvelle devient courante.
    /// L'unité doit appartenir au service.
    /// </summary>
    public Task<string> AffecterAsync(Auteur auteur, long id, RhAffectationInput input, CancellationToken ct)
        => ModifierAsync(id, ct, async (session, agent) =>
        {
            var service = await LireElementAsync(session, RhReferentielTypes.Services, input.ServiceId, ct)
                          ?? throw ApiException.Saisie("Service introuvable.");
            var unite = await LireElementAsync(session, RhReferentielTypes.Unites, input.UniteId, ct)
                        ?? throw ApiException.Saisie("Unité introuvable.");
            if (!service.Actif || !unite.Actif)
                throw ApiException.Saisie("Le service ou l'unité choisi est désactivé.");
            if (unite.ParentId != service.Id)
                throw ApiException.Saisie($"L'unité « {unite.Libelle} » n'appartient pas au service « {service.Libelle} ».");
            Element? fonction = null;
            if (input.FonctionId is { } fonctionId)
            {
                fonction = await LireElementAsync(session, RhReferentielTypes.Fonctions, fonctionId, ct)
                           ?? throw ApiException.Saisie("Fonction introuvable.");
                if (!fonction.Actif)
                    throw ApiException.Saisie("Cette fonction est désactivée.");
            }
            string? responsable = null;
            if (input.ResponsableId is { } responsableId)
            {
                if (responsableId == agent.Id)
                    throw ApiException.Saisie("Un agent ne peut pas être son propre responsable.");
                responsable = await session.FirstOrDefaultAsync(
                    "SELECT CONCAT(nom, ' ', prenom) AS libelle FROM rh_agents WHERE id = @id", r => r.Text("libelle"), ct,
                    ("@id", responsableId)) ?? throw ApiException.Saisie("Responsable introuvable.");
            }
            var motif = RhSql.MotifObligatoire(input.Motif);
            var effet = input.DateEffet ?? RhSql.Aujourdhui();

            var courante = await session.FirstOrDefaultAsync(
                "SELECT id, debut, service_id, unite_id, fonction_id, responsable_id FROM rh_affectations WHERE agent_id = @agent AND fin IS NULL ORDER BY debut DESC, id DESC LIMIT 1 FOR UPDATE",
                r => new Courante(r.Long("id"), RhSql.Jour(r, "debut") ?? default, r.NullableLong("service_id"), r.NullableLong("unite_id"),
                    r.NullableLong("fonction_id"), r.NullableLong("responsable_id")), ct, ("@agent", agent.Id));
            if (courante is not null)
            {
                if (courante.ServiceId == service.Id && courante.UniteId == unite.Id && courante.FonctionId == fonction?.Id &&
                    courante.ResponsableId == input.ResponsableId)
                    throw ApiException.Saisie("L'affectation demandée est identique à l'affectation actuelle.");
                if (effet < courante.Debut)
                    throw ApiException.Saisie($"La date d'effet précède le début de l'affectation actuelle ({RhRegles.Date(courante.Debut)}).");
                await session.ExecuteAsync("UPDATE rh_affectations SET fin = @fin WHERE id = @id", ct,
                    ("@fin", RhSql.P(effet)), ("@id", courante.Id));
                // Toutes les autres affectations restées ouvertes (données anciennes) sont closes aussi.
                await session.ExecuteAsync("UPDATE rh_affectations SET fin = @fin WHERE agent_id = @agent AND fin IS NULL", ct,
                    ("@fin", RhSql.P(effet)), ("@agent", agent.Id));
            }

            await session.ExecuteAsync(
                """
                INSERT INTO rh_affectations (agent_id, service_id, unite_id, fonction_id, responsable_id, debut, fin, motif, observations, auteur_id, auteur, cree_le)
                VALUES (@agent, @service, @unite, @fonction, @responsable, @debut, NULL, @motif, @obs, @auteur_id, @auteur, @maintenant)
                """, ct,
                ("@agent", agent.Id), ("@service", service.Id), ("@unite", unite.Id), ("@fonction", fonction?.Id),
                ("@responsable", input.ResponsableId), ("@debut", RhSql.P(effet)), ("@motif", motif),
                ("@obs", SocleRegles.Tronquer(input.Observations, 2000)), ("@auteur_id", auteur.Id), ("@auteur", auteur.Nom),
                ("@maintenant", DateTime.UtcNow));
            await session.ExecuteAsync(
                "UPDATE rh_agents SET service_id = @service, unite_id = @unite, fonction_id = @fonction WHERE id = @id", ct,
                ("@service", service.Id), ("@unite", unite.Id), ("@fonction", fonction?.Id), ("@id", agent.Id));

            var ancienne = string.Join(" / ", new[] { agent.Service, agent.Unite, agent.Fonction }.Where(s => !string.IsNullOrWhiteSpace(s)));
            var nouvelle = string.Join(" / ", new[] { service.Libelle, unite.Libelle, fonction?.Libelle }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (responsable is not null)
                nouvelle += $", responsable {responsable}";
            await Historique.EcrireAsync(session, auteur, ObjetsHistorique.Agent, agent.Reference, "Affectation", "affectation",
                ancienne.Length == 0 ? null : ancienne, $"{nouvelle} (effet {RhRegles.Date(effet)})", motif, ct);
        }, auteur);

    /// <summary>
    /// Statut administratif (4.11). Suspendre, désactiver ou archiver : motif obligatoire, jamais son propre compte,
    /// jamais le dernier administrateur actif. Les interventions et actes de l'agent sont conservés.
    /// </summary>
    public async Task<string> ChangerStatutAsync(Auteur auteur, long id, RhStatutInput input, CancellationToken ct)
    {
        var statut = AgentStatuts.All.FirstOrDefault(s => string.Equals(s, input.Statut?.Trim(), StringComparison.OrdinalIgnoreCase))
                     ?? throw ApiException.Saisie($"Statut inconnu. Statuts possibles : {string.Join(", ", AgentStatuts.All)}.");
        var discord = await ModifierAsync(id, ct, async (session, agent) =>
        {
            if (agent.Statut == statut)
                throw ApiException.Saisie("L'agent a déjà ce statut.");
            var complet = RhRegles.ProfilComplet(agent.Rio, agent.GradeId, agent.ServiceId, agent.UniteId);
            if (statut == AgentStatuts.Actif && !complet)
                throw ApiException.Saisie("Profil incomplet : renseigne le RIO, le grade, le service et l'unité avant d'activer l'agent (ou choisis « Profil à compléter »).");
            if (statut == AgentStatuts.ProfilACompleter && complet)
                throw ApiException.Saisie("Le profil est complet : choisis « Actif ».");
            var motif = RhRegles.MotifObligatoirePourStatut(statut)
                ? RhSql.MotifObligatoire(input.Motif, "Le motif est obligatoire pour suspendre, désactiver ou archiver un agent.")
                : SocleRegles.Tronquer(input.Motif, 500);

            if (AgentStatuts.PeutSeConnecter(agent.Statut) && !AgentStatuts.PeutSeConnecter(statut))
            {
                if (auteur.Id == agent.Id)
                    throw ApiException.Conflit("Tu ne peux pas suspendre, désactiver ou archiver ton propre compte.");
                var profils = (await PermissionService.CalculerAsync(session, agent.Id, ct)).Profils;
                if (profils.Contains(DefaultProfiles.AdministrateurTechnique) && await CompterAdministrateursActifsAsync(session, ct) <= 1)
                    throw ApiException.Conflit("Impossible de bloquer le dernier administrateur actif.");
            }

            await session.ExecuteAsync("UPDATE rh_agents SET statut = @statut WHERE id = @id", ct, ("@statut", statut), ("@id", agent.Id));
            await Historique.EcrireAsync(session, auteur, ObjetsHistorique.Agent, agent.Reference, "Changement de statut",
                "statut", agent.Statut, statut, motif, ct);
        }, auteur, completerStatut: false);
        droits.Invalider();
        return discord;
    }

    private static async Task<int> CompterAdministrateursActifsAsync(SqlSession session, CancellationToken ct)
    {
        var ids = await session.QueryAsync(
            """
            SELECT a.id
            FROM auth_agent_profils ap
            JOIN auth_profils p ON p.id = ap.profil_id
            JOIN rh_agents a ON a.id = ap.agent_id
            WHERE p.code = @admin AND a.statut IN (@actif, @acompleter)
            FOR UPDATE
            """, r => r.Long("id"), ct,
            ("@admin", DefaultProfiles.AdministrateurTechnique), ("@actif", AgentStatuts.Actif),
            ("@acompleter", AgentStatuts.ProfilACompleter));
        return ids.Distinct().Count();
    }

    /// <summary>
    /// Cadre commun : transaction, agent verrouillé, modification, passage automatique à « Actif » si le profil
    /// devient complet, date de mise à jour. Retourne l'ID Discord de l'agent.
    /// </summary>
    private async Task<string> ModifierAsync(long id, CancellationToken ct, Func<SqlSession, AgentVerrou, Task> modification,
        Auteur auteur, bool completerStatut = true)
    {
        await using var session = await db.OpenAsync(ct);
        await session.BeginAsync(ct);
        var agent = await LireVerrouAsync(session, id, ct) ?? throw ApiException.Introuvable("Agent introuvable.");

        await modification(session, agent);

        var apres = await LireVerrouAsync(session, id, ct) ?? throw ApiException.Introuvable("Agent introuvable.");
        if (completerStatut)
        {
            var statut = RhRegles.StatutApresMiseAJour(apres.Statut,
                RhRegles.ProfilComplet(apres.Rio, apres.GradeId, apres.ServiceId, apres.UniteId));
            if (statut != apres.Statut)
            {
                await session.ExecuteAsync("UPDATE rh_agents SET statut = @statut WHERE id = @id", ct, ("@statut", statut), ("@id", id));
                await Historique.EcrireAsync(session, auteur, ObjetsHistorique.Agent, apres.Reference, "Changement de statut",
                    "statut", apres.Statut, statut, "Profil complété par le service RH (RIO, grade, service et unité renseignés)", ct);
            }
        }
        await session.ExecuteAsync("UPDATE rh_agents SET maj_le = @maintenant WHERE id = @id", ct,
            ("@maintenant", DateTime.UtcNow), ("@id", id));
        await session.CommitAsync(ct);
        return agent.DiscordId;
    }

    private static async Task<AgentVerrou?> LireVerrouAsync(SqlSession session, long id, CancellationToken ct)
    {
        var brut = await session.FirstOrDefaultAsync(
            "SELECT id, reference, discord_id, nom, prenom, rio, matricule, statut, grade_id, service_id, unite_id, fonction_id FROM rh_agents WHERE id = @id FOR UPDATE",
            r => new AgentVerrou(r.Long("id"), r.Text("reference"), r.Text("discord_id"), r.Text("nom"), r.Text("prenom"),
                r.NullableText("rio"), r.NullableText("matricule"), r.Text("statut"), r.NullableLong("grade_id"),
                r.NullableLong("service_id"), r.NullableLong("unite_id"), r.NullableLong("fonction_id"), null, null, null), ct, ("@id", id));
        if (brut is null)
            return null;
        var libelles = await session.FirstOrDefaultAsync(
            """
            SELECT (SELECT libelle FROM rh_services WHERE id = @s) AS service,
                   (SELECT libelle FROM rh_unites WHERE id = @u) AS unite,
                   (SELECT libelle FROM rh_fonctions WHERE id = @f) AS fonction
            """,
            r => new Libelles(r.NullableText("service"), r.NullableText("unite"), r.NullableText("fonction")), ct,
            ("@s", brut.ServiceId), ("@u", brut.UniteId), ("@f", brut.FonctionId));
        return brut with { Service = libelles?.Service, Unite = libelles?.Unite, Fonction = libelles?.Fonction };
    }

    private static async Task ExecuterUniqueAsync(Func<Task<int>> commande, string messageDoublon)
    {
        try
        {
            await commande();
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            throw ApiException.Conflit(messageDoublon);
        }
    }

    // =====================================================================================
    // Référentiels (grades, services, unités, fonctions)
    // =====================================================================================

    private static string TableDe(string type) => type switch
    {
        RhReferentielTypes.Grades => "rh_grades",
        RhReferentielTypes.Services => "rh_services",
        RhReferentielTypes.Unites => "rh_unites",
        RhReferentielTypes.Fonctions => "rh_fonctions",
        _ => throw ApiException.Introuvable("Référentiel inconnu.")
    };

    private static string ColonneAgent(string type) => type switch
    {
        RhReferentielTypes.Grades => "grade_id",
        RhReferentielTypes.Services => "service_id",
        RhReferentielTypes.Unites => "unite_id",
        _ => "fonction_id"
    };

    /// <summary>Sélection d'un référentiel (colonnes communes ; categorie, service_id, responsable_id selon le type).</summary>
    private static string SelectReferentiel(string type)
    {
        var table = TableDe(type);
        var categorie = type == RhReferentielTypes.Grades ? "t.categorie" : "NULL";
        var parent = type == RhReferentielTypes.Unites ? "t.service_id" : "NULL";
        var parentLibelle = type == RhReferentielTypes.Unites ? "(SELECT s.libelle FROM rh_services s WHERE s.id = t.service_id)" : "NULL";
        var responsable = type == RhReferentielTypes.Services ? "t.responsable_id" : "NULL";
        var responsableLibelle = type == RhReferentielTypes.Services
            ? "(SELECT CONCAT(r.nom, ' ', r.prenom) FROM rh_agents r WHERE r.id = t.responsable_id)"
            : "NULL";
        return $"""
            SELECT t.id, t.code, t.libelle, t.abrege, t.ordre, t.actif,
                   {categorie} AS categorie, {parent} AS parent_id, {parentLibelle} AS parent,
                   {responsable} AS responsable_id, {responsableLibelle} AS responsable,
                   (SELECT COUNT(*) FROM rh_agents a WHERE a.{ColonneAgent(type)} = t.id AND a.statut <> @archive) AS effectif
            FROM {table} t
            """;
    }

    private static RhReferentielDto LireReferentiel(string type, SqlRow r) => new(
        r.Long("id"), type, r.Text("code"), r.Text("libelle"), r.NullableText("abrege"), r.NullableText("categorie"),
        r.NullableLong("parent_id"), r.NullableText("parent"), r.NullableLong("responsable_id"), r.NullableText("responsable"),
        r.Int("ordre"), r.Bool("actif"), r.Int("effectif"));

    public async Task<List<RhReferentielDto>> ListerReferentielAsync(string type, bool inclureInactifs, CancellationToken ct)
    {
        await using var session = await db.OpenAsync(ct);
        return await ListerReferentielAsync(session, type, inclureInactifs, null, ct);
    }

    private static Task<List<RhReferentielDto>> ListerReferentielAsync(SqlSession session, string type, bool inclureInactifs, long? id, CancellationToken ct)
    {
        var filtre = " WHERE 1 = 1" + (inclureInactifs ? string.Empty : " AND t.actif = 1") + (id is null ? string.Empty : " AND t.id = @id");
        return session.QueryAsync(SelectReferentiel(type) + filtre + " ORDER BY t.ordre, t.libelle",
            r => LireReferentiel(type, r), ct, ("@archive", AgentStatuts.Archive), ("@id", id));
    }

    /// <summary>Référentiel public (toute session d'agent) : éléments actifs, ou tous avec <paramref name="tous"/>.</summary>
    public async Task<List<ReferentielDto>> ReferentielPublicAsync(string type, bool tous, CancellationToken ct)
        => (await ListerReferentielAsync(type, tous, ct))
            .Select(e => new ReferentielDto(e.Id, e.Code, e.Libelle, e.Abrege, e.ParentId, e.Ordre, e.Actif))
            .ToList();

    /// <summary>Crée un élément ; code stable tiré du libellé (suffixe si déjà pris).</summary>
    public async Task<RhReferentielDto> CreerReferentielAsync(Auteur auteur, string type, RhReferentielInput input, CancellationToken ct)
    {
        var table = TableDe(type);
        await using var session = await db.OpenAsync(ct);
        await session.BeginAsync(ct);
        var valeurs = await ValiderReferentielAsync(session, type, input, null, ct);

        var racine = RhRegles.CodeReferentiel(valeurs.Libelle);
        var code = racine;
        for (var n = 2; await session.LongAsync($"SELECT COUNT(*) FROM {table} WHERE code = @code", ct, ("@code", code)) > 0; n++)
        {
            if (n > 1000)
                throw ApiException.Conflit("Impossible de générer un code unique : change le libellé.");
            code = SocleRegles.CodeAvecSuffixe(racine, n).ToUpperInvariant();
        }

        var maintenant = DateTime.UtcNow;
        var specifiques = ColonnesSpecifiques(type, valeurs);
        var parametresSql = new List<(string Name, object? Value)>
        {
            ("@code", code), ("@libelle", valeurs.Libelle), ("@abrege", valeurs.Abrege), ("@ordre", valeurs.Ordre),
            ("@actif", valeurs.Actif), ("@maintenant", maintenant)
        };
        parametresSql.AddRange(specifiques.Select(c => (c.Parametre, c.Valeur)));
        var id = await session.InsertAsync(
            $"INSERT INTO {table} (code, libelle, abrege, ordre, actif, cree_le, maj_le{string.Concat(specifiques.Select(c => ", " + c.Colonne))}) " +
            $"VALUES (@code, @libelle, @abrege, @ordre, @actif, @maintenant, @maintenant{string.Concat(specifiques.Select(c => ", " + c.Parametre))})",
            ct, parametresSql.ToArray());
        await Historique.EcrireAsync(session, auteur, ObjetsRh.DepuisType(type), code, "Création", null, null,
            DescriptionReferentiel(valeurs), null, ct);
        var dto = (await ListerReferentielAsync(session, type, true, id, ct)).Single();
        await session.CommitAsync(ct);
        return dto;
    }

    /// <summary>Modifie un élément (le code ne change jamais). Retourne l'élément et les agents concernés si le libellé change.</summary>
    public async Task<(RhReferentielDto Element, List<string> DiscordIds)> ModifierReferentielAsync(
        Auteur auteur, string type, long id, RhReferentielInput input, CancellationToken ct)
    {
        var table = TableDe(type);
        await using var session = await db.OpenAsync(ct);
        await session.BeginAsync(ct);
        var actuel = (await session.QueryAsync(SelectReferentiel(type) + " WHERE t.id = @id FOR UPDATE",
                         r => LireReferentiel(type, r), ct, ("@archive", AgentStatuts.Archive), ("@id", id))).SingleOrDefault()
                     ?? throw ApiException.Introuvable("Élément introuvable.");
        var valeurs = await ValiderReferentielAsync(session, type, input, id, ct);
        var objet = ObjetsRh.DepuisType(type);

        var changements = new List<(string Champ, string? Ancienne, string? Nouvelle)>();
        void Noter(string champ, string? ancienne, string? nouvelle)
        {
            if (!string.Equals(ancienne ?? string.Empty, nouvelle ?? string.Empty, StringComparison.Ordinal))
                changements.Add((champ, ancienne, nouvelle));
        }
        Noter("libelle", actuel.Libelle, valeurs.Libelle);
        Noter("abrege", actuel.Abrege, valeurs.Abrege);
        Noter("ordre", actuel.Ordre.ToString(), valeurs.Ordre.ToString());
        Noter("actif", actuel.Actif ? "Actif" : "Inactif", valeurs.Actif ? "Actif" : "Inactif");
        if (type == RhReferentielTypes.Grades)
            Noter("categorie", actuel.Categorie, valeurs.Categorie);
        if (type == RhReferentielTypes.Unites)
            Noter("service", actuel.Parent, valeurs.ParentLibelle);
        if (type == RhReferentielTypes.Services)
            Noter("responsable", actuel.Responsable, valeurs.ResponsableLibelle);
        if (changements.Count == 0)
            return (actuel, new List<string>());

        var specifiques = ColonnesSpecifiques(type, valeurs);
        var parametresSql = new List<(string Name, object? Value)>
        {
            ("@libelle", valeurs.Libelle), ("@abrege", valeurs.Abrege), ("@ordre", valeurs.Ordre), ("@actif", valeurs.Actif),
            ("@maintenant", DateTime.UtcNow), ("@id", id)
        };
        parametresSql.AddRange(specifiques.Select(c => (c.Parametre, c.Valeur)));
        var affectations = string.Concat(specifiques.Select(c => $", {c.Colonne} = {c.Parametre}"));
        await session.ExecuteAsync(
            $"UPDATE {table} SET libelle = @libelle, abrege = @abrege, ordre = @ordre, actif = @actif, maj_le = @maintenant{affectations} WHERE id = @id",
            ct, parametresSql.ToArray());
        foreach (var (champ, ancienne, nouvelle) in changements)
            await Historique.EcrireAsync(session, auteur, objet, actuel.Code, "Modification", champ, ancienne, nouvelle, null, ct);

        var discordIds = changements.Any(c => c.Champ == "libelle")
            ? await session.QueryAsync($"SELECT discord_id FROM rh_agents WHERE {ColonneAgent(type)} = @id", r => r.Text("discord_id"), ct, ("@id", id))
            : new List<string>();
        var dto = (await ListerReferentielAsync(session, type, true, id, ct)).Single();
        await session.CommitAsync(ct);
        return (dto, discordIds);
    }

    private static async Task<ValeursReferentiel> ValiderReferentielAsync(SqlSession session, string type, RhReferentielInput input,
        long? id, CancellationToken ct)
    {
        var table = TableDe(type);
        var libelle = (input.Libelle ?? string.Empty).Trim();
        if (libelle.Length is < 2 or > 100)
            throw ApiException.Saisie("Le libellé doit faire entre 2 et 100 caractères.");
        var abrege = SocleRegles.Tronquer(input.Abrege, 20);
        if (input.Abrege?.Trim().Length > 20)
            throw ApiException.Saisie("L'abrégé fait 20 caractères au plus.");
        if (input.Ordre is < 0 or > 99999)
            throw ApiException.Saisie("L'ordre doit être compris entre 0 et 99999.");

        string? categorie = null, parentLibelle = null, responsableLibelle = null;
        long? parentId = null, responsableId = null;
        if (type == RhReferentielTypes.Grades)
        {
            categorie = SocleRegles.Tronquer(input.Categorie, 100);
        }
        else if (type == RhReferentielTypes.Unites)
        {
            parentId = input.ParentId ?? throw ApiException.Saisie("Le service de l'unité est obligatoire.");
            parentLibelle = await session.FirstOrDefaultAsync("SELECT libelle FROM rh_services WHERE id = @id",
                r => r.Text("libelle"), ct, ("@id", parentId)) ?? throw ApiException.Saisie("Service introuvable.");
        }
        else if (type == RhReferentielTypes.Services && input.ResponsableId is { } rid)
        {
            responsableId = rid;
            responsableLibelle = await session.FirstOrDefaultAsync("SELECT CONCAT(nom, ' ', prenom) AS l FROM rh_agents WHERE id = @id",
                r => r.Text("l"), ct, ("@id", rid)) ?? throw ApiException.Saisie("Responsable introuvable.");
        }

        var doublon = type == RhReferentielTypes.Unites
            ? await session.LongAsync($"SELECT COUNT(*) FROM {table} WHERE libelle = @l AND service_id = @p AND (@id IS NULL OR id <> @id)", ct,
                ("@l", libelle), ("@p", parentId), ("@id", id))
            : await session.LongAsync($"SELECT COUNT(*) FROM {table} WHERE libelle = @l AND (@id IS NULL OR id <> @id)", ct,
                ("@l", libelle), ("@id", id));
        if (doublon > 0)
            throw ApiException.Conflit($"Un(e) {RhReferentielTypes.LibelleSingulier(type)} porte déjà ce libellé.");

        return new ValeursReferentiel(libelle, abrege, categorie, parentId, parentLibelle, responsableId, responsableLibelle, input.Ordre, input.Actif);
    }

    /// <summary>Colonnes propres à un type de référentiel (noms de colonnes constants, valeurs en paramètres).</summary>
    private static List<(string Colonne, string Parametre, object? Valeur)> ColonnesSpecifiques(string type, ValeursReferentiel v)
    {
        var liste = new List<(string Colonne, string Parametre, object? Valeur)>();
        if (type == RhReferentielTypes.Grades)
            liste.Add(("categorie", "@categorie", v.Categorie));
        else if (type == RhReferentielTypes.Unites)
            liste.Add(("service_id", "@parent", v.ParentId));
        else if (type == RhReferentielTypes.Services)
            liste.Add(("responsable_id", "@responsable", v.ResponsableId));
        return liste;
    }

    private static string DescriptionReferentiel(ValeursReferentiel v)
    {
        var texte = v.Libelle + (v.Abrege is null ? string.Empty : $" ({v.Abrege})");
        if (v.Categorie is not null) texte += $", {v.Categorie}";
        if (v.ParentLibelle is not null) texte += $", service {v.ParentLibelle}";
        if (v.ResponsableLibelle is not null) texte += $", responsable {v.ResponsableLibelle}";
        return texte + (v.Actif ? string.Empty : ", inactif");
    }

    private static Task<Element?> LireElementAsync(SqlSession session, string type, long id, CancellationToken ct)
    {
        var parent = type == RhReferentielTypes.Unites ? "service_id" : "NULL";
        return session.FirstOrDefaultAsync($"SELECT id, libelle, actif, {parent} AS parent_id FROM {TableDe(type)} WHERE id = @id",
            r => new Element(r.Long("id"), r.Text("libelle"), r.Bool("actif"), r.NullableLong("parent_id")), ct, ("@id", id));
    }

    // =====================================================================================

    private sealed record LigneDossier(RhAgentLigneDto Agent, DateOnly? DateIntegration, DateOnly? GradeEffet, DateTimeOffset? DerniereConnexion);

    private sealed record AgentVerrou(long Id, string Reference, string DiscordId, string Nom, string Prenom, string? Rio, string? Matricule,
        string Statut, long? GradeId, long? ServiceId, long? UniteId, long? FonctionId, string? Service, string? Unite, string? Fonction);

    private sealed record Libelles(string? Service, string? Unite, string? Fonction);

    private sealed record Element(long Id, string Libelle, bool Actif, long? ParentId);

    private sealed record Courante(long Id, DateOnly Debut, long? ServiceId, long? UniteId, long? FonctionId, long? ResponsableId);

    private sealed record ValeursReferentiel(string Libelle, string? Abrege, string? Categorie, long? ParentId, string? ParentLibelle,
        long? ResponsableId, string? ResponsableLibelle, int Ordre, bool Actif);

    private sealed record Boite<T>(T Valeur);
}
