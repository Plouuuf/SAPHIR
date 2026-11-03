using Avalonia;
using Avalonia.Controls;

namespace Saphir.Views.Apps.Complements;

/// <summary>BODYCAM : registre des enregistrements.</summary>
public partial class BodycamView : UserControl
{
    public BodycamView()
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
