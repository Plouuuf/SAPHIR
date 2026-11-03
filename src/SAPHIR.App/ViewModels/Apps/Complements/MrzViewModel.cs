using System.Collections.ObjectModel;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Complements;

namespace Saphir.ViewModels.Apps.Complements;

/// <summary>Personne SAPHIR proposée (recherche du module Judiciaire) avec ses alertes.</summary>
public sealed class PersonneRow(PersonneResumeDto dto, DateOnly? naissanceLue)
{
    public PersonneResumeDto Dto { get; } = dto;
    public string Libelle => Dto.Libelle;
    public string Reference => Dto.Reference;
    public string? LieuNaissance => Dto.LieuNaissance;
    public string Alertes => Dto.Alertes.Count == 0 ? "Aucune alerte" : string.Join(" · ", Dto.Alertes);
    public bool HasAlertes => Dto.Alertes.Count > 0;
    public bool NaissanceConcordante => naissanceLue is not null && Dto.DateNaissance == naissanceLue;
}

public sealed record ControleRow(string Libelle, bool Ok, string Detail);

public sealed record LectureMrzRow(string Reference, string Date, string Identite, string Document, string Personne, string Bilan, bool Ok);

/// <summary>
/// MRZ : saisie ou collage d'une bande (TD1, TD3, ancienne CNI), contrôle des chiffres 7-3-1, extraction,
/// confirmation manuelle, recherche de la personne dans SAPHIR (alertes) et enregistrement de la lecture.
/// </summary>
public sealed class MrzViewModel : ComplementsViewModelBase
{
    private string _texte = string.Empty;
    private MrzResultatDto? _resultat;
    private PersonneRow? _personne;
    private bool _confirmation;
    private string? _contexte;
    private string _nom = string.Empty;
    private string _prenoms = string.Empty;
    private string? _naissance, _sexe, _numero, _type, _nationalite, _expiration;
    private bool _rechercheFaite;

    public MrzViewModel()
    {
        AjouterSection("lecture", "Lecture d'une bande");
        AjouterSection("historique", "Mes lectures");
        AnalyserCommand = new RelayCommand(() => _ = AnalyserAsync());
        EffacerCommand = new RelayCommand(Effacer);
        RechercherCommand = new RelayCommand(() => _ = RechercherAsync());
        EnregistrerCommand = new RelayCommand(() => _ = EnregistrerAsync());
        OuvrirFicheCommand = new RelayCommand(() => { if (Personne is { } p) OuvrirApplication(SaphirApps.Cheops, p.Reference); });
        CreerPersonneCommand = new RelayCommand(() => _ = CreerPersonneAsync());
        ActualiserHistoriqueCommand = new RelayCommand(() => _ = ChargerHistoriqueAsync());
        Section = "lecture";
    }

    public override string TitreMenu => "MRZ";

    public bool IsLecture => Section == "lecture";
    public bool IsHistorique => Section == "historique";

    protected override void OnSectionChanged()
    {
        OnPropertyChanged(nameof(IsLecture));
        OnPropertyChanged(nameof(IsHistorique));
        if (IsHistorique) _ = ChargerHistoriqueAsync();
    }

    public bool CanCheops => SaphirSession.CanOpen(SaphirApps.Cheops);

    // ===== Saisie et analyse =====

    public string Texte { get => _texte; set => SetProperty(ref _texte, value); }

    public MrzResultatDto? Resultat
    {
        get => _resultat;
        private set
        {
            if (!SetProperty(ref _resultat, value)) return;
            Controles.Clear();
            Erreurs.Clear();
            if (value is not null)
            {
                foreach (var c in value.Controles)
                    Controles.Add(new ControleRow(c.Libelle, c.Ok, c.Ok ? $"conforme ({c.Lu})" : $"attendu {c.Attendu}, lu {c.Lu}"));
                foreach (var e in value.Erreurs) Erreurs.Add(e);
                Nom = value.Nom ?? string.Empty;
                Prenoms = value.Prenoms ?? string.Empty;
                Naissance = value.DateNaissance?.ToString("dd/MM/yyyy", Fr);
                Sexe = value.Sexe;
                Numero = value.NumeroDocument;
                Type = value.TypeDocument;
                Nationalite = value.Nationalite;
                Expiration = value.DateExpiration?.ToString("dd/MM/yyyy", Fr);
                Confirmation = false;
            }
            foreach (var n in new[] { nameof(HasResultat), nameof(Reconnu), nameof(Bilan), nameof(BilanOk), nameof(BilanKo), nameof(Expiree) })
                OnPropertyChanged(n);
        }
    }

    public bool HasResultat => Resultat is not null;
    public bool Reconnu => Resultat?.Reconnu == true;
    public bool BilanOk => Resultat is { Reconnu: true, ControlesOk: true };
    public bool BilanKo => Resultat is not null && !BilanOk;
    public string Bilan => Resultat switch
    {
        null => string.Empty,
        { Reconnu: false } => "Bande non reconnue",
        { ControlesOk: true } r => $"{r.Format} — tous les contrôles sont conformes",
        var r => $"{r.Format} — contrôles en échec : vérifie le document et confirme manuellement"
    };
    public bool Expiree => Resultat?.DateExpiration is { } d && d < DateOnly.FromDateTime(DateTime.Today);

    public ObservableCollection<ControleRow> Controles { get; } = [];
    public ObservableCollection<string> Erreurs { get; } = [];

    public string Nom { get => _nom; set => SetProperty(ref _nom, value); }
    public string Prenoms { get => _prenoms; set => SetProperty(ref _prenoms, value); }
    public string? Naissance { get => _naissance; set => SetProperty(ref _naissance, value); }
    public string? Sexe { get => _sexe; set => SetProperty(ref _sexe, value); }
    public string? Numero { get => _numero; set => SetProperty(ref _numero, value); }
    public string? Type { get => _type; set => SetProperty(ref _type, value); }
    public string? Nationalite { get => _nationalite; set => SetProperty(ref _nationalite, value); }
    public string? Expiration { get => _expiration; set => SetProperty(ref _expiration, value); }
    public bool Confirmation { get => _confirmation; set => SetProperty(ref _confirmation, value); }
    public string? Contexte { get => _contexte; set => SetProperty(ref _contexte, value); }

    // ===== Identité SAPHIR =====

    public ObservableCollection<PersonneRow> Personnes { get; } = [];

    public PersonneRow? Personne
    {
        get => _personne;
        set
        {
            if (!SetProperty(ref _personne, value)) return;
            OnPropertyChanged(nameof(HasPersonne));
            OnPropertyChanged(nameof(PeutOuvrirFiche));
        }
    }

    public bool HasPersonne => Personne is not null;
    public bool PeutOuvrirFiche => HasPersonne && CanCheops;
    public bool AucunePersonne => _rechercheFaite && Personnes.Count == 0;

    public ObservableCollection<LectureMrzRow> Lectures { get; } = [];

    public RelayCommand AnalyserCommand { get; }
    public RelayCommand EffacerCommand { get; }
    public RelayCommand RechercherCommand { get; }
    public RelayCommand EnregistrerCommand { get; }
    public RelayCommand OuvrirFicheCommand { get; }
    public RelayCommand CreerPersonneCommand { get; }
    public RelayCommand ActualiserHistoriqueCommand { get; }

    // ===== Actions =====

    private async Task AnalyserAsync()
    {
        var api = Api;
        if (api is null) return;
        if (string.IsNullOrWhiteSpace(Texte))
        {
            ErrorText = "Saisis ou colle la bande MRZ (2 ou 3 lignes).";
            return;
        }
        await RunAsync(async () =>
        {
            Resultat = await api.PostAsync<MrzResultatDto>("api/mrz/analyser", new MrzAnalyseInput(Texte));
            Personnes.Clear();
            Personne = null;
            _rechercheFaite = false;
            OnPropertyChanged(nameof(AucunePersonne));
            if (Resultat.Reconnu && !string.IsNullOrWhiteSpace(Resultat.Nom))
                await RechercherInterneAsync(api);
        });
    }

    private async Task RechercherAsync()
    {
        var api = Api;
        if (api is null) return;
        await RunAsync(() => RechercherInterneAsync(api));
    }

    private async Task RechercherInterneAsync(ApiClient api)
    {
        var naissance = LireJour(Naissance, "Date de naissance");
        var q = $"{Nom} {Prenoms.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()}".Trim();
        if (q.Length < 2)
            throw new ArgumentException("Indique au moins le nom pour rechercher la personne.");
        List<PersonneResumeDto> trouvees;
        try
        {
            trouvees = await api.GetAsync<List<PersonneResumeDto>>("api/personnes/recherche?q=" + ApiClient.Q(q));
        }
        catch (ApiException ex) when (ex.Status == System.Net.HttpStatusCode.NotFound)
        {
            trouvees = [];
        }
        Personnes.Clear();
        foreach (var p in trouvees.OrderByDescending(p => p.DateNaissance == naissance && naissance is not null))
            Personnes.Add(new PersonneRow(p, naissance));
        Personne = Personnes.FirstOrDefault(p => p.NaissanceConcordante);
        _rechercheFaite = true;
        OnPropertyChanged(nameof(AucunePersonne));
    }

    private async Task CreerPersonneAsync()
    {
        var api = Api;
        if (api is null) return;
        if (!await ConfirmAsync("Nouvelle identité",
                $"Créer la personne {Nom.ToUpperInvariant()} {Prenoms} dans SAPHIR à partir de cette lecture ?", "Créer"))
            return;
        await RunAsync(async () =>
        {
            var input = new PersonneCreationInput(Nom, Prenoms, LireJour(Naissance, "Date de naissance"), null, Sexe, Nationalite);
            var personne = await api.PostAsync<PersonneResumeDto>("api/personnes", input);
            var row = new PersonneRow(personne, LireJour(Naissance, "Date de naissance"));
            Personnes.Insert(0, row);
            Personne = row;
            OnPropertyChanged(nameof(AucunePersonne));
        }, "Identité créée dans SAPHIR.");
    }

    private async Task EnregistrerAsync()
    {
        var api = Api;
        if (api is null || Resultat is not { Reconnu: true }) return;
        if (!Resultat.ControlesOk && !Confirmation)
        {
            ErrorText = "Les contrôles sont en échec : coche la confirmation manuelle après vérification visuelle du document.";
            return;
        }
        await RunAsync(async () =>
        {
            var p = Personne?.Dto;
            var input = new MrzLectureInput(Texte, Nom, Prenoms, LireJour(Naissance, "Date de naissance"), Sexe, Numero, Type, Nationalite,
                LireJour(Expiration, "Date d'expiration"), Confirmation, p?.Id, p?.Reference, p?.Libelle, Contexte);
            var lecture = await api.PostAsync<MrzLectureDto>("api/mrz/lectures", input);
            InfoText = $"Lecture {lecture.Reference} enregistrée" + (p is null ? "." : $", liée à {p.Reference}.");
        });
    }

    private void Effacer()
    {
        Texte = string.Empty;
        Resultat = null;
        Personnes.Clear();
        Personne = null;
        Contexte = null;
        _rechercheFaite = false;
        OnPropertyChanged(nameof(AucunePersonne));
    }

    private async Task ChargerHistoriqueAsync()
    {
        var api = Api;
        if (api is null) return;
        await RunAsync(async () =>
        {
            var lectures = await api.GetAsync<List<MrzLectureDto>>("api/mrz/lectures?limit=200");
            Lectures.Clear();
            foreach (var l in lectures)
                Lectures.Add(new LectureMrzRow(l.Reference, Date(l.Date),
                    $"{l.Nom} {l.Prenoms}" + (l.DateNaissance is { } d ? $" — né(e) le {Jour(d)}" : string.Empty),
                    string.Join(" · ", new[] { l.TypeDocument, l.NumeroDocument, l.Nationalite, l.Format }.Where(x => !string.IsNullOrWhiteSpace(x))),
                    l.PersonneReference is null ? "Non liée à une identité SAPHIR" : $"Liée à {l.PersonneLibelle ?? l.PersonneReference} ({l.PersonneReference})",
                    l.ControlesOk ? "Contrôles conformes" : "Confirmée manuellement (contrôles en échec)", l.ControlesOk));
        });
    }
}
