using System.Collections.ObjectModel;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Operations;

namespace Saphir.ViewModels.Apps.Operations;

// =====================================================================================
// Listes de mentions : toutes, déclarations, archives, recherche
// =====================================================================================

public sealed class McMentionsSection : SectionViewModel
{
    public const string ModeToutes = "toutes";
    public const string ModeDeclarations = "declarations";
    public const string ModeArchives = "archives";
    public const string ModeRecherche = "recherche";

    private string _recherche = string.Empty;
    private string _statut = "Tous";
    private string _type = "Tous";
    private string _du = string.Empty;
    private string _au = string.Empty;
    private MentionLigne? _selection;

    public McMentionsSection(MainCouranteViewModel host, string mode)
        : base(host, mode switch
        {
            ModeDeclarations => "Déclarations",
            ModeArchives => "Archives",
            ModeRecherche => "Recherche",
            _ => "Mentions"
        })
    {
        App = host;
        Mode = mode;
        if (mode == ModeArchives)
        {
            _statut = MentionStatuts.Validee;
            _au = DateTime.Today.AddDays(-30).ToString("dd/MM/yyyy", Fmt.Fr);
        }
        RechercherCommand = new RelayCommand(() => _ = Host.Executer(ChargerAsync));
        OuvrirCommand = new RelayCommand(() => { if (Selection is { } m) App.OuvrirMention(m.Id); });
        NouvelleCommand = new RelayCommand(() => App.Afficher("nouvelle", true));
    }

    public MainCouranteViewModel App { get; }
    public string Mode { get; }
    public bool EstArchives => Mode == ModeArchives;
    public bool AvecFiltresAvances => Mode is ModeRecherche or ModeArchives;
    public bool PeutRediger => MainCouranteViewModel.PeutRediger;
    public string Aide => Mode switch
    {
        ModeDeclarations => "Déclarations reçues à l'accueil ou par téléphone.",
        ModeArchives => "Mentions validées de plus de 30 jours (consultation seule) et vacations clôturées.",
        ModeRecherche => "Recherche dans toutes les mentions : numéro, nature, catégorie, déclarant, rédacteur, lieu, intervention.",
        _ => "Mentions et déclarations : une mention validée est figée, les corrections se font par mention rectificative."
    };

    public ObservableCollection<MentionLigne> Lignes { get; } = [];
    public ObservableCollection<VacationResumeDto> VacationsCloturees { get; } = [];
    public IReadOnlyList<string> Statuts { get; } = ["Tous", MentionStatuts.Brouillon, MentionStatuts.Validee];
    public IReadOnlyList<string> Types { get; } = ["Tous", .. MentionTypes.All];

    public string Recherche { get => _recherche; set => SetProperty(ref _recherche, value); }
    public string Statut { get => _statut; set => SetProperty(ref _statut, value); }
    public string Type { get => _type; set => SetProperty(ref _type, value); }
    public string Du { get => _du; set => SetProperty(ref _du, value); }
    public string Au { get => _au; set => SetProperty(ref _au, value); }
    public MentionLigne? Selection { get => _selection; set => SetProperty(ref _selection, value); }
    public string Compte => Lignes.Count + " mention(s)";

    public RelayCommand RechercherCommand { get; }
    public RelayCommand OuvrirCommand { get; }
    public RelayCommand NouvelleCommand { get; }

    public override async Task ChargerAsync()
    {
        var url = "api/main-courante/mentions?limit=500";
        if (Fmt.Vide(Recherche) is { } q) url += "&q=" + ApiClient.Q(q);
        if (Statut != "Tous") url += "&statut=" + ApiClient.Q(Statut);
        var type = Mode == ModeDeclarations ? MentionTypes.Declaration : Type;
        if (type != "Tous") url += "&type=" + ApiClient.Q(type);
        if (Fmt.LireDateHeure(Du, "Du") is { } du) url += "&du=" + Fmt.Iso(du);
        if (Fmt.LireDateHeure(Au, "Au") is { } au) url += "&au=" + Fmt.Iso(au.TimeOfDay == TimeSpan.Zero ? au.AddDays(1).AddSeconds(-1) : au);
        var mentions = await Client.GetAsync<List<MentionResumeDto>>(url);
        var selection = Selection?.Id;
        Lignes.Clear();
        foreach (var m in mentions)
            Lignes.Add(new MentionLigne(m));
        Selection = selection is { } id ? Lignes.FirstOrDefault(l => l.Id == id) : null;
        OnPropertyChanged(nameof(Compte));

        if (EstArchives)
        {
            var vacations = await Client.GetAsync<List<VacationResumeDto>>("api/main-courante/vacations?statut=" + ApiClient.Q(VacationStatuts.Cloturee) + "&limit=200");
            VacationsCloturees.Clear();
            foreach (var v in vacations)
                VacationsCloturees.Add(v with { Debut = v.Debut.ToLocalTime(), Fin = v.Fin?.ToLocalTime() });
        }
    }

    public override bool Concerne(RealtimeMessage message)
        => Est(message, OperationsTypes.MainCouranteChanged) && message.Text?.StartsWith("mention:", StringComparison.Ordinal) == true;
}

// =====================================================================================
// Éditeur de mention (9.3)
// =====================================================================================

/// <summary>Saisie d'une mention (propriétés simples, objet remplacé à chaque chargement).</summary>
public sealed class MentionEdition : ViewModelBase
{
    public string Type { get; set; } = MentionTypes.Mention;
    public string? Categorie { get; set; }
    public string? Origine { get; set; } = "Accueil du public";
    public long? InterventionId { get; set; }
    public string? InterventionNumero { get; set; }
    public long? ParentId { get; set; }
    public string? ParentNumero { get; set; }

    public string? Qualite { get; set; } = "Déclarant";
    public long? PersonneId { get; set; }
    public string? PersonneReference { get; set; }
    public string? Nom { get; set; }
    public string? Prenom { get; set; }
    public string? DateNaissance { get; set; }
    public string? LieuNaissance { get; set; }
    public string? Adresse { get; set; }
    public string? Telephone { get; set; }
    public string? Email { get; set; }
    public string? AutresPersonnes { get; set; }

    public string? FaitsDate { get; set; } = Fmt.Maintenant();
    public string? Lieu { get; set; }
    public string? Nature { get; set; }
    public string? Declaration { get; set; }
    public string? PersonnesCitees { get; set; }
    public string? Vehicules { get; set; }
    public string? Objets { get; set; }
    public string? Documents { get; set; }
    public string? Observations { get; set; }

    public string? Mesures { get; set; }
    public string? Orientation { get; set; } = "Aucune";
    public string? ServiceInforme { get; set; }
    public string? ProcedureLrppn { get; set; }

    public bool EstRectificative => ParentId is not null;
    public string InterventionAffichee => InterventionNumero ?? (InterventionId is { } id ? "Intervention n° " + id : "Aucune");
    public string PersonneAffichee => PersonneReference is null ? "Saisie libre (personne non rattachée au répertoire)" : "Fiche du répertoire : " + PersonneReference;

    /// <summary>Toutes les liaisons relisent l'objet (après une modification par le programme).</summary>
    public void ToutRafraichir() => OnPropertyChanged(string.Empty);

    public static MentionEdition Depuis(MentionInput m, string? interventionNumero, string? parentNumero) => new()
    {
        Type = m.Type, Categorie = m.Categorie, Origine = m.Origine, InterventionId = m.InterventionId, InterventionNumero = interventionNumero,
        ParentId = m.MentionParentId, ParentNumero = parentNumero,
        Qualite = m.Declarant.Qualite, PersonneId = m.Declarant.PersonneId, PersonneReference = m.Declarant.PersonneReference,
        Nom = m.Declarant.Nom, Prenom = m.Declarant.Prenom, DateNaissance = Fmt.Saisie(m.Declarant.DateNaissance),
        LieuNaissance = m.Declarant.LieuNaissance, Adresse = m.Declarant.Adresse, Telephone = m.Declarant.Telephone, Email = m.Declarant.Email,
        AutresPersonnes = m.Declarant.AutresPersonnes,
        FaitsDate = Fmt.Saisie(m.Faits.Date), Lieu = m.Faits.Lieu, Nature = m.Faits.Nature, Declaration = m.Faits.Declaration,
        PersonnesCitees = m.Faits.PersonnesCitees, Vehicules = m.Faits.Vehicules, Objets = m.Faits.Objets, Documents = m.Faits.Documents,
        Observations = m.Faits.Observations,
        Mesures = m.Suites.Mesures, Orientation = m.Suites.Orientation, ServiceInforme = m.Suites.ServiceInforme, ProcedureLrppn = m.Suites.ProcedureLrppn
    };

    public static MentionEdition Depuis(MentionDto m)
    {
        var e = Depuis(new MentionInput(m.Type, m.Categorie, m.Origine, m.InterventionId, m.MentionParentId, m.Declarant, m.Faits, m.Suites, m.Version),
            m.InterventionNumero, m.ParentNumero);
        return e;
    }

    public MentionInput VersInput(long? version) => new(
        Type, Fmt.Vide(Categorie), Fmt.Vide(Origine), InterventionId, ParentId,
        new MentionDeclarant(Fmt.Vide(Qualite), PersonneId, PersonneReference, Fmt.Vide(Nom), Fmt.Vide(Prenom),
            Fmt.LireDate(DateNaissance, "Date de naissance"), Fmt.Vide(LieuNaissance), Fmt.Vide(Adresse), Fmt.Vide(Telephone), Fmt.Vide(Email),
            Fmt.Vide(AutresPersonnes)),
        new MentionFaits(Fmt.LireDateHeure(FaitsDate, "Date des faits"), Fmt.Vide(Lieu), Fmt.Vide(Nature), Fmt.Vide(Declaration),
            Fmt.Vide(PersonnesCitees), Fmt.Vide(Vehicules), Fmt.Vide(Objets), Fmt.Vide(Documents), Fmt.Vide(Observations)),
        new MentionSuites(Fmt.Vide(Mesures), Fmt.Vide(Orientation), Fmt.Vide(ServiceInforme), Fmt.Vide(ProcedureLrppn)),
        version);
}

public sealed class McMentionEditeurSection : SectionViewModel
{
    private MentionDto? _mention;
    private MentionEdition _edition = new();
    private long? _aOuvrir;
    private long? _projet;
    private bool _vierge;
    private string _recherchePersonne = string.Empty;
    private PersonneResumeDto? _personneChoisie;
    private string _rechercheIntervention = string.Empty;
    private InterventionLigneDto? _interventionChoisie;

    public McMentionEditeurSection(MainCouranteViewModel host) : base(host, "Nouvelle mention")
    {
        App = host;
        EnregistrerCommand = new RelayCommand(() => _ = EnregistrerAsync());
        ValiderCommand = new RelayCommand(() => _ = ValiderAsync());
        RectificativeCommand = new RelayCommand(Rectificative);
        NouvelleCommand = new RelayCommand(() => { Nouvelle(); });
        RechercherPersonneCommand = new RelayCommand(() => _ = RechercherPersonneAsync());
        UtiliserPersonneCommand = new RelayCommand(UtiliserPersonne);
        RechercherInterventionCommand = new RelayCommand(() => _ = RechercherInterventionAsync());
        LierInterventionCommand = new RelayCommand(LierIntervention);
        RetirerInterventionCommand = new RelayCommand(() => { Edition.InterventionId = null; Edition.InterventionNumero = null; Edition.ToutRafraichir(); });
        OuvrirRectificativeCommand = new RelayCommand(p => { if (p is MentionResumeDto m) App.OuvrirMention(m.Id); });
        OuvrirParentCommand = new RelayCommand(() => { if (Edition.ParentId is { } id) App.OuvrirMention(id); });
    }

    public MainCouranteViewModel App { get; }

    public MentionDto? Mention
    {
        get => _mention;
        private set
        {
            if (!SetProperty(ref _mention, value))
                return;
            Titre = value is null ? (Edition.EstRectificative ? "Mention rectificative" : "Nouvelle mention") : $"{value.Type} {value.Numero}";
            foreach (var nom in new[] { nameof(Titre), nameof(EstModifiable), nameof(PeutValider), nameof(PeutRectifier), nameof(Entete),
                         nameof(Redacteur), nameof(Statut), nameof(Validation), nameof(Rectificatives), nameof(AvecRectificatives) })
                OnPropertyChanged(nom);
            Host.Information(string.Empty);
        }
    }

    public MentionEdition Edition
    {
        get => _edition;
        private set
        {
            if (SetProperty(ref _edition, value))
                OnPropertyChanged(nameof(EstModifiable));
        }
    }

    public IReadOnlyList<string> Types { get; } = [MentionTypes.Mention, MentionTypes.Declaration];
    public IReadOnlyList<string> Categories => MainCouranteListes.CategoriesMention;
    public IReadOnlyList<string> Origines => MainCouranteListes.OriginesMention;
    public IReadOnlyList<string> Qualites => MainCouranteListes.QualitesDeclarant;
    public IReadOnlyList<string> Orientations => MainCouranteListes.Orientations;

    /// <summary>Brouillon (ou nouvelle mention) que l'agent peut rédiger.</summary>
    public bool EstModifiable => MainCouranteViewModel.PeutRediger && (Mention is null || Mention.Statut == MentionStatuts.Brouillon);
    public bool PeutValider => MainCouranteViewModel.PeutValider && Mention is { Statut: MentionStatuts.Brouillon };
    public bool PeutRectifier => MainCouranteViewModel.PeutRediger && Mention is { Statut: MentionStatuts.Validee };
    public string Entete => Mention is null ? "Numéro attribué à l'enregistrement" : $"{Mention.Numero} — {Fmt.DateHeure(Mention.Date)}";
    public string Redacteur => Mention is null
        ? SaphirSession.Agent is { } a ? $"{a.Nom} {a.Prenom} — RIO {a.Rio ?? "non attribué"}, {a.Grade ?? "grade non renseigné"}, {a.Service ?? "service non renseigné"}" : "—"
        : $"{Mention.Redacteur} — RIO {Mention.RedacteurRio ?? "—"}, {Mention.RedacteurGrade ?? "—"}, {Mention.RedacteurService ?? "—"}";
    public string Statut => Mention?.Statut ?? "Nouvelle (non enregistrée)";
    public string Validation => Mention?.ValideLe is { } le ? $"Validée le {Fmt.DateHeure(le)} par {Mention.ValidePar}" : "Non validée";
    public IReadOnlyList<MentionResumeDto> Rectificatives => Mention?.Rectificatives ?? Array.Empty<MentionResumeDto>();
    public bool AvecRectificatives => Rectificatives.Count > 0;

    public ObservableCollection<PersonneResumeDto> Personnes { get; } = [];
    public ObservableCollection<InterventionLigneDto> Interventions { get; } = [];
    public string RecherchePersonne { get => _recherchePersonne; set => SetProperty(ref _recherchePersonne, value); }
    public PersonneResumeDto? PersonneChoisie { get => _personneChoisie; set => SetProperty(ref _personneChoisie, value); }
    public string RechercheIntervention { get => _rechercheIntervention; set => SetProperty(ref _rechercheIntervention, value); }
    public InterventionLigneDto? InterventionChoisie { get => _interventionChoisie; set => SetProperty(ref _interventionChoisie, value); }

    public RelayCommand EnregistrerCommand { get; }
    public RelayCommand ValiderCommand { get; }
    public RelayCommand RectificativeCommand { get; }
    public RelayCommand NouvelleCommand { get; }
    public RelayCommand RechercherPersonneCommand { get; }
    public RelayCommand UtiliserPersonneCommand { get; }
    public RelayCommand RechercherInterventionCommand { get; }
    public RelayCommand LierInterventionCommand { get; }
    public RelayCommand RetirerInterventionCommand { get; }
    public RelayCommand OuvrirRectificativeCommand { get; }
    public RelayCommand OuvrirParentCommand { get; }

    public void PreparerOuverture(long id) => _aOuvrir = id;
    public void PreparerProjet(long interventionId) => _projet = interventionId;
    public override void DepuisMenu() => _vierge = true;

    public override async Task ChargerAsync()
    {
        if (_aOuvrir is { } id)
        {
            _aOuvrir = null;
            await ChargerMentionAsync(id);
        }
        else if (_projet is { } interventionId)
        {
            _projet = null;
            var projet = await Client.GetAsync<MentionInput>($"api/main-courante/mentions/projet?interventionId={interventionId}");
            string? numero = null;
            try
            {
                numero = (await Client.GetAsync<InterventionResumeDto>($"api/pegase/interventions/{interventionId}/resume")).Numero;
            }
            catch (ApiException)
            {
                // Numéro affiché après l'enregistrement si l'agent n'a pas accès à PÉGASE.
            }
            Edition = MentionEdition.Depuis(projet, numero, null);
            Mention = null;
            Titre = "Nouvelle mention (projet depuis PÉGASE)";
            OnPropertyChanged(nameof(Titre));
            Host.Information("Projet de mention prérempli depuis l'intervention : vérifie chaque information avant d'enregistrer.");
        }
        else if (_vierge)
        {
            _vierge = false;
            Nouvelle();
        }
    }

    public override bool Concerne(RealtimeMessage message)
        => Mention is { } m && !EstModifiable && Est(message, OperationsTypes.MainCouranteChanged) && message.Text == "mention:" + m.Id;

    private async Task ChargerMentionAsync(long id)
    {
        var mention = await Client.GetAsync<MentionDto>($"api/main-courante/mentions/{id}");
        Edition = MentionEdition.Depuis(mention);
        Mention = mention;
    }

    public void Nouvelle()
    {
        Edition = new MentionEdition();
        Mention = null;
        Titre = "Nouvelle mention";
        OnPropertyChanged(nameof(Titre));
        Personnes.Clear();
        Interventions.Clear();
    }

    private void Rectificative()
    {
        if (Mention is not { } origine)
            return;
        Edition = new MentionEdition
        {
            Type = MentionTypes.Rectificative, ParentId = origine.Id, ParentNumero = origine.Numero, Categorie = origine.Categorie,
            Origine = origine.Origine, InterventionId = origine.InterventionId, InterventionNumero = origine.InterventionNumero,
            Nature = "Additif à la mention " + origine.Numero, Qualite = origine.Declarant.Qualite, Nom = origine.Declarant.Nom,
            Prenom = origine.Declarant.Prenom, PersonneId = origine.Declarant.PersonneId, PersonneReference = origine.Declarant.PersonneReference
        };
        Mention = null;
        Titre = "Mention rectificative de " + origine.Numero;
        OnPropertyChanged(nameof(Titre));
        Host.Information("La mention d'origine reste inchangée : rédige ici le complément ou la correction.");
    }

    private async Task EnregistrerAsync()
    {
        await Host.Executer(async () =>
        {
            var input = Edition.VersInput(Mention?.Version);
            var enregistree = Mention is null
                ? await Client.PostAsync<MentionDto>("api/main-courante/mentions", input)
                : await Client.SendAsync<MentionDto>(HttpMethod.Put, $"api/main-courante/mentions/{Mention.Id}", input);
            Edition = MentionEdition.Depuis(enregistree);
            Mention = enregistree;
        }, "Mention enregistrée (brouillon).");
    }

    private async Task ValiderAsync()
    {
        if (Mention is not { } mention)
            return;
        if (!await Host.Confirmer("Validation de la mention",
                $"Valider la mention {mention.Numero} ? Elle ne pourra plus être modifiée : les corrections se feront par mention rectificative.", "Valider"))
            return;
        await Host.Executer(async () =>
        {
            // Les dernières saisies sont enregistrées avant la validation.
            var input = Edition.VersInput(mention.Version);
            var enregistree = await Client.SendAsync<MentionDto>(HttpMethod.Put, $"api/main-courante/mentions/{mention.Id}", input);
            var validee = await Client.PostAsync<MentionDto>($"api/main-courante/mentions/{enregistree.Id}/valider", new { });
            Edition = MentionEdition.Depuis(validee);
            Mention = validee;
        }, "Mention validée : elle est désormais figée.");
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
                throw new ApiException("Aucune personne trouvée (ou répertoire des personnes indisponible) : saisis l'identité librement.");
            }
            Personnes.Clear();
            foreach (var p in resultats)
                Personnes.Add(p);
            PersonneChoisie = Personnes.FirstOrDefault();
            if (Personnes.Count == 0)
                Host.Information("Aucune personne trouvée : saisis l'identité librement.");
        });
    }

    private void UtiliserPersonne()
    {
        if (PersonneChoisie is not { } p)
            return;
        Edition.PersonneId = p.Id;
        Edition.PersonneReference = p.Reference;
        Edition.Nom = p.Nom;
        Edition.Prenom = p.Prenom;
        Edition.DateNaissance = Fmt.Saisie(p.DateNaissance);
        Edition.LieuNaissance = p.LieuNaissance;
        Edition.ToutRafraichir();
    }

    private async Task RechercherInterventionAsync()
    {
        await Host.Executer(async () =>
        {
            var url = "api/pegase/interventions?limit=30";
            if (Fmt.Vide(RechercheIntervention) is { } q) url += "&q=" + ApiClient.Q(q);
            var resultats = await Client.GetAsync<List<InterventionLigneDto>>(url);
            Interventions.Clear();
            foreach (var i in resultats)
                Interventions.Add(i);
            InterventionChoisie = Interventions.FirstOrDefault();
        });
    }

    private void LierIntervention()
    {
        if (InterventionChoisie is not { } i)
            return;
        Edition.InterventionId = i.Id;
        Edition.InterventionNumero = i.Numero;
        Edition.ToutRafraichir();
    }
}
