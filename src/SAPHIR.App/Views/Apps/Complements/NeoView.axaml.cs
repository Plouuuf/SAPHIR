using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Saphir.ViewModels.Apps.Complements;

namespace Saphir.Views.Apps.Complements;

/// <summary>NEO : accueil du terminal mobile et applications ouvertes en plein écran.</summary>
public partial class NeoView : UserControl
{
    private NeoViewModel? _vm;

    public NeoView()
    {
        InitializeComponent();
        VueComplements.Preparer(this);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        VueComplements.Attacher(this);
        _vm = DataContext as NeoViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            AfficherApplication();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = null;
        Contenu.Content = null;
        VueComplements.Detacher(this);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NeoViewModel.AppOuverte))
            AfficherApplication();
    }

    private void AfficherApplication()
        => Contenu.Content = _vm?.AppOuverte is { } app ? ComplementsAppFactory.Vue(app) : null;
}
