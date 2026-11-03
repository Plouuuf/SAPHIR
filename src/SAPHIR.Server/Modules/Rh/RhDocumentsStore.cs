using System.Text;
using Saphir.Server.Data;
using Saphir.Server.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Rh;

namespace Saphir.Server.Modules.Rh;

/// <summary>Filtre de la liste des documents RH.</summary>
public sealed record FiltreDocuments(
    string? Q,
    long? CategorieId,
    long? DossierId,
    long? AgentId,
    long? ServiceId,
    string? Statut,
    bool ArchivesSeulement,
    bool InclureArchives);

/// <summary>
/// Gestion documentaire RH (section 5) : dossiers, catégories, documents (fichiers réels en base, versions, statuts,
/// validation qui fige le document) et modèles générant des PDF pour un agent. Tout est historisé (objet rh-document…).
/// </summary>
public sealed class RhDocumentsStore(SaphirDb db, ParametresService parametres)
{
    private const string SelectDocument = """
        SELECT d.id, d.reference, d.titre, d.type, d.categorie_id, d.dossier_id, d.description, d.agent_id, d.service_id,
               d.date_document, d.date_effet, d.date_expiration, d.confidentialite, d.statut, d.observations, d.version,
               d.modele_id, d.auteur, d.cree_le, d.maj_le, d.valide_par, d.valide_le,
               c.libelle AS categorie, dd.nom AS dossier, CONCAT(a.nom, ' ', a.prenom) AS agent, s.libelle AS service,
               v.nom_fichier, v.type_mime, v.taille
        FROM rh_documents d
        LEFT JOIN rh_doc_categories c ON c.id = d.categorie_id
        LEFT JOIN rh_doc_dossiers dd ON dd.id = d.dossier_id
        LEFT JOIN rh_agents a ON a.id = d.agent_id
        LEFT JOIN rh_services s ON s.id = d.service_id
        LEFT JOIN rh_document_versions v ON v.document_id = d.id AND v.numero = d.version
        """;

    private static RhDocumentDto LireDocument(SqlRow r) => new(
        r.Long("id"), r.Text("reference"), r.Text("titre"), r.NullableText("type"),
        r.NullableLong("categorie_id"), r.NullableText("categorie"), r.NullableLong("dossier_id"), r.NullableText("dossier"),
        r.NullableText("description"), r.NullableLong("agent_id"), r.NullableText("agent"), r.NullableLong("service_id"), r.NullableText("service"),
        RhSql.Jour(r, "date_document"), RhSql.Jour(r, "date_effet"), RhSql.Jour(r, "date_expiration"),
        r.Text("confidentialite"), r.Text("statut"), r.NullableText("observations"), r.Int("version", 1),
        r.NullableText("nom_fichier"), r.NullableText("type_mime"), r.Long("taille"),
        r.Text("auteur"), r.Date("cree_le"), r.Date("maj_le"), r.NullableText("valide_par"), r.NullableDate("valide_le"),
        r.NullableLong("modele_id"));

    // =====================================================================================
    // Lecture
    // =====================================================================================

    public async Task<List<RhDocumentDto>> ListerAsync(FiltreDocuments filtre, bool voirConfidentiel, CancellationToken ct)
    {
        await using var session = await db.OpenAsync(ct);
        return await ListerAsync(session, filtre, voirConfidentiel, ct);
    }

    public static Task<List<RhDocumentDto>> ListerAsync(SqlSession session, FiltreDocuments f, bool voirConfidentiel, CancellationToken ct)
    {
        var sql = new StringBuilder(SelectDocument).Append(" WHERE 1 = 1");
        var p = new List<(string Name, object? Value)>();
        if (!voirConfidentiel)
        {
            sql.Append(" AND d.confidentialite <> @confidentiel");
            p.Add(("@confidentiel", RhConfidentialites.Confidentiel));
        }
        if (f.ArchivesSeulement)
            sql.Append(" AND d.statut = @archive");
        else if (!f.InclureArchives)
            sql.Append(" AND d.statut <> @archive");
        p.Add(("@archive", RhDocumentStatuts.Archive));
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            sql.Append(" AND (d.titre LIKE @q OR d.reference LIKE @q OR d.description LIKE @q OR d.type LIKE @q OR v.nom_fichier LIKE @q OR CONCAT(a.nom, ' ', a.prenom) LIKE @q)");
            p.Add(("@q", "%" + SocleRegles.EchapperLike(f.Q.Trim()) + "%"));
        }
        if (f.CategorieId is not null)
        {
            sql.Append(" AND d.categorie_id = @categorie");
            p.Add(("@categorie", f.CategorieId));
        }
        if (f.DossierId is not null)
        {
            sql.Append(" AND d.dossier_id = @dossier");
            p.Add(("@dossier", f.DossierId));
        }
        if (f.AgentId is not null)
        {
            sql.Append(" AND d.agent_id = @agent");
            p.Add(("@agent", f.AgentId));
        }
        if (f.ServiceId is not null)
        {
            sql.Append(" AND d.service_id = @service");
            p.Add(("@service", f.ServiceId));
        }
        if (!string.IsNullOrWhiteSpace(f.Statut))
        {
            sql.Append(" AND d.statut = @statut");
            p.Add(("@statut", f.Statut.Trim()));
        }
        sql.Append(" ORDER BY d.maj_le DESC, d.id DESC LIMIT 1000");
        return session.QueryAsync(sql.ToString(), LireDocument, ct, p.ToArray());
    }

    public async Task<RhDocumentDetailDto> LireDetailAsync(long id, bool voirConfidentiel, CancellationToken ct)
    {
        await using var session = await db.OpenAsync(ct);
        var document = await LireVisibleAsync(session, id, voirConfidentiel, false, ct);
        var versions = await session.QueryAsync(
            """
            SELECT id, numero, nom_fichier, type_mime, taille, sha256, commentaire, statut, auteur, cree_le, valide_par, valide_le,
                   (texte IS NOT NULL) AS a_texte
            FROM rh_document_versions WHERE document_id = @id ORDER BY numero DESC
            """,
            r => new RhDocumentVersionDto(r.Long("id"), r.Int("numero"), r.Text("nom_fichier"), r.Text("type_mime"), r.Long("taille"),
                r.Text("sha256"), r.NullableText("commentaire"), r.Text("statut"), r.Text("auteur"), r.Date("cree_le"),
                r.NullableText("valide_par"), r.NullableDate("valide_le"), r.Bool("a_texte")), ct, ("@id", id));
        var historique = await Historique.ListerAsync(session, ObjetsRh.Document, document.Reference, 300, ct);
        return new RhDocumentDetailDto(document, versions, historique);
    }

    /// <summary>Fichier d'une version (contenu complet). 404 si le document n'est pas visible pour l'agent.</summary>
    public async Task<(RhDocumentDto Document, RhFichierDto Fichier)> FichierAsync(long id, int numero, bool voirConfidentiel, CancellationToken ct)
    {
        await using var session = await db.OpenAsync(ct);
        var document = await LireVisibleAsync(session, id, voirConfidentiel, false, ct);
        var fichier = await session.FirstOrDefaultAsync(
            "SELECT nom_fichier, type_mime, contenu, texte FROM rh_document_versions WHERE document_id = @id AND numero = @numero",
            r => new RhFichierDto(r.Text("nom_fichier"), r.Text("type_mime"), r.Value("contenu") as byte[] ?? [], r.NullableText("texte")),
            ct, ("@id", id), ("@numero", numero <= 0 ? document.Version : numero))
            ?? throw ApiException.Introuvable("Version introuvable.");
        return (document, fichier);
    }

    private static async Task<RhDocumentDto> LireVisibleAsync(SqlSession session, long id, bool voirConfidentiel, bool verrouiller, CancellationToken ct)
    {
        if (verrouiller)
            await session.ExecuteAsync("SELECT id FROM rh_documents WHERE id = @id FOR UPDATE", ct, ("@id", id));
        var document = await session.FirstOrDefaultAsync(SelectDocument + " WHERE d.id = @id", LireDocument, ct, ("@id", id));
        if (document is null || (!voirConfidentiel && document.Confidentialite == RhConfidentialites.Confidentiel))
            throw ApiException.Introuvable("Document introuvable.");
        return document;
    }

    // =====================================================================================
    // Création, métadonnées, versions
    // =====================================================================================

    public async Task<RhDocumentDto> CreerAsync(Auteur auteur, RhDocumentCreationInput input, CancellationToken ct)
    {
        var (controle, erreur) = RhRegles.ControlerFichier(input.NomFichier, input.Contenu);
        if (controle is null)
            throw ApiException.Saisie(erreur ?? "Fichier refusé.");

        await using var session = await db.OpenAsync(ct);
        await session.BeginAsync(ct);
        var meta = await ValiderMetadonneesAsync(session, input.Titre, input.Type, input.CategorieId, input.DossierId, input.Description,
            input.AgentId, input.ServiceId, input.Confidentialite, input.Observations, ct);
        var statut = input.Soumettre ? RhDocumentStatuts.AValider : RhDocumentStatuts.Brouillon;

        var id = await InsererDocumentAsync(session, auteur, meta, input.DateDocument ?? RhSql.Aujourdhui(), input.DateEffet,
            input.DateExpiration, statut, null, ct);
        var reference = RhRegles.ReferenceDocument(id);
        await InsererVersionAsync(session, auteur, id, 1, controle, input.Contenu, null, "Version initiale", statut, ct);
        await Historique.EcrireAsync(session, auteur, ObjetsRh.Document, reference, "Création", null, null,
            $"{meta.Titre} — {controle.NomFichier} ({FormatTaille(input.Contenu.Length)}), statut « {statut} »", null, ct);

        var dto = await LireVisibleAsync(session, id, true, false, ct);
        await session.CommitAsync(ct);
        return dto;
    }

    /// <summary>Renommer, déplacer, classer… Document validé : seul le dossier (classement) peut changer. Archivé : rien.</summary>
    public async Task<RhDocumentDto> ModifierAsync(Auteur auteur, long id, RhDocumentMajInput input, bool voirConfidentiel, CancellationToken ct)
    {
        await using var session = await db.OpenAsync(ct);
        await session.BeginAsync(ct);
        var actuel = await LireVisibleAsync(session, id, voirConfidentiel, true, ct);
        if (actuel.Statut == RhDocumentStatuts.Archive)
            throw ApiException.Conflit("Document archivé : restaure-le avant de le modifier.");
        var meta = await ValiderMetadonneesAsync(session, input.Titre, input.Type, input.CategorieId, input.DossierId, input.Description,
            input.AgentId, input.ServiceId, input.Confidentialite, input.Observations, ct);
        var motif = SocleRegles.Tronquer(input.Motif, 500);

        var changements = new List<(string Champ, string? Ancienne, string? Nouvelle)>();
        void Noter(string champ, string? ancienne, string? nouvelle)
        {
            if (!string.Equals(ancienne ?? string.Empty, nouvelle ?? string.Empty, StringComparison.Ordinal))
                changements.Add((champ, ancienne, nouvelle));
        }
        Noter("titre", actuel.Titre, meta.Titre);
        Noter("type", actuel.Type, meta.Type);
        Noter("categorie", actuel.Categorie, meta.CategorieLibelle);
        Noter("description", actuel.Description, meta.Description);
        Noter("agent", actuel.Agent, meta.AgentLibelle);
        Noter("service", actuel.Service, meta.ServiceLibelle);
        Noter("date_document", RhRegles.Date(actuel.DateDocument), RhRegles.Date(input.DateDocument ?? actuel.DateDocument));
        Noter("date_effet", RhRegles.Date(actuel.DateEffet), RhRegles.Date(input.DateEffet));
        Noter("date_expiration", RhRegles.Date(actuel.DateExpiration), RhRegles.Date(input.DateExpiration));
        Noter("confidentialite", actuel.Confidentialite, meta.Confidentialite);
        Noter("observations", actuel.Observations, meta.Observations);
        var deplacement = actuel.DossierId != meta.DossierId;

        if (actuel.EstFige && changements.Count > 0)
            throw ApiException.Conflit("Document validé : il est figé. Seul son dossier peut changer ; dépose une nouvelle version pour le modifier.");
        if (changements.Count == 0 && !deplacement)
            return actuel;

        if (deplacement)
        {
            await session.ExecuteAsync("UPDATE rh_documents SET dossier_id = @dossier, maj_le = @maintenant WHERE id = @id", ct,
                ("@dossier", meta.DossierId), ("@maintenant", DateTime.UtcNow), ("@id", id));
            await Historique.EcrireAsync(session, auteur, ObjetsRh.Document, actuel.Reference, "Déplacement", "dossier",
                actuel.Dossier, meta.DossierLibelle, motif, ct);
        }
        if (changements.Count > 0)
        {
            await session.ExecuteAsync(
                """
                UPDATE rh_documents SET titre = @titre, type = @type, categorie_id = @categorie, description = @description,
                    agent_id = @agent, service_id = @service, date_document = @date_document, date_effet = @date_effet,
                    date_expiration = @date_expiration, confidentialite = @confidentialite, observations = @observations, maj_le = @maintenant
                WHERE id = @id
                """, ct,
                ("@titre", meta.Titre), ("@type", meta.Type), ("@categorie", meta.CategorieId), ("@description", meta.Description),
                ("@agent", meta.AgentId), ("@service", meta.ServiceId), ("@date_document", RhSql.P(input.DateDocument ?? actuel.DateDocument)),
                ("@date_effet", RhSql.P(input.DateEffet)), ("@date_expiration", RhSql.P(input.DateExpiration)),
                ("@confidentialite", meta.Confidentialite), ("@observations", meta.Observations), ("@maintenant", DateTime.UtcNow), ("@id", id));
            foreach (var (champ, ancienne, nouvelle) in changements)
                await Historique.EcrireAsync(session, auteur, ObjetsRh.Document, actuel.Reference, "Modification", champ, ancienne, nouvelle, motif, ct);
        }

        var dto = await LireVisibleAsync(session, id, true, false, ct);
        await session.CommitAsync(ct);
        return dto;
    }

    /// <summary>Nouvelle version : l'ancienne est conservée (« Remplacée » si elle n'était pas validée) ; le document repasse en brouillon.</summary>
    public async Task<RhDocumentDto> NouvelleVersionAsync(Auteur auteur, long id, RhVersionInput input, bool voirConfidentiel, CancellationToken ct)
    {
        var (controle, erreur) = RhRegles.ControlerFichier(input.NomFichier, input.Contenu);
        if (controle is null)
            throw ApiException.Saisie(erreur ?? "Fichier refusé.");
        var commentaire = RhSql.MotifObligatoire(input.Commentaire, "Le commentaire de modification est obligatoire.");

        await using var session = await db.OpenAsync(ct);
        await session.BeginAsync(ct);
        var actuel = await LireVisibleAsync(session, id, voirConfidentiel, true, ct);
        if (actuel.Statut == RhDocumentStatuts.Archive)
            throw ApiException.Conflit("Document archivé : restaure-le avant d'ajouter une version.");

        var numero = actuel.Version + 1;
        await session.ExecuteAsync(
            "UPDATE rh_document_versions SET statut = @remplacee WHERE document_id = @id AND statut IN (@brouillon, @avalider)", ct,
            ("@remplacee", "Remplacée"), ("@id", id), ("@brouillon", RhDocumentStatuts.Brouillon), ("@avalider", RhDocumentStatuts.AValider));
        await InsererVersionAsync(session, auteur, id, numero, controle, input.Contenu, null, commentaire, RhDocumentStatuts.Brouillon, ct);
        await session.ExecuteAsync(
            "UPDATE rh_documents SET version = @numero, statut = @statut, valide_par = NULL, valide_le = NULL, maj_le = @maintenant WHERE id = @id", ct,
            ("@numero", numero), ("@statut", RhDocumentStatuts.Brouillon), ("@maintenant", DateTime.UtcNow), ("@id", id));
        await Historique.EcrireAsync(session, auteur, ObjetsRh.Document, actuel.Reference, "Nouvelle version", "version",
            actuel.Version.ToString(), $"{numero} — {controle.NomFichier} ({FormatTaille(input.Contenu.Length)})", commentaire, ct);

        var dto = await LireVisibleAsync(session, id, true, false, ct);
        await session.CommitAsync(ct);
        return dto;
    }

    // =====================================================================================
    // Circuit de validation et archivage
    // =====================================================================================

    public Task<RhDocumentDto> SoumettreAsync(Auteur auteur, long id, bool voirConfidentiel, CancellationToken ct)
        => ChangerStatutAsync(auteur, id, voirConfidentiel, "Soumission à validation", null, ct, actuel =>
        {
            if (actuel.Statut != RhDocumentStatuts.Brouillon)
                throw ApiException.Conflit("Seul un brouillon peut être soumis à validation.");
            return RhDocumentStatuts.AValider;
        });

    /// <summary>Validation (rh.documents.valider) : le document et sa version courante sont figés.</summary>
    public Task<RhDocumentDto> ValiderAsync(Auteur auteur, long id, bool voirConfidentiel, CancellationToken ct)
        => ChangerStatutAsync(auteur, id, voirConfidentiel, "Validation", null, ct, actuel =>
        {
            if (actuel.Statut is not (RhDocumentStatuts.AValider or RhDocumentStatuts.Brouillon))
                throw ApiException.Conflit("Seul un document en brouillon ou à valider peut être validé.");
            return RhDocumentStatuts.Valide;
        });

    public Task<RhDocumentDto> RefuserAsync(Auteur auteur, long id, string? motif, bool voirConfidentiel, CancellationToken ct)
    {
        var m = RhSql.MotifObligatoire(motif, "Le motif du refus est obligatoire.");
        return ChangerStatutAsync(auteur, id, voirConfidentiel, "Refus de validation", m, ct, actuel =>
        {
            if (actuel.Statut != RhDocumentStatuts.AValider)
                throw ApiException.Conflit("Seul un document à valider peut être refusé.");
            return RhDocumentStatuts.Brouillon;
        });
    }

    public Task<RhDocumentDto> ArchiverAsync(Auteur auteur, long id, string? motif, bool voirConfidentiel, CancellationToken ct)
        => ChangerStatutAsync(auteur, id, voirConfidentiel, "Archivage", SocleRegles.Tronquer(motif, 500), ct, actuel =>
        {
            if (actuel.Statut == RhDocumentStatuts.Archive)
                throw ApiException.Conflit("Le document est déjà archivé.");
            return RhDocumentStatuts.Archive;
        });

    public async Task<RhDocumentDto> RestaurerAsync(Auteur auteur, long id, bool voirConfidentiel, CancellationToken ct)
    {
        await using var session = await db.OpenAsync(ct);
        await session.BeginAsync(ct);
        var actuel = await LireVisibleAsync(session, id, voirConfidentiel, true, ct);
        if (actuel.Statut != RhDocumentStatuts.Archive)
            throw ApiException.Conflit("Le document n'est pas archivé.");
        var avant = await session.ScalarAsync("SELECT statut_avant_archive FROM rh_documents WHERE id = @id", ct, ("@id", id)) as string;
        var statut = avant is not null && RhDocumentStatuts.All.Contains(avant) && avant != RhDocumentStatuts.Archive ? avant : RhDocumentStatuts.Brouillon;
        await session.ExecuteAsync(
            "UPDATE rh_documents SET statut = @statut, statut_avant_archive = NULL, maj_le = @maintenant WHERE id = @id", ct,
            ("@statut", statut), ("@maintenant", DateTime.UtcNow), ("@id", id));
        await Historique.EcrireAsync(session, auteur, ObjetsRh.Document, actuel.Reference, "Restauration", "statut",
            RhDocumentStatuts.Archive, statut, null, ct);
        var dto = await LireVisibleAsync(session, id, true, false, ct);
        await session.CommitAsync(ct);
        return dto;
    }

    private async Task<RhDocumentDto> ChangerStatutAsync(Auteur auteur, long id, bool voirConfidentiel, string action, string? motif,
        CancellationToken ct, Func<RhDocumentDto, string> regle)
    {
        await using var session = await db.OpenAsync(ct);
        await session.BeginAsync(ct);
        var actuel = await LireVisibleAsync(session, id, voirConfidentiel, true, ct);
        var statut = regle(actuel);
        var maintenant = DateTime.UtcNow;

        if (statut == RhDocumentStatuts.Archive)
        {
            await session.ExecuteAsync(
                "UPDATE rh_documents SET statut = @statut, statut_avant_archive = @avant, maj_le = @maintenant WHERE id = @id", ct,
                ("@statut", statut), ("@avant", actuel.Statut), ("@maintenant", maintenant), ("@id", id));
        }
        else
        {
            var valide = statut == RhDocumentStatuts.Valide;
            await session.ExecuteAsync(
                $"UPDATE rh_documents SET statut = @statut, maj_le = @maintenant{(valide ? ", valide_par = @par, valide_le = @maintenant" : string.Empty)} WHERE id = @id", ct,
                ("@statut", statut), ("@maintenant", maintenant), ("@par", auteur.Nom), ("@id", id));
            await session.ExecuteAsync(
                $"UPDATE rh_document_versions SET statut = @statut{(valide ? ", valide_par = @par, valide_le = @maintenant" : string.Empty)} WHERE document_id = @id AND numero = @numero", ct,
                ("@statut", statut), ("@maintenant", maintenant), ("@par", auteur.Nom), ("@id", id), ("@numero", actuel.Version));
        }
        await Historique.EcrireAsync(session, auteur, ObjetsRh.Document, actuel.Reference, action, "statut", actuel.Statut, statut, motif, ct);
        var dto = await LireVisibleAsync(session, id, true, false, ct);
        await session.CommitAsync(ct);
        return dto;
    }

    // =====================================================================================
    // Aides documents
    // =====================================================================================

    private sealed record Metadonnees(
        string Titre, string? Type, long? CategorieId, string? CategorieLibelle, long? DossierId, string? DossierLibelle,
        string? Description, long? AgentId, string? AgentLibelle, long? ServiceId, string? ServiceLibelle, string Confidentialite,
        string? Observations);

    private static async Task<Metadonnees> ValiderMetadonneesAsync(SqlSession session, string? titre, string? type, long? categorieId,
        long? dossierId, string? description, long? agentId, long? serviceId, string? confidentialite, string? observations, CancellationToken ct)
    {
        var t = (titre ?? string.Empty).Trim();
        if (t.Length is < 2 or > 200)
            throw ApiException.Saisie("Le nom du document doit faire entre 2 et 200 caractères.");
        string? categorie = null, dossier = null, agent = null, service = null;
        if (categorieId is { } c)
            categorie = await session.FirstOrDefaultAsync("SELECT libelle FROM rh_doc_categories WHERE id = @id", r => r.Text("libelle"), ct, ("@id", c))
                        ?? throw ApiException.Saisie("Catégorie introuvable.");
        if (dossierId is { } d)
            dossier = await session.FirstOrDefaultAsync("SELECT nom FROM rh_doc_dossiers WHERE id = @id", r => r.Text("nom"), ct, ("@id", d))
                      ?? throw ApiException.Saisie("Dossier introuvable.");
        if (agentId is { } a)
            agent = await session.FirstOrDefaultAsync("SELECT CONCAT(nom, ' ', prenom) AS l FROM rh_agents WHERE id = @id", r => r.Text("l"), ct, ("@id", a))
                    ?? throw ApiException.Saisie("Agent introuvable.");
        if (serviceId is { } s)
            service = await session.FirstOrDefaultAsync("SELECT libelle FROM rh_services WHERE id = @id", r => r.Text("libelle"), ct, ("@id", s))
                      ?? throw ApiException.Saisie("Service introuvable.");
        return new Metadonnees(t, SocleRegles.Tronquer(type, 100), categorieId, categorie, dossierId, dossier,
            SocleRegles.Tronquer(description, 4000), agentId, agent, serviceId, service,
            RhRegles.NormaliserConfidentialite(confidentialite), SocleRegles.Tronquer(observations, 4000));
    }

    private static async Task<long> InsererDocumentAsync(SqlSession session, Auteur auteur, Metadonnees meta, DateOnly dateDocument,
        DateOnly? dateEffet, DateOnly? dateExpiration, string statut, long? modeleId, CancellationToken ct)
    {
        var maintenant = DateTime.UtcNow;
        var id = await session.InsertAsync(
            """
            INSERT INTO rh_documents (titre, type, categorie_id, dossier_id, description, agent_id, service_id, date_document, date_effet,
                date_expiration, confidentialite, statut, observations, version, modele_id, auteur_id, auteur, cree_le, maj_le)
            VALUES (@titre, @type, @categorie, @dossier, @description, @agent, @service, @date_document, @date_effet,
                @date_expiration, @confidentialite, @statut, @observations, 1, @modele, @auteur_id, @auteur, @maintenant, @maintenant)
            """, ct,
            ("@titre", meta.Titre), ("@type", meta.Type), ("@categorie", meta.CategorieId), ("@dossier", meta.DossierId),
            ("@description", meta.Description), ("@agent", meta.AgentId), ("@service", meta.ServiceId),
            ("@date_document", RhSql.P(dateDocument)), ("@date_effet", RhSql.P(dateEffet)), ("@date_expiration", RhSql.P(dateExpiration)),
            ("@confidentialite", meta.Confidentialite), ("@statut", statut), ("@observations", meta.Observations), ("@modele", modeleId),
            ("@auteur_id", auteur.Id), ("@auteur", auteur.Nom), ("@maintenant", maintenant));
        await session.ExecuteAsync("UPDATE rh_documents SET reference = @reference WHERE id = @id", ct,
            ("@reference", RhRegles.ReferenceDocument(id)), ("@id", id));
        return id;
    }

    private static Task InsererVersionAsync(SqlSession session, Auteur auteur, long documentId, int numero, FichierControle controle,
        byte[] contenu, string? texte, string? commentaire, string statut, CancellationToken ct)
        => session.ExecuteAsync(
            """
            INSERT INTO rh_document_versions (document_id, numero, nom_fichier, type_mime, taille, sha256, contenu, texte, commentaire, statut, auteur_id, auteur, cree_le)
            VALUES (@document, @numero, @nom, @mime, @taille, @sha, @contenu, @texte, @commentaire, @statut, @auteur_id, @auteur, @maintenant)
            """, ct,
            ("@document", documentId), ("@numero", numero), ("@nom", controle.NomFichier), ("@mime", controle.TypeMime),
            ("@taille", (long)contenu.Length), ("@sha", controle.Sha256), ("@contenu", contenu), ("@texte", texte),
            ("@commentaire", SocleRegles.Tronquer(commentaire, 500)), ("@statut", statut), ("@auteur_id", auteur.Id),
            ("@auteur", auteur.Nom), ("@maintenant", DateTime.UtcNow));

    private static string FormatTaille(long octets) => octets switch
    {
        < 1024 => $"{octets} o",
        < 1024 * 1024 => $"{octets / 1024.0:0.#} Ko",
        _ => $"{octets / (1024.0 * 1024.0):0.#} Mo"
    };

    // =====================================================================================
    // Dossiers
    // =====================================================================================

    public async Task<List<RhDossierDocDto>> ListerDossiersAsync(CancellationToken ct)
    {
        await using var session = await db.OpenAsync(ct);
        return await ListerDossiersAsync(session, ct);
    }

    private static async Task<List<RhDossierDocDto>> ListerDossiersAsync(SqlSession session, CancellationToken ct)
    {
        var lignes = await session.QueryAsync(
            """
            SELECT dd.id, dd.parent_id, dd.nom,
                   (SELECT COUNT(*) FROM rh_documents d WHERE d.dossier_id = dd.id AND d.statut <> @archive) AS nombre
            FROM rh_doc_dossiers dd
            """,
            r => (Id: r.Long("id"), ParentId: r.NullableLong("parent_id"), Nom: r.Text("nom"), Nombre: r.Int("nombre")), ct,
            ("@archive", RhDocumentStatuts.Archive));
        var parId = lignes.ToDictionary(l => l.Id);
        string Chemin(long id)
        {
            var noms = new List<string>();
            var courant = (long?)id;
            var garde = 0;
            while (courant is { } c && parId.TryGetValue(c, out var l) && garde++ < 50)
            {
                noms.Insert(0, l.Nom);
                courant = l.ParentId;
            }
            return string.Join(" \\ ", noms);
        }
        return lignes
            .Select(l => new RhDossierDocDto(l.Id, l.ParentId, l.Nom, Chemin(l.Id), l.Nombre))
            .OrderBy(d => d.Chemin, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("fr-FR"), true))
            .ToList();
    }

    public async Task<RhDossierDocDto> CreerDossierAsync(Auteur auteur, RhDossierDocInput input, CancellationToken ct)
    {
        var nom = ValiderNomDossier(input.Nom);
        await using var session = await db.OpenAsync(ct);
        await session.BeginAsync(ct);
        if (input.ParentId is { } parent && await session.LongAsync("SELECT COUNT(*) FROM rh_doc_dossiers WHERE id = @id", ct, ("@id", parent)) == 0)
            throw ApiException.Saisie("Dossier parent introuvable.");
        await VerifierNomLibreAsync(session, nom, input.ParentId, null, ct);
        var maintenant = DateTime.UtcNow;
        var id = await session.InsertAsync(
            "INSERT INTO rh_doc_dossiers (parent_id, nom, cree_le, maj_le) VALUES (@parent, @nom, @maintenant, @maintenant)", ct,
            ("@parent", input.ParentId), ("@nom", nom), ("@maintenant", maintenant));
        var dto = (await ListerDossiersAsync(session, ct)).Single(d => d.Id == id);
        await Historique.EcrireAsync(session, auteur, ObjetsRh.Dossier, dto.Chemin, "Création", null, null, dto.Chemin, null, ct);
        await session.CommitAsync(ct);
        return dto;
    }

    /// <summary>Renommer ou déplacer un dossier (jamais dans lui-même ni dans un de ses sous-dossiers).</summary>
    public async Task<RhDossierDocDto> ModifierDossierAsync(Auteur auteur, long id, RhDossierDocInput input, CancellationToken ct)
    {
        var nom = ValiderNomDossier(input.Nom);
        await using var session = await db.OpenAsync(ct);
        await session.BeginAsync(ct);
        var dossiers = await ListerDossiersAsync(session, ct);
        var actuel = dossiers.FirstOrDefault(d => d.Id == id) ?? throw ApiException.Introuvable("Dossier introuvable.");
        if (input.ParentId is { } parent)
        {
            if (dossiers.All(d => d.Id != parent))
                throw ApiException.Saisie("Dossier parent introuvable.");
            var courant = (long?)parent;
            var garde = 0;
            while (courant is { } c && garde++ < 100)
            {
                if (c == id)
                    throw ApiException.Saisie("Un dossier ne peut pas être déplacé dans lui-même ou dans un de ses sous-dossiers.");
                courant = dossiers.FirstOrDefault(d => d.Id == c)?.ParentId;
            }
        }
        if (nom == actuel.Nom && input.ParentId == actuel.ParentId)
            return actuel;
        await VerifierNomLibreAsync(session, nom, input.ParentId, id, ct);
        await session.ExecuteAsync("UPDATE rh_doc_dossiers SET nom = @nom, parent_id = @parent, maj_le = @maintenant WHERE id = @id", ct,
            ("@nom", nom), ("@parent", input.ParentId), ("@maintenant", DateTime.UtcNow), ("@id", id));
        var dto = (await ListerDossiersAsync(session, ct)).Single(d => d.Id == id);
        await Historique.EcrireAsync(session, auteur, ObjetsRh.Dossier, dto.Chemin,
            input.ParentId == actuel.ParentId ? "Renommage" : "Déplacement", "chemin", actuel.Chemin, dto.Chemin, null, ct);
        await session.CommitAsync(ct);
        return dto;
    }

    /// <summary>Supprime un dossier vide (sans document, même archivé, ni sous-dossier).</summary>
    public async Task SupprimerDossierAsync(Auteur auteur, long id, CancellationToken ct)
    {
        await using var session = await db.OpenAsync(ct);
        await session.BeginAsync(ct);
        var dossiers = await ListerDossiersAsync(session, ct);
        var actuel = dossiers.FirstOrDefault(d => d.Id == id) ?? throw ApiException.Introuvable("Dossier introuvable.");
        if (dossiers.Any(d => d.ParentId == id))
            throw ApiException.Conflit("Ce dossier contient des sous-dossiers.");
        if (await session.LongAsync("SELECT COUNT(*) FROM rh_documents WHERE dossier_id = @id", ct, ("@id", id)) > 0)
            throw ApiException.Conflit("Ce dossier contient des documents (y compris archivés) : déplace-les d'abord.");
        await session.ExecuteAsync("DELETE FROM rh_doc_dossiers WHERE id = @id", ct, ("@id", id));
        await Historique.EcrireAsync(session, auteur, ObjetsRh.Dossier, actuel.Chemin, "Suppression", null, actuel.Chemin, null, null, ct);
        await session.CommitAsync(ct);
    }

    private static string ValiderNomDossier(string? nom)
    {
        var n = RhRegles.NettoyerNomFichier(nom).Trim();
        if (string.IsNullOrWhiteSpace(nom) || n.Length is < 1 or > 120)
            throw ApiException.Saisie("Le nom du dossier doit faire entre 1 et 120 caractères.");
        return n;
    }

    private static async Task VerifierNomLibreAsync(SqlSession session, string nom, long? parentId, long? id, CancellationToken ct)
    {
        var pris = await session.LongAsync(
            "SELECT COUNT(*) FROM rh_doc_dossiers WHERE nom = @nom AND ((@parent IS NULL AND parent_id IS NULL) OR parent_id = @parent) AND (@id IS NULL OR id <> @id)",
            ct, ("@nom", nom), ("@parent", parentId), ("@id", id));
        if (pris > 0)
            throw ApiException.Conflit("Un dossier porte déjà ce nom à cet endroit.");
    }

    // =====================================================================================
    // Catégories (personnalisables)
    // =====================================================================================

    public async Task<List<RhCategorieDto>> ListerCategoriesAsync(CancellationToken ct)
    {
        await using var session = await db.OpenAsync(ct);
        return await session.QueryAsync("SELECT * FROM rh_doc_categories ORDER BY ordre, libelle",
            r => new RhCategorieDto(r.Long("id"), r.Text("code"), r.Text("libelle"), r.Text("groupe"), r.Int("ordre"), r.Bool("actif")), ct);
    }

    public async Task<RhCategorieDto> EnregistrerCategorieAsync(Auteur auteur, long? id, RhCategorieInput input, CancellationToken ct)
    {
        var libelle = (input.Libelle ?? string.Empty).Trim();
        if (libelle.Length is < 2 or > 100)
            throw ApiException.Saisie("Le libellé de la catégorie doit faire entre 2 et 100 caractères.");
        var groupe = RhCategorieGroupes.All.FirstOrDefault(g => g == input.Groupe?.Trim())
                     ?? throw ApiException.Saisie($"Groupe inconnu. Groupes possibles : {string.Join(", ", RhCategorieGroupes.All)}.");
        if (input.Ordre is < 0 or > 99999)
            throw ApiException.Saisie("L'ordre doit être compris entre 0 et 99999.");

        await using var session = await db.OpenAsync(ct);
        await session.BeginAsync(ct);
        if (await session.LongAsync("SELECT COUNT(*) FROM rh_doc_categories WHERE libelle = @l AND (@id IS NULL OR id <> @id)", ct,
                ("@l", libelle), ("@id", id)) > 0)
            throw ApiException.Conflit("Une catégorie porte déjà ce libellé.");
        var maintenant = DateTime.UtcNow;
        string code;
        if (id is null)
        {
            var racine = RhRegles.CodeReferentiel(libelle);
            code = racine;
            for (var n = 2; await session.LongAsync("SELECT COUNT(*) FROM rh_doc_categories WHERE code = @c", ct, ("@c", code)) > 0; n++)
                code = SocleRegles.CodeAvecSuffixe(racine, n).ToUpperInvariant();
            id = await session.InsertAsync(
                "INSERT INTO rh_doc_categories (code, libelle, groupe, ordre, actif, cree_le, maj_le) VALUES (@c, @l, @g, @o, @a, @m, @m)", ct,
                ("@c", code), ("@l", libelle), ("@g", groupe), ("@o", input.Ordre), ("@a", input.Actif), ("@m", maintenant));
            await Historique.EcrireAsync(session, auteur, ObjetsRh.Categorie, code, "Création", null, null,
                $"{libelle} ({groupe}){(input.Actif ? string.Empty : ", inactive")}", null, ct);
        }
        else
        {
            var actuel = await session.FirstOrDefaultAsync("SELECT * FROM rh_doc_categories WHERE id = @id FOR UPDATE",
                             r => new RhCategorieDto(r.Long("id"), r.Text("code"), r.Text("libelle"), r.Text("groupe"), r.Int("ordre"), r.Bool("actif")),
                             ct, ("@id", id)) ?? throw ApiException.Introuvable("Catégorie introuvable.");
            code = actuel.Code;
            await session.ExecuteAsync("UPDATE rh_doc_categories SET libelle = @l, groupe = @g, ordre = @o, actif = @a, maj_le = @m WHERE id = @id", ct,
                ("@l", libelle), ("@g", groupe), ("@o", input.Ordre), ("@a", input.Actif), ("@m", maintenant), ("@id", id));
            await Historique.EcrireAsync(session, auteur, ObjetsRh.Categorie, code, "Modification", "categorie",
                $"{actuel.Libelle} ({actuel.Groupe}, ordre {actuel.Ordre}, {(actuel.Actif ? "active" : "inactive")})",
                $"{libelle} ({groupe}, ordre {input.Ordre}, {(input.Actif ? "active" : "inactive")})", null, ct);
        }
        await session.CommitAsync(ct);
        return new RhCategorieDto(id.Value, code, libelle, groupe, input.Ordre, input.Actif);
    }

    // =====================================================================================
    // Modèles (section 5.6)
    // =====================================================================================

    private const string SelectModele = """
        SELECT m.id, m.code, m.libelle, m.type, m.categorie_id, c.libelle AS categorie, m.contenu, m.actif, m.maj_le
        FROM rh_modeles m LEFT JOIN rh_doc_categories c ON c.id = m.categorie_id
        """;

    private static RhModeleDto LireModele(SqlRow r) => new(r.Long("id"), r.Text("code"), r.Text("libelle"), r.Text("type"),
        r.NullableLong("categorie_id"), r.NullableText("categorie"), r.Text("contenu"), r.Bool("actif"), r.Date("maj_le"));

    public async Task<List<RhModeleDto>> ListerModelesAsync(CancellationToken ct)
    {
        await using var session = await db.OpenAsync(ct);
        return await session.QueryAsync(SelectModele + " ORDER BY m.libelle", LireModele, ct);
    }

    public async Task<RhModeleDto> EnregistrerModeleAsync(Auteur auteur, long? id, RhModeleInput input, CancellationToken ct)
    {
        var libelle = (input.Libelle ?? string.Empty).Trim();
        if (libelle.Length is < 2 or > 150)
            throw ApiException.Saisie("Le nom du modèle doit faire entre 2 et 150 caractères.");
        var type = SocleRegles.Tronquer(input.Type, 100) ?? throw ApiException.Saisie("Le type du modèle est obligatoire.");
        var contenu = input.Contenu ?? string.Empty;
        if (string.IsNullOrWhiteSpace(contenu) || contenu.Length > 100_000)
            throw ApiException.Saisie("Le texte du modèle est obligatoire (100 000 caractères au plus).");

        await using var session = await db.OpenAsync(ct);
        await session.BeginAsync(ct);
        if (input.CategorieId is { } c && await session.LongAsync("SELECT COUNT(*) FROM rh_doc_categories WHERE id = @id", ct, ("@id", c)) == 0)
            throw ApiException.Saisie("Catégorie introuvable.");
        var maintenant = DateTime.UtcNow;
        if (id is null)
        {
            var racine = RhRegles.CodeReferentiel(libelle);
            var code = racine;
            for (var n = 2; await session.LongAsync("SELECT COUNT(*) FROM rh_modeles WHERE code = @c", ct, ("@c", code)) > 0; n++)
                code = SocleRegles.CodeAvecSuffixe(racine, n).ToUpperInvariant();
            id = await session.InsertAsync(
                "INSERT INTO rh_modeles (code, libelle, type, categorie_id, contenu, actif, cree_le, maj_le) VALUES (@code, @l, @t, @c, @contenu, @a, @m, @m)", ct,
                ("@code", code), ("@l", libelle), ("@t", type), ("@c", input.CategorieId), ("@contenu", contenu), ("@a", input.Actif), ("@m", maintenant));
            await Historique.EcrireAsync(session, auteur, ObjetsRh.Modele, code, "Création", null, null, $"{libelle} ({type})", null, ct);
        }
        else
        {
            var actuel = await session.FirstOrDefaultAsync(SelectModele + " WHERE m.id = @id FOR UPDATE", LireModele, ct, ("@id", id))
                         ?? throw ApiException.Introuvable("Modèle introuvable.");
            await session.ExecuteAsync(
                "UPDATE rh_modeles SET libelle = @l, type = @t, categorie_id = @c, contenu = @contenu, actif = @a, maj_le = @m WHERE id = @id", ct,
                ("@l", libelle), ("@t", type), ("@c", input.CategorieId), ("@contenu", contenu), ("@a", input.Actif), ("@m", maintenant), ("@id", id));
            if (actuel.Libelle != libelle)
                await Historique.EcrireAsync(session, auteur, ObjetsRh.Modele, actuel.Code, "Modification", "libelle", actuel.Libelle, libelle, null, ct);
            if (actuel.Type != type)
                await Historique.EcrireAsync(session, auteur, ObjetsRh.Modele, actuel.Code, "Modification", "type", actuel.Type, type, null, ct);
            if (actuel.CategorieId != input.CategorieId)
                await Historique.EcrireAsync(session, auteur, ObjetsRh.Modele, actuel.Code, "Modification", "categorie",
                    actuel.Categorie, input.CategorieId?.ToString(), null, ct);
            if (actuel.Actif != input.Actif)
                await Historique.EcrireAsync(session, auteur, ObjetsRh.Modele, actuel.Code, "Modification", "actif",
                    actuel.Actif ? "Actif" : "Inactif", input.Actif ? "Actif" : "Inactif", null, ct);
            if (actuel.Contenu != contenu)
                await Historique.EcrireAsync(session, auteur, ObjetsRh.Modele, actuel.Code, "Modification", "contenu",
                    actuel.Contenu, contenu, null, ct);
        }
        var dto = await session.FirstOrDefaultAsync(SelectModele + " WHERE m.id = @id", LireModele, ct, ("@id", id))
                  ?? throw new InvalidOperationException("Modèle enregistré introuvable.");
        await session.CommitAsync(ct);
        return dto;
    }

    /// <summary>
    /// Génère un document pour un agent à partir d'un modèle : texte rempli avec les valeurs actuelles de l'agent,
    /// fichier PDF réel enregistré comme version 1 (avec le texte). Aperçu : rien n'est enregistré.
    /// </summary>
    public async Task<RhGenerationDto> GenererAsync(Auteur auteur, long modeleId, RhGenerationInput input, CancellationToken ct)
    {
        await using var session = await db.OpenAsync(ct);
        await session.BeginAsync(ct);
        var modele = await session.FirstOrDefaultAsync(SelectModele + " WHERE m.id = @id", LireModele, ct, ("@id", modeleId))
                     ?? throw ApiException.Introuvable("Modèle introuvable.");
        if (!modele.Actif && !input.Apercu)
            throw ApiException.Conflit("Ce modèle est désactivé.");
        var agent = await session.FirstOrDefaultAsync(RhSql.SelectAgent + " WHERE a.id = @id", RhSql.LireAgent, ct, ("@id", input.AgentId))
                    ?? throw ApiException.Saisie("Agent introuvable.");
        var responsable = await session.FirstOrDefaultAsync(
            """
            SELECT COALESCE(
                (SELECT CONCAT(r.nom, ' ', r.prenom) FROM rh_affectations af JOIN rh_agents r ON r.id = af.responsable_id
                 WHERE af.agent_id = @agent AND af.fin IS NULL ORDER BY af.debut DESC LIMIT 1),
                (SELECT CONCAT(r.nom, ' ', r.prenom) FROM rh_services s JOIN rh_agents r ON r.id = s.responsable_id WHERE s.id = @service)
            ) AS nom
            """, r => new Boite(r.NullableText("nom")), ct, ("@agent", agent.Id), ("@service", agent.ServiceId));
        var organisation = await parametres.ValeurAsync(ParametresCles.NomOrganisation, ct);
        var titre = SocleRegles.Tronquer(input.Titre, 200) ?? $"{modele.Libelle} — {agent.Nom} {agent.Prenom}";
        var aujourdhui = RhSql.Aujourdhui();

        ValeursModele Valeurs(string? numero) => new(agent.Nom, agent.Prenom, agent.Rio, agent.Grade, agent.Service, agent.Unite,
            agent.Fonction, agent.Matricule, agent.Reference, aujourdhui, responsable?.Valeur, numero, organisation);

        if (input.Apercu)
        {
            var (apercu, inconnues) = RhRegles.RemplirModele(modele.Contenu, RhRegles.Variables(Valeurs("(attribué à l'enregistrement)")));
            return new RhGenerationDto(titre, apercu, inconnues, null);
        }

        long? categorieId = input.CategorieId ?? modele.CategorieId;
        var meta = await ValiderMetadonneesAsync(session, titre, modele.Type, categorieId, input.DossierId, $"Généré depuis le modèle « {modele.Libelle} »",
            agent.Id, agent.ServiceId, input.Confidentialite, null, ct);
        var statut = input.Soumettre ? RhDocumentStatuts.AValider : RhDocumentStatuts.Brouillon;
        var id = await InsererDocumentAsync(session, auteur, meta, aujourdhui, null, null, statut, modele.Id, ct);
        var reference = RhRegles.ReferenceDocument(id);
        var (texte, variablesInconnues) = RhRegles.RemplirModele(modele.Contenu, RhRegles.Variables(Valeurs(reference)));
        var pdf = RhPdf.Generer(organisation, titre, texte, reference);
        var nomFichier = RhRegles.NomFichierPdf(titre, reference);
        var controle = new FichierControle(nomFichier, ".pdf", "application/pdf", RhRegles.Sha256(pdf));
        await InsererVersionAsync(session, auteur, id, 1, controle, pdf, texte, $"Généré depuis le modèle « {modele.Libelle} »", statut, ct);
        await Historique.EcrireAsync(session, auteur, ObjetsRh.Document, reference, "Génération", null, null,
            $"{titre} — modèle {modele.Code}, agent {agent.Reference}, statut « {statut} »", null, ct);
        var dto = await LireVisibleAsync(session, id, true, false, ct);
        await session.CommitAsync(ct);
        return new RhGenerationDto(titre, texte, variablesInconnues, dto);
    }

    private sealed record Boite(string? Valeur);
}
