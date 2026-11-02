using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Saphir.ViewModels;

namespace Saphir.Views.Apps;

/// <summary>
/// PEGASE. Sur ordinateur : liste des événements à gauche, fiche à droite. Fenêtre étroite ou téléphone :
/// la liste, puis la fiche en plein écran avec « ← Événements ».
/// </summary>
public partial class PegaseView : UserControl
{
    private const double CompactWidth = 760;
    private PegaseViewModel? _vm;

    public PegaseView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm = DataContext as PegaseViewModel;
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
        // Fenêtre fermée : on arrête le chrono et l'écoute du temps réel.
        (DataContext as IDisposable)?.Dispose();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
            UpdateLayoutMode();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PegaseViewModel.HasSelection))
            UpdateLayoutMode();
    }

    private void UpdateLayoutMode()
    {
        if (EventsGrid is null)
            return;

        var compact = Bounds.Width > 0 && Bounds.Width < CompactWidth;
        var columns = EventsGrid.ColumnDefinitions;
        if (!compact)
        {
            columns[0].Width = new GridLength(360);
            columns[1].Width = new GridLength(1, GridUnitType.Star);
            EventListPane.IsVisible = true;
            EventDetailPane.IsVisible = true;
            BackButton.IsVisible = false;
            return;
        }

        var detail = _vm?.HasSelection == true;
        columns[0].Width = detail ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        columns[1].Width = detail ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        EventListPane.IsVisible = !detail;
        EventDetailPane.IsVisible = detail;
        BackButton.IsVisible = detail;
    }
}
