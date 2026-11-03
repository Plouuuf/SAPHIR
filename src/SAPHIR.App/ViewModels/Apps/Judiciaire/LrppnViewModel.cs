using System.Collections.ObjectModel;
using System.Globalization;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Judiciaire;

namespace Saphir.ViewModels.Apps.Judiciaire;

public sealed record ProcedureLigne(ProcedureLigneDto Dto)
{
    public string Numero => Dto.Numero;
    public string Intitule => Dto.Intitule;
    public string Type => Dto.TypeAffaire;
    public string Statut => Dto.Statut;
    public string Responsable => Dto.Responsable;
    public string Ouverte => FormatsJ.DateHeure(Dto.OuverteLe);
    public string Actes => Dto.ActesAValider > 0 ? $"{Dto.NombreActes} ({Dto.ActesAValider} à valider)" : Dto.NombreActes.ToString(CultureInfo.InvariantCulture);
    public string Intervention => Dto.InterventionNumero ?? string.Empty;
}

public sealed record ActeLigne(ActeLigneDto Dto)
{
    public string Numero => Dto.Numero;
    public string Procedure => Dto.ProcedureNumero;
    public string Type => Dto.TypeLibelle;
    public string Titre => Dto.Titre;
    public string Statut => Dto.Statut;
    public string Redacteur => Dto.Redacteur;
    public string Personne => Dto.Personne ?? string.Empty;
    public string MisAJour => FormatsJ.DateHeure(Dto.MajLe);
    public string Validation => Dto.ValideLe is { } v ? $"{FormatsJ.DateHeure(v)} — {Dto.ValidePar}" : string.Empty;
}

public sealed record PieceLigne(PieceDto Dto)
{
    public string Numero => Dto.Numero;
    public string Procedure => Dto.ProcedureNumero;
    public string Categorie => Dto.Categorie;
    public string Libelle => Dto.Libelle;
    public string Scelle => Dto.NumeroScelle ?? string.Empty;
    public string Statut => Dto.Statut;
    public string Localisation => Dto.Localisation ?? string.Empty;
    public string Fichier => Dto.NomFichier is null ? string.Empty : $"{Dto.NomFichier} ({FormatsJ.Taille(Dto.Taille)})";
    public bool AFichier => Dto.NomFichier is not null;
    public string Mouvements => string.Join("\n", Dto.Mouvements.Select(m => $"{FormatsJ.DateHeure(m.Date)} — {m.Mouvement}" +
        (m.Destination is null ? string.Empty : $" → {m.Destination}") + $" ({m.Auteur})" + (m.Observations is null ? string.Empty : $" : {m.Observations}")));
}

/// <summary>Modèle d'acte modifiable (15.5 : versionné et administrable).</summary>
public sealed class ModeleLigne(ModeleDto dto) : ViewModelBase
{
    private string _texte = dto.Texte;
    private string? _motif;
    public ModeleDto Dto { get; } = dto;
    public string Libelle => Dto.Libelle;
    public string Version => Dto.Version == 0 ? "modèle par défaut" : $"version {Dto.Version} — {FormatsJ.DateHeure(Dto.MajLe)} par {Dto.MajPar}";
    public string Texte { get => _texte; set => SetProperty(ref _texte, value); }
    public string? Motif { get => _motif; set => SetProperty(ref _motif, value); }
}

/// <summary>
/// LRPPN : procédures judiciaires de simulation (13 à 15). Menu latéral du cahier des charges, dossier de procédure,
/// éditeur d'actes, pièces et scellés, modèles et historique. Peut être ouvert depuis PÉGASE (« intervention:ID »).
/// </summary>
public sealed class LrppnViewModel : AppViewModelBase, IHoteJudiciaire
{
    public const string Mes = "mes";
    public const string Nouvelle = "nouvelle";
    public const string RechercheCode = "recherche";
    public const string Redaction = "redaction";
    public const string AValider = "a-valider";
    public const string Personnes = "personnes";
    public const string Pieces = "pieces";
    public const string Modeles = "modeles";
    public const string HistoriqueCode = "historique";
    public const string Archives = "archives";

    private string _vue = "liste";
    private string _listeCourante = Mes;
    private MenuEntree? _menuSelection;
    private bool _menuOuvert = true;
    private string? _q;
    private string _statutFiltre = "Tous";
    private ProcedureLigne? _procedureSelection;
    private ActeLigne? _acteSelection;
    private PieceLigne? _pieceSelection;
    private string? _pieceQ;
    private string _mouvement = PieceMouvements.All[0];
    private string? _destination, _observationsMouvement;
    private ModeleLigne? _modeleSelection;
    private DossiersAssociesDto? _dossiersPersonne;
    private ActeEditeurViewModel? _editeur;

    public LrppnViewModel()
    {
        Menu.Add(new MenuEntree(Mes, "Mes procédures", "Procédures dont tu es responsable ou rédacteur"));
        if (SaphirSession.Has(Permissions.LrppnRediger))
            Menu.Add(new MenuEntree(Nouvelle, "Nouvelle procédure", "Ouvrir une procédure"));
        Menu.Add(new MenuEntree(RechercheCode, "Recherche", "Toutes les procédures"));
        if (SaphirSession.Has(Permissions.LrppnRediger))
            Menu.Add(new MenuEntree(Redaction, "Rédaction d'actes", "Mes actes en cours de rédaction"));
        if (SaphirSession.Has(Permissions.LrppnValider))
            Menu.Add(new MenuEntree(AValider, "Actes à valider", "Actes soumis à relecture"));
        Menu.Add(new MenuEntree(Personnes, "Personnes concernées", "Recherche d'individu et dossiers associés"));
        Menu.Add(new MenuEntree(Pieces, "Pièces et scellés", "Pièces, scellés de simulation et mouvements"));
        Menu.Add(new MenuEntree(Modeles, "Modèles", "Modèles d'actes versionnés"));
        Menu.Add(new MenuEntree(HistoriqueCode, "Historique", "Modifications et validations"));
        Menu.Add(new MenuEntree(Archives, "Archives", "Procédures archivées"));

        Formulaire = new ProcedureFormulaireViewModel(this);
        Dossier = new DossierViewModel(this);
        PickerPersonnes = new PersonnePickerViewModel(this, p => { if (p is not null) _ = ChargerDossiersPersonneAsync(p.Id); });

        RechercherCommand = new RelayCommand(() => _ = ChargerListeAsync());
        OuvrirProcedureCommand = new RelayCommand(() => { if (ProcedureSelection is { } p) _ = OuvrirDossierAsync(p.Dto.Id); });
        OuvrirActeCommand = new RelayCommand(() => { if (ActeSelection is { } a) _ = OuvrirActeAsync(a.Dto.Id); });
        BasculerMenuCommand = new RelayCommand(() => MenuOuvert = !MenuOuvert);
        RetourListeCommand = new RelayCommand(() => Naviguer(_listeCourante));
        RechercherPiecesCommand = new RelayCommand(() => _ = ChargerPiecesAsync());
        MouvementCommand = new RelayCommand(() => _ = MouvementAsync());
        TelechargerPieceCommand = new RelayCommand(() => { if (PieceSelection is { AFichier: true } p) _ = ExecuterAsync(() => TelechargerAsync($"api/lrppn/pieces/{p.Dto.Id}/fichier")); });
        OuvrirProcedurePieceCommand = new RelayCommand(() => { if (PieceSelection is { } p) _ = OuvrirDossierAsync(p.Dto.ProcedureId); });
        EnregistrerModeleCommand = new RelayCommand(() => _ = EnregistrerModeleAsync());
        ActualiserHistoriqueCommand = new RelayCommand(() => _ = ChargerHistoriqueAsync());
        OuvrirProcedurePersonneCommand = new RelayCommand(p => { if (p is ProcedureLigneDto l) _ = OuvrirDossierAsync(l.Id); });

        var argument = SaphirSession.TakeAppArgument(SaphirApps.Lrppn);
        if (argument is not null && TraiterArgument(argument))
            return;
        MenuSelection = Menu[0];
    }

    // ===== Droits =====

    public bool PeutRediger => SaphirSession.Has(Permissions.LrppnRediger);
    public bool PeutValider => SaphirSession.Has(Permissions.LrppnValider);
    public bool PeutPieces => SaphirSession.Has(Permissions.LrppnPieces);

    // ===== Navigation =====

    public ObservableCollection<MenuEntree> Menu { get; } = [];
    public bool MenuOuvert { get => _menuOuvert; set => SetProperty(ref _menuOuvert, value); }

    public MenuEntree? MenuSelection
    {
        get => _menuSelection;
        set
        {
            if (!SetProperty(ref _menuSelection, value) || value is null)
                return;
            Naviguer(value.Code);
        }
    }

    public string Vue
    {
        get => _vue;
        private set
        {
            if (!SetProperty(ref _vue, value))
                return;
            foreach (var nom in new[] { nameof(IsListe), nameof(IsFormulaire), nameof(IsDossier), nameof(IsActe), nameof(IsActes), nameof(IsPersonnes),
                         nameof(IsPieces), nameof(IsModeles), nameof(IsHistorique) })
                OnPropertyChanged(nom);
        }
    }

    public bool IsListe => Vue == "liste";
    public bool IsFormulaire => Vue == "formulaire";
    public bool IsDossier => Vue == "dossier";
    public bool IsActe => Vue == "acte";
    public bool IsActes => Vue == "actes";
    public bool IsPersonnes => Vue == "personnes";
    public bool IsPieces => Vue == "pieces";
    public bool IsModeles => Vue == "modeles";
    public bool IsHistorique => Vue == "historique";

    public string TitreListe => _listeCourante switch
    {
        Mes => "Mes procédures",
        Archives => "Procédures archivées",
        Redaction => "Mes actes en cours de rédaction",
        AValider => "Actes à valider (en relecture)",
        _ => "Recherche de procédures"
    };

    private void Naviguer(string code)
    {
        if (_menuSelection?.Code != code)
        {
            _menuSelection = Menu.FirstOrDefault(m => m.Code == code);
            OnPropertyChanged(nameof(MenuSelection));
        }
        switch (code)
        {
            case Mes or RechercheCode or Archives:
                _listeCourante = code;
                OnPropertyChanged(nameof(TitreListe));
                Vue = "liste";
                _ = ChargerListeAsync();
                break;
            case Nouvelle:
                Formulaire.Nouvelle();
                Vue = "formulaire";
                break;
            case Redaction or AValider:
                _listeCourante = code;
                OnPropertyChanged(nameof(TitreListe));
                Vue = "actes";
                _ = ChargerActesAsync();
                break;
            case Personnes:
                Vue = "personnes";
                break;
            case Pieces:
                Vue = "pieces";
                _ = ChargerPiecesAsync();
                break;
            case Modeles:
                Vue = "modeles";
                _ = ChargerModelesAsync();
                break;
            case HistoriqueCode:
                Vue = "historique";
                _ = ChargerHistoriqueAsync();
                break;
        }
    }

    public RelayCommand BasculerMenuCommand { get; }
    public RelayCommand RetourListeCommand { get; }

    // ===== Liste des procédures =====

    public string? Q { get => _q; set => SetProperty(ref _q, value); }
    public IReadOnlyList<string> Filtres { get; } = ["Tous", .. ProcedureStatuts.All];
    public string StatutFiltre { get => _statutFiltre; set { if (SetProperty(ref _statutFiltre, value)) _ = ChargerListeAsync(); } }
    public ObservableCollection<ProcedureLigne> ProcedureLignes { get; } = [];
    public ProcedureLigne? ProcedureSelection { get => _procedureSelection; set => SetProperty(ref _procedureSelection, value); }
    public RelayCommand RechercherCommand { get; }
    public RelayCommand OuvrirProcedureCommand { get; }

    private async Task ChargerListeAsync()
    {
        if (Api is not { } api)
            return;
        if (_listeCourante is Redaction or AValider)
        {
            await ChargerActesAsync();
            return;
        }
        await RunAsync(async () =>
        {
            var parametres = new List<string>();
            if (!string.IsNullOrWhiteSpace(Q)) parametres.Add("q=" + ApiClient.Q(Q.Trim()));
            if (StatutFiltre != "Tous") parametres.Add("statut=" + ApiClient.Q(StatutFiltre));
            if (_listeCourante == Mes) parametres.Add("miennes=true");
            if (_listeCourante == Archives) parametres.Add("archives=true");
            var liste = await api.GetAsync<List<ProcedureLigneDto>>("api/lrppn/procedures" + (parametres.Count == 0 ? string.Empty : "?" + string.Join("&", parametres)));
            var id = ProcedureSelection?.Dto.Id;
            ProcedureLignes.Clear();
            foreach (var p in liste)
                ProcedureLignes.Add(new ProcedureLigne(p));
            ProcedureSelection = ProcedureLignes.FirstOrDefault(p => p.Dto.Id == id);
        });
    }

    // ===== Actes (rédaction, à valider) =====

    public ObservableCollection<ActeLigne> ActeLignes { get; } = [];
    public ActeLigne? ActeSelection { get => _acteSelection; set => SetProperty(ref _acteSelection, value); }
    public RelayCommand OuvrirActeCommand { get; }

    private async Task ChargerActesAsync()
    {
        if (Api is not { } api)
            return;
        await RunAsync(async () =>
        {
            var chemin = _listeCourante == AValider
                ? "api/lrppn/actes?statut=" + ApiClient.Q(ActeStatuts.EnRelecture)
                : "api/lrppn/actes?miens=true&statut=" + ApiClient.Q(ActeStatuts.Brouillon);
            var liste = await api.GetAsync<List<ActeLigneDto>>(chemin);
            if (_listeCourante == Redaction)
                liste.AddRange(await api.GetAsync<List<ActeLigneDto>>("api/lrppn/actes?miens=true&statut=" + ApiClient.Q(ActeStatuts.EnRelecture)));
            ActeLignes.Clear();
            foreach (var a in liste.OrderByDescending(a => a.MajLe))
                ActeLignes.Add(new ActeLigne(a));
        });
    }

    // ===== Formulaire, dossier, acte =====

    public ProcedureFormulaireViewModel Formulaire { get; }
    public DossierViewModel Dossier { get; }

    public ActeEditeurViewModel? Editeur
    {
        get => _editeur;
        private set => SetProperty(ref _editeur, value);
    }

    public void AfficherFormulaire() => Vue = "formulaire";

    public async Task OuvrirDossierAsync(long procedureId)
    {
        await Dossier.ChargerAsync(procedureId);
        if (Dossier.Procedure is not null)
            Vue = "dossier";
    }

    public async Task OuvrirActeAsync(long acteId)
    {
        if (Api is not { } api)
            return;
        await RunAsync(async () =>
        {
            var acte = await api.GetAsync<ActeDto>($"api/lrppn/actes/{acteId}");
            if (Dossier.Procedure?.Id != acte.ProcedureId)
                await Dossier.ChargerAsync(acte.ProcedureId);
            var editeur = new ActeEditeurViewModel(this, acte, Dossier.Procedure);
            Editeur = editeur;
            Vue = "acte";
            await editeur.InitialiserAsync();
        });
    }

    /// <summary>Retour au dossier de la procédure (après l'éditeur d'acte).</summary>
    public async Task RetourDossierAsync()
    {
        if (Dossier.Procedure is { } p)
            await OuvrirDossierAsync(p.Id);
        else
            Naviguer(_listeCourante);
    }

    // ===== Ouverture depuis PÉGASE (20.3) =====

    /// <summary>Argument « intervention:ID » : nouvelle procédure préremplie depuis l'intervention.</summary>
    private bool TraiterArgument(string argument)
    {
        const string prefixe = "intervention:";
        if (!argument.StartsWith(prefixe, StringComparison.Ordinal) ||
            !long.TryParse(argument[prefixe.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var interventionId))
            return false;
        if (!PeutRediger)
        {
            ErrorText = "Ouverture d'une procédure impossible : la permission de rédaction LRPPN est nécessaire.";
            return false;
        }
        _menuSelection = Menu.FirstOrDefault(m => m.Code == Nouvelle);
        OnPropertyChanged(nameof(MenuSelection));
        Formulaire.Nouvelle();
        Vue = "formulaire";
        _ = Formulaire.PreremplirInterventionAsync(interventionId);
        return true;
    }

    // ===== Personnes concernées (14.2, 14.3) =====

    public PersonnePickerViewModel PickerPersonnes { get; }

    public DossiersAssociesDto? DossiersPersonne
    {
        get => _dossiersPersonne;
        private set
        {
            if (!SetProperty(ref _dossiersPersonne, value))
                return;
            OnPropertyChanged(nameof(ADossiersPersonne));
            OnPropertyChanged(nameof(DossiersTexte));
        }
    }

    public bool ADossiersPersonne => DossiersPersonne is not null;
    public string DossiersTexte => DossiersPersonne is { } d ? DossiersFormat.Texte(d) : string.Empty;
    public RelayCommand OuvrirProcedurePersonneCommand { get; }

    private async Task ChargerDossiersPersonneAsync(long personneId)
    {
        if (Api is not { } api)
            return;
        await RunAsync(async () => DossiersPersonne = await api.GetAsync<DossiersAssociesDto>($"api/lrppn/personnes/{personneId}/dossiers"));
    }

    // ===== Pièces et scellés =====

    public ObservableCollection<PieceLigne> PieceLignes { get; } = [];
    public PieceLigne? PieceSelection { get => _pieceSelection; set { if (SetProperty(ref _pieceSelection, value)) OnPropertyChanged(nameof(APieceSelection)); } }
    public bool APieceSelection => PieceSelection is not null;
    public string? PieceQ { get => _pieceQ; set => SetProperty(ref _pieceQ, value); }
    public IReadOnlyList<string> MouvementsPossibles => PieceMouvements.All;
    public string Mouvement { get => _mouvement; set => SetProperty(ref _mouvement, value); }
    public string? Destination { get => _destination; set => SetProperty(ref _destination, value); }
    public string? ObservationsMouvement { get => _observationsMouvement; set => SetProperty(ref _observationsMouvement, value); }
    public RelayCommand RechercherPiecesCommand { get; }
    public RelayCommand MouvementCommand { get; }
    public RelayCommand TelechargerPieceCommand { get; }
    public RelayCommand OuvrirProcedurePieceCommand { get; }

    private async Task ChargerPiecesAsync()
    {
        if (Api is not { } api)
            return;
        await RunAsync(async () =>
        {
            var liste = await api.GetAsync<List<PieceDto>>("api/lrppn/pieces" + (string.IsNullOrWhiteSpace(PieceQ) ? string.Empty : "?q=" + ApiClient.Q(PieceQ.Trim())));
            var id = PieceSelection?.Dto.Id;
            PieceLignes.Clear();
            foreach (var p in liste)
                PieceLignes.Add(new PieceLigne(p));
            PieceSelection = PieceLignes.FirstOrDefault(p => p.Dto.Id == id);
        });
    }

    private async Task MouvementAsync()
    {
        if (Api is not { } api || PieceSelection is not { } piece)
            return;
        await RunAsync(async () =>
        {
            await api.PostAsync<PieceDto>($"api/lrppn/pieces/{piece.Dto.Id}/mouvements", new MouvementInput(Mouvement, Destination, ObservationsMouvement));
            Destination = ObservationsMouvement = null;
            await ChargerPiecesAsync();
        }, "Mouvement enregistré.");
    }

    // ===== Modèles =====

    public ObservableCollection<ModeleLigne> ModeleLignes { get; } = [];
    public ModeleLigne? ModeleSelection { get => _modeleSelection; set => SetProperty(ref _modeleSelection, value); }
    public RelayCommand EnregistrerModeleCommand { get; }

    private async Task ChargerModelesAsync()
    {
        if (Api is not { } api)
            return;
        await RunAsync(async () =>
        {
            var type = ModeleSelection?.Dto.Type;
            ModeleLignes.Clear();
            foreach (var m in await api.GetAsync<List<ModeleDto>>("api/lrppn/modeles"))
                ModeleLignes.Add(new ModeleLigne(m));
            ModeleSelection = ModeleLignes.FirstOrDefault(m => m.Dto.Type == type) ?? ModeleLignes.FirstOrDefault();
        });
    }

    private async Task EnregistrerModeleAsync()
    {
        if (Api is not { } api || ModeleSelection is not { } modele)
            return;
        await RunAsync(async () =>
        {
            await api.SendAsync<ModeleDto>(HttpMethod.Put, $"api/lrppn/modeles/{modele.Dto.Type}", new ModeleInput(modele.Texte, modele.Motif));
            await ChargerModelesAsync();
        }, "Nouvelle version du modèle enregistrée.");
    }

    // ===== Historique =====

    public ObservableCollection<HistoriqueLigne> HistoriqueLignes { get; } = [];
    public RelayCommand ActualiserHistoriqueCommand { get; }

    private async Task ChargerHistoriqueAsync()
    {
        if (Api is not { } api)
            return;
        await RunAsync(async () =>
        {
            HistoriqueLignes.Clear();
            foreach (var h in await api.GetAsync<List<HistoriqueDto>>("api/lrppn/historique?limit=300"))
                HistoriqueLignes.Add(HistoriqueLigne.De(h));
        });
    }

    // ===== Hôte et fichiers =====

    public IFichiersPoste? Fichiers { get; set; }

    public Task ExecuterAsync(Func<Task> travail, string? succes = null) => RunAsync(travail, succes);

    public Task<bool> ConfirmerAsync(string titre, string message, string libelle, bool danger = false) => ConfirmAsync(titre, message, libelle, danger);

    public async Task TelechargerAsync(string chemin)
    {
        if (Api is not { } api)
            return;
        var fichier = await api.GetAsync<FichierDto>(chemin + (chemin.Contains('?') ? "&" : "?") + "format=base64");
        var fichiers = Fichiers ?? throw new ApiException("Enregistrement de fichier indisponible sur ce poste.");
        if (await fichiers.EnregistrerAsync(fichier.NomFichier, Convert.FromBase64String(fichier.ContenuBase64)))
            InfoText = $"Fichier « {fichier.NomFichier} » enregistré.";
    }

    // ===== Temps réel =====

    private void SurMessage(RealtimeMessage message)
    {
        if (message.Type == SaphirSession.OpenAppType && message.AppId == SaphirApps.Lrppn)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (SaphirSession.TakeAppArgument(SaphirApps.Lrppn) is { } argument)
                    TraiterArgument(argument);
            });
            return;
        }
        if (message.Type != JudiciaireRealtime.LrppnChanged)
            return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (IsDossier && Dossier.Procedure is { } p && message.Text == p.Id.ToString(CultureInfo.InvariantCulture))
                _ = Dossier.ChargerAsync(p.Id);
            else if (IsListe)
                _ = ChargerListeAsync();
            else if (IsActes)
                _ = ChargerActesAsync();
            else if (IsPieces)
                _ = ChargerPiecesAsync();
            else if (IsActe && Editeur is { } e && message.Text == e.Acte.ProcedureId.ToString(CultureInfo.InvariantCulture))
                e.SignalerModificationExterne();
        });
    }

    public override void Resume() => SaphirSession.Realtime += SurMessage;

    public override void Suspend() => SaphirSession.Realtime -= SurMessage;
}

/// <summary>Présentation texte des dossiers associés d'une personne (14.3).</summary>
public static class DossiersFormat
{
    public static string Texte(DossiersAssociesDto d)
    {
        var p = d.Personne;
        var lignes = new List<string>
        {
            $"Identité : {p.Libelle} — {p.Reference} (source {p.Source})",
            $"Nationalité : {p.Nationalite ?? "—"}   Adresse : {p.Adresse ?? "—"}"
        };
        if (p.Alias.Count > 0)
            lignes.Add("Alias : " + string.Join(", ", p.Alias.Select(a => a.Alias)));
        if (p.Alertes.Count > 0)
            lignes.Add("Alertes : " + FormatsJ.Alertes(p.Alertes));
        lignes.Add(string.Empty);
        if (d.Taj is { } taj)
        {
            lignes.Add($"TAJ ({taj.Count}) — une présence au TAJ ne vaut pas condamnation :");
            lignes.AddRange(taj.Select(t => $"  • {FormatsJ.Date(t.FaitsLe)} — {t.Qualite} — {t.Faits} ({t.ProcedureNumero ?? "sans procédure"})"));
        }
        if (d.Fpr is { } fpr)
        {
            lignes.Add($"FPR ({fpr.Count}) :");
            lignes.AddRange(fpr.Select(f => $"  • {f.Numero} — {f.Statut} — {f.Categorie} — {f.Motif}"));
        }
        if (d.Casier is { } casier)
        {
            lignes.Add($"Casier de simulation ({casier.Count} décision(s) validée(s)) :");
            lignes.AddRange(casier.Select(c => $"  • {FormatsJ.Jour(c.DecisionLe)} — {c.Juridiction} — {c.Infraction} — {c.Peine ?? c.Nature}"));
        }
        lignes.Add("Mentions de main courante et interventions PÉGASE : consultables dans leurs applications.");
        if (d.NonAutorises.Count > 0)
            lignes.Add("Non accessibles avec tes habilitations : " + string.Join(", ", d.NonAutorises) + ".");
        return string.Join("\n", lignes);
    }
}

// =====================================================================================
// Formulaire de procédure (13.3)
// =====================================================================================

public sealed class ProcedureFormulaireViewModel : SousVueBase
{
    private readonly LrppnViewModel _lrppn;
    private long? _id;
    private int? _version;
    private AgentResumeDto? _responsable;
    private string? _service, _unite, _grade, _qualite, _ouverteLe, _intitule, _interventionNumero, _interventionTexte, _mainCourante,
        _qualification, _faitsLe, _commune, _adresse, _description, _origine, _motif;
    private string _cadre = ProcedureReferentiels.Cadres[0];
    private string _type = ProcedureReferentiels.TypesAffaire[0];
    private long? _interventionId;

    public ProcedureFormulaireViewModel(LrppnViewModel lrppn) : base(lrppn)
    {
        _lrppn = lrppn;
        EnregistrerCommand = new RelayCommand(() => _ = EnregistrerAsync());
        AnnulerCommand = new RelayCommand(() => _ = AnnulerAsync());
        RetirerInterventionCommand = new RelayCommand(() => { InterventionId = null; InterventionNumero = null; InterventionTexte = null; });
    }

    public bool EstModification => _id is not null;
    public string Titre => EstModification ? "Modifier la procédure" : "Nouvelle procédure";
    public ObservableCollection<AgentResumeDto> Agents { get; } = [];
    public IReadOnlyList<string> Cadres => ProcedureReferentiels.Cadres;
    public IReadOnlyList<string> TypesAffaire => ProcedureReferentiels.TypesAffaire;
    public IReadOnlyList<string> Origines => ProcedureReferentiels.Origines;
    public IReadOnlyList<string> QualitesFonctionnelles => ProcedureReferentiels.QualitesFonctionnelles;

    public AgentResumeDto? Responsable
    {
        get => _responsable;
        set
        {
            if (!SetProperty(ref _responsable, value) || value is null)
                return;
            Grade = value.Grade ?? Grade;
            Service ??= value.Service;
            Unite ??= value.Unite;
            OnPropertyChanged(nameof(ResponsableRio));
        }
    }

    public string ResponsableRio => Responsable?.Rio ?? "non attribué";
    public string? Service { get => _service; set => SetProperty(ref _service, value); }
    public string? Unite { get => _unite; set => SetProperty(ref _unite, value); }
    public string? Grade { get => _grade; set => SetProperty(ref _grade, value); }
    public string? QualiteFonctionnelle { get => _qualite; set => SetProperty(ref _qualite, value); }
    public string? OuverteLe { get => _ouverteLe; set => SetProperty(ref _ouverteLe, value); }
    public string Cadre { get => _cadre; set => SetProperty(ref _cadre, value); }
    public string TypeAffaire { get => _type; set => SetProperty(ref _type, value); }
    public string? Intitule { get => _intitule; set => SetProperty(ref _intitule, value); }
    public long? InterventionId { get => _interventionId; set { if (SetProperty(ref _interventionId, value)) OnPropertyChanged(nameof(AIntervention)); } }
    public bool AIntervention => InterventionId is not null;
    public string? InterventionNumero { get => _interventionNumero; set => SetProperty(ref _interventionNumero, value); }
    public string? InterventionTexte { get => _interventionTexte; set => SetProperty(ref _interventionTexte, value); }
    public string? MainCourante { get => _mainCourante; set => SetProperty(ref _mainCourante, value); }
    public string? Qualification { get => _qualification; set => SetProperty(ref _qualification, value); }
    public string? FaitsLe { get => _faitsLe; set => SetProperty(ref _faitsLe, value); }
    public string? Commune { get => _commune; set => SetProperty(ref _commune, value); }
    public string? Adresse { get => _adresse; set => SetProperty(ref _adresse, value); }
    public string? Description { get => _description; set => SetProperty(ref _description, value); }
    public string? Origine { get => _origine; set => SetProperty(ref _origine, value); }
    public string? Motif { get => _motif; set => SetProperty(ref _motif, value); }

    public RelayCommand EnregistrerCommand { get; }
    public RelayCommand AnnulerCommand { get; }
    public RelayCommand RetirerInterventionCommand { get; }

    private void Rafraichir()
    {
        OnPropertyChanged(nameof(EstModification));
        OnPropertyChanged(nameof(Titre));
    }

    private async Task ChargerAgentsAsync(long? responsableId)
    {
        if (Api is not { } api)
            return;
        var agents = await AgentsReferentiel.ChargerAsync(api);
        Agents.Clear();
        foreach (var a in agents)
            Agents.Add(a);
        if (responsableId is { } id && Agents.All(a => a.Id != id) && AgentsReferentiel.AgentConnecte() is { } moi && moi.Id == id)
            Agents.Insert(0, moi);
        _responsable = Agents.FirstOrDefault(a => a.Id == responsableId);
        OnPropertyChanged(nameof(Responsable));
        OnPropertyChanged(nameof(ResponsableRio));
    }

    /// <summary>Formulaire vide : agent connecté responsable, ouverture maintenant.</summary>
    public void Nouvelle()
    {
        _id = null;
        _version = null;
        var moi = SaphirSession.Agent;
        Service = moi?.Service;
        Unite = moi?.Unite;
        Grade = moi?.Grade;
        QualiteFonctionnelle = null;
        OuverteLe = FormatsJ.DateHeureSaisie(DateTimeOffset.Now);
        Cadre = ProcedureReferentiels.Cadres[0];
        TypeAffaire = ProcedureReferentiels.TypesAffaire[0];
        Intitule = Qualification = FaitsLe = Commune = Adresse = Description = MainCourante = Motif = null;
        Origine = null;
        InterventionId = null;
        InterventionNumero = InterventionTexte = null;
        Rafraichir();
        _ = ChargerAgentsAsync(moi?.Id);
    }

    public void Modifier(ProcedureDto p)
    {
        _id = p.Id;
        _version = p.Version;
        Service = p.Service;
        Unite = p.Unite;
        Grade = p.ResponsableGrade;
        QualiteFonctionnelle = p.QualiteFonctionnelle;
        OuverteLe = FormatsJ.DateHeureSaisie(p.OuverteLe);
        Cadre = p.Cadre;
        TypeAffaire = p.TypeAffaire;
        Intitule = p.Intitule;
        InterventionId = p.InterventionId;
        InterventionNumero = p.InterventionNumero;
        InterventionTexte = p.InterventionNumero is null ? null : "Intervention PÉGASE " + p.InterventionNumero;
        MainCourante = p.MainCouranteReference;
        Qualification = p.Qualification;
        FaitsLe = FormatsJ.DateHeureSaisie(p.FaitsLe);
        Commune = p.Commune;
        Adresse = p.Adresse;
        Description = p.Description;
        Origine = p.Origine;
        Motif = null;
        Rafraichir();
        _ = ChargerAgentsAsync(p.ResponsableId);
    }

    /// <summary>20.3 : référence, date, heure, adresse, motif et équipages repris de l'intervention (à vérifier par le rédacteur).</summary>
    public async Task PreremplirInterventionAsync(long interventionId)
    {
        InterventionId = interventionId;
        InterventionTexte = $"Intervention PÉGASE n° {interventionId} (chargement…)";
        Origine = "Intervention PÉGASE";
        if (Api is not { } api)
            return;
        await Executer(async () =>
        {
            try
            {
                var i = await api.GetAsync<InterventionResumeDto>($"api/pegase/interventions/{interventionId}/resume");
                InterventionNumero = i.Numero;
                InterventionTexte = $"Intervention {i.Numero} — {i.Nature} — {i.Adresse} {i.Commune} — {FormatsJ.DateHeure(i.DebutLe)} — {i.Statut}";
                FaitsLe = FormatsJ.DateHeureSaisie(i.DebutLe);
                Adresse = i.Adresse;
                Commune = i.Commune;
                Qualification ??= i.Nature;
                Intitule ??= $"{i.Nature}" + (string.IsNullOrWhiteSpace(i.Commune) ? string.Empty : $" — {i.Commune}");
                Description ??= $"Procédure ouverte depuis l'intervention PÉGASE {i.Numero} ({i.Nature})." +
                                (i.Equipages.Count > 0 ? $"\nÉquipages engagés : {string.Join(", ", i.Equipages)}." : string.Empty) +
                                "\nInformations proposées à vérifier par le rédacteur.";
            }
            catch (ApiException ex)
            {
                InterventionTexte = $"Intervention PÉGASE n° {interventionId} (détail indisponible : {ex.Message})";
            }
        }, "Procédure préremplie depuis l'intervention PÉGASE : vérifie les informations proposées.");
    }

    private async Task EnregistrerAsync()
    {
        if (Api is not { } api)
            return;
        await Executer(async () =>
        {
            var responsable = Responsable ?? throw new ArgumentException("Choisis l'agent responsable (OPJ).");
            var ouverte = FormatsJ.LireDateHeure(OuverteLe, "Date d'ouverture") ?? DateTimeOffset.Now;
            var input = new ProcedureInput(Service, Unite, responsable.Id, Grade, QualiteFonctionnelle, ouverte, Cadre, TypeAffaire, Intitule ?? string.Empty,
                InterventionId, InterventionNumero, MainCourante, Qualification, FormatsJ.LireDateHeure(FaitsLe, "Date des faits"), Commune, Adresse,
                Description, Origine, _version, Motif);
            var procedure = _id is { } id
                ? await api.SendAsync<ProcedureDto>(HttpMethod.Put, $"api/lrppn/procedures/{id}", input)
                : await api.PostAsync<ProcedureDto>("api/lrppn/procedures", input);
            await _lrppn.OuvrirDossierAsync(procedure.Id);
        }, EstModification ? "Procédure enregistrée." : "Procédure ouverte.");
    }

    private async Task AnnulerAsync()
    {
        if (_id is { } id)
            await _lrppn.OuvrirDossierAsync(id);
        else
            _lrppn.MenuSelection = _lrppn.Menu.FirstOrDefault();
    }
}
