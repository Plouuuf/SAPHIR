using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Threading;
using Saphir.Services;
using Saphir.Shared.Contracts;

namespace Saphir.ViewModels;

/// <summary>Formulaire d'événement (prise d'appel et modification).</summary>
public sealed class EvenementForm
{
    public string Nature { get; set; } = string.Empty;
    public int PrioriteIndex { get; set; } = 1;
    public string Adresse { get; set; } = string.Empty;
    public string? Commune { get; set; }
    public string? Complement { get; set; }
    public string? RequerantNom { get; set; }
    public string? RequerantTel { get; set; }
    public string? Description { get; set; }

    public static EvenementForm From(EvenementDto dto) => new()
    {
        Nature = dto.Nature,
        PrioriteIndex = Math.Clamp(dto.Priorite - 1, 0, 2),
        Adresse = dto.Adresse,
        Commune = dto.Commune,
        Complement = dto.Complement,
        RequerantNom = dto.RequerantNom,
        RequerantTel = dto.RequerantTel,
        Description = dto.Description
    };

    public EvenementInput ToInput() => new(Nature, PrioriteIndex + 1, Adresse, Commune, Complement, RequerantNom, RequerantTel, Description);
}

public sealed class EquipageForm
{
    public long? Id { get; init; }
    public string Indicatif { get; set; } = string.Empty;
    public string Type { get; set; } = PegaseReferentiel.TypesEquipage[0];
    public string? Effectif { get; set; }

    public EquipageInput ToInput() => new(Indicatif, Type, Effectif);
}

/// <summary>Événement dans la liste (couleurs de priorité et de statut).</summary>
public sealed class EventItem(EvenementDto dto)
{
    public EvenementDto Dto { get; } = dto;
    public long Id => Dto.Id;
    public string Numero => Dto.Numero;
    public string Nature => Dto.Nature;
    public string Lieu => string.IsNullOrWhiteSpace(Dto.Commune) ? Dto.Adresse : $"{Dto.Adresse}, {Dto.Commune}";
    public string Statut => Dto.Statut;
    public string Priorite => "P" + Dto.Priorite;
    public string Heure => Dto.CreeLe.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
    public string Equipages => Dto.Equipages.Count == 0 ? "Aucun équipage" : string.Join(" · ", Dto.Equipages);
    public bool IsClosed => Dto.Statut == EvenementStatuts.Termine;
    public IBrush PrioriteBrush => PegaseColors.Priority(Dto.Priorite, IsClosed);
    public IBrush StatutBrush => PegaseColors.EventStatus(Dto.Statut);
}

/// <summary>Équipage dans la liste, avec ses actions.</summary>
public sealed class CrewItem(EquipageDto dto, PegaseViewModel owner)
{
    public EquipageDto Dto { get; } = dto;
    public long Id => Dto.Id;
    public string Indicatif => Dto.Indicatif;
    public string Type => Dto.Type;
    public string? Effectif => Dto.Effectif;
    public bool HasEffectif => !string.IsNullOrWhiteSpace(Dto.Effectif);
    public string Statut => Dto.Statut;
    public string Mission => Dto.EvenementNumero is { } n ? "Sur " + n : string.Empty;
    public bool IsOnEvent => Dto.EvenementId is not null;
    public IBrush StatutBrush => PegaseColors.CrewStatus(Dto.Statut);

    public RelayCommand SetStatusCommand { get; } = new(p => { if (p is string s) _ = owner.SetCrewStatusAsync(dto.Id, s); });
    public RelayCommand EditCommand { get; } = new(() => owner.EditCrew(dto));
    public RelayCommand DeleteCommand { get; } = new(() => _ = owner.DeleteCrewAsync(dto.Id));
    public RelayCommand OpenEventCommand { get; } = new(() => owner.SelectEventById(dto.EvenementId));
}

public sealed record MessageItem(string Heure, string Auteur, string Texte, IBrush Accent);

public sealed class CallItem(AppelDto dto, PegaseViewModel owner)
{
    public AppelDto Dto { get; } = dto;
    public string Titre => "Appel n° " + Dto.Id + (Dto.NumeroRequerant is { } n ? " — " + n : string.Empty);
    public string Detail => $"Décroché à {Dto.DecrocheLe.ToLocalTime():HH:mm} par {Dto.Operateur}" +
                            (Dto.RaccrocheLe is null ? " · en ligne" : " · raccroché");
    public RelayCommand ResumeCommand { get; } = new(() => owner.ResumeCall(dto));
}

public static class PegaseColors
{
    private static IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));

    public static IBrush Priority(int priority, bool closed) => closed
        ? B("#9AA5B4")
        : priority switch { 1 => B("#C62828"), 2 => B("#E07B00"), _ => B("#4A6FA5") };

    public static IBrush EventStatus(string status) => status switch
    {
        EvenementStatuts.Attente => B("#C62828"),
        EvenementStatuts.Engage => B("#E07B00"),
        EvenementStatuts.SurPlace => B("#1E4FA0"),
        _ => B("#6B7A90")
    };

    public static IBrush CrewStatus(string status) => status switch
    {
        EquipageStatuts.Disponible => B("#2E7D32"),
        EquipageStatuts.Engage => B("#E07B00"),
        EquipageStatuts.SurPlace => B("#1E4FA0"),
        EquipageStatuts.Indisponible => B("#8E5A00"),
        _ => B("#6B7A90")
    };

    public static IBrush Message(string type) => type switch
    {
        "creation" => B("#1E4FA0"),
        "engagement" => B("#E07B00"),
        "statut" => B("#2E7D32"),
        "cloture" => B("#6B7A90"),
        _ => B("#C3CDDB")
    };
}

/// <summary>
/// PEGASE : prise des appels 17 (ouverte automatiquement au décroché sur SIMCALL), événements,
/// chronologie et engagement des équipages. L'état est rechargé à chaque changement signalé par le serveur.
/// </summary>
public sealed class PegaseViewModel : ViewModelBase, IDisposable
{
    private readonly DispatcherTimer _clock;
    private bool _loading;
    private bool _reloadPending;
    private bool _rebuildingEvents;
    private bool _subscribed;

    private string _tab = "evenements";
    private bool _isBusy;
    private string? _errorText;
    private string? _infoText;
    private EventItem? _selectedEvent;
    private EvenementDetailDto? _detail;
    private bool _isEditingEvent;
    private bool _isClosingEvent;
    private string _closeReport = string.Empty;
    private string _newMessage = string.Empty;
    private EquipageDto? _crewToEngage;
    private EvenementForm _eventForm = new();
    private EquipageForm? _crewForm;
    private bool _showClosed;

    private AppelDto? _activeCall;
    private EvenementForm _callForm = new();
    private bool _isSansSuiteOpen;
    private string _sansSuiteMotif = PegaseReferentiel.MotifsSansSuite[0];
    private string? _sansSuiteObservations;
    private string _callDuration = "00:00";

    private List<EvenementDto> _allEvents = [];

    public PegaseViewModel()
    {
        ShowEventsCommand = new RelayCommand(() => Tab = "evenements");
        ShowCrewsCommand = new RelayCommand(() => Tab = "equipages");
        ShowCallsCommand = new RelayCommand(() => Tab = "appels");
        NewCallCommand = new RelayCommand(() => _ = NewManualCallAsync());
        RefreshCommand = new RelayCommand(() => _ = ReloadAsync());

        CreateEventCommand = new RelayCommand(() => _ = CreateEventAsync());
        ToggleSansSuiteCommand = new RelayCommand(() => IsSansSuiteOpen = !IsSansSuiteOpen);
        ConfirmSansSuiteCommand = new RelayCommand(() => _ = CloseCallWithoutEventAsync());
        HideCallCommand = new RelayCommand(() => ActiveCall = null);

        EditEventCommand = new RelayCommand(() =>
        {
            if (Detail is null) return;
            EventForm = EvenementForm.From(Detail.Evenement);
            IsClosingEvent = false;
            IsEditingEvent = true;
        });
        SaveEventCommand = new RelayCommand(() => _ = SaveEventAsync());
        CancelEditCommand = new RelayCommand(() => { IsEditingEvent = false; IsClosingEvent = false; });
        StartCloseCommand = new RelayCommand(() => { CloseReport = string.Empty; IsEditingEvent = false; IsClosingEvent = true; });
        ConfirmCloseCommand = new RelayCommand(() => _ = CloseEventAsync());
        AddMessageCommand = new RelayCommand(() => _ = AddMessageAsync());
        EngageCommand = new RelayCommand(() => _ = EngageAsync());
        BackCommand = new RelayCommand(() => SelectedEvent = null);

        NewCrewCommand = new RelayCommand(() => CrewForm = new EquipageForm());
        SaveCrewCommand = new RelayCommand(() => _ = SaveCrewAsync());
        CancelCrewCommand = new RelayCommand(() => CrewForm = null);

        SaphirSession.Realtime += OnRealtime;
        _subscribed = true;

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => UpdateCallDuration();
        _clock.Start();

        _ = InitializeAsync();
    }

    // =====================================================================
    // Onglets, état général
    // =====================================================================

    public string Tab
    {
        get => _tab;
        set
        {
            if (!SetProperty(ref _tab, value))
                return;
            OnPropertyChanged(nameof(IsEventsTab));
            OnPropertyChanged(nameof(IsCrewsTab));
            OnPropertyChanged(nameof(IsCallsTab));
        }
    }

    public bool IsEventsTab => Tab == "evenements";
    public bool IsCrewsTab => Tab == "equipages";
    public bool IsCallsTab => Tab == "appels";

    public ObservableCollection<EventItem> Events { get; } = [];
    public ObservableCollection<CrewItem> Crews { get; } = [];
    public ObservableCollection<CallItem> PendingCalls { get; } = [];

    public string Summary { get; private set; } = string.Empty;
    public string CallsTabLabel => PendingCalls.Count == 0 ? "Appels en attente" : $"Appels en attente ({PendingCalls.Count})";
    public bool HasPendingCalls => PendingCalls.Count > 0;
    public bool HasNoEvents => Events.Count == 0;
    public bool HasNoCrews => Crews.Count == 0;
    public string AgentName => SaphirSession.User?.DisplayName ?? string.Empty;

    /// <summary>Afficher aussi les événements terminés depuis moins de 12 heures.</summary>
    public bool ShowClosed
    {
        get => _showClosed;
        set
        {
            if (SetProperty(ref _showClosed, value))
                RebuildEvents();
        }
    }

    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

    public string? ErrorText
    {
        get => _errorText;
        private set { if (SetProperty(ref _errorText, value)) OnPropertyChanged(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    public string? InfoText
    {
        get => _infoText;
        private set { if (SetProperty(ref _infoText, value)) OnPropertyChanged(nameof(HasInfo)); }
    }

    public bool HasInfo => !string.IsNullOrEmpty(InfoText);

    // Listes
    public IReadOnlyList<string> Natures => PegaseReferentiel.Natures;
    public IReadOnlyList<string> Priorites => PegaseReferentiel.Priorites;
    public IReadOnlyList<string> MotifsSansSuite => PegaseReferentiel.MotifsSansSuite;
    public IReadOnlyList<string> TypesEquipage => PegaseReferentiel.TypesEquipage;

    // =====================================================================
    // Événement sélectionné
    // =====================================================================

    public EventItem? SelectedEvent
    {
        get => _selectedEvent;
        set
        {
            // La liste est vidée puis reconstruite à chaque rechargement : la liste remet alors la sélection à vide,
            // ce qu'on ignore pour garder la fiche ouverte.
            if (_rebuildingEvents)
                return;
            if (!SetProperty(ref _selectedEvent, value))
                return;
            IsEditingEvent = false;
            IsClosingEvent = false;
            OnPropertyChanged(nameof(HasSelection));
            if (value is null)
                Detail = null;
            else
                _ = LoadDetailAsync(value.Id);
        }
    }

    public bool HasSelection => SelectedEvent is not null;

    public EvenementDetailDto? Detail
    {
        get => _detail;
        private set
        {
            if (!SetProperty(ref _detail, value))
                return;
            RebuildDetail();
        }
    }

    public bool HasDetail => Detail is not null;
    public bool ShowNoSelection => Detail is null;
    public string DetailTitle => Detail is null ? string.Empty : $"{Detail.Evenement.Numero} — {Detail.Evenement.Nature}";
    public string DetailPriority => Detail is null ? string.Empty : PegaseReferentiel.PrioriteLabel(Detail.Evenement.Priorite);
    public IBrush? DetailPriorityBrush => Detail is null ? null : PegaseColors.Priority(Detail.Evenement.Priorite, DetailClosed);
    public string DetailStatus => Detail?.Evenement.Statut ?? string.Empty;
    public IBrush? DetailStatusBrush => Detail is null ? null : PegaseColors.EventStatus(Detail.Evenement.Statut);
    public bool DetailClosed => Detail?.Evenement.Statut == EvenementStatuts.Termine;
    public bool DetailOpen => Detail is not null && !DetailClosed;
    public bool CanEditDetail => DetailOpen && !IsEditingEvent && !IsClosingEvent;

    public ObservableCollection<FieldRow> DetailRows { get; } = [];
    public ObservableCollection<MessageItem> Messages { get; } = [];
    public ObservableCollection<CrewItem> EngagedCrews { get; } = [];
    public ObservableCollection<EquipageDto> EngageableCrews { get; } = [];
    public bool HasEngagedCrews => EngagedCrews.Count > 0;

    public EquipageDto? CrewToEngage { get => _crewToEngage; set => SetProperty(ref _crewToEngage, value); }
    public string NewMessage { get => _newMessage; set => SetProperty(ref _newMessage, value); }

    public bool IsEditingEvent
    {
        get => _isEditingEvent;
        private set { if (SetProperty(ref _isEditingEvent, value)) OnPropertyChanged(nameof(CanEditDetail)); }
    }

    public bool IsClosingEvent
    {
        get => _isClosingEvent;
        private set { if (SetProperty(ref _isClosingEvent, value)) OnPropertyChanged(nameof(CanEditDetail)); }
    }

    public EvenementForm EventForm { get => _eventForm; private set => SetProperty(ref _eventForm, value); }
    public string CloseReport { get => _closeReport; set => SetProperty(ref _closeReport, value); }

    // =====================================================================
    // Équipages
    // =====================================================================

    public EquipageForm? CrewForm
    {
        get => _crewForm;
        private set
        {
            if (SetProperty(ref _crewForm, value))
            {
                OnPropertyChanged(nameof(IsCrewFormOpen));
                OnPropertyChanged(nameof(CrewFormTitle));
            }
        }
    }

    public bool IsCrewFormOpen => CrewForm is not null;
    public string CrewFormTitle => CrewForm?.Id is null ? "Nouvel équipage" : "Modifier l'équipage";

    public IReadOnlyList<string> CrewStatuses => EquipageStatuts.All;

    // =====================================================================
    // Prise d'appel
    // =====================================================================

    public AppelDto? ActiveCall
    {
        get => _activeCall;
        private set
        {
            if (!SetProperty(ref _activeCall, value))
                return;
            IsSansSuiteOpen = false;
            OnPropertyChanged(nameof(IsCallOpen));
            OnPropertyChanged(nameof(CallTitle));
            OnPropertyChanged(nameof(CallAnsweredAt));
            OnPropertyChanged(nameof(CallNumber));
            OnPropertyChanged(nameof(CallOperator));
            OnPropertyChanged(nameof(CallSource));
            OnPropertyChanged(nameof(CallOnLine));
            UpdateCallDuration();
        }
    }

    public bool IsCallOpen => ActiveCall is not null;
    public string CallTitle => ActiveCall is null ? string.Empty : "Prise d'appel 17 — appel n° " + ActiveCall.Id;
    public string CallAnsweredAt => ActiveCall?.DecrocheLe.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty;
    public string CallNumber => ActiveCall?.NumeroRequerant ?? "Numéro masqué / non transmis";
    public string CallOperator => ActiveCall?.Operateur ?? string.Empty;
    public string CallSource => ActiveCall?.SimcallId is null ? "Saisie manuelle" : "SIMCALL";
    public bool CallOnLine => ActiveCall is { RaccrocheLe: null, SimcallId: not null };

    public string CallDuration { get => _callDuration; private set => SetProperty(ref _callDuration, value); }

    public EvenementForm CallForm { get => _callForm; private set => SetProperty(ref _callForm, value); }

    public bool IsSansSuiteOpen { get => _isSansSuiteOpen; private set => SetProperty(ref _isSansSuiteOpen, value); }
    public string SansSuiteMotif { get => _sansSuiteMotif; set => SetProperty(ref _sansSuiteMotif, value); }
    public string? SansSuiteObservations { get => _sansSuiteObservations; set => SetProperty(ref _sansSuiteObservations, value); }

    // =====================================================================
    // Commandes
    // =====================================================================

    public RelayCommand ShowEventsCommand { get; }
    public RelayCommand ShowCrewsCommand { get; }
    public RelayCommand ShowCallsCommand { get; }
    public RelayCommand NewCallCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand CreateEventCommand { get; }
    public RelayCommand ToggleSansSuiteCommand { get; }
    public RelayCommand ConfirmSansSuiteCommand { get; }
    public RelayCommand HideCallCommand { get; }
    public RelayCommand EditEventCommand { get; }
    public RelayCommand SaveEventCommand { get; }
    public RelayCommand CancelEditCommand { get; }
    public RelayCommand StartCloseCommand { get; }
    public RelayCommand ConfirmCloseCommand { get; }
    public RelayCommand AddMessageCommand { get; }
    public RelayCommand EngageCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand NewCrewCommand { get; }
    public RelayCommand SaveCrewCommand { get; }
    public RelayCommand CancelCrewCommand { get; }

    // =====================================================================
    // Chargement et temps réel
    // =====================================================================

    private async Task InitializeAsync()
    {
        await ReloadAsync();
        if (SaphirSession.TakePendingCall() is { } callId)
            await OpenCallAsync(callId);
    }

    private void OnRealtime(RealtimeMessage message)
    {
        if (message.Type == RealtimeTypes.PegaseChanged)
            _ = ReloadAsync();
        else if (message.Type == RealtimeTypes.OpenCall && SaphirSession.TakePendingCall() is { } callId)
            _ = OpenCallAsync(callId);
    }

    /// <summary>Recharge l'état ; un changement reçu pendant le chargement en relance un juste après.</summary>
    private async Task ReloadAsync()
    {
        var api = SaphirSession.Api;
        if (api is null)
            return;
        if (_loading)
        {
            _reloadPending = true;
            return;
        }

        _loading = true;
        try
        {
            do
            {
                _reloadPending = false;
                var state = await api.GetAsync<PegaseStateDto>("api/pegase/etat");
                ApplyState(state);
                if (SelectedEvent is not null)
                    Detail = await api.GetAsync<EvenementDetailDto>($"api/pegase/evenement/{SelectedEvent.Id}");
            } while (_reloadPending);
            if (ErrorText is not null && !IsBusy)
                ErrorText = null;
        }
        catch (ApiException ex)
        {
            ErrorText = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorText = "Erreur inattendue : " + ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    private void ApplyState(PegaseStateDto state)
    {
        _allEvents = state.Evenements.ToList();
        RebuildEvents();

        Crews.Clear();
        foreach (var crew in state.Equipages)
            Crews.Add(new CrewItem(crew, this));

        PendingCalls.Clear();
        foreach (var call in state.AppelsEnCours)
            PendingCalls.Add(new CallItem(call, this));

        // La prise d'appel ouverte suit l'état serveur (raccroché, déjà traitée ailleurs).
        if (ActiveCall is { } active)
        {
            var updated = state.AppelsEnCours.FirstOrDefault(c => c.Id == active.Id);
            if (updated is not null)
                ActiveCall = updated;
        }

        var open = state.Evenements.Count(e => e.Statut != EvenementStatuts.Termine);
        var p1 = state.Evenements.Count(e => e.Statut != EvenementStatuts.Termine && e.Priorite == 1);
        var available = state.Equipages.Count(c => c.Statut == EquipageStatuts.Disponible);
        Summary = $"{open} événement(s) en cours · {p1} P1 · {available} équipage(s) disponible(s)";

        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(CallsTabLabel));
        OnPropertyChanged(nameof(HasPendingCalls));
        OnPropertyChanged(nameof(HasNoCrews));
        RebuildEngageable();
    }

    private void RebuildEvents()
    {
        var selectedId = SelectedEvent?.Id;
        _rebuildingEvents = true;
        try
        {
            Events.Clear();
            foreach (var ev in _allEvents.Where(e => ShowClosed || e.Statut != EvenementStatuts.Termine))
                Events.Add(new EventItem(ev));
        }
        finally
        {
            _rebuildingEvents = false;
        }

        // Garde la sélection sans recharger la fiche (le rechargement est fait par l'appelant).
        _selectedEvent = selectedId is { } id ? Events.FirstOrDefault(e => e.Id == id) ?? _selectedEvent : null;
        OnPropertyChanged(nameof(SelectedEvent));
        OnPropertyChanged(nameof(HasNoEvents));
    }

    private async Task LoadDetailAsync(long id)
    {
        var api = SaphirSession.Api;
        if (api is null)
            return;
        await RunAsync(async () => Detail = await api.GetAsync<EvenementDetailDto>($"api/pegase/evenement/{id}"));
    }

    public void SelectEventById(long? id)
    {
        if (id is null)
            return;
        Tab = "evenements";
        var item = Events.FirstOrDefault(e => e.Id == id) ?? _allEvents.Where(e => e.Id == id).Select(e => new EventItem(e)).FirstOrDefault();
        if (item is not null)
            SelectedEvent = item;
    }

    private void RebuildDetail()
    {
        DetailRows.Clear();
        Messages.Clear();
        EngagedCrews.Clear();

        if (Detail is { } detail)
        {
            var ev = detail.Evenement;
            AddRow("Adresse", ev.Adresse);
            AddRow("Commune", ev.Commune);
            AddRow("Complément", ev.Complement);
            AddRow("Requérant", ev.RequerantNom);
            AddRow("Téléphone", ev.RequerantTel);
            AddRow("Créé", $"{ev.CreeLe.ToLocalTime():dd/MM/yyyy HH:mm} par {ev.CreePar}");
            if (detail.Appel is { } call)
                AddRow("Appel", $"n° {call.Id} décroché à {call.DecrocheLe.ToLocalTime():HH:mm:ss}" +
                                (call.RaccrocheLe is { } end ? $", durée {FormatDuration(end - call.DecrocheLe)}" : string.Empty));
            AddRow("Description", ev.Description);
            if (ev.ClosLe is { } closed)
                AddRow("Clôture", $"{closed.ToLocalTime():dd/MM/yyyy HH:mm} par {ev.ClosPar}");
            AddRow("Compte rendu", ev.CompteRendu);

            foreach (var m in detail.Messages)
                Messages.Add(new MessageItem(m.Date.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture), m.Auteur, m.Texte, PegaseColors.Message(m.Type)));

            foreach (var crew in Crews.Where(c => c.Dto.EvenementId == ev.Id))
                EngagedCrews.Add(crew);
        }

        RebuildEngageable();
        foreach (var name in new[]
                 {
                     nameof(HasDetail), nameof(ShowNoSelection), nameof(DetailTitle), nameof(DetailPriority), nameof(DetailPriorityBrush),
                     nameof(DetailStatus), nameof(DetailStatusBrush), nameof(DetailClosed), nameof(DetailOpen), nameof(CanEditDetail),
                     nameof(HasEngagedCrews)
                 })
            OnPropertyChanged(name);

        void AddRow(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                DetailRows.Add(new FieldRow(label, value));
        }
    }

    private void RebuildEngageable()
    {
        var eventId = Detail?.Evenement.Id;
        var previous = CrewToEngage?.Id;
        EngageableCrews.Clear();
        foreach (var crew in Crews.Select(c => c.Dto)
                     .Where(c => c.EvenementId != eventId && c.Statut is not (EquipageStatuts.HorsService or EquipageStatuts.Indisponible))
                     .OrderBy(c => c.Statut != EquipageStatuts.Disponible).ThenBy(c => c.Indicatif, StringComparer.Ordinal))
            EngageableCrews.Add(crew);
        CrewToEngage = EngageableCrews.FirstOrDefault(c => c.Id == previous) ?? EngageableCrews.FirstOrDefault();
    }

    // =====================================================================
    // Prise d'appel
    // =====================================================================

    private async Task OpenCallAsync(long id)
    {
        var api = SaphirSession.Api;
        if (api is null)
            return;
        await RunAsync(async () =>
        {
            var call = await api.GetAsync<AppelDto>($"api/pegase/appel/{id}");
            if (call.Statut != AppelStatuts.EnCours)
            {
                InfoText = $"L'appel n° {call.Id} a déjà été traité ({call.Statut.ToLowerInvariant()}).";
                return;
            }
            StartCallForm(call);
        });
    }

    public void ResumeCall(AppelDto call) => StartCallForm(call);

    private void StartCallForm(AppelDto call)
    {
        if (ActiveCall?.Id != call.Id)
            CallForm = new EvenementForm { RequerantTel = call.NumeroRequerant };
        SansSuiteMotif = PegaseReferentiel.MotifsSansSuite[0];
        SansSuiteObservations = null;
        ActiveCall = call;
    }

    private async Task NewManualCallAsync()
    {
        var api = SaphirSession.Api;
        if (api is null)
            return;
        await RunAsync(async () => StartCallForm(await api.PostAsync<AppelDto>("api/pegase/appels", new { })));
    }

    private async Task CreateEventAsync()
    {
        var api = SaphirSession.Api;
        var call = ActiveCall;
        if (api is null || call is null)
            return;
        if (string.IsNullOrWhiteSpace(CallForm.Nature) || string.IsNullOrWhiteSpace(CallForm.Adresse))
        {
            ErrorText = "Renseigne au moins la nature et l'adresse de l'événement.";
            return;
        }

        await RunAsync(async () =>
        {
            var created = await api.PostAsync<CreatedId>($"api/pegase/appel/{call.Id}/evenement", CallForm.ToInput());
            ActiveCall = null;
            Tab = "evenements";
            await ReloadAsync();
            SelectEventById(created.Id);
            InfoText = "Événement créé.";
        });
    }

    private async Task CloseCallWithoutEventAsync()
    {
        var api = SaphirSession.Api;
        var call = ActiveCall;
        if (api is null || call is null)
            return;
        await RunAsync(async () =>
        {
            await api.SendAsync(HttpMethod.Post, $"api/pegase/appel/{call.Id}/sans-suite", new SansSuiteInput(SansSuiteMotif, SansSuiteObservations));
            ActiveCall = null;
            InfoText = $"Appel n° {call.Id} clos sans suite ({SansSuiteMotif.ToLowerInvariant()}).";
        });
    }

    private void UpdateCallDuration()
    {
        if (ActiveCall is not { } call)
            return;
        var end = call.RaccrocheLe ?? DateTimeOffset.UtcNow;
        CallDuration = FormatDuration(end - call.DecrocheLe);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            duration = TimeSpan.Zero;
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    // =====================================================================
    // Actions sur l'événement
    // =====================================================================

    private Task SaveEventAsync()
        => WithEventAsync(async (api, id) =>
        {
            await api.SendAsync(HttpMethod.Put, $"api/pegase/evenement/{id}", EventForm.ToInput());
            IsEditingEvent = false;
            InfoText = "Événement mis à jour.";
        });

    private Task CloseEventAsync()
        => WithEventAsync(async (api, id) =>
        {
            await api.SendAsync(HttpMethod.Post, $"api/pegase/evenement/{id}/clore", new ClotureInput(CloseReport));
            IsClosingEvent = false;
            InfoText = "Événement terminé ; les équipages sont de nouveau disponibles.";
        });

    private Task AddMessageAsync()
        => WithEventAsync(async (api, id) =>
        {
            if (string.IsNullOrWhiteSpace(NewMessage))
                return;
            await api.SendAsync(HttpMethod.Post, $"api/pegase/evenement/{id}/message", new MessageInput(NewMessage));
            NewMessage = string.Empty;
        });

    private Task EngageAsync()
        => WithEventAsync(async (api, id) =>
        {
            if (CrewToEngage is not { } crew)
            {
                ErrorText = "Aucun équipage disponible à engager.";
                return;
            }
            await api.SendAsync(HttpMethod.Post, $"api/pegase/evenement/{id}/engager", new EngagementInput(crew.Id));
            InfoText = $"{crew.Indicatif} engagé.";
        });

    private async Task WithEventAsync(Func<ApiClient, long, Task> work)
    {
        var api = SaphirSession.Api;
        var id = Detail?.Evenement.Id;
        if (api is null || id is null)
            return;
        await RunAsync(async () =>
        {
            await work(api, id.Value);
            await ReloadAsync();
        });
    }

    // =====================================================================
    // Actions sur les équipages
    // =====================================================================

    public async Task SetCrewStatusAsync(long id, string status)
    {
        var api = SaphirSession.Api;
        if (api is null)
            return;
        await RunAsync(async () =>
        {
            await api.SendAsync(HttpMethod.Post, $"api/pegase/equipage/{id}/statut", new StatutInput(status));
            await ReloadAsync();
        });
    }

    public void EditCrew(EquipageDto crew)
        => CrewForm = new EquipageForm { Id = crew.Id, Indicatif = crew.Indicatif, Type = crew.Type, Effectif = crew.Effectif };

    public async Task DeleteCrewAsync(long id)
    {
        var api = SaphirSession.Api;
        if (api is null)
            return;
        await RunAsync(async () =>
        {
            await api.SendAsync(HttpMethod.Delete, $"api/pegase/equipage/{id}");
            await ReloadAsync();
        });
    }

    private async Task SaveCrewAsync()
    {
        var api = SaphirSession.Api;
        var form = CrewForm;
        if (api is null || form is null)
            return;
        await RunAsync(async () =>
        {
            if (form.Id is { } id)
                await api.SendAsync(HttpMethod.Put, $"api/pegase/equipage/{id}", form.ToInput());
            else
                await api.PostAsync<CreatedId>("api/pegase/equipages", form.ToInput());
            CrewForm = null;
            await ReloadAsync();
            InfoText = "Équipage enregistré.";
        });
    }

    // =====================================================================

    private async Task RunAsync(Func<Task> work)
    {
        IsBusy = true;
        ErrorText = null;
        InfoText = null;
        try
        {
            await work();
        }
        catch (ApiException ex)
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

    /// <summary>Fenêtre de nouveau affichée : reprend l'écoute du temps réel et recharge.</summary>
    public void Resume()
    {
        if (_subscribed)
            return;
        _subscribed = true;
        SaphirSession.Realtime += OnRealtime;
        _clock.Start();
        _ = ReloadAsync();
    }

    public void Dispose()
    {
        if (!_subscribed)
            return;
        _subscribed = false;
        SaphirSession.Realtime -= OnRealtime;
        _clock.Stop();
    }
}

/// <summary>Réponse du serveur à une création (événement, équipage).</summary>
public sealed record CreatedId(long Id);
