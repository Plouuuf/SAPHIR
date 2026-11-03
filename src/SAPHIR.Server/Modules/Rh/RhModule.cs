using Saphir.Server.Data;

namespace Saphir.Server.Modules.Rh;

/// <summary>Ressources humaines (phase 3).</summary>
public static class RhModule
{
    public static IReadOnlyList<Migration> Migrations { get; } = Array.Empty<Migration>();

    public static void AddServices(IServiceCollection services)
    {
    }

    public static void Map(WebApplication app)
    {
    }
}
