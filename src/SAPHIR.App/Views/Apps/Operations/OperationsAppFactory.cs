using Avalonia.Controls;
using Saphir.Shared.Contracts;
using Saphir.ViewModels;

namespace Saphir.Views.Apps.Operations;

/// <summary>Contenu des fenêtres : MAIN COURANTE et PÉGASE.</summary>
public static class OperationsAppFactory
{
    public static Control Create(AppTileViewModel app) => app.Definition.Id switch
    {
        _ => new PlaceholderAppView { DataContext = app },
    };
}
