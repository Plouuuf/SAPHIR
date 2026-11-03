using System.Diagnostics;
using System.Text.Json;
using Saphir.Shared;
using Saphir.Shared.Contracts;

namespace Saphir.Server.Services;

/// <summary>Réglages de la mise à jour automatique (section « Updates » de la configuration).</summary>
public sealed class UpdateOptions
{
    /// <summary>Vérification de GitHub (désactivable).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Dépôt public dont on lit la dernière version publiée.</summary>
    public string Repository { get; set; } = "Plouuuf/SAPHIR";

    /// <summary>Intervalle entre deux vérifications.</summary>
    public int CheckMinutes { get; set; } = 10;

    /// <summary>Le serveur (VPS Windows) s'installe lui-même la nouvelle version.</summary>
    public bool AutoInstallServer { get; set; } = true;

    /// <summary>Tâche planifiée qui applique la mise à jour du serveur (créée par deploy/install-serveur.ps1).</summary>
    public string InstallTask { get; set; } = "SAPHIR Mise a jour";
}

/// <summary>
/// Mise à jour automatique, comme ARGOS : le serveur lit la dernière version publiée sur GitHub (build-N),
/// télécharge les paquets des postes (Windows, macOS) et les leur sert (GET /api/updates/…) ;
/// les postes se mettent à jour depuis le serveur, sans accès à GitHub. Le serveur Windows s'installe aussi
/// lui-même sa nouvelle version via la tâche planifiée « SAPHIR Mise a jour ».
/// Journal : data\updates\verification.log.
/// </summary>
public sealed class UpdateService(
    Microsoft.Extensions.Options.IOptionsMonitor<UpdateOptions> optionsMonitor,
    RealtimeHub hub,
    IHttpClientFactory httpFactory,
    ILogger<UpdateService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string Dir { get; } = Path.Combine(AppContext.BaseDirectory, "data", "updates");
    private static string CurrentFile => Path.Combine(Dir, "postes.json");

    /// <summary>Dernière version disponible pour les postes (null tant qu'aucun paquet n'a été téléchargé).</summary>
    public static UpdateInfoDto? Current()
    {
        try
        {
            return File.Exists(CurrentFile) ? JsonSerializer.Deserialize<UpdateInfoDto>(File.ReadAllText(CurrentFile), Json) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Chemin du paquet <paramref name="name"/> de la version courante (null s'il n'existe pas).</summary>
    public static string? PackagePath(string name)
    {
        var current = Current();
        if (current is null) return null;
        var path = Path.Combine(Dir, UpdatePackages.TagPrefix + current.Build, name);
        return File.Exists(path) ? path : null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = optionsMonitor.CurrentValue;
            if (options.Enabled)
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(TimeSpan.FromMinutes(15));
                    await CheckAsync(options, timeout.Token);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Vérification des mises à jour impossible : {Message}", ex.Message);
                    Trace("ERREUR : " + ex.Message);
                }
            }
            await Task.Delay(TimeSpan.FromMinutes(Math.Max(2, options.CheckMinutes)), stoppingToken);
        }
    }

    private async Task CheckAsync(UpdateOptions options, CancellationToken ct)
    {
        var http = httpFactory.CreateClient("updates");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{options.Repository}/releases/latest");
        request.Headers.UserAgent.ParseAdd("SAPHIR-Server");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            Trace($"GitHub a répondu {(int)response.StatusCode}.");
            return;
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        if (!tag.StartsWith(UpdatePackages.TagPrefix, StringComparison.Ordinal) || !int.TryParse(tag[UpdatePackages.TagPrefix.Length..], out var build))
            return;
        var version = root.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag;
        var assets = root.GetProperty("assets").EnumerateArray().ToDictionary(
            a => a.GetProperty("name").GetString() ?? string.Empty,
            a => a.GetProperty("browser_download_url").GetString() ?? string.Empty);

        // ----- Paquets des postes -----
        var current = Current();
        if (current is null || build > current.Build)
        {
            var folder = Path.Combine(Dir, UpdatePackages.TagPrefix + build);
            Directory.CreateDirectory(folder);
            var info = new UpdateInfoDto(build, version, false, false);
            if (assets.TryGetValue(UpdatePackages.Windows, out var winUrl))
            {
                await DownloadAsync(http, winUrl, Path.Combine(folder, UpdatePackages.Windows), ct);
                info = info with { Windows = true };
            }
            if (assets.TryGetValue(UpdatePackages.MacOs, out var macUrl))
            {
                await DownloadAsync(http, macUrl, Path.Combine(folder, UpdatePackages.MacOs), ct);
                info = info with { MacOs = true };
            }
            await File.WriteAllTextAsync(CurrentFile, JsonSerializer.Serialize(info, Json), ct);
            foreach (var old in Directory.GetDirectories(Dir, UpdatePackages.TagPrefix + "*"))
                if (!string.Equals(Path.GetFileName(old), UpdatePackages.TagPrefix + build, StringComparison.Ordinal))
                    try { Directory.Delete(old, true); } catch { /* fichier en cours d'envoi : supprimé la prochaine fois */ }
            logger.LogInformation("Postes : {Version} (build {Build}) disponible.", version, build);
            Trace($"Postes : build {build} téléchargée (Windows {info.Windows}, macOS {info.MacOs}).");
            await hub.BroadcastAsync(new RealtimeMessage(RealtimeTypes.UpdateAvailable, Title: "Mise à jour de SAPHIR",
                Text: version), ct);
        }

        // ----- Serveur (VPS Windows, compilation officielle uniquement) -----
        Trace($"Dernière version : {tag} ; serveur : build {BuildInfo.Number} ; postes : build {Current()?.Build}.");
        if (!options.AutoInstallServer || !OperatingSystem.IsWindows() || BuildInfo.Number <= 0 || build <= BuildInfo.Number)
            return;
        if (!assets.TryGetValue(UpdatePackages.Server, out var serverUrl))
            return;
        var marker = Path.Combine(Dir, "tentative.txt");
        if (File.Exists(marker) && (await File.ReadAllTextAsync(marker, ct)).Trim() == build.ToString())
            return; // déjà tentée : on n'insiste pas (voir verification.log)

        var zip = Path.Combine(Dir, "serveur.zip");
        await DownloadAsync(http, serverUrl, zip, ct);
        await File.WriteAllTextAsync(marker, build.ToString(), ct);
        Trace($"Installation du serveur build {build} : lancement de la tâche « {options.InstallTask} ».");
        try
        {
            await hub.BroadcastAsync(new RealtimeMessage(RealtimeTypes.Notify, Title: "Mise à jour de SAPHIR",
                Text: "Le serveur redémarre pour se mettre à jour : reconnexion automatique dans quelques secondes."), ct);
        }
        catch
        {
            // L'annonce ne doit jamais empêcher l'installation.
        }

        using var process = Process.Start(new ProcessStartInfo("schtasks", $"/Run /TN \"{options.InstallTask}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        if (process is not null)
        {
            await process.WaitForExitAsync(ct);
            if (process.ExitCode != 0)
            {
                Trace("schtasks a échoué : " + await process.StandardError.ReadToEndAsync(ct));
                File.Delete(marker); // nouvel essai à la prochaine vérification
            }
        }
    }

    private static async Task DownloadAsync(HttpClient http, string url, string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("SAPHIR-Server");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var temp = path + ".part";
        await using (var file = File.Create(temp))
            await response.Content.CopyToAsync(file, ct);
        File.Move(temp, path, true);
    }

    private static void Trace(string text)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var file = Path.Combine(Dir, "verification.log");
            if (File.Exists(file) && new FileInfo(file).Length > 300_000) File.Delete(file);
            File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}");
        }
        catch
        {
            // Journal facultatif.
        }
    }
}
