using Avalonia;
using Avalonia.Controls;

namespace Saphir.Views.Apps.Judiciaire;

/// <summary>LRPPN.</summary>
public partial class LrppnView : UserControl
{
    public LrppnView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        JudiciaireVues.Attacher(this);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        JudiciaireVues.Detacher(this);
    }
}
