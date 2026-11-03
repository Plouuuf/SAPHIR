using Avalonia;
using Avalonia.Controls;

namespace Saphir.Views.Apps.Judiciaire;

/// <summary>CHEOPS.</summary>
public partial class CheopsView : UserControl
{
    public CheopsView()
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
