namespace Saphir.Server.Modules.Judiciaire;

/// <summary>
/// Jeu de données fictives de départ (migration 590) : quelques identités, véhicules et fiches du Val-de-Marne,
/// pour que CHEOPS et LRPPN soient utilisables dès l'installation. Toutes ces données sont inventées.
/// </summary>
public static class JudiciaireDonneesFictives
{
    private const string Auteur = "'SAPHIR (données fictives de départ)'";
    private const string Maintenant = "UTC_TIMESTAMP()";

    private static string P(string nom, string prenom) => $"(SELECT id FROM per_personnes WHERE nom = '{nom}' AND prenom = '{prenom}' LIMIT 1)";
    private static string V(string immat) => $"(SELECT id FROM veh_vehicules WHERE immatriculation = '{immat}' LIMIT 1)";

    public static readonly IReadOnlyList<string> Requetes =
    [
        $"""
        INSERT INTO per_personnes (nom, prenom, date_naissance, lieu_naissance, sexe, nationalite, adresse, telephone, signalement, source, cree_par, cree_le, maj_le) VALUES
        ('LEROY', 'Kévin', '1998-03-14', 'Créteil (94)', 'M', 'Française', '12 rue des Écoles, 94000 Créteil', '06 12 48 77 31', '1,80 m, corpulence mince, cheveux bruns courts, tatouage avant-bras droit', 'SAPHIR', {Auteur}, {Maintenant}, {Maintenant}),
        ('BENALI', 'Sofiane', '1991-07-02', 'Vitry-sur-Seine (94)', 'M', 'Française', '4 allée du Mont-Mesly, 94000 Créteil', '07 55 21 09 84', '1,75 m, barbe courte', 'SAPHIR', {Auteur}, {Maintenant}, {Maintenant}),
        ('MOREAU', 'Julie', '1985-11-23', 'Paris 12e (75)', 'F', 'Française', '27 avenue du Général-de-Gaulle, 94000 Créteil', '06 40 18 62 05', NULL, 'SAPHIR', {Auteur}, {Maintenant}, {Maintenant}),
        ('DIALLO', 'Moussa', '2001-05-09', 'Champigny-sur-Marne (94)', 'M', 'Française', '8 rue Henri-Barbusse, 94500 Champigny-sur-Marne', NULL, '1,88 m, corpulence athlétique', 'SAPHIR', {Auteur}, {Maintenant}, {Maintenant}),
        ('ROUSSEL', 'Anthony', '1979-01-30', 'Maisons-Alfort (94)', 'M', 'Française', '51 rue Jean-Jaurès, 94700 Maisons-Alfort', '06 71 03 44 90', NULL, 'SAPHIR', {Auteur}, {Maintenant}, {Maintenant}),
        ('FONTAINE', 'Léa', '2009-08-17', 'Créteil (94)', 'F', 'Française', '3 place de l''Abbaye, 94000 Créteil', NULL, '1,62 m, cheveux châtains longs, sac à dos rose', 'SAPHIR', {Auteur}, {Maintenant}, {Maintenant})
        """,
        "UPDATE per_personnes SET reference = CONCAT('PER-', LPAD(id, 6, '0')) WHERE reference IS NULL",
        $"INSERT INTO per_alias (personne_id, alias) VALUES ({P("LEROY", "Kévin")}, 'KEV'), ({P("BENALI", "Sofiane")}, 'SOSO')",
        $"""
        INSERT INTO per_documents (personne_id, type, numero, delivre_le, expire_le, autorite, cree_le) VALUES
        ({P("LEROY", "Kévin")}, 'Carte nationale d''identité', 'X4RT7ZK21', '2021-06-02', '2031-06-01', 'Préfecture du Val-de-Marne', {Maintenant}),
        ({P("MOREAU", "Julie")}, 'Passeport', '22CV91844', '2022-03-15', '2032-03-14', 'Préfecture de police', {Maintenant})
        """,

        $"""
        INSERT INTO veh_vehicules (immatriculation, affichage, vin, marque, modele, version, categorie, couleur, mise_circulation, titulaire_id, titulaire_libre, adresse, situation, source, cree_le, maj_le) VALUES
        ('GH482KL', 'GH-482-KL', 'VF1RJA00068543210', 'Renault', 'Clio V', 'TCe 90 Zen', 'VP', 'Gris', '2021-09-14', {P("MOREAU", "Julie")}, NULL, '27 avenue du Général-de-Gaulle, 94000 Créteil', 'Normale', 'SAPHIR', {Maintenant}, {Maintenant}),
        ('FT118ZB', 'FT-118-ZB', 'VF3LBHNZ6KS012345', 'Peugeot', '308', 'BlueHDi 130 Allure', 'VP', 'Noir', '2020-02-03', {P("BENALI", "Sofiane")}, NULL, '4 allée du Mont-Mesly, 94000 Créteil', 'Normale', 'SAPHIR', {Maintenant}, {Maintenant}),
        ('DX905QA', 'DX-905-QA', 'WVWZZZAUZFW098765', 'Volkswagen', 'Golf VII', '1.4 TSI Carat', 'VP', 'Blanc', '2015-11-20', {P("ROUSSEL", "Anthony")}, NULL, '51 rue Jean-Jaurès, 94700 Maisons-Alfort', 'Véhicule volé', 'SAPHIR', {Maintenant}, {Maintenant}),
        ('EK327MN', 'EK-327-MN', 'JYARM321000012345', 'Yamaha', 'MT-07', '689 cm³', 'Moto', 'Noir', '2017-04-28', {P("LEROY", "Kévin")}, NULL, '12 rue des Écoles, 94000 Créteil', 'Normale', 'SAPHIR', {Maintenant}, {Maintenant}),
        ('CB761TR', 'CB-761-TR', 'VF7SXHMZ6BT054321', 'Citroën', 'C3', '1.2 PureTech', 'VU', 'Rouge', '2012-05-11', NULL, 'SARL Transports du Mont-Mesly', 'ZA des Petites-Haies, 94000 Créteil', 'Gage', 'SAPHIR', {Maintenant}, {Maintenant})
        """,
        $"""
        INSERT INTO veh_assurances (vehicule_id, assureur, contrat, debut, fin, statut, verifie_le, cree_le) VALUES
        ({V("GH482KL")}, 'Mutuelle fictive du Val-de-Marne', 'MFV-2025-118204', '2025-09-14', '2026-12-31', 'Valide', NULL, {Maintenant}),
        ({V("FT118ZB")}, 'Assurances Marne & Seine (fictif)', 'AMS-883017', '2025-07-01', '2026-06-30', 'Valide', NULL, {Maintenant}),
        ({V("EK327MN")}, 'Moto Garantie Île-de-France (fictif)', 'MGI-40213', '2026-04-28', '2027-04-27', 'Valide', NULL, {Maintenant}),
        ({V("DX905QA")}, 'Mutuelle fictive du Val-de-Marne', 'MFV-2019-004512', '2019-01-01', NULL, 'Valide', NULL, {Maintenant})
        """,

        $"""
        INSERT INTO ch_taj (personne_id, qualite, faits, natinf, faits_le, lieu, service, procedure_numero, suite, source, cree_par, cree_le) VALUES
        ({P("LEROY", "Kévin")}, 'Mis en cause', 'Vol avec violence (vol à l''arraché)', '7151', '2025-10-04 19:30:00', 'Rue du Général-Leclerc, Créteil', 'CSP de Créteil — BSU', '2025/000412', 'Transmis au parquet de Créteil', 'Saisie initiale', {Auteur}, {Maintenant}),
        ({P("LEROY", "Kévin")}, 'Mis en cause', 'Conduite d''un véhicule sans assurance', '20339', '2024-06-11 22:10:00', 'Avenue Pierre-Brossolette, Créteil', 'CSP de Créteil — BAC', '2024/001877', NULL, 'Saisie initiale', {Auteur}, {Maintenant}),
        ({P("BENALI", "Sofiane")}, 'Mis en cause', 'Usage illicite de stupéfiants', '4434', '2026-02-18 23:45:00', 'Mont-Mesly, Créteil', 'CSP de Créteil — BAC', '2026/000093', 'Amende forfaitaire délictuelle', 'Saisie initiale', {Auteur}, {Maintenant}),
        ({P("MOREAU", "Julie")}, 'Victime', 'Cambriolage d''un local d''habitation', '7166', '2026-09-02 14:00:00', '27 avenue du Général-de-Gaulle, Créteil', 'CSP de Créteil — BSU', '2026/000381', NULL, 'Saisie initiale', {Auteur}, {Maintenant})
        """,
        "UPDATE ch_taj SET reference = CONCAT('TAJ-', LPAD(id, 6, '0')) WHERE reference IS NULL",

        $"""
        INSERT INTO ch_fpr (personne_id, categorie, motif, conduite, procedure_numero, service, reexamen_le, statut, informations, dangereux, arme, cree_par, cree_le, maj_le) VALUES
        ({P("LEROY", "Kévin")}, 'PJ — Recherche de police judiciaire', 'Recherché pour vol avec violence', 'Interpeller. Aviser immédiatement l''OPJ de permanence du CSP de Créteil.', '2025/000412', 'CSP de Créteil — BSU', '2027-04-30', 'Active', 'Peut se montrer véhément lors des contrôles.', 0, 0, {Auteur}, {Maintenant}, {Maintenant}),
        ({P("FONTAINE", "Léa")}, 'M — Mineur fugueur', 'Fugue du domicile familial depuis le 30/10/2026', 'Conduire au service. Aviser les représentants légaux et le CSP de Créteil.', '2026/000514', 'CSP de Créteil — BPF', '2027-01-31', 'Active', 'Mineure de 17 ans. Ne présente pas de danger.', 0, 0, {Auteur}, {Maintenant}, {Maintenant})
        """,
        "UPDATE ch_fpr SET numero = CONCAT('FPR-', LPAD(id, 6, '0')) WHERE numero IS NULL",

        $"""
        INSERT INTO ch_foves (type_objet, identifiant, identifiant_norm, vehicule_id, description, nature, signale_le, service, procedure_numero, statut, conduite, cree_par, cree_le, maj_le) VALUES
        ('Véhicule', 'DX-905-QA', 'DX905QA', {V("DX905QA")}, 'Volkswagen Golf VII blanche, jantes noires', 'Vol', '2026-10-28 07:15:00', 'CSP de Maisons-Alfort', '2026/000497', 'Signalement actif', 'Ne pas intervenir seul. Contrôler les occupants et aviser le CIC.', {Auteur}, {Maintenant}, {Maintenant}),
        ('Téléphone', 'IMEI 356938035643809', '356938035643809', NULL, 'Smartphone noir, coque bleue', 'Vol avec violence', '2025-10-04 19:30:00', 'CSP de Créteil — BSU', '2025/000412', 'Signalement actif', 'Saisir et aviser l''enquêteur en charge.', {Auteur}, {Maintenant}, {Maintenant})
        """,
        "UPDATE ch_foves SET reference = CONCAT('FOV-', LPAD(id, 6, '0')) WHERE reference IS NULL",

        $"""
        INSERT INTO ch_permis (personne_id, reference, categories, dates_obtention, validite, situation, points, cree_le, maj_le) VALUES
        ({P("LEROY", "Kévin")}, '18AB94012345', 'A2, B', 'B : 12/05/2017 ; A2 : 03/03/2019', '2032-05-12', 'Valide', 4, {Maintenant}, {Maintenant}),
        ({P("BENALI", "Sofiane")}, '11CD94054321', 'B', 'B : 08/09/2010', '2025-09-08', 'Valide', 12, {Maintenant}, {Maintenant}),
        ({P("MOREAU", "Julie")}, '04EF75067890', 'B', 'B : 21/06/2004', '2033-06-21', 'Valide', 12, {Maintenant}, {Maintenant}),
        ({P("ROUSSEL", "Anthony")}, '97GH94098765', 'B', 'B : 15/02/1997', NULL, 'Annulé', 0, {Maintenant}, {Maintenant})
        """,

        $"""
        INSERT INTO ch_scientifique (fichier, reference_prelevement, origine, procedure_numero, type, preleve_le, personne_id, resultat, rapport, cree_par, cree_le, maj_le) VALUES
        ('FAED', 'PTS-94-2026-0311', 'Cambriolage, 27 avenue du Général-de-Gaulle à Créteil (fenêtre de la cuisine)', '2026/000381', 'Trace papillaire', '2026-09-02 16:20:00', NULL, 'En attente', 'Trace exploitable relevée par le service local de police technique (simulation).', {Auteur}, {Maintenant}, {Maintenant}),
        ('FNAEG', 'PTS-94-2025-1188', 'Vol avec violence, rue du Général-Leclerc à Créteil', '2025/000412', 'Trace de sang', '2025-10-04 21:00:00', {P("LEROY", "Kévin")}, 'Concordance', 'Rapprochement simulé avec un profil enregistré (aucune donnée génétique réelle).', {Auteur}, {Maintenant}, {Maintenant})
        """,
        "UPDATE ch_scientifique SET reference = CONCAT(fichier, '-', LPAD(id, 6, '0')) WHERE reference IS NULL",

        $"""
        INSERT INTO ch_fijais (fichier, personne_id, reference_decision, juridiction, infraction, decision_le, fin_inscription, statut, service_gestionnaire, obligations, restrictions, cree_par, cree_le, maj_le) VALUES
        ('FIJAISV', {P("DIALLO", "Moussa")}, 'TC Créteil 24/018734', 'Tribunal correctionnel de Créteil', 'Violences aggravées (simulation)', '2024-11-19', '2044-11-19', 'Inscrit', 'Service gestionnaire national (fictif)', 'Justifier de son adresse une fois par an.', 'Consultation réservée aux agents habilités.', {Auteur}, {Maintenant}, {Maintenant})
        """,
        "UPDATE ch_fijais SET reference = CONCAT('FIJ-', LPAD(id, 6, '0')) WHERE reference IS NULL",

        $"""
        INSERT INTO ch_decisions (personne_id, juridiction, decision_le, nature, infraction, peine, cree_par, cree_le) VALUES
        ({P("ROUSSEL", "Anthony")}, 'Tribunal correctionnel de Créteil', '2025-09-12', 'Jugement contradictoire', 'Conduite malgré annulation du permis de conduire', '4 mois d''emprisonnement avec sursis', {Auteur}, {Maintenant}),
        ({P("DIALLO", "Moussa")}, 'Tribunal correctionnel de Créteil', '2024-11-19', 'Jugement contradictoire', 'Violences aggravées', '18 mois d''emprisonnement dont 12 avec sursis probatoire', {Auteur}, {Maintenant})
        """,
        "UPDATE ch_decisions SET reference = CONCAT('DEC-', LPAD(id, 6, '0')) WHERE reference IS NULL"
    ];
}
