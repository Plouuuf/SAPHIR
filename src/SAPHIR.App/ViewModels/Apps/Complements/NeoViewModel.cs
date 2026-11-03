using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Complements;

namespace Saphir.ViewModels.Apps.Complements;

/// <summary>Tuile de l'accueil NEO.</summary>
public sealed class TuileNeo(string code, string titre, string sousTitre, string icone, Action<string> ouvrir) : ViewModelBase
{
    private string? _compteur;

    public string Code { get; } = code;
    public string Titre { get; } = titre;
    public string SousTitre { get; } = sousTitre;
    public Bitmap? Icone { get; } = Charger(icone);
    public RelayCommand OuvrirCommand { get; } = new(() => ouvrir(code));

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

    private static Bitmap? Charger(string chemin)
    {
        try
        {
            return ImageLoader.Asset(chemin);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>
/// NEO — terminal mobile des effectifs (pensé d'abord pour un téléphone de 390 px) : profil de l'agent (RH),
/// intervention suivie (PÉGASE), consultations rapides personne / véhicule (fichiers du module Judiciaire),
/// et accès aux applications MRZ, PVe, Tchap, Annuaire, Notes et Messagerie (mêmes vues que sur le poste).
/// </summary>
public sealed class NeoViewModel : ComplementsViewModelBase
{
    public const string Accueil = "accueil";
    public const string PagePersonne = "personne";
    public const string PageVehicule = "vehicule";

    /// <summary>Intervention suivie pendant la session (conservée à la réouverture de NEO).</summary>
    private static long? _interventionSuivie;

    private string _page = Accueil;
    private ComplementsViewModelBase? _application;
    private string _recherche = string.Empty;
    private InterventionResumeDto? _intervention;
    private string? _saisieIntervention;
    private bool _interventionIndisponible;

    public NeoViewModel(string? argument = null)
    {
        var arg = argument ?? SaphirSession.TakeAppArgument(SaphirApps.Neo);
        if (arg is not null && arg.StartsWith("intervention:", StringComparison.Ordinal) && long.TryParse(arg["intervention:".Length..], out var id))
            _interventionSuivie = id;

        AjouterTuile(PagePersonne, "Personne", "Consultation rapide", "Icons/cheops.png", SaphirSession.CanOpen(SaphirApps.Cheops));
        AjouterTuile(PageVehicule, "Véhicule", "Immatriculation", "Icons/cheops.png", SaphirSession.CanOpen(SaphirApps.Cheops));
        AjouterTuile(SaphirApps.Mrz, "MRZ", "Lire une pièce d'identité", "Icons/mrz.png", SaphirSession.Has(Permissions.MrzUtiliser));
        AjouterTuile(SaphirApps.Pve, "PVe", "Verbaliser", "Icons/pve.png", SaphirSession.Has(Permissions.PveRediger));
        AjouterTuile(SaphirApps.Tchap, "Tchap", "Salons", "Icons/tchap.png", SaphirSession.Has(Permissions.TchapUtiliser));
        AjouterTuile(SaphirApps.Messagerie, "Messagerie", "Messages internes", "Icons/messagerie.png", SaphirSession.Has(Permissions.MessagerieUtiliser));
        AjouterTuile(SaphirApps.Annuaire, "Annuaire", "Personnel et services", "Icons/annuaire.png", SaphirSession.Has(Permissions.AnnuaireConsulter));
        AjouterTuile(SaphirApps.Notes, "Notes", "Bloc-notes", "Icons/notes.png", SaphirSession.Has(Permissions.NotesUtiliser));
        AjouterTuile(SaphirApps.Pegase, "PÉGASE", "Statut, compte rendu", "Icons/pegase.png", SaphirSession.CanOpen(SaphirApps.Pegase));

        AccueilCommand = new RelayCommand(() => Page = Accueil);
        RechercherCommand = new RelayCommand(() => _ = RechercherAsync());
        OuvrirFicheCommand = new RelayCommand(p =>
        {
            if (p is PersonneRow personne) OuvrirApplication(SaphirApps.Cheops, personne.Reference);
            else if (p is VehiculeRow vehicule) OuvrirApplication(SaphirApps.Cheops, vehicule.Dto.Immatriculation);
        });
        SuivreInterventionCommand = new RelayCommand(() => _ = SuivreAsync());
        ActualiserInterventionCommand = new RelayCommand(() => _ = ChargerInterventionAsync());
        OuvrirInterventionCommand = new RelayCommand(() =>
        {
            if (Intervention is { } i) OuvrirApplication(SaphirApps.Pegase, "intervention:" + i.Id);
        });

        _ = ChargerAccueilAsync();
    }

    public override string TitreMenu => "NEO";

    // ===== Profil (données RH) =====

    public string AgentNom => SaphirSession.Agent is { } a ? $"{a.Nom} {a.Prenom}" : "Agent";
    public string AgentGrade => SaphirSession.Agent?.Grade ?? "Grade non renseigné";
    public string AgentRio => SaphirSession.Agent?.Rio is { } rio ? "RIO " + rio : "RIO non attribué";
    public string AgentAffectation => SaphirSession.Agent is { } a
        ? string.Join(" · ", new[] { a.Service, a.Unite, a.Fonction }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } t ? t : "Affectation non renseignée"
        : string.Empty;

    // ===== Navigation =====

    public ObservableCollection<TuileNeo> Tuiles { get; } = [];

    public string Page
    {
        get => _page;
        set
        {
            if (!SetProperty(ref _page, value)) return;
            AppOuverte?.Suspend();
            AppOuverte = value switch
            {
                SaphirApps.Mrz => new MrzViewModel(),
                SaphirApps.Pve => new PveViewModel("nouveau"),
                SaphirApps.Tchap => new TchapViewModel(),
                SaphirApps.Messagerie => new MessagerieViewModel(),
                SaphirApps.Annuaire => new AnnuaireViewModel(),
                SaphirApps.Notes => new NotesViewModel(),
                _ => null
            };
            Resultats.Clear();
            Vehicules.Clear();
            Recherche = string.Empty;
            foreach (var n in new[] { nameof(IsAccueil), nameof(IsRecherche), nameof(TitrePage), nameof(FiligraneRecherche), nameof(IsPersonne), nameof(IsVehicule) })
                OnPropertyChanged(n);
            if (value == Accueil) _ = ChargerAccueilAsync();
        }
    }

    /// <summary>Application ouverte dans NEO (vue créée par NeoView selon le type du modèle).</summary>
    public ComplementsViewModelBase? AppOuverte
    {
        get => _application;
        private set
        {
            if (SetProperty(ref _application, value))
                OnPropertyChanged(nameof(HasApplication));
        }
    }

    public bool HasApplication => AppOuverte is not null;
    public bool IsAccueil => Page == Accueil;
    public bool IsPersonne => Page == PagePersonne;
    public bool IsVehicule => Page == PageVehicule;
    public bool IsRecherche => IsPersonne || IsVehicule;
    public string TitrePage => Tuiles.FirstOrDefault(t => t.Code == Page)?.Titre ?? "NEO";
    public string FiligraneRecherche => IsVehicule ? "Immatriculation (AB-123-CD)" : "Nom, prénom ou identifiant PER-…";

    // ===== Consultation rapide =====

    public string Recherche { get => _recherche; set => SetProperty(ref _recherche, value); }
    public ObservableCollection<PersonneRow> Resultats { get; } = [];
    public ObservableCollection<VehiculeRow> Vehicules { get; } = [];
    private bool _aucunResultat;
    public bool AucunResultat { get => _aucunResultat; set => SetProperty(ref _aucunResultat, value); }

    // ===== Intervention suivie =====

    public InterventionResumeDto? Intervention
    {
        get => _intervention;
        private set
        {
            if (!SetProperty(ref _intervention, value)) return;
            foreach (var n in new[] { nameof(HasIntervention), nameof(InterventionTitre), nameof(InterventionLieu), nameof(InterventionDetail), nameof(InterventionEquipages) })
                OnPropertyChanged(n);
        }
    }

    public bool HasIntervention => Intervention is not null;
    public bool CanIntervention => SaphirSession.Has(Permissions.PegaseConsulter) || SaphirSession.Has(Permissions.LrppnConsulter);
    public bool InterventionIndisponible { get => _interventionIndisponible; private set => SetProperty(ref _interventionIndisponible, value); }
    public string InterventionTitre => Intervention is { } i ? $"{i.Numero} — {i.Nature}" : string.Empty;
    public string InterventionLieu => Intervention is { } i ? string.Join(", ", new[] { i.Adresse, i.Commune }.Where(s => !string.IsNullOrWhiteSpace(s))) : string.Empty;
    public string InterventionDetail => Intervention is { } i ? $"Statut : {i.Statut} — début {Date(i.DebutLe)}" : string.Empty;
    public string InterventionEquipages => Intervention is { Equipages.Count: > 0 } i ? "Équipages : " + string.Join(", ", i.Equipages) : "Aucun équipage engagé.";
    public string? SaisieIntervention { get => _saisieIntervention; set => SetProperty(ref _saisieIntervention, value); }

    public RelayCommand AccueilCommand { get; }
    public RelayCommand RechercherCommand { get; }
    public RelayCommand OuvrirFicheCommand { get; }
    public RelayCommand SuivreInterventionCommand { get; }
    public RelayCommand ActualiserInterventionCommand { get; }
    public RelayCommand OuvrirInterventionCommand { get; }

    // ===== Actions =====

    private void AjouterTuile(string code, string titre, string sousTitre, string icone, bool visible)
    {
        if (visible)
            Tuiles.Add(new TuileNeo(code, titre, sousTitre, icone, Ouvrir));
    }

    private void Ouvrir(string code)
    {
        if (code == SaphirApps.Pegase)
        {
            OuvrirApplication(SaphirApps.Pegase, Intervention is { } i ? "intervention:" + i.Id : null);
            return;
        }
        Page = code;
    }

    private async Task ChargerAccueilAsync()
    {
        var api = Api;
        if (api is null) return;
        if (Tuiles.FirstOrDefault(t => t.Code == SaphirApps.Messagerie) is { } messagerie)
        {
            try
            {
                var c = await api.GetAsync<MessagerieCompteursDto>("api/messagerie/compteurs");
                messagerie.Compteur = c.NonLus > 0 ? c.NonLus.ToString(Fr) : null;
            }
            catch (ApiException)
            {
                messagerie.Compteur = null;
            }
        }
        if (Tuiles.FirstOrDefault(t => t.Code == SaphirApps.Tchap) is { } tchap)
        {
            try
            {
                var salons = await api.GetAsync<List<SalonDto>>("api/tchap/salons");
                var nonLus = salons.Sum(s => s.NonLus);
                tchap.Compteur = nonLus > 0 ? nonLus.ToString(Fr) : null;
            }
            catch (ApiException)
            {
                tchap.Compteur = null;
            }
        }
        await ChargerInterventionAsync();
    }

    private async Task ChargerInterventionAsync()
    {
        var api = Api;
        if (api is null || !CanIntervention || _interventionSuivie is not { } id) return;
        try
        {
            Intervention = await api.GetAsync<InterventionResumeDto>($"api/pegase/interventions/{id}/resume");
            InterventionIndisponible = false;
        }
        catch (ApiException)
        {
            // Module Opérations absent ou intervention inaccessible : NEO reste utilisable.
            Intervention = null;
            InterventionIndisponible = true;
        }
    }

    private async Task SuivreAsync()
    {
        if (!long.TryParse((SaisieIntervention ?? string.Empty).Trim().Split('-').Last(), out var id) || id <= 0)
        {
            ErrorText = "Indique l'identifiant de l'intervention (nombre ou fin du numéro INT-…).";
            return;
        }
        _interventionSuivie = id;
        SaisieIntervention = null;
        await ChargerInterventionAsync();
        if (InterventionIndisponible)
            ErrorText = "Intervention introuvable ou non accessible.";
    }

    private async Task RechercherAsync()
    {
        var api = Api;
        if (api is null) return;
        var q = Recherche.Trim();
        if (q.Length < 2)
        {
            ErrorText = "Saisis au moins deux caractères.";
            return;
        }
        await RunAsync(async () =>
        {
            Resultats.Clear();
            Vehicules.Clear();
            if (IsVehicule)
            {
                foreach (var v in await api.GetAsync<List<VehiculeResumeDto>>("api/vehicules/recherche?q=" + ApiClient.Q(q)))
                    Vehicules.Add(new VehiculeRow(v));
                AucunResultat = Vehicules.Count == 0;
            }
            else
            {
                foreach (var p in await api.GetAsync<List<PersonneResumeDto>>("api/personnes/recherche?q=" + ApiClient.Q(q)))
                    Resultats.Add(new PersonneRow(p, null));
                AucunResultat = Resultats.Count == 0;
            }
        });
    }

    public override void Resume()
    {
        base.Resume();
        AppOuverte?.Resume();
    }

    public override void Suspend()
    {
        base.Suspend();
        AppOuverte?.Suspend();
    }

    protected override void OnRealtime(RealtimeMessage message)
    {
        if (IsAccueil && message.Type is ComplementsRealtime.MessageRecu or ComplementsRealtime.TchapMessage)
            _ = ChargerAccueilAsync();
    }
}
