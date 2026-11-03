using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using Saphir.Server.Data;
using Saphir.Server.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Complements;

namespace Saphir.Server.Modules.Complements;

/// <summary>
/// BODYCAM (cahier § 18.5) : registre des enregistrements de simulation. La vidéo est soit une adresse (URL), soit un
/// fichier envoyé par morceaux (200 Mo au plus) et stocké sur le disque du serveur dans <c>data/bodycam/</c> à côté de
/// l'exécutable (stockage privé, jamais servi directement) ; métadonnées, empreinte SHA-256 et documents associés en base.
/// Chaque visionnage est inscrit dans le journal d'accès de l'enregistrement et dans le journal des accès SAPHIR.
/// </summary>
internal sealed class BodycamStore(SaphirDb saphirDb, RealtimeHub hub, JournalAcces journal)
{
    private static readonly ConcurrentDictionary<long, SemaphoreSlim> Verrous = new();

    public static string Dossier
    {
        get
        {
            var dossier = Path.Combine(AppContext.BaseDirectory, "data", "bodycam");
            Directory.CreateDirectory(dossier);
            return dossier;
        }
    }

    public async Task<List<BodycamResumeDto>> ListerAsync(string? q, string? intervention, long? agentId, bool archives, CancellationToken ct)
    {
        var sql = "SELECT * FROM app_bodycam WHERE 1 = 1";
        var parametres = new List<(string, object?)>();
        if (!archives)
        {
            sql += " AND statut <> @archive";
            parametres.Add(("@archive", BodycamStatuts.Archive));
        }
        if (!string.IsNullOrWhiteSpace(intervention))
        {
            sql += " AND intervention_numero = @intervention";
            parametres.Add(("@intervention", intervention.Trim()));
        }
        if (agentId is { } a)
        {
            sql += " AND agent_id = @agent";
            parametres.Add(("@agent", a));
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            sql += " AND (numero LIKE @q OR agent LIKE @q OR intervention_numero LIKE @q OR description LIKE @q OR provenance LIKE @q)";
            parametres.Add(("@q", "%" + SocleRegles.EchapperLike(q.Trim()) + "%"));
        }
        sql += " ORDER BY enregistre_le DESC, id DESC LIMIT 500";
        await using var db = await saphirDb.OpenAsync(ct);
        return await db.QueryAsync(sql, r => new BodycamResumeDto(
            r.Long("id"), r.Text("numero"), r.Text("agent"), r.Date("enregistre_le"), r.Int("duree_secondes"),
            r.NullableText("intervention_numero"), r.Text("source"), r.Text("statut"), r.NullableText("description")),
            ct, parametres.ToArray());
    }

    public async Task<BodycamDto> LireAsync(long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await ChargerAsync(db, id, ct) ?? throw ApiException.Introuvable("Enregistrement introuvable.");
    }

    public async Task<BodycamDto> EnregistrerAsync(AgentContext ctx, long? id, BodycamInput input, CancellationToken ct)
    {
        if (input.Source is not (BodycamSources.Url or BodycamSources.Fichier))
            throw ApiException.Saisie("Source de la vidéo : adresse (url) ou fichier.");
        if (input.EnregistreLe > DateTimeOffset.UtcNow.AddMinutes(10))
            throw ApiException.Saisie("La date d'enregistrement ne peut pas être dans le futur.");
        if (input.DureeSecondes is < 0 or > 86_400)
            throw ApiException.Saisie("Durée invalide (24 heures au plus).");
        string? url = null;
        if (input.Source == BodycamSources.Url)
        {
            url = ComplementsOutils.Obligatoire(input.Url, 500, "Adresse de la vidéo");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                throw ApiException.Saisie("L'adresse de la vidéo doit commencer par http:// ou https://.");
        }

        var maintenant = DateTime.UtcNow;
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var porteur = await ComplementsOutils.AgentAsync(db, input.AgentId, ct) ?? throw ApiException.Saisie("Agent porteur introuvable.");
        (string, object?)[] Parametres(long? cle) =>
        [
            ("@agent_id", porteur.Id), ("@agent", $"{porteur.Nom} {porteur.Prenom}"), ("@date", input.EnregistreLe.UtcDateTime),
            ("@duree", input.DureeSecondes), ("@intervention", ComplementsOutils.Facultatif(input.InterventionNumero, 40)),
            ("@description", ComplementsOutils.Facultatif(input.Description, 4000)),
            ("@provenance", ComplementsOutils.Facultatif(input.Provenance, 120)), ("@source", input.Source), ("@url", url),
            ("@maintenant", maintenant), ("@id", cle ?? 0)
        ];

        if (id is null)
        {
            var statut = input.Source == BodycamSources.Url ? BodycamStatuts.Disponible : BodycamStatuts.EnAttente;
            id = await db.InsertAsync(
                """
                INSERT INTO app_bodycam (agent_id, agent, enregistre_le, duree_secondes, intervention_numero, description, provenance,
                    source, url, statut, cree_par_id, cree_par, version, cree_le, maj_le)
                VALUES (@agent_id, @agent, @date, @duree, @intervention, @description, @provenance,
                    @source, @url, @statut, @createur_id, @createur, 1, @maintenant, @maintenant)
                """, ct, [.. Parametres(null), ("@statut", statut), ("@createur_id", ctx.Agent.Id), ("@createur", ctx.Auteur.Nom)]);
            var numero = ComplementsOutils.Reference("BC", maintenant, id.Value);
            await db.ExecuteAsync("UPDATE app_bodycam SET numero = @n WHERE id = @id", ct, ("@n", numero), ("@id", id.Value));
            await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Bodycam, numero, "Création", null, null,
                $"Porteur {porteur.Libelle}, source {input.Source}", null, ct);
        }
        else
        {
            var avant = await ChargerAsync(db, id.Value, ct, verrouiller: true) ?? throw ApiException.Introuvable("Enregistrement introuvable.");
            ComplementsOutils.VerifierVersion(input.Version, avant.Version);
            if (avant.Statut == BodycamStatuts.Archive)
                throw ApiException.Conflit("Un enregistrement archivé n'est plus modifiable.");
            if (avant.Source != input.Source && avant.FichierRecu > 0)
                throw ApiException.Conflit("Un fichier a déjà été envoyé : la source ne peut plus changer.");
            var statut = input.Source == BodycamSources.Url ? BodycamStatuts.Disponible
                : avant.Source == BodycamSources.Fichier ? avant.Statut : BodycamStatuts.EnAttente;
            await db.ExecuteAsync(
                """
                UPDATE app_bodycam SET agent_id = @agent_id, agent = @agent, enregistre_le = @date, duree_secondes = @duree,
                    intervention_numero = @intervention, description = @description, provenance = @provenance, source = @source,
                    url = @url, statut = @statut, version = version + 1, maj_le = @maintenant
                WHERE id = @id
                """, ct, [.. Parametres(id.Value), ("@statut", statut)]);
            var apres = await ChargerAsync(db, id.Value, ct) ?? throw new InvalidOperationException("Enregistrement introuvable.");
            foreach (var (champ, a, b) in new (string, string?, string?)[]
                     {
                         ("porteur", avant.Agent, apres.Agent),
                         ("date", avant.EnregistreLe.ToString("u", CultureInfo.InvariantCulture), apres.EnregistreLe.ToString("u", CultureInfo.InvariantCulture)),
                         ("durée", avant.DureeSecondes.ToString(CultureInfo.InvariantCulture), apres.DureeSecondes.ToString(CultureInfo.InvariantCulture)),
                         ("intervention", avant.InterventionNumero, apres.InterventionNumero),
                         ("description", avant.Description, apres.Description),
                         ("provenance", avant.Provenance, apres.Provenance),
                         ("source", avant.Source, apres.Source),
                         ("url", avant.Url, apres.Url)
                     })
                if (a != b)
                    await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Bodycam, avant.Numero, "Modification", champ, a, b, null, ct);
        }

        var dto = await ChargerAsync(db, id.Value, ct) ?? throw new InvalidOperationException("Enregistrement introuvable.");
        await db.CommitAsync(ct);
        await PrevenirAsync(dto.Id);
        return dto;
    }

    public async Task<BodycamDto> ArchiverAsync(AgentContext ctx, long id, BodycamArchivageInput input, CancellationToken ct)
    {
        var motif = ComplementsOutils.Obligatoire(input.Motif, 500, "Motif de l'archivage");
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var avant = await ChargerAsync(db, id, ct, verrouiller: true) ?? throw ApiException.Introuvable("Enregistrement introuvable.");
        ComplementsOutils.VerifierVersion(input.Version, avant.Version);
        if (avant.Statut == BodycamStatuts.Archive)
            throw ApiException.Conflit("Enregistrement déjà archivé.");
        await db.ExecuteAsync(
            "UPDATE app_bodycam SET statut = @statut, motif_archivage = @motif, version = version + 1, maj_le = @maintenant WHERE id = @id", ct,
            ("@statut", BodycamStatuts.Archive), ("@motif", motif), ("@maintenant", DateTime.UtcNow), ("@id", id));
        await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Bodycam, avant.Numero, "Archivage", "statut", avant.Statut,
            BodycamStatuts.Archive, motif, ct);
        var dto = await ChargerAsync(db, id, ct) ?? throw new InvalidOperationException("Enregistrement introuvable.");
        await db.CommitAsync(ct);
        await PrevenirAsync(id);
        return dto;
    }

    // ===== Envoi du fichier par morceaux =====

    public async Task<BodycamEnvoiEtatDto> DebutEnvoiAsync(AgentContext ctx, long id, BodycamEnvoiDebutInput input, CancellationToken ct)
    {
        var nom = ComplementsOutils.NomFichier(input.Nom);
        var extension = PiecesJointes.Extension(nom);
        if (!BodycamFichiers.Extensions.Contains(extension))
            throw ApiException.Saisie($"Format vidéo non autorisé. Autorisés : {string.Join(", ", BodycamFichiers.Extensions)}.");
        if (input.Taille <= 0 || input.Taille > BodycamFichiers.TailleMax)
            throw ApiException.Saisie($"La vidéo doit peser au plus {PiecesJointes.TailleLisible(BodycamFichiers.TailleMax)}.");

        var verrou = Verrous.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await verrou.WaitAsync(ct);
        try
        {
            await using var db = await saphirDb.OpenAsync(ct);
            await db.BeginAsync(ct);
            var e = await ChargerAsync(db, id, ct, verrouiller: true) ?? throw ApiException.Introuvable("Enregistrement introuvable.");
            if (e.Source != BodycamSources.Fichier)
                throw ApiException.Conflit("Cet enregistrement renvoie vers une adresse : choisis la source « fichier » avant d'envoyer une vidéo.");
            if (e.Statut != BodycamStatuts.EnAttente)
                throw ApiException.Conflit("La vidéo de cet enregistrement a déjà été reçue : elle ne peut pas être remplacée.");
            File.Delete(CheminPartiel(id));
            await using (File.Create(CheminPartiel(id))) { }
            await db.ExecuteAsync(
                """
                UPDATE app_bodycam SET fichier_nom = @nom, fichier_taille = @taille, fichier_type = @type, fichier_recu = 0,
                    fichier_chemin = NULL, fichier_sha256 = NULL, maj_le = @maintenant
                WHERE id = @id
                """, ct,
                ("@nom", nom), ("@taille", input.Taille), ("@type", PiecesJointes.TypeMime(nom)), ("@maintenant", DateTime.UtcNow), ("@id", id));
            await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Bodycam, e.Numero, "Envoi de la vidéo commencé", "fichier", null,
                $"{nom} ({PiecesJointes.TailleLisible(input.Taille)})", null, ct);
            await db.CommitAsync(ct);
            return new BodycamEnvoiEtatDto(0, input.Taille, false, BodycamStatuts.EnAttente);
        }
        finally
        {
            verrou.Release();
        }
    }

    public async Task<BodycamEnvoiEtatDto> MorceauAsync(long id, BodycamMorceauInput input, CancellationToken ct)
    {
        byte[] donnees;
        try
        {
            donnees = Convert.FromBase64String(input.Donnees ?? string.Empty);
        }
        catch (FormatException)
        {
            throw ApiException.Saisie("Morceau de vidéo illisible.");
        }
        if (donnees.Length == 0 || donnees.Length > BodycamFichiers.TailleMorceau)
            throw ApiException.Saisie("Taille de morceau invalide.");

        var verrou = Verrous.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await verrou.WaitAsync(ct);
        try
        {
            await using var db = await saphirDb.OpenAsync(ct);
            var e = await ChargerAsync(db, id, ct) ?? throw ApiException.Introuvable("Enregistrement introuvable.");
            if (e.Statut != BodycamStatuts.EnAttente || e.FichierTaille is not { } attendu || e.FichierNom is null)
                throw ApiException.Conflit("Aucun envoi de vidéo en cours pour cet enregistrement.");
            if (input.Offset != e.FichierRecu)
                throw ApiException.Conflit($"Morceau inattendu : reprise à l'octet {e.FichierRecu}.");
            if (e.FichierRecu + donnees.Length > attendu)
                throw ApiException.Saisie("La vidéo envoyée dépasse la taille annoncée.");
            if (input.Offset == 0 && !ComplementsOutils.SignatureCoherente(PiecesJointes.Extension(e.FichierNom), donnees))
                throw ApiException.Saisie("Le contenu ne correspond pas à une vidéo du format annoncé.");

            var chemin = CheminPartiel(id);
            if (!File.Exists(chemin) || new FileInfo(chemin).Length != e.FichierRecu)
                throw ApiException.Conflit("Envoi interrompu : recommence l'envoi de la vidéo.");
            await using (var flux = new FileStream(chemin, FileMode.Append, FileAccess.Write, FileShare.None))
                await flux.WriteAsync(donnees, ct);
            var recu = e.FichierRecu + donnees.Length;
            await db.ExecuteAsync("UPDATE app_bodycam SET fichier_recu = @recu WHERE id = @id", ct, ("@recu", recu), ("@id", id));
            return new BodycamEnvoiEtatDto(recu, attendu, false, BodycamStatuts.EnAttente);
        }
        finally
        {
            verrou.Release();
        }
    }

    public async Task<BodycamDto> FinEnvoiAsync(AgentContext ctx, long id, CancellationToken ct)
    {
        var verrou = Verrous.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await verrou.WaitAsync(ct);
        try
        {
            await using var db = await saphirDb.OpenAsync(ct);
            await db.BeginAsync(ct);
            var e = await ChargerAsync(db, id, ct, verrouiller: true) ?? throw ApiException.Introuvable("Enregistrement introuvable.");
            if (e.Statut != BodycamStatuts.EnAttente || e.FichierTaille is not { } attendu || e.FichierNom is null)
                throw ApiException.Conflit("Aucun envoi de vidéo en cours pour cet enregistrement.");
            var partiel = CheminPartiel(id);
            if (e.FichierRecu != attendu || !File.Exists(partiel) || new FileInfo(partiel).Length != attendu)
                throw ApiException.Conflit("La vidéo n'est pas complète : poursuis l'envoi.");

            string empreinte;
            await using (var flux = File.OpenRead(partiel))
                empreinte = Convert.ToHexString(await SHA256.HashDataAsync(flux, ct)).ToLowerInvariant();
            var nomFinal = $"{e.Numero}.{PiecesJointes.Extension(e.FichierNom)}";
            File.Move(partiel, CheminFinal(nomFinal), overwrite: true);

            await db.ExecuteAsync(
                """
                UPDATE app_bodycam SET statut = @statut, fichier_chemin = @chemin, fichier_sha256 = @sha, version = version + 1, maj_le = @maintenant
                WHERE id = @id
                """, ct,
                ("@statut", BodycamStatuts.Disponible), ("@chemin", nomFinal), ("@sha", empreinte), ("@maintenant", DateTime.UtcNow), ("@id", id));
            await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Bodycam, e.Numero, "Vidéo reçue", "statut", BodycamStatuts.EnAttente,
                BodycamStatuts.Disponible, $"{e.FichierNom}, SHA-256 {empreinte}", ct);
            var dto = await ChargerAsync(db, id, ct) ?? throw new InvalidOperationException("Enregistrement introuvable.");
            await db.CommitAsync(ct);
            await PrevenirAsync(id);
            return dto;
        }
        finally
        {
            verrou.Release();
        }
    }

    // ===== Visionnage (journalisé) =====

    public async Task<BodycamVisionnageDto> VisionnerAsync(AgentContext ctx, long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var e = await ChargerAsync(db, id, ct) ?? throw ApiException.Introuvable("Enregistrement introuvable.");
        if (e.Source == BodycamSources.Fichier && (await CheminAsync(db, id, ct) is not { } stocke || !File.Exists(CheminFinal(stocke))))
            throw ApiException.Conflit("La vidéo de cet enregistrement n'est pas disponible.");
        await NoterAccesAsync(db, ctx, e, "Visionnage", ct);
        return new BodycamVisionnageDto(e.Source, e.Url, e.FichierNom, e.FichierTaille, e.FichierType);
    }

    public async Task<BodycamMorceauDto> LireMorceauAsync(long id, long offset, int? taille, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var e = await ChargerAsync(db, id, ct) ?? throw ApiException.Introuvable("Enregistrement introuvable.");
        var nom = await CheminAsync(db, id, ct);
        if (e.Source != BodycamSources.Fichier || nom is null || !File.Exists(CheminFinal(nom)))
            throw ApiException.Conflit("La vidéo de cet enregistrement n'est pas disponible.");
        var chemin = CheminFinal(nom);
        await using var flux = File.OpenRead(chemin);
        if (offset < 0 || offset > flux.Length)
            throw ApiException.Saisie("Position de lecture invalide.");
        var demande = Math.Clamp(taille ?? BodycamFichiers.TailleMorceau, 1, BodycamFichiers.TailleMorceau);
        var tampon = new byte[(int)Math.Min(demande, flux.Length - offset)];
        flux.Seek(offset, SeekOrigin.Begin);
        var lus = 0;
        while (lus < tampon.Length)
        {
            var n = await flux.ReadAsync(tampon.AsMemory(lus), ct);
            if (n == 0) break;
            lus += n;
        }
        return new BodycamMorceauDto(offset, flux.Length, Convert.ToBase64String(tampon, 0, lus));
    }

    public async Task<List<BodycamAccesDto>> AccesAsync(long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await db.QueryAsync(
            "SELECT id, `date`, agent, `action` FROM app_bodycam_acces WHERE enregistrement_id = @id ORDER BY `date` DESC, id DESC LIMIT 500",
            r => new BodycamAccesDto(r.Long("id"), r.Date("date"), r.Text("agent"), r.Text("action")), ct, ("@id", id));
    }

    public async Task<List<HistoriqueDto>> HistoriqueAsync(long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var e = await ChargerAsync(db, id, ct) ?? throw ApiException.Introuvable("Enregistrement introuvable.");
        return await Historique.ListerAsync(db, ObjetsComplements.Bodycam, e.Numero, 300, ct);
    }

    // ===== Documents associés =====

    public async Task<BodycamDto> AjouterDocumentAsync(AgentContext ctx, long id, PieceJointeInput input, CancellationToken ct)
    {
        var fichier = ComplementsOutils.DecoderPiece(input);
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var e = await ChargerAsync(db, id, ct, verrouiller: true) ?? throw ApiException.Introuvable("Enregistrement introuvable.");
        if (e.Statut == BodycamStatuts.Archive)
            throw ApiException.Conflit("Un enregistrement archivé n'est plus modifiable.");
        if (e.Documents.Count >= 20)
            throw ApiException.Saisie("20 documents au plus par enregistrement.");
        await db.ExecuteAsync(
            """
            INSERT INTO app_bodycam_documents (enregistrement_id, nom, type_mime, taille, sha256, contenu, auteur, cree_le)
            VALUES (@id, @nom, @type, @taille, @sha, @contenu, @auteur, @maintenant)
            """, ct,
            ("@id", id), ("@nom", fichier.Nom), ("@type", fichier.TypeMime), ("@taille", fichier.Contenu.LongLength), ("@sha", fichier.Sha256),
            ("@contenu", fichier.Contenu), ("@auteur", ctx.Auteur.Nom), ("@maintenant", DateTime.UtcNow));
        await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Bodycam, e.Numero, "Document associé", null, null, fichier.Nom, null, ct);
        var dto = await ChargerAsync(db, id, ct) ?? throw new InvalidOperationException("Enregistrement introuvable.");
        await db.CommitAsync(ct);
        return dto;
    }

    public async Task<PieceJointeContenuDto> DocumentAsync(AgentContext ctx, long documentId, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var enregistrement = await db.ScalarAsync("SELECT enregistrement_id FROM app_bodycam_documents WHERE id = @id", ct, ("@id", documentId))
                             ?? throw ApiException.Introuvable("Document introuvable.");
        var contenu = await db.FirstOrDefaultAsync(
                          "SELECT id, nom, type_mime, taille, contenu FROM app_bodycam_documents WHERE id = @id",
                          ComplementsOutils.LirePieceContenu, ct, ("@id", documentId))
                      ?? throw ApiException.Introuvable("Document introuvable.");
        var e = await ChargerAsync(db, Convert.ToInt64(enregistrement, CultureInfo.InvariantCulture), ct)
                ?? throw ApiException.Introuvable("Enregistrement introuvable.");
        await NoterAccesAsync(db, ctx, e, "Consultation du document " + contenu.Nom, ct);
        return contenu;
    }

    // ===== Outils =====

    private async Task NoterAccesAsync(SqlSession db, AgentContext ctx, BodycamDto e, string action, CancellationToken ct)
    {
        await db.ExecuteAsync(
            "INSERT INTO app_bodycam_acces (enregistrement_id, `date`, agent_id, agent, `action`) VALUES (@id, @date, @agent_id, @agent, @action)",
            ct, ("@id", e.Id), ("@date", DateTime.UtcNow), ("@agent_id", ctx.Agent.Id), ("@agent", ctx.Auteur.Nom),
            ("@action", SocleRegles.Tronquer(action, 100)));
        await journal.EcrireAsync(ctx.Agent.Id, ctx.Auteur.Nom, "Bodycam : " + action, e.Numero, "Autorisé");
    }

    private static string CheminPartiel(long id) => Path.Combine(Dossier, id.ToString(CultureInfo.InvariantCulture) + ".part");

    private static string CheminFinal(string nom)
    {
        var dossier = Dossier;
        var chemin = Path.GetFullPath(Path.Combine(dossier, Path.GetFileName(nom)));
        if (!chemin.StartsWith(Path.GetFullPath(dossier), StringComparison.Ordinal))
            throw ApiException.Saisie("Chemin de fichier invalide.");
        return chemin;
    }

    private Task PrevenirAsync(long id)
        => hub.BroadcastAsync(new RealtimeMessage(ComplementsRealtime.Bodycam, Text: id.ToString(CultureInfo.InvariantCulture), AppId: SaphirApps.Bodycam),
            CancellationToken.None);

    private static async Task<BodycamDto?> ChargerAsync(SqlSession db, long id, CancellationToken ct, bool verrouiller = false)
    {
        var documents = await db.QueryAsync(
            "SELECT id, nom, type_mime, taille FROM app_bodycam_documents WHERE enregistrement_id = @id ORDER BY id",
            ComplementsOutils.LirePiece, ct, ("@id", id));
        return await db.FirstOrDefaultAsync(
            "SELECT * FROM app_bodycam WHERE id = @id" + (verrouiller ? " FOR UPDATE" : string.Empty),
            r => new BodycamDto(
                    r.Long("id"), r.Text("numero"), r.Long("agent_id"), r.Text("agent"), r.Date("enregistre_le"), r.Int("duree_secondes"),
                    r.NullableText("intervention_numero"), r.NullableText("description"), r.NullableText("provenance"), r.Text("source"),
                    r.NullableText("url"), r.NullableText("fichier_nom"), r.NullableLong("fichier_taille"), r.NullableText("fichier_type"),
                    r.NullableText("fichier_sha256"), r.Long("fichier_recu"), r.Text("statut"), r.Text("cree_par"), r.Date("cree_le"),
                    r.Date("maj_le"), r.NullableText("motif_archivage"), r.Int("version"), documents),
            ct, ("@id", id));
    }

    /// <summary>Nom du fichier stocké (jamais transmis au poste).</summary>
    private static async Task<string?> CheminAsync(SqlSession db, long id, CancellationToken ct)
        => await db.ScalarAsync("SELECT fichier_chemin FROM app_bodycam WHERE id = @id", ct, ("@id", id)) as string;
}

internal static class BodycamEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/bodycam");
        string[] consulter = [Permissions.BodycamConsulter, Permissions.BodycamGerer];
        string[] gerer = [Permissions.BodycamGerer];

        g.MapGet("", (HttpContext http, BodycamStore store, string? q, string? intervention, long? agent, bool? archives) =>
            Api.RunAsync(http, consulter, async (_, ct) => Results.Ok(await store.ListerAsync(q, intervention, agent, archives == true, ct))));

        g.MapGet("/{id:long}", (HttpContext http, long id, BodycamStore store) =>
            Api.RunAsync(http, consulter, async (_, ct) => Results.Ok(await store.LireAsync(id, ct))));

        g.MapPost("", (HttpContext http, BodycamInput input, BodycamStore store) =>
            Api.RunAsync(http, gerer, async (ctx, ct) => Results.Ok(await store.EnregistrerAsync(ctx, null, input, ct))));

        g.MapPut("/{id:long}", (HttpContext http, long id, BodycamInput input, BodycamStore store) =>
            Api.RunAsync(http, gerer, async (ctx, ct) => Results.Ok(await store.EnregistrerAsync(ctx, id, input, ct))));

        g.MapPost("/{id:long}/archiver", (HttpContext http, long id, BodycamArchivageInput input, BodycamStore store) =>
            Api.RunAsync(http, gerer, async (ctx, ct) => Results.Ok(await store.ArchiverAsync(ctx, id, input, ct))));

        g.MapPost("/{id:long}/fichier/debut", (HttpContext http, long id, BodycamEnvoiDebutInput input, BodycamStore store) =>
            Api.RunAsync(http, gerer, async (ctx, ct) => Results.Ok(await store.DebutEnvoiAsync(ctx, id, input, ct))));

        g.MapPut("/{id:long}/fichier/morceau", (HttpContext http, long id, BodycamMorceauInput input, BodycamStore store) =>
            Api.RunAsync(http, gerer, async (_, ct) => Results.Ok(await store.MorceauAsync(id, input, ct))));

        g.MapPost("/{id:long}/fichier/fin", (HttpContext http, long id, BodycamStore store) =>
            Api.RunAsync(http, gerer, async (ctx, ct) => Results.Ok(await store.FinEnvoiAsync(ctx, id, ct))));

        g.MapPost("/{id:long}/visionner", (HttpContext http, long id, BodycamStore store) =>
            Api.RunAsync(http, consulter, async (ctx, ct) => Results.Ok(await store.VisionnerAsync(ctx, id, ct))));

        g.MapGet("/{id:long}/fichier", (HttpContext http, long id, long? offset, int? taille, BodycamStore store) =>
            Api.RunAsync(http, consulter, async (_, ct) => Results.Ok(await store.LireMorceauAsync(id, offset ?? 0, taille, ct))));

        g.MapGet("/{id:long}/acces", (HttpContext http, long id, BodycamStore store) =>
            Api.RunAsync(http, gerer, async (_, ct) => Results.Ok(await store.AccesAsync(id, ct))));

        g.MapGet("/{id:long}/historique", (HttpContext http, long id, BodycamStore store) =>
            Api.RunAsync(http, gerer, async (_, ct) => Results.Ok(await store.HistoriqueAsync(id, ct))));

        g.MapPost("/{id:long}/documents", (HttpContext http, long id, PieceJointeInput input, BodycamStore store) =>
            Api.RunAsync(http, gerer, async (ctx, ct) => Results.Ok(await store.AjouterDocumentAsync(ctx, id, input, ct))));

        g.MapGet("/documents/{id:long}", (HttpContext http, long id, BodycamStore store) =>
            Api.RunAsync(http, consulter, async (ctx, ct) => Results.Ok(await store.DocumentAsync(ctx, id, ct))));
    }
}
