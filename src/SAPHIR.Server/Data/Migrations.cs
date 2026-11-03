namespace Saphir.Server.Data;

/// <summary>Migration numérotée : liste de requêtes appliquée une seule fois, dans l'ordre des numéros.</summary>
public sealed record Migration(int Version, string Nom, IReadOnlyList<string> Requetes);

/// <summary>
/// Schéma de la base SAPHIR. Règles : on n'édite jamais une migration publiée, on en ajoute une nouvelle ;
/// jamais de DROP de données ; requêtes rejouables (IF NOT EXISTS) car MariaDB valide implicitement le DDL.
/// </summary>
public static class Migrations
{
    private const string Table = "ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

    public static readonly IReadOnlyList<Migration> All =
    [
        new(1, "Socle : agents, profils, paramètres, historique, journal des accès",
        [
            $"""
            CREATE TABLE IF NOT EXISTS rh_agents (
                id BIGINT NOT NULL AUTO_INCREMENT,
                reference VARCHAR(16) NULL,
                discord_id VARCHAR(32) NOT NULL,
                nom VARCHAR(60) NOT NULL,
                prenom VARCHAR(60) NOT NULL,
                rio VARCHAR(32) NULL,
                matricule VARCHAR(32) NULL,
                statut VARCHAR(32) NOT NULL,
                cree_le DATETIME NOT NULL,
                maj_le DATETIME NOT NULL,
                derniere_connexion DATETIME NULL,
                PRIMARY KEY (id),
                UNIQUE KEY ux_rh_agents_reference (reference),
                UNIQUE KEY ux_rh_agents_discord (discord_id),
                UNIQUE KEY ux_rh_agents_rio (rio),
                UNIQUE KEY ux_rh_agents_matricule (matricule),
                KEY ix_rh_agents_nom (nom, prenom),
                KEY ix_rh_agents_statut (statut)
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS auth_profils (
                id BIGINT NOT NULL AUTO_INCREMENT,
                code VARCHAR(64) NOT NULL,
                nom VARCHAR(100) NOT NULL,
                description VARCHAR(500) NOT NULL DEFAULT '',
                systeme TINYINT(1) NOT NULL DEFAULT 0,
                cree_le DATETIME NOT NULL,
                maj_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                UNIQUE KEY ux_auth_profils_code (code)
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS auth_profil_permissions (
                profil_id BIGINT NOT NULL,
                permission VARCHAR(100) NOT NULL,
                PRIMARY KEY (profil_id, permission),
                KEY ix_auth_profil_permissions_permission (permission),
                CONSTRAINT fk_auth_profil_permissions_profil FOREIGN KEY (profil_id)
                    REFERENCES auth_profils (id) ON DELETE CASCADE
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS auth_agent_profils (
                agent_id BIGINT NOT NULL,
                profil_id BIGINT NOT NULL,
                attribue_le DATETIME NOT NULL,
                PRIMARY KEY (agent_id, profil_id),
                KEY ix_auth_agent_profils_profil (profil_id),
                CONSTRAINT fk_auth_agent_profils_agent FOREIGN KEY (agent_id)
                    REFERENCES rh_agents (id) ON DELETE CASCADE,
                CONSTRAINT fk_auth_agent_profils_profil FOREIGN KEY (profil_id)
                    REFERENCES auth_profils (id) ON DELETE RESTRICT
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS sys_parametres (
                cle VARCHAR(100) NOT NULL,
                valeur TEXT NOT NULL,
                libelle VARCHAR(200) NOT NULL,
                aide VARCHAR(500) NULL,
                modifiable TINYINT(1) NOT NULL DEFAULT 1,
                maj_le DATETIME NOT NULL,
                PRIMARY KEY (cle)
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS sys_historique (
                id BIGINT NOT NULL AUTO_INCREMENT,
                `date` DATETIME NOT NULL,
                auteur_id BIGINT NULL,
                auteur VARCHAR(200) NOT NULL,
                objet VARCHAR(50) NOT NULL,
                reference VARCHAR(100) NOT NULL,
                `action` VARCHAR(50) NOT NULL,
                champ VARCHAR(100) NULL,
                ancienne TEXT NULL,
                nouvelle TEXT NULL,
                motif VARCHAR(500) NULL,
                PRIMARY KEY (id),
                KEY ix_sys_historique_objet (objet, reference, `date`),
                KEY ix_sys_historique_date (`date`),
                KEY ix_sys_historique_auteur (auteur_id)
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS sys_journal_acces (
                id BIGINT NOT NULL AUTO_INCREMENT,
                `date` DATETIME NOT NULL,
                agent_id BIGINT NULL,
                agent VARCHAR(200) NOT NULL,
                `action` VARCHAR(100) NOT NULL,
                cible VARCHAR(200) NOT NULL,
                resultat VARCHAR(100) NOT NULL,
                PRIMARY KEY (id),
                KEY ix_sys_journal_acces_date (`date`),
                KEY ix_sys_journal_acces_agent (agent_id, `date`)
            ) {Table}
            """
        ])
    ];

    private const string LockName = "saphir_migrations";

    /// <summary>
    /// Applique les migrations manquantes (verrou nommé pour éviter deux serveurs en parallèle).
    /// Retourne le nombre de migrations appliquées.
    /// </summary>
    public static async Task<int> AppliquerAsync(SqlSession db, ILogger logger, CancellationToken ct)
    {
        await db.ExecuteAsync($"""
            CREATE TABLE IF NOT EXISTS sys_migrations (
                version INT NOT NULL,
                nom VARCHAR(200) NOT NULL,
                applique_le DATETIME NOT NULL,
                PRIMARY KEY (version)
            ) {Table}
            """, ct);

        var verrou = await db.LongAsync("SELECT GET_LOCK(@nom, 60)", ct, ("@nom", LockName));
        if (verrou != 1)
            throw new InvalidOperationException("Verrou des migrations indisponible : un autre serveur SAPHIR migre la base.");

        try
        {
            var appliquees = (await db.QueryAsync("SELECT version FROM sys_migrations", r => r.Int("version"), ct)).ToHashSet();
            var nombre = 0;
            foreach (var migration in All.Concat(Saphir.Server.Modules.ModuleRegistry.Migrations).OrderBy(m => m.Version))
            {
                if (appliquees.Contains(migration.Version))
                    continue;

                logger.LogInformation("Migration {Version} : {Nom}", migration.Version, migration.Nom);
                await db.BeginAsync(ct);
                try
                {
                    foreach (var requete in migration.Requetes)
                        await db.ExecuteAsync(requete, ct);
                    await db.ExecuteAsync(
                        "INSERT INTO sys_migrations (version, nom, applique_le) VALUES (@version, @nom, @date)", ct,
                        ("@version", migration.Version), ("@nom", migration.Nom), ("@date", DateTime.UtcNow));
                    await db.CommitAsync(ct);
                }
                catch
                {
                    await db.RollbackAsync();
                    throw;
                }
                nombre++;
            }
            return nombre;
        }
        finally
        {
            try
            {
                await db.ExecuteAsync("SELECT RELEASE_LOCK(@nom)", CancellationToken.None, ("@nom", LockName));
            }
            catch (Exception ex)
            {
                // Le verrou est libéré de toute façon à la fermeture de la connexion.
                logger.LogDebug(ex, "Libération du verrou des migrations impossible");
            }
        }
    }
}
