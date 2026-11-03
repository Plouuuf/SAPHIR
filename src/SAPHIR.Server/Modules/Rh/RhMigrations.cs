using System.Text;
using Saphir.Server.Data;
using Saphir.Shared.Contracts.Rh;

namespace Saphir.Server.Modules.Rh;

/// <summary>
/// Schéma RH (migrations 100-199, tables rh_*). Rejouables (IF NOT EXISTS, INSERT IGNORE), jamais de DROP.
/// Les données de départ sont des constantes du code (aucune saisie utilisateur dans ce SQL).
/// </summary>
public static class RhMigrations
{
    private const string Table = "ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

    /// <summary>Calculé à la demande : les listes de données de départ ci-dessous sont initialisées après cette ligne.</summary>
    public static IReadOnlyList<Migration> All =>
    [
        new(100, "RH : référentiels, carrière, affectations, RIO, identités, documents, modèles", Schema()),
        new(101, "RH : grades de la Police nationale, CSP de Créteil, fonctions, catégories et modèles", DonneesDeDepart())
    ];

    private static List<string> Schema() =>
    [
        Referentiel("rh_grades", "categorie VARCHAR(100) NULL,", string.Empty),
        Referentiel("rh_services", "responsable_id BIGINT NULL,", string.Empty),
        Referentiel("rh_unites", "service_id BIGINT NULL,", "KEY ix_rh_unites_service (service_id),"),
        Referentiel("rh_fonctions", string.Empty, string.Empty),
        """
        ALTER TABLE rh_agents
            ADD COLUMN IF NOT EXISTS grade_id BIGINT NULL,
            ADD COLUMN IF NOT EXISTS service_id BIGINT NULL,
            ADD COLUMN IF NOT EXISTS unite_id BIGINT NULL,
            ADD COLUMN IF NOT EXISTS fonction_id BIGINT NULL,
            ADD COLUMN IF NOT EXISTS date_integration DATE NULL,
            ADD COLUMN IF NOT EXISTS grade_effet DATE NULL
        """,
        "ALTER TABLE rh_agents ADD INDEX IF NOT EXISTS ix_rh_agents_service (service_id, unite_id)",
        "ALTER TABLE rh_agents ADD INDEX IF NOT EXISTS ix_rh_agents_grade (grade_id)",
        $"""
        CREATE TABLE IF NOT EXISTS rh_affectations (
            id BIGINT NOT NULL AUTO_INCREMENT,
            agent_id BIGINT NOT NULL,
            service_id BIGINT NULL,
            unite_id BIGINT NULL,
            fonction_id BIGINT NULL,
            responsable_id BIGINT NULL,
            debut DATE NOT NULL,
            fin DATE NULL,
            motif VARCHAR(500) NULL,
            observations TEXT NULL,
            auteur_id BIGINT NULL,
            auteur VARCHAR(200) NOT NULL,
            cree_le DATETIME NOT NULL,
            PRIMARY KEY (id),
            KEY ix_rh_affectations_agent (agent_id, fin),
            KEY ix_rh_affectations_service (service_id, unite_id)
        ) {Table}
        """,
        $"""
        CREATE TABLE IF NOT EXISTS rh_carriere (
            id BIGINT NOT NULL AUTO_INCREMENT,
            agent_id BIGINT NOT NULL,
            ancien_grade_id BIGINT NULL,
            ancien_grade VARCHAR(100) NULL,
            grade_id BIGINT NOT NULL,
            grade VARCHAR(100) NOT NULL,
            effet DATE NOT NULL,
            motif VARCHAR(500) NULL,
            observations TEXT NULL,
            document_id BIGINT NULL,
            auteur_id BIGINT NULL,
            auteur VARCHAR(200) NOT NULL,
            cree_le DATETIME NOT NULL,
            PRIMARY KEY (id),
            KEY ix_rh_carriere_agent (agent_id, effet)
        ) {Table}
        """,
        $"""
        CREATE TABLE IF NOT EXISTS rh_rio_historique (
            id BIGINT NOT NULL AUTO_INCREMENT,
            agent_id BIGINT NOT NULL,
            ancien VARCHAR(32) NULL,
            nouveau VARCHAR(32) NOT NULL,
            effet DATE NOT NULL,
            motif VARCHAR(500) NULL,
            observations TEXT NULL,
            auteur_id BIGINT NULL,
            auteur VARCHAR(200) NOT NULL,
            cree_le DATETIME NOT NULL,
            PRIMARY KEY (id),
            KEY ix_rh_rio_historique_agent (agent_id),
            KEY ix_rh_rio_historique_nouveau (nouveau)
        ) {Table}
        """,
        $"""
        CREATE TABLE IF NOT EXISTS rh_identites (
            id BIGINT NOT NULL AUTO_INCREMENT,
            agent_id BIGINT NOT NULL,
            ancien_nom VARCHAR(60) NOT NULL,
            ancien_prenom VARCHAR(60) NOT NULL,
            nouveau_nom VARCHAR(60) NOT NULL,
            nouveau_prenom VARCHAR(60) NOT NULL,
            effet DATE NOT NULL,
            motif VARCHAR(500) NULL,
            observations TEXT NULL,
            auteur_id BIGINT NULL,
            auteur VARCHAR(200) NOT NULL,
            cree_le DATETIME NOT NULL,
            PRIMARY KEY (id),
            KEY ix_rh_identites_agent (agent_id)
        ) {Table}
        """,
        $"""
        CREATE TABLE IF NOT EXISTS rh_doc_categories (
            id BIGINT NOT NULL AUTO_INCREMENT,
            code VARCHAR(40) NOT NULL,
            libelle VARCHAR(100) NOT NULL,
            groupe VARCHAR(40) NOT NULL,
            ordre INT NOT NULL DEFAULT 0,
            actif TINYINT(1) NOT NULL DEFAULT 1,
            cree_le DATETIME NOT NULL,
            maj_le DATETIME NOT NULL,
            PRIMARY KEY (id),
            UNIQUE KEY ux_rh_doc_categories_code (code)
        ) {Table}
        """,
        $"""
        CREATE TABLE IF NOT EXISTS rh_doc_dossiers (
            id BIGINT NOT NULL AUTO_INCREMENT,
            parent_id BIGINT NULL,
            nom VARCHAR(120) NOT NULL,
            cree_le DATETIME NOT NULL,
            maj_le DATETIME NOT NULL,
            PRIMARY KEY (id),
            KEY ix_rh_doc_dossiers_parent (parent_id, nom)
        ) {Table}
        """,
        $"""
        CREATE TABLE IF NOT EXISTS rh_documents (
            id BIGINT NOT NULL AUTO_INCREMENT,
            reference VARCHAR(20) NULL,
            titre VARCHAR(200) NOT NULL,
            type VARCHAR(100) NULL,
            categorie_id BIGINT NULL,
            dossier_id BIGINT NULL,
            description TEXT NULL,
            agent_id BIGINT NULL,
            service_id BIGINT NULL,
            date_document DATE NULL,
            date_effet DATE NULL,
            date_expiration DATE NULL,
            confidentialite VARCHAR(40) NOT NULL,
            statut VARCHAR(30) NOT NULL,
            statut_avant_archive VARCHAR(30) NULL,
            observations TEXT NULL,
            version INT NOT NULL DEFAULT 1,
            modele_id BIGINT NULL,
            auteur_id BIGINT NULL,
            auteur VARCHAR(200) NOT NULL,
            cree_le DATETIME NOT NULL,
            maj_le DATETIME NOT NULL,
            valide_par VARCHAR(200) NULL,
            valide_le DATETIME NULL,
            PRIMARY KEY (id),
            UNIQUE KEY ux_rh_documents_reference (reference),
            KEY ix_rh_documents_statut (statut),
            KEY ix_rh_documents_agent (agent_id),
            KEY ix_rh_documents_dossier (dossier_id),
            KEY ix_rh_documents_categorie (categorie_id)
        ) {Table}
        """,
        $"""
        CREATE TABLE IF NOT EXISTS rh_document_versions (
            id BIGINT NOT NULL AUTO_INCREMENT,
            document_id BIGINT NOT NULL,
            numero INT NOT NULL,
            nom_fichier VARCHAR(255) NOT NULL,
            type_mime VARCHAR(120) NOT NULL,
            taille BIGINT NOT NULL,
            sha256 CHAR(64) NOT NULL,
            contenu MEDIUMBLOB NOT NULL,
            texte MEDIUMTEXT NULL,
            commentaire VARCHAR(500) NULL,
            statut VARCHAR(30) NOT NULL,
            auteur_id BIGINT NULL,
            auteur VARCHAR(200) NOT NULL,
            cree_le DATETIME NOT NULL,
            valide_par VARCHAR(200) NULL,
            valide_le DATETIME NULL,
            PRIMARY KEY (id),
            UNIQUE KEY ux_rh_document_versions_numero (document_id, numero)
        ) {Table}
        """,
        $"""
        CREATE TABLE IF NOT EXISTS rh_modeles (
            id BIGINT NOT NULL AUTO_INCREMENT,
            code VARCHAR(40) NOT NULL,
            libelle VARCHAR(150) NOT NULL,
            type VARCHAR(100) NOT NULL,
            categorie_id BIGINT NULL,
            contenu MEDIUMTEXT NOT NULL,
            actif TINYINT(1) NOT NULL DEFAULT 1,
            cree_le DATETIME NOT NULL,
            maj_le DATETIME NOT NULL,
            PRIMARY KEY (id),
            UNIQUE KEY ux_rh_modeles_code (code)
        ) {Table}
        """
    ];

    /// <summary>Table de référentiel : code stable unique, libellé, abrégé, ordre, actif.</summary>
    private static string Referentiel(string nom, string colonnes, string index)
        => $"""
            CREATE TABLE IF NOT EXISTS {nom} (
                id BIGINT NOT NULL AUTO_INCREMENT,
                code VARCHAR(40) NOT NULL,
                libelle VARCHAR(100) NOT NULL,
                abrege VARCHAR(20) NULL,
                {colonnes}
                ordre INT NOT NULL DEFAULT 0,
                actif TINYINT(1) NOT NULL DEFAULT 1,
                cree_le DATETIME NOT NULL,
                maj_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                {index}
                UNIQUE KEY ux_{nom}_code (code)
            ) {Table}
            """;

    // =====================================================================================
    // Données de départ
    // =====================================================================================

    /// <summary>Grades de la Police nationale (code = abrégé), dans l'ordre d'affichage.</summary>
    public static readonly IReadOnlyList<(string Abrege, string Libelle, string Categorie)> Grades =
    [
        ("GPX", "Gardien de la paix", RhGradeCategories.EncadrementApplication),
        ("SBC", "Sous-brigadier", RhGradeCategories.EncadrementApplication),
        ("BG", "Brigadier", RhGradeCategories.EncadrementApplication),
        ("BC", "Brigadier-chef", RhGradeCategories.EncadrementApplication),
        ("MJR", "Major", RhGradeCategories.EncadrementApplication),
        ("RULP", "Major RULP", RhGradeCategories.EncadrementApplication),
        ("LTN", "Lieutenant", RhGradeCategories.Commandement),
        ("CNE", "Capitaine", RhGradeCategories.Commandement),
        ("CDT", "Commandant", RhGradeCategories.Commandement),
        ("CDTD", "Commandant divisionnaire", RhGradeCategories.Commandement),
        ("COM", "Commissaire", RhGradeCategories.ConceptionDirection),
        ("CD", "Commissaire divisionnaire", RhGradeCategories.ConceptionDirection),
        ("ADS", "Adjoint de sécurité", RhGradeCategories.Contractuels),
        ("PA", "Policier adjoint", RhGradeCategories.Contractuels),
        ("ELGPX", "Élève gardien de la paix", RhGradeCategories.Eleves),
        ("RES", "Réserviste", RhGradeCategories.Reserve)
    ];

    public const string CodeServiceCreteil = "CSP-CRETEIL";

    public static readonly IReadOnlyList<(string Code, string Libelle, string Abrege)> UnitesCreteil =
    [
        ("CRETEIL-DIRECTION", "Direction", "DIR"),
        ("CRETEIL-BPS-BAC", "BPS / BAC", "BPS/BAC"),
        ("CRETEIL-CSI", "CSI", "CSI"),
        ("CRETEIL-BMU", "BMU", "BMU"),
        ("CRETEIL-ACCUEIL", "Accueil", "ACC"),
        ("CRETEIL-CIC", "CIC", "CIC")
    ];

    public static readonly IReadOnlyList<(string Libelle, string Abrege)> Fonctions =
    [
        ("Chef de service", "CS"),
        ("Adjoint au chef de service", "ACS"),
        ("Chef d'unité", "CU"),
        ("Chef de bord", "CDB"),
        ("Équipier", "EQ"),
        ("Opérateur CIC", "OPE CIC"),
        ("TN", "TN"),
        ("Officier de quart", "OQ"),
        ("Enquêteur", "ENQ"),
        ("OPJ", "OPJ"),
        ("Agent d'accueil", "ACC"),
        ("Gestionnaire RH", "GRH")
    ];

    public static readonly IReadOnlyList<(string Groupe, string Libelle)> Categories =
    [
        (RhCategorieGroupes.Personnel, "Dossiers individuels"),
        (RhCategorieGroupes.Personnel, "Fiches administratives"),
        (RhCategorieGroupes.Personnel, "Nominations"),
        (RhCategorieGroupes.Personnel, "Changements de grade"),
        (RhCategorieGroupes.Personnel, "Changements d'affectation"),
        (RhCategorieGroupes.Personnel, "Documents de fonction"),
        (RhCategorieGroupes.Personnel, "Documents administratifs divers"),
        (RhCategorieGroupes.Service, "Notes de service"),
        (RhCategorieGroupes.Service, "Consignes"),
        (RhCategorieGroupes.Service, "Organigrammes"),
        (RhCategorieGroupes.Service, "Formulaires"),
        (RhCategorieGroupes.Service, "Documents internes"),
        (RhCategorieGroupes.Generaux, "Modèles"),
        (RhCategorieGroupes.Generaux, "Documents partagés"),
        (RhCategorieGroupes.Generaux, "Archives"),
        (RhCategorieGroupes.Generaux, "Ressources administratives")
    ];

    /// <summary>Modèles RH de la section 5.6 : (type, catégorie, texte).</summary>
    public static readonly IReadOnlyList<(string Type, string Categorie, string Texte)> Modeles =
    [
        ("Fiche individuelle", "Fiches administratives", """
            FICHE INDIVIDUELLE DE RENSEIGNEMENTS

            Identifiant SAPHIR : {{agent_reference}}
            NOM : {{agent_nom}}
            Prénom : {{agent_prenom}}
            RIO : {{agent_rio}}
            Matricule : {{agent_matricule}}

            Grade : {{agent_grade}}
            Service : {{agent_service}}
            Unité : {{agent_unite}}
            Fonction : {{agent_fonction}}
            Responsable hiérarchique : {{responsable_nom}}

            Fiche établie le {{date_document}} par le service des ressources humaines.
            Document n° {{numero_document}}.
            """),
        ("Note de service", "Notes de service", """
            NOTE DE SERVICE N° {{numero_document}}

            Émetteur : {{organisation}}
            Destinataires : personnels du {{agent_service}}
            Date : {{date_document}}

            Objet :

            Texte de la note.

            Le chef de service,
            {{responsable_nom}}
            """),
        ("Document d'affectation", "Changements d'affectation", """
            DÉCISION D'AFFECTATION N° {{numero_document}}

            Vu le dossier administratif de l'agent,

            Le {{agent_grade}} {{agent_nom}} {{agent_prenom}}, RIO {{agent_rio}}, matricule {{agent_matricule}},
            est affecté(e) au {{agent_service}}, unité {{agent_unite}}, en qualité de {{agent_fonction}}.

            Responsable hiérarchique : {{responsable_nom}}.

            Fait à Créteil, le {{date_document}}.
            Le service des ressources humaines.
            """),
        ("Document de changement de grade", "Changements de grade", """
            DÉCISION DE CHANGEMENT DE GRADE N° {{numero_document}}

            L'agent {{agent_nom}} {{agent_prenom}}, RIO {{agent_rio}}, affecté(e) au {{agent_service}} ({{agent_unite}}),
            est promu(e) au grade de {{agent_grade}}.

            La présente décision prend effet à la date du {{date_document}}.

            Fait à Créteil, le {{date_document}}.
            Le service des ressources humaines.
            """),
        ("Attestation administrative", "Documents administratifs divers", """
            ATTESTATION ADMINISTRATIVE N° {{numero_document}}

            Le service des ressources humaines du {{organisation}} atteste que :

            {{agent_grade}} {{agent_nom}} {{agent_prenom}}
            RIO : {{agent_rio}} — Matricule : {{agent_matricule}}

            est affecté(e) au {{agent_service}}, unité {{agent_unite}}, où il/elle exerce les fonctions de {{agent_fonction}}.

            Attestation délivrée pour servir et valoir ce que de droit.
            Fait à Créteil, le {{date_document}}.
            """),
        ("Document de nomination", "Nominations", """
            ARRÊTÉ DE NOMINATION N° {{numero_document}}

            Le {{agent_grade}} {{agent_nom}} {{agent_prenom}}, RIO {{agent_rio}},
            est nommé(e) {{agent_fonction}} au sein du {{agent_service}} ({{agent_unite}}).

            Responsable hiérarchique : {{responsable_nom}}.

            Fait à Créteil, le {{date_document}}.
            """),
        ("Document de changement de fonction", "Documents de fonction", """
            DÉCISION DE CHANGEMENT DE FONCTION N° {{numero_document}}

            À compter du {{date_document}}, le {{agent_grade}} {{agent_nom}} {{agent_prenom}} (RIO {{agent_rio}})
            exerce la fonction de {{agent_fonction}} au sein de l'unité {{agent_unite}} du {{agent_service}}.

            Le service des ressources humaines.
            """),
        ("Formulaire administratif libre", "Formulaires", """
            FORMULAIRE ADMINISTRATIF N° {{numero_document}}

            Agent : {{agent_grade}} {{agent_nom}} {{agent_prenom}} — RIO {{agent_rio}}
            Service : {{agent_service}} — Unité : {{agent_unite}}
            Date : {{date_document}}

            Objet :

            Contenu :

            Signature de l'agent :                          Visa du responsable :
            """)
    ];

    private static List<string> DonneesDeDepart()
    {
        var requetes = new List<string>();
        const string Maintenant = "UTC_TIMESTAMP()";

        var grades = new StringBuilder("INSERT IGNORE INTO rh_grades (code, libelle, abrege, categorie, ordre, actif, cree_le, maj_le) VALUES ");
        grades.AppendJoin(", ", Grades.Select((g, i) =>
            $"({Lit(g.Abrege)}, {Lit(g.Libelle)}, {Lit(g.Abrege)}, {Lit(g.Categorie)}, {(i + 1) * 10}, 1, {Maintenant}, {Maintenant})"));
        requetes.Add(grades.ToString());

        requetes.Add(
            $"INSERT IGNORE INTO rh_services (code, libelle, abrege, ordre, actif, cree_le, maj_le) VALUES " +
            $"({Lit(CodeServiceCreteil)}, {Lit("CSP de Créteil")}, {Lit("CSP Créteil")}, 10, 1, {Maintenant}, {Maintenant})");

        var ordre = 10;
        foreach (var unite in UnitesCreteil)
        {
            requetes.Add(
                $"INSERT IGNORE INTO rh_unites (code, libelle, abrege, service_id, ordre, actif, cree_le, maj_le) " +
                $"SELECT {Lit(unite.Code)}, {Lit(unite.Libelle)}, {Lit(unite.Abrege)}, s.id, {ordre}, 1, {Maintenant}, {Maintenant} " +
                $"FROM rh_services s WHERE s.code = {Lit(CodeServiceCreteil)}");
            ordre += 10;
        }

        var fonctions = new StringBuilder("INSERT IGNORE INTO rh_fonctions (code, libelle, abrege, ordre, actif, cree_le, maj_le) VALUES ");
        fonctions.AppendJoin(", ", Fonctions.Select((f, i) =>
            $"({Lit(RhRegles.CodeReferentiel(f.Libelle))}, {Lit(f.Libelle)}, {Lit(f.Abrege)}, {(i + 1) * 10}, 1, {Maintenant}, {Maintenant})"));
        requetes.Add(fonctions.ToString());

        var categories = new StringBuilder("INSERT IGNORE INTO rh_doc_categories (code, libelle, groupe, ordre, actif, cree_le, maj_le) VALUES ");
        categories.AppendJoin(", ", Categories.Select((c, i) =>
            $"({Lit(RhRegles.CodeReferentiel(c.Libelle))}, {Lit(c.Libelle)}, {Lit(c.Groupe)}, {(i + 1) * 10}, 1, {Maintenant}, {Maintenant})"));
        requetes.Add(categories.ToString());

        foreach (var groupe in RhCategorieGroupes.All)
            requetes.Add(
                $"INSERT INTO rh_doc_dossiers (parent_id, nom, cree_le, maj_le) SELECT NULL, {Lit(groupe)}, {Maintenant}, {Maintenant} " +
                $"FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM rh_doc_dossiers WHERE parent_id IS NULL AND nom = {Lit(groupe)})");

        foreach (var modele in Modeles)
            requetes.Add(
                $"INSERT IGNORE INTO rh_modeles (code, libelle, type, categorie_id, contenu, actif, cree_le, maj_le) " +
                $"SELECT {Lit(RhRegles.CodeReferentiel(modele.Type))}, {Lit(modele.Type)}, {Lit(modele.Type)}, " +
                $"(SELECT c.id FROM rh_doc_categories c WHERE c.code = {Lit(RhRegles.CodeReferentiel(modele.Categorie))}), " +
                $"{Lit(modele.Texte)}, 1, {Maintenant}, {Maintenant} FROM DUAL");

        return requetes;
    }

    /// <summary>Littéral SQL d'une constante du code : apostrophes doublées, barres obliques inverses échappées.</summary>
    private static string Lit(string valeur)
        => "'" + valeur.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "''", StringComparison.Ordinal) + "'";
}
