using System.Collections.ObjectModel;
using System.Globalization;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Operations;

namespace Saphir.ViewModels.Apps.Operations;

/// <summary>Formats d'affichage et lecture des saisies (dates « jj/mm/aaaa hh:mm », nombres).</summary>
public static class Fmt
{
    public static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly string[] FormatsDateHeure = ["dd/MM/yyyy HH:mm", "d/M/yyyy HH:mm", "dd/MM/yyyy H:mm", "d/M/yyyy H:mm", "dd/MM/yyyy HH:mm:ss"];
    private static readonly string[] FormatsDate = ["dd/MM/yyyy", "d/M/yyyy"];
    private static readonly string[] FormatsHeure = ["HH:mm", "H:mm"];

    public static string DateHeure(DateTimeOffset? date) => date is { } d ? d.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Fr) : "—";
    public static string Heure(DateTimeOffset? date) => date is { } d ? d.ToLocalTime().ToString("HH:mm", Fr) : "—";
    public static string Saisie(DateTimeOffset? date) => date is { } d ? d.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Fr) : string.Empty;
    public static string Saisie(DateOnly? date) => date is { } d ? d.ToString("dd/MM/yyyy", Fr) : string.Empty;
    public static string Maintenant() => DateTime.Now.ToString("dd/MM/yyyy HH:mm", Fr);

    /// <summary>Paramètre d'URL d'une date (UTC, ISO 8601).</summary>
    public static string Iso(DateTimeOffset date) => ApiClient.Q(date.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

    public static string? Vide(string? texte) => string.IsNullOrWhiteSpace(texte) ? null : texte.Trim();

    /// <summary>« jj/mm/aaaa hh:mm », « jj/mm/aaaa » (minuit) ou « hh:mm » (aujourd'hui), heure locale ; vide → null.</summary>
    public static DateTimeOffset? LireDateHeure(string? texte, string champ)
    {
        var t = Vide(texte);
        if (t is null)
            return null;
        if (DateTime.TryParseExact(t, FormatsDateHeure, Fr, DateTimeStyles.AssumeLocal, out var dh))
            return new DateTimeOffset(dh);
        if (DateTime.TryParseExact(t, FormatsDate, Fr, DateTimeStyles.AssumeLocal, out var d))
            return new DateTimeOffset(d);
        if (DateTime.TryParseExact(t, FormatsHeure, Fr, DateTimeStyles.AssumeLocal | DateTimeStyles.NoCurrentDateDefault, out var h))
            return new DateTimeOffset(DateTime.Today.Add(h.TimeOfDay));
        throw new ArgumentException($"« {champ} » : date ou heure invalide (format jj/mm/aaaa hh:mm).");
    }

    public static DateOnly? LireDate(string? texte, string champ)
    {
        var t = Vide(texte);
        if (t is null)
            return null;
        if (DateOnly.TryParseExact(t, FormatsDate, Fr, DateTimeStyles.None, out var d))
            return d;
        throw new ArgumentException($"« {champ} » : date invalide (format jj/mm/aaaa).");
    }

    public static int? LireEntier(string? texte, string champ)
    {
        var t = Vide(texte);
        if (t is null)
            return null;
        if (int.TryParse(t.Replace(" ", string.Empty).Replace(" ", string.Empty), NumberStyles.Integer, Fr, out var n) && n >= 0)
            return n;
        throw new ArgumentException($"« {champ} » : nombre entier attendu.");
    }

    public static double? LireDecimal(string? texte, string champ)
    {
        var t = Vide(texte);
        if (t is null)
            return null;
        if (double.TryParse(t.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v))
            return v;
        throw new ArgumentException($"« {champ} » : nombre attendu.");
    }

    public static string Decimal(double? valeur) => valeur is { } v ? v.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;

    public static string Oui(bool valeur) => valeur ? "Oui" : "Non";
}

/// <summary>Entrée du menu latéral d'une application.</summary>
public sealed record MenuEntree(string Code, string Libelle, string Groupe);

/// <summary>Répertoire RH (agents actifs), mis en cache quelques minutes pour les listes de choix.</summary>
public static class RepertoireRh
{
    private static List<AgentResumeDto>? _agents;
    private static DateTime _chargeLe;

    public static async Task<List<AgentResumeDto>> AgentsAsync(bool forcer = false)
    {
        var api = SaphirSession.Api ?? throw new ApiException("Session SAPHIR fermée.");
        if (!forcer && _agents is not null && DateTime.UtcNow - _chargeLe < TimeSpan.FromMinutes(3))
            return _agents;
        try
        {
            var agents = await api.GetAsync<List<AgentResumeDto>>("api/referentiels/agents?tous=false");
            _agents = agents.OrderBy(a => a.Nom, StringComparer.Create(Fmt.Fr, true)).ThenBy(a => a.Prenom).ToList();
            _chargeLe = DateTime.UtcNow;
            return _agents;
        }
        catch (ApiException ex) when (ex.Status == System.Net.HttpStatusCode.NotFound)
        {
            throw new ApiException("Répertoire RH indisponible : le module RH n'est pas encore installé sur le serveur.");
        }
    }
}

/// <summary>
/// Base des deux applications (MAIN COURANTE, PÉGASE) : menu latéral, sections, temps réel, affichage compact (téléphone).
/// </summary>
public abstract class OperationsHostViewModel : AppViewModelBase
{
    private readonly Dictionary<string, SectionViewModel> _sections = new(StringComparer.Ordinal);
    private MenuEntree? _menuSelectionne;
    private SectionViewModel? _section;
    private bool _estCompact;
    private bool _abonne;
    private bool _changementInterne;

    protected OperationsHostViewModel(string titre)
    {
        Titre = titre;
    }

    public string Titre { get; }

    public ObservableCollection<MenuEntree> Menu { get; } = [];

    public MenuEntree? MenuSelectionne
    {
        get => _menuSelectionne;
        set
        {
            if (!SetProperty(ref _menuSelectionne, value) || value is null || _changementInterne)
                return;
            Afficher(value.Code, true);
        }
    }

    public SectionViewModel? Section
    {
        get => _section;
        private set
        {
            if (SetProperty(ref _section, value))
                OnPropertyChanged(nameof(TitreSection));
        }
    }

    public string TitreSection => Section?.Titre ?? Titre;

    /// <summary>Largeur réduite (téléphone) : menu en liste déroulante au-dessus du contenu.</summary>
    public bool EstCompact
    {
        get => _estCompact;
        set => SetProperty(ref _estCompact, value);
    }

    public ApiClient? Client => Api;

    public Task Executer(Func<Task> travail, string? succes = null) => RunAsync(travail, succes);

    public Task<bool> Confirmer(string titre, string message, string libelle = "Confirmer", bool danger = false)
        => ConfirmAsync(titre, message, libelle, danger);

    public void Erreur(string message) => ErrorText = message;

    public void Information(string message) => InfoText = message;

    /// <summary>Chargement discret (temps réel) : pas d'attente affichée, messages conservés sauf erreur.</summary>
    public async Task Silencieux(Func<Task> travail)
    {
        try
        {
            await travail();
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

    protected abstract SectionViewModel CreerSection(string code);

    /// <summary>Section du menu (créée au premier affichage, puis conservée) ; rechargée à chaque affichage.</summary>
    public T Afficher<T>(string code) where T : SectionViewModel => (T)Afficher(code);

    /// <summary>Section déjà créée (ou créée maintenant) sans l'afficher.</summary>
    public T Obtenir<T>(string code) where T : SectionViewModel
    {
        if (!_sections.TryGetValue(code, out var section))
        {
            section = CreerSection(code);
            _sections[code] = section;
        }
        return (T)section;
    }

    /// <summary>Affiche une section ; <paramref name="depuisMenu"/> : choix de l'agent dans le menu latéral.</summary>
    public SectionViewModel Afficher(string code, bool depuisMenu = false)
    {
        var section = Obtenir<SectionViewModel>(code);
        if (depuisMenu)
            section.DepuisMenu();
        _changementInterne = true;
        MenuSelectionne = Menu.FirstOrDefault(m => m.Code == code);
        _changementInterne = false;
        Section = section;
        _ = Executer(section.ChargerAsync);
        return section;
    }

    public override void Resume()
    {
        base.Resume();
        if (!_abonne)
        {
            SaphirSession.Realtime += SurMessage;
            _abonne = true;
        }
        if (Section is { } section)
            _ = section.Rafraichir();
    }

    public override void Suspend()
    {
        base.Suspend();
        if (_abonne)
        {
            SaphirSession.Realtime -= SurMessage;
            _abonne = false;
        }
    }

    private void SurMessage(RealtimeMessage message) => Avalonia.Threading.Dispatcher.UIThread.Post(() => SurTempsReel(message));

    protected virtual void SurTempsReel(RealtimeMessage message)
    {
        if (Section is { } section && section.Concerne(message))
            _ = section.Rafraichir();
    }
}

/// <summary>Section d'une application (un écran du menu).</summary>
public abstract class SectionViewModel(OperationsHostViewModel host, string titre) : ViewModelBase
{
    private bool _enCours;
    private bool _aRefaire;

    public OperationsHostViewModel Host { get; } = host;
    public string Titre { get; protected set; } = titre;

    protected static ApiClient Client => SaphirSession.Api ?? throw new ApiException("Session SAPHIR fermée : reconnecte-toi.");

    /// <summary>Charge les données de l'écran (les erreurs remontent vers la barre de message).</summary>
    public abstract Task ChargerAsync();

    /// <summary>L'agent choisit cet écran dans le menu (ex. « Nouvelle mention » : formulaire vierge).</summary>
    public virtual void DepuisMenu()
    {
    }

    /// <summary>Message temps réel concernant cet écran (rechargement discret).</summary>
    public virtual bool Concerne(RealtimeMessage message) => false;

    public async Task Rafraichir()
    {
        if (_enCours)
        {
            _aRefaire = true;
            return;
        }
        _enCours = true;
        try
        {
            do
            {
                _aRefaire = false;
                await Host.Silencieux(ChargerAsync);
            } while (_aRefaire);
        }
        finally
        {
            _enCours = false;
        }
    }

    protected static bool Est(RealtimeMessage message, string type) => message.Type == type;
}

/// <summary>Ligne d'historique (sys_historique) affichable.</summary>
public sealed class HistoriqueLigne(HistoriqueDto dto)
{
    public HistoriqueDto Dto { get; } = dto;
    public string Date => Fmt.DateHeure(Dto.Date);
    public string Auteur => Dto.Auteur;
    public string Reference => Dto.Reference;
    public string Action => Dto.Action;
    public string Detail => Dto.Champ is null ? Dto.Nouvelle ?? string.Empty : $"{Dto.Champ} : {Dto.Ancienne ?? "—"} → {Dto.Nouvelle ?? "—"}";
    public string Motif => Dto.Motif ?? string.Empty;
}

/// <summary>Ligne du journal de service.</summary>
public sealed class JournalLigne(JournalEntreeDto dto)
{
    public JournalEntreeDto Dto { get; } = dto;
    public string Date => Fmt.DateHeure(Dto.Date);
    public string Heure => Fmt.Heure(Dto.Date);
    public string Type => Dto.Type;
    public string Libelle => Dto.Libelle;
    public string Details => Dto.Details ?? string.Empty;
    public string Source => Dto.Source;
    public string Auteur => Dto.Auteur;
}

/// <summary>Ligne de mention (listes).</summary>
public sealed class MentionLigne(MentionResumeDto dto)
{
    public MentionResumeDto Dto { get; } = dto;
    public long Id => Dto.Id;
    public string Numero => Dto.Numero;
    public string Date => Fmt.DateHeure(Dto.Date);
    public string Type => Dto.Type;
    public string Categorie => Dto.Categorie ?? "—";
    public string Nature => Dto.Nature ?? "—";
    public string Declarant => Dto.Declarant ?? "—";
    public string Redacteur => Dto.Redacteur;
    public string Statut => Dto.Statut;
    public string Intervention => Dto.InterventionNumero ?? string.Empty;
    public string Origine => Dto.ParentNumero is null ? string.Empty : "Additif à " + Dto.ParentNumero;
}

/// <summary>Ligne d'équipage (Main courante, PÉGASE).</summary>
public sealed class EquipageLigne(EquipageDto dto)
{
    public EquipageDto Dto { get; } = dto;
    public long Id => Dto.Id;
    public string Indicatif => Dto.Indicatif;
    public string Type => Dto.Type;
    public string Chef => Dto.Chef ?? "—";
    public string Membres => string.Join(", ", Dto.Membres.Select(m => m.Libelle));
    public int NombreAgents => Dto.Membres.Count;
    public string Vehicule => Dto.Vehicule ?? "—";
    public string Statut => Dto.Statut;
    public string Intervention => Dto.InterventionNumero ?? string.Empty;
    public string Vacation => Dto.VacationReference;
    public string Etat => Dto.Actif ? "Actif" : "Dissous";
    public string Libelle => $"{Dto.Indicatif} — {Dto.Statut}";
}

/// <summary>Ligne d'intervention (listes PÉGASE).</summary>
public sealed class InterventionLigne(InterventionLigneDto dto)
{
    public InterventionLigneDto Dto { get; } = dto;
    public long Id => Dto.Id;
    public string Numero => Dto.Numero;
    public string Debut => Fmt.DateHeure(Dto.DebutLe);
    public string Priorite => Dto.Priorite;
    public string Nature => Dto.SousMotif is null ? Dto.Nature : $"{Dto.Nature} — {Dto.SousMotif}";
    public string Lieu => Dto.Adresse ?? Dto.Commune ?? "—";
    public string Commune => Dto.Commune ?? "—";
    public string Statut => Dto.Statut;
    public string Equipages => Dto.Equipages.Count == 0 ? "—" : string.Join(", ", Dto.Equipages);
    public int Appels => Dto.NombreAppels;
    public string Libelle => $"{Dto.Numero} — {Dto.Nature} ({Dto.Commune ?? "commune ?"})";
}

/// <summary>Ligne d'appel (listes PÉGASE).</summary>
public sealed class AppelLigne(AppelResumeDto dto)
{
    public AppelResumeDto Dto { get; } = dto;
    public long Id => Dto.Id;
    public string Numero => Dto.Numero;
    public string Recu => Fmt.DateHeure(Dto.RecuLe);
    public string Etat => Dto.Etat;
    public string Canal => Dto.Canal ?? "—";
    public string Motif => Dto.Motif ?? "À qualifier";
    public string Priorite => Dto.Priorite ?? "—";
    public string Commune => Dto.Commune ?? "—";
    public string Operateur => Dto.Operateur;
    public string Intervention => Dto.InterventionNumero ?? string.Empty;
    public string Libelle => $"{Dto.Numero} — {Motif} ({Etat})";
}

/// <summary>Barre d'histogramme simple (statistiques).</summary>
public sealed class BarreStatistique(string libelle, int nombre, int maximum)
{
    public string Libelle { get; } = libelle;
    public int Nombre { get; } = nombre;
    public double Largeur { get; } = maximum <= 0 ? 0 : Math.Max(2, 180.0 * nombre / maximum);
}
