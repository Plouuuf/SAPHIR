using Android.App;
using Android.Content.PM;
using Saphir;
using Avalonia.Android;

namespace SaphirDroid;

/// <summary>SAPHIR sur Android : une seule vue (<see cref="Saphir.Views.ShellView"/>), plein écran.</summary>
[Activity(
    Label = "SAPHIR",
    Theme = "@style/SaphirTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleTask,
    WindowSoftInputMode = Android.Views.SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout |
                           ConfigChanges.SmallestScreenSize | ConfigChanges.UiMode | ConfigChanges.Keyboard |
                           ConfigChanges.KeyboardHidden | ConfigChanges.Density)]
public class MainActivity : AvaloniaMainActivity<App>
{
}
