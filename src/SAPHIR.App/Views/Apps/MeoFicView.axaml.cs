using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Saphir.ViewModels;

namespace Saphir.Views.Apps;

/// <summary>
/// MEO-FIC. Sur ordinateur : trois colonnes (fichiers, résultats, fiche). Fenêtre étroite ou téléphone :
/// onglets en haut, puis résultats ou fiche en plein écran avec un bouton « ← Résultats ».
/// </summary>
public partial class MeoFicView : UserControl
{
    private const double CompactWidth = 760;
    private MeoFicViewModel? _vm;

    public MeoFicView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm = DataContext as MeoFicViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        UpdateLayoutMode();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _vm?.Resume();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _vm?.Suspend();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
            UpdateLayoutMode();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MeoFicViewModel.ShowDetailOnCompact) or nameof(MeoFicViewModel.IsJournalMode)
            or nameof(MeoFicViewModel.IsTracesMode))
            UpdateLayoutMode();
    }

    private void UpdateLayoutMode()
    {
        if (BodyGrid is null)
            return;

        var compact = Bounds.Width > 0 && Bounds.Width < CompactWidth;
        var columns = BodyGrid.ColumnDefinitions;
        Sidebar.IsVisible = !compact;
        CompactTabs.IsVisible = compact;

        if (!compact)
        {
            columns[0].Width = new GridLength(200);
            columns[1].Width = new GridLength(300);
            columns[2].Width = new GridLength(1, GridUnitType.Star);
            ResultsPane.IsVisible = true;
            DetailPane.IsVisible = true;
            BackButton.IsVisible = false;
            return;
        }

        var journal = _vm?.IsJournalMode == true || _vm?.IsTracesMode == true;
        var showDetail = journal || _vm?.ShowDetailOnCompact == true;
        columns[0].Width = new GridLength(0);
        columns[1].Width = showDetail ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        columns[2].Width = showDetail ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        ResultsPane.IsVisible = !showDetail;
        DetailPane.IsVisible = showDetail;
        BackButton.IsVisible = showDetail && !journal;
    }
}
