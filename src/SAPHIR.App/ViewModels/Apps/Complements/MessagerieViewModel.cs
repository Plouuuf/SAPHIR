using System.Collections.ObjectModel;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Complements;

namespace Saphir.ViewModels.Apps.Complements;

/// <summary>Ligne d'une boîte de messagerie.</summary>
public sealed class MessageRow(MessageResumeDto dto, bool envoye)
{
    public MessageResumeDto Dto { get; } = dto;
    public long Id => Dto.Id;
    public string Correspondant => envoye || Dto.Brouillon ? "À : " + (Dto.Destinataires.Length == 0 ? "(aucun destinataire)" : Dto.Destinataires) : Dto.Expediteur;
    public string Sujet => Dto.Sujet;
    public string Date => ComplementsViewModelBase.DateCourte(Dto.Date);
    public bool NonLu => !Dto.Lu;
    public bool Important => Dto.Important;
    public bool HasPieces => Dto.NombrePieces > 0;
    public string Pieces => Dto.NombrePieces + " PJ";
}

/// <summary>Destinataire affiché avec son accusé de lecture.</summary>
public sealed record DestinataireRow(string Libelle, string Type, string Lecture, bool Lu);

/// <summary>Pièce jointe d'un message en cours de rédaction (déjà enregistrée ou nouvelle).</summary>
public sealed class PieceComposee(string nom, long taille, long? id, PieceJointeInput? nouvelle)
{
    public string Nom { get; } = nom;
    public string Taille { get; } = PiecesJointes.TailleLisible(taille);
    public long? Id { get; } = id;
    public PieceJointeInput? Nouvelle { get; } = nouvelle;
}

/// <summary>Message d'une conversation (fil).</summary>
public sealed record ConversationRow(long Id, string Expediteur, string Date, string Apercu, bool Courant);

/// <summary>
/// MESSAGERIE interne : réception, envoyés, brouillons, corbeille, lecture avec accusés, conversations,
/// rédaction (destinataires, copie, liste de diffusion par service, pièces jointes, références SAPHIR).
/// </summary>
public sealed class MessagerieViewModel : ComplementsViewModelBase
{
    public const string SectionNouveau = "nouveau";

    private readonly SectionMenu _menuReception;
    private readonly SectionMenu _menuBrouillons;
    private string _recherche = string.Empty;
    private MessageRow? _selection;
    private MessageDto? _ouvert;
    private string? _argument;

    // Rédaction
    private long? _brouillonId;
    private int _brouillonVersion;
    private long? _reponseA;
    private string _sujet = string.Empty;
    private string _corps = string.Empty;
    private bool _important;
    private string? _listeDiffusion;
    private string _nouveauLien = string.Empty;
    private string? _serviceDiffusion;

    public MessagerieViewModel(string? argument = null)
    {
        _argument = argument ?? SaphirSession.TakeAppArgument(SaphirApps.Messagerie);
        AjouterSection(SectionNouveau, "Nouveau message");
        _menuReception = AjouterSection(MessagerieDossiers.Reception, "Boîte de réception");
        AjouterSection(MessagerieDossiers.Envoyes, "Messages envoyés");
        _menuBrouillons = AjouterSection(MessagerieDossiers.Brouillons, "Brouillons");
        AjouterSection(MessagerieDossiers.Corbeille, "Corbeille");

        A = new SelecteurAgentsViewModel("À");
        Cc = new SelecteurAgentsViewModel("Copie à (Cc)");

        RechercherCommand = new RelayCommand(() => _ = ChargerListeAsync());
        ActualiserCommand = new RelayCommand(() => _ = ToutChargerAsync());
        RepondreCommand = new RelayCommand(() => Repondre(false));
        RepondreTousCommand = new RelayCommand(() => Repondre(true));
        TransfererCommand = new RelayCommand(Transferer);
        NonLuCommand = new RelayCommand(() => _ = MarquerNonLuAsync());
        SupprimerCommand = new RelayCommand(() => _ = SupprimerAsync());
        RestaurerCommand = new RelayCommand(() => _ = RestaurerAsync());
        ModifierBrouillonCommand = new RelayCommand(ModifierBrouillon);
        EnvoyerCommand = new RelayCommand(() => _ = EnregistrerAsync(true));
        EnregistrerBrouillonCommand = new RelayCommand(() => _ = EnregistrerAsync(false));
        AbandonnerCommand = new RelayCommand(() => _ = AbandonnerAsync());
        AjouterPieceCommand = new RelayCommand(() => _ = AjouterPieceAsync());
        RetirerPieceCommand = new RelayCommand(p => { if (p is PieceComposee piece) PiecesComposees.Remove(piece); });
        AjouterLienCommand = new RelayCommand(AjouterLien);
        RetirerLienCommand = new RelayCommand(p => { if (p is string l) LiensComposes.Remove(l); });
        AjouterDiffusionCommand = new RelayCommand(AjouterDiffusion);
        EnregistrerPieceCommand = new RelayCommand(p => { if (p is PieceJointeDto piece) _ = EnregistrerPieceAsync($"api/messagerie/pieces/{piece.Id}"); });
        OuvrirLienCommand = new RelayCommand(p => { if (p is string l) OuvrirLien(l); });
        OuvrirConversationCommand = new RelayCommand(p => { if (p is ConversationRow c) _ = OuvrirAsync(c.Id); });
        RetourListeCommand = new RelayCommand(() => { Selection = null; Ouvert = null; });

        Section = MessagerieDossiers.Reception;
        _ = DemarrerAsync();
    }

    public override string TitreMenu => "MESSAGERIE";
    protected override double LargeurDetailLarge => 470;

    public bool IsNouveau => Section == SectionNouveau;
    public bool IsDossier => Section != SectionNouveau;
    public bool IsCorbeille => Section == MessagerieDossiers.Corbeille;
    public string TitreDossier => Menu.FirstOrDefault(m => m.Code == Section)?.Libelle ?? string.Empty;

    protected override void OnSectionChanged()
    {
        OnPropertyChanged(nameof(IsNouveau));
        OnPropertyChanged(nameof(IsDossier));
        OnPropertyChanged(nameof(IsCorbeille));
        OnPropertyChanged(nameof(TitreDossier));
        if (Section == SectionNouveau)
        {
            if (!_redactionEnCours) NouvelleRedaction();
            return;
        }
        Selection = null;
        Ouvert = null;
        _ = ChargerListeAsync();
    }

    protected override void OnCompactChanged() => RafraichirPanneaux();

    // ===== Liste =====

    public ObservableCollection<MessageRow> Messages { get; } = [];
    public string Recherche { get => _recherche; set => SetProperty(ref _recherche, value); }
    public bool ListeVide => Messages.Count == 0;

    public MessageRow? Selection
    {
        get => _selection;
        set
        {
            if (!SetProperty(ref _selection, value)) return;
            if (value is not null)
                _ = OuvrirAsync(value.Id);
            RafraichirPanneaux();
        }
    }

    // ===== Lecture =====

    public MessageDto? Ouvert
    {
        get => _ouvert;
        private set
        {
            if (!SetProperty(ref _ouvert, value)) return;
            Destinataires.Clear();
            Conversation.Clear();
            if (value is not null)
            {
                var expediteur = value.ExpediteurId == SaphirSession.Agent?.Id;
                foreach (var d in value.Destinataires)
                    Destinataires.Add(new DestinataireRow(d.Libelle, d.Type, expediteur
                        ? (d.LuLe is { } lu ? "Lu le " + Date(lu) : value.Brouillon ? string.Empty : "Non lu")
                        : string.Empty, d.LuLe is not null));
            }
            foreach (var n in new[] { nameof(HasOuvert), nameof(OuvertDate), nameof(OuvertBrouillon), nameof(OuvertRecu), nameof(OuvertEnvoye),
                         nameof(HasPiecesOuvert), nameof(HasLiensOuvert), nameof(HasConversation), nameof(OuvertDiffusion), nameof(HasDiffusion) })
                OnPropertyChanged(n);
            RafraichirPanneaux();
        }
    }

    public bool HasOuvert => Ouvert is not null;
    public string OuvertDate => Ouvert is null ? string.Empty : Date(Ouvert.EnvoyeLe ?? Ouvert.CreeLe);
    public bool OuvertBrouillon => Ouvert?.Brouillon == true;
    public bool OuvertRecu => Ouvert is { Brouillon: false } m && m.Destinataires.Any(d => d.AgentId == SaphirSession.Agent?.Id);
    public bool OuvertEnvoye => Ouvert is { Brouillon: false };
    public bool HasPiecesOuvert => Ouvert?.Pieces.Count > 0;
    public bool HasLiensOuvert => Ouvert?.Liens.Count > 0;
    public string? OuvertDiffusion => Ouvert?.ListeDiffusion is { } l ? "Liste de diffusion : " + l : null;
    public bool HasDiffusion => Ouvert?.ListeDiffusion is not null;
    public ObservableCollection<DestinataireRow> Destinataires { get; } = [];
    public ObservableCollection<ConversationRow> Conversation { get; } = [];
    public bool HasConversation => Conversation.Count > 1;

    public bool AfficherListe => IsLarge || Ouvert is null;
    public bool AfficherLecture => IsLarge || Ouvert is not null;

    private void RafraichirPanneaux()
    {
        OnPropertyChanged(nameof(AfficherListe));
        OnPropertyChanged(nameof(AfficherLecture));
    }

    // ===== Rédaction =====

    private bool _redactionEnCours;

    public SelecteurAgentsViewModel A { get; }
    public SelecteurAgentsViewModel Cc { get; }
    public ObservableCollection<PieceComposee> PiecesComposees { get; } = [];
    public ObservableCollection<string> LiensComposes { get; } = [];
    public ObservableCollection<string> ServicesDiffusion { get; } = [];
    public string Sujet { get => _sujet; set => SetProperty(ref _sujet, value); }
    public string Corps { get => _corps; set => SetProperty(ref _corps, value); }
    public bool Important { get => _important; set => SetProperty(ref _important, value); }
    public string? ListeDiffusion { get => _listeDiffusion; set => SetProperty(ref _listeDiffusion, value); }
    public string NouveauLien { get => _nouveauLien; set => SetProperty(ref _nouveauLien, value); }
    public string? ServiceDiffusion { get => _serviceDiffusion; set => SetProperty(ref _serviceDiffusion, value); }
    public string TitreRedaction => _brouillonId is null ? (_reponseA is null ? "Nouveau message" : "Réponse") : "Brouillon";

    // ===== Commandes =====

    public RelayCommand RechercherCommand { get; }
    public RelayCommand ActualiserCommand { get; }
    public RelayCommand RepondreCommand { get; }
    public RelayCommand RepondreTousCommand { get; }
    public RelayCommand TransfererCommand { get; }
    public RelayCommand NonLuCommand { get; }
    public RelayCommand SupprimerCommand { get; }
    public RelayCommand RestaurerCommand { get; }
    public RelayCommand ModifierBrouillonCommand { get; }
    public RelayCommand EnvoyerCommand { get; }
    public RelayCommand EnregistrerBrouillonCommand { get; }
    public RelayCommand AbandonnerCommand { get; }
    public RelayCommand AjouterPieceCommand { get; }
    public RelayCommand RetirerPieceCommand { get; }
    public RelayCommand AjouterLienCommand { get; }
    public RelayCommand RetirerLienCommand { get; }
    public RelayCommand AjouterDiffusionCommand { get; }
    public RelayCommand EnregistrerPieceCommand { get; }
    public RelayCommand OuvrirLienCommand { get; }
    public RelayCommand OuvrirConversationCommand { get; }
    public RelayCommand RetourListeCommand { get; }

    // ===== Chargement =====

    private async Task DemarrerAsync()
    {
        if (SaphirSession.Agent is { } moi)
        {
            A.Exclus.Add(moi.Id);
            Cc.Exclus.Add(moi.Id);
        }
        await ChargerServicesAsync();
        await ToutChargerAsync();
        if (_argument is { } arg)
        {
            _argument = null;
            await TraiterArgumentAsync(arg);
        }
    }

    protected override string? IdApplication => SaphirApps.Messagerie;

    protected override void OnArgument(string argument) => _ = TraiterArgumentAsync(argument);

    /// <summary>« a:12 » : nouveau message à l'agent 12 ; « m:34 » : ouvre le message 34.</summary>
    private async Task TraiterArgumentAsync(string arg)
    {
        if (arg.StartsWith("a:", StringComparison.Ordinal) && long.TryParse(arg[2..], out var agentId))
        {
            var agents = await AgentsSaphir.ListeAsync();
            if (agents.FirstOrDefault(a => a.Id == agentId) is { } agent)
            {
                NouvelleRedaction();
                A.Ajouter(agent.Id, agent.Libelle, AgentsSaphir.Detail(agent));
                _redactionEnCours = true;
                Section = SectionNouveau;
            }
        }
        else if (arg.StartsWith("m:", StringComparison.Ordinal) && long.TryParse(arg[2..], out var messageId))
        {
            await OuvrirAsync(messageId);
        }
    }

    private async Task ChargerServicesAsync()
    {
        try
        {
            var agents = await AgentsSaphir.ListeAsync();
            ServicesDiffusion.Clear();
            foreach (var s in agents.Select(a => a.Service).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).Distinct().Order(StringComparer.Create(Fr, true)))
                ServicesDiffusion.Add(s);
        }
        catch (Exception)
        {
            // Liste de diffusion indisponible : choix individuel des destinataires.
        }
    }

    private async Task ToutChargerAsync()
    {
        await ChargerCompteursAsync();
        if (IsDossier)
            await ChargerListeAsync();
    }

    private async Task ChargerCompteursAsync()
    {
        var api = Api;
        if (api is null) return;
        try
        {
            var c = await api.GetAsync<MessagerieCompteursDto>("api/messagerie/compteurs");
            _menuReception.Compteur = c.NonLus > 0 ? c.NonLus.ToString(Fr) : null;
            _menuBrouillons.Compteur = c.Brouillons > 0 ? c.Brouillons.ToString(Fr) : null;
        }
        catch (ApiException)
        {
            // Compteurs facultatifs.
        }
    }

    private async Task ChargerListeAsync()
    {
        var api = Api;
        if (api is null || !IsDossier) return;
        var dossier = Section;
        await RunAsync(async () =>
        {
            var liste = await api.GetAsync<List<MessageResumeDto>>($"api/messagerie/messages?dossier={ApiClient.Q(dossier)}&q={ApiClient.Q(Recherche.Trim())}");
            if (dossier != Section) return;
            var id = Selection?.Id;
            _selection = null;
            Messages.Clear();
            foreach (var m in liste)
                Messages.Add(new MessageRow(m, dossier == MessagerieDossiers.Envoyes));
            if (id is { } i && Messages.FirstOrDefault(m => m.Id == i) is { } row)
            {
                _selection = row;
            }
            OnPropertyChanged(nameof(Selection));
            OnPropertyChanged(nameof(ListeVide));
        });
    }

    private async Task OuvrirAsync(long id)
    {
        var api = Api;
        if (api is null) return;
        await RunAsync(async () =>
        {
            var message = await api.GetAsync<MessageDto>($"api/messagerie/messages/{id}");
            Ouvert = message;
            var conversation = await api.GetAsync<List<MessageDto>>($"api/messagerie/conversations/{message.ConversationId}");
            Conversation.Clear();
            foreach (var m in conversation)
                Conversation.Add(new ConversationRow(m.Id, m.Expediteur, Date(m.EnvoyeLe ?? m.CreeLe),
                    m.Corps.Length > 90 ? m.Corps[..90].Replace('\n', ' ') + "…" : m.Corps.Replace('\n', ' '), m.Id == message.Id));
            OnPropertyChanged(nameof(HasConversation));
            if (Messages.FirstOrDefault(m => m.Id == id) is { NonLu: true } row)
            {
                var index = Messages.IndexOf(row);
                Messages[index] = new MessageRow(row.Dto with { Lu = true }, Section == MessagerieDossiers.Envoyes);
                _selection = Messages[index];
                OnPropertyChanged(nameof(Selection));
            }
            await ChargerCompteursAsync();
        });
    }

    // ===== Actions sur le message ouvert =====

    private void Repondre(bool tous)
    {
        if (Ouvert is not { } m) return;
        NouvelleRedaction();
        _reponseA = m.Id;
        if (m.ExpediteurId != SaphirSession.Agent?.Id)
            A.Ajouter(m.ExpediteurId, m.Expediteur);
        if (tous || m.ExpediteurId == SaphirSession.Agent?.Id)
            foreach (var d in m.Destinataires.Where(d => d.AgentId != SaphirSession.Agent?.Id))
                (d.Type == MessagerieTypes.Cc ? Cc : A).Ajouter(d.AgentId, d.Libelle);
        Sujet = m.Sujet.StartsWith("RE :", StringComparison.OrdinalIgnoreCase) ? m.Sujet : "RE : " + m.Sujet;
        Corps = $"\n\n----- Message de {m.Expediteur} du {Date(m.EnvoyeLe ?? m.CreeLe)} -----\n{m.Corps}";
        foreach (var l in m.Liens) LiensComposes.Add(l);
        AllerRedaction();
    }

    private void Transferer()
    {
        if (Ouvert is not { } m) return;
        NouvelleRedaction();
        Sujet = m.Sujet.StartsWith("TR :", StringComparison.OrdinalIgnoreCase) ? m.Sujet : "TR : " + m.Sujet;
        Corps = $"\n\n----- Message transféré — de {m.Expediteur} du {Date(m.EnvoyeLe ?? m.CreeLe)} -----\nObjet : {m.Sujet}\n\n{m.Corps}";
        foreach (var p in m.Pieces) PiecesComposees.Add(new PieceComposee(p.Nom, p.Taille, p.Id, null));
        foreach (var l in m.Liens) LiensComposes.Add(l);
        AllerRedaction();
    }

    private void ModifierBrouillon()
    {
        if (Ouvert is not { Brouillon: true } m) return;
        NouvelleRedaction();
        _brouillonId = m.Id;
        _brouillonVersion = m.Version;
        _reponseA = m.ReponseA;
        foreach (var d in m.Destinataires)
            (d.Type == MessagerieTypes.Cc ? Cc : A).Ajouter(d.AgentId, d.Libelle);
        Sujet = m.Sujet;
        Corps = m.Corps;
        Important = m.Important;
        ListeDiffusion = m.ListeDiffusion;
        foreach (var p in m.Pieces) PiecesComposees.Add(new PieceComposee(p.Nom, p.Taille, p.Id, null));
        foreach (var l in m.Liens) LiensComposes.Add(l);
        AllerRedaction();
    }

    private void AllerRedaction()
    {
        _redactionEnCours = true;
        OnPropertyChanged(nameof(TitreRedaction));
        Section = SectionNouveau;
    }

    private async Task MarquerNonLuAsync()
    {
        var api = Api;
        if (api is null || Ouvert is not { } m) return;
        await RunAsync(async () =>
        {
            await api.SendAsync(HttpMethod.Post, $"api/messagerie/messages/{m.Id}/non-lu");
            Ouvert = null;
            Selection = null;
            await ToutChargerAsync();
        });
    }

    private async Task SupprimerAsync()
    {
        var api = Api;
        if (api is null || Ouvert is not { } m) return;
        var message = m.Brouillon ? "Supprimer définitivement ce brouillon ?"
            : IsCorbeille ? "Effacer ce message de ta corbeille ? Il ne sera plus visible dans ta messagerie."
            : "Placer ce message dans la corbeille ?";
        if (!await ConfirmAsync("Suppression", message, "Supprimer", danger: m.Brouillon || IsCorbeille))
            return;
        await RunAsync(async () =>
        {
            await api.SendAsync(HttpMethod.Delete, $"api/messagerie/messages/{m.Id}");
            Ouvert = null;
            Selection = null;
            await ToutChargerAsync();
        }, "Message supprimé.");
    }

    private async Task RestaurerAsync()
    {
        var api = Api;
        if (api is null || Ouvert is not { } m) return;
        await RunAsync(async () =>
        {
            await api.SendAsync(HttpMethod.Post, $"api/messagerie/messages/{m.Id}/restaurer");
            Ouvert = null;
            Selection = null;
            await ToutChargerAsync();
        }, "Message replacé dans la boîte de réception.");
    }

    private void OuvrirLien(string reference)
    {
        if (LiensSaphir.Application(reference) is not { } app)
        {
            ErrorText = $"Référence « {reference} » : aucune application SAPHIR ne sait l'ouvrir.";
            return;
        }
        OuvrirApplication(app, reference);
    }

    // ===== Rédaction =====

    private void NouvelleRedaction()
    {
        _brouillonId = null;
        _brouillonVersion = 0;
        _reponseA = null;
        _redactionEnCours = false;
        A.Vider();
        Cc.Vider();
        Sujet = string.Empty;
        Corps = string.Empty;
        Important = false;
        ListeDiffusion = null;
        ServiceDiffusion = null;
        NouveauLien = string.Empty;
        PiecesComposees.Clear();
        LiensComposes.Clear();
        OnPropertyChanged(nameof(TitreRedaction));
    }

    private void AjouterLien()
    {
        var l = NouveauLien.Trim().ToUpperInvariant();
        if (!LiensSaphir.Valide(l))
        {
            ErrorText = "Référence SAPHIR invalide (exemples : PER-000012, PVE-2026-000004, INT-2026-000031).";
            return;
        }
        if (!LiensComposes.Contains(l)) LiensComposes.Add(l);
        NouveauLien = string.Empty;
    }

    private void AjouterDiffusion()
    {
        if (string.IsNullOrWhiteSpace(ServiceDiffusion)) return;
        var service = ServiceDiffusion;
        A.AjouterTous(A.Tous.Where(a => a.Service == service && a.Statut == AgentStatuts.Actif));
        ListeDiffusion = string.IsNullOrWhiteSpace(ListeDiffusion) ? service : ListeDiffusion.Contains(service, StringComparison.Ordinal) ? ListeDiffusion : ListeDiffusion + ", " + service;
    }

    private async Task AjouterPieceAsync()
    {
        if (Hote is null)
        {
            ErrorText = "L'ajout de fichiers n'est pas disponible sur ce poste.";
            return;
        }
        if (PiecesComposees.Count >= PiecesJointes.NombreMaxParMessage)
        {
            ErrorText = $"{PiecesJointes.NombreMaxParMessage} pièces jointes au plus.";
            return;
        }
        await RunAsync(async () =>
        {
            var fichier = await Hote.ChoisirFichierAsync("Joindre un fichier", PiecesJointes.Extensions);
            if (fichier is null) return;
            var piece = await LirePieceAsync(fichier);
            PiecesComposees.Add(new PieceComposee(piece.Nom, piece.Donnees.Length * 3L / 4, null, piece));
        });
    }

    private async Task EnregistrerAsync(bool envoyer)
    {
        var api = Api;
        if (api is null) return;
        if (envoyer && !A.HasSelection && !Cc.HasSelection)
        {
            ErrorText = "Choisis au moins un destinataire.";
            return;
        }
        var input = new MessageInput(Sujet, Corps, A.Ids, Cc.Ids, ListeDiffusion, LiensComposes.ToList(),
            PiecesComposees.Where(p => p.Nouvelle is not null).Select(p => p.Nouvelle!).ToList(),
            PiecesComposees.Where(p => p.Id is not null).Select(p => p.Id!.Value).ToList(),
            _reponseA, Important, envoyer, _brouillonVersion);
        if (envoyer && !await ConfirmAsync("Envoi", $"Envoyer ce message à {A.Selection.Count + Cc.Selection.Count} destinataire(s) ? Un message envoyé n'est plus modifiable.", "Envoyer"))
            return;
        await RunAsync(async () =>
        {
            var dto = _brouillonId is { } id
                ? await api.SendAsync<MessageDto>(HttpMethod.Put, $"api/messagerie/messages/{id}", input)
                : await api.PostAsync<MessageDto>("api/messagerie/messages", input);
            if (envoyer)
            {
                NouvelleRedaction();
                Section = MessagerieDossiers.Envoyes;
            }
            else
            {
                _brouillonId = dto.Id;
                _brouillonVersion = dto.Version;
                // Les nouvelles pièces sont désormais enregistrées dans le brouillon.
                PiecesComposees.Clear();
                foreach (var p in dto.Pieces) PiecesComposees.Add(new PieceComposee(p.Nom, p.Taille, p.Id, null));
                OnPropertyChanged(nameof(TitreRedaction));
            }
            await ChargerCompteursAsync();
        }, envoyer ? "Message envoyé." : "Brouillon enregistré.");
    }

    private async Task AbandonnerAsync()
    {
        if (!await ConfirmAsync("Abandon", _brouillonId is null
                ? "Abandonner ce message ? Le texte saisi sera perdu."
                : "Fermer la rédaction ? Le brouillon reste enregistré dans tes brouillons (dernière version enregistrée).", "Abandonner"))
            return;
        NouvelleRedaction();
        Section = MessagerieDossiers.Reception;
    }

    // ===== Temps réel =====

    protected override void OnRealtime(RealtimeMessage message)
    {
        if (message.Type == ComplementsRealtime.MessageRecu)
        {
            _ = ToutChargerAsync();
        }
        else if (message.Type == ComplementsRealtime.MessageLu && long.TryParse(message.Text, out var id))
        {
            if (Ouvert?.Id == id) _ = OuvrirAsync(id);
            else if (Section == MessagerieDossiers.Envoyes) _ = ChargerListeAsync();
        }
    }
}
