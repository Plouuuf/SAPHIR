using System.Collections.ObjectModel;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Rh;

namespace Saphir.ViewModels.Apps.Rh;

/// <summary>
/// Formulaire d'une modification d'agent (identité, matricule, RIO, grade, affectation, statut).
/// Réutilisé par le dossier individuel et par les pages Affectations et Gestion des RIO.
/// </summary>
public abstract class RhFormulaire : ViewModelBase
{
    private long? _agentId;
    private string _agentLibelle = string.Empty;
    private DateTime? _dateEffet;
    private string? _motif;
    private string? _observations;

    protected RhFormulaire(RhViewModel rh)
    {
        Rh = rh;
        EnregistrerCommand = new RelayCommand(() => _ = EnregistrerAsync());
    }

    public RhViewModel Rh { get; }

    public long? AgentId
    {
        get => _agentId;
        protected set
        {
            if (SetProperty(ref _agentId, value))
                OnPropertyChanged(nameof(HasAgent));
        }
    }

    public bool HasAgent => AgentId is not null;

    public string AgentLibelle
    {
        get => _agentLibelle;
        protected set => SetProperty(ref _agentLibelle, value);
    }

    public DateTime? DateEffet { get => _dateEffet; set => SetProperty(ref _dateEffet, value); }
    public string? Motif { get => _motif; set => SetProperty(ref _motif, value); }
    public string? Observations { get => _observations; set => SetProperty(ref _observations, value); }

    public RelayCommand EnregistrerCommand { get; }

    /// <summary>Appelé après un enregistrement réussi (rechargement de la page).</summary>
    public Func<Task>? Apres { get; set; }

    /// <summary>L'agent connecté a la permission de ce formulaire (sinon il est en lecture seule).</summary>
    public abstract bool Autorise { get; }

    public void RafraichirDroits() => OnPropertyChanged(nameof(Autorise));

    /// <summary>Charge l'agent ; <paramref name="reinitialiser"/> vide les champs saisis (changement d'agent).</summary>
    public void Charger(RhAgentLigneDto agent, bool reinitialiser)
    {
        var autre = AgentId != agent.Id;
        AgentId = agent.Id;
        AgentLibelle = $"{agent.Libelle} — {agent.Reference}";
        if (reinitialiser || autre)
        {
            DateEffet = DateTime.Today;
            Motif = null;
            Observations = null;
        }
        SurCharger(agent, reinitialiser || autre);
    }

    public void Vider()
    {
        AgentId = null;
        AgentLibelle = string.Empty;
    }

    protected abstract void SurCharger(RhAgentLigneDto agent, bool reinitialiser);

    protected abstract Task EnregistrerAsync();

    protected async Task TerminerAsync()
    {
        Motif = null;
        Observations = null;
        if (Apres is not null)
            await Apres();
    }

    protected bool MotifManquant()
    {
        if (!string.IsNullOrWhiteSpace(Motif))
            return false;
        Rh.Erreur("Le motif est obligatoire.");
        return true;
    }
}

/// <summary>Modification du NOM et du prénom (section 4.7).</summary>
public sealed class RhIdentiteFormulaire(RhViewModel rh) : RhFormulaire(rh)
{
    private string _nom = string.Empty;
    private string _prenom = string.Empty;
    private string _actuel = string.Empty;

    public override bool Autorise => RhDroits.ModifierAgent;
    public string Actuel { get => _actuel; private set => SetProperty(ref _actuel, value); }
    public string Nom { get => _nom; set => SetProperty(ref _nom, value); }
    public string Prenom { get => _prenom; set => SetProperty(ref _prenom, value); }

    protected override void SurCharger(RhAgentLigneDto agent, bool reinitialiser)
    {
        Actuel = $"{agent.Nom} {agent.Prenom}";
        if (reinitialiser)
        {
            Nom = agent.Nom;
            Prenom = agent.Prenom;
        }
    }

    protected override async Task EnregistrerAsync()
    {
        var api = SaphirSession.Api;
        if (api is null || AgentId is not { } id || MotifManquant())
            return;
        var nom = NomsFormat.Nom(Nom);
        var prenom = NomsFormat.Prenom(Prenom);
        if (!await Rh.Confirmer("Modification de l'identité",
                $"Remplacer « {Actuel} » par « {nom} {prenom} » ?\nLes fiches et les nouveaux documents utiliseront la nouvelle identité ; les actes déjà validés ne sont pas modifiés."))
            return;
        await Rh.Executer(async () =>
        {
            await api.SendAsync(HttpMethod.Put, $"api/rh/agents/{id}/identite",
                new RhIdentiteInput(nom, prenom, RhFormat.VersJour(DateEffet), Motif, Observations));
            await TerminerAsync();
        }, "Identité modifiée.");
    }
}

public sealed class RhMatriculeFormulaire(RhViewModel rh) : RhFormulaire(rh)
{
    private string _matricule = string.Empty;
    private string _actuel = string.Empty;

    public override bool Autorise => RhDroits.ModifierAgent;
    public string Actuel { get => _actuel; private set => SetProperty(ref _actuel, value); }
    public string Matricule { get => _matricule; set => SetProperty(ref _matricule, value); }

    protected override void SurCharger(RhAgentLigneDto agent, bool reinitialiser)
    {
        Actuel = RhFormat.Texte(agent.Matricule);
        if (reinitialiser)
            Matricule = agent.Matricule ?? string.Empty;
    }

    protected override async Task EnregistrerAsync()
    {
        var api = SaphirSession.Api;
        if (api is null || AgentId is not { } id || MotifManquant())
            return;
        await Rh.Executer(async () =>
        {
            await api.SendAsync(HttpMethod.Put, $"api/rh/agents/{id}/matricule", new RhMatriculeInput(Matricule, Motif));
            await TerminerAsync();
        }, "Matricule modifié.");
    }
}

public sealed class RhIntegrationFormulaire(RhViewModel rh) : RhFormulaire(rh)
{
    private DateTime? _date;

    public override bool Autorise => RhDroits.ModifierAgent;
    public DateTime? DateIntegration { get => _date; set => SetProperty(ref _date, value); }

    public void DefinirActuelle(DateOnly? date, bool reinitialiser)
    {
        if (reinitialiser || DateIntegration is null)
            DateIntegration = RhFormat.VersDate(date);
    }

    protected override void SurCharger(RhAgentLigneDto agent, bool reinitialiser)
    {
    }

    protected override async Task EnregistrerAsync()
    {
        var api = SaphirSession.Api;
        if (api is null || AgentId is not { } id)
            return;
        await Rh.Executer(async () =>
        {
            await api.SendAsync(HttpMethod.Put, $"api/rh/agents/{id}/integration",
                new RhIntegrationInput(RhFormat.VersJour(DateIntegration), Motif));
            await TerminerAsync();
        }, "Date d'intégration enregistrée.");
    }
}

/// <summary>Attribution ou changement de RIO (section 4.6).</summary>
public sealed class RhRioFormulaire(RhViewModel rh) : RhFormulaire(rh)
{
    private string _rio = string.Empty;
    private string _actuel = string.Empty;

    public override bool Autorise => RhDroits.Rio;
    public string Actuel { get => _actuel; private set => SetProperty(ref _actuel, value); }
    public string Rio { get => _rio; set => SetProperty(ref _rio, value); }

    public string Aide => Rh.ParametresRh is { } p
        ? $"Format en vigueur : {p.RioFormat} (exemple : {p.RioExemple}). Un RIO n'est jamais réattribué à un autre agent."
        : "Le RIO doit respecter le format défini par VDMS (7 chiffres par défaut). Un RIO n'est jamais réattribué.";

    protected override void SurCharger(RhAgentLigneDto agent, bool reinitialiser)
    {
        Actuel = agent.Rio ?? "Aucun RIO attribué";
        if (reinitialiser)
            Rio = string.Empty;
        OnPropertyChanged(nameof(Aide));
    }

    protected override async Task EnregistrerAsync()
    {
        var api = SaphirSession.Api;
        if (api is null || AgentId is not { } id)
            return;
        if (string.IsNullOrWhiteSpace(Rio))
        {
            Rh.Erreur("Saisis le nouveau RIO.");
            return;
        }
        var changement = Actuel != "Aucun RIO attribué";
        if (changement && MotifManquant())
            return;
        if (!await Rh.Confirmer(changement ? "Changement de RIO" : "Attribution du RIO",
                changement
                    ? $"Remplacer le RIO {Actuel} par {Rio.Trim()} pour {AgentLibelle} ?\nL'ancien RIO reste dans l'historique ; les actes validés conservent l'ancien."
                    : $"Attribuer le RIO {Rio.Trim()} à {AgentLibelle} ?"))
            return;
        await Rh.Executer(async () =>
        {
            await api.SendAsync(HttpMethod.Put, $"api/rh/agents/{id}/rio",
                new RhRioInput(Rio.Trim(), RhFormat.VersJour(DateEffet), Motif, Observations));
            Rio = string.Empty;
            await TerminerAsync();
        }, changement ? "RIO changé." : "RIO attribué.");
    }
}

/// <summary>Attribution d'un nouveau grade (section 4.8).</summary>
public sealed class RhGradeFormulaire : RhFormulaire
{
    private RhOption? _grade;
    private RhOption? _justificatif;
    private string _actuel = string.Empty;

    public RhGradeFormulaire(RhViewModel rh) : base(rh) => ReconstruireOptions();

    public override bool Autorise => RhDroits.Carriere;
    public string Actuel { get => _actuel; private set => SetProperty(ref _actuel, value); }
    public ObservableCollection<RhOption> Grades { get; } = [];
    public RhOption? Grade { get => _grade; set => SetProperty(ref _grade, value); }
    public ObservableCollection<RhOption> Justificatifs { get; } = [];
    public RhOption? Justificatif { get => _justificatif; set => SetProperty(ref _justificatif, value); }

    public void ReconstruireOptions()
    {
        var choix = Grade;
        Grades.Clear();
        foreach (var o in Rh.Options(RhReferentielTypes.Grades))
            Grades.Add(o);
        Grade = choix is null ? null : Grades.FirstOrDefault(o => o.Id == choix.Id);
    }

    /// <summary>Documents de l'agent proposés comme justificatif.</summary>
    public void DefinirJustificatifs(IEnumerable<RhDocumentDto> documents)
    {
        var choix = Justificatif?.Id;
        Justificatifs.Clear();
        Justificatifs.Add(new RhOption(null, "Aucun document justificatif"));
        foreach (var d in documents)
            Justificatifs.Add(new RhOption(d.Id, $"{d.Reference} — {d.Titre}"));
        Justificatif = Justificatifs.FirstOrDefault(j => j.Id == choix) ?? Justificatifs[0];
    }

    protected override void SurCharger(RhAgentLigneDto agent, bool reinitialiser)
    {
        Actuel = RhFormat.Texte(agent.Grade);
        if (reinitialiser)
            Grade = null;
    }

    protected override async Task EnregistrerAsync()
    {
        var api = SaphirSession.Api;
        if (api is null || AgentId is not { } id)
            return;
        if (Grade?.Id is not { } gradeId)
        {
            Rh.Erreur("Choisis le nouveau grade.");
            return;
        }
        if (MotifManquant())
            return;
        if (!await Rh.Confirmer("Changement de grade", $"Attribuer le grade « {Grade.Libelle} » à {AgentLibelle} (grade actuel : {Actuel}) ?"))
            return;
        await Rh.Executer(async () =>
        {
            await api.SendAsync(HttpMethod.Put, $"api/rh/agents/{id}/grade",
                new RhGradeInput(gradeId, RhFormat.VersJour(DateEffet), Motif, Observations, Justificatif?.Id));
            Grade = null;
            await TerminerAsync();
        }, "Grade attribué.");
    }
}

/// <summary>Nouvelle affectation : service, unité, fonction, responsable (section 4.10).</summary>
public sealed class RhAffectationFormulaire : RhFormulaire
{
    private RhOption? _service;
    private RhOption? _unite;
    private RhOption? _fonction;
    private RhOption? _responsable;
    private string _actuelle = string.Empty;

    public RhAffectationFormulaire(RhViewModel rh) : base(rh) => ReconstruireOptions();

    public override bool Autorise => RhDroits.Carriere;
    public string Actuelle { get => _actuelle; private set => SetProperty(ref _actuelle, value); }

    public ObservableCollection<RhOption> Services { get; } = [];
    public ObservableCollection<RhOption> Unites { get; } = [];
    public ObservableCollection<RhOption> Fonctions { get; } = [];
    public ObservableCollection<RhOption> Responsables { get; } = [];

    public RhOption? Service
    {
        get => _service;
        set
        {
            if (SetProperty(ref _service, value))
                FiltrerUnites();
        }
    }

    public RhOption? Unite { get => _unite; set => SetProperty(ref _unite, value); }
    public RhOption? Fonction { get => _fonction; set => SetProperty(ref _fonction, value); }
    public RhOption? Responsable { get => _responsable; set => SetProperty(ref _responsable, value); }

    public void ReconstruireOptions()
    {
        var (s, u, f, r) = (Service?.Id, Unite?.Id, Fonction?.Id, Responsable?.Id);
        Services.Clear();
        foreach (var o in Rh.Options(RhReferentielTypes.Services))
            Services.Add(o);
        Fonctions.Clear();
        foreach (var o in Rh.Options(RhReferentielTypes.Fonctions, vide: "Aucune fonction"))
            Fonctions.Add(o);
        Responsables.Clear();
        foreach (var o in Rh.OptionsAgents("Aucun responsable"))
            Responsables.Add(o);
        _service = Services.FirstOrDefault(o => o.Id == s);
        OnPropertyChanged(nameof(Service));
        FiltrerUnites();
        Unite = Unites.FirstOrDefault(o => o.Id == u);
        Fonction = Fonctions.FirstOrDefault(o => o.Id == f) ?? Fonctions.FirstOrDefault();
        Responsable = Responsables.FirstOrDefault(o => o.Id == r) ?? Responsables.FirstOrDefault();
    }

    private void FiltrerUnites()
    {
        var choix = Unite?.Id;
        Unites.Clear();
        if (Service?.Id is { } serviceId)
            foreach (var o in Rh.Options(RhReferentielTypes.Unites).Where(o => o.ParentId == serviceId))
                Unites.Add(o);
        Unite = Unites.FirstOrDefault(o => o.Id == choix);
    }

    /// <summary>Charge l'agent et préremplit avec son affectation actuelle.</summary>
    protected override void SurCharger(RhAgentLigneDto agent, bool reinitialiser)
    {
        Actuelle = agent.Service is null
            ? "Aucune affectation"
            : string.Join(" / ", new[] { agent.Service, agent.Unite, agent.Fonction }.Where(t => !string.IsNullOrWhiteSpace(t)));
        if (!reinitialiser)
            return;
        Service = Services.FirstOrDefault(o => o.Id == agent.ServiceId);
        Unite = Unites.FirstOrDefault(o => o.Id == agent.UniteId);
        Fonction = Fonctions.FirstOrDefault(o => o.Id == agent.FonctionId) ?? Fonctions.FirstOrDefault();
    }

    public void DefinirResponsable(long? responsableId)
        => Responsable = Responsables.FirstOrDefault(o => o.Id == responsableId) ?? Responsables.FirstOrDefault();

    protected override async Task EnregistrerAsync()
    {
        var api = SaphirSession.Api;
        if (api is null || AgentId is not { } id)
            return;
        if (Service?.Id is not { } serviceId || Unite?.Id is not { } uniteId)
        {
            Rh.Erreur("Choisis le service et l'unité.");
            return;
        }
        if (MotifManquant())
            return;
        var nouvelle = string.Join(" / ", new[] { Service.Libelle, Unite.Libelle, Fonction?.Id is null ? null : Fonction.Libelle }
            .Where(t => !string.IsNullOrWhiteSpace(t)));
        if (!await Rh.Confirmer("Nouvelle affectation",
                $"Affecter {AgentLibelle} : {nouvelle} ?\nAffectation actuelle : {Actuelle}. Elle sera close à la date d'effet."))
            return;
        await Rh.Executer(async () =>
        {
            await api.SendAsync(HttpMethod.Post, $"api/rh/agents/{id}/affectations",
                new RhAffectationInput(serviceId, uniteId, Fonction?.Id, Responsable?.Id, RhFormat.VersJour(DateEffet), Motif, Observations));
            await TerminerAsync();
        }, "Affectation enregistrée.");
    }
}

/// <summary>Statut administratif : suspension, inactivité, archivage, réactivation (section 4.11).</summary>
public sealed class RhStatutFormulaire(RhViewModel rh) : RhFormulaire(rh)
{
    private string _actuel = string.Empty;
    private string _statut = AgentStatuts.Actif;

    public override bool Autorise => RhDroits.Statut;
    public string Actuel { get => _actuel; private set => SetProperty(ref _actuel, value); }
    public IReadOnlyList<string> Statuts => AgentStatuts.All;
    public string Statut { get => _statut; set => SetProperty(ref _statut, value); }

    protected override void SurCharger(RhAgentLigneDto agent, bool reinitialiser)
    {
        Actuel = agent.Statut;
        if (reinitialiser)
            Statut = agent.Statut;
    }

    protected override async Task EnregistrerAsync()
    {
        var api = SaphirSession.Api;
        if (api is null || AgentId is not { } id)
            return;
        if (Statut == Actuel)
        {
            Rh.Erreur("L'agent a déjà ce statut.");
            return;
        }
        var bloquant = !AgentStatuts.PeutSeConnecter(Statut);
        if (bloquant && MotifManquant())
            return;
        if (!await Rh.Confirmer("Statut administratif",
                $"Passer {AgentLibelle} au statut « {Statut} » ?" +
                (bloquant ? "\nL'agent ne pourra plus ouvrir de session. Ses interventions et procès-verbaux restent conservés." : string.Empty),
                "Appliquer", bloquant))
            return;
        await Rh.Executer(async () =>
        {
            await api.SendAsync(HttpMethod.Put, $"api/rh/agents/{id}/statut", new RhStatutInput(Statut, Motif));
            await TerminerAsync();
        }, "Statut modifié.");
    }
}
