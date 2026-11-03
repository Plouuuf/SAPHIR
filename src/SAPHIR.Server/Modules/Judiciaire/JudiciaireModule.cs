using Saphir.Server.Data;

namespace Saphir.Server.Modules.Judiciaire;

/// <summary>
/// CHEOPS et LRPPN (phases 6 et 7) : personnes et véhicules fictifs (routes partagées), fichiers CHEOPS, procédures,
/// actes, pièces, validation, PDF, ANADOC. Tables per_, veh_, ch_, lr_ (migrations 500-699).
/// Configuration facultative « FiveM » (ConnectionString, Framework : esx | qbcore) : lecture seule des personnages FiveM.
/// </summary>
public static class JudiciaireModule
{
    public static IReadOnlyList<Migration> Migrations { get; } = JudiciaireMigrations.All;

    public static void AddServices(IServiceCollection services)
    {
        services.AddOptions<FiveMOptions>().BindConfiguration("FiveM");
        services.AddSingleton<FiveMLecture>();
        services.AddSingleton<PersonnesStore>();
        services.AddSingleton<CheopsStore>();
        services.AddSingleton<LrppnStore>();
        services.AddSingleton<ActesStore>();
    }

    public static void Map(WebApplication app) => JudiciaireEndpoints.Map(app);
}
