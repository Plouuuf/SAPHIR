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
- Les autres applications (PTS, PEGASE, GÉOOPS, Main courante, LRPPN, NEO, Video Manager, PVe, Mugshot,
  Garde à vue, Armurerie) affichent « arrive dans une prochaine version ».

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

Les tables SAPHIR (`saphir_personnes`, `saphir_taj`, `saphir_fpr`, `saphir_permis`, `saphir_vehicules`,
`saphir_foves`, `saphir_journal`) sont créées automatiquement au démarrage du serveur.

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

### 4. Poste ordinateur

```bash
dotnet run --project src/SAPHIR.Desktop
```

SAPHIR s'ouvre en plein écran ; **F11** bascule en fenêtre. Pour viser un serveur de test, placer un
`saphir.local.json` à côté de l'exécutable : `{ "ServerUrl": "http://127.0.0.1:8091" }`.

### 5. iPhone / iPad et Android

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

## Tester une notification

```bash
curl -X POST http://127.0.0.1:8091/api/integrations/notify \
  -H "X-Saphir-Key: une-longue-cle-aleatoire" -H "Content-Type: application/json" \
  -d '{"type":"notify","title":"PEGASE","text":"Appel 17 entrant","appId":"pegase"}'
```
