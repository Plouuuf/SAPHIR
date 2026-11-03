namespace Saphir.Screenshots;

/// <summary>Captures et données fictives du module Operations.</summary>
public static class OperationsCaptures
{
    /// <summary>Réponse fictive à une requête du poste (null = route inconnue de ce module).</summary>
    public static object? Sample(string method, string path, string query, string? body) => null;

    /// <summary>Captures du module (utiliser Shots.Capture). Les fichiers sont numérotés à partir de 40.</summary>
    public static void Run()
    {
    }
}
