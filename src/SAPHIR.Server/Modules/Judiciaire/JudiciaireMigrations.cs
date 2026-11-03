using Saphir.Server.Data;

namespace Saphir.Server.Modules.Judiciaire;

/// <summary>
/// Schéma du module Judiciaire (migrations 500-699) : personnes et véhicules (per_, veh_), CHEOPS (ch_), LRPPN (lr_).
/// Pas de clé étrangère vers un autre module : agents et interventions PÉGASE sont stockés par identifiant.
/// </summary>
public static class JudiciaireMigrations
{
    private const string T = "ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

    public static readonly IReadOnlyList<Migration> All =
    [
        new(500, "Judiciaire : personnes, alias, documents d'identité, véhicules et assurances",
        [
            $"""
            CREATE TABLE IF NOT EXISTS per_personnes (
                id BIGINT NOT NULL AUTO_INCREMENT,
                reference VARCHAR(16) NULL,
                fivem_id VARCHAR(128) NULL,
                nom VARCHAR(80) NOT NULL,
                prenom VARCHAR(80) NOT NULL,
                date_naissance DATE NULL,
                lieu_naissance VARCHAR(120) NULL,
                sexe VARCHAR(10) NULL,
                nationalite VARCHAR(60) NULL,
                adresse VARCHAR(255) NULL,
                telephone VARCHAR(40) NULL,
                signalement TEXT NULL,
                source VARCHAR(20) NOT NULL DEFAULT 'SAPHIR',
                cree_par VARCHAR(200) NOT NULL,
                cree_le DATETIME NOT NULL,
                maj_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                UNIQUE KEY ux_per_personnes_reference (reference),
                UNIQUE KEY ux_per_personnes_fivem (fivem_id),
                KEY ix_per_personnes_nom (nom, prenom),
                KEY ix_per_personnes_naissance (date_naissance)
            ) {T}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS per_alias (
                id BIGINT NOT NULL AUTO_INCREMENT,
                personne_id BIGINT NOT NULL,
                alias VARCHAR(120) NOT NULL,
                PRIMARY KEY (id),
                KEY ix_per_alias_alias (alias),
                KEY ix_per_alias_personne (personne_id),
                CONSTRAINT fk_per_alias_personne FOREIGN KEY (personne_id) REFERENCES per_personnes (id) ON DELETE CASCADE
            ) {T}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS per_documents (
                id BIGINT NOT NULL AUTO_INCREMENT,
                personne_id BIGINT NOT NULL,
                type VARCHAR(60) NOT NULL,
                numero VARCHAR(60) NOT NULL,
                delivre_le DATE NULL,
                expire_le DATE NULL,
                autorite VARCHAR(120) NULL,
                cree_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                KEY ix_per_documents_personne (personne_id),
                KEY ix_per_documents_numero (numero),
                CONSTRAINT fk_per_documents_personne FOREIGN KEY (personne_id) REFERENCES per_personnes (id) ON DELETE CASCADE
            ) {T}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS veh_vehicules (
                id BIGINT NOT NULL AUTO_INCREMENT,
                immatriculation VARCHAR(16) NOT NULL,
                affichage VARCHAR(20) NOT NULL,
                vin VARCHAR(40) NULL,
                marque VARCHAR(60) NULL,
                modele VARCHAR(60) NULL,
                version VARCHAR(80) NULL,
                categorie VARCHAR(30) NULL,
                couleur VARCHAR(40) NULL,
                mise_circulation DATE NULL,
                titulaire_id BIGINT NULL,
                titulaire_libre VARCHAR(200) NULL,
                adresse VARCHAR(255) NULL,
                situation VARCHAR(40) NOT NULL DEFAULT 'Normale',
                source VARCHAR(20) NOT NULL DEFAULT 'SAPHIR',
                cree_le DATETIME NOT NULL,
                maj_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                UNIQUE KEY ux_veh_vehicules_immat (immatriculation),
                KEY ix_veh_vehicules_vin (vin),
                KEY ix_veh_vehicules_titulaire (titulaire_id),
                CONSTRAINT fk_veh_vehicules_titulaire FOREIGN KEY (titulaire_id) REFERENCES per_personnes (id) ON DELETE SET NULL
            ) {T}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS veh_assurances (
                id BIGINT NOT NULL AUTO_INCREMENT,
                vehicule_id BIGINT NOT NULL,
                assureur VARCHAR(100) NOT NULL,
                contrat VARCHAR(60) NOT NULL,
                debut DATE NOT NULL,
                fin DATE NULL,
                statut VARCHAR(20) NOT NULL,
                verifie_le DATETIME NULL,
                cree_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                KEY ix_veh_assurances_vehicule (vehicule_id),
                CONSTRAINT fk_veh_assurances_vehicule FOREIGN KEY (vehicule_id) REFERENCES veh_vehicules (id) ON DELETE CASCADE
            ) {T}
            """
        ]),

        new(510, "CHEOPS : TAJ, FPR, FOVeS, SNPC, FAED/FNAEG, FIJAISV/FIJAIT, décisions, ANADOC",
        [
            $"""
            CREATE TABLE IF NOT EXISTS ch_taj (
                id BIGINT NOT NULL AUTO_INCREMENT,
                reference VARCHAR(20) NULL,
                personne_id BIGINT NOT NULL,
                qualite VARCHAR(30) NOT NULL,
                faits VARCHAR(255) NOT NULL,
                natinf VARCHAR(20) NULL,
                faits_le DATETIME NULL,
                lieu VARCHAR(255) NULL,
                service VARCHAR(120) NULL,
                procedure_numero VARCHAR(40) NULL,
                procedure_id BIGINT NULL,
                acte_id BIGINT NULL,
                suite VARCHAR(500) NULL,
                source VARCHAR(40) NOT NULL,
                cree_par VARCHAR(200) NOT NULL,
                cree_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                UNIQUE KEY ux_ch_taj_reference (reference),
                UNIQUE KEY ux_ch_taj_acte_personne (acte_id, personne_id),
                KEY ix_ch_taj_personne (personne_id),
                KEY ix_ch_taj_procedure (procedure_numero),
                CONSTRAINT fk_ch_taj_personne FOREIGN KEY (personne_id) REFERENCES per_personnes (id)
            ) {T}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS ch_fpr (
                id BIGINT NOT NULL AUTO_INCREMENT,
                numero VARCHAR(20) NULL,
                personne_id BIGINT NOT NULL,
                categorie VARCHAR(80) NOT NULL,
                motif VARCHAR(255) NOT NULL,
                conduite VARCHAR(500) NOT NULL,
                procedure_numero VARCHAR(40) NULL,
                service VARCHAR(120) NULL,
                reexamen_le DATE NULL,
                statut VARCHAR(20) NOT NULL,
                informations TEXT NULL,
                dangereux TINYINT(1) NOT NULL DEFAULT 0,
                arme TINYINT(1) NOT NULL DEFAULT 0,
                cree_par VARCHAR(200) NOT NULL,
                cree_le DATETIME NOT NULL,
                maj_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                UNIQUE KEY ux_ch_fpr_numero (numero),
                KEY ix_ch_fpr_personne (personne_id, statut),
                CONSTRAINT fk_ch_fpr_personne FOREIGN KEY (personne_id) REFERENCES per_personnes (id)
            ) {T}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS ch_foves (
                id BIGINT NOT NULL AUTO_INCREMENT,
                reference VARCHAR(20) NULL,
                type_objet VARCHAR(60) NOT NULL,
                identifiant VARCHAR(60) NOT NULL,
                identifiant_norm VARCHAR(60) NOT NULL,
                vehicule_id BIGINT NULL,
                description TEXT NULL,
                nature VARCHAR(60) NOT NULL,
                signale_le DATETIME NOT NULL,
                service VARCHAR(120) NULL,
                procedure_numero VARCHAR(40) NULL,
                statut VARCHAR(30) NOT NULL,
                decouvert_le DATETIME NULL,
                lieu_decouverte VARCHAR(255) NULL,
                conduite VARCHAR(500) NULL,
                cree_par VARCHAR(200) NOT NULL,
                cree_le DATETIME NOT NULL,
                maj_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                UNIQUE KEY ux_ch_foves_reference (reference),
                KEY ix_ch_foves_identifiant (identifiant_norm, statut),
                KEY ix_ch_foves_vehicule (vehicule_id),
                CONSTRAINT fk_ch_foves_vehicule FOREIGN KEY (vehicule_id) REFERENCES veh_vehicules (id) ON DELETE SET NULL
            ) {T}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS ch_permis (
                id BIGINT NOT NULL AUTO_INCREMENT,
                personne_id BIGINT NOT NULL,
                reference VARCHAR(30) NULL,
                categories VARCHAR(60) NOT NULL,
                dates_obtention VARCHAR(255) NULL,
                validite DATE NULL,
                situation VARCHAR(30) NOT NULL,
                points INT NOT NULL DEFAULT 12,
                cree_le DATETIME NOT NULL,
                maj_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                UNIQUE KEY ux_ch_permis_personne (personne_id),
                UNIQUE KEY ux_ch_permis_reference (reference),
                CONSTRAINT fk_ch_permis_personne FOREIGN KEY (personne_id) REFERENCES per_personnes (id)
            ) {T}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS ch_scientifique (
                id BIGINT NOT NULL AUTO_INCREMENT,
                fichier VARCHAR(10) NOT NULL,
                reference VARCHAR(20) NULL,
                reference_prelevement VARCHAR(60) NULL,
                origine VARCHAR(255) NOT NULL,
                procedure_numero VARCHAR(40) NULL,
                type VARCHAR(80) NOT NULL,
                preleve_le DATETIME NOT NULL,
                personne_id BIGINT NULL,
                resultat VARCHAR(40) NOT NULL,
                rapport TEXT NULL,
                cree_par VARCHAR(200) NOT NULL,
                cree_le DATETIME NOT NULL,
                maj_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                UNIQUE KEY ux_ch_scientifique_reference (reference),
                KEY ix_ch_scientifique_fichier (fichier, preleve_le),
                KEY ix_ch_scientifique_personne (personne_id),
                CONSTRAINT fk_ch_scientifique_personne FOREIGN KEY (personne_id) REFERENCES per_personnes (id) ON DELETE SET NULL
            ) {T}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS ch_fijais (
                id BIGINT NOT NULL AUTO_INCREMENT,
                fichier VARCHAR(10) NOT NULL,
                reference VARCHAR(20) NULL,
                personne_id BIGINT NOT NULL,
                reference_decision VARCHAR(60) NOT NULL,
                juridiction VARCHAR(160) NULL,
                infraction VARCHAR(255) NULL,
                decision_le DATE NULL,
                fin_inscription DATE NULL,
                statut VARCHAR(30) NOT NULL,
                service_gestionnaire VARCHAR(160) NULL,
                obligations TEXT NULL,
                restrictions TEXT NULL,
                cree_par VARCHAR(200) NOT NULL,
                cree_le DATETIME NOT NULL,
                maj_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                UNIQUE KEY ux_ch_fijais_reference (reference),
                KEY ix_ch_fijais_personne (personne_id, fichier),
                CONSTRAINT fk_ch_fijais_personne FOREIGN KEY (personne_id) REFERENCES per_personnes (id)
            ) {T}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS ch_decisions (
                id BIGINT NOT NULL AUTO_INCREMENT,
                reference VARCHAR(20) NULL,
                personne_id BIGINT NOT NULL,
                juridiction VARCHAR(160) NOT NULL,
                decision_le DATE NOT NULL,
                nature VARCHAR(80) NOT NULL,
                infraction VARCHAR(255) NOT NULL,
                peine VARCHAR(500) NULL,
                cree_par VARCHAR(200) NOT NULL,
                cree_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                UNIQUE KEY ux_ch_decisions_reference (reference),
                KEY ix_ch_decisions_personne (personne_id),
                CONSTRAINT fk_ch_decisions_personne FOREIGN KEY (personne_id) REFERENCES per_personnes (id)
            ) {T}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS ch_anadoc (
                id BIGINT NOT NULL AUTO_INCREMENT,
                source VARCHAR(20) NOT NULL,
                acte_id BIGINT NULL,
                piece_id BIGINT NULL,
                procedure_id BIGINT NOT NULL,
                procedure_numero VARCHAR(40) NOT NULL,
                numero_pv VARCHAR(60) NULL,
                type_acte VARCHAR(80) NOT NULL,
                titre VARCHAR(255) NOT NULL,
                `date` DATETIME NOT NULL,
                service VARCHAR(120) NULL,
                redacteur VARCHAR(200) NULL,
                personnes VARCHAR(1000) NULL,
                personnes_ids VARCHAR(500) NULL,
                texte MEDIUMTEXT NOT NULL,
                indexe_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                UNIQUE KEY ux_ch_anadoc_acte (acte_id),
                UNIQUE KEY ux_ch_anadoc_piece (piece_id),
                KEY ix_ch_anadoc_procedure (procedure_numero),
                KEY ix_ch_anadoc_date (`date`)
            ) {T}
            """
        ]),

        new(520, "LRPPN : procédures, personnes, véhicules, liens, actes, versions, mentions, pièces, mouvements, modèles",
        [
            $"""
            CREATE TABLE IF NOT EXISTS lr_procedures (
                id BIGINT NOT NULL AUTO_INCREMENT,
                numero VARCHAR(30) NULL,
                statut VARCHAR(30) NOT NULL,
                service VARCHAR(120) NULL,
                unite VARCHAR(120) NULL,
                responsable_id BIGINT NULL,
                responsable VARCHAR(200) NOT NULL,
                responsable_rio VARCHAR(32) NULL,
                responsable_grade VARCHAR(80) NULL,
                qualite_fonctionnelle VARCHAR(40) NULL,
                ouverte_le DATETIME NOT NULL,
                cadre VARCHAR(80) NOT NULL,
                type_affaire VARCHAR(80) NOT NULL,
                intitule VARCHAR(255) NOT NULL,
                intervention_id BIGINT NULL,
                intervention_numero VARCHAR(40) NULL,
                main_courante_ref VARCHAR(60) NULL,
                qualification VARCHAR(255) NULL,
                faits_le DATETIME NULL,
                commune VARCHAR(80) NULL,
                adresse VARCHAR(255) NULL,
                description TEXT NULL,
                origine VARCHAR(60) NULL,
                version INT NOT NULL DEFAULT 1,
                cree_par_id BIGINT NULL,
                cree_par VARCHAR(200) NOT NULL,
                cree_le DATETIME NOT NULL,
                maj_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                UNIQUE KEY ux_lr_procedures_numero (numero),
                KEY ix_lr_procedures_statut (statut, ouverte_le),
                KEY ix_lr_procedures_responsable (responsable_id),
                KEY ix_lr_procedures_intervention (intervention_id)
            ) {T}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS lr_procedure_personnes (
                id BIGINT NOT NULL AUTO_INCREMENT,
                procedure_id BIGINT NOT NULL,
                personne_id BIGINT NOT NULL,
                qualite VARCHAR(60) NOT NULL,
                observations VARCHAR(500) NULL,
                ajoute_par VARCHAR(200) NOT NULL,
                ajoute_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                UNIQUE KEY ux_lr_procedure_personnes (procedure_id, personne_id, qualite),
                KEY ix_lr_procedure_personnes_personne (personne_id),
                CONSTRAINT fk_lr_pp_procedure FOREIGN KEY (procedure_id) REFERENCES lr_procedures (id),
                CONSTRAINT fk_lr_pp_personne FOREIGN KEY (personne_id) REFERENCES per_personnes (id)
            ) {T}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS lr_procedure_vehicules (
                id BIGINT NOT NULL AUTO_INCREMENT,
                procedure_id BIGINT NOT NULL,
                vehicule_id BIGINT NOT NULL,
                role VARCHAR(60) NOT NULL,
                observations VARCHAR(500) NULL,
                ajoute_par VARCHAR(200) NOT NULL,
                ajoute_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                UNIQUE KEY ux_lr_procedure_vehicules (procedure_id, vehicule_id, role),
                KEY ix_lr_procedure_vehicules_vehicule (vehicule_id),
                CONSTRAINT fk_lr_pv_procedure FOREIGN KEY (procedure_id) REFERENCES lr_procedures (id),
                CONSTRAINT fk_lr_pv_vehicule FOREIGN KEY (vehicule_id) REFERENCES veh_vehicules (id)
            ) {T}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS lr_procedure_liens (
                id BIGINT NOT NULL AUTO_INCREMENT,
                procedure_id BIGINT NOT NULL,
                liee_id BIGINT NOT NULL,
                cree_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                UNIQUE KEY ux_lr_procedure_liens (procedure_id, liee_id),
                CONSTRAINT fk_lr_pl_procedure FOREIGN KEY (procedure_id) REFERENCES lr_procedures (id),
                CONSTRAINT fk_lr_pl_liee FOREIGN KEY (liee_id) REFERENCES lr_procedures (id)
            ) {T}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS lr_actes (
                id BIGINT NOT NULL AUTO_INCREMENT,
                procedure_id BIGINT NOT NULL,
                ordre INT NOT NULL,
                numero VARCHAR(40) NOT NULL,
                type VARCHAR(40) NOT NULL,
                titre VARCHAR(255) NOT NULL,
                statut VARCHAR(20) NOT NULL,
                personne_id BIGINT NULL,
                mis_en_cause_id BIGINT NULL,
                redacteur_id BIGINT NULL,
                redacteur VARCHAR(200) NOT NULL,
                redacteur_rio VARCHAR(32) NULL,
                redacteur_grade VARCHAR(80) NULL,
                redacteur_service VARCHAR(120) NULL,
                contenu MEDIUMTEXT NOT NULL,
                version INT NOT NULL DEFAULT 1,
                cree_le DATETIME NOT NULL,
                maj_le DATETIME NOT NULL,
                relecture_le DATETIME NULL,
                valide_le DATETIME NULL,
                valide_par_id BIGINT NULL,
                valide_par VARCHAR(200) NULL,
                empreinte VARCHAR(64) NULL,
                texte_fige MEDIUMTEXT NULL,
                pdf MEDIUMBLOB NULL,
                PRIMARY KEY (id),
                UNIQUE KEY ux_lr_actes_ordre (procedure_id, ordre),
                UNIQUE KEY ux_lr_actes_numero (numero),
                KEY ix_lr_actes_statut (statut, maj_le),
                KEY ix_lr_actes_redacteur (redacteur_id),
                KEY ix_lr_actes_personne (personne_id),
                CONSTRAINT fk_lr_actes_procedure FOREIGN KEY (procedure_id) REFERENCES lr_procedures (id),
                CONSTRAINT fk_lr_actes_personne FOREIGN KEY (personne_id) REFERENCES per_personnes (id),
                CONSTRAINT fk_lr_actes_mec FOREIGN KEY (mis_en_cause_id) REFERENCES per_personnes (id)
            ) {T}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS lr_actes_versions (
                id BIGINT NOT NULL AUTO_INCREMENT,
                acte_id BIGINT NOT NULL,
                version INT NOT NULL,
                statut VARCHAR(20) NOT NULL,
                contenu MEDIUMTEXT NOT NULL,
                auteur VARCHAR(200) NOT NULL,
                motif VARCHAR(500) NULL,
                enregistre_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                KEY ix_lr_actes_versions_acte (acte_id, version),
                CONSTRAINT fk_lr_av_acte FOREIGN KEY (acte_id) REFERENCES lr_actes (id)
            ) {T}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS lr_actes_mentions (
                id BIGINT NOT NULL AUTO_INCREMENT,
                acte_id BIGINT NOT NULL,
                texte TEXT NOT NULL,
                auteur_id BIGINT NULL,
                auteur VARCHAR(200) NOT NULL,
                cree_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                KEY ix_lr_actes_mentions_acte (acte_id),
                CONSTRAINT fk_lr_am_acte FOREIGN KEY (acte_id) REFERENCES lr_actes (id)
            ) {T}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS lr_pieces (
                id BIGINT NOT NULL AUTO_INCREMENT,
                procedure_id BIGINT NOT NULL,
                ordre INT NOT NULL,
                numero VARCHAR(40) NOT NULL,
                categorie VARCHAR(30) NOT NULL,
                libelle VARCHAR(255) NOT NULL,
                description TEXT NULL,
                numero_scelle VARCHAR(60) NULL,
                statut VARCHAR(40) NOT NULL,
                nom_fichier VARCHAR(255) NULL,
                type_mime VARCHAR(120) NULL,
                taille BIGINT NOT NULL DEFAULT 0,
                contenu MEDIUMBLOB NULL,
                empreinte VARCHAR(64) NULL,
                acte_id BIGINT NULL,
                localisation VARCHAR(160) NULL,
                cree_par VARCHAR(200) NOT NULL,
                cree_le DATETIME NOT NULL,
                maj_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                UNIQUE KEY ux_lr_pieces_ordre (procedure_id, ordre),
                UNIQUE KEY ux_lr_pieces_numero (numero),
                KEY ix_lr_pieces_categorie (categorie),
                CONSTRAINT fk_lr_pieces_procedure FOREIGN KEY (procedure_id) REFERENCES lr_procedures (id)
            ) {T}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS lr_pieces_mouvements (
                id BIGINT NOT NULL AUTO_INCREMENT,
                piece_id BIGINT NOT NULL,
                `date` DATETIME NOT NULL,
                mouvement VARCHAR(60) NOT NULL,
                destination VARCHAR(160) NULL,
                observations VARCHAR(500) NULL,
                auteur VARCHAR(200) NOT NULL,
                PRIMARY KEY (id),
                KEY ix_lr_pieces_mouvements_piece (piece_id, `date`),
                CONSTRAINT fk_lr_pm_piece FOREIGN KEY (piece_id) REFERENCES lr_pieces (id)
            ) {T}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS lr_modeles (
                type VARCHAR(40) NOT NULL,
                texte TEXT NOT NULL,
                version INT NOT NULL,
                maj_par VARCHAR(200) NOT NULL,
                maj_le DATETIME NOT NULL,
                PRIMARY KEY (type)
            ) {T}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS lr_modeles_versions (
                id BIGINT NOT NULL AUTO_INCREMENT,
                type VARCHAR(40) NOT NULL,
                version INT NOT NULL,
                texte TEXT NOT NULL,
                maj_par VARCHAR(200) NOT NULL,
                motif VARCHAR(500) NULL,
                maj_le DATETIME NOT NULL,
                PRIMARY KEY (id),
                KEY ix_lr_modeles_versions_type (type, version)
            ) {T}
            """
        ]),

        new(590, "Judiciaire : données fictives de départ (Créteil, Val-de-Marne)", JudiciaireDonneesFictives.Requetes)
    ];
}
