using Saphir.Server.Data;

namespace Saphir.Server.Modules.Operations;

/// <summary>Main courante, PÉGASE, GÉOOPS (phases 4 et 5).</summary>
public static class OperationsModule
{
    public static IReadOnlyList<Migration> Migrations { get; } = Array.Empty<Migration>();

    public static void AddServices(IServiceCollection services)
    {
    }

    public static void Map(WebApplication app)
    {
    }
}
