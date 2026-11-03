# API — module Opérations (MAIN COURANTE, PÉGASE, GÉOOPS)

Phases 4 et 5. Contrats : `src/SAPHIR.Shared/Contracts/Operations/*.cs` (namespace `Saphir.Shared.Contracts.Operations`).
Serveur : `src/SAPHIR.Server/Modules/Operations/`. Poste : `ViewModels/Apps/Operations`, `Views/Apps/Operations`.

Toutes les routes `/api/…` exigent le jeton de session (`Authorization: Bearer …`), un agent autorisé et **au moins une**
des permissions indiquées (vérifiées sur le serveur). Erreurs : `{ "error": "message" }` — 400 saisie, 403 droits,
404 introuvable, 409 conflit (état incompatible ou fiche modifiée entre-temps : champ `version`), 503 base indisponible.
Dates en ISO 8601 (UTC). Toute modification est historisée dans `sys_historique` ; les consultations de mentions, appels
et interventions sont inscrites au journal des accès.

Raccourcis de permissions :
- **MC*** = `main-courante.consulter`, `.mention.rediger`, `.mention.valider`, `.vacation.gerer`, `.equipage.gerer`, `.vehicule.gerer`
- **PG*** = `pegase.consulter`, `.appel.creer`, `.intervention.creer`, `.intervention.affecter`, `.equipage.statut`, `.compte-rendu`, `.intervention.cloturer`, `.tn`

## MAIN COURANTE — `/api/main-courante`

| Méthode | Route | Corps / paramètres | Réponse | Permissions |
|---|---|---|---|---|
| GET | `/tableau-de-bord` | | `McTableauDeBordDto` | MC* |
| GET | `/vacations` | `?statut=Ouverte\|Clôturée&limit=` | `VacationResumeDto[]` | MC* |
| GET | `/vacations/{id}` | | `VacationDto` | MC* |
| POST | `/vacations` | `VacationInput` (ouverture, journal « Prise de service ») | `VacationDto` | `vacation.gerer` |
| PUT | `/vacations/{id}` | `VacationInput` (+`version`) | `VacationDto` | `vacation.gerer` |
| POST | `/vacations/{id}/cloturer` | `ClotureVacationInput` — refus si équipage engagé ou véhicule sorti ; équipages dissous | `VacationDto` | `vacation.gerer` |
| GET | `/equipages` | `?vacationId=&actifs=true` | `EquipageDto[]` | MC* ou PG* |
| POST | `/equipages` | `EquipageInput` (un seul chef de bord ; agent dans un seul équipage actif) | `EquipageDto` | `equipage.gerer` |
| PUT | `/equipages/{id}` | `EquipageInput` | `EquipageDto` | `equipage.gerer` |
| POST | `/equipages/{id}/dissoudre` | `MotifInput` — refus si engagé | 204 | `equipage.gerer` |
| GET | `/vehicules` | | `VehiculeServiceDto[]` | MC* ou PG* |
| GET | `/vehicules/{id}` | | `VehiculeFicheDto` (mouvements + historique) | MC* |
| POST | `/vehicules` | `VehiculeServiceInput` | `VehiculeServiceDto` | `vehicule.gerer` |
| PUT | `/vehicules/{id}` | `VehiculeServiceInput` (+`version`) | `VehiculeServiceDto` | `vehicule.gerer` |
| POST | `/vehicules/{id}/sortie` | `SortieVehiculeInput` (disponible → sorti ; km ≥ dernier relevé) | `VehiculeServiceDto` | `vehicule.gerer` |
| POST | `/vehicules/{id}/retour` | `RetourVehiculeInput` (sorti → disponible / indisponible) | `VehiculeServiceDto` | `vehicule.gerer` |
| POST | `/vehicules/{id}/disponibilite` | `DisponibiliteVehiculeInput` (motif obligatoire si indisponible) | `VehiculeServiceDto` | `vehicule.gerer` |
| GET | `/mentions` | `?q=&type=&statut=&du=&au=&interventionId=&limit=` | `MentionResumeDto[]` | `consulter`, `mention.rediger`, `mention.valider` |
| GET | `/mentions/{id}` | | `MentionDto` (avec rectificatives) | idem |
| GET | `/mentions/projet` | `?interventionId=` — projet prérempli depuis PÉGASE (20.2) | `MentionInput` | `mention.rediger` |
| POST | `/mentions` | `MentionInput` (brouillon ; `mentionParentId` = mention rectificative d'une mention validée) | `MentionDto` | `mention.rediger` |
| PUT | `/mentions/{id}` | `MentionInput` (+`version`) — brouillon seulement, rédacteur ou valideur | `MentionDto` | `mention.rediger`, `mention.valider` |
| POST | `/mentions/{id}/valider` | — mention figée ensuite | `MentionDto` | `mention.valider` |
| GET | `/journal` | `?du=&au=&vacationId=&type=&q=&limit=` | `JournalEntreeDto[]` | MC* |
| POST | `/journal` | `JournalInput` (événement saisi) | 204 | `mention.rediger`, `vacation.gerer` |
| GET | `/historique` | `?reference=&q=&limit=` (objets `mc-*`) | `HistoriqueDto[]` | MC* |

Numéros : `VAC-2026-000003`, `VS-2026-000021` (véhicule), `MC-2026-000017` (mention).
Journal de service alimenté automatiquement : prise de service, constitution / modification / dissolution d'équipage,
sortie et retour de véhicule, intervention créée, engagement, arrivée sur place, retour de mission, statut d'équipage
(indisponible / hors service), intervention terminée, mention validée, fin de vacation.

## PÉGASE — `/api/pegase`

| Méthode | Route | Corps / paramètres | Réponse | Permissions |
|---|---|---|---|---|
| GET | `/tableau-de-bord` | | `PegaseTableauDeBordDto` | PG* |
| GET | `/appels` | `?etat=ouverts\|<état>&q=&limit=` | `AppelResumeDto[]` | PG* |
| GET | `/appels/{id}` | | `AppelDto` (formulaire A à H) | PG* |
| POST | `/appels` | `AppelCreationInput { canal }` — nouvel appel, opérateur = agent | `AppelDto` | `appel.creer` |
| PUT | `/appels/{id}` | `AppelActionInput { formulaire, version }` — brouillon / modification | `AppelDto` | `appel.creer` |
| POST | `/appels/{id}/attente` | `AppelActionInput { formulaire?, version, motif? }` | `AppelDto` | `appel.creer` |
| POST | `/appels/{id}/transmettre-tn` | idem — bulle au TN de permanence | `AppelDto` | `appel.creer` |
| POST | `/appels/{id}/clore` | idem, `motif` obligatoire (clos sans suite) | `AppelDto` | `appel.creer` |
| POST | `/appels/{id}/rattacher` | idem + `interventionId` (plusieurs appels → une intervention) | `AppelDto` | `appel.creer` |
| POST | `/appels/{id}/intervention` | idem + `equipageId?` (engagement immédiat, exige `intervention.affecter`) — webhook Discord | `InterventionDto` | `intervention.creer` |
| GET | `/interventions` | `?vue=en-cours\|en-attente\|actives\|terminees&q=&du=&au=&limit=` | `InterventionLigneDto[]` | PG* |
| GET | `/interventions/{id}` | | `InterventionDto` (fiche 7.1) | PG* |
| GET | `/interventions/{id}/resume` | | `InterventionResumeDto` (contrat partagé) | `pegase.consulter`, `lrppn.consulter` |
| PUT | `/interventions/{id}` | `InterventionModificationInput` (+`version` ; justification si la priorité change) | `InterventionDto` | `intervention.creer`, `intervention.affecter` |
| PUT | `/interventions/{id}/position` | `PositionInput { x, y }` (coordonnées du jeu, GÉOOPS) | `InterventionDto` | idem |
| POST | `/interventions/{id}/statut` | `InterventionStatutInput { statut, motif }` — clôture : `intervention.cloturer` (motif si pas de compte rendu validé) ; annulation : motif obligatoire | `InterventionDto` | `intervention.affecter`, `intervention.cloturer`, `intervention.creer` |
| POST | `/interventions/{id}/engagements` | `EngagementInput { equipageId, renfort }` | `InterventionDto` | `intervention.affecter` |
| POST | `/interventions/{id}/engagements/{engagementId}/desengager` | `MotifInput` | `InterventionDto` | `intervention.affecter` |
| POST | `/interventions/{id}/messages` | `MessageInput { texte }` (communication horodatée) | `InterventionDto` | PG* sauf `consulter` |
| PUT | `/interventions/{id}/references` | `ReferencesInput { procedureLrppn, suites, documents }` | `InterventionDto` | `compte-rendu`, `intervention.cloturer`, `intervention.affecter`, `lrppn.acte.rediger` |
| POST | `/interventions/{id}/comptes-rendus` | `CompteRenduInput` (brouillon) | `CompteRenduDto` | `compte-rendu` |
| PUT | `/comptes-rendus/{id}` | `CompteRenduInput` (+`version`) | `CompteRenduDto` | `compte-rendu` |
| POST | `/comptes-rendus/{id}/valider` | — figé ; l'équipage du compte rendu encore engagé termine sa mission | `CompteRenduDto` | `compte-rendu`, `intervention.cloturer` |
| POST | `/equipages/{id}/statut` | `EquipageStatutInput { statut, motif }` — membre de l'équipage ou affectateur | `EquipageDto` | `equipage.statut`, `intervention.affecter` |
| GET | `/communications` | `?messages=true&limit=` | `CommunicationDto[]` | PG* |
| GET | `/tn` | `?limit=` | `TnDto[]` | PG* |
| POST | `/tn/prendre` | `TnInput { service }` — clôt la permanence en cours | `TnDto` | `pegase.tn` |
| POST | `/tn/{id}/terminer` | | 204 | `pegase.tn` |
| GET | `/statistiques` | `?du=&au=` (30 derniers jours par défaut) | `PegaseStatistiquesDto` | PG* |
| GET | `/historique` | `?reference=&q=&limit=` (objets `pg-*`) | `HistoriqueDto[]` | PG* |

Numéros : `APL-2026-000119` (appel), `INT-2026-000042` (intervention), `CR-2026-…` (compte rendu, historique).

Statuts d'intervention (7.2) : À qualifier, En attente d'engagement, Moyens affectés, En cours, Compte rendu attendu,
Clôturée, Annulée. Le statut global suit automatiquement les équipages (premier « En route » / « Sur place » → En cours ;
plus aucun engagement actif après passage sur le terrain → Compte rendu attendu ; désengagement avant → En attente).
Statuts d'équipage : Disponible, Affecté (donné par l'engagement), En route, Sur place, Transport en cours, Au service
destinataire, Mission terminée, Indisponible, Hors service — indépendants du statut de l'intervention.

### Notification Discord (8.3)

Paramètre central `pegase.discord.webhook` (Administration › Paramètres) : URL `https://discord.com/api/webhooks/…`.
Toute autre valeur (« désactivé ») coupe l'envoi. À chaque nouvelle intervention, embed : référence, heure, motif,
commune, priorité, moyens demandés, statut — **aucune donnée personnelle** ; aucune mention `@` n'est autorisée.

## GÉOOPS — `/api/geoops`

| Méthode | Route | Réponse | Permissions |
|---|---|---|---|
| GET | `/carte` | image brute (`data/geoops/carte.jpg`, ou `.png`/`.webp`) ; 404 si absente | PG* |
| GET | `/carte/donnees` | `GeoopsCarteDto { contentType, donnees (base64), version }` ; 404 si absente | PG* |
| GET | `/calibration` | `GeoopsCalibrationDto` | PG* |
| GET | `/situation` | `GeoopsSituationDto` (équipages positionnés, autres agents, interventions placées / non placées) | PG* |
| POST | `/equipages/{id}/position` | `PositionInput` → position **déclarée** de l'équipage, 204 | `intervention.affecter` |

Calibration (paramètres centraux) : `geoops.carte.x_min`, `x_max` (X du jeu aux bords gauche / droit de l'image),
`geoops.carte.y_min`, `y_max` (Y du jeu aux bords bas / haut), `geoops.positions.peremption` (secondes).
Conversion : `u = (x − x_min) / (x_max − x_min)`, `v = (y_max − y) / (y_max − y_min)`.

## Intégrations serveur à serveur (en-tête `X-Saphir-Key` = `SAPHIR:AccessKey`)

| Méthode | Route | Corps | Réponse |
|---|---|---|---|
| POST | `/api/integrations/simcall/answered` | `{ simcallCallId, discordUserId, operatorName, callerNumber, answeredAt }` | `200 { id }` ; 400 si incomplet |
| POST | `/api/integrations/simcall/ended` | `{ simcallCallId, endedAt }` | 204 |
| POST | `/api/integrations/geoops/positions` | `[{ discordId, serverId, nom, x, y, z, cap, vitesse, vehicule }]` (512 au plus) | `200 { recues, enregistrees }` |

SIMCALL (contrat inchangé depuis SAPHIR 0.1, SIMCALL 0.7.3) : au décroché d'un 17, l'appel PÉGASE est créé (canal 17,
téléphone du requérant prérempli) et le poste de l'opérateur (même compte Discord) reçoit
`{ type: "open-app", appId: "pegase", text: "appel:<id>" }` : PÉGASE s'ouvre sur la prise d'appel. Un second envoi du
même `simcallCallId` réouvre le même appel. Au raccroché, l'heure de fin est figée (durée affichée).

Script FiveM : `fivem/saphir-geoops/` (voir son README).

## Temps réel

Messages diffusés à tous les postes (identifiant seulement, aucune donnée personnelle) :
`main-courante-changed` (`Text` = `vacation:3`, `equipage:11`, `vehicule:21`, `mention:17`, `journal`),
`pegase-changed` (`intervention:42`, `appel:119`, `equipage:11`, `vehicule:21`, `tn`), `geoops-positions` (nombre).
Bulles (`notify`) : TN à la création d'une intervention et à la transmission d'un appel ; agents d'un équipage engagé ;
opérateur SIMCALL. Ouverture d'application sur le poste : `open-app` avec `appel:<id>`, `intervention:<id>`,
`mention:<id>`, `projet-mention:<interventionId>` (Main courante), `intervention:<id>` vers LRPPN.

## Tables (migrations 300 à 303)

`mc_vacations`, `mc_vacation_agents`, `mc_vehicules`, `mc_equipages`, `mc_equipage_membres`, `mc_vehicule_mouvements`,
`mc_mentions`, `mc_journal`, `pg_appels`, `pg_interventions`, `pg_personnes`, `pg_vehicules`, `pg_engagements`,
`pg_chronologie`, `pg_comptes_rendus`, `pg_statuts`, `pg_tn`, `geo_positions` ; paramètres `pegase.discord.webhook`,
`geoops.carte.*`, `geoops.positions.peremption`.
