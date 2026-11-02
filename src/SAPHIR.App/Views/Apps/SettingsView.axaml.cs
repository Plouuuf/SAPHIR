using Avalonia;
using Avalonia.Controls;
using Saphir.ViewModels;

namespace Saphir.Views.Apps;

/// <summary>Centre de paramètres.</summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
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
}
