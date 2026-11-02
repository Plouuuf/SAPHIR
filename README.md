# SAPHIR

**S**ystème d'**A**dministration, de **P**ilotage, d'**H**abilitation, d'**I**nformation et de **R**enseignement :
poste de travail Police nationale du serveur RP **Val-de-Marne Simulation**. Un bureau Windows 7 avec les
logiciels métier, réservé aux membres du Discord VDMS qui ont le rôle SAPHIR.

Plateformes : **Windows**, **macOS**, **iPhone / iPad**, **Android** (.NET 9, Avalonia 11.3).

> Toutes les données sont fictives (jeu de rôle). SAPHIR n'a aucun lien avec les fichiers réels de l'État.

L'ancienne version (MEO-FIC, PEGASE 0.1, PTS) est conservée sur la branche `v0-archive`.

## Version 0.2 — socle

- **Ouverture de session Windows 7** : connexion Discord, rôle SAPHIR obligatoire (`1544429010191261778`),
  revérifié toutes les 5 minutes et en temps réel.
- **Première connexion** : NOM (en majuscules) et Prénom, demandés une seule fois. Le serveur crée la fiche agent
  (référence `AG-000001`, matricule automatique) au statut « Profil à compléter » avec le profil *Agent*.
- **Statuts** : Profil à compléter, Actif, Suspendu, Inactif, Archivé. Un compte suspendu, inactif ou archivé
  ne peut plus ouvrir de session ; son historique reste.
- **Profils et permissions** : 11 profils fournis (Agent, Chef de bord, Opérateur CIC, TN, Accueil, Enquêteur,
  OPJ, Responsable de service, Responsable RH, Documentaliste, Administrateur technique), cumulables. Chaque
  application n'apparaît qu'avec au moins une de ses permissions. Le serveur revérifie tout, même en appel direct.
- **Ressources humaines** : exige en plus le rôle Discord RH (`1544426429666689044`), contrôlé côté serveur.
- **Bureau Windows 7** : menu Démarrer avec recherche, barre des tâches Aero, fenêtres déplaçables et
  redimensionnables, zone de notification, notifications en bulle. Vue téléphone sur les petits écrans.
- **Administration** : comptes (profils, statut avec motif et confirmation), profils (permissions par module),
  paramètres centraux (version affichée, format du RIO à 7 chiffres…), historique des modifications et journal
  des accès. Garde-fou : impossible de retirer le dernier administrateur actif.
- **Centre de paramètres** : fiche de l'agent, profils, applications accessibles, version.
- Les modules métier (RH, Main courante, PÉGASE, CHEOPS, LRPPN, NEO, PVe…) arrivent dans les phases suivantes ;
  leur icône indique déjà la phase prévue.
- Icônes d'applications provisoires : remplacer les fichiers de `src/SAPHIR.App/Assets/Icons/` (mêmes noms).

## Arborescence

```text
src/SAPHIR.Shared    Contrats communs (applications, permissions, profils, session, administration)
src/SAPHIR.Server    Serveur : Discord OAuth, WebSocket, API du socle, MariaDB, migrations
src/SAPHIR.App       Interface commune (ouverture de session, bureau Windows 7, vue téléphone, applications)
src/SAPHIR.Desktop   Windows et macOS
src/SAPHIR.iOS       iPhone / iPad
src/SAPHIR.Android   Android
src/SAPHIR.Tests     Tests (contrats, règles du socle)
tools/SAPHIR.Screenshots  Captures d'écran avec données fictives (CI)
API-SOCLE.md         Routes de l'API du socle
```

## Mise en route

### 1. Application Discord

Dans le [portail développeur Discord](https://discord.com/developers/applications), ajouter l'adresse de retour
OAuth2 :

```text
http://46.105.78.32:8091/auth/discord/callback
```

### Installation automatique sur le VPS Windows

Dans PowerShell ouvert **en administrateur** :

```powershell
irm https://raw.githubusercontent.com/Plouuuf/SAPHIR/main/deploy/install-serveur.ps1 | iex
```

La première fois, le script télécharge la dernière version publiée, crée la base `saphir` et son utilisateur (il
demande le mot de passe root de MariaDB), reprend l'application Discord d'ARGOS si elle est dans `C:\ARGOS`,
écrit `C:\SAPHIR\appsettings.Local.json`, ouvre le port 8091, crée la tâche planifiée « SAPHIR Server » et lance
le serveur. Ensuite, la même commande sert à mettre à jour (les réglages sont conservés).

Les étapes manuelles ci-dessous restent valables pour une installation à la main.

### 2. Base de données (MariaDB, sur le VPS)

```sql
CREATE DATABASE saphir CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER 'saphir'@'localhost' IDENTIFIED BY 'mot-de-passe-saphir';
GRANT ALL PRIVILEGES ON saphir.* TO 'saphir'@'localhost';
FLUSH PRIVILEGES;
```

Les tables sont créées au démarrage du serveur par des migrations numérotées (jamais de suppression). Si la base
est injoignable, le serveur démarre quand même et réessaie toutes les 30 secondes.

### 3. Serveur

Créer `src/SAPHIR.Server/appsettings.Local.json` (jamais versionné) :

```json
{
  "SAPHIR": {
    "AccessKey": "une-longue-cle-aleatoire",
    "AdminDiscordIds": [ "560037927695024139" ]
  },
  "Discord": {
    "ClientId": "…",
    "ClientSecret": "…",
    "PublicBaseUrl": "http://46.105.78.32:8091"
  },
  "Database": {
    "Saphir": "Server=127.0.0.1;Database=saphir;User ID=saphir;Password=mot-de-passe-saphir"
  }
}
```

Les comptes Discord de `AdminDiscordIds` reçoivent le profil *Administrateur technique* à leur première
connexion. Le serveur, les rôles et la guilde sont déjà renseignés dans `appsettings.json`.

```bash
dotnet run --project src/SAPHIR.Server
```

Le serveur écoute sur le port **8091** (ARGOS sur 8090, SIMCALL sur 8080).

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

Identifiant d'application : `fr.vdms.saphir`.

## Tests et captures

```bash
dotnet test src/SAPHIR.Tests
dotnet run --project tools/SAPHIR.Screenshots -- captures
```

La compilation GitHub Actions produit le serveur, les postes Windows et macOS, lance les tests et publie les
captures d'écran (artefact `SAPHIR-Captures`).
