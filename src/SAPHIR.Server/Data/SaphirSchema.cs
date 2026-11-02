namespace Saphir.Server.Data;

/// <summary>Tables de la base SAPHIR, créées au démarrage si elles n'existent pas.</summary>
public static class SaphirSchema
{
    public static readonly IReadOnlyList<string> Statements =
    [
        """
        CREATE TABLE IF NOT EXISTS saphir_personnes (
            id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            fivem_id VARCHAR(128) NULL,
            nom VARCHAR(100) NOT NULL,
            prenom VARCHAR(100) NOT NULL,
            date_naissance VARCHAR(20) NULL,
            lieu_naissance VARCHAR(120) NULL,
            sexe VARCHAR(10) NULL,
            nationalite VARCHAR(60) NULL,
            adresse VARCHAR(255) NULL,
            telephone VARCHAR(40) NULL,
            taille VARCHAR(20) NULL,
            signalement TEXT NULL,
            alias VARCHAR(255) NULL,
            cree_par VARCHAR(100) NOT NULL,
            cree_le DATETIME NOT NULL,
            maj_le DATETIME NOT NULL,
            UNIQUE KEY ux_personnes_fivem (fivem_id),
            KEY ix_personnes_nom (nom, prenom)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS saphir_taj (
            id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            personne_id BIGINT NOT NULL,
            qualite VARCHAR(40) NOT NULL,
            infraction VARCHAR(255) NOT NULL,
            natinf VARCHAR(20) NULL,
            date_faits VARCHAR(20) NOT NULL,
            lieu VARCHAR(255) NULL,
            service VARCHAR(120) NULL,
            procedure_ref VARCHAR(60) NULL,
            details TEXT NULL,
            auteur VARCHAR(100) NOT NULL,
            auteur_id VARCHAR(32) NOT NULL,
            cree_le DATETIME NOT NULL,
            KEY ix_taj_personne (personne_id),
            CONSTRAINT fk_taj_personne FOREIGN KEY (personne_id) REFERENCES saphir_personnes(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS saphir_fpr (
            id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            personne_id BIGINT NOT NULL,
            motif VARCHAR(80) NOT NULL,
            conduite VARCHAR(255) NOT NULL,
            autorite VARCHAR(160) NULL,
            details TEXT NULL,
            dangereux TINYINT(1) NOT NULL DEFAULT 0,
            arme TINYINT(1) NOT NULL DEFAULT 0,
            actif TINYINT(1) NOT NULL DEFAULT 1,
            auteur VARCHAR(100) NOT NULL,
            auteur_id VARCHAR(32) NOT NULL,
            cree_le DATETIME NOT NULL,
            clos_par VARCHAR(100) NULL,
            clos_le DATETIME NULL,
            KEY ix_fpr_personne (personne_id),
            CONSTRAINT fk_fpr_personne FOREIGN KEY (personne_id) REFERENCES saphir_personnes(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS saphir_permis (
            personne_id BIGINT NOT NULL PRIMARY KEY,
            numero VARCHAR(40) NULL,
            categories VARCHAR(60) NOT NULL,
            points INT NOT NULL DEFAULT 12,
            statut VARCHAR(40) NOT NULL,
            date_obtention VARCHAR(20) NULL,
            restrictions VARCHAR(255) NULL,
            maj_par VARCHAR(100) NOT NULL,
            maj_le DATETIME NOT NULL,
            CONSTRAINT fk_permis_personne FOREIGN KEY (personne_id) REFERENCES saphir_personnes(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS saphir_vehicules (
            plaque_norm VARCHAR(16) NOT NULL PRIMARY KEY,
            plaque VARCHAR(20) NOT NULL,
            marque VARCHAR(60) NULL,
            modele VARCHAR(60) NULL,
            couleur VARCHAR(40) NULL,
            vin VARCHAR(40) NULL,
            date_mec VARCHAR(20) NULL,
            titulaire_key VARCHAR(140) NULL,
            titulaire_nom VARCHAR(200) NULL,
            controle_technique VARCHAR(60) NULL,
            opposition VARCHAR(255) NULL,
            assureur VARCHAR(80) NULL,
            numero_contrat VARCHAR(60) NULL,
            assurance_jusquau VARCHAR(20) NULL,
            maj_par VARCHAR(100) NOT NULL,
            maj_le DATETIME NOT NULL,
            KEY ix_vehicules_titulaire (titulaire_key)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS saphir_foves (
            id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            plaque_norm VARCHAR(16) NOT NULL,
            motif VARCHAR(80) NOT NULL,
            conduite VARCHAR(255) NOT NULL,
            details TEXT NULL,
            actif TINYINT(1) NOT NULL DEFAULT 1,
            auteur VARCHAR(100) NOT NULL,
            auteur_id VARCHAR(32) NOT NULL,
            cree_le DATETIME NOT NULL,
            clos_par VARCHAR(100) NULL,
            clos_le DATETIME NULL,
            KEY ix_foves_plaque (plaque_norm)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS saphir_journal (
            id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            date_utc DATETIME NOT NULL,
            agent_id VARCHAR(32) NOT NULL,
            agent VARCHAR(100) NOT NULL,
            action VARCHAR(80) NOT NULL,
            cible VARCHAR(255) NOT NULL,
            KEY ix_journal_date (date_utc)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """
    ];
}
