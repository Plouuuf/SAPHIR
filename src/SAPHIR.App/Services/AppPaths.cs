namespace Saphir.Services;

/// <summary>Dossiers de données de SAPHIR sur chaque plateforme.</summary>
internal static class AppPaths
{
    // Windows : %LOCALAPPDATA%\VDMS\SAPHIR
    // macOS   : ~/Library/Application Support/VDMS/SAPHIR
    // Android / iOS : dossier privé de l'application (supprimé avec elle).
    public static string DataDirectory
    {
        get
        {
            var dir = IsMobile
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SAPHIR")
                : OperatingSystem.IsMacOS()
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "VDMS", "SAPHIR")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VDMS", "SAPHIR");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static bool IsMobile => OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();

    public static string DiscordSessionPath => Path.Combine(DataDirectory, "discord-session.json");
}
