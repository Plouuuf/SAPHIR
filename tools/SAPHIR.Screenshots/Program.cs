using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Saphir;
using Saphir.Screenshots;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.ViewModels;
using Saphir.Views;
using Saphir.Views.Apps;

// Captures des écrans de SAPHIR avec des données d'exemple, sans serveur ni affichage.
var output = Path.GetFullPath(args.Length > 0 ? args[0] : "captures");
Directory.CreateDirectory(output);

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();
AvaloniaSynchronizationContext.InstallIfNeeded();

var user = new SaphirUserDto("1", "Brigadier DURAND", null,
    [AccessLevels.Agent, AccessLevels.Administration, .. SaphirApps.All]);
SaphirSession.Open(new ApiClient("exemple", new SampleApi()), user);

// 1. Bureau avec PEGASE ouvert
{
    var desktop = new DesktopViewModel(user, null, () => { }, () => { });
    var root = new Grid();
    root.Children.Add(new Image { Source = ImageLoader.Asset("Wallpapers/landscape.jpg"), Stretch = Stretch.UniformToFill });
    root.Children.Add(new DesktopView { DataContext = desktop });
    Capture(root, 1600, 900, "01-bureau.png", () =>
    {
        desktop.OpenApp(desktop.Apps.First(a => a.Id == SaphirApps.MeoFic));
        Pump(300);
        desktop.OpenApp(desktop.Apps.First(a => a.Id == SaphirApps.Pegase));
    });
}

// 2. MEO-FIC : TAJ d'une personne inscrite au FPR
{
    var vm = new MeoFicViewModel();
    Search(vm, "MARCHETTI");
    vm.SelectedPersonResult = vm.PersonResults[0];
    Pump();
    Capture(new MeoFicView { DataContext = vm }, 1280, 820, "02-meofic-taj.png");

    vm.PersonTabs[1].SelectCommand.Execute(null);
    Pump();
    Capture(new MeoFicView { DataContext = vm }, 1280, 820, "03-meofic-fpr.png");

    vm.PersonTabs[2].SelectCommand.Execute(null);
    Pump();
    Capture(new MeoFicView { DataContext = vm }, 1280, 820, "04-meofic-snpc.png");
}

// 3. MEO-FIC : véhicule signalé au FOVeS, puis SIV
{
    var vm = new MeoFicViewModel();
    vm.VehicleTabs[0].SelectCommand.Execute(null);
    Search(vm, "FT-119");
    vm.SelectedVehicleResult = vm.VehicleResults[0];
    Pump();
    Capture(new MeoFicView { DataContext = vm }, 1280, 820, "05-meofic-foves.png");

    vm.VehicleTabs[2].SelectCommand.Execute(null);
    Pump();
    Capture(new MeoFicView { DataContext = vm }, 1280, 820, "06-meofic-fva.png");
}

// 4. PEGASE : événements et fiche
{
    var vm = new PegaseViewModel();
    Pump();
    vm.SelectedEvent = vm.Events.First(e => e.Id == 108);
    Pump();
    Capture(new PegaseView { DataContext = vm }, 1280, 860, "07-pegase-evenement.png");

    vm.ShowCrewsCommand.Execute(null);
    Pump();
    Capture(new PegaseView { DataContext = vm }, 1280, 860, "08-pegase-equipages.png");
    vm.Dispose();
}

// 5. PEGASE : prise d'appel ouverte au décroché d'un 17
{
    SaphirSession.PendingCallId = 42;
    var vm = new PegaseViewModel();
    Pump();
    vm.CallForm.Adresse = "Rue du Général Leclerc, devant la pharmacie";
    vm.CallForm.Commune = "Créteil";
    vm.CallForm.Nature = "Vol avec violence / à l'arraché";
    vm.CallForm.PrioriteIndex = 0;
    vm.CallForm.RequerantNom = "Mme LEFÈVRE Anne";
    vm.CallForm.Description = "On vient de m'arracher mon sac. Un homme en scooter noir, casque blanc, parti vers la mairie.";
    Capture(new PegaseView { DataContext = vm }, 1280, 900, "09-pegase-prise-appel.png");
    vm.Dispose();
}

// 6. PTS : FIJAIS, FAED et traces
{
    var vm = new MeoFicViewModel(MeoMode.Pts);
    Search(vm, "MARCHETTI");
    vm.SelectedPersonResult = vm.PersonResults[0];
    Pump();
    Capture(new MeoFicView { DataContext = vm }, 1280, 820, "10-pts-faed.png");

    vm.PersonTabs[2].SelectCommand.Execute(null);
    Pump();
    Capture(new MeoFicView { DataContext = vm }, 1280, 820, "11-pts-fijais.png");

    vm.VehicleTabs[0].SelectCommand.Execute(null);
    Pump();
    Capture(new MeoFicView { DataContext = vm }, 1280, 820, "12-pts-traces.png");
    vm.Suspend();
}

// 7. Téléphone : PEGASE
{
    var vm = new PegaseViewModel();
    Pump();
    Capture(new PegaseView { DataContext = vm }, 390, 844, "13-telephone-pegase.png");
    vm.Dispose();
}

Console.WriteLine("Captures enregistrées dans " + output);
return 0;

void Search(MeoFicViewModel vm, string text)
{
    vm.SearchText = text;
    vm.SearchCommand.Execute(null);
    Pump();
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
    Pump(100);
    Console.WriteLine("✓ " + name);
}
