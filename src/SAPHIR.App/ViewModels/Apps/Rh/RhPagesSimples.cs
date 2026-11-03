using System.Collections.ObjectModel;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Rh;

namespace Saphir.ViewModels.Apps.Rh;

// =====================================================================================
// 1. Tableau de bord (4.3)
// =====================================================================================

public sealed class RhTableauBordViewModel : RhPageViewModel
{
    private const double LargeurBarre = 180;
    private RhTableauBordDto? _donnees;

    public RhTableauBordViewModel(RhViewModel rh) : base(rh)
    {
        VoirProfilsACompleterCommand = new RelayCommand(() => VoirPersonnel(AgentStatuts.ProfilACompleter));
        VoirSuspendusCommand = new RelayCommand(() => VoirPersonnel(AgentStatuts.Suspendu));
        VoirActifsCommand = new RelayCommand(() => VoirPersonnel(AgentStatuts.Actif));
        VoirTousCommand = new RelayCommand(() => VoirPersonnel(null));
        VoirSansRioCommand = new RelayCommand(() =>
        {
            if (Rh.AfficherPage(RhViewModel.Rio, charger: false) is RhRioViewModel rio)
                _ = rio.AfficherSansRioAsync();
        });
        VoirAValiderCommand = new RelayCommand(() => Rh.AfficherPage(RhViewModel.AValider));
        OuvrirAgentCommand = new RelayCommand(p =>
        {
            if (p is RhAgentRow row)
                Rh.OuvrirDossier(row.Id);
        });
    }

    public override string Titre => "Tableau de bord";
    public override string Description => "Effectifs et activité du service RH, calculés en direct depuis la base SAPHIR.";

    public int Total => _donnees?.Total ?? 0;
    public int Actifs => _donnees?.Actifs ?? 0;
    public int Suspendus => _donnees?.Suspendus ?? 0;
    public int ProfilsACompleter => _donnees?.ProfilsACompleter ?? 0;
    public int SansRio => _donnees?.SansRio ?? 0;
    public int DocumentsAValider => _donnees?.DocumentsAValider ?? 0;
    public int Archives => _donnees?.Archives ?? 0;
    public int Inactifs => _donnees?.Inactifs ?? 0;

    public ObservableCollection<RhBarre> ParStatut { get; } = [];
    public ObservableCollection<RhBarre> ParGrade { get; } = [];
    public ObservableCollection<RhBarre> ParService { get; } = [];
    public ObservableCollection<RhBarre> ParUnite { get; } = [];
    public ObservableCollection<RhHistoriqueLigne> Modifications { get; } = [];
    public ObservableCollection<RhAgentRow> Creations { get; } = [];

    public RelayCommand VoirProfilsACompleterCommand { get; }
    public RelayCommand VoirSuspendusCommand { get; }
    public RelayCommand VoirActifsCommand { get; }
    public RelayCommand VoirTousCommand { get; }
    public RelayCommand VoirSansRioCommand { get; }
    public RelayCommand VoirAValiderCommand { get; }
    public RelayCommand OuvrirAgentCommand { get; }

    public override async Task ChargerAsync()
    {
        var api = Api;
        if (api is null)
            return;
        await Rh.Charger(async () =>
        {
            _donnees = await api.GetAsync<RhTableauBordDto>("api/rh/tableau-de-bord");
            Remplir(ParStatut, _donnees.ParStatut);
            Remplir(ParGrade, _donnees.ParGrade);
            Remplir(ParService, _donnees.ParService);
            Remplir(ParUnite, _donnees.ParUnite);
            Modifications.Clear();
            foreach (var h in _donnees.DernieresModifications)
                Modifications.Add(RhHistoriqueLigne.Depuis(h));
            Creations.Clear();
            foreach (var a in _donnees.DernieresCreations)
                Creations.Add(new RhAgentRow(a));
            foreach (var nom in new[] { nameof(Total), nameof(Actifs), nameof(Suspendus), nameof(ProfilsACompleter), nameof(SansRio),
                         nameof(DocumentsAValider), nameof(Archives), nameof(Inactifs) })
                OnPropertyChanged(nom);
        });
    }

    private static void Remplir(ObservableCollection<RhBarre> cible, IReadOnlyList<RhCompteurDto> compteurs)
    {
        cible.Clear();
        var max = Math.Max(1, compteurs.Count == 0 ? 1 : compteurs.Max(c => c.Nombre));
        foreach (var c in compteurs)
            cible.Add(new RhBarre(c.Libelle, c.Nombre, Math.Max(c.Nombre > 0 ? 3 : 0, LargeurBarre * c.Nombre / max)));
    }

    private void VoirPersonnel(string? statut)
    {
        if (Rh.AfficherPage(RhViewModel.Personnel, charger: false) is RhPersonnelViewModel personnel)
            _ = personnel.FiltrerStatutAsync(statut);
    }
}

// =====================================================================================
// 13. Habilitations (lecture ; modification dans Administration)
// =====================================================================================

public sealed record RhPermissionLigne(string Module, string Libelle, string Code);

public sealed class RhHabilitationsViewModel : RhPageViewModel
{
    private RhOption? _agent;
    private bool _charge;

    public RhHabilitationsViewModel(RhViewModel rh) : base(rh)
    {
        Rh.DonneesChangees += ReconstruireAgents;
        ReconstruireAgents();
        OuvrirDossierCommand = new RelayCommand(() =>
        {
            if (Agent?.Id is { } id)
                Rh.OuvrirDossier(id, 5);
        });
    }

    public override string Titre => "Habilitations";
    public override string Description =>
        "Profils et permissions internes d'un agent (lecture seule). Les profils s'attribuent dans l'application Administration.";

    public ObservableCollection<RhOption> AgentsChoix { get; } = [];

    public RhOption? Agent
    {
        get => _agent;
        set
        {
            if (SetProperty(ref _agent, value) && value?.Id is { } id)
                _ = OuvrirAsync(id);
        }
    }

    public bool Charge { get => _charge; private set => SetProperty(ref _charge, value); }
    public ObservableCollection<string> Profils { get; } = [];
    public ObservableCollection<RhPermissionLigne> PermissionsAgent { get; } = [];
    public ObservableCollection<string> Applications { get; } = [];
    public ObservableCollection<RhHistoriqueLigne> HistoriqueDroits { get; } = [];
    public RelayCommand OuvrirDossierCommand { get; }

    public string RolesDiscord =>
        "Rôle Discord SAPHIR (WL) : obligatoire, vérifié par le serveur à chaque connexion. " +
        "Rôle Discord RH : vérifié par le serveur à chaque appel du module RH ; sans lui, ce module reste fermé.";

    private void ReconstruireAgents()
    {
        var id = Agent?.Id;
        AgentsChoix.Clear();
        foreach (var o in Rh.OptionsAgents(avecArchives: true))
            AgentsChoix.Add(o);
        _agent = AgentsChoix.FirstOrDefault(o => o.Id == id);
        OnPropertyChanged(nameof(Agent));
    }

    public async Task OuvrirAsync(long agentId)
    {
        if (Agent?.Id != agentId)
        {
            _agent = AgentsChoix.FirstOrDefault(o => o.Id == agentId);
            OnPropertyChanged(nameof(Agent));
        }
        var api = Api;
        if (api is null)
            return;
        await Rh.Charger(async () =>
        {
            var dossier = await api.GetAsync<RhDossierDto>($"api/rh/agents/{agentId}");
            Remplir(dossier.Habilitations);
            Charge = true;
        });
    }

    private void Remplir(RhHabilitationsDto h)
    {
        Profils.Clear();
        foreach (var p in h.Profils)
            Profils.Add(p);
        if (Profils.Count == 0)
            Profils.Add("Aucun profil attribué");
        PermissionsAgent.Clear();
        foreach (var code in h.Permissions)
        {
            var info = Permissions.All.FirstOrDefault(p => p.Code == code);
            PermissionsAgent.Add(new RhPermissionLigne(info?.Module ?? "—", info?.Libelle ?? code, code));
        }
        Applications.Clear();
        foreach (var a in h.Applications)
            Applications.Add(a);
        HistoriqueDroits.Clear();
        foreach (var l in h.Historique)
            HistoriqueDroits.Add(RhHistoriqueLigne.Depuis(l));
    }

    public override Task ChargerAsync() => Agent?.Id is { } id ? OuvrirAsync(id) : Task.CompletedTask;

    public override Task SurTempsReelAsync(RealtimeMessage message)
        => message.Type == RhRealtimeTypes.AgentModifie && Agent?.Id?.ToString() == message.Text ? ChargerAsync() : Task.CompletedTask;
}

// =====================================================================================
// 14. Historique
// =====================================================================================

public sealed class RhHistoriqueViewModel : RhPageViewModel
{
    private static readonly IReadOnlyList<(string Code, string Libelle)> ObjetsConnus =
    [
        (string.Empty, "Tous les objets"), ("agent", "Agents"), ("rh-grade", "Grades"), ("rh-service", "Services"),
        ("rh-unite", "Unités"), ("rh-fonction", "Fonctions"), ("rh-document", "Documents"), ("rh-dossier", "Dossiers"),
        ("rh-categorie", "Catégories"), ("rh-modele", "Modèles")
    ];

    private string _objet = ObjetsConnus[0].Libelle;
    private string? _reference;

    public RhHistoriqueViewModel(RhViewModel rh) : base(rh)
        => FiltrerCommand = new RelayCommand(() => _ = ChargerAsync());

    public override string Titre => "Historique";
    public override string Description => "Toutes les modifications RH : auteur, date, ancienne et nouvelle valeur, motif.";

    public IReadOnlyList<string> Objets { get; } = ObjetsConnus.Select(o => o.Libelle).ToList();
    public string Objet { get => _objet; set => SetProperty(ref _objet, value); }
    public string? Reference { get => _reference; set => SetProperty(ref _reference, value); }
    public ObservableCollection<RhHistoriqueLigne> Lignes { get; } = [];
    public RelayCommand FiltrerCommand { get; }
    public string Compte => $"{Lignes.Count} modification(s)";

    public override async Task ChargerAsync()
    {
        var api = Api;
        if (api is null)
            return;
        await Rh.Charger(async () =>
        {
            var code = ObjetsConnus.FirstOrDefault(o => o.Libelle == Objet).Code ?? string.Empty;
            var requete = "api/rh/historique?limit=500";
            if (code.Length > 0) requete += "&objet=" + ApiClient.Q(code);
            if (!string.IsNullOrWhiteSpace(Reference)) requete += "&reference=" + ApiClient.Q(Reference.Trim());
            var lignes = await api.GetAsync<List<HistoriqueDto>>(requete);
            Lignes.Clear();
            foreach (var h in lignes)
                Lignes.Add(RhHistoriqueLigne.Depuis(h));
            OnPropertyChanged(nameof(Compte));
        });
    }
}

// =====================================================================================
// 15. Archives : agents archivés et documents archivés
// =====================================================================================

public sealed class RhArchivesViewModel : RhPageViewModel, IRhDroitsSensibles
{
    private RhAgentRow? _agent;
    private RhDocumentRow? _document;

    public RhArchivesViewModel(RhViewModel rh) : base(rh)
    {
        Statut = new RhStatutFormulaire(rh) { Apres = ApresAsync };
        RestaurerCommand = new RelayCommand(() => _ = RestaurerAsync());
        TelechargerCommand = new RelayCommand(() =>
        {
            if (Document is { } d)
                _ = Rh.TelechargerAsync(d.Id, d.Dto.Version);
        });
        OuvrirDossierCommand = new RelayCommand(() =>
        {
            if (Agent is { } a)
                Rh.OuvrirDossier(a.Id, 6);
        });
    }

    public override string Titre => "Archives";
    public override string Description =>
        "Agents et documents archivés : rien n'est supprimé, tout reste consultable et peut être restauré selon les droits.";

    public ObservableCollection<RhAgentRow> AgentsArchives { get; } = [];
    public ObservableCollection<RhDocumentRow> DocumentsArchives { get; } = [];
    public RhStatutFormulaire Statut { get; }

    public RhAgentRow? Agent
    {
        get => _agent;
        set
        {
            if (!SetProperty(ref _agent, value))
                return;
            if (value is null)
                Statut.Vider();
            else
            {
                Statut.Charger(value.Dto, true);
                Statut.Statut = AgentStatuts.ProfilACompleter;
            }
        }
    }

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
    public bool PeutRestaurer => RhDroits.Documents;
    public bool VoitAgents => RhDroits.Consulter;
    public bool VoitDocuments => RhDroits.LireDocuments;
    public RelayCommand RestaurerCommand { get; }
    public RelayCommand TelechargerCommand { get; }
    public RelayCommand OuvrirDossierCommand { get; }

    public void RafraichirDroits()
    {
        Statut.RafraichirDroits();
        foreach (var nom in new[] { nameof(PeutRestaurer), nameof(VoitAgents), nameof(VoitDocuments) })
            OnPropertyChanged(nom);
    }

    public override async Task ChargerAsync()
    {
        var api = Api;
        if (api is null)
            return;
        await Rh.Charger(async () =>
        {
            var agentId = Agent?.Id;
            var documentId = Document?.Id;
            AgentsArchives.Clear();
            if (RhDroits.Consulter)
                foreach (var a in await api.GetAsync<List<RhAgentLigneDto>>("api/rh/agents?statut=" + ApiClient.Q(AgentStatuts.Archive)))
                    AgentsArchives.Add(new RhAgentRow(a));
            DocumentsArchives.Clear();
            if (RhDroits.LireDocuments)
                foreach (var d in await api.GetAsync<List<RhDocumentDto>>("api/rh/documents?archives=true"))
                    DocumentsArchives.Add(new RhDocumentRow(d));
            Agent = AgentsArchives.FirstOrDefault(a => a.Id == agentId);
            Document = DocumentsArchives.FirstOrDefault(d => d.Id == documentId);
        });
    }

    private async Task ApresAsync()
    {
        await Rh.ChargerCommunAsync();
        await ChargerAsync();
    }

    private async Task RestaurerAsync()
    {
        var api = Api;
        if (api is null || Document is not { } d)
            return;
        if (!await Rh.Confirmer("Restauration", $"Restaurer le document « {d.Titre} » ({d.Reference}) ?"))
            return;
        await Rh.Executer(async () =>
        {
            await api.SendAsync(HttpMethod.Post, $"api/rh/documents/{d.Id}/restaurer");
            await ChargerAsync();
        }, "Document restauré.");
    }
}

// =====================================================================================
// 16. Paramètres RH : format du RIO (lecture), fichiers acceptés, catégories documentaires
// =====================================================================================

public sealed class RhCategorieRow(RhCategorieDto dto)
{
    public RhCategorieDto Dto { get; } = dto;
    public string Libelle => Dto.Libelle;
    public string Groupe => Dto.Groupe;
    public string Code => Dto.Code;
    public int Ordre => Dto.Ordre;
    public string Etat => Dto.Actif ? "Active" : "Inactive";
}

public sealed class RhParametresViewModel : RhPageViewModel, IRhDroitsSensibles
{
    private RhCategorieRow? _selection;
    private bool _nouvelle;
    private string _libelle = string.Empty;
    private string _groupe = RhCategorieGroupes.Personnel;
    private string _ordre = "100";
    private bool _actif = true;

    public RhParametresViewModel(RhViewModel rh) : base(rh)
    {
        NouvelleCommand = new RelayCommand(() =>
        {
            Selection = null;
            Nouvelle = true;
            Libelle = string.Empty;
            Groupe = RhCategorieGroupes.Personnel;
            Ordre = ((Rh.Categories.Count + 1) * 10).ToString(RhFormat.Francais);
            Actif = true;
        });
        EnregistrerCommand = new RelayCommand(() => _ = EnregistrerAsync());
    }

    public override string Titre => "Paramètres RH";
    public override string Description => "Règles d'attribution des RIO, fichiers acceptés et catégories documentaires personnalisables.";

    public string RioFormat => Rh.ParametresRh?.RioFormat ?? "—";
    public string RioExemple => Rh.ParametresRh?.RioExemple ?? "—";
    public string Organisation => Rh.ParametresRh?.Organisation ?? "—";
    public string Fichiers => Rh.ParametresRh is { } p
        ? $"{string.Join(", ", p.Extensions)} — {RhFormat.Taille(p.TailleMaxFichier)} au plus par fichier"
        : "—";
    public bool PeutModifierFormat => RhDroits.AdminParametres && Rh.PeutOuvrirAdministration;

    public ObservableCollection<RhCategorieRow> Categories { get; } = [];
    public IReadOnlyList<string> Groupes => RhCategorieGroupes.All;
    public bool PeutGerer => RhDroits.Documents;

    public RhCategorieRow? Selection
    {
        get => _selection;
        set
        {
            if (!SetProperty(ref _selection, value) || value is null)
                return;
            Nouvelle = false;
            Libelle = value.Dto.Libelle;
            Groupe = value.Dto.Groupe;
            Ordre = value.Dto.Ordre.ToString(RhFormat.Francais);
            Actif = value.Dto.Actif;
        }
    }

    public bool Nouvelle
    {
        get => _nouvelle;
        private set
        {
            if (SetProperty(ref _nouvelle, value))
                OnPropertyChanged(nameof(EnEdition));
        }
    }

    public bool EnEdition => Nouvelle || Selection is not null;
    public string Libelle { get => _libelle; set => SetProperty(ref _libelle, value); }
    public string Groupe { get => _groupe; set => SetProperty(ref _groupe, value); }
    public string Ordre { get => _ordre; set => SetProperty(ref _ordre, value); }
    public bool Actif { get => _actif; set => SetProperty(ref _actif, value); }
    public RelayCommand NouvelleCommand { get; }
    public RelayCommand EnregistrerCommand { get; }

    public void RafraichirDroits()
    {
        OnPropertyChanged(nameof(PeutGerer));
        OnPropertyChanged(nameof(PeutModifierFormat));
    }

    public override async Task ChargerAsync()
    {
        foreach (var nom in new[] { nameof(RioFormat), nameof(RioExemple), nameof(Organisation), nameof(Fichiers), nameof(PeutModifierFormat) })
            OnPropertyChanged(nom);
        var id = Selection?.Dto.Id;
        Categories.Clear();
        foreach (var c in Rh.Categories)
            Categories.Add(new RhCategorieRow(c));
        _selection = Categories.FirstOrDefault(c => c.Dto.Id == id);
        OnPropertyChanged(nameof(Selection));
        OnPropertyChanged(nameof(EnEdition));
        await Task.CompletedTask;
    }

    private async Task EnregistrerAsync()
    {
        var api = Api;
        if (api is null || !EnEdition)
            return;
        var input = new RhCategorieInput(Libelle.Trim(), Groupe, RhFormat.Entier(Ordre), Actif);
        var selection = Selection;
        await Rh.Executer(async () =>
        {
            var dto = selection is null
                ? await api.PostAsync<RhCategorieDto>("api/rh/categories", input)
                : await api.SendAsync<RhCategorieDto>(HttpMethod.Put, $"api/rh/categories/{selection.Dto.Id}", input);
            await Rh.ChargerCommunAsync();
            Nouvelle = false;
            await ChargerAsync();
            Selection = Categories.FirstOrDefault(c => c.Dto.Id == dto.Id);
        }, "Catégorie enregistrée.");
    }
}
