using System.Globalization;
using System.Text;
using Saphir.Server.Data;

namespace Saphir.Server.Modules.Complements;

/// <summary>
/// Schéma des applications complémentaires (migrations 800-899, tables app_* et pve_*).
/// Pas de clé étrangère vers les autres modules : on stocke l'identifiant (agents, personnes, véhicules).
/// Les données de départ (services, infractions, salons) sont insérées une seule fois (INSERT IGNORE).
/// </summary>
public static class ComplementsMigrations
{
    private const string Table = "ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

    public static IReadOnlyList<Migration> All { get; } =
    [
        new(800, "Compléments : annuaire (services, coordonnées professionnelles)",
        [
            $"""
            CREATE TABLE IF NOT EXISTS app_annuaire_services (
                id BIGINT NOT NULL AUTO_INCREMENT,
                nom VARCHAR(120) NOT NULL,
                categorie VARCHAR(80) NULL,
                indicatif VARCHAR(40) NULL,
                telephone VARCHAR(40) NULL,
                frequence VARCHAR(40) NULL,
                adresse VARCHAR(250) NULL,
                horaires VARCHAR(120) NULL,
                observations VARCHAR(500) NULL,
                ordre INT NOT NULL DEFAULT 0,
                actif TINYINT(1) NOT NULL DEFAULT 1,
                cree_le DATETIME NOT NULL,
                maj_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                UNIQUE KEY ux_app_annuaire_services_nom (nom)
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS app_annuaire_coordonnees (
                agent_id BIGINT NOT NULL,
                telephone VARCHAR(40) NULL,
                indicatif VARCHAR(40) NULL,
                poste VARCHAR(80) NULL,
                email VARCHAR(120) NULL,
                maj_le DATETIME NOT NULL,
                PRIMARY KEY (agent_id)
            ) {Table}
            """
        ]),

        new(801, "Compléments : messagerie interne",
        [
            $"""
            CREATE TABLE IF NOT EXISTS app_messages (
                id BIGINT NOT NULL AUTO_INCREMENT,
                reference VARCHAR(20) NULL,
                conversation_id BIGINT NULL,
                reponse_a BIGINT NULL,
                expediteur_id BIGINT NOT NULL,
                expediteur VARCHAR(200) NOT NULL,
                sujet VARCHAR(200) NOT NULL,
                corps MEDIUMTEXT NOT NULL,
                liste_diffusion VARCHAR(200) NULL,
                liens TEXT NULL,
                important TINYINT(1) NOT NULL DEFAULT 0,
                brouillon TINYINT(1) NOT NULL DEFAULT 1,
                masque_expediteur TINYINT(1) NOT NULL DEFAULT 0,
                version INT NOT NULL DEFAULT 1,
                cree_le DATETIME NOT NULL,
                maj_le DATETIME NOT NULL,
                envoye_le DATETIME NULL,
                PRIMARY KEY (id),
                KEY ix_app_messages_expediteur (expediteur_id, brouillon, envoye_le),
                KEY ix_app_messages_conversation (conversation_id)
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS app_message_destinataires (
                message_id BIGINT NOT NULL,
                agent_id BIGINT NOT NULL,
                agent VARCHAR(200) NOT NULL,
                type VARCHAR(4) NOT NULL DEFAULT 'A',
                lu_le DATETIME NULL,
                non_lu TINYINT(1) NOT NULL DEFAULT 1,
                supprime TINYINT(1) NOT NULL DEFAULT 0,
                PRIMARY KEY (message_id, agent_id),
                KEY ix_app_message_destinataires_agent (agent_id, supprime, non_lu),
                CONSTRAINT fk_app_message_destinataires_message FOREIGN KEY (message_id)
                    REFERENCES app_messages (id) ON DELETE CASCADE
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS app_message_pieces (
                id BIGINT NOT NULL AUTO_INCREMENT,
                message_id BIGINT NOT NULL,
                nom VARCHAR(200) NOT NULL,
                type_mime VARCHAR(120) NOT NULL,
                taille BIGINT NOT NULL,
                sha256 CHAR(64) NOT NULL,
                contenu MEDIUMBLOB NOT NULL,
                cree_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                KEY ix_app_message_pieces_message (message_id),
                CONSTRAINT fk_app_message_pieces_message FOREIGN KEY (message_id)
                    REFERENCES app_messages (id) ON DELETE CASCADE
            ) {Table}
            """
        ]),

        new(802, "Compléments : notes personnelles et partagées",
        [
            $"""
            CREATE TABLE IF NOT EXISTS app_notes (
                id BIGINT NOT NULL AUTO_INCREMENT,
                auteur_id BIGINT NOT NULL,
                auteur VARCHAR(200) NOT NULL,
                titre VARCHAR(200) NOT NULL,
                contenu MEDIUMTEXT NOT NULL,
                categorie VARCHAR(60) NULL,
                epinglee TINYINT(1) NOT NULL DEFAULT 0,
                supprimee TINYINT(1) NOT NULL DEFAULT 0,
                version INT NOT NULL DEFAULT 1,
                cree_le DATETIME NOT NULL,
                maj_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                KEY ix_app_notes_auteur (auteur_id, supprimee, maj_le)
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS app_note_partages (
                note_id BIGINT NOT NULL,
                agent_id BIGINT NOT NULL,
                agent VARCHAR(200) NOT NULL,
                peut_modifier TINYINT(1) NOT NULL DEFAULT 0,
                partage_le DATETIME NOT NULL,
                PRIMARY KEY (note_id, agent_id),
                KEY ix_app_note_partages_agent (agent_id),
                CONSTRAINT fk_app_note_partages_note FOREIGN KEY (note_id)
                    REFERENCES app_notes (id) ON DELETE CASCADE
            ) {Table}
            """
        ]),

        new(803, "Compléments : lectures MRZ",
        [
            $"""
            CREATE TABLE IF NOT EXISTS app_mrz_lectures (
                id BIGINT NOT NULL AUTO_INCREMENT,
                reference VARCHAR(20) NULL,
                agent_id BIGINT NOT NULL,
                agent VARCHAR(200) NOT NULL,
                `date` DATETIME NOT NULL,
                format VARCHAR(60) NOT NULL,
                controles_ok TINYINT(1) NOT NULL,
                confirmation_manuelle TINYINT(1) NOT NULL,
                texte VARCHAR(200) NOT NULL,
                nom VARCHAR(80) NOT NULL,
                prenoms VARCHAR(120) NOT NULL,
                date_naissance DATE NULL,
                sexe VARCHAR(1) NULL,
                numero_document VARCHAR(40) NULL,
                type_document VARCHAR(10) NULL,
                nationalite VARCHAR(3) NULL,
                date_expiration DATE NULL,
                personne_id BIGINT NULL,
                personne_reference VARCHAR(30) NULL,
                personne_libelle VARCHAR(200) NULL,
                contexte VARCHAR(200) NULL,
                PRIMARY KEY (id),
                KEY ix_app_mrz_lectures_agent (agent_id, `date`),
                KEY ix_app_mrz_lectures_personne (personne_id)
            ) {Table}
            """
        ]),

        new(804, "Compléments : PVe (infractions, avis, justificatifs, mentions)",
        [
            $"""
            CREATE TABLE IF NOT EXISTS pve_infractions (
                id BIGINT NOT NULL AUTO_INCREMENT,
                natinf VARCHAR(10) NOT NULL,
                libelle VARCHAR(250) NOT NULL,
                texte_reference VARCHAR(200) NULL,
                categorie VARCHAR(60) NOT NULL,
                classe VARCHAR(5) NOT NULL,
                montant_forfaitaire DECIMAL(8,2) NOT NULL,
                montant_minore DECIMAL(8,2) NULL,
                montant_majore DECIMAL(8,2) NULL,
                points INT NOT NULL DEFAULT 0,
                actif TINYINT(1) NOT NULL DEFAULT 1,
                ordre INT NOT NULL DEFAULT 0,
                cree_le DATETIME NOT NULL,
                maj_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                UNIQUE KEY ux_pve_infractions_natinf (natinf)
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS pve_avis (
                id BIGINT NOT NULL AUTO_INCREMENT,
                reference VARCHAR(20) NULL,
                numero_avis VARCHAR(20) NULL,
                statut VARCHAR(20) NOT NULL,
                infraction_id BIGINT NOT NULL,
                natinf VARCHAR(10) NOT NULL,
                infraction VARCHAR(250) NOT NULL,
                texte_reference VARCHAR(200) NULL,
                classe VARCHAR(5) NOT NULL,
                montant_forfaitaire DECIMAL(8,2) NOT NULL,
                montant_minore DECIMAL(8,2) NULL,
                montant_majore DECIMAL(8,2) NULL,
                points INT NOT NULL DEFAULT 0,
                date_infraction DATETIME NOT NULL,
                lieu VARCHAR(250) NOT NULL,
                commune VARCHAR(120) NULL,
                personne_id BIGINT NULL,
                personne_reference VARCHAR(30) NULL,
                contrevenant_nom VARCHAR(60) NULL,
                contrevenant_prenom VARCHAR(80) NULL,
                contrevenant_naissance DATE NULL,
                contrevenant_lieu_naissance VARCHAR(120) NULL,
                contrevenant_adresse VARCHAR(250) NULL,
                vehicule_id BIGINT NULL,
                immatriculation VARCHAR(20) NULL,
                vehicule_marque VARCHAR(60) NULL,
                vehicule_modele VARCHAR(60) NULL,
                vehicule_couleur VARCHAR(40) NULL,
                circonstances TEXT NULL,
                observations TEXT NULL,
                intervention_numero VARCHAR(40) NULL,
                agent_id BIGINT NOT NULL,
                agent VARCHAR(200) NOT NULL,
                agent_rio VARCHAR(32) NULL,
                agent_grade VARCHAR(80) NULL,
                agent_service VARCHAR(120) NULL,
                agent_unite VARCHAR(120) NULL,
                motif_abandon VARCHAR(500) NULL,
                version INT NOT NULL DEFAULT 1,
                cree_le DATETIME NOT NULL,
                maj_le DATETIME NOT NULL,
                valide_le DATETIME NULL,
                PRIMARY KEY (id),
                UNIQUE KEY ux_pve_avis_numero (numero_avis),
                KEY ix_pve_avis_agent (agent_id, cree_le),
                KEY ix_pve_avis_statut (statut, date_infraction),
                KEY ix_pve_avis_immatriculation (immatriculation),
                KEY ix_pve_avis_personne (personne_id)
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS pve_justificatifs (
                id BIGINT NOT NULL AUTO_INCREMENT,
                avis_id BIGINT NOT NULL,
                nom VARCHAR(200) NOT NULL,
                type_mime VARCHAR(120) NOT NULL,
                taille BIGINT NOT NULL,
                sha256 CHAR(64) NOT NULL,
                contenu MEDIUMBLOB NOT NULL,
                auteur VARCHAR(200) NOT NULL,
                cree_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                KEY ix_pve_justificatifs_avis (avis_id),
                CONSTRAINT fk_pve_justificatifs_avis FOREIGN KEY (avis_id)
                    REFERENCES pve_avis (id) ON DELETE CASCADE
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS pve_mentions (
                id BIGINT NOT NULL AUTO_INCREMENT,
                avis_id BIGINT NOT NULL,
                `date` DATETIME NOT NULL,
                auteur_id BIGINT NOT NULL,
                auteur VARCHAR(200) NOT NULL,
                texte TEXT NOT NULL,
                PRIMARY KEY (id),
                KEY ix_pve_mentions_avis (avis_id),
                CONSTRAINT fk_pve_mentions_avis FOREIGN KEY (avis_id)
                    REFERENCES pve_avis (id) ON DELETE CASCADE
            ) {Table}
            """
        ]),

        new(805, "Compléments : registre bodycam",
        [
            $"""
            CREATE TABLE IF NOT EXISTS app_bodycam (
                id BIGINT NOT NULL AUTO_INCREMENT,
                numero VARCHAR(20) NULL,
                agent_id BIGINT NOT NULL,
                agent VARCHAR(200) NOT NULL,
                enregistre_le DATETIME NOT NULL,
                duree_secondes INT NOT NULL DEFAULT 0,
                intervention_numero VARCHAR(40) NULL,
                description TEXT NULL,
                provenance VARCHAR(120) NULL,
                source VARCHAR(10) NOT NULL,
                url VARCHAR(500) NULL,
                fichier_nom VARCHAR(200) NULL,
                fichier_chemin VARCHAR(200) NULL,
                fichier_taille BIGINT NULL,
                fichier_type VARCHAR(120) NULL,
                fichier_sha256 CHAR(64) NULL,
                fichier_recu BIGINT NOT NULL DEFAULT 0,
                statut VARCHAR(30) NOT NULL,
                motif_archivage VARCHAR(500) NULL,
                cree_par_id BIGINT NOT NULL,
                cree_par VARCHAR(200) NOT NULL,
                version INT NOT NULL DEFAULT 1,
                cree_le DATETIME NOT NULL,
                maj_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                KEY ix_app_bodycam_date (enregistre_le),
                KEY ix_app_bodycam_agent (agent_id),
                KEY ix_app_bodycam_intervention (intervention_numero)
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS app_bodycam_documents (
                id BIGINT NOT NULL AUTO_INCREMENT,
                enregistrement_id BIGINT NOT NULL,
                nom VARCHAR(200) NOT NULL,
                type_mime VARCHAR(120) NOT NULL,
                taille BIGINT NOT NULL,
                sha256 CHAR(64) NOT NULL,
                contenu MEDIUMBLOB NOT NULL,
                auteur VARCHAR(200) NOT NULL,
                cree_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                KEY ix_app_bodycam_documents_enr (enregistrement_id),
                CONSTRAINT fk_app_bodycam_documents_enr FOREIGN KEY (enregistrement_id)
                    REFERENCES app_bodycam (id) ON DELETE CASCADE
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS app_bodycam_acces (
                id BIGINT NOT NULL AUTO_INCREMENT,
                enregistrement_id BIGINT NOT NULL,
                `date` DATETIME NOT NULL,
                agent_id BIGINT NOT NULL,
                agent VARCHAR(200) NOT NULL,
                `action` VARCHAR(100) NOT NULL,
                PRIMARY KEY (id),
                KEY ix_app_bodycam_acces_enr (enregistrement_id, `date`)
            ) {Table}
            """
        ]),

        new(806, "Compléments : Tchap (salons, membres, messages)",
        [
            $"""
            CREATE TABLE IF NOT EXISTS app_tchap_salons (
                id BIGINT NOT NULL AUTO_INCREMENT,
                code VARCHAR(60) NULL,
                nom VARCHAR(100) NOT NULL,
                description VARCHAR(300) NULL,
                type VARCHAR(10) NOT NULL,
                archive TINYINT(1) NOT NULL DEFAULT 0,
                cree_par_id BIGINT NULL,
                cree_par VARCHAR(200) NOT NULL,
                cree_le DATETIME NOT NULL,
                maj_le DATETIME NOT NULL,
                dernier_message_id BIGINT NULL,
                dernier_message_le DATETIME NULL,
                PRIMARY KEY (id),
                UNIQUE KEY ux_app_tchap_salons_code (code)
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS app_tchap_membres (
                salon_id BIGINT NOT NULL,
                agent_id BIGINT NOT NULL,
                agent VARCHAR(200) NOT NULL,
                role VARCHAR(10) NOT NULL DEFAULT 'membre',
                ajoute_le DATETIME NOT NULL,
                PRIMARY KEY (salon_id, agent_id),
                KEY ix_app_tchap_membres_agent (agent_id),
                CONSTRAINT fk_app_tchap_membres_salon FOREIGN KEY (salon_id)
                    REFERENCES app_tchap_salons (id) ON DELETE CASCADE
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS app_tchap_lectures (
                salon_id BIGINT NOT NULL,
                agent_id BIGINT NOT NULL,
                dernier_lu_id BIGINT NOT NULL DEFAULT 0,
                PRIMARY KEY (salon_id, agent_id)
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS app_tchap_messages (
                id BIGINT NOT NULL AUTO_INCREMENT,
                salon_id BIGINT NOT NULL,
                auteur_id BIGINT NOT NULL,
                auteur VARCHAR(200) NOT NULL,
                texte TEXT NOT NULL,
                supprime TINYINT(1) NOT NULL DEFAULT 0,
                cree_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                KEY ix_app_tchap_messages_salon (salon_id, id),
                CONSTRAINT fk_app_tchap_messages_salon FOREIGN KEY (salon_id)
                    REFERENCES app_tchap_salons (id) ON DELETE CASCADE
            ) {Table}
            """
        ]),

        new(807, "Compléments : données de départ (annuaire des services, salons Tchap)", DonneesDeDepart()),

        new(808, "Compléments : référentiel des infractions PVe", Infractions())
    ];

    private static IReadOnlyList<string> DonneesDeDepart()
    {
        var requetes = new List<string>();
        var ordre = 0;
        foreach (var s in ComplementsDonnees.Services)
        {
            ordre += 10;
            requetes.Add(
                "INSERT IGNORE INTO app_annuaire_services (nom, categorie, indicatif, telephone, frequence, adresse, horaires, observations, ordre, actif, cree_le, maj_le) VALUES (" +
                string.Join(", ", Texte(s.Nom), Texte(s.Categorie), Texte(s.Indicatif), Texte(s.Telephone), Texte(s.Frequence),
                    Texte(s.Adresse), Texte(s.Horaires), Texte(s.Observations), ordre.ToString(CultureInfo.InvariantCulture), "1",
                    "UTC_TIMESTAMP()", "UTC_TIMESTAMP()") + ")");
        }
        foreach (var salon in ComplementsDonnees.Salons)
            requetes.Add(
                "INSERT IGNORE INTO app_tchap_salons (code, nom, description, type, archive, cree_par_id, cree_par, cree_le, maj_le) VALUES (" +
                string.Join(", ", Texte(salon.Code), Texte(salon.Nom), Texte(salon.Description), "'service'", "0", "NULL", "'SAPHIR'",
                    "UTC_TIMESTAMP()", "UTC_TIMESTAMP()") + ")");
        return requetes;
    }

    private static IReadOnlyList<string> Infractions()
    {
        var requetes = new List<string>();
        var ordre = 0;
        foreach (var i in ComplementsDonnees.Infractions)
        {
            ordre += 10;
            requetes.Add(
                "INSERT IGNORE INTO pve_infractions (natinf, libelle, texte_reference, categorie, classe, montant_forfaitaire, montant_minore, montant_majore, points, actif, ordre, cree_le, maj_le) VALUES (" +
                string.Join(", ", Texte(i.Natinf), Texte(i.Libelle), Texte(i.TexteReference), Texte(i.Categorie), Texte(i.Classe),
                    Montant(i.MontantForfaitaire), Montant(i.MontantMinore), Montant(i.MontantMajore),
                    i.Points.ToString(CultureInfo.InvariantCulture), "1", ordre.ToString(CultureInfo.InvariantCulture),
                    "UTC_TIMESTAMP()", "UTC_TIMESTAMP()") + ")");
        }
        return requetes;
    }

    /// <summary>Littéral SQL d'une donnée de départ constante (apostrophes doublées, barres obliques inverses refusées).</summary>
    public static string Texte(string? valeur)
    {
        if (valeur is null)
            return "NULL";
        if (valeur.Contains('\\', StringComparison.Ordinal))
            throw new InvalidOperationException("Donnée de départ invalide : " + valeur);
        var sb = new StringBuilder("'");
        sb.Append(valeur.Replace("'", "''", StringComparison.Ordinal));
        sb.Append('\'');
        return sb.ToString();
    }

    private static string Montant(decimal? valeur)
        => valeur is { } v ? v.ToString("0.00", CultureInfo.InvariantCulture) : "NULL";
}
