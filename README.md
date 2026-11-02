# SAPHIR

**S**ystème d'**A**dministration, de **P**ilotage, d'**H**abilitation, d'**I**nformation et de **R**enseignement :
poste de travail Police nationale du serveur **Val De Marne Simulation**. Un bureau Windows 10 avec les logiciels
métier, accessible uniquement aux membres du Discord VDMS ayant le rôle Police, chaque application étant ouverte
par son propre rôle Discord.

Plateformes : **Windows**, **macOS**, **iPhone / iPad**, **Android**. Même base technique qu'ARGOS et SIMCALL
(.NET 9, Avalonia 11.3).

## Version 0.1

- Socle repris d'ARGOS : écran de verrouillage, connexion Discord, bureau Windows 10, vue téléphone, temps réel.
- Fond d'écran et icône SAPHIR. Icônes d'applications provisoires (à remplacer par les tiennes, mêmes noms de
  fichiers dans `src/SAPHIR.App/Assets/Icons/`).
- **Un rôle Discord par application** : seules les applications autorisées apparaissent sur le bureau, et la
  liste est relue toutes les 5 minutes. Une application sans rôle configuré est ouverte à tous les agents.
- **MEO-FIC** complet :
  - Personnes : **TAJ** (antécédents, mis en cause / victime), **FPR** (fiches, conduite à tenir, dangereux /
    armé, clôture), **SNPC** (statut, catégories, solde de points).
  - Véhicules : **FOVeS** (signalements, clôture), **SIV** (immatriculation, titulaire), **FVA** (assurance).
  - Identités et véhicules lus dans la **base FiveM** (ESX ou QBCore, lecture seule), complétés dans SAPHIR ;
    fiches pour les personnes inconnues du serveur.
  - Bandeau rouge dès qu'une personne est au FPR ou un véhicule au FOVeS, quel que soit le fichier consulté.
  - Passage personne → véhicules et véhicule → titulaire.
  - **Journal** de toutes les consultations et inscriptions (administration), suppression réservée à
    l'administration.
- **PEGASE** (prise des appels 17) :
  - Au **décroché d'un 17 sur SIMCALL**, PEGASE s'ouvre sur le poste SAPHIR de l'opérateur avec la prise d'appel
    déjà remplie (heure, numéro du requérant, chrono de l'appel figé au raccroché). Bouton « Nouvel appel 17 »
    pour une saisie manuelle.
  - Prise d'appel : localisation, nature, priorité P1 à P3, requérant, motif ; puis **créer l'événement**,
    **clore sans suite** (renseignement, réorientation, malveillant…) ou **mettre en attente**.
  - Événements : liste par priorité, fiche, modification, **chronologie** (messages, engagements, statuts),
    clôture avec compte rendu. Notification à tous les postes pour un P1.
  - **Équipages** : indicatif, type, effectif ; engagement sur un événement, statuts Disponible / Engagé /
    Sur place / Indisponible / Hors service. Tout se met à jour en temps réel sur tous les postes.
- **PTS** (police technique et scientifique), même écran que MEO-FIC :
  - **FAED** (signalisations, empreintes) et **FNAEG** (profils ADN) d'une personne.
  - **FIJAIS** et **FIJAIT** : inscriptions avec décision, juridiction, obligations, fin d'inscription ;
    bandeau violet sur la fiche.
  - **Traces** relevées sur les scènes (papillaires ou ADN), puis **rapprochement** avec une personne.
- Les autres applications (GÉOOPS, Main courante, LRPPN, NEO, Video Manager, PVe, Mugshot, Garde à vue,
  Armurerie) affichent « arrive dans une prochaine version ».

## Arborescence

```text
src/SAPHIR.Shared    Contrats communs (connexion, applications, MEO-FIC)
src/SAPHIR.Server    Serveur : Discord OAuth, WebSocket, API MEO-FIC, MariaDB
src/SAPHIR.App       Interface commune : écrans, bureau, vue mobile, applications
src/SAPHIR.Desktop   Exécutable Windows + macOS
src/SAPHIR.iOS       iPhone + iPad
src/SAPHIR.Android   Android (APK)
```

## Mise en route

### 1. Application Discord

Dans le [portail développeur Discord](https://discord.com/developers/applications), réutiliser l'application
d'ARGOS (ou en créer une) et ajouter l'adresse de retour OAuth2 :

```text
http://46.105.78.32:8091/auth/discord/callback
```

### 2. Bases de données (MariaDB, sur le VPS)

```sql
CREATE DATABASE saphir CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER 'saphir'@'localhost' IDENTIFIED BY 'mot-de-passe-saphir';
GRANT ALL PRIVILEGES ON saphir.* TO 'saphir'@'localhost';

-- Lecture seule de la base FiveM (remplacer « fivem » par le nom de ta base)
CREATE USER 'saphir_ro'@'localhost' IDENTIFIED BY 'mot-de-passe-lecture';
GRANT SELECT ON fivem.* TO 'saphir_ro'@'localhost';
FLUSH PRIVILEGES;
```

Les tables SAPHIR (`saphir_*` pour MEO-FIC, `pegase_*` pour PEGASE, `pts_*` pour PTS) sont créées
automatiquement au démarrage du serveur.

### 3. Serveur

Créer `src/SAPHIR.Server/appsettings.Local.json` (jamais versionné) :

```json
{
  "SAPHIR": { "AccessKey": "une-longue-cle-aleatoire" },
  "Discord": {
    "ClientId": "…",
    "ClientSecret": "…",
    "PublicBaseUrl": "http://46.105.78.32:8091",
    "RequiredRoleId": "ID du rôle Police",
    "LevelRoles": {
      "meo-fic": "ID du rôle MEO-FIC",
      "superviseur": "ID des rôles chef de poste, officier (séparés par des virgules)",
      "administration": "ID du rôle développeur"
    }
  },
  "Database": {
    "Saphir": "Server=127.0.0.1;Database=saphir;User ID=saphir;Password=mot-de-passe-saphir",
    "FiveM": "Server=127.0.0.1;Database=fivem;User ID=saphir_ro;Password=mot-de-passe-lecture",
    "Framework": "esx"
  }
}
```

- `Framework` : `esx`, `qbcore`, ou `aucun` (pas de lecture FiveM).
- Rôles d'application disponibles dans `LevelRoles` : `meo-fic`, `scientifique`, `pegase`, `geoops`,
  `main-courante`, `lrppn`, `neo`, `bodycam`, `pve`, `mugshot`, `garde-a-vue`, `armurerie`.
  Laisser vide = application ouverte à tous les agents. `administration` voit tout.

Puis :

```bash
dotnet run --project src/SAPHIR.Server
```

Le serveur écoute sur le port **8091** (ARGOS reste sur 8090, SIMCALL sur 8080).

### 4. Lien avec SIMCALL (PEGASE)

SIMCALL 0.7.3 ou plus (PR « Lien SAPHIR » sur le dépôt SIMCALL). Dans `C:\SIMCALL\Server\appsettings.json` :

```json
"Saphir": { "Url": "http://127.0.0.1:8091", "Key": "la même valeur que SAPHIR:AccessKey" }
```

Puis redémarrer le service SIMCALL. Au décroché d'un 17, la prise d'appel s'ouvre sur le poste SAPHIR de
l'opérateur (même compte Discord, rôle `pegase`).

### 5. Poste ordinateur

```bash
dotnet run --project src/SAPHIR.Desktop
```

SAPHIR s'ouvre en plein écran ; **F11** bascule en fenêtre. Pour viser un serveur de test, placer un
`saphir.local.json` à côté de l'exécutable : `{ "ServerUrl": "http://127.0.0.1:8091" }`.

### 6. iPhone / iPad et Android

```bash
dotnet workload install ios android
dotnet build src/SAPHIR.iOS -c Release -f net9.0-ios -r iossimulator-arm64
dotnet publish src/SAPHIR.Android -c Release -f net9.0-android
```

Identifiant d'application : `fr.vdms.saphir` (profil de provisionnement à créer sur le compte Apple Developer).

## Ce que lit SAPHIR dans la base FiveM

| | ESX | QBCore |
|---|---|---|
| Identités | `users` (identifier, firstname, lastname, dateofbirth, sex, height, phone_number) | `players` (citizenid, charinfo) |
| Véhicules | `owned_vehicles` (+ noms de modèles via `vehicles`) | `player_vehicles` |
| Permis | `user_licenses` (drive → B, drive_bike → A, drive_truck → C) | `players.metadata.licences` |

Les colonnes absentes sont ignorées. Si la base FiveM est injoignable, seules les fiches SAPHIR s'affichent.

## API MEO-FIC

Jeton de session Discord dans `Authorization: Bearer …`.

| Méthode | Adresse | Rôle |
|---|---|---|
| GET | `/api/meo/personnes?q=` | Recherche (nom, prénom, date de naissance) |
| GET | `/api/meo/personne?key=` | Fiche complète (TAJ, FPR, SNPC, véhicules) |
| POST | `/api/meo/personnes` | Nouvelle fiche personne |
| PUT | `/api/meo/personne/identite?key=` | Identité et signalement |
| POST | `/api/meo/personne/taj?key=` | Inscription TAJ |
| POST | `/api/meo/personne/fpr?key=` | Inscription FPR |
| PUT | `/api/meo/personne/permis?key=` | SNPC |
| POST | `/api/meo/fpr/{id}/clore` | Clôture FPR |
| GET | `/api/meo/vehicules?q=` | Recherche par plaque |
| GET | `/api/meo/vehicule?plaque=` | SIV, FVA, FOVeS |
| PUT | `/api/meo/vehicule` | SIV / FVA |
| POST | `/api/meo/vehicule/foves?plaque=` | Signalement FOVeS |
| POST | `/api/meo/foves/{id}/clore` | Clôture FOVeS |
| DELETE | `/api/meo/inscription/{taj\|fpr\|foves}/{id}` | Suppression (administration) |
| GET | `/api/meo/journal` | Journal (administration) |

## API PEGASE et PTS

PEGASE : `GET /api/pegase/etat`, `POST /api/pegase/appels`, `GET /api/pegase/appel/{id}`,
`POST /api/pegase/appel/{id}/evenement`, `POST /api/pegase/appel/{id}/sans-suite`, `GET|PUT /api/pegase/evenement/{id}`,
`POST /api/pegase/evenement/{id}/message|engager|clore`, `POST /api/pegase/equipages`,
`PUT|DELETE /api/pegase/equipage/{id}`, `POST /api/pegase/equipage/{id}/statut`.

SIMCALL (clé `X-Saphir-Key`) : `POST /api/integrations/simcall/answered`, `POST /api/integrations/simcall/ended`.

PTS : `GET /api/pts/personne?key=`, `POST /api/pts/personne/signalisation|inscription?key=`, `GET /api/pts/traces`
(`?toutes=true`), `POST /api/pts/traces`, `POST /api/pts/trace/{id}/rapprocher|dissocier`,
`DELETE /api/pts/{signalisation|inscription|trace}/{id}` (administration).

## Simuler un appel 17

```bash
curl -X POST http://127.0.0.1:8091/api/integrations/simcall/answered \
  -H "X-Saphir-Key: une-longue-cle-aleatoire" -H "Content-Type: application/json" \
  -d '{"simcallCallId":"test-001","discordUserId":"TON_ID_DISCORD","operatorName":"Opérateur test","callerNumber":"0601020304","answeredAt":"2026-11-02T10:00:00Z"}'
```

## Tester une notification

```bash
curl -X POST http://127.0.0.1:8091/api/integrations/notify \
  -H "X-Saphir-Key: une-longue-cle-aleatoire" -H "Content-Type: application/json" \
  -d '{"type":"notify","title":"PEGASE","text":"Appel 17 entrant","appId":"pegase"}'
```
