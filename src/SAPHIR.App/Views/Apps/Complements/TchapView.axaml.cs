using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Saphir.ViewModels.Apps.Complements;

namespace Saphir.Views.Apps.Complements;

/// <summary>TCHAP : messagerie instantanée par salons.</summary>
public partial class TchapView : UserControl
{
    private TchapViewModel? _vm;

    public TchapView()
    {
        InitializeComponent();
        VueComplements.Preparer(this);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        VueComplements.Attacher(this);
        _vm = DataContext as TchapViewModel;
        if (_vm is not null)
            _vm.DefilerEnBas += Defiler;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_vm is not null)
            _vm.DefilerEnBas -= Defiler;
        _vm = null;
        VueComplements.Detacher(this);
    }

    private void Defiler()
        => Dispatcher.UIThread.Post(() => Defilement.ScrollToEnd(), DispatcherPriority.Background);
}
