using System.Collections.ObjectModel;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Judiciaire;

namespace Saphir.ViewModels.Apps.Judiciaire;

/// <summary>Champ d'un formulaire d'acte (rendu selon son type).</summary>
public sealed class ChampSaisie(ChampActe champ, string? valeur) : ViewModelBase
{
    private string? _valeur = valeur;

    public ChampActe Champ { get; } = champ;
    public string Code => Champ.Code;
    public string Libelle => Champ.Obligatoire ? Champ.Libelle + " *" : Champ.Libelle;
    public string? Valeur { get => _valeur; set => SetProperty(ref _valeur, value); }
    public bool EstTexte => Champ.Type is ChampTypes.Texte or ChampTypes.Date or ChampTypes.Heure;
    public bool EstLong => Champ.Type == ChampTypes.TexteLong;
    public bool EstChoix => Champ.Type is ChampTypes.Choix or ChampTypes.OuiNon;
    public IReadOnlyList<string> Choix => Champ.Choix ?? Array.Empty<string>();
    public string Indice => Champ.Type switch
    {
        ChampTypes.Date => "jj/mm/aaaa",
        ChampTypes.Heure => "hh:mm",
        _ => string.Empty
    };
}

/// <summary>Section du formulaire (14.4 A à H, 15.x) avec ses blocs spéciaux.</summary>
public sealed class SectionActe(string titre, IReadOnlyList<ChampSaisie> champs, bool personne, bool misEnCause, bool agents, bool questions, bool pieces)
{
    public string Titre { get; } = titre;
    public IReadOnlyList<ChampSaisie> Champs { get; } = champs;
    public bool EstPersonne { get; } = personne;
    public bool EstMisEnCause { get; } = misEnCause;
    public bool EstAgents { get; } = agents;
    public bool EstQuestions { get; } = questions;
    public bool EstPieces { get; } = pieces;
}

/// <summary>Agent cité dans l'acte (modifiable tant que l'acte n'est pas validé).</summary>
public sealed class AgentActeLigne(ActeAgentDto dto) : ViewModelBase
{
    private string _role = dto.Role;
    private string _libelle = dto.Libelle;
    private string? _rio = dto.Rio, _grade = dto.Grade, _service = dto.Service;

    public long? AgentId { get; } = dto.AgentId;
    public string Role { get => _role; set => SetProperty(ref _role, value); }
    public string Libelle { get => _libelle; set => SetProperty(ref _libelle, value); }
    public string? Rio { get => _rio; set => SetProperty(ref _rio, value); }
    public string? Grade { get => _grade; set => SetProperty(ref _grade, value); }
    public string? Service { get => _service; set => SetProperty(ref _service, value); }
    public ActeAgentDto Dto() => new(AgentId, Role, Libelle, Rio, Grade, Service);
}

public sealed class QuestionLigne(string question, string reponse) : ViewModelBase
{
    private string _question = question;
    private string _reponse = reponse;
    public string Question { get => _question; set => SetProperty(ref _question, value); }
    public string Reponse { get => _reponse; set => SetProperty(ref _reponse, value); }
}

public sealed class PieceChoix(PieceDto piece, bool coche) : ViewModelBase
{
    private bool _coche = coche;
    public long Id { get; } = piece.Id;
    public string Libelle { get; } = $"{piece.Numero} — {piece.Categorie} : {piece.Libelle}" + (piece.NumeroScelle is null ? string.Empty : $" (scellé {piece.NumeroScelle})");
    public bool Coche { get => _coche; set => SetProperty(ref _coche, value); }
}

/// <summary>
/// Éditeur d'un acte (14.4, 15) : sections dynamiques, identité préremplie depuis la fiche (14.2) avec dossiers associés (14.3),
/// agents du référentiel RH, questions/réponses, pièces, et boutons H (brouillon, aperçu, document, relecture, correction,
/// validation, PDF, historique). Un acte validé est figé : seules les mentions restent possibles.
/// </summary>
public sealed class ActeEditeurViewModel : SousVueBase
{
    private readonly LrppnViewModel _lrppn;
    private ActeDto _acte;
    private string _titreActe;
    private string? _apercu, _motifCorrection, _nouvelleMention, _dossiersPersonne, _avertissement;
    private bool _apercuVisible, _historiqueVisible;
    private AgentResumeDto? _agentAAjouter;
    private string _roleAAjouter = "Interpellateur";

    public ActeEditeurViewModel(LrppnViewModel lrppn, ActeDto acte, ProcedureDto? procedure) : base(lrppn)
    {
        _lrppn = lrppn;
        _acte = acte;
        _titreActe = acte.Titre;
        Type = ActesCatalogue.Trouver(acte.Type) ?? throw new ApiException("Type d'acte inconnu de ce poste : mets SAPHIR à jour.");
        Procedure = procedure;

        PickerPersonne = new PersonnePickerViewModel(lrppn, p => { if (p is not null) _ = PreremplirIdentiteAsync(p.Id); });
        PickerMisEnCause = new PersonnePickerViewModel(lrppn);

        EnregistrerCommand = new RelayCommand(() => _ = EnregistrerAsync());
        PrevisualiserCommand = new RelayCommand(() => _ = PrevisualiserAsync());
        GenererCommand = new RelayCommand(() => _ = GenererAsync());
        RelectureCommand = new RelayCommand(() => _ = RelectureAsync());
        CorrigerCommand = new RelayCommand(() => _ = CorrigerAsync());
        ValiderCommand = new RelayCommand(() => _ = ValiderAsync());
        ExporterCommand = new RelayCommand(() => _ = Executer(() => _lrppn.TelechargerAsync($"api/lrppn/actes/{Acte.Id}/pdf")));
        HistoriqueCommand = new RelayCommand(() => _ = HistoriqueAsync());
        SupprimerCommand = new RelayCommand(() => _ = SupprimerAsync());
        MentionCommand = new RelayCommand(() => _ = MentionAsync());
        RetourCommand = new RelayCommand(() => _ = _lrppn.RetourDossierAsync());
        AjouterAgentCommand = new RelayCommand(AjouterAgent);
        AjouterAgentLibreCommand = new RelayCommand(() => Agents.Add(new AgentActeLigne(new ActeAgentDto(null, RoleAAjouter, "NOM Prénom", null, null, null))));
        RetirerAgentCommand = new RelayCommand(p => { if (p is AgentActeLigne a) Agents.Remove(a); });
        AjouterQuestionCommand = new RelayCommand(() => Questions.Add(new QuestionLigne(string.Empty, string.Empty)));
        RetirerQuestionCommand = new RelayCommand(p => { if (p is QuestionLigne q) Questions.Remove(q); });
        FermerApercuCommand = new RelayCommand(() => ApercuVisible = false);
        FermerHistoriqueCommand = new RelayCommand(() => HistoriqueVisible = false);

        Charger(acte);
    }

    public TypeActeInfo Type { get; }
    public ProcedureDto? Procedure { get; }

    public ActeDto Acte
    {
        get => _acte;
        private set => SetProperty(ref _acte, value);
    }

    // ===== État =====

    public bool EstFige => Acte.Statut == ActeStatuts.Valide;
    public bool EstModifiable => !EstFige && SaphirSession.Has(Permissions.LrppnRediger);
    public bool EstEnRelecture => Acte.Statut == ActeStatuts.EnRelecture;
    public bool EstBrouillon => Acte.Statut == ActeStatuts.Brouillon;
    public bool PeutValider => !EstFige && SaphirSession.Has(Permissions.LrppnValider);
    public bool PeutCorriger => EstEnRelecture && SaphirSession.Has(Permissions.LrppnValider);
    public bool PeutRelecture => EstBrouillon && SaphirSession.Has(Permissions.LrppnRediger);
    public bool PeutMention => EstFige && (SaphirSession.Has(Permissions.LrppnRediger) || SaphirSession.Has(Permissions.LrppnValider));

    public string Entete => $"{Type.Libelle} n° {Acte.Numero} — procédure {Acte.ProcedureNumero}";

    public string Etat => $"Statut : {Acte.Statut}   Version : {Acte.Version}   Rédacteur : {Acte.Redacteur}" +
                          (Acte.RedacteurGrade is null ? string.Empty : $", {Acte.RedacteurGrade}") +
                          $" — RIO {Acte.RedacteurRio ?? "non attribué"} — {Acte.RedacteurService ?? "service non renseigné"}" +
                          (Acte.InterventionNumero is null ? string.Empty : $"   Intervention : {Acte.InterventionNumero}");

    public string Validation => Acte.ValideLe is { } v
        ? $"Acte validé le {FormatsJ.DateHeure(v)} par {Acte.ValidePar}. Il est figé (empreinte SHA-256 {Acte.Empreinte})."
        : string.Empty;

    public string TitreActe { get => _titreActe; set => SetProperty(ref _titreActe, value); }
    public string? Avertissement { get => _avertissement; private set { if (SetProperty(ref _avertissement, value)) OnPropertyChanged(nameof(AAvertissement)); } }
    public bool AAvertissement => !string.IsNullOrEmpty(Avertissement);

    // ===== Contenu =====

    public ObservableCollection<SectionActe> Sections { get; } = [];
    public ObservableCollection<AgentActeLigne> Agents { get; } = [];
    public ObservableCollection<QuestionLigne> Questions { get; } = [];
    public ObservableCollection<PieceChoix> Pieces { get; } = [];
    public ObservableCollection<MentionDto> Mentions { get; } = [];
    public ObservableCollection<AgentResumeDto> AgentsDisponibles { get; } = [];
    public IReadOnlyList<string> RolesAgents => ActesCatalogue.RolesAgents;

    public PersonnePickerViewModel PickerPersonne { get; }
    public PersonnePickerViewModel PickerMisEnCause { get; }
    public string LibellePersonne => Type.LibellePersonne ?? "Personne";
    public string LibelleMisEnCause => Type.LibelleMisEnCause ?? "Mis en cause";
    public string? DossiersPersonne { get => _dossiersPersonne; private set { if (SetProperty(ref _dossiersPersonne, value)) OnPropertyChanged(nameof(ADossiersPersonne)); } }
    public bool ADossiersPersonne => !string.IsNullOrEmpty(DossiersPersonne);

    public AgentResumeDto? AgentAAjouter { get => _agentAAjouter; set => SetProperty(ref _agentAAjouter, value); }
    public string RoleAAjouter { get => _roleAAjouter; set => SetProperty(ref _roleAAjouter, value); }

    public string? Apercu { get => _apercu; private set => SetProperty(ref _apercu, value); }
    public bool ApercuVisible { get => _apercuVisible; set => SetProperty(ref _apercuVisible, value); }
    public ObservableCollection<HistoriqueLigne> Historique { get; } = [];
    public bool HistoriqueVisible { get => _historiqueVisible; set => SetProperty(ref _historiqueVisible, value); }
    public string? MotifCorrection { get => _motifCorrection; set => SetProperty(ref _motifCorrection, value); }
    public string? NouvelleMention { get => _nouvelleMention; set => SetProperty(ref _nouvelleMention, value); }

    // ===== Commandes (14.4 H) =====

    public RelayCommand EnregistrerCommand { get; }
    public RelayCommand PrevisualiserCommand { get; }
    public RelayCommand GenererCommand { get; }
    public RelayCommand RelectureCommand { get; }
    public RelayCommand CorrigerCommand { get; }
    public RelayCommand ValiderCommand { get; }
    public RelayCommand ExporterCommand { get; }
    public RelayCommand HistoriqueCommand { get; }
    public RelayCommand SupprimerCommand { get; }
    public RelayCommand MentionCommand { get; }
    public RelayCommand RetourCommand { get; }
    public RelayCommand AjouterAgentCommand { get; }
    public RelayCommand AjouterAgentLibreCommand { get; }
    public RelayCommand RetirerAgentCommand { get; }
    public RelayCommand AjouterQuestionCommand { get; }
    public RelayCommand RetirerQuestionCommand { get; }
    public RelayCommand FermerApercuCommand { get; }
    public RelayCommand FermerHistoriqueCommand { get; }

    /// <summary>Chargements complémentaires : agents du référentiel RH.</summary>
    public async Task InitialiserAsync()
    {
        if (Api is not { } api || EstFige || Type.SectionAgents is null)
            return;
        var agents = await AgentsReferentiel.ChargerAsync(api);
        AgentsDisponibles.Clear();
        foreach (var a in agents)
            AgentsDisponibles.Add(a);
    }

    private void Charger(ActeDto acte)
    {
        Acte = acte;
        TitreActe = acte.Titre;
        Sections.Clear();
        foreach (var titre in Type.Sections)
        {
            var champs = Type.Champs.Where(c => c.Section == titre)
                .Select(c => new ChampSaisie(c, acte.Champs.TryGetValue(c.Code, out var v) ? v : null))
                .ToList();
            Sections.Add(new SectionActe(titre, champs, titre == Type.SectionPersonne, titre == Type.SectionMisEnCause, titre == Type.SectionAgents,
                titre == Type.SectionQuestions, titre == Type.SectionPieces));
        }
        Agents.Clear();
        foreach (var a in acte.Agents)
            Agents.Add(new AgentActeLigne(a));
        Questions.Clear();
        foreach (var q in acte.QuestionsReponses)
            Questions.Add(new QuestionLigne(q.Question, q.Reponse));
        Pieces.Clear();
        foreach (var p in Procedure?.Pieces ?? Array.Empty<PieceDto>())
            Pieces.Add(new PieceChoix(p, acte.PiecesIds.Contains(p.Id)));
        Mentions.Clear();
        foreach (var m in acte.Mentions)
            Mentions.Add(m);
        PickerPersonne.Definir(acte.PersonneId is { } pid ? new PersonneResumeDto(pid, string.Empty, acte.Personne ?? string.Empty, string.Empty, null, null, null, Array.Empty<string>()) : null);
        PickerMisEnCause.Definir(acte.MisEnCauseId is { } mid ? new PersonneResumeDto(mid, string.Empty, acte.MisEnCause ?? string.Empty, string.Empty, null, null, null, Array.Empty<string>()) : null);
        Avertissement = null;
        if (EstFige)
        {
            Apercu = acte.TexteFige;
            ApercuVisible = true;
        }
        foreach (var nom in new[] { nameof(EstFige), nameof(EstModifiable), nameof(EstEnRelecture), nameof(EstBrouillon), nameof(PeutValider), nameof(PeutCorriger),
                     nameof(PeutRelecture), nameof(PeutMention), nameof(Entete), nameof(Etat), nameof(Validation) })
            OnPropertyChanged(nom);
    }

    /// <summary>La procédure a été modifiée ailleurs : prévenir sans écraser la saisie en cours.</summary>
    public void SignalerModificationExterne()
    {
        if (!EstFige)
            Avertissement = "La procédure a été modifiée par un autre agent. Enregistre ton brouillon : en cas de conflit, recharge l'acte.";
    }

    private ActeInput Saisie() => new(
        TitreActe,
        PickerPersonne.Selection?.Id,
        PickerMisEnCause.Selection?.Id,
        Sections.SelectMany(s => s.Champs).ToDictionary(c => c.Code, c => c.Valeur),
        Agents.Select(a => a.Dto()).ToList(),
        Questions.Select(q => new QuestionReponseDto(q.Question, q.Reponse)).ToList(),
        Pieces.Where(p => p.Coche).Select(p => p.Id).ToList(),
        Acte.Version);

    /// <summary>14.2 : identité reprise de la fiche sélectionnée, dossiers associés affichés selon les habilitations.</summary>
    private async Task PreremplirIdentiteAsync(long personneId)
    {
        if (Api is not { } api || EstFige)
            return;
        await Executer(async () =>
        {
            var dossiers = await api.GetAsync<DossiersAssociesDto>($"api/lrppn/personnes/{personneId}/dossiers");
            var valeurs = ActesCatalogue.ChampsIdentite(Type.Code, dossiers.Personne);
            foreach (var champ in Sections.SelectMany(s => s.Champs))
                if (valeurs.TryGetValue(champ.Code, out var valeur) && !string.IsNullOrWhiteSpace(valeur))
                    champ.Valeur = valeur;
            DossiersPersonne = DossiersFormat.Texte(dossiers);
        }, "Identité préremplie depuis la fiche : vérifie-la auprès de la personne.");
    }

    private void AjouterAgent()
    {
        if (AgentAAjouter is not { } a)
            return;
        Agents.Add(new AgentActeLigne(new ActeAgentDto(a.Id, RoleAAjouter, $"{a.Nom} {a.Prenom}", a.Rio, a.Grade, a.Service)));
        AgentAAjouter = null;
    }

    private async Task<bool> EnregistrerSiModifiableAsync(ApiClient api)
    {
        if (!EstModifiable)
            return false;
        Charger(await api.SendAsync<ActeDto>(HttpMethod.Put, $"api/lrppn/actes/{Acte.Id}", Saisie()));
        return true;
    }

    private async Task EnregistrerAsync()
    {
        if (Api is not { } api)
            return;
        await Executer(async () => { await EnregistrerSiModifiableAsync(api); }, "Brouillon enregistré (nouvelle version).");
    }

    private async Task PrevisualiserAsync()
    {
        if (Api is not { } api)
            return;
        await Executer(async () =>
        {
            await EnregistrerSiModifiableAsync(api);
            var apercu = await api.GetAsync<ApercuDto>($"api/lrppn/actes/{Acte.Id}/apercu");
            Apercu = apercu.Texte;
            ApercuVisible = true;
        });
    }

    private async Task GenererAsync()
    {
        if (Api is not { } api)
            return;
        await Executer(async () =>
        {
            await EnregistrerSiModifiableAsync(api);
            await _lrppn.TelechargerAsync($"api/lrppn/actes/{Acte.Id}/pdf");
        });
    }

    private async Task RelectureAsync()
    {
        if (Api is not { } api)
            return;
        await Executer(async () =>
        {
            await EnregistrerSiModifiableAsync(api);
            Charger(await api.PostAsync<ActeDto>($"api/lrppn/actes/{Acte.Id}/relecture", new { }));
        }, "Relecture demandée : l'acte apparaît dans « Actes à valider ».");
    }

    private async Task CorrigerAsync()
    {
        if (Api is not { } api)
            return;
        await Executer(async () =>
        {
            Charger(await api.PostAsync<ActeDto>($"api/lrppn/actes/{Acte.Id}/correction", new MotifInput(MotifCorrection)));
            MotifCorrection = null;
        }, "Acte renvoyé au rédacteur pour correction.");
    }

    private async Task ValiderAsync()
    {
        if (Api is not { } api)
            return;
        if (!await Hote.ConfirmerAsync("Valider l'acte",
                $"Valider « {Type.Libelle} » n° {Acte.Numero} ? L'acte sera figé (plus aucune modification), historisé et indexé dans ANADOC." +
                (Type.InscriptionTaj ? " La personne mise en cause sera inscrite au TAJ." : string.Empty), "Valider", true))
            return;
        await Executer(async () =>
        {
            await EnregistrerSiModifiableAsync(api);
            Charger(await api.PostAsync<ActeDto>($"api/lrppn/actes/{Acte.Id}/valider", new { }));
        }, "Acte validé et figé.");
    }

    private async Task HistoriqueAsync()
    {
        if (Api is not { } api)
            return;
        await Executer(async () =>
        {
            Historique.Clear();
            foreach (var h in await api.GetAsync<List<HistoriqueDto>>($"api/lrppn/actes/{Acte.Id}/historique"))
                Historique.Add(HistoriqueLigne.De(h));
            HistoriqueVisible = true;
        });
    }

    private async Task SupprimerAsync()
    {
        if (Api is not { } api || !EstBrouillon)
            return;
        if (!await Hote.ConfirmerAsync("Supprimer le brouillon", $"Supprimer définitivement le brouillon {Acte.Numero} ?", "Supprimer", true))
            return;
        await Executer(async () =>
        {
            await api.SendAsync(HttpMethod.Delete, $"api/lrppn/actes/{Acte.Id}");
            await _lrppn.RetourDossierAsync();
        }, "Brouillon supprimé.");
    }

    private async Task MentionAsync()
    {
        if (Api is not { } api || string.IsNullOrWhiteSpace(NouvelleMention))
            return;
        await Executer(async () =>
        {
            Charger(await api.PostAsync<ActeDto>($"api/lrppn/actes/{Acte.Id}/mentions", new MentionInput(NouvelleMention!)));
            NouvelleMention = null;
        }, "Mention ajoutée à l'acte validé.");
    }
}
