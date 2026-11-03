using System.Collections.ObjectModel;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Rh;

namespace Saphir.ViewModels.Apps.Rh;

// =====================================================================================
// 2. Gestion du personnel (4.4)
// =====================================================================================

public sealed class RhPersonnelViewModel : RhPageViewModel
{
    private const string TousStatuts = "Tous les statuts";
    private string _recherche = string.Empty;
    private string _statut = TousStatuts;
    private RhOption? _service;
    private RhOption? _unite;
    private RhOption? _grade;
    private RhAgentRow? _selection;
    private bool _remplissage;

    public RhPersonnelViewModel(RhViewModel rh) : base(rh)
    {
        RechercherCommand = new RelayCommand(() => _ = ChargerAsync());
        ReinitialiserCommand = new RelayCommand(() =>
        {
            _remplissage = true;
            Recherche = string.Empty;
            Statut = TousStatuts;
            Service = Services.FirstOrDefault();
            Grade = Grades.FirstOrDefault();
            _remplissage = false;
            _ = ChargerAsync();
        });
        OuvrirDossierCommand = new RelayCommand(() => { if (Selection is { } s) Rh.OuvrirDossier(s.Id); });
        DocumentsCommand = new RelayCommand(() => { if (Selection is { } s) Rh.OuvrirDocuments(s.Id); });
        HistoriqueCommand = new RelayCommand(() => { if (Selection is { } s) Rh.OuvrirDossier(s.Id, 6); });
        StatutCommand = new RelayCommand(() => { if (Selection is { } s) Rh.OuvrirDossier(s.Id, 0); });
        Rh.DonneesChangees += ReconstruireFiltres;
        ReconstruireFiltres();
    }

    public override string Titre => "Gestion du personnel";
    public override string Description => "Tous les agents SAPHIR : rechercher, filtrer, ouvrir le dossier individuel, les documents ou l'historique.";

    public IReadOnlyList<string> Statuts { get; } = [TousStatuts, .. AgentStatuts.All];
    public ObservableCollection<RhOption> Services { get; } = [];
    public ObservableCollection<RhOption> Unites { get; } = [];
    public ObservableCollection<RhOption> Grades { get; } = [];
    public ObservableCollection<RhAgentRow> Agents { get; } = [];

    public string Recherche { get => _recherche; set => SetProperty(ref _recherche, value); }

    public string Statut
    {
        get => _statut;
        set
        {
            if (SetProperty(ref _statut, value) && !_remplissage)
                _ = ChargerAsync();
        }
    }

    public RhOption? Service
    {
        get => _service;
        set
        {
            if (!SetProperty(ref _service, value))
                return;
            ReconstruireUnites();
            if (!_remplissage)
                _ = ChargerAsync();
        }
    }

    public RhOption? Unite
    {
        get => _unite;
        set
        {
            if (SetProperty(ref _unite, value) && !_remplissage)
                _ = ChargerAsync();
        }
    }

    public RhOption? Grade
    {
        get => _grade;
        set
        {
            if (SetProperty(ref _grade, value) && !_remplissage)
                _ = ChargerAsync();
        }
    }

    public RhAgentRow? Selection
    {
        get => _selection;
        set
        {
            if (SetProperty(ref _selection, value))
                OnPropertyChanged(nameof(HasSelection));
        }
    }

    public bool HasSelection => Selection is not null;
    public string Compte => $"{Agents.Count} agent(s)";

    public RelayCommand RechercherCommand { get; }
    public RelayCommand ReinitialiserCommand { get; }
    public RelayCommand OuvrirDossierCommand { get; }
    public RelayCommand DocumentsCommand { get; }
    public RelayCommand HistoriqueCommand { get; }
    public RelayCommand StatutCommand { get; }

    private void ReconstruireFiltres()
    {
        _remplissage = true;
        var (s, u, g) = (Service?.Id, Unite?.Id, Grade?.Id);
        Services.Clear();
        foreach (var o in Rh.Options(RhReferentielTypes.Services, false, "Tous les services"))
            Services.Add(o);
        Grades.Clear();
        foreach (var o in Rh.Options(RhReferentielTypes.Grades, false, "Tous les grades"))
            Grades.Add(o);
        Service = Services.FirstOrDefault(o => o.Id == s) ?? Services.FirstOrDefault();
        Unite = Unites.FirstOrDefault(o => o.Id == u) ?? Unites.FirstOrDefault();
        Grade = Grades.FirstOrDefault(o => o.Id == g) ?? Grades.FirstOrDefault();
        _remplissage = false;
    }

    private void ReconstruireUnites()
    {
        var u = Unite?.Id;
        var ancien = _remplissage;
        _remplissage = true;
        Unites.Clear();
        Unites.Add(new RhOption(null, "Toutes les unités"));
        foreach (var o in Rh.Options(RhReferentielTypes.Unites, false).Where(o => Service?.Id is null || o.ParentId == Service.Id))
            Unites.Add(o);
        Unite = Unites.FirstOrDefault(o => o.Id == u) ?? Unites[0];
        _remplissage = ancien;
    }

    /// <summary>Filtre par statut (depuis le tableau de bord) ; <c>null</c> = tous.</summary>
    public async Task FiltrerStatutAsync(string? statut)
    {
        _remplissage = true;
        Statut = statut ?? TousStatuts;
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
            var requete = "api/rh/agents?q=" + ApiClient.Q(Recherche.Trim());
            if (Statut != TousStatuts) requete += "&statut=" + ApiClient.Q(Statut);
            if (Service?.Id is { } s) requete += $"&serviceId={s}";
            if (Unite?.Id is { } u) requete += $"&uniteId={u}";
            if (Grade?.Id is { } g) requete += $"&gradeId={g}";
            var agents = await api.GetAsync<List<RhAgentLigneDto>>(requete);
            var id = Selection?.Id;
            Agents.Clear();
            foreach (var a in agents)
                Agents.Add(new RhAgentRow(a));
            Selection = Agents.FirstOrDefault(a => a.Id == id);
            OnPropertyChanged(nameof(Compte));
        });
    }
}

// =====================================================================================
// 3. Dossier individuel (4.5)
// =====================================================================================

public sealed record RhCarriereRow(string Effet, string Ancien, string Nouveau, string Motif, string Observations, string Auteur, string Saisi);
public sealed record RhRioRow(string Agent, string Effet, string Ancien, string Nouveau, string Motif, string Auteur, string Saisi);
public sealed record RhIdentiteRow(string Effet, string Ancienne, string Nouvelle, string Motif, string Auteur, string Saisi);

public sealed class RhDossierViewModel : RhPageViewModel, IRhDroitsSensibles
{
    private RhOption? _agentChoisi;
    private RhDossierDto? _dossier;
    private int _onglet;
    private RhDocumentRow? _document;

    public RhDossierViewModel(RhViewModel rh) : base(rh)
    {
        Identite = new RhIdentiteFormulaire(rh) { Apres = ApresModificationAsync };
        Matricule = new RhMatriculeFormulaire(rh) { Apres = ApresModificationAsync };
        Integration = new RhIntegrationFormulaire(rh) { Apres = ApresModificationAsync };
        Rio = new RhRioFormulaire(rh) { Apres = ApresModificationAsync };
        Grade = new RhGradeFormulaire(rh) { Apres = ApresModificationAsync };
        Affectation = new RhAffectationFormulaire(rh) { Apres = ApresModificationAsync };
        Statut = new RhStatutFormulaire(rh) { Apres = ApresModificationAsync };

        ActualiserCommand = new RelayCommand(() => _ = ChargerAsync());
        DocumentsCommand = new RelayCommand(() => { if (AgentId is { } id) Rh.OuvrirDocuments(id); });
        GenererCommand = new RelayCommand(() => { if (AgentId is { } id) Rh.OuvrirModeles(id); });
        TelechargerCommand = new RelayCommand(() => { if (Document is { } d) _ = Rh.TelechargerAsync(d.Id, d.Dto.Version); });
        AdministrationCommand = new RelayCommand(Rh.OuvrirAdministration);

        Rh.DonneesChangees += ReconstruireOptions;
        ReconstruireOptions();
    }

    public override string Titre => "Dossiers individuels";
    public override string Description => "Fiche RH complète d'un agent : identité, affectation, grade, RIO, documents, habilitations et historique.";

    public ObservableCollection<RhOption> AgentsChoix { get; } = [];

    public RhOption? AgentChoisi
    {
        get => _agentChoisi;
        set
        {
            if (SetProperty(ref _agentChoisi, value) && value?.Id is { } id && id != AgentId)
                _ = OuvrirAsync(id, Onglet);
        }
    }

    public RhDossierDto? Dossier
    {
        get => _dossier;
        private set
        {
            if (!SetProperty(ref _dossier, value))
                return;
            foreach (var nom in new[] { nameof(HasDossier), nameof(Agent), nameof(Entete), nameof(DateIntegration), nameof(GradeEffet),
                         nameof(DerniereConnexion), nameof(AffectationService), nameof(AffectationUnite), nameof(AffectationFonction),
                         nameof(AffectationResponsable), nameof(AffectationDebut), nameof(RioActuel), nameof(MatriculeActuel), nameof(GradeActuel),
                         nameof(StatutActuel), nameof(MajLe) })
                OnPropertyChanged(nom);
        }
    }

    public long? AgentId => Dossier?.Agent.Id;
    public bool HasDossier => Dossier is not null;
    public RhAgentLigneDto? Agent => Dossier?.Agent;
    public string Entete => Agent is { } a ? $"{a.Libelle} — {a.Reference}" : string.Empty;
    public string DateIntegration => RhFormat.Jour(Dossier?.DateIntegration);
    public string GradeEffet => RhFormat.Jour(Dossier?.GradeEffet);
    public string DerniereConnexion => Dossier?.DerniereConnexion is { } d ? RhFormat.DateHeure(d) : "Jamais";
    public string RioActuel => RhFormat.Texte(Agent?.Rio);
    public string MatriculeActuel => RhFormat.Texte(Agent?.Matricule);
    public string GradeActuel => RhFormat.Texte(Agent?.Grade);
    public string StatutActuel => Agent?.Statut ?? string.Empty;
    public string MajLe => Agent is { } a ? RhFormat.DateHeure(a.MajLe) : "—";
    public string AffectationService => RhFormat.Texte(Dossier?.AffectationCourante?.Service ?? Agent?.Service);
    public string AffectationUnite => RhFormat.Texte(Dossier?.AffectationCourante?.Unite ?? Agent?.Unite);
    public string AffectationFonction => RhFormat.Texte(Dossier?.AffectationCourante?.Fonction ?? Agent?.Fonction);
    public string AffectationResponsable => RhFormat.Texte(Dossier?.AffectationCourante?.Responsable);
    public string AffectationDebut => RhFormat.Jour(Dossier?.AffectationCourante?.Debut);

    public int Onglet { get => _onglet; set => SetProperty(ref _onglet, value); }

    public RhIdentiteFormulaire Identite { get; }
    public RhMatriculeFormulaire Matricule { get; }
    public RhIntegrationFormulaire Integration { get; }
    public RhRioFormulaire Rio { get; }
    public RhGradeFormulaire Grade { get; }
    public RhAffectationFormulaire Affectation { get; }
    public RhStatutFormulaire Statut { get; }

    public ObservableCollection<RhAffectationRow> Affectations { get; } = [];
    public ObservableCollection<RhCarriereRow> Carriere { get; } = [];
    public ObservableCollection<RhRioRow> RioHistorique { get; } = [];
    public ObservableCollection<RhIdentiteRow> Identites { get; } = [];
    public ObservableCollection<RhDocumentRow> Documents { get; } = [];
    public ObservableCollection<RhHistoriqueLigne> Historique { get; } = [];
    public ObservableCollection<string> Profils { get; } = [];
    public ObservableCollection<RhPermissionLigne> PermissionsAgent { get; } = [];
    public ObservableCollection<string> Applications { get; } = [];
    public ObservableCollection<RhHistoriqueLigne> HistoriqueDroits { get; } = [];

    public RhDocumentRow? Document
    {
        get => _document;
        set
        {
            if (SetProperty(ref _document, value))
                OnPropertyChanged(nameof(HasDocument));
        }
    }

    public bool HasDocument => Document is not null;
    public bool PeutGenerer => RhDroits.Documents;
    public bool PeutOuvrirAdministration => Rh.PeutOuvrirAdministration;

    public RelayCommand ActualiserCommand { get; }
    public RelayCommand DocumentsCommand { get; }
    public RelayCommand GenererCommand { get; }
    public RelayCommand TelechargerCommand { get; }
    public RelayCommand AdministrationCommand { get; }

    public void RafraichirDroits()
    {
        foreach (var f in new RhFormulaire[] { Identite, Matricule, Integration, Rio, Grade, Affectation, Statut })
            f.RafraichirDroits();
        OnPropertyChanged(nameof(PeutGenerer));
        OnPropertyChanged(nameof(PeutOuvrirAdministration));
    }

    private void ReconstruireOptions()
    {
        var id = AgentChoisi?.Id;
        AgentsChoix.Clear();
        foreach (var o in Rh.OptionsAgents(avecArchives: true))
            AgentsChoix.Add(o);
        _agentChoisi = AgentsChoix.FirstOrDefault(o => o.Id == id);
        OnPropertyChanged(nameof(AgentChoisi));
        Grade.ReconstruireOptions();
        Affectation.ReconstruireOptions();
    }

    /// <summary>Ouvre le dossier d'un agent sur l'onglet demandé.</summary>
    public async Task OuvrirAsync(long agentId, int onglet = 0)
    {
        Onglet = onglet;
        if (AgentChoisi?.Id != agentId)
        {
            _agentChoisi = AgentsChoix.FirstOrDefault(o => o.Id == agentId);
            OnPropertyChanged(nameof(AgentChoisi));
        }
        await ChargerDossierAsync(agentId, reinitialiser: true);
    }

    public override Task ChargerAsync()
        => AgentId is { } id ? ChargerDossierAsync(id, reinitialiser: false) : Task.CompletedTask;

    public override Task SurTempsReelAsync(RealtimeMessage message)
        => message.Type != RhRealtimeTypes.AgentModifie || message.Text == AgentId?.ToString() ? ChargerAsync() : Task.CompletedTask;

    private async Task ApresModificationAsync()
    {
        await Rh.ChargerCommunAsync();
        await ChargerAsync();
    }

    private async Task ChargerDossierAsync(long agentId, bool reinitialiser)
    {
        var api = Api;
        if (api is null)
            return;
        await Rh.Charger(async () =>
        {
            var dossier = await api.GetAsync<RhDossierDto>($"api/rh/agents/{agentId}");
            var autre = Dossier?.Agent.Id != agentId;
            Dossier = dossier;
            Remplir(dossier);
            var re = reinitialiser || autre;
            foreach (var f in new RhFormulaire[] { Identite, Matricule, Integration, Rio, Grade, Affectation, Statut })
                f.Charger(dossier.Agent, re);
            Integration.DefinirActuelle(dossier.DateIntegration, re);
            Grade.DefinirJustificatifs(dossier.Documents);
            if (re)
                Affectation.DefinirResponsable(dossier.AffectationCourante?.ResponsableId);
        });
    }

    private void Remplir(RhDossierDto d)
    {
        Affectations.Clear();
        foreach (var a in d.Affectations)
            Affectations.Add(new RhAffectationRow(a));
        Carriere.Clear();
        foreach (var c in d.Carriere)
            Carriere.Add(new RhCarriereRow(RhFormat.Jour(c.Effet), RhFormat.Texte(c.AncienGrade), c.Grade, c.Motif ?? string.Empty,
                c.Observations ?? string.Empty, c.Auteur, RhFormat.DateHeure(c.CreeLe)));
        RioHistorique.Clear();
        foreach (var r in d.Rio)
            RioHistorique.Add(new RhRioRow(r.Agent, RhFormat.Jour(r.Effet), RhFormat.Texte(r.Ancien), r.Nouveau, r.Motif ?? string.Empty,
                r.Auteur, RhFormat.DateHeure(r.CreeLe)));
        Identites.Clear();
        foreach (var i in d.Identites)
            Identites.Add(new RhIdentiteRow(RhFormat.Jour(i.Effet), $"{i.AncienNom} {i.AncienPrenom}", $"{i.NouveauNom} {i.NouveauPrenom}",
                i.Motif ?? string.Empty, i.Auteur, RhFormat.DateHeure(i.CreeLe)));
        var docId = Document?.Id;
        Documents.Clear();
        foreach (var doc in d.Documents)
            Documents.Add(new RhDocumentRow(doc));
        Document = Documents.FirstOrDefault(x => x.Id == docId);
        Historique.Clear();
        foreach (var h in d.Historique)
            Historique.Add(RhHistoriqueLigne.Depuis(h));

        Profils.Clear();
        foreach (var p in d.Habilitations.Profils)
            Profils.Add(p);
        if (Profils.Count == 0)
            Profils.Add("Aucun profil attribué");
        PermissionsAgent.Clear();
        foreach (var code in d.Habilitations.Permissions)
        {
            var info = Permissions.All.FirstOrDefault(p => p.Code == code);
            PermissionsAgent.Add(new RhPermissionLigne(info?.Module ?? "—", info?.Libelle ?? code, code));
        }
        Applications.Clear();
        foreach (var a in d.Habilitations.Applications)
            Applications.Add(a);
        HistoriqueDroits.Clear();
        foreach (var h in d.Habilitations.Historique)
            HistoriqueDroits.Add(RhHistoriqueLigne.Depuis(h));
    }
}

// =====================================================================================
// 4-7. Référentiels : grades, services, unités, fonctions (4.8, 4.9)
// =====================================================================================

public sealed class RhReferentielRow(RhReferentielDto dto)
{
    public RhReferentielDto Dto { get; } = dto;
    public string Libelle => Dto.Libelle;
    public string Abrege => RhFormat.Texte(Dto.Abrege);
    public string Code => Dto.Code;
    public string Lien => Dto.Type switch
    {
        RhReferentielTypes.Grades => RhFormat.Texte(Dto.Categorie),
        RhReferentielTypes.Unites => RhFormat.Texte(Dto.Parent),
        RhReferentielTypes.Services => RhFormat.Texte(Dto.Responsable),
        _ => string.Empty
    };
    public int Ordre => Dto.Ordre;
    public string Etat => Dto.Actif ? "Actif" : "Inactif";
    public int Effectif => Dto.Effectif;
}

public sealed class RhReferentielViewModel : RhPageViewModel, IRhDroitsSensibles
{
    private RhReferentielRow? _selection;
    private bool _nouveau;
    private string _libelle = string.Empty;
    private string? _abrege;
    private string? _categorie;
    private RhOption? _parent;
    private RhOption? _responsable;
    private string _ordre = "10";
    private bool _actif = true;

    public RhReferentielViewModel(RhViewModel rh, string type) : base(rh)
    {
        Type = type;
        NouveauCommand = new RelayCommand(Nouveau);
        EnregistrerCommand = new RelayCommand(() => _ = EnregistrerAsync());
        PersonnelCommand = new RelayCommand(VoirPersonnel);
        Rh.DonneesChangees += () => _ = ChargerAsync();
    }

    public string Type { get; }
    public override string Titre => RhReferentielTypes.Libelle(Type);
    public override string Description => Type switch
    {
        RhReferentielTypes.Grades => "Référentiel des grades : intitulé, abréviation, catégorie, ordre hiérarchique et statut.",
        RhReferentielTypes.Services => "Services : identifiant interne stable, nom, responsable, unités et effectifs. Un service se désactive, il ne se supprime pas.",
        RhReferentielTypes.Unites => "Unités rattachées à un service (Direction, BPS / BAC, CSI, BMU, Accueil, CIC…).",
        _ => "Fonctions exercées par les agents (chef de bord, équipier, opérateur CIC, OPJ…)."
    };

    public bool EstGrades => Type == RhReferentielTypes.Grades;
    public bool EstServices => Type == RhReferentielTypes.Services;
    public bool EstUnites => Type == RhReferentielTypes.Unites;
    public string EnteteLien => Type switch
    {
        RhReferentielTypes.Grades => "Catégorie",
        RhReferentielTypes.Unites => "Service",
        RhReferentielTypes.Services => "Responsable",
        _ => "—"
    };
    public bool AfficheLien => Type != RhReferentielTypes.Fonctions;
    public bool PeutModifier => RhDroits.Referentiels;

    public ObservableCollection<RhReferentielRow> Elements { get; } = [];
    public IReadOnlyList<string> Categories => RhGradeCategories.All;
    public ObservableCollection<RhOption> Parents { get; } = [];
    public ObservableCollection<RhOption> Responsables { get; } = [];
    public ObservableCollection<string> UnitesDuService { get; } = [];

    public RhReferentielRow? Selection
    {
        get => _selection;
        set
        {
            if (!SetProperty(ref _selection, value))
                return;
            if (value is not null)
            {
                Nouveau_ = false;
                var e = value.Dto;
                Libelle = e.Libelle;
                Abrege = e.Abrege;
                Categorie = e.Categorie;
                Parent = Parents.FirstOrDefault(p => p.Id == e.ParentId);
                Responsable = Responsables.FirstOrDefault(p => p.Id == e.ResponsableId) ?? Responsables.FirstOrDefault();
                Ordre = e.Ordre.ToString(RhFormat.Francais);
                Actif = e.Actif;
            }
            RemplirUnitesDuService();
            OnPropertyChanged(nameof(EnEdition));
            OnPropertyChanged(nameof(TitreEditeur));
        }
    }

    private bool Nouveau_
    {
        get => _nouveau;
        set
        {
            if (SetProperty(ref _nouveau, value))
            {
                OnPropertyChanged(nameof(EnEdition));
                OnPropertyChanged(nameof(TitreEditeur));
            }
        }
    }

    public bool EnEdition => _nouveau || Selection is not null;
    public string TitreEditeur => _nouveau ? $"Nouveau : {RhReferentielTypes.LibelleSingulier(Type)}" : Selection?.Libelle ?? string.Empty;
    public string InfoSelection => Selection is { } s ? $"Code stable : {s.Code} — {s.Effectif} agent(s)" : "Le code interne est généré à la création et ne change jamais.";

    public string Libelle { get => _libelle; set => SetProperty(ref _libelle, value); }
    public string? Abrege { get => _abrege; set => SetProperty(ref _abrege, value); }
    public string? Categorie { get => _categorie; set => SetProperty(ref _categorie, value); }
    public RhOption? Parent { get => _parent; set => SetProperty(ref _parent, value); }
    public RhOption? Responsable { get => _responsable; set => SetProperty(ref _responsable, value); }
    public string Ordre { get => _ordre; set => SetProperty(ref _ordre, value); }
    public bool Actif { get => _actif; set => SetProperty(ref _actif, value); }

    public RelayCommand NouveauCommand { get; }
    public RelayCommand EnregistrerCommand { get; }
    public RelayCommand PersonnelCommand { get; }

    public void RafraichirDroits() => OnPropertyChanged(nameof(PeutModifier));

    public override async Task ChargerAsync()
    {
        var id = Selection?.Dto.Id;
        var (parent, responsable) = (Parent?.Id, Responsable?.Id);
        Parents.Clear();
        foreach (var o in Rh.Options(RhReferentielTypes.Services, actifsSeulement: false))
            Parents.Add(o);
        Responsables.Clear();
        foreach (var o in Rh.OptionsAgents("Aucun responsable"))
            Responsables.Add(o);
        Parent = Parents.FirstOrDefault(p => p.Id == parent);
        Responsable = Responsables.FirstOrDefault(p => p.Id == responsable) ?? Responsables.FirstOrDefault();

        Elements.Clear();
        foreach (var e in Rh.Referentiel(Type))
            Elements.Add(new RhReferentielRow(e));
        _selection = Elements.FirstOrDefault(e => e.Dto.Id == id);
        OnPropertyChanged(nameof(Selection));
        OnPropertyChanged(nameof(EnEdition));
        OnPropertyChanged(nameof(InfoSelection));
        RemplirUnitesDuService();
        await Task.CompletedTask;
    }

    private void RemplirUnitesDuService()
    {
        UnitesDuService.Clear();
        if (!EstServices || Selection is null)
            return;
        foreach (var u in Rh.Unites.Where(u => u.ParentId == Selection.Dto.Id))
            UnitesDuService.Add($"{u.Libelle} — {u.Effectif} agent(s){(u.Actif ? string.Empty : " (inactive)")}");
        if (UnitesDuService.Count == 0)
            UnitesDuService.Add("Aucune unité rattachée");
        OnPropertyChanged(nameof(InfoSelection));
    }

    private void Nouveau()
    {
        _selection = null;
        OnPropertyChanged(nameof(Selection));
        Nouveau_ = true;
        Libelle = string.Empty;
        Abrege = null;
        Categorie = EstGrades ? RhGradeCategories.EncadrementApplication : null;
        Parent = Parents.FirstOrDefault();
        Responsable = Responsables.FirstOrDefault();
        Ordre = ((Elements.Count + 1) * 10).ToString(RhFormat.Francais);
        Actif = true;
        UnitesDuService.Clear();
        OnPropertyChanged(nameof(InfoSelection));
    }

    private async Task EnregistrerAsync()
    {
        var api = Api;
        if (api is null || !EnEdition)
            return;
        if (EstUnites && Parent?.Id is null)
        {
            Rh.Erreur("Choisis le service de l'unité.");
            return;
        }
        var input = new RhReferentielInput(Libelle.Trim(), Abrege, Categorie, EstUnites ? Parent?.Id : null,
            EstServices ? Responsable?.Id : null, RhFormat.Entier(Ordre), Actif);
        var selection = Selection;
        if (selection is { Dto.Actif: true, Dto.Effectif: > 0 } && !Actif &&
            !await Rh.Confirmer("Désactivation", $"« {selection.Libelle} » concerne {selection.Dto.Effectif} agent(s). Ils le conservent, mais il ne sera plus proposé. Continuer ?"))
            return;
        await Rh.Executer(async () =>
        {
            var dto = selection is null
                ? await api.PostAsync<RhReferentielDto>($"api/rh/referentiels/{Type}", input)
                : await api.SendAsync<RhReferentielDto>(HttpMethod.Put, $"api/rh/referentiels/{Type}/{selection.Dto.Id}", input);
            Nouveau_ = false;
            await Rh.ChargerCommunAsync();
            Selection = Elements.FirstOrDefault(e => e.Dto.Id == dto.Id);
        }, "Enregistré.");
    }

    private void VoirPersonnel()
    {
        if (Selection is not { } s || Rh.AfficherPage(RhViewModel.Personnel, charger: false) is not RhPersonnelViewModel personnel)
            return;
        switch (Type)
        {
            case RhReferentielTypes.Services:
                personnel.Service = personnel.Services.FirstOrDefault(o => o.Id == s.Dto.Id);
                break;
            case RhReferentielTypes.Grades:
                personnel.Grade = personnel.Grades.FirstOrDefault(o => o.Id == s.Dto.Id);
                break;
            case RhReferentielTypes.Unites:
                personnel.Service = personnel.Services.FirstOrDefault(o => o.Id == s.Dto.ParentId);
                personnel.Unite = personnel.Unites.FirstOrDefault(o => o.Id == s.Dto.Id);
                break;
            default:
                personnel.Recherche = s.Libelle;
                break;
        }
        _ = personnel.ChargerAsync();
    }
}

// =====================================================================================
// 8. Affectations (4.10)
// =====================================================================================

public sealed class RhAffectationsViewModel : RhPageViewModel, IRhDroitsSensibles
{
    private bool _enCours = true;
    private RhAffectationRow? _selection;
    private RhOption? _agent;

    public RhAffectationsViewModel(RhViewModel rh) : base(rh)
    {
        Formulaire = new RhAffectationFormulaire(rh) { Apres = ApresAsync };
        OuvrirDossierCommand = new RelayCommand(() => { if (Formulaire.AgentId is { } id) Rh.OuvrirDossier(id, 1); });
        Rh.DonneesChangees += ReconstruireOptions;
        ReconstruireOptions();
    }

    public override string Titre => "Affectations";
    public override string Description =>
        "Affectations datées des agents (service, unité, fonction, responsable). Elles alimentent les listes d'agents des autres applications.";

    public RhAffectationFormulaire Formulaire { get; }
    public ObservableCollection<RhAffectationRow> Lignes { get; } = [];
    public ObservableCollection<RhOption> AgentsChoix { get; } = [];
    public RelayCommand OuvrirDossierCommand { get; }

    public bool EnCoursSeulement
    {
        get => _enCours;
        set
        {
            if (SetProperty(ref _enCours, value))
                _ = ChargerAsync();
        }
    }

    public RhAffectationRow? Selection
    {
        get => _selection;
        set
        {
            if (SetProperty(ref _selection, value) && value is not null)
                ChoisirAgent(value.AgentId, value.Dto.ResponsableId);
        }
    }

    public RhOption? Agent
    {
        get => _agent;
        set
        {
            if (SetProperty(ref _agent, value) && value?.Id is { } id && id != Formulaire.AgentId)
                ChoisirAgent(id, null);
        }
    }

    public string Compte => $"{Lignes.Count} affectation(s)";

    public void RafraichirDroits() => Formulaire.RafraichirDroits();

    private void ReconstruireOptions()
    {
        var id = Agent?.Id;
        AgentsChoix.Clear();
        foreach (var o in Rh.OptionsAgents())
            AgentsChoix.Add(o);
        _agent = AgentsChoix.FirstOrDefault(o => o.Id == id);
        OnPropertyChanged(nameof(Agent));
        Formulaire.ReconstruireOptions();
    }

    private void ChoisirAgent(long agentId, long? responsableId)
    {
        var agent = Rh.Agents.FirstOrDefault(a => a.Id == agentId);
        if (agent is null)
            return;
        Formulaire.Charger(agent, true);
        Formulaire.DefinirResponsable(responsableId ?? Lignes.FirstOrDefault(l => l.AgentId == agentId && l.Dto.EnCours)?.Dto.ResponsableId);
        _agent = AgentsChoix.FirstOrDefault(o => o.Id == agentId);
        OnPropertyChanged(nameof(Agent));
    }

    public override async Task ChargerAsync()
    {
        var api = Api;
        if (api is null)
            return;
        await Rh.Charger(async () =>
        {
            var lignes = await api.GetAsync<List<RhAffectationDto>>("api/rh/affectations?enCours=" + (EnCoursSeulement ? "true" : "false"));
            var id = Selection?.Dto.Id;
            Lignes.Clear();
            foreach (var l in lignes)
                Lignes.Add(new RhAffectationRow(l));
            _selection = Lignes.FirstOrDefault(l => l.Dto.Id == id);
            OnPropertyChanged(nameof(Selection));
            OnPropertyChanged(nameof(Compte));
            if (Formulaire.AgentId is { } agentId && Rh.Agents.FirstOrDefault(a => a.Id == agentId) is { } agent)
                Formulaire.Charger(agent, false);
        });
    }

    private async Task ApresAsync()
    {
        await Rh.ChargerCommunAsync();
        await ChargerAsync();
    }
}

// =====================================================================================
// 9. Gestion des RIO (4.6)
// =====================================================================================

public sealed class RhRioViewModel : RhPageViewModel, IRhDroitsSensibles
{
    private bool _sansRio;
    private string _recherche = string.Empty;
    private RhAgentRow? _selection;

    public RhRioViewModel(RhViewModel rh) : base(rh)
    {
        Formulaire = new RhRioFormulaire(rh) { Apres = ApresAsync };
        RechercherCommand = new RelayCommand(() => _ = ChargerAsync());
        OuvrirDossierCommand = new RelayCommand(() => { if (Selection is { } s) Rh.OuvrirDossier(s.Id, 3); });
    }

    public override string Titre => "Gestion des RIO";
    public override string Description =>
        "Attribution et changement des RIO (distincts de l'identifiant Discord et de l'identifiant SAPHIR). Les anciens RIO restent dans l'historique.";

    public RhRioFormulaire Formulaire { get; }
    public ObservableCollection<RhAgentRow> Agents { get; } = [];
    public ObservableCollection<RhRioRow> Historique { get; } = [];
    public RelayCommand RechercherCommand { get; }
    public RelayCommand OuvrirDossierCommand { get; }

    public bool SansRioSeulement
    {
        get => _sansRio;
        set
        {
            if (SetProperty(ref _sansRio, value))
                _ = ChargerAsync();
        }
    }

    public string Recherche { get => _recherche; set => SetProperty(ref _recherche, value); }

    public RhAgentRow? Selection
    {
        get => _selection;
        set
        {
            if (!SetProperty(ref _selection, value))
                return;
            if (value is null)
                Formulaire.Vider();
            else
                Formulaire.Charger(value.Dto, false);
        }
    }

    public string Compte => $"{Agents.Count} agent(s)";

    public void RafraichirDroits() => Formulaire.RafraichirDroits();

    public async Task AfficherSansRioAsync()
    {
        _sansRio = true;
        OnPropertyChanged(nameof(SansRioSeulement));
        await ChargerAsync();
    }

    public override async Task ChargerAsync()
    {
        var api = Api;
        if (api is null)
            return;
        await Rh.Charger(async () =>
        {
            var agents = await api.GetAsync<List<RhAgentLigneDto>>("api/rh/agents?q=" + ApiClient.Q(Recherche.Trim()));
            var id = Selection?.Id;
            Agents.Clear();
            foreach (var a in agents.Where(a => a.Statut != AgentStatuts.Archive && (!SansRioSeulement || string.IsNullOrWhiteSpace(a.Rio))))
                Agents.Add(new RhAgentRow(a));
            _selection = Agents.FirstOrDefault(a => a.Id == id);
            OnPropertyChanged(nameof(Selection));
            if (_selection is not null)
                Formulaire.Charger(_selection.Dto, false);
            OnPropertyChanged(nameof(Compte));

            var historique = await api.GetAsync<List<RhRioHistoriqueDto>>("api/rh/rio/historique?limit=300");
            Historique.Clear();
            foreach (var r in historique)
                Historique.Add(new RhRioRow(r.Agent, RhFormat.Jour(r.Effet), RhFormat.Texte(r.Ancien), r.Nouveau, r.Motif ?? string.Empty,
                    r.Auteur, RhFormat.DateHeure(r.CreeLe)));
        });
    }

    private async Task ApresAsync()
    {
        await Rh.ChargerCommunAsync();
        await ChargerAsync();
    }
}
