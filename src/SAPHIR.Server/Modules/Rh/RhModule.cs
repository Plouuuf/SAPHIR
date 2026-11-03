using Saphir.Server.Data;

namespace Saphir.Server.Modules.Rh;

/// <summary>
/// Ressources humaines (phase 3) : personnel, RIO, grades, services, unités, fonctions, affectations, statuts,
/// gestion documentaire et modèles. Migrations 100-199, tables rh_*. Contrat : docs/API-RH.md.
/// </summary>
public static class RhModule
{
    public static IReadOnlyList<Migration> Migrations => RhMigrations.All;

    public static void AddServices(IServiceCollection services)
    {
        services.AddSingleton<RhStore>();
        services.AddSingleton<RhDocumentsStore>();
    }

    public static void Map(WebApplication app) => RhEndpoints.Map(app);
}
