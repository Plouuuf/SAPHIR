using System.Collections.ObjectModel;
using System.Globalization;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Operations;

namespace Saphir.ViewModels.Apps.Operations;

public sealed class VehiculeLigne(VehiculeServiceDto dto)
{
    public VehiculeServiceDto Dto { get; } = dto;
    public long Id => Dto.Id;
    public string Immatriculation => Dto.Immatriculation;
    public string Modele => string.Join(" ", new[] { Dto.Marque, Dto.Modele }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public string Type => Dto.Type;
    public string Indicatif => Dto.IndicatifHabituel ?? "—";
    public string Kilometrage => Dto.Kilometrage.ToString("N0", Fmt.Fr) + " km";
    public string Disponibilite => Dto.Disponibilite;
    public string Equipage => Dto.EquipageIndicatif ?? "—";
}

public sealed class MouvementLigne(VehiculeMouvementDto dto)
{
    public VehiculeMouvementDto Dto { get; } = dto;
    public string Date => Fmt.DateHeure(Dto.Date);
    public string Type => Dto.Type;
    public string Equipage => Dto.Equipage ?? "—";
    public string Conducteur => Dto.Conducteur ?? "—";
    public string Kilometrage => Dto.Kilometrage.ToString("N0", Fmt.Fr);
    public string Carburant => Dto.Carburant ?? "—";
    public string Etat => string.Join(" — ", new[] { Dto.Etat, Dto.Anomalies, Dto.Observations }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public string Disponibilite => Dto.Disponibilite ?? string.Empty;
    public string Responsable => Dto.Responsable;
}

public sealed class McVehiculesSection : SectionViewModel
{
    private VehiculeLigne? _selection;
    private VehiculeServiceDto? _vehicule;
    private bool _edition;
    private string _immatriculation = string.Empty;
    private string _marque = string.Empty;
    private string _modele = string.Empty;
    private string _type = MainCouranteListes.TypesVehicule[0];
    private string _service = string.Empty;
    private string _indicatif = string.Empty;
    private string _kilometrage = "0";
    private string _etat = string.Empty;
    private string _observations = string.Empty;
    private EquipageDto? _equipageSortie;
    private AgentResumeDto? _conducteur;
    private string _heureSortie = Fmt.Maintenant();
    private string _kmSortie = string.Empty;
    private string _carburantSortie = "Plein";
    private string _etatSortie = string.Empty;
    private string _anomaliesSortie = string.Empty;
    private string _heureRetour = Fmt.Maintenant();
    private string _kmRetour = string.Empty;
    private string _carburantRetour = "1/2";
    private string _etatRetour = string.Empty;
    private string _degradations = string.Empty;
    private string _observationsRetour = string.Empty;
    private bool _disponibleRetour = true;
    private string _motifDisponibilite = string.Empty;

    public McVehiculesSection(MainCouranteViewModel host) : base(host, "Véhicules de service")
    {
        NouveauCommand = new RelayCommand(Nouveau);
        EnregistrerCommand = new RelayCommand(() => _ = EnregistrerAsync());
        SortieCommand = new RelayCommand(() => _ = SortieAsync());
        RetourCommand = new RelayCommand(() => _ = RetourAsync());
        IndisponibleCommand = new RelayCommand(() => _ = DisponibiliteAsync(VehiculeDisponibilites.Indisponible));
        DisponibleCommand = new RelayCommand(() => _ = DisponibiliteAsync(VehiculeDisponibilites.Disponible));
    }

    public bool PeutGerer => MainCouranteViewModel.PeutGererVehicules;
    public IReadOnlyList<string> Types => MainCouranteListes.TypesVehicule;
    public IReadOnlyList<string> Carburants => MainCouranteListes.NiveauxCarburant;

    public ObservableCollection<VehiculeLigne> Lignes { get; } = [];
    public ObservableCollection<MouvementLigne> Mouvements { get; } = [];
    public ObservableCollection<HistoriqueLigne> Historique { get; } = [];
    public ObservableCollection<EquipageDto> Equipages { get; } = [];
    public ObservableCollection<AgentResumeDto> Repertoire { get; } = [];

    public VehiculeLigne? Selection
    {
        get => _selection;
        set
        {
            if (SetProperty(ref _selection, value) && value is not null)
                _ = Host.Executer(() => ChargerFicheAsync(value.Id, true));
        }
    }

    public VehiculeServiceDto? Vehicule
    {
        get => _vehicule;
        private set
        {
            SetProperty(ref _vehicule, value);
            foreach (var nom in new[] { nameof(PeutSortir), nameof(PeutRentrer), nameof(PeutRendreIndisponible), nameof(PeutRendreDisponible),
                         nameof(TitreEditeur), nameof(InfoVehicule), nameof(EstExistant) })
                OnPropertyChanged(nom);
        }
    }

    public bool EnEdition { get => _edition; private set => SetProperty(ref _edition, value); }
    public bool EstExistant => Vehicule is not null;
    public bool PeutSortir => PeutGerer && Vehicule is { Disponibilite: VehiculeDisponibilites.Disponible };
    public bool PeutRentrer => PeutGerer && Vehicule is { Disponibilite: VehiculeDisponibilites.Sorti };
    public bool PeutRendreIndisponible => PeutGerer && Vehicule is { Disponibilite: VehiculeDisponibilites.Disponible };
    public bool PeutRendreDisponible => PeutGerer && Vehicule is { Disponibilite: VehiculeDisponibilites.Indisponible };
    public string TitreEditeur => Vehicule is null ? "Nouveau véhicule de service" : $"{Vehicule.Immatriculation} — {Vehicule.Disponibilite}";
    public string InfoVehicule => Vehicule is null ? "Fiche 10.3 : le kilométrage est ensuite tenu par les sorties et retours."
        : $"Référence {Vehicule.Reference}" + (Vehicule.EquipageIndicatif is { } e ? $" — affecté à l'équipage {e}" : string.Empty);

    public string Immatriculation { get => _immatriculation; set => SetProperty(ref _immatriculation, value); }
    public string Marque { get => _marque; set => SetProperty(ref _marque, value); }
    public string Modele { get => _modele; set => SetProperty(ref _modele, value); }
    public string Type { get => _type; set => SetProperty(ref _type, value); }
    public string Service { get => _service; set => SetProperty(ref _service, value); }
    public string Indicatif { get => _indicatif; set => SetProperty(ref _indicatif, value); }
    public string Kilometrage { get => _kilometrage; set => SetProperty(ref _kilometrage, value); }
    public string Etat { get => _etat; set => SetProperty(ref _etat, value); }
    public string Observations { get => _observations; set => SetProperty(ref _observations, value); }

    public EquipageDto? EquipageSortie { get => _equipageSortie; set => SetProperty(ref _equipageSortie, value); }
    public AgentResumeDto? Conducteur { get => _conducteur; set => SetProperty(ref _conducteur, value); }
    public string HeureSortie { get => _heureSortie; set => SetProperty(ref _heureSortie, value); }
    public string KmSortie { get => _kmSortie; set => SetProperty(ref _kmSortie, value); }
    public string CarburantSortie { get => _carburantSortie; set => SetProperty(ref _carburantSortie, value); }
    public string EtatSortie { get => _etatSortie; set => SetProperty(ref _etatSortie, value); }
    public string AnomaliesSortie { get => _anomaliesSortie; set => SetProperty(ref _anomaliesSortie, value); }
    public string HeureRetour { get => _heureRetour; set => SetProperty(ref _heureRetour, value); }
    public string KmRetour { get => _kmRetour; set => SetProperty(ref _kmRetour, value); }
    public string CarburantRetour { get => _carburantRetour; set => SetProperty(ref _carburantRetour, value); }
    public string EtatRetour { get => _etatRetour; set => SetProperty(ref _etatRetour, value); }
    public string Degradations { get => _degradations; set => SetProperty(ref _degradations, value); }
    public string ObservationsRetour { get => _observationsRetour; set => SetProperty(ref _observationsRetour, value); }
    public bool DisponibleRetour { get => _disponibleRetour; set => SetProperty(ref _disponibleRetour, value); }
    public string MotifDisponibilite { get => _motifDisponibilite; set => SetProperty(ref _motifDisponibilite, value); }

    public RelayCommand NouveauCommand { get; }
    public RelayCommand EnregistrerCommand { get; }
    public RelayCommand SortieCommand { get; }
    public RelayCommand RetourCommand { get; }
    public RelayCommand IndisponibleCommand { get; }
    public RelayCommand DisponibleCommand { get; }

    public override async Task ChargerAsync()
    {
        var vehicules = await Client.GetAsync<List<VehiculeServiceDto>>("api/main-courante/vehicules");
        var selection = _selection?.Id;
        _selection = null;
        Lignes.Clear();
        foreach (var v in vehicules)
            Lignes.Add(new VehiculeLigne(v));
        _selection = selection is { } id ? Lignes.FirstOrDefault(l => l.Id == id) : null;
        OnPropertyChanged(nameof(Selection));

        if (PeutGerer)
        {
            var equipages = await Client.GetAsync<List<EquipageDto>>("api/main-courante/equipages?actifs=true");
            var equipageId = EquipageSortie?.Id;
            Equipages.Clear();
            foreach (var e in equipages)
                Equipages.Add(e);
            EquipageSortie = equipageId is { } eid ? Equipages.FirstOrDefault(e => e.Id == eid) : null;
            if (Repertoire.Count == 0)
            {
                try
                {
                    foreach (var a in await RepertoireRh.AgentsAsync())
                        Repertoire.Add(a);
                }
                catch (ApiException ex)
                {
                    Host.Erreur(ex.Message);
                }
            }
        }
        if (Vehicule is { } courant)
            await ChargerFicheAsync(courant.Id, false);
    }

    private async Task ChargerFicheAsync(long id, bool remplirFormulaire)
    {
        var fiche = await Client.GetAsync<VehiculeFicheDto>($"api/main-courante/vehicules/{id}");
        var v = fiche.Vehicule;
        var changementDeVehicule = Vehicule?.Id != v.Id;
        Vehicule = v;
        if (remplirFormulaire || changementDeVehicule)
        {
            Immatriculation = v.Immatriculation;
            Marque = v.Marque ?? string.Empty;
            Modele = v.Modele ?? string.Empty;
            Type = v.Type;
            Service = v.Service ?? string.Empty;
            Indicatif = v.IndicatifHabituel ?? string.Empty;
            Etat = v.Etat ?? string.Empty;
            Observations = v.Observations ?? string.Empty;
            HeureSortie = Fmt.Maintenant();
            HeureRetour = Fmt.Maintenant();
            EquipageSortie = v.EquipageId is { } eid ? Equipages.FirstOrDefault(e => e.Id == eid) : null;
            AnomaliesSortie = string.Empty;
            Degradations = string.Empty;
            ObservationsRetour = string.Empty;
            MotifDisponibilite = string.Empty;
            DisponibleRetour = true;
            // Le kilométrage proposé est le dernier relevé.
            Kilometrage = v.Kilometrage.ToString(CultureInfo.InvariantCulture);
            KmSortie = Kilometrage;
            KmRetour = Kilometrage;
        }
        Mouvements.Clear();
        foreach (var m in fiche.Mouvements)
            Mouvements.Add(new MouvementLigne(m));
        Historique.Clear();
        foreach (var h in fiche.Historique)
            Historique.Add(new HistoriqueLigne(h));
        EnEdition = true;
    }

    private void Nouveau()
    {
        _selection = null;
        OnPropertyChanged(nameof(Selection));
        Vehicule = null;
        Immatriculation = string.Empty;
        Marque = string.Empty;
        Modele = string.Empty;
        Type = Types[0];
        Service = SaphirSession.Agent?.Service ?? string.Empty;
        Indicatif = string.Empty;
        Kilometrage = "0";
        Etat = "Bon état";
        Observations = string.Empty;
        Mouvements.Clear();
        Historique.Clear();
        EnEdition = true;
    }

    private async Task EnregistrerAsync()
    {
        await Host.Executer(async () =>
        {
            var input = new VehiculeServiceInput(Immatriculation, Fmt.Vide(Marque), Fmt.Vide(Modele), Type, Fmt.Vide(Service), Fmt.Vide(Indicatif),
                Fmt.LireEntier(Kilometrage, "Kilométrage") ?? 0, Fmt.Vide(Etat), Fmt.Vide(Observations), Vehicule?.Version);
            var enregistre = Vehicule is null
                ? await Client.PostAsync<VehiculeServiceDto>("api/main-courante/vehicules", input)
                : await Client.SendAsync<VehiculeServiceDto>(HttpMethod.Put, $"api/main-courante/vehicules/{Vehicule.Id}", input);
            Vehicule = enregistre;
            await ChargerAsync();
        }, "Fiche véhicule enregistrée.");
    }

    private async Task SortieAsync()
    {
        if (Vehicule is not { } v)
            return;
        await Host.Executer(async () =>
        {
            var input = new SortieVehiculeInput(EquipageSortie?.Id, Conducteur?.Id, Conducteur?.Libelle, Fmt.LireDateHeure(HeureSortie, "Heure de sortie"),
                Fmt.LireEntier(KmSortie, "Kilométrage de départ") ?? v.Kilometrage, CarburantSortie, Fmt.Vide(EtatSortie), Fmt.Vide(AnomaliesSortie));
            Vehicule = await Client.PostAsync<VehiculeServiceDto>($"api/main-courante/vehicules/{v.Id}/sortie", input);
            await ChargerFicheAsync(v.Id, true);
            await ChargerAsync();
        }, "Sortie du véhicule enregistrée.");
    }

    private async Task RetourAsync()
    {
        if (Vehicule is not { } v)
            return;
        await Host.Executer(async () =>
        {
            var input = new RetourVehiculeInput(Fmt.LireDateHeure(HeureRetour, "Heure de retour"), Fmt.LireEntier(KmRetour, "Kilométrage de retour") ?? v.Kilometrage,
                CarburantRetour, Fmt.Vide(EtatRetour), Fmt.Vide(Degradations), Fmt.Vide(ObservationsRetour), DisponibleRetour);
            Vehicule = await Client.PostAsync<VehiculeServiceDto>($"api/main-courante/vehicules/{v.Id}/retour", input);
            await ChargerFicheAsync(v.Id, true);
            await ChargerAsync();
        }, "Retour du véhicule enregistré.");
    }

    private async Task DisponibiliteAsync(string disponibilite)
    {
        if (Vehicule is not { } v)
            return;
        await Host.Executer(async () =>
        {
            Vehicule = await Client.PostAsync<VehiculeServiceDto>($"api/main-courante/vehicules/{v.Id}/disponibilite",
                new DisponibiliteVehiculeInput(disponibilite, Fmt.Vide(MotifDisponibilite)));
            await ChargerFicheAsync(v.Id, true);
            await ChargerAsync();
        }, $"Véhicule {disponibilite.ToLowerInvariant()}.");
    }

    public override bool Concerne(RealtimeMessage message)
        => Est(message, OperationsTypes.MainCouranteChanged) && message.Text is { } t &&
           (t.StartsWith("vehicule:", StringComparison.Ordinal) || t.StartsWith("equipage:", StringComparison.Ordinal));
}
