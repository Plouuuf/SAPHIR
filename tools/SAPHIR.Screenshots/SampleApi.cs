using System.Net;
using System.Text;
using System.Text.Json;
using Saphir.Shared.Contracts;

namespace Saphir.Screenshots;

/// <summary>Serveur SAPHIR simulé : réponses d'exemple pour chaque adresse appelée par les écrans.</summary>
public sealed class SampleApi : HttpMessageHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.Trim('/');
        object? body = path switch
        {
            "api/meo/personnes" => People(),
            "api/meo/personne" => Person(),
            "api/meo/vehicules" => Vehicles(),
            "api/meo/vehicule" => Vehicle(),
            "api/meo/journal" => Journal(),
            "api/pegase/etat" => PegaseState(),
            "api/pts/personne" => PtsPerson(),
            "api/pts/traces" => Traces(),
            _ when path.StartsWith("api/pegase/evenement/", StringComparison.Ordinal) => EventDetail(),
            _ when path.StartsWith("api/pegase/appel/", StringComparison.Ordinal) => Call(),
            _ => null
        };

        var response = body is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"error\":\"Introuvable.\"}", Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body, body.GetType(), Json), Encoding.UTF8, "application/json") };
        return Task.FromResult(response);
    }

    // ===== MEO-FIC =====

    private static List<PersonSummaryDto> People() =>
    [
        new("f:char1:7a3f", "MARCHETTI", "Lucas", "14/03/1996", "M", "FiveM", true, 2, "Suspendu"),
        new("f:char1:91bc", "MARCHETTI", "Léa", "02/11/1999", "F", "FiveM", false, 0, "Valide"),
        new("s:42", "MARCHETTI", "Enzo", "21/07/2004", "M", "SAPHIR", false, 1, null)
    ];

    private static PersonDetailDto Person() => new(
        "f:char1:7a3f", 12, "char1:7a3f", "FiveM + SAPHIR",
        new IdentityDto("MARCHETTI", "Lucas", "14/03/1996", "Marseille (13)", "M", "Française",
            "12 rue des Écoles, 94000 Créteil", "06 12 34 56 78", "182 cm",
            "Corpulence athlétique, cheveux bruns courts, tatouage tribal avant-bras droit", "« Luca », « Le Marseillais »"),
        [
            new TajEntryDto(31, "Mis en cause", "Vol avec violence", "7151", "28/10/2026", "Centre commercial Créteil Soleil",
                "Commissariat de Créteil", "2026/004512", "Arrachage de sac à main sur une femme âgée, fuite à pied vers le métro.",
                "Brigadier DURAND", Now.AddDays(-5)),
            new TajEntryDto(18, "Mis en cause", "Conduite sans permis", "11013", "02/09/2026", "RN 186, Créteil",
                "Brigade motocycliste 94", "2026/003107", null, "Gardien de la paix LEROY", Now.AddDays(-61))
        ],
        [
            new FprEntryDto(7, "PJ — Recherche judiciaire", "Interpeller et aviser l'autorité", "OPJ de permanence, SDPJ 94 — 01 45 17 00 00",
                "Recherché dans le cadre d'une série de vols avec violence sur le secteur Créteil / Maisons-Alfort.",
                true, true, true, "Lieutenant MOREAU", Now.AddDays(-3), null, null)
        ],
        new PermisDto("19AB34567", "B", 0, "Suspendu", "18/06/2015", "Suspension administrative de 6 mois", "Brigadier DURAND", Now.AddDays(-20)),
        [
            new VehicleSummaryDto("GH-482-KT", "Peugeot", "308", "Gris", "MARCHETTI Lucas", "FiveM + SAPHIR", false),
            new VehicleSummaryDto("FT-119-ZA", "Yamaha", "MT-07", "Noir", "MARCHETTI Lucas", "FiveM", true)
        ]);

    private static List<VehicleSummaryDto> Vehicles() =>
    [
        new("FT-119-ZA", "Yamaha", "MT-07", "Noir", "MARCHETTI Lucas", "FiveM", true),
        new("FT-542-BD", "Renault", "Clio", "Blanc", "NGUYEN Thomas", "FiveM", false)
    ];

    private static VehicleDetailDto Vehicle() => new(
        "FT-119-ZA",
        new SivDto("Yamaha", "MT-07", "Noir", "JYARM321000123456", "04/04/2021", "MARCHETTI Lucas", "f:char1:7a3f",
            "Non soumis", null, "FiveM + SAPHIR"),
        new FvaDto("Contrat expiré", "Assurance Mutuelle du Val-de-Marne", "AMV-2021-88412", "30/09/2026"),
        [
            new FovesEntryDto(5, "Recherche judiciaire", "Interpeller et aviser l'autorité",
                "Moto utilisée lors du vol avec violence du 28/10 à Créteil Soleil, deux occupants casqués.",
                true, "Brigadier DURAND", Now.AddDays(-5), null, null)
        ]);

    private static List<AuditEntryDto> Journal() =>
    [
        new(Now.AddMinutes(-3), "Brigadier DURAND", "Consultation personne", "MARCHETTI Lucas"),
        new(Now.AddMinutes(-9), "Gardien LEROY", "Consultation véhicule", "FT-119-ZA"),
        new(Now.AddHours(-2), "Lieutenant MOREAU", "Inscription FPR", "MARCHETTI Lucas — PJ — Recherche judiciaire")
    ];

    // ===== PEGASE =====

    private static EvenementDto Event1() => new(108, "2026-00108", "Rixe", 1, "Place Salvador Allende", "Créteil",
        "Devant la sortie du métro, ligne 8", "Mme KACI", "06 44 55 66 77",
        "Une dizaine d'individus se battent, l'un aurait un couteau. Un blessé au sol.",
        EvenementStatuts.SurPlace, "Opérateur BERNARD", Now.AddMinutes(-18), null, null, null, ["PS 94-1", "BAC N 94"]);

    private static PegaseStateDto PegaseState() => new(
        [
            Event1(),
            new EvenementDto(109, "2026-00109", "Cambriolage en cours", 1, "14 allée des Tilleuls", "Maisons-Alfort", null,
                "M. PETIT", "06 98 76 54 32", "Bruit de vitre brisée chez le voisin absent, deux silhouettes à l'intérieur.",
                EvenementStatuts.Attente, "Opérateur BERNARD", Now.AddMinutes(-4), null, null, null, []),
            new EvenementDto(105, "2026-00105", "Accident de la circulation", 2, "Avenue du Général de Gaulle", "Créteil",
                "Hauteur n° 80", null, null, "Accrochage VL / deux-roues, conducteur du deux-roues conscient.",
                EvenementStatuts.Engage, "Opérateur BERNARD", Now.AddMinutes(-35), null, null, null, ["PS 94-2"]),
            new EvenementDto(101, "2026-00101", "Tapage / nuisances", 3, "3 rue Juliette Savar", "Créteil", "Bât. B, 4e étage",
                "Anonyme", null, "Musique forte depuis 23 h.", EvenementStatuts.Attente, "Opérateur BERNARD", Now.AddMinutes(-52),
                null, null, null, [])
        ],
        [
            new EquipageDto(1, "PS 94-1", "Police secours", "GPX DURAND, GPX LEROY — VL 2", EquipageStatuts.SurPlace, 108, "2026-00108", Now),
            new EquipageDto(2, "BAC N 94", "BAC", "BRI MOREAU + 2 — VL banalisé", EquipageStatuts.Engage, 108, "2026-00108", Now),
            new EquipageDto(3, "PS 94-2", "Police secours", "GPX MARTIN, ADS COLIN — VL 5", EquipageStatuts.Engage, 105, "2026-00105", Now),
            new EquipageDto(4, "MOTO 94", "Brigade motocycliste", "BRI FAURE", EquipageStatuts.Disponible, null, null, Now),
            new EquipageDto(5, "CYNO 94", "Unité cynophile", "GPX ROUX et « Rex »", EquipageStatuts.Indisponible, null, null, Now)
        ],
        [new AppelDto(42, "a1b2c3", "06 71 22 33 44", "Opérateur BERNARD", "1", Now.AddSeconds(-47), null, AppelStatuts.EnCours, null, null)]);

    private static EvenementDetailDto EventDetail() => new(
        Event1(),
        [
            new PegaseMessageDto(1, Now.AddMinutes(-18), "Opérateur BERNARD", "creation", "Création de l'événement — Rixe (P1 — Urgence absolue), appel n° 40 du 06 44 55 66 77."),
            new PegaseMessageDto(2, Now.AddMinutes(-17), "Opérateur BERNARD", "engagement", "Engagement de PS 94-1 (Police secours)."),
            new PegaseMessageDto(3, Now.AddMinutes(-16), "Opérateur BERNARD", "engagement", "Engagement de BAC N 94 (BAC)."),
            new PegaseMessageDto(4, Now.AddMinutes(-11), "Opérateur BERNARD", "statut", "PS 94-1 : sur place."),
            new PegaseMessageDto(5, Now.AddMinutes(-9), "PS 94-1", "info", "Un blessé léger à l'arcade, BSPP demandée. Deux individus en fuite vers le lac, survêtement noir.")
        ],
        new AppelDto(40, "x9y8", "06 44 55 66 77", "Opérateur BERNARD", "1", Now.AddMinutes(-19), Now.AddMinutes(-18), AppelStatuts.Evenement, null, 108));

    private static AppelDto Call() => new(42, "a1b2c3", "06 71 22 33 44", "Opérateur BERNARD", "1", Now.AddSeconds(-47), null, AppelStatuts.EnCours, null, null);

    // ===== PTS =====

    private static PtsPersonDto PtsPerson() => new(
        "f:char1:7a3f",
        [
            new SignalisationDto(3, PtsFichiers.Faed, "IDPP-94-2026-00871", "28/10/2026", "SLPT Créteil", "Vol avec violence, procédure 2026/004512", null, "Technicien PTS GARNIER", Now.AddDays(-5)),
            new SignalisationDto(4, PtsFichiers.Fnaeg, "FNAEG-2026-115204", "28/10/2026", "SLPT Créteil", "Prélèvement buccal en garde à vue", null, "Technicien PTS GARNIER", Now.AddDays(-5))
        ],
        [
            new InscriptionDto(2, PtsFichiers.Fijais, "Violences volontaires aggravées", "Condamnation définitive", "12/05/2024",
                "Tribunal judiciaire de Créteil", "Justifier de son adresse tous les ans", "12/05/2044", null, "Greffe — transmission", Now.AddDays(-120))
        ],
        [
            new TraceDto(9, PtsFichiers.Faed, "T-2026-0412", "Vol avec violence Créteil Soleil", "Rétroviseur gauche de la Yamaha FT-119-ZA",
                "29/10/2026", "Trace papillaire exploitable, index droit.", "f:char1:7a3f", "MARCHETTI Lucas", "Technicien PTS GARNIER", Now.AddDays(-1),
                "Technicien PTS GARNIER", Now.AddDays(-4))
        ]);

    private static List<TraceDto> Traces() =>
    [
        new(11, PtsFichiers.Fnaeg, "T-2026-0431", "Cambriolage Maisons-Alfort", "14 allée des Tilleuls, poignée de la porte-fenêtre",
            "02/11/2026", "Trace de contact, profil partiel.", null, null, null, null, "Technicien PTS GARNIER", Now.AddHours(-1)),
        new(10, PtsFichiers.Faed, "T-2026-0425", "Vol de véhicule Créteil", "Portière conducteur, Renault Clio FT-542-BD",
            "31/10/2026", "Deux traces digitales, une exploitable.", null, null, null, null, "Technicien PTS GARNIER", Now.AddDays(-2))
    ];
}
