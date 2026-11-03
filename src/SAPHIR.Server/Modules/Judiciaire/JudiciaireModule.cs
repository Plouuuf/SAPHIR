using Saphir.Server.Data;

namespace Saphir.Server.Modules.Judiciaire;

/// <summary>CHEOPS et LRPPN (phases 6 et 7).</summary>
public static class JudiciaireModule
{
    public static IReadOnlyList<Migration> Migrations { get; } = Array.Empty<Migration>();

    public static void AddServices(IServiceCollection services)
    {
    }

    public static void Map(WebApplication app)
    {
    }
}
