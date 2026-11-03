# API du socle (phase 2)

Toutes les routes /api/* exigent `Authorization: Bearer <jeton de session Discord>`.
Erreurs : JSON `{ "error": "message lisible en français" }` avec 400 (saisie), 401 (session), 403 (droit), 404, 409 (conflit), 503 (base).

| Méthode | Route | Permission | Corps → réponse |
|---|---|---|---|
| GET | /api/version | aucune (public) | → `{ "version": "SAPHIR - v1.0.0.0" }` |
| GET | /api/session | session | → `SessionInfoDto` |
| POST | /api/session/premiere-connexion | session, pas encore d'agent | `PremiereConnexionInput` → `SessionInfoDto` |
| GET | /api/admin/comptes?q=&statut= | admin.comptes | → `List<CompteDto>` |
| PUT | /api/admin/comptes/{id}/profils | admin.comptes | `ProfilsAgentInput` → 204 |
| PUT | /api/admin/comptes/{id}/statut | admin.comptes | `StatutAgentInput` → 204 |
| GET | /api/admin/profils | admin.profils ou admin.comptes | → `List<ProfilDto>` |
| POST | /api/admin/profils | admin.profils | `ProfilInput` → `ProfilDto` |
| PUT | /api/admin/profils/{id} | admin.profils | `ProfilInput` → `ProfilDto` |
| DELETE | /api/admin/profils/{id} | admin.profils | → 204 (refus 409 si profil système ou attribué) |
| GET | /api/admin/parametres | admin.parametres | → `List<ParametreDto>` |
| PUT | /api/admin/parametres/{cle} | admin.parametres | `ParametreInput` → 204 |
| GET | /api/admin/historique?objet=&reference=&limit= | admin.journaux | → `List<HistoriqueDto>` |
| GET | /api/admin/journal?limit= | admin.journaux | → `List<JournalAccesDto>` |
