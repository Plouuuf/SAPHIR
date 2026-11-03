using System.Collections.ObjectModel;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Complements;

namespace Saphir.ViewModels.Apps.Complements;

/// <summary>Salon de la liste (non-lus, type).</summary>
public sealed class SalonRow(SalonDto dto) : ViewModelBase
{
    private int _nonLus = dto.NonLus;

    public SalonDto Dto { get; } = dto;
    public long Id => Dto.Id;
    public string Nom => Dto.Nom;
    public string Sous => Dto.Type == TchapTypesSalon.Service
        ? "Salon de service"
        : $"{Dto.NombreMembres} membre(s)" + (Dto.Archive ? " · archivé" : string.Empty);
    public string Initiale => Dto.Nom.Length > 0 ? Dto.Nom[..1].ToUpperInvariant() : "#";
    public bool Service => Dto.Type == TchapTypesSalon.Service;

    /// <summary>Nombre de messages non lus (remis à zéro à l'ouverture du salon).</summary>
    public int NombreNonLus
    {
        get => _nonLus;
        set
        {
            if (!SetProperty(ref _nonLus, value)) return;
            OnPropertyChanged(nameof(HasNonLus));
            OnPropertyChanged(nameof(NonLus));
        }
    }

    public bool HasNonLus => NombreNonLus > 0;
    public string NonLus => NombreNonLus > 99 ? "99+" : NombreNonLus.ToString(ComplementsViewModelBase.Fr);
}

/// <summary>Message d'un salon.</summary>
public sealed class TchapMessageRow(TchapMessageDto dto, bool moi, bool peutRetirer)
{
    public TchapMessageDto Dto { get; } = dto;
    public long Id => Dto.Id;
    public string Auteur => Dto.Auteur;
    public string Heure => ComplementsViewModelBase.DateCourte(Dto.Date);
    public string Texte => Dto.Texte;
    public bool Moi => moi;
    public bool Autre => !moi;
    public bool Supprime => Dto.Supprime;
    public bool PeutRetirer => peutRetirer && !Dto.Supprime;
    public Avalonia.Layout.HorizontalAlignment Alignement => moi ? Avalonia.Layout.HorizontalAlignment.Right : Avalonia.Layout.HorizontalAlignment.Left;
}

/// <summary>
/// TCHAP (version SAPHIR simplifiée, sans lien avec le Tchap réel) : salons de service et salons de groupe,
/// membres, messages temps réel, historique, non-lus.
/// </summary>
public sealed class TchapViewModel : ComplementsViewModelBase
{
    private SalonRow? _salon;
    private string _texte = string.Empty;
    private bool _nouveauSalon;
    private bool _membresVisibles;
    private bool _plusAnciens;
    private string? _argument;

    public TchapViewModel(string? argument = null)
    {
        _argument = argument ?? SaphirSession.TakeAppArgument(SaphirApps.Tchap);
        NouveauxMembres = new SelecteurAgentsViewModel("Membres du salon");
        AjoutMembres = new SelecteurAgentsViewModel("Ajouter des membres");
        if (SaphirSession.Agent is { } moi)
        {
            NouveauxMembres.Exclus.Add(moi.Id);
            AjoutMembres.Exclus.Add(moi.Id);
        }

        EnvoyerCommand = new RelayCommand(() => _ = EnvoyerAsync());
        PlusAnciensCommand = new RelayCommand(() => _ = ChargerPlusAnciensAsync());
        NouveauSalonCommand = new RelayCommand(() => { NouveauSalon = true; MembresVisibles = false; });
        AnnulerSalonCommand = new RelayCommand(() => NouveauSalon = false);
        CreerSalonCommand = new RelayCommand(() => _ = CreerSalonAsync());
        MembresCommand = new RelayCommand(() => _ = BasculerMembresAsync());
        AjouterMembresCommand = new RelayCommand(() => _ = ModifierMembresAsync(AjoutMembres.Ids, null));
        RetirerMembreCommand = new RelayCommand(p => { if (p is SalonMembreDto m) _ = ModifierMembresAsync(null, [m.AgentId]); });
        QuitterCommand = new RelayCommand(() => _ = QuitterAsync());
        ArchiverCommand = new RelayCommand(() => _ = ArchiverAsync());
        RenommerCommand = new RelayCommand(() => _ = RenommerAsync());
        RetirerMessageCommand = new RelayCommand(p => { if (p is TchapMessageRow m) _ = RetirerMessageAsync(m); });
        RetourCommand = new RelayCommand(() => Salon = null);
        ActualiserCommand = new RelayCommand(() => _ = ChargerSalonsAsync());

        _ = DemarrerAsync();
    }

    public override string TitreMenu => "TCHAP";

    protected override void OnCompactChanged() => RafraichirPanneaux();

    public bool CanSalonService => SaphirSession.Has(Permissions.AdminParametres);

    // ===== Salons =====

    public ObservableCollection<SalonRow> Salons { get; } = [];

    public SalonRow? Salon
    {
        get => _salon;
        set
        {
            if (!SetProperty(ref _salon, value)) return;
            Messages.Clear();
            Membres.Clear();
            MembresVisibles = false;
            NouveauSalon = false;
            Renommage = value?.Nom;
            foreach (var n in new[] { nameof(HasSalon), nameof(EstAdmin), nameof(EstGroupe), nameof(PeutEcrire), nameof(SalonDescription) })
                OnPropertyChanged(n);
            RafraichirPanneaux();
            if (value is not null) _ = ChargerMessagesAsync(value.Id);
        }
    }

    public bool HasSalon => Salon is not null;
    public bool EstAdmin => Salon?.Dto.EstAdmin == true;
    public bool EstGroupe => Salon?.Dto.Type == TchapTypesSalon.Groupe;
    public bool PeutEcrire => Salon is { Dto.Archive: false };
    public string SalonDescription => Salon?.Dto.Description ?? (Salon?.Service == true ? "Salon de service ouvert à tous les agents." : string.Empty);
    public bool AfficherSalons => IsLarge || (Salon is null && !NouveauSalon);
    public bool AucunSalonOuvert => !HasSalon && !NouveauSalon;
    public bool PeutGererMembres => EstAdmin && EstGroupe;
    public double LargeurSalons => IsCompact ? double.NaN : 260;
    public bool AfficherConversation => IsLarge || Salon is not null || NouveauSalon;

    private void RafraichirPanneaux()
    {
        OnPropertyChanged(nameof(AfficherSalons));
        OnPropertyChanged(nameof(AfficherConversation));
        OnPropertyChanged(nameof(AucunSalonOuvert));
        OnPropertyChanged(nameof(PeutGererMembres));
        OnPropertyChanged(nameof(LargeurSalons));
    }

    // ===== Messages =====

    public ObservableCollection<TchapMessageRow> Messages { get; } = [];
    public string Texte { get => _texte; set => SetProperty(ref _texte, value); }
    public bool PlusAnciens { get => _plusAnciens; set => SetProperty(ref _plusAnciens, value); }

    /// <summary>Demande à la vue de faire défiler la conversation jusqu'au dernier message.</summary>
    public event Action? DefilerEnBas;

    // ===== Création et membres =====

    public bool NouveauSalon
    {
        get => _nouveauSalon;
        set
        {
            if (!SetProperty(ref _nouveauSalon, value)) return;
            if (value)
            {
                NomSalon = string.Empty;
                DescriptionSalon = null;
                SalonService = false;
                NouveauxMembres.Vider();
            }
            RafraichirPanneaux();
        }
    }

    private string _nomSalon = string.Empty;
    private string? _descriptionSalon;
    private bool _salonService;
    private string? _renommage;
    public string NomSalon { get => _nomSalon; set => SetProperty(ref _nomSalon, value); }
    public string? DescriptionSalon { get => _descriptionSalon; set => SetProperty(ref _descriptionSalon, value); }
    public bool SalonService
    {
        get => _salonService;
        set
        {
            if (SetProperty(ref _salonService, value))
                OnPropertyChanged(nameof(MembresNouveauSalon));
        }
    }

    public bool MembresNouveauSalon => !SalonService;
    public string? Renommage { get => _renommage; set => SetProperty(ref _renommage, value); }
    public SelecteurAgentsViewModel NouveauxMembres { get; }
    public SelecteurAgentsViewModel AjoutMembres { get; }
    public ObservableCollection<SalonMembreDto> Membres { get; } = [];
    public bool MembresVisibles { get => _membresVisibles; set => SetProperty(ref _membresVisibles, value); }

    public RelayCommand EnvoyerCommand { get; }
    public RelayCommand PlusAnciensCommand { get; }
    public RelayCommand NouveauSalonCommand { get; }
    public RelayCommand AnnulerSalonCommand { get; }
    public RelayCommand CreerSalonCommand { get; }
    public RelayCommand MembresCommand { get; }
    public RelayCommand AjouterMembresCommand { get; }
    public RelayCommand RetirerMembreCommand { get; }
    public RelayCommand QuitterCommand { get; }
    public RelayCommand ArchiverCommand { get; }
    public RelayCommand RenommerCommand { get; }
    public RelayCommand RetirerMessageCommand { get; }
    public RelayCommand RetourCommand { get; }
    public RelayCommand ActualiserCommand { get; }

    // ===== Chargement =====

    private async Task DemarrerAsync()
    {
        await ChargerSalonsAsync();
        if (_argument is { } arg && arg.StartsWith("salon:", StringComparison.Ordinal) && long.TryParse(arg[6..], out var id))
            Salon = Salons.FirstOrDefault(s => s.Id == id);
        else if (IsLarge && Salon is null)
            Salon = Salons.FirstOrDefault();
        _argument = null;
    }

    private async Task ChargerSalonsAsync()
    {
        var api = Api;
        if (api is null) return;
        await RunAsync(async () =>
        {
            var salons = await api.GetAsync<List<SalonDto>>("api/tchap/salons");
            var courant = Salon?.Id;
            _salon = null;
            Salons.Clear();
            foreach (var s in salons) Salons.Add(new SalonRow(s));
            if (courant is { } id && Salons.FirstOrDefault(s => s.Id == id) is { } row)
                _salon = row;
            else if (courant is not null)
            {
                Messages.Clear();
                Membres.Clear();
            }
            OnPropertyChanged(nameof(Salon));
            foreach (var n in new[] { nameof(HasSalon), nameof(EstAdmin), nameof(EstGroupe), nameof(PeutEcrire), nameof(SalonDescription) })
                OnPropertyChanged(n);
            RafraichirPanneaux();
        });
    }

    private async Task ChargerMessagesAsync(long salonId)
    {
        var api = Api;
        if (api is null) return;
        await RunAsync(async () =>
        {
            var messages = await api.GetAsync<List<TchapMessageDto>>($"api/tchap/salons/{salonId}/messages?limit=100");
            if (Salon?.Id != salonId) return;
            Messages.Clear();
            foreach (var m in messages) Messages.Add(Ligne(m));
            PlusAnciens = messages.Count >= 100;
            DefilerEnBas?.Invoke();
            // Les non-lus du salon ouvert sont remis à zéro côté serveur.
            if (Salon is { } ouvert) ouvert.NombreNonLus = 0;
        });
    }

    private async Task ChargerPlusAnciensAsync()
    {
        var api = Api;
        if (api is null || Salon is not { } s || Messages.Count == 0) return;
        await RunAsync(async () =>
        {
            var anciens = await api.GetAsync<List<TchapMessageDto>>($"api/tchap/salons/{s.Id}/messages?avant={Messages[0].Id}&limit=100");
            for (var i = anciens.Count - 1; i >= 0; i--)
                Messages.Insert(0, Ligne(anciens[i]));
            PlusAnciens = anciens.Count >= 100;
        });
    }

    private TchapMessageRow Ligne(TchapMessageDto m)
    {
        var moi = m.AuteurId == SaphirSession.Agent?.Id;
        return new TchapMessageRow(m, moi, moi || EstAdmin);
    }

    // ===== Actions =====

    private async Task EnvoyerAsync()
    {
        var api = Api;
        if (api is null || Salon is not { } s || string.IsNullOrWhiteSpace(Texte)) return;
        var texte = Texte;
        await RunAsync(async () =>
        {
            var m = await api.PostAsync<TchapMessageDto>($"api/tchap/salons/{s.Id}/messages", new TchapMessageInput(texte));
            Texte = string.Empty;
            if (Messages.All(x => x.Id != m.Id)) Messages.Add(Ligne(m));
            DefilerEnBas?.Invoke();
        });
    }

    private async Task CreerSalonAsync()
    {
        var api = Api;
        if (api is null) return;
        await RunAsync(async () =>
        {
            var salon = await api.PostAsync<SalonDto>("api/tchap/salons",
                new SalonInput(NomSalon, DescriptionSalon, SalonService ? TchapTypesSalon.Service : TchapTypesSalon.Groupe, NouveauxMembres.Ids));
            NouveauSalon = false;
            await ChargerSalonsAsync();
            Salon = Salons.FirstOrDefault(x => x.Id == salon.Id);
        }, "Salon créé.");
    }

    private async Task BasculerMembresAsync()
    {
        if (MembresVisibles)
        {
            MembresVisibles = false;
            return;
        }
        await ChargerMembresAsync();
        MembresVisibles = true;
    }

    private async Task ChargerMembresAsync()
    {
        var api = Api;
        if (api is null || Salon is not { } s) return;
        await RunAsync(async () =>
        {
            var membres = await api.GetAsync<List<SalonMembreDto>>($"api/tchap/salons/{s.Id}/membres");
            Membres.Clear();
            foreach (var m in membres) Membres.Add(m);
            AjoutMembres.Exclus.Clear();
            foreach (var m in membres) AjoutMembres.Exclus.Add(m.AgentId);
        });
    }

    private async Task ModifierMembresAsync(IReadOnlyList<long>? ajouter, IReadOnlyList<long>? retirer)
    {
        var api = Api;
        if (api is null || Salon is not { } s) return;
        if (retirer is { Count: > 0 } && !await ConfirmAsync("Membres", "Retirer ce membre du salon ? Il n'aura plus accès aux messages.", "Retirer"))
            return;
        await RunAsync(async () =>
        {
            var membres = await api.SendAsync<List<SalonMembreDto>>(HttpMethod.Put, $"api/tchap/salons/{s.Id}/membres",
                new SalonMembresInput(ajouter ?? Array.Empty<long>(), retirer ?? Array.Empty<long>()));
            Membres.Clear();
            foreach (var m in membres) Membres.Add(m);
            AjoutMembres.Vider();
            AjoutMembres.Exclus.Clear();
            foreach (var m in membres) AjoutMembres.Exclus.Add(m.AgentId);
        }, "Membres mis à jour.");
    }

    private async Task QuitterAsync()
    {
        var api = Api;
        if (api is null || Salon is not { } s) return;
        if (!await ConfirmAsync("Quitter le salon", $"Quitter « {s.Nom} » ? Tu n'auras plus accès à ses messages.", "Quitter", danger: true))
            return;
        await RunAsync(async () =>
        {
            await api.SendAsync(HttpMethod.Post, $"api/tchap/salons/{s.Id}/quitter");
            Salon = null;
            await ChargerSalonsAsync();
        }, "Tu as quitté le salon.");
    }

    private async Task ArchiverAsync()
    {
        var api = Api;
        if (api is null || Salon is not { } s) return;
        if (!await ConfirmAsync("Archiver le salon", $"Archiver « {s.Nom} » ? Il passera en lecture seule et disparaîtra de la liste.", "Archiver", danger: true))
            return;
        await RunAsync(async () =>
        {
            await api.SendAsync(HttpMethod.Post, $"api/tchap/salons/{s.Id}/archiver");
            Salon = null;
            await ChargerSalonsAsync();
        }, "Salon archivé.");
    }

    private async Task RenommerAsync()
    {
        var api = Api;
        if (api is null || Salon is not { } s || string.IsNullOrWhiteSpace(Renommage)) return;
        await RunAsync(async () =>
        {
            await api.SendAsync<SalonDto>(HttpMethod.Put, $"api/tchap/salons/{s.Id}", new SalonInput(Renommage!, s.Dto.Description, s.Dto.Type, null));
            await ChargerSalonsAsync();
        }, "Salon renommé.");
    }

    private async Task RetirerMessageAsync(TchapMessageRow m)
    {
        var api = Api;
        if (api is null) return;
        if (!await ConfirmAsync("Retirer le message", "Retirer ce message ? Il sera remplacé par « Message retiré. » (le retrait est historisé).", "Retirer"))
            return;
        await RunAsync(async () =>
        {
            await api.SendAsync(HttpMethod.Delete, $"api/tchap/messages/{m.Id}");
            if (Salon is { } s) await ChargerMessagesAsync(s.Id);
        });
    }

    protected override string? IdApplication => SaphirApps.Tchap;

    protected override void OnArgument(string argument)
    {
        if (argument.StartsWith("salon:", StringComparison.Ordinal) && long.TryParse(argument[6..], out var id)
            && Salons.FirstOrDefault(s => s.Id == id) is { } salon)
            Salon = salon;
    }

    protected override void OnRealtime(RealtimeMessage message)
    {
        if (message.Type == ComplementsRealtime.TchapSalons)
        {
            _ = ChargerSalonsAsync();
        }
        else if (message.Type == ComplementsRealtime.TchapMessage && long.TryParse(message.Text, out var salonId))
        {
            if (Salon?.Id == salonId) _ = ChargerMessagesAsync(salonId);
            else _ = ChargerSalonsAsync();
        }
    }
}
