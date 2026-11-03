using System.Collections.ObjectModel;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Operations;

namespace Saphir.ViewModels.Apps.Operations;

/// <summary>
/// MAIN COURANTE (menu 9.2) : tableau de bord, journal de service, mentions et déclarations, vacations, équipages,
/// véhicules, historique, archives, recherche. Argument d'ouverture : « mention:12 », « projet-mention:34 » (intervention),
/// « vacations », « equipages », « vehicules ».
/// </summary>
public sealed class MainCouranteViewModel : OperationsHostViewModel
{
    public MainCouranteViewModel() : base("MAIN COURANTE")
    {
        Menu.Add(new MenuEntree("tableau", "Tableau de bord", "Service"));
        Menu.Add(new MenuEntree("journal", "Journal de service", "Service"));
        if (PeutRediger)
            Menu.Add(new MenuEntree("nouvelle", "Nouvelle mention", "Mentions"));
        if (PeutLireMentions)
        {
            Menu.Add(new MenuEntree("mentions", "Mentions", "Mentions"));
            Menu.Add(new MenuEntree("declarations", "Déclarations", "Mentions"));
        }
        Menu.Add(new MenuEntree("vacations", "Vacations", "Moyens"));
        Menu.Add(new MenuEntree("equipages", "Équipages", "Moyens"));
        Menu.Add(new MenuEntree("vehicules", "Véhicules", "Moyens"));
        Menu.Add(new MenuEntree("historique", "Historique", "Consultation"));
        if (PeutLireMentions)
        {
            Menu.Add(new MenuEntree("archives", "Archives", "Consultation"));
            Menu.Add(new MenuEntree("recherche", "Recherche", "Consultation"));
        }

        if (!TraiterArgument(SaphirSession.TakeAppArgument(SaphirApps.MainCourante)))
            Afficher("tableau");
    }

    // ===== Droits (le serveur revérifie chaque action) =====

    public static bool PeutRediger => SaphirSession.Has(Permissions.McMentionRediger);
    public static bool PeutValider => SaphirSession.Has(Permissions.McMentionValider);
    public static bool PeutLireMentions => PeutRediger || PeutValider || SaphirSession.Has(Permissions.McConsulter);
    public static bool PeutGererVacations => SaphirSession.Has(Permissions.McVacationGerer);
    public static bool PeutGererEquipages => SaphirSession.Has(Permissions.McEquipageGerer);
    public static bool PeutGererVehicules => SaphirSession.Has(Permissions.McVehiculeGerer);
    public static bool PeutSaisirJournal => PeutRediger || PeutGererVacations;

    protected override SectionViewModel CreerSection(string code) => code switch
    {
        "journal" => new McJournalSection(this),
        "nouvelle" => new McMentionEditeurSection(this),
        "mentions" => new McMentionsSection(this, McMentionsSection.ModeToutes),
        "declarations" => new McMentionsSection(this, McMentionsSection.ModeDeclarations),
        "archives" => new McMentionsSection(this, McMentionsSection.ModeArchives),
        "recherche" => new McMentionsSection(this, McMentionsSection.ModeRecherche),
        "vacations" => new McVacationsSection(this),
        "equipages" => new McEquipagesSection(this),
        "vehicules" => new McVehiculesSection(this),
        "historique" => new McHistoriqueSection(this),
        _ => new McTableauSection(this)
    };

    /// <summary>Ouvre une mention dans l'éditeur (consultation, brouillon, rectificative).</summary>
    public void OuvrirMention(long id)
    {
        Obtenir<McMentionEditeurSection>("nouvelle").PreparerOuverture(id);
        Afficher("nouvelle");
    }

    /// <summary>Nouvelle mention préremplie depuis une intervention PÉGASE (20.2).</summary>
    public void ProjetDepuisIntervention(long interventionId)
    {
        Obtenir<McMentionEditeurSection>("nouvelle").PreparerProjet(interventionId);
        Afficher("nouvelle");
    }

    protected override void SurTempsReel(RealtimeMessage message)
    {
        if (message.Type == OperationsTypes.OuvrirApplication && message.AppId == SaphirApps.MainCourante)
        {
            TraiterArgument(SaphirSession.TakeAppArgument(SaphirApps.MainCourante));
            return;
        }
        base.SurTempsReel(message);
    }

    private bool TraiterArgument(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
            return false;
        var (cle, valeur) = Decouper(argument);
        switch (cle)
        {
            case "mention" when long.TryParse(valeur, out var id) && PeutLireMentions:
                OuvrirMention(id);
                return true;
            case "projet-mention" when long.TryParse(valeur, out var interventionId) && PeutRediger:
                ProjetDepuisIntervention(interventionId);
                return true;
            case "vacations" or "equipages" or "vehicules" or "journal":
                Afficher(cle);
                return true;
            default:
                return false;
        }
    }

    internal static (string Cle, string Valeur) Decouper(string argument)
    {
        var i = argument.IndexOf(':');
        return i < 0 ? (argument.Trim(), string.Empty) : (argument[..i].Trim(), argument[(i + 1)..].Trim());
    }
}

// =====================================================================================
// Tableau de bord
// =====================================================================================

public sealed class McTableauSection : SectionViewModel
{
    private McTableauDeBordDto? _donnees;

    public McTableauSection(MainCouranteViewModel host) : base(host, "Tableau de bord")
    {
        App = host;
        OuvrirMentionCommand = new RelayCommand(p => { if (p is MentionLigne m) App.OuvrirMention(m.Id); });
        AllerCommand = new RelayCommand(p => { if (p is string code) App.Afficher(code); });
    }

    public MainCouranteViewModel App { get; }

    public McTableauDeBordDto? Donnees
    {
        get => _donnees;
        private set => SetProperty(ref _donnees, value);
    }

    public ObservableCollection<VacationResumeDto> Vacations { get; } = [];
    public ObservableCollection<JournalLigne> Evenements { get; } = [];
    public ObservableCollection<MentionLigne> Brouillons { get; } = [];
    public bool AucuneVacation => Vacations.Count == 0;

    public RelayCommand OuvrirMentionCommand { get; }
    public RelayCommand AllerCommand { get; }

    public override async Task ChargerAsync()
    {
        var donnees = await Client.GetAsync<McTableauDeBordDto>("api/main-courante/tableau-de-bord");
        Donnees = donnees;
        Vacations.Clear();
        foreach (var v in donnees.VacationsOuvertes)
            Vacations.Add(v with { Debut = v.Debut.ToLocalTime() });
        Evenements.Clear();
        foreach (var e in donnees.DerniersEvenements)
            Evenements.Add(new JournalLigne(e));
        Brouillons.Clear();
        foreach (var m in donnees.MentionsBrouillon)
            Brouillons.Add(new MentionLigne(m));
        OnPropertyChanged(nameof(AucuneVacation));
    }

    public override bool Concerne(RealtimeMessage message) => Est(message, OperationsTypes.MainCouranteChanged);
}

// =====================================================================================
// Journal de service (9.4)
// =====================================================================================

public sealed class McJournalSection : SectionViewModel
{
    private string _du = DateTime.Today.ToString("dd/MM/yyyy", Fmt.Fr);
    private string _au = string.Empty;
    private string _type = "Tous";
    private string _recherche = string.Empty;
    private string _libelle = string.Empty;
    private string _details = string.Empty;
    private JournalLigne? _selection;

    public McJournalSection(MainCouranteViewModel host) : base(host, "Journal de service")
    {
        FiltrerCommand = new RelayCommand(() => _ = Host.Executer(ChargerAsync));
        AjouterCommand = new RelayCommand(() => _ = AjouterAsync());
    }

    public ObservableCollection<JournalLigne> Lignes { get; } = [];
    public IReadOnlyList<string> Types { get; } = ["Tous", .. JournalTypes.All];
    public bool PeutSaisir => MainCouranteViewModel.PeutSaisirJournal;

    public string Du { get => _du; set => SetProperty(ref _du, value); }
    public string Au { get => _au; set => SetProperty(ref _au, value); }
    public string Type { get => _type; set => SetProperty(ref _type, value); }
    public string Recherche { get => _recherche; set => SetProperty(ref _recherche, value); }
    public string Libelle { get => _libelle; set => SetProperty(ref _libelle, value); }
    public string Details { get => _details; set => SetProperty(ref _details, value); }

    public JournalLigne? Selection
    {
        get => _selection;
        set
        {
            if (SetProperty(ref _selection, value))
                OnPropertyChanged(nameof(DetailSelection));
        }
    }

    public string DetailSelection => Selection is null ? "Sélectionne un événement pour voir son détail."
        : $"{Selection.Date} — {Selection.Type} ({Selection.Source}, {Selection.Auteur})\n{Selection.Libelle}" +
          (Selection.Details.Length == 0 ? string.Empty : "\n" + Selection.Details) +
          (Selection.Dto.InterventionNumero is null ? string.Empty : "\nIntervention : " + Selection.Dto.InterventionNumero);

    public RelayCommand FiltrerCommand { get; }
    public RelayCommand AjouterCommand { get; }

    public override async Task ChargerAsync()
    {
        var url = "api/main-courante/journal?limit=500";
        if (Fmt.LireDateHeure(Du, "Du") is { } du) url += "&du=" + Fmt.Iso(du);
        if (Fmt.LireDateHeure(Au, "Au") is { } au) url += "&au=" + Fmt.Iso(au.TimeOfDay == TimeSpan.Zero ? au.AddDays(1).AddSeconds(-1) : au);
        if (Type != "Tous") url += "&type=" + ApiClient.Q(Type);
        if (Fmt.Vide(Recherche) is { } q) url += "&q=" + ApiClient.Q(q);
        var lignes = await Client.GetAsync<List<JournalEntreeDto>>(url);
        var selection = Selection?.Dto.Id;
        Lignes.Clear();
        foreach (var l in lignes)
            Lignes.Add(new JournalLigne(l));
        Selection = selection is { } id ? Lignes.FirstOrDefault(l => l.Dto.Id == id) : null;
    }

    private async Task AjouterAsync()
    {
        if (Fmt.Vide(Libelle) is not { } libelle)
        {
            Host.Erreur("Le libellé de l'événement est obligatoire.");
            return;
        }
        await Host.Executer(async () =>
        {
            await Client.SendAsync(HttpMethod.Post, "api/main-courante/journal", new JournalInput(libelle, Fmt.Vide(Details), null));
            Libelle = string.Empty;
            Details = string.Empty;
            await ChargerAsync();
        }, "Événement inscrit au journal de service.");
    }

    public override bool Concerne(RealtimeMessage message) => Est(message, OperationsTypes.MainCouranteChanged);
}

// =====================================================================================
// Historique
// =====================================================================================

public sealed class McHistoriqueSection : SectionViewModel
{
    private string _reference = string.Empty;
    private string _recherche = string.Empty;

    public McHistoriqueSection(OperationsHostViewModel host, string url = "api/main-courante/historique") : base(host, "Historique")
    {
        Url = url;
        FiltrerCommand = new RelayCommand(() => _ = Host.Executer(ChargerAsync));
    }

    private string Url { get; }
    public ObservableCollection<HistoriqueLigne> Lignes { get; } = [];
    public string Reference { get => _reference; set => SetProperty(ref _reference, value); }
    public string Recherche { get => _recherche; set => SetProperty(ref _recherche, value); }
    public RelayCommand FiltrerCommand { get; }

    public override async Task ChargerAsync()
    {
        var url = Url + "?limit=500";
        if (Fmt.Vide(Reference) is { } r) url += "&reference=" + ApiClient.Q(r);
        if (Fmt.Vide(Recherche) is { } q) url += "&q=" + ApiClient.Q(q);
        var lignes = await Client.GetAsync<List<HistoriqueDto>>(url);
        Lignes.Clear();
        foreach (var l in lignes)
            Lignes.Add(new HistoriqueLigne(l));
    }
}
