# API — Applications complémentaires (phase 8)

NEO, PVe, ANNUAIRE, MESSAGERIE, NOTES, MRZ, BODYCAM, TCHAP. Serveur : `src/SAPHIR.Server/Modules/Complements`,
contrats : `src/SAPHIR.Shared/Contracts/Complements` (namespace `Saphir.Shared.Contracts.Complements`).

Toutes les routes exigent le jeton d'application (`Authorization: Bearer …`) et passent par `Api.RunAsync`
(agent existant, statut autorisé, permission). Erreurs : `{ "error": "…" }` (400 saisie, 403 droits, 404 introuvable,
409 conflit / version périmée, 503 base). JSON en camelCase. Dates : `DateTimeOffset` ISO 8601 (stockées en UTC),
`DateOnly` au format `AAAA-MM-JJ`.

Toute modification est historisée dans `sys_historique` (objets `message`, `note`, `mrz`, `pve`, `pve-infraction`,
`bodycam`, `tchap-salon`, `annuaire-service`, `annuaire-coordonnees`). Les consultations sensibles (lecture MRZ,
visionnage bodycam, documents bodycam) sont inscrites au journal des accès (`sys_journal_acces`).

## Migrations (800-899)

| N° | Contenu |
|----|---------|
| 800 | `app_annuaire_services`, `app_annuaire_coordonnees` |
| 801 | `app_messages`, `app_message_destinataires`, `app_message_pieces` (MEDIUMBLOB) |
| 802 | `app_notes`, `app_note_partages` |
| 803 | `app_mrz_lectures` |
| 804 | `pve_infractions`, `pve_avis`, `pve_justificatifs` (MEDIUMBLOB), `pve_mentions` |
| 805 | `app_bodycam`, `app_bodycam_documents` (MEDIUMBLOB), `app_bodycam_acces` |
| 806 | `app_tchap_salons`, `app_tchap_membres`, `app_tchap_lectures`, `app_tchap_messages` |
| 807 | Données de départ : 14 services de l'annuaire (numéros fictifs 01 99 00 …), 5 salons Tchap de service |
| 808 | Données de départ : 34 infractions routières (NATINF et montants indicatifs, fictifs) |

Aucune clé étrangère vers les autres modules (agents, personnes, véhicules : identifiants stockés). Les libellés des
agents sont recopiés au moment de l'acte (historique conservé, cahier § 20.1).

## Temps réel (`ComplementsRealtime`)

| Type | Destinataires | Text |
|------|---------------|------|
| `messagerie-recu` | destinataires (SendToUser) | id du message |
| `messagerie-lu` | expéditeur, à la première lecture | id du message |
| `tchap-message` | tous (salon de service) ou membres (groupe) | id du salon |
| `tchap-salons` | concernés | — |
| `notes-change` | agents partageant la note | id de la note |
| `pve-change`, `bodycam-change` | tous | id |
| `annuaire-change` | tous | `services` / `coordonnees` |

Bulles `notify` (AppId = application) : nouveau message (messagerie), nouveau message dans un salon de groupe (Tchap).
Le texte ne contient que le nom de l'expéditeur, jamais le contenu.

Arguments d'ouverture (`SaphirSession.Publish(open-app)`) : Messagerie `a:{agentId}` (nouveau message) ou `m:{id}` ;
PVe `pve:{id}`, `PVE-…`, numéro d'avis, `nouveau` ; Bodycam `bodycam:{id}` ou `BC-…` ; Annuaire `AG-…` ou id ;
Tchap `salon:{id}` ; NEO `intervention:{id}`.

## ANNUAIRE (§ 18.1)

Le poste lit le personnel par `GET /api/referentiels/agents` (module RH) et les filtres par
`GET /api/referentiels/services|unites|grades` ; repli : route du socle ci-dessous.

| Méthode | Route | Permission | Corps / réponse |
|---|---|---|---|
| GET | `/api/annuaire/agents?q=` | annuaire, messagerie, tchap, notes, bodycam.gerer, neo | `AnnuaireAgentDto[]` (agents Actifs, colonnes du socle) |
| GET | `/api/annuaire/coordonnees` | idem | `CoordonneesDto[]` |
| PUT | `/api/annuaire/coordonnees/moi` | `annuaire.consulter` | `CoordonneesInput` → `CoordonneesDto` |
| PUT | `/api/annuaire/coordonnees/{agentId}` | `admin.comptes` | idem |
| GET | `/api/annuaire/services?tous=` | idem liste | `ServiceAnnuaireDto[]` (`tous` = inactifs, admin.parametres) |
| POST | `/api/annuaire/services` | `admin.parametres` | `ServiceAnnuaireInput` → `ServiceAnnuaireDto` |
| PUT | `/api/annuaire/services/{id}` | `admin.parametres` | idem |

## MESSAGERIE (§ 18.2) — permission `messagerie.utiliser`

| Méthode | Route | Corps / réponse |
|---|---|---|
| GET | `/api/messagerie/messages?dossier=reception|envoyes|brouillons|corbeille&q=` | `MessageResumeDto[]` (300 max) |
| GET | `/api/messagerie/compteurs` | `MessagerieCompteursDto` |
| GET | `/api/messagerie/messages/{id}` | `MessageDto` ; marque lu, accusé de lecture à l'expéditeur. Les accusés (`LuLe`) ne sont visibles que par l'expéditeur |
| GET | `/api/messagerie/conversations/{conversationId}` | `MessageDto[]` lisibles par l'agent |
| POST | `/api/messagerie/messages` | `MessageInput` → `MessageDto` (brouillon, ou envoi si `envoyer`) |
| PUT | `/api/messagerie/messages/{id}` | `MessageInput` (brouillon uniquement, `version`) |
| DELETE | `/api/messagerie/messages/{id}` | brouillon supprimé ; reçu → corbeille puis effacé ; envoyé → retiré des envoyés |
| POST | `/api/messagerie/messages/{id}/restaurer` | corbeille → réception |
| POST | `/api/messagerie/messages/{id}/non-lu` | — |
| GET | `/api/messagerie/pieces/{id}` | `PieceJointeContenuDto` (base64), si l'agent peut lire le message |

Pièces jointes : base64, 10 Mo décodés, 10 par message, extensions `PiecesJointes.Extensions`, signature des premiers
octets contrôlée, SHA-256 stocké. `piecesConservees` : pièces du brouillon à garder ou pièces d'un message lisible à
transférer (copiées). `liens` : références SAPHIR (`PER-…`, `PVE-…`, `INT-…`, `BC-…`…) ; l'application cible vérifie
les droits à l'ouverture. Un message envoyé n'est plus modifiable.

## NOTES (§ 18.3) — permission `notes.utiliser`

| Méthode | Route | Corps / réponse |
|---|---|---|
| GET | `/api/notes?q=&categorie=&filtre=toutes|epinglees|miennes|partagees` | `NoteDto[]` |
| GET | `/api/notes/categories` | `string[]` |
| GET | `/api/notes/{id}` | `NoteDto` |
| GET | `/api/notes/{id}/historique` | `HistoriqueDto[]` |
| POST | `/api/notes` | `NoteInput` → `NoteDto` |
| PUT | `/api/notes/{id}` | `NoteInput` (`version` contrôlée, 409 si périmée) |
| DELETE | `/api/notes/{id}` | auteur : suppression logique ; destinataire : retrait du partage |

Partage avec lecture seule ou modification (`peutModifier`) ; seul l'auteur gère l'épinglage et les partages.

## MRZ (§ 18.4) — permission `mrz.utiliser`

| Méthode | Route | Corps / réponse |
|---|---|---|
| POST | `/api/mrz/analyser` | `MrzAnalyseInput` → `MrzResultatDto` (journal des accès) |
| POST | `/api/mrz/lectures` | `MrzLectureInput` → `MrzLectureDto` (refusé si contrôles en échec sans `confirmationManuelle`) |
| GET | `/api/mrz/lectures?limit=` | `MrzLectureDto[]` de l'agent |

Analyse : classe pure `MrzParser` (TD1 3×30, TD3 2×44, ancienne CNI française 2×36 ; pondération 7-3-1 ; bande collée
sur une ligne redécoupée). Le poste recherche ensuite la personne par `GET /api/personnes/recherche?q=` (module
Judiciaire) et peut la créer par `POST /api/personnes`.

## PVe (§ 17) — permission `pve.rediger`

| Méthode | Route | Corps / réponse |
|---|---|---|
| GET | `/api/pve/infractions?tous=` | `InfractionDto[]` (`pve.rediger` ou `admin.parametres`) |
| POST / PUT | `/api/pve/infractions` / `{id}` | `InfractionInput` (`admin.parametres`) |
| GET | `/api/pve?q=&statut=&mien=&limit=` | `PveResumeDto[]` (brouillons : ceux de l'agent seulement) |
| GET | `/api/pve/{id}` ; `/api/pve/reference/{référence ou n° d'avis}` | `PveDto` |
| POST | `/api/pve` | `PveInput` → `PveDto` (brouillon ; rédacteur repris de la fiche RH : nom, RIO, grade, service, unité) |
| PUT | `/api/pve/{id}` | `PveInput` (brouillon du rédacteur, `version`) |
| POST | `/api/pve/{id}/valider` | `PveValidationInput` → numéro d'avis (13 chiffres, clé mod 97), acte figé |
| POST | `/api/pve/{id}/abandonner` | `PveAbandonInput` (brouillon) |
| POST | `/api/pve/{id}/mentions` | `PveMentionInput` (PVe validé uniquement) |
| POST | `/api/pve/{id}/justificatifs` | `PieceJointeInput` (brouillon) → `PveDto` |
| GET / DELETE | `/api/pve/justificatifs/{id}` | `PieceJointeContenuDto` / retrait (brouillon) |
| GET | `/api/pve/{id}/historique` | `HistoriqueDto[]` |
| GET | `/api/pve/{id}/export` | `PveExportDto` (HTML autonome, échappé, mention « document fictif ») |

L'infraction (NATINF, libellé, texte, classe, montants forfaitaire / minoré / majoré, points) est recopiée dans l'avis.
Règles : `PveRegles` (contrevenant ou immatriculation obligatoire, date ≤ maintenant et ≥ J-30, immatriculation plausible).

## BODYCAM (§ 18.5)

Consultation : `bodycam.consulter` ou `bodycam.gerer` ; gestion : `bodycam.gerer`.

| Méthode | Route | Permission | Corps / réponse |
|---|---|---|---|
| GET | `/api/bodycam?q=&intervention=&agent=&archives=` | consulter | `BodycamResumeDto[]` |
| GET | `/api/bodycam/{id}` | consulter | `BodycamDto` |
| POST / PUT | `/api/bodycam` / `{id}` | gérer | `BodycamInput` (`source` = `url` ou `fichier`) |
| POST | `/api/bodycam/{id}/archiver` | gérer | `BodycamArchivageInput` |
| POST | `/api/bodycam/{id}/fichier/debut` | gérer | `BodycamEnvoiDebutInput` (≤ 200 Mo, mp4/m4v/mov/webm/mkv/avi) |
| PUT | `/api/bodycam/{id}/fichier/morceau` | gérer | `BodycamMorceauInput` (base64, ≤ 4 Mo, `offset` = octets déjà reçus) |
| POST | `/api/bodycam/{id}/fichier/fin` | gérer | → `BodycamDto` (SHA-256 calculé, statut Disponible) |
| POST | `/api/bodycam/{id}/visionner` | consulter | `BodycamVisionnageDto` — **inscrit au journal d'accès** |
| GET | `/api/bodycam/{id}/fichier?offset=&taille=` | consulter | `BodycamMorceauDto` (lecture par morceaux de 4 Mo max) |
| GET | `/api/bodycam/{id}/acces` | gérer | `BodycamAccesDto[]` |
| GET | `/api/bodycam/{id}/historique` | gérer | `HistoriqueDto[]` |
| POST | `/api/bodycam/{id}/documents` | gérer | `PieceJointeInput` → `BodycamDto` |
| GET | `/api/bodycam/documents/{id}` | consulter | `PieceJointeContenuDto` (accès journalisé) |

Fichiers stockés dans `data/bodycam/` à côté de l'exécutable du serveur (`{numéro}.{ext}`, `{id}.part` pendant
l'envoi), jamais servis directement. Une vidéo reçue ne peut pas être remplacée.

## TCHAP (§ 18.6) — permission `tchap.utiliser`

Version interne simplifiée (aucune liaison avec le Tchap réel). Salons de service : ouverts à tous, créés par
`admin.parametres`. Salons de groupe : créés par un agent (administrateur du salon), réservés aux membres.

| Méthode | Route | Corps / réponse |
|---|---|---|
| GET | `/api/tchap/salons?archives=` | `SalonDto[]` (non-lus par agent) |
| POST | `/api/tchap/salons` | `SalonInput` → `SalonDto` |
| PUT | `/api/tchap/salons/{id}` | `SalonInput` (nom, description ; administrateur) |
| POST | `/api/tchap/salons/{id}/archiver` | administrateur ; lecture seule |
| GET / PUT | `/api/tchap/salons/{id}/membres` | `SalonMembreDto[]` / `SalonMembresInput` (groupe, administrateur) |
| POST | `/api/tchap/salons/{id}/quitter` | groupe ; le rôle d'administrateur est transmis si besoin |
| GET | `/api/tchap/salons/{id}/messages?avant=&limit=` | `TchapMessageDto[]` (ordre chronologique ; marque lu) |
| POST | `/api/tchap/salons/{id}/messages` | `TchapMessageInput` → `TchapMessageDto` |
| DELETE | `/api/tchap/messages/{id}` | auteur ou administrateur ; retrait historisé |

## NEO (§ 16) — permission `neo.utiliser`

Pas de route propre : accueil téléphone (profil RH de la session, intervention suivie via
`GET /api/pegase/interventions/{id}/resume`, consultations rapides `GET /api/personnes/recherche` et
`GET /api/vehicules/recherche`), puis les vues MRZ, PVe, Tchap, Messagerie, Annuaire et Notes ouvertes dans NEO.
Le changement de statut et le compte rendu se font dans PÉGASE (bouton d'ouverture).
