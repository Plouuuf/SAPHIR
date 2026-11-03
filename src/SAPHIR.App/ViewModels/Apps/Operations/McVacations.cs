using System.Collections.ObjectModel;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Operations;

namespace Saphir.ViewModels.Apps.Operations;

// =====================================================================================
// Vacations (10.1)
// =====================================================================================

public sealed class VacationLigne(VacationResumeDto dto)
{
    public VacationResumeDto Dto { get; } = dto;
    public long Id => Dto.Id;
    public string Reference => Dto.Reference;
    public string Service => Dto.Service;
    public string Debut => Fmt.DateHeure(Dto.Debut);
    public string Fin => Dto.Fin is { } f ? Fmt.DateHeure(f) : Dto.FinPrevue is { } p ? "prévue " + Fmt.DateHeure(p) : "—";
    public string Responsable => Dto.Responsable ?? "—";
    public string Statut => Dto.Statut;
    public int Agents => Dto.NombreAgents;
    public int Equipages => Dto.NombreEquipages;
    public string Libelle => $"{Dto.Reference} — {Dto.Service} ({Fmt.DateHeure(Dto.Debut)})";
}

/// <summary>Agent de la vacation en cours d'édition (présence modifiable).</summary>
public sealed class VacationAgentLigne
{
    public long AgentId { get; init; }
    public string Libelle { get; init; } = string.Empty;
    public string Presence { get; set; } = PresencesAgent.Present;
    public string? Observation { get; set; }
}

public sealed class McVacationsSection : SectionViewModel
{
    private string _filtre = "Ouvertes";
    private VacationLigne? _selection;
    private VacationDto? _vacation;
    private bool _edition;
    private string _service = string.Empty;
    private string _debut = Fmt.Maintenant();
    private string _finPrevue = string.Empty;
    private AgentResumeDto? _responsable;
    private string _consignes = string.Empty;
    private string _observations = string.Empty;
    private AgentResumeDto? _agentAAjouter;
    private string _presenceAAjouter = PresencesAgent.Present;
    private string _finCloture = string.Empty;
    private string _observationsCloture = string.Empty;

    public McVacationsSection(MainCouranteViewModel host) : base(host, "Vacations")
    {
        App = host;
        FiltrerCommand = new RelayCommand(() => _ = Host.Executer(ChargerAsync));
        NouvelleCommand = new RelayCommand(Nouvelle);
        EnregistrerCommand = new RelayCommand(() => _ = EnregistrerAsync());
        CloturerCommand = new RelayCommand(() => _ = CloturerAsync());
        AjouterAgentCommand = new RelayCommand(AjouterAgent);
        RetirerAgentCommand = new RelayCommand(p => { if (p is VacationAgentLigne a) Agents.Remove(a); });
        ConstituerEquipageCommand = new RelayCommand(() =>
        {
            if (Vacation is { } v)
            {
                App.Obtenir<McEquipagesSection>("equipages").PreparerNouveau(v.Id);
                App.Afficher("equipages");
            }
        });
    }

    public MainCouranteViewModel App { get; }
    public bool PeutGerer => MainCouranteViewModel.PeutGererVacations;
    public IReadOnlyList<string> Filtres { get; } = ["Ouvertes", "Clôturées", "Toutes"];
    public IReadOnlyList<string> Presences => PresencesAgent.All;

    public ObservableCollection<VacationLigne> Lignes { get; } = [];
    public ObservableCollection<AgentResumeDto> Repertoire { get; } = [];
    public ObservableCollection<VacationAgentLigne> Agents { get; } = [];
    public ObservableCollection<EquipageLigne> Equipages { get; } = [];

    public string Filtre
    {
        get => _filtre;
        set
        {
            if (SetProperty(ref _filtre, value))
                _ = Host.Executer(ChargerAsync);
        }
    }

    public VacationLigne? Selection
    {
        get => _selection;
        set
        {
            if (SetProperty(ref _selection, value) && value is not null)
                _ = Host.Executer(() => ChargerVacationAsync(value.Id));
        }
    }

    public VacationDto? Vacation
    {
        get => _vacation;
        private set
        {
            SetProperty(ref _vacation, value);
            foreach (var nom in new[] { nameof(EstOuverte), nameof(PeutModifier), nameof(PeutCloturer), nameof(TitreEditeur), nameof(InfoVacation) })
                OnPropertyChanged(nom);
        }
    }

    /// <summary>Formulaire affiché (nouvelle vacation ou vacation choisie).</summary>
    public bool EnEdition
    {
        get => _edition;
        private set => SetProperty(ref _edition, value);
    }

    public bool EstOuverte => Vacation is null || Vacation.Statut == VacationStatuts.Ouverte;
    public bool PeutModifier => PeutGerer && EstOuverte;
    public bool PeutCloturer => PeutGerer && Vacation is { Statut: VacationStatuts.Ouverte };
    public string TitreEditeur => Vacation is null ? "Nouvelle vacation (prise de service)" : $"Vacation {Vacation.Reference} — {Vacation.Statut}";
    public string InfoVacation => Vacation is null ? "Les agents sont choisis dans le répertoire RH."
        : $"Ouverte par {Vacation.CreePar} le {Fmt.DateHeure(Vacation.CreeLe)}" + (Vacation.Fin is { } f ? $" — clôturée le {Fmt.DateHeure(f)}" : string.Empty);

    public string Service { get => _service; set => SetProperty(ref _service, value); }
    public string Debut { get => _debut; set => SetProperty(ref _debut, value); }
    public string FinPrevue { get => _finPrevue; set => SetProperty(ref _finPrevue, value); }
    public AgentResumeDto? Responsable { get => _responsable; set => SetProperty(ref _responsable, value); }
    public string Consignes { get => _consignes; set => SetProperty(ref _consignes, value); }
    public string Observations { get => _observations; set => SetProperty(ref _observations, value); }
    public AgentResumeDto? AgentAAjouter { get => _agentAAjouter; set => SetProperty(ref _agentAAjouter, value); }
    public string PresenceAAjouter { get => _presenceAAjouter; set => SetProperty(ref _presenceAAjouter, value); }
    public string FinCloture { get => _finCloture; set => SetProperty(ref _finCloture, value); }
    public string ObservationsCloture { get => _observationsCloture; set => SetProperty(ref _observationsCloture, value); }
    public string ResumeAgents => $"{Agents.Count(a => a.Presence != PresencesAgent.Absent)} présent(s), {Agents.Count(a => a.Presence == PresencesAgent.Absent)} absent(s)";

    public RelayCommand FiltrerCommand { get; }
    public RelayCommand NouvelleCommand { get; }
    public RelayCommand EnregistrerCommand { get; }
    public RelayCommand CloturerCommand { get; }
    public RelayCommand AjouterAgentCommand { get; }
    public RelayCommand RetirerAgentCommand { get; }
    public RelayCommand ConstituerEquipageCommand { get; }

    public override async Task ChargerAsync()
    {
        var url = "api/main-courante/vacations?limit=200" + Filtre switch
        {
            "Ouvertes" => "&statut=" + ApiClient.Q(VacationStatuts.Ouverte),
            "Clôturées" => "&statut=" + ApiClient.Q(VacationStatuts.Cloturee),
            _ => string.Empty
        };
        var vacations = await Client.GetAsync<List<VacationResumeDto>>(url);
        var selection = _selection?.Id;
        _selection = null;
        Lignes.Clear();
        foreach (var v in vacations)
            Lignes.Add(new VacationLigne(v));
        _selection = selection is { } id ? Lignes.FirstOrDefault(l => l.Id == id) : null;
        OnPropertyChanged(nameof(Selection));
        if (Vacation is { } courante)
        {
            if (!PeutModifier)
            {
                await ChargerVacationAsync(courante.Id);
            }
            else
            {
                // Vacation en cours d'édition : seuls ses équipages sont actualisés (la saisie n'est pas écrasée).
                var aJour = await Client.GetAsync<VacationDto>($"api/main-courante/vacations/{courante.Id}");
                Equipages.Clear();
                foreach (var e in aJour.Equipages)
                    Equipages.Add(new EquipageLigne(e));
            }
        }

        if (PeutGerer && Repertoire.Count == 0)
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

    private async Task ChargerVacationAsync(long id)
    {
        var vacation = await Client.GetAsync<VacationDto>($"api/main-courante/vacations/{id}");
        Service = vacation.Service;
        Debut = Fmt.Saisie(vacation.Debut);
        FinPrevue = Fmt.Saisie(vacation.FinPrevue);
        Responsable = vacation.ResponsableId is { } rid
            ? Repertoire.FirstOrDefault(a => a.Id == rid) ?? new AgentResumeDto(rid, string.Empty, vacation.Responsable ?? "?", string.Empty, null, null, null, null, null, null, AgentStatuts.Actif)
            : null;
        Consignes = vacation.Consignes ?? string.Empty;
        Observations = vacation.Observations ?? string.Empty;
        Agents.Clear();
        foreach (var a in vacation.Agents)
            Agents.Add(new VacationAgentLigne { AgentId = a.AgentId, Libelle = a.Libelle, Presence = a.Presence, Observation = a.Observation });
        Equipages.Clear();
        foreach (var e in vacation.Equipages)
            Equipages.Add(new EquipageLigne(e));
        FinCloture = string.Empty;
        ObservationsCloture = string.Empty;
        Vacation = vacation;
        EnEdition = true;
        OnPropertyChanged(nameof(ResumeAgents));
    }

    private void Nouvelle()
    {
        _selection = null;
        OnPropertyChanged(nameof(Selection));
        Vacation = null;
        Service = SaphirSession.Agent?.Service ?? string.Empty;
        Debut = Fmt.Maintenant();
        FinPrevue = DateTime.Now.AddHours(8).ToString("dd/MM/yyyy HH:mm", Fmt.Fr);
        Responsable = SaphirSession.Agent is { } moi ? Repertoire.FirstOrDefault(a => a.Id == moi.Id) : null;
        Consignes = string.Empty;
        Observations = string.Empty;
        Agents.Clear();
        Equipages.Clear();
        EnEdition = true;
        OnPropertyChanged(nameof(ResumeAgents));
    }

    private void AjouterAgent()
    {
        if (AgentAAjouter is not { } agent)
            return;
        if (Agents.Any(a => a.AgentId == agent.Id))
        {
            Host.Erreur($"{agent.Libelle} figure déjà dans la vacation.");
            return;
        }
        Agents.Add(new VacationAgentLigne { AgentId = agent.Id, Libelle = agent.Libelle, Presence = PresenceAAjouter });
        OnPropertyChanged(nameof(ResumeAgents));
    }

    private async Task EnregistrerAsync()
    {
        await Host.Executer(async () =>
        {
            var debut = Fmt.LireDateHeure(Debut, "Début") ?? DateTimeOffset.Now;
            var input = new VacationInput(Service.Trim(), debut, Fmt.LireDateHeure(FinPrevue, "Fin prévue"), Responsable?.Id, Responsable?.Libelle,
                Fmt.Vide(Consignes), Fmt.Vide(Observations),
                Agents.Select(a => new VacationAgentInput(a.AgentId, a.Libelle, a.Presence, Fmt.Vide(a.Observation))).ToList(),
                Vacation?.Version);
            var enregistree = Vacation is null
                ? await Client.PostAsync<VacationDto>("api/main-courante/vacations", input)
                : await Client.SendAsync<VacationDto>(HttpMethod.Put, $"api/main-courante/vacations/{Vacation.Id}", input);
            Vacation = enregistree;
            if (Filtre == "Clôturées")
                _filtre = "Ouvertes";
            OnPropertyChanged(nameof(Filtre));
            await ChargerAsync();
            _selection = Lignes.FirstOrDefault(l => l.Id == enregistree.Id);
            OnPropertyChanged(nameof(Selection));
        }, "Vacation enregistrée.");
    }

    private async Task CloturerAsync()
    {
        if (Vacation is not { } vacation)
            return;
        if (!await Host.Confirmer("Fin de vacation",
                $"Clôturer la vacation {vacation.Reference} ? Ses équipages seront dissous ; les véhicules doivent être rentrés.", "Clôturer", true))
            return;
        await Host.Executer(async () =>
        {
            var fin = Fmt.LireDateHeure(FinCloture, "Fin de vacation");
            var cloturee = await Client.PostAsync<VacationDto>($"api/main-courante/vacations/{vacation.Id}/cloturer",
                new ClotureVacationInput(fin, Fmt.Vide(ObservationsCloture)));
            Vacation = cloturee;
            await ChargerAsync();
        }, "Vacation clôturée.");
    }

    public override bool Concerne(RealtimeMessage message)
        => Est(message, OperationsTypes.MainCouranteChanged) &&
           message.Text is { } t && (t.StartsWith("vacation:", StringComparison.Ordinal) || t.StartsWith("equipage:", StringComparison.Ordinal));
}

// =====================================================================================
// Équipages (10.2)
// =====================================================================================

/// <summary>Membre en cours d'édition (rôle modifiable).</summary>
public sealed class MembreLigne
{
    public long AgentId { get; init; }
    public string Libelle { get; init; } = string.Empty;
    public string Role { get; set; } = "Équipier";
}

public sealed class McEquipagesSection : SectionViewModel
{
    private bool _actifsSeulement = true;
    private EquipageLigne? _selection;
    private EquipageDto? _equipage;
    private bool _edition;
    private VacationResumeDto? _vacationChoisie;
    private string _indicatif = string.Empty;
    private string _type = MainCouranteListes.TypesEquipage[0];
    private string _service = string.Empty;
    private VehiculeServiceDto? _vehicule;
    private AgentResumeDto? _agentAAjouter;
    private string _roleAAjouter = "Équipier";
    private string _motifDissolution = string.Empty;
    private long? _vacationAPreselectionner;

    public McEquipagesSection(MainCouranteViewModel host) : base(host, "Équipages")
    {
        App = host;
        FiltrerCommand = new RelayCommand(() => _ = Host.Executer(ChargerAsync));
        NouveauCommand = new RelayCommand(Nouveau);
        EnregistrerCommand = new RelayCommand(() => _ = EnregistrerAsync());
        DissoudreCommand = new RelayCommand(() => _ = DissoudreAsync());
        AjouterMembreCommand = new RelayCommand(AjouterMembre);
        RetirerMembreCommand = new RelayCommand(p => { if (p is MembreLigne m) Membres.Remove(m); });
        SansVehiculeCommand = new RelayCommand(() => Vehicule = null);
    }

    public MainCouranteViewModel App { get; }
    public bool PeutGerer => MainCouranteViewModel.PeutGererEquipages;
    public IReadOnlyList<string> Types => MainCouranteListes.TypesEquipage;
    public IReadOnlyList<string> Roles => MainCouranteListes.RolesMembre;

    public ObservableCollection<EquipageLigne> Lignes { get; } = [];
    public ObservableCollection<VacationResumeDto> VacationsOuvertes { get; } = [];
    public ObservableCollection<VehiculeServiceDto> Vehicules { get; } = [];
    public ObservableCollection<AgentResumeDto> Repertoire { get; } = [];
    public ObservableCollection<MembreLigne> Membres { get; } = [];

    public bool ActifsSeulement
    {
        get => _actifsSeulement;
        set
        {
            if (SetProperty(ref _actifsSeulement, value))
                _ = Host.Executer(ChargerAsync);
        }
    }

    public EquipageLigne? Selection
    {
        get => _selection;
        set
        {
            if (SetProperty(ref _selection, value) && value is not null)
                Ouvrir(value.Dto);
        }
    }

    public EquipageDto? Equipage
    {
        get => _equipage;
        private set
        {
            SetProperty(ref _equipage, value);
            foreach (var nom in new[] { nameof(PeutModifier), nameof(PeutDissoudre), nameof(TitreEditeur), nameof(InfoEquipage) })
                OnPropertyChanged(nom);
        }
    }

    public bool EnEdition { get => _edition; private set => SetProperty(ref _edition, value); }
    public bool PeutModifier => PeutGerer && (Equipage is null || Equipage.Actif);
    public bool PeutDissoudre => PeutGerer && Equipage is { Actif: true };
    public string TitreEditeur => Equipage is null ? "Constitution d'un équipage" : $"Équipage {Equipage.Indicatif} — {Equipage.Statut}";
    public string InfoEquipage => Equipage is null
        ? "Un agent ne peut appartenir qu'à un seul équipage actif ; un seul chef de bord par équipage."
        : $"Vacation {Equipage.VacationReference}" + (Equipage.InterventionNumero is { } n ? $" — engagé sur {n}" : string.Empty) +
          (Equipage.Actif ? string.Empty : " — dissous");

    public VacationResumeDto? VacationChoisie { get => _vacationChoisie; set => SetProperty(ref _vacationChoisie, value); }
    public string Indicatif { get => _indicatif; set => SetProperty(ref _indicatif, value); }
    public string Type { get => _type; set => SetProperty(ref _type, value); }
    public string Service { get => _service; set => SetProperty(ref _service, value); }
    public VehiculeServiceDto? Vehicule { get => _vehicule; set => SetProperty(ref _vehicule, value); }
    public AgentResumeDto? AgentAAjouter { get => _agentAAjouter; set => SetProperty(ref _agentAAjouter, value); }
    public string RoleAAjouter { get => _roleAAjouter; set => SetProperty(ref _roleAAjouter, value); }
    public string MotifDissolution { get => _motifDissolution; set => SetProperty(ref _motifDissolution, value); }

    public RelayCommand FiltrerCommand { get; }
    public RelayCommand NouveauCommand { get; }
    public RelayCommand EnregistrerCommand { get; }
    public RelayCommand DissoudreCommand { get; }
    public RelayCommand AjouterMembreCommand { get; }
    public RelayCommand RetirerMembreCommand { get; }
    public RelayCommand SansVehiculeCommand { get; }

    /// <summary>Ouverture depuis une vacation : nouvel équipage sur cette vacation.</summary>
    public void PreparerNouveau(long vacationId) => _vacationAPreselectionner = vacationId;

    public override async Task ChargerAsync()
    {
        var equipages = await Client.GetAsync<List<EquipageDto>>("api/main-courante/equipages?actifs=" + (ActifsSeulement ? "true" : "false"));
        var selection = _selection?.Id;
        _selection = null;
        Lignes.Clear();
        foreach (var e in equipages)
            Lignes.Add(new EquipageLigne(e));
        _selection = selection is { } id ? Lignes.FirstOrDefault(l => l.Id == id) : null;
        OnPropertyChanged(nameof(Selection));

        var vacations = await Client.GetAsync<List<VacationResumeDto>>("api/main-courante/vacations?statut=" + ApiClient.Q(VacationStatuts.Ouverte));
        var vacationId = VacationChoisie?.Id;
        VacationsOuvertes.Clear();
        foreach (var v in vacations)
            VacationsOuvertes.Add(v);
        VacationChoisie = vacationId is { } vid ? VacationsOuvertes.FirstOrDefault(v => v.Id == vid) : VacationChoisie;

        var vehicules = await Client.GetAsync<List<VehiculeServiceDto>>("api/main-courante/vehicules");
        var vehiculeId = Vehicule?.Id;
        Vehicules.Clear();
        foreach (var v in vehicules.Where(v => v.Disponibilite != VehiculeDisponibilites.Indisponible))
            Vehicules.Add(v);
        Vehicule = vehiculeId is { } veh ? Vehicules.FirstOrDefault(v => v.Id == veh) : null;

        if (PeutGerer && Repertoire.Count == 0)
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

        if (_vacationAPreselectionner is { } preselection)
        {
            _vacationAPreselectionner = null;
            Nouveau();
            VacationChoisie = VacationsOuvertes.FirstOrDefault(v => v.Id == preselection);
        }
        else if (Equipage is { } courant && Lignes.FirstOrDefault(l => l.Id == courant.Id) is { } ligne)
        {
            Equipage = ligne.Dto;
        }
    }

    private void Ouvrir(EquipageDto equipage)
    {
        Equipage = equipage;
        VacationChoisie = VacationsOuvertes.FirstOrDefault(v => v.Id == equipage.VacationId);
        Indicatif = equipage.Indicatif;
        Type = equipage.Type;
        Service = equipage.Service ?? string.Empty;
        Vehicule = equipage.VehiculeId is { } vid ? Vehicules.FirstOrDefault(v => v.Id == vid) : null;
        Membres.Clear();
        foreach (var m in equipage.Membres)
            Membres.Add(new MembreLigne { AgentId = m.AgentId, Libelle = m.Libelle, Role = m.Role });
        MotifDissolution = string.Empty;
        EnEdition = true;
    }

    private void Nouveau()
    {
        _selection = null;
        OnPropertyChanged(nameof(Selection));
        Equipage = null;
        VacationChoisie ??= VacationsOuvertes.FirstOrDefault();
        Indicatif = string.Empty;
        Type = Types[0];
        Service = VacationChoisie?.Service ?? string.Empty;
        Vehicule = null;
        Membres.Clear();
        EnEdition = true;
    }

    private void AjouterMembre()
    {
        if (AgentAAjouter is not { } agent)
            return;
        if (Membres.Any(m => m.AgentId == agent.Id))
        {
            Host.Erreur($"{agent.Libelle} figure déjà dans l'équipage.");
            return;
        }
        if (RoleAAjouter == MainCouranteListes.ChefDeBord && Membres.Any(m => m.Role == MainCouranteListes.ChefDeBord))
        {
            Host.Erreur("L'équipage a déjà un chef de bord.");
            return;
        }
        Membres.Add(new MembreLigne { AgentId = agent.Id, Libelle = agent.Libelle, Role = RoleAAjouter });
    }

    private async Task EnregistrerAsync()
    {
        if (VacationChoisie is not { } vacation)
        {
            Host.Erreur("Choisis la vacation (ouverte) de l'équipage.");
            return;
        }
        await Host.Executer(async () =>
        {
            var input = new EquipageInput(vacation.Id, Indicatif.Trim(), Type, Fmt.Vide(Service), Vehicule?.Id,
                Membres.Select(m => new EquipageMembreInput(m.AgentId, m.Libelle, m.Role)).ToList());
            var enregistre = Equipage is null
                ? await Client.PostAsync<EquipageDto>("api/main-courante/equipages", input)
                : await Client.SendAsync<EquipageDto>(HttpMethod.Put, $"api/main-courante/equipages/{Equipage.Id}", input);
            Equipage = enregistre;
            await ChargerAsync();
            _selection = Lignes.FirstOrDefault(l => l.Id == enregistre.Id);
            OnPropertyChanged(nameof(Selection));
        }, "Équipage enregistré.");
    }

    private async Task DissoudreAsync()
    {
        if (Equipage is not { } equipage)
            return;
        if (!await Host.Confirmer("Dissolution de l'équipage", $"Dissoudre l'équipage {equipage.Indicatif} ? Son véhicule sera libéré.", "Dissoudre", true))
            return;
        await Host.Executer(async () =>
        {
            await Client.SendAsync(HttpMethod.Post, $"api/main-courante/equipages/{equipage.Id}/dissoudre", new MotifInput(Fmt.Vide(MotifDissolution)));
            Equipage = null;
            EnEdition = false;
            await ChargerAsync();
        }, "Équipage dissous.");
    }

    public override bool Concerne(RealtimeMessage message)
        => (Est(message, OperationsTypes.MainCouranteChanged) || Est(message, OperationsTypes.PegaseChanged)) &&
           message.Text is { } t && (t.StartsWith("equipage:", StringComparison.Ordinal) || t.StartsWith("vehicule:", StringComparison.Ordinal) ||
                                     t.StartsWith("vacation:", StringComparison.Ordinal) || t.StartsWith("intervention:", StringComparison.Ordinal));
}
