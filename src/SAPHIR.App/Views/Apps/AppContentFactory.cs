using Saphir.Shared.Contracts;
using Saphir.ViewModels;
using Avalonia.Controls;

namespace Saphir.Views.Apps;

/// <summary>
/// Crée le contenu d'une application (dans une fenêtre du bureau ou en plein écran sur téléphone).
/// Les applications pas encore développées affichent une page provisoire.
/// </summary>
public static class AppContentFactory
{
    public static Control Create(AppTileViewModel app) => app.Id switch
    {
        SaphirApps.MeoFic => new MeoFicView { DataContext = new MeoFicViewModel() },
        SaphirApps.Scientifique => new MeoFicView { DataContext = new MeoFicViewModel(MeoMode.Pts) },
        SaphirApps.Pegase => new PegaseView { DataContext = new PegaseViewModel() },
        _ => new PlaceholderAppView { DataContext = app }
    };
}
