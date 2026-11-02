using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Saphir;
using Saphir.Screenshots;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.ViewModels;
using Saphir.ViewModels.Apps;
using Saphir.Views;
using Saphir.Views.Apps;

// Captures des écrans de SAPHIR avec des données fictives, sans serveur ni affichage.
var output = Path.GetFullPath(args.Length > 0 ? args[0] : "captures");
Directory.CreateDirectory(output);

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();
AvaloniaSynchronizationContext.InstallIfNeeded();

Window? currentWindow = null;
var user = new SaphirUserDto("560037927695024139", "jpdurand", null, true);

// 1-3. Ouverture de session, première connexion, « Bienvenue »
{
    var shell = new ShellViewModel();
    Pump(1500);
    shell.ShowForCapture(ShellScreen.Logon, "DURAND Jean-Pierre", SampleApi.Version);
    Capture(new ShellView { DataContext = shell }, 1600, 900, "01-ouverture-session.png");

    shell.ShowForCapture(ShellScreen.FirstLogin, "jpdurand", SampleApi.Version, "durand", "jean-pierre");
    Capture(new ShellView { DataContext = shell }, 1600, 900, "02-premiere-connexion.png");

    shell.ShowForCapture(ShellScreen.Welcome, "DURAND Jean-Pierre", SampleApi.Version);
    Capture(new ShellView { DataContext = shell }, 1600, 900, "03-bienvenue.png");
}

SaphirSession.Open(new ApiClient("exemple", new SampleApi()), user);
SaphirSession.SetInfo(SampleApi.Session());

// 4. Bureau avec le menu Démarrer ouvert
{
    var desktop = new DesktopViewModel(null, () => { }, () => { });
    desktop.UpdateClock(new DateTime(2026, 11, 2, 9, 41, 0));
    Capture(new DesktopView { DataContext = desktop }, 1600, 900, "04-bureau-menu-demarrer.png",
        () => desktop.IsStartMenuOpen = true);
}

// 5. Bureau avec deux fenêtres : Centre de paramètres puis Administration
{
    var desktop = new DesktopViewModel(null, () => { }, () => { });
    desktop.UpdateClock(new DateTime(2026, 11, 2, 9, 41, 0));
    Capture(new DesktopView { DataContext = desktop }, 1600, 900, "05-bureau-fenetres.png", () =>
    {
        desktop.OpenById(SaphirApps.Parametres);
        Pump(300);
        desktop.OpenById(SaphirApps.Administration);
    });
}

// 6-10. Administration : chaque onglet
{
    var vm = new AdministrationViewModel();
    Pump();
    Capture(new AdministrationView { DataContext = vm }, 1280, 820, "06-admin-comptes.png",
        () => vm.SelectedCompte = vm.Comptes.FirstOrDefault(c => c.Nom == "BORDINI"));

    vm.SelectedProfil = vm.Profils.FirstOrDefault(p => p.Code == "chef-de-bord");
    Capture(new AdministrationView { DataContext = vm }, 1280, 820, "07-admin-profils.png", () => SelectTab(1));
    Capture(new AdministrationView { DataContext = vm }, 1280, 820, "08-admin-parametres.png", () => SelectTab(2));
    Capture(new AdministrationView { DataContext = vm }, 1280, 820, "09-admin-historique.png", () => SelectTab(3));
    Capture(new AdministrationView { DataContext = vm }, 1280, 820, "10-admin-journal.png", () => SelectTab(4));
}

// 11. Centre de paramètres
{
    var vm = new SettingsViewModel();
    Pump();
    Capture(new SettingsView { DataContext = vm }, 1100, 760, "11-centre-parametres.png");
}

// 12. Téléphone : accueil
{
    var desktop = new DesktopViewModel(null, () => { }, () => { }) { IsCompactLayout = true };
    desktop.UpdateClock(new DateTime(2026, 11, 2, 9, 41, 0));
    var root = new Grid();
    root.Children.Add(new Image { Source = ImageLoader.Asset("Wallpapers/logon-portrait.jpg"), Stretch = Stretch.UniformToFill });
    root.Children.Add(new MobileHomeView { DataContext = desktop });
    Capture(root, 390, 844, "12-telephone-accueil.png");
}

Console.WriteLine("Captures enregistrées dans " + output);
return 0;

// Sélectionne l'onglet n de la vue Administration actuellement affichée.
void SelectTab(int index)
{
    if (currentWindow?.Content is Control content &&
        content.GetLogicalDescendants().OfType<TabControl>().FirstOrDefault() is { } tabs)
        tabs.SelectedIndex = index;
}

void Pump(int milliseconds = 800)
{
    var end = DateTime.UtcNow.AddMilliseconds(milliseconds);
    while (DateTime.UtcNow < end)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Thread.Sleep(15);
    }
}

void Capture(Control content, int width, int height, string name, Action? afterShow = null)
{
    var window = new Window
    {
        Width = width,
        Height = height,
        Content = content,
        SystemDecorations = SystemDecorations.None,
        FontFamily = new FontFamily("Segoe UI, Helvetica Neue, Roboto, DejaVu Sans, Arial")
    };
    currentWindow = window;
    window.Show();
    Pump();
    if (afterShow is not null)
    {
        afterShow();
        Pump(1500);
    }
    var frame = window.CaptureRenderedFrame();
    if (frame is null)
        Console.Error.WriteLine("Capture vide : " + name);
    else
        frame.Save(Path.Combine(output, name));
    window.Content = null;
    window.Close();
    currentWindow = null;
    Pump(100);
    Console.WriteLine("✓ " + name);
}
