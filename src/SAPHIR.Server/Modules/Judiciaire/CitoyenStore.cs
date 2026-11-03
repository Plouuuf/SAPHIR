using System.Globalization;
using Saphir.Server.Data;
using Saphir.Server.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Judiciaire;

namespace Saphir.Server.Modules.Judiciaire;

/// <summary>
/// Déclaration d'identité par le joueur depuis le jeu (ressource FiveM saphir-identite, routes /api/integrations/identite/…).
/// Une fiche est liée au personnage FiveM (<c>per_personnes.fivem_id</c>, unique) et au compte Discord du joueur
/// (<c>discord_proprietaire</c>) : le joueur n'agit que sur ses personnages (même personnage ET même Discord).
/// La fiche apparaît aussitôt dans CHEOPS (badge « Déclarée par le joueur »). Contrat : docs/API-CITOYEN.md.
/// </summary>
public sealed class CitoyenStore(SaphirDb saphirDb, ParametresService parametres, RealtimeHub hub, ILogger<CitoyenStore> logger)
{
    private const int MaxVehiculesParEnvoi = 30;

    private static DateTime Maintenant => DateTime.UtcNow;
    private static DateOnly Aujourdhui => DateOnly.FromDateTime(DateTime.UtcNow);

    // =====================================================================================
    // Contrôles communs
    // =====================================================================================

    /// <summary>Portail ouvert (paramètre citoyen.portail.actif) ; sinon 503.</summary>
    public async Task ControlerPortailAsync(CancellationToken ct)
    {
        if (!CitoyenRegles.PortailActif(await parametres.ValeurAsync(CitoyenParametres.PortailActif, ct)))
            throw new ApiException(StatusCodes.Status503ServiceUnavailable,
                "La déclaration d'identité est fermée pour le moment par l'administration du serveur.");
    }

    private async Task<int> MaximumAsync(CancellationToken ct)
        => CitoyenRegles.Maximum(await parametres.ValeurAsync(CitoyenParametres.PersonnagesMax, ct));

    private static (string Discord, string Personnage, Auteur Auteur) Joueur(string? discordId, string? personnageId, string? nomJoueur)
    {
        if (!CitoyenRegles.DiscordValide(discordId))
            throw ApiException.Saisie("Compte Discord introuvable : relie Discord à FiveM puis reconnecte-toi au serveur.");
        if (!CitoyenRegles.PersonnageValide(personnageId))
            throw ApiException.Saisie("Identifiant de personnage invalide.");
        var discord = discordId!.Trim();
        return (discord, personnageId!.Trim(), new Auteur(null, CitoyenRegles.Auteur(discord, nomJoueur)));
    }

    /// <summary>Verrou par compte Discord (limite de personnages, déclarations simultanées), libéré à la fin.</summary>
    private static async Task<IAsyncDisposable> VerrouAsync(SqlSession db, string discordId, CancellationToken ct)
    {
        var nom = "saphir-citoyen-" + discordId;
        if (await db.LongAsync("SELECT GET_LOCK(@nom, 10)", ct, ("@nom", nom)) != 1)
            throw ApiException.Conflit("Une autre déclaration est en cours pour ce compte. Réessaie dans quelques secondes.");
        return new Liberation(db, nom);
    }

    private sealed class Liberation(SqlSession db, string nom) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await db.LongAsync("SELECT RELEASE_LOCK(@nom)", CancellationToken.None, ("@nom", nom));
            }
            catch
            {
                // Connexion perdue : le verrou est libéré avec elle.
            }
        }
    }

    /// <summary>Première ligne (valeur par défaut si aucune) : pour les tuples, que FirstOrDefaultAsync n'accepte pas.</summary>
    private static async Task<T> PremierAsync<T>(SqlSession db, string sql, Func<SqlRow, T> map, CancellationToken ct,
        params (string Name, object? Value)[] parametres)
        => (await db.QueryAsync(sql, map, ct, parametres)).FirstOrDefault()!;

    private sealed record Ligne(
        long Id, string Reference, string? Discord, DateTime? RetireLe, DateTime? DeclareeLe, string Nom, string Prenom, DateOnly? Naissance,
        string? Lieu, string? Sexe, string? Nationalite, string? Adresse, string? Telephone, string? Profession, int? Taille,
        string? Corpulence, string? Signes, string? Signalement, string FivemId);

    private const string ColonnesLigne =
        """
        id, reference, discord_proprietaire, retire_le, declaree_le, nom, prenom, date_naissance, lieu_naissance, sexe, nationalite,
        adresse, telephone, profession, taille_cm, corpulence, signes_particuliers, signalement, fivem_id
        """;

    private static Ligne LireLigne(SqlRow r) => new(
        r.Long("id"), r.Text("reference"), r.NullableText("discord_proprietaire"), r.Value("retire_le") as DateTime?,
        r.Value("declaree_le") as DateTime?, r.Text("nom"), r.Text("prenom"), Sqlj.Jour(r, "date_naissance"), r.NullableText("lieu_naissance"),
        r.NullableText("sexe"), r.NullableText("nationalite"), r.NullableText("adresse"), r.NullableText("telephone"),
        r.NullableText("profession"), r.Value("taille_cm") is { } t and not DBNull ? Convert.ToInt32(t, CultureInfo.InvariantCulture) : null,
        r.NullableText("corpulence"), r.NullableText("signes_particuliers"), r.NullableText("signalement"), r.NullableText("fivem_id") ?? string.Empty);

    private static Task<Ligne?> LigneParPersonnageAsync(SqlSession db, string personnageId, CancellationToken ct)
        => db.FirstOrDefaultAsync($"SELECT {ColonnesLigne} FROM per_personnes WHERE fivem_id = @fivem FOR UPDATE", LireLigne, ct,
            ("@fivem", personnageId));

    /// <summary>Personnage actif du joueur (même personnage ET même Discord), sinon 404.</summary>
    private static async Task<Ligne> PersonnageDuJoueurAsync(SqlSession db, string discordId, string personnageId, CancellationToken ct)
    {
        var ligne = await LigneParPersonnageAsync(db, personnageId, ct);
        if (ligne is null || ligne.RetireLe is not null || !string.Equals(ligne.Discord, discordId, StringComparison.Ordinal))
            throw ApiException.Introuvable("Ce personnage n'est pas déclaré par ton compte. Utilise d'abord /identite.");
        return ligne;
    }

    // =====================================================================================
    // Liens avec les fichiers de police (identité figée, retrait refusé)
    // =====================================================================================

    private static readonly (string Libelle, string Sql)[] LiensPersonne =
    [
        ("TAJ", "SELECT 1 FROM ch_taj WHERE personne_id = @id LIMIT 1"),
        ("FPR", "SELECT 1 FROM ch_fpr WHERE personne_id = @id LIMIT 1"),
        ("FIJAIS", "SELECT 1 FROM ch_fijais WHERE personne_id = @id LIMIT 1"),
        ("casier", "SELECT 1 FROM ch_decisions WHERE personne_id = @id LIMIT 1"),
        ("FAED / FNAEG", "SELECT 1 FROM ch_scientifique WHERE personne_id = @id LIMIT 1"),
        ("procédure LRPPN", "SELECT 1 FROM lr_procedure_personnes WHERE personne_id = @id LIMIT 1"),
        ("acte de procédure", "SELECT 1 FROM lr_actes WHERE personne_id = @id OR mis_en_cause_id = @id LIMIT 1"),
        ("main courante", "SELECT 1 FROM mc_mentions WHERE declarant_personne_id = @id LIMIT 1"),
        ("intervention PÉGASE", "SELECT 1 FROM pg_personnes WHERE personne_id = @id LIMIT 1"),
        ("avis de contravention", "SELECT 1 FROM pve_avis WHERE personne_id = @id LIMIT 1"),
        ("contrôle d'identité", "SELECT 1 FROM app_mrz_lectures WHERE personne_id = @id LIMIT 1")
    ];

    private static readonly (string Libelle, string Sql)[] LiensVehicule =
    [
        ("FOVeS", "SELECT 1 FROM ch_foves WHERE vehicule_id = @id LIMIT 1"),
        ("procédure LRPPN", "SELECT 1 FROM lr_procedure_vehicules WHERE vehicule_id = @id LIMIT 1"),
        ("intervention PÉGASE", "SELECT 1 FROM pg_vehicules WHERE vehicule_id = @id LIMIT 1"),
        ("avis de contravention", "SELECT 1 FROM pve_avis WHERE vehicule_id = @id LIMIT 1")
    ];

    private static async Task<List<string>> LiensAsync(SqlSession db, (string Libelle, string Sql)[] requetes, long id, CancellationToken ct)
    {
        var liens = new List<string>();
        foreach (var (libelle, sql) in requetes)
            if (await db.ScalarAsync(sql, ct, ("@id", id)) is not null)
                liens.Add(libelle);
        return liens;
    }

    /// <summary>Fichiers de police (et procédures) où figure la personne. Liste vide : identité encore modifiable.</summary>
    public static Task<List<string>> LiensPoliceAsync(SqlSession db, long personneId, CancellationToken ct)
        => LiensAsync(db, LiensPersonne, personneId, ct);

    // =====================================================================================
    // État
    // =====================================================================================

    public async Task<CitoyenEtatDto> EtatAsync(string? discordId, string? personnageId, CancellationToken ct)
    {
        await ControlerPortailAsync(ct);
        if (!CitoyenRegles.DiscordValide(discordId))
            throw ApiException.Saisie("Compte Discord introuvable : relie Discord à FiveM puis reconnecte-toi au serveur.");
        var discord = discordId!.Trim();
        await using var db = await saphirDb.OpenAsync(ct);
        var personnages = await db.QueryAsync(
            """
            SELECT reference, fivem_id, nom, prenom, date_naissance FROM per_personnes
            WHERE discord_proprietaire = @discord AND retire_le IS NULL ORDER BY declaree_le, id
            """,
            r => new CitoyenPersonnageResumeDto(r.Text("reference"), r.NullableText("fivem_id") ?? string.Empty, r.Text("nom"), r.Text("prenom"),
                Sqlj.Jour(r, "date_naissance")), ct, ("@discord", discord));
        CitoyenPersonnageDto? personnage = null;
        if (CitoyenRegles.PersonnageValide(personnageId))
        {
            var id = await db.ScalarAsync(
                "SELECT id FROM per_personnes WHERE fivem_id = @fivem AND discord_proprietaire = @discord AND retire_le IS NULL", ct,
                ("@fivem", personnageId!.Trim()), ("@discord", discord));
            if (id is not null)
                personnage = await LirePersonnageAsync(db, Convert.ToInt64(id, CultureInfo.InvariantCulture), ct);
        }
        return new CitoyenEtatDto(true, await MaximumAsync(ct), personnages, personnage);
    }

    private static async Task<CitoyenPersonnageDto> LirePersonnageAsync(SqlSession db, long id, CancellationToken ct)
    {
        var l = await db.FirstOrDefaultAsync($"SELECT {ColonnesLigne} FROM per_personnes WHERE id = @id", LireLigne, ct, ("@id", id))
                ?? throw ApiException.Introuvable("Personnage introuvable.");
        var permis = await db.FirstOrDefaultAsync(
            "SELECT reference, categories, dates_obtention, situation, points FROM ch_permis WHERE personne_id = @id",
            r => new CitoyenPermisDto(r.Text("reference"), r.Text("categories"), r.NullableText("dates_obtention"), r.Text("situation"), r.Int("points")),
            ct, ("@id", id));
        var vehicules = await db.QueryAsync(
            """
            SELECT v.id, v.affichage, v.marque, v.modele, v.couleur,
                   a.assureur, a.contrat, a.debut, a.fin, a.statut
            FROM veh_vehicules v
            LEFT JOIN veh_assurances a ON a.id = (SELECT a2.id FROM veh_assurances a2 WHERE a2.vehicule_id = v.id ORDER BY a2.debut DESC, a2.id DESC LIMIT 1)
            WHERE v.titulaire_id = @id AND v.retire_le IS NULL
            ORDER BY v.affichage
            """,
            r =>
            {
                CitoyenAssuranceDto? assurance = null;
                if (r.NullableText("contrat") is { } contrat)
                {
                    var debut = Sqlj.Jour(r, "debut") ?? DateOnly.MinValue;
                    var fin = Sqlj.Jour(r, "fin");
                    assurance = new CitoyenAssuranceDto(r.Text("assureur"), contrat, fin, JudiciaireRegles.Couvert(r.Text("statut"), debut, fin, Aujourdhui));
                }
                return new CitoyenVehiculeDto(r.Text("affichage"), r.NullableText("marque"), r.NullableText("modele"), r.NullableText("couleur"), assurance);
            }, ct, ("@id", id));
        var figee = (await LiensPoliceAsync(db, id, ct)).Count > 0;
        return new CitoyenPersonnageDto(l.Reference, l.FivemId, l.Nom, l.Prenom, l.Naissance, l.Lieu, l.Sexe, l.Nationalite, l.Adresse,
            l.Telephone, l.Profession, l.Taille, l.Corpulence, l.Signes, figee, permis, vehicules);
    }

    // =====================================================================================
    // Déclaration et mise à jour de l'identité
    // =====================================================================================

    public async Task<CitoyenResultatDto> DeclarerAsync(CitoyenIdentiteInput input, CancellationToken ct)
    {
        await ControlerPortailAsync(ct);
        var (discord, personnage, auteur) = Joueur(input.DiscordId, input.PersonnageId, input.NomJoueur);
        var erreurs = CitoyenRegles.ControlerIdentite(input, Aujourdhui);
        if (erreurs.Count > 0)
            throw ApiException.Saisie(string.Join(" ", erreurs));

        var nom = NomsFormat.Nom(CitoyenRegles.Nettoyer(input.Nom, 60));
        var prenom = NomsFormat.Prenom(CitoyenRegles.Nettoyer(input.Prenom, 60));
        var naissance = JudiciaireRegles.LireDate(input.DateNaissance);
        var sexe = CitoyenRegles.Sexe(input.Sexe);
        var corpulence = CitoyenRegles.Nettoyer(input.Corpulence, 40) is { } c ? CitoyenRegles.Majuscule(c) : null;
        var maximum = await MaximumAsync(ct);

        await using var db = await saphirDb.OpenAsync(ct);
        await using (await VerrouAsync(db, discord, ct))
        {
            await db.BeginAsync(ct);
            var existant = await LigneParPersonnageAsync(db, personnage, ct);
            var avertissements = new List<string>();
            long id;
            bool cree;
            string message;

            if (existant is not null && existant.Discord is not null && !string.Equals(existant.Discord, discord, StringComparison.Ordinal))
                throw ApiException.Conflit("Ce personnage est déjà déclaré par un autre compte Discord. Contacte l'équipe du serveur.");

            var actif = existant is not null && existant.Discord is not null && existant.RetireLe is null;
            if (!actif)
            {
                var nombre = await db.LongAsync(
                    "SELECT COUNT(*) FROM per_personnes WHERE discord_proprietaire = @discord AND retire_le IS NULL", ct, ("@discord", discord));
                if (nombre >= maximum)
                    throw ApiException.Conflit($"Tu as déjà {nombre} personnage(s) déclaré(s) : c'est le maximum autorisé ({maximum}). Retire d'abord un personnage (/identite retirer).");
            }

            var liens = existant is null ? new List<string>() : await LiensPoliceAsync(db, existant.Id, ct);
            var figee = liens.Count > 0;
            var lieuSaisi = CitoyenRegles.Nettoyer(input.LieuNaissance, 120);

            // Identité retenue : celle de la fiche si elle est figée par un fichier de police.
            var identite = (Nom: nom, Prenom: prenom, Naissance: naissance, Lieu: lieuSaisi ?? existant?.Lieu);
            if (existant is not null && figee)
            {
                var changes = CitoyenRegles.ChangementsIdentite((existant.Nom, existant.Prenom, existant.Naissance, existant.Lieu),
                    (nom, prenom, naissance, lieuSaisi));
                if (changes.Count > 0)
                    avertissements.Add($"Identité figée (la personne figure dans un fichier de police) : {string.Join(", ", changes)} non modifié(s).");
                identite = (existant.Nom, existant.Prenom, existant.Naissance, existant.Lieu);
            }

            var doublon = await db.ScalarAsync(
                """
                SELECT id FROM per_personnes
                WHERE nom = @nom AND prenom = @prenom AND date_naissance <=> @naissance AND retire_le IS NULL AND id <> @id LIMIT 1
                """, ct,
                ("@nom", identite.Nom), ("@prenom", identite.Prenom), ("@naissance", Sqlj.Jour(identite.Naissance)), ("@id", existant?.Id ?? 0));
            if (doublon is not null)
                throw ApiException.Conflit("Cette identité (même NOM, prénom et date de naissance) existe déjà dans les fichiers. Choisis une autre identité ou contacte l'équipe du serveur.");

            var adresse = CitoyenRegles.Nettoyer(input.Adresse, 255) ?? existant?.Adresse;
            var telephone = CitoyenRegles.Nettoyer(input.Telephone, 40) ?? existant?.Telephone;
            var profession = CitoyenRegles.Nettoyer(input.Profession, 120) ?? existant?.Profession;
            var nationalite = CitoyenRegles.Nettoyer(input.Nationalite, 60) ?? existant?.Nationalite;
            var sexeRetenu = sexe ?? existant?.Sexe;
            var taille = input.TailleCm ?? existant?.Taille;
            corpulence ??= existant?.Corpulence;
            var signes = CitoyenRegles.Nettoyer(input.SignesParticuliers, 500) ?? existant?.Signes;
            var signalementSaisi = input.TailleCm is not null || input.Corpulence is not null || input.SignesParticuliers is not null;
            var signalement = signalementSaisi || existant is null
                ? CitoyenRegles.ComposerSignalement(taille, corpulence, signes) ?? existant?.Signalement
                : existant.Signalement;

            var valeurs = new (string Name, object? Value)[]
            {
                ("@nom", identite.Nom), ("@prenom", identite.Prenom), ("@naissance", Sqlj.Jour(identite.Naissance)), ("@lieu", identite.Lieu),
                ("@sexe", sexeRetenu), ("@nationalite", nationalite), ("@adresse", adresse), ("@telephone", telephone),
                ("@profession", profession), ("@taille", taille), ("@corpulence", corpulence), ("@signes", signes),
                ("@signalement", signalement), ("@discord", discord), ("@fivem", personnage), ("@auteur", auteur.Nom),
                ("@maintenant", Maintenant), ("@id", existant?.Id ?? 0)
            };

            if (existant is null)
            {
                id = await db.InsertAsync(
                    """
                    INSERT INTO per_personnes (fivem_id, nom, prenom, date_naissance, lieu_naissance, sexe, nationalite, adresse, telephone,
                        signalement, source, cree_par, cree_le, maj_le, discord_proprietaire, declaree_le, profession, taille_cm, corpulence,
                        signes_particuliers)
                    VALUES (@fivem, @nom, @prenom, @naissance, @lieu, @sexe, @nationalite, @adresse, @telephone, @signalement, 'Joueur',
                        @auteur, @maintenant, @maintenant, @discord, @maintenant, @profession, @taille, @corpulence, @signes)
                    """, ct, valeurs);
                var reference = JudiciaireRegles.ReferencePersonne(id);
                await db.ExecuteAsync("UPDATE per_personnes SET reference = @reference WHERE id = @id", ct, ("@reference", reference), ("@id", id));
                await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Personne, reference, "Déclaration par le joueur", null, null,
                    $"{identite.Nom} {identite.Prenom}", "Déclaration en jeu (/identite)", ct);
                cree = true;
                message = $"Personnage {identite.Nom} {identite.Prenom} déclaré ({reference}).";
            }
            else
            {
                id = existant.Id;
                // Fiche importée sans référence (ancienne lecture FiveM) : la référence PER- est attribuée maintenant.
                var reference = string.IsNullOrEmpty(existant.Reference) ? JudiciaireRegles.ReferencePersonne(id) : existant.Reference;
                if (reference != existant.Reference)
                    await db.ExecuteAsync("UPDATE per_personnes SET reference = @reference WHERE id = @id", ct, ("@reference", reference), ("@id", id));
                await db.ExecuteAsync(
                    """
                    UPDATE per_personnes SET nom = @nom, prenom = @prenom, date_naissance = @naissance, lieu_naissance = @lieu, sexe = @sexe,
                        nationalite = @nationalite, adresse = @adresse, telephone = @telephone, signalement = @signalement,
                        profession = @profession, taille_cm = @taille, corpulence = @corpulence, signes_particuliers = @signes,
                        discord_proprietaire = @discord, declaree_le = COALESCE(declaree_le, @maintenant), retire_le = NULL, maj_le = @maintenant
                    WHERE id = @id
                    """, ct, valeurs);
                if (!actif)
                    await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Personne, reference,
                        existant.RetireLe is not null ? "Personnage déclaré à nouveau" : "Déclaration par le joueur", "Propriétaire",
                        existant.Discord, discord, "Déclaration en jeu (/identite)", ct);
                await Sqlj.HistoriserChangementsAsync(db, auteur, ObjetsJudiciaire.Personne, reference,
                [
                    ("NOM", existant.Nom, identite.Nom),
                    ("Prénom", existant.Prenom, identite.Prenom),
                    ("Date de naissance", Sqlj.Texte(existant.Naissance), Sqlj.Texte(identite.Naissance)),
                    ("Lieu de naissance", existant.Lieu, identite.Lieu),
                    ("Sexe", existant.Sexe, sexeRetenu),
                    ("Nationalité", existant.Nationalite, nationalite),
                    ("Adresse", existant.Adresse, adresse),
                    ("Téléphone", existant.Telephone, telephone),
                    ("Profession", existant.Profession, profession),
                    ("Signalement", existant.Signalement, signalement)
                ], "Mise à jour en jeu (/identite)", ct);
                cree = false;
                message = actif
                    ? $"Fiche {reference} mise à jour."
                    : $"Personnage {identite.Nom} {identite.Prenom} déclaré ({reference}).";
            }
            await db.CommitAsync(ct);
            await PrevenirCheopsAsync(id);
            return new CitoyenResultatDto(cree, message, await LirePersonnageAsync(db, id, ct), avertissements);
        }
    }

    // =====================================================================================
    // Retrait
    // =====================================================================================

    public async Task<string> RetirerAsync(CitoyenJoueurInput input, CancellationToken ct)
    {
        await ControlerPortailAsync(ct);
        var (discord, personnage, auteur) = Joueur(input.DiscordId, input.PersonnageId, input.NomJoueur);
        await using var db = await saphirDb.OpenAsync(ct);
        await using (await VerrouAsync(db, discord, ct))
        {
            await db.BeginAsync(ct);
            var ligne = await PersonnageDuJoueurAsync(db, discord, personnage, ct);
            var liens = await LiensPoliceAsync(db, ligne.Id, ct);
            if (liens.Count > 0)
                throw ApiException.Conflit($"Retrait impossible : la personne figure dans un fichier de police ou une procédure ({string.Join(", ", liens)}).");
            var vehicules = await db.QueryAsync(
                "SELECT id, affichage, declare_le FROM veh_vehicules WHERE titulaire_id = @id AND retire_le IS NULL FOR UPDATE",
                r => (Id: r.Long("id"), Plaque: r.Text("affichage"), Declare: r.Value("declare_le") is DateTime), ct, ("@id", ligne.Id));
            foreach (var v in vehicules)
            {
                var liensVehicule = await LiensAsync(db, LiensVehicule, v.Id, ct);
                if (liensVehicule.Count > 0)
                    throw ApiException.Conflit($"Retrait impossible : le véhicule {v.Plaque} figure dans un fichier de police ou une procédure ({string.Join(", ", liensVehicule)}).");
            }

            var maintenant = Maintenant;
            await db.ExecuteAsync("UPDATE per_personnes SET retire_le = @maintenant, maj_le = @maintenant WHERE id = @id", ct,
                ("@maintenant", maintenant), ("@id", ligne.Id));
            await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Personne, ligne.Reference, "Retrait par le joueur", "Statut",
                "Active", "Retirée", "Retrait en jeu (/identite retirer)", ct);
            foreach (var v in vehicules.Where(v => v.Declare))
            {
                await db.ExecuteAsync("UPDATE veh_vehicules SET retire_le = @maintenant, maj_le = @maintenant WHERE id = @id", ct,
                    ("@maintenant", maintenant), ("@id", v.Id));
                await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Vehicule, v.Plaque, "Retrait par le joueur", "Statut",
                    "Actif", "Retiré", $"Retrait du titulaire {ligne.Reference}", ct);
            }
            await db.CommitAsync(ct);
            await PrevenirCheopsAsync(ligne.Id);
            return $"Personnage {ligne.Nom} {ligne.Prenom} retiré des fichiers ({ligne.Reference}).";
        }
    }

    // =====================================================================================
    // Véhicules
    // =====================================================================================

    public async Task<CitoyenVehiculesResultatDto> DeclarerVehiculesAsync(CitoyenVehiculesInput input, CancellationToken ct)
    {
        await ControlerPortailAsync(ct);
        var (discord, personnage, auteur) = Joueur(input.DiscordId, input.PersonnageId, input.NomJoueur);
        var liste = input.Vehicules ?? [];
        if (liste.Count == 0)
            throw ApiException.Saisie("Aucun véhicule à déclarer.");
        if (liste.Count > MaxVehiculesParEnvoi)
            throw ApiException.Saisie($"Au plus {MaxVehiculesParEnvoi} véhicules par déclaration.");

        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var titulaire = await PersonnageDuJoueurAsync(db, discord, personnage, ct);
        var resultats = new List<CitoyenVehiculeResultatDto>();
        var vues = new HashSet<string>(StringComparer.Ordinal);
        foreach (var v in liste)
        {
            var plaqueSaisie = CitoyenRegles.Nettoyer(v?.Plaque, 20) ?? string.Empty;
            if (v is null || !CitoyenRegles.PlaqueValide(plaqueSaisie))
            {
                resultats.Add(new(plaqueSaisie, CitoyenVehiculeStatuts.Refuse, "Plaque invalide (2 à 12 lettres ou chiffres)."));
                continue;
            }
            var immat = JudiciaireRegles.Normaliser(plaqueSaisie);
            var affichage = JudiciaireRegles.FormaterImmatriculation(plaqueSaisie);
            if (!vues.Add(immat))
                continue;
            var finTexte = CitoyenRegles.Nettoyer(v.FinAssurance, 20);
            var fin = JudiciaireRegles.LireDate(finTexte);
            if (CitoyenRegles.ControlerAssurance(v.Assureur, v.Contrat, fin, finTexte is null || fin is not null) is { } erreurAssurance)
            {
                resultats.Add(new(affichage, CitoyenVehiculeStatuts.Refuse, erreurAssurance));
                continue;
            }
            resultats.Add(await DeclarerVehiculeAsync(db, auteur, titulaire, v, immat, affichage, fin, ct));
        }
        await db.CommitAsync(ct);
        await PrevenirCheopsAsync(titulaire.Id);
        return new CitoyenVehiculesResultatDto(resultats, await LirePersonnageAsync(db, titulaire.Id, ct));
    }

    private static async Task<CitoyenVehiculeResultatDto> DeclarerVehiculeAsync(SqlSession db, Auteur auteur, Ligne titulaire,
        CitoyenVehiculeInput v, string immat, string affichage, DateOnly? finAssurance, CancellationToken ct)
    {
        var marque = CitoyenRegles.Nettoyer(v.Marque, 60);
        var modele = CitoyenRegles.Nettoyer(v.Modele, 60);
        var couleur = CitoyenRegles.Nettoyer(v.Couleur, 40);
        var maintenant = Maintenant;
        var existant = await PremierAsync(db, 
            "SELECT id, affichage, marque, modele, couleur, titulaire_id, titulaire_libre, retire_le FROM veh_vehicules WHERE immatriculation = @immat FOR UPDATE",
            r => (Id: r.Long("id"), Affichage: r.Text("affichage"), Marque: r.NullableText("marque"), Modele: r.NullableText("modele"),
                Couleur: r.NullableText("couleur"), Titulaire: r.NullableLong("titulaire_id"), Libre: r.NullableText("titulaire_libre"),
                Retire: r.Value("retire_le") is DateTime), ct, ("@immat", immat));

        long vehiculeId;
        string statut;
        string message;
        if (existant.Id == 0)
        {
            vehiculeId = await db.InsertAsync(
                """
                INSERT INTO veh_vehicules (immatriculation, affichage, marque, modele, couleur, titulaire_id, adresse, situation, source,
                    cree_le, maj_le, declare_le)
                VALUES (@immat, @affichage, @marque, @modele, @couleur, @titulaire, @adresse, 'Normale', 'Joueur', @maintenant, @maintenant, @maintenant)
                """, ct,
                ("@immat", immat), ("@affichage", affichage), ("@marque", marque), ("@modele", modele), ("@couleur", couleur),
                ("@titulaire", titulaire.Id), ("@adresse", titulaire.Adresse), ("@maintenant", maintenant));
            await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Vehicule, affichage, "Déclaration par le joueur", "Titulaire", null,
                titulaire.Reference, "Déclaration en jeu (/identite vehicules)", ct);
            statut = CitoyenVehiculeStatuts.Cree;
            message = "Véhicule déclaré.";
        }
        else
        {
            if (!CitoyenRegles.PlaqueDisponible(existant.Titulaire, existant.Libre, titulaire.Id))
                return new(existant.Affichage, CitoyenVehiculeStatuts.Refuse, "Cette plaque est déjà enregistrée au nom d'un autre titulaire.");
            vehiculeId = existant.Id;
            var nouvelleMarque = marque ?? existant.Marque;
            var nouveauModele = modele ?? existant.Modele;
            var nouvelleCouleur = couleur ?? existant.Couleur;
            await db.ExecuteAsync(
                """
                UPDATE veh_vehicules SET marque = @marque, modele = @modele, couleur = @couleur, titulaire_id = @titulaire,
                    declare_le = COALESCE(declare_le, @maintenant), retire_le = NULL, maj_le = @maintenant
                WHERE id = @id
                """, ct,
                ("@marque", nouvelleMarque), ("@modele", nouveauModele), ("@couleur", nouvelleCouleur), ("@titulaire", titulaire.Id),
                ("@maintenant", maintenant), ("@id", vehiculeId));
            await Sqlj.HistoriserChangementsAsync(db, auteur, ObjetsJudiciaire.Vehicule, existant.Affichage,
            [
                ("Titulaire", existant.Titulaire?.ToString(CultureInfo.InvariantCulture), titulaire.Id.ToString(CultureInfo.InvariantCulture)),
                ("Statut", existant.Retire ? "Retiré" : "Actif", "Actif"),
                ("Marque", existant.Marque, nouvelleMarque),
                ("Modèle", existant.Modele, nouveauModele),
                ("Couleur", existant.Couleur, nouvelleCouleur)
            ], "Déclaration en jeu (/identite vehicules)", ct);
            statut = existant.Titulaire == titulaire.Id && !existant.Retire ? CitoyenVehiculeStatuts.MisAJour : CitoyenVehiculeStatuts.Cree;
            message = statut == CitoyenVehiculeStatuts.Cree ? "Véhicule déclaré." : "Véhicule mis à jour.";
            affichage = existant.Affichage;
        }

        if (CitoyenRegles.Nettoyer(v.Contrat, 60) is { } contrat)
            message += " " + await EnregistrerAssuranceAsync(db, auteur, vehiculeId, affichage, CitoyenRegles.Nettoyer(v.Assureur, 100)!, contrat, finAssurance, ct);
        return new(affichage, statut, message);
    }

    /// <summary>Contrat d'assurance (FVA) : même numéro → assureur et date de fin mis à jour ; nouveau numéro → nouveau contrat.</summary>
    private static async Task<string> EnregistrerAssuranceAsync(SqlSession db, Auteur auteur, long vehiculeId, string affichage, string assureur,
        string contrat, DateOnly? fin, CancellationToken ct)
    {
        var existant = await PremierAsync(db, 
            "SELECT id, assureur, fin, debut FROM veh_assurances WHERE vehicule_id = @vehicule AND contrat = @contrat ORDER BY id DESC LIMIT 1",
            r => (Id: r.Long("id"), Assureur: r.Text("assureur"), Fin: Sqlj.Jour(r, "fin"), Debut: Sqlj.Jour(r, "debut")), ct,
            ("@vehicule", vehiculeId), ("@contrat", contrat));
        if (existant.Id == 0)
        {
            var debut = fin is { } f && f < Aujourdhui ? f : Aujourdhui;
            await db.ExecuteAsync(
                """
                INSERT INTO veh_assurances (vehicule_id, assureur, contrat, debut, fin, statut, cree_le)
                VALUES (@vehicule, @assureur, @contrat, @debut, @fin, @statut, @maintenant)
                """, ct,
                ("@vehicule", vehiculeId), ("@assureur", assureur), ("@contrat", contrat), ("@debut", Sqlj.Jour(debut)), ("@fin", Sqlj.Jour(fin)),
                ("@statut", AssuranceStatuts.Valide), ("@maintenant", Maintenant));
            await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Vehicule, affichage, "Assurance ajoutée", "FVA", null,
                $"{assureur} — {contrat} ({AssuranceStatuts.Valide})", "Déclaration en jeu", ct);
            return "Assurance enregistrée.";
        }
        if (existant.Assureur == assureur && existant.Fin == fin)
            return "Assurance inchangée.";
        var nouveauDebut = fin is { } fn && existant.Debut is { } d && fn < d ? fn : existant.Debut;
        await db.ExecuteAsync("UPDATE veh_assurances SET assureur = @assureur, fin = @fin, debut = @debut WHERE id = @id", ct,
            ("@assureur", assureur), ("@fin", Sqlj.Jour(fin)), ("@debut", Sqlj.Jour(nouveauDebut)), ("@id", existant.Id));
        await Sqlj.HistoriserChangementsAsync(db, auteur, ObjetsJudiciaire.Vehicule, affichage,
            [("Assureur", existant.Assureur, assureur), ("Fin d'assurance", Sqlj.Texte(existant.Fin), Sqlj.Texte(fin))],
            $"Contrat {contrat} — déclaration en jeu", ct);
        return "Assurance mise à jour.";
    }

    public async Task<string> RetirerVehiculeAsync(CitoyenVehiculeRetraitInput input, CancellationToken ct)
    {
        await ControlerPortailAsync(ct);
        var (discord, personnage, auteur) = Joueur(input.DiscordId, input.PersonnageId, input.NomJoueur);
        var immat = JudiciaireRegles.Normaliser(input.Plaque);
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var titulaire = await PersonnageDuJoueurAsync(db, discord, personnage, ct);
        var vehicule = await PremierAsync(db, 
            "SELECT id, affichage FROM veh_vehicules WHERE immatriculation = @immat AND titulaire_id = @titulaire AND retire_le IS NULL FOR UPDATE",
            r => (Id: r.Long("id"), Affichage: r.Text("affichage")), ct, ("@immat", immat), ("@titulaire", titulaire.Id));
        if (vehicule.Id == 0)
            throw ApiException.Introuvable("Aucun véhicule de ce personnage ne porte cette plaque.");
        var liens = await LiensAsync(db, LiensVehicule, vehicule.Id, ct);
        if (liens.Count > 0)
            throw ApiException.Conflit($"Retrait impossible : le véhicule figure dans un fichier de police ou une procédure ({string.Join(", ", liens)}).");
        await db.ExecuteAsync("UPDATE veh_vehicules SET retire_le = @maintenant, maj_le = @maintenant WHERE id = @id", ct,
            ("@maintenant", Maintenant), ("@id", vehicule.Id));
        await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Vehicule, vehicule.Affichage, "Retrait par le joueur", "Statut", "Actif", "Retiré",
            "Retrait en jeu (/identite vehicules retirer)", ct);
        await db.CommitAsync(ct);
        await PrevenirCheopsAsync(titulaire.Id);
        return $"Véhicule {vehicule.Affichage} retiré.";
    }

    // =====================================================================================
    // Permis
    // =====================================================================================

    public async Task<CitoyenResultatDto> DeclarerPermisAsync(CitoyenPermisInput input, CancellationToken ct)
    {
        await ControlerPortailAsync(ct);
        var (discord, personnage, auteur) = Joueur(input.DiscordId, input.PersonnageId, input.NomJoueur);
        string categories;
        try
        {
            categories = CitoyenRegles.NormaliserCategories(input.Categories);
        }
        catch (ArgumentException ex)
        {
            throw ApiException.Saisie(ex.Message);
        }
        var obtention = JudiciaireRegles.LireDate(input.DateObtention);

        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var titulaire = await PersonnageDuJoueurAsync(db, discord, personnage, ct);
        if (CitoyenRegles.ControlerObtention(obtention, titulaire.Naissance, Aujourdhui) is { } erreur)
            throw ApiException.Saisie(erreur);
        var dates = Sqlj.Texte(obtention);
        var avant = await PremierAsync(db, 
            "SELECT reference, categories, dates_obtention FROM ch_permis WHERE personne_id = @id FOR UPDATE",
            r => (Reference: r.Text("reference"), Categories: r.Text("categories"), Dates: r.NullableText("dates_obtention")), ct, ("@id", titulaire.Id));
        string message;
        var cree = avant.Reference is null;
        if (cree)
        {
            var (points, situation) = CitoyenRegles.PermisInitial(obtention!.Value, Aujourdhui);
            var id = await db.InsertAsync(
                """
                INSERT INTO ch_permis (personne_id, categories, dates_obtention, validite, situation, points, cree_le, maj_le)
                VALUES (@personne, @categories, @dates, NULL, @situation, @points, @maintenant, @maintenant)
                """, ct,
                ("@personne", titulaire.Id), ("@categories", categories), ("@dates", dates), ("@situation", situation), ("@points", points),
                ("@maintenant", Maintenant));
            var reference = JudiciaireRegles.Reference("PC", id);
            await db.ExecuteAsync("UPDATE ch_permis SET reference = @reference WHERE id = @id", ct, ("@reference", reference), ("@id", id));
            await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Permis, reference, "Déclaration par le joueur", null, null,
                $"{categories} — {points} point(s) — {situation}", "Déclaration en jeu (/identite permis)", ct);
            message = $"Permis {categories} déclaré : {points} points ({situation}).";
        }
        else
        {
            // Le joueur ne touche jamais aux points ni à la situation du titre.
            await db.ExecuteAsync("UPDATE ch_permis SET categories = @categories, dates_obtention = @dates, maj_le = @maintenant WHERE personne_id = @id", ct,
                ("@categories", categories), ("@dates", dates), ("@maintenant", Maintenant), ("@id", titulaire.Id));
            await Sqlj.HistoriserChangementsAsync(db, auteur, ObjetsJudiciaire.Permis, avant.Reference!,
                [("Catégories", avant.Categories, categories), ("Dates d'obtention", avant.Dates, dates)], "Mise à jour en jeu (/identite permis)", ct);
            message = $"Permis mis à jour ({categories}).";
        }
        await db.CommitAsync(ct);
        await PrevenirCheopsAsync(titulaire.Id);
        return new CitoyenResultatDto(cree, message, await LirePersonnageAsync(db, titulaire.Id, ct), Array.Empty<string>());
    }

    private async Task PrevenirCheopsAsync(long personneId)
    {
        try
        {
            await hub.BroadcastAsync(new RealtimeMessage(JudiciaireRealtime.CheopsChanged,
                Text: personneId.ToString(CultureInfo.InvariantCulture), AppId: SaphirApps.Cheops), CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Diffusion cheops-changed impossible");
        }
    }
}
