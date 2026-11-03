# Déclaration d'identité en jeu (API citoyen)

Un joueur civil déclare lui-même, **depuis le jeu**, ses personnages RP pour qu'ils existent dans les fichiers de la
police : commande `/identite` de la ressource FiveM [`fivem/saphir-identite`](../fivem/saphir-identite/README.md).
La fiche apparaît **aussitôt** dans CHEOPS avec le badge « Déclarée par le joueur ». Données **fictives** (jeu de rôle).

Le serveur FiveM lit lui-même les données du personnage (framework ESX / QBCore, base FiveM) et appelle SAPHIR ; le
client du joueur ne fournit que les champs libres (lieu de naissance, adresse, profession, signalement).

Code : `src/SAPHIR.Server/Modules/Judiciaire/Citoyen*.cs` ; contrats : `SAPHIR.Shared/Contracts/Judiciaire/CitoyenContracts.cs`.

## Authentification

Routes serveur à serveur : en-tête `X-Saphir-Key` = `SAPHIR:AccessKey` (comme GÉOOPS et SIMCALL), comparaison à
temps constant. Chaque corps porte le joueur :

- `discordId` : identifiant Discord du joueur (`GetPlayerIdentifiers` → `discord:…`), 15 à 22 chiffres ;
- `personnageId` : identifiant du personnage dans le framework (QBCore `citizenid`, ESX `identifier`) — c'est la
  colonne `per_personnes.fivem_id`, déjà utilisée par la lecture FiveM (`FiveMLecture.cs`) ;
- `nomJoueur` : nom affiché du joueur (historique).

Un joueur n'agit que sur **ses** personnages : même `personnageId` **et** même `discordId`, sinon `404`.

Erreurs : `{ "error": "…" }` en français — `400` saisie, `401` clé absente ou fausse, `404` personnage ou véhicule
d'un autre joueur, `409` règle métier (maximum, doublon, retrait refusé…), `503` portail fermé ou base indisponible.

## Routes (`/api/integrations/identite`)

| Méthode | Route | Corps | Réponse |
|---|---|---|---|
| GET | `/etat?discordId=&personnageId=` | — | `CitoyenEtatDto` : personnages actifs du compte, maximum, fiche du personnage joué (ou `null`) |
| POST | `/declarer` | `CitoyenIdentiteInput` | `CitoyenResultatDto` (création **ou** mise à jour, avertissements) |
| POST | `/retirer` | `CitoyenJoueurInput` | `{ message }` |
| POST | `/vehicules` | `CitoyenVehiculesInput` (1 à 30 véhicules) | `CitoyenVehiculesResultatDto` : statut par plaque (`Déclaré`, `Mis à jour`, `Refusé` + raison) |
| POST | `/vehicules/retirer` | `CitoyenVehiculeRetraitInput` | `{ message }` |
| POST | `/permis` | `CitoyenPermisInput` | `CitoyenResultatDto` |

Exemple :

```bash
curl -X POST http://127.0.0.1:8091/api/integrations/identite/declarer \
  -H "X-Saphir-Key: VOTRE_CLE" -H "Content-Type: application/json" \
  -d '{"discordId":"560037927695024139","personnageId":"ABC12345","nomJoueur":"Lea",
       "nom":"Martin","prenom":"Léa","dateNaissance":"1998-03-14","lieuNaissance":"Créteil","sexe":"1",
       "nationalite":"Française","adresse":"3 rue de Paris, 94000 Créteil","telephone":"06 12 34 56 78",
       "profession":"Infirmière","tailleCm":172,"corpulence":"Mince","signesParticuliers":"Tatouage poignet"}'
```

Formats : dates `jj/mm/aaaa` ou `aaaa-mm-jj` ; sexe `M`, `F`, `X` (accepte aussi `m`/`f` ESX et `0`/`1` QBCore) ;
corpulence `Mince`, `Moyenne`, `Forte`, `Athlétique`, `Corpulente` ; catégories de permis `AM, A1, A2, A, B1, B, BE,
C1, C, CE, D1, D, DE`. Un champ libre absent ou vide conserve la valeur de la fiche.

## Règles

- **Portail** : paramètre central `citoyen.portail.actif` (« oui » / « non », Administration › Paramètres). Fermé :
  toutes les routes répondent `503`.
- **Nombre de personnages** : au plus `citoyen.personnages.max` (5 par défaut) personnages **actifs** par compte
  Discord ; un personnage retiré libère une place.
- **Doublon** : même NOM, prénom et date de naissance qu'une autre fiche active → `409`.
- **Réconciliation** : si une fiche porte déjà ce `personnageId` (import FiveM, ou fiche créée par la police avec le
  même identifiant), elle est **reprise et mise à jour** (même référence `PER-…`), jamais dupliquée. Un personnage
  déjà déclaré par un autre compte Discord → `409`.
- **Identité figée** : dès que la personne figure dans un fichier de police ou une procédure (TAJ, FPR, FIJAIS,
  casier, FAED/FNAEG, procédure ou acte LRPPN, mention de main courante, intervention PÉGASE, avis de contravention,
  contrôle MRZ), NOM, prénom(s), date et lieu de naissance ne changent plus : la déclaration est acceptée pour le reste
  et la réponse contient un avertissement. Adresse, téléphone, profession et signalement restent modifiables.
- **Retrait** : retrait logique (`retire_le`), fiche conservée et consultable par son identifiant, mais **exclue des
  recherches** (CHEOPS, `/api/personnes/recherche`, SNPC). Refusé (`409`) si la personne ou l'un de ses véhicules
  figure dans un fichier de police ou une procédure. Les véhicules déclarés par le joueur sont retirés avec elle.
  Une nouvelle déclaration du même personnage le rétablit (dans la limite du maximum).
- **Permis** : création à 12 points (situation « Valide »), ou 6 points « Probatoire » si obtenu il y a moins de
  3 ans. Le joueur ne modifie jamais les points ni la situation (seulement catégories et date d'obtention).
- **Véhicules** : titulaire = le personnage. Une plaque déjà enregistrée au nom d'un autre titulaire est refusée ; une
  plaque sans titulaire (import FiveM) est rattachée au personnage. Assurance facultative (assureur + contrat + date
  de fin → contrat FVA « Valide » ; même numéro de contrat = mise à jour). Le joueur ne voit ni ne modifie le FOVeS,
  la situation SIV ou les fichiers de police. Retrait d'un véhicule refusé s'il figure au FOVeS, dans une procédure,
  une intervention ou un avis de contravention.
- **Traçabilité** : chaque création, modification et retrait est inscrit dans `sys_historique` (objets `personne`,
  `vehicule`, `permis`) avec l'auteur « Joueur (Discord <nom>, <identifiant>) ». Message temps réel `cheops-changed`.

## Côté police (CHEOPS)

- `PersonneFicheDto.DeclareeParJoueur`, `Profession`, `RetireeLe` (propriétés ajoutées, signature inchangée) : badge
  « Déclarée par le joueur », profession, bandeau « Personnage retiré par le joueur ».
- `RechercheGeneraleDto.DeclareesParJoueur` : identifiants des personnes déclarées (colonne « Origine » des résultats).
- `PersonneResumeDto` (contrat partagé) est inchangé.

## Données (migration 530)

`per_personnes` : `discord_proprietaire`, `declaree_le`, `retire_le`, `profession`, `taille_cm`, `corpulence`,
`signes_particuliers` (+ index `ix_per_personnes_discord`) ; `source = 'Joueur'` pour une fiche créée en jeu (les
fiches reprises gardent leur source). `veh_vehicules` : `declare_le`, `retire_le`. Paramètres `citoyen.portail.actif`
(`oui`) et `citoyen.personnages.max` (`5`). Le signalement lisible (`signalement`) est recomposé à partir de la
taille, de la corpulence et des signes particuliers quand le joueur les modifie.
