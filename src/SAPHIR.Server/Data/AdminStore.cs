using System.Text;
using Saphir.Server.Services;
using Saphir.Shared.Contracts;

namespace Saphir.Server.Data;

/// <summary>
/// Administration du socle : comptes (profils, statut), profils et permissions, paramètres centraux.
/// Chaque modification est historisée (auteur, ancienne et nouvelle valeur, motif) dans sa transaction.
/// </summary>
public sealed class AdminStore(SaphirDb db, PermissionService droits, ParametresService parametres)
{
    private const int LimiteComptes = 1000;

    // =====================================================================================
    // Comptes
    // =====================================================================================

    /// <summary>Liste des comptes ; <paramref name="q"/> cherche dans nom, prénom, référence, RIO, matricule et ID Discord.</summary>
    public async Task<List<CompteDto>> ListerComptesAsync(string? q, string? statut, CancellationToken ct)
    {
        var sql = new StringBuilder($"SELECT {AgentStore.Colonnes} FROM rh_agents a WHERE 1 = 1");
        var parametresSql = new List<(string Name, object? Value)>();
        if (!string.IsNullOrWhiteSpace(q))
        {
            sql.Append("""
                 AND (a.nom LIKE @q OR a.prenom LIKE @q OR a.reference LIKE @q OR a.rio LIKE @q
                      OR a.matricule LIKE @q OR a.discord_id LIKE @q
                      OR CONCAT(a.nom, ' ', a.prenom) LIKE @q OR CONCAT(a.prenom, ' ', a.nom) LIKE @q)
                """);
            parametresSql.Add(("@q", "%" + SocleRegles.EchapperLike(q.Trim()) + "%"));
        }
        if (!string.IsNullOrWhiteSpace(statut))
        {
            sql.Append(" AND a.statut = @statut");
            parametresSql.Add(("@statut", statut.Trim()));
        }
        sql.Append(" ORDER BY a.nom, a.prenom, a.id LIMIT @limite");
        parametresSql.Add(("@limite", LimiteComptes));

        await using var session = await db.OpenAsync(ct);
        var lignes = await session.QueryAsync(sql.ToString(),
            r => new LigneCompte(AgentStore.Lire(r), r.NullableDate("derniere_connexion")), ct, parametresSql.ToArray());

        var attributions = await session.QueryAsync(
            """
            SELECT ap.agent_id, p.code
            FROM auth_agent_profils ap
            JOIN auth_profils p ON p.id = ap.profil_id
            ORDER BY p.code
            """, r => new Attribution(r.Long("agent_id"), r.Text("code")), ct);
        var profilsParAgent = attributions
            .GroupBy(a => a.AgentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(a => a.Code).ToList());

        return lignes
            .Select(l => new CompteDto(
                l.Agent,
                profilsParAgent.TryGetValue(l.Agent.Id, out var profils) ? profils : Array.Empty<string>(),
                l.DerniereConnexion))
            .ToList();
    }

    /// <summary>
    /// Remplace les profils d'un agent. Retourne l'ID Discord à prévenir (SessionChanged), ou <c>null</c> si rien n'a changé.
    /// </summary>
    public async Task<string?> DefinirProfilsAsync(Auteur auteur, long agentId, ProfilsAgentInput input, CancellationToken ct)
    {
        var demandes = (input.Profils ?? Array.Empty<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        await using var session = await db.OpenAsync(ct);
        await session.BeginAsync(ct);

        var cible = await AgentStore.TrouverAsync(session, agentId, verrouiller: true, ct)
                    ?? throw ApiException.Introuvable("Agent introuvable.");

        var profils = await session.QueryAsync("SELECT id, code FROM auth_profils",
            r => new ProfilCourt(r.Long("id"), r.Text("code")), ct);
        var idParCode = profils.ToDictionary(p => p.Code, p => p.Id, StringComparer.Ordinal);
        var inconnu = demandes.FirstOrDefault(c => !idParCode.ContainsKey(c));
        if (inconnu is not null)
            throw ApiException.Saisie($"Profil inconnu : {inconnu}.");

        var anciens = (await PermissionService.CalculerAsync(session, agentId, ct)).Profils
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();
        if (anciens.SequenceEqual(demandes, StringComparer.Ordinal))
            return null;

        var retireAdmin = anciens.Contains(DefaultProfiles.AdministrateurTechnique) &&
                          !demandes.Contains(DefaultProfiles.AdministrateurTechnique);
        if (retireAdmin && AgentStatuts.PeutSeConnecter(cible.Statut) && await CompterAdministrateursActifsAsync(session, ct) <= 1)
            throw ApiException.Conflit("Impossible de retirer le profil administrateur au dernier administrateur actif.");

        var maintenant = DateTime.UtcNow;
        await session.ExecuteAsync("DELETE FROM auth_agent_profils WHERE agent_id = @agent", ct, ("@agent", agentId));
        foreach (var code in demandes)
            await session.ExecuteAsync(
                "INSERT INTO auth_agent_profils (agent_id, profil_id, attribue_le) VALUES (@agent, @profil, @maintenant)", ct,
                ("@agent", agentId), ("@profil", idParCode[code]), ("@maintenant", maintenant));

        await Historique.EcrireAsync(session, auteur, ObjetsHistorique.Agent, cible.Reference, "Attribution des profils",
            "profils", SocleRegles.Liste(anciens), SocleRegles.Liste(demandes), input.Motif, ct);
        await session.CommitAsync(ct);

        droits.Invalider();
        return cible.DiscordId;
    }

    /// <summary>
    /// Change le statut d'un agent. Un administrateur ne peut pas bloquer son propre compte, ni le dernier administrateur actif.
    /// Retourne l'ID Discord à prévenir, ou <c>null</c> si rien n'a changé.
    /// </summary>
    public async Task<string?> DefinirStatutAsync(Auteur auteur, long agentId, StatutAgentInput input, CancellationToken ct)
    {
        var statut = AgentStatuts.All.FirstOrDefault(s => string.Equals(s, input.Statut?.Trim(), StringComparison.OrdinalIgnoreCase))
                     ?? throw ApiException.Saisie($"Statut inconnu. Statuts possibles : {string.Join(", ", AgentStatuts.All)}.");

        await using var session = await db.OpenAsync(ct);
        await session.BeginAsync(ct);

        var cible = await AgentStore.TrouverAsync(session, agentId, verrouiller: true, ct)
                    ?? throw ApiException.Introuvable("Agent introuvable.");
        if (cible.Statut == statut)
            return null;

        if (AgentStatuts.PeutSeConnecter(cible.Statut) && !AgentStatuts.PeutSeConnecter(statut))
        {
            if (auteur.Id == cible.Id)
                throw ApiException.Conflit("Tu ne peux pas suspendre, désactiver ou archiver ton propre compte.");

            var profilsCible = (await PermissionService.CalculerAsync(session, agentId, ct)).Profils;
            if (profilsCible.Contains(DefaultProfiles.AdministrateurTechnique) &&
                await CompterAdministrateursActifsAsync(session, ct) <= 1)
                throw ApiException.Conflit("Impossible de bloquer le dernier administrateur actif.");
        }

        await session.ExecuteAsync("UPDATE rh_agents SET statut = @statut, maj_le = @maintenant WHERE id = @id", ct,
            ("@statut", statut), ("@maintenant", DateTime.UtcNow), ("@id", agentId));
        await Historique.EcrireAsync(session, auteur, ObjetsHistorique.Agent, cible.Reference, "Changement de statut",
            "statut", cible.Statut, statut, input.Motif, ct);
        await session.CommitAsync(ct);

        droits.Invalider();
        return cible.DiscordId;
    }

    /// <summary>Administrateurs dont le compte peut encore se connecter (lignes verrouillées jusqu'à la fin de la transaction).</summary>
    private static async Task<int> CompterAdministrateursActifsAsync(SqlSession session, CancellationToken ct)
    {
        var administrateurs = await session.QueryAsync(
            """
            SELECT a.id
            FROM auth_agent_profils ap
            JOIN auth_profils p ON p.id = ap.profil_id
            JOIN rh_agents a ON a.id = ap.agent_id
            WHERE p.code = @admin AND a.statut IN (@actif, @acompleter)
            FOR UPDATE
            """, r => r.Long("id"), ct,
            ("@admin", DefaultProfiles.AdministrateurTechnique),
            ("@actif", AgentStatuts.Actif),
            ("@acompleter", AgentStatuts.ProfilACompleter));
        return administrateurs.Distinct().Count();
    }

    // =====================================================================================
    // Profils
    // =====================================================================================

    public async Task<List<ProfilDto>> ListerProfilsAsync(CancellationToken ct)
    {
        await using var session = await db.OpenAsync(ct);
        return await ListerProfilsAsync(session, null, ct);
    }

    public async Task<ProfilDto> CreerProfilAsync(Auteur auteur, ProfilInput input, CancellationToken ct)
    {
        var (nom, description, permissions) = ValiderProfil(input);
        var maintenant = DateTime.UtcNow;

        await using var session = await db.OpenAsync(ct);
        await session.BeginAsync(ct);

        var racine = SocleRegles.CodeDepuisNom(nom);
        var code = racine;
        for (var numero = 2; await session.LongAsync("SELECT COUNT(*) FROM auth_profils WHERE code = @code", ct, ("@code", code)) > 0; numero++)
        {
            if (numero > 1000)
                throw ApiException.Conflit("Impossible de générer un code unique pour ce profil : change son nom.");
            code = SocleRegles.CodeAvecSuffixe(racine, numero);
        }

        var id = await session.InsertAsync(
            """
            INSERT INTO auth_profils (code, nom, description, systeme, cree_le, maj_le)
            VALUES (@code, @nom, @description, 0, @maintenant, @maintenant)
            """, ct,
            ("@code", code), ("@nom", nom), ("@description", description), ("@maintenant", maintenant));
        await RemplacerPermissionsAsync(session, id, permissions, ct);

        await Historique.EcrireAsync(session, auteur, ObjetsHistorique.Profil, code, "Création", null, null,
            $"{nom} — permissions : {SocleRegles.Liste(permissions)}", null, ct);
        var dto = (await ListerProfilsAsync(session, id, ct)).Single();
        await session.CommitAsync(ct);
        return dto;
    }

    /// <summary>
    /// Modifie un profil (nom, description, permissions). Le profil administrateur garde toujours toutes les permissions.
    /// Retourne le profil et les ID Discord des agents qui le portent (à prévenir).
    /// </summary>
    public async Task<(ProfilDto Profil, List<string> DiscordIds)> ModifierProfilAsync(Auteur auteur, long id, ProfilInput input, CancellationToken ct)
    {
        var (nom, description, permissions) = ValiderProfil(input);

        await using var session = await db.OpenAsync(ct);
        await session.BeginAsync(ct);

        var actuel = (await ListerProfilsAsync(session, id, ct, verrouiller: true)).SingleOrDefault()
                     ?? throw ApiException.Introuvable("Profil introuvable.");
        if (actuel.Code == DefaultProfiles.AdministrateurTechnique)
            permissions = Permissions.All.Select(p => p.Code).OrderBy(p => p, StringComparer.Ordinal).ToList();

        var anciennes = actuel.Permissions.OrderBy(p => p, StringComparer.Ordinal).ToList();
        var change = false;
        if (actuel.Nom != nom)
        {
            await Historique.EcrireAsync(session, auteur, ObjetsHistorique.Profil, actuel.Code, "Modification", "nom",
                actuel.Nom, nom, null, ct);
            change = true;
        }
        if (actuel.Description != description)
        {
            await Historique.EcrireAsync(session, auteur, ObjetsHistorique.Profil, actuel.Code, "Modification", "description",
                actuel.Description, description, null, ct);
            change = true;
        }
        var permissionsChangees = !anciennes.SequenceEqual(permissions, StringComparer.Ordinal);
        if (permissionsChangees)
        {
            await RemplacerPermissionsAsync(session, id, permissions, ct);
            await Historique.EcrireAsync(session, auteur, ObjetsHistorique.Profil, actuel.Code, "Modification", "permissions",
                SocleRegles.Liste(anciennes), SocleRegles.Liste(permissions), null, ct);
            change = true;
        }

        if (!change)
            return (actuel, new List<string>());

        await session.ExecuteAsync("UPDATE auth_profils SET nom = @nom, description = @description, maj_le = @maintenant WHERE id = @id", ct,
            ("@nom", nom), ("@description", description), ("@maintenant", DateTime.UtcNow), ("@id", id));

        var dto = (await ListerProfilsAsync(session, id, ct)).Single();
        var discordIds = permissionsChangees
            ? await session.QueryAsync(
                """
                SELECT a.discord_id
                FROM auth_agent_profils ap
                JOIN rh_agents a ON a.id = ap.agent_id
                WHERE ap.profil_id = @profil
                """, r => r.Text("discord_id"), ct, ("@profil", id))
            : new List<string>();
        await session.CommitAsync(ct);

        droits.Invalider();
        return (dto, discordIds);
    }

    /// <summary>Supprime un profil créé par un administrateur et attribué à personne (409 sinon).</summary>
    public async Task SupprimerProfilAsync(Auteur auteur, long id, CancellationToken ct)
    {
        await using var session = await db.OpenAsync(ct);
        await session.BeginAsync(ct);

        var actuel = (await ListerProfilsAsync(session, id, ct, verrouiller: true)).SingleOrDefault()
                     ?? throw ApiException.Introuvable("Profil introuvable.");
        if (actuel.Systeme)
            throw ApiException.Conflit("Ce profil est un profil système : il ne peut pas être supprimé.");
        if (actuel.NombreAgents > 0)
            throw ApiException.Conflit($"Ce profil est encore attribué à {actuel.NombreAgents} agent(s) : retire-le d'abord.");

        await session.ExecuteAsync("DELETE FROM auth_profil_permissions WHERE profil_id = @id", ct, ("@id", id));
        await session.ExecuteAsync("DELETE FROM auth_profils WHERE id = @id", ct, ("@id", id));
        await Historique.EcrireAsync(session, auteur, ObjetsHistorique.Profil, actuel.Code, "Suppression", null,
            $"{actuel.Nom} — permissions : {SocleRegles.Liste(actuel.Permissions)}", null, null, ct);
        await session.CommitAsync(ct);

        droits.Invalider();
    }

    private static (string Nom, string Description, List<string> Permissions) ValiderProfil(ProfilInput input)
    {
        var nom = (input.Nom ?? string.Empty).Trim();
        if (nom.Length is < 2 or > 100)
            throw ApiException.Saisie("Le nom du profil doit faire entre 2 et 100 caractères.");
        var description = (input.Description ?? string.Empty).Trim();
        if (description.Length > 500)
            throw ApiException.Saisie("La description du profil est trop longue (500 caractères au plus).");

        var permissions = (input.Permissions ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        var inconnue = permissions.FirstOrDefault(p => !Permissions.Exists(p));
        if (inconnue is not null)
            throw ApiException.Saisie($"Permission inconnue : {inconnue}.");
        return (nom, description, permissions);
    }

    private static async Task RemplacerPermissionsAsync(SqlSession session, long profilId, IReadOnlyList<string> permissions, CancellationToken ct)
    {
        await session.ExecuteAsync("DELETE FROM auth_profil_permissions WHERE profil_id = @profil", ct, ("@profil", profilId));
        foreach (var permission in permissions)
            await session.ExecuteAsync(
                "INSERT INTO auth_profil_permissions (profil_id, permission) VALUES (@profil, @permission)", ct,
                ("@profil", profilId), ("@permission", permission));
    }

    /// <summary>Profils avec permissions et nombre d'agents ; <paramref name="id"/> = un seul profil.</summary>
    private static async Task<List<ProfilDto>> ListerProfilsAsync(SqlSession session, long? id, CancellationToken ct, bool verrouiller = false)
    {
        var filtre = id is null ? string.Empty : " WHERE p.id = @id";
        var verrou = verrouiller ? " FOR UPDATE" : string.Empty;
        var lignes = await session.QueryAsync(
            $"""
            SELECT p.id, p.code, p.nom, p.description, p.systeme,
                   (SELECT COUNT(*) FROM auth_agent_profils ap WHERE ap.profil_id = p.id) AS nombre_agents
            FROM auth_profils p{filtre}
            ORDER BY p.systeme DESC, p.nom{verrou}
            """,
            r => new LigneProfil(r.Long("id"), r.Text("code"), r.Text("nom"), r.Text("description"), r.Bool("systeme"),
                r.Int("nombre_agents")), ct, ("@id", id));

        var permissions = await session.QueryAsync(
            "SELECT profil_id, permission FROM auth_profil_permissions" + (id is null ? string.Empty : " WHERE profil_id = @id"),
            r => new PermissionProfil(r.Long("profil_id"), r.Text("permission")), ct, ("@id", id));
        var parProfil = permissions
            .GroupBy(p => p.ProfilId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(p => p.Permission).OrderBy(p => p, StringComparer.Ordinal).ToList());

        return lignes
            .Select(l => new ProfilDto(l.Id, l.Code, l.Nom, l.Description,
                parProfil.TryGetValue(l.Id, out var liste) ? liste : Array.Empty<string>(), l.Systeme, l.NombreAgents))
            .ToList();
    }

    // =====================================================================================
    // Paramètres
    // =====================================================================================

    public Task<List<ParametreDto>> ListerParametresAsync(CancellationToken ct) => parametres.ListerAsync(ct);

    /// <summary>Modifie un paramètre central. Retourne <c>true</c> si la valeur a changé (à diffuser : ParametresChanged).</summary>
    public async Task<bool> ModifierParametreAsync(Auteur auteur, string cle, ParametreInput input, CancellationToken ct)
    {
        var valeur = (input.Valeur ?? string.Empty).Trim();

        await using var session = await db.OpenAsync(ct);
        await session.BeginAsync(ct);

        var actuel = (await ParametresService.LireAsync(session, ct)).FirstOrDefault(p => p.Cle == cle)
                     ?? throw ApiException.Introuvable("Paramètre inconnu.");
        if (!actuel.Modifiable)
            throw ApiException.Conflit("Ce paramètre n'est pas modifiable.");

        var formatRio = await session.ScalarAsync("SELECT valeur FROM sys_parametres WHERE cle = @cle", ct,
            ("@cle", ParametresCles.RioFormat)) as string;
        var erreur = SocleRegles.ValiderParametre(cle, valeur, formatRio);
        if (erreur is not null)
            throw ApiException.Saisie(erreur);
        if (actuel.Valeur == valeur)
            return false;

        await session.ExecuteAsync("UPDATE sys_parametres SET valeur = @valeur, maj_le = @maintenant WHERE cle = @cle", ct,
            ("@valeur", valeur), ("@maintenant", DateTime.UtcNow), ("@cle", cle));
        await Historique.EcrireAsync(session, auteur, ObjetsHistorique.Parametre, cle, "Modification", cle,
            actuel.Valeur, valeur, null, ct);
        await session.CommitAsync(ct);

        parametres.Invalider();
        return true;
    }

    private sealed record LigneCompte(AgentDto Agent, DateTimeOffset? DerniereConnexion);
    private sealed record Attribution(long AgentId, string Code);
    private sealed record ProfilCourt(long Id, string Code);
    private sealed record LigneProfil(long Id, string Code, string Nom, string Description, bool Systeme, int NombreAgents);
    private sealed record PermissionProfil(long ProfilId, string Permission);
}
