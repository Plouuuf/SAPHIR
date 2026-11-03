# saphir-geoops — positions GÉOOPS pour SAPHIR

Ressource FiveM **serveur** qui envoie toutes les 5 secondes au serveur SAPHIR la position (simulée, celle du jeu) des
joueurs **policiers en service**. Le poste SAPHIR (PÉGASE › GÉOOPS) affiche alors les équipages sur la carte, avec leur
statut, en temps réel.

## Installation

1. Copier le dossier `saphir-geoops` dans `resources/` du serveur FiveM.
2. Renseigner `config.lua` :
   - `Config.SaphirUrl` : adresse du serveur SAPHIR (ex. `http://127.0.0.1:8091`) ;
   - `Config.SaphirKey` : **même valeur** que `SAPHIR:AccessKey` dans `appsettings.Local.json` du serveur SAPHIR ;
   - `Config.Framework` : `esx`, `qbcore` ou `aucun` ;
   - `Config.Jobs` : noms des métiers police (par défaut `police`) ; `Config.ServiceSeulement` pour n'envoyer que les
     agents en service (QBCore : `job.onduty` ; ESX : `job.onDuty` si votre version le gère).
   - Sans framework : donner la permission ACE `saphir.geoops` aux policiers
     (`add_ace group.police saphir.geoops allow`).
3. Ajouter `ensure saphir-geoops` dans `server.cfg` (après `es_extended` / `qb-core`).
4. **OneSync** doit être actif (lecture des coordonnées côté serveur).

## Rattachement aux équipages

Chaque position porte l'identifiant **Discord** du joueur (`GetPlayerIdentifiers`, `discord:…`). SAPHIR le relie à
l'agent (fiche RH, même compte Discord) puis à son **équipage actif** (MAIN COURANTE › Équipages). La position affichée
pour un équipage est celle de son chef de bord, sinon du membre vu le plus récemment. Un policier en service sans
équipage apparaît en gris (« autre agent »). Sans identifiant Discord, le joueur est suivi par son numéro de serveur.

Une position n'est plus affichée au-delà de `geoops.positions.peremption` secondes (paramètre central, 60 par défaut).

## Contrat

`POST /api/integrations/geoops/positions` — en-tête `X-Saphir-Key`, corps JSON :

```json
[
  { "discordId": "560037927695024139", "serverId": 12, "nom": "DURAND Jean-Pierre",
    "x": -215.4, "y": -890.1, "z": 29.3, "cap": 90, "vitesse": 12.5, "vehicule": "GE418PN" }
]
```

Réponse : `200 { "recues": 1, "enregistrees": 1 }` ; `401` si la clé est fausse ; `503` si la base SAPHIR est indisponible.

## Fond de carte

L'image de la carte GTA se dépose sur le serveur SAPHIR dans `data/geoops/carte.jpg` (à côté de l'exécutable). Sa
calibration (coordonnées du jeu des bords de l'image) se règle dans Administration › Paramètres :
`geoops.carte.x_min`, `geoops.carte.x_max`, `geoops.carte.y_min`, `geoops.carte.y_max`. Sans image, le poste affiche
une grille de repérage graduée en coordonnées du jeu.

## Test rapide

```bash
curl -X POST http://127.0.0.1:8091/api/integrations/geoops/positions \
  -H "X-Saphir-Key: VOTRE_CLE" -H "Content-Type: application/json" \
  -d '[{"discordId":"560037927695024139","serverId":1,"nom":"Test","x":-215,"y":-890,"z":29,"cap":0,"vitesse":0,"vehicule":null}]'
```
