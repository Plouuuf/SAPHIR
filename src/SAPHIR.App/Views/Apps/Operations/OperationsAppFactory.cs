using Avalonia.Controls;
using Saphir.Shared.Contracts;
using Saphir.ViewModels;
using Saphir.ViewModels.Apps.Operations;

namespace Saphir.Views.Apps.Operations;

/// <summary>Contenu des fenêtres : MAIN COURANTE et PÉGASE.</summary>
public static class OperationsAppFactory
{
    public static Control Create(AppTileViewModel app) => app.Definition.Id switch
    {
        SaphirApps.MainCourante => new OperationsAppView { DataContext = new MainCouranteViewModel() },
        SaphirApps.Pegase => new OperationsAppView { DataContext = new PegaseViewModel() },
        _ => new PlaceholderAppView { DataContext = app },
    };
}
