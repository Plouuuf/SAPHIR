using System.Net.Http.Json;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Saphir.ViewModels;

public enum ShellScreen
{
    Logon,
    FirstLogin,
    Welcome,
    Desktop
}

/// <summary>
/// Enchaînement des écrans, comme sous Windows 7 : ouverture de session → (première connexion) → « Bienvenue » → bureau.
/// La connexion passe par Discord : le serveur exige le rôle SAPHIR, puis la session SAPHIR (agent, profils,
/// permissions) décide de ce que le bureau affiche.
/// </summary>
public sealed class ShellViewModel : ViewModelBase
{
    private static readonly TimeSpan RevalidationInterval = TimeSpan.FromMinutes(5);

    private readonly DiscordAuthService _auth = new();
    private readonly RealtimeClient _realtime = new();
    private readonly DispatcherTimer _clock;
    private readonly DispatcherTimer _revalidation;

    private CancellationTokenSource? _signInCts;
    private ShellScreen _screen = ShellScreen.Logon;
    private bool _isCompact;
    private bool _isPortrait;
    private string _displayName = "SAPHIR";
    private string _subtitle = "Police nationale";
    private Bitmap? _avatar;
    private bool _hasKnownAccount;
    private bool _isBusy;
    private string? _statusText;
    private string? _errorText;
    private string _version = "SAPHIR";
    private string _firstNom = string.Empty;
    private string _firstPrenom = string.Empty;
    private string? _firstError;
    private DesktopViewModel? _desktop;

    public ShellViewModel()
    {
        SignInCommand = new RelayCommand(() => _ = SignInAsync(), () => !IsBusy);
        CancelSignInCommand = new RelayCommand(() => _signInCts?.Cancel());
        UseOtherAccountCommand = new RelayCommand(() => _ = UseOtherAccountAsync(), () => !IsBusy);
        SubmitFirstLoginCommand = new RelayCommand(() => _ = SubmitFirstLoginAsync(), () => !IsBusy);
        CancelFirstLoginCommand = new RelayCommand(() => _ = SignOutAsync());
        QuitCommand = new RelayCommand(() => App.Quit());

        _realtime.ConnectionChanged += connected => Dispatcher.UIThread.Post(() =>
        {
            if (Desktop is not null) Desktop.IsOnline = connected;
        });
        _realtime.MessageReceived += message => Dispatcher.UIThread.Post(() => OnRealtime(message));
        _realtime.SessionDenied += reason => Dispatcher.UIThread.Post(() => _ = ForceSignOutAsync(reason));

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => Desktop?.UpdateClock(DateTime.Now);
        _clock.Start();

        _revalidation = new DispatcherTimer { Interval = RevalidationInterval };
        _revalidation.Tick += (_, _) => _ = RevalidateAsync();

        _ = InitializeAsync();
    }

    // ===== Écrans =====

    public ShellScreen Screen
    {
        get => _screen;
        private set
        {
            if (!SetProperty(ref _screen, value)) return;
            OnPropertyChanged(nameof(IsLogonScreen));
            OnPropertyChanged(nameof(IsFirstLoginScreen));
            OnPropertyChanged(nameof(IsWelcomeScreen));
            OnPropertyChanged(nameof(IsDesktopScreen));
            OnPropertyChanged(nameof(ShowLogonBackground));
            OnPropertyChanged(nameof(ShowDesktopLayout));
            OnPropertyChanged(nameof(ShowMobileLayout));
        }
    }

    public bool IsLogonScreen => Screen == ShellScreen.Logon;
    public bool IsFirstLoginScreen => Screen == ShellScreen.FirstLogin;
    public bool IsWelcomeScreen => Screen == ShellScreen.Welcome;
    public bool IsDesktopScreen => Screen == ShellScreen.Desktop;

    /// <summary>Fond bleu de Windows 7 : avant le bureau, et derrière l'accueil du téléphone.</summary>
    public bool ShowLogonBackground => Screen != ShellScreen.Desktop || IsCompact;

    public bool IsCompact
    {
        get => _isCompact;
        set
        {
            if (!SetProperty(ref _isCompact, value)) return;
            OnPropertyChanged(nameof(ShowLogonBackground));
            OnPropertyChanged(nameof(ShowDesktopLayout));
            OnPropertyChanged(nameof(ShowMobileLayout));
        }
    }

    public bool IsPortrait
    {
        get => _isPortrait;
        set
        {
            if (!SetProperty(ref _isPortrait, value)) return;
            OnPropertyChanged(nameof(LogonBackground));
        }
    }

    public bool ShowDesktopLayout => IsDesktopScreen && !IsCompact;
    public bool ShowMobileLayout => IsDesktopScreen && IsCompact;

    public Bitmap LogonBackground => ImageLoader.Asset(IsPortrait ? "Wallpapers/logon-portrait.jpg" : "Wallpapers/logon.jpg");

    public bool CanQuit => !AppPaths.IsMobile;

    /// <summary>« SAPHIR - v0.2.0.0 », lu sur le serveur.</summary>
    public string Version
    {
        get => _version;
        private set => SetProperty(ref _version, value);
    }

    // ===== Ouverture de session =====

    public string DisplayName { get => _displayName; private set => SetProperty(ref _displayName, value); }
    public string Subtitle { get => _subtitle; private set => SetProperty(ref _subtitle, value); }

    public Bitmap? Avatar
    {
        get => _avatar;
        private set
        {
            if (SetProperty(ref _avatar, value))
                OnPropertyChanged(nameof(HasAvatar));
        }
    }

    public bool HasAvatar => Avatar is not null;

    public bool HasKnownAccount
    {
        get => _hasKnownAccount;
        private set
        {
            if (SetProperty(ref _hasKnownAccount, value))
                OnPropertyChanged(nameof(SignInLabel));
        }
    }

    public string SignInLabel => HasKnownAccount ? "Ouvrir la session" : "Se connecter avec Discord";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            SignInCommand.RaiseCanExecuteChanged();
            UseOtherAccountCommand.RaiseCanExecuteChanged();
            SubmitFirstLoginCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(IsIdle));
        }
    }

    public bool IsIdle => !IsBusy;

    public string? StatusText
    {
        get => _statusText;
        private set { if (SetProperty(ref _statusText, value)) OnPropertyChanged(nameof(HasStatus)); }
    }

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);

    public string? ErrorText
    {
        get => _errorText;
        private set { if (SetProperty(ref _errorText, value)) OnPropertyChanged(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    // ===== Première connexion =====

    public string FirstNom
    {
        get => _firstNom;
        set { if (SetProperty(ref _firstNom, value)) OnPropertyChanged(nameof(FirstPreview)); }
    }

    public string FirstPrenom
    {
        get => _firstPrenom;
        set { if (SetProperty(ref _firstPrenom, value)) OnPropertyChanged(nameof(FirstPreview)); }
    }

    /// <summary>Aperçu de l'identité telle qu'elle sera enregistrée (NOM Prénom).</summary>
    public string FirstPreview
    {
        get
        {
            var preview = $"{NomsFormat.Nom(FirstNom)} {NomsFormat.Prenom(FirstPrenom)}".Trim();
            return preview.Length == 0 ? "—" : preview;
        }
    }

    public string? FirstError
    {
        get => _firstError;
        private set { if (SetProperty(ref _firstError, value)) OnPropertyChanged(nameof(HasFirstError)); }
    }

    public bool HasFirstError => !string.IsNullOrWhiteSpace(FirstError);

    public DesktopViewModel? Desktop
    {
        get => _desktop;
        private set => SetProperty(ref _desktop, value);
    }

    public RelayCommand SignInCommand { get; }
    public RelayCommand CancelSignInCommand { get; }
    public RelayCommand UseOtherAccountCommand { get; }
    public RelayCommand SubmitFirstLoginCommand { get; }
    public RelayCommand CancelFirstLoginCommand { get; }
    public RelayCommand QuitCommand { get; }

    /// <summary>Ouverture d'une adresse dans le navigateur, fournie par la vue (TopLevel.Launcher).</summary>
    public Func<Uri, Task<bool>>? OpenUrl { get; set; }

    // ===== Déroulé =====

    private async Task InitializeAsync()
    {
        _ = LoadVersionAsync();

        var saved = _auth.PeekSavedUser();
        if (saved is not null)
            await ApplyUserAsync(saved);

        try
        {
            await _auth.TryResumeAsync();
            if (_auth.User is not null)
                await ApplyUserAsync(_auth.User);
            else
                ResetIdentity();
        }
        catch
        {
            // Serveur injoignable : on réessaiera à l'ouverture de session.
        }
    }

    private async Task LoadVersionAsync()
    {
        try
        {
            using var http = new HttpClient { BaseAddress = ClientSettings.Current.HttpBaseUri, Timeout = TimeSpan.FromSeconds(8) };
            var result = await http.GetFromJsonAsync<ServerVersionResponse>("api/version");
            if (!string.IsNullOrWhiteSpace(result?.Version))
                Version = result.Version;
        }
        catch
        {
            // Version inconnue tant que le serveur ne répond pas.
        }
    }

    private async Task SignInAsync()
    {
        if (IsBusy) return;
        ErrorText = null;
        IsBusy = true;
        _signInCts = new CancellationTokenSource();
        var ct = _signInCts.Token;

        try
        {
            var ready = false;
            if (HasKnownAccount)
            {
                StatusText = "Vérification du compte Discord…";
                ready = await _auth.TryResumeAsync(ct) && _auth.User is not null;
                if (!ready)
                    ResetIdentity();
            }

            if (!ready)
            {
                StatusText = "Connexion au serveur SAPHIR…";
                var start = await _auth.StartAsync(ct);
                var opened = OpenUrl is not null && await OpenUrl(new Uri(start.AuthorizeUrl));
                if (!opened)
                    throw new InvalidOperationException("Impossible d'ouvrir le navigateur pour la connexion Discord.");

                StatusText = "Valide la connexion dans ton navigateur…";
                var result = await _auth.WaitForApprovalAsync(start.SessionId, ct);
                if (result.Status != "approved" || _auth.User is null)
                {
                    ErrorText = result.Error ?? "La connexion Discord n'a pas abouti.";
                    return;
                }
            }

            await ApplyUserAsync(_auth.User!);
            StatusText = "Ouverture de la session SAPHIR…";
            SaphirSession.Open(_auth.SessionToken!, _auth.User!);
            var info = await SaphirSession.Api!.GetAsync<SessionInfoDto>("api/session", ct);
            await ContinueWithSessionAsync(info);
        }
        catch (OperationCanceledException)
        {
            ErrorText = null;
        }
        catch (HttpRequestException)
        {
            ErrorText = "Le serveur SAPHIR est injoignable. Vérifie ta connexion internet.";
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            StatusText = null;
            IsBusy = false;
            _signInCts?.Dispose();
            _signInCts = null;
        }
    }

    private async Task ContinueWithSessionAsync(SessionInfoDto info)
    {
        Version = info.Version;
        if (!string.IsNullOrWhiteSpace(info.Refus))
        {
            SaphirSession.Close();
            Screen = ShellScreen.Logon;
            ErrorText = info.Refus;
            return;
        }

        if (info.PremiereConnexion)
        {
            FirstNom = string.Empty;
            FirstPrenom = string.Empty;
            FirstError = null;
            Screen = ShellScreen.FirstLogin;
            return;
        }

        await EnterDesktopAsync(info);
    }

    private async Task SubmitFirstLoginAsync()
    {
        var api = SaphirSession.Api;
        if (api is null || IsBusy) return;
        if (!NomsFormat.EstValide(FirstNom) || !NomsFormat.EstValide(FirstPrenom))
        {
            FirstError = "Renseigne ton NOM et ton prénom (lettres, espaces, traits d'union ; 2 caractères au moins).";
            return;
        }

        FirstError = null;
        IsBusy = true;
        try
        {
            var info = await api.PostAsync<SessionInfoDto>("api/session/premiere-connexion",
                new PremiereConnexionInput(NomsFormat.Nom(FirstNom), NomsFormat.Prenom(FirstPrenom)));
            await ContinueWithSessionAsync(info);
        }
        catch (ApiException ex)
        {
            FirstError = ex.Message;
        }
        catch (Exception ex)
        {
            FirstError = "Erreur inattendue : " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task EnterDesktopAsync(SessionInfoDto info)
    {
        SaphirSession.SetInfo(info);
        if (info.Agent is { } agent)
        {
            DisplayName = agent.NomComplet;
            Subtitle = agent.Grade ?? agent.Statut;
        }

        Screen = ShellScreen.Welcome;
        Desktop = new DesktopViewModel(Avatar, LockSession, () => _ = SignOutAsync());
        Desktop.IsCompactLayout = IsCompact;
        Desktop.IsOnline = _realtime.IsConnected;
        _realtime.Start(_auth.SessionToken!);
        _revalidation.Start();

        // Même durée que « Bienvenue » sous Windows 7.
        await Task.Delay(TimeSpan.FromSeconds(1.8));
        if (Screen == ShellScreen.Welcome)
            Screen = ShellScreen.Desktop;
    }

    /// <summary>Verrouiller : retour à l'ouverture de session, le compte reste enregistré sur l'appareil.</summary>
    private void LockSession() => _ = SignOutAsync();

    private async Task SignOutAsync()
    {
        _revalidation.Stop();
        await _realtime.StopAsync();
        SaphirSession.Close();
        Desktop = null;
        ErrorText = null;
        Screen = ShellScreen.Logon;
    }

    private async Task UseOtherAccountAsync()
    {
        await _auth.ForgetAsync();
        ResetIdentity();
        await SignInAsync();
    }

    /// <summary>Le serveur a refusé la session (rôle retiré, compte suspendu, session expirée).</summary>
    private async Task ForceSignOutAsync(string reason)
    {
        await SignOutAsync();
        await _auth.ForgetAsync();
        ResetIdentity();
        ErrorText = reason;
    }

    private void OnRealtime(RealtimeMessage message)
    {
        if (message.Type is RealtimeTypes.SessionChanged or RealtimeTypes.ParametresChanged)
            _ = ReloadSessionAsync(message.Type == RealtimeTypes.SessionChanged);
        Desktop?.HandleRealtime(message);
        SaphirSession.Publish(message);
    }

    /// <summary>Profils, statut ou paramètres changés : relit la session et met le bureau à jour.</summary>
    private async Task ReloadSessionAsync(bool announce)
    {
        var api = SaphirSession.Api;
        if (api is null || Desktop is null) return;
        try
        {
            var info = await api.GetAsync<SessionInfoDto>("api/session");
            if (!string.IsNullOrWhiteSpace(info.Refus))
            {
                await ForceSignOutAsync(info.Refus);
                return;
            }
            Version = info.Version;
            SaphirSession.SetInfo(info);
            Desktop?.ApplySession();
            if (announce)
                Desktop?.ShowNotification("Habilitations mises à jour", "Tes profils SAPHIR ont changé : le bureau a été actualisé.");
        }
        catch
        {
            // Rechargement impossible pour l'instant : la revérification périodique s'en chargera.
        }
    }

    private async Task RevalidateAsync()
    {
        if (Screen != ShellScreen.Desktop && Screen != ShellScreen.Welcome) return;
        try
        {
            var result = await _auth.RevalidateAsync();
            if (result.Valid && result.User is not null)
            {
                SaphirSession.Update(result.User);
                await ReloadSessionAsync(false);
            }
            else if (result.Reauthenticate)
            {
                await ForceSignOutAsync(result.Error ?? "Ta session SAPHIR a pris fin.");
            }
        }
        catch
        {
            // Serveur momentanément injoignable : la session reste ouverte.
        }
    }

    private async Task ApplyUserAsync(SaphirUserDto user)
    {
        HasKnownAccount = true;
        DisplayName = user.DisplayName;
        Subtitle = "Police nationale";
        Avatar = await ImageLoader.AvatarAsync(user.AvatarUrl) ?? Avatar;
    }

    /// <summary>Outil de captures d'écran : affiche un écran avec une identité d'exemple, sans Discord.</summary>
    internal void ShowForCapture(ShellScreen screen, string displayName, string version, string nom = "", string prenom = "")
    {
        HasKnownAccount = true;
        DisplayName = displayName;
        Subtitle = "Police nationale";
        Version = version;
        FirstNom = nom;
        FirstPrenom = prenom;
        Screen = screen;
    }

    private void ResetIdentity()
    {
        HasKnownAccount = false;
        DisplayName = "SAPHIR";
        Subtitle = "Police nationale";
        Avatar = null;
    }

}

/// <summary>Réponse de GET /api/version.</summary>
public sealed record ServerVersionResponse(string Version);
