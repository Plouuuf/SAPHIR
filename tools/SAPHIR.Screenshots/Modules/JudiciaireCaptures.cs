using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Judiciaire;
using Saphir.ViewModels.Apps.Judiciaire;
using Saphir.Views.Apps.Judiciaire;

namespace Saphir.Screenshots;

/// <summary>Captures et données fictives du module Judiciaire (CHEOPS, LRPPN) — Créteil, Val-de-Marne.</summary>
public static class JudiciaireCaptures
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 41, 0, TimeSpan.FromHours(1));
    private static readonly IReadOnlyList<string> Aucune = Array.Empty<string>();

    private static readonly PersonneResumeDto Leroy = new(1, "PER-000001", "LEROY", "Kévin", new DateOnly(1998, 3, 14), "Créteil (94)", "M", ["FPR", "TAJ", "FNAEG"]);
    private static readonly PersonneResumeDto Benali = new(2, "PER-000002", "BENALI", "Sofiane", new DateOnly(1991, 7, 2), "Vitry-sur-Seine (94)", "M", ["TAJ"]);
    private static readonly PersonneResumeDto Moreau = new(3, "PER-000003", "MOREAU", "Julie", new DateOnly(1985, 11, 23), "Paris 12e (75)", "F", ["TAJ"]);
    private static readonly PersonneResumeDto Roussel = new(5, "PER-000005", "ROUSSEL", "Anthony", new DateOnly(1979, 1, 30), "Maisons-Alfort (94)", "M", ["SNPC"]);

    private static HistoriqueDto H(int id, int minutes, string objet, string reference, string action, string? champ, string? avant, string? apres, string? motif = null)
        => new(id, Now.AddMinutes(-minutes), "DURAND Jean-Pierre (AG-000001)", objet, reference, action, champ, avant, apres, motif);

    private static readonly AlerteBandeauDto BandeauLeroy = new("FPR", "FPR-000001",
        "PERSONNE INSCRITE AU FPR — PJ — Recherche de police judiciaire — Recherché pour vol avec violence",
        "Interpeller. Aviser immédiatement l'OPJ de permanence du CSP de Créteil.");

    private static PersonneFicheDto FicheLeroy() => new(1, "PER-000001", "LEROY", "Kévin", new DateOnly(1998, 3, 14), "Créteil (94)", "M", "Française",
        "12 rue des Écoles, 94000 Créteil", "06 12 48 77 31", "1,80 m, corpulence mince, cheveux bruns courts, tatouage avant-bras droit", "SAPHIR",
        [new AliasDto(1, "KEV")], [new DocumentIdentiteDto(1, "Carte nationale d'identité", "X4RT7ZK21", new DateOnly(2021, 6, 2), new DateOnly(2031, 6, 1), "Préfecture du Val-de-Marne")],
        ["FPR", "TAJ", "FNAEG"], [BandeauLeroy], Now.AddDays(-30), Now.AddDays(-2));

    private static List<TajFaitDto> TajLeroy() =>
    [
        new(1, "TAJ-000001", 1, "Mis en cause", "Vol avec violence (vol à l'arraché)", "7151", new DateTimeOffset(2025, 10, 4, 19, 30, 0, TimeSpan.FromHours(2)),
            "Rue du Général-Leclerc, Créteil", "CSP de Créteil — BSU", "2025/000412", null, "Transmis au parquet de Créteil", "Saisie initiale", Now.AddDays(-30), "SAPHIR"),
        new(2, "TAJ-000002", 1, "Mis en cause", "Conduite d'un véhicule sans assurance", "20339", new DateTimeOffset(2024, 6, 11, 22, 10, 0, TimeSpan.FromHours(2)),
            "Avenue Pierre-Brossolette, Créteil", "CSP de Créteil — BAC", "2024/001877", null, null, "Saisie initiale", Now.AddDays(-30), "SAPHIR")
    ];

    private static FprFicheDto FprLeroy(bool historique) => new(1, "FPR-000001", Leroy, "PJ — Recherche de police judiciaire", "Recherché pour vol avec violence",
        "Interpeller. Aviser immédiatement l'OPJ de permanence du CSP de Créteil.", "2025/000412", "CSP de Créteil — BSU", Now.AddDays(-28),
        new DateOnly(2027, 4, 30), FprStatuts.Active, "Peut se montrer véhément lors des contrôles.", false, false, "GIRARD Valentin (AG-000002)",
        historique ? [H(1, 40320, "fpr", "FPR-000001", "Inscription", "statut", null, "Active", "Recherché pour vol avec violence")] : Array.Empty<HistoriqueDto>());

    private static readonly FprFicheDto FprFontaine = new(2, "FPR-000002", new PersonneResumeDto(6, "PER-000006", "FONTAINE", "Léa", new DateOnly(2009, 8, 17), "Créteil (94)", "F", Aucune),
        "M — Mineur fugueur", "Fugue du domicile familial depuis le 30/10/2026", "Conduire au service. Aviser les représentants légaux et le CSP de Créteil.",
        "2026/000514", "CSP de Créteil — BPF", Now.AddDays(-3), new DateOnly(2027, 1, 31), FprStatuts.Active, null, false, false, "BORDINI Nolan (AG-000004)", Array.Empty<HistoriqueDto>());

    private static readonly VehiculeResumeDto Golf = new(3, "DX-905-QA", "Volkswagen", "Golf VII", "Blanc", 5, "ROUSSEL Anthony", ["FOVeS", "SIV"]);
    private static readonly VehiculeResumeDto Clio = new(1, "GH-482-KL", "Renault", "Clio V", "Gris", 3, "MOREAU Julie", Aucune);
    private static readonly VehiculeResumeDto Peugeot = new(2, "FT-118-ZB", "Peugeot", "308", "Noir", 2, "BENALI Sofiane", ["FVA"]);

    private static FovesFicheDto FovesGolf() => new(1, "FOV-000001", "Véhicule", "DX-905-QA", 3, "Volkswagen Golf VII blanche, jantes noires", "Vol",
        Now.AddDays(-5), "CSP de Maisons-Alfort", "2026/000497", FovesStatuts.Actif, null, null,
        "Ne pas intervenir seul. Contrôler les occupants et aviser le CIC.", "NGUYEN Thomas (AG-000006)", Array.Empty<HistoriqueDto>());

    private static readonly ProcedureLigneDto ProcLigne = new(12, "2026/000531", "Vol avec violence — Créteil, rue du Général-Leclerc", "Atteintes aux biens",
        "Flagrant délit", ProcedureStatuts.EnCours, "CSP de Créteil — BSU", "GIRARD Valentin", Now.AddHours(-3), 3, 1, "PG-2026-001284");

    private static ActeLigneDto LigneActe(long id, int n, string type, string statut, string? personne, bool valide) => new(id, 12, "2026/000531",
        $"2026/000531-PV00{n}", type, ActesCatalogue.Libelle(type), ActesCatalogue.Libelle(type), statut, "DURAND Jean-Pierre", personne,
        Now.AddMinutes(-20 * n), valide ? Now.AddMinutes(-15 * n) : null, valide ? "GIRARD Valentin, Commandant, RIO 2045871" : null);

    private static PieceDto Piece(long id, int n, string categorie, string libelle, string? scelle, string? fichier, long taille) => new(id, 12, "2026/000531",
        $"2026/000531-P00{n}", categorie, libelle, null, scelle, "Au service", fichier, fichier is null ? null : "image/jpeg", taille, null, null,
        "Salle des scellés — CSP Créteil", Now.AddHours(-2), "DURAND Jean-Pierre (AG-000001)",
        [new MouvementPieceDto(n, Now.AddHours(-2), "Enregistrement", "Salle des scellés — CSP Créteil", null, "DURAND Jean-Pierre (AG-000001)")]);

    private static ProcedureDto Procedure() => new(12, "2026/000531", ProcedureStatuts.EnCours, "CSP de Créteil — BSU", "Groupe d'appui judiciaire", 2,
        "GIRARD Valentin", "2045871", "Commandant", "OPJ", Now.AddHours(-3), "Flagrant délit", "Atteintes aux biens",
        "Vol avec violence — Créteil, rue du Général-Leclerc", 1284, "PG-2026-001284", "MC-2026-004411", "Vol avec violence (vol à l'arraché)",
        Now.AddHours(-4), "Créteil", "Rue du Général-Leclerc", "Requérante victime d'un vol à l'arraché de son téléphone. Individu interpellé par l'équipage TV 94-12 après une courte poursuite à pied.",
        "Intervention PÉGASE", 4, Now.AddHours(-3), "DURAND Jean-Pierre (AG-000001)", Now.AddMinutes(-10),
        [
            new ProcedurePersonneDto(1, Leroy, "Mis en cause", "Interpellé à 19 h 52", Now.AddHours(-3)),
            new ProcedurePersonneDto(2, Moreau, "Victime", null, Now.AddHours(-3))
        ],
        [new ProcedureVehiculeDto(1, Clio, "Véhicule victime", null)],
        [
            LigneActe(31, 1, ActesCatalogue.Pvi, ActeStatuts.Valide, "LEROY Kévin", true),
            LigneActe(32, 2, ActesCatalogue.Plainte, ActeStatuts.EnRelecture, "MOREAU Julie", false),
            LigneActe(33, 3, ActesCatalogue.Saisie, ActeStatuts.Brouillon, "LEROY Kévin", false)
        ],
        [
            Piece(41, 1, "Scellé", "Téléphone portable noir, coque bleue", "SC-2026-0531-01", null, 0),
            Piece(42, 2, "Photographie", "Photographies des lieux", null, "lieux.jpg", 845_000)
        ],
        [new ProcedureLienDto(1, 9, "2025/000412", "Vol avec violence — rue du Général-Leclerc (2025)")],
        [new ValidationDto(31, "2026/000531-PV001", "Procès-verbal d'interpellation", Now.AddMinutes(-15), "GIRARD Valentin, Commandant, RIO 2045871",
            "9f2c41d07a6be35e0c8d77a1b29f4c56e0a3d1f88b7c2e94a5d60f13c7b2e8a4")]);

    private static ActeDto Pvi() => new(33, 12, "2026/000531", "2026/000531-PV001", ActesCatalogue.Pvi, "Procès-verbal d'interpellation", ActeStatuts.Brouillon,
        1, "LEROY Kévin (PER-000001)", null, null, 1, "DURAND Jean-Pierre", "1234567", "Brigadier-chef", "CSP de Créteil",
        new Dictionary<string, string?>
        {
            ["id_nom"] = "LEROY", ["id_prenoms"] = "Kévin", ["id_naissance"] = "14/03/1998 à Créteil (94)", ["id_nationalite"] = "Française",
            ["id_adresse"] = "12 rue des Écoles, 94000 Créteil", ["id_alias"] = "KEV", ["id_document"] = "Carte nationale d'identité n° X4RT7ZK21",
            ["id_verification"] = "Vérifiée", ["id_moyen"] = "Présentation de la CNI", ["b_equipage"] = "TV 94-12", ["b_vehicule"] = "Peugeot 5008 sérigraphiée",
            ["b_intervention"] = "PG-2026-001284", ["c_date"] = "02/11/2026", ["c_heure"] = "19:45", ["c_commune"] = "Créteil",
            ["c_adresse"] = "Rue du Général-Leclerc", ["c_motif"] = "Vol avec violence (vol à l'arraché)", ["c_cadre"] = "Flagrant délit",
            ["c_faits_constates"] = "Individu correspondant au signalement diffusé par le CIC, en possession d'un téléphone correspondant à la description.",
            ["d_chronologie"] = "19 h 45 : prise en compte de l'appel par le CIC.\n19 h 49 : arrivée sur les lieux.\n19 h 52 : interpellation après une courte poursuite à pied.",
            ["d_heure_interpellation"] = "19:52", ["d_assistance"] = "Non nécessaire", ["e_effectue"] = "Oui", ["e_service_destinataire"] = "CSP de Créteil",
            ["f_situation"] = "Conduit au service et présenté à l'OPJ", ["f_service_saisi"] = "CSP de Créteil — BSU", ["f_responsable"] = "GIRARD Valentin"
        },
        [
            new ActeAgentDto(1, "Rédacteur", "DURAND Jean-Pierre", "1234567", "Brigadier-chef", "CSP de Créteil"),
            new ActeAgentDto(3, "Interpellateur", "TABOULOT Louis", "3301458", "Major", "CSP de Créteil")
        ],
        Array.Empty<QuestionReponseDto>(), [41], 3, Now.AddHours(-2), Now.AddMinutes(-5), null, null, null, null, Array.Empty<MentionDto>(), 1284, "PG-2026-001284");

    /// <summary>Réponse fictive à une requête du poste (null = route inconnue de ce module).</summary>
    public static object? Sample(string method, string path, string query, string? body)
    {
        if (method != "GET")
            return null;
        return path switch
        {
            "api/personnes/recherche" => new List<PersonneResumeDto> { Leroy, Benali },
            "api/vehicules/recherche" => new List<VehiculeResumeDto> { Clio, Peugeot },
            "api/referentiels/agents" => new List<AgentResumeDto>
            {
                new(1, "AG-000001", "DURAND", "Jean-Pierre", "1234567", "Brigadier-chef", "BC", "CSP de Créteil", "BPS / BAC", "Chef de bord", "Actif"),
                new(2, "AG-000002", "GIRARD", "Valentin", "2045871", "Commandant", "CDT", "CSP de Créteil", "Direction", "OPJ", "Actif"),
                new(3, "AG-000003", "TABOULOT", "Louis", "3301458", "Major", "MAJ", "CSP de Créteil", "BPS / BAC", "Chef d'unité", "Actif")
            },
            "api/pegase/interventions/1284/resume" => new InterventionResumeDto(1284, "PG-2026-001284", "Vol avec violence", "Rue du Général-Leclerc", "Créteil",
                Now.AddHours(-4), "Clôturée", ["TV 94-12", "BAC 94-3"]),

            "api/cheops/recherche" => new RechercheGeneraleDto([Leroy], [Golf],
                [new CorrespondanceDto("TAJ", 1, "TAJ-000001", "LEROY Kévin — Vol avec violence (vol à l'arraché) (2025/000412)", 1, null)]),
            "api/cheops/personnes/1" => FicheLeroy(),
            "api/cheops/taj" => new List<TajLigneDto> { new(Leroy, 2, TajLeroy()[0].FaitsLe) },
            "api/cheops/taj/personnes/1" => new TajFicheDto(FicheLeroy(), TajLeroy(), [H(2, 300, "taj", "PER-000001", "Inscription", "TAJ-000002", null, "Mis en cause — Conduite sans assurance")]),
            "api/cheops/fpr" => new List<FprFicheDto> { FprLeroy(false), FprFontaine },
            "api/cheops/fpr/1" => FprLeroy(true),
            "api/cheops/foves" => new List<FovesFicheDto> { FovesGolf() },
            "api/cheops/foves/1" => FovesGolf(),
            "api/cheops/siv" => new List<VehiculeResumeDto> { Golf },
            "api/cheops/siv/3" => new VehiculeFicheDto(3, "DX-905-QA", "WVWZZZAUZFW098765", "Volkswagen", "Golf VII", "1.4 TSI Carat", "VP", "Blanc",
                new DateOnly(2015, 11, 20), 5, "ROUSSEL Anthony", "51 rue Jean-Jaurès, 94700 Maisons-Alfort", "Véhicule volé", "SAPHIR", ["FOVeS", "SIV"],
                [new AlerteBandeauDto("FOVeS", "FOV-000001", "VÉHICULE SIGNALÉ AU FOVeS — Vol — signalé le 28/10/2026", "Ne pas intervenir seul. Contrôler les occupants et aviser le CIC.")],
                [new AssuranceDto(4, 3, "DX-905-QA", "Mutuelle fictive du Val-de-Marne", "MFV-2019-004512", new DateOnly(2019, 1, 1), null, "Valide", true, null)],
                [FovesGolf()], [H(3, 7200, "vehicule", "DX-905-QA", "Modification", "Situation", "Normale", "Véhicule volé", "Plainte du titulaire")]),
            "api/cheops/fva" => new List<FvaLigneDto>
            {
                new(Peugeot, "Non assuré (contrat expiré ou résilié)", new AssuranceDto(2, 2, "FT-118-ZB", "Assurances Marne & Seine (fictif)", "AMS-883017",
                    new DateOnly(2025, 7, 1), new DateOnly(2026, 6, 30), "Valide", false, null))
            },
            "api/cheops/snpc" => new List<PermisDto> { PermisRoussel(false) },
            "api/cheops/snpc/personnes/5" => PermisRoussel(true),
            "api/cheops/scientifique" => new List<ScientifiqueDto>
            {
                new(1, "FAED", "FAED-000001", "PTS-94-2026-0311", "Cambriolage, 27 avenue du Général-de-Gaulle à Créteil", "2026/000381", "Trace papillaire",
                    Now.AddDays(-60), null, null, "En attente", "Trace exploitable relevée (simulation).", "SAPHIR", Array.Empty<HistoriqueDto>())
            },
            "api/cheops/anadoc" => new List<AnadocResultatDto>
            {
                new(1, "Acte", 12, "2026/000531", "2026/000531-PV001", "Procès-verbal d'interpellation", "Procès-verbal d'interpellation", Now.AddMinutes(-15),
                    "CSP de Créteil", "DURAND Jean-Pierre", "LEROY Kévin (PER-000001)", "… interpellation après une courte poursuite à pied, rue du Général-Leclerc …")
            },

            "api/lrppn/procedures" => new List<ProcedureLigneDto>
            {
                ProcLigne,
                new(9, "2026/000497", "Vol de véhicule — Maisons-Alfort", "Atteintes aux biens", "Enquête préliminaire", ProcedureStatuts.EnCours,
                    "CSP de Maisons-Alfort", "DURAND Jean-Pierre", Now.AddDays(-5), 2, 0, null),
                new(7, "2026/000381", "Cambriolage — avenue du Général-de-Gaulle, Créteil", "Atteintes aux biens", "Enquête préliminaire",
                    ProcedureStatuts.Transmise, "CSP de Créteil — BSU", "GIRARD Valentin", Now.AddDays(-61), 5, 0, null)
            },
            "api/lrppn/procedures/12" => Procedure(),
            "api/lrppn/procedures/12/historique" => new List<HistoriqueDto>
            {
                H(4, 15, "acte", "2026/000531-PV001", "Validation", "statut", "En relecture", "Validé", "Empreinte SHA-256 : 9f2c41d0…"),
                H(5, 60, "acte", "2026/000531-PV002", "Relecture demandée", "statut", "Brouillon", "En relecture"),
                H(6, 180, "procedure", "2026/000531", "Création", null, null, "Vol avec violence — Créteil", "Ouverte depuis l'intervention PÉGASE PG-2026-001284")
            },
            "api/lrppn/actes/33" => Pvi(),
            "api/lrppn/actes" => new List<ActeLigneDto> { LigneActe(32, 2, ActesCatalogue.Plainte, ActeStatuts.EnRelecture, "MOREAU Julie", false) },
            "api/lrppn/personnes/1/dossiers" => new DossiersAssociesDto(FicheLeroy(), TajLeroy(), [FprLeroy(false)], [ProcLigne], [], Aucune),
            _ => null
        };
    }

    private static PermisDto PermisRoussel(bool historique) => new(4, Roussel, "PC-000004", "B", "B : 15/02/1997", null, PermisSituations.Annule, 0, 12,
        historique ? [H(7, 90000, "permis", "PC-000004", "Situation", "situation", "Invalidé (solde nul)", "Annulé", "Décision judiciaire du 12/09/2025")] : Array.Empty<HistoriqueDto>());

    /// <summary>Captures du module (utiliser Shots.Capture). Les fichiers sont numérotés à partir de 60.</summary>
    public static void Run()
    {
        {
            var vm = new CheopsViewModel();
            Shots.Capture(new CheopsView { DataContext = vm }, 1280, 820, "60-cheops-portail.png");
        }
        {
            var vm = new CheopsViewModel();
            Shots.Capture(new CheopsView { DataContext = vm }, 1280, 820, "61-cheops-recherche-generale.png", () =>
            {
                vm.Ouvrir(CheopsViewModel.RechercheCode);
                vm.Recherche.Nom = "LEROY";
                vm.Recherche.RechercherCommand.Execute(null);
                Shots.Pump(600);
                vm.Recherche.PersonneSelection = vm.Recherche.Personnes.FirstOrDefault();
            });
        }
        {
            var vm = new CheopsViewModel();
            Shots.Capture(new CheopsView { DataContext = vm }, 1280, 820, "62-cheops-fpr.png", () =>
            {
                vm.Ouvrir(FichiersCheops.Fpr);
                Shots.Pump(600);
                vm.Fpr.Selection = vm.Fpr.Lignes.FirstOrDefault();
            });
        }
        {
            var vm = new CheopsViewModel();
            Shots.Capture(new CheopsView { DataContext = vm }, 1280, 820, "63-cheops-siv-foves.png", () =>
            {
                vm.Ouvrir(FichiersCheops.Siv);
                vm.Siv.Q = "DX-905-QA";
                vm.Siv.RechercherCommand.Execute(null);
                Shots.Pump(600);
                vm.Siv.Selection = vm.Siv.Lignes.FirstOrDefault();
            });
        }
        {
            var vm = new CheopsViewModel();
            Shots.Capture(new CheopsView { DataContext = vm }, 1280, 820, "64-cheops-taj.png", () =>
            {
                vm.Ouvrir(FichiersCheops.Taj);
                _ = vm.Taj.ChargerPersonneAsync(1);
            });
        }
        {
            var vm = new LrppnViewModel();
            Shots.Capture(new LrppnView { DataContext = vm }, 1280, 820, "65-lrppn-mes-procedures.png");
        }
        {
            var vm = new LrppnViewModel();
            Shots.Capture(new LrppnView { DataContext = vm }, 1280, 820, "66-lrppn-dossier.png", () => _ = vm.OuvrirDossierAsync(12));
        }
        {
            var vm = new LrppnViewModel();
            Shots.Capture(new LrppnView { DataContext = vm }, 1280, 820, "67-lrppn-pvi.png", () => _ = vm.OuvrirActeAsync(33));
        }
        {
            Saphir.Services.SaphirSession.Publish(new RealtimeMessage(Saphir.Services.SaphirSession.OpenAppType, AppId: SaphirApps.Lrppn, Text: "intervention:1284"));
            var vm = new LrppnViewModel();
            Shots.Capture(new LrppnView { DataContext = vm }, 1280, 820, "68-lrppn-procedure-depuis-pegase.png");
        }
    }
}
