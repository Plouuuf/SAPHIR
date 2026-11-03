# saphir-identite — déclaration d'identité en jeu pour SAPHIR

Ressource FiveM qui permet à un joueur de **déclarer son personnage RP auprès de la police** depuis le jeu : la fiche
est créée aussitôt dans SAPHIR (CHEOPS, badge « Déclarée par le joueur »). Le joueur peut aussi déclarer ses
véhicules (SIV, assurance FVA) et son permis de conduire, mettre à jour sa fiche ou retirer le personnage.

Jeu de rôle : **données fictives**. Le joueur ne doit jamais indiquer sa véritable identité.

## Installation

1. Copier le dossier `saphir-identite` dans `resources/` du serveur FiveM.
2. Renseigner `config.lua` (fichier lu **uniquement côté serveur** : la clé n'est jamais envoyée aux joueurs) :
   - `Config.SaphirUrl` : adresse du serveur SAPHIR (ex. `http://127.0.0.1:8091`) ;
   - `Config.SaphirKey` : **même valeur** que `SAPHIR:AccessKey` dans `appsettings.Local.json` du serveur SAPHIR ;
   - `Config.Framework` : `esx`, `qbcore` ou `aucun` ;
   - `Config.Commande` : nom de la commande (`identite` par défaut) ;
   - `Config.Licences` : correspondance licences du framework → catégories de permis ;
   - `Config.FormatDateEsx`, `Config.NationaliteParDefaut`, `Config.Delai`, `Config.Notification`, `Config.Dialogue`.
3. Ajouter dans `server.cfg`, **après** `oxmysql`, `es_extended` / `qb-core` et (facultatif) `ox_lib` :

   ```
   ensure saphir-identite
   ```

4. Les joueurs doivent avoir **Discord relié à FiveM** (identifiant `discord:` présent) : la fiche est rattachée à
   leur compte Discord.

### Dépendances

| Ressource | Rôle | Obligatoire |
|---|---|---|
| `oxmysql` | lecture des véhicules possédés, des licences (ESX) et de la table `users` (ESX) | oui avec ESX ou QBCore |
| `es_extended` | personnage ESX (`xPlayer.identifier`, `users` : firstname, lastname, dateofbirth, sex, height, phone_number) | si `esx` |
| `qb-core` | personnage QBCore (`PlayerData.citizenid`, `charinfo`, `metadata.licences`, `QBCore.Shared.Vehicles`) | si `qbcore` |
| `ox_lib` | boîtes de dialogue (`inputDialog`, `alertDialog`) et notifications | non : sans elle, saisie par arguments et messages dans le chat |

## Commandes

| Commande | Effet |
|---|---|
| `/identite` | ouvre la boîte de dialogue et déclare (ou met à jour) le personnage **actuellement joué** |
| `/identite maj` | idem (mise à jour) ; sans ox_lib : `/identite maj Lieu de naissance \| Adresse \| Profession \| Taille (cm) \| Corpulence \| Signes particuliers` |
| `/identite etat` | personnages déclarés du compte, fiche du personnage joué, permis et véhicules |
| `/identite vehicules` | déclare tous les véhicules possédés (ESX `owned_vehicles`, QBCore `player_vehicles`) |
| `/identite vehicules PLAQUE` | déclare un seul véhicule possédé |
| `/identite assurance PLAQUE \| ASSUREUR \| N° DE CONTRAT \| JJ/MM/AAAA` | assurance d'un véhicule possédé (boîte de dialogue sans argument) |
| `/identite vehicule-retirer PLAQUE` | retire un véhicule déclaré |
| `/identite permis [JJ/MM/AAAA]` | déclare le permis : catégories lues en jeu (ESX `user_licenses` : drive → B, drive_bike → A, drive_truck → C ; QBCore `metadata.licences.driver` → B), date d'obtention saisie |
| `/identite retirer` | retire le personnage (confirmation ; sans ox_lib : `/identite retirer confirmer`) |
| `/identite aide` | rappel des commandes |

Avec `Config.Framework = 'aucun'`, le joueur saisit aussi NOM, prénom, date de naissance et sexe (le personnage est
identifié par sa licence Rockstar) ; véhicules et permis ne sont pas disponibles.

## Données envoyées

- Lues **côté serveur** : identifiant Discord, identifiant de personnage, NOM, prénom, date de naissance, sexe,
  nationalité, téléphone, taille (ESX), véhicules possédés (plaque, modèle), licences.
- Saisies par le joueur (contrôlées par le serveur FiveM puis par SAPHIR) : lieu de naissance, adresse, profession,
  taille, corpulence, signes particuliers ; plaque, assureur, contrat et date de fin pour l'assurance ; date
  d'obtention du permis. Une réponse de boîte de dialogue n'est acceptée que si le serveur l'a demandée (3 minutes).

## Règles appliquées par SAPHIR

Résumé (détail : `docs/API-CITOYEN.md`) : 5 personnages actifs par compte Discord au plus (paramètre
`citoyen.personnages.max`) ; identité en double refusée ; NOM, prénom(s), date et lieu de naissance figés dès que la
personne figure dans un fichier de police ; retrait refusé dans ce cas ; permis à 12 points (6 en probatoire),
points jamais modifiables par le joueur ; plaque d'un autre titulaire refusée ; tout est historisé ; portail
désactivable par le paramètre `citoyen.portail.actif`.

## Test rapide

```bash
curl "http://127.0.0.1:8091/api/integrations/identite/etat?discordId=560037927695024139" -H "X-Saphir-Key: VOTRE_CLE"
```

Réponse `200` (état du compte), `401` si la clé est fausse, `503` si le portail est fermé ou la base indisponible.
