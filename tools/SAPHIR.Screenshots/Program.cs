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
Shots.Output = Path.GetFullPath(args.Length > 0 ? args[0] : "captures");
Directory.CreateDirectory(Shots.Output);

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();
AvaloniaSynchronizationContext.InstallIfNeeded();

var user = new SaphirUserDto("560037927695024139", "jpdurand", null, true);

// 1-3. Ouverture de session, première connexion, « Bienvenue »
{
    var shell = new ShellViewModel();
    Shots.Pump(1500);
    shell.ShowForCapture(ShellScreen.Logon, "DURAND Jean-Pierre", SampleApi.Version);
    Shots.Capture(new ShellView { DataContext = shell }, 1600, 900, "01-ouverture-session.png");

    shell.ShowForCapture(ShellScreen.FirstLogin, "jpdurand", SampleApi.Version, "durand", "jean-pierre");
    Shots.Capture(new ShellView { DataContext = shell }, 1600, 900, "02-premiere-connexion.png");

    shell.ShowForCapture(ShellScreen.Welcome, "DURAND Jean-Pierre", SampleApi.Version);
    Shots.Capture(new ShellView { DataContext = shell }, 1600, 900, "03-bienvenue.png");
}

SaphirSession.Open(new ApiClient("exemple", new SampleApi()), user);
SaphirSession.SetInfo(SampleApi.Session());

// 4. Bureau avec le menu Démarrer ouvert
{
    var desktop = new DesktopViewModel(null, () => { }, () => { });
    desktop.UpdateClock(new DateTime(2026, 11, 2, 9, 41, 0));
    Shots.Capture(new DesktopView { DataContext = desktop }, 1600, 900, "04-bureau-menu-demarrer.png",
        () => desktop.IsStartMenuOpen = true);
}

// 5. Bureau avec deux fenêtres : Centre de paramètres puis Administration
{
    var desktop = new DesktopViewModel(null, () => { }, () => { });
    desktop.UpdateClock(new DateTime(2026, 11, 2, 9, 41, 0));
    Shots.Capture(new DesktopView { DataContext = desktop }, 1600, 900, "05-bureau-fenetres.png", () =>
    {
        desktop.OpenById(SaphirApps.Parametres);
        Shots.Pump(300);
        desktop.OpenById(SaphirApps.Administration);
    });
}

// 6-10. Administration : chaque onglet
{
    var vm = new AdministrationViewModel();
    Shots.Pump();
    Shots.Capture(new AdministrationView { DataContext = vm }, 1280, 820, "06-admin-comptes.png",
        () => vm.SelectedCompte = vm.Comptes.FirstOrDefault(c => c.Nom == "BORDINI"));

    vm.SelectedProfil = vm.Profils.FirstOrDefault(p => p.Code == "chef-de-bord");
    Shots.Capture(new AdministrationView { DataContext = vm }, 1280, 820, "07-admin-profils.png", () => Shots.SelectTab(1));
    Shots.Capture(new AdministrationView { DataContext = vm }, 1280, 820, "08-admin-parametres.png", () => Shots.SelectTab(2));
    Shots.Capture(new AdministrationView { DataContext = vm }, 1280, 820, "09-admin-historique.png", () => Shots.SelectTab(3));
    Shots.Capture(new AdministrationView { DataContext = vm }, 1280, 820, "10-admin-journal.png", () => Shots.SelectTab(4));
}

// 11. Centre de paramètres
{
    var vm = new SettingsViewModel();
    Shots.Pump();
    Shots.Capture(new SettingsView { DataContext = vm }, 1100, 760, "11-centre-parametres.png");
}

// 12. Téléphone : accueil
{
    var desktop = new DesktopViewModel(null, () => { }, () => { }) { IsCompactLayout = true };
    desktop.UpdateClock(new DateTime(2026, 11, 2, 9, 41, 0));
    var root = new Grid();
    root.Children.Add(new Image { Source = ImageLoader.Asset("Wallpapers/logon-portrait.jpg"), Stretch = Stretch.UniformToFill });
    root.Children.Add(new MobileHomeView { DataContext = desktop });
    Shots.Capture(root, 390, 844, "12-telephone-accueil.png");
}

// Modules métier : chaque module ajoute ses captures dans Modules/<Module>Captures.cs.
RhCaptures.Run();
OperationsCaptures.Run();
JudiciaireCaptures.Run();
ComplementsCaptures.Run();

Console.WriteLine("Captures enregistrées dans " + Shots.Output);
return 0;
