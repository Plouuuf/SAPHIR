using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Media.Imaging;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Rh;

namespace Saphir.ViewModels.Apps.Rh;

public enum RhDocumentsMode
{
    /// <summary>Explorateur complet (dossiers, import, classement).</summary>
    Explorateur,

    /// <summary>Documents en attente de validation.</summary>
    AValider
}

// =====================================================================================
// 10. Documents et 12. Documents à valider (section 5)
// =====================================================================================

public sealed class RhDocumentsViewModel : RhPageViewModel, IRhDroitsSensibles
{
    private const string TousStatuts = "Tous les statuts";

    private readonly RhDocumentsMode _mode;
    private RhOption? _dossier;
    private string _recherche = string.Empty;
    private RhOption? _categorieFiltre;
    private string _statutFiltre = TousStatuts;
    private RhOption? _agentFiltre;
    private RhDocumentRow? _selection;
    private RhDocumentDetailDto? _detail;
    private Bitmap? _apercuImage;
    private string? _apercuTexte;
    private string _apercuInfo = string.Empty;
    private bool _remplissage;

    // Métadonnées en édition
    private string _editTitre = string.Empty;
    private string? _editType;
    private RhOption? _editCategorie;
    private RhOption? _editDossier;
    private string? _editDescription;
    private RhOption? _editAgent;
    private RhOption? _editService;
    private DateTime? _editDateDocument;
    private DateTime? _editDateEffet;
    private DateTime? _editDateExpiration;
    private string _editConfidentialite = RhConfidentialites.Normal;
    private string? _editObservations;
    private string? _editMotif;
    private string? _motifRefus;
    private string? _motifArchivage;
    private string? _commentaireVersion;

    // Import
    private bool _importOuvert;
    private string _nvTitre = string.Empty;
    private string? _nvType;
    private RhOption? _nvCategorie;
    private RhOption? _nvDossier;
    private string? _nvDescription;
    private RhOption? _nvAgent;
    private RhOption? _nvService;
    private DateTime? _nvDateDocument = DateTime.Today;
    private DateTime? _nvDateEffet;
    private DateTime? _nvDateExpiration;
    private string _nvConfidentialite = RhConfidentialites.Normal;
    private string? _nvObservations;
    private bool _nvSoumettre;
    private RhFichierChoisi? _nvFichier;

    private string? _nomDossier;

    public RhDocumentsViewModel(RhViewModel rh, RhDocumentsMode mode) : base(rh)
    {
        _mode = mode;
        if (mode == RhDocumentsMode.AValider)
            _statutFiltre = RhDocumentStatuts.AValider;

        RechercherCommand = new RelayCommand(() => _ = ChargerAsync());
        EnregistrerCommand = new RelayCommand(() => _ = EnregistrerMetadonneesAsync());
        SoumettreCommand = new RelayCommand(() => _ = ActionAsync("soumettre", null, "Soumettre à validation",
            "Soumettre ce document à la validation d'un responsable RH ?", "Document soumis à validation."));
        ValiderCommand = new RelayCommand(() => _ = ActionAsync("valider", null, "Validation",
            "Valider ce document ? Il sera figé : seules une nouvelle version, un déplacement ou l'archivage resteront possibles.", "Document validé."));
        RefuserCommand = new RelayCommand(() => _ = RefuserAsync());
        ArchiverCommand = new RelayCommand(() => _ = ActionAsync("archiver", new RhMotifInput(MotifArchivage), "Archivage",
            "Archiver ce document ? Il restera consultable dans les Archives et pourra être restauré.", "Document archivé."));
        RestaurerCommand = new RelayCommand(() => _ = ActionAsync("restaurer", null, "Restauration", "Restaurer ce document ?", "Document restauré."));
        NouvelleVersionCommand = new RelayCommand(() => _ = NouvelleVersionAsync());
        TelechargerCommand = new RelayCommand(() => { if (Detail is { } d) _ = Rh.TelechargerAsync(d.Document.Id, d.Document.Version); });
        TelechargerVersionCommand = new RelayCommand(p => { if (p is RhVersionRow v && Detail is { } d) _ = Rh.TelechargerAsync(d.Document.Id, v.Numero); });
        OuvrirImportCommand = new RelayCommand(OuvrirImport);
        FermerImportCommand = new RelayCommand(() => ImportOuvert = false);
        ChoisirFichierCommand = new RelayCommand(() => _ = ChoisirFichierImportAsync());
        ImporterCommand = new RelayCommand(() => _ = ImporterAsync());
        CreerDossierCommand = new RelayCommand(() => _ = CreerDossierAsync());
        RenommerDossierCommand = new RelayCommand(() => _ = RenommerDossierAsync());
        SupprimerDossierCommand = new RelayCommand(() => _ = SupprimerDossierAsync());
        OuvrirDossierAgentCommand = new RelayCommand(() => { if (Detail?.Document.AgentId is { } id) Rh.OuvrirDossier(id, 4); });

        Rh.DonneesChangees += ReconstruireOptions;
        ReconstruireOptions();
    }

    public override string Titre => _mode == RhDocumentsMode.AValider ? "Documents à valider" : "Documents";
    public override string Description => _mode == RhDocumentsMode.AValider
        ? "Documents soumis à validation. Un document validé est figé ; une nouvelle version peut ensuite être déposée."
        : "Gestionnaire documentaire RH : dossiers, import de fichiers (PDF, images, DOCX), versions, validation et archivage.";

    public bool EstExplorateur => _mode == RhDocumentsMode.Explorateur;

    // ===== Filtres =====

    public ObservableCollection<RhOption> Dossiers { get; } = [];
    public ObservableCollection<RhOption> CategoriesFiltre { get; } = [];
    public ObservableCollection<RhOption> AgentsFiltre { get; } = [];
    public IReadOnlyList<string> StatutsFiltre { get; } =
        [TousStatuts, RhDocumentStatuts.Brouillon, RhDocumentStatuts.AValider, RhDocumentStatuts.Valide];

    public RhOption? Dossier
    {
        get => _dossier;
        set
        {
            if (!SetProperty(ref _dossier, value))
                return;
            NomDossier = value?.Id is null ? null : DossierNom(value.Id.Value);
            OnPropertyChanged(nameof(DossierChoisi));
            if (!_remplissage)
                _ = ChargerAsync();
        }
    }

    public bool DossierChoisi => Dossier?.Id is not null;
    public string Recherche { get => _recherche; set => SetProperty(ref _recherche, value); }

    public RhOption? CategorieFiltre
    {
        get => _categorieFiltre;
        set
        {
            if (SetProperty(ref _categorieFiltre, value) && !_remplissage)
                _ = ChargerAsync();
        }
    }

    public string StatutFiltre
    {
        get => _statutFiltre;
        set
        {
            if (SetProperty(ref _statutFiltre, value) && !_remplissage)
                _ = ChargerAsync();
        }
    }

    public RhOption? AgentFiltre
    {
        get => _agentFiltre;
        set
        {
            if (SetProperty(ref _agentFiltre, value) && !_remplissage)
                _ = ChargerAsync();
        }
    }

    // ===== Liste et détail =====

    public ObservableCollection<RhDocumentRow> Lignes { get; } = [];
    public string Compte => $"{Lignes.Count} document(s)";

    public RhDocumentRow? Selection
    {
        get => _selection;
        set
        {
            if (!SetProperty(ref _selection, value))
                return;
            if (value is null)
                Detail = null;
            else if (Detail?.Document.Id != value.Id)
                _ = ChargerDetailAsync(value.Id);
        }
    }

    public RhDocumentDetailDto? Detail
    {
        get => _detail;
        private set
        {
            if (!SetProperty(ref _detail, value))
                return;
            RafraichirActions();
        }
    }

    public bool HasDetail => Detail is not null;
    public string DetailEntete => Detail is { } d ? $"{d.Document.Reference} — {d.Document.Titre}" : string.Empty;
    public string DetailInfo => Detail is { } d
        ? $"{d.Document.Statut} · version {d.Document.Version} · {d.Document.Confidentialite} · créé par {d.Document.Auteur} le {RhFormat.DateHeure(d.Document.CreeLe)}"
          + (d.Document.ValidePar is null ? string.Empty : $" · validé par {d.Document.ValidePar} le {RhFormat.DateHeure(d.Document.ValideLe)}")
        : string.Empty;
    public bool DetailAgent => Detail?.Document.AgentId is not null;

    public ObservableCollection<RhVersionRow> Versions { get; } = [];
    public ObservableCollection<RhHistoriqueLigne> HistoriqueDocument { get; } = [];

    public Bitmap? ApercuImage
    {
        get => _apercuImage;
        private set
        {
            var ancien = _apercuImage;
            if (SetProperty(ref _apercuImage, value))
            {
                ancien?.Dispose();
                OnPropertyChanged(nameof(HasApercuImage));
            }
        }
    }

    public bool HasApercuImage => ApercuImage is not null;

    public string? ApercuTexte
    {
        get => _apercuTexte;
        private set
        {
            if (SetProperty(ref _apercuTexte, value))
                OnPropertyChanged(nameof(HasApercuTexte));
        }
    }

    public bool HasApercuTexte => !string.IsNullOrEmpty(ApercuTexte);
    public string ApercuInfo { get => _apercuInfo; private set => SetProperty(ref _apercuInfo, value); }

    // ===== Édition des métadonnées =====

    public ObservableCollection<RhOption> CategoriesEdit { get; } = [];
    public ObservableCollection<RhOption> DossiersEdit { get; } = [];
    public ObservableCollection<RhOption> AgentsEdit { get; } = [];
    public ObservableCollection<RhOption> ServicesEdit { get; } = [];
    public IReadOnlyList<string> Confidentialites => RhConfidentialites.All;

    public string EditTitre { get => _editTitre; set => SetProperty(ref _editTitre, value); }
    public string? EditType { get => _editType; set => SetProperty(ref _editType, value); }
    public RhOption? EditCategorie { get => _editCategorie; set => SetProperty(ref _editCategorie, value); }
    public RhOption? EditDossier { get => _editDossier; set => SetProperty(ref _editDossier, value); }
    public string? EditDescription { get => _editDescription; set => SetProperty(ref _editDescription, value); }
    public RhOption? EditAgent { get => _editAgent; set => SetProperty(ref _editAgent, value); }
    public RhOption? EditService { get => _editService; set => SetProperty(ref _editService, value); }
    public DateTime? EditDateDocument { get => _editDateDocument; set => SetProperty(ref _editDateDocument, value); }
    public DateTime? EditDateEffet { get => _editDateEffet; set => SetProperty(ref _editDateEffet, value); }
    public DateTime? EditDateExpiration { get => _editDateExpiration; set => SetProperty(ref _editDateExpiration, value); }
    public string EditConfidentialite { get => _editConfidentialite; set => SetProperty(ref _editConfidentialite, value); }
    public string? EditObservations { get => _editObservations; set => SetProperty(ref _editObservations, value); }
    public string? EditMotif { get => _editMotif; set => SetProperty(ref _editMotif, value); }
    public string? MotifRefus { get => _motifRefus; set => SetProperty(ref _motifRefus, value); }
    public string? MotifArchivage { get => _motifArchivage; set => SetProperty(ref _motifArchivage, value); }
    public string? CommentaireVersion { get => _commentaireVersion; set => SetProperty(ref _commentaireVersion, value); }

    private RhDocumentDto? Doc => Detail?.Document;
    private bool Archive => Doc?.Statut == RhDocumentStatuts.Archive;

    /// <summary>Métadonnées modifiables : droit documentaire, document ni validé ni archivé.</summary>
    public bool PeutModifierMeta => RhDroits.Documents && Doc is { EstFige: false } && !Archive;
    public bool LectureSeuleMeta => !PeutModifierMeta;
    public bool PeutDeplacer => RhDroits.Documents && Doc is not null && !Archive;
    public bool PeutEnregistrer => PeutDeplacer;
    public bool PeutSoumettre => RhDroits.Documents && Doc?.Statut == RhDocumentStatuts.Brouillon;
    public bool PeutValider => RhDroits.ValiderDocuments && Doc?.Statut is RhDocumentStatuts.Brouillon or RhDocumentStatuts.AValider;
    public bool PeutRefuser => RhDroits.ValiderDocuments && Doc?.Statut == RhDocumentStatuts.AValider;
    public bool PeutNouvelleVersion => RhDroits.Documents && Doc is not null && !Archive;
    public bool PeutArchiver => RhDroits.Documents && Doc is not null && !Archive;
    public bool PeutRestaurer => RhDroits.Documents && Archive;
    public bool PeutImporter => RhDroits.Documents && EstExplorateur;
    public bool PeutGererDossiers => RhDroits.Documents && EstExplorateur;
    public string AvisFige => Doc?.EstFige == true
        ? "Document validé : il est figé. Tu peux le déplacer, l'archiver ou déposer une nouvelle version (qui repassera en brouillon)."
        : Archive ? "Document archivé : restaure-le pour le modifier." : string.Empty;
    public bool HasAvisFige => AvisFige.Length > 0;

    // ===== Import =====

    public bool ImportOuvert { get => _importOuvert; set => SetProperty(ref _importOuvert, value); }
    public string NvTitre { get => _nvTitre; set => SetProperty(ref _nvTitre, value); }
    public string? NvType { get => _nvType; set => SetProperty(ref _nvType, value); }
    public RhOption? NvCategorie { get => _nvCategorie; set => SetProperty(ref _nvCategorie, value); }
    public RhOption? NvDossier { get => _nvDossier; set => SetProperty(ref _nvDossier, value); }
    public string? NvDescription { get => _nvDescription; set => SetProperty(ref _nvDescription, value); }
    public RhOption? NvAgent { get => _nvAgent; set => SetProperty(ref _nvAgent, value); }
    public RhOption? NvService { get => _nvService; set => SetProperty(ref _nvService, value); }
    public DateTime? NvDateDocument { get => _nvDateDocument; set => SetProperty(ref _nvDateDocument, value); }
    public DateTime? NvDateEffet { get => _nvDateEffet; set => SetProperty(ref _nvDateEffet, value); }
    public DateTime? NvDateExpiration { get => _nvDateExpiration; set => SetProperty(ref _nvDateExpiration, value); }
    public string NvConfidentialite { get => _nvConfidentialite; set => SetProperty(ref _nvConfidentialite, value); }
    public string? NvObservations { get => _nvObservations; set => SetProperty(ref _nvObservations, value); }
    public bool NvSoumettre { get => _nvSoumettre; set => SetProperty(ref _nvSoumettre, value); }

    public RhFichierChoisi? NvFichier
    {
        get => _nvFichier;
        private set
        {
            if (SetProperty(ref _nvFichier, value))
                OnPropertyChanged(nameof(NvFichierTexte));
        }
    }

    public string NvFichierTexte => NvFichier is { } f ? $"{f.Nom} ({RhFormat.Taille(f.Contenu.Length)})" : "Aucun fichier choisi";
    public string Auteur => SaphirSession.Agent is { } a ? $"{a.Nom} {a.Prenom}" : string.Empty;
    public string AideFichiers => Rh.ParametresRh is { } p
        ? $"Fichiers acceptés : {string.Join(", ", p.Extensions)} — {RhFormat.Taille(p.TailleMaxFichier)} au plus."
        : "Fichiers acceptés : PDF, PNG, JPEG, DOCX, TXT — 10 Mo au plus.";

    // ===== Dossiers =====

    public string? NomDossier { get => _nomDossier; set => SetProperty(ref _nomDossier, value); }

    // ===== Commandes =====

    public RelayCommand RechercherCommand { get; }
    public RelayCommand EnregistrerCommand { get; }
    public RelayCommand SoumettreCommand { get; }
    public RelayCommand ValiderCommand { get; }
    public RelayCommand RefuserCommand { get; }
    public RelayCommand ArchiverCommand { get; }
    public RelayCommand RestaurerCommand { get; }
    public RelayCommand NouvelleVersionCommand { get; }
    public RelayCommand TelechargerCommand { get; }
    public RelayCommand TelechargerVersionCommand { get; }
    public RelayCommand OuvrirImportCommand { get; }
    public RelayCommand FermerImportCommand { get; }
    public RelayCommand ChoisirFichierCommand { get; }
    public RelayCommand ImporterCommand { get; }
    public RelayCommand CreerDossierCommand { get; }
    public RelayCommand RenommerDossierCommand { get; }
    public RelayCommand SupprimerDossierCommand { get; }
    public RelayCommand OuvrirDossierAgentCommand { get; }

    public void RafraichirDroits() => RafraichirActions();

    private void RafraichirActions()
    {
        foreach (var nom in new[]
                 {
                     nameof(HasDetail), nameof(DetailEntete), nameof(DetailInfo), nameof(DetailAgent), nameof(PeutModifierMeta),
                     nameof(LectureSeuleMeta), nameof(PeutDeplacer), nameof(PeutEnregistrer), nameof(PeutSoumettre), nameof(PeutValider),
                     nameof(PeutRefuser), nameof(PeutNouvelleVersion), nameof(PeutArchiver), nameof(PeutRestaurer), nameof(PeutImporter),
                     nameof(PeutGererDossiers), nameof(AvisFige), nameof(HasAvisFige)
                 })
            OnPropertyChanged(nom);
    }

    private string? DossierNom(long id) => Rh.DossiersDocuments.FirstOrDefault(d => d.Id == id)?.Nom;

    private void ReconstruireOptions()
    {
        _remplissage = true;
        var (dossier, categorie, agent) = (Dossier?.Id, CategorieFiltre?.Id, AgentFiltre?.Id);
        Remplacer(Dossiers, Rh.OptionsDossiers("Tous les documents"));
        Remplacer(CategoriesFiltre, Rh.OptionsCategories("Toutes les catégories", actifsSeulement: false));
        Remplacer(AgentsFiltre, Rh.OptionsAgents("Tous les agents", avecArchives: true));
        Dossier = Dossiers.FirstOrDefault(o => o.Id == dossier) ?? Dossiers.FirstOrDefault();
        CategorieFiltre = CategoriesFiltre.FirstOrDefault(o => o.Id == categorie) ?? CategoriesFiltre.FirstOrDefault();
        AgentFiltre = AgentsFiltre.FirstOrDefault(o => o.Id == agent) ?? AgentsFiltre.FirstOrDefault();

        var (ec, ed, ea, es) = (EditCategorie?.Id, EditDossier?.Id, EditAgent?.Id, EditService?.Id);
        Remplacer(CategoriesEdit, Rh.OptionsCategories("Sans catégorie"));
        Remplacer(DossiersEdit, Rh.OptionsDossiers("Aucun dossier (racine)"));
        Remplacer(AgentsEdit, Rh.OptionsAgents("Aucun agent", avecArchives: true));
        Remplacer(ServicesEdit, Rh.Options(RhReferentielTypes.Services, false, "Aucun service"));
        EditCategorie = CategoriesEdit.FirstOrDefault(o => o.Id == ec) ?? CategoriesEdit.FirstOrDefault();
        EditDossier = DossiersEdit.FirstOrDefault(o => o.Id == ed) ?? DossiersEdit.FirstOrDefault();
        EditAgent = AgentsEdit.FirstOrDefault(o => o.Id == ea) ?? AgentsEdit.FirstOrDefault();
        EditService = ServicesEdit.FirstOrDefault(o => o.Id == es) ?? ServicesEdit.FirstOrDefault();
        OnPropertyChanged(nameof(AideFichiers));
        _remplissage = false;
    }

    private static void Remplacer(ObservableCollection<RhOption> cible, IEnumerable<RhOption> source)
    {
        cible.Clear();
        foreach (var o in source)
            cible.Add(o);
    }

    /// <summary>Documents d'un agent (depuis le dossier individuel ou la gestion du personnel).</summary>
    public async Task FiltrerAgentAsync(long? agentId)
    {
        _remplissage = true;
        AgentFiltre = AgentsFiltre.FirstOrDefault(o => o.Id == agentId) ?? AgentsFiltre.FirstOrDefault();
        Dossier = Dossiers.FirstOrDefault();
        _remplissage = false;
        await ChargerAsync();
    }

    public override async Task ChargerAsync()
    {
        var api = Api;
        if (api is null)
            return;
        await Rh.Charger(async () =>
        {
            var requete = "api/rh/documents?q=" + ApiClient.Q(Recherche.Trim());
            if (EstExplorateur && Dossier?.Id is { } d) requete += $"&dossierId={d}";
            if (CategorieFiltre?.Id is { } c) requete += $"&categorieId={c}";
            if (AgentFiltre?.Id is { } a) requete += $"&agentId={a}";
            var statut = _mode == RhDocumentsMode.AValider ? RhDocumentStatuts.AValider : StatutFiltre;
            if (statut != TousStatuts) requete += "&statut=" + ApiClient.Q(statut);
            var documents = await api.GetAsync<List<RhDocumentDto>>(requete);
            var id = Selection?.Id;
            Lignes.Clear();
            foreach (var doc in documents)
                Lignes.Add(new RhDocumentRow(doc));
            _selection = Lignes.FirstOrDefault(l => l.Id == id);
            OnPropertyChanged(nameof(Selection));
            OnPropertyChanged(nameof(Compte));
            if (_selection is not null)
                await ChargerDetailAsync(_selection.Id);
            else
                Detail = null;
        });
    }

    private async Task ChargerDetailAsync(long id)
    {
        var api = Api;
        if (api is null)
            return;
        await Rh.Charger(async () =>
        {
            var detail = await api.GetAsync<RhDocumentDetailDto>($"api/rh/documents/{id}");
            Detail = detail;
            var d = detail.Document;
            EditTitre = d.Titre;
            EditType = d.Type;
            EditCategorie = CategoriesEdit.FirstOrDefault(o => o.Id == d.CategorieId) ?? CategoriesEdit.FirstOrDefault();
            EditDossier = DossiersEdit.FirstOrDefault(o => o.Id == d.DossierId) ?? DossiersEdit.FirstOrDefault();
            EditDescription = d.Description;
            EditAgent = AgentsEdit.FirstOrDefault(o => o.Id == d.AgentId) ?? AgentsEdit.FirstOrDefault();
            EditService = ServicesEdit.FirstOrDefault(o => o.Id == d.ServiceId) ?? ServicesEdit.FirstOrDefault();
            EditDateDocument = RhFormat.VersDate(d.DateDocument);
            EditDateEffet = RhFormat.VersDate(d.DateEffet);
            EditDateExpiration = RhFormat.VersDate(d.DateExpiration);
            EditConfidentialite = d.Confidentialite;
            EditObservations = d.Observations;
            EditMotif = null;
            MotifRefus = null;
            MotifArchivage = null;
            CommentaireVersion = null;
            Versions.Clear();
            foreach (var v in detail.Versions)
                Versions.Add(new RhVersionRow(v));
            HistoriqueDocument.Clear();
            foreach (var h in detail.Historique)
                HistoriqueDocument.Add(RhHistoriqueLigne.Depuis(h));
            await ChargerApercuAsync(detail);
        });
    }

    /// <summary>Aperçu : image (PNG, JPEG), texte (TXT ou document généré) ; sinon téléchargement.</summary>
    private async Task ChargerApercuAsync(RhDocumentDetailDto detail)
    {
        ApercuImage = null;
        ApercuTexte = null;
        var courante = detail.Versions.FirstOrDefault(v => v.Numero == detail.Document.Version);
        if (courante is null || Api is not { } api)
        {
            ApercuInfo = "Aucun fichier.";
            return;
        }
        var image = RhFichiers.EstImage(courante.TypeMime);
        if (!image && !courante.ATexte && courante.TypeMime != "text/plain")
        {
            ApercuInfo = "Aperçu indisponible pour ce type de fichier : utilise « Télécharger » pour l'ouvrir.";
            return;
        }
        var fichier = await api.GetAsync<RhFichierDto>($"api/rh/documents/{detail.Document.Id}/versions/{courante.Numero}/fichier");
        if (image)
        {
            using var flux = new MemoryStream(fichier.Contenu);
            ApercuImage = new Bitmap(flux);
            ApercuInfo = string.Empty;
        }
        else
        {
            ApercuTexte = fichier.Texte ?? Encoding.UTF8.GetString(fichier.Contenu);
            ApercuInfo = string.Empty;
        }
    }

    // ===== Actions =====

    private async Task EnregistrerMetadonneesAsync()
    {
        var api = Api;
        if (api is null || Doc is not { } d)
            return;
        var input = new RhDocumentMajInput(EditTitre, EditType, EditCategorie?.Id, EditDossier?.Id, EditDescription, EditAgent?.Id,
            EditService?.Id, RhFormat.VersJour(EditDateDocument), RhFormat.VersJour(EditDateEffet), RhFormat.VersJour(EditDateExpiration),
            EditConfidentialite, EditObservations, EditMotif);
        if (d.EstFige)
            input = new RhDocumentMajInput(d.Titre, d.Type, d.CategorieId, EditDossier?.Id, d.Description, d.AgentId, d.ServiceId,
                d.DateDocument, d.DateEffet, d.DateExpiration, d.Confidentialite, d.Observations, EditMotif);
        await Rh.Executer(async () =>
        {
            await api.SendAsync<RhDocumentDto>(HttpMethod.Put, $"api/rh/documents/{d.Id}", input);
            await Rh.ChargerCommunAsync();
            await ChargerAsync();
        }, "Document enregistré.");
    }

    private async Task ActionAsync(string action, object? corps, string titre, string question, string succes)
    {
        var api = Api;
        if (api is null || Doc is not { } d)
            return;
        if (!await Rh.Confirmer(titre, $"{d.Reference} — {d.Titre}\n{question}", titre, action == "archiver"))
            return;
        await Rh.Executer(async () =>
        {
            await api.SendAsync<RhDocumentDto>(HttpMethod.Post, $"api/rh/documents/{d.Id}/{action}", corps);
            await ChargerAsync();
        }, succes);
    }

    private async Task RefuserAsync()
    {
        if (string.IsNullOrWhiteSpace(MotifRefus))
        {
            Rh.Erreur("Le motif du refus est obligatoire.");
            return;
        }
        await ActionAsync("refuser", new RhMotifInput(MotifRefus), "Refus", "Refuser ce document ? Il repassera en brouillon.", "Document refusé (retour en brouillon).");
    }

    private async Task NouvelleVersionAsync()
    {
        var api = Api;
        if (api is null || Doc is not { } d)
            return;
        if (string.IsNullOrWhiteSpace(CommentaireVersion))
        {
            Rh.Erreur("Indique le commentaire de modification de la nouvelle version.");
            return;
        }
        var fichier = await Rh.ChoisirFichierAsync();
        if (fichier is null)
            return;
        if (!await Rh.Confirmer("Nouvelle version",
                $"Déposer « {fichier.Nom} » comme version {d.Version + 1} de {d.Reference} ?\nLa version {d.Version} est conservée ; le document repasse en brouillon."))
            return;
        await Rh.Executer(async () =>
        {
            await api.PostAsync<RhDocumentDto>($"api/rh/documents/{d.Id}/versions", new RhVersionInput(fichier.Nom, fichier.Contenu, CommentaireVersion));
            await ChargerAsync();
        }, "Nouvelle version déposée.");
    }

    private void OuvrirImport()
    {
        NvTitre = string.Empty;
        NvType = null;
        NvCategorie = CategoriesEdit.FirstOrDefault();
        NvDossier = DossiersEdit.FirstOrDefault(o => o.Id == Dossier?.Id) ?? DossiersEdit.FirstOrDefault();
        NvDescription = null;
        NvAgent = AgentsEdit.FirstOrDefault(o => o.Id == AgentFiltre?.Id) ?? AgentsEdit.FirstOrDefault();
        NvService = ServicesEdit.FirstOrDefault();
        NvDateDocument = DateTime.Today;
        NvDateEffet = null;
        NvDateExpiration = null;
        NvConfidentialite = RhConfidentialites.Normal;
        NvObservations = null;
        NvSoumettre = false;
        NvFichier = null;
        ImportOuvert = true;
    }

    private async Task ChoisirFichierImportAsync()
    {
        var fichier = await Rh.ChoisirFichierAsync();
        if (fichier is null)
            return;
        NvFichier = fichier;
        if (string.IsNullOrWhiteSpace(NvTitre))
            NvTitre = Path.GetFileNameWithoutExtension(fichier.Nom);
    }

    private async Task ImporterAsync()
    {
        var api = Api;
        if (api is null)
            return;
        if (NvFichier is not { } fichier)
        {
            Rh.Erreur("Choisis le fichier à importer.");
            return;
        }
        var input = new RhDocumentCreationInput(NvTitre, NvType, NvCategorie?.Id, NvDossier?.Id, NvDescription, NvAgent?.Id, NvService?.Id,
            RhFormat.VersJour(NvDateDocument), RhFormat.VersJour(NvDateEffet), RhFormat.VersJour(NvDateExpiration), NvConfidentialite,
            NvObservations, NvSoumettre, fichier.Nom, fichier.Contenu);
        await Rh.Executer(async () =>
        {
            var dto = await api.PostAsync<RhDocumentDto>("api/rh/documents", input);
            ImportOuvert = false;
            await Rh.ChargerCommunAsync();
            await ChargerAsync();
            Selection = Lignes.FirstOrDefault(l => l.Id == dto.Id);
        }, "Document importé.");
    }

    private async Task CreerDossierAsync()
    {
        var api = Api;
        if (api is null)
            return;
        if (string.IsNullOrWhiteSpace(NomDossier))
        {
            Rh.Erreur("Indique le nom du nouveau dossier.");
            return;
        }
        var parent = Dossier?.Id;
        var nom = NomDossier.Trim();
        if (parent is not null && nom == DossierNom(parent.Value))
        {
            Rh.Erreur("Change le nom pour créer un sous-dossier (ou utilise « Renommer »).");
            return;
        }
        await Rh.Executer(async () =>
        {
            var dto = await api.PostAsync<RhDossierDocDto>("api/rh/dossiers", new RhDossierDocInput(nom, parent));
            await Rh.ChargerCommunAsync();
            Dossier = Dossiers.FirstOrDefault(o => o.Id == dto.Id);
        }, "Dossier créé.");
    }

    private async Task RenommerDossierAsync()
    {
        var api = Api;
        if (api is null || Dossier?.Id is not { } id || string.IsNullOrWhiteSpace(NomDossier))
            return;
        var parent = Rh.DossiersDocuments.FirstOrDefault(d => d.Id == id)?.ParentId;
        await Rh.Executer(async () =>
        {
            await api.SendAsync<RhDossierDocDto>(HttpMethod.Put, $"api/rh/dossiers/{id}", new RhDossierDocInput(NomDossier.Trim(), parent));
            await Rh.ChargerCommunAsync();
        }, "Dossier renommé.");
    }

    private async Task SupprimerDossierAsync()
    {
        var api = Api;
        if (api is null || Dossier?.Id is not { } id)
            return;
        if (!await Rh.Confirmer("Suppression du dossier", $"Supprimer le dossier « {Dossier.Libelle} » ? Il doit être vide.", "Supprimer", true))
            return;
        await Rh.Executer(async () =>
        {
            await api.SendAsync(HttpMethod.Delete, $"api/rh/dossiers/{id}");
            await Rh.ChargerCommunAsync();
            Dossier = Dossiers.FirstOrDefault();
        }, "Dossier supprimé.");
    }

    /// <summary>Sélectionne un document (après génération depuis un modèle).</summary>
    public async Task SelectionnerAsync(long documentId)
    {
        _remplissage = true;
        Dossier = Dossiers.FirstOrDefault();
        AgentFiltre = AgentsFiltre.FirstOrDefault();
        CategorieFiltre = CategoriesFiltre.FirstOrDefault();
        StatutFiltre = TousStatuts;
        Recherche = string.Empty;
        _remplissage = false;
        await ChargerAsync();
        Selection = Lignes.FirstOrDefault(l => l.Id == documentId);
    }
}

// =====================================================================================
// 11. Modèles documentaires (5.6)
// =====================================================================================

public sealed record RhVariableLigne(string Code, string Description);

public sealed class RhModelesViewModel : RhPageViewModel, IRhDroitsSensibles
{
    private RhModeleDto? _selection;
    private bool _nouveau;
    private string _libelle = string.Empty;
    private string _type = RhModeleTypes.All[0];
    private RhOption? _categorie;
    private string _contenu = string.Empty;
    private bool _actif = true;
    private RhOption? _agent;
    private string? _titreDocument;
    private RhOption? _dossier;
    private string _confidentialite = RhConfidentialites.Normal;
    private bool _soumettre;
    private string? _apercu;
    private RhDocumentDto? _genere;

    public RhModelesViewModel(RhViewModel rh) : base(rh)
    {
        NouveauCommand = new RelayCommand(Nouveau);
        EnregistrerCommand = new RelayCommand(() => _ = EnregistrerAsync());
        ApercuCommand = new RelayCommand(() => _ = GenererAsync(apercu: true));
        GenererCommand = new RelayCommand(() => _ = GenererAsync(apercu: false));
        OuvrirGenereCommand = new RelayCommand(() =>
        {
            if (Genere is { } d && Rh.AfficherPage(RhViewModel.Documents, charger: false) is RhDocumentsViewModel docs)
                _ = docs.SelectionnerAsync(d.Id);
        });
        InsererCommand = new RelayCommand(p =>
        {
            if (p is RhVariableLigne v && PeutModifier)
                Contenu += v.Code;
        });
        Rh.DonneesChangees += ReconstruireOptions;
        ReconstruireOptions();
    }

    public override string Titre => "Modèles documentaires";
    public override string Description =>
        "Modèles RH à variables automatiques ({{agent_nom}} ou {{NOM}}, {{agent_rio}}…) générant un document PDF pour un agent.";

    public ObservableCollection<RhModeleDto> Modeles { get; } = [];
    public IReadOnlyList<string> Types => RhModeleTypes.All;
    public ObservableCollection<RhOption> CategoriesChoix { get; } = [];
    public ObservableCollection<RhOption> AgentsChoix { get; } = [];
    public ObservableCollection<RhOption> DossiersChoix { get; } = [];
    public IReadOnlyList<string> Confidentialites => RhConfidentialites.All;
    public IReadOnlyList<RhVariableLigne> Variables { get; } =
        RhModeleVariables.All.Select(v => new RhVariableLigne("{{" + v.Code + "}}", $"{v.Description} (ou {{{{{v.Alias}}}}})")).ToList();

    public bool PeutModifier => RhDroits.Documents;

    public RhModeleDto? Selection
    {
        get => _selection;
        set
        {
            if (!SetProperty(ref _selection, value) || value is null)
                return;
            Nouveau_ = false;
            Libelle = value.Libelle;
            Type = value.Type;
            Categorie = CategoriesChoix.FirstOrDefault(o => o.Id == value.CategorieId) ?? CategoriesChoix.FirstOrDefault();
            Contenu = value.Contenu;
            Actif = value.Actif;
            Apercu = null;
        }
    }

    private bool Nouveau_
    {
        get => _nouveau;
        set
        {
            if (SetProperty(ref _nouveau, value))
                OnPropertyChanged(nameof(EnEdition));
        }
    }

    public bool EnEdition => _nouveau || Selection is not null;
    public bool PeutGenerer => RhDroits.Documents && Selection is { Actif: true };

    public string Libelle { get => _libelle; set => SetProperty(ref _libelle, value); }
    public string Type { get => _type; set => SetProperty(ref _type, value); }
    public RhOption? Categorie { get => _categorie; set => SetProperty(ref _categorie, value); }
    public string Contenu { get => _contenu; set => SetProperty(ref _contenu, value); }
    public bool Actif { get => _actif; set => SetProperty(ref _actif, value); }

    public RhOption? Agent { get => _agent; set => SetProperty(ref _agent, value); }
    public string? TitreDocument { get => _titreDocument; set => SetProperty(ref _titreDocument, value); }
    public RhOption? Dossier { get => _dossier; set => SetProperty(ref _dossier, value); }
    public string Confidentialite { get => _confidentialite; set => SetProperty(ref _confidentialite, value); }
    public bool Soumettre { get => _soumettre; set => SetProperty(ref _soumettre, value); }

    public string? Apercu
    {
        get => _apercu;
        private set
        {
            if (SetProperty(ref _apercu, value))
                OnPropertyChanged(nameof(HasApercu));
        }
    }

    public bool HasApercu => !string.IsNullOrEmpty(Apercu);

    public RhDocumentDto? Genere
    {
        get => _genere;
        private set
        {
            if (SetProperty(ref _genere, value))
            {
                OnPropertyChanged(nameof(HasGenere));
                OnPropertyChanged(nameof(GenereTexte));
            }
        }
    }

    public bool HasGenere => Genere is not null;
    public string GenereTexte => Genere is { } d ? $"Document {d.Reference} créé ({d.Statut}) : {d.NomFichier}" : string.Empty;

    public RelayCommand NouveauCommand { get; }
    public RelayCommand EnregistrerCommand { get; }
    public RelayCommand ApercuCommand { get; }
    public RelayCommand GenererCommand { get; }
    public RelayCommand OuvrirGenereCommand { get; }
    public RelayCommand InsererCommand { get; }

    public void RafraichirDroits()
    {
        OnPropertyChanged(nameof(PeutModifier));
        OnPropertyChanged(nameof(PeutGenerer));
    }

    private void ReconstruireOptions()
    {
        var (c, a, d) = (Categorie?.Id, Agent?.Id, Dossier?.Id);
        CategoriesChoix.Clear();
        foreach (var o in Rh.OptionsCategories("Sans catégorie"))
            CategoriesChoix.Add(o);
        AgentsChoix.Clear();
        foreach (var o in Rh.OptionsAgents())
            AgentsChoix.Add(o);
        DossiersChoix.Clear();
        foreach (var o in Rh.OptionsDossiers("Aucun dossier (racine)"))
            DossiersChoix.Add(o);
        Categorie = CategoriesChoix.FirstOrDefault(o => o.Id == c) ?? CategoriesChoix.FirstOrDefault();
        Agent = AgentsChoix.FirstOrDefault(o => o.Id == a);
        Dossier = DossiersChoix.FirstOrDefault(o => o.Id == d) ?? DossiersChoix.FirstOrDefault();
    }

    /// <summary>Prépare une génération pour un agent (depuis son dossier).</summary>
    public async Task PreparerAsync(long? agentId)
    {
        await ChargerAsync();
        if (agentId is not null)
            Agent = AgentsChoix.FirstOrDefault(o => o.Id == agentId);
    }

    public override async Task ChargerAsync()
    {
        var api = Api;
        if (api is null)
            return;
        await Rh.Charger(async () =>
        {
            var modeles = await api.GetAsync<List<RhModeleDto>>("api/rh/modeles");
            var id = Selection?.Id;
            Modeles.Clear();
            foreach (var m in modeles)
                Modeles.Add(m);
            var selection = Modeles.FirstOrDefault(m => m.Id == id) ?? (_nouveau ? null : Modeles.FirstOrDefault());
            if (selection is not null && selection.Id != id)
                Selection = selection;
            else
            {
                _selection = selection;
                OnPropertyChanged(nameof(Selection));
            }
            OnPropertyChanged(nameof(EnEdition));
            OnPropertyChanged(nameof(PeutGenerer));
        });
    }

    private void Nouveau()
    {
        _selection = null;
        OnPropertyChanged(nameof(Selection));
        Nouveau_ = true;
        Libelle = string.Empty;
        Type = RhModeleTypes.All[^1];
        Categorie = CategoriesChoix.FirstOrDefault();
        Contenu = "TITRE DU DOCUMENT N° {{numero_document}}\n\n{{agent_grade}} {{agent_nom}} {{agent_prenom}} — RIO {{agent_rio}}\n\nFait à Créteil, le {{date_document}}.";
        Actif = true;
        Apercu = null;
        OnPropertyChanged(nameof(PeutGenerer));
    }

    private async Task EnregistrerAsync()
    {
        var api = Api;
        if (api is null || !EnEdition)
            return;
        var input = new RhModeleInput(Libelle.Trim(), Type, Categorie?.Id, Contenu, Actif);
        var selection = Selection;
        await Rh.Executer(async () =>
        {
            var dto = selection is null
                ? await api.PostAsync<RhModeleDto>("api/rh/modeles", input)
                : await api.SendAsync<RhModeleDto>(HttpMethod.Put, $"api/rh/modeles/{selection.Id}", input);
            Nouveau_ = false;
            await ChargerAsync();
            Selection = Modeles.FirstOrDefault(m => m.Id == dto.Id);
        }, "Modèle enregistré.");
    }

    private async Task GenererAsync(bool apercu)
    {
        var api = Api;
        if (api is null || Selection is not { } modele)
            return;
        if (Agent?.Id is not { } agentId)
        {
            Rh.Erreur("Choisis l'agent concerné.");
            return;
        }
        if (!apercu && !await Rh.Confirmer("Génération du document",
                $"Générer « {modele.Libelle} » pour {Agent.Libelle} ?\nLe document PDF est enregistré avec les valeurs actuelles de l'agent ; une fois validé, il ne changera plus."))
            return;
        var input = new RhGenerationInput(agentId, TitreDocument, Dossier?.Id, null, Confidentialite, apercu, Soumettre);
        await Rh.Executer(async () =>
        {
            var resultat = await api.PostAsync<RhGenerationDto>($"api/rh/modeles/{modele.Id}/generer", input);
            Apercu = resultat.Texte;
            if (resultat.VariablesInconnues.Count > 0)
                Rh.Erreur("Variables inconnues laissées telles quelles : " + string.Join(", ", resultat.VariablesInconnues));
            if (!apercu)
            {
                Genere = resultat.Document;
                await Rh.ChargerCommunAsync();
            }
        }, apercu ? null : "Document généré.");
    }
}
