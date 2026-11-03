using System.Collections.ObjectModel;
using System.Globalization;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Complements;

namespace Saphir.ViewModels.Apps.Complements;

/// <summary>Fichier choisi sur le poste (ouverture différée du flux : utile pour les vidéos de 200 Mo).</summary>
public sealed record FichierLocal(string Nom, long Taille, Func<Task<Stream>> Ouvrir);

/// <summary>
/// Services de la plateforme fournis par la vue (TopLevel) : choix et enregistrement de fichiers, ouverture d'adresses
/// et de fichiers locaux. Absent dans l'outil de captures.
/// </summary>
public interface IHotePlateforme
{
    Task<FichierLocal?> ChoisirFichierAsync(string titre, IReadOnlyList<string> extensions);
    Task<bool> EnregistrerFichierAsync(string nomSuggere, byte[] contenu);
    Task<bool> OuvrirAdresseAsync(Uri adresse);
    Task<bool> OuvrirFichierLocalAsync(string chemin);
}

/// <summary>Base des applications complémentaires : disposition compacte (téléphone), hôte de plateforme, temps réel.</summary>
public abstract class ComplementsViewModelBase : AppViewModelBase
{
    public static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private bool _isCompact;
    private bool _abonne;

    /// <summary>Largeur disponible inférieure à 720 px : menus repliés, une seule colonne.</summary>
    public bool IsCompact
    {
        get => _isCompact;
        set
        {
            if (SetProperty(ref _isCompact, value))
            {
                OnPropertyChanged(nameof(IsLarge));
                OnPropertyChanged(nameof(MenuLignes));
                OnPropertyChanged(nameof(MenuColonnes));
                OnPropertyChanged(nameof(ColonneDetail));
                OnPropertyChanged(nameof(LargeurDetail));
                OnPropertyChanged(nameof(MargeDetail));
                OnCompactChanged();
            }
        }
    }

    public bool IsLarge => !IsCompact;

    // ===== Liste + détail : côte à côte sur ordinateur, l'un après l'autre sur téléphone =====

    protected virtual double LargeurDetailLarge => 380;
    public int ColonneDetail => IsCompact ? 0 : 1;
    public double LargeurDetail => IsCompact ? double.NaN : LargeurDetailLarge;
    public Avalonia.Thickness MargeDetail => IsCompact ? new Avalonia.Thickness(0) : new Avalonia.Thickness(10, 0, 0, 0);

    // ===== Menu latéral (bandeau horizontal en disposition compacte) =====

    private string _section = string.Empty;

    public ObservableCollection<SectionMenu> Menu { get; } = [];

    /// <summary>Titre du menu latéral (nom de l'application).</summary>
    public virtual string TitreMenu => string.Empty;

    /// <summary>Lignes et colonnes occupées par le menu dans la grille de la vue (colonne latérale ou bandeau).</summary>
    public int MenuLignes => IsCompact ? 1 : 2;
    public int MenuColonnes => IsCompact ? 2 : 1;

    public string Section
    {
        get => _section;
        set
        {
            if (!SetProperty(ref _section, value)) return;
            foreach (var m in Menu)
                m.IsSelected = m.Code == value;
            OnSectionChanged();
        }
    }

    protected SectionMenu AjouterSection(string code, string libelle, bool visible = true)
    {
        var section = new SectionMenu(code, libelle, c => Section = c) { IsVisible = visible };
        Menu.Add(section);
        return section;
    }

    protected virtual void OnSectionChanged()
    {
    }

    public IHotePlateforme? Hote { get; set; }

    protected virtual void OnCompactChanged()
    {
    }

    /// <summary>Message temps réel reçu (fil de l'interface).</summary>
    protected virtual void OnRealtime(RealtimeMessage message)
    {
    }

    public override void Resume()
    {
        if (_abonne) return;
        SaphirSession.Realtime += Recevoir;
        _abonne = true;
    }

    public override void Suspend()
    {
        if (!_abonne) return;
        SaphirSession.Realtime -= Recevoir;
        _abonne = false;
    }

    /// <summary>Identifiant de l'application (arguments d'ouverture reçus alors qu'elle est déjà ouverte).</summary>
    protected virtual string? IdApplication => null;

    /// <summary>Argument d'ouverture (« a:12 », « PVE-2026-000004 »…) reçu alors que l'application est déjà affichée.</summary>
    protected virtual void OnArgument(string argument)
    {
    }

    private void Recevoir(RealtimeMessage message)
    {
        if (message.Type == SaphirSession.OpenAppType && IdApplication is { } id && message.AppId == id
            && SaphirSession.TakeAppArgument(id) is { } argument)
            OnArgument(argument);
        OnRealtime(message);
    }

    public static string Date(DateTimeOffset? date) => date?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Fr) ?? "—";

    public static string DateCourte(DateTimeOffset date)
    {
        var local = date.ToLocalTime();
        return local.Date == DateTime.Today ? local.ToString("HH:mm", Fr) : local.ToString("dd/MM/yy HH:mm", Fr);
    }

    public static string Jour(DateOnly? date) => date?.ToString("dd/MM/yyyy", Fr) ?? "—";

    /// <summary>« 15/03/1990 » → date ; vide → null ; sinon exception de saisie.</summary>
    public static DateOnly? LireJour(string? texte, string libelle)
    {
        if (string.IsNullOrWhiteSpace(texte)) return null;
        if (DateOnly.TryParseExact(texte.Trim(), ["dd/MM/yyyy", "d/M/yyyy", "ddMMyyyy", "yyyy-MM-dd"], Fr, DateTimeStyles.None, out var d))
            return d;
        throw new ArgumentException($"{libelle} : date attendue au format JJ/MM/AAAA.");
    }

    /// <summary>Date « JJ/MM/AAAA » + heure « HH:MM » locales → instant.</summary>
    public static DateTimeOffset LireInstant(string? jour, string? heure, string libelle)
    {
        var d = LireJour(jour, libelle) ?? throw new ArgumentException($"{libelle} : date obligatoire.");
        if (!TimeOnly.TryParseExact((heure ?? string.Empty).Trim(), ["HH:mm", "H:mm", "HH'h'mm", "HHmm"], Fr, DateTimeStyles.None, out var h))
            throw new ArgumentException($"{libelle} : heure attendue au format HH:MM.");
        var local = d.ToDateTime(h, DateTimeKind.Local);
        return new DateTimeOffset(local);
    }

    /// <summary>Lit un fichier choisi en mémoire (pièces jointes, 10 Mo au plus).</summary>
    protected static async Task<PieceJointeInput> LirePieceAsync(FichierLocal fichier)
    {
        if (!PiecesJointes.ExtensionAutorisee(fichier.Nom))
            throw new ArgumentException($"Type de fichier non autorisé : {fichier.Nom}.");
        if (fichier.Taille > PiecesJointes.TailleMax)
            throw new ArgumentException($"« {fichier.Nom} » dépasse {PiecesJointes.TailleLisible(PiecesJointes.TailleMax)}.");
        await using var flux = await fichier.Ouvrir();
        using var memoire = new MemoryStream();
        await flux.CopyToAsync(memoire);
        if (memoire.Length > PiecesJointes.TailleMax)
            throw new ArgumentException($"« {fichier.Nom} » dépasse {PiecesJointes.TailleLisible(PiecesJointes.TailleMax)}.");
        return new PieceJointeInput(fichier.Nom, PiecesJointes.TypeMime(fichier.Nom), Convert.ToBase64String(memoire.ToArray()));
    }

    /// <summary>Télécharge un fichier joint et propose de l'enregistrer.</summary>
    protected async Task EnregistrerPieceAsync(string route)
    {
        var api = Api;
        if (api is null) return;
        if (Hote is null)
        {
            ErrorText = "L'enregistrement de fichiers n'est pas disponible sur ce poste.";
            return;
        }
        await RunAsync(async () =>
        {
            var piece = await api.GetAsync<PieceJointeContenuDto>(route);
            if (await Hote.EnregistrerFichierAsync(piece.Nom, Convert.FromBase64String(piece.Donnees)))
                InfoText = $"« {piece.Nom} » enregistré.";
        });
    }

    /// <summary>Ouvre une autre application SAPHIR avec un argument (permissions vérifiées par l'application ouverte).</summary>
    protected bool OuvrirApplication(string appId, string? argument)
    {
        if (!SaphirSession.CanOpen(appId))
        {
            ErrorText = "Tu n'as pas accès à cette application : la référence ne peut pas être ouverte.";
            return false;
        }
        SaphirSession.Publish(new RealtimeMessage(SaphirSession.OpenAppType, Text: argument, AppId: appId));
        return true;
    }
}

/// <summary>Entrée du menu latéral d'une application.</summary>
public sealed class SectionMenu : ViewModelBase
{
    private bool _isSelected;
    private bool _isVisible = true;
    private string? _compteur;

    public SectionMenu(string code, string libelle, Action<string> aller)
    {
        Code = code;
        Libelle = libelle;
        AllerCommand = new RelayCommand(() => aller(Code));
    }

    public string Code { get; }
    public string Libelle { get; }
    public RelayCommand AllerCommand { get; }
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    public bool IsVisible { get => _isVisible; set => SetProperty(ref _isVisible, value); }

    /// <summary>Compteur affiché à côté du libellé (« 3 » non lus), null = aucun.</summary>
    public string? Compteur
    {
        get => _compteur;
        set
        {
            if (SetProperty(ref _compteur, value))
                OnPropertyChanged(nameof(HasCompteur));
        }
    }

    public bool HasCompteur => !string.IsNullOrEmpty(Compteur);
}

/// <summary>Agent choisi (destinataire, membre, partage…) ; <see cref="Option"/> : « peut modifier » pour les notes.</summary>
public sealed class AgentChoisi(long id, string libelle, string? detail) : ViewModelBase
{
    private bool _option;

    public long Id { get; } = id;
    public string Libelle { get; } = libelle;
    public string? Detail { get; } = detail;
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
    public bool Option { get => _option; set => SetProperty(ref _option, value); }
}

/// <summary>
/// Annuaire des agents actifs pour les listes de choix : GET /api/referentiels/agents (module RH, avec grade et service),
/// à défaut GET /api/annuaire/agents (colonnes du socle). Cache de 2 minutes.
/// </summary>
public static class AgentsSaphir
{
    private static List<AgentResumeDto>? _cache;
    private static DateTime _chargeLe;
    private static readonly SemaphoreSlim Verrou = new(1, 1);

    public static async Task<IReadOnlyList<AgentResumeDto>> ListeAsync(bool forcer = false)
    {
        var api = SaphirSession.Api;
        if (api is null) return Array.Empty<AgentResumeDto>();
        await Verrou.WaitAsync();
        try
        {
            if (!forcer && _cache is not null && DateTime.UtcNow - _chargeLe < TimeSpan.FromMinutes(2))
                return _cache;
            List<AgentResumeDto> agents;
            try
            {
                agents = await api.GetAsync<List<AgentResumeDto>>("api/referentiels/agents");
            }
            catch (ApiException)
            {
                var socle = await api.GetAsync<List<AnnuaireAgentDto>>("api/annuaire/agents");
                agents = socle.Select(a => new AgentResumeDto(a.Id, a.Reference, a.Nom, a.Prenom, a.Rio, null, null, null, null, null, a.Statut)).ToList();
            }
            _cache = agents.OrderBy(a => a.Nom, StringComparer.Create(ComplementsViewModelBase.Fr, true))
                .ThenBy(a => a.Prenom, StringComparer.Create(ComplementsViewModelBase.Fr, true)).ToList();
            _chargeLe = DateTime.UtcNow;
            return _cache;
        }
        finally
        {
            Verrou.Release();
        }
    }

    public static string Detail(AgentResumeDto a)
        => string.Join(" · ", new[] { a.Grade, a.Service, a.Unite, a.Rio is null ? null : "RIO " + a.Rio }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public static bool Correspond(AgentResumeDto a, string recherche)
    {
        if (string.IsNullOrWhiteSpace(recherche)) return true;
        var r = recherche.Trim();
        return a.Nom.Contains(r, StringComparison.OrdinalIgnoreCase) || a.Prenom.Contains(r, StringComparison.OrdinalIgnoreCase)
               || $"{a.Nom} {a.Prenom}".Contains(r, StringComparison.OrdinalIgnoreCase)
               || (a.Rio?.Contains(r, StringComparison.OrdinalIgnoreCase) ?? false)
               || a.Reference.Contains(r, StringComparison.OrdinalIgnoreCase)
               || (a.Service?.Contains(r, StringComparison.OrdinalIgnoreCase) ?? false)
               || (a.Unite?.Contains(r, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}

/// <summary>Sélecteur d'agents réutilisable (destinataires, partages, membres, porteur de caméra).</summary>
public sealed class SelecteurAgentsViewModel : ViewModelBase
{
    private string _recherche = string.Empty;
    private IReadOnlyList<AgentResumeDto> _tous = Array.Empty<AgentResumeDto>();
    private readonly bool _unique;

    public SelecteurAgentsViewModel(string titre, bool unique = false, string? libelleOption = null)
    {
        Titre = titre;
        _unique = unique;
        LibelleOption = libelleOption;
        AjouterCommand = new RelayCommand(p => { if (p is AgentResumeDto a) Ajouter(a.Id, a.Libelle, AgentsSaphir.Detail(a)); });
        RetirerCommand = new RelayCommand(p => { if (p is AgentChoisi a) { Selection.Remove(a); Changer(); } });
        _ = ChargerAsync();
    }

    public string Titre { get; }
    public string? LibelleOption { get; }
    public bool HasOption => LibelleOption is not null;
    public ObservableCollection<AgentResumeDto> Resultats { get; } = [];
    public ObservableCollection<AgentChoisi> Selection { get; } = [];
    public RelayCommand AjouterCommand { get; }
    public RelayCommand RetirerCommand { get; }
    public bool HasSelection => Selection.Count > 0;
    public bool HasResultats => Resultats.Count > 0;

    /// <summary>Agents à exclure des résultats (l'agent connecté, par exemple).</summary>
    public HashSet<long> Exclus { get; } = [];

    public event Action? Modifie;

    public string Recherche
    {
        get => _recherche;
        set
        {
            if (SetProperty(ref _recherche, value))
                Filtrer();
        }
    }

    public IReadOnlyList<long> Ids => Selection.Select(s => s.Id).ToList();

    public void Ajouter(long id, string libelle, string? detail = null, bool option = false)
    {
        if (Selection.Any(s => s.Id == id)) return;
        if (_unique) Selection.Clear();
        Selection.Add(new AgentChoisi(id, libelle, detail) { Option = option });
        Recherche = string.Empty;
        Changer();
    }

    /// <summary>Ajoute plusieurs agents (liste de diffusion d'un service).</summary>
    public void AjouterTous(IEnumerable<AgentResumeDto> agents)
    {
        foreach (var a in agents)
            if (Selection.All(s => s.Id != a.Id) && !Exclus.Contains(a.Id))
                Selection.Add(new AgentChoisi(a.Id, a.Libelle, AgentsSaphir.Detail(a)));
        Changer();
    }

    public void Vider()
    {
        Selection.Clear();
        Recherche = string.Empty;
        Changer();
    }

    public IReadOnlyList<AgentResumeDto> Tous => _tous;

    public async Task ChargerAsync()
    {
        try
        {
            _tous = await AgentsSaphir.ListeAsync();
        }
        catch (Exception)
        {
            _tous = Array.Empty<AgentResumeDto>();
        }
        Filtrer();
    }

    private void Filtrer()
    {
        Resultats.Clear();
        if (!string.IsNullOrWhiteSpace(Recherche))
            foreach (var a in _tous.Where(a => !Exclus.Contains(a.Id) && Selection.All(s => s.Id != a.Id) && AgentsSaphir.Correspond(a, Recherche)).Take(25))
                Resultats.Add(a);
        OnPropertyChanged(nameof(HasResultats));
    }

    private void Changer()
    {
        OnPropertyChanged(nameof(HasSelection));
        Filtrer();
        Modifie?.Invoke();
    }
}
