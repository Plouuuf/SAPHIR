using Avalonia.Controls;
using Saphir.Shared.Contracts;
using Saphir.ViewModels;

namespace Saphir.Views.Apps.Complements;

/// <summary>Contenu des fenêtres : NEO, PVe, Tchap, Annuaire, Messagerie, MRZ, Notes, Bodycam.</summary>
public static class ComplementsAppFactory
{
    public static Control Create(AppTileViewModel app) => app.Definition.Id switch
    {
        _ => new PlaceholderAppView { DataContext = app },
    };
}
