using Microsoft.Extensions.Options;
using Saphir.Server.Services;
using Saphir.Shared.Contracts;

namespace Saphir.Server.Data;

/// <summary>
/// Accès à la base SAPHIR (MariaDB). Au premier usage (et au démarrage), applique les migrations puis les données
/// de départ ; si la base est injoignable, nouvel essai au plus toutes les 30 secondes, les routes répondant 503 entre-temps.
/// </summary>
public sealed class SaphirDb(IOptionsMonitor<DatabaseOptions> options, ILogger<SaphirDb> logger)
{
    private static readonly TimeSpan DelaiNouvelEssai = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _initialisation = new(1, 1);
    private volatile bool _prete;
    private long _dernierEssaiTicks;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.CurrentValue.Saphir);

    /// <summary>Migrations et données de départ appliquées.</summary>
    public bool IsReady => _prete;

    public Task<SqlSession> OpenAsync(CancellationToken ct)
        => SqlSession.OpenAsync(options.CurrentValue.Saphir, ct);

    /// <summary>Prépare la base si ce n'est pas déjà fait. Retourne <c>false</c> si elle reste indisponible.</summary>
    public async Task<bool> EnsureReadyAsync(CancellationToken ct)
    {
        if (_prete)
            return true;
        if (!IsConfigured || !PeutReessayer())
            return false;

        await _initialisation.WaitAsync(ct);
        try
        {
            if (_prete)
                return true;
            if (!PeutReessayer())
                return false;
            Interlocked.Exchange(ref _dernierEssaiTicks, DateTime.UtcNow.Ticks);

            // Pas d'annulation pendant une migration : la requête du poste peut être abandonnée, pas le schéma.
            await using var db = await OpenAsync(CancellationToken.None);
            var appliquees = await Migrations.AppliquerAsync(db, logger, CancellationToken.None);
            await SocleSeeder.SeedAsync(db, CancellationToken.None);
            _prete = true;
            logger.LogInformation("Base SAPHIR prête ({Count} migration(s) appliquée(s)).", appliquees);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Base SAPHIR injoignable ou migration impossible : les routes /api répondront 503 (nouvel essai dans 30 s).");
            return false;
        }
        finally
        {
            _initialisation.Release();
        }
    }

    private bool PeutReessayer()
    {
        var dernier = Interlocked.Read(ref _dernierEssaiTicks);
        return dernier == 0 || DateTime.UtcNow.Ticks - dernier >= DelaiNouvelEssai.Ticks;
    }
}

/// <summary>Données de départ, rejouées à chaque démarrage sans rien écraser de ce qu'un administrateur a modifié.</summary>
public static class SocleSeeder
{
    public static async Task SeedAsync(SqlSession db, CancellationToken ct)
    {
        var maintenant = DateTime.UtcNow;
        await db.BeginAsync(ct);
        try
        {
            foreach (var profil in DefaultProfiles.All)
            {
                var existant = await db.ScalarAsync("SELECT id FROM auth_profils WHERE code = @code", ct, ("@code", profil.Code));
                long profilId;
                if (existant is null)
                {
                    profilId = await db.InsertAsync(
                        """
                        INSERT INTO auth_profils (code, nom, description, systeme, cree_le, maj_le)
                        VALUES (@code, @nom, @description, 1, @maintenant, @maintenant)
                        """, ct,
                        ("@code", profil.Code), ("@nom", profil.Nom), ("@description", profil.Description),
                        ("@maintenant", maintenant));

                    // Permissions par défaut : seulement à la création, pour ne pas écraser les choix d'un administrateur.
                    foreach (var permission in profil.Permissions.Distinct(StringComparer.Ordinal))
                        await AjouterPermissionAsync(db, profilId, permission, ct);
                }
                else
                {
                    profilId = Convert.ToInt64(existant, System.Globalization.CultureInfo.InvariantCulture);
                    await db.ExecuteAsync("UPDATE auth_profils SET systeme = 1 WHERE id = @id AND systeme = 0", ct, ("@id", profilId));
                }

                // L'administrateur technique reçoit toujours tout le catalogue, y compris les permissions ajoutées depuis.
                if (profil.Code == DefaultProfiles.AdministrateurTechnique)
                    foreach (var permission in Permissions.All)
                        await AjouterPermissionAsync(db, profilId, permission.Code, ct);
            }

            foreach (var parametre in SocleRegles.ParametresParDefaut)
            {
                await db.ExecuteAsync(
                    """
                    INSERT IGNORE INTO sys_parametres (cle, valeur, libelle, aide, modifiable, maj_le)
                    VALUES (@cle, @valeur, @libelle, @aide, @modifiable, @maintenant)
                    """, ct,
                    ("@cle", parametre.Cle), ("@valeur", parametre.Valeur), ("@libelle", parametre.Libelle),
                    ("@aide", parametre.Aide), ("@modifiable", parametre.Modifiable), ("@maintenant", maintenant));
                // Libellé et aide suivent le code ; la valeur reste celle choisie par l'administrateur.
                await db.ExecuteAsync(
                    "UPDATE sys_parametres SET libelle = @libelle, aide = @aide WHERE cle = @cle", ct,
                    ("@cle", parametre.Cle), ("@libelle", parametre.Libelle), ("@aide", parametre.Aide));
            }

            // Montée de version : une version affichée antérieure à celle du serveur est remplacée.
            await db.ExecuteAsync(
                "UPDATE sys_parametres SET valeur = @v WHERE cle = @cle AND valeur IN ('0.1.0.0', '0.2.0.0')", ct,
                ("@v", SocleRegles.VersionParDefaut), ("@cle", ParametresCles.Version));

            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
    }

    private static Task<int> AjouterPermissionAsync(SqlSession db, long profilId, string permission, CancellationToken ct)
        => db.ExecuteAsync(
            "INSERT IGNORE INTO auth_profil_permissions (profil_id, permission) VALUES (@profil, @permission)", ct,
            ("@profil", profilId), ("@permission", permission));
}
