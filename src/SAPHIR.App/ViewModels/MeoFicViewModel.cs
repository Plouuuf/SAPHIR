using System.Collections.ObjectModel;
using System.Globalization;
using Saphir.Services;
using Saphir.Shared.Contracts;

namespace Saphir.ViewModels;

/// <summary>Fichier consultable dans la colonne de gauche de MEO-FIC.</summary>
public sealed class MeoFileTab : ViewModelBase
{
    private bool _isSelected;

    public MeoFileTab(MeoFile file, string code, string label, Action<MeoFileTab> select)
    {
        File = file;
        Code = code;
        Label = label;
        SelectCommand = new RelayCommand(() => select(this));
    }

    public MeoFile File { get; }
    public string Code { get; }
    public string Label { get; }
    public RelayCommand SelectCommand { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>Ligne « libellé : valeur » d'une fiche.</summary>
public sealed record FieldRow(string Label, string Value);

/// <summary>Inscription affichée sous forme de carte (TAJ, FPR, FOVeS).</summary>
public sealed class EntryCard
{
    public required string Title { get; init; }
    public string? Badge { get; init; }
    public IReadOnlyList<FieldRow> Fields { get; init; } = [];
    public string? Details { get; init; }
    public string Footer { get; init; } = string.Empty;

    /// <summary>Inscription en vigueur (FPR, FOVeS) : bordure rouge.</summary>
    public bool IsAlert { get; init; }
    public bool IsClosed { get; init; }

    public bool HasBadge => !string.IsNullOrEmpty(Badge);
    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);
    public bool CanClose => CloseCommand is not null;
    public bool CanDelete => DeleteCommand is not null;
    public RelayCommand? CloseCommand { get; init; }
    public RelayCommand? DeleteCommand { get; init; }
}

/// <summary>Ligne du journal des consultations (administration).</summary>
public sealed record JournalRow(string Date, string Agent, string Action, string Cible);

/// <summary>
/// MEO-FIC : consultation des fichiers de police. Personnes (TAJ, FPR, SNPC) et véhicules (FOVeS, SIV, FVA),
/// recherche, fiche détaillée, inscriptions et journal des consultations.
/// </summary>
public sealed class MeoFicViewModel : ViewModelBase
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private MeoFileTab _selectedTab;
    private string _searchText = string.Empty;
    private bool _isBusy;
    private string? _errorText;
    private string? _infoText;
    private string? _resultsInfo;
    private PersonSummaryDto? _selectedPersonResult;
    private VehicleSummaryDto? _selectedVehicleResult;
    private PersonDetailDto? _person;
    private VehicleDetailDto? _vehicle;
    private MeoForm _form = MeoForm.None;
    private bool _showDetailOnCompact;

    private IdentityForm _identityForm = new();
    private TajForm _tajForm = new();
    private FprForm _fprForm = new();
    private PermisForm _permisForm = new();
    private VehicleForm _vehicleForm = new();
    private FovesForm _fovesForm = new();

    public MeoFicViewModel()
    {
        PersonTabs =
        [
            new MeoFileTab(MeoFile.Taj, "TAJ", "Antécédents judiciaires", SelectTab),
            new MeoFileTab(MeoFile.Fpr, "FPR", "Personnes recherchées", SelectTab),
            new MeoFileTab(MeoFile.Snpc, "SNPC", "Permis de conduire", SelectTab)
        ];
        VehicleTabs =
        [
            new MeoFileTab(MeoFile.Foves, "FOVeS", "Objets et véhicules signalés", SelectTab),
            new MeoFileTab(MeoFile.Siv, "SIV", "Immatriculation des véhicules", SelectTab),
            new MeoFileTab(MeoFile.Fva, "FVA", "Véhicules assurés", SelectTab)
        ];
        AdminTabs = [new MeoFileTab(MeoFile.Journal, "JOURNAL", "Consultations et inscriptions", SelectTab)];

        _selectedTab = PersonTabs[0];
        _selectedTab.IsSelected = true;

        SearchCommand = new RelayCommand(() => _ = SearchAsync());
        NewPersonCommand = new RelayCommand(() => OpenForm(MeoForm.NewPerson));
        EditIdentityCommand = new RelayCommand(() => OpenForm(MeoForm.Identity));
        NewTajCommand = new RelayCommand(() => OpenForm(MeoForm.Taj));
        NewFprCommand = new RelayCommand(() => OpenForm(MeoForm.Fpr));
        EditPermisCommand = new RelayCommand(() => OpenForm(MeoForm.Permis));
        NewVehicleCommand = new RelayCommand(() => OpenVehicleForm(false));
        EditVehicleCommand = new RelayCommand(() => OpenVehicleForm(true));
        NewFovesCommand = new RelayCommand(() => OpenForm(MeoForm.Foves));
        SaveFormCommand = new RelayCommand(() => _ = SaveFormAsync());
        CancelFormCommand = new RelayCommand(() => Form = MeoForm.None);
        OpenVehicleCommand = new RelayCommand(p => { if (p is VehicleSummaryDto v) _ = OpenVehicleFromPersonAsync(v); });
        OpenTitulaireCommand = new RelayCommand(() => _ = OpenTitulaireAsync());
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync());
        BackToResultsCommand = new RelayCommand(() => ShowDetailOnCompact = false);
    }

    // =====================================================================
    // Onglets et recherche
    // =====================================================================

    public IReadOnlyList<MeoFileTab> PersonTabs { get; }
    public IReadOnlyList<MeoFileTab> VehicleTabs { get; }
    public IReadOnlyList<MeoFileTab> AdminTabs { get; }

    /// <summary>Tous les fichiers accessibles, pour la barre d'onglets du téléphone.</summary>
    public IReadOnlyList<MeoFileTab> AllTabs => PersonTabs.Concat(VehicleTabs).Concat(IsAdmin ? AdminTabs : Array.Empty<MeoFileTab>()).ToList();
    public bool IsAdmin => SaphirSession.IsAdministrator;
    public string AgentName => SaphirSession.User?.DisplayName ?? string.Empty;

    public MeoFile SelectedFile => _selectedTab.File;
    public string SelectedFileTitle => $"{_selectedTab.Code} — {_selectedTab.Label}";
    public bool IsPersonMode => SelectedFile is MeoFile.Taj or MeoFile.Fpr or MeoFile.Snpc;
    public bool IsVehicleMode => SelectedFile is MeoFile.Foves or MeoFile.Siv or MeoFile.Fva;
    public bool IsJournalMode => SelectedFile == MeoFile.Journal;
    public bool IsSearchMode => !IsJournalMode;

    public string SearchWatermark => IsPersonMode ? "Nom, prénom ou date de naissance" : "Plaque d'immatriculation";

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public ObservableCollection<PersonSummaryDto> PersonResults { get; } = [];
    public ObservableCollection<VehicleSummaryDto> VehicleResults { get; } = [];
    public ObservableCollection<JournalRow> Journal { get; } = [];

    public string? ResultsInfo
    {
        get => _resultsInfo;
        private set => SetProperty(ref _resultsInfo, value);
    }

    public PersonSummaryDto? SelectedPersonResult
    {
        get => _selectedPersonResult;
        set
        {
            if (SetProperty(ref _selectedPersonResult, value) && value is not null)
                _ = LoadPersonAsync(value.Key);
        }
    }

    public VehicleSummaryDto? SelectedVehicleResult
    {
        get => _selectedVehicleResult;
        set
        {
            if (SetProperty(ref _selectedVehicleResult, value) && value is not null)
                _ = LoadVehicleAsync(value.Plaque);
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string? ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetProperty(ref _errorText, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    public string? InfoText
    {
        get => _infoText;
        private set
        {
            if (SetProperty(ref _infoText, value))
                OnPropertyChanged(nameof(HasInfo));
        }
    }

    public bool HasInfo => !string.IsNullOrEmpty(InfoText);

    /// <summary>Sur téléphone : vrai quand la fiche est affichée à la place des résultats.</summary>
    public bool ShowDetailOnCompact
    {
        get => _showDetailOnCompact;
        set => SetProperty(ref _showDetailOnCompact, value);
    }

    // =====================================================================
    // Fiche personne
    // =====================================================================

    public PersonDetailDto? Person
    {
        get => _person;
        private set
        {
            if (!SetProperty(ref _person, value))
                return;
            RebuildPerson();
            RaiseDetailState();
        }
    }

    public bool HasPerson => Person is not null;
    public string PersonTitle => Person is null ? string.Empty : $"{Person.Identite.Nom} {Person.Identite.Prenom}";
    public string PersonSubtitle => Person is null
        ? string.Empty
        : string.Join(" · ", new[]
        {
            Person.Identite.DateNaissance is { } d ? (Person.Identite.Sexe == "F" ? "Née le " : "Né le ") + d : null,
            Person.Identite.LieuNaissance is { } l ? "à " + l : null,
            "Source : " + Person.Source
        }.Where(s => s is not null));

    public ObservableCollection<FieldRow> IdentityRows { get; } = [];
    public ObservableCollection<EntryCard> TajCards { get; } = [];
    public ObservableCollection<EntryCard> FprCards { get; } = [];
    public ObservableCollection<FieldRow> PermisRows { get; } = [];
    public ObservableCollection<VehicleSummaryDto> PersonVehicles { get; } = [];

    /// <summary>Bandeau rouge : personne inscrite au FPR (affiché quel que soit le fichier consulté).</summary>
    public string? PersonAlert { get; private set; }
    public string? PersonAlertDetail { get; private set; }
    public bool HasPersonAlert => PersonAlert is not null;

    public bool HasTaj => TajCards.Count > 0;
    public bool HasFpr => FprCards.Count > 0;
    public bool HasPermis => PermisRows.Count > 0;
    public bool HasPersonVehicles => PersonVehicles.Count > 0;

    public bool ShowPersonDetail => IsPersonMode && HasPerson && Form == MeoForm.None;
    public bool ShowTaj => ShowPersonDetail && SelectedFile == MeoFile.Taj;
    public bool ShowFpr => ShowPersonDetail && SelectedFile == MeoFile.Fpr;
    public bool ShowSnpc => ShowPersonDetail && SelectedFile == MeoFile.Snpc;

    // =====================================================================
    // Fiche véhicule
    // =====================================================================

    public VehicleDetailDto? Vehicle
    {
        get => _vehicle;
        private set
        {
            if (!SetProperty(ref _vehicle, value))
                return;
            RebuildVehicle();
            RaiseDetailState();
        }
    }

    public bool HasVehicle => Vehicle is not null;
    public string VehicleTitle => Vehicle?.Plaque ?? string.Empty;
    public string VehicleSubtitle => Vehicle is null
        ? string.Empty
        : string.Join(" · ", new[] { Vehicle.Siv.Marque, Vehicle.Siv.Modele, Vehicle.Siv.Couleur, "Source : " + Vehicle.Siv.Source }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

    public ObservableCollection<FieldRow> SivRows { get; } = [];
    public ObservableCollection<FieldRow> FvaRows { get; } = [];
    public ObservableCollection<EntryCard> FovesCards { get; } = [];

    public string? VehicleAlert { get; private set; }
    public bool HasVehicleAlert => VehicleAlert is not null;
    public bool HasFoves => FovesCards.Count > 0;
    public bool HasTitulaire => Vehicle?.Siv.TitulaireKey is not null;
    public string FvaStatus => Vehicle?.Fva.Statut ?? string.Empty;
    public bool IsInsured => Vehicle?.Fva.Statut == "Assuré";

    public bool ShowVehicleDetail => IsVehicleMode && HasVehicle && Form == MeoForm.None;
    public bool ShowFoves => ShowVehicleDetail && SelectedFile == MeoFile.Foves;
    public bool ShowSiv => ShowVehicleDetail && SelectedFile == MeoFile.Siv;
    public bool ShowFva => ShowVehicleDetail && SelectedFile == MeoFile.Fva;

    public bool ShowEmpty => Form == MeoForm.None && !IsJournalMode &&
                             !(IsPersonMode && HasPerson) && !(IsVehicleMode && HasVehicle);

    public string EmptyText => IsPersonMode
        ? "Recherche une personne par son nom pour consulter le " + _selectedTab.Code + "."
        : "Recherche un véhicule par sa plaque pour consulter le " + _selectedTab.Code + ".";

    // =====================================================================
    // Formulaires
    // =====================================================================

    public MeoForm Form
    {
        get => _form;
        private set
        {
            if (SetProperty(ref _form, value))
                RaiseDetailState();
        }
    }

    public bool IsFormOpen => Form != MeoForm.None;
    public bool IsNewPersonForm => Form == MeoForm.NewPerson;
    public bool IsIdentityForm => Form is MeoForm.NewPerson or MeoForm.Identity;
    public bool IsTajForm => Form == MeoForm.Taj;
    public bool IsFprForm => Form == MeoForm.Fpr;
    public bool IsPermisForm => Form == MeoForm.Permis;
    public bool IsVehicleForm => Form == MeoForm.Vehicle;
    public bool IsFovesForm => Form == MeoForm.Foves;

    public string FormTitle => Form switch
    {
        MeoForm.NewPerson => "Nouvelle fiche personne",
        MeoForm.Identity => "Identité et signalement — " + PersonTitle,
        MeoForm.Taj => "Nouvelle inscription TAJ — " + PersonTitle,
        MeoForm.Fpr => "Inscription au FPR — " + PersonTitle,
        MeoForm.Permis => "Permis de conduire (SNPC) — " + PersonTitle,
        MeoForm.Vehicle => _editExistingVehicle && HasVehicle ? "SIV / FVA — " + VehicleTitle : "Nouveau véhicule (SIV / FVA)",
        MeoForm.Foves => "Signalement FOVeS — " + FovesForm.Plaque,
        _ => string.Empty
    };

    public IdentityForm IdentityForm { get => _identityForm; private set => SetProperty(ref _identityForm, value); }
    public TajForm TajForm { get => _tajForm; private set => SetProperty(ref _tajForm, value); }
    public FprForm FprForm { get => _fprForm; private set => SetProperty(ref _fprForm, value); }
    public PermisForm PermisForm { get => _permisForm; private set => SetProperty(ref _permisForm, value); }
    public VehicleForm VehicleForm { get => _vehicleForm; private set => SetProperty(ref _vehicleForm, value); }
    public FovesForm FovesForm { get => _fovesForm; private set => SetProperty(ref _fovesForm, value); }

    // Listes des formulaires
    public IReadOnlyList<string> Sexes => IdentityForm.Sexes;
    public IReadOnlyList<string> TajQualites => MeoFicReferentiel.TajQualites;
    public IReadOnlyList<string> FprMotifs => MeoFicReferentiel.FprMotifs;
    public IReadOnlyList<string> ConduitesATenir => MeoFicReferentiel.ConduitesATenir;
    public IReadOnlyList<string> PermisStatuts => MeoFicReferentiel.PermisStatuts;
    public IReadOnlyList<string> FovesMotifs => MeoFicReferentiel.FovesMotifs;

    // =====================================================================
    // Commandes
    // =====================================================================

    public RelayCommand SearchCommand { get; }
    public RelayCommand NewPersonCommand { get; }
    public RelayCommand EditIdentityCommand { get; }
    public RelayCommand NewTajCommand { get; }
    public RelayCommand NewFprCommand { get; }
    public RelayCommand EditPermisCommand { get; }
    public RelayCommand NewVehicleCommand { get; }
    public RelayCommand EditVehicleCommand { get; }
    public RelayCommand NewFovesCommand { get; }
    public RelayCommand SaveFormCommand { get; }
    public RelayCommand CancelFormCommand { get; }
    public RelayCommand OpenVehicleCommand { get; }
    public RelayCommand OpenTitulaireCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand BackToResultsCommand { get; }

    // =====================================================================
    // Logique
    // =====================================================================

    private void SelectTab(MeoFileTab tab)
    {
        var wasPerson = IsPersonMode;
        var wasVehicle = IsVehicleMode;

        _selectedTab = tab;
        foreach (var t in PersonTabs.Concat(VehicleTabs).Concat(AdminTabs))
            t.IsSelected = ReferenceEquals(t, tab);

        Form = MeoForm.None;
        ErrorText = null;
        InfoText = null;

        // Changement de famille de fichiers : la recherche en cours ne s'applique plus.
        if (wasPerson != IsPersonMode || wasVehicle != IsVehicleMode)
        {
            SearchText = string.Empty;
            ResultsInfo = null;
            ShowDetailOnCompact = false;
        }

        OnPropertyChanged(nameof(SelectedFile));
        OnPropertyChanged(nameof(SelectedFileTitle));
        OnPropertyChanged(nameof(IsPersonMode));
        OnPropertyChanged(nameof(IsVehicleMode));
        OnPropertyChanged(nameof(IsJournalMode));
        OnPropertyChanged(nameof(IsSearchMode));
        OnPropertyChanged(nameof(SearchWatermark));
        OnPropertyChanged(nameof(EmptyText));
        RaiseDetailState();

        if (IsJournalMode)
            _ = LoadJournalAsync();
    }

    private async Task SearchAsync()
    {
        var api = SaphirSession.Api;
        var query = SearchText.Trim();
        if (api is null || IsJournalMode)
            return;
        if (query.Length < 2)
        {
            ErrorText = IsPersonMode ? "Saisis au moins deux lettres du nom ou du prénom." : "Saisis au moins deux caractères de la plaque.";
            return;
        }

        await RunAsync(async () =>
        {
            ShowDetailOnCompact = false;
            if (IsPersonMode)
            {
                var results = await api.GetAsync<List<PersonSummaryDto>>("api/meo/personnes?q=" + ApiClient.Q(query));
                PersonResults.Clear();
                foreach (var r in results)
                    PersonResults.Add(r);
                ResultsInfo = results.Count switch
                {
                    0 => "Aucune personne trouvée.",
                    1 => "1 personne",
                    _ => results.Count + " personnes"
                };
            }
            else
            {
                var results = await api.GetAsync<List<VehicleSummaryDto>>("api/meo/vehicules?q=" + ApiClient.Q(query));
                VehicleResults.Clear();
                foreach (var r in results)
                    VehicleResults.Add(r);
                ResultsInfo = results.Count switch
                {
                    0 => "Aucun véhicule trouvé. Tu peux le créer au SIV ou signaler la plaque au FOVeS.",
                    1 => "1 véhicule",
                    _ => results.Count + " véhicules"
                };
            }
        });
    }

    private async Task LoadPersonAsync(string key)
    {
        var api = SaphirSession.Api;
        if (api is null)
            return;
        await RunAsync(async () =>
        {
            Form = MeoForm.None;
            Person = await api.GetAsync<PersonDetailDto>("api/meo/personne?key=" + ApiClient.Q(key));
            ShowDetailOnCompact = true;
        });
    }

    private async Task LoadVehicleAsync(string plate)
    {
        var api = SaphirSession.Api;
        if (api is null)
            return;
        await RunAsync(async () =>
        {
            Form = MeoForm.None;
            Vehicle = await api.GetAsync<VehicleDetailDto>("api/meo/vehicule?plaque=" + ApiClient.Q(plate));
            ShowDetailOnCompact = true;
        });
    }

    private async Task LoadJournalAsync()
    {
        var api = SaphirSession.Api;
        if (api is null)
            return;
        await RunAsync(async () =>
        {
            var rows = await api.GetAsync<List<AuditEntryDto>>("api/meo/journal?limit=300");
            Journal.Clear();
            foreach (var r in rows)
                Journal.Add(new JournalRow(FormatDate(r.Date), r.Agent, r.Action, r.Cible));
            ResultsInfo = rows.Count + " entrées";
        });
    }

    private Task RefreshAsync()
    {
        if (IsJournalMode) return LoadJournalAsync();
        if (IsPersonMode && Person is not null) return LoadPersonAsync(Person.Key);
        if (IsVehicleMode && Vehicle is not null) return LoadVehicleAsync(Vehicle.Plaque);
        return SearchAsync();
    }

    /// <summary>Véhicule de la personne → bascule sur le SIV.</summary>
    private async Task OpenVehicleFromPersonAsync(VehicleSummaryDto vehicle)
    {
        SelectTab(VehicleTabs[1]);
        SearchText = vehicle.Plaque;
        await LoadVehicleAsync(vehicle.Plaque);
    }

    /// <summary>Titulaire du véhicule → bascule sur le TAJ.</summary>
    private async Task OpenTitulaireAsync()
    {
        var key = Vehicle?.Siv.TitulaireKey;
        if (key is null)
            return;
        SelectTab(PersonTabs[0]);
        SearchText = Vehicle?.Siv.Titulaire ?? string.Empty;
        await LoadPersonAsync(key);
    }

    private void OpenForm(MeoForm form)
    {
        ErrorText = null;
        InfoText = null;
        switch (form)
        {
            case MeoForm.NewPerson:
                IdentityForm = new IdentityForm();
                break;
            case MeoForm.Identity when Person is not null:
                IdentityForm = IdentityForm.From(Person.Identite);
                break;
            case MeoForm.Taj:
                TajForm = new TajForm();
                break;
            case MeoForm.Fpr:
                FprForm = new FprForm();
                break;
            case MeoForm.Permis:
                PermisForm = PermisForm.From(Person?.Permis);
                break;
            case MeoForm.Vehicle:
                VehicleForm = _editExistingVehicle && Vehicle is not null
                    ? VehicleForm.From(Vehicle)
                    : new VehicleForm { Plaque = SearchText.Trim().ToUpperInvariant() };
                break;
            case MeoForm.Foves:
                FovesForm = new FovesForm { Plaque = Vehicle?.Plaque ?? SearchText.Trim().ToUpperInvariant() };
                if (string.IsNullOrWhiteSpace(FovesForm.Plaque))
                {
                    ErrorText = "Saisis d'abord la plaque à signaler dans le champ de recherche.";
                    return;
                }
                break;
            default:
                return;
        }
        Form = form;
        ShowDetailOnCompact = true;
    }

    private bool _editExistingVehicle;

    /// <summary>« Modifier » sur une fiche véhicule ouverte, ou « Nouveau véhicule » depuis la recherche.</summary>
    private void OpenVehicleForm(bool existing)
    {
        _editExistingVehicle = existing;
        OpenForm(MeoForm.Vehicle);
    }

    private async Task SaveFormAsync()
    {
        var api = SaphirSession.Api;
        if (api is null)
            return;

        await RunAsync(async () =>
        {
            switch (Form)
            {
                case MeoForm.NewPerson:
                {
                    var created = await api.PostAsync<CreatedPerson>("api/meo/personnes", new PersonInput(IdentityForm.ToDto()));
                    Form = MeoForm.None;
                    Person = await api.GetAsync<PersonDetailDto>("api/meo/personne?key=" + ApiClient.Q(created.Key));
                    InfoText = "Fiche personne créée.";
                    break;
                }
                case MeoForm.Identity when Person is not null:
                    await api.SendAsync(HttpMethod.Put, "api/meo/personne/identite?key=" + ApiClient.Q(Person.Key), IdentityForm.ToDto());
                    await ReloadPersonAsync(api, "Identité mise à jour.");
                    break;
                case MeoForm.Taj when Person is not null:
                    await api.SendAsync(HttpMethod.Post, "api/meo/personne/taj?key=" + ApiClient.Q(Person.Key), TajForm.ToInput());
                    await ReloadPersonAsync(api, "Inscription TAJ enregistrée.");
                    break;
                case MeoForm.Fpr when Person is not null:
                    await api.SendAsync(HttpMethod.Post, "api/meo/personne/fpr?key=" + ApiClient.Q(Person.Key), FprForm.ToInput());
                    await ReloadPersonAsync(api, "Inscription au FPR enregistrée.");
                    break;
                case MeoForm.Permis when Person is not null:
                    await api.SendAsync(HttpMethod.Put, "api/meo/personne/permis?key=" + ApiClient.Q(Person.Key), PermisForm.ToInput());
                    await ReloadPersonAsync(api, "Permis de conduire mis à jour.");
                    break;
                case MeoForm.Vehicle:
                {
                    var input = VehicleForm.ToInput();
                    await api.SendAsync(HttpMethod.Put, "api/meo/vehicule", input);
                    Form = MeoForm.None;
                    Vehicle = await api.GetAsync<VehicleDetailDto>("api/meo/vehicule?plaque=" + ApiClient.Q(input.Plaque));
                    InfoText = "Véhicule enregistré au SIV / FVA.";
                    break;
                }
                case MeoForm.Foves:
                {
                    var plate = FovesForm.Plaque;
                    await api.SendAsync(HttpMethod.Post, "api/meo/vehicule/foves?plaque=" + ApiClient.Q(plate), FovesForm.ToInput());
                    Form = MeoForm.None;
                    Vehicle = await api.GetAsync<VehicleDetailDto>("api/meo/vehicule?plaque=" + ApiClient.Q(plate));
                    InfoText = "Signalement FOVeS enregistré.";
                    break;
                }
            }
        });
    }

    private async Task ReloadPersonAsync(ApiClient api, string message)
    {
        Form = MeoForm.None;
        Person = await api.GetAsync<PersonDetailDto>("api/meo/personne?key=" + ApiClient.Q(Person!.Key));
        InfoText = message;
    }

    private async Task CloseEntryAsync(string path, string message)
    {
        var api = SaphirSession.Api;
        if (api is null)
            return;
        await RunAsync(async () =>
        {
            await api.SendAsync(HttpMethod.Post, path);
            await RefreshDetailAsync(api);
            InfoText = message;
        });
    }

    private async Task DeleteEntryAsync(string kind, long id)
    {
        var api = SaphirSession.Api;
        if (api is null)
            return;
        await RunAsync(async () =>
        {
            await api.SendAsync(HttpMethod.Delete, $"api/meo/inscription/{kind}/{id}");
            await RefreshDetailAsync(api);
            InfoText = "Inscription supprimée.";
        });
    }

    private async Task RefreshDetailAsync(ApiClient api)
    {
        if (IsPersonMode && Person is not null)
            Person = await api.GetAsync<PersonDetailDto>("api/meo/personne?key=" + ApiClient.Q(Person.Key));
        else if (IsVehicleMode && Vehicle is not null)
            Vehicle = await api.GetAsync<VehicleDetailDto>("api/meo/vehicule?plaque=" + ApiClient.Q(Vehicle.Plaque));
    }

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

    // =====================================================================
    // Construction des fiches
    // =====================================================================

    private void RebuildPerson()
    {
        IdentityRows.Clear();
        TajCards.Clear();
        FprCards.Clear();
        PermisRows.Clear();
        PersonVehicles.Clear();
        PersonAlert = null;
        PersonAlertDetail = null;

        var person = Person;
        if (person is not null)
        {
            var id = person.Identite;
            AddRow(IdentityRows, "Nom", id.Nom);
            AddRow(IdentityRows, "Prénom(s)", id.Prenom);
            AddRow(IdentityRows, "Alias", id.Alias);
            AddRow(IdentityRows, "Sexe", id.Sexe);
            AddRow(IdentityRows, "Date de naissance", id.DateNaissance);
            AddRow(IdentityRows, "Lieu de naissance", id.LieuNaissance);
            AddRow(IdentityRows, "Nationalité", id.Nationalite);
            AddRow(IdentityRows, "Adresse", id.Adresse);
            AddRow(IdentityRows, "Téléphone", id.Telephone);
            AddRow(IdentityRows, "Taille", id.Taille);
            AddRow(IdentityRows, "Signalement", id.Signalement);

            var admin = IsAdmin;
            foreach (var t in person.Taj)
            {
                var entryId = t.Id;
                TajCards.Add(new EntryCard
                {
                    Title = t.Infraction,
                    Badge = t.Qualite.ToUpperInvariant(),
                    Fields = Rows(("Date des faits", t.DateFaits), ("Lieu", t.Lieu), ("NATINF", t.Natinf),
                        ("Service", t.Service), ("Procédure", t.Procedure)),
                    Details = t.Details,
                    Footer = $"Inscrit par {t.Auteur} le {FormatDate(t.CreeLe)}",
                    DeleteCommand = admin ? new RelayCommand(() => _ = DeleteEntryAsync("taj", entryId)) : null
                });
            }

            foreach (var f in person.Fpr)
            {
                var entryId = f.Id;
                var flags = string.Join(" · ", new[] { f.Dangereux ? "DANGEREUX" : null, f.Arme ? "ARMÉ" : null }.Where(x => x is not null));
                FprCards.Add(new EntryCard
                {
                    Title = f.Motif,
                    Badge = f.Actif ? (flags.Length > 0 ? "EN VIGUEUR · " + flags : "EN VIGUEUR") : "CLOSE",
                    IsAlert = f.Actif,
                    IsClosed = !f.Actif,
                    Fields = Rows(("Conduite à tenir", f.ConduiteATenir), ("Autorité", f.Autorite)),
                    Details = f.Details,
                    Footer = $"Inscrit par {f.Auteur} le {FormatDate(f.CreeLe)}" +
                             (f.ClosLe is { } closed ? $" · close par {f.ClosPar} le {FormatDate(closed)}" : string.Empty),
                    CloseCommand = f.Actif ? new RelayCommand(() => _ = CloseEntryAsync($"api/meo/fpr/{entryId}/clore", "Fiche FPR close.")) : null,
                    DeleteCommand = admin ? new RelayCommand(() => _ = DeleteEntryAsync("fpr", entryId)) : null
                });
            }

            var active = person.Fpr.Where(f => f.Actif).ToList();
            if (active.Count > 0)
            {
                PersonAlert = active.Count == 1
                    ? "PERSONNE INSCRITE AU FPR — " + active[0].Motif
                    : $"PERSONNE INSCRITE AU FPR — {active.Count} fiches en vigueur";
                var danger = active.Any(f => f.Arme) ? "Susceptible d'être armé. " : string.Empty;
                danger += active.Any(f => f.Dangereux) ? "Individu dangereux. " : string.Empty;
                PersonAlertDetail = danger + "Conduite à tenir : " + active[0].ConduiteATenir;
            }

            if (person.Permis is { } permis)
            {
                AddRow(PermisRows, "Statut", permis.Statut);
                AddRow(PermisRows, "Catégories", permis.Categories);
                AddRow(PermisRows, "Solde de points", permis.Points + " / 12");
                AddRow(PermisRows, "N° de permis", permis.Numero);
                AddRow(PermisRows, "Délivré le", permis.DateObtention);
                AddRow(PermisRows, "Restrictions", permis.Restrictions);
                AddRow(PermisRows, "Mise à jour", permis.MisAJourLe is { } at ? $"{permis.MisAJourPar}, le {FormatDate(at)}" : permis.MisAJourPar);
            }

            foreach (var v in person.Vehicules)
                PersonVehicles.Add(v);
        }

        OnPropertyChanged(nameof(HasPerson));
        OnPropertyChanged(nameof(PersonTitle));
        OnPropertyChanged(nameof(PersonSubtitle));
        OnPropertyChanged(nameof(PersonAlert));
        OnPropertyChanged(nameof(PersonAlertDetail));
        OnPropertyChanged(nameof(HasPersonAlert));
        OnPropertyChanged(nameof(HasTaj));
        OnPropertyChanged(nameof(HasFpr));
        OnPropertyChanged(nameof(HasPermis));
        OnPropertyChanged(nameof(HasPersonVehicles));
    }

    private void RebuildVehicle()
    {
        SivRows.Clear();
        FvaRows.Clear();
        FovesCards.Clear();
        VehicleAlert = null;

        var vehicle = Vehicle;
        if (vehicle is not null)
        {
            var siv = vehicle.Siv;
            AddRow(SivRows, "Immatriculation", vehicle.Plaque);
            AddRow(SivRows, "Marque", siv.Marque);
            AddRow(SivRows, "Modèle", siv.Modele);
            AddRow(SivRows, "Couleur", siv.Couleur);
            AddRow(SivRows, "N° VIN", siv.Vin);
            AddRow(SivRows, "1re mise en circulation", siv.DateMiseEnCirculation);
            AddRow(SivRows, "Titulaire", siv.Titulaire ?? "Inconnu");
            AddRow(SivRows, "Contrôle technique", siv.ControleTechnique);
            AddRow(SivRows, "Opposition / gage", siv.Opposition);
            AddRow(SivRows, "Source", siv.Source);

            var fva = vehicle.Fva;
            AddRow(FvaRows, "Situation", fva.Statut);
            AddRow(FvaRows, "Assureur", fva.Assureur);
            AddRow(FvaRows, "N° de contrat", fva.NumeroContrat);
            AddRow(FvaRows, "Valide jusqu'au", fva.ValideJusquau);

            var admin = IsAdmin;
            foreach (var f in vehicle.Foves)
            {
                var entryId = f.Id;
                FovesCards.Add(new EntryCard
                {
                    Title = f.Motif,
                    Badge = f.Actif ? "EN VIGUEUR" : "CLOS",
                    IsAlert = f.Actif,
                    IsClosed = !f.Actif,
                    Fields = Rows(("Conduite à tenir", f.ConduiteATenir)),
                    Details = f.Details,
                    Footer = $"Signalé par {f.Auteur} le {FormatDate(f.CreeLe)}" +
                             (f.ClosLe is { } closed ? $" · clos par {f.ClosPar} le {FormatDate(closed)}" : string.Empty),
                    CloseCommand = f.Actif ? new RelayCommand(() => _ = CloseEntryAsync($"api/meo/foves/{entryId}/clore", "Signalement FOVeS clos.")) : null,
                    DeleteCommand = admin ? new RelayCommand(() => _ = DeleteEntryAsync("foves", entryId)) : null
                });
            }

            var active = vehicle.Foves.Where(f => f.Actif).ToList();
            if (active.Count > 0)
                VehicleAlert = "VÉHICULE SIGNALÉ AU FOVeS — " + active[0].Motif + " · " + active[0].ConduiteATenir;
        }

        OnPropertyChanged(nameof(HasVehicle));
        OnPropertyChanged(nameof(VehicleTitle));
        OnPropertyChanged(nameof(VehicleSubtitle));
        OnPropertyChanged(nameof(VehicleAlert));
        OnPropertyChanged(nameof(HasVehicleAlert));
        OnPropertyChanged(nameof(HasFoves));
        OnPropertyChanged(nameof(HasTitulaire));
        OnPropertyChanged(nameof(FvaStatus));
        OnPropertyChanged(nameof(IsInsured));
    }

    private void RaiseDetailState()
    {
        foreach (var name in new[]
                 {
                     nameof(ShowPersonDetail), nameof(ShowTaj), nameof(ShowFpr), nameof(ShowSnpc),
                     nameof(ShowVehicleDetail), nameof(ShowFoves), nameof(ShowSiv), nameof(ShowFva), nameof(ShowEmpty),
                     nameof(IsFormOpen), nameof(IsNewPersonForm), nameof(IsIdentityForm), nameof(IsTajForm), nameof(IsFprForm),
                     nameof(IsPermisForm), nameof(IsVehicleForm), nameof(IsFovesForm), nameof(FormTitle)
                 })
            OnPropertyChanged(name);
    }

    private static void AddRow(ICollection<FieldRow> rows, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            rows.Add(new FieldRow(label, value));
    }

    private static List<FieldRow> Rows(params (string Label, string? Value)[] values)
        => values.Where(v => !string.IsNullOrWhiteSpace(v.Value)).Select(v => new FieldRow(v.Label, v.Value!)).ToList();

    private static string FormatDate(DateTimeOffset date) => date.ToLocalTime().ToString("dd/MM/yyyy à HH:mm", French);
}

/// <summary>Réponse du serveur à la création d'une fiche personne.</summary>
public sealed record CreatedPerson(string Key);
