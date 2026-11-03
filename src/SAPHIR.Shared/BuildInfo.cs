using System.Reflection;

namespace Saphir.Shared;

/// <summary>Numéro de compilation officiel (build-N publié sur GitHub), 0 pour une compilation locale.</summary>
public static class BuildInfo
{
    public static int Number { get; } = Lire();

    private static int Lire()
    {
        var valeur = typeof(BuildInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "SaphirBuild")?.Value;
        return int.TryParse(valeur, out var n) ? n : 0;
    }
}

/// <summary>Paquets publiés et servis aux postes par le serveur SAPHIR.</summary>
public static class UpdatePackages
{
    public const string Windows = "SAPHIR.exe";
    public const string MacOs = "SAPHIR-macOS.zip";
    public const string Server = "SAPHIR-server-win-x64.zip";
    public const string TagPrefix = "build-";
}

/// <summary>Réponse de GET /api/updates/latest : dernière version disponible pour les postes.</summary>
public sealed record UpdateInfoDto(int Build, string Version, bool Windows, bool MacOs);
