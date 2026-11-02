namespace Saphir.Shared.Contracts;

/// <summary>Permission interne SAPHIR : code stable, module, libellé affiché dans Administration.</summary>
public sealed record PermissionInfo(string Code, string Module, string Libelle);

/// <summary>
/// Catalogue des permissions internes. Le rôle Discord ouvre SAPHIR ; ces permissions, portées par les profils,
/// décident de chaque action, et le serveur les vérifie sur chaque appel d'API.
/// Les modules livrés plus tard (phases 3 à 8) ont déjà leurs codes, pour que les profils soient complets dès le départ.
/// </summary>
public static class Permissions
{
    // ----- Administration (phase 2) -----
    public const string AdminComptes = "admin.comptes";
    public const string AdminProfils = "admin.profils";
    public const string AdminParametres = "admin.parametres";
    public const string AdminJournaux = "admin.journaux";

    // ----- RH (phase 3) -----
    public const string RhConsulter = "rh.consulter";
    public const string RhAgentModifier = "rh.agent.modifier";
    public const string RhRioAttribuer = "rh.rio.attribuer";
    public const string RhCarriere = "rh.carriere";
    public const string RhReferentiels = "rh.referentiels";
    public const string RhStatut = "rh.statut";
    public const string RhDocuments = "rh.documents";
    public const string RhDocumentsValider = "rh.documents.valider";

    // ----- Main courante (phase 4) -----
    public const string McConsulter = "main-courante.consulter";
    public const string McMentionRediger = "main-courante.mention.rediger";
    public const string McMentionValider = "main-courante.mention.valider";
    public const string McVacationGerer = "main-courante.vacation.gerer";
    public const string McEquipageGerer = "main-courante.equipage.gerer";
    public const string McVehiculeGerer = "main-courante.vehicule.gerer";

    // ----- PÉGASE (phase 5) -----
    public const string PegaseConsulter = "pegase.consulter";
    public const string PegaseAppelCreer = "pegase.appel.creer";
    public const string PegaseInterventionCreer = "pegase.intervention.creer";
    public const string PegaseAffecter = "pegase.intervention.affecter";
    public const string PegaseStatut = "pegase.equipage.statut";
    public const string PegaseCompteRendu = "pegase.compte-rendu";
    public const string PegaseCloturer = "pegase.intervention.cloturer";
    public const string PegaseTn = "pegase.tn";

    // ----- CHEOPS (phase 6) -----
    public const string CheopsTaj = "cheops.taj";
    public const string CheopsFpr = "cheops.fpr";
    public const string CheopsFprInscrire = "cheops.fpr.inscrire";
    public const string CheopsVehicules = "cheops.vehicules";
    public const string CheopsSnpc = "cheops.snpc";
    public const string CheopsScientifique = "cheops.scientifique";
    public const string CheopsFijais = "cheops.fijais";
    public const string CheopsAnadoc = "cheops.anadoc";

    // ----- LRPPN (phase 7) -----
    public const string LrppnConsulter = "lrppn.consulter";
    public const string LrppnRediger = "lrppn.acte.rediger";
    public const string LrppnValider = "lrppn.acte.valider";
    public const string LrppnPieces = "lrppn.pieces";

    // ----- Applications complémentaires (phase 8) -----
    public const string NeoUtiliser = "neo.utiliser";
    public const string PveRediger = "pve.rediger";
    public const string AnnuaireConsulter = "annuaire.consulter";
    public const string MessagerieUtiliser = "messagerie.utiliser";
    public const string NotesUtiliser = "notes.utiliser";
    public const string MrzUtiliser = "mrz.utiliser";
    public const string BodycamConsulter = "bodycam.consulter";
    public const string BodycamGerer = "bodycam.gerer";
    public const string TchapUtiliser = "tchap.utiliser";

    public static readonly IReadOnlyList<PermissionInfo> All =
    [
        new(AdminComptes, "Administration", "Gérer les comptes et attribuer les profils"),
        new(AdminProfils, "Administration", "Créer et modifier les profils et leurs permissions"),
        new(AdminParametres, "Administration", "Modifier les paramètres (version, format du RIO…)"),
        new(AdminJournaux, "Administration", "Consulter l'historique et le journal des accès"),

        new(RhConsulter, "Ressources humaines", "Consulter le personnel et les dossiers"),
        new(RhAgentModifier, "Ressources humaines", "Modifier l'identité et la fiche d'un agent"),
        new(RhRioAttribuer, "Ressources humaines", "Attribuer et changer les RIO"),
        new(RhCarriere, "Ressources humaines", "Grades, affectations et fonctions"),
        new(RhReferentiels, "Ressources humaines", "Gérer grades, services, unités et fonctions"),
        new(RhStatut, "Ressources humaines", "Suspendre, réactiver ou archiver un agent"),
        new(RhDocuments, "Ressources humaines", "Gérer les documents RH"),
        new(RhDocumentsValider, "Ressources humaines", "Valider les documents RH"),

        new(McConsulter, "Main courante", "Consulter la main courante et le journal de service"),
        new(McMentionRediger, "Main courante", "Rédiger des mentions et déclarations"),
        new(McMentionValider, "Main courante", "Valider des mentions"),
        new(McVacationGerer, "Main courante", "Gérer les vacations"),
        new(McEquipageGerer, "Main courante", "Constituer les équipages"),
        new(McVehiculeGerer, "Main courante", "Gérer les véhicules de service, sorties et retours"),

        new(PegaseConsulter, "PÉGASE", "Consulter les appels et interventions"),
        new(PegaseAppelCreer, "PÉGASE", "Prendre un appel"),
        new(PegaseInterventionCreer, "PÉGASE", "Créer une intervention"),
        new(PegaseAffecter, "PÉGASE", "Affecter les équipages"),
        new(PegaseStatut, "PÉGASE", "Changer le statut d'un équipage"),
        new(PegaseCompteRendu, "PÉGASE", "Rédiger un compte rendu"),
        new(PegaseCloturer, "PÉGASE", "Clôturer une intervention"),
        new(PegaseTn, "PÉGASE", "Prendre la permanence TN"),

        new(CheopsTaj, "CHEOPS", "Consulter le TAJ"),
        new(CheopsFpr, "CHEOPS", "Consulter le FPR"),
        new(CheopsFprInscrire, "CHEOPS", "Inscrire et lever une fiche FPR"),
        new(CheopsVehicules, "CHEOPS", "Consulter FOVeS, SIV et FVA"),
        new(CheopsSnpc, "CHEOPS", "Consulter le SNPC"),
        new(CheopsScientifique, "CHEOPS", "Consulter FAED et FNAEG"),
        new(CheopsFijais, "CHEOPS", "Consulter FIJAISV et FIJAIT"),
        new(CheopsAnadoc, "CHEOPS", "Rechercher dans ANADOC"),

        new(LrppnConsulter, "LRPPN", "Consulter les procédures"),
        new(LrppnRediger, "LRPPN", "Ouvrir une procédure et rédiger des actes"),
        new(LrppnValider, "LRPPN", "Valider les actes"),
        new(LrppnPieces, "LRPPN", "Gérer les pièces et scellés"),

        new(NeoUtiliser, "Applications", "Utiliser NEO"),
        new(PveRediger, "Applications", "Rédiger des PVe"),
        new(AnnuaireConsulter, "Applications", "Consulter l'annuaire"),
        new(MessagerieUtiliser, "Applications", "Utiliser la messagerie"),
        new(NotesUtiliser, "Applications", "Utiliser les notes"),
        new(MrzUtiliser, "Applications", "Lire une bande MRZ"),
        new(BodycamConsulter, "Applications", "Consulter les vidéos bodycam"),
        new(BodycamGerer, "Applications", "Ajouter et gérer les vidéos bodycam"),
        new(TchapUtiliser, "Applications", "Accéder à Tchap")
    ];

    public static bool Exists(string code) => All.Any(p => p.Code == code);
}

/// <summary>Profil interne fourni à l'installation (modifiable ensuite dans Administration).</summary>
public sealed record DefaultProfile(string Code, string Nom, string Description, IReadOnlyList<string> Permissions);

/// <summary>Les 11 profils du cahier des charges, avec leurs permissions de départ.</summary>
public static class DefaultProfiles
{
    public const string Agent = "agent";
    public const string AdministrateurTechnique = "administrateur";

    private static readonly string[] Commun =
    [
        Permissions.AnnuaireConsulter, Permissions.MessagerieUtiliser, Permissions.NotesUtiliser, Permissions.TchapUtiliser
    ];

    private static string[] With(params string[] permissions) => [.. Commun, .. permissions];

    public static readonly IReadOnlyList<DefaultProfile> All =
    [
        new(Agent, "Agent", "Profil de base de tout policier",
            With(Permissions.NeoUtiliser, Permissions.PveRediger, Permissions.MrzUtiliser, Permissions.McConsulter,
                Permissions.PegaseConsulter, Permissions.PegaseStatut, Permissions.CheopsFpr, Permissions.CheopsVehicules)),
        new("chef-de-bord", "Chef de bord", "Responsable d'un équipage",
            With(Permissions.NeoUtiliser, Permissions.PveRediger, Permissions.McConsulter, Permissions.McMentionRediger,
                Permissions.PegaseConsulter, Permissions.PegaseStatut, Permissions.PegaseCompteRendu,
                Permissions.CheopsTaj, Permissions.CheopsFpr, Permissions.CheopsVehicules, Permissions.CheopsSnpc)),
        new("operateur-cic", "Opérateur CIC", "Prise des appels au CIC",
            With(Permissions.PegaseConsulter, Permissions.PegaseAppelCreer, Permissions.PegaseInterventionCreer,
                Permissions.CheopsFpr, Permissions.CheopsVehicules)),
        new("tn", "TN / Responsable CIC", "Engagement des moyens et suivi des interventions",
            With(Permissions.PegaseConsulter, Permissions.PegaseAppelCreer, Permissions.PegaseInterventionCreer,
                Permissions.PegaseAffecter, Permissions.PegaseStatut, Permissions.PegaseCloturer, Permissions.PegaseTn,
                Permissions.McConsulter, Permissions.CheopsFpr, Permissions.CheopsVehicules)),
        new("accueil", "Agent d'accueil", "Accueil du public et déclarations",
            With(Permissions.McConsulter, Permissions.McMentionRediger, Permissions.CheopsFpr)),
        new("enqueteur", "Enquêteur", "Procédures judiciaires",
            With(Permissions.LrppnConsulter, Permissions.LrppnRediger, Permissions.LrppnPieces, Permissions.McConsulter,
                Permissions.PegaseConsulter, Permissions.CheopsTaj, Permissions.CheopsFpr, Permissions.CheopsVehicules,
                Permissions.CheopsSnpc, Permissions.CheopsScientifique, Permissions.CheopsAnadoc, Permissions.BodycamConsulter)),
        new("opj", "OPJ", "Officier de police judiciaire",
            With(Permissions.LrppnConsulter, Permissions.LrppnRediger, Permissions.LrppnValider, Permissions.LrppnPieces,
                Permissions.McConsulter, Permissions.McMentionValider, Permissions.PegaseConsulter,
                Permissions.CheopsTaj, Permissions.CheopsFpr, Permissions.CheopsFprInscrire, Permissions.CheopsVehicules,
                Permissions.CheopsSnpc, Permissions.CheopsScientifique, Permissions.CheopsFijais, Permissions.CheopsAnadoc,
                Permissions.BodycamConsulter)),
        new("responsable-service", "Responsable de service", "Encadrement d'un service",
            With(Permissions.McConsulter, Permissions.McMentionValider, Permissions.McVacationGerer, Permissions.McEquipageGerer,
                Permissions.McVehiculeGerer, Permissions.PegaseConsulter, Permissions.PegaseCloturer, Permissions.LrppnConsulter,
                Permissions.LrppnValider, Permissions.BodycamConsulter, Permissions.BodycamGerer, Permissions.RhConsulter)),
        new("responsable-rh", "Responsable RH", "Gestion administrative du personnel (rôle Discord RH requis)",
            With(Permissions.RhConsulter, Permissions.RhAgentModifier, Permissions.RhRioAttribuer, Permissions.RhCarriere,
                Permissions.RhReferentiels, Permissions.RhStatut, Permissions.RhDocuments, Permissions.RhDocumentsValider)),
        new("documentaliste", "Gestionnaire documentaire", "Documents et modèles",
            With(Permissions.RhConsulter, Permissions.RhDocuments, Permissions.CheopsAnadoc)),
        new(AdministrateurTechnique, "Administrateur technique", "Toutes les permissions",
            Permissions.All.Select(p => p.Code).ToArray())
    ];
}
