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
        """,
        """
        CREATE TABLE IF NOT EXISTS pegase_appels (
            id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            simcall_id VARCHAR(64) NULL,
            numero_requerant VARCHAR(40) NULL,
            operateur VARCHAR(100) NOT NULL,
            operateur_id VARCHAR(32) NOT NULL,
            decroche_le DATETIME NOT NULL,
            raccroche_le DATETIME NULL,
            statut VARCHAR(40) NOT NULL,
            issue VARCHAR(500) NULL,
            evenement_id BIGINT NULL,
            UNIQUE KEY ux_appels_simcall (simcall_id),
            KEY ix_appels_statut (statut, decroche_le)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS pegase_evenements (
            id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            nature VARCHAR(80) NOT NULL,
            priorite TINYINT NOT NULL DEFAULT 2,
            adresse VARCHAR(255) NOT NULL,
            commune VARCHAR(80) NULL,
            complement VARCHAR(255) NULL,
            requerant_nom VARCHAR(120) NULL,
            requerant_tel VARCHAR(40) NULL,
            description TEXT NULL,
            statut VARCHAR(20) NOT NULL,
            cree_par VARCHAR(100) NOT NULL,
            cree_le DATETIME NOT NULL,
            clos_par VARCHAR(100) NULL,
            clos_le DATETIME NULL,
            compte_rendu TEXT NULL,
            KEY ix_evenements_statut (statut, cree_le)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS pegase_messages (
            id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            evenement_id BIGINT NOT NULL,
            date_utc DATETIME NOT NULL,
            auteur VARCHAR(100) NOT NULL,
            type VARCHAR(20) NOT NULL,
            texte TEXT NOT NULL,
            KEY ix_messages_evenement (evenement_id),
            CONSTRAINT fk_messages_evenement FOREIGN KEY (evenement_id) REFERENCES pegase_evenements(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS pegase_equipages (
            id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            indicatif VARCHAR(40) NOT NULL,
            type VARCHAR(60) NOT NULL,
            effectif VARCHAR(255) NULL,
            statut VARCHAR(20) NOT NULL,
            evenement_id BIGINT NULL,
            maj_le DATETIME NOT NULL,
            UNIQUE KEY ux_equipages_indicatif (indicatif)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS pts_signalisations (
            id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            personne_id BIGINT NOT NULL,
            fichier VARCHAR(10) NOT NULL,
            reference VARCHAR(60) NOT NULL,
            date_signalisation VARCHAR(20) NOT NULL,
            service VARCHAR(120) NULL,
            motif VARCHAR(255) NULL,
            details TEXT NULL,
            auteur VARCHAR(100) NOT NULL,
            auteur_id VARCHAR(32) NOT NULL,
            cree_le DATETIME NOT NULL,
            KEY ix_signalisations_personne (personne_id),
            CONSTRAINT fk_signalisations_personne FOREIGN KEY (personne_id) REFERENCES saphir_personnes(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS pts_inscriptions (
            id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            personne_id BIGINT NOT NULL,
            fichier VARCHAR(10) NOT NULL,
            infraction VARCHAR(255) NOT NULL,
            decision VARCHAR(80) NOT NULL,
            date_decision VARCHAR(20) NULL,
            juridiction VARCHAR(160) NULL,
            obligations VARCHAR(255) NULL,
            fin_inscription VARCHAR(20) NULL,
            details TEXT NULL,
            auteur VARCHAR(100) NOT NULL,
            auteur_id VARCHAR(32) NOT NULL,
            cree_le DATETIME NOT NULL,
            KEY ix_inscriptions_personne (personne_id),
            CONSTRAINT fk_inscriptions_personne FOREIGN KEY (personne_id) REFERENCES saphir_personnes(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS pts_traces (
            id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            fichier VARCHAR(10) NOT NULL,
            reference VARCHAR(60) NOT NULL,
            affaire VARCHAR(160) NULL,
            lieu VARCHAR(255) NULL,
            date_prelevement VARCHAR(20) NULL,
            description TEXT NULL,
            personne_id BIGINT NULL,
            rapproche_par VARCHAR(100) NULL,
            rapproche_le DATETIME NULL,
            auteur VARCHAR(100) NOT NULL,
            auteur_id VARCHAR(32) NOT NULL,
            cree_le DATETIME NOT NULL,
            KEY ix_traces_personne (personne_id),
            CONSTRAINT fk_traces_personne FOREIGN KEY (personne_id) REFERENCES saphir_personnes(id) ON DELETE SET NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """
    ];
}
