using System.Collections.ObjectModel;
using System.Globalization;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Complements;

namespace Saphir.ViewModels.Apps.Complements;

/// <summary>Ligne du registre bodycam.</summary>
public sealed class BodycamRow(BodycamResumeDto dto)
{
    public BodycamResumeDto Dto { get; } = dto;
    public long Id => Dto.Id;
    public string Titre => $"{Dto.Numero} — {Dto.Agent}";
    public string Detail => string.Join(" · ", new[]
    {
        ComplementsViewModelBase.Date(Dto.EnregistreLe), BodycamViewModel.Duree(Dto.DureeSecondes),
        Dto.InterventionNumero is { } i ? "Intervention " + i : null, Dto.Source == BodycamSources.Url ? "Lien vidéo" : "Fichier vidéo"
    }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public string Description => Dto.Description ?? string.Empty;
    public string Statut => Dto.Statut;
    public bool Disponible => Dto.Statut == BodycamStatuts.Disponible;
    public bool EnAttente => Dto.Statut == BodycamStatuts.EnAttente;
    public bool Archive => Dto.Statut == BodycamStatuts.Archive;
}

public sealed record AccesRow(string Date, string Agent, string Action);

/// <summary>
/// BODYCAM : registre des enregistrements de simulation (porteur, date, intervention, durée, provenance), vidéo par lien
/// ou fichier (200 Mo, envoi par morceaux), documents associés, visionnage journalisé, historique des accès.
/// </summary>
public sealed class BodycamViewModel : ComplementsViewModelBase
{
    public const string SectionRegistre = "registre";
    public const string SectionNouveau = "nouveau";
    public const string SectionFiche = "fiche";

    /// <summary>Taille des morceaux échangés par le poste (le serveur accepte jusqu'à 4 Mo).</summary>
    private const int Morceau = 2 * 1024 * 1024;

    private BodycamDto? _fiche;
    private bool _nouveau;
    private string? _argument;

    public BodycamViewModel(string? argument = null)
    {
        _argument = argument ?? SaphirSession.TakeAppArgument(SaphirApps.Bodycam);
        AjouterSection(SectionRegistre, "Registre des enregistrements");
        AjouterSection(SectionNouveau, "Nouvel enregistrement", CanGerer);
        AjouterSection(SectionFiche, "Enregistrement", visible: false);

        Porteur = new SelecteurAgentsViewModel("Agent porteur de la caméra", unique: true);

        RechercherCommand = new RelayCommand(() => _ = ChargerListeAsync());
        OuvrirCommand = new RelayCommand(p => { if (p is BodycamRow r) _ = OuvrirAsync(r.Id); });
        RetourCommand = new RelayCommand(() => Section = SectionRegistre);
        ModifierCommand = new RelayCommand(() => { if (Fiche is { } f) { Remplir(f); Edition = true; } });
        AnnulerCommand = new RelayCommand(() => { if (_nouveau) Section = SectionRegistre; else Edition = false; });
        EnregistrerCommand = new RelayCommand(() => _ = EnregistrerAsync());
        EnvoyerVideoCommand = new RelayCommand(() => _ = EnvoyerVideoAsync());
        VisionnerCommand = new RelayCommand(() => _ = VisionnerAsync());
        ArchiverCommand = new RelayCommand(() => _ = ArchiverAsync());
        AjouterDocumentCommand = new RelayCommand(() => _ = AjouterDocumentAsync());
        EnregistrerDocumentCommand = new RelayCommand(p => { if (p is PieceJointeDto d) _ = EnregistrerPieceAsync($"api/bodycam/documents/{d.Id}"); });
        AccesCommand = new RelayCommand(() => _ = ChargerAccesAsync());
        HistoriqueCommand = new RelayCommand(() => _ = ChargerHistoriqueAsync());

        Section = SectionRegistre;
        _ = DemarrerAsync();
    }

    public override string TitreMenu => "BODYCAM";

    public bool CanGerer => SaphirSession.Has(Permissions.BodycamGerer);
    public bool IsRegistre => Section == SectionRegistre;
    public bool IsFiche => Section is SectionFiche or SectionNouveau;

    protected override void OnSectionChanged()
    {
        OnPropertyChanged(nameof(IsRegistre));
        OnPropertyChanged(nameof(IsFiche));
        if (Section == SectionRegistre) _ = ChargerListeAsync();
        else if (Section == SectionNouveau) Nouveau();
    }

    public static string Duree(int secondes)
        => secondes <= 0 ? "durée inconnue" : TimeSpan.FromSeconds(secondes).ToString(secondes >= 3600 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);

    // ===== Registre =====

    private string _recherche = string.Empty;
    private string? _filtreIntervention;
    private bool _archives;
    public ObservableCollection<BodycamRow> Liste { get; } = [];
    public string Recherche { get => _recherche; set => SetProperty(ref _recherche, value); }
    public string? FiltreIntervention { get => _filtreIntervention; set => SetProperty(ref _filtreIntervention, value); }
    public bool Archives { get => _archives; set { if (SetProperty(ref _archives, value)) _ = ChargerListeAsync(); } }
    public bool ListeVide => Liste.Count == 0;

    // ===== Fiche =====

    public BodycamDto? Fiche
    {
        get => _fiche;
        private set
        {
            SetProperty(ref _fiche, value);
            Documents.Clear();
            if (value is not null)
                foreach (var d in value.Documents) Documents.Add(d);
            foreach (var n in new[]
                     {
                         nameof(HasFiche), nameof(TitreFiche), nameof(FicheDate), nameof(FicheDuree), nameof(FicheSource), nameof(FicheFichier),
                         nameof(PeutEnvoyer), nameof(PeutVisionner), nameof(PeutArchiver), nameof(PeutModifier), nameof(FicheArchivee),
                         nameof(FicheEnAttente), nameof(HasDocuments), nameof(PeutDocuments)
                     })
                OnPropertyChanged(n);
        }
    }

    public bool HasFiche => Fiche is not null && !_nouveau;
    public string TitreFiche => _nouveau ? "Nouvel enregistrement" : Fiche?.Numero ?? string.Empty;
    public string FicheDate => Fiche is { } f ? Date(f.EnregistreLe) : string.Empty;
    public string FicheDuree => Fiche is { } f ? Duree(f.DureeSecondes) : string.Empty;
    public string FicheSource => Fiche is { Source: BodycamSources.Url } f ? "Lien : " + f.Url : "Fichier vidéo stocké sur le serveur SAPHIR";
    public string FicheFichier => Fiche is { FichierNom: { } nom } f
        ? $"{nom} — {PiecesJointes.TailleLisible(f.FichierTaille ?? 0)}" + (f.FichierSha256 is { } sha ? $" — SHA-256 {sha[..16]}…" : $" — reçu {PiecesJointes.TailleLisible(f.FichierRecu)}")
        : "Aucun fichier envoyé.";
    public bool FicheArchivee => Fiche?.Statut == BodycamStatuts.Archive;
    public bool FicheEnAttente => Fiche?.Statut == BodycamStatuts.EnAttente;
    public bool PeutEnvoyer => CanGerer && Fiche is { Source: BodycamSources.Fichier, Statut: BodycamStatuts.EnAttente } && !_nouveau;
    public bool PeutVisionner => Fiche is { Statut: not BodycamStatuts.EnAttente } && !_nouveau;
    public bool PeutArchiver => CanGerer && Fiche is { Statut: not BodycamStatuts.Archive } && !_nouveau;
    public bool PeutModifier => CanGerer && Fiche is { Statut: not BodycamStatuts.Archive } && !_nouveau;
    public bool PeutDocuments => CanGerer && Fiche is { Statut: not BodycamStatuts.Archive } && !_nouveau;
    public bool HasDocuments => Documents.Count > 0;
    public ObservableCollection<PieceJointeDto> Documents { get; } = [];
    public ObservableCollection<AccesRow> Acces { get; } = [];
    public ObservableCollection<HistoriqueNoteRow> Historique { get; } = [];
    private bool _accesVisible, _historiqueVisible;
    public bool AccesVisible { get => _accesVisible; set => SetProperty(ref _accesVisible, value); }
    public bool HistoriqueVisible { get => _historiqueVisible; set => SetProperty(ref _historiqueVisible, value); }

    // ===== Formulaire =====

    private bool _edition;
    public bool Edition
    {
        get => _edition;
        set
        {
            if (SetProperty(ref _edition, value))
                OnPropertyChanged(nameof(Lecture));
        }
    }

    public bool Lecture => !Edition && HasFiche;

    public SelecteurAgentsViewModel Porteur { get; }
    private string? _jour, _heure, _duree, _intervention, _description, _provenance, _url, _motifArchivage;
    private bool _sourceUrl;
    public string? Jour { get => _jour; set => SetProperty(ref _jour, value); }
    public string? Heure { get => _heure; set => SetProperty(ref _heure, value); }
    public string? DureeSaisie { get => _duree; set => SetProperty(ref _duree, value); }
    public string? InterventionNumero { get => _intervention; set => SetProperty(ref _intervention, value); }
    public string? Description { get => _description; set => SetProperty(ref _description, value); }
    public string? Provenance { get => _provenance; set => SetProperty(ref _provenance, value); }
    public string? Url { get => _url; set => SetProperty(ref _url, value); }
    public string? MotifArchivage { get => _motifArchivage; set => SetProperty(ref _motifArchivage, value); }
    public IReadOnlyList<string> Provenances { get; } = ["Caméra-piéton individuelle", "Caméra embarquée du véhicule", "Terminal NEO", "Import d'un tiers (réquisition)"];

    public bool SourceUrl
    {
        get => _sourceUrl;
        set
        {
            if (!SetProperty(ref _sourceUrl, value)) return;
            OnPropertyChanged(nameof(SourceFichier));
        }
    }

    public bool SourceFichier
    {
        get => !SourceUrl;
        set => SourceUrl = !value;
    }

    // Transfert
    private double _progression;
    private string? _texteTransfert;
    private bool _transfert;
    public double Progression { get => _progression; set => SetProperty(ref _progression, value); }
    public string? TexteTransfert { get => _texteTransfert; set => SetProperty(ref _texteTransfert, value); }
    public bool Transfert { get => _transfert; set => SetProperty(ref _transfert, value); }

    // ===== Commandes =====

    public RelayCommand RechercherCommand { get; }
    public RelayCommand OuvrirCommand { get; }
    public RelayCommand RetourCommand { get; }
    public RelayCommand ModifierCommand { get; }
    public RelayCommand AnnulerCommand { get; }
    public RelayCommand EnregistrerCommand { get; }
    public RelayCommand EnvoyerVideoCommand { get; }
    public RelayCommand VisionnerCommand { get; }
    public RelayCommand ArchiverCommand { get; }
    public RelayCommand AjouterDocumentCommand { get; }
    public RelayCommand EnregistrerDocumentCommand { get; }
    public RelayCommand AccesCommand { get; }
    public RelayCommand HistoriqueCommand { get; }

    // ===== Chargement =====

    private async Task DemarrerAsync()
    {
        if (_argument is not { } arg) return;
        _argument = null;
        await TraiterArgumentAsync(arg);
    }

    protected override string? IdApplication => SaphirApps.Bodycam;

    protected override void OnArgument(string argument) => _ = TraiterArgumentAsync(argument);

    /// <summary>« bodycam:12 » ou numéro « BC-2026-000012 ».</summary>
    private async Task TraiterArgumentAsync(string arg)
    {
        var api = Api;
        if (api is null) return;
        if (arg.StartsWith("bodycam:", StringComparison.Ordinal) && long.TryParse(arg[8..], out var id))
        {
            await OuvrirAsync(id);
            return;
        }
        await RunAsync(async () =>
        {
            var trouves = await api.GetAsync<List<BodycamResumeDto>>("api/bodycam?archives=true&q=" + ApiClient.Q(arg));
            if (trouves.FirstOrDefault(t => string.Equals(t.Numero, arg, StringComparison.OrdinalIgnoreCase)) is { } t)
                await OuvrirAsync(t.Id);
        });
    }

    private async Task ChargerListeAsync()
    {
        var api = Api;
        if (api is null) return;
        await RunAsync(async () =>
        {
            var route = $"api/bodycam?q={ApiClient.Q(Recherche.Trim())}&archives={(Archives ? "true" : "false")}";
            if (!string.IsNullOrWhiteSpace(FiltreIntervention)) route += "&intervention=" + ApiClient.Q(FiltreIntervention.Trim());
            var liste = await api.GetAsync<List<BodycamResumeDto>>(route);
            Liste.Clear();
            foreach (var b in liste) Liste.Add(new BodycamRow(b));
            OnPropertyChanged(nameof(ListeVide));
        });
    }

    private async Task OuvrirAsync(long id)
    {
        var api = Api;
        if (api is null) return;
        await RunAsync(async () =>
        {
            var dto = await api.GetAsync<BodycamDto>($"api/bodycam/{id}");
            _nouveau = false;
            Edition = false;
            AccesVisible = false;
            HistoriqueVisible = false;
            Fiche = dto;
            if (Section != SectionFiche) Section = SectionFiche;
            OnPropertyChanged(nameof(Lecture));
        });
    }

    private void Nouveau()
    {
        _nouveau = true;
        Fiche = null;
        Porteur.Vider();
        if (SaphirSession.Agent is { } moi) Porteur.Ajouter(moi.Id, $"{moi.Nom} {moi.Prenom}");
        var maintenant = DateTime.Now;
        Jour = maintenant.ToString("dd/MM/yyyy", Fr);
        Heure = maintenant.ToString("HH:mm", Fr);
        DureeSaisie = null;
        InterventionNumero = Description = Url = null;
        Provenance = Provenances[0];
        SourceUrl = false;
        Edition = true;
        OnPropertyChanged(nameof(Lecture));
    }

    private void Remplir(BodycamDto f)
    {
        Porteur.Vider();
        Porteur.Ajouter(f.AgentId, f.Agent);
        var local = f.EnregistreLe.ToLocalTime();
        Jour = local.ToString("dd/MM/yyyy", Fr);
        Heure = local.ToString("HH:mm", Fr);
        DureeSaisie = f.DureeSecondes > 0 ? Duree(f.DureeSecondes) : null;
        InterventionNumero = f.InterventionNumero;
        Description = f.Description;
        Provenance = f.Provenance;
        Url = f.Url;
        SourceUrl = f.Source == BodycamSources.Url;
    }

    /// <summary>« 12:34 » ou « 1:02:03 » ou « 754 » (secondes).</summary>
    private static int LireDuree(string? texte)
    {
        if (string.IsNullOrWhiteSpace(texte)) return 0;
        var t = texte.Trim();
        if (int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var s)) return s;
        var parties = t.Split(':');
        if (parties.Length is 2 or 3 && parties.All(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            return parties.Select(p => int.Parse(p, CultureInfo.InvariantCulture)).Aggregate(0, (total, p) => total * 60 + p);
        throw new ArgumentException("Durée attendue au format MM:SS ou H:MM:SS.");
    }

    // ===== Actions =====

    private async Task EnregistrerAsync()
    {
        var api = Api;
        if (api is null || !CanGerer) return;
        await RunAsync(async () =>
        {
            if (Porteur.Selection.FirstOrDefault() is not { } porteur)
                throw new ArgumentException("Choisis l'agent porteur de la caméra.");
            var input = new BodycamInput(porteur.Id, LireInstant(Jour, Heure, "Date d'enregistrement"), LireDuree(DureeSaisie), InterventionNumero,
                Description, Provenance, SourceUrl ? BodycamSources.Url : BodycamSources.Fichier, SourceUrl ? Url : null,
                _nouveau ? 0 : Fiche?.Version ?? 0);
            var dto = _nouveau || Fiche is null
                ? await api.PostAsync<BodycamDto>("api/bodycam", input)
                : await api.SendAsync<BodycamDto>(HttpMethod.Put, $"api/bodycam/{Fiche.Id}", input);
            _nouveau = false;
            Edition = false;
            Fiche = dto;
            if (Section != SectionFiche) Section = SectionFiche;
            OnPropertyChanged(nameof(Lecture));
            InfoText = dto.Statut == BodycamStatuts.EnAttente
                ? $"Enregistrement {dto.Numero} créé : envoie maintenant le fichier vidéo."
                : $"Enregistrement {dto.Numero} enregistré.";
        });
    }

    private async Task EnvoyerVideoAsync()
    {
        var api = Api;
        if (api is null || Fiche is not { } f) return;
        if (Hote is null)
        {
            ErrorText = "L'envoi de fichiers n'est pas disponible sur ce poste.";
            return;
        }
        var fichier = await Hote.ChoisirFichierAsync("Vidéo bodycam (200 Mo au plus)", BodycamFichiers.Extensions);
        if (fichier is null) return;
        await RunAsync(async () =>
        {
            await using var flux = await fichier.Ouvrir();
            var taille = fichier.Taille > 0 ? fichier.Taille : flux.CanSeek ? flux.Length : 0;
            if (taille <= 0)
                throw new ArgumentException("Taille de la vidéo inconnue : choisis un fichier local.");
            if (taille > BodycamFichiers.TailleMax)
                throw new ArgumentException($"La vidéo dépasse {PiecesJointes.TailleLisible(BodycamFichiers.TailleMax)}.");

            Transfert = true;
            Progression = 0;
            TexteTransfert = "Préparation de l'envoi…";
            try
            {
                await api.PostAsync<BodycamEnvoiEtatDto>($"api/bodycam/{f.Id}/fichier/debut",
                    new BodycamEnvoiDebutInput(fichier.Nom, taille, PiecesJointes.TypeMime(fichier.Nom)));
                var tampon = new byte[Morceau];
                long envoye = 0;
                while (envoye < taille)
                {
                    var lus = 0;
                    while (lus < tampon.Length && envoye + lus < taille)
                    {
                        var n = await flux.ReadAsync(tampon.AsMemory(lus, (int)Math.Min(tampon.Length - lus, taille - envoye - lus)));
                        if (n == 0) break;
                        lus += n;
                    }
                    if (lus == 0)
                        throw new ArgumentException("Lecture du fichier interrompue.");
                    await api.SendAsync<BodycamEnvoiEtatDto>(HttpMethod.Put, $"api/bodycam/{f.Id}/fichier/morceau",
                        new BodycamMorceauInput(envoye, Convert.ToBase64String(tampon, 0, lus)));
                    envoye += lus;
                    Progression = 100d * envoye / taille;
                    TexteTransfert = $"Envoi : {PiecesJointes.TailleLisible(envoye)} sur {PiecesJointes.TailleLisible(taille)}";
                }
                Fiche = await api.PostAsync<BodycamDto>($"api/bodycam/{f.Id}/fichier/fin", new { });
                InfoText = "Vidéo reçue et contrôlée par le serveur (empreinte SHA-256 enregistrée).";
            }
            finally
            {
                Transfert = false;
            }
        });
    }

    private async Task VisionnerAsync()
    {
        var api = Api;
        if (api is null || Fiche is not { } f) return;
        if (Hote is null)
        {
            ErrorText = "La lecture vidéo n'est pas disponible sur ce poste.";
            return;
        }
        await RunAsync(async () =>
        {
            var v = await api.PostAsync<BodycamVisionnageDto>($"api/bodycam/{f.Id}/visionner", new { });
            if (v.Source == BodycamSources.Url)
            {
                if (v.Url is null || !Uri.TryCreate(v.Url, UriKind.Absolute, out var uri))
                    throw new ArgumentException("Adresse de la vidéo invalide.");
                if (!await Hote.OuvrirAdresseAsync(uri))
                    throw new ArgumentException("Impossible d'ouvrir le navigateur.");
                InfoText = "Vidéo ouverte dans le navigateur. Visionnage inscrit au journal des accès.";
                return;
            }

            var dossier = Path.Combine(Path.GetTempPath(), "SAPHIR-bodycam");
            Directory.CreateDirectory(dossier);
            var chemin = Path.Combine(dossier, $"{f.Numero}.{PiecesJointes.Extension(v.FichierNom)}");
            var total = v.Taille ?? 0;
            Transfert = true;
            Progression = 0;
            try
            {
                await using (var sortie = new FileStream(chemin, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    long offset = 0;
                    do
                    {
                        var morceau = await api.GetAsync<BodycamMorceauDto>($"api/bodycam/{f.Id}/fichier?offset={offset}&taille={Morceau}");
                        var octets = Convert.FromBase64String(morceau.Donnees);
                        if (octets.Length == 0) break;
                        await sortie.WriteAsync(octets);
                        offset += octets.Length;
                        total = morceau.Total;
                        Progression = total > 0 ? 100d * offset / total : 100;
                        TexteTransfert = $"Téléchargement : {PiecesJointes.TailleLisible(offset)} sur {PiecesJointes.TailleLisible(total)}";
                    } while (offset < total);
                }
            }
            finally
            {
                Transfert = false;
            }
            if (!await Hote.OuvrirFichierLocalAsync(chemin))
                throw new ArgumentException("Aucun lecteur vidéo n'a pu ouvrir le fichier : " + chemin);
            InfoText = "Vidéo ouverte dans le lecteur du poste. Visionnage inscrit au journal des accès.";
        });
    }

    private async Task ArchiverAsync()
    {
        var api = Api;
        if (api is null || Fiche is not { } f) return;
        if (string.IsNullOrWhiteSpace(MotifArchivage))
        {
            ErrorText = "Indique le motif de l'archivage.";
            return;
        }
        if (!await ConfirmAsync("Archivage", $"Archiver l'enregistrement {f.Numero} ? Il ne sera plus modifiable mais restera consultable.", "Archiver", danger: true))
            return;
        await RunAsync(async () =>
        {
            Fiche = await api.PostAsync<BodycamDto>($"api/bodycam/{f.Id}/archiver", new BodycamArchivageInput(MotifArchivage!, f.Version));
            MotifArchivage = null;
        }, "Enregistrement archivé.");
    }

    private async Task AjouterDocumentAsync()
    {
        var api = Api;
        if (api is null || Fiche is not { } f) return;
        if (Hote is null)
        {
            ErrorText = "L'ajout de fichiers n'est pas disponible sur ce poste.";
            return;
        }
        await RunAsync(async () =>
        {
            var fichier = await Hote.ChoisirFichierAsync("Document associé", PiecesJointes.Extensions);
            if (fichier is null) return;
            Fiche = await api.PostAsync<BodycamDto>($"api/bodycam/{f.Id}/documents", await LirePieceAsync(fichier));
        }, "Document associé.");
    }

    private async Task ChargerAccesAsync()
    {
        var api = Api;
        if (api is null || Fiche is not { } f) return;
        if (AccesVisible)
        {
            AccesVisible = false;
            return;
        }
        await RunAsync(async () =>
        {
            var acces = await api.GetAsync<List<BodycamAccesDto>>($"api/bodycam/{f.Id}/acces");
            Acces.Clear();
            foreach (var a in acces) Acces.Add(new AccesRow(Date(a.Date), a.Agent, a.Action));
            AccesVisible = true;
        });
    }

    private async Task ChargerHistoriqueAsync()
    {
        var api = Api;
        if (api is null || Fiche is not { } f) return;
        if (HistoriqueVisible)
        {
            HistoriqueVisible = false;
            return;
        }
        await RunAsync(async () =>
        {
            var lignes = await api.GetAsync<List<HistoriqueDto>>($"api/bodycam/{f.Id}/historique");
            Historique.Clear();
            foreach (var h in lignes)
                Historique.Add(new HistoriqueNoteRow(Date(h.Date), h.Auteur, h.Action,
                    string.Join(" — ", new[] { h.Champ is null ? h.Nouvelle : $"{h.Champ} : {h.Ancienne ?? "—"} → {h.Nouvelle ?? "—"}", h.Motif }
                        .Where(s => !string.IsNullOrWhiteSpace(s)))));
            HistoriqueVisible = true;
        });
    }

    protected override void OnRealtime(RealtimeMessage message)
    {
        if (message.Type != ComplementsRealtime.Bodycam) return;
        if (IsRegistre) _ = ChargerListeAsync();
        else if (Lecture && !Transfert && Fiche is { } f && long.TryParse(message.Text, out var id) && id == f.Id)
            _ = OuvrirAsync(id);
    }
}
