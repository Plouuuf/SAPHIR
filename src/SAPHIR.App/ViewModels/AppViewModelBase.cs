using Saphir.Services;

namespace Saphir.ViewModels;

/// <summary>Boîte de dialogue Windows 7 affichée par-dessus une application (confirmation, information).</summary>
public sealed class DialogViewModel : ViewModelBase
{
    private readonly TaskCompletionSource<bool> _result = new();

    public DialogViewModel(string title, string message, string confirmLabel, string? cancelLabel, bool danger)
    {
        Title = title;
        Message = message;
        ConfirmLabel = confirmLabel;
        CancelLabel = cancelLabel;
        IsDanger = danger;
        ConfirmCommand = new RelayCommand(() => _result.TrySetResult(true));
        CancelCommand = new RelayCommand(() => _result.TrySetResult(false));
    }

    public string Title { get; }
    public string Message { get; }
    public string ConfirmLabel { get; }
    public string? CancelLabel { get; }
    public bool HasCancel => CancelLabel is not null;
    public bool IsDanger { get; }
    public RelayCommand ConfirmCommand { get; }
    public RelayCommand CancelCommand { get; }

    public Task<bool> Result => _result.Task;
}

/// <summary>
/// Base des applications SAPHIR : chargement, message d'erreur explicite, message d'information,
/// confirmation avant les actions importantes.
/// </summary>
public abstract class AppViewModelBase : ViewModelBase
{
    private bool _isBusy;
    private string? _errorText;
    private string? _infoText;
    private DialogViewModel? _dialog;

    public bool IsBusy
    {
        get => _isBusy;
        protected set => SetProperty(ref _isBusy, value);
    }

    public string? ErrorText
    {
        get => _errorText;
        protected set
        {
            if (SetProperty(ref _errorText, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    public string? InfoText
    {
        get => _infoText;
        protected set
        {
            if (SetProperty(ref _infoText, value))
                OnPropertyChanged(nameof(HasInfo));
        }
    }

    public bool HasInfo => !string.IsNullOrEmpty(InfoText);

    /// <summary>Boîte de dialogue ouverte (null = aucune).</summary>
    public DialogViewModel? Dialog
    {
        get => _dialog;
        private set
        {
            if (SetProperty(ref _dialog, value))
                OnPropertyChanged(nameof(HasDialog));
        }
    }

    public bool HasDialog => Dialog is not null;

    public RelayCommand DismissMessagesCommand => new(() => { ErrorText = null; InfoText = null; });

    protected static ApiClient? Api => SaphirSession.Api;

    /// <summary>Demande une confirmation ; renvoie vrai si l'agent confirme.</summary>
    protected async Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "Confirmer", bool danger = false)
    {
        var dialog = new DialogViewModel(title, message, confirmLabel, "Annuler", danger);
        Dialog = dialog;
        try
        {
            return await dialog.Result;
        }
        finally
        {
            Dialog = null;
        }
    }

    /// <summary>Exécute une action en affichant l'attente, et transforme les erreurs en message lisible.</summary>
    protected async Task RunAsync(Func<Task> work, string? success = null)
    {
        IsBusy = true;
        ErrorText = null;
        InfoText = null;
        try
        {
            await work();
            if (success is not null)
                InfoText = success;
        }
        catch (ApiException ex)
        {
            ErrorText = ex.Message;
        }
        catch (ArgumentException ex)
        {
            ErrorText = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorText = "Erreur inattendue : " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Appelé quand la fenêtre de l'application se ferme ou disparaît.</summary>
    public virtual void Suspend()
    {
    }

    /// <summary>Appelé quand la fenêtre de l'application est de nouveau affichée.</summary>
    public virtual void Resume()
    {
    }
}
