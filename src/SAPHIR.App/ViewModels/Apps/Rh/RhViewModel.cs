using System.Collections.ObjectModel;
using Avalonia.Threading;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Rh;

namespace Saphir.ViewModels.Apps.Rh;

/// <summary>Page du module RH (une entrée du menu latéral).</summary>
public abstract class RhPageViewModel(RhViewModel rh) : ViewModelBase
{
    public RhViewModel Rh { get; } = rh;

    public abstract string Titre { get; }

    public virtual string Description => string.Empty;

    public bool HasDescription => !string.IsNullOrEmpty(Description);

    /// <summary>Charge (ou recharge) les données de la page depuis le serveur.</summary>
    public abstract Task ChargerAsync();

    /// <summary>Modification signalée par le serveur (rh-agent-modifie, rh-documents-modifies…) : par défaut, rechargement.</summary>
    public virtual Task SurTempsReelAsync(RealtimeMessage message) => ChargerAsync();

    protected static ApiClient? Api => SaphirSession.Api;
}

/// <summary>
/// RESSOURCES HUMAINES : menu latéral des 16 rubriques (section 4.2), pages, données communes (référentiels, agents,
/// catégories, dossiers), messages et confirmations. Les actions sont masquées selon les permissions ; le serveur
/// revérifie tout (rôle Discord RH + permissions rh.*).
/// </summary>
public sealed class RhViewModel : AppViewModelBase
{
    public const string Tableau = "tableau";
    public const string Personnel = "personnel";
    public const string Dossier = "dossier";
    public const string Affectations = "affectations";
    public const string Rio = "rio";
    public const string Documents = "documents";
    public const string Modeles = "modeles";
    public const string AValider = "a-valider";
    public const string Habilitations = "habilitations";
    public const string HistoriquePage = "historique";
    public const string Archives = "archives";
    public const string ParametresPage = "parametres";

    private readonly Dictionary<string, RhPageViewModel> _pages = new(StringComparer.Ordinal);
    private RhPageViewModel? _page;
    private RhMenuEntree? _menuSelection;
    private bool _compact;
    private bool _menuOuvert = true;
    private bool _abonne;

    public RhViewModel()
    {
        BasculerMenuCommand = new RelayCommand(() => MenuOuvert = !MenuOuvert);
        ActualiserCommand = new RelayCommand(() => _ = ActualiserToutAsync());
        OuvrirAdministrationCommand = new RelayCommand(OuvrirAdministration);
        ConstruireMenu();
        _ = DemarrerAsync();
    }

    // =====================================================================================
    // Menu et navigation
    // =====================================================================================

    public ObservableCollection<RhMenuEntree> Menu { get; } = [];

    public RhMenuEntree? MenuSelection
    {
        get => _menuSelection;
        set
        {
            if (!SetProperty(ref _menuSelection, value) || value is null)
                return;
            AfficherPage(value.Cle);
            if (Compact)
                MenuOuvert = false;
        }
    }

    public RhPageViewModel? Page
    {
        get => _page;
        private set => SetProperty(ref _page, value);
    }

    /// <summary>Affichage étroit (téléphone) : le menu se replie après chaque choix.</summary>
    public bool Compact
    {
        get => _compact;
        set
        {
            if (!SetProperty(ref _compact, value))
                return;
            MenuOuvert = !value;
        }
    }

    public bool MenuOuvert
    {
        get => _menuOuvert;
        set => SetProperty(ref _menuOuvert, value);
    }

    public string AgentConnecte => SaphirSession.Agent is { } a ? $"{a.Nom} {a.Prenom} — {a.Reference}" : string.Empty;

    public RelayCommand BasculerMenuCommand { get; }
    public RelayCommand ActualiserCommand { get; }
    public RelayCommand OuvrirAdministrationCommand { get; }

    private void ConstruireMenu()
    {
        var cle = MenuSelection?.Cle;
        Menu.Clear();
        void Ajouter(bool visible, string c, string numero, string libelle)
        {
            if (visible)
                Menu.Add(new RhMenuEntree(c, numero, libelle));
        }

        var consulter = RhDroits.Consulter;
        Ajouter(consulter, Tableau, "1", "Tableau de bord");
        Ajouter(consulter, Personnel, "2", "Gestion du personnel");
        Ajouter(consulter, Dossier, "3", "Dossiers individuels");
        Ajouter(consulter || RhDroits.Referentiels, RhReferentielTypes.Grades, "4", "Grades");
        Ajouter(consulter || RhDroits.Referentiels, RhReferentielTypes.Services, "5", "Services");
        Ajouter(consulter || RhDroits.Referentiels, RhReferentielTypes.Unites, "6", "Unités");
        Ajouter(consulter || RhDroits.Referentiels, RhReferentielTypes.Fonctions, "7", "Fonctions");
        Ajouter(consulter, Affectations, "8", "Affectations");
        Ajouter(consulter, Rio, "9", "Gestion des RIO");
        Ajouter(RhDroits.LireDocuments, Documents, "10", "Documents");
        Ajouter(consulter || RhDroits.Documents, Modeles, "11", "Modèles documentaires");
        Ajouter(RhDroits.LireDocuments, AValider, "12", "Documents à valider");
        Ajouter(consulter, Habilitations, "13", "Habilitations");
        Ajouter(consulter, HistoriquePage, "14", "Historique");
        Ajouter(consulter || RhDroits.LireDocuments, Archives, "15", "Archives");
        Ajouter(RhDroits.LireDocuments, ParametresPage, "16", "Paramètres RH");

        _menuSelection = Menu.FirstOrDefault(m => m.Cle == cle) ?? Menu.FirstOrDefault();
        OnPropertyChanged(nameof(MenuSelection));
    }

    /// <summary>Affiche une rubrique (la page est créée une fois puis rechargée à chaque affichage).</summary>
    public RhPageViewModel? AfficherPage(string cle, bool charger = true)
    {
        if (!_pages.TryGetValue(cle, out var page))
        {
            page = CreerPage(cle);
            if (page is null)
                return null;
            _pages[cle] = page;
        }
        if (_menuSelection?.Cle != cle)
        {
            _menuSelection = Menu.FirstOrDefault(m => m.Cle == cle);
            OnPropertyChanged(nameof(MenuSelection));
        }
        Page = page;
        if (charger)
            _ = page.ChargerAsync();
        return page;
    }

    private RhPageViewModel? CreerPage(string cle) => cle switch
    {
        Tableau => new RhTableauBordViewModel(this),
        Personnel => new RhPersonnelViewModel(this),
        Dossier => new RhDossierViewModel(this),
        RhReferentielTypes.Grades or RhReferentielTypes.Services or RhReferentielTypes.Unites or RhReferentielTypes.Fonctions
            => new RhReferentielViewModel(this, cle),
        Affectations => new RhAffectationsViewModel(this),
        Rio => new RhRioViewModel(this),
        Documents => new RhDocumentsViewModel(this, RhDocumentsMode.Explorateur),
        AValider => new RhDocumentsViewModel(this, RhDocumentsMode.AValider),
        Modeles => new RhModelesViewModel(this),
        Habilitations => new RhHabilitationsViewModel(this),
        HistoriquePage => new RhHistoriqueViewModel(this),
        Archives => new RhArchivesViewModel(this),
        ParametresPage => new RhParametresViewModel(this),
        _ => null
    };

    /// <summary>Ouvre le dossier individuel d'un agent (onglet facultatif : 0 identité … 6 historique).</summary>
    public void OuvrirDossier(long agentId, int onglet = 0)
    {
        if (AfficherPage(Dossier, charger: false) is RhDossierViewModel dossier)
            _ = dossier.OuvrirAsync(agentId, onglet);
    }

    public void OuvrirDocuments(long? agentId)
    {
        if (AfficherPage(Documents, charger: false) is RhDocumentsViewModel documents)
            _ = documents.FiltrerAgentAsync(agentId);
    }

    public void OuvrirModeles(long? agentId)
    {
        if (AfficherPage(Modeles, charger: false) is RhModelesViewModel modeles)
            _ = modeles.PreparerAsync(agentId);
    }

    public void OuvrirHabilitations(long agentId)
    {
        if (AfficherPage(Habilitations, charger: false) is RhHabilitationsViewModel page)
            _ = page.OuvrirAsync(agentId);
    }

    /// <summary>Ouvre l'application Administration (profils et permissions), si l'agent y a accès.</summary>
    public void OuvrirAdministration()
    {
        if (SaphirSession.CanOpen(SaphirApps.Administration))
            SaphirSession.Publish(new RealtimeMessage(SaphirSession.OpenAppType, AppId: SaphirApps.Administration));
    }

    public bool PeutOuvrirAdministration => SaphirSession.CanOpen(SaphirApps.Administration);

    // =====================================================================================
    // Données communes
    // =====================================================================================

    public IReadOnlyList<RhReferentielDto> Grades { get; private set; } = [];
    public IReadOnlyList<RhReferentielDto> Services { get; private set; } = [];
    public IReadOnlyList<RhReferentielDto> Unites { get; private set; } = [];
    public IReadOnlyList<RhReferentielDto> Fonctions { get; private set; } = [];
    public IReadOnlyList<RhAgentLigneDto> Agents { get; private set; } = [];
    public IReadOnlyList<RhCategorieDto> Categories { get; private set; } = [];
    public IReadOnlyList<RhDossierDocDto> DossiersDocuments { get; private set; } = [];
    public RhParametresDto? ParametresRh { get; private set; }

    /// <summary>Données communes rechargées : les pages reconstruisent leurs listes de choix.</summary>
    public event Action? DonneesChangees;

    public IReadOnlyList<RhReferentielDto> Referentiel(string type) => type switch
    {
        RhReferentielTypes.Grades => Grades,
        RhReferentielTypes.Services => Services,
        RhReferentielTypes.Unites => Unites,
        _ => Fonctions
    };

    /// <summary>Options d'un référentiel (actifs seulement par défaut) ; <paramref name="vide"/> ajoute un premier choix sans id.</summary>
    public List<RhOption> Options(string type, bool actifsSeulement = true, string? vide = null)
    {
        var liste = new List<RhOption>();
        if (vide is not null)
            liste.Add(new RhOption(null, vide));
        liste.AddRange(Referentiel(type)
            .Where(e => e.Actif || !actifsSeulement)
            .Select(e => new RhOption(e.Id, e.Abrege is { Length: > 0 } a && type == RhReferentielTypes.Grades ? $"{e.Libelle} ({a})" : e.Libelle, e.ParentId)));
        return liste;
    }

    public List<RhOption> OptionsAgents(string? vide = null, bool avecArchives = false)
    {
        var liste = new List<RhOption>();
        if (vide is not null)
            liste.Add(new RhOption(null, vide));
        liste.AddRange(Agents
            .Where(a => avecArchives || a.Statut != AgentStatuts.Archive)
            .Select(a => new RhOption(a.Id, $"{a.Libelle} — {a.Reference}")));
        return liste;
    }

    public List<RhOption> OptionsCategories(string? vide = null, bool actifsSeulement = true)
    {
        var liste = new List<RhOption>();
        if (vide is not null)
            liste.Add(new RhOption(null, vide));
        liste.AddRange(Categories.Where(c => c.Actif || !actifsSeulement).Select(c => new RhOption(c.Id, $"{c.Groupe} › {c.Libelle}")));
        return liste;
    }

    public List<RhOption> OptionsDossiers(string? vide = null)
    {
        var liste = new List<RhOption>();
        if (vide is not null)
            liste.Add(new RhOption(null, vide));
        liste.AddRange(DossiersDocuments.Select(d => new RhOption(d.Id, d.Chemin, d.ParentId)));
        return liste;
    }

    private async Task DemarrerAsync()
    {
        await ChargerCommunAsync();
        if (MenuSelection is { } premiere)
            AfficherPage(premiere.Cle);
    }

    /// <summary>Recharge référentiels, agents, catégories et dossiers selon les droits.</summary>
    public async Task ChargerCommunAsync()
    {
        var api = Api;
        if (api is null)
            return;
        await Charger(async () =>
        {
            if (RhDroits.LireDocuments)
                ParametresRh = await api.GetAsync<RhParametresDto>("api/rh/parametres");
            if (RhDroits.Consulter || RhDroits.Referentiels)
            {
                Grades = await api.GetAsync<List<RhReferentielDto>>("api/rh/referentiels/grades?inactifs=true");
                Services = await api.GetAsync<List<RhReferentielDto>>("api/rh/referentiels/services?inactifs=true");
                Unites = await api.GetAsync<List<RhReferentielDto>>("api/rh/referentiels/unites?inactifs=true");
                Fonctions = await api.GetAsync<List<RhReferentielDto>>("api/rh/referentiels/fonctions?inactifs=true");
            }
            if (RhDroits.Consulter)
                Agents = await api.GetAsync<List<RhAgentLigneDto>>("api/rh/agents");
            if (RhDroits.LireDocuments)
            {
                Categories = await api.GetAsync<List<RhCategorieDto>>("api/rh/categories");
                DossiersDocuments = await api.GetAsync<List<RhDossierDocDto>>("api/rh/dossiers");
            }
        });
        DonneesChangees?.Invoke();
    }

    private async Task ActualiserToutAsync()
    {
        await ChargerCommunAsync();
        if (Page is { } page)
            await page.ChargerAsync();
    }

    // =====================================================================================
    // Messages, confirmations, fichiers
    // =====================================================================================

    /// <summary>Action de l'agent : attente, message de succès, erreur lisible.</summary>
    public Task Executer(Func<Task> action, string? succes = null) => RunAsync(action, succes);

    public Task<bool> Confirmer(string titre, string message, string libelle = "Confirmer", bool danger = false)
        => ConfirmAsync(titre, message, libelle, danger);

    /// <summary>Chargement silencieux : seules les erreurs s'affichent (les messages de succès restent).</summary>
    public async Task Charger(Func<Task> chargement)
    {
        try
        {
            await chargement();
        }
        catch (ApiException ex)
        {
            ErrorText = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorText = "Erreur inattendue : " + ex.Message;
        }
    }

    public void Erreur(string message) => ErrorText = message;

    /// <summary>Sélecteur d'enregistrement fourni par la vue (TopLevel.StorageProvider).</summary>
    public Func<string, byte[], Task<bool>>? EnregistrerFichier { get; set; }

    /// <summary>Sélecteur d'ouverture fourni par la vue (TopLevel.StorageProvider).</summary>
    public Func<Task<RhFichierChoisi?>>? ChoisirFichier { get; set; }

    /// <summary>Ouvre le sélecteur de fichiers ; <c>null</c> si l'agent annule ou si le choix est impossible (message affiché).</summary>
    public async Task<RhFichierChoisi?> ChoisirFichierAsync()
    {
        if (ChoisirFichier is not { } choisir)
        {
            Erreur("Le choix de fichiers n'est pas disponible sur cet appareil.");
            return null;
        }
        try
        {
            return await choisir();
        }
        catch (Exception ex)
        {
            Erreur("Lecture du fichier impossible : " + ex.Message);
            return null;
        }
    }

    /// <summary>Télécharge une version de document et propose de l'enregistrer.</summary>
    public async Task TelechargerAsync(long documentId, int numero)
    {
        var api = Api;
        if (api is null)
            return;
        var enregistrer = EnregistrerFichier;
        if (enregistrer is null)
        {
            Erreur("L'enregistrement de fichiers n'est pas disponible sur cet appareil.");
            return;
        }
        await Executer(async () =>
        {
            var fichier = await api.GetAsync<RhFichierDto>($"api/rh/documents/{documentId}/versions/{numero}/fichier");
            if (!await enregistrer(fichier.NomFichier, fichier.Contenu))
                return;
            InfoTexte($"Fichier « {fichier.NomFichier} » enregistré.");
        });
    }

    private void InfoTexte(string texte) => InfoText = texte;

    // =====================================================================================
    // Temps réel et droits
    // =====================================================================================

    public override void Resume()
    {
        if (_abonne)
            return;
        _abonne = true;
        SaphirSession.Realtime += SurTempsReel;
        SaphirSession.Changed += SurSessionModifiee;
        SurSessionModifiee();
    }

    public override void Suspend()
    {
        if (!_abonne)
            return;
        _abonne = false;
        SaphirSession.Realtime -= SurTempsReel;
        SaphirSession.Changed -= SurSessionModifiee;
    }

    private void SurSessionModifiee() => Dispatcher.UIThread.Post(() =>
    {
        ConstruireMenu();
        OnPropertyChanged(nameof(AgentConnecte));
        OnPropertyChanged(nameof(PeutOuvrirAdministration));
        foreach (var page in _pages.Values)
            if (page is IRhDroitsSensibles sensible)
                sensible.RafraichirDroits();
    });

    private void SurTempsReel(RealtimeMessage message)
    {
        if (message.Type is not (RhRealtimeTypes.AgentModifie or RhRealtimeTypes.DocumentsModifies or RhRealtimeTypes.ReferentielsModifies))
            return;
        Dispatcher.UIThread.Post(() => _ = SurModificationAsync(message));
    }

    private async Task SurModificationAsync(RealtimeMessage message)
    {
        await ChargerCommunAsync();
        if (Page is { } page)
            await page.SurTempsReelAsync(message);
    }
}

/// <summary>Page qui affiche ou masque des actions selon les permissions (rafraîchies quand la session change).</summary>
public interface IRhDroitsSensibles
{
    void RafraichirDroits();
}
