using System.Collections.ObjectModel;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Complements;

namespace Saphir.ViewModels.Apps.Complements;

/// <summary>Ligne de la liste des notes.</summary>
public sealed class NoteRow(NoteDto dto)
{
    public NoteDto Dto { get; } = dto;
    public long Id => Dto.Id;
    public string Titre => Dto.Titre;
    public bool Epinglee => Dto.Epinglee;
    public string Apercu => Dto.Contenu.Length > 120 ? Dto.Contenu[..120].Replace('\n', ' ') + "…" : Dto.Contenu.Replace('\n', ' ');
    public string Infos => string.Join(" · ", new[]
    {
        Dto.Categorie,
        ComplementsViewModelBase.DateCourte(Dto.MajLe),
        Dto.EstAuteur ? (Dto.Partages.Count > 0 ? $"partagée avec {Dto.Partages.Count}" : null) : "de " + Dto.Auteur
    }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

public sealed record HistoriqueNoteRow(string Date, string Auteur, string Action, string Detail);

/// <summary>NOTES : notes personnelles et partagées, catégories, épinglage, recherche, historique.</summary>
public sealed class NotesViewModel : ComplementsViewModelBase
{
    private const string Toutes = "Toutes les catégories";

    private string _recherche = string.Empty;
    private string _categorie = Toutes;
    private NoteRow? _selection;
    private NoteDto? _ouverte;
    private bool _nouvelle;
    private string _titre = string.Empty;
    private string _contenu = string.Empty;
    private string? _editCategorie;
    private bool _epinglee;
    private bool _historiqueVisible;

    public NotesViewModel()
    {
        AjouterSection("toutes", "Toutes mes notes");
        AjouterSection("epinglees", "Épinglées");
        AjouterSection("miennes", "Mes notes personnelles");
        AjouterSection("partagees", "Partagées avec moi");

        Partages = new SelecteurAgentsViewModel("Partager avec", libelleOption: "peut modifier");
        if (SaphirSession.Agent is { } moi) Partages.Exclus.Add(moi.Id);

        NouvelleCommand = new RelayCommand(Nouvelle);
        EnregistrerCommand = new RelayCommand(() => _ = EnregistrerAsync());
        SupprimerCommand = new RelayCommand(() => _ = SupprimerAsync());
        RechercherCommand = new RelayCommand(() => _ = ChargerAsync());
        HistoriqueCommand = new RelayCommand(() => _ = HistoriqueAsync());
        FermerHistoriqueCommand = new RelayCommand(() => HistoriqueVisible = false);
        ChoisirCategorieCommand = new RelayCommand(p => { if (p is string c) EditCategorie = c; });
        RetourListeCommand = new RelayCommand(() => { Selection = null; Fermer(); });

        Section = "toutes";
    }

    public override string TitreMenu => "NOTES";
    protected override double LargeurDetailLarge => 480;

    protected override void OnSectionChanged() => _ = ChargerAsync();
    protected override void OnCompactChanged() => RafraichirPanneaux();

    // ===== Liste =====

    public ObservableCollection<NoteRow> Notes { get; } = [];
    public ObservableCollection<string> Categories { get; } = [Toutes];
    public ObservableCollection<string> CategoriesExistantes { get; } = [];
    public string Recherche { get => _recherche; set => SetProperty(ref _recherche, value); }

    public string Categorie
    {
        get => _categorie;
        set
        {
            if (SetProperty(ref _categorie, value ?? Toutes))
                _ = ChargerAsync();
        }
    }

    public bool ListeVide => Notes.Count == 0;

    public NoteRow? Selection
    {
        get => _selection;
        set
        {
            if (!SetProperty(ref _selection, value)) return;
            if (value is not null) Ouvrir(value.Dto);
        }
    }

    // ===== Édition =====

    public SelecteurAgentsViewModel Partages { get; }
    public ObservableCollection<HistoriqueNoteRow> Historique { get; } = [];

    public bool EditionVisible => _ouverte is not null || _nouvelle;
    public bool AfficherListe => IsLarge || !EditionVisible;
    public bool AfficherEdition => IsLarge || EditionVisible;
    public bool PeutModifier => _nouvelle || _ouverte?.PeutModifier == true;
    public bool LectureSeule => !PeutModifier;
    public bool EstAuteur => _nouvelle || _ouverte?.EstAuteur == true;
    public bool PeutSupprimer => _ouverte is not null;
    public bool PeutHistorique => _ouverte is not null;
    public string LibelleSupprimer => _ouverte?.EstAuteur == false ? "Retirer de ma liste" : "Supprimer";
    public string TitreEdition => _nouvelle ? "Nouvelle note" : _ouverte is { } n ? n.Reference : string.Empty;
    public string InfosEdition => _ouverte is { } n
        ? $"Créée le {Date(n.CreeLe)} par {n.Auteur} — modifiée le {Date(n.MajLe)}" + (n.PeutModifier ? string.Empty : " — lecture seule")
        : "Note personnelle : visible par toi seul, sauf si tu la partages.";

    public string Titre { get => _titre; set => SetProperty(ref _titre, value); }
    public string Contenu { get => _contenu; set => SetProperty(ref _contenu, value); }
    public string? EditCategorie { get => _editCategorie; set => SetProperty(ref _editCategorie, value); }
    public bool Epinglee { get => _epinglee; set => SetProperty(ref _epinglee, value); }
    public bool HistoriqueVisible { get => _historiqueVisible; set => SetProperty(ref _historiqueVisible, value); }

    public RelayCommand NouvelleCommand { get; }
    public RelayCommand EnregistrerCommand { get; }
    public RelayCommand SupprimerCommand { get; }
    public RelayCommand RechercherCommand { get; }
    public RelayCommand HistoriqueCommand { get; }
    public RelayCommand FermerHistoriqueCommand { get; }
    public RelayCommand ChoisirCategorieCommand { get; }
    public RelayCommand RetourListeCommand { get; }

    // ===== Chargement =====

    private async Task ChargerAsync()
    {
        var api = Api;
        if (api is null) return;
        await RunAsync(async () =>
        {
            var route = $"api/notes?filtre={ApiClient.Q(Section)}&q={ApiClient.Q(Recherche.Trim())}";
            if (Categorie != Toutes) route += "&categorie=" + ApiClient.Q(Categorie);
            var notes = await api.GetAsync<List<NoteDto>>(route);
            var id = _ouverte?.Id;
            _selection = null;
            Notes.Clear();
            foreach (var n in notes) Notes.Add(new NoteRow(n));
            if (id is { } i && Notes.FirstOrDefault(n => n.Id == i) is { } row)
                _selection = row;
            OnPropertyChanged(nameof(Selection));
            OnPropertyChanged(nameof(ListeVide));

            var categories = await api.GetAsync<List<string>>("api/notes/categories");
            var courante = _categorie;
            CategoriesExistantes.Clear();
            foreach (var c in categories) CategoriesExistantes.Add(c);
            Categories.Clear();
            Categories.Add(Toutes);
            foreach (var c in categories) Categories.Add(c);
            _categorie = Categories.Contains(courante) ? courante : Toutes;
            OnPropertyChanged(nameof(Categorie));
        });
    }

    private void Ouvrir(NoteDto note)
    {
        _nouvelle = false;
        _ouverte = note;
        Titre = note.Titre;
        Contenu = note.Contenu;
        EditCategorie = note.Categorie;
        Epinglee = note.Epinglee;
        Partages.Vider();
        foreach (var p in note.Partages)
            Partages.Ajouter(p.AgentId, p.Libelle, null, p.PeutModifier);
        HistoriqueVisible = false;
        RafraichirEdition();
    }

    private void Nouvelle()
    {
        _ouverte = null;
        _nouvelle = true;
        _selection = null;
        OnPropertyChanged(nameof(Selection));
        Titre = string.Empty;
        Contenu = string.Empty;
        EditCategorie = Categorie == Toutes ? null : Categorie;
        Epinglee = false;
        Partages.Vider();
        HistoriqueVisible = false;
        RafraichirEdition();
    }

    private void Fermer()
    {
        _ouverte = null;
        _nouvelle = false;
        RafraichirEdition();
    }

    private void RafraichirEdition()
    {
        foreach (var n in new[] { nameof(EditionVisible), nameof(PeutModifier), nameof(LectureSeule), nameof(EstAuteur), nameof(PeutSupprimer),
                     nameof(PeutHistorique), nameof(LibelleSupprimer), nameof(TitreEdition), nameof(InfosEdition) })
            OnPropertyChanged(n);
        RafraichirPanneaux();
    }

    private void RafraichirPanneaux()
    {
        OnPropertyChanged(nameof(AfficherListe));
        OnPropertyChanged(nameof(AfficherEdition));
    }

    // ===== Actions =====

    private async Task EnregistrerAsync()
    {
        var api = Api;
        if (api is null || !PeutModifier) return;
        if (string.IsNullOrWhiteSpace(Titre))
        {
            ErrorText = "Donne un titre à la note.";
            return;
        }
        var partages = Partages.Selection.Select(p => new NotePartageInput(p.Id, p.Option)).ToList();
        var input = new NoteInput(Titre, Contenu, EditCategorie, Epinglee, partages, _ouverte?.Version ?? 0);
        var existante = _ouverte;
        await RunAsync(async () =>
        {
            var dto = existante is null
                ? await api.PostAsync<NoteDto>("api/notes", input)
                : await api.SendAsync<NoteDto>(HttpMethod.Put, $"api/notes/{existante.Id}", input);
            Ouvrir(dto);
            await ChargerAsync();
        }, "Note enregistrée.");
    }

    private async Task SupprimerAsync()
    {
        var api = Api;
        if (api is null || _ouverte is not { } note) return;
        var message = note.EstAuteur
            ? $"Supprimer la note « {note.Titre} » ?" + (note.Partages.Count > 0 ? " Elle disparaîtra aussi pour les agents avec qui elle est partagée." : string.Empty)
            : $"Retirer la note « {note.Titre} » de ta liste ? Elle reste chez son auteur.";
        if (!await ConfirmAsync("Notes", message, LibelleSupprimer, danger: note.EstAuteur))
            return;
        await RunAsync(async () =>
        {
            await api.SendAsync(HttpMethod.Delete, $"api/notes/{note.Id}");
            Fermer();
            await ChargerAsync();
        }, "Note supprimée.");
    }

    private async Task HistoriqueAsync()
    {
        var api = Api;
        if (api is null || _ouverte is not { } note) return;
        await RunAsync(async () =>
        {
            var lignes = await api.GetAsync<List<HistoriqueDto>>($"api/notes/{note.Id}/historique");
            Historique.Clear();
            foreach (var h in lignes)
                Historique.Add(new HistoriqueNoteRow(Date(h.Date), h.Auteur, h.Action,
                    h.Champ is null ? (h.Nouvelle ?? h.Ancienne ?? string.Empty) : $"{h.Champ} : {h.Ancienne ?? "—"} → {h.Nouvelle ?? "—"}"));
            HistoriqueVisible = true;
        });
    }

    protected override void OnRealtime(RealtimeMessage message)
    {
        if (message.Type != ComplementsRealtime.Note) return;
        _ = RechargerAsync(message.Text);
    }

    private async Task RechargerAsync(string? texte)
    {
        await ChargerAsync();
        if (long.TryParse(texte, out var id) && _ouverte?.Id == id && Notes.FirstOrDefault(n => n.Id == id) is { } row
            && row.Dto.Version != _ouverte.Version && Titre == _ouverte.Titre && Contenu == _ouverte.Contenu)
        {
            Ouvrir(row.Dto);
            InfoText = "Cette note vient d'être modifiée par un autre agent : affichage mis à jour.";
        }
    }
}
