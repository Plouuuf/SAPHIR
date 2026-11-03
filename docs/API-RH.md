# API du module RESSOURCES HUMAINES (phase 3)

Serveur : `src/SAPHIR.Server/Modules/Rh` — contrats : `src/SAPHIR.Shared/Contracts/Rh/RhContracts.cs`
(+ `Contracts/Referentiels.cs` pour les routes publiques). Poste : `ViewModels/Apps/Rh`, `Views/Apps/Rh`.

Toutes les réponses d'erreur ont la forme `{ "error": "…" }` (400 saisie, 403 droits, 404 introuvable, 409 conflit,
503 base). JSON web (camelCase). Dates : `DateOnly` = `"2026-11-02"`, `DateTimeOffset` UTC. Fichiers : `byte[]` en base64.

## Règles d'accès

- `/api/referentiels/**` : toute session d'agent (agent existant, statut Actif ou Profil à compléter), **sans** rôle RH.
- `/api/rh/**` : rôle Discord RH obligatoire (`requireRhRole: true`, vérifié par le serveur à chaque appel ; refus journalisé)
  **et** au moins une des permissions indiquées.
- Documents « Confidentiel RH » : visibles seulement avec `rh.documents` ou `rh.documents.valider` (sinon 404).
- Consultation d'un dossier individuel et téléchargement d'un fichier : écrits dans le journal des accès.

## Routes publiques (tous les modules)

| Méthode | Route | Réponse | Notes |
|---|---|---|---|
| GET | `/api/referentiels/agents?q=&tous=false` | `List<AgentResumeDto>` | Agents **Actifs** par défaut ; `tous=true` : tous statuts. Recherche nom, prénom, RIO, matricule, référence, grade, service, unité, fonction. |
| GET | `/api/referentiels/grades?tous=false` | `List<ReferentielDto>` | Éléments actifs (tous avec `tous=true`), triés par ordre. `Abrege` = « BC », « GPX »… |
| GET | `/api/referentiels/services` | `List<ReferentielDto>` | |
| GET | `/api/referentiels/unites` | `List<ReferentielDto>` | `ParentId` = service de l'unité. |
| GET | `/api/referentiels/fonctions` | `List<ReferentielDto>` | |

`AgentDto` (session, Administration, tous les modules via `AgentStore`) contient désormais `Grade`, `Service`, `Unite`,
`Fonction` (libellés actuels).

## Personnel

| Méthode | Route | Permission | Corps → réponse |
|---|---|---|---|
| GET | `/api/rh/tableau-de-bord` | rh.consulter | → `RhTableauBordDto` |
| GET | `/api/rh/parametres` | rh.consulter / rh.documents / rh.documents.valider | → `RhParametresDto` (format et exemple de RIO, organisation, fichiers acceptés) |
| GET | `/api/rh/agents?q=&statut=&serviceId=&uniteId=&gradeId=` | rh.consulter | → `List<RhAgentLigneDto>` |
| GET | `/api/rh/agents/{id}` | rh.consulter | → `RhDossierDto` (identité, affectations, carrière, RIO, identités, documents, habilitations, historique) |
| PUT | `/api/rh/agents/{id}/identite` | rh.agent.modifier | `RhIdentiteInput` (motif obligatoire) → 204 |
| PUT | `/api/rh/agents/{id}/matricule` | rh.agent.modifier | `RhMatriculeInput` (motif obligatoire, unique) → 204 |
| PUT | `/api/rh/agents/{id}/integration` | rh.agent.modifier | `RhIntegrationInput` → 204 |
| PUT | `/api/rh/agents/{id}/rio` | rh.rio.attribuer | `RhRioInput` → 204 |
| PUT | `/api/rh/agents/{id}/grade` | rh.carriere | `RhGradeInput` (motif obligatoire) → 204 |
| POST | `/api/rh/agents/{id}/affectations` | rh.carriere | `RhAffectationInput` (motif obligatoire) → 204 |
| PUT | `/api/rh/agents/{id}/statut` | rh.statut | `RhStatutInput` → 204 |
| GET | `/api/rh/affectations?enCours=false` | rh.consulter | → `List<RhAffectationDto>` |
| GET | `/api/rh/rio/historique?limit=300` | rh.consulter | → `List<RhRioHistoriqueDto>` |
| GET | `/api/rh/historique?objet=&reference=&limit=300` | rh.consulter | → `List<HistoriqueDto>` (objets `agent`, `rh-grade`, `rh-service`, `rh-unite`, `rh-fonction`, `rh-document`, `rh-dossier`, `rh-categorie`, `rh-modele`) |

Règles :
- **RIO** : format = paramètre central `rh.rio.format` (7 chiffres par défaut) ; unique ; un RIO porté un jour par un agent
  n'est jamais réattribué à un autre (409) ; motif obligatoire pour un changement ; ancien RIO conservé (`rh_rio_historique`).
- **Identité** : NOM en majuscules, Prénom capitalisé ; ancienne identité conservée (`rh_identites`). Les actes validés des
  autres modules gardent les valeurs copiées au moment de leur création.
- **Grade** : ligne de carrière (`rh_carriere`) avec libellés figés ; justificatif facultatif (`DocumentId` d'un document RH).
- **Affectation** : l'unité doit appartenir au service ; l'affectation en cours est close à la date d'effet ; la courante
  est celle sans date de fin ; service, unité et fonction de l'agent sont mis à jour.
- **Statut** : « Profil à compléter » devient automatiquement « Actif » dès que RIO + grade + service + unité sont renseignés
  (historisé). Suspendre / désactiver / archiver : motif obligatoire, jamais son propre compte, jamais le dernier
  administrateur actif. « Actif » refusé si le profil est incomplet.
- Après chaque modification : `session-changed` envoyé au poste de l'agent concerné, puis diffusion `rh-agent-modifie`
  (Text = id de l'agent) pour que les listes d'agents des autres fenêtres se rechargent.

## Référentiels RH (gestion)

| Méthode | Route | Permission | Corps → réponse |
|---|---|---|---|
| GET | `/api/rh/referentiels/{grades\|services\|unites\|fonctions}?inactifs=true` | rh.consulter / rh.referentiels | → `List<RhReferentielDto>` (avec effectif, service parent, responsable) |
| POST | `/api/rh/referentiels/{type}` | rh.referentiels | `RhReferentielInput` → `RhReferentielDto` |
| PUT | `/api/rh/referentiels/{type}/{id}` | rh.referentiels | `RhReferentielInput` → `RhReferentielDto` |

Code interne stable généré depuis le libellé à la création (jamais modifié). Pas de suppression : désactivation (`Actif=false`).
Libellé unique (par service pour les unités). Renommage : `session-changed` aux agents concernés. Diffusion
`rh-referentiels-modifies` (Text = type).

## Gestion documentaire

| Méthode | Route | Permission | Corps → réponse |
|---|---|---|---|
| GET | `/api/rh/documents?q=&categorieId=&dossierId=&agentId=&serviceId=&statut=&archives=false` | rh.consulter / rh.documents / rh.documents.valider | → `List<RhDocumentDto>` (`archives=true` : archivés seulement) |
| GET | `/api/rh/documents/{id}` | idem | → `RhDocumentDetailDto` (versions, historique) |
| GET | `/api/rh/documents/{id}/versions/{numero}/fichier` | idem | → `RhFichierDto` (`numero=0` : version courante) |
| POST | `/api/rh/documents` | rh.documents | `RhDocumentCreationInput` → `RhDocumentDto` |
| PUT | `/api/rh/documents/{id}` | rh.documents | `RhDocumentMajInput` → `RhDocumentDto` |
| POST | `/api/rh/documents/{id}/versions` | rh.documents | `RhVersionInput` (commentaire obligatoire) → `RhDocumentDto` |
| POST | `/api/rh/documents/{id}/soumettre` | rh.documents | → `RhDocumentDto` (Brouillon → À valider) |
| POST | `/api/rh/documents/{id}/valider` | rh.documents.valider | → `RhDocumentDto` (Brouillon / À valider → Validé, figé) |
| POST | `/api/rh/documents/{id}/refuser` | rh.documents.valider | `RhMotifInput` (motif obligatoire) → `RhDocumentDto` (À valider → Brouillon) |
| POST | `/api/rh/documents/{id}/archiver` | rh.documents | `RhMotifInput` → `RhDocumentDto` |
| POST | `/api/rh/documents/{id}/restaurer` | rh.documents | → `RhDocumentDto` (statut d'avant l'archivage) |
| GET | `/api/rh/dossiers` | lecture documents | → `List<RhDossierDocDto>` (chemin « Personnel \ Nominations ») |
| POST | `/api/rh/dossiers` | rh.documents | `RhDossierDocInput` → `RhDossierDocDto` |
| PUT | `/api/rh/dossiers/{id}` | rh.documents | `RhDossierDocInput` (renommer / déplacer, sans cycle) → `RhDossierDocDto` |
| DELETE | `/api/rh/dossiers/{id}` | rh.documents | → 204 (dossier vide uniquement) |
| GET | `/api/rh/categories` | lecture documents | → `List<RhCategorieDto>` |
| POST / PUT | `/api/rh/categories[/{id}]` | rh.documents | `RhCategorieInput` → `RhCategorieDto` |
| GET | `/api/rh/modeles` | rh.consulter / rh.documents | → `List<RhModeleDto>` |
| POST / PUT | `/api/rh/modeles[/{id}]` | rh.documents | `RhModeleInput` → `RhModeleDto` |
| POST | `/api/rh/modeles/{id}/generer` | rh.documents | `RhGenerationInput` → `RhGenerationDto` |

Règles :
- Fichiers : PDF, PNG, JPEG, DOCX, TXT ; 10 Mo au plus ; extension **et** signature du contenu contrôlées ; nom nettoyé ;
  empreinte SHA-256 ; contenu en base (`MEDIUMBLOB`, stockage privé).
- Versions : une nouvelle version n'efface jamais l'ancienne (les versions non validées passent « Remplacée ») ; le
  document repasse en brouillon.
- Validé = figé : métadonnées verrouillées (seul le dossier peut changer), nouvelle version ou archivage possibles.
- Modèles : variables `{{agent_nom}}`, `{{agent_prenom}}`, `{{agent_rio}}`, `{{agent_grade}}`, `{{agent_service}}`,
  `{{agent_unite}}`, `{{agent_fonction}}`, `{{agent_matricule}}`, `{{agent_reference}}`, `{{date_document}}`,
  `{{responsable_nom}}`, `{{numero_document}}`, `{{organisation}}` — ou leurs alias courts `{{NOM}}`, `{{PRENOM}}`, `{{RIO}}`,
  `{{GRADE}}`, `{{SERVICE}}`, `{{UNITE}}`, `{{FONCTION}}`, `{{MATRICULE}}`, `{{REFERENCE}}`, `{{DATE}}`, `{{RESPONSABLE}}`,
  `{{NUMERO}}`, `{{ORGANISATION}}` (insensibles à la casse). La génération crée un vrai PDF (version 1, texte conservé
  pour l'aperçu) avec les valeurs **actuelles** de l'agent ; `Apercu=true` n'enregistre rien.
- Diffusion `rh-documents-modifies` après chaque modification documentaire.

## Tables (migrations 100-101)

`rh_grades`, `rh_services`, `rh_unites`, `rh_fonctions` (code unique, libellé, abrégé, ordre, actif + catégorie /
service_id / responsable_id), colonnes ajoutées à `rh_agents` (`grade_id`, `service_id`, `unite_id`, `fonction_id`,
`date_integration`, `grade_effet`), `rh_affectations`, `rh_carriere`, `rh_rio_historique`, `rh_identites`,
`rh_doc_categories`, `rh_doc_dossiers`, `rh_documents`, `rh_document_versions`, `rh_modeles`.

Données de départ (101) : 16 grades de la Police nationale, service « CSP de Créteil » et ses 6 unités (Direction,
BPS / BAC, CSI, BMU, Accueil, CIC), 12 fonctions, 16 catégories documentaires (section 5.3), dossiers racine
(Personnel, Service, Documents généraux), 8 modèles (section 5.6). Aucun agent fictif.

## Temps réel

| Type | Text | Émis quand |
|---|---|---|
| `session-changed` (socle) | — | fiche de l'agent modifiée (envoyé à cet agent) |
| `rh-agent-modifie` | id de l'agent | toute modification d'un agent |
| `rh-referentiels-modifies` | type | création / modification d'un grade, service, unité, fonction |
| `rh-documents-modifies` | — | document, dossier, catégorie, modèle |
