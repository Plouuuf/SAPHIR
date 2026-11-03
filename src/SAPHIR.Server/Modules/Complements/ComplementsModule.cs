using Saphir.Server.Data;

namespace Saphir.Server.Modules.Complements;

/// <summary>
/// NEO, PVe, Annuaire, Messagerie, Notes, MRZ, Bodycam, Tchap (phase 8). Contrat : docs/API-COMPLEMENTS.md.
/// NEO n'a pas de route propre : il réutilise celles des autres applications (et des modules Judiciaire / Opérations).
/// </summary>
public static class ComplementsModule
{
    public static IReadOnlyList<Migration> Migrations { get; } = ComplementsMigrations.All;

    public static void AddServices(IServiceCollection services)
    {
        services.AddSingleton<MessagerieStore>();
        services.AddSingleton<NotesStore>();
        services.AddSingleton<PveStore>();
        services.AddSingleton<BodycamStore>();
        services.AddSingleton<TchapStore>();
    }

    public static void Map(WebApplication app)
    {
        AnnuaireEndpoints.Map(app);
        MessagerieEndpoints.Map(app);
        NotesEndpoints.Map(app);
        MrzEndpoints.Map(app);
        PveEndpoints.Map(app);
        BodycamEndpoints.Map(app);
        TchapEndpoints.Map(app);
    }
}
