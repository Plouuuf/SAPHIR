# Consignes communes aux agents des modules SAPHIR (phases 3 à 8)

SAPHIR = poste de travail Police nationale du serveur RP FiveM « Val-de-Marne Simulation » (VDMS). Données
**fictives** uniquement (jeu de rôle). Cahier des charges complet : `/mnt/user-data/uploads/d99d0618-message.txt`
(lis les sections de ton module + 20 Interconnexions, 21 Base de données, 22 Sécurité, 25 Règles). Tout en français
(code métier, libellés, messages, commentaires).

Arbre de travail partagé : `/home/claude/s2` (pas un dépôt git ; d'autres agents écrivent en même temps dans
d'autres dossiers). **Aucun outil .NET ici** (ni dotnet ni NuGet) : tu ne peux pas compiler. Relis chaque fichier
avec soin ; la compilation se fera sur GitHub Actions après ton travail.

## Ce que tu peux modifier

Seulement **tes** fichiers :
- Serveur : `src/SAPHIR.Server/Modules/<TonModule>/**` (le fichier `<TonModule>Module.cs` existe : remplis
  `Migrations`, `AddServices`, `Map`). Autres fichiers libres dans ce dossier.
- Contrats : `src/SAPHIR.Shared/Contracts/<TonModule>/**` (namespace `Saphir.Shared.Contracts.<TonModule>`).
- Poste : `src/SAPHIR.App/ViewModels/Apps/<TonModule>/**`, `src/SAPHIR.App/Views/Apps/<TonModule>/**`
  (le fichier `<TonModule>AppFactory.cs` existe : branche tes vues par identifiant d'application).
- Captures : `tools/SAPHIR.Screenshots/Modules/<TonModule>Captures.cs` (réponses fictives `Sample` + `Run`).
- Tests : `src/SAPHIR.Tests/<TonModule>Tests.cs` (xUnit, règles pures sans base).
- Doc : `docs/API-<TONMODULE>.md` (routes, corps, permissions).
- Ressources : `src/SAPHIR.App/Assets/<TonModule>/**` si besoin ; `fivem/**` pour un script FiveM (agent Opérations).

**Ne modifie aucun autre fichier.** Si tu as besoin d'un changement ailleurs (permission manquante, champ de
contrat commun, service du socle), décris-le précisément dans ton rapport final : je l'intégrerai.

## Socle disponible (à lire avant d'écrire)

- Permissions : `src/SAPHIR.Shared/Contracts/Habilitations.cs` (`Permissions.*`, toutes déjà définies).
  Identifiants d'applications : `Contracts/SaphirApps.cs`. Agents : `AgentDto` dans `Contracts/AuthContracts.cs`.
- Serveur :
  - `Api.RunAsync(http, [Permissions.X, …], async (ctx, ct) => …, requireRhRole: false)` : vérifie session,
    agent, statut, permission (au moins une) ; `ctx.Agent` (AgentDto), `ctx.Auteur` (pour l'historique),
    `ctx.Droits.Possede(code)`. Erreurs métier : `throw ApiException.Saisie/Introuvable/Conflit("message")`.
    Exemple complet : `SocleEndpoints.cs`, `Data/AdminStore.cs`.
  - Base : service `SaphirDb` → `await using var db = await saphirDb.OpenAsync(ct);` puis `SqlSession` :
    `QueryAsync(sql, r => …, ct, ("@p", v))`, `FirstOrDefaultAsync`, `ExecuteAsync`, `InsertAsync` (renvoie l'id),
    `LongAsync`, `BeginAsync/CommitAsync/RollbackAsync`. Lecture : `SqlRow.Text/NullableText/Long/Int/Bool/Date…`.
    **Toujours des paramètres** (jamais de concaténation de valeurs dans le SQL).
  - Migrations : `new Migration(numéro, "nom", [ "CREATE TABLE IF NOT EXISTS …" ])`, rejouables, jamais de DROP,
    **pas de clé étrangère vers un autre module** (stocke l'id). Plage de numéros : RH 100-199, Opérations
    300-499, Judiciaire 500-699, Compléments 800-899. Suffixe de table :
    `ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci`. Préfixes : RH `rh_`, Opérations
    `mc_`/`pg_`/`geo_`, Judiciaire `per_`/`veh_`/`ch_`/`lr_`, Compléments `app_`/`pve_`.
  - Historique : `Historique.EcrireAsync(db, ctx.Auteur, objet, reference, action, champ, ancienne, nouvelle, motif, ct)`
    pour **toute** modification (règle du cahier). Journal des accès (consultations sensibles) :
    service `JournalAcces.EcrireAsync(agentId, agentLibelle, action, cible, resultat)`.
  - Temps réel : service `RealtimeHub` → `BroadcastAsync(new RealtimeMessage(type, Title:…, Text:…, AppId:…))`
    ou `SendToUserAsync(discordId, …)`. Définis tes types (chaînes) dans tes contrats ; le poste recharge les
    données à réception (pas de données personnelles dans Text, juste un identifiant).
  - Paramètres centraux : service `ParametresService` (voir le fichier) si utile.
  - Identité des agents : table `rh_agents` (id, reference, discord_id, nom, prenom, rio, matricule, statut…).
    Seul l'agent RH modifie `rh_agents`/`AgentStore` ; les autres **lisent** (jointures permises en lecture).
- Poste (Avalonia 11.3, liaisons par réflexion, `AvaloniaUseCompiledBindingsByDefault=false`) :
  - Modèle à suivre : `ViewModels/Apps/AdministrationViewModel.cs` + `Views/Apps/AdministrationView.axaml(.cs)`.
  - `AppViewModelBase` : `Api` (ApiClient), `RunAsync(async () => …, "message de succès")`,
    `ConfirmAsync(titre, message, libellé, danger)`, `ErrorText/InfoText`, `Resume()/Suspend()`.
    Vue : `<controls:MessageBar/>` en haut, `<controls:DialogOverlay DataContext="{Binding Dialog}" IsVisible="{Binding $parent[UserControl].DataContext.HasDialog}"/>` en dernier enfant du Grid racine,
    et le code-behind appelle `Resume/Suspend` à l'attachement (copie `AdministrationView.axaml.cs`).
  - `ApiClient` : `GetAsync<T>`, `PostAsync<T>`, `SendAsync(HttpMethod, path, body)`, `SendAsync<T>(…)`,
    `ApiClient.Q(texte)` pour encoder les paramètres d'URL. Chemins relatifs sans `/` initial (`"api/rh/agents"`).
  - Session : `SaphirSession.Has(Permissions.X)`, `SaphirSession.Agent`, `SaphirSession.Realtime += …`
    (désabonne-toi dans `Suspend`, réabonne dans `Resume`), `RelayCommand`, `ViewModelBase.SetProperty`.
  - Thème Windows 7 : `Styles/Windows7.axaml` et ressources de `App.axaml` (classes `instruction`, `label`,
    `muted`, `groupheader`, `TabControl.win7`, boutons `default`/`small`/`link`, DataGrid). Respecte ce style
    (fenêtres de type « panneau de configuration » / outils Windows 7) ; pas de Fluent moderne voyant.
  - Une application = une fenêtre redimensionnable (taille de départ ~1100×720) ; prévois aussi l'usage
    téléphone (390 px de large) : contenu défilable, pas de largeur fixe énorme.

## Pièges déjà rencontrés (à éviter absolument)

- `<Run Text="{Binding X}"/>` doit avoir `Mode=OneWay` (sinon boucle infinie).
- Un élément avec `DataContext="{Binding Y}"` résout ses autres liaisons contre Y : utilise
  `$parent[UserControl].DataContext.Z` pour remonter.
- Une `ListBox`/`DataGrid` remet `SelectedItem` à null quand on vide la collection : mémorise l'id avant de recharger.
- Pas d'animation de `RenderTransform` dans les styles.
- N'utilise que des `StaticResource` qui existent dans `App.axaml` (vérifie le nom) ; sinon couleurs en dur.
- Commandes : `new RelayCommand(() => _ = MaMethodeAsync())` ; jamais `async void` hors gestionnaires d'événements.
- Records/DTO partagés : propriétés en PascalCase, sérialisation JSON web (camelCase) des deux côtés.
- Collections d'expressions `[]` OK en C# 13, mais pour un type `IReadOnlyList<string>` vide préfère `Array.Empty<string>()`.
- Pas de paquet NuGet nouveau sans le signaler (version exacte, compatible net9.0) ; le poste ne doit dépendre
  que d'Avalonia 11.3.x.
- Vérifie que chaque `x:Class` correspond au namespace + nom de classe du code-behind, que chaque vue a son `.axaml.cs`
  avec `InitializeComponent()`, et que les `Name=` utilisés dans le code-behind existent dans le XAML.

## Qualité attendue

- Formulaires réellement fonctionnels, boutons qui agissent, données persistées, validations côté serveur,
  historisation, actes validés figés (plus modifiables, seulement consultables / complétables par mention).
- Contrôle des permissions **sur le serveur** pour chaque route, et masquage des actions non permises côté poste.
- Interface claire, dense, de type logiciel métier Windows 7 ; menus latéraux comme dans le cahier des charges.
- Captures : fournis des données fictives réalistes (Créteil, Val-de-Marne) dans `<TonModule>Captures.Sample` et
  quelques captures parlantes dans `Run()` (1280×820), nommées `<numéro>-<module>-<écran>.png`.

## Rapport final (court, en français)

Liste des fichiers créés, routes, tables/migrations, décisions, doutes de compilation, et **demandes de
changements hors de ton périmètre** (fichier, modification exacte).
