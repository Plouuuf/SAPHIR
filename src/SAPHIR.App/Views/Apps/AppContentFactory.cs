using Avalonia.Controls;
using Saphir.Shared.Contracts;
using Saphir.ViewModels;
using Saphir.ViewModels.Apps;

namespace Saphir.Views.Apps;

/// <summary>Crée le contenu d'une fenêtre d'application (un modèle de vue neuf par fenêtre).</summary>
public static class AppContentFactory
{
    public static Control Create(AppTileViewModel app) => app.Definition.Id switch
    {
        SaphirApps.Parametres => new SettingsView { DataContext = new SettingsViewModel() },
        SaphirApps.Administration => new AdministrationView { DataContext = new AdministrationViewModel() },
        _ => new PlaceholderAppView { DataContext = app },
    };
}
