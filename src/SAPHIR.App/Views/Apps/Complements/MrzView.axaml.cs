using Avalonia;
using Avalonia.Controls;

namespace Saphir.Views.Apps.Complements;

/// <summary>MRZ : lecture de documents.</summary>
public partial class MrzView : UserControl
{
    public MrzView()
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
