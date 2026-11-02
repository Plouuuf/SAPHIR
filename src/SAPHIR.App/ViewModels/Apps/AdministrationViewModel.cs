using System.Collections.ObjectModel;
using System.Globalization;
using Saphir.Services;
using Saphir.Shared.Contracts;

namespace Saphir.ViewModels.Apps;

/// <summary>Case à cocher d'un profil (attribution à un agent).</summary>
public sealed class ProfilChoice
{
    public required string Code { get; init; }
    public required string Nom { get; init; }
    public string Description { get; init; } = string.Empty;
    public bool IsChecked { get; set; }
}

/// <summary>Case à cocher d'une permission (édition d'un profil).</summary>
public sealed class PermissionChoice
{
    public required string Code { get; init; }
    public required string Libelle { get; init; }
    public bool IsChecked { get; set; }
}

public sealed record PermissionGroup(string Module, IReadOnlyList<PermissionChoice> Items);

/// <summary>Ligne du tableau des comptes.</summary>
public sealed class CompteRow(CompteDto dto, string profils)
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public CompteDto Dto { get; } = dto;
    public long Id => Dto.Agent.Id;
    public string Reference => Dto.Agent.Reference;
    public string Nom => Dto.Agent.Nom;
    public string Prenom => Dto.Agent.Prenom;
    public string Rio => Dto.Agent.Rio ?? "—";
    public string Statut => Dto.Agent.Statut;
    public string Profils { get; } = profils;
    public string Discord => Dto.Agent.DiscordId;
    public string DerniereConnexion => Dto.DerniereConnexion?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", French) ?? "Jamais";
}

/// <summary>Paramètre modifiable.</summary>
public sealed class ParametreRow(ParametreDto dto, Func<ParametreRow, Task> save) : ViewModelBase
{
    private string _valeur = dto.Valeur;

    public string Cle { get; } = dto.Cle;
    public string Libelle { get; } = dto.Libelle;
    public string? Aide { get; } = dto.Aide;
    public bool HasAide => !string.IsNullOrWhiteSpace(Aide);
    public bool Modifiable { get; } = dto.Modifiable;
    public string Valeur { get => _valeur; set => SetProperty(ref _valeur, value); }
    public bool IsReadOnly => !Modifiable;

    private RelayCommand? _saveCommand;
    public RelayCommand SaveCommand => _saveCommand ??= new RelayCommand(() => _ = save(this));
}

public sealed record HistoriqueRow(string Date, string Auteur, string Objet, string Reference, string Action, string Detail, string Motif);
public sealed record JournalRow(string Date, string Agent, string Action, string Cible, string Resultat);

/// <summary>
/// Administration : comptes (profils, statut), profils (permissions), paramètres centraux, historique et journal des accès.
/// Chaque onglet n'apparaît qu'avec la permission correspondante ; le serveur revérifie tout.
/// </summary>
public sealed class AdministrationViewModel : AppViewModelBase
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private List<ProfilDto> _profils = [];
    private string _searchText = string.Empty;
    private string _statusFilter = "Tous";
    private CompteRow? _selectedCompte;
    private string? _profilsMotif;
    private string _newStatut = AgentStatuts.Actif;
    private string? _statutMotif;
    private ProfilDto? _selectedProfil;
    private bool _isNewProfil;
    private string _editNom = string.Empty;
    private string _editDescription = string.Empty;
    private string? _historiqueObjet;
    private string? _historiqueReference;

    public AdministrationViewModel()
    {
        SearchCommand = new RelayCommand(() => _ = LoadComptesAsync());
        SaveProfilsCommand = new RelayCommand(() => _ = SaveProfilsAsync());
        ApplyStatutCommand = new RelayCommand(() => _ = ApplyStatutAsync());
        NewProfilCommand = new RelayCommand(StartNewProfil);
        SaveProfilCommand = new RelayCommand(() => _ = SaveProfilAsync());
        DeleteProfilCommand = new RelayCommand(() => _ = DeleteProfilAsync());
        RefreshHistoriqueCommand = new RelayCommand(() => _ = LoadHistoriqueAsync());
        RefreshJournalCommand = new RelayCommand(() => _ = LoadJournalAsync());
        RefreshAllCommand = new RelayCommand(() => _ = LoadAllAsync());

        _ = LoadAllAsync();
    }

    // ===== Droits =====

    public bool CanComptes => SaphirSession.Has(Permissions.AdminComptes);
    public bool CanProfils => SaphirSession.Has(Permissions.AdminProfils);
    public bool CanParametres => SaphirSession.Has(Permissions.AdminParametres);
    public bool CanJournaux => SaphirSession.Has(Permissions.AdminJournaux);

    // ===== Comptes =====

    public ObservableCollection<CompteRow> Comptes { get; } = [];
    public IReadOnlyList<string> StatusFilters { get; } = ["Tous", .. AgentStatuts.All];
    public IReadOnlyList<string> Statuts => AgentStatuts.All;

    public string SearchText { get => _searchText; set => SetProperty(ref _searchText, value); }

    public string StatusFilter
    {
        get => _statusFilter;
        set
        {
            if (SetProperty(ref _statusFilter, value))
                _ = LoadComptesAsync();
        }
    }

    public string ComptesInfo => Comptes.Count + " compte(s)";

    public CompteRow? SelectedCompte
    {
        get => _selectedCompte;
        set
        {
            if (!SetProperty(ref _selectedCompte, value)) return;
            BuildProfilChoices();
            NewStatut = value?.Statut ?? AgentStatuts.Actif;
            ProfilsMotif = null;
            StatutMotif = null;
            OnPropertyChanged(nameof(HasSelectedCompte));
            OnPropertyChanged(nameof(SelectedCompteTitle));
        }
    }

    public bool HasSelectedCompte => SelectedCompte is not null;
    public string SelectedCompteTitle => SelectedCompte is null ? string.Empty : $"{SelectedCompte.Nom} {SelectedCompte.Prenom} — {SelectedCompte.Reference}";

    public ObservableCollection<ProfilChoice> ProfilChoices { get; } = [];
    public string? ProfilsMotif { get => _profilsMotif; set => SetProperty(ref _profilsMotif, value); }
    public string NewStatut { get => _newStatut; set => SetProperty(ref _newStatut, value); }
    public string? StatutMotif { get => _statutMotif; set => SetProperty(ref _statutMotif, value); }

    // ===== Profils =====

    public ObservableCollection<ProfilDto> Profils { get; } = [];

    public ProfilDto? SelectedProfil
    {
        get => _selectedProfil;
        set
        {
            if (!SetProperty(ref _selectedProfil, value)) return;
            if (value is not null)
            {
                _isNewProfil = false;
                EditNom = value.Nom;
                EditDescription = value.Description;
                BuildPermissionGroups(value.Permissions);
            }
            RaiseProfilEditor();
        }
    }

    public bool IsEditingProfil => SelectedProfil is not null || _isNewProfil;
    public bool CanDeleteProfil => SelectedProfil is { Systeme: false, NombreAgents: 0 };
    public string ProfilEditorTitle => _isNewProfil ? "Nouveau profil" : SelectedProfil?.Nom ?? string.Empty;
    public string ProfilEditorInfo => SelectedProfil is { } p
        ? (p.Systeme ? "Profil fourni par SAPHIR (non supprimable). " : string.Empty) + $"{p.NombreAgents} agent(s) ont ce profil."
        : "Choisis les permissions du nouveau profil.";

    public string EditNom { get => _editNom; set => SetProperty(ref _editNom, value); }
    public string EditDescription { get => _editDescription; set => SetProperty(ref _editDescription, value); }
    public ObservableCollection<PermissionGroup> PermissionGroups { get; } = [];

    // ===== Paramètres, historique, journal =====

    public ObservableCollection<ParametreRow> Parametres { get; } = [];
    public ObservableCollection<HistoriqueRow> Historique { get; } = [];
    public ObservableCollection<JournalRow> Journal { get; } = [];
    public string? HistoriqueObjet { get => _historiqueObjet; set => SetProperty(ref _historiqueObjet, value); }
    public string? HistoriqueReference { get => _historiqueReference; set => SetProperty(ref _historiqueReference, value); }

    // ===== Commandes =====

    public RelayCommand SearchCommand { get; }
    public RelayCommand SaveProfilsCommand { get; }
    public RelayCommand ApplyStatutCommand { get; }
    public RelayCommand NewProfilCommand { get; }
    public RelayCommand SaveProfilCommand { get; }
    public RelayCommand DeleteProfilCommand { get; }
    public RelayCommand RefreshHistoriqueCommand { get; }
    public RelayCommand RefreshJournalCommand { get; }
    public RelayCommand RefreshAllCommand { get; }

    // ===== Chargement =====

    private async Task LoadAllAsync()
    {
        foreach (var name in new[] { nameof(CanComptes), nameof(CanProfils), nameof(CanParametres), nameof(CanJournaux) })
            OnPropertyChanged(name);

        if (CanComptes || CanProfils)
            await LoadProfilsAsync();
        if (CanComptes)
            await LoadComptesAsync();
        if (CanParametres)
            await LoadParametresAsync();
        if (CanJournaux)
        {
            await LoadHistoriqueAsync();
            await LoadJournalAsync();
        }
    }

    private async Task LoadProfilsAsync()
    {
        var api = Api;
        if (api is null) return;
        await RunAsync(async () =>
        {
            _profils = await api.GetAsync<List<ProfilDto>>("api/admin/profils");
            var selectedId = SelectedProfil?.Id;
            Profils.Clear();
            foreach (var p in _profils.OrderBy(p => p.Nom, StringComparer.Create(French, true)))
                Profils.Add(p);
            if (selectedId is { } id)
                SelectedProfil = Profils.FirstOrDefault(p => p.Id == id);
        });
    }

    private async Task LoadComptesAsync()
    {
        var api = Api;
        if (api is null || !CanComptes) return;
        await RunAsync(async () =>
        {
            var query = "api/admin/comptes?q=" + ApiClient.Q(SearchText.Trim());
            if (StatusFilter != "Tous")
                query += "&statut=" + ApiClient.Q(StatusFilter);
            var comptes = await api.GetAsync<List<CompteDto>>(query);
            var selectedId = SelectedCompte?.Id;
            Comptes.Clear();
            foreach (var c in comptes)
                Comptes.Add(new CompteRow(c, string.Join(", ", c.Profils.Select(ProfilName))));
            OnPropertyChanged(nameof(ComptesInfo));
            if (selectedId is { } id)
                SelectedCompte = Comptes.FirstOrDefault(c => c.Id == id);
        });
    }

    private async Task LoadParametresAsync()
    {
        var api = Api;
        if (api is null) return;
        await RunAsync(async () =>
        {
            var parametres = await api.GetAsync<List<ParametreDto>>("api/admin/parametres");
            Parametres.Clear();
            foreach (var p in parametres)
                Parametres.Add(new ParametreRow(p, SaveParametreAsync));
        });
    }

    private async Task LoadHistoriqueAsync()
    {
        var api = Api;
        if (api is null) return;
        await RunAsync(async () =>
        {
            var query = "api/admin/historique?limit=300";
            if (!string.IsNullOrWhiteSpace(HistoriqueObjet)) query += "&objet=" + ApiClient.Q(HistoriqueObjet.Trim());
            if (!string.IsNullOrWhiteSpace(HistoriqueReference)) query += "&reference=" + ApiClient.Q(HistoriqueReference.Trim());
            var rows = await api.GetAsync<List<HistoriqueDto>>(query);
            Historique.Clear();
            foreach (var h in rows)
            {
                var detail = h.Champ is null
                    ? string.Empty
                    : $"{h.Champ} : {h.Ancienne ?? "—"} → {h.Nouvelle ?? "—"}";
                Historique.Add(new HistoriqueRow(FormatDate(h.Date), h.Auteur, h.Objet, h.Reference, h.Action, detail, h.Motif ?? string.Empty));
            }
        });
    }

    private async Task LoadJournalAsync()
    {
        var api = Api;
        if (api is null) return;
        await RunAsync(async () =>
        {
            var rows = await api.GetAsync<List<JournalAccesDto>>("api/admin/journal?limit=300");
            Journal.Clear();
            foreach (var j in rows)
                Journal.Add(new JournalRow(FormatDate(j.Date), j.Agent, j.Action, j.Cible, j.Resultat));
        });
    }

    // ===== Comptes : profils et statut =====

    private void BuildProfilChoices()
    {
        ProfilChoices.Clear();
        if (SelectedCompte is null) return;
        var current = SelectedCompte.Dto.Profils.ToHashSet(StringComparer.Ordinal);
        foreach (var p in _profils.OrderBy(p => p.Nom, StringComparer.Create(French, true)))
            ProfilChoices.Add(new ProfilChoice { Code = p.Code, Nom = p.Nom, Description = p.Description, IsChecked = current.Contains(p.Code) });
    }

    private async Task SaveProfilsAsync()
    {
        var api = Api;
        var compte = SelectedCompte;
        if (api is null || compte is null) return;
        var codes = ProfilChoices.Where(c => c.IsChecked).Select(c => c.Code).ToList();
        var names = codes.Count == 0 ? "aucun profil" : string.Join(", ", codes.Select(ProfilName));
        if (!await ConfirmAsync("Attribution des profils",
                $"Attribuer à {compte.Nom} {compte.Prenom} : {names} ?\nSes applications et ses droits changeront immédiatement."))
            return;

        await RunAsync(async () =>
        {
            await api.SendAsync(HttpMethod.Put, $"api/admin/comptes/{compte.Id}/profils", new ProfilsAgentInput(codes, ProfilsMotif));
            await LoadComptesAsync();
            await LoadProfilsAsync();
        }, "Profils enregistrés.");
    }

    private async Task ApplyStatutAsync()
    {
        var api = Api;
        var compte = SelectedCompte;
        if (api is null || compte is null) return;
        if (NewStatut == compte.Statut)
        {
            ErrorText = "Le compte a déjà ce statut.";
            return;
        }
        var bloquant = !AgentStatuts.PeutSeConnecter(NewStatut);
        if (!await ConfirmAsync("Changement de statut",
                $"Passer {compte.Nom} {compte.Prenom} au statut « {NewStatut} » ?" +
                (bloquant ? "\nL'agent ne pourra plus ouvrir de session ; ses interventions et procès-verbaux restent conservés." : string.Empty),
                "Appliquer", bloquant))
            return;

        await RunAsync(async () =>
        {
            await api.SendAsync(HttpMethod.Put, $"api/admin/comptes/{compte.Id}/statut", new StatutAgentInput(NewStatut, StatutMotif));
            await LoadComptesAsync();
        }, "Statut modifié.");
    }

    // ===== Profils : permissions =====

    private void StartNewProfil()
    {
        _selectedProfil = null;
        OnPropertyChanged(nameof(SelectedProfil));
        _isNewProfil = true;
        EditNom = string.Empty;
        EditDescription = string.Empty;
        BuildPermissionGroups([]);
        RaiseProfilEditor();
    }

    private void BuildPermissionGroups(IReadOnlyCollection<string> granted)
    {
        PermissionGroups.Clear();
        foreach (var group in Permissions.All.GroupBy(p => p.Module))
            PermissionGroups.Add(new PermissionGroup(group.Key,
                group.Select(p => new PermissionChoice { Code = p.Code, Libelle = p.Libelle, IsChecked = granted.Contains(p.Code) }).ToList()));
    }

    private async Task SaveProfilAsync()
    {
        var api = Api;
        if (api is null || !IsEditingProfil) return;
        if (string.IsNullOrWhiteSpace(EditNom))
        {
            ErrorText = "Le nom du profil est obligatoire.";
            return;
        }
        var permissions = PermissionGroups.SelectMany(g => g.Items).Where(i => i.IsChecked).Select(i => i.Code).ToList();
        var input = new ProfilInput(EditNom.Trim(), EditDescription.Trim(), permissions);
        var editing = SelectedProfil;

        if (editing is { NombreAgents: > 0 } &&
            !await ConfirmAsync("Modification d'un profil",
                $"Le profil « {editing.Nom} » est attribué à {editing.NombreAgents} agent(s). Leurs droits changeront immédiatement. Continuer ?"))
            return;

        await RunAsync(async () =>
        {
            var saved = editing is null
                ? await api.PostAsync<ProfilDto>("api/admin/profils", input)
                : await api.SendAsync<ProfilDto>(HttpMethod.Put, $"api/admin/profils/{editing.Id}", input);
            _isNewProfil = false;
            await LoadProfilsAsync();
            SelectedProfil = Profils.FirstOrDefault(p => p.Id == saved.Id);
        }, "Profil enregistré.");
    }

    private async Task DeleteProfilAsync()
    {
        var api = Api;
        var profil = SelectedProfil;
        if (api is null || profil is null) return;
        if (!await ConfirmAsync("Suppression d'un profil", $"Supprimer définitivement le profil « {profil.Nom} » ?", "Supprimer", true))
            return;
        await RunAsync(async () =>
        {
            await api.SendAsync(HttpMethod.Delete, $"api/admin/profils/{profil.Id}");
            SelectedProfil = null;
            await LoadProfilsAsync();
        }, "Profil supprimé.");
    }

    private void RaiseProfilEditor()
    {
        foreach (var name in new[] { nameof(IsEditingProfil), nameof(CanDeleteProfil), nameof(ProfilEditorTitle), nameof(ProfilEditorInfo) })
            OnPropertyChanged(name);
    }

    // ===== Paramètres =====

    private async Task SaveParametreAsync(ParametreRow row)
    {
        var api = Api;
        if (api is null) return;
        if (!await ConfirmAsync("Modification d'un paramètre", $"Enregistrer « {row.Libelle} » = « {row.Valeur} » pour tout SAPHIR ?"))
            return;
        await RunAsync(async () =>
        {
            await api.SendAsync(HttpMethod.Put, "api/admin/parametres/" + ApiClient.Q(row.Cle), new ParametreInput(row.Valeur));
            await LoadParametresAsync();
        }, "Paramètre enregistré.");
    }

    // ===== Aides =====

    private string ProfilName(string code)
        => _profils.FirstOrDefault(p => p.Code == code)?.Nom ?? DefaultProfiles.All.FirstOrDefault(p => p.Code == code)?.Nom ?? code;

    private static string FormatDate(DateTimeOffset date) => date.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", French);

    public override void Resume()
    {
        foreach (var name in new[] { nameof(CanComptes), nameof(CanProfils), nameof(CanParametres), nameof(CanJournaux) })
            OnPropertyChanged(name);
    }
}
