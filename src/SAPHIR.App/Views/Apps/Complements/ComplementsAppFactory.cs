using Avalonia.Controls;
using Saphir.Shared.Contracts;
using Saphir.ViewModels;
using Saphir.ViewModels.Apps.Complements;

namespace Saphir.Views.Apps.Complements;

/// <summary>Contenu des fenêtres : NEO, PVe, Tchap, Annuaire, Messagerie, MRZ, Notes, Bodycam.</summary>
public static class ComplementsAppFactory
{
    public static Control Create(AppTileViewModel app) => app.Definition.Id switch
    {
        SaphirApps.Neo => new NeoView { DataContext = new NeoViewModel() },
        SaphirApps.Pve => new PveView { DataContext = new PveViewModel() },
        SaphirApps.Tchap => new TchapView { DataContext = new TchapViewModel() },
        SaphirApps.Annuaire => new AnnuaireView { DataContext = new AnnuaireViewModel() },
        SaphirApps.Messagerie => new MessagerieView { DataContext = new MessagerieViewModel() },
        SaphirApps.Mrz => new MrzView { DataContext = new MrzViewModel() },
        SaphirApps.Notes => new NotesView { DataContext = new NotesViewModel() },
        SaphirApps.Bodycam => new BodycamView { DataContext = new BodycamViewModel() },
        _ => new PlaceholderAppView { DataContext = app },
    };

    /// <summary>Vue d'un modèle d'application complémentaire (applications ouvertes dans NEO).</summary>
    public static Control Vue(ComplementsViewModelBase vm) => vm switch
    {
        PveViewModel => new PveView { DataContext = vm },
        TchapViewModel => new TchapView { DataContext = vm },
        AnnuaireViewModel => new AnnuaireView { DataContext = vm },
        MessagerieViewModel => new MessagerieView { DataContext = vm },
        MrzViewModel => new MrzView { DataContext = vm },
        NotesViewModel => new NotesView { DataContext = vm },
        BodycamViewModel => new BodycamView { DataContext = vm },
        NeoViewModel => new NeoView { DataContext = vm },
        _ => new TextBlock { Text = "Application indisponible.", Margin = new Avalonia.Thickness(16) }
    };
}
