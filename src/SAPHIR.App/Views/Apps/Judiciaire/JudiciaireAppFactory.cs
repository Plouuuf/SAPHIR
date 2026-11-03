using Avalonia.Controls;
using Saphir.Shared.Contracts;
using Saphir.ViewModels;
using Saphir.ViewModels.Apps.Judiciaire;

namespace Saphir.Views.Apps.Judiciaire;

/// <summary>Contenu des fenêtres : CHEOPS et LRPPN.</summary>
public static class JudiciaireAppFactory
{
    public static Control Create(AppTileViewModel app) => app.Definition.Id switch
    {
        SaphirApps.Cheops => new CheopsView { DataContext = new CheopsViewModel() },
        SaphirApps.Lrppn => new LrppnView { DataContext = new LrppnViewModel() },
        _ => new PlaceholderAppView { DataContext = app },
    };
}
