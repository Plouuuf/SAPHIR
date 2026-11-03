using System.Collections.ObjectModel;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Judiciaire;

namespace Saphir.ViewModels.Apps.Judiciaire;

public sealed record PersonneProcedureLigne(ProcedurePersonneDto Dto)
{
    public string Reference => Dto.Personne.Reference;
    public string Identite => Dto.Personne.Libelle;
    public string Qualite => Dto.Qualite;
    public string Alertes => FormatsJ.Alertes(Dto.Personne.Alertes);
    public string Observations => Dto.Observations ?? string.Empty;
    public string Ajoute => FormatsJ.DateHeure(Dto.AjouteLe);
}

public sealed record VehiculeProcedureLigne(ProcedureVehiculeDto Dto)
{
    public string Immatriculation => Dto.Vehicule.Immatriculation;
    public string Vehicule => $"{Dto.Vehicule.Marque} {Dto.Vehicule.Modele} {Dto.Vehicule.Couleur}".Trim();
    public string Titulaire => Dto.Vehicule.Titulaire ?? "—";
    public string Role => Dto.Role;
    public string Alertes => FormatsJ.Alertes(Dto.Vehicule.Alertes);
}

public sealed record ValidationLigne(ValidationDto Dto)
{
    public string Numero => Dto.Numero;
    public string Titre => Dto.Titre;
    public string Date => FormatsJ.DateHeure(Dto.ValideLe);
    public string ValidePar => Dto.ValidePar;
    public string Empreinte => Dto.Empreinte;
}

/// <summary>Document d'une procédure : acte validé (PDF figé) ou pièce jointe.</summary>
public sealed record DocumentLigne(string Nature, string Numero, string Libelle, string Date, string Chemin);

/// <summary>Dossier d'une procédure (13.4) : synthèse, personnes, actes, pièces, véhicules, objets, interventions, historique, validations, documents, exports.</summary>
public sealed class DossierViewModel : SousVueBase
{
    private readonly LrppnViewModel _lrppn;
    private ProcedureDto? _procedure;
    private PersonneProcedureLigne? _personneSelection;
    private VehiculeProcedureLigne? _vehiculeSelection;
    private ActeLigne? _acteSelection;
    private PieceLigne? _pieceSelection;
    private ProcedureLienDto? _lienSelection;
    private DocumentLigne? _documentSelection;
    private DossiersAssociesDto? _dossiers;
    private string _qualite = ProcedureReferentiels.QualitesPersonne[0];
    private string _role = ProcedureReferentiels.RolesVehicule[0];
    private string? _observations, _observationsVehicule, _numeroLien, _nouveauStatut, _motifStatut, _interventionTexte;
    private TypeActeInfo? _typeActe = ActesCatalogue.All[0];
    private PersonneProcedureLigne? _personneActe;
    private string _categorie = PieceCategories.Piece;
    private string? _libelle, _description, _scelle, _localisation, _fichierNom;
    private byte[]? _fichierContenu;
    private ActeLigne? _actePiece;
    private string _mouvement = PieceMouvements.All[0];
    private string? _destination, _observationsMouvement;

    public DossierViewModel(LrppnViewModel lrppn) : base(lrppn)
    {
        _lrppn = lrppn;
        Picker = new PersonnePickerViewModel(lrppn);
        VehiculePicker = new VehiculePickerViewModel(lrppn);

        ModifierCommand = new RelayCommand(() => { if (Procedure is { } p) { _lrppn.Formulaire.Modifier(p); _lrppn.AfficherFormulaire(); } });
        ChangerStatutCommand = new RelayCommand(() => _ = ChangerStatutAsync());
        LierCommand = new RelayCommand(() => _ = EnvoyerAsync(HttpMethod.Post, "liens", new ProcedureLienInput(NumeroLien ?? string.Empty), "Procédure liée."));
        DelierCommand = new RelayCommand(() => { if (LienSelection is { } l) _ = EnvoyerAsync(HttpMethod.Delete, $"liens/{l.Id}", null, "Lien supprimé."); });
        AjouterPersonneCommand = new RelayCommand(() => _ = AjouterPersonneAsync());
        RetirerPersonneCommand = new RelayCommand(() => _ = RetirerPersonneAsync());
        DossiersCommand = new RelayCommand(() => _ = ChargerDossiersAsync());
        AjouterVehiculeCommand = new RelayCommand(() => _ = AjouterVehiculeAsync());
        RetirerVehiculeCommand = new RelayCommand(() => { if (VehiculeSelection is { } v) _ = EnvoyerAsync(HttpMethod.Delete, $"vehicules/{v.Dto.Id}", null, "Véhicule retiré."); });
        NouvelActeCommand = new RelayCommand(() => _ = NouvelActeAsync());
        OuvrirActeCommand = new RelayCommand(() => { if (ActeSelection is { } a) _ = _lrppn.OuvrirActeAsync(a.Dto.Id); });
        JoindreCommand = new RelayCommand(() => _ = JoindreAsync());
        AjouterPieceCommand = new RelayCommand(() => _ = AjouterPieceAsync());
        MouvementCommand = new RelayCommand(() => _ = MouvementAsync());
        TelechargerPieceCommand = new RelayCommand(() => { if (PieceSelection is { AFichier: true } p) _ = Executer(() => _lrppn.TelechargerAsync($"api/lrppn/pieces/{p.Dto.Id}/fichier")); });
        TelechargerDocumentCommand = new RelayCommand(() => { if (DocumentSelection is { } d) _ = Executer(() => _lrppn.TelechargerAsync(d.Chemin)); });
        ExporterCommand = new RelayCommand(() => { if (Procedure is { } p) _ = Executer(() => _lrppn.TelechargerAsync($"api/lrppn/procedures/{p.Id}/pdf")); });
        ChargerHistoriqueCommand = new RelayCommand(() => _ = ChargerHistoriqueAsync());
        ChargerInterventionCommand = new RelayCommand(() => _ = ChargerInterventionAsync());
        RetourCommand = new RelayCommand(() => _lrppn.RetourListeCommand.Execute(null));
    }

    // ===== Droits et état =====

    public bool PeutRediger => SaphirSession.Has(Permissions.LrppnRediger) && EstOuverte;
    public bool PeutValider => SaphirSession.Has(Permissions.LrppnValider);
    public bool PeutPieces => SaphirSession.Has(Permissions.LrppnPieces) && EstOuverte;
    public bool EstOuverte => Procedure?.Statut is ProcedureStatuts.EnCours or ProcedureStatuts.Transmise;

    public ProcedureDto? Procedure
    {
        get => _procedure;
        private set
        {
            if (!SetProperty(ref _procedure, value))
                return;
            foreach (var nom in new[] { nameof(Titre), nameof(Administration), nameof(Faits), nameof(EstOuverte), nameof(PeutRediger), nameof(PeutPieces),
                         nameof(AIntervention), nameof(Statut) })
                OnPropertyChanged(nom);
        }
    }

    public string Titre => Procedure is { } p ? $"Procédure n° {p.Numero} — {p.Intitule}" : string.Empty;
    public string Statut => Procedure?.Statut ?? string.Empty;

    public string Administration => Procedure is { } p
        ? $"Numéro : {p.Numero}   Statut : {p.Statut}\nService : {p.Service ?? "—"}   Unité : {p.Unite ?? "—"}\n" +
          $"Agent responsable : {p.Responsable}{(p.ResponsableGrade is null ? string.Empty : ", " + p.ResponsableGrade)} — RIO {p.ResponsableRio ?? "non attribué"}" +
          $"{(p.QualiteFonctionnelle is null ? string.Empty : " — " + p.QualiteFonctionnelle)}\n" +
          $"Ouverte le {FormatsJ.DateHeure(p.OuverteLe)} — {p.Cadre} — {p.TypeAffaire}\n" +
          $"Référence PÉGASE : {p.InterventionNumero ?? "—"}   Référence main courante : {p.MainCouranteReference ?? "—"}\n" +
          $"Créée par {p.CreePar} le {FormatsJ.DateHeure(p.CreeLe)} — version {p.Version}"
        : string.Empty;

    public string Faits => Procedure is { } p
        ? $"Qualification envisagée : {p.Qualification ?? "—"}\nDate des faits : {FormatsJ.DateHeure(p.FaitsLe)}\n" +
          $"Lieu : {p.Adresse ?? "—"} {p.Commune ?? string.Empty}\nOrigine : {p.Origine ?? "—"}\n\n{p.Description ?? string.Empty}"
        : string.Empty;

    public ObservableCollection<PersonneProcedureLigne> PersonneLignes { get; } = [];
    public ObservableCollection<VehiculeProcedureLigne> VehiculeLignes { get; } = [];
    public ObservableCollection<ActeLigne> ActeLignes { get; } = [];
    public ObservableCollection<PieceLigne> PieceLignes { get; } = [];
    public ObservableCollection<PieceLigne> ObjetLignes { get; } = [];
    public ObservableCollection<ProcedureLienDto> Liens { get; } = [];
    public ObservableCollection<ValidationLigne> Validations { get; } = [];
    public ObservableCollection<DocumentLigne> Documents { get; } = [];
    public ObservableCollection<HistoriqueLigne> Historique { get; } = [];

    public RelayCommand RetourCommand { get; }
    public RelayCommand ModifierCommand { get; }
    public RelayCommand ExporterCommand { get; }

    public async Task ChargerAsync(long id)
    {
        if (Api is not { } api)
            return;
        await Executer(async () => Afficher(await api.GetAsync<ProcedureDto>($"api/lrppn/procedures/{id}")));
        if (Procedure is not null)
        {
            await ChargerHistoriqueAsync();
            if (Procedure.InterventionId is not null)
                await ChargerInterventionAsync();
            else
                InterventionTexte = "Aucune intervention PÉGASE n'est liée à cette procédure.";
        }
    }

    private void Afficher(ProcedureDto p)
    {
        var personne = PersonneSelection?.Dto.Id;
        var acte = ActeSelection?.Dto.Id;
        var piece = PieceSelection?.Dto.Id;
        if (Procedure?.Id != p.Id)
            Dossiers = null;
        Procedure = p;
        Remplir(PersonneLignes, p.Personnes.Select(x => new PersonneProcedureLigne(x)));
        Remplir(VehiculeLignes, p.Vehicules.Select(x => new VehiculeProcedureLigne(x)));
        Remplir(ActeLignes, p.Actes.Select(x => new ActeLigne(x)));
        Remplir(PieceLignes, p.Pieces.Select(x => new PieceLigne(x)));
        Remplir(ObjetLignes, p.Pieces.Where(x => x.Categorie is PieceCategories.Objet or PieceCategories.Scelle).Select(x => new PieceLigne(x)));
        Remplir(Liens, p.Liens);
        Remplir(Validations, p.Validations.Select(x => new ValidationLigne(x)));
        var documents = p.Actes.Where(a => a.Statut == ActeStatuts.Valide)
            .Select(a => new DocumentLigne("Acte validé (PDF figé)", a.Numero, a.Titre, FormatsJ.DateHeure(a.ValideLe), $"api/lrppn/actes/{a.Id}/pdf"))
            .Concat(p.Pieces.Where(x => x.NomFichier is not null)
                .Select(x => new DocumentLigne(x.Categorie, x.Numero, $"{x.Libelle} — {x.NomFichier}", FormatsJ.DateHeure(x.CreeLe), $"api/lrppn/pieces/{x.Id}/fichier")));
        Remplir(Documents, documents);
        PersonneSelection = PersonneLignes.FirstOrDefault(x => x.Dto.Id == personne);
        ActeSelection = ActeLignes.FirstOrDefault(x => x.Dto.Id == acte);
        PieceSelection = PieceLignes.FirstOrDefault(x => x.Dto.Id == piece);
        OnPropertyChanged(nameof(PersonnesActe));
        OnPropertyChanged(nameof(ActesPieces));
    }

    private static void Remplir<T>(ObservableCollection<T> cible, IEnumerable<T> source)
    {
        cible.Clear();
        foreach (var e in source)
            cible.Add(e);
    }

    /// <summary>POST ou DELETE sur une sous-ressource de la procédure qui renvoie la procédure à jour.</summary>
    private async Task EnvoyerAsync(HttpMethod methode, string suffixe, object? corps, string succes)
    {
        if (Api is not { } api || Procedure is not { } p)
            return;
        await Executer(async () =>
        {
            Afficher(await api.SendAsync<ProcedureDto>(methode, $"api/lrppn/procedures/{p.Id}/{suffixe}", corps));
            NumeroLien = null;
        }, succes);
    }

    // ===== Synthèse : statut et procédures liées =====

    public IReadOnlyList<string> Statuts => ProcedureStatuts.All;
    public string? NouveauStatut { get => _nouveauStatut; set => SetProperty(ref _nouveauStatut, value); }
    public string? MotifStatut { get => _motifStatut; set => SetProperty(ref _motifStatut, value); }
    public string? NumeroLien { get => _numeroLien; set => SetProperty(ref _numeroLien, value); }
    public ProcedureLienDto? LienSelection { get => _lienSelection; set => SetProperty(ref _lienSelection, value); }
    public RelayCommand ChangerStatutCommand { get; }
    public RelayCommand LierCommand { get; }
    public RelayCommand DelierCommand { get; }

    private async Task ChangerStatutAsync()
    {
        if (Api is not { } api || Procedure is not { } p || string.IsNullOrWhiteSpace(NouveauStatut))
            return;
        if (NouveauStatut == ProcedureStatuts.Archivee &&
            !await Hote.ConfirmerAsync("Archiver la procédure", $"Archiver la procédure {p.Numero} ? Elle ne pourra plus être modifiée.", "Archiver", true))
            return;
        await Executer(async () =>
        {
            Afficher(await api.SendAsync<ProcedureDto>(HttpMethod.Put, $"api/lrppn/procedures/{p.Id}/statut", new StatutInput(NouveauStatut!, MotifStatut)));
            NouveauStatut = MotifStatut = null;
            await ChargerHistoriqueAsync();
        }, "Statut de la procédure modifié.");
    }

    // ===== Personnes =====

    public PersonnePickerViewModel Picker { get; }
    public IReadOnlyList<string> Qualites => ProcedureReferentiels.QualitesPersonne;
    public string Qualite { get => _qualite; set => SetProperty(ref _qualite, value); }
    public string? Observations { get => _observations; set => SetProperty(ref _observations, value); }
    public PersonneProcedureLigne? PersonneSelection { get => _personneSelection; set => SetProperty(ref _personneSelection, value); }

    public DossiersAssociesDto? Dossiers
    {
        get => _dossiers;
        private set
        {
            if (!SetProperty(ref _dossiers, value))
                return;
            OnPropertyChanged(nameof(ADossiers));
            OnPropertyChanged(nameof(DossiersTexte));
        }
    }

    public bool ADossiers => Dossiers is not null;
    public string DossiersTexte => Dossiers is { } d ? DossiersFormat.Texte(d) : string.Empty;

    public RelayCommand AjouterPersonneCommand { get; }
    public RelayCommand RetirerPersonneCommand { get; }
    public RelayCommand DossiersCommand { get; }

    private async Task AjouterPersonneAsync()
    {
        if (Picker.Selection is not { } personne)
        {
            await Executer(() => throw new ArgumentException("Recherche puis sélectionne une identité."));
            return;
        }
        await EnvoyerAsync(HttpMethod.Post, "personnes", new ProcedurePersonneInput(personne.Id, Qualite, Observations), "Personne ajoutée à la procédure.");
        Observations = null;
    }

    private async Task RetirerPersonneAsync()
    {
        if (PersonneSelection is not { } ligne)
            return;
        if (!await Hote.ConfirmerAsync("Retirer la personne", $"Retirer {ligne.Identite} ({ligne.Qualite}) de la procédure ?", "Retirer", true))
            return;
        await EnvoyerAsync(HttpMethod.Delete, $"personnes/{ligne.Dto.Id}", null, "Personne retirée.");
    }

    private async Task ChargerDossiersAsync()
    {
        if (Api is not { } api || PersonneSelection is not { } ligne)
            return;
        await Executer(async () => Dossiers = await api.GetAsync<DossiersAssociesDto>($"api/lrppn/personnes/{ligne.Dto.Personne.Id}/dossiers"));
    }

    // ===== Véhicules =====

    public VehiculePickerViewModel VehiculePicker { get; }
    public IReadOnlyList<string> Roles => ProcedureReferentiels.RolesVehicule;
    public string Role { get => _role; set => SetProperty(ref _role, value); }
    public string? ObservationsVehicule { get => _observationsVehicule; set => SetProperty(ref _observationsVehicule, value); }
    public VehiculeProcedureLigne? VehiculeSelection { get => _vehiculeSelection; set => SetProperty(ref _vehiculeSelection, value); }
    public RelayCommand AjouterVehiculeCommand { get; }
    public RelayCommand RetirerVehiculeCommand { get; }

    private async Task AjouterVehiculeAsync()
    {
        if (VehiculePicker.Selection is not { } vehicule)
        {
            await Executer(() => throw new ArgumentException("Recherche puis sélectionne un véhicule."));
            return;
        }
        await EnvoyerAsync(HttpMethod.Post, "vehicules", new ProcedureVehiculeInput(vehicule.Id, Role, ObservationsVehicule), "Véhicule ajouté à la procédure.");
        ObservationsVehicule = null;
    }

    // ===== Actes =====

    public IReadOnlyList<TypeActeInfo> TypesActe => ActesCatalogue.All;
    public TypeActeInfo? TypeActe { get => _typeActe; set => SetProperty(ref _typeActe, value); }
    public IEnumerable<PersonneProcedureLigne?> PersonnesActe => new PersonneProcedureLigne?[] { null }.Concat(PersonneLignes);
    public PersonneProcedureLigne? PersonneActe { get => _personneActe; set => SetProperty(ref _personneActe, value); }
    public ActeLigne? ActeSelection { get => _acteSelection; set => SetProperty(ref _acteSelection, value); }
    public RelayCommand NouvelActeCommand { get; }
    public RelayCommand OuvrirActeCommand { get; }

    private async Task NouvelActeAsync()
    {
        if (Api is not { } api || Procedure is not { } p || TypeActe is not { } type)
            return;
        await Executer(async () =>
        {
            var acte = await api.PostAsync<ActeDto>($"api/lrppn/procedures/{p.Id}/actes", new ActeCreationInput(type.Code, PersonneActe?.Dto.Personne.Id));
            await _lrppn.OuvrirActeAsync(acte.Id);
        });
    }

    // ===== Pièces et scellés =====

    public IReadOnlyList<string> Categories => PieceCategories.All;
    public string Categorie { get => _categorie; set => SetProperty(ref _categorie, value); }
    public string? Libelle { get => _libelle; set => SetProperty(ref _libelle, value); }
    public string? Description { get => _description; set => SetProperty(ref _description, value); }
    public string? Scelle { get => _scelle; set => SetProperty(ref _scelle, value); }
    public string? Localisation { get => _localisation; set => SetProperty(ref _localisation, value); }
    public IEnumerable<ActeLigne?> ActesPieces => new ActeLigne?[] { null }.Concat(ActeLignes);
    public ActeLigne? ActePiece { get => _actePiece; set => SetProperty(ref _actePiece, value); }
    public string FichierTexte => _fichierNom is null ? "Aucun fichier joint (10 Mo au plus)." : $"{_fichierNom} ({FormatsJ.Taille(_fichierContenu?.Length ?? 0)})";
    public PieceLigne? PieceSelection { get => _pieceSelection; set { if (SetProperty(ref _pieceSelection, value)) OnPropertyChanged(nameof(APieceSelection)); } }
    public bool APieceSelection => PieceSelection is not null;
    public IReadOnlyList<string> MouvementsPossibles => PieceMouvements.All;
    public string Mouvement { get => _mouvement; set => SetProperty(ref _mouvement, value); }
    public string? Destination { get => _destination; set => SetProperty(ref _destination, value); }
    public string? ObservationsMouvement { get => _observationsMouvement; set => SetProperty(ref _observationsMouvement, value); }
    public DocumentLigne? DocumentSelection { get => _documentSelection; set => SetProperty(ref _documentSelection, value); }
    public RelayCommand JoindreCommand { get; }
    public RelayCommand AjouterPieceCommand { get; }
    public RelayCommand MouvementCommand { get; }
    public RelayCommand TelechargerPieceCommand { get; }
    public RelayCommand TelechargerDocumentCommand { get; }

    private async Task JoindreAsync()
    {
        if (Hote.Fichiers is not { } fichiers)
            return;
        await Executer(async () =>
        {
            if (await fichiers.ChoisirAsync() is not { } choisi)
                return;
            if (choisi.Contenu.Length > 10 * 1024 * 1024)
                throw new ArgumentException("Le fichier dépasse 10 Mo.");
            _fichierNom = choisi.Nom;
            _fichierContenu = choisi.Contenu;
            OnPropertyChanged(nameof(FichierTexte));
        });
    }

    private async Task AjouterPieceAsync()
    {
        if (Api is not { } api || Procedure is not { } p)
            return;
        await Executer(async () =>
        {
            await api.PostAsync<PieceDto>($"api/lrppn/procedures/{p.Id}/pieces", new PieceInput(Categorie, Libelle ?? string.Empty, Description, Scelle,
                ActePiece?.Dto.Id, Localisation, _fichierNom, null, _fichierContenu is null ? null : Convert.ToBase64String(_fichierContenu)));
            Libelle = Description = Scelle = Localisation = null;
            _fichierNom = null;
            _fichierContenu = null;
            OnPropertyChanged(nameof(FichierTexte));
            Afficher(await api.GetAsync<ProcedureDto>($"api/lrppn/procedures/{p.Id}"));
        }, "Pièce enregistrée.");
    }

    private async Task MouvementAsync()
    {
        if (Api is not { } api || PieceSelection is not { } piece || Procedure is not { } p)
            return;
        await Executer(async () =>
        {
            await api.PostAsync<PieceDto>($"api/lrppn/pieces/{piece.Dto.Id}/mouvements", new MouvementInput(Mouvement, Destination, ObservationsMouvement));
            Destination = ObservationsMouvement = null;
            Afficher(await api.GetAsync<ProcedureDto>($"api/lrppn/procedures/{p.Id}"));
        }, "Mouvement enregistré.");
    }

    // ===== Interventions, historique =====

    public bool AIntervention => Procedure?.InterventionId is not null;
    public string? InterventionTexte { get => _interventionTexte; private set => SetProperty(ref _interventionTexte, value); }
    public RelayCommand ChargerInterventionCommand { get; }
    public RelayCommand ChargerHistoriqueCommand { get; }

    private async Task ChargerInterventionAsync()
    {
        if (Api is not { } api || Procedure?.InterventionId is not { } id)
            return;
        try
        {
            var i = await api.GetAsync<InterventionResumeDto>($"api/pegase/interventions/{id}/resume");
            InterventionTexte = $"Intervention {i.Numero} — {i.Nature}\nAdresse : {i.Adresse ?? "—"} {i.Commune ?? string.Empty}\n" +
                                $"Début : {FormatsJ.DateHeure(i.DebutLe)}   Statut : {i.Statut}\n" +
                                $"Équipages : {(i.Equipages.Count == 0 ? "—" : string.Join(", ", i.Equipages))}";
        }
        catch (ApiException ex)
        {
            InterventionTexte = $"Intervention PÉGASE {Procedure.InterventionNumero ?? id.ToString(System.Globalization.CultureInfo.InvariantCulture)} : détail indisponible ({ex.Message}).";
        }
    }

    private async Task ChargerHistoriqueAsync()
    {
        if (Api is not { } api || Procedure is not { } p)
            return;
        try
        {
            Remplir(Historique, (await api.GetAsync<List<HistoriqueDto>>($"api/lrppn/procedures/{p.Id}/historique")).Select(HistoriqueLigne.De));
        }
        catch (ApiException)
        {
            Historique.Clear();
        }
    }
}
