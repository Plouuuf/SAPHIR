using Saphir.Server.Services;
using Saphir.Shared;

namespace Saphir.Server;

/// <summary>Mise à jour des postes : dernière version et téléchargement des paquets (publics, sans données).</summary>
public static class UpdateEndpoints
{
    public static void MapUpdates(this WebApplication app)
    {
        app.MapGet("/api/updates/latest", () =>
            UpdateService.Current() is { } info ? Results.Ok(info) : Results.NoContent());

        app.MapGet("/api/updates/download/{platform}", (string platform) =>
        {
            var name = platform switch
            {
                "windows" => UpdatePackages.Windows,
                "macos" => UpdatePackages.MacOs,
                _ => null
            };
            var path = name is null ? null : UpdateService.PackagePath(name);
            return path is null
                ? Results.NotFound(new { error = "Aucun paquet disponible pour cette plateforme." })
                : Results.File(path, "application/octet-stream", name, enableRangeProcessing: true);
        });
    }
}
