using Saphir.Server.Data;
using Saphir.Server.Modules.Complements;
using Saphir.Server.Modules.Judiciaire;
using Saphir.Server.Modules.Operations;
using Saphir.Server.Modules.Rh;

namespace Saphir.Server.Modules;

/// <summary>
/// Point d'entrée des modules métier. Chaque module possède son dossier (Modules/&lt;Module&gt;), ses tables (préfixes)
/// et sa plage de numéros de migration :
///   RH : 100-199 (rh_*)            Opérations (Main courante, PÉGASE, GÉOOPS) : 300-499 (mc_*, pg_*, geo_*)
///   Judiciaire (CHEOPS, LRPPN) : 500-699 (per_*, veh_*, ch_*, lr_*)    Compléments : 800-899 (app_*, pve_*)
/// Pas de clé étrangère entre modules : on stocke l'identifiant.
/// </summary>
public static class ModuleRegistry
{
    public static IEnumerable<Migration> Migrations =>
        RhModule.Migrations
            .Concat(OperationsModule.Migrations)
            .Concat(JudiciaireModule.Migrations)
            .Concat(ComplementsModule.Migrations);

    public static IServiceCollection AddSaphirModules(this IServiceCollection services)
    {
        RhModule.AddServices(services);
        OperationsModule.AddServices(services);
        JudiciaireModule.AddServices(services);
        ComplementsModule.AddServices(services);
        return services;
    }

    public static WebApplication MapSaphirModules(this WebApplication app)
    {
        RhModule.Map(app);
        OperationsModule.Map(app);
        JudiciaireModule.Map(app);
        ComplementsModule.Map(app);
        return app;
    }
}
