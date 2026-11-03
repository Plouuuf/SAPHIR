using Avalonia;
using Avalonia.Controls;

namespace Saphir.Views.Apps.Complements;

/// <summary>MESSAGERIE interne.</summary>
public partial class MessagerieView : UserControl
{
    public MessagerieView()
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
