using System.Diagnostics;
using System.Net.Http.Json;
using Saphir.Shared;

namespace Saphir.Services;

/// <summary>
/// Mise à jour automatique du poste (Windows et macOS), comme ARGOS : le poste interroge le serveur SAPHIR
/// (GET /api/updates/latest), télécharge le nouveau paquet depuis le serveur, remplace l'application puis la relance.
/// Seules les compilations officielles (build &gt; 0) se mettent à jour ; téléphones et tablettes passent par les stores.
/// </summary>
public static class UpdateClient
{
    public static bool IsSupported =>
        !AppPaths.IsMobile && BuildInfo.Number > 0 && (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());

    private static string Platform => OperatingSystem.IsWindows() ? "windows" : "macos";

    /// <summary>Version plus récente disponible sur le serveur, sinon null.</summary>
    public static async Task<UpdateInfoDto?> CheckAsync(CancellationToken ct = default)
    {
        if (!IsSupported) return null;
        try
        {
            using var http = new HttpClient { BaseAddress = ClientSettings.Current.HttpBaseUri, Timeout = TimeSpan.FromSeconds(10) };
            using var response = await http.GetAsync("api/updates/latest", ct);
            if (!response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NoContent)
                return null;
            var info = await response.Content.ReadFromJsonAsync<UpdateInfoDto>(cancellationToken: ct);
            if (info is null || info.Build <= BuildInfo.Number) return null;
            return (OperatingSystem.IsWindows() ? info.Windows : info.MacOs) ? info : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Télécharge puis installe la mise à jour et relance SAPHIR. Ne revient qu'en cas d'échec (message).</summary>
    public static async Task<string?> InstallAsync(UpdateInfoDto info, IProgress<int>? progress, CancellationToken ct = default)
    {
        try
        {
            var dir = Path.Combine(AppPaths.DataDirectory, "updates");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, OperatingSystem.IsWindows() ? UpdatePackages.Windows : UpdatePackages.MacOs);

            using (var http = new HttpClient { BaseAddress = ClientSettings.Current.HttpBaseUri, Timeout = TimeSpan.FromMinutes(15) })
            using (var response = await http.GetAsync("api/updates/download/" + Platform, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? 0;
                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var target = File.Create(file);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total > 0) progress?.Report((int)(done * 100 / total));
                }
            }

            if (OperatingSystem.IsWindows())
                InstallWindows(file);
            else
                await InstallMacAsync(file, dir, ct);
            App.Quit();
            return null;
        }
        catch (Exception ex)
        {
            return "Mise à jour impossible : " + ex.Message;
        }
    }

    /// <summary>Windows : l'exécutable en cours peut être renommé ; le nouveau prend sa place puis est lancé.</summary>
    private static void InstallWindows(string downloaded)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Emplacement de SAPHIR inconnu.");
        var old = exe + ".old";
        if (File.Exists(old)) File.Delete(old);
        File.Move(exe, old);
        try
        {
            File.Copy(downloaded, exe, true);
        }
        catch
        {
            File.Move(old, exe);
            throw;
        }
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
    }

    /// <summary>macOS : décompresse SAPHIR.app (ditto garde droits et signature), remplace l'application puis la relance.</summary>
    private static async Task InstallMacAsync(string zip, string dir, CancellationToken ct)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Emplacement de SAPHIR inconnu.");
        // …/SAPHIR.app/Contents/MacOS/SAPHIR → …/SAPHIR.app
        var bundle = Directory.GetParent(exe)?.Parent?.Parent?.FullName;
        if (bundle is null || !bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SAPHIR doit être lancé depuis SAPHIR.app pour se mettre à jour.");

        var extract = Path.Combine(dir, "extraction");
        if (Directory.Exists(extract)) Directory.Delete(extract, true);
        Directory.CreateDirectory(extract);
        await RunAsync("/usr/bin/ditto", ["-x", "-k", zip, extract], ct);
        var fresh = Path.Combine(extract, "SAPHIR.app");
        if (!Directory.Exists(fresh))
            throw new InvalidOperationException("Paquet macOS incomplet.");

        var old = bundle + ".old";
        if (Directory.Exists(old)) Directory.Delete(old, true);
        Directory.Move(bundle, old);
        try
        {
            await RunAsync("/usr/bin/ditto", [fresh, bundle], ct);
        }
        catch
        {
            if (Directory.Exists(bundle)) Directory.Delete(bundle, true);
            Directory.Move(old, bundle);
            throw;
        }
        try { await RunAsync("/usr/bin/xattr", ["-cr", bundle], ct); } catch { /* sans quarantaine : sans effet */ }
        Process.Start(new ProcessStartInfo("/usr/bin/open", ["-n", bundle]) { UseShellExecute = false });
    }

    /// <summary>Au démarrage : supprime les restes de la mise à jour précédente.</summary>
    public static void CleanUp()
    {
        if (!IsSupported) return;
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is null) return;
            if (OperatingSystem.IsWindows() && File.Exists(exe + ".old"))
                File.Delete(exe + ".old");
            if (OperatingSystem.IsMacOS() && Directory.GetParent(exe)?.Parent?.Parent?.FullName is { } bundle
                && Directory.Exists(bundle + ".old"))
                Directory.Delete(bundle + ".old", true);
            var dir = Path.Combine(AppPaths.DataDirectory, "updates");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch
        {
            // L'ancien exécutable peut encore être verrouillé quelques secondes : supprimé la prochaine fois.
        }
    }

    private static async Task RunAsync(string file, string[] args, CancellationToken ct)
    {
        using var process = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, RedirectStandardError = true })
                            ?? throw new InvalidOperationException(file + " introuvable.");
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(file)} : {await process.StandardError.ReadToEndAsync(ct)}");
    }
}
