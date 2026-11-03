using Saphir.Server.Data;

namespace Saphir.Server.Modules.Operations;

/// <summary>Main courante, PÉGASE, GÉOOPS (phases 4 et 5).</summary>
public static class OperationsModule
{
    public static IReadOnlyList<Migration> Migrations { get; } = OperationsMigrations.All;

    public static void AddServices(IServiceCollection services)
    {
        services.AddHttpClient(OperationsNotifier.ClientDiscord);
        services.AddSingleton<OperationsNotifier>();
        services.AddSingleton<MainCouranteStore>();
        services.AddSingleton<PegaseStore>();
        services.AddSingleton<GeoopsStore>();
    }

    public static void Map(WebApplication app)
    {
        app.MapMainCourante();
        app.MapPegase();
        app.MapGeoops();
        app.MapIntegrationsOperations();
    }
}
