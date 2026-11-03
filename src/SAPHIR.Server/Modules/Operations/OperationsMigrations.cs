using Saphir.Server.Data;

namespace Saphir.Server.Modules.Operations;

/// <summary>
/// Schéma du module Opérations (plage 300-499) : MAIN COURANTE (mc_*), PÉGASE (pg_*), GÉOOPS (geo_*).
/// Requêtes rejouables, jamais de DROP ; pas de clé étrangère vers les autres modules (rh_agents, per_*, veh_* : id stocké).
/// </summary>
public static class OperationsMigrations
{
    private const string Table = "ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

    public static IReadOnlyList<Migration> All { get; } =
    [
        new(300, "Main courante : vacations, équipages, véhicules de service, mentions, journal de service",
        [
            $"""
            CREATE TABLE IF NOT EXISTS mc_vacations (
                id BIGINT NOT NULL AUTO_INCREMENT,
                reference VARCHAR(20) NULL,
                service VARCHAR(120) NOT NULL,
                debut DATETIME NOT NULL,
                fin_prevue DATETIME NULL,
                fin DATETIME NULL,
                responsable_id BIGINT NULL,
                responsable VARCHAR(200) NULL,
                statut VARCHAR(20) NOT NULL,
                consignes TEXT NULL,
                observations TEXT NULL,
                cree_par_id BIGINT NULL,
                cree_par VARCHAR(200) NOT NULL,
                cree_le DATETIME NOT NULL,
                maj_le DATETIME NOT NULL,
                version INT NOT NULL DEFAULT 1,
                PRIMARY KEY (id),
                UNIQUE KEY ux_mc_vacations_reference (reference),
                KEY ix_mc_vacations_statut (statut, debut),
                KEY ix_mc_vacations_debut (debut)
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS mc_vacation_agents (
                vacation_id BIGINT NOT NULL,
                agent_id BIGINT NOT NULL,
                libelle VARCHAR(200) NOT NULL,
                presence VARCHAR(20) NOT NULL,
                observation VARCHAR(300) NULL,
                PRIMARY KEY (vacation_id, agent_id),
                KEY ix_mc_vacation_agents_agent (agent_id),
                CONSTRAINT fk_mc_vacation_agents_vacation FOREIGN KEY (vacation_id) REFERENCES mc_vacations (id) ON DELETE CASCADE
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS mc_vehicules (
                id BIGINT NOT NULL AUTO_INCREMENT,
                reference VARCHAR(20) NULL,
                immatriculation VARCHAR(20) NOT NULL,
                marque VARCHAR(60) NULL,
                modele VARCHAR(60) NULL,
                type VARCHAR(40) NOT NULL,
                service VARCHAR(120) NULL,
                indicatif_habituel VARCHAR(20) NULL,
                kilometrage INT NOT NULL DEFAULT 0,
                etat VARCHAR(300) NULL,
                disponibilite VARCHAR(20) NOT NULL,
                observations TEXT NULL,
                equipage_id BIGINT NULL,
                cree_le DATETIME NOT NULL,
                maj_le DATETIME NOT NULL,
                version INT NOT NULL DEFAULT 1,
                PRIMARY KEY (id),
                UNIQUE KEY ux_mc_vehicules_reference (reference),
                UNIQUE KEY ux_mc_vehicules_immatriculation (immatriculation),
                KEY ix_mc_vehicules_disponibilite (disponibilite)
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS mc_equipages (
                id BIGINT NOT NULL AUTO_INCREMENT,
                vacation_id BIGINT NOT NULL,
                indicatif VARCHAR(20) NOT NULL,
                type VARCHAR(40) NOT NULL,
                service VARCHAR(120) NULL,
                vehicule_id BIGINT NULL,
                statut VARCHAR(40) NOT NULL,
                intervention_id BIGINT NULL,
                actif TINYINT(1) NOT NULL DEFAULT 1,
                cree_le DATETIME NOT NULL,
                maj_le DATETIME NOT NULL,
                dissous_le DATETIME NULL,
                PRIMARY KEY (id),
                KEY ix_mc_equipages_vacation (vacation_id),
                KEY ix_mc_equipages_actif (actif, statut),
                KEY ix_mc_equipages_intervention (intervention_id),
                CONSTRAINT fk_mc_equipages_vacation FOREIGN KEY (vacation_id) REFERENCES mc_vacations (id) ON DELETE RESTRICT
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS mc_equipage_membres (
                equipage_id BIGINT NOT NULL,
                agent_id BIGINT NOT NULL,
                libelle VARCHAR(200) NOT NULL,
                role VARCHAR(30) NOT NULL,
                PRIMARY KEY (equipage_id, agent_id),
                KEY ix_mc_equipage_membres_agent (agent_id),
                CONSTRAINT fk_mc_equipage_membres_equipage FOREIGN KEY (equipage_id) REFERENCES mc_equipages (id) ON DELETE CASCADE
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS mc_vehicule_mouvements (
                id BIGINT NOT NULL AUTO_INCREMENT,
                vehicule_id BIGINT NOT NULL,
                type VARCHAR(20) NOT NULL,
                `date` DATETIME NOT NULL,
                equipage_id BIGINT NULL,
                equipage VARCHAR(40) NULL,
                conducteur_id BIGINT NULL,
                conducteur VARCHAR(200) NULL,
                kilometrage INT NOT NULL,
                carburant VARCHAR(20) NULL,
                etat VARCHAR(300) NULL,
                anomalies TEXT NULL,
                observations TEXT NULL,
                disponibilite VARCHAR(20) NULL,
                responsable_id BIGINT NULL,
                responsable VARCHAR(200) NOT NULL,
                PRIMARY KEY (id),
                KEY ix_mc_vehicule_mouvements_vehicule (vehicule_id, `date`),
                CONSTRAINT fk_mc_vehicule_mouvements_vehicule FOREIGN KEY (vehicule_id) REFERENCES mc_vehicules (id) ON DELETE RESTRICT
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS mc_mentions (
                id BIGINT NOT NULL AUTO_INCREMENT,
                numero VARCHAR(20) NULL,
                type VARCHAR(30) NOT NULL,
                categorie VARCHAR(80) NULL,
                origine VARCHAR(60) NULL,
                statut VARCHAR(20) NOT NULL,
                `date` DATETIME NOT NULL,
                redacteur_id BIGINT NULL,
                redacteur VARCHAR(200) NOT NULL,
                redacteur_rio VARCHAR(32) NULL,
                redacteur_grade VARCHAR(80) NULL,
                redacteur_service VARCHAR(120) NULL,
                intervention_id BIGINT NULL,
                intervention_numero VARCHAR(20) NULL,
                mention_parent_id BIGINT NULL,
                declarant_qualite VARCHAR(40) NULL,
                declarant_personne_id BIGINT NULL,
                declarant_personne_reference VARCHAR(20) NULL,
                declarant_nom VARCHAR(60) NULL,
                declarant_prenom VARCHAR(60) NULL,
                declarant_naissance DATE NULL,
                declarant_lieu_naissance VARCHAR(120) NULL,
                declarant_adresse VARCHAR(300) NULL,
                declarant_telephone VARCHAR(30) NULL,
                declarant_email VARCHAR(120) NULL,
                autres_personnes TEXT NULL,
                faits_le DATETIME NULL,
                faits_lieu VARCHAR(300) NULL,
                nature VARCHAR(200) NULL,
                declaration MEDIUMTEXT NULL,
                personnes_citees TEXT NULL,
                vehicules TEXT NULL,
                objets TEXT NULL,
                documents TEXT NULL,
                observations TEXT NULL,
                mesures TEXT NULL,
                orientation VARCHAR(120) NULL,
                service_informe VARCHAR(120) NULL,
                procedure_lrppn VARCHAR(60) NULL,
                valide_par_id BIGINT NULL,
                valide_par VARCHAR(200) NULL,
                valide_le DATETIME NULL,
                cree_le DATETIME NOT NULL,
                maj_le DATETIME NOT NULL,
                version INT NOT NULL DEFAULT 1,
                PRIMARY KEY (id),
                UNIQUE KEY ux_mc_mentions_numero (numero),
                KEY ix_mc_mentions_date (`date`),
                KEY ix_mc_mentions_statut (statut, `date`),
                KEY ix_mc_mentions_intervention (intervention_id),
                KEY ix_mc_mentions_parent (mention_parent_id),
                KEY ix_mc_mentions_declarant (declarant_nom, declarant_prenom)
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS mc_journal (
                id BIGINT NOT NULL AUTO_INCREMENT,
                `date` DATETIME NOT NULL,
                type VARCHAR(40) NOT NULL,
                libelle VARCHAR(300) NOT NULL,
                details TEXT NULL,
                source VARCHAR(30) NOT NULL,
                auteur_id BIGINT NULL,
                auteur VARCHAR(200) NOT NULL,
                vacation_id BIGINT NULL,
                equipage_id BIGINT NULL,
                vehicule_id BIGINT NULL,
                intervention_id BIGINT NULL,
                intervention_numero VARCHAR(20) NULL,
                mention_id BIGINT NULL,
                PRIMARY KEY (id),
                KEY ix_mc_journal_date (`date`),
                KEY ix_mc_journal_vacation (vacation_id, `date`),
                KEY ix_mc_journal_intervention (intervention_id)
            ) {Table}
            """
        ]),

        new(301, "PÉGASE : appels, interventions, engagements, chronologie, comptes rendus, statuts, TN",
        [
            $"""
            CREATE TABLE IF NOT EXISTS pg_appels (
                id BIGINT NOT NULL AUTO_INCREMENT,
                numero VARCHAR(20) NULL,
                simcall_id VARCHAR(64) NULL,
                etat VARCHAR(40) NOT NULL,
                recu_le DATETIME NOT NULL,
                raccroche_le DATETIME NULL,
                operateur_id BIGINT NULL,
                operateur VARCHAR(200) NOT NULL,
                operateur_discord VARCHAR(32) NULL,
                service VARCHAR(120) NULL,
                canal VARCHAR(40) NULL,
                nature VARCHAR(60) NULL,
                motif VARCHAR(60) NULL,
                priorite VARCHAR(4) NULL,
                commune VARCHAR(100) NULL,
                adresse VARCHAR(300) NULL,
                telephone VARCHAR(40) NULL,
                requerant_json TEXT NULL,
                localisation_json TEXT NULL,
                motif_json TEXT NULL,
                securite_json TEXT NULL,
                engagement_json TEXT NULL,
                intervention_id BIGINT NULL,
                motif_cloture VARCHAR(500) NULL,
                cree_le DATETIME NOT NULL,
                maj_le DATETIME NOT NULL,
                version INT NOT NULL DEFAULT 1,
                PRIMARY KEY (id),
                UNIQUE KEY ux_pg_appels_numero (numero),
                UNIQUE KEY ux_pg_appels_simcall (simcall_id),
                KEY ix_pg_appels_etat (etat, recu_le),
                KEY ix_pg_appels_recu (recu_le),
                KEY ix_pg_appels_intervention (intervention_id)
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS pg_interventions (
                id BIGINT NOT NULL AUTO_INCREMENT,
                numero VARCHAR(20) NULL,
                nature VARCHAR(60) NOT NULL,
                sous_motif VARCHAR(120) NULL,
                priorite VARCHAR(4) NOT NULL,
                description TEXT NULL,
                adresse VARCHAR(300) NULL,
                commune VARCHAR(100) NULL,
                code_postal VARCHAR(10) NULL,
                x DOUBLE NULL,
                y DOUBLE NULL,
                fiabilite VARCHAR(40) NULL,
                requerant VARCHAR(200) NULL,
                securite_json TEXT NULL,
                statut VARCHAR(40) NOT NULL,
                debut_le DATETIME NOT NULL,
                cloture_le DATETIME NULL,
                motif_statut VARCHAR(500) NULL,
                equipages_demandes INT NULL,
                categorie_moyen VARCHAR(40) NULL,
                suite_judiciaire TINYINT(1) NOT NULL DEFAULT 0,
                procedure_lrppn VARCHAR(60) NULL,
                suites TEXT NULL,
                documents TEXT NULL,
                cree_par_id BIGINT NULL,
                cree_par VARCHAR(200) NOT NULL,
                cree_le DATETIME NOT NULL,
                maj_le DATETIME NOT NULL,
                version INT NOT NULL DEFAULT 1,
                PRIMARY KEY (id),
                UNIQUE KEY ux_pg_interventions_numero (numero),
                KEY ix_pg_interventions_statut (statut, debut_le),
                KEY ix_pg_interventions_debut (debut_le)
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS pg_personnes (
                id BIGINT NOT NULL AUTO_INCREMENT,
                appel_id BIGINT NOT NULL,
                intervention_id BIGINT NULL,
                ordre INT NOT NULL DEFAULT 0,
                role VARCHAR(40) NULL,
                identite_connue TINYINT(1) NOT NULL DEFAULT 0,
                personne_id BIGINT NULL,
                personne_reference VARCHAR(20) NULL,
                nom VARCHAR(60) NULL,
                prenom VARCHAR(60) NULL,
                description TEXT NULL,
                localisation VARCHAR(300) NULL,
                etat_apparent VARCHAR(200) NULL,
                informations TEXT NULL,
                source VARCHAR(120) NULL,
                PRIMARY KEY (id),
                KEY ix_pg_personnes_appel (appel_id),
                KEY ix_pg_personnes_intervention (intervention_id),
                KEY ix_pg_personnes_personne (personne_id),
                CONSTRAINT fk_pg_personnes_appel FOREIGN KEY (appel_id) REFERENCES pg_appels (id) ON DELETE CASCADE
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS pg_vehicules (
                id BIGINT NOT NULL AUTO_INCREMENT,
                appel_id BIGINT NOT NULL,
                intervention_id BIGINT NULL,
                ordre INT NOT NULL DEFAULT 0,
                vehicule_id BIGINT NULL,
                immatriculation VARCHAR(20) NULL,
                pays VARCHAR(40) NULL,
                marque VARCHAR(60) NULL,
                modele VARCHAR(60) NULL,
                couleur VARCHAR(40) NULL,
                categorie VARCHAR(40) NULL,
                description TEXT NULL,
                direction VARCHAR(200) NULL,
                lien_faits VARCHAR(200) NULL,
                PRIMARY KEY (id),
                KEY ix_pg_vehicules_appel (appel_id),
                KEY ix_pg_vehicules_intervention (intervention_id),
                KEY ix_pg_vehicules_immatriculation (immatriculation),
                CONSTRAINT fk_pg_vehicules_appel FOREIGN KEY (appel_id) REFERENCES pg_appels (id) ON DELETE CASCADE
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS pg_engagements (
                id BIGINT NOT NULL AUTO_INCREMENT,
                intervention_id BIGINT NOT NULL,
                equipage_id BIGINT NOT NULL,
                indicatif VARCHAR(20) NOT NULL,
                chef VARCHAR(200) NULL,
                agents VARCHAR(1000) NULL,
                vehicule VARCHAR(100) NULL,
                statut VARCHAR(40) NOT NULL,
                renfort TINYINT(1) NOT NULL DEFAULT 0,
                actif TINYINT(1) NOT NULL DEFAULT 1,
                demande_le DATETIME NOT NULL,
                affecte_le DATETIME NULL,
                affecte_par VARCHAR(200) NULL,
                en_route_le DATETIME NULL,
                sur_place_le DATETIME NULL,
                termine_le DATETIME NULL,
                maj_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                KEY ix_pg_engagements_intervention (intervention_id),
                KEY ix_pg_engagements_equipage (equipage_id, actif),
                CONSTRAINT fk_pg_engagements_intervention FOREIGN KEY (intervention_id) REFERENCES pg_interventions (id) ON DELETE RESTRICT
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS pg_chronologie (
                id BIGINT NOT NULL AUTO_INCREMENT,
                intervention_id BIGINT NOT NULL,
                `date` DATETIME NOT NULL,
                type VARCHAR(40) NOT NULL,
                texte TEXT NOT NULL,
                auteur_id BIGINT NULL,
                auteur VARCHAR(200) NOT NULL,
                equipage_id BIGINT NULL,
                PRIMARY KEY (id),
                KEY ix_pg_chronologie_intervention (intervention_id, `date`),
                KEY ix_pg_chronologie_date (`date`),
                CONSTRAINT fk_pg_chronologie_intervention FOREIGN KEY (intervention_id) REFERENCES pg_interventions (id) ON DELETE RESTRICT
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS pg_comptes_rendus (
                id BIGINT NOT NULL AUTO_INCREMENT,
                intervention_id BIGINT NOT NULL,
                equipage_id BIGINT NULL,
                indicatif VARCHAR(20) NULL,
                chef_de_bord VARCHAR(200) NULL,
                arrivee_le DATETIME NULL,
                depart_le DATETIME NULL,
                fin_le DATETIME NULL,
                resultat VARCHAR(80) NULL,
                faits MEDIUMTEXT NULL,
                mesures TEXT NULL,
                personnes_controlees TEXT NULL,
                interpellations TEXT NULL,
                victimes TEXT NULL,
                vehicules TEXT NULL,
                secours TEXT NULL,
                transport TEXT NULL,
                destination VARCHAR(200) NULL,
                documents TEXT NULL,
                suite_judiciaire TINYINT(1) NOT NULL DEFAULT 0,
                observations TEXT NULL,
                statut VARCHAR(20) NOT NULL,
                redacteur_id BIGINT NULL,
                redacteur VARCHAR(200) NOT NULL,
                valide_par_id BIGINT NULL,
                valide_par VARCHAR(200) NULL,
                valide_le DATETIME NULL,
                cree_le DATETIME NOT NULL,
                maj_le DATETIME NOT NULL,
                version INT NOT NULL DEFAULT 1,
                PRIMARY KEY (id),
                KEY ix_pg_comptes_rendus_intervention (intervention_id),
                CONSTRAINT fk_pg_comptes_rendus_intervention FOREIGN KEY (intervention_id) REFERENCES pg_interventions (id) ON DELETE RESTRICT
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS pg_statuts (
                id BIGINT NOT NULL AUTO_INCREMENT,
                objet VARCHAR(20) NOT NULL,
                objet_id BIGINT NOT NULL,
                intervention_id BIGINT NULL,
                ancien VARCHAR(40) NULL,
                nouveau VARCHAR(40) NOT NULL,
                motif VARCHAR(500) NULL,
                `date` DATETIME NOT NULL,
                auteur_id BIGINT NULL,
                auteur VARCHAR(200) NOT NULL,
                PRIMARY KEY (id),
                KEY ix_pg_statuts_objet (objet, objet_id, `date`),
                KEY ix_pg_statuts_intervention (intervention_id)
            ) {Table}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS pg_tn (
                id BIGINT NOT NULL AUTO_INCREMENT,
                agent_id BIGINT NOT NULL,
                agent VARCHAR(200) NOT NULL,
                service VARCHAR(120) NULL,
                debut DATETIME NOT NULL,
                fin DATETIME NULL,
                statut VARCHAR(20) NOT NULL,
                PRIMARY KEY (id),
                KEY ix_pg_tn_statut (statut, debut)
            ) {Table}
            """
        ]),

        new(302, "GÉOOPS : dernières positions connues (FiveM ou déclarées)",
        [
            $"""
            CREATE TABLE IF NOT EXISTS geo_positions (
                cle VARCHAR(64) NOT NULL,
                discord_id VARCHAR(32) NULL,
                server_id INT NULL,
                equipage_id BIGINT NULL,
                nom VARCHAR(120) NULL,
                x DOUBLE NOT NULL,
                y DOUBLE NOT NULL,
                z DOUBLE NOT NULL DEFAULT 0,
                cap DOUBLE NULL,
                vitesse DOUBLE NULL,
                vehicule VARCHAR(60) NULL,
                source VARCHAR(30) NOT NULL,
                auteur VARCHAR(200) NULL,
                maj_le DATETIME NOT NULL,
                PRIMARY KEY (cle),
                KEY ix_geo_positions_discord (discord_id),
                KEY ix_geo_positions_equipage (equipage_id),
                KEY ix_geo_positions_maj (maj_le)
            ) {Table}
            """
        ]),

        new(303, "Paramètres centraux PÉGASE (webhook Discord) et GÉOOPS (calibration de la carte)",
        [
            """
            INSERT IGNORE INTO sys_parametres (cle, valeur, libelle, aide, modifiable, maj_le)
            VALUES ('pegase.discord.webhook', 'désactivé', 'PÉGASE : webhook Discord des nouvelles interventions',
                    'URL du webhook du salon Discord (https://discord.com/api/webhooks/…). Toute autre valeur (ex. « désactivé ») coupe la notification. Le message ne contient ni nom ni adresse précise.',
                    1, UTC_TIMESTAMP())
            """,
            """
            INSERT IGNORE INTO sys_parametres (cle, valeur, libelle, aide, modifiable, maj_le)
            VALUES ('geoops.carte.x_min', '-4140', 'GÉOOPS : X du bord gauche de la carte',
                    'Coordonnée X du jeu (GTA) correspondant au bord gauche de l''image data/geoops/carte.jpg.', 1, UTC_TIMESTAMP())
            """,
            """
            INSERT IGNORE INTO sys_parametres (cle, valeur, libelle, aide, modifiable, maj_le)
            VALUES ('geoops.carte.x_max', '4860', 'GÉOOPS : X du bord droit de la carte',
                    'Coordonnée X du jeu (GTA) correspondant au bord droit de l''image.', 1, UTC_TIMESTAMP())
            """,
            """
            INSERT IGNORE INTO sys_parametres (cle, valeur, libelle, aide, modifiable, maj_le)
            VALUES ('geoops.carte.y_min', '-4300', 'GÉOOPS : Y du bord bas de la carte',
                    'Coordonnée Y du jeu (GTA) correspondant au bord bas de l''image.', 1, UTC_TIMESTAMP())
            """,
            """
            INSERT IGNORE INTO sys_parametres (cle, valeur, libelle, aide, modifiable, maj_le)
            VALUES ('geoops.carte.y_max', '8700', 'GÉOOPS : Y du bord haut de la carte',
                    'Coordonnée Y du jeu (GTA) correspondant au bord haut de l''image.', 1, UTC_TIMESTAMP())
            """,
            """
            INSERT IGNORE INTO sys_parametres (cle, valeur, libelle, aide, modifiable, maj_le)
            VALUES ('geoops.positions.peremption', '60', 'GÉOOPS : durée de validité d''une position (secondes)',
                    'Une position FiveM plus ancienne n''est plus affichée sur la carte.', 1, UTC_TIMESTAMP())
            """
        ])
    ];
}
