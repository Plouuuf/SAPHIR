using System.Collections.ObjectModel;
using System.Globalization;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Complements;

namespace Saphir.ViewModels.Apps.Complements;

/// <summary>Ligne de la liste des PVe.</summary>
public sealed class PveRow(PveResumeDto dto)
{
    public PveResumeDto Dto { get; } = dto;
    public long Id => Dto.Id;
    public string Titre => $"{Dto.Reference} — NATINF {Dto.Natinf}";
    public string Infraction => Dto.Infraction;
    public string Detail => string.Join(" · ", new[]
    {
        ComplementsViewModelBase.Date(Dto.DateInfraction), Dto.Contrevenant, Dto.Immatriculation, Dto.Commune, Dto.Agent
    }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public string Statut => Dto.Statut;
    public string Avis => Dto.NumeroAvis is { } n ? "Avis " + n : string.Empty;
    public string Montant => PveTexte.Euros(Dto.MontantForfaitaire);
    public bool Valide => Dto.Statut == PveStatuts.Valide;
    public bool Brouillon => Dto.Statut == PveStatuts.Brouillon;
    public bool Abandonne => Dto.Statut == PveStatuts.Abandonne;
}

/// <summary>Infraction du référentiel affichée.</summary>
public sealed class InfractionRow(InfractionDto dto)
{
    public InfractionDto Dto { get; } = dto;
    public long Id => Dto.Id;
    public string Titre => $"{Dto.Natinf} — {Dto.Libelle}";
    public string Detail => $"{PveTexte.Classe(Dto.Classe)} · {PveTexte.Euros(Dto.MontantForfaitaire)}"
                            + (Dto.MontantMinore is { } m ? $" (minorée {PveTexte.Euros(m)})" : string.Empty)
                            + (Dto.Points > 0 ? $" · {Dto.Points} point(s)" : " · sans retrait de points")
                            + (Dto.TexteReference is { } t ? " · " + t : string.Empty);
    public string Categorie => Dto.Categorie;
    public bool Inactive => !Dto.Actif;
}

/// <summary>Véhicule proposé par la recherche (module Judiciaire).</summary>
public sealed class VehiculeRow(VehiculeResumeDto dto)
{
    public VehiculeResumeDto Dto { get; } = dto;
    public string Titre => $"{Dto.Immatriculation} — {string.Join(" ", new[] { Dto.Marque, Dto.Modele, Dto.Couleur }.Where(s => !string.IsNullOrWhiteSpace(s)))}";
    public string Titulaire => Dto.Titulaire is { } t ? "Titulaire : " + t : "Titulaire inconnu";
    public string Alertes => Dto.Alertes.Count == 0 ? "Aucune alerte" : string.Join(" · ", Dto.Alertes);
    public bool HasAlertes => Dto.Alertes.Count > 0;
}

public sealed record MentionRow(string Entete, string Texte);

/// <summary>Mises en forme du PVe côté poste.</summary>
public static class PveTexte
{
    public static string Euros(decimal? montant)
        => montant is { } m ? m.ToString("N2", CultureInfo.GetCultureInfo("fr-FR")) + " €" : "—";

    public static string Classe(string classe) => classe switch
    {
        "AFD" => "Amende forfaitaire délictuelle",
        "1" => "Contravention de 1re classe",
        _ => $"Contravention de {classe}e classe"
    };
}

/// <summary>
/// PVe — procès-verbal électronique : rédaction (infraction du référentiel, contrevenant et véhicule recherchés dans
/// SAPHIR, lieu, circonstances, justificatifs), validation (numéro d'avis, acte figé), mentions, export, recherche,
/// référentiel des infractions (administration).
/// </summary>
public sealed class PveViewModel : ComplementsViewModelBase
{
    public const string SectionNouveau = "nouveau";
    public const string SectionMes = "mes";
    public const string SectionTous = "tous";
    public const string SectionInfractions = "infractions";
    public const string SectionFiche = "fiche";

    private string _sectionRetour = SectionMes;
    private List<InfractionDto> _infractions = [];
    private PveDto? _pve;
    private bool _nouveau;
    private string? _argument;

    public PveViewModel(string? argument = null)
    {
        _argument = argument ?? SaphirSession.TakeAppArgument(SaphirApps.Pve);
        AjouterSection(SectionNouveau, "Nouveau PVe");
        AjouterSection(SectionMes, "Mes PVe");
        AjouterSection(SectionTous, "Rechercher un PVe");
        AjouterSection(SectionInfractions, "Référentiel des infractions");
        AjouterSection(SectionFiche, "Procès-verbal", visible: false);

        RechercherCommand = new RelayCommand(() => _ = ChargerListeAsync());
        OuvrirCommand = new RelayCommand(p => { if (p is PveRow r) _ = OuvrirAsync(r.Id); });
        RetourCommand = new RelayCommand(() => Section = _sectionRetour);
        ChoisirInfractionCommand = new RelayCommand(p => { if (p is InfractionRow r) InfractionChoisie = r.Dto; });
        RechercherPersonneCommand = new RelayCommand(() => _ = RechercherPersonneAsync());
        ChoisirPersonneCommand = new RelayCommand(p => { if (p is PersonneRow r) ChoisirPersonne(r.Dto); });
        DelierPersonneCommand = new RelayCommand(() => { PersonneId = null; PersonneReference = null; RafraichirLiens(); });
        RechercherVehiculeCommand = new RelayCommand(() => _ = RechercherVehiculeAsync());
        ChoisirVehiculeCommand = new RelayCommand(p => { if (p is VehiculeRow r) _ = ChoisirVehiculeAsync(r.Dto); });
        DelierVehiculeCommand = new RelayCommand(() => { VehiculeId = null; RafraichirLiens(); });
        EnregistrerCommand = new RelayCommand(() => _ = EnregistrerAsync());
        ValiderCommand = new RelayCommand(() => _ = ValiderAsync());
        AbandonnerCommand = new RelayCommand(() => _ = AbandonnerAsync());
        AjouterJustificatifCommand = new RelayCommand(() => _ = AjouterJustificatifAsync());
        RetirerJustificatifCommand = new RelayCommand(p => { if (p is PieceJointeDto j) _ = RetirerJustificatifAsync(j); });
        EnregistrerJustificatifCommand = new RelayCommand(p => { if (p is PieceJointeDto j) _ = EnregistrerPieceAsync($"api/pve/justificatifs/{j.Id}"); });
        AjouterMentionCommand = new RelayCommand(() => _ = AjouterMentionAsync());
        ExporterCommand = new RelayCommand(() => _ = ExporterAsync());
        HistoriqueCommand = new RelayCommand(() => _ = HistoriqueAsync());
        NouvelleInfractionCommand = new RelayCommand(NouvelleInfraction);
        EnregistrerInfractionCommand = new RelayCommand(() => _ = EnregistrerInfractionAsync());

        Section = SectionMes;
        _ = DemarrerAsync();
    }

    public override string TitreMenu => "PVe";

    public bool IsListe => Section is SectionMes or SectionTous;
    public bool IsFiche => Section is SectionFiche or SectionNouveau;
    public bool IsInfractions => Section == SectionInfractions;
    public string TitreListe => Section == SectionMes ? "Mes procès-verbaux électroniques" : "Rechercher un procès-verbal électronique";

    protected override void OnSectionChanged()
    {
        foreach (var n in new[] { nameof(IsListe), nameof(IsFiche), nameof(IsInfractions), nameof(TitreListe) })
            OnPropertyChanged(n);
        if (IsListe)
        {
            _sectionRetour = Section;
            _ = ChargerListeAsync();
        }
        else if (Section == SectionNouveau)
        {
            NouveauPve();
        }
        else if (IsInfractions)
        {
            _ = ChargerInfractionsAsync();
        }
    }

    public bool CanGererReferentiel => SaphirSession.Has(Permissions.AdminParametres);

    // ===== Liste =====

    private string _recherche = string.Empty;
    private string _filtreStatut = "Tous";
    public ObservableCollection<PveRow> Liste { get; } = [];
    public IReadOnlyList<string> Statuts { get; } = ["Tous", .. PveStatuts.All];
    public string Recherche { get => _recherche; set => SetProperty(ref _recherche, value); }
    public string FiltreStatut { get => _filtreStatut; set { if (SetProperty(ref _filtreStatut, value ?? "Tous")) _ = ChargerListeAsync(); } }
    public bool ListeVide => Liste.Count == 0;

    // ===== Fiche =====

    public PveDto? Pve
    {
        get => _pve;
        private set
        {
            SetProperty(ref _pve, value);
            Justificatifs.Clear();
            Mentions.Clear();
            if (value is not null)
            {
                foreach (var j in value.Justificatifs) Justificatifs.Add(j);
                foreach (var m in value.Mentions) Mentions.Add(new MentionRow($"{Date(m.Date)} — {m.Auteur}", m.Texte));
            }
            RafraichirFiche();
        }
    }

    /// <summary>Formulaire modifiable : nouveau PVe ou brouillon de l'agent connecté.</summary>
    public bool Edition => _nouveau || (Pve is { Statut: PveStatuts.Brouillon } p && p.AgentId == SaphirSession.Agent?.Id);
    public bool Consultation => !Edition && Pve is not null;
    public bool Enregistre => Pve is not null;
    public bool PeutMention => Pve is { Statut: PveStatuts.Valide };
    public bool PeutJustificatifs => Edition && Pve is not null;
    public bool HasJustificatifs => Justificatifs.Count > 0;
    public bool HasMentions => Mentions.Count > 0;
    public bool AfficherMentions => PeutMention || HasMentions;
    public bool DoitEnregistrer => Edition && !Enregistre;
    public string TitreFiche => _nouveau ? "Nouveau procès-verbal électronique"
        : Pve is { } p ? $"{p.Reference}" + (p.NumeroAvis is { } n ? $" — avis n° {n}" : string.Empty) : string.Empty;
    public string StatutFiche => Pve?.Statut ?? PveStatuts.Brouillon;
    public bool FicheValide => Pve?.Statut == PveStatuts.Valide;
    public bool FicheAbandonnee => Pve?.Statut == PveStatuts.Abandonne;
    public string InfosFiche => Pve is { } p
        ? $"Créé le {Date(p.CreeLe)}" + (p.ValideLe is { } v ? $" — validé le {Date(v)}" : string.Empty)
          + (p.MotifAbandon is { } m ? $" — abandonné : {m}" : string.Empty)
        : "Les informations du rédacteur proviennent du module RH ; elles sont conservées dans l'acte.";

    // Rédacteur
    public string RedacteurNom => Pve?.Agent ?? (SaphirSession.Agent is { } a ? $"{a.Nom} {a.Prenom}" : "—");
    public string RedacteurRio => (Pve is null ? SaphirSession.Agent?.Rio : Pve.AgentRio) ?? "non attribué";
    public string RedacteurGrade => (Pve is null ? SaphirSession.Agent?.Grade : Pve.AgentGrade) ?? "non renseigné";
    public string RedacteurService => string.Join(" · ", new[]
    {
        Pve is null ? SaphirSession.Agent?.Service : Pve.AgentService,
        Pve is null ? SaphirSession.Agent?.Unite : Pve.AgentUnite
    }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } t ? t : "non renseigné";

    // Champs du formulaire
    private string? _jour, _heure, _lieu, _commune, _intervention, _circonstances, _observations;
    private string? _nom, _prenom, _naissance, _lieuNaissance, _adresse, _immatriculation, _marque, _modele, _couleur;
    public string? Jour { get => _jour; set => SetProperty(ref _jour, value); }
    public string? Heure { get => _heure; set => SetProperty(ref _heure, value); }
    public string? Lieu { get => _lieu; set => SetProperty(ref _lieu, value); }
    public string? Commune { get => _commune; set => SetProperty(ref _commune, value); }
    public string? InterventionNumero { get => _intervention; set => SetProperty(ref _intervention, value); }
    public string? Circonstances { get => _circonstances; set => SetProperty(ref _circonstances, value); }
    public string? Observations { get => _observations; set => SetProperty(ref _observations, value); }
    public string? Nom { get => _nom; set => SetProperty(ref _nom, value); }
    public string? Prenom { get => _prenom; set => SetProperty(ref _prenom, value); }
    public string? Naissance { get => _naissance; set => SetProperty(ref _naissance, value); }
    public string? LieuNaissance { get => _lieuNaissance; set => SetProperty(ref _lieuNaissance, value); }
    public string? Adresse { get => _adresse; set => SetProperty(ref _adresse, value); }
    public string? Immatriculation { get => _immatriculation; set => SetProperty(ref _immatriculation, value); }
    public string? Marque { get => _marque; set => SetProperty(ref _marque, value); }
    public string? Modele { get => _modele; set => SetProperty(ref _modele, value); }
    public string? Couleur { get => _couleur; set => SetProperty(ref _couleur, value); }

    public long? PersonneId { get; private set; }
    public string? PersonneReference { get; private set; }
    public long? VehiculeId { get; private set; }
    public bool HasPersonneLiee => PersonneReference is not null;
    public bool HasVehiculeLie => VehiculeId is not null;
    public string PersonneLiee => PersonneReference is { } r ? $"Identité SAPHIR liée : {r}" : string.Empty;

    private void RafraichirLiens()
    {
        OnPropertyChanged(nameof(HasPersonneLiee));
        OnPropertyChanged(nameof(HasVehiculeLie));
        OnPropertyChanged(nameof(PersonneLiee));
    }

    // Infraction
    private string _rechercheInfraction = string.Empty;
    private InfractionDto? _infractionChoisie;
    public ObservableCollection<InfractionRow> InfractionsProposees { get; } = [];
    public string RechercheInfraction { get => _rechercheInfraction; set { if (SetProperty(ref _rechercheInfraction, value)) FiltrerInfractions(); } }

    public InfractionDto? InfractionChoisie
    {
        get => _infractionChoisie;
        set
        {
            if (!SetProperty(ref _infractionChoisie, value)) return;
            foreach (var n in new[] { nameof(HasInfraction), nameof(InfractionTitre), nameof(InfractionClasse), nameof(InfractionMontants), nameof(InfractionPoints), nameof(InfractionTexte) })
                OnPropertyChanged(n);
        }
    }

    public bool HasInfraction => InfractionChoisie is not null;
    public string InfractionTitre => InfractionChoisie is { } i ? $"NATINF {i.Natinf} — {i.Libelle}" : string.Empty;
    public string InfractionTexte => InfractionChoisie?.TexteReference ?? string.Empty;
    public string InfractionClasse => InfractionChoisie is { } i ? PveTexte.Classe(i.Classe) : string.Empty;
    public string InfractionMontants => InfractionChoisie is { } i
        ? $"Forfaitaire {PveTexte.Euros(i.MontantForfaitaire)} · minorée {PveTexte.Euros(i.MontantMinore)} · majorée {PveTexte.Euros(i.MontantMajore)}"
        : string.Empty;
    public string InfractionPoints => InfractionChoisie is { Points: > 0 } i ? $"Retrait de {i.Points} point(s)" : "Sans retrait de points";

    // Recherches liées
    private string _recherchePersonne = string.Empty;
    private string _rechercheVehicule = string.Empty;
    public string RecherchePersonne { get => _recherchePersonne; set => SetProperty(ref _recherchePersonne, value); }
    public string RechercheVehicule { get => _rechercheVehicule; set => SetProperty(ref _rechercheVehicule, value); }
    public ObservableCollection<PersonneRow> Personnes { get; } = [];
    public ObservableCollection<VehiculeRow> Vehicules { get; } = [];

    // Justificatifs, mentions, validation
    public ObservableCollection<PieceJointeDto> Justificatifs { get; } = [];
    public ObservableCollection<MentionRow> Mentions { get; } = [];
    public ObservableCollection<HistoriqueNoteRow> Historique { get; } = [];
    private string? _nouvelleMention;
    private bool _certifie;
    private bool _historiqueVisible;
    public string? NouvelleMention { get => _nouvelleMention; set => SetProperty(ref _nouvelleMention, value); }
    private string? _motifAbandon;
    public string? MotifAbandon { get => _motifAbandon; set => SetProperty(ref _motifAbandon, value); }
    public bool Certifie { get => _certifie; set => SetProperty(ref _certifie, value); }
    public bool HistoriqueVisible { get => _historiqueVisible; set => SetProperty(ref _historiqueVisible, value); }

    // ===== Référentiel =====

    private string _rechercheReferentiel = string.Empty;
    private InfractionRow? _infractionEditee;
    private bool _nouvelleInfraction;
    public ObservableCollection<InfractionRow> Referentiel { get; } = [];
    public string RechercheReferentiel { get => _rechercheReferentiel; set { if (SetProperty(ref _rechercheReferentiel, value)) FiltrerReferentiel(); } }
    public IReadOnlyList<string> Classes { get; } = ["1", "2", "3", "4", "5", "AFD"];

    public InfractionRow? InfractionEditee
    {
        get => _infractionEditee;
        set
        {
            if (!SetProperty(ref _infractionEditee, value)) return;
            if (value is not null) RemplirInfraction(value.Dto);
            OnPropertyChanged(nameof(EditionInfractionVisible));
        }
    }

    public bool EditionInfractionVisible => CanGererReferentiel && (InfractionEditee is not null || _nouvelleInfraction);
    private string _iNatinf = string.Empty, _iLibelle = string.Empty, _iCategorie = string.Empty, _iClasse = "4";
    private string? _iTexte, _iForfait, _iMinore, _iMajore, _iPoints, _iOrdre;
    private bool _iActif = true;
    public string INatinf { get => _iNatinf; set => SetProperty(ref _iNatinf, value); }
    public string ILibelle { get => _iLibelle; set => SetProperty(ref _iLibelle, value); }
    public string ICategorie { get => _iCategorie; set => SetProperty(ref _iCategorie, value); }
    public string IClasse { get => _iClasse; set => SetProperty(ref _iClasse, value); }
    public string? ITexte { get => _iTexte; set => SetProperty(ref _iTexte, value); }
    public string? IForfait { get => _iForfait; set => SetProperty(ref _iForfait, value); }
    public string? IMinore { get => _iMinore; set => SetProperty(ref _iMinore, value); }
    public string? IMajore { get => _iMajore; set => SetProperty(ref _iMajore, value); }
    public string? IPoints { get => _iPoints; set => SetProperty(ref _iPoints, value); }
    public string? IOrdre { get => _iOrdre; set => SetProperty(ref _iOrdre, value); }
    public bool IActif { get => _iActif; set => SetProperty(ref _iActif, value); }

    // ===== Commandes =====

    public RelayCommand RechercherCommand { get; }
    public RelayCommand OuvrirCommand { get; }
    public RelayCommand RetourCommand { get; }
    public RelayCommand ChoisirInfractionCommand { get; }
    public RelayCommand RechercherPersonneCommand { get; }
    public RelayCommand ChoisirPersonneCommand { get; }
    public RelayCommand DelierPersonneCommand { get; }
    public RelayCommand RechercherVehiculeCommand { get; }
    public RelayCommand ChoisirVehiculeCommand { get; }
    public RelayCommand DelierVehiculeCommand { get; }
    public RelayCommand EnregistrerCommand { get; }
    public RelayCommand ValiderCommand { get; }
    public RelayCommand AbandonnerCommand { get; }
    public RelayCommand AjouterJustificatifCommand { get; }
    public RelayCommand RetirerJustificatifCommand { get; }
    public RelayCommand EnregistrerJustificatifCommand { get; }
    public RelayCommand AjouterMentionCommand { get; }
    public RelayCommand ExporterCommand { get; }
    public RelayCommand HistoriqueCommand { get; }
    public RelayCommand NouvelleInfractionCommand { get; }
    public RelayCommand EnregistrerInfractionCommand { get; }

    // ===== Chargement =====

    private async Task DemarrerAsync()
    {
        await ChargerInfractionsAsync();
        if (_argument is { } arg)
        {
            _argument = null;
            await TraiterArgumentAsync(arg);
        }
    }

    protected override string? IdApplication => SaphirApps.Pve;

    protected override void OnArgument(string argument) => _ = TraiterArgumentAsync(argument);

    /// <summary>« pve:12 », « PVE-2026-000004 », numéro d'avis, ou « nouveau ».</summary>
    private async Task TraiterArgumentAsync(string arg)
    {
        if (arg.StartsWith("pve:", StringComparison.Ordinal) && long.TryParse(arg[4..], out var id))
            await OuvrirAsync(id);
        else if (arg == "nouveau")
            Section = SectionNouveau;
        else if (arg.StartsWith("PVE-", StringComparison.OrdinalIgnoreCase) || arg.All(char.IsAsciiDigit))
            await OuvrirReferenceAsync(arg);
    }

    private async Task ChargerListeAsync()
    {
        var api = Api;
        if (api is null || !IsListe) return;
        await RunAsync(async () =>
        {
            var route = $"api/pve?q={ApiClient.Q(Recherche.Trim())}&mien={(Section == SectionMes ? "true" : "false")}";
            if (FiltreStatut != "Tous") route += "&statut=" + ApiClient.Q(FiltreStatut);
            var liste = await api.GetAsync<List<PveResumeDto>>(route);
            Liste.Clear();
            foreach (var p in liste) Liste.Add(new PveRow(p));
            OnPropertyChanged(nameof(ListeVide));
        });
    }

    private async Task ChargerInfractionsAsync()
    {
        var api = Api;
        if (api is null) return;
        await RunAsync(async () =>
        {
            _infractions = await api.GetAsync<List<InfractionDto>>("api/pve/infractions" + (CanGererReferentiel ? "?tous=true" : string.Empty));
            FiltrerInfractions();
            FiltrerReferentiel();
        });
    }

    private void FiltrerInfractions()
    {
        InfractionsProposees.Clear();
        var r = RechercheInfraction.Trim();
        foreach (var i in _infractions.Where(i => i.Actif && (r.Length == 0
                                                  || i.Natinf.Contains(r, StringComparison.OrdinalIgnoreCase)
                                                  || i.Libelle.Contains(r, StringComparison.OrdinalIgnoreCase)
                                                  || i.Categorie.Contains(r, StringComparison.OrdinalIgnoreCase))).Take(40))
            InfractionsProposees.Add(new InfractionRow(i));
    }

    private void FiltrerReferentiel()
    {
        var id = InfractionEditee?.Id;
        Referentiel.Clear();
        var r = RechercheReferentiel.Trim();
        foreach (var i in _infractions.Where(i => r.Length == 0
                                                  || i.Natinf.Contains(r, StringComparison.OrdinalIgnoreCase)
                                                  || i.Libelle.Contains(r, StringComparison.OrdinalIgnoreCase)
                                                  || i.Categorie.Contains(r, StringComparison.OrdinalIgnoreCase)))
            Referentiel.Add(new InfractionRow(i));
        if (id is { } x) InfractionEditee = Referentiel.FirstOrDefault(i => i.Id == x);
    }

    private async Task OuvrirAsync(long id)
    {
        var api = Api;
        if (api is null) return;
        await RunAsync(async () => Afficher(await api.GetAsync<PveDto>($"api/pve/{id}")));
    }

    private async Task OuvrirReferenceAsync(string reference)
    {
        var api = Api;
        if (api is null) return;
        await RunAsync(async () => Afficher(await api.GetAsync<PveDto>($"api/pve/reference/{ApiClient.Q(reference.Trim())}")));
    }

    private void Afficher(PveDto pve)
    {
        _nouveau = false;
        Pve = pve;
        RemplirFormulaire(pve);
        HistoriqueVisible = false;
        if (Section != SectionFiche) Section = SectionFiche;
        else RafraichirFiche();
    }

    private void NouveauPve()
    {
        _nouveau = true;
        Pve = null;
        var maintenant = DateTime.Now;
        Jour = maintenant.ToString("dd/MM/yyyy", Fr);
        Heure = maintenant.ToString("HH:mm", Fr);
        Lieu = Commune = InterventionNumero = Circonstances = Observations = null;
        Nom = Prenom = Naissance = LieuNaissance = Adresse = Immatriculation = Marque = Modele = Couleur = null;
        PersonneId = null;
        PersonneReference = null;
        VehiculeId = null;
        InfractionChoisie = null;
        RechercheInfraction = string.Empty;
        RecherchePersonne = string.Empty;
        RechercheVehicule = string.Empty;
        Personnes.Clear();
        Vehicules.Clear();
        Certifie = false;
        _sectionRetour = SectionMes;
        RafraichirLiens();
        RafraichirFiche();
    }

    private void RemplirFormulaire(PveDto p)
    {
        var local = p.DateInfraction.ToLocalTime();
        Jour = local.ToString("dd/MM/yyyy", Fr);
        Heure = local.ToString("HH:mm", Fr);
        Lieu = p.Lieu;
        Commune = p.Commune;
        InterventionNumero = p.InterventionNumero;
        Circonstances = p.Circonstances;
        Observations = p.Observations;
        Nom = p.ContrevenantNom;
        Prenom = p.ContrevenantPrenom;
        Naissance = p.ContrevenantNaissance?.ToString("dd/MM/yyyy", Fr);
        LieuNaissance = p.ContrevenantLieuNaissance;
        Adresse = p.ContrevenantAdresse;
        Immatriculation = p.Immatriculation;
        Marque = p.VehiculeMarque;
        Modele = p.VehiculeModele;
        Couleur = p.VehiculeCouleur;
        PersonneId = p.PersonneId;
        PersonneReference = p.PersonneReference;
        VehiculeId = p.VehiculeId;
        InfractionChoisie = _infractions.FirstOrDefault(i => i.Id == p.InfractionId)
                            ?? new InfractionDto(p.InfractionId, p.Natinf, p.Infraction, p.TexteReference, string.Empty, p.Classe,
                                p.MontantForfaitaire, p.MontantMinore, p.MontantMajore, p.Points, true, 0);
        Personnes.Clear();
        Vehicules.Clear();
        Certifie = false;
        NouvelleMention = null;
        MotifAbandon = null;
        RafraichirLiens();
    }

    private void RafraichirFiche()
    {
        foreach (var n in new[]
                 {
                     nameof(Edition), nameof(Consultation), nameof(Enregistre), nameof(PeutMention), nameof(PeutJustificatifs),
                     nameof(HasJustificatifs), nameof(HasMentions), nameof(TitreFiche), nameof(StatutFiche), nameof(FicheValide),
                     nameof(FicheAbandonnee), nameof(InfosFiche), nameof(RedacteurNom), nameof(RedacteurRio), nameof(RedacteurGrade),
                     nameof(RedacteurService), nameof(AfficherMentions), nameof(DoitEnregistrer)
                 })
            OnPropertyChanged(n);
    }

    // ===== Contrevenant et véhicule =====

    private async Task RechercherPersonneAsync()
    {
        var api = Api;
        if (api is null) return;
        var q = string.IsNullOrWhiteSpace(RecherchePersonne) ? $"{Nom} {Prenom}".Trim() : RecherchePersonne.Trim();
        if (q.Length < 2)
        {
            ErrorText = "Saisis au moins deux caractères (nom, prénom ou identifiant PER-…).";
            return;
        }
        await RunAsync(async () =>
        {
            var liste = await api.GetAsync<List<PersonneResumeDto>>("api/personnes/recherche?q=" + ApiClient.Q(q));
            Personnes.Clear();
            foreach (var p in liste) Personnes.Add(new PersonneRow(p, null));
            if (Personnes.Count == 0) InfoText = "Aucune personne trouvée : saisis l'identité déclarée.";
        });
    }

    private void ChoisirPersonne(PersonneResumeDto p)
    {
        PersonneId = p.Id;
        PersonneReference = p.Reference;
        Nom = p.Nom;
        Prenom = p.Prenom;
        Naissance = p.DateNaissance?.ToString("dd/MM/yyyy", Fr);
        LieuNaissance = p.LieuNaissance;
        Personnes.Clear();
        RafraichirLiens();
        if (p.Alertes.Count > 0)
            InfoText = $"Attention : {p.Reference} fait l'objet d'alertes ({string.Join(", ", p.Alertes)}). Consulte CHEOPS selon tes habilitations.";
    }

    private async Task RechercherVehiculeAsync()
    {
        var api = Api;
        if (api is null) return;
        var q = string.IsNullOrWhiteSpace(RechercheVehicule) ? Immatriculation?.Trim() ?? string.Empty : RechercheVehicule.Trim();
        if (q.Length < 2)
        {
            ErrorText = "Saisis l'immatriculation (au moins deux caractères).";
            return;
        }
        await RunAsync(async () =>
        {
            var liste = await api.GetAsync<List<VehiculeResumeDto>>("api/vehicules/recherche?q=" + ApiClient.Q(q));
            Vehicules.Clear();
            foreach (var v in liste) Vehicules.Add(new VehiculeRow(v));
            if (Vehicules.Count == 0) InfoText = "Aucun véhicule trouvé : saisis les caractéristiques constatées.";
        });
    }

    private async Task ChoisirVehiculeAsync(VehiculeResumeDto v)
    {
        VehiculeId = v.Id;
        Immatriculation = v.Immatriculation;
        Marque = v.Marque;
        Modele = v.Modele;
        Couleur = v.Couleur;
        Vehicules.Clear();
        RafraichirLiens();
        if (v.Alertes.Count > 0)
            InfoText = $"Attention : véhicule signalé ({string.Join(", ", v.Alertes)}).";
        // Sans contrevenant identifié (stationnement), le titulaire est proposé comme contrevenant.
        var api = Api;
        if (api is not null && v.TitulaireId is { } titulaire && PersonneId is null && string.IsNullOrWhiteSpace(Nom))
        {
            try
            {
                ChoisirPersonne(await api.GetAsync<PersonneResumeDto>($"api/personnes/{titulaire}/resume"));
            }
            catch (ApiException)
            {
                Nom = v.Titulaire;
            }
        }
    }

    // ===== Enregistrement, validation, abandon =====

    private PveInput Saisie()
    {
        if (InfractionChoisie is not { } infraction)
            throw new ArgumentException("Choisis l'infraction relevée dans le référentiel.");
        return new PveInput(infraction.Id, LireInstant(Jour, Heure, "Date de l'infraction"), Lieu ?? string.Empty, Commune,
            PersonneId, PersonneReference, Nom, Prenom, LireJour(Naissance, "Date de naissance"), LieuNaissance, Adresse,
            VehiculeId, Immatriculation, Marque, Modele, Couleur, Circonstances, Observations, InterventionNumero, Pve?.Version ?? 0);
    }

    private async Task<PveDto?> EnregistrerInterneAsync(ApiClient api)
    {
        var input = Saisie();
        var dto = Pve is { } p
            ? await api.SendAsync<PveDto>(HttpMethod.Put, $"api/pve/{p.Id}", input)
            : await api.PostAsync<PveDto>("api/pve", input);
        _nouveau = false;
        Pve = dto;
        if (Section != SectionFiche)
        {
            _sectionRetour = SectionMes;
            Section = SectionFiche;
        }
        return dto;
    }

    private async Task EnregistrerAsync()
    {
        var api = Api;
        if (api is null || !Edition) return;
        await RunAsync(async () => { await EnregistrerInterneAsync(api); }, "Brouillon du PVe enregistré.");
    }

    private async Task ValiderAsync()
    {
        var api = Api;
        if (api is null || !Edition) return;
        if (!Certifie)
        {
            ErrorText = "Coche la certification des constatations avant de valider.";
            return;
        }
        if (!await ConfirmAsync("Validation du PVe",
                "Valider ce procès-verbal ? Un numéro d'avis sera attribué et l'acte ne pourra plus être modifié (seules des mentions pourront être ajoutées).",
                "Valider"))
            return;
        await RunAsync(async () =>
        {
            var dto = await EnregistrerInterneAsync(api) ?? throw new ArgumentException("PVe introuvable.");
            var valide = await api.PostAsync<PveDto>($"api/pve/{dto.Id}/valider", new PveValidationInput(dto.Version, true));
            Pve = valide;
        }, "PVe validé : numéro d'avis attribué.");
    }

    private async Task AbandonnerAsync()
    {
        var api = Api;
        if (api is null || Pve is not { } p) return;
        if (string.IsNullOrWhiteSpace(MotifAbandon))
        {
            ErrorText = "Indique le motif de l'abandon avant d'abandonner le brouillon.";
            return;
        }
        if (!await ConfirmAsync("Abandon du brouillon", $"Abandonner le brouillon {p.Reference} ? Il restera consultable avec le motif indiqué.", "Abandonner", danger: true))
            return;
        await RunAsync(async () =>
        {
            Pve = await api.PostAsync<PveDto>($"api/pve/{p.Id}/abandonner", new PveAbandonInput(MotifAbandon!, p.Version));
        }, "Brouillon abandonné.");
    }

    private async Task AjouterJustificatifAsync()
    {
        var api = Api;
        if (api is null || Pve is not { } p) return;
        if (Hote is null)
        {
            ErrorText = "L'ajout de fichiers n'est pas disponible sur ce poste.";
            return;
        }
        await RunAsync(async () =>
        {
            var fichier = await Hote.ChoisirFichierAsync("Ajouter un justificatif", PiecesJointes.Extensions);
            if (fichier is null) return;
            Pve = await api.PostAsync<PveDto>($"api/pve/{p.Id}/justificatifs", await LirePieceAsync(fichier));
        }, "Justificatif ajouté.");
    }

    private async Task RetirerJustificatifAsync(PieceJointeDto j)
    {
        var api = Api;
        if (api is null) return;
        if (!await ConfirmAsync("Justificatif", $"Retirer « {j.Nom} » du PVe ?", "Retirer"))
            return;
        await RunAsync(async () => Pve = await api.SendAsync<PveDto>(HttpMethod.Delete, $"api/pve/justificatifs/{j.Id}", null));
    }

    private async Task AjouterMentionAsync()
    {
        var api = Api;
        if (api is null || Pve is not { } p) return;
        if (string.IsNullOrWhiteSpace(NouvelleMention))
        {
            ErrorText = "Saisis le texte de la mention.";
            return;
        }
        await RunAsync(async () =>
        {
            Pve = await api.PostAsync<PveDto>($"api/pve/{p.Id}/mentions", new PveMentionInput(NouvelleMention!));
            NouvelleMention = null;
        }, "Mention ajoutée.");
    }

    private async Task ExporterAsync()
    {
        var api = Api;
        if (api is null || Pve is not { } p) return;
        if (Hote is null)
        {
            ErrorText = "L'enregistrement de fichiers n'est pas disponible sur ce poste.";
            return;
        }
        await RunAsync(async () =>
        {
            var export = await api.GetAsync<PveExportDto>($"api/pve/{p.Id}/export");
            if (await Hote.EnregistrerFichierAsync(export.NomFichier, System.Text.Encoding.UTF8.GetBytes(export.Contenu)))
                InfoText = $"PVe exporté ({export.NomFichier}) : ouvre-le dans un navigateur pour l'imprimer.";
        });
    }

    private async Task HistoriqueAsync()
    {
        var api = Api;
        if (api is null || Pve is not { } p) return;
        if (HistoriqueVisible)
        {
            HistoriqueVisible = false;
            return;
        }
        await RunAsync(async () =>
        {
            var lignes = await api.GetAsync<List<HistoriqueDto>>($"api/pve/{p.Id}/historique");
            Historique.Clear();
            foreach (var h in lignes)
                Historique.Add(new HistoriqueNoteRow(Date(h.Date), h.Auteur, h.Action,
                    string.Join(" — ", new[] { h.Champ is null ? null : $"{h.Champ} : {h.Ancienne ?? "—"} → {h.Nouvelle ?? "—"}", h.Champ is null ? h.Nouvelle : null, h.Motif }
                        .Where(s => !string.IsNullOrWhiteSpace(s)))));
            HistoriqueVisible = true;
        });
    }

    // ===== Référentiel des infractions =====

    private void NouvelleInfraction()
    {
        _nouvelleInfraction = true;
        InfractionEditee = null;
        INatinf = ILibelle = ICategorie = string.Empty;
        IClasse = "4";
        ITexte = IForfait = IMinore = IMajore = null;
        IPoints = "0";
        IOrdre = "1000";
        IActif = true;
        OnPropertyChanged(nameof(EditionInfractionVisible));
    }

    private void RemplirInfraction(InfractionDto i)
    {
        _nouvelleInfraction = false;
        INatinf = i.Natinf;
        ILibelle = i.Libelle;
        ICategorie = i.Categorie;
        IClasse = i.Classe;
        ITexte = i.TexteReference;
        IForfait = i.MontantForfaitaire.ToString("0.00", Fr);
        IMinore = i.MontantMinore?.ToString("0.00", Fr);
        IMajore = i.MontantMajore?.ToString("0.00", Fr);
        IPoints = i.Points.ToString(Fr);
        IOrdre = i.Ordre.ToString(Fr);
        IActif = i.Actif;
    }

    private static decimal? Montant(string? texte, string libelle, bool obligatoire)
    {
        if (string.IsNullOrWhiteSpace(texte))
            return obligatoire ? throw new ArgumentException($"{libelle} : montant obligatoire.") : null;
        var t = texte.Replace("€", string.Empty, StringComparison.Ordinal).Replace('.', ',').Trim();
        return decimal.TryParse(t, NumberStyles.Number, Fr, out var m) ? m : throw new ArgumentException($"{libelle} : montant invalide.");
    }

    private async Task EnregistrerInfractionAsync()
    {
        var api = Api;
        if (api is null || !CanGererReferentiel) return;
        var existante = _nouvelleInfraction ? null : InfractionEditee;
        await RunAsync(async () =>
        {
            var input = new InfractionInput(INatinf, ILibelle, ITexte, ICategorie, IClasse,
                Montant(IForfait, "Montant forfaitaire", true)!.Value, Montant(IMinore, "Montant minoré", false), Montant(IMajore, "Montant majoré", false),
                int.TryParse(IPoints, out var points) ? points : throw new ArgumentException("Points : nombre entier attendu."),
                IActif, int.TryParse(IOrdre, out var ordre) ? ordre : 1000);
            var dto = existante is null
                ? await api.PostAsync<InfractionDto>("api/pve/infractions", input)
                : await api.SendAsync<InfractionDto>(HttpMethod.Put, $"api/pve/infractions/{existante.Id}", input);
            _nouvelleInfraction = false;
            _infractions = await api.GetAsync<List<InfractionDto>>("api/pve/infractions?tous=true");
            FiltrerInfractions();
            _infractionEditee = null;
            FiltrerReferentiel();
            InfractionEditee = Referentiel.FirstOrDefault(i => i.Id == dto.Id);
        }, "Infraction enregistrée dans le référentiel.");
    }

    // ===== Temps réel =====

    protected override void OnRealtime(RealtimeMessage message)
    {
        if (message.Type != ComplementsRealtime.Pve) return;
        if (IsListe) _ = ChargerListeAsync();
        if (Consultation && Pve is { } p && long.TryParse(message.Text, out var id) && id == p.Id)
            _ = OuvrirAsync(id);
    }
}
