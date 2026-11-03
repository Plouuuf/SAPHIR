using Saphir.Server.Data;

namespace Saphir.Server.Modules.Complements;

/// <summary>NEO, PVe, Annuaire, Messagerie, Notes, MRZ, Bodycam, Tchap (phase 8).</summary>
public static class ComplementsModule
{
    public static IReadOnlyList<Migration> Migrations { get; } = Array.Empty<Migration>();

    public static void AddServices(IServiceCollection services)
    {
    }

    public static void Map(WebApplication app)
    {
    }
}
