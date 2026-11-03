using System.Collections.ObjectModel;
using System.Globalization;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Judiciaire;

namespace Saphir.ViewModels.Apps.Judiciaire;

/// <summary>Enregistrement et choix de fichiers sur le poste (fourni par la vue : TopLevel.StorageProvider).</summary>
public interface IFichiersPoste
{
    /// <summary>Propose un emplacement et écrit le fichier ; faux si l'agent annule.</summary>
    Task<bool> EnregistrerAsync(string nomSuggere, byte[] contenu);

    /// <summary>Choix d'un fichier à joindre ; null si l'agent annule.</summary>
    Task<(string Nom, byte[] Contenu)?> ChoisirAsync();
}

/// <summary>Services de l'application hôte (CHEOPS ou LRPPN) utilisés par ses sous-écrans.</summary>
public interface IHoteJudiciaire
{
    Task ExecuterAsync(Func<Task> travail, string? succes = null);
    Task<bool> ConfirmerAsync(string titre, string message, string libelle, bool danger = false);
    IFichiersPoste? Fichiers { get; }
}

/// <summary>Sous-écran d'une application judiciaire : accès à l'API et aux services de l'hôte.</summary>
public abstract class SousVueBase(IHoteJudiciaire hote) : ViewModelBase
{
    protected IHoteJudiciaire Hote { get; } = hote;
    protected static ApiClient? Api => SaphirSession.Api;
    protected Task Executer(Func<Task> travail, string? succes = null) => Hote.ExecuterAsync(travail, succes);

    /// <summary>Télécharge un fichier (réponse base64) et propose de l'enregistrer.</summary>
    protected async Task TelechargerAsync(string chemin)
    {
        var api = Api;
        if (api is null)
            return;
        var fichier = await api.GetAsync<FichierDto>(chemin + (chemin.Contains('?') ? "&" : "?") + "format=base64");
        var fichiers = Hote.Fichiers ?? throw new ApiException("Enregistrement de fichier indisponible sur ce poste.");
        await fichiers.EnregistrerAsync(fichier.NomFichier, Convert.FromBase64String(fichier.ContenuBase64));
    }
}

/// <summary>Entrée d'un menu latéral.</summary>
public sealed record MenuEntree(string Code, string Libelle, string Description);

/// <summary>Formats français des dates (poste) et lecture des saisies.</summary>
public static class FormatsJ
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly string[] FormatsJour = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yyyy"];
    private static readonly string[] FormatsDateHeure = ["dd/MM/yyyy HH:mm", "d/M/yyyy H:mm", "dd/MM/yyyy H'h'mm", "dd/MM/yyyy"];

    public static string Date(DateTimeOffset? d) => d?.ToLocalTime().ToString("dd/MM/yyyy", Fr) ?? "—";
    public static string DateHeure(DateTimeOffset? d) => d?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Fr) ?? "—";
    public static string Jour(DateOnly? d) => d?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "—";
    public static string? JourSaisie(DateOnly? d) => d?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    public static string? DateHeureSaisie(DateTimeOffset? d) => d?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Date « jj/mm/aaaa » ; null si vide ; <see cref="ArgumentException"/> si illisible.</summary>
    public static DateOnly? LireJour(string? texte, string champ = "Date")
    {
        if (string.IsNullOrWhiteSpace(texte))
            return null;
        if (DateOnly.TryParseExact(texte.Trim(), FormatsJour, CultureInfo.InvariantCulture, DateTimeStyles.None, out var jour))
            return jour;
        throw new ArgumentException($"{champ} : date attendue au format jj/mm/aaaa.");
    }

    /// <summary>Date et heure locales « jj/mm/aaaa hh:mm » ; null si vide.</summary>
    public static DateTimeOffset? LireDateHeure(string? texte, string champ = "Date")
    {
        if (string.IsNullOrWhiteSpace(texte))
            return null;
        if (DateTime.TryParseExact(texte.Trim(), FormatsDateHeure, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date))
            return new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date));
        throw new ArgumentException($"{champ} : date attendue au format jj/mm/aaaa hh:mm.");
    }

    public static string Alertes(IReadOnlyList<string>? alertes) => alertes is { Count: > 0 } ? string.Join(" · ", alertes) : string.Empty;

    public static string Taille(long octets) => octets switch
    {
        <= 0 => "—",
        < 1024 => $"{octets} o",
        < 1024 * 1024 => $"{octets / 1024.0:0.#} Ko",
        _ => $"{octets / 1024.0 / 1024.0:0.#} Mo"
    };

    public static string Historique(HistoriqueDto h)
    {
        var detail = h.Champ is null ? h.Nouvelle ?? string.Empty : $"{h.Champ} : {h.Ancienne ?? "—"} → {h.Nouvelle ?? "—"}";
        return detail;
    }
}

/// <summary>Ligne d'historique affichée dans un tableau.</summary>
public sealed record HistoriqueLigne(string Date, string Auteur, string Action, string Detail, string Motif, string Reference)
{
    public static HistoriqueLigne De(HistoriqueDto h)
        => new(FormatsJ.DateHeure(h.Date), h.Auteur, h.Action, FormatsJ.Historique(h), h.Motif ?? string.Empty, h.Reference);
}

/// <summary>
/// Recherche et sélection d'une identité (14.2) avec création d'une fiche si la personne est inconnue (POST /api/personnes).
/// </summary>
public sealed class PersonnePickerViewModel : SousVueBase
{
    private readonly Action<PersonneResumeDto?>? _surSelection;
    private string _recherche = string.Empty;
    private PersonneResumeDto? _selection;
    private bool _modeCreation;
    private string _nom = string.Empty;
    private string _prenom = string.Empty;
    private string? _naissance;
    private string? _lieu;
    private string? _sexe;
    private string? _nationalite = "Française";

    public PersonnePickerViewModel(IHoteJudiciaire hote, Action<PersonneResumeDto?>? surSelection = null) : base(hote)
    {
        _surSelection = surSelection;
        RechercherCommand = new RelayCommand(() => _ = RechercherAsync());
        BasculerCreationCommand = new RelayCommand(() => ModeCreation = !ModeCreation);
        CreerCommand = new RelayCommand(() => _ = CreerAsync());
        EffacerCommand = new RelayCommand(() => Selection = null);
    }

    public ObservableCollection<PersonneResumeDto> Resultats { get; } = [];
    public IReadOnlyList<string> Sexes { get; } = ["M", "F", "X"];

    public string Recherche { get => _recherche; set => SetProperty(ref _recherche, value); }

    public PersonneResumeDto? Selection
    {
        get => _selection;
        set
        {
            if (!SetProperty(ref _selection, value))
                return;
            OnPropertyChanged(nameof(SelectionLibelle));
            OnPropertyChanged(nameof(ASelection));
            _surSelection?.Invoke(value);
        }
    }

    public bool ASelection => Selection is not null;

    public string SelectionLibelle => Selection is { } p
        ? $"{p.Libelle} — {p.Reference}" + (p.Alertes.Count > 0 ? "   [" + FormatsJ.Alertes(p.Alertes) + "]" : string.Empty)
        : "Aucune identité sélectionnée.";

    public bool ModeCreation { get => _modeCreation; set => SetProperty(ref _modeCreation, value); }
    public string Nom { get => _nom; set => SetProperty(ref _nom, value); }
    public string Prenom { get => _prenom; set => SetProperty(ref _prenom, value); }
    public string? Naissance { get => _naissance; set => SetProperty(ref _naissance, value); }
    public string? Lieu { get => _lieu; set => SetProperty(ref _lieu, value); }
    public string? Sexe { get => _sexe; set => SetProperty(ref _sexe, value); }
    public string? Nationalite { get => _nationalite; set => SetProperty(ref _nationalite, value); }

    public RelayCommand RechercherCommand { get; }
    public RelayCommand BasculerCreationCommand { get; }
    public RelayCommand CreerCommand { get; }
    public RelayCommand EffacerCommand { get; }

    /// <summary>Sélection imposée (identité déjà connue de l'acte ou de la procédure).</summary>
    public void Definir(PersonneResumeDto? personne)
    {
        if (personne is not null && Resultats.All(p => p.Id != personne.Id))
            Resultats.Insert(0, personne);
        _selection = personne;
        OnPropertyChanged(nameof(Selection));
        OnPropertyChanged(nameof(SelectionLibelle));
        OnPropertyChanged(nameof(ASelection));
    }

    private async Task RechercherAsync()
    {
        var api = Api;
        if (api is null || string.IsNullOrWhiteSpace(Recherche))
            return;
        await Executer(async () =>
        {
            var liste = await api.GetAsync<List<PersonneResumeDto>>("api/personnes/recherche?q=" + ApiClient.Q(Recherche.Trim()));
            Resultats.Clear();
            foreach (var p in liste)
                Resultats.Add(p);
            if (liste.Count == 0)
                throw new ApiException("Aucune identité trouvée. Vérifie l'orthographe ou crée la fiche.");
        });
    }

    private async Task CreerAsync()
    {
        var api = Api;
        if (api is null)
            return;
        await Executer(async () =>
        {
            var personne = await api.PostAsync<PersonneResumeDto>("api/personnes",
                new PersonneCreationInput(Nom, Prenom, FormatsJ.LireJour(Naissance, "Date de naissance"), Lieu, Sexe, Nationalite));
            Resultats.Insert(0, personne);
            Selection = personne;
            ModeCreation = false;
            Nom = Prenom = string.Empty;
            Naissance = Lieu = null;
        }, "Identité créée.");
    }
}

/// <summary>Recherche et sélection d'un véhicule (route partagée /api/vehicules/recherche).</summary>
public sealed class VehiculePickerViewModel : SousVueBase
{
    private readonly Action<VehiculeResumeDto?>? _surSelection;
    private string _recherche = string.Empty;
    private VehiculeResumeDto? _selection;

    public VehiculePickerViewModel(IHoteJudiciaire hote, Action<VehiculeResumeDto?>? surSelection = null) : base(hote)
    {
        _surSelection = surSelection;
        RechercherCommand = new RelayCommand(() => _ = RechercherAsync());
    }

    public ObservableCollection<VehiculeResumeDto> Resultats { get; } = [];
    public string Recherche { get => _recherche; set => SetProperty(ref _recherche, value); }

    public VehiculeResumeDto? Selection
    {
        get => _selection;
        set
        {
            if (!SetProperty(ref _selection, value))
                return;
            OnPropertyChanged(nameof(SelectionLibelle));
            _surSelection?.Invoke(value);
        }
    }

    public string SelectionLibelle => Selection is { } v
        ? $"{v.Immatriculation} — {v.Marque} {v.Modele}" + (v.Titulaire is null ? string.Empty : $" — {v.Titulaire}")
        : "Aucun véhicule sélectionné.";

    public RelayCommand RechercherCommand { get; }

    private async Task RechercherAsync()
    {
        var api = Api;
        if (api is null || string.IsNullOrWhiteSpace(Recherche))
            return;
        await Executer(async () =>
        {
            var liste = await api.GetAsync<List<VehiculeResumeDto>>("api/vehicules/recherche?q=" + ApiClient.Q(Recherche.Trim()));
            Resultats.Clear();
            foreach (var v in liste)
                Resultats.Add(v);
            if (liste.Count == 0)
                throw new ApiException("Aucun véhicule trouvé.");
        });
    }
}

/// <summary>Liste des agents (référentiel RH, GET /api/referentiels/agents), avec repli sur l'agent connecté.</summary>
public static class AgentsReferentiel
{
    public static AgentResumeDto? AgentConnecte()
        => SaphirSession.Agent is { } a
            ? new AgentResumeDto(a.Id, a.Reference, a.Nom, a.Prenom, a.Rio, a.Grade, null, a.Service, a.Unite, a.Fonction, a.Statut)
            : null;

    public static async Task<List<AgentResumeDto>> ChargerAsync(ApiClient api, string? q = null)
    {
        try
        {
            return await api.GetAsync<List<AgentResumeDto>>("api/referentiels/agents" + (string.IsNullOrWhiteSpace(q) ? string.Empty : "?q=" + ApiClient.Q(q.Trim())));
        }
        catch (ApiException)
        {
            // Module RH indisponible : l'agent connecté reste proposé.
            var liste = new List<AgentResumeDto>();
            if (AgentConnecte() is { } moi)
                liste.Add(moi);
            return liste;
        }
    }
}
