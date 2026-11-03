using System.Collections.ObjectModel;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Operations;

namespace Saphir.ViewModels.Apps.Operations;

/// <summary>
/// Gestion des équipages côté CIC : statuts 7.2 (indépendants de l'intervention), engagement sur une intervention.
/// Les équipages sont constitués dans la Main courante (vacations).
/// </summary>
public sealed class PgEquipagesSection : SectionViewModel
{
    private EquipageLigne? _selection;
    private string _nouveauStatut = EquipageStatuts.Disponible;
    private string _motif = string.Empty;
    private InterventionLigneDto? _intervention;
    private bool _renfort;

    public PgEquipagesSection(PegaseViewModel host) : base(host, "Gestion des équipages")
    {
        App = host;
        StatutCommand = new RelayCommand(() => _ = StatutAsync());
        EngagerCommand = new RelayCommand(() => _ = EngagerAsync());
        OuvrirInterventionCommand = new RelayCommand(() => { if (Selection?.Dto.InterventionId is { } id) App.OuvrirIntervention(id); });
        ConstituerCommand = new RelayCommand(() => PegaseViewModel.OuvrirApplication(SaphirApps.MainCourante, "equipages"));
        MonEquipageCommand = new RelayCommand(() =>
        {
            var moi = SaphirSession.Agent?.Id;
            Selection = Lignes.FirstOrDefault(l => l.Dto.Membres.Any(m => m.AgentId == moi)) ?? Selection;
            if (Selection is null)
                Host.Information("Tu n'es membre d'aucun équipage actif.");
        });
    }

    public PegaseViewModel App { get; }
    public bool PeutChangerStatut => PegaseViewModel.PeutChangerStatut;
    public bool PeutAffecter => PegaseViewModel.PeutAffecter;
    public bool PeutConstituer => SaphirSession.Has(Permissions.McEquipageGerer);
    public IReadOnlyList<string> Statuts { get; } = EquipageStatuts.All.Where(s => s != EquipageStatuts.Affecte).ToList();

    public ObservableCollection<EquipageLigne> Lignes { get; } = [];
    public ObservableCollection<InterventionLigneDto> Interventions { get; } = [];

    public EquipageLigne? Selection
    {
        get => _selection;
        set
        {
            if (!SetProperty(ref _selection, value))
                return;
            if (value is not null)
                NouveauStatut = value.Dto.InterventionId is null ? EquipageStatuts.Disponible : SuivantLogique(value.Dto.Statut);
            OnPropertyChanged(nameof(Detail));
            OnPropertyChanged(nameof(EstEngage));
        }
    }

    public bool EstEngage => Selection?.Dto.InterventionId is not null;
    public string Detail => Selection is { } s
        ? $"{s.Indicatif} ({s.Type}) — vacation {s.Vacation}\nChef de bord : {s.Chef}\nMembres : {s.Membres}\nVéhicule : {s.Vehicule}\nStatut : {s.Statut}" +
          (s.Dto.InterventionNumero is { } n ? $"\nEngagé sur {n}" : string.Empty)
        : "Sélectionne un équipage.";

    public string NouveauStatut { get => _nouveauStatut; set => SetProperty(ref _nouveauStatut, value); }
    public string Motif { get => _motif; set => SetProperty(ref _motif, value); }
    public InterventionLigneDto? Intervention { get => _intervention; set => SetProperty(ref _intervention, value); }
    public bool Renfort { get => _renfort; set => SetProperty(ref _renfort, value); }

    public RelayCommand StatutCommand { get; }
    public RelayCommand EngagerCommand { get; }
    public RelayCommand OuvrirInterventionCommand { get; }
    public RelayCommand ConstituerCommand { get; }
    public RelayCommand MonEquipageCommand { get; }

    private static string SuivantLogique(string statut) => statut switch
    {
        EquipageStatuts.Affecte => EquipageStatuts.EnRoute,
        EquipageStatuts.EnRoute => EquipageStatuts.SurPlace,
        EquipageStatuts.SurPlace => EquipageStatuts.MissionTerminee,
        EquipageStatuts.Transport => EquipageStatuts.AuService,
        EquipageStatuts.AuService => EquipageStatuts.MissionTerminee,
        _ => EquipageStatuts.Disponible
    };

    public override async Task ChargerAsync()
    {
        var equipages = await Client.GetAsync<List<EquipageDto>>("api/main-courante/equipages?actifs=true");
        var selection = _selection?.Id;
        _selection = null;
        Lignes.Clear();
        foreach (var e in equipages)
            Lignes.Add(new EquipageLigne(e));
        _selection = selection is { } id ? Lignes.FirstOrDefault(l => l.Id == id) : null;
        OnPropertyChanged(nameof(Selection));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(EstEngage));

        if (PeutAffecter)
        {
            var interventions = await Client.GetAsync<List<InterventionLigneDto>>("api/pegase/interventions?vue=actives&limit=200");
            var choisie = Intervention?.Id;
            Interventions.Clear();
            foreach (var i in interventions)
                Interventions.Add(i);
            Intervention = choisie is { } iid ? Interventions.FirstOrDefault(i => i.Id == iid) : Interventions.FirstOrDefault();
        }
    }

    private async Task StatutAsync()
    {
        if (Selection is not { } s)
            return;
        await Host.Executer(async () =>
        {
            await Client.PostAsync<EquipageDto>($"api/pegase/equipages/{s.Id}/statut", new EquipageStatutInput(NouveauStatut, Fmt.Vide(Motif)));
            Motif = string.Empty;
            await ChargerAsync();
        }, $"{s.Indicatif} : {NouveauStatut}.");
    }

    private async Task EngagerAsync()
    {
        if (Selection is not { } s || Intervention is not { } i)
        {
            Host.Erreur("Choisis un équipage et une intervention.");
            return;
        }
        await Host.Executer(async () =>
        {
            await Client.PostAsync<InterventionDto>($"api/pegase/interventions/{i.Id}/engagements", new EngagementInput(s.Id, Renfort));
            Renfort = false;
            await ChargerAsync();
        }, $"{s.Indicatif} engagé sur {i.Numero}.");
    }

    public override bool Concerne(RealtimeMessage message)
        => Est(message, OperationsTypes.PegaseChanged) || (Est(message, OperationsTypes.MainCouranteChanged) &&
                                                           message.Text?.StartsWith("equipage:", StringComparison.Ordinal) == true);
}
