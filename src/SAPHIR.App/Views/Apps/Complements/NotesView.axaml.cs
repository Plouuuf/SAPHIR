using Avalonia;
using Avalonia.Controls;

namespace Saphir.Views.Apps.Complements;

/// <summary>NOTES personnelles et partagées.</summary>
public partial class NotesView : UserControl
{
    public NotesView()
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
