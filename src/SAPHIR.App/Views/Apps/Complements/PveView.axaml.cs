using Avalonia;
using Avalonia.Controls;

namespace Saphir.Views.Apps.Complements;

/// <summary>PVe — procès-verbal électronique.</summary>
public partial class PveView : UserControl
{
    public PveView()
    {
        InitializeComponent();
        VueComplements.Preparer(this);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        VueComplements.Attacher(this);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        VueComplements.Detacher(this);
    }
}
