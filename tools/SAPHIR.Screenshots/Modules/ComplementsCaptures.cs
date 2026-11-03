using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Media;
using Saphir.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Complements;
using Saphir.ViewModels.Apps.Complements;
using Saphir.Views.Apps.Complements;

namespace Saphir.Screenshots;

/// <summary>Captures et données fictives du module Complements (NEO, PVe, Annuaire, Messagerie, Notes, MRZ, Bodycam, Tchap).</summary>
public static class ComplementsCaptures
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 41, 0, TimeSpan.FromHours(1));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ===== Données fictives (Créteil, Val-de-Marne) =====

    private static readonly List<AgentResumeDto> Agents =
    [
        new(1, "AG-000001", "DURAND", "Jean-Pierre", "1234567", "Brigadier-chef", "BC", "CSP de Créteil", "BPS / BAC", "Chef de bord", AgentStatuts.Actif),
        new(2, "AG-000002", "GIRARD", "Valentin", "2045871", "Commandant", "CDT", "CSP de Créteil", "Direction", "Adjoint au chef de service", AgentStatuts.Actif),
        new(3, "AG-000003", "TABOULOT", "Louis", "3301458", "Major", "MAJ", "CSP de Créteil", "BPS / BAC", "Chef d'unité", AgentStatuts.Actif),
        new(4, "AG-000004", "BORDINI", "Nolan", "4410256", "Major", "MAJ", "CSP de Créteil", "CSI", "Chef d'unité", AgentStatuts.Actif),
        new(7, "AG-000007", "LEROY", "Camille", "5530912", "Gardien de la paix", "GPX", "CSP de Créteil", "Police secours", "Équipière", AgentStatuts.Actif),
        new(8, "AG-000008", "BENALI", "Yanis", "5602177", "Brigadier", "BG", "CSP de Créteil", "Unité de sécurité routière", "Motocycliste", AgentStatuts.Actif),
        new(9, "AG-000009", "FONTAINE", "Sarah", "4987301", "Capitaine", "CNE", "CSP de Créteil", "Service d'investigation", "Officier de police judiciaire", AgentStatuts.Actif),
        new(10, "AG-000010", "MOREL", "Hugo", "5711420", "Gardien de la paix", "GPX", "CSP de Créteil", "CIC 94", "Opérateur", AgentStatuts.Actif),
    ];

    private static readonly List<CoordonneesDto> Coordonnees =
    [
        new(1, "01 99 00 02 15", "BAC 94-3", "Bureau 214 — poste 2214", "jean-pierre.durand@vdms-police.fr", Now),
        new(2, "01 99 00 02 01", "Créteil Direction", "Bureau 301", "valentin.girard@vdms-police.fr", Now),
        new(3, "01 99 00 02 20", "BAC 94", null, null, Now),
        new(8, "01 99 00 02 44", "Moto 94-2", null, null, Now),
        new(9, "01 99 00 02 60", "SIR 94-1", "Bureau 118", "sarah.fontaine@vdms-police.fr", Now),
    ];

    private static readonly List<ServiceAnnuaireDto> Services =
    [
        new(1, "CIC 94 — Centre d'information et de commandement", "Commandement", "TN 94", "01 99 00 01 00", "Canal 1 — CIC", "Hôtel de police, Créteil", "24 h / 24", "Engagement des moyens", 10, true),
        new(2, "CSP de Créteil — Accueil", "Commissariat", "Créteil Accueil", "01 99 00 01 10", null, "1 place du Général-Billotte, Créteil", "24 h / 24", "Plaintes, main courante", 20, true),
        new(3, "BAC 94 de nuit", "Voie publique", "BAC 94", "01 99 00 01 20", "Canal 3 — BAC", "Créteil", "20 h – 6 h", null, 30, true),
        new(4, "Unité de sécurité routière", "Voie publique", "USR 94", "01 99 00 01 30", "Canal 4 — Routière", "Créteil", "7 h – 19 h", "Contrôles routiers, PVe", 40, true),
        new(5, "Service d'investigation et de recherche", "Investigation", "SIR 94", "01 99 00 01 40", null, "Créteil", "9 h – 19 h", null, 50, true),
        new(6, "SAMU 94 (simulation)", "Partenaires", "SAMU 94", "01 99 00 01 60", null, "Hôpital Henri-Mondor, Créteil", "24 h / 24", null, 60, true),
    ];

    private static readonly List<PersonneResumeDto> Personnes =
    [
        new(12, "PER-000012", "MARTIN", "Kevin", new DateOnly(1998, 4, 22), "Créteil (94)", "M", ["FPR"]),
        new(15, "PER-000015", "MARTIN", "Lea", new DateOnly(1990, 2, 15), "Vitry-sur-Seine (94)", "F", Array.Empty<string>()),
        new(21, "PER-000021", "DURAND", "Jean Pierre", new DateOnly(1985, 11, 3), "Maisons-Alfort (94)", "M", ["TAJ"]),
    ];

    private static readonly List<VehiculeResumeDto> Vehicules =
    [
        new(5, "GH-482-KL", "Peugeot", "308", "Gris", 12, "MARTIN Kevin", ["FOVeS"]),
        new(6, "FT-019-BZ", "Renault", "Clio", "Blanc", 15, "MARTIN Lea", Array.Empty<string>()),
    ];

    private static readonly List<InfractionDto> Infractions =
    [
        new(1, "11298", "Excès de vitesse inférieur à 20 km/h — vitesse maximale autorisée supérieure à 50 km/h", "Art. R413-14 C. route", "Vitesse", "3", 68m, 45m, 180m, 1, true, 10),
        new(3, "11300", "Excès de vitesse égal ou supérieur à 20 km/h et inférieur à 30 km/h", "Art. R413-14 C. route", "Vitesse", "4", 135m, 90m, 375m, 2, true, 30),
        new(6, "20015", "Inobservation de l'arrêt imposé par un feu rouge fixe ou clignotant", "Art. R412-30 C. route", "Signalisation", "4", 135m, 90m, 375m, 4, true, 60),
        new(17, "21001", "Usage d'un téléphone tenu en main par le conducteur d'un véhicule en circulation", "Art. R412-6-1 C. route", "Comportement", "4", 135m, 90m, 375m, 3, true, 170),
        new(19, "21003", "Défaut de port de la ceinture de sécurité par le conducteur", "Art. R412-1 C. route", "Comportement", "4", 135m, 90m, 375m, 3, true, 190),
        new(22, "22001", "Stationnement très gênant sur un trottoir", "Art. R417-11 C. route", "Stationnement", "4", 135m, null, 375m, 0, true, 220),
        new(26, "23001", "Défaut de présentation immédiate du permis de conduire", "Art. R233-1 C. route", "Documents", "1", 11m, null, 33m, 0, true, 260),
        new(33, "24001", "Conduite d'un véhicule sans assurance (amende forfaitaire délictuelle)", "Art. L324-2 C. route", "Délits forfaitaires", "AFD", 500m, 400m, 1000m, 0, true, 330),
    ];

    private static PveDto PveDetail(long id)
    {
        var brouillon = id == 2;
        return new PveDto(id, $"PVE-2026-00000{id}", brouillon ? null : "9426000000148", brouillon ? PveStatuts.Brouillon : PveStatuts.Valide,
            17, "21001", "Usage d'un téléphone tenu en main par le conducteur d'un véhicule en circulation", "Art. R412-6-1 C. route", "4",
            135m, 90m, 375m, 3, Now.AddMinutes(-35), "Avenue du Général-de-Gaulle, face au n° 52", "Créteil", 12, "PER-000012", "MARTIN", "Kevin",
            new DateOnly(1998, 4, 22), "Créteil (94)", "14 rue des Écoles, 94000 Créteil", 5, "GH-482-KL", "Peugeot", "308", "Gris",
            "Conducteur observé téléphone à l'oreille gauche en circulation, intercepté rue de Paris. Reconnaît les faits.",
            "Avis remis en main propre.", "INT-2026-000031", 1, "DURAND Jean-Pierre", "1234567", "Brigadier-chef", "CSP de Créteil", "BPS / BAC",
            Now.AddMinutes(-30), Now.AddMinutes(-5), brouillon ? null : Now.AddMinutes(-4), null, 3,
            [new PieceJointeDto(41, "photo-interception.jpg", "image/jpeg", 845_000)],
            brouillon ? Array.Empty<PveMentionDto>() : [new PveMentionDto(1, Now.AddMinutes(-2), "DURAND Jean-Pierre (AG-000001)", "Adresse confirmée par le SIV fictif.")]);
    }

    private static readonly List<PveResumeDto> Pves =
    [
        new(2, "PVE-2026-000002", null, PveStatuts.Brouillon, Now.AddMinutes(-35), "21001", "Usage d'un téléphone tenu en main par le conducteur", "MARTIN Kevin", "GH-482-KL", "Créteil", "DURAND Jean-Pierre", 135m),
        new(1, "PVE-2026-000001", "9426000000148", PveStatuts.Valide, Now.AddHours(-3), "11300", "Excès de vitesse égal ou supérieur à 20 km/h et inférieur à 30 km/h", "BERTIN Paul", "EK-334-RS", "Créteil", "DURAND Jean-Pierre", 135m),
        new(3, "PVE-2026-000003", "9426000000312", PveStatuts.Valide, Now.AddDays(-1), "22001", "Stationnement très gênant sur un trottoir", null, "FT-019-BZ", "Maisons-Alfort", "BENALI Yanis", 135m),
        new(4, "PVE-2026-000004", null, PveStatuts.Abandonne, Now.AddDays(-2), "20015", "Inobservation de l'arrêt imposé par un feu rouge", "NOEL Inès", "CB-771-ZA", "Créteil", "DURAND Jean-Pierre", 135m),
    ];

    private static List<MessageResumeDto> Boite(string dossier) => dossier switch
    {
        MessagerieDossiers.Envoyes =>
        [
            new(5, "MSG-000005", 3, "RE : Dispositif contrôle routier av. de Verdun", 1, "DURAND Jean-Pierre", "BENALI Yanis, TABOULOT Louis", Now.AddMinutes(-20), true, false, false, 0),
        ],
        MessagerieDossiers.Brouillons =>
        [
            new(6, "MSG-000006", 6, "Compte rendu de vacation du 01/11", 1, "DURAND Jean-Pierre", "GIRARD Valentin", Now.AddHours(-1), true, true, false, 1),
        ],
        MessagerieDossiers.Corbeille => [],
        _ =>
        [
            new(4, "MSG-000004", 4, "Note de service — tenue du registre bodycam", 2, "GIRARD Valentin", "DURAND Jean-Pierre", Now.AddMinutes(-8), false, false, true, 1),
            new(3, "MSG-000003", 3, "Dispositif contrôle routier av. de Verdun", 8, "BENALI Yanis", "DURAND Jean-Pierre", Now.AddMinutes(-50), true, false, false, 0),
            new(2, "MSG-000002", 2, "Audition prévue demain 10 h — PROC-2026-000002", 9, "FONTAINE Sarah", "DURAND Jean-Pierre", Now.AddHours(-5), true, false, false, 0),
            new(1, "MSG-000001", 1, "Bienvenue sur la messagerie SAPHIR", 2, "GIRARD Valentin", "DURAND Jean-Pierre", Now.AddDays(-3), true, false, false, 0),
        ]
    };

    private static MessageDto Message(long id) => id switch
    {
        3 => new(3, "MSG-000003", 3, null, "Dispositif contrôle routier av. de Verdun", "Bonjour chef,\n\nPour cet après-midi : contrôle de vitesse avenue de Verdun (sens Paris → Créteil), de 14 h à 16 h, avec cinémomètre et deux motocyclistes en interception.\n\nJe prépare les PVe sur NEO.\n\nBG BENALI",
            8, "BENALI Yanis", null, [new(1, "DURAND Jean-Pierre", MessagerieTypes.A, Now.AddMinutes(-45)), new(3, "TABOULOT Louis", MessagerieTypes.Cc, null)],
            [], ["INT-2026-000031"], false, false, Now.AddMinutes(-50), Now.AddMinutes(-50), 1),
        _ => new(4, "MSG-000004", 4, null, "Note de service — tenue du registre bodycam",
            "À l'attention des chefs de bord,\n\nÀ compter du 3 novembre, chaque enregistrement de caméra-piéton doit être inscrit au registre BODYCAM de SAPHIR dans l'heure suivant la fin de l'intervention, avec le numéro de l'intervention PÉGASE.\n\nLes visionnages sont journalisés.\n\nCDT GIRARD",
            2, "GIRARD Valentin", "BPS / BAC", [new(1, "DURAND Jean-Pierre", MessagerieTypes.A, Now.AddMinutes(-1)), new(3, "TABOULOT Louis", MessagerieTypes.A, null)],
            [new PieceJointeDto(7, "note-de-service-bodycam.pdf", "application/pdf", 182_000)], ["BC-2026-000003"], false, true, Now.AddMinutes(-8), Now.AddMinutes(-8), 1),
    };

    private static List<NoteDto> Notes() =>
    [
        new(3, "NOTE-000003", "Indicatifs radio — vacation de nuit", "BAC 94-1 : TABOULOT / LEROY\nBAC 94-3 : DURAND / BENALI\nTN : canal 1, relais Créteil-Préfecture\nFréquence de repli : canal 5", "Service", true, 1, "DURAND Jean-Pierre", true, true,
            [new NotePartageDto(3, "TABOULOT Louis", true), new NotePartageDto(7, "LEROY Camille", false)], Now.AddDays(-2), Now.AddHours(-2), 4),
        new(2, "NOTE-000002", "Points de contrôle routiers", "• Av. de Verdun (sens Paris → Créteil), zone 50\n• RD 86 sortie Pompadour\n• Rue de Paris, feu tricolore Hôtel de ville", "Sécurité routière", false, 1, "DURAND Jean-Pierre", true, true,
            [], Now.AddDays(-5), Now.AddDays(-1), 2),
        new(1, "NOTE-000001", "Procédure garde à vue — rappels", "Avis magistrat immédiat, médecin sur demande, avocat dès le début de la mesure.", "Procédure", false, 9, "FONTAINE Sarah", false, false,
            [new NotePartageDto(1, "DURAND Jean-Pierre", false)], Now.AddDays(-9), Now.AddDays(-9), 1),
    ];

    private static List<BodycamResumeDto> Bodycams() =>
    [
        new(3, "BC-2026-000003", "DURAND Jean-Pierre", Now.AddMinutes(-70), 754, "INT-2026-000031", BodycamSources.Fichier, BodycamStatuts.Disponible, "Interpellation rue de Paris après refus d'obtempérer."),
        new(2, "BC-2026-000002", "BENALI Yanis", Now.AddHours(-4), 312, "INT-2026-000027", BodycamSources.Url, BodycamStatuts.Disponible, "Contrôle routier, conducteur sans permis."),
        new(1, "BC-2026-000001", "LEROY Camille", Now.AddDays(-1), 0, null, BodycamSources.Fichier, BodycamStatuts.EnAttente, "Différend familial, Créteil Mont-Mesly."),
    ];

    private static BodycamDto Bodycam(long id) => new(3, "BC-2026-000003", 1, "DURAND Jean-Pierre", Now.AddMinutes(-70), 754, "INT-2026-000031",
        "Interpellation rue de Paris après refus d'obtempérer. Conducteur interpellé sans violence, palpation, menottage, transport au CSP.",
        "Caméra-piéton individuelle", BodycamSources.Fichier, null, "VID_20261102_0831.mp4", 148_600_000,
        "video/mp4", "3f9a0c2d71be44e0a8d1c5f2e7b6a9c40d3e2f1a5b6c7d8e9f0a1b2c3d4e5f60", 148_600_000, BodycamStatuts.Disponible,
        "TABOULOT Louis (AG-000003)", Now.AddMinutes(-60), Now.AddMinutes(-40), null, 2,
        [new PieceJointeDto(9, "pv-interpellation-extrait.pdf", "application/pdf", 96_000)]);

    private static List<SalonDto> Salons() =>
    [
        new(1, "Général — CSP de Créteil", "Informations de service pour tous les agents", TchapTypesSalon.Service, true, true, 0, 0, Now.AddMinutes(-4), false),
        new(2, "CIC 94 — Informations", "Diffusions du CIC", TchapTypesSalon.Service, true, true, 0, 3, Now.AddMinutes(-12), false),
        new(3, "Sécurité routière", "Opérations de contrôle, PVe, dépistages", TchapTypesSalon.Service, true, true, 0, 0, Now.AddHours(-2), false),
        new(10, "BAC 94-3 — équipage", null, TchapTypesSalon.Groupe, true, true, 3, 1, Now.AddMinutes(-2), false),
    ];

    private static List<TchapMessageDto> TchapMessages(long salon) =>
    [
        new(101, salon, 10, "MOREL Hugo", "TN 94 à tous : accident matériel RD 86 sortie Pompadour, circulation ralentie. Évitez le secteur.", Now.AddMinutes(-31), false),
        new(102, salon, 3, "TABOULOT Louis", "Reçu. BAC 94-1 reste sur le secteur Mont-Mesly.", Now.AddMinutes(-27), false),
        new(103, salon, 1, "DURAND Jean-Pierre", "BAC 94-3 disponible au CSP, retour de conduite au poste.", Now.AddMinutes(-20), false),
        new(104, salon, 8, "BENALI Yanis", "Contrôle av. de Verdun terminé : 6 PVe, 1 dépistage positif (procédure en cours).", Now.AddMinutes(-9), false),
        new(105, salon, 2, "GIRARD Valentin", "Merci à tous. Rappel : registre BODYCAM à jour avant la fin de vacation.", Now.AddMinutes(-4), false),
    ];

    private static readonly InterventionResumeDto Intervention = new(31, "INT-2026-000031", "Refus d'obtempérer", "Rue de Paris, face au n° 18", "Créteil",
        Now.AddMinutes(-80), "Sur place", ["BAC 94-3", "PS Créteil 2"]);

    /// <summary>Réponse fictive à une requête du poste (null = route inconnue de ce module).</summary>
    public static object? Sample(string method, string path, string query, string? body)
    {
        var parametres = Requete(query);
        string Param(string nom) => parametres.TryGetValue(nom, out var v) ? v : string.Empty;
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Routes partagées des autres modules (repli si ces modules ne fournissent pas d'exemple).
        switch (path)
        {
            case "api/referentiels/agents": return Agents;
            case "api/referentiels/services":
                return Agents.Select(a => a.Service).Distinct().Select((s, i) => new ReferentielDto(i + 1, "S" + i, s!, null, null, i, true)).ToList();
            case "api/referentiels/unites":
                return Agents.Select(a => a.Unite).Distinct().Select((s, i) => new ReferentielDto(i + 1, "U" + i, s!, null, 1, i, true)).ToList();
            case "api/referentiels/grades":
                return Agents.Select(a => a.Grade).Distinct().Select((s, i) => new ReferentielDto(i + 1, "G" + i, s!, null, null, i, true)).ToList();
            case "api/personnes/recherche":
                return Personnes.Where(p => Param("q").Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Any(m => p.Nom.Contains(m, StringComparison.OrdinalIgnoreCase) || p.Reference.Contains(m, StringComparison.OrdinalIgnoreCase))).ToList();
            case "api/vehicules/recherche":
                return Vehicules.Where(v => v.Immatriculation.Replace("-", "").Contains(Param("q").Replace("-", "").Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (path == "api/pegase/interventions/31/resume") return Intervention;
        if (path.StartsWith("api/personnes/", StringComparison.Ordinal) && path.EndsWith("/resume", StringComparison.Ordinal)) return Personnes[0];

        // Annuaire
        switch (path)
        {
            case "api/annuaire/agents": return Agents.Select(a => new AnnuaireAgentDto(a.Id, a.Reference, a.Nom, a.Prenom, a.Rio, a.Statut)).ToList();
            case "api/annuaire/coordonnees": return Coordonnees;
            case "api/annuaire/services": return Services;
        }

        // Messagerie
        if (path == "api/messagerie/compteurs") return new MessagerieCompteursDto(1, 1);
        if (path == "api/messagerie/messages" && method == "GET") return Boite(Param("dossier"));
        if (segments is ["api", "messagerie", "messages", var mid] && long.TryParse(mid, out var messageId)) return Message(messageId);
        if (segments is ["api", "messagerie", "conversations", var cid] && long.TryParse(cid, out var conversation))
            return conversation == 3
                ? new List<MessageDto> { Message(3), Message(3) with { Id = 5, Reference = "MSG-000005", ExpediteurId = 1, Expediteur = "DURAND Jean-Pierre", Corps = "Reçu, je valide le dispositif. Pense au registre bodycam.", EnvoyeLe = Now.AddMinutes(-20) } }
                : [Message(conversation)];

        // Notes
        if (path == "api/notes") return Notes();
        if (path == "api/notes/categories") return new List<string> { "Procédure", "Sécurité routière", "Service" };

        // MRZ
        if (path == "api/mrz/analyser" && body is not null)
            return MrzParser.Analyser(JsonSerializer.Deserialize<MrzAnalyseInput>(body, Json)?.Texte, DateOnly.FromDateTime(Now.Date));
        if (path == "api/mrz/lectures" && method == "GET")
            return new List<MrzLectureDto>
            {
                new(2, "MRZ-2026-000002", Now.AddMinutes(-15), "DURAND Jean-Pierre (AG-000001)", MrzParser.FormatTd1, true, "MARTIN", "LEA CHLOE",
                    new DateOnly(1990, 2, 15), "F", "X4RTBPFW4", "ID", "FRA", new DateOnly(2033, 6, 14), 15, "PER-000015", "MARTIN Lea", "Contrôle routier av. de Verdun"),
            };

        // PVe
        if (path == "api/pve/infractions") return Infractions;
        if (path == "api/pve") return Pves.Where(p => Param("mien") != "true" || p.Agent == "DURAND Jean-Pierre").ToList();
        if (segments is ["api", "pve", var pid] && long.TryParse(pid, out var pveId)) return PveDetail(pveId);

        // Bodycam
        if (path == "api/bodycam") return Bodycams();
        if (segments is ["api", "bodycam", var bid] && long.TryParse(bid, out var bodycamId)) return Bodycam(bodycamId);
        if (segments is ["api", "bodycam", _, "acces"])
            return new List<BodycamAccesDto> { new(2, Now.AddMinutes(-30), "GIRARD Valentin (AG-000002)", "Visionnage"), new(1, Now.AddMinutes(-45), "DURAND Jean-Pierre (AG-000001)", "Visionnage") };

        // Tchap
        if (path == "api/tchap/salons") return Salons();
        if (segments is ["api", "tchap", "salons", var sid, "messages"] && long.TryParse(sid, out var salon)) return TchapMessages(salon);
        if (segments is ["api", "tchap", "salons", _, "membres"])
            return new List<SalonMembreDto> { new(1, "DURAND Jean-Pierre", "admin", Now.AddDays(-3)), new(8, "BENALI Yanis", "membre", Now.AddDays(-3)), new(3, "TABOULOT Louis", "membre", Now.AddDays(-1)) };

        return null;
    }

    private static Dictionary<string, string> Requete(string query)
    {
        var resultat = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var partie in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var egal = partie.IndexOf('=');
            if (egal > 0)
                resultat[Uri.UnescapeDataString(partie[..egal])] = Uri.UnescapeDataString(partie[(egal + 1)..].Replace('+', ' '));
        }
        return resultat;
    }

    /// <summary>Captures du module (utiliser Shots.Capture). Les fichiers sont numérotés à partir de 80.</summary>
    public static void Run()
    {
        // 80. NEO sur téléphone : profil, intervention suivie, tuiles
        {
            var vm = new NeoViewModel("intervention:31");
            Shots.Pump();
            Shots.Capture(Telephone(new NeoView { DataContext = vm }), 390, 844, "80-complements-neo-telephone.png");
        }

        // 81. NEO : consultation rapide d'une personne (alertes)
        {
            var vm = new NeoViewModel();
            Shots.Pump();
            Shots.Capture(Telephone(new NeoView { DataContext = vm }), 390, 844, "81-complements-neo-personne.png", () =>
            {
                vm.Page = NeoViewModel.PagePersonne;
                vm.Recherche = "MARTIN";
                vm.RechercherCommand.Execute(null);
            });
        }

        // 82. Annuaire : personnel et fiche contact
        {
            var vm = new AnnuaireViewModel();
            Shots.Pump();
            Shots.Capture(new AnnuaireView { DataContext = vm }, 1280, 820, "82-complements-annuaire.png",
                () => vm.Selection = vm.Agents.FirstOrDefault(a => a.Nom == "FONTAINE"));
        }

        // 83. Annuaire des services
        {
            var vm = new AnnuaireViewModel();
            Shots.Pump();
            Shots.Capture(new AnnuaireView { DataContext = vm }, 1280, 820, "83-complements-annuaire-services.png", () =>
            {
                vm.Section = AnnuaireViewModel.SectionServices;
                Shots.Pump(300);
                vm.ServiceSelectionne = vm.ListeServices.FirstOrDefault(s => s.Id == 4);
            });
        }

        // 84-85. Messagerie : lecture avec accusés, rédaction
        {
            var vm = new MessagerieViewModel();
            Shots.Pump();
            Shots.Capture(new MessagerieView { DataContext = vm }, 1280, 820, "84-complements-messagerie.png",
                () => vm.Selection = vm.Messages.FirstOrDefault(m => m.Id == 3));
        }
        {
            var vm = new MessagerieViewModel("a:9");
            Shots.Pump();
            Shots.Capture(new MessagerieView { DataContext = vm }, 1280, 820, "85-complements-messagerie-redaction.png", () =>
            {
                vm.Cc.Ajouter(3, "MAJ TABOULOT Louis");
                vm.Sujet = "Remise de la procédure PROC-2026-000002";
                vm.Corps = "Capitaine,\n\nJe vous transmets les éléments de l'interpellation de ce matin (rue de Paris).\nLa vidéo bodycam est au registre.\n\nBC DURAND";
                vm.NouveauLien = "BC-2026-000003";
                vm.AjouterLienCommand.Execute(null);
                vm.NouveauLien = "PER-000012";
                vm.AjouterLienCommand.Execute(null);
            });
        }

        // 86. Notes
        {
            var vm = new NotesViewModel();
            Shots.Pump();
            Shots.Capture(new NotesView { DataContext = vm }, 1280, 820, "86-complements-notes.png",
                () => vm.Selection = vm.Notes.FirstOrDefault());
        }

        // 87. MRZ : carte d'identité TD1 analysée, identité retrouvée
        {
            var vm = new MrzViewModel();
            Shots.Pump();
            Shots.Capture(new MrzView { DataContext = vm }, 1280, 820, "87-complements-mrz.png", () =>
            {
                vm.Texte = "IDFRAX4RTBPFW46<<<<<<<<<<<<<<<\n9002155F3306149FRA<<<<<<<<<<<0\nMARTIN<<LEA<CHLOE<<<<<<<<<<<<<";
                vm.AnalyserCommand.Execute(null);
            });
        }

        // 88-89. PVe : rédaction d'un brouillon, liste
        {
            var vm = new PveViewModel("pve:2");
            Shots.Pump();
            Shots.Capture(new PveView { DataContext = vm }, 1280, 820, "88-complements-pve-redaction.png");
        }
        {
            var vm = new PveViewModel();
            Shots.Pump();
            Shots.Capture(new PveView { DataContext = vm }, 1280, 820, "89-complements-pve-liste.png",
                () => vm.Section = PveViewModel.SectionTous);
        }

        // 90. Bodycam : fiche d'un enregistrement
        {
            var vm = new BodycamViewModel("bodycam:3");
            Shots.Pump();
            Shots.Capture(new BodycamView { DataContext = vm }, 1280, 820, "90-complements-bodycam.png",
                () => vm.AccesCommand.Execute(null));
        }

        // 91-92. Tchap : ordinateur et téléphone
        {
            var vm = new TchapViewModel("salon:1");
            Shots.Pump();
            Shots.Capture(new TchapView { DataContext = vm }, 1280, 820, "91-complements-tchap.png");
        }
        {
            var vm = new TchapViewModel("salon:10");
            Shots.Pump();
            Shots.Capture(Telephone(new TchapView { DataContext = vm }), 390, 844, "92-complements-tchap-telephone.png");
        }
    }

    private static Control Telephone(Control contenu)
    {
        var racine = new Grid { Background = Brushes.White };
        racine.Children.Add(contenu);
        return racine;
    }
}
