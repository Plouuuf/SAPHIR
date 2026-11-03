using System.Text;
using Saphir.Server.Data;
using Saphir.Server.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Judiciaire;

namespace Saphir.Server.Modules.Judiciaire;

/// <summary>
/// Personnes (identifiant stable « PER-000001 ») et véhicules fictifs : recherche, fiches, création, modification.
/// Les identités et véhicules des personnages FiveM sont importés à la demande (lecture seule de la base FiveM).
/// </summary>
public sealed class PersonnesStore(SaphirDb saphirDb, FiveMLecture fivem)
{
    private const int Limite = 50;

    // =====================================================================================
    // Personnes
    // =====================================================================================

    /// <summary>Recherche rapide (route partagée) : chaque terme cherche dans NOM, prénom, alias, référence ou date de naissance.</summary>
    public async Task<List<PersonneResumeDto>> RechercherAsync(string? q, AgentDroits droits, CancellationToken ct)
    {
        var termes = JudiciaireRegles.Termes(q);
        if (termes.Count == 0)
            return [];

        await using var db = await saphirDb.OpenAsync(ct);
        await ImporterFiveMAsync(db, termes, ct);

        var sql = new StringBuilder($"SELECT {Sqlj.ColonnesPersonne} FROM per_personnes p WHERE p.retire_le IS NULL");
        var parametres = new List<(string Name, object? Value)>();
        for (var i = 0; i < termes.Count; i++)
        {
            var terme = termes[i];
            if (JudiciaireRegles.LireDate(terme) is { } date)
            {
                sql.Append($" AND p.date_naissance = @d{i}");
                parametres.Add(($"@d{i}", Sqlj.Jour(date)));
                continue;
            }
            sql.Append($"""
                 AND (p.nom LIKE @t{i} OR p.prenom LIKE @t{i} OR p.reference = @r{i}
                      OR EXISTS (SELECT 1 FROM per_alias a WHERE a.personne_id = p.id AND a.alias LIKE @t{i}))
                """);
            parametres.Add(($"@t{i}", Sqlj.Like(terme)));
            parametres.Add(($"@r{i}", terme.ToUpperInvariant()));
        }
        sql.Append($" ORDER BY p.nom, p.prenom, p.id LIMIT {Limite}");
        return await LireResumesAsync(db, sql.ToString(), parametres, droits, ct);
    }

    /// <summary>Recherche générale CHEOPS (11.2) : critères séparés, tous facultatifs (au moins un).</summary>
    public async Task<List<PersonneResumeDto>> RechercherCriteresAsync(string? nom, string? prenom, string? alias, string? naissance,
        string? lieu, string? identifiant, AgentDroits droits, CancellationToken ct)
    {
        var sql = new StringBuilder($"SELECT {Sqlj.ColonnesPersonne} FROM per_personnes p WHERE p.retire_le IS NULL");
        var parametres = new List<(string Name, object? Value)>();
        var critere = false;
        if (!string.IsNullOrWhiteSpace(nom))
        {
            sql.Append(" AND p.nom LIKE @nom");
            parametres.Add(("@nom", Sqlj.Like(nom)));
            critere = true;
        }
        if (!string.IsNullOrWhiteSpace(prenom))
        {
            sql.Append(" AND p.prenom LIKE @prenom");
            parametres.Add(("@prenom", Sqlj.Like(prenom)));
            critere = true;
        }
        if (!string.IsNullOrWhiteSpace(alias))
        {
            sql.Append(" AND EXISTS (SELECT 1 FROM per_alias a WHERE a.personne_id = p.id AND a.alias LIKE @alias)");
            parametres.Add(("@alias", Sqlj.Like(alias)));
            critere = true;
        }
        if (!string.IsNullOrWhiteSpace(naissance))
        {
            var date = JudiciaireRegles.LireDate(naissance) ?? throw ApiException.Saisie("Date de naissance attendue au format jj/mm/aaaa.");
            sql.Append(" AND p.date_naissance = @naissance");
            parametres.Add(("@naissance", Sqlj.Jour(date)));
            critere = true;
        }
        if (!string.IsNullOrWhiteSpace(lieu))
        {
            sql.Append(" AND p.lieu_naissance LIKE @lieu");
            parametres.Add(("@lieu", Sqlj.Like(lieu)));
            critere = true;
        }
        if (!string.IsNullOrWhiteSpace(identifiant))
        {
            sql.Append(" AND (p.reference = @identifiant OR p.fivem_id = @identifiantBrut)");
            parametres.Add(("@identifiant", identifiant.Trim().ToUpperInvariant()));
            parametres.Add(("@identifiantBrut", identifiant.Trim()));
            critere = true;
        }
        if (!critere)
            return [];

        await using var db = await saphirDb.OpenAsync(ct);
        var termes = new[] { nom, prenom }.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()).ToList();
        if (termes.Count > 0)
            await ImporterFiveMAsync(db, termes, ct);
        sql.Append($" ORDER BY p.nom, p.prenom, p.id LIMIT {Limite}");
        return await LireResumesAsync(db, sql.ToString(), parametres, droits, ct);
    }

    private static async Task<List<PersonneResumeDto>> LireResumesAsync(SqlSession db, string sql,
        List<(string Name, object? Value)> parametres, AgentDroits droits, CancellationToken ct)
    {
        var lignes = await db.QueryAsync(sql, r => Sqlj.LirePersonne(r), ct, parametres.ToArray());
        if (lignes.Count == 0)
            return lignes;
        var alertes = await AlertesJudiciaires.PersonnesAsync(db, lignes.Select(p => p.Id).ToList(), droits, ct);
        return lignes.Select(p => p with { Alertes = alertes[p.Id] }).ToList();
    }

    public static async Task<PersonneResumeDto?> LireResumeAsync(SqlSession db, long id, AgentDroits droits, CancellationToken ct)
    {
        var personne = await db.FirstOrDefaultAsync($"SELECT {Sqlj.ColonnesPersonne} FROM per_personnes p WHERE p.id = @id",
            r => Sqlj.LirePersonne(r), ct, ("@id", id));
        if (personne is null)
            return null;
        return personne with { Alertes = await AlertesJudiciaires.PersonneAsync(db, id, droits, ct) };
    }

    public async Task<PersonneResumeDto> ResumeAsync(long id, AgentDroits droits, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await LireResumeAsync(db, id, droits, ct) ?? throw ApiException.Introuvable("Personne introuvable.");
    }

    public static async Task<PersonneFicheDto?> LireFicheAsync(SqlSession db, long id, AgentDroits droits, CancellationToken ct)
    {
        var fiche = await db.FirstOrDefaultAsync(
            """
            SELECT id, reference, nom, prenom, date_naissance, lieu_naissance, sexe, nationalite, adresse, telephone, signalement,
                   source, cree_le, maj_le, discord_proprietaire, profession, retire_le
            FROM per_personnes WHERE id = @id
            """,
            r => new PersonneFicheDto(
                r.Long("id"), r.Text("reference"), r.Text("nom"), r.Text("prenom"), Sqlj.Jour(r, "date_naissance"),
                r.NullableText("lieu_naissance"), r.NullableText("sexe"), r.NullableText("nationalite"), r.NullableText("adresse"),
                r.NullableText("telephone"), r.NullableText("signalement"), r.Text("source", "SAPHIR"),
                Array.Empty<AliasDto>(), Array.Empty<DocumentIdentiteDto>(), Array.Empty<string>(), Array.Empty<AlerteBandeauDto>(),
                r.Date("cree_le"), r.Date("maj_le"))
            {
                DeclareeParJoueur = r.NullableText("discord_proprietaire") is not null,
                Profession = r.NullableText("profession"),
                RetireeLe = r.NullableDate("retire_le")
            }, ct, ("@id", id));
        if (fiche is null)
            return null;

        var alias = await db.QueryAsync("SELECT id, alias FROM per_alias WHERE personne_id = @id ORDER BY alias",
            r => new AliasDto(r.Long("id"), r.Text("alias")), ct, ("@id", id));
        var documents = await db.QueryAsync(
            "SELECT id, type, numero, delivre_le, expire_le, autorite FROM per_documents WHERE personne_id = @id ORDER BY id",
            r => new DocumentIdentiteDto(r.Long("id"), r.Text("type"), r.Text("numero"), Sqlj.Jour(r, "delivre_le"),
                Sqlj.Jour(r, "expire_le"), r.NullableText("autorite")), ct, ("@id", id));
        return fiche with
        {
            Alias = alias,
            Documents = documents,
            Alertes = await AlertesJudiciaires.PersonneAsync(db, id, droits, ct),
            Bandeaux = await AlertesJudiciaires.BandeauxPersonneAsync(db, id, droits, ct)
        };
    }

    public async Task<PersonneFicheDto> FicheAsync(long id, AgentDroits droits, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await LireFicheAsync(db, id, droits, ct) ?? throw ApiException.Introuvable("Personne introuvable.");
    }

    public static async Task<string> ReferenceAsync(SqlSession db, long id, CancellationToken ct)
        => (await db.ScalarAsync("SELECT reference FROM per_personnes WHERE id = @id", ct, ("@id", id)) as string)
           ?? throw ApiException.Introuvable("Personne introuvable.");

    /// <summary>Création d'une identité (POST /api/personnes) avec contrôle des doublons (même NOM, prénom et date de naissance).</summary>
    public async Task<PersonneResumeDto> CreerAsync(Auteur auteur, PersonneCreationInput input, AgentDroits droits, CancellationToken ct)
    {
        if (!NomsFormat.EstValide(input.Nom) || !NomsFormat.EstValide(input.Prenom))
            throw ApiException.Saisie("NOM et prénom obligatoires (2 à 60 lettres, espaces, traits d'union ou apostrophes).");
        if (input.DateNaissance is { } naissance && (naissance > DateOnly.FromDateTime(DateTime.UtcNow) || naissance.Year < 1900))
            throw ApiException.Saisie("Date de naissance invalide.");
        var sexe = ControlerSexe(input.Sexe);
        var nom = NomsFormat.Nom(input.Nom);
        var prenom = NomsFormat.Prenom(input.Prenom);

        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        // Lecture simple (sans FOR UPDATE) : un verrou de plage sur une identité absente, suivi de l'insertion,
        // provoquait des interblocages entre créations simultanées.
        var doublon = await db.ScalarAsync(
            "SELECT reference FROM per_personnes WHERE nom = @nom AND prenom = @prenom AND date_naissance <=> @naissance LIMIT 1", ct,
            ("@nom", nom), ("@prenom", prenom), ("@naissance", Sqlj.Jour(input.DateNaissance)));
        if (doublon is string reference)
            throw ApiException.Conflit($"Cette identité existe déjà ({reference}). Sélectionne la fiche existante.");

        var maintenant = DateTime.UtcNow;
        var id = await db.InsertAsync(
            """
            INSERT INTO per_personnes (nom, prenom, date_naissance, lieu_naissance, sexe, nationalite, source, cree_par, cree_le, maj_le)
            VALUES (@nom, @prenom, @naissance, @lieu, @sexe, @nationalite, 'SAPHIR', @auteur, @maintenant, @maintenant)
            """, ct,
            ("@nom", nom), ("@prenom", prenom), ("@naissance", Sqlj.Jour(input.DateNaissance)),
            ("@lieu", SocleRegles.Tronquer(input.LieuNaissance, 120)), ("@sexe", sexe),
            ("@nationalite", SocleRegles.Tronquer(input.Nationalite, 60)), ("@auteur", auteur.Nom), ("@maintenant", maintenant));
        var refPersonne = JudiciaireRegles.ReferencePersonne(id);
        await db.ExecuteAsync("UPDATE per_personnes SET reference = @reference WHERE id = @id", ct, ("@reference", refPersonne), ("@id", id));
        await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Personne, refPersonne, "Création", null, null, $"{nom} {prenom}", null, ct);
        await db.CommitAsync(ct);
        return await LireResumeAsync(db, id, droits, ct) ?? throw ApiException.Introuvable("Personne introuvable.");
    }

    /// <summary>Modification d'une identité : chaque champ modifié est historisé (ancienne et nouvelle valeur).</summary>
    public async Task<PersonneFicheDto> ModifierAsync(Auteur auteur, long id, PersonneModificationInput input, AgentDroits droits, CancellationToken ct)
    {
        if (!NomsFormat.EstValide(input.Nom) || !NomsFormat.EstValide(input.Prenom))
            throw ApiException.Saisie("NOM et prénom obligatoires (2 à 60 lettres, espaces, traits d'union ou apostrophes).");
        var sexe = ControlerSexe(input.Sexe);
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var avant = await db.FirstOrDefaultAsync(
            "SELECT reference, nom, prenom, date_naissance, lieu_naissance, sexe, nationalite, adresse, telephone, signalement FROM per_personnes WHERE id = @id FOR UPDATE",
            r => new Dictionary<string, string?>
            {
                ["reference"] = r.Text("reference"),
                ["NOM"] = r.Text("nom"),
                ["Prénom"] = r.Text("prenom"),
                ["Date de naissance"] = Sqlj.Texte(Sqlj.Jour(r, "date_naissance")),
                ["Lieu de naissance"] = r.NullableText("lieu_naissance"),
                ["Sexe"] = r.NullableText("sexe"),
                ["Nationalité"] = r.NullableText("nationalite"),
                ["Adresse"] = r.NullableText("adresse"),
                ["Téléphone"] = r.NullableText("telephone"),
                ["Signalement"] = r.NullableText("signalement")
            }, ct, ("@id", id)) ?? throw ApiException.Introuvable("Personne introuvable.");

        var apres = new Dictionary<string, string?>
        {
            ["NOM"] = NomsFormat.Nom(input.Nom),
            ["Prénom"] = NomsFormat.Prenom(input.Prenom),
            ["Date de naissance"] = Sqlj.Texte(input.DateNaissance),
            ["Lieu de naissance"] = SocleRegles.Tronquer(input.LieuNaissance, 120),
            ["Sexe"] = sexe,
            ["Nationalité"] = SocleRegles.Tronquer(input.Nationalite, 60),
            ["Adresse"] = SocleRegles.Tronquer(input.Adresse, 255),
            ["Téléphone"] = SocleRegles.Tronquer(input.Telephone, 40),
            ["Signalement"] = SocleRegles.Tronquer(input.Signalement, 4000)
        };
        await db.ExecuteAsync(
            """
            UPDATE per_personnes SET nom = @nom, prenom = @prenom, date_naissance = @naissance, lieu_naissance = @lieu, sexe = @sexe,
                nationalite = @nationalite, adresse = @adresse, telephone = @telephone, signalement = @signalement, maj_le = @maintenant
            WHERE id = @id
            """, ct,
            ("@nom", apres["NOM"]), ("@prenom", apres["Prénom"]), ("@naissance", Sqlj.Jour(input.DateNaissance)),
            ("@lieu", apres["Lieu de naissance"]), ("@sexe", sexe), ("@nationalite", apres["Nationalité"]),
            ("@adresse", apres["Adresse"]), ("@telephone", apres["Téléphone"]), ("@signalement", apres["Signalement"]),
            ("@maintenant", DateTime.UtcNow), ("@id", id));

        var reference = avant["reference"]!;
        await Sqlj.HistoriserChangementsAsync(db, auteur, ObjetsJudiciaire.Personne, reference,
            apres.Select(kv => (kv.Key, avant[kv.Key], kv.Value)), input.Motif, ct);

        if (input.Alias is not null)
        {
            var anciens = await db.QueryAsync("SELECT alias FROM per_alias WHERE personne_id = @id ORDER BY alias", r => r.Text("alias"), ct, ("@id", id));
            var nouveaux = input.Alias.Select(a => SocleRegles.Tronquer(a, 120)).Where(a => a is not null).Select(a => a!)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(a => a, StringComparer.Ordinal).ToList();
            if (!anciens.SequenceEqual(nouveaux))
            {
                await db.ExecuteAsync("DELETE FROM per_alias WHERE personne_id = @id", ct, ("@id", id));
                foreach (var alias in nouveaux)
                    await db.ExecuteAsync("INSERT INTO per_alias (personne_id, alias) VALUES (@id, @alias)", ct, ("@id", id), ("@alias", alias));
                await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Personne, reference, "Modification", "Alias",
                    SocleRegles.Liste(anciens), SocleRegles.Liste(nouveaux), input.Motif, ct);
            }
        }
        await db.CommitAsync(ct);
        return await LireFicheAsync(db, id, droits, ct) ?? throw ApiException.Introuvable("Personne introuvable.");
    }

    public async Task AjouterDocumentAsync(Auteur auteur, long personneId, DocumentIdentiteInput input, CancellationToken ct)
    {
        var type = SocleRegles.Tronquer(input.Type, 60) ?? throw ApiException.Saisie("Type de document obligatoire.");
        var numero = SocleRegles.Tronquer(input.Numero, 60) ?? throw ApiException.Saisie("Numéro de document obligatoire.");
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var reference = await ReferenceAsync(db, personneId, ct);
        await db.ExecuteAsync(
            """
            INSERT INTO per_documents (personne_id, type, numero, delivre_le, expire_le, autorite, cree_le)
            VALUES (@personne, @type, @numero, @delivre, @expire, @autorite, @maintenant)
            """, ct,
            ("@personne", personneId), ("@type", type), ("@numero", numero), ("@delivre", Sqlj.Jour(input.DelivreLe)),
            ("@expire", Sqlj.Jour(input.ExpireLe)), ("@autorite", SocleRegles.Tronquer(input.Autorite, 120)), ("@maintenant", DateTime.UtcNow));
        await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Personne, reference, "Document d'identité ajouté", "documents", null,
            $"{type} n° {numero}", null, ct);
        await db.CommitAsync(ct);
    }

    private static string? ControlerSexe(string? sexe)
    {
        var s = SocleRegles.Tronquer(sexe, 10)?.ToUpperInvariant();
        return s switch
        {
            null => null,
            "M" or "F" or "X" => s,
            _ => throw ApiException.Saisie("Sexe attendu : M, F ou X.")
        };
    }

    // ----- Import FiveM -----

    private async Task ImporterFiveMAsync(SqlSession db, IReadOnlyList<string> termes, CancellationToken ct)
    {
        if (!fivem.Active)
            return;
        foreach (var personne in await fivem.ChercherPersonnesAsync(termes, ct))
            await ImporterPersonneAsync(db, personne, ct);
    }

    /// <summary>Crée la fiche SAPHIR d'un personnage FiveM s'il n'en a pas encore ; retourne son identifiant.</summary>
    private static async Task<long?> ImporterPersonneAsync(SqlSession db, FiveMPersonne p, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(p.Nom) || string.IsNullOrWhiteSpace(p.Prenom))
            return null;
        var maintenant = DateTime.UtcNow;
        var id = await db.InsertAsync(
            """
            INSERT IGNORE INTO per_personnes (fivem_id, nom, prenom, date_naissance, sexe, nationalite, telephone, source, cree_par, cree_le, maj_le)
            VALUES (@fivem, @nom, @prenom, @naissance, @sexe, @nationalite, @telephone, 'FiveM', 'Import FiveM', @maintenant, @maintenant)
            """, ct,
            ("@fivem", p.Id), ("@nom", SocleRegles.Tronquer(NomsFormat.Nom(p.Nom), 80)), ("@prenom", SocleRegles.Tronquer(NomsFormat.Prenom(p.Prenom), 80)),
            ("@naissance", Sqlj.Jour(p.DateNaissance)), ("@sexe", SocleRegles.Tronquer(p.Sexe, 10)),
            ("@nationalite", SocleRegles.Tronquer(p.Nationalite, 60)), ("@telephone", SocleRegles.Tronquer(p.Telephone, 40)),
            ("@maintenant", maintenant));
        if (id > 0)
        {
            await db.ExecuteAsync("UPDATE per_personnes SET reference = @reference WHERE id = @id", ct,
                ("@reference", JudiciaireRegles.ReferencePersonne(id)), ("@id", id));
            return id;
        }
        var existant = await db.ScalarAsync("SELECT id FROM per_personnes WHERE fivem_id = @fivem", ct, ("@fivem", p.Id));
        return existant is null ? null : Convert.ToInt64(existant, System.Globalization.CultureInfo.InvariantCulture);
    }

    // =====================================================================================
    // Véhicules
    // =====================================================================================

    private const string ColonnesVehicule =
        "v.id, v.affichage, v.marque, v.modele, v.couleur, v.titulaire_id, v.titulaire_libre, tp.nom AS t_nom, tp.prenom AS t_prenom";

    private const string JointureTitulaire = "LEFT JOIN per_personnes tp ON tp.id = v.titulaire_id";

    private static string? Titulaire(SqlRow r)
        => r.NullableText("t_nom") is { } nom ? $"{nom} {r.Text("t_prenom")}" : r.NullableText("titulaire_libre");

    private static VehiculeResumeDto LireVehicule(SqlRow r) => new(
        r.Long("id"), r.Text("affichage"), r.NullableText("marque"), r.NullableText("modele"), r.NullableText("couleur"),
        r.NullableLong("titulaire_id"), Titulaire(r), Array.Empty<string>());

    public async Task<List<VehiculeResumeDto>> RechercherVehiculesAsync(string? q, AgentDroits droits, CancellationToken ct)
    {
        var texte = (q ?? string.Empty).Trim();
        if (texte.Length < 2)
            return [];
        var normalise = JudiciaireRegles.Normaliser(texte);

        await using var db = await saphirDb.OpenAsync(ct);
        if (normalise.Length >= 2)
            await ImporterVehiculesFiveMAsync(db, normalise, ct);

        var lignes = await db.QueryAsync(
            $"""
            SELECT {ColonnesVehicule} FROM veh_vehicules v {JointureTitulaire}
            WHERE v.retire_le IS NULL AND (v.immatriculation LIKE @plaque OR v.vin LIKE @plaque OR CONCAT_WS(' ', v.marque, v.modele) LIKE @texte
               OR tp.nom LIKE @texte OR v.titulaire_libre LIKE @texte)
            ORDER BY v.immatriculation LIMIT {Limite}
            """, LireVehicule, ct,
            ("@plaque", Sqlj.Like(normalise.Length == 0 ? texte : normalise)), ("@texte", Sqlj.Like(texte)));
        return await AvecAlertesAsync(db, lignes, droits, ct);
    }

    /// <summary>Recherche par immatriculation exacte ou partielle (CHEOPS : SIV, FVA, recherche générale).</summary>
    public async Task<List<VehiculeResumeDto>> RechercherParPlaqueAsync(string? plaque, AgentDroits droits, CancellationToken ct)
    {
        var normalise = JudiciaireRegles.Normaliser(plaque);
        if (normalise.Length < 2)
            return [];
        await using var db = await saphirDb.OpenAsync(ct);
        await ImporterVehiculesFiveMAsync(db, normalise, ct);
        var lignes = await db.QueryAsync(
            $"SELECT {ColonnesVehicule} FROM veh_vehicules v {JointureTitulaire} WHERE v.retire_le IS NULL AND (v.immatriculation LIKE @plaque OR v.vin LIKE @plaque) ORDER BY v.immatriculation LIMIT {Limite}",
            LireVehicule, ct, ("@plaque", Sqlj.Like(normalise)));
        return await AvecAlertesAsync(db, lignes, droits, ct);
    }

    private static async Task<List<VehiculeResumeDto>> AvecAlertesAsync(SqlSession db, List<VehiculeResumeDto> lignes, AgentDroits droits, CancellationToken ct)
    {
        if (lignes.Count == 0)
            return lignes;
        var alertes = await AlertesJudiciaires.VehiculesAsync(db, lignes.Select(v => v.Id).ToList(), droits, ct);
        return lignes.Select(v => v with { Alertes = alertes[v.Id] }).ToList();
    }

    public static async Task<VehiculeResumeDto?> LireVehiculeResumeAsync(SqlSession db, long id, AgentDroits droits, CancellationToken ct)
    {
        var vehicule = await db.FirstOrDefaultAsync(
            $"SELECT {ColonnesVehicule} FROM veh_vehicules v {JointureTitulaire} WHERE v.id = @id", LireVehicule, ct, ("@id", id));
        if (vehicule is null)
            return null;
        return vehicule with { Alertes = (await AlertesJudiciaires.VehiculesAsync(db, [id], droits, ct))[id] };
    }

    public async Task<VehiculeResumeDto> VehiculeResumeAsync(long id, AgentDroits droits, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await LireVehiculeResumeAsync(db, id, droits, ct) ?? throw ApiException.Introuvable("Véhicule introuvable.");
    }

    public static AssuranceDto LireAssurance(SqlRow r)
    {
        var debut = Sqlj.Jour(r, "debut") ?? DateOnly.MinValue;
        var fin = Sqlj.Jour(r, "fin");
        var statut = r.Text("statut");
        return new AssuranceDto(r.Long("id"), r.Long("vehicule_id"), r.Text("affichage"), r.Text("assureur"), r.Text("contrat"),
            debut, fin, statut, JudiciaireRegles.Couvert(statut, debut, fin, DateOnly.FromDateTime(DateTime.UtcNow)), r.NullableDate("verifie_le"));
    }

    public const string ColonnesAssurance =
        "a.id, a.vehicule_id, v.affichage, a.assureur, a.contrat, a.debut, a.fin, a.statut, a.verifie_le";

    public async Task<VehiculeFicheDto> VehiculeFicheAsync(long id, AgentDroits droits, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var fiche = await db.FirstOrDefaultAsync(
            $"""
            SELECT v.id, v.affichage, v.vin, v.marque, v.modele, v.version, v.categorie, v.couleur, v.mise_circulation,
                   v.titulaire_id, v.titulaire_libre, v.adresse, v.situation, v.source, tp.nom AS t_nom, tp.prenom AS t_prenom
            FROM veh_vehicules v {JointureTitulaire} WHERE v.id = @id
            """,
            r => new VehiculeFicheDto(
                r.Long("id"), r.Text("affichage"), r.NullableText("vin"), r.NullableText("marque"), r.NullableText("modele"),
                r.NullableText("version"), r.NullableText("categorie"), r.NullableText("couleur"), Sqlj.Jour(r, "mise_circulation"),
                r.NullableLong("titulaire_id"), Titulaire(r), r.NullableText("adresse"), r.Text("situation", SivSituations.Normale),
                r.Text("source", "SAPHIR"), Array.Empty<string>(), Array.Empty<AlerteBandeauDto>(), Array.Empty<AssuranceDto>(),
                Array.Empty<FovesFicheDto>(), Array.Empty<HistoriqueDto>()), ct, ("@id", id))
            ?? throw ApiException.Introuvable("Véhicule introuvable.");

        var assurances = await db.QueryAsync(
            $"SELECT {ColonnesAssurance} FROM veh_assurances a JOIN veh_vehicules v ON v.id = a.vehicule_id WHERE a.vehicule_id = @id ORDER BY a.debut DESC",
            LireAssurance, ct, ("@id", id));
        List<FovesFicheDto> signalements = droits.Possede(Permissions.CheopsVehicules)
            ? await db.QueryAsync(
                $"""
                SELECT {CheopsStore.ColonnesFoves} FROM ch_foves f
                WHERE f.vehicule_id = @id OR f.identifiant_norm = (SELECT immatriculation FROM veh_vehicules WHERE id = @id)
                ORDER BY f.signale_le DESC
                """, CheopsStore.LireFoves, ct, ("@id", id))
            : new List<FovesFicheDto>();
        return fiche with
        {
            Alertes = (await AlertesJudiciaires.VehiculesAsync(db, [id], droits, ct))[id],
            Bandeaux = await AlertesJudiciaires.BandeauxVehiculeAsync(db, id, droits, ct),
            Assurances = assurances,
            Signalements = signalements,
            Historique = await Sqlj.HistoriqueAsync(db, ObjetsJudiciaire.Vehicule, fiche.Immatriculation, ct)
        };
    }

    /// <summary>Création ou modification d'un véhicule (SIV fictif). Plaque unique ; chaque champ modifié est historisé.</summary>
    public async Task<long> EnregistrerVehiculeAsync(Auteur auteur, long? id, VehiculeInput input, CancellationToken ct)
    {
        if (!JudiciaireRegles.ImmatriculationValide(input.Immatriculation))
            throw ApiException.Saisie("Immatriculation invalide.");
        var immat = JudiciaireRegles.Normaliser(input.Immatriculation);
        var affichage = JudiciaireRegles.FormaterImmatriculation(input.Immatriculation);
        var situation = SocleRegles.Tronquer(input.Situation, 40) ?? SivSituations.Normale;
        if (!SivSituations.All.Contains(situation))
            throw ApiException.Saisie("Situation administrative inconnue.");
        var valeurs = new Dictionary<string, string?>
        {
            ["Immatriculation"] = affichage,
            ["VIN"] = SocleRegles.Tronquer(input.Vin?.ToUpperInvariant(), 40),
            ["Marque"] = SocleRegles.Tronquer(input.Marque, 60),
            ["Modèle"] = SocleRegles.Tronquer(input.Modele, 60),
            ["Version"] = SocleRegles.Tronquer(input.Version, 80),
            ["Catégorie"] = SocleRegles.Tronquer(input.Categorie, 30),
            ["Couleur"] = SocleRegles.Tronquer(input.Couleur, 40),
            ["Mise en circulation"] = Sqlj.Texte(input.MiseEnCirculation),
            ["Titulaire"] = input.TitulaireId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Titulaire (texte)"] = SocleRegles.Tronquer(input.TitulaireLibre, 200),
            ["Adresse"] = SocleRegles.Tronquer(input.Adresse, 255),
            ["Situation"] = situation
        };

        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        if (input.TitulaireId is { } titulaire)
            _ = await ReferenceAsync(db, titulaire, ct);
        var autre = await db.ScalarAsync("SELECT id FROM veh_vehicules WHERE immatriculation = @immat AND id <> @id", ct,
            ("@immat", immat), ("@id", id ?? 0));
        if (autre is not null)
            throw ApiException.Conflit($"Un véhicule immatriculé {affichage} existe déjà.");

        var parametres = new (string Name, object? Value)[]
        {
            ("@immat", immat), ("@affichage", affichage), ("@vin", valeurs["VIN"]), ("@marque", valeurs["Marque"]),
            ("@modele", valeurs["Modèle"]), ("@version", valeurs["Version"]), ("@categorie", valeurs["Catégorie"]),
            ("@couleur", valeurs["Couleur"]), ("@mec", Sqlj.Jour(input.MiseEnCirculation)), ("@titulaire", input.TitulaireId),
            ("@libre", valeurs["Titulaire (texte)"]), ("@adresse", valeurs["Adresse"]), ("@situation", situation),
            ("@maintenant", DateTime.UtcNow), ("@id", id ?? 0)
        };

        long vehiculeId;
        if (id is null)
        {
            vehiculeId = await db.InsertAsync(
                """
                INSERT INTO veh_vehicules (immatriculation, affichage, vin, marque, modele, version, categorie, couleur, mise_circulation,
                    titulaire_id, titulaire_libre, adresse, situation, source, cree_le, maj_le)
                VALUES (@immat, @affichage, @vin, @marque, @modele, @version, @categorie, @couleur, @mec, @titulaire, @libre, @adresse,
                    @situation, 'SAPHIR', @maintenant, @maintenant)
                """, ct, parametres);
            await Historique.EcrireAsync(db, auteur, ObjetsJudiciaire.Vehicule, affichage, "Création", null, null,
                $"{valeurs["Marque"]} {valeurs["Modèle"]}".Trim(), input.Motif, ct);
        }
        else
        {
            var avant = await db.FirstOrDefaultAsync(
                """
                SELECT affichage, vin, marque, modele, version, categorie, couleur, mise_circulation, titulaire_id, titulaire_libre, adresse, situation
                FROM veh_vehicules WHERE id = @id FOR UPDATE
                """,
                r => new Dictionary<string, string?>
                {
                    ["Immatriculation"] = r.Text("affichage"),
                    ["VIN"] = r.NullableText("vin"),
                    ["Marque"] = r.NullableText("marque"),
                    ["Modèle"] = r.NullableText("modele"),
                    ["Version"] = r.NullableText("version"),
                    ["Catégorie"] = r.NullableText("categorie"),
                    ["Couleur"] = r.NullableText("couleur"),
                    ["Mise en circulation"] = Sqlj.Texte(Sqlj.Jour(r, "mise_circulation")),
                    ["Titulaire"] = r.NullableLong("titulaire_id")?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["Titulaire (texte)"] = r.NullableText("titulaire_libre"),
                    ["Adresse"] = r.NullableText("adresse"),
                    ["Situation"] = r.Text("situation")
                }, ct, ("@id", id.Value)) ?? throw ApiException.Introuvable("Véhicule introuvable.");
            await db.ExecuteAsync(
                """
                UPDATE veh_vehicules SET immatriculation = @immat, affichage = @affichage, vin = @vin, marque = @marque, modele = @modele,
                    version = @version, categorie = @categorie, couleur = @couleur, mise_circulation = @mec, titulaire_id = @titulaire,
                    titulaire_libre = @libre, adresse = @adresse, situation = @situation, maj_le = @maintenant
                WHERE id = @id
                """, ct, parametres);
            await Sqlj.HistoriserChangementsAsync(db, auteur, ObjetsJudiciaire.Vehicule, avant["Immatriculation"]!,
                valeurs.Select(kv => (kv.Key, avant[kv.Key], kv.Value)), input.Motif, ct);
            vehiculeId = id.Value;
        }
        await db.CommitAsync(ct);
        return vehiculeId;
    }

    // ----- Import FiveM -----

    private async Task ImporterVehiculesFiveMAsync(SqlSession db, string plaqueNormalisee, CancellationToken ct)
    {
        if (!fivem.Active)
            return;
        foreach (var vehicule in await fivem.ChercherVehiculesAsync(plaqueNormalisee, ct))
        {
            var immat = JudiciaireRegles.Normaliser(vehicule.Plaque);
            if (immat.Length < 2 || immat.Length > 16)
                continue;
            var existe = await db.ScalarAsync("SELECT id FROM veh_vehicules WHERE immatriculation = @immat", ct, ("@immat", immat));
            if (existe is not null)
                continue;
            long? titulaireId = null;
            if (!string.IsNullOrWhiteSpace(vehicule.ProprietaireId))
            {
                var connu = await db.ScalarAsync("SELECT id FROM per_personnes WHERE fivem_id = @fivem", ct, ("@fivem", vehicule.ProprietaireId));
                if (connu is not null)
                    titulaireId = Convert.ToInt64(connu, System.Globalization.CultureInfo.InvariantCulture);
                else if (await fivem.LirePersonneAsync(vehicule.ProprietaireId, ct) is { } proprietaire)
                    titulaireId = await ImporterPersonneAsync(db, proprietaire, ct);
            }
            var maintenant = DateTime.UtcNow;
            await db.ExecuteAsync(
                """
                INSERT IGNORE INTO veh_vehicules (immatriculation, affichage, modele, titulaire_id, situation, source, cree_le, maj_le)
                VALUES (@immat, @affichage, @modele, @titulaire, 'Normale', 'FiveM', @maintenant, @maintenant)
                """, ct,
                ("@immat", immat), ("@affichage", JudiciaireRegles.FormaterImmatriculation(immat)),
                ("@modele", SocleRegles.Tronquer(vehicule.Modele, 60)), ("@titulaire", titulaireId), ("@maintenant", maintenant));
        }
    }
}
