using Avalonia;
using Avalonia.Controls;
using Saphir.ViewModels;
using Saphir.ViewModels.Apps.Operations;

namespace Saphir.Views.Apps.Operations;

/// <summary>Fenêtre MAIN COURANTE ou PÉGASE (menu latéral + écran choisi).</summary>
public partial class OperationsAppView : UserControl
{
    /// <summary>En dessous de cette largeur, le menu latéral devient une liste déroulante (téléphone).</summary>
    private const double LargeurCompacte = 760;

    public OperationsAppView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as AppViewModelBase)?.Resume();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as AppViewModelBase)?.Suspend();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty && DataContext is OperationsHostViewModel host && Bounds.Width > 0)
            host.EstCompact = Bounds.Width < LargeurCompacte;
    }
}
