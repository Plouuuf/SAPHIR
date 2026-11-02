using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Saphir.Services;
using Saphir.Shared.Contracts;

namespace Saphir.ViewModels.Apps;

/// <summary>Ligne « libellé : valeur ».</summary>
public sealed record InfoRow(string Label, string Value);

/// <summary>
/// Centre de paramètres : mon compte (fiche agent, profils, applications accessibles), à propos (version, serveur).
/// </summary>
public sealed class SettingsViewModel : AppViewModelBase
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public SettingsViewModel()
    {
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync());
        SaphirSession.Changed += Rebuild;
        Rebuild();
    }

    public ObservableCollection<InfoRow> AccountRows { get; } = [];
    public ObservableCollection<string> ProfileNames { get; } = [];
    public ObservableCollection<string> AccessibleApps { get; } = [];
    public ObservableCollection<InfoRow> AboutRows { get; } = [];

    public string AgentName => SaphirSession.Agent?.NomComplet ?? SaphirSession.User?.DisplayName ?? string.Empty;
    public string AgentStatus => SaphirSession.Agent?.Statut ?? string.Empty;
    public bool IsIncomplete => SaphirSession.Agent?.Statut == AgentStatuts.ProfilACompleter;
    public string Version => SaphirSession.Version;
    public int PermissionCount => SaphirSession.Info?.Permissions.Count ?? 0;

    public RelayCommand RefreshCommand { get; }

    private async Task RefreshAsync()
    {
        var api = Api;
        if (api is null) return;
        await RunAsync(async () => SaphirSession.SetInfo(await api.GetAsync<SessionInfoDto>("api/session")),
            "Informations actualisées.");
    }

    private void Rebuild()
    {
        AccountRows.Clear();
        ProfileNames.Clear();
        AccessibleApps.Clear();
        AboutRows.Clear();

        var agent = SaphirSession.Agent;
        var user = SaphirSession.User;
        if (agent is not null)
        {
            Add(AccountRows, "Identifiant SAPHIR", agent.Reference);
            Add(AccountRows, "NOM", agent.Nom);
            Add(AccountRows, "Prénom", agent.Prenom);
            Add(AccountRows, "RIO", agent.Rio ?? "Non attribué");
            Add(AccountRows, "Matricule", agent.Matricule ?? "—");
            Add(AccountRows, "Grade", agent.Grade ?? "Non renseigné");
            Add(AccountRows, "Service", agent.Service ?? "Non renseigné");
            Add(AccountRows, "Unité", agent.Unite ?? "Non renseignée");
            Add(AccountRows, "Fonction", agent.Fonction ?? "Non renseignée");
            Add(AccountRows, "Statut", agent.Statut);
            Add(AccountRows, "Compte créé le", agent.CreeLe.ToLocalTime().ToString("dd/MM/yyyy à HH:mm", French));
        }
        if (user is not null)
            Add(AccountRows, "Compte Discord", $"{user.DisplayName} ({user.UserId})");
        Add(AccountRows, "Rôle Discord RH", SaphirSession.RoleRh ? "Oui" : "Non");

        foreach (var code in SaphirSession.Info?.Profils ?? Array.Empty<string>())
            ProfileNames.Add(DefaultProfiles.All.FirstOrDefault(p => p.Code == code)?.Nom ?? code);

        foreach (var app in AppCatalog.All.Where(a => SaphirSession.CanOpen(a.Id)))
            AccessibleApps.Add(app.Title);

        Add(AboutRows, "Version", SaphirSession.Version);
        Add(AboutRows, "Serveur", ClientSettings.Current.HttpBaseUri.ToString().TrimEnd('/'));
        Add(AboutRows, "Plateforme", RuntimeInformation.OSDescription);
        Add(AboutRows, "Organisation", "Police nationale — Val-de-Marne Simulation");
        Add(AboutRows, "Données", "Toutes les données de SAPHIR sont fictives (simulation).");

        foreach (var name in new[] { nameof(AgentName), nameof(AgentStatus), nameof(IsIncomplete), nameof(Version), nameof(PermissionCount) })
            OnPropertyChanged(name);
    }

    private static void Add(ICollection<InfoRow> rows, string label, string value) => rows.Add(new InfoRow(label, value));

    public override void Suspend() => SaphirSession.Changed -= Rebuild;

    public override void Resume()
    {
        SaphirSession.Changed -= Rebuild;
        SaphirSession.Changed += Rebuild;
        Rebuild();
    }
}
