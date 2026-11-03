using Avalonia.Controls;
using Saphir.Shared.Contracts;
using Saphir.ViewModels;
using Saphir.ViewModels.Apps.Rh;

namespace Saphir.Views.Apps.Rh;

/// <summary>Contenu des fenêtres : RESSOURCES HUMAINES.</summary>
public static class RhAppFactory
{
    public static Control Create(AppTileViewModel app) => app.Definition.Id switch
    {
        SaphirApps.Rh => new RhView { DataContext = new RhViewModel() },
        _ => new PlaceholderAppView { DataContext = app },
    };
}
