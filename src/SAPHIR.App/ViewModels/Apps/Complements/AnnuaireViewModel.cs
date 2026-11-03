using System.Collections.ObjectModel;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Complements;

namespace Saphir.ViewModels.Apps.Complements;

/// <summary>Ligne de l'annuaire du personnel : informations RH + coordonnées professionnelles.</summary>
public sealed class AgentAnnuaireRow(AgentResumeDto agent, CoordonneesDto? coordonnees)
{
    public AgentResumeDto Agent { get; } = agent;
    public CoordonneesDto? Coordonnees { get; } = coordonnees;
    public long Id => Agent.Id;
    public string Reference => Agent.Reference;
    public string Nom => Agent.Nom;
    public string Prenom => Agent.Prenom;
    public string NomComplet => $"{Agent.Nom} {Agent.Prenom}";
    public string Grade => Agent.Grade ?? "—";
    public string Rio => Agent.Rio ?? "—";
    public string Service => Agent.Service ?? "—";
    public string Unite => Agent.Unite ?? "—";
    public string Fonction => Agent.Fonction ?? "—";
    public string Telephone => Coordonnees?.Telephone ?? "—";
    public string Indicatif => Coordonnees?.Indicatif ?? "—";
    public string Poste => Coordonnees?.Poste ?? "—";
    public string Email => Coordonnees?.Email ?? "—";
    public string Ligne2 => string.Join(" · ", new[] { Agent.Grade, Agent.Service, Agent.Unite }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public string Contact => string.Join(" · ", new[] { Coordonnees?.Telephone, Coordonnees?.Indicatif }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public bool HasContact => Contact.Length > 0;
}

/// <summary>
/// ANNUAIRE : personnel actif (synchronisé avec RH), fiche contact, annuaire des services (numéros fictifs gérés par
/// l'administration) et coordonnées professionnelles de l'agent connecté.
/// </summary>
public sealed class AnnuaireViewModel : ComplementsViewModelBase
{
    public const string SectionPersonnel = "personnel";
    public const string SectionServices = "services";
    public const string SectionCoordonnees = "coordonnees";
    private const string Tous = "Tous";

    private List<AgentAnnuaireRow> _tous = [];
    private string _recherche = string.Empty;
    private string _filtreService = Tous;
    private string _filtreUnite = Tous;
    private string _filtreGrade = Tous;
    private AgentAnnuaireRow? _selection;
    private ServiceAnnuaireDto? _serviceSelectionne;
    private string _rechercheService = string.Empty;
    private List<ServiceAnnuaireDto> _services = [];
    private bool _editionCoordonnees;
    private string? _argument;

    public AnnuaireViewModel(string? argument = null)
    {
        _argument = argument ?? SaphirSession.TakeAppArgument(SaphirApps.Annuaire);
        AjouterSection(SectionPersonnel, "Personnel");
        AjouterSection(SectionServices, "Annuaire des services");
        AjouterSection(SectionCoordonnees, "Mes coordonnées");
        Section = SectionPersonnel;

        ActualiserCommand = new RelayCommand(() => _ = ChargerAsync(true));
        EcrireCommand = new RelayCommand(Ecrire);
        EnregistrerMesCoordonneesCommand = new RelayCommand(() => _ = EnregistrerCoordonneesAsync(null));
        ModifierCoordonneesCommand = new RelayCommand(() => EditionCoordonnees = true);
        EnregistrerCoordonneesAgentCommand = new RelayCommand(() => _ = EnregistrerCoordonneesAsync(Selection?.Id));
        AnnulerCoordonneesCommand = new RelayCommand(() => EditionCoordonnees = false);
        NouveauServiceCommand = new RelayCommand(NouveauService);
        EnregistrerServiceCommand = new RelayCommand(() => _ = EnregistrerServiceAsync());
        RetourListeCommand = new RelayCommand(() => Selection = null);

        _ = ChargerAsync(false);
    }

    public override string TitreMenu => "ANNUAIRE";

    public bool IsPersonnel => Section == SectionPersonnel;
    public bool IsServices => Section == SectionServices;
    public bool IsCoordonnees => Section == SectionCoordonnees;

    protected override void OnSectionChanged()
    {
        OnPropertyChanged(nameof(IsPersonnel));
        OnPropertyChanged(nameof(IsServices));
        OnPropertyChanged(nameof(IsCoordonnees));
    }

    protected override void OnCompactChanged() => RafraichirPanneaux();

    public bool CanGererServices => SaphirSession.Has(Permissions.AdminParametres);
    public bool CanGererComptes => SaphirSession.Has(Permissions.AdminComptes);
    public bool CanEcrire => SaphirSession.CanOpen(SaphirApps.Messagerie);

    // ===== Personnel =====

    public ObservableCollection<AgentAnnuaireRow> Agents { get; } = [];
    public ObservableCollection<string> Services { get; } = [Tous];
    public ObservableCollection<string> Unites { get; } = [Tous];
    public ObservableCollection<string> Grades { get; } = [Tous];

    public string Recherche { get => _recherche; set { if (SetProperty(ref _recherche, value)) Filtrer(); } }
    public string FiltreService { get => _filtreService; set { if (SetProperty(ref _filtreService, value ?? Tous)) Filtrer(); } }
    public string FiltreUnite { get => _filtreUnite; set { if (SetProperty(ref _filtreUnite, value ?? Tous)) Filtrer(); } }
    public string FiltreGrade { get => _filtreGrade; set { if (SetProperty(ref _filtreGrade, value ?? Tous)) Filtrer(); } }
    public string Compte => $"{Agents.Count} agent(s) sur {_tous.Count}";

    public AgentAnnuaireRow? Selection
    {
        get => _selection;
        set
        {
            if (!SetProperty(ref _selection, value)) return;
            EditionCoordonnees = false;
            EditTelephone = value?.Coordonnees?.Telephone;
            EditIndicatif = value?.Coordonnees?.Indicatif;
            EditPoste = value?.Coordonnees?.Poste;
            EditEmail = value?.Coordonnees?.Email;
            OnPropertyChanged(nameof(HasSelection));
            RafraichirPanneaux();
        }
    }

    public bool HasSelection => Selection is not null;

    /// <summary>Téléphone : la liste et la fiche ne s'affichent pas en même temps.</summary>
    public bool AfficherListe => IsLarge || Selection is null;
    public bool AfficherFiche => IsLarge || Selection is not null;

    private void RafraichirPanneaux()
    {
        OnPropertyChanged(nameof(AfficherListe));
        OnPropertyChanged(nameof(AfficherFiche));
    }

    public bool EditionCoordonnees
    {
        get => _editionCoordonnees;
        set
        {
            if (SetProperty(ref _editionCoordonnees, value))
                OnPropertyChanged(nameof(PeutModifierCoordonnees));
        }
    }

    public bool PeutModifierCoordonnees => CanGererComptes && !EditionCoordonnees;

    // Coordonnées en édition (les miennes, ou celles de l'agent sélectionné pour un administrateur)
    private string? _editTelephone, _editIndicatif, _editPoste, _editEmail;
    public string? EditTelephone { get => _editTelephone; set => SetProperty(ref _editTelephone, value); }
    public string? EditIndicatif { get => _editIndicatif; set => SetProperty(ref _editIndicatif, value); }
    public string? EditPoste { get => _editPoste; set => SetProperty(ref _editPoste, value); }
    public string? EditEmail { get => _editEmail; set => SetProperty(ref _editEmail, value); }

    private string? _monTelephone, _monIndicatif, _monPoste, _monEmail;
    public string? MonTelephone { get => _monTelephone; set => SetProperty(ref _monTelephone, value); }
    public string? MonIndicatif { get => _monIndicatif; set => SetProperty(ref _monIndicatif, value); }
    public string? MonPoste { get => _monPoste; set => SetProperty(ref _monPoste, value); }
    public string? MonEmail { get => _monEmail; set => SetProperty(ref _monEmail, value); }
    public string MonIdentite => SaphirSession.Agent is { } a
        ? $"{a.Nom} {a.Prenom} — {a.Grade ?? "grade non renseigné"} — RIO {a.Rio ?? "non attribué"}"
        : string.Empty;
    public string MonAffectation => SaphirSession.Agent is { } a
        ? string.Join(" · ", new[] { a.Service, a.Unite, a.Fonction }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } t ? t : "Affectation non renseignée par le service RH."
        : string.Empty;

    // ===== Services =====

    public ObservableCollection<ServiceAnnuaireDto> ListeServices { get; } = [];
    public string RechercheService { get => _rechercheService; set { if (SetProperty(ref _rechercheService, value)) FiltrerServices(); } }

    public ServiceAnnuaireDto? ServiceSelectionne
    {
        get => _serviceSelectionne;
        set
        {
            if (!SetProperty(ref _serviceSelectionne, value)) return;
            if (value is not null) RemplirService(value);
            OnPropertyChanged(nameof(HasServiceSelectionne));
            OnPropertyChanged(nameof(EditionServiceVisible));
        }
    }

    public bool HasServiceSelectionne => ServiceSelectionne is not null;
    private bool _nouveauService;
    public bool EditionServiceVisible => CanGererServices && (ServiceSelectionne is not null || _nouveauService);
    public string TitreEditionService => _nouveauService ? "Nouveau service" : "Modifier le service";

    private string _sNom = string.Empty;
    private string? _sCategorie, _sIndicatif, _sTelephone, _sFrequence, _sAdresse, _sHoraires, _sObservations;
    private string _sOrdre = "100";
    private bool _sActif = true;
    public string SNom { get => _sNom; set => SetProperty(ref _sNom, value); }
    public string? SCategorie { get => _sCategorie; set => SetProperty(ref _sCategorie, value); }
    public string? SIndicatif { get => _sIndicatif; set => SetProperty(ref _sIndicatif, value); }
    public string? STelephone { get => _sTelephone; set => SetProperty(ref _sTelephone, value); }
    public string? SFrequence { get => _sFrequence; set => SetProperty(ref _sFrequence, value); }
    public string? SAdresse { get => _sAdresse; set => SetProperty(ref _sAdresse, value); }
    public string? SHoraires { get => _sHoraires; set => SetProperty(ref _sHoraires, value); }
    public string? SObservations { get => _sObservations; set => SetProperty(ref _sObservations, value); }
    public string SOrdre { get => _sOrdre; set => SetProperty(ref _sOrdre, value); }
    public bool SActif { get => _sActif; set => SetProperty(ref _sActif, value); }

    // ===== Commandes =====

    public RelayCommand ActualiserCommand { get; }
    public RelayCommand EcrireCommand { get; }
    public RelayCommand EnregistrerMesCoordonneesCommand { get; }
    public RelayCommand ModifierCoordonneesCommand { get; }
    public RelayCommand EnregistrerCoordonneesAgentCommand { get; }
    public RelayCommand AnnulerCoordonneesCommand { get; }
    public RelayCommand NouveauServiceCommand { get; }
    public RelayCommand EnregistrerServiceCommand { get; }
    public RelayCommand RetourListeCommand { get; }

    // ===== Chargement =====

    private async Task ChargerAsync(bool forcer)
    {
        var api = Api;
        if (api is null) return;
        await RunAsync(async () =>
        {
            var agents = await AgentsSaphir.ListeAsync(forcer);
            var coordonnees = await api.GetAsync<List<CoordonneesDto>>("api/annuaire/coordonnees");
            var parAgent = coordonnees.ToDictionary(c => c.AgentId);
            _tous = agents.Where(a => a.Statut == AgentStatuts.Actif)
                .Select(a => new AgentAnnuaireRow(a, parAgent.GetValueOrDefault(a.Id))).ToList();

            await RemplirFiltresAsync(api);
            Filtrer();

            if (SaphirSession.Agent is { } moi && parAgent.TryGetValue(moi.Id, out var mes))
            {
                MonTelephone = mes.Telephone;
                MonIndicatif = mes.Indicatif;
                MonPoste = mes.Poste;
                MonEmail = mes.Email;
            }

            await ChargerServicesAsync(api);

            if (_argument is { } arg)
            {
                _argument = null;
                Selection = _tous.FirstOrDefault(r => string.Equals(r.Reference, arg, StringComparison.OrdinalIgnoreCase)
                                                      || (long.TryParse(arg, out var id) && r.Id == id));
            }
        });
    }

    /// <summary>Listes de filtres : référentiels RH si disponibles, sinon valeurs présentes dans l'annuaire.</summary>
    private async Task RemplirFiltresAsync(ApiClient api)
    {
        static void Remplir(ObservableCollection<string> cible, IEnumerable<string?> valeurs)
        {
            cible.Clear();
            cible.Add(Tous);
            foreach (var v in valeurs.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).Distinct().Order(StringComparer.Create(Fr, true)))
                cible.Add(v);
        }

        async Task<IEnumerable<string?>> Referentiel(string route, Func<AgentAnnuaireRow, string?> repli)
        {
            try
            {
                var liste = await api.GetAsync<List<ReferentielDto>>(route);
                if (liste.Count > 0)
                    return liste.Where(r => r.Actif).OrderBy(r => r.Ordre).Select(r => (string?)r.Libelle);
            }
            catch (ApiException)
            {
                // Module RH absent : valeurs de l'annuaire.
            }
            return _tous.Select(repli);
        }

        var service = FiltreService;
        var unite = FiltreUnite;
        var grade = FiltreGrade;
        Remplir(Services, await Referentiel("api/referentiels/services", r => r.Agent.Service));
        Remplir(Unites, await Referentiel("api/referentiels/unites", r => r.Agent.Unite));
        Remplir(Grades, await Referentiel("api/referentiels/grades", r => r.Agent.Grade));
        _filtreService = Services.Contains(service) ? service : Tous;
        _filtreUnite = Unites.Contains(unite) ? unite : Tous;
        _filtreGrade = Grades.Contains(grade) ? grade : Tous;
        OnPropertyChanged(nameof(FiltreService));
        OnPropertyChanged(nameof(FiltreUnite));
        OnPropertyChanged(nameof(FiltreGrade));
    }

    private void Filtrer()
    {
        var id = Selection?.Id;
        Agents.Clear();
        foreach (var r in _tous.Where(r =>
                     AgentsSaphir.Correspond(r.Agent, Recherche)
                     && (FiltreService == Tous || r.Agent.Service == FiltreService)
                     && (FiltreUnite == Tous || r.Agent.Unite == FiltreUnite)
                     && (FiltreGrade == Tous || r.Agent.Grade == FiltreGrade || r.Agent.GradeAbrege == FiltreGrade)))
            Agents.Add(r);
        OnPropertyChanged(nameof(Compte));
        if (id is { } i)
            Selection = _tous.FirstOrDefault(r => r.Id == i);
    }

    private async Task ChargerServicesAsync(ApiClient api)
    {
        _services = await api.GetAsync<List<ServiceAnnuaireDto>>("api/annuaire/services" + (CanGererServices ? "?tous=true" : string.Empty));
        FiltrerServices();
    }

    private void FiltrerServices()
    {
        var id = ServiceSelectionne?.Id;
        ListeServices.Clear();
        var r = RechercheService.Trim();
        foreach (var s in _services.Where(s => r.Length == 0
                                               || s.Nom.Contains(r, StringComparison.OrdinalIgnoreCase)
                                               || (s.Categorie?.Contains(r, StringComparison.OrdinalIgnoreCase) ?? false)
                                               || (s.Indicatif?.Contains(r, StringComparison.OrdinalIgnoreCase) ?? false)))
            ListeServices.Add(s);
        if (id is { } i)
            ServiceSelectionne = ListeServices.FirstOrDefault(s => s.Id == i);
    }

    // ===== Actions =====

    private void Ecrire()
    {
        if (Selection is { } s)
            OuvrirApplication(SaphirApps.Messagerie, "a:" + s.Id);
    }

    private async Task EnregistrerCoordonneesAsync(long? agentId)
    {
        var api = Api;
        if (api is null) return;
        var input = agentId is null
            ? new CoordonneesInput(MonTelephone, MonIndicatif, MonPoste, MonEmail)
            : new CoordonneesInput(EditTelephone, EditIndicatif, EditPoste, EditEmail);
        await RunAsync(async () =>
        {
            await api.SendAsync<CoordonneesDto>(HttpMethod.Put,
                agentId is { } id ? $"api/annuaire/coordonnees/{id}" : "api/annuaire/coordonnees/moi", input);
            EditionCoordonnees = false;
            await ChargerAsync(false);
        }, "Coordonnées professionnelles enregistrées.");
    }

    private void NouveauService()
    {
        _nouveauService = true;
        ServiceSelectionne = null;
        SNom = string.Empty;
        SCategorie = SIndicatif = STelephone = SFrequence = SAdresse = SHoraires = SObservations = null;
        SOrdre = "100";
        SActif = true;
        OnPropertyChanged(nameof(EditionServiceVisible));
        OnPropertyChanged(nameof(TitreEditionService));
    }

    private void RemplirService(ServiceAnnuaireDto s)
    {
        _nouveauService = false;
        SNom = s.Nom;
        SCategorie = s.Categorie;
        SIndicatif = s.Indicatif;
        STelephone = s.Telephone;
        SFrequence = s.Frequence;
        SAdresse = s.Adresse;
        SHoraires = s.Horaires;
        SObservations = s.Observations;
        SOrdre = s.Ordre.ToString(Fr);
        SActif = s.Actif;
        OnPropertyChanged(nameof(TitreEditionService));
    }

    private async Task EnregistrerServiceAsync()
    {
        var api = Api;
        if (api is null) return;
        if (!int.TryParse(SOrdre, out var ordre))
        {
            ErrorText = "L'ordre d'affichage est un nombre entier.";
            return;
        }
        var input = new ServiceAnnuaireInput(SNom, SCategorie, SIndicatif, STelephone, SFrequence, SAdresse, SHoraires, SObservations, ordre, SActif);
        var existant = _nouveauService ? null : ServiceSelectionne;
        await RunAsync(async () =>
        {
            var dto = existant is null
                ? await api.PostAsync<ServiceAnnuaireDto>("api/annuaire/services", input)
                : await api.SendAsync<ServiceAnnuaireDto>(HttpMethod.Put, $"api/annuaire/services/{existant.Id}", input);
            _nouveauService = false;
            await ChargerServicesAsync(api);
            ServiceSelectionne = ListeServices.FirstOrDefault(s => s.Id == dto.Id);
        }, "Service enregistré.");
    }

    protected override string? IdApplication => SaphirApps.Annuaire;

    protected override void OnArgument(string argument)
    {
        Section = SectionPersonnel;
        Selection = _tous.FirstOrDefault(r => string.Equals(r.Reference, argument, StringComparison.OrdinalIgnoreCase)
                                              || (long.TryParse(argument, out var id) && r.Id == id));
    }

    protected override void OnRealtime(RealtimeMessage message)
    {
        if (message.Type == ComplementsRealtime.Annuaire)
            _ = ChargerAsync(false);
    }
}
