using System.Collections.ObjectModel;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Operations;

namespace Saphir.ViewModels.Apps.Operations;

/// <summary>
/// PÉGASE (menu 6.2) : tableau de bord CIC, nouvel appel, interventions en cours / en attente, équipages, GÉOOPS,
/// communications, TN du jour, historique, statistiques ; fiche d'intervention (7.1).
/// Arguments d'ouverture : « appel:12 » (SIMCALL), « intervention:34 », « nouvel-appel ».
/// </summary>
public sealed class PegaseViewModel : OperationsHostViewModel
{
    public PegaseViewModel() : base("PÉGASE")
    {
        Menu.Add(new MenuEntree("tableau", "Tableau de bord CIC", "CIC"));
        if (PeutPrendreAppel)
            Menu.Add(new MenuEntree("appel", "Nouvel appel", "CIC"));
        Menu.Add(new MenuEntree("en-cours", "Interventions en cours", "Interventions"));
        Menu.Add(new MenuEntree("en-attente", "Interventions en attente", "Interventions"));
        Menu.Add(new MenuEntree("equipages", "Gestion des équipages", "Moyens"));
        Menu.Add(new MenuEntree("geoops", "GÉOOPS", "Moyens"));
        Menu.Add(new MenuEntree("communications", "Communications", "Suivi"));
        Menu.Add(new MenuEntree("tn", "TN du jour", "Suivi"));
        Menu.Add(new MenuEntree("historique", "Historique", "Suivi"));
        Menu.Add(new MenuEntree("statistiques", "Statistiques", "Suivi"));

        if (!TraiterArgument(SaphirSession.TakeAppArgument(SaphirApps.Pegase)))
            Afficher("tableau");
    }

    public static bool PeutPrendreAppel => SaphirSession.Has(Permissions.PegaseAppelCreer);
    public static bool PeutCreerIntervention => SaphirSession.Has(Permissions.PegaseInterventionCreer);
    public static bool PeutAffecter => SaphirSession.Has(Permissions.PegaseAffecter);
    public static bool PeutChangerStatut => SaphirSession.Has(Permissions.PegaseStatut) || PeutAffecter;
    public static bool PeutCompteRendu => SaphirSession.Has(Permissions.PegaseCompteRendu);
    public static bool PeutCloturer => SaphirSession.Has(Permissions.PegaseCloturer);
    public static bool PeutTn => SaphirSession.Has(Permissions.PegaseTn);
    public static bool PeutQualifier => PeutCreerIntervention || PeutAffecter;
    public static bool EstActeur => PeutPrendreAppel || PeutCreerIntervention || PeutAffecter || PeutChangerStatut || PeutCompteRendu || PeutCloturer || PeutTn;

    protected override SectionViewModel CreerSection(string code) => code switch
    {
        "appel" => new PgAppelSection(this),
        "en-cours" => new PgInterventionsSection(this, "en-cours", "Interventions en cours"),
        "en-attente" => new PgInterventionsSection(this, "en-attente", "Interventions en attente"),
        "equipages" => new PgEquipagesSection(this),
        "geoops" => new GeoopsSection(this),
        "communications" => new PgCommunicationsSection(this),
        "tn" => new PgTnSection(this),
        "historique" => new PgHistoriqueSection(this),
        "statistiques" => new PgStatistiquesSection(this),
        "fiche" => new PgFicheSection(this),
        _ => new PgTableauSection(this)
    };

    public void OuvrirIntervention(long id)
    {
        Obtenir<PgFicheSection>("fiche").Preparer(id);
        Afficher("fiche");
    }

    public void OuvrirAppel(long id)
    {
        Obtenir<PgAppelSection>("appel").PreparerOuverture(id);
        Afficher("appel");
    }

    /// <summary>Ouvre une autre application du poste (Main courante, LRPPN) avec un argument.</summary>
    public static void OuvrirApplication(string appId, string argument)
        => SaphirSession.Publish(new RealtimeMessage(OperationsTypes.OuvrirApplication, AppId: appId, Text: argument));

    protected override void SurTempsReel(RealtimeMessage message)
    {
        if (message.Type == OperationsTypes.OuvrirApplication && message.AppId == SaphirApps.Pegase)
        {
            TraiterArgument(SaphirSession.TakeAppArgument(SaphirApps.Pegase));
            return;
        }
        base.SurTempsReel(message);
    }

    private bool TraiterArgument(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
            return false;
        var (cle, valeur) = MainCouranteViewModel.Decouper(argument);
        switch (cle)
        {
            case "appel" when long.TryParse(valeur, out var id):
                OuvrirAppel(id);
                return true;
            case "intervention" when long.TryParse(valeur, out var interventionId):
                OuvrirIntervention(interventionId);
                return true;
            case "nouvel-appel" when PeutPrendreAppel:
                Obtenir<PgAppelSection>("appel").PreparerNouveau();
                Afficher("appel");
                return true;
            case "geoops" or "equipages" or "tn":
                Afficher(cle);
                return true;
            default:
                return false;
        }
    }
}

// =====================================================================================
// Tableau de bord CIC
// =====================================================================================

public sealed class PgTableauSection : SectionViewModel
{
    private PegaseTableauDeBordDto? _donnees;

    public PgTableauSection(PegaseViewModel host) : base(host, "Tableau de bord CIC")
    {
        App = host;
        OuvrirInterventionCommand = new RelayCommand(p => { if (p is InterventionLigne i) App.OuvrirIntervention(i.Id); });
        OuvrirAppelCommand = new RelayCommand(p => { if (p is AppelLigne a) App.OuvrirAppel(a.Id); });
        NouvelAppelCommand = new RelayCommand(() =>
        {
            App.Obtenir<PgAppelSection>("appel").PreparerNouveau();
            App.Afficher("appel");
        });
        AllerCommand = new RelayCommand(p => { if (p is string code) App.Afficher(code); });
    }

    public PegaseViewModel App { get; }
    public bool PeutPrendreAppel => PegaseViewModel.PeutPrendreAppel;

    public PegaseTableauDeBordDto? Donnees { get => _donnees; private set => SetProperty(ref _donnees, value); }
    public ObservableCollection<InterventionLigne> Interventions { get; } = [];
    public ObservableCollection<AppelLigne> Appels { get; } = [];
    public ObservableCollection<EquipageLigne> Equipages { get; } = [];
    public string Tn => Donnees?.Tn is { } tn ? $"{tn.Agent}" + (tn.Service is null ? string.Empty : $" ({tn.Service})") + $" depuis {Fmt.DateHeure(tn.Debut)}" : "Aucun TN de permanence";

    public RelayCommand OuvrirInterventionCommand { get; }
    public RelayCommand OuvrirAppelCommand { get; }
    public RelayCommand NouvelAppelCommand { get; }
    public RelayCommand AllerCommand { get; }

    public override async Task ChargerAsync()
    {
        var donnees = await Client.GetAsync<PegaseTableauDeBordDto>("api/pegase/tableau-de-bord");
        Donnees = donnees;
        Interventions.Clear();
        foreach (var i in donnees.Interventions)
            Interventions.Add(new InterventionLigne(i));
        Appels.Clear();
        foreach (var a in donnees.Appels)
            Appels.Add(new AppelLigne(a));
        Equipages.Clear();
        foreach (var e in donnees.Equipages)
            Equipages.Add(new EquipageLigne(e));
        OnPropertyChanged(nameof(Tn));
    }

    public override bool Concerne(RealtimeMessage message)
        => Est(message, OperationsTypes.PegaseChanged) || Est(message, OperationsTypes.MainCouranteChanged);
}

// =====================================================================================
// Interventions en cours / en attente
// =====================================================================================

public sealed class PgInterventionsSection : SectionViewModel
{
    private string _recherche = string.Empty;
    private InterventionLigne? _selection;

    public PgInterventionsSection(PegaseViewModel host, string vue, string titre) : base(host, titre)
    {
        App = host;
        Vue = vue;
        RechercherCommand = new RelayCommand(() => _ = Host.Executer(ChargerAsync));
        OuvrirCommand = new RelayCommand(p =>
        {
            if ((p as InterventionLigne ?? Selection) is { } i)
                App.OuvrirIntervention(i.Id);
        });
    }

    public PegaseViewModel App { get; }
    public string Vue { get; }
    public string Aide => Vue == "en-attente"
        ? "Interventions à qualifier ou en attente d'engagement, par priorité."
        : "Interventions avec des moyens affectés, en cours ou en attente de compte rendu.";
    public ObservableCollection<InterventionLigne> Lignes { get; } = [];
    public string Recherche { get => _recherche; set => SetProperty(ref _recherche, value); }
    public InterventionLigne? Selection { get => _selection; set => SetProperty(ref _selection, value); }
    public string Compte => Lignes.Count + " intervention(s)";
    public RelayCommand RechercherCommand { get; }
    public RelayCommand OuvrirCommand { get; }

    public override async Task ChargerAsync()
    {
        var url = $"api/pegase/interventions?vue={Vue}&limit=300";
        if (Fmt.Vide(Recherche) is { } q) url += "&q=" + ApiClient.Q(q);
        var lignes = await Client.GetAsync<List<InterventionLigneDto>>(url);
        var selection = Selection?.Id;
        Lignes.Clear();
        foreach (var l in lignes)
            Lignes.Add(new InterventionLigne(l));
        Selection = selection is { } id ? Lignes.FirstOrDefault(l => l.Id == id) : null;
        OnPropertyChanged(nameof(Compte));
    }

    public override bool Concerne(RealtimeMessage message) => Est(message, OperationsTypes.PegaseChanged);
}

// =====================================================================================
// TN du jour (8.2)
// =====================================================================================

public sealed class TnLigne(TnDto dto)
{
    public TnDto Dto { get; } = dto;
    public string Agent => Dto.Agent;
    public string Service => Dto.Service ?? "—";
    public string Date => Dto.Debut.ToLocalTime().ToString("dddd d MMMM", Fmt.Fr);
    public string Debut => Fmt.DateHeure(Dto.Debut);
    public string Fin => Dto.Fin is { } f ? Fmt.DateHeure(f) : "—";
    public string Statut => Dto.Statut;
}

public sealed class PgTnSection : SectionViewModel
{
    private string _service = SaphirSession.Agent?.Service ?? string.Empty;
    private TnDto? _actuel;

    public PgTnSection(PegaseViewModel host) : base(host, "TN du jour")
    {
        PrendreCommand = new RelayCommand(() => _ = PrendreAsync());
        TerminerCommand = new RelayCommand(() => _ = TerminerAsync());
    }

    public bool PeutTn => PegaseViewModel.PeutTn;
    public ObservableCollection<TnLigne> Lignes { get; } = [];
    public string Service { get => _service; set => SetProperty(ref _service, value); }

    public TnDto? Actuel
    {
        get => _actuel;
        private set
        {
            SetProperty(ref _actuel, value);
            OnPropertyChanged(nameof(TexteActuel));
            OnPropertyChanged(nameof(PeutTerminer));
        }
    }

    public string TexteActuel => Actuel is { } tn
        ? $"{tn.Agent}{(tn.Service is null ? string.Empty : " — " + tn.Service)}, de permanence depuis le {Fmt.DateHeure(tn.Debut)}."
        : "Aucun agent n'assure la permanence TN : les nouvelles interventions ne sont signalées qu'aux postes PÉGASE.";
    public bool PeutTerminer => PeutTn && Actuel is not null;

    public RelayCommand PrendreCommand { get; }
    public RelayCommand TerminerCommand { get; }

    public override async Task ChargerAsync()
    {
        var lignes = await Client.GetAsync<List<TnDto>>("api/pegase/tn?limit=60");
        Lignes.Clear();
        foreach (var l in lignes)
            Lignes.Add(new TnLigne(l));
        Actuel = lignes.FirstOrDefault(l => l.Statut == TnStatuts.EnCours);
    }

    private async Task PrendreAsync()
    {
        if (!await Host.Confirmer("Permanence TN", "Prendre la permanence TN ? La permanence en cours sera close et tu recevras les nouvelles interventions."))
            return;
        await Host.Executer(async () =>
        {
            await Client.PostAsync<TnDto>("api/pegase/tn/prendre", new TnInput(Fmt.Vide(Service)));
            await ChargerAsync();
        }, "Tu assures la permanence TN.");
    }

    private async Task TerminerAsync()
    {
        if (Actuel is not { } tn || !await Host.Confirmer("Fin de permanence", $"Terminer la permanence TN de {tn.Agent} ?", "Terminer"))
            return;
        await Host.Executer(async () =>
        {
            await Client.SendAsync(HttpMethod.Post, $"api/pegase/tn/{tn.Id}/terminer");
            await ChargerAsync();
        }, "Permanence terminée.");
    }

    public override bool Concerne(RealtimeMessage message) => Est(message, OperationsTypes.PegaseChanged) && message.Text == "tn";
}

// =====================================================================================
// Communications
// =====================================================================================

public sealed class CommunicationLigne(CommunicationDto dto)
{
    public CommunicationDto Dto { get; } = dto;
    public string Date => Fmt.DateHeure(Dto.Date);
    public string Intervention => Dto.InterventionNumero;
    public string Type => Dto.Type;
    public string Texte => Dto.Texte;
    public string Auteur => Dto.Auteur;
}

public sealed class PgCommunicationsSection : SectionViewModel
{
    private bool _messagesSeulement;
    private CommunicationLigne? _selection;
    private string _message = string.Empty;

    public PgCommunicationsSection(PegaseViewModel host) : base(host, "Communications")
    {
        App = host;
        EnvoyerCommand = new RelayCommand(() => _ = EnvoyerAsync());
        OuvrirCommand = new RelayCommand(() => { if (Selection is { } s) App.OuvrirIntervention(s.Dto.InterventionId); });
    }

    public PegaseViewModel App { get; }
    public bool PeutEcrire => PegaseViewModel.EstActeur;
    public ObservableCollection<CommunicationLigne> Lignes { get; } = [];

    public bool MessagesSeulement
    {
        get => _messagesSeulement;
        set
        {
            if (SetProperty(ref _messagesSeulement, value))
                _ = Host.Executer(ChargerAsync);
        }
    }

    public CommunicationLigne? Selection
    {
        get => _selection;
        set
        {
            if (SetProperty(ref _selection, value))
                OnPropertyChanged(nameof(Destinataire));
        }
    }

    public string Destinataire => Selection is { } s ? "Message sur l'intervention " + s.Intervention : "Sélectionne une ligne pour répondre sur son intervention.";
    public string Message { get => _message; set => SetProperty(ref _message, value); }
    public RelayCommand EnvoyerCommand { get; }
    public RelayCommand OuvrirCommand { get; }

    public override async Task ChargerAsync()
    {
        var lignes = await Client.GetAsync<List<CommunicationDto>>("api/pegase/communications?limit=300&messages=" + (MessagesSeulement ? "true" : "false"));
        var selection = Selection?.Dto.Id;
        Lignes.Clear();
        foreach (var l in lignes)
            Lignes.Add(new CommunicationLigne(l));
        Selection = selection is { } id ? Lignes.FirstOrDefault(l => l.Dto.Id == id) : null;
    }

    private async Task EnvoyerAsync()
    {
        if (Selection is not { } s || Fmt.Vide(Message) is not { } texte)
        {
            Host.Erreur("Choisis une intervention et écris le message.");
            return;
        }
        await Host.Executer(async () =>
        {
            await Client.PostAsync<InterventionDto>($"api/pegase/interventions/{s.Dto.InterventionId}/messages", new MessageInput(texte));
            Message = string.Empty;
            await ChargerAsync();
        }, "Message horodaté enregistré.");
    }

    public override bool Concerne(RealtimeMessage message) => Est(message, OperationsTypes.PegaseChanged);
}

// =====================================================================================
// Historique
// =====================================================================================

public sealed class PgHistoriqueSection : SectionViewModel
{
    private string _recherche = string.Empty;
    private string _du = DateTime.Today.AddDays(-7).ToString("dd/MM/yyyy", Fmt.Fr);
    private string _au = string.Empty;
    private InterventionLigne? _selection;
    private string _reference = string.Empty;

    public PgHistoriqueSection(PegaseViewModel host) : base(host, "Historique")
    {
        App = host;
        RechercherCommand = new RelayCommand(() => _ = Host.Executer(ChargerAsync));
        OuvrirCommand = new RelayCommand(() => { if (Selection is { } i) App.OuvrirIntervention(i.Id); });
    }

    public PegaseViewModel App { get; }
    public ObservableCollection<InterventionLigne> Interventions { get; } = [];
    public ObservableCollection<HistoriqueLigne> Modifications { get; } = [];
    public string Recherche { get => _recherche; set => SetProperty(ref _recherche, value); }
    public string Du { get => _du; set => SetProperty(ref _du, value); }
    public string Au { get => _au; set => SetProperty(ref _au, value); }
    public string Reference { get => _reference; set => SetProperty(ref _reference, value); }

    public InterventionLigne? Selection
    {
        get => _selection;
        set
        {
            if (SetProperty(ref _selection, value) && value is not null)
            {
                Reference = value.Numero;
                _ = Host.Executer(ChargerModificationsAsync);
            }
        }
    }

    public RelayCommand RechercherCommand { get; }
    public RelayCommand OuvrirCommand { get; }

    public override async Task ChargerAsync()
    {
        var url = "api/pegase/interventions?vue=terminees&limit=500";
        if (Fmt.Vide(Recherche) is { } q) url += "&q=" + ApiClient.Q(q);
        if (Fmt.LireDateHeure(Du, "Du") is { } du) url += "&du=" + Fmt.Iso(du);
        if (Fmt.LireDateHeure(Au, "Au") is { } au) url += "&au=" + Fmt.Iso(au.TimeOfDay == TimeSpan.Zero ? au.AddDays(1).AddSeconds(-1) : au);
        var lignes = await Client.GetAsync<List<InterventionLigneDto>>(url);
        _selection = null;
        Interventions.Clear();
        foreach (var l in lignes)
            Interventions.Add(new InterventionLigne(l));
        OnPropertyChanged(nameof(Selection));
        await ChargerModificationsAsync();
    }

    private async Task ChargerModificationsAsync()
    {
        var url = "api/pegase/historique?limit=300";
        if (Fmt.Vide(Reference) is { } r) url += "&reference=" + ApiClient.Q(r);
        var lignes = await Client.GetAsync<List<HistoriqueDto>>(url);
        Modifications.Clear();
        foreach (var l in lignes)
            Modifications.Add(new HistoriqueLigne(l));
    }
}

// =====================================================================================
// Statistiques
// =====================================================================================

public sealed class PgStatistiquesSection : SectionViewModel
{
    private string _du = DateTime.Today.AddDays(-30).ToString("dd/MM/yyyy", Fmt.Fr);
    private string _au = string.Empty;
    private PegaseStatistiquesDto? _donnees;

    public PgStatistiquesSection(PegaseViewModel host) : base(host, "Statistiques")
    {
        CalculerCommand = new RelayCommand(() => _ = Host.Executer(ChargerAsync));
    }

    public string Du { get => _du; set => SetProperty(ref _du, value); }
    public string Au { get => _au; set => SetProperty(ref _au, value); }
    public PegaseStatistiquesDto? Donnees { get => _donnees; private set => SetProperty(ref _donnees, value); }
    public ObservableCollection<BarreStatistique> ParMotif { get; } = [];
    public ObservableCollection<BarreStatistique> ParPriorite { get; } = [];
    public ObservableCollection<BarreStatistique> ParStatut { get; } = [];
    public ObservableCollection<BarreStatistique> ParCanal { get; } = [];
    public ObservableCollection<BarreStatistique> ParCommune { get; } = [];
    public string DelaiAffectation => Donnees?.DelaiAffectationMinutes is { } d ? $"{d:0.#} min" : "—";
    public string DelaiArrivee => Donnees?.DelaiArriveeMinutes is { } d ? $"{d:0.#} min" : "—";
    public string Periode => Donnees is { } s ? $"Du {Fmt.DateHeure(s.Du)} au {Fmt.DateHeure(s.Au)}" : string.Empty;
    public RelayCommand CalculerCommand { get; }

    public override async Task ChargerAsync()
    {
        var url = "api/pegase/statistiques";
        var parametres = new List<string>();
        if (Fmt.LireDateHeure(Du, "Du") is { } du) parametres.Add("du=" + Fmt.Iso(du));
        if (Fmt.LireDateHeure(Au, "Au") is { } au) parametres.Add("au=" + Fmt.Iso(au.TimeOfDay == TimeSpan.Zero ? au.AddDays(1).AddSeconds(-1) : au));
        if (parametres.Count > 0) url += "?" + string.Join("&", parametres);
        var donnees = await Client.GetAsync<PegaseStatistiquesDto>(url);
        Donnees = donnees;
        Remplir(ParMotif, donnees.ParMotif);
        Remplir(ParPriorite, donnees.ParPriorite.Select(l => l with { Libelle = PegaseListes.LibellePriorite(l.Libelle) }).ToList());
        Remplir(ParStatut, donnees.ParStatut);
        Remplir(ParCanal, donnees.ParCanal);
        Remplir(ParCommune, donnees.ParCommune);
        foreach (var nom in new[] { nameof(DelaiAffectation), nameof(DelaiArrivee), nameof(Periode) })
            OnPropertyChanged(nom);
    }

    private static void Remplir(ObservableCollection<BarreStatistique> cible, IReadOnlyList<StatistiqueLigneDto> lignes)
    {
        cible.Clear();
        var max = lignes.Count == 0 ? 0 : lignes.Max(l => l.Nombre);
        foreach (var l in lignes)
            cible.Add(new BarreStatistique(l.Libelle, l.Nombre, max));
    }
}
