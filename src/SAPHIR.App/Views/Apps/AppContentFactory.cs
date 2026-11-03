using Avalonia.Controls;
using Saphir.Shared.Contracts;
using Saphir.ViewModels;
using Saphir.ViewModels.Apps;
using Saphir.Views.Apps.Complements;
using Saphir.Views.Apps.Judiciaire;
using Saphir.Views.Apps.Operations;
using Saphir.Views.Apps.Rh;

namespace Saphir.Views.Apps;

/// <summary>Crée le contenu d'une fenêtre d'application (un modèle de vue neuf par fenêtre).</summary>
public static class AppContentFactory
{
    public static Control Create(AppTileViewModel app) => app.Definition.Id switch
    {
        SaphirApps.Parametres => new SettingsView { DataContext = new SettingsViewModel() },
        SaphirApps.Administration => new AdministrationView { DataContext = new AdministrationViewModel() },
        SaphirApps.Rh => RhAppFactory.Create(app),
        SaphirApps.MainCourante or SaphirApps.Pegase => OperationsAppFactory.Create(app),
        SaphirApps.Cheops or SaphirApps.Lrppn => JudiciaireAppFactory.Create(app),
        _ => ComplementsAppFactory.Create(app),
    };
}
