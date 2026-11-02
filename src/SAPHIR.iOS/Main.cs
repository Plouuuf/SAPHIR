using UIKit;

namespace SaphirIos;

public static class Application
{
    // Point d'entrée iOS.
    private static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
