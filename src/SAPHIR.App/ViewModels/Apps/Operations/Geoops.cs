using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Operations;

namespace Saphir.ViewModels.Apps.Operations;

/// <summary>Point de la carte en coordonnées du jeu (clic de l'opérateur).</summary>
public sealed record PositionCarte(double X, double Y);

/// <summary>Marqueur dessiné sur la carte GÉOOPS.</summary>
public sealed class MarqueurCarte
{
    public required string Code { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public required string Libelle { get; init; }
    /// <summary>Couleur « #RRGGBB ».</summary>
    public required string Couleur { get; init; }
    /// <summary>« equipage » (rond), « intervention » (losange), « agent » (point gris).</summary>
    public required string Forme { get; init; }
    public bool Selectionne { get; init; }
    /// <summary>Position déclarée (contour pointillé) plutôt que simulée par FiveM.</summary>
    public bool Declaree { get; init; }
}

/// <summary>Équipage listé à côté de la carte.</summary>
public sealed class GeoopsEquipageLigne(GeoopsEquipageDto dto)
{
    public GeoopsEquipageDto Dto { get; } = dto;
    public string Indicatif => Dto.Indicatif;
    public string Statut => Dto.Statut;
    public string Intervention => Dto.InterventionNumero ?? string.Empty;
    public string Source => Dto.Source == "Déclarée" ? $"Position déclarée ({Fmt.Heure(Dto.Le)})" : $"Simulation FiveM ({Fmt.Heure(Dto.Le)})";
    public string Couleur => GeoopsSection.CouleurEquipage(Dto.Statut);
    public IBrush Pinceau => SolidColorBrush.Parse(Couleur);
}

/// <summary>GÉOOPS (8.1) : carte opérationnelle, équipages et interventions actives, positions simulées ou déclarées.</summary>
public sealed class GeoopsSection : SectionViewModel
{
    private static (string Version, Bitmap Image)? _carteEnCache;

    private Bitmap? _carte;
    private GeoopsCalibrationDto _calibration = new(-4140, 4860, -4300, 8700, false, 60);
    private IReadOnlyList<MarqueurCarte> _marqueurs = Array.Empty<MarqueurCarte>();
    private GeoopsSituationDto? _situation;
    private string? _codeSelectionne;
    private string _recherche = string.Empty;
    private string? _mode;
    private PositionCarte? _centre;
    private long? _aSelectionner;

    public GeoopsSection(PegaseViewModel host) : base(host, "GÉOOPS")
    {
        App = host;
        ActualiserCommand = new RelayCommand(() => _ = Host.Executer(ChargerAsync));
        RechargerCarteCommand = new RelayCommand(() => { _carteEnCache = null; Carte = null; _ = Host.Executer(ChargerAsync); });
        RechercherCommand = new RelayCommand(Rechercher);
        OuvrirFicheCommand = new RelayCommand(() =>
        {
            if (InterventionSelectionnee is { } i) App.OuvrirIntervention(i.Id);
            else if (EquipageSelectionne?.Dto.InterventionId is { } id) App.OuvrirIntervention(id);
        });
        PlacerInterventionCommand = new RelayCommand(() => Mode = "intervention");
        DeclarerPositionCommand = new RelayCommand(() => Mode = "equipage");
        AnnulerModeCommand = new RelayCommand(() => Mode = null);
        ClicCarteCommand = new RelayCommand(p => { if (p is PositionCarte position) _ = ClicAsync(position); });
        SelectionCarteCommand = new RelayCommand(p => { if (p is MarqueurCarte m) Selectionner(m.Code, false); });
        ChoisirInterventionCommand = new RelayCommand(p => { if (p is GeoopsInterventionDto i) Selectionner("intervention:" + i.Id, true); });
        ChoisirEquipageCommand = new RelayCommand(p => { if (p is GeoopsEquipageLigne e) Selectionner("equipage:" + e.Dto.EquipageId, true); });
    }

    public PegaseViewModel App { get; }
    public bool PeutPlacer => PegaseViewModel.PeutQualifier;
    public bool PeutDeclarer => PegaseViewModel.PeutAffecter;

    public Bitmap? Carte { get => _carte; private set { if (SetProperty(ref _carte, value)) OnPropertyChanged(nameof(Avertissement)); } }
    public GeoopsCalibrationDto Calibration { get => _calibration; private set => SetProperty(ref _calibration, value); }
    public IReadOnlyList<MarqueurCarte> Marqueurs { get => _marqueurs; private set => SetProperty(ref _marqueurs, value); }
    public PositionCarte? Centre { get => _centre; private set => SetProperty(ref _centre, value); }

    public ObservableCollection<GeoopsEquipageLigne> Equipages { get; } = [];
    public ObservableCollection<GeoopsInterventionDto> Interventions { get; } = [];
    public ObservableCollection<GeoopsInterventionDto> NonPlacees { get; } = [];
    /// <summary>Tous les équipages actifs (déclaration de position, même sans position connue).</summary>
    public ObservableCollection<EquipageDto> TousEquipages { get; } = [];

    private EquipageDto? _equipageADeclarer;
    public EquipageDto? EquipageADeclarer
    {
        get => _equipageADeclarer;
        set
        {
            if (SetProperty(ref _equipageADeclarer, value))
                OnPropertyChanged(nameof(TexteMode));
        }
    }

    public string Recherche { get => _recherche; set => SetProperty(ref _recherche, value); }

    public string? Mode
    {
        get => _mode;
        private set
        {
            if (SetProperty(ref _mode, value))
                OnPropertyChanged(nameof(TexteMode));
        }
    }

    public string TexteMode => Mode switch
    {
        "intervention" => InterventionSelectionnee is { } i ? $"Clique sur la carte pour placer {i.Numero}." : "Choisis d'abord une intervention (liste « Non placées » ou carte).",
        "equipage" => EquipageADeclarer is { } e ? $"Clique sur la carte pour déclarer la position de {e.Indicatif}." : "Choisis d'abord l'équipage dans la liste « Déclarer ».",
        _ => "Molette : zoom — glisser : déplacer — clic sur un repère : sélection."
    };

    public string Avertissement => (Carte is null ? "Fond de carte absent (data/geoops/carte.jpg sur le serveur) : grille de repérage en coordonnées du jeu. " : string.Empty) +
                                   "Positions simulées (jeu) ou déclarées : ce ne sont pas des positions GPS mesurées.";

    public string Resume => _situation is { } s
        ? $"{s.Equipages.Count} équipage(s) positionné(s), {s.Interventions.Count} intervention(s) placée(s), {s.NonPlacees.Count} non placée(s), {s.AutresAgents.Count} autre(s) agent(s) en service — {Fmt.Heure(s.Le)}"
        : string.Empty;

    public GeoopsInterventionDto? InterventionSelectionnee => _codeSelectionne is { } c && c.StartsWith("intervention:", StringComparison.Ordinal)
        ? _situation?.Interventions.Concat(_situation.NonPlacees).FirstOrDefault(i => "intervention:" + i.Id == c)
        : null;

    public GeoopsEquipageLigne? EquipageSelectionne => _codeSelectionne is { } c && c.StartsWith("equipage:", StringComparison.Ordinal)
        ? Equipages.FirstOrDefault(e => "equipage:" + e.Dto.EquipageId == c)
        : null;

    public string InfoSelection => InterventionSelectionnee is { } i
        ? $"{i.Numero} — {i.Nature}\n{PegaseListes.LibellePriorite(i.Priorite)} — {i.Statut}\n{i.Adresse ?? "Adresse non précisée"}"
        : EquipageSelectionne is { } e
            ? $"{e.Indicatif} — {e.Statut}\n{(e.Dto.InterventionNumero is { } n ? "Engagé sur " + n : "Aucune intervention")}\n{e.Source}" +
              (e.Dto.Agent is null ? string.Empty : "\nPosition de " + e.Dto.Agent) + (e.Dto.Vehicule is null ? string.Empty : "\nVéhicule : " + e.Dto.Vehicule) +
              (e.Dto.Vitesse is { } v ? $"\nVitesse : {v * 3.6:0} km/h" : string.Empty)
            : "Sélectionne un équipage ou une intervention.";

    public bool ASelection => _codeSelectionne is not null;

    public RelayCommand ActualiserCommand { get; }
    public RelayCommand RechargerCarteCommand { get; }
    public RelayCommand RechercherCommand { get; }
    public RelayCommand OuvrirFicheCommand { get; }
    public RelayCommand PlacerInterventionCommand { get; }
    public RelayCommand DeclarerPositionCommand { get; }
    public RelayCommand AnnulerModeCommand { get; }
    public RelayCommand ClicCarteCommand { get; }
    public RelayCommand SelectionCarteCommand { get; }
    public RelayCommand ChoisirInterventionCommand { get; }
    public RelayCommand ChoisirEquipageCommand { get; }

    /// <summary>Ouverture depuis une fiche : l'intervention sera sélectionnée et centrée.</summary>
    public void PreparerSelection(long interventionId) => _aSelectionner = interventionId;

    public override async Task ChargerAsync()
    {
        var situation = await Client.GetAsync<GeoopsSituationDto>("api/geoops/situation");
        Calibration = situation.Calibration;
        _situation = situation;
        await ChargerCarteAsync(situation.Calibration);

        Equipages.Clear();
        foreach (var e in situation.Equipages.OrderBy(e => e.Indicatif))
            Equipages.Add(new GeoopsEquipageLigne(e));
        Interventions.Clear();
        foreach (var i in situation.Interventions)
            Interventions.Add(i);
        NonPlacees.Clear();
        foreach (var i in situation.NonPlacees)
            NonPlacees.Add(i);

        if (PeutDeclarer)
        {
            var equipages = await Client.GetAsync<List<EquipageDto>>("api/main-courante/equipages?actifs=true");
            var choisi = EquipageADeclarer?.Id ?? EquipageSelectionne?.Dto.EquipageId;
            TousEquipages.Clear();
            foreach (var e in equipages)
                TousEquipages.Add(e);
            EquipageADeclarer = choisi is { } eid ? TousEquipages.FirstOrDefault(e => e.Id == eid) : null;
        }

        if (_aSelectionner is { } id)
        {
            _aSelectionner = null;
            Selectionner("intervention:" + id, true);
        }
        else
        {
            ConstruireMarqueurs();
        }
        OnPropertyChanged(nameof(Resume));
    }

    private async Task ChargerCarteAsync(GeoopsCalibrationDto calibration)
    {
        if (!calibration.CarteDisponible)
        {
            Carte = null;
            return;
        }
        if (_carteEnCache is { } cache)
        {
            Carte = cache.Image;
            return;
        }
        try
        {
            var donnees = await Client.GetAsync<GeoopsCarteDto>("api/geoops/carte/donnees");
            using var flux = new MemoryStream(Convert.FromBase64String(donnees.Donnees));
            var image = new Bitmap(flux);
            _carteEnCache = (donnees.Version, image);
            Carte = image;
        }
        catch (ApiException ex) when (ex.Status == System.Net.HttpStatusCode.NotFound)
        {
            Carte = null;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
        {
            Carte = null;
            Host.Erreur("Fond de carte GÉOOPS illisible : " + ex.Message);
        }
    }

    public static string CouleurEquipage(string statut) => statut switch
    {
        EquipageStatuts.Disponible or EquipageStatuts.MissionTerminee => "#2E8B3E",
        EquipageStatuts.Affecte or EquipageStatuts.EnRoute => "#E08A1E",
        EquipageStatuts.SurPlace or EquipageStatuts.Transport or EquipageStatuts.AuService => "#C7503B",
        _ => "#7A7A7A"
    };

    private static string CouleurIntervention(string priorite) => priorite switch
    {
        "P1" => "#B3261E",
        "P2" => "#D9822B",
        _ => "#2F6FBF"
    };

    private void ConstruireMarqueurs()
    {
        if (_situation is not { } s)
            return;
        var marqueurs = new List<MarqueurCarte>();
        foreach (var a in s.AutresAgents)
            marqueurs.Add(new MarqueurCarte { Code = "agent", X = a.X, Y = a.Y, Libelle = a.Nom, Couleur = "#8A8F98", Forme = "agent" });
        foreach (var i in s.Interventions)
        {
            var code = "intervention:" + i.Id;
            marqueurs.Add(new MarqueurCarte
            {
                Code = code, X = i.X, Y = i.Y, Libelle = $"{i.Numero} {i.Nature}", Couleur = CouleurIntervention(i.Priorite), Forme = "intervention",
                Selectionne = code == _codeSelectionne
            });
        }
        foreach (var e in s.Equipages)
        {
            var code = "equipage:" + e.EquipageId;
            marqueurs.Add(new MarqueurCarte
            {
                Code = code, X = e.X, Y = e.Y, Libelle = e.Indicatif, Couleur = CouleurEquipage(e.Statut), Forme = "equipage",
                Selectionne = code == _codeSelectionne, Declaree = e.Source == "Déclarée"
            });
        }
        Marqueurs = marqueurs;
    }

    private void Selectionner(string code, bool centrer)
    {
        _codeSelectionne = code;
        ConstruireMarqueurs();
        foreach (var nom in new[] { nameof(InterventionSelectionnee), nameof(EquipageSelectionne), nameof(InfoSelection), nameof(ASelection), nameof(TexteMode) })
            OnPropertyChanged(nom);
        if (code.StartsWith("equipage:", StringComparison.Ordinal) && long.TryParse(code["equipage:".Length..], out var equipageId) &&
            TousEquipages.FirstOrDefault(e => e.Id == equipageId) is { } aDeclarer)
            EquipageADeclarer = aDeclarer;
        if (centrer && Marqueurs.FirstOrDefault(m => m.Code == code) is { } m)
            Centre = new PositionCarte(m.X, m.Y);
    }

    private void Rechercher()
    {
        if (Fmt.Vide(Recherche) is not { } q || _situation is not { } s)
            return;
        var intervention = s.Interventions.Concat(s.NonPlacees).FirstOrDefault(i =>
            i.Numero.Contains(q, StringComparison.OrdinalIgnoreCase) || i.Nature.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            (i.Adresse?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        if (intervention is not null)
        {
            Selectionner("intervention:" + intervention.Id, true);
            return;
        }
        var equipage = s.Equipages.FirstOrDefault(e => e.Indicatif.Contains(q, StringComparison.OrdinalIgnoreCase));
        if (equipage is not null)
            Selectionner("equipage:" + equipage.EquipageId, true);
        else
            Host.Information("Aucune intervention ni aucun équipage ne correspond à « " + q + " ».");
    }

    private async Task ClicAsync(PositionCarte position)
    {
        switch (Mode)
        {
            case "intervention" when InterventionSelectionnee is { } i:
                await Host.Executer(async () =>
                {
                    await Client.SendAsync<InterventionDto>(HttpMethod.Put, $"api/pegase/interventions/{i.Id}/position", new PositionInput(position.X, position.Y));
                    Mode = null;
                    await ChargerAsync();
                }, $"{i.Numero} placée sur la carte (position déclarée).");
                break;
            case "equipage" when EquipageADeclarer is { } e:
                await Host.Executer(async () =>
                {
                    await Client.SendAsync(HttpMethod.Post, $"api/geoops/equipages/{e.Id}/position", new PositionInput(position.X, position.Y));
                    Mode = null;
                    await ChargerAsync();
                }, $"Position déclarée de {e.Indicatif} enregistrée.");
                break;
        }
    }

    public override bool Concerne(RealtimeMessage message)
        => Est(message, OperationsTypes.GeoopsPositions) || Est(message, OperationsTypes.PegaseChanged);
}
