using Avalonia.Controls;
using Avalonia.Input;
using Saphir.ViewModels.Apps.Rh;

namespace Saphir.Views.Apps.Rh;

/// <summary>Gestion du personnel : double-clic sur une ligne = dossier individuel.</summary>
public partial class RhPersonnelView : UserControl
{
    public RhPersonnelView()
    {
        InitializeComponent();
    }

    private void Grille_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is RhPersonnelViewModel vm && vm.HasSelection)
            vm.OuvrirDossierCommand.Execute(null);
    }
}
