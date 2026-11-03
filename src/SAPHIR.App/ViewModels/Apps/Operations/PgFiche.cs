using System.Collections.ObjectModel;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Operations;

namespace Saphir.ViewModels.Apps.Operations;

public sealed class EngagementLigne(EngagementDto dto)
{
    public EngagementDto Dto { get; } = dto;
    public string Indicatif => Dto.Indicatif + (Dto.Renfort ? " (renfort)" : string.Empty);
    public string Chef => Dto.Chef ?? "—";
    public string Agents => Dto.Agents ?? "—";
    public string Vehicule => Dto.Vehicule ?? "—";
    public string Statut => Dto.Actif ? Dto.StatutEquipage : Dto.Statut;
    public string Affecte => Fmt.Heure(Dto.AffecteLe);
    public string EnRoute => Fmt.Heure(Dto.EnRouteLe);
    public string SurPlace => Fmt.Heure(Dto.SurPlaceLe);
    public string Termine => Fmt.Heure(Dto.TermineLe);
    public string AffectePar => Dto.AffectePar ?? "—";
    public string Etat => Dto.Actif ? "Engagé" : "Terminé";
    public string Libelle => $"{Dto.Indicatif} — {Statut}";
}

public sealed class ChronologieLigne(ChronologieDto dto)
{
    public ChronologieDto Dto { get; } = dto;
    public string Heure => Fmt.DateHeure(Dto.Date);
    public string Type => Dto.Type;
    public string Texte => Dto.Texte;
    public string Auteur => Dto.Auteur;
}

/// <summary>Compte rendu (7.3) en cours de saisie.</summary>
public sealed class CompteRenduEdition : ViewModelBase
{
    public string? Indicatif { get; set; }
    public string? ChefDeBord { get; set; }
    public string? Arrivee { get; set; }
    public string? Depart { get; set; }
    public string? Fin { get; set; }
    public string? Resultat { get; set; }
    public string? Faits { get; set; }
    public string? Mesures { get; set; }
    public string? PersonnesControlees { get; set; }
    public string? Interpellations { get; set; }
    public string? Victimes { get; set; }
    public string? Vehicules { get; set; }
    public string? Secours { get; set; }
    public string? Transport { get; set; }
    public string? Destination { get; set; }
    public string? Documents { get; set; }
    public bool SuiteJudiciaire { get; set; }
    public string? Observations { get; set; }

    /// <summary>Toutes les liaisons relisent l'objet (après une modification par le programme).</summary>
    public void ToutRafraichir() => OnPropertyChanged(string.Empty);

    public static CompteRenduEdition Depuis(CompteRenduDto c) => new()
    {
        Indicatif = c.Indicatif, ChefDeBord = c.ChefDeBord, Arrivee = Fmt.Saisie(c.ArriveeLe), Depart = Fmt.Saisie(c.DepartLe), Fin = Fmt.Saisie(c.FinLe),
        Resultat = c.Resultat, Faits = c.Faits, Mesures = c.Mesures, PersonnesControlees = c.PersonnesControlees, Interpellations = c.Interpellations,
        Victimes = c.Victimes, Vehicules = c.Vehicules, Secours = c.Secours, Transport = c.Transport, Destination = c.Destination,
        Documents = c.Documents, SuiteJudiciaire = c.SuiteJudiciaire, Observations = c.Observations
    };

    public CompteRenduInput VersInput(long? equipageId, long? version) => new(
        equipageId, Fmt.Vide(Indicatif), Fmt.Vide(ChefDeBord), Fmt.LireDateHeure(Arrivee, "Heure d'arrivée"), Fmt.LireDateHeure(Depart, "Heure de départ"),
        Fmt.LireDateHeure(Fin, "Heure de fin"), Fmt.Vide(Resultat), Fmt.Vide(Faits), Fmt.Vide(Mesures), Fmt.Vide(PersonnesControlees),
        Fmt.Vide(Interpellations), Fmt.Vide(Victimes), Fmt.Vide(Vehicules), Fmt.Vide(Secours), Fmt.Vide(Transport), Fmt.Vide(Destination),
        Fmt.Vide(Documents), SuiteJudiciaire, Fmt.Vide(Observations), version);
}

/// <summary>Fiche d'intervention (7.1) : synthèse, équipages, chronologie et communications, compte rendu, suites.</summary>
public sealed class PgFicheSection : SectionViewModel
{
    private long? _aOuvrir;
    private InterventionDto? _intervention;
    private long _versionQualification;
    private string _nature = PegaseListes.Motifs[0];
    private string _sousMotif = string.Empty;
    private string _priorite = "P2";
    private string _description = string.Empty;
    private string _adresse = string.Empty;
    private string _commune = string.Empty;
    private string _codePostal = string.Empty;
    private string _posX = string.Empty;
    private string _posY = string.Empty;
    private string _equipagesDemandes = string.Empty;
    private string? _categorieMoyen;
    private string _justification = string.Empty;
    private string _nouveauStatut = InterventionStatuts.EnCours;
    private string _motifStatut = string.Empty;
    private EquipageDto? _equipageAEngager;
    private bool _renfort;
    private EngagementLigne? _engagementChoisi;
    private string _statutEquipage = EquipageStatuts.EnRoute;
    private string _motifEquipage = string.Empty;
    private string _message = string.Empty;
    private CompteRenduDto? _compteRendu;
    private CompteRenduEdition _crEdition = new();
    private EngagementLigne? _crEquipage;
    private string _procedureLrppn = string.Empty;
    private string _suites = string.Empty;
    private string _documents = string.Empty;

    public PgFicheSection(PegaseViewModel host) : base(host, "Fiche d'intervention")
    {
        App = host;
        QualifierCommand = new RelayCommand(() => _ = QualifierAsync());
        StatutCommand = new RelayCommand(() => _ = StatutAsync());
        EngagerCommand = new RelayCommand(() => _ = EngagerAsync());
        DesengagerCommand = new RelayCommand(() => _ = DesengagerAsync());
        StatutEquipageCommand = new RelayCommand(() => _ = StatutEquipageAsync());
        MessageCommand = new RelayCommand(() => _ = MessageAsync());
        NouveauCrCommand = new RelayCommand(NouveauCompteRendu);
        EnregistrerCrCommand = new RelayCommand(() => _ = EnregistrerCompteRenduAsync());
        ValiderCrCommand = new RelayCommand(() => _ = ValiderCompteRenduAsync());
        OuvrirCrCommand = new RelayCommand(p => { if (p is CompteRenduDto c) OuvrirCompteRendu(c); });
        ReferencesCommand = new RelayCommand(() => _ = ReferencesAsync());
        ProjetMentionCommand = new RelayCommand(() =>
        {
            if (Intervention is { } i)
                PegaseViewModel.OuvrirApplication(SaphirApps.MainCourante, "projet-mention:" + i.Id);
        });
        OuvrirMentionCommand = new RelayCommand(p =>
        {
            if (p is MentionResumeDto m)
                PegaseViewModel.OuvrirApplication(SaphirApps.MainCourante, "mention:" + m.Id);
        });
        OuvrirLrppnCommand = new RelayCommand(() =>
        {
            if (Intervention is { } i)
                PegaseViewModel.OuvrirApplication(SaphirApps.Lrppn, "intervention:" + i.Id);
        });
        OuvrirAppelCommand = new RelayCommand(p => { if (p is AppelLigne a) App.OuvrirAppel(a.Id); });
        GeoopsCommand = new RelayCommand(() =>
        {
            if (Intervention is { } i)
            {
                App.Obtenir<GeoopsSection>("geoops").PreparerSelection(i.Id);
                App.Afficher("geoops");
            }
        });
        ActualiserCommand = new RelayCommand(() => { if (Intervention is { } i) { _aOuvrir = i.Id; _ = Host.Executer(ChargerAsync); } });
    }

    public PegaseViewModel App { get; }

    // ===== Listes et droits =====
    public IReadOnlyList<string> Motifs => PegaseListes.Motifs;
    public IReadOnlyList<string> Priorites => PegaseListes.Priorites;
    public IReadOnlyList<string> CategoriesMoyen => PegaseListes.CategoriesMoyen;
    public IReadOnlyList<string> Statuts => InterventionStatuts.All;
    public IReadOnlyList<string> StatutsEquipage { get; } = EquipageStatuts.All.Where(s => s != EquipageStatuts.Affecte).ToList();
    public IReadOnlyList<string> Resultats => PegaseListes.ResultatsIntervention;
    public bool PeutQualifier => PegaseViewModel.PeutQualifier && EstActive;
    public bool PeutChangerStatutIntervention => (PegaseViewModel.PeutQualifier || PegaseViewModel.PeutCloturer) && EstActive;
    public bool PeutAffecter => PegaseViewModel.PeutAffecter && EstActive;
    public bool PeutChangerStatutEquipage => PegaseViewModel.PeutChangerStatut && EstActive;
    public bool PeutEcrire => PegaseViewModel.EstActeur && EstActive;
    public bool PeutRediger => PegaseViewModel.PeutCompteRendu && Intervention is { Statut: not (InterventionStatuts.Cloturee or InterventionStatuts.Annulee) };
    public bool PeutModifierCr => PeutRediger && (CompteRendu is null || CompteRendu.Statut == CompteRenduStatuts.Brouillon);
    public bool PeutValiderCr => (PegaseViewModel.PeutCompteRendu || PegaseViewModel.PeutCloturer) && CompteRendu is { Statut: CompteRenduStatuts.Brouillon };
    public bool PeutReferences => PegaseViewModel.PeutCompteRendu || PegaseViewModel.PeutCloturer || PegaseViewModel.PeutAffecter;
    public bool PeutProposerMention => SaphirSession.Has(Permissions.McMentionRediger);
    public bool PeutOuvrirLrppn => SaphirSession.Has(Permissions.LrppnRediger) || SaphirSession.Has(Permissions.LrppnConsulter);

    // ===== Intervention =====

    public InterventionDto? Intervention
    {
        get => _intervention;
        private set
        {
            SetProperty(ref _intervention, value);
            Titre = value is null ? "Fiche d'intervention" : $"Intervention {value.Numero}";
            foreach (var nom in new[] { nameof(Titre), nameof(EstActive), nameof(Entete), nameof(Lieu), nameof(Requerant), nameof(Securite),
                         nameof(PeutQualifier), nameof(PeutChangerStatutIntervention), nameof(PeutAffecter), nameof(PeutChangerStatutEquipage),
                         nameof(PeutEcrire), nameof(PeutRediger), nameof(PeutModifierCr), nameof(PeutValiderCr), nameof(Cloture), nameof(Demandes),
                         nameof(AlerteSecurite), nameof(ASecurite), nameof(ComptesRendus), nameof(Mentions), nameof(Appels), nameof(Personnes),
                         nameof(Vehicules), nameof(SuiteJudiciaire), nameof(AucunePersonne), nameof(AucunVehicule) })
                OnPropertyChanged(nom);
        }
    }

    public bool EstActive => Intervention is { } i && !InterventionStatuts.EstTerminee(i.Statut);
    public string Entete => Intervention is { } i
        ? $"{i.Numero} — {i.Nature}{(i.SousMotif is null ? string.Empty : " (" + i.SousMotif + ")")} — {PegaseListes.LibellePriorite(i.Priorite)} — {i.Statut}"
        : "Chargement…";
    public string Lieu => Intervention is { } i
        ? (i.Adresse ?? i.Commune ?? "Lieu non précisé") + (i.X is { } x && i.Y is { } y ? FormattableString.Invariant($" — position déclarée {x:0} ; {y:0}") : " — non placée sur GÉOOPS")
        : string.Empty;
    public string Requerant => Intervention?.Requerant ?? "—";
    public string Cloture => Intervention is { } i
        ? $"Début : {Fmt.DateHeure(i.DebutLe)}" + (i.ClotureLe is { } c ? $" — fin : {Fmt.DateHeure(c)}" : string.Empty) +
          (i.MotifStatut is null ? string.Empty : $" — motif : {i.MotifStatut}") + $" — créée par {i.CreePar}"
        : string.Empty;
    public string Demandes => Intervention is { } i ? $"{i.EquipagesDemandes?.ToString(Fmt.Fr) ?? "?"} équipage(s) demandé(s), {i.CategorieMoyen ?? "catégorie non précisée"}" : string.Empty;
    public string SuiteJudiciaire => Intervention?.SuiteJudiciaire == true ? "Suite judiciaire nécessaire (compte rendu)" : "Pas de suite judiciaire signalée";
    public bool ASecurite => AlerteSecurite.Length > 0;
    public string AlerteSecurite => Intervention?.Securite is { } s
        ? string.Join(" — ", new[]
        {
            s.Arme ? "ARME SIGNALÉE" + (s.ArmePrecision is null ? string.Empty : " (" + s.ArmePrecision + ")") : null,
            s.Menace ? "Menace signalée" : null,
            s.Blesses ? $"Blessés : {s.NombreBlesses?.ToString(Fmt.Fr) ?? "nombre inconnu"}" : null,
            s.Mineurs ? "Mineurs présents" : null,
            s.PersonneAssistance ? "Personne ayant besoin d'assistance" : null,
            s.SecoursDemandes ? "Secours demandés" : null,
            s.Consignes is null ? null : "Consignes : " + s.Consignes
        }.Where(t => t is not null))
        : string.Empty;
    public string Securite => Intervention?.Securite is { } s
        ? $"Services sur place : {s.ServicesSurPlace ?? "—"} — renfort envisagé : {Fmt.Oui(s.RenfortEnvisage)}"
        : string.Empty;

    public IReadOnlyList<AppelLigne> Appels => Intervention?.Appels.Select(a => new AppelLigne(a)).ToList() ?? (IReadOnlyList<AppelLigne>)Array.Empty<AppelLigne>();
    public IReadOnlyList<string> Personnes => (Intervention?.Personnes ?? Array.Empty<AppelPersonne>()).Select(p =>
        string.Join(" — ", new[]
        {
            p.Role ?? "Rôle non précisé",
            p.IdentiteConnue || p.Nom is not null ? $"{p.Nom} {p.Prenom}".Trim() + (p.PersonneReference is null ? string.Empty : $" ({p.PersonneReference})") : "identité inconnue",
            p.Description, p.EtatApparent, p.Localisation, p.Informations
        }.Where(t => !string.IsNullOrWhiteSpace(t)))).ToList();
    public IReadOnlyList<string> Vehicules => (Intervention?.Vehicules ?? Array.Empty<AppelVehicule>()).Select(v =>
        string.Join(" — ", new[]
        {
            string.Join(" ", new[] { v.Immatriculation ?? "Sans immatriculation", v.Marque, v.Modele, v.Couleur }.Where(t => !string.IsNullOrWhiteSpace(t))),
            v.LienFaits, v.Direction is null ? null : "direction : " + v.Direction, v.Description
        }.Where(t => !string.IsNullOrWhiteSpace(t)))).ToList();
    public bool AucunePersonne => Personnes.Count == 0;
    public bool AucunVehicule => Vehicules.Count == 0;
    public IReadOnlyList<CompteRenduDto> ComptesRendus => Intervention?.ComptesRendus ?? Array.Empty<CompteRenduDto>();
    public IReadOnlyList<MentionResumeDto> Mentions => Intervention?.Mentions ?? Array.Empty<MentionResumeDto>();
    public ObservableCollection<EngagementLigne> Engagements { get; } = [];
    public ObservableCollection<ChronologieLigne> Chronologie { get; } = [];
    public ObservableCollection<EquipageDto> EquipagesDisponibles { get; } = [];

    // ===== Qualification =====
    public string Nature { get => _nature; set => SetProperty(ref _nature, value); }
    public string SousMotif { get => _sousMotif; set => SetProperty(ref _sousMotif, value); }
    public string Priorite { get => _priorite; set => SetProperty(ref _priorite, value); }
    public string Description { get => _description; set => SetProperty(ref _description, value); }
    public string Adresse { get => _adresse; set => SetProperty(ref _adresse, value); }
    public string Commune { get => _commune; set => SetProperty(ref _commune, value); }
    public string CodePostal { get => _codePostal; set => SetProperty(ref _codePostal, value); }
    public string PosX { get => _posX; set => SetProperty(ref _posX, value); }
    public string PosY { get => _posY; set => SetProperty(ref _posY, value); }
    public string EquipagesDemandes { get => _equipagesDemandes; set => SetProperty(ref _equipagesDemandes, value); }
    public string? CategorieMoyen { get => _categorieMoyen; set => SetProperty(ref _categorieMoyen, value); }
    public string Justification { get => _justification; set => SetProperty(ref _justification, value); }
    public string NouveauStatut { get => _nouveauStatut; set => SetProperty(ref _nouveauStatut, value); }
    public string MotifStatut { get => _motifStatut; set => SetProperty(ref _motifStatut, value); }

    // ===== Équipages =====
    public EquipageDto? EquipageAEngager { get => _equipageAEngager; set => SetProperty(ref _equipageAEngager, value); }
    public bool Renfort { get => _renfort; set => SetProperty(ref _renfort, value); }
    public EngagementLigne? EngagementChoisi { get => _engagementChoisi; set => SetProperty(ref _engagementChoisi, value); }
    public string StatutEquipage { get => _statutEquipage; set => SetProperty(ref _statutEquipage, value); }
    public string MotifEquipage { get => _motifEquipage; set => SetProperty(ref _motifEquipage, value); }

    // ===== Chronologie =====
    public string Message { get => _message; set => SetProperty(ref _message, value); }

    // ===== Compte rendu =====
    public CompteRenduDto? CompteRendu
    {
        get => _compteRendu;
        private set
        {
            SetProperty(ref _compteRendu, value);
            foreach (var nom in new[] { nameof(PeutModifierCr), nameof(PeutValiderCr), nameof(EtatCompteRendu), nameof(CrValide) })
                OnPropertyChanged(nom);
        }
    }

    public CompteRenduEdition CrEdition { get => _crEdition; private set => SetProperty(ref _crEdition, value); }
    public EngagementLigne? CrEquipage
    {
        get => _crEquipage;
        set
        {
            if (!SetProperty(ref _crEquipage, value) || value is null || !PeutModifierCr)
                return;
            CrEdition.Indicatif = value.Dto.Indicatif;
            CrEdition.ChefDeBord = value.Dto.Chef;
            if (string.IsNullOrWhiteSpace(CrEdition.Arrivee)) CrEdition.Arrivee = Fmt.Saisie(value.Dto.SurPlaceLe);
            if (string.IsNullOrWhiteSpace(CrEdition.Fin)) CrEdition.Fin = Fmt.Saisie(value.Dto.TermineLe);
            CrEdition.ToutRafraichir();
        }
    }

    public bool CrValide => CompteRendu is { Statut: CompteRenduStatuts.Valide };
    public string EtatCompteRendu => CompteRendu is null ? "Nouveau compte rendu (brouillon à l'enregistrement)."
        : CompteRendu.Statut == CompteRenduStatuts.Valide
            ? $"Validé le {Fmt.DateHeure(CompteRendu.ValideLe)} par {CompteRendu.ValidePar} — rédigé par {CompteRendu.Redacteur}."
            : $"Brouillon rédigé par {CompteRendu.Redacteur} le {Fmt.DateHeure(CompteRendu.CreeLe)}.";

    // ===== Suites =====
    public string ProcedureLrppn { get => _procedureLrppn; set => SetProperty(ref _procedureLrppn, value); }
    public string Suites { get => _suites; set => SetProperty(ref _suites, value); }
    public string Documents { get => _documents; set => SetProperty(ref _documents, value); }

    public RelayCommand QualifierCommand { get; }
    public RelayCommand StatutCommand { get; }
    public RelayCommand EngagerCommand { get; }
    public RelayCommand DesengagerCommand { get; }
    public RelayCommand StatutEquipageCommand { get; }
    public RelayCommand MessageCommand { get; }
    public RelayCommand NouveauCrCommand { get; }
    public RelayCommand EnregistrerCrCommand { get; }
    public RelayCommand ValiderCrCommand { get; }
    public RelayCommand OuvrirCrCommand { get; }
    public RelayCommand ReferencesCommand { get; }
    public RelayCommand ProjetMentionCommand { get; }
    public RelayCommand OuvrirMentionCommand { get; }
    public RelayCommand OuvrirLrppnCommand { get; }
    public RelayCommand OuvrirAppelCommand { get; }
    public RelayCommand GeoopsCommand { get; }
    public RelayCommand ActualiserCommand { get; }

    public void Preparer(long id) => _aOuvrir = id;

    public override async Task ChargerAsync()
    {
        if (_aOuvrir is { } id)
        {
            _aOuvrir = null;
            await ChargerInterventionAsync(id, true);
        }
        else if (Intervention is { } courante)
        {
            await ChargerInterventionAsync(courante.Id, false);
        }
    }

    /// <summary>Charge la fiche ; <paramref name="remplir"/> : réinitialise aussi les formulaires (qualification, compte rendu, suites).</summary>
    private async Task ChargerInterventionAsync(long id, bool remplir)
    {
        var intervention = await Client.GetAsync<InterventionDto>($"api/pegase/interventions/{id}");
        remplir |= Intervention?.Id != intervention.Id;
        Intervention = intervention;

        var engagement = EngagementChoisi?.Dto.Id;
        var crEngagement = CrEquipage?.Dto.Id;
        _engagementChoisi = null;
        _crEquipage = null;
        Engagements.Clear();
        foreach (var e in intervention.Engagements)
            Engagements.Add(new EngagementLigne(e));
        _engagementChoisi = engagement is { } eid ? Engagements.FirstOrDefault(e => e.Dto.Id == eid) : Engagements.FirstOrDefault(e => e.Dto.Actif);
        _crEquipage = crEngagement is { } cid ? Engagements.FirstOrDefault(e => e.Dto.Id == cid) : null;
        OnPropertyChanged(nameof(EngagementChoisi));
        OnPropertyChanged(nameof(CrEquipage));
        Chronologie.Clear();
        foreach (var c in intervention.Chronologie.Reverse())
            Chronologie.Add(new ChronologieLigne(c));

        if (remplir)
        {
            _versionQualification = intervention.Version;
            Nature = PegaseListes.Motifs.Contains(intervention.Nature) ? intervention.Nature : PegaseListes.Motifs[^1];
            SousMotif = intervention.SousMotif ?? string.Empty;
            Priorite = intervention.Priorite;
            Description = intervention.Description ?? string.Empty;
            Adresse = intervention.Adresse ?? string.Empty;
            Commune = intervention.Commune ?? string.Empty;
            CodePostal = intervention.CodePostal ?? string.Empty;
            PosX = Fmt.Decimal(intervention.X);
            PosY = Fmt.Decimal(intervention.Y);
            EquipagesDemandes = intervention.EquipagesDemandes?.ToString(Fmt.Fr) ?? string.Empty;
            CategorieMoyen = intervention.CategorieMoyen;
            Justification = string.Empty;
            NouveauStatut = intervention.Statut == InterventionStatuts.CompteRenduAttendu ? InterventionStatuts.Cloturee : intervention.Statut;
            MotifStatut = string.Empty;
            ProcedureLrppn = intervention.ProcedureLrppn ?? string.Empty;
            Suites = intervention.Suites ?? string.Empty;
            Documents = intervention.Documents ?? string.Empty;
            var brouillon = intervention.ComptesRendus.LastOrDefault(c => c.Statut == CompteRenduStatuts.Brouillon);
            if (brouillon is not null)
                OuvrirCompteRendu(brouillon);
            else
                NouveauCompteRendu();
        }
        else if (CompteRendu is { } cr && intervention.ComptesRendus.FirstOrDefault(c => c.Id == cr.Id) is { } aJour && aJour.Statut != cr.Statut)
        {
            OuvrirCompteRendu(aJour);
        }

        if (PegaseViewModel.PeutAffecter && EstActive)
        {
            var equipages = await Client.GetAsync<List<EquipageDto>>("api/main-courante/equipages?actifs=true");
            var choisi = EquipageAEngager?.Id;
            EquipagesDisponibles.Clear();
            foreach (var e in equipages.Where(e => EquipageStatuts.PeutEtreEngage(e.Statut) && e.InterventionId is null))
                EquipagesDisponibles.Add(e);
            EquipageAEngager = choisi is { } ch ? EquipagesDisponibles.FirstOrDefault(e => e.Id == ch) : EquipagesDisponibles.FirstOrDefault();
        }
    }

    public override bool Concerne(RealtimeMessage message)
        => Intervention is { } i && ((Est(message, OperationsTypes.PegaseChanged) && message.Text == "intervention:" + i.Id) ||
                                     (Est(message, OperationsTypes.MainCouranteChanged) && message.Text?.StartsWith("equipage:", StringComparison.Ordinal) == true));

    private Task RecevoirAsync(InterventionDto intervention) => ChargerInterventionAsync(intervention.Id, false);

    private async Task QualifierAsync()
    {
        if (Intervention is not { } i)
            return;
        await Host.Executer(async () =>
        {
            var input = new InterventionModificationInput(Nature, Fmt.Vide(SousMotif), Priorite, Fmt.Vide(Description), Fmt.Vide(Adresse), Fmt.Vide(Commune),
                Fmt.Vide(CodePostal), Fmt.LireDecimal(PosX, "Position X"), Fmt.LireDecimal(PosY, "Position Y"),
                Fmt.LireEntier(EquipagesDemandes, "Équipages demandés"), CategorieMoyen, Fmt.Vide(Justification), _versionQualification);
            var maj = await Client.SendAsync<InterventionDto>(HttpMethod.Put, $"api/pegase/interventions/{i.Id}", input);
            await ChargerInterventionAsync(maj.Id, true);
        }, "Qualification enregistrée.");
    }

    private async Task StatutAsync()
    {
        if (Intervention is not { } i)
            return;
        if (InterventionStatuts.EstTerminee(NouveauStatut) &&
            !await Host.Confirmer("Fin de l'intervention", $"Passer l'intervention {i.Numero} au statut « {NouveauStatut} » ? Les équipages engagés seront libérés.",
                NouveauStatut == InterventionStatuts.Annulee ? "Annuler l'intervention" : "Clôturer", true))
            return;
        await Host.Executer(async () =>
        {
            await RecevoirAsync(await Client.PostAsync<InterventionDto>($"api/pegase/interventions/{i.Id}/statut",
                new InterventionStatutInput(NouveauStatut, Fmt.Vide(MotifStatut))));
            MotifStatut = string.Empty;
        }, "Statut de l'intervention modifié.");
    }

    private async Task EngagerAsync()
    {
        if (Intervention is not { } i || EquipageAEngager is not { } e)
        {
            Host.Erreur("Choisis un équipage disponible.");
            return;
        }
        await Host.Executer(async () =>
        {
            await RecevoirAsync(await Client.PostAsync<InterventionDto>($"api/pegase/interventions/{i.Id}/engagements", new EngagementInput(e.Id, Renfort)));
            Renfort = false;
        }, $"Équipage {e.Indicatif} engagé.");
    }

    private async Task DesengagerAsync()
    {
        if (Intervention is not { } i || EngagementChoisi is not { Dto.Actif: true } engagement)
        {
            Host.Erreur("Choisis un engagement en cours.");
            return;
        }
        if (!await Host.Confirmer("Désengagement", $"Désengager {engagement.Dto.Indicatif} de l'intervention {i.Numero} ? L'équipage redevient disponible.", "Désengager", true))
            return;
        await Host.Executer(async () =>
        {
            await RecevoirAsync(await Client.PostAsync<InterventionDto>($"api/pegase/interventions/{i.Id}/engagements/{engagement.Dto.Id}/desengager",
                new MotifInput(Fmt.Vide(MotifEquipage))));
            MotifEquipage = string.Empty;
        }, "Équipage désengagé.");
    }

    private async Task StatutEquipageAsync()
    {
        if (Intervention is not { } i || EngagementChoisi is not { Dto.Actif: true } engagement)
        {
            Host.Erreur("Choisis un équipage engagé.");
            return;
        }
        await Host.Executer(async () =>
        {
            await Client.PostAsync<EquipageDto>($"api/pegase/equipages/{engagement.Dto.EquipageId}/statut",
                new EquipageStatutInput(StatutEquipage, Fmt.Vide(MotifEquipage)));
            MotifEquipage = string.Empty;
            await ChargerInterventionAsync(i.Id, false);
        }, $"{engagement.Dto.Indicatif} : {StatutEquipage}.");
    }

    private async Task MessageAsync()
    {
        if (Intervention is not { } i || Fmt.Vide(Message) is not { } texte)
            return;
        await Host.Executer(async () =>
        {
            await RecevoirAsync(await Client.PostAsync<InterventionDto>($"api/pegase/interventions/{i.Id}/messages", new MessageInput(texte)));
            Message = string.Empty;
        });
    }

    // ===== Compte rendu =====

    private void NouveauCompteRendu()
    {
        CompteRendu = null;
        var engagement = Engagements.FirstOrDefault(e => e.Dto.Membres().Contains(SaphirSession.Agent?.Nom ?? "\u0000")) ?? Engagements.FirstOrDefault();
        CrEdition = new CompteRenduEdition();
        _crEquipage = null;
        CrEquipage = engagement;
        if (engagement is null)
            OnPropertyChanged(nameof(CrEquipage));
    }

    private void OuvrirCompteRendu(CompteRenduDto cr)
    {
        CompteRendu = cr;
        CrEdition = CompteRenduEdition.Depuis(cr);
        _crEquipage = cr.EquipageId is { } eid ? Engagements.FirstOrDefault(e => e.Dto.EquipageId == eid) : null;
        OnPropertyChanged(nameof(CrEquipage));
    }

    private async Task EnregistrerCompteRenduAsync()
    {
        if (Intervention is not { } i)
            return;
        await Host.Executer(async () =>
        {
            var input = CrEdition.VersInput(CrEquipage?.Dto.EquipageId, CompteRendu?.Version);
            var cr = CompteRendu is null
                ? await Client.PostAsync<CompteRenduDto>($"api/pegase/interventions/{i.Id}/comptes-rendus", input)
                : await Client.SendAsync<CompteRenduDto>(HttpMethod.Put, $"api/pegase/comptes-rendus/{CompteRendu.Id}", input);
            OuvrirCompteRendu(cr);
            await ChargerInterventionAsync(i.Id, false);
        }, "Compte rendu enregistré (brouillon).");
    }

    private async Task ValiderCompteRenduAsync()
    {
        if (Intervention is not { } i || CompteRendu is not { } cr)
            return;
        if (!await Host.Confirmer("Validation du compte rendu",
                "Valider le compte rendu ? Il sera figé ; l'équipage concerné terminera sa mission s'il est encore engagé.", "Valider"))
            return;
        await Host.Executer(async () =>
        {
            // Dernières saisies enregistrées avant validation.
            var enregistre = await Client.SendAsync<CompteRenduDto>(HttpMethod.Put, $"api/pegase/comptes-rendus/{cr.Id}",
                CrEdition.VersInput(CrEquipage?.Dto.EquipageId, cr.Version));
            var valide = await Client.PostAsync<CompteRenduDto>($"api/pegase/comptes-rendus/{enregistre.Id}/valider", new { });
            OuvrirCompteRendu(valide);
            await ChargerInterventionAsync(i.Id, false);
        }, "Compte rendu validé. Tu peux maintenant proposer une mention de main courante ou ouvrir une procédure LRPPN (onglet Suites).");
    }

    private async Task ReferencesAsync()
    {
        if (Intervention is not { } i)
            return;
        await Host.Executer(async () =>
            await RecevoirAsync(await Client.SendAsync<InterventionDto>(HttpMethod.Put, $"api/pegase/interventions/{i.Id}/references",
                new ReferencesInput(Fmt.Vide(ProcedureLrppn), Fmt.Vide(Suites), Fmt.Vide(Documents)))), "Références enregistrées.");
    }
}

internal static class EngagementExtensions
{
    /// <summary>Noms des agents de l'engagement (pour proposer l'équipage de l'agent connecté).</summary>
    public static string Membres(this EngagementDto engagement) => (engagement.Agents ?? string.Empty) + " " + (engagement.Chef ?? string.Empty);
}
