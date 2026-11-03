using Avalonia.Controls;
using Saphir.Shared.Contracts;
using Saphir.ViewModels;

namespace Saphir.Views.Apps.Judiciaire;

/// <summary>Contenu des fenêtres : CHEOPS et LRPPN.</summary>
public static class JudiciaireAppFactory
{
    public static Control Create(AppTileViewModel app) => app.Definition.Id switch
    {
        _ => new PlaceholderAppView { DataContext = app },
    };
}
