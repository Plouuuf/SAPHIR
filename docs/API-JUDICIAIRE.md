# API du module Judiciaire (CHEOPS, LRPPN)

Toutes les routes exigent le jeton de session (`Authorization: Bearer …`) et passent par `Api.RunAsync` :
agent existant et actif, **au moins une** des permissions indiquées. Erreurs : `{ "error": "…" }`
(400 saisie, 403 droits, 404 introuvable, 409 conflit / acte figé / version dépassée, 503 base).
Contrats : `SAPHIR.Shared/Contracts/Judiciaire/*.cs` et `Contracts/Referentiels.cs`. Données **fictives**.

Abréviations de permissions : `CHEOPS*` = n'importe quelle permission `cheops.*` ;
`LECTURE` = `lrppn.consulter` | `lrppn.acte.rediger` | `lrppn.acte.valider` ;
`ECRITURE-REF` = `lrppn.acte.rediger` | `cheops.fpr.inscrire` (identités, véhicules, permis, assurances).

Consultation sensible = ligne écrite dans le journal des accès (`sys_journal_acces`, action « Consultation … »).
Toute modification = lignes dans `sys_historique` (objets `personne`, `vehicule`, `taj`, `fpr`, `foves`, `permis`,
`scientifique`, `fijais`, `decision`, `procedure`, `acte`, `piece`, `modele`).

Temps réel (diffusé à tous, `Text` = identifiant seulement) : `cheops-changed` (AppId `cheops`), `lrppn-changed`
(AppId `lrppn`, Text = id de procédure). Notification `notify` au rédacteur quand son acte est validé ou renvoyé.

## Routes partagées (Referentiels.cs)

Permissions : `CHEOPS*`, `lrppn.consulter`, `lrppn.acte.rediger`, `pegase.appel.creer`, `pegase.consulter`,
`neo.utiliser`, `mrz.utiliser`, `pve.rediger`. `Alertes` = codes autorisés pour l'agent uniquement
(`FPR`, `TAJ`, `FIJAISV`, `FIJAIT`, `FAED`, `FNAEG`, `SNPC` ; véhicules : `FOVeS`, `SIV`, `FVA`, `FPR` du titulaire).

| Méthode | Route | Corps / réponse |
|---|---|---|
| GET | `/api/personnes/recherche?q=` | `PersonneResumeDto[]` — termes : NOM, prénom, alias, `PER-000001`, date `jj/mm/aaaa` (import FiveM à la volée) |
| GET | `/api/personnes/{id}/resume` | `PersonneResumeDto` |
| POST | `/api/personnes` | `PersonneCreationInput` → `PersonneResumeDto` ; 409 si même NOM + prénom + naissance |
| GET | `/api/vehicules/recherche?q=` | `VehiculeResumeDto[]` (plaque, VIN, marque/modèle, titulaire) |
| GET | `/api/vehicules/{id}/resume` | `VehiculeResumeDto` |

## CHEOPS (`/api/cheops`)

| Méthode | Route | Permissions | Notes |
|---|---|---|---|
| GET | `/recherche?nom&prenom&alias&naissance&lieu&identifiant&dossier&immatriculation` | CHEOPS* | `RechercheGeneraleDto`, journalisée ; dossiers filtrés par droits |
| GET | `/personnes/{id}` | CHEOPS*, LECTURE | `PersonneFicheDto` (alias, documents, alertes, bandeaux FPR), journalisée |
| PUT | `/personnes/{id}` | ECRITURE-REF | `PersonneModificationInput` (historisé champ par champ) |
| POST | `/personnes/{id}/documents` | ECRITURE-REF | `DocumentIdentiteInput` → fiche |
| GET | `/taj?q&naissance&reference&procedure` | `cheops.taj` | `TajLigneDto[]`, journalisée |
| GET | `/taj/personnes/{id}` | `cheops.taj` | `TajFicheDto`, journalisée |
| POST | `/taj` | `lrppn.acte.valider` (+ `cheops.taj`) | `TajFaitInput` → `{ reference }` (inscription manuelle) |
| PUT | `/taj/{id}/suite` | idem | `TajSuiteInput` |
| GET | `/fpr?q&statut` | `cheops.fpr` | `FprFicheDto[]` |
| GET | `/fpr/{id}` | `cheops.fpr` | journalisée, avec historique |
| POST | `/fpr` | `cheops.fpr.inscrire` | `FprInput` → fiche (statut Active) |
| PUT | `/fpr/{id}/statut` | `cheops.fpr.inscrire` | `StatutInput` ; Active→Suspendue/Levée, Suspendue→Active/Levée, Levée→Archivée ; motif obligatoire pour suspendre/lever |
| GET | `/foves?q&statut`, `/foves/{id}` | `cheops.vehicules` | fiche journalisée |
| POST | `/foves` | `cheops.fpr.inscrire`, `lrppn.acte.rediger` | `FovesInput` ; 409 si signalement actif existant |
| PUT | `/foves/{id}/statut` | idem | `FovesStatutInput` (découverte : date + lieu) |
| GET | `/siv?q`, `/siv/{id}` | `cheops.vehicules` | `VehiculeFicheDto` (bandeaux, assurances, signalements, historique), journalisées |
| POST / PUT | `/siv`, `/siv/{id}` | ECRITURE-REF | `VehiculeInput` → fiche ; plaque unique |
| GET | `/fva?q` | `cheops.vehicules` | `FvaLigneDto[]`, journalisée |
| POST | `/fva` | ECRITURE-REF | `AssuranceInput` |
| POST | `/fva/{id}/verifier` | `cheops.vehicules` | date de vérification |
| GET | `/snpc?q`, `/snpc/personnes/{id}` | `cheops.snpc` | `PermisDto` ; 404 si aucun permis |
| PUT | `/snpc/personnes/{id}` | ECRITURE-REF | `PermisInput` (création ou modification) |
| POST | `/snpc/personnes/{id}/points` | ECRITURE-REF | `PointsInput` (solde borné 0–12, solde nul → « Invalidé ») |
| GET | `/scientifique?fichier=FAED|FNAEG&q`, `/scientifique/{id}` | `cheops.scientifique` | journalisées |
| POST | `/scientifique` | `cheops.scientifique` | `ScientifiqueInput` |
| PUT | `/scientifique/{id}/resultat` | `cheops.scientifique` | `ResultatInput` (concordance = personne obligatoire) |
| GET | `/fijais?fichier=FIJAISV|FIJAIT&q`, `/fijais/{id}` | `cheops.fijais` | toutes journalisées |
| POST / PUT | `/fijais`, `/fijais/{id}/statut` | `cheops.fijais` **et** `lrppn.acte.valider` | inscription manuelle seulement (jamais depuis une interpellation) |
| GET | `/casier/{personneId}` | `cheops.fijais`, `lrppn.acte.valider` | `DecisionDto[]`, journalisée |
| POST | `/casier` | `lrppn.acte.valider` | `DecisionInput` (décision judiciaire fictive) |
| GET | `/anadoc?procedure&pv&type&du&au&service&redacteur&personne&motsCles` | `cheops.anadoc` | `AnadocResultatDto[]`, journalisée |
| GET | `/anadoc/{id}` | `cheops.anadoc` | `AnadocDocumentDto` (texte figé), journalisée |

## LRPPN (`/api/lrppn`)

| Méthode | Route | Permissions | Notes |
|---|---|---|---|
| GET | `/procedures?q&statut&miennes&archives&interventionId` | LECTURE | `ProcedureLigneDto[]` |
| POST | `/procedures` | `lrppn.acte.rediger` | `ProcedureInput` → `ProcedureDto` ; numéro `2026/000123` ; responsable lu dans `rh_agents` |
| GET | `/procedures/{id}` | LECTURE | `ProcedureDto` (personnes, véhicules, actes, pièces, liens, validations) |
| PUT | `/procedures/{id}` | `lrppn.acte.rediger` | `ProcedureInput` avec `Version` (409 si modifiée entre-temps) |
| PUT | `/procedures/{id}/statut` | `lrppn.acte.valider` | `StatutInput` ; clôture/transmission refusées s'il reste des actes non validés ; archivage après clôture |
| POST / DELETE | `/procedures/{id}/personnes`, `/procedures/{id}/personnes/{lienId}?motif=` | `lrppn.acte.rediger` | `ProcedurePersonneInput` → `ProcedureDto` ; retrait refusé si la personne est citée dans un acte validé |
| POST / DELETE | `/procedures/{id}/vehicules`, `…/vehicules/{lienId}` | `lrppn.acte.rediger` | `ProcedureVehiculeInput` |
| POST / DELETE | `/procedures/{id}/liens`, `…/liens/{lienId}` | `lrppn.acte.rediger` | `ProcedureLienInput { numero }` |
| GET | `/procedures/{id}/historique` | LECTURE | procédure + ses actes et pièces |
| GET | `/procedures/{id}/pdf[?format=base64]` | LECTURE | PDF (ou `FichierDto`), journalisé |
| POST | `/procedures/{id}/actes` | `lrppn.acte.rediger` | `ActeCreationInput { type, personneId }` → `ActeDto` prérempli (rédacteur RH, procédure, intervention, identité) |
| GET | `/actes?statut&miens&q` | LECTURE | `ActeLigneDto[]` |
| GET | `/actes/{id}` | LECTURE | `ActeDto` |
| PUT | `/actes/{id}` | `lrppn.acte.rediger` | `ActeInput` avec `Version` ; 409 si acte validé (figé) |
| DELETE | `/actes/{id}` | `lrppn.acte.rediger` | brouillon seulement (rédacteur ou valideur) |
| POST | `/actes/{id}/relecture` | `lrppn.acte.rediger` | Brouillon → En relecture |
| POST | `/actes/{id}/correction` | `lrppn.acte.valider` | `MotifInput` ; En relecture → Brouillon |
| POST | `/actes/{id}/valider` | `lrppn.acte.valider` | contrôles, texte + PDF figés, empreinte SHA-256, historique « Validation », index ANADOC, inscription TAJ du mis en cause (PVI, plainte) |
| POST | `/actes/{id}/mentions` | `lrppn.acte.rediger`, `lrppn.acte.valider` | `MentionInput` (acte validé seulement) |
| GET | `/actes/{id}/apercu` | LECTURE | `ApercuDto` |
| GET | `/actes/{id}/pdf[?format=base64]` | LECTURE | PDF figé, ou projet filigrané « PROJET » ; journalisé |
| GET | `/actes/{id}/historique` | LECTURE | `HistoriqueDto[]` |
| POST | `/procedures/{id}/pieces` | `lrppn.pieces` | `PieceInput` (fichier base64 ≤ 10 Mo, extensions et signatures contrôlées) → `PieceDto` ; indexée ANADOC |
| GET | `/pieces?q&categorie` | LECTURE, `lrppn.pieces` | `PieceDto[]` |
| GET | `/pieces/{id}/fichier[?format=base64]` | LECTURE, `lrppn.pieces` | fichier, journalisé |
| POST | `/pieces/{id}/mouvements` | `lrppn.pieces` | `MouvementInput` |
| GET | `/personnes/{id}/dossiers` | LECTURE, CHEOPS* | `DossiersAssociesDto` (listes nulles = non autorisé), journalisée |
| GET | `/historique?limit=` | LECTURE | historique procédures/actes/pièces/modèles |
| GET | `/modeles` | LECTURE | `ModeleDto[]` (version 0 = modèle par défaut) |
| PUT | `/modeles/{type}` | `lrppn.acte.valider` | `ModeleInput` → nouvelle version (`lr_modeles_versions`) |

## Tables (migrations 500, 510, 520, 590)

`per_personnes`, `per_alias`, `per_documents`, `veh_vehicules`, `veh_assurances` ; `ch_taj`, `ch_fpr`, `ch_foves`,
`ch_permis`, `ch_scientifique`, `ch_fijais`, `ch_decisions`, `ch_anadoc` ; `lr_procedures`, `lr_procedure_personnes`,
`lr_procedure_vehicules`, `lr_procedure_liens`, `lr_actes`, `lr_actes_versions`, `lr_actes_mentions`, `lr_pieces`
(fichier en MEDIUMBLOB), `lr_pieces_mouvements`, `lr_modeles`, `lr_modeles_versions`. Migration 590 : jeu de données fictives.

## Configuration facultative (lecture FiveM)

```json
"FiveM": { "ConnectionString": "Server=…;Database=…;User=lecture;Password=…", "Framework": "esx" }
```
`esx` ou `qbcore` ; vide = pas de lecture. Utilisateur MariaDB en lecture seule conseillé. Les personnages trouvés
reçoivent une fiche SAPHIR (`source = FiveM`) avec un identifiant `PER-…` stable.
