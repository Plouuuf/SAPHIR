using Saphir.Server.Data;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Complements;

namespace Saphir.Server.Modules.Complements;

/// <summary>
/// MRZ (cahier § 18.4) : analyse d'une bande (classe pure <see cref="MrzParser"/>), confirmation manuelle des champs,
/// liaison avec une identité SAPHIR (personne choisie par le poste via GET /api/personnes/recherche) et registre des lectures.
/// </summary>
internal static class MrzEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/mrz");
        string[] p = [Permissions.MrzUtiliser];

        g.MapPost("/analyser", (HttpContext http, MrzAnalyseInput input, JournalAcces journal) =>
            Api.RunAsync(http, p, async (ctx, _) =>
            {
                if ((input.Texte ?? string.Empty).Length > 400)
                    throw ApiException.Saisie("Bande MRZ trop longue.");
                var resultat = MrzParser.Analyser(input.Texte);
                await journal.EcrireAsync(ctx.Agent.Id, SocleRegles.LibelleAgent(ctx.Agent), "Lecture MRZ", resultat.Format,
                    resultat.ControlesOk ? "Contrôles conformes" : "Contrôles en échec");
                return Results.Ok(resultat);
            }));

        g.MapPost("/lectures", (HttpContext http, MrzLectureInput input, SaphirDb saphirDb) =>
            Api.RunAsync(http, p, async (ctx, ct) =>
            {
                var analyse = MrzParser.Analyser(input.Texte);
                if (!analyse.Reconnu)
                    throw ApiException.Saisie("La bande MRZ n'est pas reconnue : analyse-la avant d'enregistrer.");
                if (!analyse.ControlesOk && !input.ConfirmationManuelle)
                    throw ApiException.Saisie("Les chiffres de contrôle sont en échec : confirme manuellement l'identité après vérification du document.");
                var nom = ComplementsOutils.Obligatoire(input.Nom, 80, "Nom");
                var prenoms = ComplementsOutils.Facultatif(input.Prenoms, 120) ?? string.Empty;
                if (input.PersonneId is { } pid && pid <= 0)
                    throw ApiException.Saisie("Personne SAPHIR invalide.");

                var maintenant = DateTime.UtcNow;
                await using var db = await saphirDb.OpenAsync(ct);
                await db.BeginAsync(ct);
                var id = await db.InsertAsync(
                    """
                    INSERT INTO app_mrz_lectures (agent_id, agent, `date`, format, controles_ok, confirmation_manuelle, texte, nom, prenoms,
                        date_naissance, sexe, numero_document, type_document, nationalite, date_expiration,
                        personne_id, personne_reference, personne_libelle, contexte)
                    VALUES (@agent_id, @agent, @date, @format, @ok, @confirmation, @texte, @nom, @prenoms,
                        @naissance, @sexe, @numero, @type, @nationalite, @expiration,
                        @personne, @personne_ref, @personne_libelle, @contexte)
                    """, ct,
                    ("@agent_id", ctx.Agent.Id), ("@agent", ctx.Auteur.Nom), ("@date", maintenant), ("@format", analyse.Format),
                    ("@ok", analyse.ControlesOk), ("@confirmation", input.ConfirmationManuelle),
                    ("@texte", string.Join('\n', analyse.Lignes)), ("@nom", nom.ToUpperInvariant()), ("@prenoms", prenoms),
                    ("@naissance", ComplementsOutils.Parametre(input.DateNaissance)),
                    ("@sexe", input.Sexe is "M" or "F" ? input.Sexe : null),
                    ("@numero", ComplementsOutils.Facultatif(input.NumeroDocument, 40)),
                    ("@type", ComplementsOutils.Facultatif(input.TypeDocument, 10)),
                    ("@nationalite", ComplementsOutils.Facultatif(input.Nationalite, 3)),
                    ("@expiration", ComplementsOutils.Parametre(input.DateExpiration)),
                    ("@personne", input.PersonneId), ("@personne_ref", ComplementsOutils.Facultatif(input.PersonneReference, 30)),
                    ("@personne_libelle", ComplementsOutils.Facultatif(input.PersonneLibelle, 200)),
                    ("@contexte", ComplementsOutils.Facultatif(input.Contexte, 200)));
                var reference = ComplementsOutils.Reference("MRZ", maintenant, id);
                await db.ExecuteAsync("UPDATE app_mrz_lectures SET reference = @r WHERE id = @id", ct, ("@r", reference), ("@id", id));

                var corrections = new List<string>();
                if (!string.Equals(analyse.Nom, nom, StringComparison.OrdinalIgnoreCase)) corrections.Add($"nom « {analyse.Nom} » → « {nom} »");
                if (!string.Equals(analyse.Prenoms ?? string.Empty, prenoms, StringComparison.OrdinalIgnoreCase)) corrections.Add($"prénoms « {analyse.Prenoms} » → « {prenoms} »");
                if (analyse.DateNaissance != input.DateNaissance) corrections.Add("date de naissance corrigée");
                await Historique.EcrireAsync(db, ctx.Auteur, ObjetsComplements.Mrz, reference, "Lecture confirmée",
                    input.PersonneReference is null ? null : "personne", null, input.PersonneReference,
                    corrections.Count == 0
                        ? (analyse.ControlesOk ? "Contrôles conformes" : "Confirmation manuelle malgré des contrôles en échec")
                        : "Corrections manuelles : " + string.Join(", ", corrections), ct);
                var dto = await db.FirstOrDefaultAsync("SELECT * FROM app_mrz_lectures WHERE id = @id", Lire, ct, ("@id", id))
                          ?? throw new InvalidOperationException("Lecture enregistrée introuvable.");
                await db.CommitAsync(ct);
                return Results.Ok(dto);
            }));

        g.MapGet("/lectures", (HttpContext http, SaphirDb saphirDb, int? limit) =>
            Api.RunAsync(http, p, async (ctx, ct) =>
            {
                await using var db = await saphirDb.OpenAsync(ct);
                return Results.Ok(await db.QueryAsync(
                    "SELECT * FROM app_mrz_lectures WHERE agent_id = @moi ORDER BY `date` DESC, id DESC LIMIT @limite",
                    Lire, ct, ("@moi", ctx.Agent.Id), ("@limite", SocleRegles.Limite(limit, 100, 500))));
            }));
    }

    private static MrzLectureDto Lire(SqlRow r) => new(
        r.Long("id"), r.Text("reference"), r.Date("date"), r.Text("agent"), r.Text("format"), r.Bool("controles_ok"),
        r.Text("nom"), r.Text("prenoms"), ComplementsOutils.DateSeule(r, "date_naissance"), r.NullableText("sexe"),
        r.NullableText("numero_document"), r.NullableText("type_document"), r.NullableText("nationalite"),
        ComplementsOutils.DateSeule(r, "date_expiration"), r.NullableLong("personne_id"), r.NullableText("personne_reference"),
        r.NullableText("personne_libelle"), r.NullableText("contexte"));
}
