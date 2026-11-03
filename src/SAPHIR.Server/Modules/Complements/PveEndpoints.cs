using System.Globalization;
using System.Net;
using System.Text;
using Saphir.Server.Data;
using Saphir.Server.Services;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Complements;

namespace Saphir.Server.Modules.Complements;

/// <summary>
/// PVe — procès-verbal électronique fictif (cahier § 17). Le rédacteur (NOM, RIO, grade, service) est repris de RH
/// au moment de la rédaction ; l'infraction est figée dans l'avis (NATINF, montants, points) ; la validation attribue
/// le numéro d'avis et fige l'acte (seules des mentions peuvent ensuite être ajoutées).
/// </summary>
internal sealed class PveStore(SaphirDb saphirDb, RealtimeHub hub)
{
    // ===== Référentiel des infractions =====

    public async Task<List<InfractionDto>> InfractionsAsync(bool tous, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await db.QueryAsync(
            "SELECT * FROM pve_infractions" + (tous ? string.Empty : " WHERE actif = 1") + " ORDER BY ordre, natinf",
            LireInfraction, ct);
    }

    public async Task<InfractionDto> EnregistrerInfractionAsync(AgentContext ctx, long? id, InfractionInput input, CancellationToken ct)
    {
        if (PveRegles.ValiderInfraction(input) is { } erreur)
            throw ApiException.Saisie(erreur);
        var natinf = input.Natinf.Trim();
        var maintenant = DateTime.UtcNow;
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        if (await db.LongAsync("SELECT COUNT(*) FROM pve_infractions WHERE natinf = @n AND id <> @id", ct, ("@n", natinf), ("@id", id ?? 0)) > 0)
            throw ApiException.Conflit($"Le code NATINF {natinf} existe déjà dans le référentiel.");
        (string, object?)[] parametres =
        [
            ("@natinf", natinf), ("@libelle", ComplementsOutils.Obligatoire(input.Libelle, 250, "Libellé")),
            ("@texte", ComplementsOutils.Facultatif(input.TexteReference, 200)),
            ("@categorie", ComplementsOutils.Obligatoire(input.Categorie, 60, "Catégorie")), ("@classe", input.Classe),
            ("@forfait", input.MontantForfaitaire), ("@minore", input.MontantMinore), ("@majore", input.MontantMajore),
            ("@points", input.Points), ("@actif", input.Actif), ("@ordre", input.Ordre), ("@maintenant", maintenant), ("@id", id ?? 0)
        ];
        if (id is null)
        {
            id = await db.InsertAsync(
                """
                INSERT INTO pve_infractions (natinf, libelle, texte_reference, categorie, classe, montant_forfaitaire, montant_minore,
                    montant_majore, points, actif, ordre, cree_le, maj_le)
                VALUES (@natinf, @libelle, @texte, @categorie, @classe, @forfait, @minore, @majore, @points, @actif, @ordre, @maintenant, @maintenant)
                """, ct, parametres);
            await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Infraction, natinf, "Création", null, null, input.Libelle, null, ct);
        }
        else
        {
            var avant = await db.FirstOrDefaultAsync("SELECT * FROM pve_infractions WHERE id = @id FOR UPDATE", LireInfraction, ct, ("@id", id.Value))
                        ?? throw ApiException.Introuvable("Infraction introuvable.");
            await db.ExecuteAsync(
                """
                UPDATE pve_infractions SET natinf = @natinf, libelle = @libelle, texte_reference = @texte, categorie = @categorie,
                    classe = @classe, montant_forfaitaire = @forfait, montant_minore = @minore, montant_majore = @majore,
                    points = @points, actif = @actif, ordre = @ordre, maj_le = @maintenant
                WHERE id = @id
                """, ct, parametres);
            var apres = new InfractionDto(id.Value, natinf, input.Libelle.Trim(), ComplementsOutils.Facultatif(input.TexteReference, 200),
                input.Categorie.Trim(), input.Classe, input.MontantForfaitaire, input.MontantMinore, input.MontantMajore, input.Points,
                input.Actif, input.Ordre);
            foreach (var (champ, a, b) in Differences(avant, apres))
                await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Infraction, natinf, "Modification", champ, a, b, null, ct);
        }
        var dto = await db.FirstOrDefaultAsync("SELECT * FROM pve_infractions WHERE id = @id", LireInfraction, ct, ("@id", id.Value))
                  ?? throw new InvalidOperationException("Infraction introuvable.");
        await db.CommitAsync(ct);
        return dto;
    }

    private static IEnumerable<(string, string?, string?)> Differences(InfractionDto a, InfractionDto b)
    {
        static string? M(decimal? m) => m?.ToString("0.00", CultureInfo.InvariantCulture);
        if (a.Natinf != b.Natinf) yield return ("natinf", a.Natinf, b.Natinf);
        if (a.Libelle != b.Libelle) yield return ("libellé", a.Libelle, b.Libelle);
        if (a.TexteReference != b.TexteReference) yield return ("texte", a.TexteReference, b.TexteReference);
        if (a.Categorie != b.Categorie) yield return ("catégorie", a.Categorie, b.Categorie);
        if (a.Classe != b.Classe) yield return ("classe", a.Classe, b.Classe);
        if (a.MontantForfaitaire != b.MontantForfaitaire) yield return ("montant forfaitaire", M(a.MontantForfaitaire), M(b.MontantForfaitaire));
        if (a.MontantMinore != b.MontantMinore) yield return ("montant minoré", M(a.MontantMinore), M(b.MontantMinore));
        if (a.MontantMajore != b.MontantMajore) yield return ("montant majoré", M(a.MontantMajore), M(b.MontantMajore));
        if (a.Points != b.Points) yield return ("points", a.Points.ToString(CultureInfo.InvariantCulture), b.Points.ToString(CultureInfo.InvariantCulture));
        if (a.Actif != b.Actif) yield return ("actif", a.Actif ? "oui" : "non", b.Actif ? "oui" : "non");
        if (a.Ordre != b.Ordre) yield return ("ordre", a.Ordre.ToString(CultureInfo.InvariantCulture), b.Ordre.ToString(CultureInfo.InvariantCulture));
    }

    // ===== Avis =====

    public async Task<List<PveResumeDto>> ListerAsync(AgentContext ctx, string? q, string? statut, bool mien, int? limite, CancellationToken ct)
    {
        var sql = new StringBuilder("SELECT * FROM pve_avis WHERE (statut <> @brouillon OR agent_id = @moi)");
        var parametres = new List<(string, object?)> { ("@brouillon", PveStatuts.Brouillon), ("@moi", ctx.Agent.Id) };
        if (mien)
            sql.Append(" AND agent_id = @moi");
        if (!string.IsNullOrWhiteSpace(statut))
        {
            sql.Append(" AND statut = @statut");
            parametres.Add(("@statut", statut.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            sql.Append(" AND (reference LIKE @q OR numero_avis LIKE @q OR natinf LIKE @q OR infraction LIKE @q OR contrevenant_nom LIKE @q")
               .Append(" OR immatriculation LIKE @qi OR commune LIKE @q OR lieu LIKE @q OR agent LIKE @q OR intervention_numero LIKE @q)");
            parametres.Add(("@q", "%" + SocleRegles.EchapperLike(q.Trim()) + "%"));
            parametres.Add(("@qi", "%" + SocleRegles.EchapperLike(PveRegles.NormaliserImmatriculation(q) ?? string.Empty) + "%"));
        }
        sql.Append(" ORDER BY date_infraction DESC, id DESC LIMIT @limite");
        parametres.Add(("@limite", SocleRegles.Limite(limite, 300, 1000)));

        await using var db = await saphirDb.OpenAsync(ct);
        return await db.QueryAsync(sql.ToString(), r => new PveResumeDto(
            r.Long("id"), r.Text("reference"), r.NullableText("numero_avis"), r.Text("statut"), r.Date("date_infraction"),
            r.Text("natinf"), r.Text("infraction"),
            string.Join(" ", new[] { r.NullableText("contrevenant_nom"), r.NullableText("contrevenant_prenom") }.Where(s => s is not null)) is { Length: > 0 } c ? c : null,
            r.NullableText("immatriculation"), r.NullableText("commune"), r.Text("agent"), Montant(r, "montant_forfaitaire") ?? 0),
            ct, parametres.ToArray());
    }

    public async Task<PveDto> LireAsync(AgentContext ctx, long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        return await ChargerAsync(db, ctx.Agent.Id, id, ct) ?? throw ApiException.Introuvable("PVe introuvable.");
    }

    public async Task<PveDto> LireParReferenceAsync(AgentContext ctx, string reference, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var id = await db.ScalarAsync("SELECT id FROM pve_avis WHERE reference = @r OR numero_avis = @r", ct, ("@r", reference.Trim().ToUpperInvariant()));
        if (id is null)
            throw ApiException.Introuvable("PVe introuvable.");
        return await ChargerAsync(db, ctx.Agent.Id, Convert.ToInt64(id, CultureInfo.InvariantCulture), ct) ?? throw ApiException.Introuvable("PVe introuvable.");
    }

    public async Task<PveDto> EnregistrerAsync(AgentContext ctx, long? id, PveInput input, CancellationToken ct)
    {
        if (PveRegles.ValiderSaisie(input, DateTimeOffset.UtcNow) is { } erreur)
            throw ApiException.Saisie(erreur);
        var maintenant = DateTime.UtcNow;
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var infraction = await db.FirstOrDefaultAsync("SELECT * FROM pve_infractions WHERE id = @id", LireInfraction, ct, ("@id", input.InfractionId))
                         ?? throw ApiException.Saisie("Infraction inconnue du référentiel.");

        var champs = new List<(string, object?)>
        {
            ("@infraction_id", infraction.Id), ("@natinf", infraction.Natinf), ("@infraction", infraction.Libelle),
            ("@texte", infraction.TexteReference), ("@classe", infraction.Classe), ("@forfait", infraction.MontantForfaitaire),
            ("@minore", infraction.MontantMinore), ("@majore", infraction.MontantMajore), ("@points", infraction.Points),
            ("@date", input.DateInfraction.UtcDateTime), ("@lieu", ComplementsOutils.Obligatoire(input.Lieu, 250, "Lieu")),
            ("@commune", ComplementsOutils.Facultatif(input.Commune, 120)),
            ("@personne", input.PersonneId), ("@personne_ref", ComplementsOutils.Facultatif(input.PersonneReference, 30)),
            ("@nom", ComplementsOutils.Facultatif(input.ContrevenantNom, 60)?.ToUpperInvariant()),
            ("@prenom", ComplementsOutils.Facultatif(input.ContrevenantPrenom, 80)),
            ("@naissance", ComplementsOutils.Parametre(input.ContrevenantNaissance)),
            ("@lieu_naissance", ComplementsOutils.Facultatif(input.ContrevenantLieuNaissance, 120)),
            ("@adresse", ComplementsOutils.Facultatif(input.ContrevenantAdresse, 250)),
            ("@vehicule", input.VehiculeId), ("@immat", PveRegles.NormaliserImmatriculation(input.Immatriculation)),
            ("@marque", ComplementsOutils.Facultatif(input.VehiculeMarque, 60)), ("@modele", ComplementsOutils.Facultatif(input.VehiculeModele, 60)),
            ("@couleur", ComplementsOutils.Facultatif(input.VehiculeCouleur, 40)),
            ("@circonstances", ComplementsOutils.Facultatif(input.Circonstances, 4000)),
            ("@observations", ComplementsOutils.Facultatif(input.Observations, 4000)),
            ("@intervention", ComplementsOutils.Facultatif(input.InterventionNumero, 40)),
            ("@maintenant", maintenant)
        };

        if (id is null)
        {
            // Rédacteur : valeurs RH actuelles, conservées dans l'acte (cahier § 20.1).
            var a = ctx.Agent;
            champs.AddRange(
            [
                ("@agent_id", a.Id), ("@agent", $"{a.Nom} {a.Prenom}"), ("@rio", a.Rio), ("@grade", a.Grade),
                ("@service", a.Service), ("@unite", a.Unite), ("@statut", PveStatuts.Brouillon)
            ]);
            id = await db.InsertAsync(
                """
                INSERT INTO pve_avis (statut, infraction_id, natinf, infraction, texte_reference, classe, montant_forfaitaire, montant_minore,
                    montant_majore, points, date_infraction, lieu, commune, personne_id, personne_reference, contrevenant_nom,
                    contrevenant_prenom, contrevenant_naissance, contrevenant_lieu_naissance, contrevenant_adresse, vehicule_id,
                    immatriculation, vehicule_marque, vehicule_modele, vehicule_couleur, circonstances, observations, intervention_numero,
                    agent_id, agent, agent_rio, agent_grade, agent_service, agent_unite, version, cree_le, maj_le)
                VALUES (@statut, @infraction_id, @natinf, @infraction, @texte, @classe, @forfait, @minore,
                    @majore, @points, @date, @lieu, @commune, @personne, @personne_ref, @nom,
                    @prenom, @naissance, @lieu_naissance, @adresse, @vehicule,
                    @immat, @marque, @modele, @couleur, @circonstances, @observations, @intervention,
                    @agent_id, @agent, @rio, @grade, @service, @unite, 1, @maintenant, @maintenant)
                """, ct, champs.ToArray());
            var reference = ComplementsOutils.Reference("PVE", maintenant, id.Value);
            await db.ExecuteAsync("UPDATE pve_avis SET reference = @r WHERE id = @id", ct, ("@r", reference), ("@id", id.Value));
            await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Pve, reference, "Création", null, null,
                $"NATINF {infraction.Natinf} — {infraction.Libelle}", null, ct);
        }
        else
        {
            var avant = await VerrouillerBrouillonAsync(db, ctx, id.Value, input.Version, ct);
            champs.Add(("@id", id.Value));
            await db.ExecuteAsync(
                """
                UPDATE pve_avis SET infraction_id = @infraction_id, natinf = @natinf, infraction = @infraction, texte_reference = @texte,
                    classe = @classe, montant_forfaitaire = @forfait, montant_minore = @minore, montant_majore = @majore, points = @points,
                    date_infraction = @date, lieu = @lieu, commune = @commune, personne_id = @personne, personne_reference = @personne_ref,
                    contrevenant_nom = @nom, contrevenant_prenom = @prenom, contrevenant_naissance = @naissance,
                    contrevenant_lieu_naissance = @lieu_naissance, contrevenant_adresse = @adresse, vehicule_id = @vehicule,
                    immatriculation = @immat, vehicule_marque = @marque, vehicule_modele = @modele, vehicule_couleur = @couleur,
                    circonstances = @circonstances, observations = @observations, intervention_numero = @intervention,
                    version = version + 1, maj_le = @maintenant
                WHERE id = @id
                """, ct, champs.ToArray());
            var apres = await ChargerAsync(db, ctx.Agent.Id, id.Value, ct) ?? throw new InvalidOperationException("PVe introuvable.");
            foreach (var (champ, a, b) in Differences(avant, apres))
                await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Pve, avant.Reference, "Modification", champ, a, b, null, ct);
        }

        var dto = await ChargerAsync(db, ctx.Agent.Id, id.Value, ct) ?? throw new InvalidOperationException("PVe enregistré introuvable.");
        await db.CommitAsync(ct);
        await PrevenirAsync(dto.Id);
        return dto;
    }

    private static IEnumerable<(string, string?, string?)> Differences(PveDto a, PveDto b)
    {
        static string? D(DateTimeOffset d) => d.ToString("dd/MM/yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture);
        static string? J(DateOnly? d) => d?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        if (a.Natinf != b.Natinf) yield return ("infraction", $"{a.Natinf} {a.Infraction}", $"{b.Natinf} {b.Infraction}");
        if (a.DateInfraction != b.DateInfraction) yield return ("date", D(a.DateInfraction), D(b.DateInfraction));
        if (a.Lieu != b.Lieu) yield return ("lieu", a.Lieu, b.Lieu);
        if (a.Commune != b.Commune) yield return ("commune", a.Commune, b.Commune);
        if (a.PersonneReference != b.PersonneReference) yield return ("personne", a.PersonneReference, b.PersonneReference);
        if (a.ContrevenantNom != b.ContrevenantNom) yield return ("nom", a.ContrevenantNom, b.ContrevenantNom);
        if (a.ContrevenantPrenom != b.ContrevenantPrenom) yield return ("prénom", a.ContrevenantPrenom, b.ContrevenantPrenom);
        if (a.ContrevenantNaissance != b.ContrevenantNaissance) yield return ("naissance", J(a.ContrevenantNaissance), J(b.ContrevenantNaissance));
        if (a.ContrevenantLieuNaissance != b.ContrevenantLieuNaissance) yield return ("lieu de naissance", a.ContrevenantLieuNaissance, b.ContrevenantLieuNaissance);
        if (a.ContrevenantAdresse != b.ContrevenantAdresse) yield return ("adresse", a.ContrevenantAdresse, b.ContrevenantAdresse);
        if (a.Immatriculation != b.Immatriculation) yield return ("immatriculation", a.Immatriculation, b.Immatriculation);
        if (a.VehiculeMarque != b.VehiculeMarque) yield return ("marque", a.VehiculeMarque, b.VehiculeMarque);
        if (a.VehiculeModele != b.VehiculeModele) yield return ("modèle", a.VehiculeModele, b.VehiculeModele);
        if (a.VehiculeCouleur != b.VehiculeCouleur) yield return ("couleur", a.VehiculeCouleur, b.VehiculeCouleur);
        if (a.Circonstances != b.Circonstances) yield return ("circonstances", a.Circonstances, b.Circonstances);
        if (a.Observations != b.Observations) yield return ("observations", a.Observations, b.Observations);
        if (a.InterventionNumero != b.InterventionNumero) yield return ("intervention", a.InterventionNumero, b.InterventionNumero);
    }

    /// <summary>Validation : attribution du numéro d'avis, acte figé.</summary>
    public async Task<PveDto> ValiderAsync(AgentContext ctx, long id, PveValidationInput input, CancellationToken ct)
    {
        if (!input.Certifie)
            throw ApiException.Saisie("Certifie l'exactitude des constatations avant de valider.");
        var maintenant = DateTime.UtcNow;
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var avant = await VerrouillerBrouillonAsync(db, ctx, id, input.Version, ct);
        if (string.IsNullOrWhiteSpace(avant.ContrevenantNom) && string.IsNullOrWhiteSpace(avant.Immatriculation))
            throw ApiException.Saisie("Indique le contrevenant ou l'immatriculation avant de valider.");
        var numero = PveRegles.NumeroAvis(maintenant.Year, (id - 1) % 9_999_999 + 1);
        await db.ExecuteAsync(
            "UPDATE pve_avis SET statut = @statut, numero_avis = @numero, valide_le = @maintenant, version = version + 1, maj_le = @maintenant WHERE id = @id",
            ct, ("@statut", PveStatuts.Valide), ("@numero", numero), ("@maintenant", maintenant), ("@id", id));
        await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Pve, avant.Reference, "Validation", "statut",
            PveStatuts.Brouillon, PveStatuts.Valide, "Avis n° " + numero, ct);
        var dto = await ChargerAsync(db, ctx.Agent.Id, id, ct) ?? throw new InvalidOperationException("PVe introuvable.");
        await db.CommitAsync(ct);
        await PrevenirAsync(id);
        return dto;
    }

    public async Task<PveDto> AbandonnerAsync(AgentContext ctx, long id, PveAbandonInput input, CancellationToken ct)
    {
        var motif = ComplementsOutils.Obligatoire(input.Motif, 500, "Motif de l'abandon");
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var avant = await VerrouillerBrouillonAsync(db, ctx, id, input.Version, ct);
        await db.ExecuteAsync(
            "UPDATE pve_avis SET statut = @statut, motif_abandon = @motif, version = version + 1, maj_le = @maintenant WHERE id = @id",
            ct, ("@statut", PveStatuts.Abandonne), ("@motif", motif), ("@maintenant", DateTime.UtcNow), ("@id", id));
        await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Pve, avant.Reference, "Abandon", "statut",
            PveStatuts.Brouillon, PveStatuts.Abandonne, motif, ct);
        var dto = await ChargerAsync(db, ctx.Agent.Id, id, ct) ?? throw new InvalidOperationException("PVe introuvable.");
        await db.CommitAsync(ct);
        await PrevenirAsync(id);
        return dto;
    }

    public async Task<PveDto> AjouterMentionAsync(AgentContext ctx, long id, PveMentionInput input, CancellationToken ct)
    {
        var texte = ComplementsOutils.Obligatoire(input.Texte, 4000, "Mention");
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var pve = await ChargerAsync(db, ctx.Agent.Id, id, ct) ?? throw ApiException.Introuvable("PVe introuvable.");
        if (pve.Statut != PveStatuts.Valide)
            throw ApiException.Conflit("Les mentions se portent sur un PVe validé ; un brouillon se modifie directement.");
        await db.ExecuteAsync(
            "INSERT INTO pve_mentions (avis_id, `date`, auteur_id, auteur, texte) VALUES (@id, @maintenant, @auteur_id, @auteur, @texte)",
            ct, ("@id", id), ("@maintenant", DateTime.UtcNow), ("@auteur_id", ctx.Agent.Id), ("@auteur", ctx.Auteur.Nom), ("@texte", texte));
        await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Pve, pve.Reference, "Mention", null, null, texte, null, ct);
        var dto = await ChargerAsync(db, ctx.Agent.Id, id, ct) ?? throw new InvalidOperationException("PVe introuvable.");
        await db.CommitAsync(ct);
        await PrevenirAsync(id);
        return dto;
    }

    // ===== Justificatifs =====

    public async Task<PveDto> AjouterJustificatifAsync(AgentContext ctx, long id, PieceJointeInput input, CancellationToken ct)
    {
        var fichier = ComplementsOutils.DecoderPiece(input);
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var pve = await ChargerAsync(db, ctx.Agent.Id, id, ct, verrouiller: true) ?? throw ApiException.Introuvable("PVe introuvable.");
        if (pve.Statut != PveStatuts.Brouillon || pve.AgentId != ctx.Agent.Id)
            throw ApiException.Conflit("Les justificatifs s'ajoutent par le rédacteur, avant la validation.");
        if (pve.Justificatifs.Count >= PiecesJointes.NombreMaxParMessage)
            throw ApiException.Saisie($"{PiecesJointes.NombreMaxParMessage} justificatifs au plus.");
        await db.ExecuteAsync(
            """
            INSERT INTO pve_justificatifs (avis_id, nom, type_mime, taille, sha256, contenu, auteur, cree_le)
            VALUES (@id, @nom, @type, @taille, @sha, @contenu, @auteur, @maintenant)
            """, ct,
            ("@id", id), ("@nom", fichier.Nom), ("@type", fichier.TypeMime), ("@taille", fichier.Contenu.LongLength), ("@sha", fichier.Sha256),
            ("@contenu", fichier.Contenu), ("@auteur", ctx.Auteur.Nom), ("@maintenant", DateTime.UtcNow));
        await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Pve, pve.Reference, "Justificatif ajouté", null, null,
            $"{fichier.Nom} ({PiecesJointes.TailleLisible(fichier.Contenu.LongLength)}, SHA-256 {fichier.Sha256[..12]}…)", null, ct);
        var dto = await ChargerAsync(db, ctx.Agent.Id, id, ct) ?? throw new InvalidOperationException("PVe introuvable.");
        await db.CommitAsync(ct);
        return dto;
    }

    public async Task<PveDto> RetirerJustificatifAsync(AgentContext ctx, long justificatifId, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        var ligne = await db.FirstOrDefaultAsync("SELECT avis_id, nom FROM pve_justificatifs WHERE id = @id",
            r => new { Avis = r.Long("avis_id"), Nom = r.Text("nom") }, ct, ("@id", justificatifId))
                    ?? throw ApiException.Introuvable("Justificatif introuvable.");
        var pve = await ChargerAsync(db, ctx.Agent.Id, ligne.Avis, ct, verrouiller: true) ?? throw ApiException.Introuvable("PVe introuvable.");
        if (pve.Statut != PveStatuts.Brouillon || pve.AgentId != ctx.Agent.Id)
            throw ApiException.Conflit("Un justificatif d'un PVe validé ne peut plus être retiré.");
        await db.ExecuteAsync("DELETE FROM pve_justificatifs WHERE id = @id", ct, ("@id", justificatifId));
        await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Pve, pve.Reference, "Justificatif retiré", null, ligne.Nom, null, null, ct);
        var dto = await ChargerAsync(db, ctx.Agent.Id, ligne.Avis, ct) ?? throw new InvalidOperationException("PVe introuvable.");
        await db.CommitAsync(ct);
        return dto;
    }

    public async Task<PieceJointeContenuDto> JustificatifAsync(AgentContext ctx, long justificatifId, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var avis = await db.ScalarAsync("SELECT avis_id FROM pve_justificatifs WHERE id = @id", ct, ("@id", justificatifId));
        if (avis is null || await ChargerAsync(db, ctx.Agent.Id, Convert.ToInt64(avis, CultureInfo.InvariantCulture), ct) is null)
            throw ApiException.Introuvable("Justificatif introuvable.");
        return await db.FirstOrDefaultAsync("SELECT id, nom, type_mime, taille, contenu FROM pve_justificatifs WHERE id = @id",
                   ComplementsOutils.LirePieceContenu, ct, ("@id", justificatifId))
               ?? throw ApiException.Introuvable("Justificatif introuvable.");
    }

    public async Task<List<HistoriqueDto>> HistoriqueAsync(AgentContext ctx, long id, CancellationToken ct)
    {
        await using var db = await saphirDb.OpenAsync(ct);
        var pve = await ChargerAsync(db, ctx.Agent.Id, id, ct) ?? throw ApiException.Introuvable("PVe introuvable.");
        return await Historique.ListerAsync(db, ObjetsComplements.Pve, pve.Reference, 300, ct);
    }

    // ===== Export =====

    public async Task<PveExportDto> ExporterAsync(AgentContext ctx, long id, CancellationToken ct)
    {
        PveDto pve;
        await using (var db = await saphirDb.OpenAsync(ct))
        {
            pve = await ChargerAsync(db, ctx.Agent.Id, id, ct) ?? throw ApiException.Introuvable("PVe introuvable.");
            await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Pve, pve.Reference, "Export", null, null, null, null, ct);
        }
        return new PveExportDto($"{pve.Reference}.html", PveExport.Html(pve));
    }

    // ===== Lecture =====

    private async Task<PveDto> VerrouillerBrouillonAsync(SqlSession db, AgentContext ctx, long id, int version, CancellationToken ct)
    {
        var pve = await ChargerAsync(db, ctx.Agent.Id, id, ct, verrouiller: true) ?? throw ApiException.Introuvable("PVe introuvable.");
        if (pve.Statut != PveStatuts.Brouillon)
            throw ApiException.Conflit($"Ce PVe est {pve.Statut.ToLowerInvariant()} : il n'est plus modifiable (ajoute une mention si besoin).");
        if (pve.AgentId != ctx.Agent.Id)
            throw ApiException.Conflit("Seul le rédacteur peut modifier ou valider ce brouillon.");
        ComplementsOutils.VerifierVersion(version, pve.Version);
        return pve;
    }

    /// <summary>PVe complet ; un brouillon n'est visible que par son rédacteur.</summary>
    private static async Task<PveDto?> ChargerAsync(SqlSession db, long moi, long id, CancellationToken ct, bool verrouiller = false)
    {
        var entete = await db.FirstOrDefaultAsync(
            "SELECT * FROM pve_avis WHERE id = @id" + (verrouiller ? " FOR UPDATE" : string.Empty),
            r => new { Ligne = LireAvis(r), Agent = r.Long("agent_id") }, ct, ("@id", id));
        if (entete is null || (entete.Ligne.Statut == PveStatuts.Brouillon && entete.Agent != moi))
            return null;
        var justificatifs = await db.QueryAsync(
            "SELECT id, nom, type_mime, taille FROM pve_justificatifs WHERE avis_id = @id ORDER BY id",
            ComplementsOutils.LirePiece, ct, ("@id", id));
        var mentions = await db.QueryAsync(
            "SELECT id, `date`, auteur, texte FROM pve_mentions WHERE avis_id = @id ORDER BY `date`, id",
            r => new PveMentionDto(r.Long("id"), r.Date("date"), r.Text("auteur"), r.Text("texte")), ct, ("@id", id));
        return entete.Ligne with { Justificatifs = justificatifs, Mentions = mentions };
    }

    private static PveDto LireAvis(SqlRow r) => new(
        r.Long("id"), r.Text("reference"), r.NullableText("numero_avis"), r.Text("statut"), r.Long("infraction_id"),
        r.Text("natinf"), r.Text("infraction"), r.NullableText("texte_reference"), r.Text("classe"),
        Montant(r, "montant_forfaitaire") ?? 0, Montant(r, "montant_minore"), Montant(r, "montant_majore"), r.Int("points"),
        r.Date("date_infraction"), r.Text("lieu"), r.NullableText("commune"), r.NullableLong("personne_id"),
        r.NullableText("personne_reference"), r.NullableText("contrevenant_nom"), r.NullableText("contrevenant_prenom"),
        ComplementsOutils.DateSeule(r, "contrevenant_naissance"), r.NullableText("contrevenant_lieu_naissance"),
        r.NullableText("contrevenant_adresse"), r.NullableLong("vehicule_id"), r.NullableText("immatriculation"),
        r.NullableText("vehicule_marque"), r.NullableText("vehicule_modele"), r.NullableText("vehicule_couleur"),
        r.NullableText("circonstances"), r.NullableText("observations"), r.NullableText("intervention_numero"),
        r.Long("agent_id"), r.Text("agent"), r.NullableText("agent_rio"), r.NullableText("agent_grade"),
        r.NullableText("agent_service"), r.NullableText("agent_unite"), r.Date("cree_le"), r.Date("maj_le"),
        r.NullableDate("valide_le"), r.NullableText("motif_abandon"), r.Int("version"),
        Array.Empty<PieceJointeDto>(), Array.Empty<PveMentionDto>());

    private static InfractionDto LireInfraction(SqlRow r) => new(
        r.Long("id"), r.Text("natinf"), r.Text("libelle"), r.NullableText("texte_reference"), r.Text("categorie"), r.Text("classe"),
        Montant(r, "montant_forfaitaire") ?? 0, Montant(r, "montant_minore"), Montant(r, "montant_majore"), r.Int("points"),
        r.Bool("actif"), r.Int("ordre"));

    private static decimal? Montant(SqlRow r, string colonne) => r.Value(colonne) switch
    {
        null => null,
        decimal d => d,
        var autre => Convert.ToDecimal(autre, CultureInfo.InvariantCulture)
    };

    private Task PrevenirAsync(long id)
        => hub.BroadcastAsync(new RealtimeMessage(ComplementsRealtime.Pve, Text: id.ToString(CultureInfo.InvariantCulture), AppId: SaphirApps.Pve),
            CancellationToken.None);
}

/// <summary>Document HTML d'un PVe (export / impression), textes échappés.</summary>
internal static class PveExport
{
    public static string Html(PveDto p)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "—");
        static string Ligne(string libelle, string? valeur) => $"<tr><th>{E(libelle)}</th><td>{E(valeur)}</td></tr>";
        var fr = ComplementsOutils.Francais;
        var local = TimeZoneInfo.ConvertTime(p.DateInfraction, ParisOuUtc());
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"fr\"><head><meta charset=\"utf-8\"><title>").Append(E(p.Reference)).Append("</title>")
          .Append("<style>body{font-family:Segoe UI,Arial,sans-serif;font-size:13px;margin:32px;color:#000}")
          .Append("h1{font-size:18px;color:#1E395B;margin:0}h2{font-size:14px;color:#1E395B;border-bottom:1px solid #A9C7EA;margin-top:22px}")
          .Append("table{border-collapse:collapse;width:100%}th{text-align:left;width:240px;color:#3C3C3C;font-weight:normal;padding:3px 6px}")
          .Append("td{padding:3px 6px}.bandeau{background:#E8F2FC;border:1px solid #A9C7EA;padding:8px 12px;margin:12px 0}")
          .Append(".fictif{color:#C7503B;font-size:11px}</style></head><body>");
        sb.Append("<h1>POLICE NATIONALE — PROCÈS-VERBAL ÉLECTRONIQUE</h1>")
          .Append("<div class=\"fictif\">Document fictif — Val-de-Marne Simulation (jeu de rôle). Aucune valeur légale.</div>")
          .Append("<div class=\"bandeau\">Référence <b>").Append(E(p.Reference)).Append("</b> — Avis n° <b>").Append(E(p.NumeroAvis ?? "non attribué"))
          .Append("</b> — Statut : <b>").Append(E(p.Statut)).Append("</b></div>");
        sb.Append("<h2>Agent verbalisateur</h2><table>")
          .Append(Ligne("Agent", p.Agent)).Append(Ligne("RIO", p.AgentRio)).Append(Ligne("Grade", p.AgentGrade))
          .Append(Ligne("Service", p.AgentService)).Append(Ligne("Unité", p.AgentUnite)).Append("</table>");
        sb.Append("<h2>Infraction</h2><table>")
          .Append(Ligne("Date et heure", local.ToString("dddd d MMMM yyyy 'à' HH'h'mm", fr)))
          .Append(Ligne("Lieu", p.Lieu)).Append(Ligne("Commune", p.Commune))
          .Append(Ligne("NATINF", p.Natinf)).Append(Ligne("Qualification", p.Infraction)).Append(Ligne("Texte", p.TexteReference))
          .Append(Ligne("Classe", PveRegles.LibelleClasse(p.Classe)))
          .Append(Ligne("Amende forfaitaire", PveRegles.Euros(p.MontantForfaitaire)))
          .Append(Ligne("Amende minorée", PveRegles.Euros(p.MontantMinore)))
          .Append(Ligne("Amende majorée", PveRegles.Euros(p.MontantMajore)))
          .Append(Ligne("Retrait de points", p.Points == 0 ? "Aucun" : p.Points.ToString(CultureInfo.InvariantCulture)))
          .Append("</table>");
        sb.Append("<h2>Contrevenant</h2><table>")
          .Append(Ligne("Identifiant SAPHIR", p.PersonneReference)).Append(Ligne("Nom", p.ContrevenantNom)).Append(Ligne("Prénom", p.ContrevenantPrenom))
          .Append(Ligne("Né(e) le", p.ContrevenantNaissance?.ToString("dd/MM/yyyy", fr))).Append(Ligne("à", p.ContrevenantLieuNaissance))
          .Append(Ligne("Adresse", p.ContrevenantAdresse)).Append("</table>");
        sb.Append("<h2>Véhicule</h2><table>")
          .Append(Ligne("Immatriculation", p.Immatriculation)).Append(Ligne("Marque", p.VehiculeMarque))
          .Append(Ligne("Modèle", p.VehiculeModele)).Append(Ligne("Couleur", p.VehiculeCouleur)).Append("</table>");
        sb.Append("<h2>Constatations</h2><table>")
          .Append(Ligne("Circonstances", p.Circonstances)).Append(Ligne("Observations", p.Observations))
          .Append(Ligne("Intervention liée", p.InterventionNumero))
          .Append(Ligne("Justificatifs", p.Justificatifs.Count == 0 ? "Aucun" : string.Join(", ", p.Justificatifs.Select(j => j.Nom))))
          .Append("</table>");
        if (p.Mentions.Count > 0)
        {
            sb.Append("<h2>Mentions</h2><table>");
            foreach (var m in p.Mentions)
                sb.Append(Ligne(TimeZoneInfo.ConvertTime(m.Date, ParisOuUtc()).ToString("dd/MM/yyyy HH:mm", fr) + " — " + m.Auteur, m.Texte));
            sb.Append("</table>");
        }
        if (p.ValideLe is { } v)
            sb.Append("<p>Validé le ").Append(E(TimeZoneInfo.ConvertTime(v, ParisOuUtc()).ToString("dd/MM/yyyy à HH:mm", fr)))
              .Append(" par ").Append(E(p.Agent)).Append(".</p>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static TimeZoneInfo ParisOuUtc()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}

internal static class PveEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/pve");
        string[] p = [Permissions.PveRediger];

        g.MapGet("/infractions", (HttpContext http, PveStore store, bool? tous) =>
            Api.RunAsync(http, [Permissions.PveRediger, Permissions.AdminParametres],
                async (ctx, ct) => Results.Ok(await store.InfractionsAsync(tous == true && ctx.Droits.Possede(Permissions.AdminParametres), ct))));

        g.MapPost("/infractions", (HttpContext http, InfractionInput input, PveStore store) =>
            Api.RunAsync(http, [Permissions.AdminParametres], async (ctx, ct) => Results.Ok(await store.EnregistrerInfractionAsync(ctx, null, input, ct))));

        g.MapPut("/infractions/{id:long}", (HttpContext http, long id, InfractionInput input, PveStore store) =>
            Api.RunAsync(http, [Permissions.AdminParametres], async (ctx, ct) => Results.Ok(await store.EnregistrerInfractionAsync(ctx, id, input, ct))));

        g.MapGet("", (HttpContext http, PveStore store, string? q, string? statut, bool? mien, int? limit) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.ListerAsync(ctx, q, statut, mien == true, limit, ct))));

        g.MapGet("/{id:long}", (HttpContext http, long id, PveStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.LireAsync(ctx, id, ct))));

        g.MapGet("/reference/{reference}", (HttpContext http, string reference, PveStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.LireParReferenceAsync(ctx, reference, ct))));

        g.MapPost("", (HttpContext http, PveInput input, PveStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.EnregistrerAsync(ctx, null, input, ct))));

        g.MapPut("/{id:long}", (HttpContext http, long id, PveInput input, PveStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.EnregistrerAsync(ctx, id, input, ct))));

        g.MapPost("/{id:long}/valider", (HttpContext http, long id, PveValidationInput input, PveStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.ValiderAsync(ctx, id, input, ct))));

        g.MapPost("/{id:long}/abandonner", (HttpContext http, long id, PveAbandonInput input, PveStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.AbandonnerAsync(ctx, id, input, ct))));

        g.MapPost("/{id:long}/mentions", (HttpContext http, long id, PveMentionInput input, PveStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.AjouterMentionAsync(ctx, id, input, ct))));

        g.MapPost("/{id:long}/justificatifs", (HttpContext http, long id, PieceJointeInput input, PveStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.AjouterJustificatifAsync(ctx, id, input, ct))));

        g.MapGet("/justificatifs/{id:long}", (HttpContext http, long id, PveStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.JustificatifAsync(ctx, id, ct))));

        g.MapDelete("/justificatifs/{id:long}", (HttpContext http, long id, PveStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.RetirerJustificatifAsync(ctx, id, ct))));

        g.MapGet("/{id:long}/historique", (HttpContext http, long id, PveStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.HistoriqueAsync(ctx, id, ct))));

        g.MapGet("/{id:long}/export", (HttpContext http, long id, PveStore store) =>
            Api.RunAsync(http, p, async (ctx, ct) => Results.Ok(await store.ExporterAsync(ctx, id, ct))));
    }
}
