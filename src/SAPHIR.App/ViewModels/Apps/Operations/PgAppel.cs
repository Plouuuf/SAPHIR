using System.Collections.ObjectModel;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Operations;

namespace Saphir.ViewModels.Apps.Operations;

/// <summary>E — Personne concernée en cours de saisie.</summary>
public sealed class PersonneEdition
{
    public string? Role { get; set; }
    public bool IdentiteConnue { get; set; }
    public long? PersonneId { get; set; }
    public string? PersonneReference { get; set; }
    public string? Nom { get; set; }
    public string? Prenom { get; set; }
    public string? Description { get; set; }
    public string? Localisation { get; set; }
    public string? EtatApparent { get; set; }
    public string? Informations { get; set; }
    public string? Source { get; set; }
    public string Fiche => PersonneReference is null ? "Saisie libre" : "Fiche " + PersonneReference;

    public static PersonneEdition Depuis(AppelPersonne p) => new()
    {
        Role = p.Role, IdentiteConnue = p.IdentiteConnue, PersonneId = p.PersonneId, PersonneReference = p.PersonneReference, Nom = p.Nom,
        Prenom = p.Prenom, Description = p.Description, Localisation = p.Localisation, EtatApparent = p.EtatApparent, Informations = p.Informations,
        Source = p.Source
    };

    public AppelPersonne Vers() => new(Fmt.Vide(Role), IdentiteConnue || PersonneId is not null || Fmt.Vide(Nom) is not null, PersonneId, PersonneReference,
        Fmt.Vide(Nom), Fmt.Vide(Prenom), Fmt.Vide(Description), Fmt.Vide(Localisation), Fmt.Vide(EtatApparent), Fmt.Vide(Informations), Fmt.Vide(Source));
}

/// <summary>F — Véhicule concerné en cours de saisie.</summary>
public sealed class VehiculeEdition
{
    public long? VehiculeId { get; set; }
    public string? Immatriculation { get; set; }
    public string? Pays { get; set; } = "France";
    public string? Marque { get; set; }
    public string? Modele { get; set; }
    public string? Couleur { get; set; }
    public string? Categorie { get; set; }
    public string? Description { get; set; }
    public string? Direction { get; set; }
    public string? LienFaits { get; set; }
    public string Fiche => VehiculeId is null ? "Saisie libre" : "Fiche véhicule n° " + VehiculeId;

    public static VehiculeEdition Depuis(AppelVehicule v) => new()
    {
        VehiculeId = v.VehiculeId, Immatriculation = v.Immatriculation, Pays = v.Pays, Marque = v.Marque, Modele = v.Modele, Couleur = v.Couleur,
        Categorie = v.Categorie, Description = v.Description, Direction = v.Direction, LienFaits = v.LienFaits
    };

    public AppelVehicule Vers() => new(VehiculeId, Fmt.Vide(Immatriculation), Fmt.Vide(Pays), Fmt.Vide(Marque), Fmt.Vide(Modele), Fmt.Vide(Couleur),
        Fmt.Vide(Categorie), Fmt.Vide(Description), Fmt.Vide(Direction), Fmt.Vide(LienFaits));
}

/// <summary>Formulaire « Nouvel appel CIC » (6.3, sections A à H) en cours de saisie.</summary>
public sealed class AppelEdition : ViewModelBase
{
    // A
    public string? Canal { get; set; } = "17";
    public string? Nature { get; set; } = "Demande d'intervention";
    // B
    public string? ReqQualite { get; set; }
    public bool ReqIdentiteConnue { get; set; }
    public long? ReqPersonneId { get; set; }
    public string? ReqPersonneReference { get; set; }
    public string? ReqNom { get; set; }
    public string? ReqPrenom { get; set; }
    public string? ReqTelephone { get; set; }
    public string? ReqRappel { get; set; }
    public string? ReqAdresse { get; set; }
    public string? ReqNaissance { get; set; }
    public string? ReqLien { get; set; }
    public string? ReqLangue { get; set; } = "Français";
    public bool ReqInterprete { get; set; }
    public string? ReqObservations { get; set; }
    // C
    public string? Commune { get; set; }
    public string? CodePostal { get; set; }
    public string? Numero { get; set; }
    public string? Rue { get; set; }
    public string? AdresseComplete { get; set; }
    public string? Residence { get; set; }
    public string? Batiment { get; set; }
    public string? Etage { get; set; }
    public string? Appartement { get; set; }
    public string? Acces { get; set; }
    public string? TypeLieu { get; set; }
    public string? Repere { get; set; }
    public string? PosX { get; set; }
    public string? PosY { get; set; }
    public string? Fiabilite { get; set; } = "Déclarée par le requérant";
    // D
    public string? Motif { get; set; }
    public string? SousMotif { get; set; }
    public string? Description { get; set; }
    public string? FaitsLe { get; set; } = Fmt.Maintenant();
    public bool FaitsEnCours { get; set; }
    public string? Priorite { get; set; } = "P2";
    public string? Gravite { get; set; } = "Non précisée";
    public string? NombrePersonnes { get; set; }
    public bool BlessesSignales { get; set; }
    public bool DangerImmediat { get; set; }
    public string? MotifObservations { get; set; }
    // E, F
    public ObservableCollection<PersonneEdition> Personnes { get; } = [];
    public ObservableCollection<VehiculeEdition> Vehicules { get; } = [];
    // G
    public bool Blesses { get; set; }
    public string? NombreBlesses { get; set; }
    public bool Menace { get; set; }
    public bool Arme { get; set; }
    public string? ArmePrecision { get; set; }
    public bool Mineurs { get; set; }
    public bool Assistance { get; set; }
    public bool SecoursDemandes { get; set; }
    public string? ServicesSurPlace { get; set; }
    public string? Consignes { get; set; }
    public bool RenfortEnvisage { get; set; }
    // H
    public bool EngagementNecessaire { get; set; } = true;
    public string? NombreEquipages { get; set; } = "1";
    public string? CategorieMoyen { get; set; } = "Police-secours";
    public string? EngagementObservations { get; set; }

    public string ReqFiche => ReqPersonneReference is null ? "Saisie libre" : "Fiche du répertoire : " + ReqPersonneReference;

    public void ToutRafraichir() => OnPropertyChanged(string.Empty);

    public static AppelEdition Depuis(AppelFormulaire f)
    {
        var e = new AppelEdition
        {
            Canal = f.Canal, Nature = f.Nature,
            ReqQualite = f.Requerant.Qualite, ReqIdentiteConnue = f.Requerant.IdentiteConnue, ReqPersonneId = f.Requerant.PersonneId,
            ReqPersonneReference = f.Requerant.PersonneReference, ReqNom = f.Requerant.Nom, ReqPrenom = f.Requerant.Prenom,
            ReqTelephone = f.Requerant.Telephone, ReqRappel = f.Requerant.TelephoneRappel, ReqAdresse = f.Requerant.Adresse,
            ReqNaissance = Fmt.Saisie(f.Requerant.DateNaissance), ReqLien = f.Requerant.LienFaits, ReqLangue = f.Requerant.Langue,
            ReqInterprete = f.Requerant.Interprete, ReqObservations = f.Requerant.Observations,
            Commune = f.Localisation.Commune, CodePostal = f.Localisation.CodePostal, Numero = f.Localisation.Numero, Rue = f.Localisation.Rue,
            AdresseComplete = f.Localisation.AdresseComplete, Residence = f.Localisation.Residence, Batiment = f.Localisation.Batiment,
            Etage = f.Localisation.Etage, Appartement = f.Localisation.Appartement, Acces = f.Localisation.Acces, TypeLieu = f.Localisation.TypeLieu,
            Repere = f.Localisation.Repere, PosX = Fmt.Decimal(f.Localisation.X), PosY = Fmt.Decimal(f.Localisation.Y),
            Fiabilite = f.Localisation.Fiabilite,
            Motif = f.Motif.Motif, SousMotif = f.Motif.SousMotif, Description = f.Motif.Description, FaitsLe = Fmt.Saisie(f.Motif.FaitsLe),
            FaitsEnCours = f.Motif.FaitsEnCours, Priorite = f.Motif.Priorite, Gravite = f.Motif.Gravite,
            NombrePersonnes = f.Motif.NombrePersonnes?.ToString(Fmt.Fr), BlessesSignales = f.Motif.BlessesSignales,
            DangerImmediat = f.Motif.DangerImmediat, MotifObservations = f.Motif.Observations,
            Blesses = f.Securite.Blesses, NombreBlesses = f.Securite.NombreBlesses?.ToString(Fmt.Fr), Menace = f.Securite.Menace, Arme = f.Securite.Arme,
            ArmePrecision = f.Securite.ArmePrecision, Mineurs = f.Securite.Mineurs, Assistance = f.Securite.PersonneAssistance,
            SecoursDemandes = f.Securite.SecoursDemandes, ServicesSurPlace = f.Securite.ServicesSurPlace, Consignes = f.Securite.Consignes,
            RenfortEnvisage = f.Securite.RenfortEnvisage,
            EngagementNecessaire = f.Engagement.Necessaire, NombreEquipages = f.Engagement.NombreEquipages?.ToString(Fmt.Fr),
            CategorieMoyen = f.Engagement.CategorieMoyen, EngagementObservations = f.Engagement.Observations
        };
        foreach (var p in f.Personnes)
            e.Personnes.Add(PersonneEdition.Depuis(p));
        foreach (var v in f.Vehicules)
            e.Vehicules.Add(VehiculeEdition.Depuis(v));
        return e;
    }

    public AppelFormulaire VersFormulaire() => new(
        Fmt.Vide(Canal), Fmt.Vide(Nature),
        new AppelRequerant(Fmt.Vide(ReqQualite), ReqIdentiteConnue || ReqPersonneId is not null || Fmt.Vide(ReqNom) is not null, ReqPersonneId,
            ReqPersonneReference, Fmt.Vide(ReqNom), Fmt.Vide(ReqPrenom), Fmt.Vide(ReqTelephone), Fmt.Vide(ReqRappel), Fmt.Vide(ReqAdresse),
            Fmt.LireDate(ReqNaissance, "Date de naissance du requérant"), Fmt.Vide(ReqLien), Fmt.Vide(ReqLangue), ReqInterprete, Fmt.Vide(ReqObservations)),
        new AppelLocalisation(Fmt.Vide(Commune), Fmt.Vide(CodePostal), Fmt.Vide(Numero), Fmt.Vide(Rue), Fmt.Vide(AdresseComplete), Fmt.Vide(Residence),
            Fmt.Vide(Batiment), Fmt.Vide(Etage), Fmt.Vide(Appartement), Fmt.Vide(Acces), Fmt.Vide(TypeLieu), Fmt.Vide(Repere),
            Fmt.LireDecimal(PosX, "Position X"), Fmt.LireDecimal(PosY, "Position Y"), Fmt.Vide(Fiabilite)),
        new AppelMotif(Fmt.Vide(Motif), Fmt.Vide(SousMotif), Fmt.Vide(Description), Fmt.LireDateHeure(FaitsLe, "Date des faits"), FaitsEnCours,
            Fmt.Vide(Priorite), Fmt.Vide(Gravite), Fmt.LireEntier(NombrePersonnes, "Nombre de personnes"), BlessesSignales, DangerImmediat,
            Fmt.Vide(MotifObservations)),
        Personnes.Select(p => p.Vers()).ToList(),
        Vehicules.Select(v => v.Vers()).ToList(),
        new AppelSecurite(Blesses, Fmt.LireEntier(NombreBlesses, "Nombre de blessés"), Menace, Arme, Fmt.Vide(ArmePrecision), Mineurs, Assistance,
            SecoursDemandes, Fmt.Vide(ServicesSurPlace), Fmt.Vide(Consignes), RenfortEnvisage),
        new AppelEngagement(EngagementNecessaire, Fmt.LireEntier(NombreEquipages, "Nombre d'équipages"), Fmt.Vide(CategorieMoyen),
            Fmt.Vide(EngagementObservations)));
}

/// <summary>Prise d'appel CIC : appels en cours, formulaire A à H et actions (6.3).</summary>
public sealed class PgAppelSection : SectionViewModel
{
    private AppelDto? _appel;
    private AppelEdition _edition = new();
    private AppelLigne? _selection;
    private long? _aOuvrir;
    private bool _nouveau;
    private string _canalNouveau = "17";
    private string _motifAction = string.Empty;
    private InterventionLigneDto? _interventionRattachement;
    private EquipageDto? _equipageAEngager;
    private string _recherchePersonne = string.Empty;
    private PersonneResumeDto? _personneChoisie;
    private string _rechercheVehicule = string.Empty;
    private VehiculeResumeDto? _vehiculeChoisi;

    public PgAppelSection(PegaseViewModel host) : base(host, "Nouvel appel")
    {
        App = host;
        NouvelAppelCommand = new RelayCommand(() => _ = NouvelAppelAsync());
        EnregistrerCommand = new RelayCommand(() => _ = ActionAsync("enregistrer"));
        CreerInterventionCommand = new RelayCommand(() => _ = ActionAsync("intervention"));
        RattacherCommand = new RelayCommand(() => _ = ActionAsync("rattacher"));
        AttenteCommand = new RelayCommand(() => _ = ActionAsync("attente"));
        TransmettreTnCommand = new RelayCommand(() => _ = ActionAsync("transmettre-tn"));
        CloreCommand = new RelayCommand(() => _ = ActionAsync("clore"));
        OuvrirInterventionCommand = new RelayCommand(() => { if (Appel?.InterventionId is { } id) App.OuvrirIntervention(id); });
        AjouterPersonneCommand = new RelayCommand(() => Edition.Personnes.Add(new PersonneEdition { Role = "Victime" }));
        RetirerPersonneCommand = new RelayCommand(p => { if (p is PersonneEdition pe) Edition.Personnes.Remove(pe); });
        AjouterVehiculeCommand = new RelayCommand(() => Edition.Vehicules.Add(new VehiculeEdition()));
        RetirerVehiculeCommand = new RelayCommand(p => { if (p is VehiculeEdition ve) Edition.Vehicules.Remove(ve); });
        RechercherPersonneCommand = new RelayCommand(() => _ = RechercherPersonneAsync());
        PersonneRequerantCommand = new RelayCommand(UtiliserPourRequerant);
        PersonneConcerneeCommand = new RelayCommand(AjouterPersonneTrouvee);
        RechercherVehiculeCommand = new RelayCommand(() => _ = RechercherVehiculeAsync());
        VehiculeConcerneCommand = new RelayCommand(AjouterVehiculeTrouve);
    }

    public PegaseViewModel App { get; }

    // ===== Listes =====
    public IReadOnlyList<string> Canaux => PegaseListes.Canaux;
    public IReadOnlyList<string> Natures => PegaseListes.NaturesAppel;
    public IReadOnlyList<string> Qualites => PegaseListes.QualitesRequerant;
    public IReadOnlyList<string> TypesLieu => PegaseListes.TypesLieu;
    public IReadOnlyList<string> Fiabilites => PegaseListes.Fiabilites;
    public IReadOnlyList<string> Motifs => PegaseListes.Motifs;
    public IReadOnlyList<string> Priorites => PegaseListes.Priorites;
    public IReadOnlyList<string> Gravites => PegaseListes.Gravites;
    public IReadOnlyList<string> Roles => PegaseListes.RolesPersonne;
    public IReadOnlyList<string> CategoriesVehicule => PegaseListes.CategoriesVehicule;
    public IReadOnlyList<string> CategoriesMoyen => PegaseListes.CategoriesMoyen;

    public ObservableCollection<AppelLigne> AppelsOuverts { get; } = [];
    public ObservableCollection<InterventionLigneDto> InterventionsActives { get; } = [];
    public ObservableCollection<EquipageDto> EquipagesDisponibles { get; } = [];
    public ObservableCollection<PersonneResumeDto> PersonnesTrouvees { get; } = [];
    public ObservableCollection<VehiculeResumeDto> VehiculesTrouves { get; } = [];

    // ===== Appel courant =====

    public AppelDto? Appel
    {
        get => _appel;
        private set
        {
            SetProperty(ref _appel, value);
            Titre = value is null ? "Nouvel appel" : "Appel " + value.Numero;
            foreach (var nom in new[] { nameof(Titre), nameof(AAppel), nameof(EstModifiable), nameof(EstOuvert), nameof(Entete), nameof(Etat),
                         nameof(Intervention), nameof(AIntervention), nameof(Duree), nameof(PeutCreerIntervention), nameof(PeutEngager) })
                OnPropertyChanged(nom);
        }
    }

    public AppelEdition Edition { get => _edition; private set => SetProperty(ref _edition, value); }

    public AppelLigne? Selection
    {
        get => _selection;
        set
        {
            if (SetProperty(ref _selection, value) && value is not null && value.Id != Appel?.Id)
                _ = Host.Executer(() => OuvrirAsync(value.Id));
        }
    }

    public bool AAppel => Appel is not null;
    public bool EstModifiable => PegaseViewModel.PeutPrendreAppel && Appel is not null && Appel.Etat != AppelEtats.ClosSansSuite;
    public bool EstOuvert => EstModifiable && AppelEtats.EstOuvert(Appel!.Etat);
    public bool PeutCreerIntervention => EstOuvert && PegaseViewModel.PeutCreerIntervention;
    public bool PeutEngager => PeutCreerIntervention && PegaseViewModel.PeutAffecter;
    public bool PeutPrendreAppel => PegaseViewModel.PeutPrendreAppel;
    public string Entete => Appel is null ? "Aucun appel ouvert : prends un nouvel appel ou choisis un appel en cours."
        : $"{Appel.Numero} — reçu le {Fmt.DateHeure(Appel.RecuLe)} — opérateur {Appel.Operateur}" + (Appel.Service is null ? string.Empty : $" ({Appel.Service})") +
          (Appel.SimcallId is null ? string.Empty : " — SIMCALL");
    public string Etat => Appel is null ? string.Empty : Appel.Etat + (Appel.MotifCloture is null ? string.Empty : $" : {Appel.MotifCloture}");
    public string Intervention => Appel?.InterventionNumero ?? "Aucune";
    public bool AIntervention => Appel?.InterventionId is not null;
    public string Duree => Appel?.RaccrocheLe is { } fin ? $"Appel terminé à {Fmt.Heure(fin)} (durée {(fin - Appel.RecuLe):mm\\:ss})" : Appel?.SimcallId is null ? string.Empty : "Appel en cours sur SIMCALL";

    public string CanalNouveau { get => _canalNouveau; set => SetProperty(ref _canalNouveau, value); }
    public string MotifAction { get => _motifAction; set => SetProperty(ref _motifAction, value); }
    public InterventionLigneDto? InterventionRattachement { get => _interventionRattachement; set => SetProperty(ref _interventionRattachement, value); }
    public EquipageDto? EquipageAEngager { get => _equipageAEngager; set => SetProperty(ref _equipageAEngager, value); }
    public string RecherchePersonne { get => _recherchePersonne; set => SetProperty(ref _recherchePersonne, value); }
    public PersonneResumeDto? PersonneChoisie { get => _personneChoisie; set => SetProperty(ref _personneChoisie, value); }
    public string RechercheVehicule { get => _rechercheVehicule; set => SetProperty(ref _rechercheVehicule, value); }
    public VehiculeResumeDto? VehiculeChoisi { get => _vehiculeChoisi; set => SetProperty(ref _vehiculeChoisi, value); }

    public RelayCommand NouvelAppelCommand { get; }
    public RelayCommand EnregistrerCommand { get; }
    public RelayCommand CreerInterventionCommand { get; }
    public RelayCommand RattacherCommand { get; }
    public RelayCommand AttenteCommand { get; }
    public RelayCommand TransmettreTnCommand { get; }
    public RelayCommand CloreCommand { get; }
    public RelayCommand OuvrirInterventionCommand { get; }
    public RelayCommand AjouterPersonneCommand { get; }
    public RelayCommand RetirerPersonneCommand { get; }
    public RelayCommand AjouterVehiculeCommand { get; }
    public RelayCommand RetirerVehiculeCommand { get; }
    public RelayCommand RechercherPersonneCommand { get; }
    public RelayCommand PersonneRequerantCommand { get; }
    public RelayCommand PersonneConcerneeCommand { get; }
    public RelayCommand RechercherVehiculeCommand { get; }
    public RelayCommand VehiculeConcerneCommand { get; }

    public void PreparerOuverture(long id) => _aOuvrir = id;
    public void PreparerNouveau() => _nouveau = true;

    public override async Task ChargerAsync()
    {
        await ChargerListesAsync();
        if (_aOuvrir is { } id)
        {
            _aOuvrir = null;
            await OuvrirAsync(id);
        }
        else if (_nouveau)
        {
            _nouveau = false;
            await CreerAppelAsync();
        }
        else if (Appel is { } courant)
        {
            // Actualisation discrète de l'en-tête (raccroché SIMCALL, état) sans toucher à la saisie ni à la version.
            var aJour = await Client.GetAsync<AppelDto>($"api/pegase/appels/{courant.Id}");
            Appel = aJour with { Version = courant.Version, Formulaire = courant.Formulaire };
        }
    }

    private async Task ChargerListesAsync()
    {
        var appels = await Client.GetAsync<List<AppelResumeDto>>("api/pegase/appels?etat=ouverts&limit=100");
        _selection = null;
        AppelsOuverts.Clear();
        foreach (var a in appels)
            AppelsOuverts.Add(new AppelLigne(a));
        _selection = Appel is { } c ? AppelsOuverts.FirstOrDefault(a => a.Id == c.Id) : null;
        OnPropertyChanged(nameof(Selection));

        var interventions = await Client.GetAsync<List<InterventionLigneDto>>("api/pegase/interventions?vue=actives&limit=200");
        var rattachement = InterventionRattachement?.Id;
        InterventionsActives.Clear();
        foreach (var i in interventions)
            InterventionsActives.Add(i);
        InterventionRattachement = rattachement is { } rid ? InterventionsActives.FirstOrDefault(i => i.Id == rid) : null;

        if (PegaseViewModel.PeutAffecter)
        {
            var equipages = await Client.GetAsync<List<EquipageDto>>("api/main-courante/equipages?actifs=true");
            var choisi = EquipageAEngager?.Id;
            EquipagesDisponibles.Clear();
            foreach (var e in equipages.Where(e => EquipageStatuts.PeutEtreEngage(e.Statut) && e.InterventionId is null))
                EquipagesDisponibles.Add(e);
            EquipageAEngager = choisi is { } eid ? EquipagesDisponibles.FirstOrDefault(e => e.Id == eid) : null;
        }
    }

    private async Task OuvrirAsync(long id)
    {
        await EnregistrerEnCoursSiBesoinAsync(id);
        var appel = await Client.GetAsync<AppelDto>($"api/pegase/appels/{id}");
        Edition = AppelEdition.Depuis(appel.Formulaire);
        Appel = appel;
        MotifAction = string.Empty;
        _selection = AppelsOuverts.FirstOrDefault(a => a.Id == id);
        OnPropertyChanged(nameof(Selection));
    }

    /// <summary>Un autre appel arrive (SIMCALL) : la saisie en cours est conservée en brouillon.</summary>
    private async Task EnregistrerEnCoursSiBesoinAsync(long prochain)
    {
        if (Appel is not { } courant || courant.Id == prochain || !EstModifiable)
            return;
        try
        {
            await Client.SendAsync<AppelDto>(HttpMethod.Put, $"api/pegase/appels/{courant.Id}",
                new AppelActionInput(Edition.VersFormulaire(), courant.Version, null, null, null));
            Host.Information($"La saisie de l'appel {courant.Numero} a été conservée en brouillon.");
        }
        catch (Exception ex) when (ex is ApiException or ArgumentException)
        {
            Host.Erreur($"Appel {courant.Numero} non enregistré : {ex.Message}");
        }
    }

    private async Task NouvelAppelAsync() => await Host.Executer(CreerAppelAsync, "Nouvel appel ouvert : saisis les informations disponibles.");

    private async Task CreerAppelAsync()
    {
        if (!PegaseViewModel.PeutPrendreAppel)
            throw new ApiException("Tu n'as pas la permission de prendre un appel.");
        var appel = await Client.PostAsync<AppelDto>("api/pegase/appels", new AppelCreationInput(CanalNouveau));
        await EnregistrerEnCoursSiBesoinAsync(appel.Id);
        var edition = AppelEdition.Depuis(appel.Formulaire);
        edition.FaitsLe = Fmt.Maintenant();
        edition.Fiabilite ??= "Déclarée par le requérant";
        edition.Priorite ??= "P2";
        edition.Nature ??= "Demande d'intervention";
        edition.EngagementNecessaire = true;
        edition.NombreEquipages ??= "1";
        edition.CategorieMoyen ??= "Police-secours";
        Edition = edition;
        Appel = appel;
        MotifAction = string.Empty;
        await ChargerListesAsync();
    }

    private async Task ActionAsync(string action)
    {
        if (Appel is not { } appel)
            return;
        if (action == "clore")
        {
            if (Fmt.Vide(MotifAction) is null)
            {
                Host.Erreur("Indique le motif de la clôture sans suite.");
                return;
            }
            if (!await Host.Confirmer("Clôture sans suite", $"Clore l'appel {appel.Numero} sans suite ?", "Clore", true))
                return;
        }
        if (action == "rattacher" && InterventionRattachement is null)
        {
            Host.Erreur("Choisis l'intervention à laquelle rattacher l'appel.");
            return;
        }

        await Host.Executer(async () =>
        {
            var input = new AppelActionInput(Edition.VersFormulaire(), appel.Version, Fmt.Vide(MotifAction), InterventionRattachement?.Id,
                action == "intervention" && PeutEngager ? EquipageAEngager?.Id : null);
            switch (action)
            {
                case "enregistrer":
                    await RecevoirAsync(await Client.SendAsync<AppelDto>(HttpMethod.Put, $"api/pegase/appels/{appel.Id}", input));
                    Host.Information("Brouillon enregistré.");
                    break;
                case "intervention":
                    var intervention = await Client.PostAsync<InterventionDto>($"api/pegase/appels/{appel.Id}/intervention", input);
                    Appel = null;
                    Edition = new AppelEdition();
                    App.OuvrirIntervention(intervention.Id);
                    Host.Information($"Intervention {intervention.Numero} créée.");
                    break;
                default:
                    await RecevoirAsync(await Client.PostAsync<AppelDto>($"api/pegase/appels/{appel.Id}/{action}", input));
                    Host.Information(action switch
                    {
                        "rattacher" => "Appel rattaché à l'intervention.",
                        "attente" => "Appel mis en attente.",
                        "transmettre-tn" => "Appel transmis au TN.",
                        _ => "Appel clos sans suite."
                    });
                    break;
            }
        });
    }

    private async Task RecevoirAsync(AppelDto appel)
    {
        Edition = AppelEdition.Depuis(appel.Formulaire);
        Appel = appel;
        MotifAction = string.Empty;
        await ChargerListesAsync();
    }

    private async Task RechercherPersonneAsync()
    {
        if (Fmt.Vide(RecherchePersonne) is not { } q)
            return;
        await Host.Executer(async () =>
        {
            List<PersonneResumeDto> resultats;
            try
            {
                resultats = await Client.GetAsync<List<PersonneResumeDto>>("api/personnes/recherche?q=" + ApiClient.Q(q));
            }
            catch (ApiException ex) when (ex.Status == System.Net.HttpStatusCode.NotFound)
            {
                resultats = [];
            }
            PersonnesTrouvees.Clear();
            foreach (var p in resultats)
                PersonnesTrouvees.Add(p);
            PersonneChoisie = PersonnesTrouvees.FirstOrDefault();
            if (PersonnesTrouvees.Count == 0)
                Host.Information("Aucune personne trouvée dans SAPHIR : saisis l'identité librement.");
        });
    }

    private void UtiliserPourRequerant()
    {
        if (PersonneChoisie is not { } p)
            return;
        Edition.ReqIdentiteConnue = true;
        Edition.ReqPersonneId = p.Id;
        Edition.ReqPersonneReference = p.Reference;
        Edition.ReqNom = p.Nom;
        Edition.ReqPrenom = p.Prenom;
        Edition.ReqNaissance = Fmt.Saisie(p.DateNaissance);
        Edition.ToutRafraichir();
    }

    private void AjouterPersonneTrouvee()
    {
        if (PersonneChoisie is not { } p)
            return;
        Edition.Personnes.Add(new PersonneEdition
        {
            Role = "Mis en cause", IdentiteConnue = true, PersonneId = p.Id, PersonneReference = p.Reference, Nom = p.Nom, Prenom = p.Prenom,
            Source = "Répertoire SAPHIR",
            Informations = p.Alertes.Count == 0 ? null : "Alertes : " + string.Join(", ", p.Alertes)
        });
    }

    private async Task RechercherVehiculeAsync()
    {
        if (Fmt.Vide(RechercheVehicule) is not { } q)
            return;
        await Host.Executer(async () =>
        {
            List<VehiculeResumeDto> resultats;
            try
            {
                resultats = await Client.GetAsync<List<VehiculeResumeDto>>("api/vehicules/recherche?q=" + ApiClient.Q(q));
            }
            catch (ApiException ex) when (ex.Status == System.Net.HttpStatusCode.NotFound)
            {
                resultats = [];
            }
            VehiculesTrouves.Clear();
            foreach (var v in resultats)
                VehiculesTrouves.Add(v);
            VehiculeChoisi = VehiculesTrouves.FirstOrDefault();
            if (VehiculesTrouves.Count == 0)
                Host.Information("Aucun véhicule trouvé dans SAPHIR : saisis-le librement.");
        });
    }

    private void AjouterVehiculeTrouve()
    {
        if (VehiculeChoisi is not { } v)
            return;
        Edition.Vehicules.Add(new VehiculeEdition
        {
            VehiculeId = v.Id, Immatriculation = v.Immatriculation, Marque = v.Marque, Modele = v.Modele, Couleur = v.Couleur,
            Description = v.Alertes.Count == 0 ? null : "Alertes : " + string.Join(", ", v.Alertes)
        });
    }

    public override bool Concerne(RealtimeMessage message)
        => Est(message, OperationsTypes.PegaseChanged) && message.Text is { } t &&
           (t.StartsWith("appel:", StringComparison.Ordinal) || t.StartsWith("intervention:", StringComparison.Ordinal));
}
