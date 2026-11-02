using System.Collections.ObjectModel;
using System.Globalization;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Saphir.ViewModels;

/// <summary>
/// Bureau Windows 7 (ordinateur, iPad) et écran d'accueil (téléphone) : icônes selon les permissions,
/// fenêtres, barre des tâches, menu Démarrer, zone de notification et bulles de notification.
/// </summary>
public sealed class DesktopViewModel : ViewModelBase
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private const double DefaultWidth = 1040;
    private const double DefaultHeight = 680;

    private readonly Action _lock;
    private readonly Action _signOut;
    private int _nextZ = 1;
    private int _cascade;

    private bool _isStartMenuOpen;
    private bool _isAllProgramsOpen;
    private bool _isOnline;
    private string _trayTime = string.Empty;
    private string _trayDate = string.Empty;
    private string _longDate = string.Empty;
    private AppTileViewModel? _mobileApp;
    private Bitmap? _avatar;
    private string _searchText = string.Empty;

    public DesktopViewModel(Bitmap? avatar, Action lockSession, Action signOut)
    {
        _lock = lockSession;
        _signOut = signOut;
        _avatar = avatar;

        ToggleStartMenuCommand = new RelayCommand(() =>
        {
            IsStartMenuOpen = !IsStartMenuOpen;
            IsAllProgramsOpen = false;
            SearchText = string.Empty;
        });
        CloseStartMenuCommand = new RelayCommand(() => IsStartMenuOpen = false);
        ToggleAllProgramsCommand = new RelayCommand(() => IsAllProgramsOpen = !IsAllProgramsOpen);
        LockCommand = new RelayCommand(() => { IsStartMenuOpen = false; _lock(); });
        SignOutCommand = new RelayCommand(() => { IsStartMenuOpen = false; _signOut(); });
        ShowDesktopCommand = new RelayCommand(ShowDesktop);
        MobileBackCommand = new RelayCommand(() => MobileApp = null);
        RefreshCommand = new RelayCommand(() => { ApplySession(); SelectIcon(null); });
        OpenSettingsCommand = new RelayCommand(() => OpenById(SaphirApps.Parametres));
        OpenAdministrationCommand = new RelayCommand(() => OpenById(SaphirApps.Administration));

        ApplySession();
        UpdateClock(DateTime.Now);
    }

    // ===== Applications =====

    /// <summary>Icônes du bureau : les applications que l'agent peut ouvrir.</summary>
    public ObservableCollection<AppTileViewModel> Apps { get; } = [];

    /// <summary>Menu Démarrer, colonne de gauche : modules principaux.</summary>
    public ObservableCollection<AppTileViewModel> StartMain { get; } = [];

    /// <summary>Menu Démarrer, « Tous les programmes » : applications complémentaires.</summary>
    public ObservableCollection<AppTileViewModel> StartOther { get; } = [];

    public ObservableCollection<AppWindowViewModel> Windows { get; } = [];
    public ObservableCollection<ToastViewModel> Toasts { get; } = [];

    // ===== Agent connecté =====

    public string AgentName => SaphirSession.Agent?.NomComplet ?? SaphirSession.User?.DisplayName ?? "SAPHIR";

    public string AgentLine
    {
        get
        {
            var agent = SaphirSession.Agent;
            if (agent is null) return string.Empty;
            var parts = new[] { agent.Grade, agent.Service, agent.Rio is { } rio ? "RIO " + rio : null }
                .Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            return parts.Count > 0 ? string.Join(" · ", parts) : agent.Statut;
        }
    }

    public string AgentReference => SaphirSession.Agent?.Reference ?? string.Empty;
    public string Version => SaphirSession.Version;
    public bool CanAdminister => SaphirSession.CanOpen(SaphirApps.Administration);

    public Bitmap? Avatar
    {
        get => _avatar;
        private set { if (SetProperty(ref _avatar, value)) OnPropertyChanged(nameof(HasAvatar)); }
    }

    public bool HasAvatar => Avatar is not null;

    /// <summary>Fond d'écran du bureau.</summary>
    public Bitmap Wallpaper => ImageLoader.Asset("Wallpapers/landscape.jpg");

    /// <summary>Recherche du menu Démarrer : filtre les programmes par nom ou description.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            if (!string.IsNullOrWhiteSpace(value))
                IsAllProgramsOpen = false;
            RebuildStartMenu();
        }
    }

    // ===== Barre des tâches, menu Démarrer =====

    public bool IsStartMenuOpen { get => _isStartMenuOpen; set => SetProperty(ref _isStartMenuOpen, value); }

    public bool IsAllProgramsOpen
    {
        get => _isAllProgramsOpen;
        set
        {
            if (SetProperty(ref _isAllProgramsOpen, value))
                OnPropertyChanged(nameof(AllProgramsLabel));
        }
    }

    public string AllProgramsLabel => IsAllProgramsOpen ? "Précédent" : "Tous les programmes";
    public bool HasOtherPrograms => StartOther.Count > 0;
    public bool HasMainPrograms => StartMain.Count > 0;

    public bool IsOnline
    {
        get => _isOnline;
        set { if (SetProperty(ref _isOnline, value)) OnPropertyChanged(nameof(NetworkTooltip)); }
    }

    public string NetworkTooltip => IsOnline ? "Serveur SAPHIR : connecté" : "Serveur SAPHIR : connexion en cours…";

    public string TrayTime { get => _trayTime; private set => SetProperty(ref _trayTime, value); }
    public string TrayDate { get => _trayDate; private set => SetProperty(ref _trayDate, value); }
    public string LongDate { get => _longDate; private set => SetProperty(ref _longDate, value); }

    /// <summary>Application ouverte en plein écran sur téléphone (null = écran d'accueil).</summary>
    public AppTileViewModel? MobileApp
    {
        get => _mobileApp;
        private set { if (SetProperty(ref _mobileApp, value)) OnPropertyChanged(nameof(IsMobileAppOpen)); }
    }

    public bool IsMobileAppOpen => MobileApp is not null;

    public RelayCommand ToggleStartMenuCommand { get; }
    public RelayCommand CloseStartMenuCommand { get; }
    public RelayCommand ToggleAllProgramsCommand { get; }
    public RelayCommand LockCommand { get; }
    public RelayCommand SignOutCommand { get; }
    public RelayCommand ShowDesktopCommand { get; }
    public RelayCommand MobileBackCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand OpenAdministrationCommand { get; }

    /// <summary>Taille de la zone des fenêtres, fournie par la vue.</summary>
    public double WorkAreaWidth { get; set; } = 1280;
    public double WorkAreaHeight { get; set; } = 760;

    /// <summary>Vrai quand le poste affiche l'interface téléphone.</summary>
    public bool IsCompactLayout { get; set; }

    // ===== Session =====

    /// <summary>
    /// Icônes, menu Démarrer et identité selon la session SAPHIR. Relu à chaque changement de profils :
    /// une application retirée disparaît et sa fenêtre se ferme.
    /// </summary>
    public void ApplySession()
    {
        var allowed = AppCatalog.All.Where(d => SaphirSession.CanOpen(d.Id)).ToList();
        if (!allowed.Select(d => d.Id).SequenceEqual(Apps.Select(a => a.Id)))
        {
            foreach (var window in Windows.Where(w => allowed.All(d => d.Id != w.App.Id)).ToList())
                Close(window);
            if (MobileApp is not null && allowed.All(d => d.Id != MobileApp.Id))
                MobileApp = null;

            var existing = Apps.ToDictionary(a => a.Id);
            Apps.Clear();
            foreach (var definition in allowed)
                Apps.Add(existing.TryGetValue(definition.Id, out var tile) ? tile : new AppTileViewModel(definition, OpenApp));
        }

        RebuildStartMenu();

        foreach (var name in new[]
                 {
                     nameof(AgentName), nameof(AgentLine), nameof(AgentReference), nameof(Version), nameof(CanAdminister)
                 })
            OnPropertyChanged(name);
    }

    /// <summary>
    /// Menu Démarrer. Sans recherche : modules principaux à gauche, applications complémentaires dans
    /// « Tous les programmes ». Avec recherche : tous les programmes qui correspondent, dans la liste principale.
    /// </summary>
    private void RebuildStartMenu()
    {
        StartMain.Clear();
        StartOther.Clear();
        var query = SearchText.Trim();
        foreach (var app in Apps)
        {
            if (query.Length > 0)
            {
                if (app.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                    app.Subtitle.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                    StartMain.Add(app);
                continue;
            }
            if (app.Definition.Principale)
                StartMain.Add(app);
            else if (app.Id is not (SaphirApps.Parametres or SaphirApps.Administration))
                StartOther.Add(app);
        }
        OnPropertyChanged(nameof(HasOtherPrograms));
        OnPropertyChanged(nameof(HasMainPrograms));
    }

    // ===== Fenêtres =====

    public void OpenById(string appId)
    {
        var tile = Apps.FirstOrDefault(a => a.Id == appId);
        if (tile is not null)
            OpenApp(tile);
    }

    public void OpenApp(AppTileViewModel app)
    {
        IsStartMenuOpen = false;
        SelectIcon(null);

        if (IsCompactLayout)
        {
            MobileApp = app;
            return;
        }

        var existing = Windows.FirstOrDefault(w => w.App == app);
        if (existing is not null)
        {
            Activate(existing);
            return;
        }

        var width = Math.Min(DefaultWidth, Math.Max(520, WorkAreaWidth - 80));
        var height = Math.Min(DefaultHeight, Math.Max(380, WorkAreaHeight - 60));
        var offset = 26 * (_cascade++ % 8);
        var x = Math.Max(0, (WorkAreaWidth - width) / 2 - 100 + offset);
        var y = Math.Max(0, (WorkAreaHeight - height) / 2 - 60 + offset);

        var window = new AppWindowViewModel(app, x, y, width, height, this);
        Windows.Add(window);
        Activate(window);
    }

    public void Activate(AppWindowViewModel window)
    {
        window.IsMinimized = false;
        window.ZIndex = ++_nextZ;
        foreach (var w in Windows)
            w.IsActive = ReferenceEquals(w, window);
    }

    public void Minimize(AppWindowViewModel window)
    {
        window.IsMinimized = true;
        window.IsActive = false;
        ActivateTopmost();
    }

    public void Close(AppWindowViewModel window)
    {
        Windows.Remove(window);
        if (window.IsActive)
            ActivateTopmost();
    }

    public void OnTaskbarClick(AppWindowViewModel window)
    {
        IsStartMenuOpen = false;
        if (window.IsActive && !window.IsMinimized)
            Minimize(window);
        else
            Activate(window);
    }

    public void SelectIcon(AppTileViewModel? app)
    {
        foreach (var a in Apps)
            a.IsSelected = ReferenceEquals(a, app);
    }

    public void OnDesktopBackgroundPressed()
    {
        IsStartMenuOpen = false;
        SelectIcon(null);
        foreach (var w in Windows)
            w.IsActive = false;
    }

    private void ShowDesktop()
    {
        IsStartMenuOpen = false;
        foreach (var w in Windows)
        {
            w.IsMinimized = true;
            w.IsActive = false;
        }
    }

    private void ActivateTopmost()
    {
        var topmost = Windows.Where(w => !w.IsMinimized).MaxBy(w => w.ZIndex);
        foreach (var w in Windows)
            w.IsActive = ReferenceEquals(w, topmost);
    }

    // ===== Horloge, notifications =====

    public void UpdateClock(DateTime now)
    {
        TrayTime = now.ToString("HH:mm", French);
        TrayDate = now.ToString("dd/MM/yyyy", French);
        LongDate = now.ToString("dddd d MMMM yyyy", French);
    }

    public void HandleRealtime(RealtimeMessage message)
    {
        if (message.Type != RealtimeTypes.Notify)
            return;
        var app = message.AppId is null ? null : Apps.FirstOrDefault(a => a.Id == message.AppId);
        // Une notification d'application n'est montrée qu'aux agents qui y ont accès.
        if (message.AppId is null || app is not null)
            ShowNotification(message.Title ?? "SAPHIR", message.Text ?? string.Empty, app?.Icon);
    }

    /// <summary>Bulle de notification au-dessus de la zone de notification (8 secondes).</summary>
    public void ShowNotification(string title, string text, Bitmap? icon = null)
    {
        var toast = new ToastViewModel(title, text, icon, t => Toasts.Remove(t));
        Toasts.Add(toast);
        while (Toasts.Count > 3)
            Toasts.RemoveAt(0);
        DispatcherTimer.RunOnce(() => Toasts.Remove(toast), TimeSpan.FromSeconds(8));
    }
}
