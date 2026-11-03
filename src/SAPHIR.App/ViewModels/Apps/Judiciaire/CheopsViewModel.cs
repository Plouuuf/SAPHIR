using System.Collections.ObjectModel;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Judiciaire;

namespace Saphir.ViewModels.Apps.Judiciaire;

/// <summary>
/// CHEOPS : portail des fichiers fictifs (11.1), recherche générale (11.2) et un écran par fichier (12).
/// Le menu ne montre que les fichiers autorisés ; le serveur revérifie chaque consultation et la journalise.
/// Le bandeau rouge FPR / FOVeS reste affiché en tête quel que soit le fichier consulté.
/// </summary>
public sealed class CheopsViewModel : AppViewModelBase, IHoteJudiciaire
{
    public const string Portail = "portail";
    public const string RechercheCode = "recherche";

    private string _section = Portail;
    private MenuEntree? _menuSelection;
    private bool _menuOuvert = true;

    public CheopsViewModel()
    {
        Menu.Add(new MenuEntree(Portail, "Portail", "Applications accessibles"));
        Menu.Add(new MenuEntree(RechercheCode, "Recherche générale", "Personnes, véhicules, numéros de dossier"));
        foreach (var fichier in FichiersCheops.All.Where(f => SaphirSession.Has(f.Permission)))
        {
            Menu.Add(new MenuEntree(fichier.Code, fichier.Libelle, fichier.Description));
            Tuiles.Add(new MenuEntree(fichier.Code, fichier.Libelle, fichier.Description));
        }

        Recherche = new RechercheGeneraleViewModel(this);
        Taj = new TajViewModel(this);
        Fpr = new FprViewModel(this);
        Foves = new FovesViewModel(this);
        Siv = new SivViewModel(this);
        Fva = new FvaViewModel(this);
        Snpc = new SnpcViewModel(this);
        Faed = new ScientifiqueViewModel(this, FichiersCheops.Faed);
        Fnaeg = new ScientifiqueViewModel(this, FichiersCheops.Fnaeg);
        Fijaisv = new FijaisViewModel(this, FichiersCheops.Fijaisv);
        Fijait = new FijaisViewModel(this, FichiersCheops.Fijait);
        Anadoc = new AnadocViewModel(this);

        OuvrirCommand = new RelayCommand(p => { if (p is string code) Ouvrir(code); });
        BasculerMenuCommand = new RelayCommand(() => MenuOuvert = !MenuOuvert);
        FermerBandeauxCommand = new RelayCommand(() => DefinirBandeaux([]));
        MenuSelection = Menu[0];
    }

    // ===== Navigation =====

    public ObservableCollection<MenuEntree> Menu { get; } = [];
    public ObservableCollection<MenuEntree> Tuiles { get; } = [];
    public bool AucunFichier => Tuiles.Count == 0;

    public MenuEntree? MenuSelection
    {
        get => _menuSelection;
        set
        {
            if (!SetProperty(ref _menuSelection, value) || value is null)
                return;
            Section = value.Code;
        }
    }

    public bool MenuOuvert { get => _menuOuvert; set => SetProperty(ref _menuOuvert, value); }

    public string Section
    {
        get => _section;
        private set
        {
            if (!SetProperty(ref _section, value))
                return;
            foreach (var nom in new[] { nameof(IsPortail), nameof(IsRecherche), nameof(IsTaj), nameof(IsFpr), nameof(IsFoves), nameof(IsSiv),
                         nameof(IsFva), nameof(IsSnpc), nameof(IsFaed), nameof(IsFnaeg), nameof(IsFijaisv), nameof(IsFijait), nameof(IsAnadoc), nameof(TitreSection) })
                OnPropertyChanged(nom);
            if (MenuSelection?.Code != value)
            {
                _menuSelection = Menu.FirstOrDefault(m => m.Code == value);
                OnPropertyChanged(nameof(MenuSelection));
            }
            _ = SectionCourante()?.EntrerAsync();
        }
    }

    public string TitreSection => Menu.FirstOrDefault(m => m.Code == Section) is { } m ? $"{m.Libelle} — {m.Description}" : "CHEOPS";

    public bool IsPortail => Section == Portail;
    public bool IsRecherche => Section == RechercheCode;
    public bool IsTaj => Section == FichiersCheops.Taj;
    public bool IsFpr => Section == FichiersCheops.Fpr;
    public bool IsFoves => Section == FichiersCheops.Foves;
    public bool IsSiv => Section == FichiersCheops.Siv;
    public bool IsFva => Section == FichiersCheops.Fva;
    public bool IsSnpc => Section == FichiersCheops.Snpc;
    public bool IsFaed => Section == FichiersCheops.Faed;
    public bool IsFnaeg => Section == FichiersCheops.Fnaeg;
    public bool IsFijaisv => Section == FichiersCheops.Fijaisv;
    public bool IsFijait => Section == FichiersCheops.Fijait;
    public bool IsAnadoc => Section == FichiersCheops.Anadoc;

    public RelayCommand OuvrirCommand { get; }
    public RelayCommand BasculerMenuCommand { get; }
    public RelayCommand FermerBandeauxCommand { get; }

    /// <summary>Ouvre un fichier (s'il est autorisé).</summary>
    public bool Ouvrir(string code)
    {
        if (Menu.All(m => m.Code != code))
        {
            ErrorText = "Ce fichier n'est pas accessible avec tes habilitations.";
            return false;
        }
        Section = code;
        return true;
    }

    /// <summary>Ouvre un fichier directement sur une personne (depuis la recherche générale).</summary>
    public async Task OuvrirPersonneAsync(string code, PersonneResumeDto personne)
    {
        if (!Ouvrir(code))
            return;
        switch (code)
        {
            case FichiersCheops.Taj: await Taj.ChargerPersonneAsync(personne.Id); break;
            case FichiersCheops.Fpr: await Fpr.ChercherPersonneAsync(personne); break;
            case FichiersCheops.Snpc: await Snpc.ChargerPersonneAsync(personne.Id); break;
            case FichiersCheops.Fijaisv: await Fijaisv.ChercherPersonneAsync(personne); break;
            case FichiersCheops.Fijait: await Fijait.ChercherPersonneAsync(personne); break;
        }
    }

    public async Task OuvrirVehiculeAsync(long vehiculeId)
    {
        if (Ouvrir(FichiersCheops.Siv))
            await Siv.ChargerAsync(vehiculeId);
    }

    /// <summary>Ouvre la fiche correspondant à un numéro de dossier.</summary>
    public async Task OuvrirDossierAsync(CorrespondanceDto dossier)
    {
        switch (dossier.Fichier)
        {
            case FichiersCheops.Taj when dossier.PersonneId is { } p:
                if (Ouvrir(FichiersCheops.Taj)) await Taj.ChargerPersonneAsync(p);
                break;
            case FichiersCheops.Fpr:
                if (Ouvrir(FichiersCheops.Fpr)) await Fpr.ChargerAsync(dossier.Id);
                break;
            case FichiersCheops.Foves:
                if (Ouvrir(FichiersCheops.Foves)) await Foves.ChargerAsync(dossier.Id);
                break;
            case FichiersCheops.Faed:
                if (Ouvrir(FichiersCheops.Faed)) await Faed.ChargerAsync(dossier.Id);
                break;
            case FichiersCheops.Fnaeg:
                if (Ouvrir(FichiersCheops.Fnaeg)) await Fnaeg.ChargerAsync(dossier.Id);
                break;
            case FichiersCheops.Fijaisv:
                if (Ouvrir(FichiersCheops.Fijaisv)) await Fijaisv.ChargerAsync(dossier.Id);
                break;
            case FichiersCheops.Fijait:
                if (Ouvrir(FichiersCheops.Fijait)) await Fijait.ChargerAsync(dossier.Id);
                break;
            default:
                InfoText = $"{dossier.Fichier} {dossier.Reference} : {dossier.Libelle}";
                break;
        }
    }

    private CheopsSectionBase? SectionCourante() => Section switch
    {
        RechercheCode => Recherche,
        FichiersCheops.Taj => Taj,
        FichiersCheops.Fpr => Fpr,
        FichiersCheops.Foves => Foves,
        FichiersCheops.Siv => Siv,
        FichiersCheops.Fva => Fva,
        FichiersCheops.Snpc => Snpc,
        FichiersCheops.Faed => Faed,
        FichiersCheops.Fnaeg => Fnaeg,
        FichiersCheops.Fijaisv => Fijaisv,
        FichiersCheops.Fijait => Fijait,
        FichiersCheops.Anadoc => Anadoc,
        _ => null
    };

    // ===== Sous-écrans =====

    public RechercheGeneraleViewModel Recherche { get; }
    public TajViewModel Taj { get; }
    public FprViewModel Fpr { get; }
    public FovesViewModel Foves { get; }
    public SivViewModel Siv { get; }
    public FvaViewModel Fva { get; }
    public SnpcViewModel Snpc { get; }
    public ScientifiqueViewModel Faed { get; }
    public ScientifiqueViewModel Fnaeg { get; }
    public FijaisViewModel Fijaisv { get; }
    public FijaisViewModel Fijait { get; }
    public AnadocViewModel Anadoc { get; }

    // ===== Bandeau rouge FPR / FOVeS =====

    public ObservableCollection<AlerteBandeauDto> Bandeaux { get; } = [];
    public bool ABandeaux => Bandeaux.Count > 0;

    public void DefinirBandeaux(IEnumerable<AlerteBandeauDto> bandeaux)
    {
        Bandeaux.Clear();
        foreach (var b in bandeaux)
            Bandeaux.Add(b);
        OnPropertyChanged(nameof(ABandeaux));
    }

    /// <summary>Bandeaux d'une personne consultée depuis un fichier dont la fiche ne les porte pas (FPR, SNPC, FIJAIS).</summary>
    public async Task BandeauxPersonneAsync(long personneId)
    {
        if (Api is not { } api)
            return;
        try
        {
            var fiche = await api.GetAsync<PersonneFicheDto>($"api/cheops/personnes/{personneId}");
            DefinirBandeaux(fiche.Bandeaux);
        }
        catch (ApiException)
        {
            // Pas de bandeau si la fiche d'identité est inaccessible.
        }
    }

    // ===== Hôte =====

    public IFichiersPoste? Fichiers { get; set; }

    public Task ExecuterAsync(Func<Task> travail, string? succes = null) => RunAsync(travail, succes);

    public Task<bool> ConfirmerAsync(string titre, string message, string libelle, bool danger = false) => ConfirmAsync(titre, message, libelle, danger);

    // ===== Temps réel =====

    private void SurMessage(RealtimeMessage message)
    {
        if (message.Type == JudiciaireRealtime.CheopsChanged)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = SectionCourante()?.RafraichirAsync());
    }

    public override void Resume() => SaphirSession.Realtime += SurMessage;

    public override void Suspend() => SaphirSession.Realtime -= SurMessage;
}

/// <summary>Écran d'un fichier CHEOPS.</summary>
public abstract class CheopsSectionBase(CheopsViewModel cheops) : SousVueBase(cheops)
{
    protected CheopsViewModel Cheops { get; } = cheops;

    /// <summary>L'agent arrive sur l'écran (premier chargement éventuel).</summary>
    public virtual Task EntrerAsync() => Task.CompletedTask;

    /// <summary>Une fiche a changé sur le serveur : recharger ce qui est affiché.</summary>
    public virtual Task RafraichirAsync() => Task.CompletedTask;

    protected static bool Peut(params string[] permissions) => permissions.Any(SaphirSession.Has);

    protected static void Remplir<T>(ObservableCollection<T> cible, IEnumerable<T> source)
    {
        cible.Clear();
        foreach (var e in source)
            cible.Add(e);
    }

    protected static string Chemin(string debut, params (string Nom, string? Valeur)[] parametres)
    {
        var morceaux = parametres.Where(p => !string.IsNullOrWhiteSpace(p.Valeur)).Select(p => p.Nom + "=" + ApiClient.Q(p.Valeur!.Trim())).ToList();
        return morceaux.Count == 0 ? debut : debut + "?" + string.Join("&", morceaux);
    }
}

// =====================================================================================
// Recherche générale (11.2) et fiche d'identité
// =====================================================================================

public sealed record VehiculeLigne(VehiculeResumeDto Dto)
{
    public string Immatriculation => Dto.Immatriculation;
    public string Vehicule => $"{Dto.Marque} {Dto.Modele}".Trim();
    public string Couleur => Dto.Couleur ?? string.Empty;
    public string Titulaire => Dto.Titulaire ?? "—";
    public string Alertes => FormatsJ.Alertes(Dto.Alertes);
}

public sealed record PersonneLigne(PersonneResumeDto Dto)
{
    public string Reference => Dto.Reference;
    public string Nom => Dto.Nom;
    public string Prenom => Dto.Prenom;
    public string Naissance => FormatsJ.Jour(Dto.DateNaissance);
    public string Lieu => Dto.LieuNaissance ?? string.Empty;
    public string Alertes => FormatsJ.Alertes(Dto.Alertes);
}

public sealed class RechercheGeneraleViewModel : CheopsSectionBase
{
    private string? _nom, _prenom, _alias, _naissance, _lieu, _identifiant, _dossier, _immatriculation;
    private PersonneLigne? _personneSelection;
    private VehiculeLigne? _vehiculeSelection;
    private CorrespondanceDto? _dossierSelection;
    private PersonneFicheDto? _fiche;
    private bool _modeCreation;
    private string _editNom = string.Empty, _editPrenom = string.Empty;
    private string? _editNaissance, _editLieu, _editSexe, _editNationalite, _editAdresse, _editTelephone, _editSignalement, _editAlias, _editMotif;
    private string? _docType = "Carte nationale d'identité", _docNumero, _docDelivre, _docExpire, _docAutorite;

    public RechercheGeneraleViewModel(CheopsViewModel cheops) : base(cheops)
    {
        RechercherCommand = new RelayCommand(() => _ = RechercherAsync());
        EffacerCommand = new RelayCommand(Effacer);
        NouvelleIdentiteCommand = new RelayCommand(NouvelleIdentite);
        EnregistrerIdentiteCommand = new RelayCommand(() => _ = EnregistrerIdentiteAsync());
        AjouterDocumentCommand = new RelayCommand(() => _ = AjouterDocumentAsync());
        OuvrirFichierCommand = new RelayCommand(p => { if (p is string code && Fiche is { } f) _ = Cheops.OuvrirPersonneAsync(code, Resume(f)); });
        OuvrirVehiculeCommand = new RelayCommand(() => { if (VehiculeSelection is { } v) _ = Cheops.OuvrirVehiculeAsync(v.Dto.Id); });
        OuvrirDossierCommand = new RelayCommand(() => { if (DossierSelection is { } d) _ = Cheops.OuvrirDossierAsync(d); });
    }

    private static PersonneResumeDto Resume(PersonneFicheDto f)
        => new(f.Id, f.Reference, f.Nom, f.Prenom, f.DateNaissance, f.LieuNaissance, f.Sexe, f.Alertes);

    public string? Nom { get => _nom; set => SetProperty(ref _nom, value); }
    public string? Prenom { get => _prenom; set => SetProperty(ref _prenom, value); }
    public string? Alias { get => _alias; set => SetProperty(ref _alias, value); }
    public string? Naissance { get => _naissance; set => SetProperty(ref _naissance, value); }
    public string? Lieu { get => _lieu; set => SetProperty(ref _lieu, value); }
    public string? Identifiant { get => _identifiant; set => SetProperty(ref _identifiant, value); }
    public string? Dossier { get => _dossier; set => SetProperty(ref _dossier, value); }
    public string? Immatriculation { get => _immatriculation; set => SetProperty(ref _immatriculation, value); }

    public ObservableCollection<PersonneLigne> Personnes { get; } = [];
    public ObservableCollection<VehiculeLigne> Vehicules { get; } = [];
    public ObservableCollection<CorrespondanceDto> Dossiers { get; } = [];
    public string Resultat => $"{Personnes.Count} personne(s), {Vehicules.Count} véhicule(s), {Dossiers.Count} dossier(s)";

    public bool PeutVehicules => Peut(Permissions.CheopsVehicules);
    public bool PeutModifier => Peut(Permissions.LrppnRediger, Permissions.CheopsFprInscrire);
    public bool PeutTaj => Peut(Permissions.CheopsTaj);
    public bool PeutFpr => Peut(Permissions.CheopsFpr);
    public bool PeutSnpc => Peut(Permissions.CheopsSnpc);
    public bool PeutFijais => Peut(Permissions.CheopsFijais);

    public PersonneLigne? PersonneSelection
    {
        get => _personneSelection;
        set
        {
            if (SetProperty(ref _personneSelection, value) && value is not null)
                _ = ChargerFicheAsync(value.Dto.Id);
        }
    }

    public VehiculeLigne? VehiculeSelection { get => _vehiculeSelection; set => SetProperty(ref _vehiculeSelection, value); }
    public CorrespondanceDto? DossierSelection { get => _dossierSelection; set => SetProperty(ref _dossierSelection, value); }

    public PersonneFicheDto? Fiche
    {
        get => _fiche;
        private set
        {
            if (!SetProperty(ref _fiche, value))
                return;
            OnPropertyChanged(nameof(AFiche));
            OnPropertyChanged(nameof(EditionVisible));
        }
    }

    public bool AFiche => Fiche is not null;
    public bool ModeCreation { get => _modeCreation; set { if (SetProperty(ref _modeCreation, value)) OnPropertyChanged(nameof(EditionVisible)); } }
    public bool EditionVisible => ModeCreation || (AFiche && PeutModifier);
    public string TitreEdition => ModeCreation ? "Nouvelle identité" : "Modifier l'identité (historisé)";

    public string EditNom { get => _editNom; set => SetProperty(ref _editNom, value); }
    public string EditPrenom { get => _editPrenom; set => SetProperty(ref _editPrenom, value); }
    public string? EditNaissance { get => _editNaissance; set => SetProperty(ref _editNaissance, value); }
    public string? EditLieu { get => _editLieu; set => SetProperty(ref _editLieu, value); }
    public string? EditSexe { get => _editSexe; set => SetProperty(ref _editSexe, value); }
    public string? EditNationalite { get => _editNationalite; set => SetProperty(ref _editNationalite, value); }
    public string? EditAdresse { get => _editAdresse; set => SetProperty(ref _editAdresse, value); }
    public string? EditTelephone { get => _editTelephone; set => SetProperty(ref _editTelephone, value); }
    public string? EditSignalement { get => _editSignalement; set => SetProperty(ref _editSignalement, value); }
    public string? EditAlias { get => _editAlias; set => SetProperty(ref _editAlias, value); }
    public string? EditMotif { get => _editMotif; set => SetProperty(ref _editMotif, value); }
    public IReadOnlyList<string> Sexes { get; } = ["M", "F", "X"];

    public string? DocType { get => _docType; set => SetProperty(ref _docType, value); }
    public string? DocNumero { get => _docNumero; set => SetProperty(ref _docNumero, value); }
    public string? DocDelivre { get => _docDelivre; set => SetProperty(ref _docDelivre, value); }
    public string? DocExpire { get => _docExpire; set => SetProperty(ref _docExpire, value); }
    public string? DocAutorite { get => _docAutorite; set => SetProperty(ref _docAutorite, value); }
    public IReadOnlyList<string> TypesDocuments { get; } = ["Carte nationale d'identité", "Passeport", "Titre de séjour", "Permis de conduire", "Autre"];

    public RelayCommand RechercherCommand { get; }
    public RelayCommand EffacerCommand { get; }
    public RelayCommand NouvelleIdentiteCommand { get; }
    public RelayCommand EnregistrerIdentiteCommand { get; }
    public RelayCommand AjouterDocumentCommand { get; }
    public RelayCommand OuvrirFichierCommand { get; }
    public RelayCommand OuvrirVehiculeCommand { get; }
    public RelayCommand OuvrirDossierCommand { get; }

    private void Effacer()
    {
        Nom = Prenom = Alias = Naissance = Lieu = Identifiant = Dossier = Immatriculation = null;
        Personnes.Clear();
        Vehicules.Clear();
        Dossiers.Clear();
        Fiche = null;
        Cheops.DefinirBandeaux([]);
        OnPropertyChanged(nameof(Resultat));
    }

    private async Task RechercherAsync()
    {
        if (Api is not { } api)
            return;
        await Executer(async () =>
        {
            var chemin = Chemin("api/cheops/recherche", ("nom", Nom), ("prenom", Prenom), ("alias", Alias), ("naissance", Naissance),
                ("lieu", Lieu), ("identifiant", Identifiant), ("dossier", Dossier), ("immatriculation", Immatriculation));
            if (!chemin.Contains('?'))
                throw new ArgumentException("Renseigne au moins un critère de recherche.");
            var resultat = await api.GetAsync<RechercheGeneraleDto>(chemin);
            Remplir(Personnes, resultat.Personnes.Select(p => new PersonneLigne(p)));
            Remplir(Vehicules, resultat.Vehicules.Select(v => new VehiculeLigne(v)));
            Remplir(Dossiers, resultat.Dossiers);
            OnPropertyChanged(nameof(Resultat));
        });
    }

    private async Task ChargerFicheAsync(long id)
    {
        if (Api is not { } api)
            return;
        await Executer(async () =>
        {
            var fiche = await api.GetAsync<PersonneFicheDto>($"api/cheops/personnes/{id}");
            AfficherFiche(fiche);
        });
    }

    private void AfficherFiche(PersonneFicheDto fiche)
    {
        ModeCreation = false;
        Fiche = fiche;
        Cheops.DefinirBandeaux(fiche.Bandeaux);
        EditNom = fiche.Nom;
        EditPrenom = fiche.Prenom;
        EditNaissance = FormatsJ.JourSaisie(fiche.DateNaissance);
        EditLieu = fiche.LieuNaissance;
        EditSexe = fiche.Sexe;
        EditNationalite = fiche.Nationalite;
        EditAdresse = fiche.Adresse;
        EditTelephone = fiche.Telephone;
        EditSignalement = fiche.Signalement;
        EditAlias = string.Join(", ", fiche.Alias.Select(a => a.Alias));
        EditMotif = null;
        OnPropertyChanged(nameof(TitreEdition));
    }

    private void NouvelleIdentite()
    {
        Fiche = null;
        ModeCreation = true;
        EditNom = Nom ?? string.Empty;
        EditPrenom = Prenom ?? string.Empty;
        EditNaissance = Naissance;
        EditLieu = Lieu;
        EditSexe = null;
        EditNationalite = "Française";
        EditAdresse = EditTelephone = EditSignalement = EditAlias = EditMotif = null;
        OnPropertyChanged(nameof(TitreEdition));
    }

    private async Task EnregistrerIdentiteAsync()
    {
        if (Api is not { } api)
            return;
        await Executer(async () =>
        {
            var naissance = FormatsJ.LireJour(EditNaissance, "Date de naissance");
            if (ModeCreation)
            {
                var creee = await api.PostAsync<PersonneResumeDto>("api/personnes",
                    new PersonneCreationInput(EditNom, EditPrenom, naissance, EditLieu, EditSexe, EditNationalite));
                Personnes.Insert(0, new PersonneLigne(creee));
                if (PeutModifier && (EditAdresse ?? EditTelephone ?? EditSignalement ?? EditAlias) is not null)
                    await ModifierAsync(api, creee.Id, naissance);
                await ChargerFicheAsync(creee.Id);
                return;
            }
            if (Fiche is { } fiche)
                AfficherFiche(await ModifierAsync(api, fiche.Id, naissance));
        }, ModeCreation ? "Identité créée." : "Identité enregistrée.");
    }

    private Task<PersonneFicheDto> ModifierAsync(ApiClient api, long id, DateOnly? naissance)
        => api.SendAsync<PersonneFicheDto>(HttpMethod.Put, $"api/cheops/personnes/{id}",
            new PersonneModificationInput(EditNom, EditPrenom, naissance, EditLieu, EditSexe, EditNationalite, EditAdresse, EditTelephone,
                EditSignalement, (EditAlias ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), EditMotif));

    private async Task AjouterDocumentAsync()
    {
        if (Api is not { } api || Fiche is not { } fiche)
            return;
        await Executer(async () =>
        {
            var maj = await api.PostAsync<PersonneFicheDto>($"api/cheops/personnes/{fiche.Id}/documents",
                new DocumentIdentiteInput(DocType ?? "Autre", DocNumero ?? string.Empty, FormatsJ.LireJour(DocDelivre, "Délivrance"),
                    FormatsJ.LireJour(DocExpire, "Expiration"), DocAutorite));
            AfficherFiche(maj);
            DocNumero = DocDelivre = DocExpire = DocAutorite = null;
        }, "Document d'identité ajouté.");
    }

    public override async Task RafraichirAsync()
    {
        if (Fiche is { } fiche)
            await ChargerFicheAsync(fiche.Id);
    }
}

// =====================================================================================
// TAJ (12.1)
// =====================================================================================

public sealed record TajLigne(TajLigneDto Dto)
{
    public string Reference => Dto.Personne.Reference;
    public string Identite => Dto.Personne.Libelle;
    public int Faits => Dto.NombreFaits;
    public string Dernier => FormatsJ.Date(Dto.DernierFait);
}

public sealed record TajFaitLigne(TajFaitDto Dto)
{
    public string Reference => Dto.Reference;
    public string Date => FormatsJ.DateHeure(Dto.FaitsLe);
    public string Qualite => Dto.Qualite;
    public string Faits => Dto.Faits + (Dto.Natinf is null ? string.Empty : $" (NATINF {Dto.Natinf})");
    public string Lieu => Dto.Lieu ?? string.Empty;
    public string Service => Dto.Service ?? string.Empty;
    public string Procedure => Dto.ProcedureNumero ?? string.Empty;
    public string Suite => Dto.Suite ?? string.Empty;
    public string Source => Dto.Source;
}

public sealed class TajViewModel : CheopsSectionBase
{
    private string? _q, _naissance, _reference, _procedure;
    private TajLigne? _selection;
    private TajFicheDto? _fiche;
    private TajFaitLigne? _faitSelection;
    private string _qualite = TajQualites.MisEnCause;
    private string? _faits, _natinf, _faitsLe, _lieu, _service, _procedureNumero, _suite, _nouvelleSuite, _motifSuite;

    public TajViewModel(CheopsViewModel cheops) : base(cheops)
    {
        RechercherCommand = new RelayCommand(() => _ = RechercherAsync());
        AjouterCommand = new RelayCommand(() => _ = AjouterAsync());
        ModifierSuiteCommand = new RelayCommand(() => _ = ModifierSuiteAsync());
    }

    public string? Q { get => _q; set => SetProperty(ref _q, value); }
    public string? Naissance { get => _naissance; set => SetProperty(ref _naissance, value); }
    public string? Reference { get => _reference; set => SetProperty(ref _reference, value); }
    public string? Procedure { get => _procedure; set => SetProperty(ref _procedure, value); }

    public ObservableCollection<TajLigne> Lignes { get; } = [];
    public ObservableCollection<TajFaitLigne> Faits { get; } = [];
    public ObservableCollection<HistoriqueLigne> Historique { get; } = [];
    public bool PeutInscrire => Peut(Permissions.LrppnValider);

    public TajLigne? Selection
    {
        get => _selection;
        set
        {
            if (SetProperty(ref _selection, value) && value is not null)
                _ = ChargerPersonneAsync(value.Dto.Personne.Id);
        }
    }

    public TajFicheDto? Fiche
    {
        get => _fiche;
        private set
        {
            if (SetProperty(ref _fiche, value))
                OnPropertyChanged(nameof(AFiche));
        }
    }

    public bool AFiche => Fiche is not null;

    public TajFaitLigne? FaitSelection
    {
        get => _faitSelection;
        set
        {
            if (SetProperty(ref _faitSelection, value))
                NouvelleSuite = value?.Dto.Suite;
        }
    }

    public IReadOnlyList<string> Qualites => TajQualites.All;
    public string QualiteSaisie { get => _qualite; set => SetProperty(ref _qualite, value); }
    public string? FaitsSaisie { get => _faits; set => SetProperty(ref _faits, value); }
    public string? Natinf { get => _natinf; set => SetProperty(ref _natinf, value); }
    public string? FaitsLe { get => _faitsLe; set => SetProperty(ref _faitsLe, value); }
    public string? Lieu { get => _lieu; set => SetProperty(ref _lieu, value); }
    public string? Service { get => _service; set => SetProperty(ref _service, value); }
    public string? ProcedureNumero { get => _procedureNumero; set => SetProperty(ref _procedureNumero, value); }
    public string? Suite { get => _suite; set => SetProperty(ref _suite, value); }
    public string? NouvelleSuite { get => _nouvelleSuite; set => SetProperty(ref _nouvelleSuite, value); }
    public string? MotifSuite { get => _motifSuite; set => SetProperty(ref _motifSuite, value); }

    public RelayCommand RechercherCommand { get; }
    public RelayCommand AjouterCommand { get; }
    public RelayCommand ModifierSuiteCommand { get; }

    private async Task RechercherAsync()
    {
        if (Api is not { } api)
            return;
        await Executer(async () =>
        {
            var chemin = Chemin("api/cheops/taj", ("q", Q), ("naissance", Naissance), ("reference", Reference), ("procedure", Procedure));
            if (!chemin.Contains('?'))
                throw new ArgumentException("Renseigne un NOM, une date de naissance, une référence TAJ ou un numéro de procédure.");
            Remplir(Lignes, (await api.GetAsync<List<TajLigneDto>>(chemin)).Select(l => new TajLigne(l)));
            if (Lignes.Count == 0)
                throw new ApiException("Aucune personne connue du TAJ pour ces critères.");
        });
    }

    public async Task ChargerPersonneAsync(long personneId)
    {
        if (Api is not { } api)
            return;
        await Executer(async () =>
        {
            var fiche = await api.GetAsync<TajFicheDto>($"api/cheops/taj/personnes/{personneId}");
            Fiche = fiche;
            Remplir(Faits, fiche.Faits.Select(f => new TajFaitLigne(f)));
            Remplir(Historique, fiche.Historique.Select(HistoriqueLigne.De));
            Cheops.DefinirBandeaux(fiche.Personne.Bandeaux);
        });
    }

    private async Task AjouterAsync()
    {
        if (Api is not { } api || Fiche is not { } fiche)
            return;
        await Executer(async () =>
        {
            await api.PostAsync<object>("api/cheops/taj", new TajFaitInput(fiche.Personne.Id, QualiteSaisie, FaitsSaisie ?? string.Empty, Natinf,
                FormatsJ.LireDateHeure(FaitsLe, "Date des faits"), Lieu, Service, ProcedureNumero, Suite));
            FaitsSaisie = Natinf = FaitsLe = Lieu = ProcedureNumero = Suite = null;
            await ChargerPersonneAsync(fiche.Personne.Id);
        }, "Inscription TAJ enregistrée.");
    }

    private async Task ModifierSuiteAsync()
    {
        if (Api is not { } api || FaitSelection is not { } fait || Fiche is not { } fiche)
            return;
        await Executer(async () =>
        {
            await api.SendAsync(HttpMethod.Put, $"api/cheops/taj/{fait.Dto.Id}/suite", new TajSuiteInput(NouvelleSuite, MotifSuite));
            MotifSuite = null;
            await ChargerPersonneAsync(fiche.Personne.Id);
        }, "Suite enregistrée.");
    }

    public override async Task RafraichirAsync()
    {
        if (Fiche is { } fiche)
            await ChargerPersonneAsync(fiche.Personne.Id);
    }
}

// =====================================================================================
// FPR (12.2)
// =====================================================================================

public sealed record FprLigne(FprFicheDto Dto)
{
    public string Numero => Dto.Numero;
    public string Identite => Dto.Personne.Libelle;
    public string Categorie => Dto.Categorie;
    public string Motif => Dto.Motif;
    public string Statut => Dto.Statut;
    public string Creee => FormatsJ.Date(Dto.CreeLe);
    public string Reexamen => FormatsJ.Jour(Dto.ReexamenLe);
}

public sealed class FprViewModel : CheopsSectionBase
{
    private string? _q;
    private string _statutFiltre = FprStatuts.Active;
    private FprLigne? _selection;
    private FprFicheDto? _detail;
    private string? _nouveauStatut, _motifStatut;
    private bool _modeInscription;
    private string? _categorie, _motif, _conduite, _procedure, _service, _reexamen, _informations;
    private bool _dangereux, _arme;
    private bool _chargee;

    public FprViewModel(CheopsViewModel cheops) : base(cheops)
    {
        Picker = new PersonnePickerViewModel(cheops);
        RechercherCommand = new RelayCommand(() => _ = RechercherAsync());
        ChangerStatutCommand = new RelayCommand(() => _ = ChangerStatutAsync());
        BasculerInscriptionCommand = new RelayCommand(() => ModeInscription = !ModeInscription);
        InscrireCommand = new RelayCommand(() => _ = InscrireAsync());
    }

    public PersonnePickerViewModel Picker { get; }
    public string? Q { get => _q; set => SetProperty(ref _q, value); }
    public IReadOnlyList<string> Filtres { get; } = ["Tous", .. FprStatuts.All];
    public string StatutFiltre { get => _statutFiltre; set { if (SetProperty(ref _statutFiltre, value)) _ = RechercherAsync(); } }
    public ObservableCollection<FprLigne> Lignes { get; } = [];
    public ObservableCollection<HistoriqueLigne> Historique { get; } = [];
    public bool PeutInscrire => Peut(Permissions.CheopsFprInscrire);

    public FprLigne? Selection
    {
        get => _selection;
        set
        {
            if (SetProperty(ref _selection, value) && value is not null)
                _ = ChargerAsync(value.Dto.Id);
        }
    }

    public FprFicheDto? Detail
    {
        get => _detail;
        private set
        {
            if (!SetProperty(ref _detail, value))
                return;
            OnPropertyChanged(nameof(ADetail));
            OnPropertyChanged(nameof(DetailTexte));
        }
    }

    public bool ADetail => Detail is not null;

    public string DetailTexte => Detail is { } d
        ? $"Fiche {d.Numero} — {d.Statut}\nIdentité : {d.Personne.Libelle} ({d.Personne.Reference})\nCatégorie : {d.Categorie}\nMotif : {d.Motif}\n" +
          $"Procédure : {d.ProcedureNumero ?? "—"}   Service : {d.Service ?? "—"}\nCréée le {FormatsJ.Date(d.CreeLe)} par {d.CreePar}   Réexamen : {FormatsJ.Jour(d.ReexamenLe)}\n" +
          (d.Dangereux ? "PERSONNE SIGNALÉE DANGEREUSE\n" : string.Empty) + (d.Arme ? "PERSONNE SUSCEPTIBLE D'ÊTRE ARMÉE\n" : string.Empty) +
          $"Informations autorisées : {d.Informations ?? "—"}"
        : string.Empty;

    public string ConduiteATenir => Detail?.ConduiteATenir ?? string.Empty;
    public IReadOnlyList<string> Statuts => FprStatuts.All;
    public string? NouveauStatut { get => _nouveauStatut; set => SetProperty(ref _nouveauStatut, value); }
    public string? MotifStatut { get => _motifStatut; set => SetProperty(ref _motifStatut, value); }

    public bool ModeInscription { get => _modeInscription; set => SetProperty(ref _modeInscription, value); }
    public IReadOnlyList<string> Categories => FprCategories.All;
    public string? Categorie { get => _categorie; set => SetProperty(ref _categorie, value); }
    public string? Motif { get => _motif; set => SetProperty(ref _motif, value); }
    public string? Conduite { get => _conduite; set => SetProperty(ref _conduite, value); }
    public string? Procedure { get => _procedure; set => SetProperty(ref _procedure, value); }
    public string? Service { get => _service; set => SetProperty(ref _service, value); }
    public string? Reexamen { get => _reexamen; set => SetProperty(ref _reexamen, value); }
    public string? Informations { get => _informations; set => SetProperty(ref _informations, value); }
    public bool Dangereux { get => _dangereux; set => SetProperty(ref _dangereux, value); }
    public bool Arme { get => _arme; set => SetProperty(ref _arme, value); }

    public RelayCommand RechercherCommand { get; }
    public RelayCommand ChangerStatutCommand { get; }
    public RelayCommand BasculerInscriptionCommand { get; }
    public RelayCommand InscrireCommand { get; }

    public override async Task EntrerAsync()
    {
        if (!_chargee)
        {
            _chargee = true;
            await RechercherAsync();
        }
    }

    private async Task RechercherAsync()
    {
        if (Api is not { } api)
            return;
        await Executer(async () =>
        {
            var liste = await api.GetAsync<List<FprFicheDto>>(Chemin("api/cheops/fpr", ("q", Q), ("statut", StatutFiltre == "Tous" ? null : StatutFiltre)));
            var id = Selection?.Dto.Id;
            Remplir(Lignes, liste.Select(f => new FprLigne(f)));
            if (id is { } i)
                _selection = Lignes.FirstOrDefault(l => l.Dto.Id == i);
            OnPropertyChanged(nameof(Selection));
        });
    }

    public async Task ChercherPersonneAsync(PersonneResumeDto personne)
    {
        Q = personne.Reference;
        _statutFiltre = "Tous";
        OnPropertyChanged(nameof(StatutFiltre));
        await RechercherAsync();
        if (Lignes.FirstOrDefault() is { } premiere)
            Selection = premiere;
        else
            Cheops.DefinirBandeaux([]);
    }

    public async Task ChargerAsync(long id)
    {
        if (Api is not { } api)
            return;
        await Executer(async () =>
        {
            var fiche = await api.GetAsync<FprFicheDto>($"api/cheops/fpr/{id}");
            Detail = fiche;
            OnPropertyChanged(nameof(ConduiteATenir));
            Remplir(Historique, fiche.Historique.Select(HistoriqueLigne.De));
            NouveauStatut = null;
            MotifStatut = null;
            await Cheops.BandeauxPersonneAsync(fiche.Personne.Id);
        });
    }

    private async Task ChangerStatutAsync()
    {
        if (Api is not { } api || Detail is not { } d || string.IsNullOrWhiteSpace(NouveauStatut))
            return;
        if (NouveauStatut == FprStatuts.Levee &&
            !await Hote.ConfirmerAsync("Lever la fiche", $"Lever la fiche {d.Numero} ? La personne ne sera plus signalée comme recherchée.", "Lever", true))
            return;
        await Executer(async () =>
        {
            var fiche = await api.SendAsync<FprFicheDto>(HttpMethod.Put, $"api/cheops/fpr/{d.Id}/statut", new StatutInput(NouveauStatut!, MotifStatut));
            Detail = fiche;
            Remplir(Historique, fiche.Historique.Select(HistoriqueLigne.De));
            await RechercherAsync();
            await Cheops.BandeauxPersonneAsync(fiche.Personne.Id);
        }, "Statut de la fiche modifié.");
    }

    private async Task InscrireAsync()
    {
        if (Api is not { } api)
            return;
        await Executer(async () =>
        {
            var personne = Picker.Selection ?? throw new ArgumentException("Sélectionne la personne à inscrire.");
            var fiche = await api.PostAsync<FprFicheDto>("api/cheops/fpr", new FprInput(personne.Id, Categorie ?? string.Empty, Motif ?? string.Empty,
                Conduite ?? string.Empty, Procedure, Service, FormatsJ.LireJour(Reexamen, "Réexamen"), Informations, Dangereux, Arme));
            ModeInscription = false;
            Motif = Conduite = Procedure = Informations = Reexamen = null;
            Dangereux = Arme = false;
            await RechercherAsync();
            await ChargerAsync(fiche.Id);
        }, "Fiche FPR inscrite.");
    }

    public override async Task RafraichirAsync()
    {
        await RechercherAsync();
        if (Detail is { } d)
            await ChargerAsync(d.Id);
    }
}

// =====================================================================================
// FOVeS (12.3)
// =====================================================================================

public sealed record FovesLigne(FovesFicheDto Dto)
{
    public string Reference => Dto.Reference;
    public string Type => Dto.TypeObjet;
    public string Identifiant => Dto.Identifiant;
    public string Nature => Dto.Nature;
    public string Date => FormatsJ.DateHeure(Dto.SignaleLe);
    public string Statut => Dto.Statut;
    public string Service => Dto.Service ?? string.Empty;
}

public sealed class FovesViewModel : CheopsSectionBase
{
    private string? _q;
    private string _statutFiltre = FovesStatuts.Actif;
    private FovesLigne? _selection;
    private FovesFicheDto? _detail;
    private bool _modeSignalement, _chargee;
    private string _type = FovesTypes.All[0];
    private string _nature = FovesTypes.Natures[0];
    private string? _identifiant, _description, _signaleLe, _service, _procedure, _conduite;
    private string? _nouveauStatut, _decouvertLe, _lieuDecouverte, _motif;

    public FovesViewModel(CheopsViewModel cheops) : base(cheops)
    {
        RechercherCommand = new RelayCommand(() => _ = RechercherAsync());
        BasculerSignalementCommand = new RelayCommand(() => ModeSignalement = !ModeSignalement);
        SignalerCommand = new RelayCommand(() => _ = SignalerAsync());
        ChangerStatutCommand = new RelayCommand(() => _ = ChangerStatutAsync());
    }

    public string? Q { get => _q; set => SetProperty(ref _q, value); }
    public IReadOnlyList<string> Filtres { get; } = ["Tous", .. FovesStatuts.All];
    public string StatutFiltre { get => _statutFiltre; set { if (SetProperty(ref _statutFiltre, value)) _ = RechercherAsync(); } }
    public ObservableCollection<FovesLigne> Lignes { get; } = [];
    public ObservableCollection<HistoriqueLigne> Historique { get; } = [];
    public bool PeutSignaler => Peut(Permissions.CheopsFprInscrire, Permissions.LrppnRediger);

    public FovesLigne? Selection
    {
        get => _selection;
        set
        {
            if (SetProperty(ref _selection, value) && value is not null)
                _ = ChargerAsync(value.Dto.Id);
        }
    }

    public FovesFicheDto? Detail
    {
        get => _detail;
        private set
        {
            if (!SetProperty(ref _detail, value))
                return;
            OnPropertyChanged(nameof(ADetail));
            OnPropertyChanged(nameof(DetailTexte));
        }
    }

    public bool ADetail => Detail is not null;

    public string DetailTexte => Detail is { } d
        ? $"{d.Reference} — {d.Statut}\n{d.TypeObjet} : {d.Identifiant}\nNature : {d.Nature} — signalé le {FormatsJ.DateHeure(d.SignaleLe)}\n" +
          $"Description : {d.Description ?? "—"}\nService : {d.Service ?? "—"}   Procédure : {d.ProcedureNumero ?? "—"}\n" +
          $"Conduite à tenir : {d.ConduiteATenir ?? "—"}\n" +
          (d.DecouvertLe is { } dl ? $"Découvert le {FormatsJ.DateHeure(dl)} — {d.LieuDecouverte}" : "Non découvert")
        : string.Empty;

    public bool ModeSignalement { get => _modeSignalement; set => SetProperty(ref _modeSignalement, value); }
    public IReadOnlyList<string> Types => FovesTypes.All;
    public IReadOnlyList<string> Natures => FovesTypes.Natures;
    public IReadOnlyList<string> Statuts => FovesStatuts.All;
    public string TypeObjet { get => _type; set => SetProperty(ref _type, value); }
    public string Nature { get => _nature; set => SetProperty(ref _nature, value); }
    public string? Identifiant { get => _identifiant; set => SetProperty(ref _identifiant, value); }
    public string? Description { get => _description; set => SetProperty(ref _description, value); }
    public string? SignaleLe { get => _signaleLe; set => SetProperty(ref _signaleLe, value); }
    public string? Service { get => _service; set => SetProperty(ref _service, value); }
    public string? Procedure { get => _procedure; set => SetProperty(ref _procedure, value); }
    public string? Conduite { get => _conduite; set => SetProperty(ref _conduite, value); }
    public string? NouveauStatut { get => _nouveauStatut; set => SetProperty(ref _nouveauStatut, value); }
    public string? DecouvertLe { get => _decouvertLe; set => SetProperty(ref _decouvertLe, value); }
    public string? LieuDecouverte { get => _lieuDecouverte; set => SetProperty(ref _lieuDecouverte, value); }
    public string? Motif { get => _motif; set => SetProperty(ref _motif, value); }

    public RelayCommand RechercherCommand { get; }
    public RelayCommand BasculerSignalementCommand { get; }
    public RelayCommand SignalerCommand { get; }
    public RelayCommand ChangerStatutCommand { get; }

    public override async Task EntrerAsync()
    {
        if (!_chargee)
        {
            _chargee = true;
            await RechercherAsync();
        }
    }

    private async Task RechercherAsync()
    {
        if (Api is not { } api)
            return;
        await Executer(async () =>
            Remplir(Lignes, (await api.GetAsync<List<FovesFicheDto>>(Chemin("api/cheops/foves", ("q", Q), ("statut", StatutFiltre == "Tous" ? null : StatutFiltre))))
                .Select(f => new FovesLigne(f))));
    }

    public async Task ChargerAsync(long id)
    {
        if (Api is not { } api)
            return;
        await Executer(async () =>
        {
            var fiche = await api.GetAsync<FovesFicheDto>($"api/cheops/foves/{id}");
            Detail = fiche;
            Remplir(Historique, fiche.Historique.Select(HistoriqueLigne.De));
            var bandeaux = new List<AlerteBandeauDto>();
            if (fiche.Statut == FovesStatuts.Actif)
                bandeaux.Add(new AlerteBandeauDto(FichiersCheops.Foves, fiche.Reference, $"OBJET SIGNALÉ AU FOVeS — {fiche.Nature} — {fiche.Identifiant}",
                    fiche.ConduiteATenir ?? "Aviser le CIC."));
            Cheops.DefinirBandeaux(bandeaux);
        });
    }

    private async Task SignalerAsync()
    {
        if (Api is not { } api)
            return;
        await Executer(async () =>
        {
            var fiche = await api.PostAsync<FovesFicheDto>("api/cheops/foves", new FovesInput(TypeObjet, Identifiant ?? string.Empty, Description, Nature,
                FormatsJ.LireDateHeure(SignaleLe, "Date du signalement"), Service, Procedure, Conduite));
            ModeSignalement = false;
            Identifiant = Description = SignaleLe = Procedure = Conduite = null;
            await RechercherAsync();
            await ChargerAsync(fiche.Id);
        }, "Signalement enregistré.");
    }

    private async Task ChangerStatutAsync()
    {
        if (Api is not { } api || Detail is not { } d || string.IsNullOrWhiteSpace(NouveauStatut))
            return;
        await Executer(async () =>
        {
            var fiche = await api.SendAsync<FovesFicheDto>(HttpMethod.Put, $"api/cheops/foves/{d.Id}/statut",
                new FovesStatutInput(NouveauStatut!, FormatsJ.LireDateHeure(DecouvertLe, "Date de découverte"), LieuDecouverte, Motif));
            NouveauStatut = DecouvertLe = LieuDecouverte = Motif = null;
            await RechercherAsync();
            await ChargerAsync(fiche.Id);
        }, "Statut du signalement modifié.");
    }

    public override async Task RafraichirAsync()
    {
        await RechercherAsync();
        if (Detail is { } d)
            await ChargerAsync(d.Id);
    }
}

// =====================================================================================
// SIV (12.4)
// =====================================================================================

public sealed record AssuranceLigne(AssuranceDto Dto)
{
    public string Assureur => Dto.Assureur;
    public string Contrat => Dto.Contrat;
    public string Periode => $"{FormatsJ.Jour(Dto.Debut)} → {(Dto.Fin is { } f ? FormatsJ.Jour(f) : "sans terme")}";
    public string Statut => Dto.Statut;
    public string Couverture => Dto.Couvert ? "Couvert" : "Non couvert";
    public string Verifie => FormatsJ.DateHeure(Dto.VerifieLe);
}

public sealed class SivViewModel : CheopsSectionBase
{
    private string? _q;
    private VehiculeLigne? _selection;
    private VehiculeFicheDto? _fiche;
    private bool _modeEdition, _creation;
    private string? _immat, _vin, _marque, _modele, _version, _categorie, _couleur, _mec, _titulaireLibre, _adresse, _motif;
    private string _situation = SivSituations.Normale;

    public SivViewModel(CheopsViewModel cheops) : base(cheops)
    {
        Titulaire = new PersonnePickerViewModel(cheops);
        RechercherCommand = new RelayCommand(() => _ = RechercherAsync());
        NouveauCommand = new RelayCommand(Nouveau);
        ModifierCommand = new RelayCommand(Modifier);
        AnnulerCommand = new RelayCommand(() => ModeEdition = false);
        EnregistrerCommand = new RelayCommand(() => _ = EnregistrerAsync());
    }

    public PersonnePickerViewModel Titulaire { get; }
    public string? Q { get => _q; set => SetProperty(ref _q, value); }
    public ObservableCollection<VehiculeLigne> Lignes { get; } = [];
    public ObservableCollection<AssuranceLigne> Assurances { get; } = [];
    public ObservableCollection<FovesLigne> Signalements { get; } = [];
    public ObservableCollection<HistoriqueLigne> Historique { get; } = [];
    public bool PeutEcrire => Peut(Permissions.LrppnRediger, Permissions.CheopsFprInscrire);

    public VehiculeLigne? Selection
    {
        get => _selection;
        set
        {
            if (SetProperty(ref _selection, value) && value is not null)
                _ = ChargerAsync(value.Dto.Id);
        }
    }

    public VehiculeFicheDto? Fiche
    {
        get => _fiche;
        private set
        {
            if (!SetProperty(ref _fiche, value))
                return;
            OnPropertyChanged(nameof(AFiche));
            OnPropertyChanged(nameof(FicheTexte));
        }
    }

    public bool AFiche => Fiche is not null;

    public string FicheTexte => Fiche is { } f
        ? $"Immatriculation : {f.Immatriculation}   VIN : {f.Vin ?? "—"}\nVéhicule : {f.Marque} {f.Modele} {f.Version}\n" +
          $"Catégorie : {f.Categorie ?? "—"}   Couleur : {f.Couleur ?? "—"}   Mise en circulation : {FormatsJ.Jour(f.MiseEnCirculation)}\n" +
          $"Titulaire : {f.Titulaire ?? "—"}\nAdresse : {f.Adresse ?? "—"}\nSituation administrative : {f.Situation}   Source : {f.Source}" +
          (f.Alertes.Count > 0 ? $"\nAlertes : {FormatsJ.Alertes(f.Alertes)}" : string.Empty)
        : string.Empty;

    public bool ModeEdition { get => _modeEdition; set => SetProperty(ref _modeEdition, value); }
    public string TitreEdition => _creation ? "Nouveau véhicule" : "Modifier le véhicule (historisé)";
    public IReadOnlyList<string> Situations => SivSituations.All;
    public IReadOnlyList<string> Categories => SivSituations.Categories;
    public string? Immat { get => _immat; set => SetProperty(ref _immat, value); }
    public string? Vin { get => _vin; set => SetProperty(ref _vin, value); }
    public string? Marque { get => _marque; set => SetProperty(ref _marque, value); }
    public string? Modele { get => _modele; set => SetProperty(ref _modele, value); }
    public string? Version { get => _version; set => SetProperty(ref _version, value); }
    public string? Categorie { get => _categorie; set => SetProperty(ref _categorie, value); }
    public string? Couleur { get => _couleur; set => SetProperty(ref _couleur, value); }
    public string? Mec { get => _mec; set => SetProperty(ref _mec, value); }
    public string? TitulaireLibre { get => _titulaireLibre; set => SetProperty(ref _titulaireLibre, value); }
    public string? Adresse { get => _adresse; set => SetProperty(ref _adresse, value); }
    public string Situation { get => _situation; set => SetProperty(ref _situation, value); }
    public string? Motif { get => _motif; set => SetProperty(ref _motif, value); }

    public RelayCommand RechercherCommand { get; }
    public RelayCommand NouveauCommand { get; }
    public RelayCommand ModifierCommand { get; }
    public RelayCommand AnnulerCommand { get; }
    public RelayCommand EnregistrerCommand { get; }

    private async Task RechercherAsync()
    {
        if (Api is not { } api || string.IsNullOrWhiteSpace(Q))
            return;
        await Executer(async () =>
        {
            Remplir(Lignes, (await api.GetAsync<List<VehiculeResumeDto>>("api/cheops/siv?q=" + ApiClient.Q(Q!.Trim()))).Select(v => new VehiculeLigne(v)));
            if (Lignes.Count == 0)
                throw new ApiException("Aucun véhicule immatriculé ne correspond.");
        });
    }

    public async Task ChargerAsync(long id)
    {
        if (Api is not { } api)
            return;
        await Executer(async () =>
        {
            var fiche = await api.GetAsync<VehiculeFicheDto>($"api/cheops/siv/{id}");
            Fiche = fiche;
            Remplir(Assurances, fiche.Assurances.Select(a => new AssuranceLigne(a)));
            Remplir(Signalements, fiche.Signalements.Select(s => new FovesLigne(s)));
            Remplir(Historique, fiche.Historique.Select(HistoriqueLigne.De));
            Cheops.DefinirBandeaux(fiche.Bandeaux);
            ModeEdition = false;
        });
    }

    private void Nouveau()
    {
        _creation = true;
        Immat = Q;
        Vin = Marque = Modele = Version = Categorie = Couleur = Mec = TitulaireLibre = Adresse = Motif = null;
        Situation = SivSituations.Normale;
        Titulaire.Definir(null);
        OnPropertyChanged(nameof(TitreEdition));
        ModeEdition = true;
    }

    private void Modifier()
    {
        if (Fiche is not { } f)
            return;
        _creation = false;
        Immat = f.Immatriculation;
        Vin = f.Vin;
        Marque = f.Marque;
        Modele = f.Modele;
        Version = f.Version;
        Categorie = f.Categorie;
        Couleur = f.Couleur;
        Mec = FormatsJ.JourSaisie(f.MiseEnCirculation);
        TitulaireLibre = f.TitulaireId is null ? f.Titulaire : null;
        Adresse = f.Adresse;
        Situation = f.Situation;
        Motif = null;
        Titulaire.Definir(f.TitulaireId is { } t ? new PersonneResumeDto(t, string.Empty, f.Titulaire ?? string.Empty, string.Empty, null, null, null, Array.Empty<string>()) : null);
        OnPropertyChanged(nameof(TitreEdition));
        ModeEdition = true;
    }

    private async Task EnregistrerAsync()
    {
        if (Api is not { } api)
            return;
        await Executer(async () =>
        {
            var input = new VehiculeInput(Immat ?? string.Empty, Vin, Marque, Modele, Version, Categorie, Couleur, FormatsJ.LireJour(Mec, "Mise en circulation"),
                Titulaire.Selection?.Id, TitulaireLibre, Adresse, Situation, Motif);
            var fiche = _creation
                ? await api.PostAsync<VehiculeFicheDto>("api/cheops/siv", input)
                : await api.SendAsync<VehiculeFicheDto>(HttpMethod.Put, $"api/cheops/siv/{Fiche!.Id}", input);
            ModeEdition = false;
            await ChargerAsync(fiche.Id);
        }, "Véhicule enregistré.");
    }

    public override async Task RafraichirAsync()
    {
        if (Fiche is { } f && !ModeEdition)
            await ChargerAsync(f.Id);
    }
}

// =====================================================================================
// FVA (12.5)
// =====================================================================================

public sealed record FvaLigne(FvaLigneDto Dto)
{
    public string Plaque => Dto.Vehicule.Immatriculation;
    public string Vehicule => $"{Dto.Vehicule.Marque} {Dto.Vehicule.Modele}".Trim();
    public string Situation => Dto.Situation;
    public string Assureur => Dto.Contrat?.Assureur ?? "—";
    public string Contrat => Dto.Contrat?.Contrat ?? "—";
    public string Couverture => Dto.Contrat is { } c ? $"{FormatsJ.Jour(c.Debut)} → {(c.Fin is { } f ? FormatsJ.Jour(f) : "sans terme")}" : "—";
    public string Verifie => FormatsJ.DateHeure(Dto.Contrat?.VerifieLe);
}

public sealed class FvaViewModel : CheopsSectionBase
{
    private string? _q, _assureur, _contrat, _debut, _fin;
    private string _statut = AssuranceStatuts.Valide;
    private FvaLigne? _selection;

    public FvaViewModel(CheopsViewModel cheops) : base(cheops)
    {
        RechercherCommand = new RelayCommand(() => _ = RechercherAsync());
        VerifierCommand = new RelayCommand(() => _ = VerifierAsync());
        AjouterCommand = new RelayCommand(() => _ = AjouterAsync());
    }

    public string? Q { get => _q; set => SetProperty(ref _q, value); }
    public ObservableCollection<FvaLigne> Lignes { get; } = [];
    public FvaLigne? Selection { get => _selection; set { if (SetProperty(ref _selection, value)) OnPropertyChanged(nameof(ASelection)); } }
    public bool ASelection => Selection is not null;
    public bool PeutEcrire => Peut(Permissions.LrppnRediger, Permissions.CheopsFprInscrire);
    public IReadOnlyList<string> Statuts => AssuranceStatuts.All;
    public string? Assureur { get => _assureur; set => SetProperty(ref _assureur, value); }
    public string? Contrat { get => _contrat; set => SetProperty(ref _contrat, value); }
    public string? Debut { get => _debut; set => SetProperty(ref _debut, value); }
    public string? Fin { get => _fin; set => SetProperty(ref _fin, value); }
    public string Statut { get => _statut; set => SetProperty(ref _statut, value); }

    public RelayCommand RechercherCommand { get; }
    public RelayCommand VerifierCommand { get; }
    public RelayCommand AjouterCommand { get; }

    private async Task RechercherAsync()
    {
        if (Api is not { } api || string.IsNullOrWhiteSpace(Q))
            return;
        await Executer(async () =>
        {
            Remplir(Lignes, (await api.GetAsync<List<FvaLigneDto>>("api/cheops/fva?q=" + ApiClient.Q(Q!.Trim()))).Select(l => new FvaLigne(l)));
            if (Lignes.Count == 0)
                throw new ApiException("Aucun véhicule ne correspond à cette plaque.");
        });
    }

    private async Task VerifierAsync()
    {
        if (Api is not { } api || Selection?.Dto.Contrat is not { } contrat)
            return;
        await Executer(async () =>
        {
            await api.SendAsync(HttpMethod.Post, $"api/cheops/fva/{contrat.Id}/verifier");
            await RechercherAsync();
        }, "Vérification enregistrée.");
    }

    private async Task AjouterAsync()
    {
        if (Api is not { } api || Selection is not { } ligne)
            return;
        await Executer(async () =>
        {
            var debut = FormatsJ.LireJour(Debut, "Début de couverture") ?? throw new ArgumentException("La date de début est obligatoire.");
            await api.SendAsync(HttpMethod.Post, "api/cheops/fva",
                new AssuranceInput(ligne.Dto.Vehicule.Id, Assureur ?? string.Empty, Contrat ?? string.Empty, debut, FormatsJ.LireJour(Fin, "Fin de couverture"), Statut));
            Assureur = Contrat = Debut = Fin = null;
            await RechercherAsync();
        }, "Contrat enregistré.");
    }

    public override Task RafraichirAsync() => Lignes.Count > 0 ? RechercherAsync() : Task.CompletedTask;
}

// =====================================================================================
// SNPC (12.6)
// =====================================================================================

public sealed record PermisLigne(PermisDto Dto)
{
    public string Reference => Dto.Reference;
    public string Identite => Dto.Personne.Libelle;
    public string Categories => Dto.Categories;
    public string Situation => Dto.Situation;
    public string Points => $"{Dto.Points} / {Dto.PointsMax}";
    public string Validite => FormatsJ.Jour(Dto.ValiditeJusquAu);
}

public sealed class SnpcViewModel : CheopsSectionBase
{
    private string? _q;
    private PermisLigne? _selection;
    private PermisDto? _detail;
    private string? _variation, _motifPoints, _categories, _dates, _validite, _motif;
    private string _situation = PermisSituations.Valide;
    private string _points = "12";
    private bool _modeEdition;
    private long? _personneEdition;

    public SnpcViewModel(CheopsViewModel cheops) : base(cheops)
    {
        Picker = new PersonnePickerViewModel(cheops);
        RechercherCommand = new RelayCommand(() => _ = RechercherAsync());
        RetirerCommand = new RelayCommand(() => _ = PointsAsync(-1));
        RestituerCommand = new RelayCommand(() => _ = PointsAsync(1));
        ModifierCommand = new RelayCommand(Modifier);
        CreerCommand = new RelayCommand(Creer);
        EnregistrerCommand = new RelayCommand(() => _ = EnregistrerAsync());
        AnnulerCommand = new RelayCommand(() => ModeEdition = false);
    }

    public PersonnePickerViewModel Picker { get; }
    public string? Q { get => _q; set => SetProperty(ref _q, value); }
    public ObservableCollection<PermisLigne> Lignes { get; } = [];
    public ObservableCollection<HistoriqueLigne> Historique { get; } = [];
    public bool PeutEcrire => Peut(Permissions.LrppnRediger, Permissions.CheopsFprInscrire);

    public PermisLigne? Selection
    {
        get => _selection;
        set
        {
            if (SetProperty(ref _selection, value) && value is not null)
                _ = ChargerPersonneAsync(value.Dto.Personne.Id);
        }
    }

    public PermisDto? Detail
    {
        get => _detail;
        private set
        {
            if (!SetProperty(ref _detail, value))
                return;
            OnPropertyChanged(nameof(ADetail));
            OnPropertyChanged(nameof(DetailTexte));
            OnPropertyChanged(nameof(Solde));
            OnPropertyChanged(nameof(SoldeTexte));
        }
    }

    public bool ADetail => Detail is not null;
    public double Solde => Detail?.Points ?? 0;
    public string SoldeTexte => Detail is { } d ? $"{d.Points} point(s) sur {d.PointsMax}" : string.Empty;

    public string DetailTexte => Detail is { } d
        ? $"Titulaire : {d.Personne.Libelle} ({d.Personne.Reference})\nPermis n° {d.Reference} — catégories {d.Categories}\n" +
          $"Obtention : {d.DatesObtention ?? "—"}\nValidité du titre : {FormatsJ.Jour(d.ValiditeJusquAu)}\nSituation : {d.Situation}"
        : string.Empty;

    public string? Variation { get => _variation; set => SetProperty(ref _variation, value); }
    public string? MotifPoints { get => _motifPoints; set => SetProperty(ref _motifPoints, value); }
    public bool ModeEdition { get => _modeEdition; set => SetProperty(ref _modeEdition, value); }
    public IReadOnlyList<string> Situations => PermisSituations.All;
    public string? Categories { get => _categories; set => SetProperty(ref _categories, value); }
    public string? Dates { get => _dates; set => SetProperty(ref _dates, value); }
    public string? Validite { get => _validite; set => SetProperty(ref _validite, value); }
    public string Situation { get => _situation; set => SetProperty(ref _situation, value); }
    public string Points { get => _points; set => SetProperty(ref _points, value); }
    public string? Motif { get => _motif; set => SetProperty(ref _motif, value); }

    public RelayCommand RechercherCommand { get; }
    public RelayCommand RetirerCommand { get; }
    public RelayCommand RestituerCommand { get; }
    public RelayCommand ModifierCommand { get; }
    public RelayCommand CreerCommand { get; }
    public RelayCommand EnregistrerCommand { get; }
    public RelayCommand AnnulerCommand { get; }

    private async Task RechercherAsync()
    {
        if (Api is not { } api || string.IsNullOrWhiteSpace(Q))
            return;
        await Executer(async () =>
        {
            Remplir(Lignes, (await api.GetAsync<List<PermisDto>>("api/cheops/snpc?q=" + ApiClient.Q(Q!.Trim()))).Select(p => new PermisLigne(p)));
            if (Lignes.Count == 0)
                throw new ApiException("Aucun permis ne correspond. Un permis peut être créé pour une identité sélectionnée.");
        });
    }

    public async Task ChargerPersonneAsync(long personneId)
    {
        if (Api is not { } api)
            return;
        await Executer(async () =>
        {
            try
            {
                var permis = await api.GetAsync<PermisDto>($"api/cheops/snpc/personnes/{personneId}");
                Detail = permis;
                Remplir(Historique, permis.Historique.Select(HistoriqueLigne.De));
            }
            catch (ApiException ex) when (ex.Status == System.Net.HttpStatusCode.NotFound)
            {
                Detail = null;
                Historique.Clear();
                throw new ApiException(ex.Message + (PeutEcrire ? " Sélectionne l'identité ci-dessous puis « Créer un permis »." : string.Empty));
            }
            finally
            {
                await Cheops.BandeauxPersonneAsync(personneId);
            }
        });
    }

    private async Task PointsAsync(int signe)
    {
        if (Api is not { } api || Detail is not { } d)
            return;
        await Executer(async () =>
        {
            if (!int.TryParse(Variation, out var nombre) || nombre <= 0)
                throw new ArgumentException("Indique un nombre de points (1 à 12).");
            var permis = await api.PostAsync<PermisDto>($"api/cheops/snpc/personnes/{d.Personne.Id}/points", new PointsInput(signe * nombre, MotifPoints ?? string.Empty));
            Detail = permis;
            Remplir(Historique, permis.Historique.Select(HistoriqueLigne.De));
            Variation = MotifPoints = null;
        }, signe < 0 ? "Retrait de points enregistré." : "Restitution de points enregistrée.");
    }

    private void Modifier()
    {
        if (Detail is not { } d)
            return;
        _personneEdition = d.Personne.Id;
        Categories = d.Categories;
        Dates = d.DatesObtention;
        Validite = FormatsJ.JourSaisie(d.ValiditeJusquAu);
        Situation = d.Situation;
        Points = d.Points.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Motif = null;
        ModeEdition = true;
    }

    private void Creer()
    {
        if (Picker.Selection is not { } personne)
        {
            _ = Hote.ExecuterAsync(() => throw new ArgumentException("Sélectionne d'abord l'identité du titulaire."));
            return;
        }
        _personneEdition = personne.Id;
        Categories = "B";
        Dates = Validite = Motif = null;
        Situation = PermisSituations.Valide;
        Points = "12";
        ModeEdition = true;
    }

    private async Task EnregistrerAsync()
    {
        if (Api is not { } api || _personneEdition is not { } personneId)
            return;
        await Executer(async () =>
        {
            if (!int.TryParse(Points, out var points))
                throw new ArgumentException("Solde de points invalide.");
            var permis = await api.SendAsync<PermisDto>(HttpMethod.Put, $"api/cheops/snpc/personnes/{personneId}",
                new PermisInput(Categories ?? string.Empty, Dates, FormatsJ.LireJour(Validite, "Validité"), Situation, points, Motif));
            Detail = permis;
            Remplir(Historique, permis.Historique.Select(HistoriqueLigne.De));
            ModeEdition = false;
        }, "Permis enregistré.");
    }

    public override async Task RafraichirAsync()
    {
        if (Detail is { } d && !ModeEdition)
            await ChargerPersonneAsync(d.Personne.Id);
    }
}

// =====================================================================================
// FAED / FNAEG (12.7, 12.8)
// =====================================================================================

public sealed record ScientifiqueLigne(ScientifiqueDto Dto)
{
    public string Reference => Dto.Reference;
    public string Prelevement => Dto.ReferencePrelevement ?? string.Empty;
    public string Type => Dto.Type;
    public string Origine => Dto.Origine;
    public string Date => FormatsJ.DateHeure(Dto.PreleveLe);
    public string Resultat => Dto.Resultat;
    public string Personne => Dto.Personne ?? "—";
}

public sealed class ScientifiqueViewModel : CheopsSectionBase
{
    private string? _q, _prelevement, _origine, _procedure, _preleveLe, _rapport, _rapportResultat, _motif;
    private string _type;
    private string _resultat = ScientifiqueResultats.EnAttente;
    private ScientifiqueLigne? _selection;
    private ScientifiqueDto? _detail;
    private bool _modeCreation, _chargee;

    public ScientifiqueViewModel(CheopsViewModel cheops, string fichier) : base(cheops)
    {
        Fichier = fichier;
        Types = fichier == FichiersCheops.Faed ? ScientifiqueResultats.TypesFaed : ScientifiqueResultats.TypesFnaeg;
        _type = Types[0];
        PickerCreation = new PersonnePickerViewModel(cheops);
        PickerResultat = new PersonnePickerViewModel(cheops);
        RechercherCommand = new RelayCommand(() => _ = RechercherAsync());
        BasculerCreationCommand = new RelayCommand(() => ModeCreation = !ModeCreation);
        CreerCommand = new RelayCommand(() => _ = CreerAsync());
        ResultatCommand = new RelayCommand(() => _ = ResultatAsync());
    }

    public string Fichier { get; }
    public string Avertissement => Fichier == FichiersCheops.Faed
        ? "Empreintes fictives : aucune collecte de véritable empreinte biométrique."
        : "Profils génétiques fictifs : aucune conservation de données génétiques réelles.";
    public IReadOnlyList<string> Types { get; }
    public IReadOnlyList<string> Resultats => ScientifiqueResultats.All;
    public PersonnePickerViewModel PickerCreation { get; }
    public PersonnePickerViewModel PickerResultat { get; }
    public string? Q { get => _q; set => SetProperty(ref _q, value); }
    public ObservableCollection<ScientifiqueLigne> Lignes { get; } = [];
    public ObservableCollection<HistoriqueLigne> Historique { get; } = [];

    public ScientifiqueLigne? Selection
    {
        get => _selection;
        set
        {
            if (SetProperty(ref _selection, value) && value is not null)
                _ = ChargerAsync(value.Dto.Id);
        }
    }

    public ScientifiqueDto? Detail
    {
        get => _detail;
        private set
        {
            if (!SetProperty(ref _detail, value))
                return;
            OnPropertyChanged(nameof(ADetail));
            OnPropertyChanged(nameof(DetailTexte));
        }
    }

    public bool ADetail => Detail is not null;

    public string DetailTexte => Detail is { } d
        ? $"{d.Fichier} {d.Reference} — prélèvement {d.ReferencePrelevement ?? "—"}\nType : {d.Type}\nOrigine : {d.Origine}\n" +
          $"Procédure : {d.ProcedureNumero ?? "—"}   Date : {FormatsJ.DateHeure(d.PreleveLe)}\nRésultat de comparaison simulée : {d.Resultat}\n" +
          $"Personne rapprochée : {d.Personne ?? "—"}\n\nRapport fictif :\n{d.Rapport ?? "—"}"
        : string.Empty;

    public bool ModeCreation { get => _modeCreation; set => SetProperty(ref _modeCreation, value); }
    public string? Prelevement { get => _prelevement; set => SetProperty(ref _prelevement, value); }
    public string? Origine { get => _origine; set => SetProperty(ref _origine, value); }
    public string? Procedure { get => _procedure; set => SetProperty(ref _procedure, value); }
    public string Type { get => _type; set => SetProperty(ref _type, value); }
    public string? PreleveLe { get => _preleveLe; set => SetProperty(ref _preleveLe, value); }
    public string? Rapport { get => _rapport; set => SetProperty(ref _rapport, value); }
    public string Resultat { get => _resultat; set => SetProperty(ref _resultat, value); }
    public string? RapportResultat { get => _rapportResultat; set => SetProperty(ref _rapportResultat, value); }
    public string? Motif { get => _motif; set => SetProperty(ref _motif, value); }

    public RelayCommand RechercherCommand { get; }
    public RelayCommand BasculerCreationCommand { get; }
    public RelayCommand CreerCommand { get; }
    public RelayCommand ResultatCommand { get; }

    public override async Task EntrerAsync()
    {
        if (!_chargee)
        {
            _chargee = true;
            await RechercherAsync();
        }
    }

    private async Task RechercherAsync()
    {
        if (Api is not { } api)
            return;
        await Executer(async () =>
            Remplir(Lignes, (await api.GetAsync<List<ScientifiqueDto>>(Chemin("api/cheops/scientifique", ("fichier", Fichier), ("q", Q))))
                .Select(s => new ScientifiqueLigne(s))));
    }

    public async Task ChargerAsync(long id)
    {
        if (Api is not { } api)
            return;
        await Executer(async () =>
        {
            var fiche = await api.GetAsync<ScientifiqueDto>($"api/cheops/scientifique/{id}");
            Detail = fiche;
            Remplir(Historique, fiche.Historique.Select(HistoriqueLigne.De));
            Resultat = fiche.Resultat;
            if (fiche.PersonneId is { } p)
                await Cheops.BandeauxPersonneAsync(p);
            else
                Cheops.DefinirBandeaux([]);
        });
    }

    private async Task CreerAsync()
    {
        if (Api is not { } api)
            return;
        await Executer(async () =>
        {
            var fiche = await api.PostAsync<ScientifiqueDto>("api/cheops/scientifique", new ScientifiqueInput(Fichier, Prelevement, Origine ?? string.Empty,
                Procedure, Type, FormatsJ.LireDateHeure(PreleveLe, "Date du prélèvement"), PickerCreation.Selection?.Id, Rapport));
            ModeCreation = false;
            Prelevement = Origine = Procedure = PreleveLe = Rapport = null;
            await RechercherAsync();
            await ChargerAsync(fiche.Id);
        }, "Enregistrement créé.");
    }

    private async Task ResultatAsync()
    {
        if (Api is not { } api || Detail is not { } d)
            return;
        await Executer(async () =>
        {
            var fiche = await api.SendAsync<ScientifiqueDto>(HttpMethod.Put, $"api/cheops/scientifique/{d.Id}/resultat",
                new ResultatInput(Resultat, PickerResultat.Selection?.Id ?? d.PersonneId, RapportResultat, Motif));
            RapportResultat = Motif = null;
            await RechercherAsync();
            await ChargerAsync(fiche.Id);
        }, "Résultat enregistré.");
    }

    public override async Task RafraichirAsync()
    {
        await RechercherAsync();
        if (Detail is { } d)
            await ChargerAsync(d.Id);
    }
}

// =====================================================================================
// FIJAISV / FIJAIT (12.9)
// =====================================================================================

public sealed record FijaisLigne(FijaisDto Dto)
{
    public string Reference => Dto.Reference;
    public string Identite => Dto.Personne.Libelle;
    public string Decision => Dto.ReferenceDecision;
    public string Statut => Dto.Statut;
    public string Fin => FormatsJ.Jour(Dto.FinInscription);
}

public sealed class FijaisViewModel : CheopsSectionBase
{
    private string? _q, _decision, _juridiction, _infraction, _decisionLe, _fin, _service, _obligations, _restrictions, _nouveauStatut, _motif;
    private FijaisLigne? _selection;
    private FijaisDto? _detail;
    private bool _modeInscription;

    public FijaisViewModel(CheopsViewModel cheops, string fichier) : base(cheops)
    {
        Fichier = fichier;
        Picker = new PersonnePickerViewModel(cheops);
        RechercherCommand = new RelayCommand(() => _ = RechercherAsync());
        BasculerInscriptionCommand = new RelayCommand(() => ModeInscription = !ModeInscription);
        InscrireCommand = new RelayCommand(() => _ = InscrireAsync());
        ChangerStatutCommand = new RelayCommand(() => _ = ChangerStatutAsync());
    }

    public string Fichier { get; }
    public string Titre => Fichier == FichiersCheops.Fijaisv ? "FIJAISV — auteurs d'infractions sexuelles ou violentes (fictif)" : "FIJAIT — auteurs d'infractions terroristes (fictif)";
    public PersonnePickerViewModel Picker { get; }
    public bool PeutInscrire => Peut(Permissions.LrppnValider);
    public IReadOnlyList<string> Statuts => FijaisStatuts.All;
    public string? Q { get => _q; set => SetProperty(ref _q, value); }
    public ObservableCollection<FijaisLigne> Lignes { get; } = [];
    public ObservableCollection<HistoriqueLigne> Historique { get; } = [];

    public FijaisLigne? Selection
    {
        get => _selection;
        set
        {
            if (SetProperty(ref _selection, value) && value is not null)
                _ = ChargerAsync(value.Dto.Id);
        }
    }

    public FijaisDto? Detail
    {
        get => _detail;
        private set
        {
            if (!SetProperty(ref _detail, value))
                return;
            OnPropertyChanged(nameof(ADetail));
            OnPropertyChanged(nameof(DetailTexte));
        }
    }

    public bool ADetail => Detail is not null;

    public string DetailTexte => Detail is { } d
        ? $"{d.Fichier} {d.Reference} — {d.Statut}\nIdentité : {d.Personne.Libelle} ({d.Personne.Reference})\nDécision : {d.ReferenceDecision} — {d.Juridiction ?? "—"} — {FormatsJ.Jour(d.DecisionLe)}\n" +
          $"Infraction : {d.Infraction ?? "—"}\nFin d'inscription : {FormatsJ.Jour(d.FinInscription)}\nService gestionnaire : {d.ServiceGestionnaire ?? "—"}\n" +
          $"Obligations : {d.Obligations ?? "—"}\nRestrictions de consultation : {d.Restrictions ?? "—"}"
        : string.Empty;

    public bool ModeInscription { get => _modeInscription; set => SetProperty(ref _modeInscription, value); }
    public string? DecisionRef { get => _decision; set => SetProperty(ref _decision, value); }
    public string? Juridiction { get => _juridiction; set => SetProperty(ref _juridiction, value); }
    public string? Infraction { get => _infraction; set => SetProperty(ref _infraction, value); }
    public string? DecisionLe { get => _decisionLe; set => SetProperty(ref _decisionLe, value); }
    public string? FinInscription { get => _fin; set => SetProperty(ref _fin, value); }
    public string? ServiceGestionnaire { get => _service; set => SetProperty(ref _service, value); }
    public string? Obligations { get => _obligations; set => SetProperty(ref _obligations, value); }
    public string? Restrictions { get => _restrictions; set => SetProperty(ref _restrictions, value); }
    public string? NouveauStatut { get => _nouveauStatut; set => SetProperty(ref _nouveauStatut, value); }
    public string? Motif { get => _motif; set => SetProperty(ref _motif, value); }

    public RelayCommand RechercherCommand { get; }
    public RelayCommand BasculerInscriptionCommand { get; }
    public RelayCommand InscrireCommand { get; }
    public RelayCommand ChangerStatutCommand { get; }

    private async Task RechercherAsync()
    {
        if (Api is not { } api || string.IsNullOrWhiteSpace(Q))
            return;
        await Executer(async () =>
        {
            Remplir(Lignes, (await api.GetAsync<List<FijaisDto>>(Chemin("api/cheops/fijais", ("fichier", Fichier), ("q", Q)))).Select(f => new FijaisLigne(f)));
            if (Lignes.Count == 0)
                throw new ApiException($"Aucune inscription au {Fichier} pour cette recherche.");
        });
    }

    public async Task ChercherPersonneAsync(PersonneResumeDto personne)
    {
        Q = personne.Reference;
        await RechercherAsync();
        if (Lignes.FirstOrDefault() is { } premiere)
            Selection = premiere;
    }

    public async Task ChargerAsync(long id)
    {
        if (Api is not { } api)
            return;
        await Executer(async () =>
        {
            var fiche = await api.GetAsync<FijaisDto>($"api/cheops/fijais/{id}");
            Detail = fiche;
            Remplir(Historique, fiche.Historique.Select(HistoriqueLigne.De));
            await Cheops.BandeauxPersonneAsync(fiche.Personne.Id);
        });
    }

    private async Task InscrireAsync()
    {
        if (Api is not { } api)
            return;
        await Executer(async () =>
        {
            var personne = Picker.Selection ?? throw new ArgumentException("Sélectionne la personne concernée par la décision.");
            var fiche = await api.PostAsync<FijaisDto>("api/cheops/fijais", new FijaisInput(Fichier, personne.Id, DecisionRef ?? string.Empty, Juridiction,
                Infraction, FormatsJ.LireJour(DecisionLe, "Date de décision"), FormatsJ.LireJour(FinInscription, "Fin d'inscription"), ServiceGestionnaire,
                Obligations, Restrictions));
            ModeInscription = false;
            DecisionRef = Juridiction = Infraction = DecisionLe = FinInscription = Obligations = Restrictions = null;
            Q = fiche.Personne.Reference;
            await RechercherAsync();
            await ChargerAsync(fiche.Id);
        }, "Inscription enregistrée.");
    }

    private async Task ChangerStatutAsync()
    {
        if (Api is not { } api || Detail is not { } d || string.IsNullOrWhiteSpace(NouveauStatut))
            return;
        await Executer(async () =>
        {
            await api.SendAsync<FijaisDto>(HttpMethod.Put, $"api/cheops/fijais/{d.Id}/statut", new StatutInput(NouveauStatut!, Motif));
            NouveauStatut = Motif = null;
            await ChargerAsync(d.Id);
        }, "Statut modifié.");
    }

    public override async Task RafraichirAsync()
    {
        if (Detail is { } d)
            await ChargerAsync(d.Id);
    }
}

// =====================================================================================
// ANADOC (12.10)
// =====================================================================================

public sealed record AnadocLigne(AnadocResultatDto Dto)
{
    public string Date => FormatsJ.DateHeure(Dto.Date);
    public string Procedure => Dto.ProcedureNumero;
    public string Numero => Dto.NumeroPv ?? string.Empty;
    public string Type => Dto.TypeActe;
    public string Titre => Dto.Titre;
    public string Service => Dto.Service ?? string.Empty;
    public string Redacteur => Dto.Redacteur ?? string.Empty;
    public string Extrait => Dto.Extrait;
}

public sealed class AnadocViewModel : CheopsSectionBase
{
    private string? _procedure, _pv, _type, _du, _au, _service, _redacteur, _personne, _motsCles;
    private AnadocLigne? _selection;
    private AnadocDocumentDto? _document;

    public AnadocViewModel(CheopsViewModel cheops) : base(cheops)
    {
        RechercherCommand = new RelayCommand(() => _ = RechercherAsync());
        ExporterCommand = new RelayCommand(() => _ = ExporterAsync());
    }

    public IReadOnlyList<string> Types { get; } = ["", .. ActesCatalogue.All.Select(t => t.Libelle), .. PieceCategories.All];
    public string? Procedure { get => _procedure; set => SetProperty(ref _procedure, value); }
    public string? Pv { get => _pv; set => SetProperty(ref _pv, value); }
    public string? Type { get => _type; set => SetProperty(ref _type, value); }
    public string? Du { get => _du; set => SetProperty(ref _du, value); }
    public string? Au { get => _au; set => SetProperty(ref _au, value); }
    public string? Service { get => _service; set => SetProperty(ref _service, value); }
    public string? Redacteur { get => _redacteur; set => SetProperty(ref _redacteur, value); }
    public string? Personne { get => _personne; set => SetProperty(ref _personne, value); }
    public string? MotsCles { get => _motsCles; set => SetProperty(ref _motsCles, value); }
    public ObservableCollection<AnadocLigne> Lignes { get; } = [];
    public bool PeutExporter => Peut(Permissions.LrppnConsulter, Permissions.LrppnRediger, Permissions.LrppnValider);

    public AnadocLigne? Selection
    {
        get => _selection;
        set
        {
            if (SetProperty(ref _selection, value) && value is not null)
                _ = ChargerAsync(value.Dto.Id);
        }
    }

    public AnadocDocumentDto? Document
    {
        get => _document;
        private set
        {
            if (!SetProperty(ref _document, value))
                return;
            OnPropertyChanged(nameof(ADocument));
            OnPropertyChanged(nameof(Texte));
        }
    }

    public bool ADocument => Document is not null;
    public string Texte => Document?.Texte ?? string.Empty;

    public RelayCommand RechercherCommand { get; }
    public RelayCommand ExporterCommand { get; }

    private async Task RechercherAsync()
    {
        if (Api is not { } api)
            return;
        await Executer(async () =>
        {
            var du = FormatsJ.LireJour(Du, "Du");
            var au = FormatsJ.LireJour(Au, "Au");
            var chemin = Chemin("api/cheops/anadoc", ("procedure", Procedure), ("pv", Pv), ("type", Type), ("du", FormatsJ.JourSaisie(du)),
                ("au", FormatsJ.JourSaisie(au)), ("service", Service), ("redacteur", Redacteur), ("personne", Personne), ("motsCles", MotsCles));
            Remplir(Lignes, (await api.GetAsync<List<AnadocResultatDto>>(chemin)).Select(r => new AnadocLigne(r)));
            if (Lignes.Count == 0)
                throw new ApiException("Aucun document validé ne correspond.");
        });
    }

    private async Task ChargerAsync(long id)
    {
        if (Api is not { } api)
            return;
        await Executer(async () => Document = await api.GetAsync<AnadocDocumentDto>($"api/cheops/anadoc/{id}"));
    }

    private async Task ExporterAsync()
    {
        if (Document is not { } d)
            return;
        await Executer(async () =>
        {
            if (d.ActeId is { } acte)
                await TelechargerAsync($"api/lrppn/actes/{acte}/pdf");
            else if (d.PieceId is { } piece)
                await TelechargerAsync($"api/lrppn/pieces/{piece}/fichier");
        });
    }
}
