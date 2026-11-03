using System.Text;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Rh;
using Saphir.ViewModels.Apps.Rh;
using Saphir.Views.Apps.Rh;

namespace Saphir.Screenshots;

/// <summary>Captures et données fictives du module RH (commissariat central de Créteil, Val-de-Marne).</summary>
public static class RhCaptures
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 41, 0, TimeSpan.FromHours(1));
    private static readonly DateOnly Aujourdhui = new(2026, 11, 2);

    // ===== Référentiels =====

    private static readonly List<RhReferentielDto> Grades =
    [
        G(1, "GPX", "Gardien de la paix", RhGradeCategories.EncadrementApplication, 10, 3),
        G(2, "SBC", "Sous-brigadier", RhGradeCategories.EncadrementApplication, 20, 1),
        G(3, "BG", "Brigadier", RhGradeCategories.EncadrementApplication, 30, 1),
        G(4, "BC", "Brigadier-chef", RhGradeCategories.EncadrementApplication, 40, 2),
        G(5, "MJR", "Major", RhGradeCategories.EncadrementApplication, 50, 2),
        G(6, "RULP", "Major RULP", RhGradeCategories.EncadrementApplication, 60, 0),
        G(7, "LTN", "Lieutenant", RhGradeCategories.Commandement, 70, 1),
        G(8, "CNE", "Capitaine", RhGradeCategories.Commandement, 80, 0),
        G(9, "CDT", "Commandant", RhGradeCategories.Commandement, 90, 1),
        G(10, "CDTD", "Commandant divisionnaire", RhGradeCategories.Commandement, 100, 0),
        G(11, "COM", "Commissaire", RhGradeCategories.ConceptionDirection, 110, 1),
        G(12, "CD", "Commissaire divisionnaire", RhGradeCategories.ConceptionDirection, 120, 0),
        G(13, "ADS", "Adjoint de sécurité", RhGradeCategories.Contractuels, 130, 0),
        G(14, "PA", "Policier adjoint", RhGradeCategories.Contractuels, 140, 1),
        G(15, "ELGPX", "Élève gardien de la paix", RhGradeCategories.Eleves, 150, 0),
        G(16, "RES", "Réserviste", RhGradeCategories.Reserve, 160, 0)
    ];

    private static readonly List<RhReferentielDto> Services =
    [
        new(1, RhReferentielTypes.Services, "CSP-CRETEIL", "CSP de Créteil", "CSP Créteil", null, null, null, 3, "TABOULOT Louis", 10, true, 13)
    ];

    private static readonly List<RhReferentielDto> Unites =
    [
        U(1, "CRETEIL-DIRECTION", "Direction", "DIR", 10, 2),
        U(2, "CRETEIL-BPS-BAC", "BPS / BAC", "BPS/BAC", 20, 5),
        U(3, "CRETEIL-CSI", "CSI", "CSI", 30, 2),
        U(4, "CRETEIL-BMU", "BMU", "BMU", 40, 1),
        U(5, "CRETEIL-ACCUEIL", "Accueil", "ACC", 50, 1),
        U(6, "CRETEIL-CIC", "CIC", "CIC", 60, 2)
    ];

    private static readonly List<RhReferentielDto> Fonctions =
    [
        F(1, "Chef de service", "CS", 10, 1), F(2, "Adjoint au chef de service", "ACS", 20, 1), F(3, "Chef d'unité", "CU", 30, 2),
        F(4, "Chef de bord", "CDB", 40, 2), F(5, "Équipier", "EQ", 50, 3), F(6, "Opérateur CIC", "OPE CIC", 60, 1),
        F(7, "TN", "TN", 70, 1), F(8, "Officier de quart", "OQ", 80, 0), F(9, "Enquêteur", "ENQ", 90, 1),
        F(10, "OPJ", "OPJ", 100, 0), F(11, "Agent d'accueil", "ACC", 110, 1), F(12, "Gestionnaire RH", "GRH", 120, 1)
    ];

    // ===== Agents =====

    private static readonly List<RhAgentLigneDto> Agents =
    [
        A(1, "DURAND", "Jean-Pierre", "1234567", 4, 1, 2, 4, AgentStatuts.Actif, -12, -2),
        A(2, "GIRARD", "Valentin", "2045871", 9, 1, 1, 2, AgentStatuts.Actif, -11, -1),
        A(3, "TABOULOT", "Louis", "3301458", 5, 1, 2, 3, AgentStatuts.Actif, -10, -3),
        A(4, "BORDINI", "Nolan", "4410256", 5, 1, 3, 3, AgentStatuts.Actif, -9, -2),
        A(5, "MARTIN", "Léa", null, null, null, null, null, AgentStatuts.ProfilACompleter, 0, 0),
        A(6, "NGUYEN", "Thomas", "5120447", 1, 1, 2, 5, AgentStatuts.Suspendu, -8, -1),
        A(7, "LEFEBVRE", "Camille", "6024713", 3, 1, 6, 6, AgentStatuts.Actif, -7, -4),
        A(8, "BENALI", "Sofiane", "6031198", 1, 1, 2, 5, AgentStatuts.Actif, -7, -5),
        A(9, "ROUSSEAU", "Inès", "6107725", 7, 1, 6, 7, AgentStatuts.Actif, -6, -2),
        A(10, "MOREAU", "Julien", "6215580", 2, 1, 4, 5, AgentStatuts.Actif, -5, -3),
        A(11, "PETIT", "Manon", "6302214", 14, 1, 5, 11, AgentStatuts.Actif, -4, -1),
        A(12, "LAMBERT", "Hugo", "6309971", 11, 1, 1, 1, AgentStatuts.Actif, -3, -1),
        A(13, "FONTAINE", "Chloé", "6411036", 1, 1, 3, 9, AgentStatuts.Actif, -2, -1),
        A(14, "GARNIER", "Paul", "5890014", 4, 1, 2, 4, AgentStatuts.Archive, -40, -15)
    ];

    // ===== Documents =====

    private static readonly List<RhDossierDocDto> Dossiers =
    [
        new(1, null, "Documents généraux", "Documents généraux", 1),
        new(2, null, "Personnel", "Personnel", 4),
        new(4, 2, "Nominations 2026", "Personnel \\ Nominations 2026", 1),
        new(3, null, "Service", "Service", 2)
    ];

    private static readonly List<RhCategorieDto> Categories =
        RhCategoriesDeDepart().Select((c, i) => new RhCategorieDto(i + 1, c.Code, c.Libelle, c.Groupe, (i + 1) * 10, true)).ToList();

    private static readonly List<RhDocumentDto> Documents =
    [
        D(1, "Attestation administrative — MARTIN Léa", "Attestation administrative", 7, 2, 5, RhDocumentStatuts.AValider, 1, "DOC-RH-000001 - Attestation administrative.pdf", 2140, -1),
        D(2, "Décision d'affectation — BENALI Sofiane", "Document d'affectation", 5, 2, 8, RhDocumentStatuts.Valide, 1, "DOC-RH-000002 - Décision d'affectation.pdf", 2311, -3),
        D(3, "Note de service — organisation des vacations de nuit", "Note de service", 8, 3, null, RhDocumentStatuts.Valide, 2, "note-vacations-nuit-v2.pdf", 184_220, -4),
        D(4, "Organigramme du CSP de Créteil", "Organigramme", 10, 3, null, RhDocumentStatuts.Brouillon, 1, "organigramme-csp-creteil.png", 412_870, -2),
        D(5, "Arrêté de nomination — ROUSSEAU Inès", "Document de nomination", 3, 4, 9, RhDocumentStatuts.AValider, 1, "DOC-RH-000005 - Arrêté de nomination.pdf", 2205, 0),
        D(6, "Fiche individuelle — TABOULOT Louis", "Fiche individuelle", 2, 2, 3, RhDocumentStatuts.Valide, 1, "DOC-RH-000006 - Fiche individuelle.pdf", 1980, -6),
        D(7, "Consignes accueil du public (2025)", "Consigne", 9, 3, null, RhDocumentStatuts.Archive, 3, "consignes-accueil-2025.docx", 48_112, -20)
    ];

    private static readonly List<RhModeleDto> Modeles = ModelesDeDepart();

    /// <summary>Réponse fictive à une requête du poste (null = route inconnue de ce module).</summary>
    public static object? Sample(string method, string path, string query, string? body)
    {
        var q = ParseQuery(query);
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (path == "api/referentiels/agents")
            return Agents.Where(a => a.Statut == AgentStatuts.Actif).Select(a => new AgentResumeDto(a.Id, a.Reference, a.Nom, a.Prenom, a.Rio,
                a.Grade, a.GradeAbrege, a.Service, a.Unite, a.Fonction, a.Statut)).ToList();
        if (segments is ["api", "referentiels", var typePublic] && RhReferentielTypes.Existe(typePublic))
            return Referentiel(typePublic).Select(e => new ReferentielDto(e.Id, e.Code, e.Libelle, e.Abrege, e.ParentId, e.Ordre, e.Actif)).ToList();

        if (segments.Length < 3 || segments[0] != "api" || segments[1] != "rh")
            return null;
        if (method != "GET")
            return segments[2..] is ["modeles", _, "generer"] ? (object)Generation() : Documents[0];

        return segments[2..] switch
        {
            ["tableau-de-bord"] => TableauBord(),
            ["parametres"] => new RhParametresDto(@"^\d{7}$", "1234567", "Commissariat central de Créteil", RhFichiers.TailleMax, RhFichiers.Extensions),
            ["agents"] => Agents
                .Where(a => !q.TryGetValue("statut", out var s) || a.Statut == s)
                .Where(a => !q.TryGetValue("serviceId", out var s) || a.ServiceId?.ToString() == s)
                .ToList(),
            ["agents", var id] => Dossier(long.Parse(id)),
            ["referentiels", var type] when RhReferentielTypes.Existe(type) => Referentiel(type),
            ["affectations"] => Affectations().Where(a => q.GetValueOrDefault("enCours") != "true" || a.EnCours).ToList(),
            ["rio", "historique"] => RioHistorique(),
            ["historique"] => Historique(),
            ["categories"] => Categories,
            ["dossiers"] => Dossiers,
            ["documents"] => Documents
                .Where(d => q.GetValueOrDefault("archives") == "true" ? d.Statut == RhDocumentStatuts.Archive : d.Statut != RhDocumentStatuts.Archive)
                .Where(d => !q.TryGetValue("statut", out var s) || d.Statut == s)
                .Where(d => !q.TryGetValue("agentId", out var s) || d.AgentId?.ToString() == s)
                .Where(d => !q.TryGetValue("dossierId", out var s) || d.DossierId?.ToString() == s)
                .ToList(),
            ["documents", var id] => DetailDocument(long.Parse(id)),
            ["documents", var id, "versions", _, "fichier"] => Fichier(long.Parse(id)),
            ["modeles"] => Modeles,
            _ => null
        };
    }

    /// <summary>Captures du module (utiliser Shots.Capture). Les fichiers sont numérotés à partir de 20.</summary>
    public static void Run()
    {
        var vm = new RhViewModel();
        Shots.Pump(1200);
        Shots.Capture(new RhView { DataContext = vm }, 1280, 820, "20-rh-tableau-de-bord.png");

        vm.AfficherPage(RhViewModel.Personnel);
        Shots.Pump();
        Shots.Capture(new RhView { DataContext = vm }, 1280, 820, "21-rh-gestion-personnel.png", () =>
        {
            if (vm.Page is RhPersonnelViewModel p)
                p.Selection = p.Agents.FirstOrDefault(a => a.Nom == "TABOULOT");
        });

        vm.OuvrirDossier(3);
        Shots.Pump();
        Shots.Capture(new RhView { DataContext = vm }, 1280, 820, "22-rh-dossier-identite.png");

        Shots.Capture(new RhView { DataContext = vm }, 1280, 820, "23-rh-dossier-affectation.png", () =>
        {
            if (vm.Page is RhDossierViewModel d)
                d.Onglet = 1;
        });

        vm.AfficherPage(RhReferentielTypes.Grades);
        Shots.Pump();
        Shots.Capture(new RhView { DataContext = vm }, 1280, 820, "24-rh-grades.png", () =>
        {
            if (vm.Page is RhReferentielViewModel r)
                r.Selection = r.Elements.FirstOrDefault(e => e.Code == "BC");
        });

        vm.AfficherPage(RhViewModel.Rio);
        Shots.Pump();
        Shots.Capture(new RhView { DataContext = vm }, 1280, 820, "25-rh-gestion-rio.png", () =>
        {
            if (vm.Page is RhRioViewModel r)
                r.Selection = r.Agents.FirstOrDefault(a => a.Nom == "MARTIN");
        });

        vm.AfficherPage(RhViewModel.Documents);
        Shots.Pump();
        Shots.Capture(new RhView { DataContext = vm }, 1280, 820, "26-rh-documents.png", () =>
        {
            if (vm.Page is RhDocumentsViewModel d)
                d.Selection = d.Lignes.FirstOrDefault(l => l.Id == 2);
        });

        vm.AfficherPage(RhViewModel.AValider);
        Shots.Pump();
        Shots.Capture(new RhView { DataContext = vm }, 1280, 820, "27-rh-documents-a-valider.png", () =>
        {
            if (vm.Page is RhDocumentsViewModel d)
                d.Selection = d.Lignes.FirstOrDefault();
        });

        vm.OuvrirModeles(8);
        Shots.Pump();
        Shots.Capture(new RhView { DataContext = vm }, 1280, 820, "28-rh-modeles.png", () =>
        {
            if (vm.Page is RhModelesViewModel m)
            {
                m.Selection = m.Modeles.FirstOrDefault(x => x.Type == "Attestation administrative");
                m.ApercuCommand.Execute(null);
            }
        });

        vm.AfficherPage(RhViewModel.Tableau);
        Shots.Pump();
        Shots.Capture(new RhView { DataContext = vm }, 390, 844, "29-rh-telephone.png");
    }

    // =====================================================================================
    // Données fictives
    // =====================================================================================

    private static RhReferentielDto G(long id, string code, string libelle, string categorie, int ordre, int effectif)
        => new(id, RhReferentielTypes.Grades, code, libelle, code, categorie, null, null, null, null, ordre, true, effectif);

    private static RhReferentielDto U(long id, string code, string libelle, string abrege, int ordre, int effectif)
        => new(id, RhReferentielTypes.Unites, code, libelle, abrege, null, 1, "CSP de Créteil", null, null, ordre, true, effectif);

    private static RhReferentielDto F(long id, string libelle, string abrege, int ordre, int effectif)
        => new(id, RhReferentielTypes.Fonctions, libelle.ToUpperInvariant().Replace(' ', '-').Replace('\'', '-'), libelle, abrege, null, null, null, null, null, ordre, true, effectif);

    private static List<RhReferentielDto> Referentiel(string type) => type switch
    {
        RhReferentielTypes.Grades => Grades,
        RhReferentielTypes.Services => Services,
        RhReferentielTypes.Unites => Unites,
        _ => Fonctions
    };

    private static RhAgentLigneDto A(long id, string nom, string prenom, string? rio, long? grade, long? service, long? unite, long? fonction,
        string statut, int creeJours, int majJours)
    {
        var g = Grades.FirstOrDefault(x => x.Id == grade);
        return new RhAgentLigneDto(id, $"AG-{id:D6}", $"30124577881234{id:D4}", nom, prenom, rio, $"94{id:D6}",
            grade, g?.Libelle, g?.Abrege, service, service is null ? null : "CSP de Créteil",
            unite, Unites.FirstOrDefault(x => x.Id == unite)?.Libelle, fonction, Fonctions.FirstOrDefault(x => x.Id == fonction)?.Libelle,
            statut, creeJours == 0 ? Now.AddHours(-5) : Now.AddDays(creeJours), majJours == 0 ? Now.AddHours(-5) : Now.AddDays(majJours));
    }

    private static RhDocumentDto D(long id, string titre, string type, long categorie, long dossier, long? agent, string statut, int version,
        string fichier, long taille, int jours)
    {
        var a = Agents.FirstOrDefault(x => x.Id == agent);
        var mime = RhFichiers.TypeMime(Path.GetExtension(fichier));
        var valide = statut == RhDocumentStatuts.Valide;
        return new RhDocumentDto(id, $"DOC-RH-{id:D6}", titre, type, categorie, Categories.FirstOrDefault(c => c.Id == categorie)?.Libelle,
            dossier, Dossiers.FirstOrDefault(d => d.Id == dossier)?.Nom, null, agent, a is null ? null : $"{a.Nom} {a.Prenom}",
            1, "CSP de Créteil", Aujourdhui.AddDays(jours), null, null,
            agent is null ? RhConfidentialites.Normal : RhConfidentialites.Restreint, statut, null, version, fichier, mime, taille,
            "DURAND Jean-Pierre (AG-000001)", Now.AddDays(jours - 1), Now.AddDays(jours),
            valide ? "GIRARD Valentin (AG-000002)" : null, valide ? Now.AddDays(jours) : null, fichier.StartsWith("DOC-RH", StringComparison.Ordinal) ? 5 : null);
    }

    private static IEnumerable<(string Code, string Libelle, string Groupe)> RhCategoriesDeDepart() =>
    [
        ("DOSSIERS-INDIVIDUELS", "Dossiers individuels", RhCategorieGroupes.Personnel),
        ("FICHES-ADMINISTRATIVES", "Fiches administratives", RhCategorieGroupes.Personnel),
        ("NOMINATIONS", "Nominations", RhCategorieGroupes.Personnel),
        ("CHANGEMENTS-DE-GRADE", "Changements de grade", RhCategorieGroupes.Personnel),
        ("CHANGEMENTS-D-AFFECTATION", "Changements d'affectation", RhCategorieGroupes.Personnel),
        ("DOCUMENTS-DE-FONCTION", "Documents de fonction", RhCategorieGroupes.Personnel),
        ("DOCUMENTS-ADMINISTRATIFS-DIVERS", "Documents administratifs divers", RhCategorieGroupes.Personnel),
        ("NOTES-DE-SERVICE", "Notes de service", RhCategorieGroupes.Service),
        ("CONSIGNES", "Consignes", RhCategorieGroupes.Service),
        ("ORGANIGRAMMES", "Organigrammes", RhCategorieGroupes.Service),
        ("FORMULAIRES", "Formulaires", RhCategorieGroupes.Service),
        ("DOCUMENTS-INTERNES", "Documents internes", RhCategorieGroupes.Service),
        ("MODELES", "Modèles", RhCategorieGroupes.Generaux),
        ("DOCUMENTS-PARTAGES", "Documents partagés", RhCategorieGroupes.Generaux),
        ("ARCHIVES", "Archives", RhCategorieGroupes.Generaux),
        ("RESSOURCES-ADMINISTRATIVES", "Ressources administratives", RhCategorieGroupes.Generaux)
    ];

    private static List<RhModeleDto> ModelesDeDepart()
    {
        const string Attestation = """
            ATTESTATION ADMINISTRATIVE N° {{numero_document}}

            Le service des ressources humaines du {{organisation}} atteste que :

            {{agent_grade}} {{agent_nom}} {{agent_prenom}}
            RIO : {{agent_rio}} — Matricule : {{agent_matricule}}

            est affecté(e) au {{agent_service}}, unité {{agent_unite}}, où il/elle exerce les fonctions de {{agent_fonction}}.

            Attestation délivrée pour servir et valoir ce que de droit.
            Fait à Créteil, le {{date_document}}.
            """;
        return RhModeleTypes.All.Select((type, i) => new RhModeleDto(i + 1, type.ToUpperInvariant().Replace(' ', '-').Replace('\'', '-'), type, type,
            7, "Documents administratifs divers",
            type == "Attestation administrative" ? Attestation : $"{type.ToUpperInvariant()} N° {{{{numero_document}}}}\n\n{{{{agent_grade}}}} {{{{agent_nom}}}} {{{{agent_prenom}}}} — RIO {{{{agent_rio}}}}\n\nFait à Créteil, le {{{{date_document}}}}.",
            true, Now.AddDays(-10))).ToList();
    }

    private static RhTableauBordDto TableauBord()
    {
        var actifs = Agents.Where(a => a.Statut != AgentStatuts.Archive).ToList();
        static List<RhCompteurDto> Compter(IEnumerable<string?> valeurs, string vide)
            => valeurs.GroupBy(v => v ?? vide).Select(g => new RhCompteurDto(g.Key, g.Count())).ToList();
        return new RhTableauBordDto(
            Agents.Count,
            Agents.Count(a => a.Statut == AgentStatuts.Actif),
            Agents.Count(a => a.Statut == AgentStatuts.Suspendu),
            0,
            Agents.Count(a => a.Statut == AgentStatuts.Archive),
            Agents.Count(a => a.Statut == AgentStatuts.ProfilACompleter),
            actifs.Count(a => a.Rio is null),
            Documents.Count(d => d.Statut == RhDocumentStatuts.AValider),
            AgentStatuts.All.Select(s => new RhCompteurDto(s, Agents.Count(a => a.Statut == s))).ToList(),
            Compter(actifs.OrderBy(a => Grades.FirstOrDefault(g => g.Id == a.GradeId)?.Ordre ?? 999).Select(a => a.Grade), "Sans grade"),
            Compter(actifs.Select(a => a.Service), "Sans service"),
            Compter(actifs.OrderBy(a => a.UniteId ?? 99).Select(a => a.Unite), "Sans unité"),
            Historique().Take(8).ToList(),
            Agents.OrderByDescending(a => a.CreeLe).Take(6).ToList());
    }

    private static List<HistoriqueDto> Historique() =>
    [
        new(40, Now.AddMinutes(-8), "DURAND Jean-Pierre (AG-000001)", "agent", "AG-000013", "Attribution du RIO", "rio", null, "6411036 (effet 02/11/2026)", "Attribution initiale"),
        new(39, Now.AddMinutes(-9), "DURAND Jean-Pierre (AG-000001)", "agent", "AG-000013", "Changement de statut", "statut", AgentStatuts.ProfilACompleter, AgentStatuts.Actif, "Profil complété par le service RH (RIO, grade, service et unité renseignés)"),
        new(38, Now.AddMinutes(-25), "DURAND Jean-Pierre (AG-000001)", "rh-document", "DOC-RH-000005", "Génération", null, null, "Arrêté de nomination — ROUSSEAU Inès, statut « À valider »", null),
        new(37, Now.AddHours(-1), "DURAND Jean-Pierre (AG-000001)", "agent", "AG-000009", "Changement de grade", "grade", "Brigadier-chef", "Lieutenant (effet 01/11/2026)", "Réussite à l'examen d'officier"),
        new(36, Now.AddHours(-2), "GIRARD Valentin (AG-000002)", "agent", "AG-000006", "Changement de statut", "statut", AgentStatuts.Actif, AgentStatuts.Suspendu, "Absence prolongée non justifiée"),
        new(35, Now.AddHours(-3), "DURAND Jean-Pierre (AG-000001)", "agent", "AG-000008", "Affectation", "affectation", "CSP de Créteil / BMU / Équipier", "CSP de Créteil / BPS / BAC / Équipier (effet 30/10/2026)", "Renfort de la BAC de nuit"),
        new(34, Now.AddDays(-1), "DURAND Jean-Pierre (AG-000001)", "agent", "AG-000004", "Modification de l'identité", "identite", "BORDINI Nolan", "BORDINI Nolan", "Correction de saisie"),
        new(33, Now.AddDays(-2), "DURAND Jean-Pierre (AG-000001)", "rh-grade", "RULP", "Modification", "libelle", "Major RULP", "Major RULP", null)
    ];

    private static List<RhAffectationDto> Affectations()
    {
        var liste = Agents.Where(a => a.ServiceId is not null).Select(a => new RhAffectationDto(a.Id, a.Id, $"{a.Nom} {a.Prenom}", a.ServiceId,
            a.Service, a.UniteId, a.Unite, a.FonctionId, a.Fonction, a.Id == 3 ? 2 : 3, a.Id == 3 ? "GIRARD Valentin" : "TABOULOT Louis",
            DateOnly.FromDateTime(a.CreeLe.Date), a.Statut == AgentStatuts.Archive ? Aujourdhui.AddDays(-15) : null,
            "Affectation initiale", null, "DURAND Jean-Pierre (AG-000001)", a.CreeLe)).ToList();
        liste.Add(new RhAffectationDto(100, 8, "BENALI Sofiane", 1, "CSP de Créteil", 4, "BMU", 5, "Équipier", 3, "TABOULOT Louis",
            Aujourdhui.AddDays(-7), Aujourdhui.AddDays(-3), "Affectation initiale", null, "DURAND Jean-Pierre (AG-000001)", Now.AddDays(-7)));
        return liste.OrderByDescending(a => a.Debut).ToList();
    }

    private static List<RhRioHistoriqueDto> RioHistorique() =>
    [
        new(9, 13, "FONTAINE Chloé", null, "6411036", Aujourdhui, "Attribution initiale", null, "DURAND Jean-Pierre (AG-000001)", Now.AddMinutes(-8)),
        new(8, 11, "PETIT Manon", "6301999", "6302214", Aujourdhui.AddDays(-1), "Erreur de saisie lors de l'attribution", null, "DURAND Jean-Pierre (AG-000001)", Now.AddDays(-1)),
        new(7, 12, "LAMBERT Hugo", null, "6309971", Aujourdhui.AddDays(-3), "Attribution initiale", null, "DURAND Jean-Pierre (AG-000001)", Now.AddDays(-3)),
        new(6, 9, "ROUSSEAU Inès", null, "6107725", Aujourdhui.AddDays(-6), "Attribution initiale", null, "GIRARD Valentin (AG-000002)", Now.AddDays(-6))
    ];

    private static RhDossierDto Dossier(long id)
    {
        var agent = Agents.FirstOrDefault(a => a.Id == id) ?? Agents[0];
        var affectations = Affectations().Where(a => a.AgentId == agent.Id).ToList();
        var carriere = agent.GradeId is null
            ? new List<RhCarriereDto>()
            :
            [
                new(2, agent.Id, $"{agent.Nom} {agent.Prenom}", "Brigadier-chef", agent.Grade!, Aujourdhui.AddDays(-30), "Promotion au choix (tableau d'avancement 2026)", null, 6, "DURAND Jean-Pierre (AG-000001)", Now.AddDays(-30)),
                new(1, agent.Id, $"{agent.Nom} {agent.Prenom}", null, "Brigadier-chef", DateOnly.FromDateTime(agent.CreeLe.Date), "Reprise du dossier", null, null, "DURAND Jean-Pierre (AG-000001)", agent.CreeLe)
            ];
        var rio = RioHistorique().Where(r => r.AgentId == agent.Id).ToList();
        if (agent.Rio is not null && rio.Count == 0)
            rio.Add(new RhRioHistoriqueDto(1, agent.Id, $"{agent.Nom} {agent.Prenom}", null, agent.Rio, DateOnly.FromDateTime(agent.CreeLe.Date),
                "Attribution initiale", null, "DURAND Jean-Pierre (AG-000001)", agent.CreeLe));
        var habilitations = new RhHabilitationsDto(
            ["Chef de bord", "Agent"],
            DefaultProfiles.All.First(p => p.Code == "chef-de-bord").Permissions.OrderBy(p => p, StringComparer.Ordinal).ToList(),
            ["CHEOPS", "PÉGASE", "MAIN COURANTE", "NEO", "PVe", "Tchap", "Annuaire", "Messagerie", "Notes"],
            [new(30, Now.AddDays(-9), "GIRARD Valentin (AG-000002)", "agent", agent.Reference, "Attribution des profils", "profils", "agent", "agent, chef-de-bord", "Prise de fonction de chef de bord")]);
        var historique = Historique().Where(h => h.Reference == agent.Reference).ToList();
        historique.Add(new HistoriqueDto(1, agent.CreeLe, $"{agent.Nom} {agent.Prenom} ({agent.Reference})", "agent", agent.Reference, "Création",
            null, null, $"{agent.Nom} {agent.Prenom}, matricule {agent.Matricule}, statut « Profil à compléter »", "Première connexion"));
        historique.AddRange(habilitations.Historique);
        return new RhDossierDto(agent, DateOnly.FromDateTime(agent.CreeLe.Date), Aujourdhui.AddDays(-30), Now.AddHours(-1),
            affectations.FirstOrDefault(a => a.EnCours), affectations, carriere, rio,
            [new(1, "TABOULOT", "Louis-Marie", "TABOULOT", "Louis", Aujourdhui.AddDays(-9), "Correction de l'état civil", null, "DURAND Jean-Pierre (AG-000001)", Now.AddDays(-9))],
            Documents.Where(d => d.AgentId == agent.Id).ToList(), habilitations, historique.OrderByDescending(h => h.Date).ToList());
    }

    private static RhDocumentDetailDto DetailDocument(long id)
    {
        var document = Documents.FirstOrDefault(d => d.Id == id) ?? Documents[0];
        var versions = Enumerable.Range(1, document.Version).Reverse().Select(n => new RhDocumentVersionDto(n, n, document.NomFichier ?? "document.pdf",
            document.TypeMime ?? "application/pdf", document.Taille - (document.Version - n) * 1200, "9f2c4e1ab07d53c8e6f1a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4d5e6f7",
            n == 1 ? "Version initiale" : "Mise à jour des horaires de la vacation de nuit",
            n == document.Version ? document.Statut : "Remplacée", document.Auteur, document.CreeLe.AddDays(n - 1),
            n == document.Version ? document.ValidePar : null, n == document.Version ? document.ValideLe : null,
            document.ModeleId is not null)).ToList();
        var historique = new List<HistoriqueDto>
        {
            new(3, document.MajLe, "GIRARD Valentin (AG-000002)", "rh-document", document.Reference, "Validation", "statut", RhDocumentStatuts.AValider, document.Statut, null),
            new(2, document.CreeLe.AddHours(1), "DURAND Jean-Pierre (AG-000001)", "rh-document", document.Reference, "Soumission à validation", "statut", RhDocumentStatuts.Brouillon, RhDocumentStatuts.AValider, null),
            new(1, document.CreeLe, "DURAND Jean-Pierre (AG-000001)", "rh-document", document.Reference, document.ModeleId is null ? "Création" : "Génération", null, null, document.Titre, null)
        };
        return new RhDocumentDetailDto(document, versions, historique);
    }

    private static RhGenerationDto Generation()
    {
        var modele = Modeles.First(m => m.Type == "Attestation administrative");
        var valeurs = new Dictionary<string, string>
        {
            ["numero_document"] = "(attribué à l'enregistrement)", ["organisation"] = "Commissariat central de Créteil",
            ["agent_grade"] = "Gardien de la paix", ["agent_nom"] = "BENALI", ["agent_prenom"] = "Sofiane", ["agent_rio"] = "6031198",
            ["agent_matricule"] = "94000008", ["agent_service"] = "CSP de Créteil", ["agent_unite"] = "BPS / BAC",
            ["agent_fonction"] = "Équipier", ["date_document"] = "02/11/2026"
        };
        var texte = valeurs.Aggregate(modele.Contenu, (t, v) => t.Replace("{{" + v.Key + "}}", v.Value, StringComparison.Ordinal));
        return new RhGenerationDto("Attestation administrative — BENALI Sofiane", texte, Array.Empty<string>(), null);
    }

    private static RhFichierDto Fichier(long id)
    {
        var document = Documents.FirstOrDefault(d => d.Id == id) ?? Documents[0];
        var texte = $"""
            DÉCISION D'AFFECTATION N° {document.Reference}

            Vu le dossier administratif de l'agent,

            Le Gardien de la paix BENALI Sofiane, RIO 6031198, matricule 94000008,
            est affecté(e) au CSP de Créteil, unité BPS / BAC, en qualité de Équipier.

            Responsable hiérarchique : TABOULOT Louis.

            Fait à Créteil, le 30/10/2026.
            Le service des ressources humaines.
            """;
        return new RhFichierDto(document.NomFichier ?? "document.pdf", document.TypeMime ?? "application/pdf", Encoding.UTF8.GetBytes("%PDF-1.4"), texte);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var resultat = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var partie in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var egal = partie.IndexOf('=');
            var cle = egal < 0 ? partie : partie[..egal];
            var valeur = egal < 0 ? string.Empty : Uri.UnescapeDataString(partie[(egal + 1)..]);
            if (valeur.Length > 0)
                resultat[cle] = valeur;
        }
        return resultat;
    }
}
