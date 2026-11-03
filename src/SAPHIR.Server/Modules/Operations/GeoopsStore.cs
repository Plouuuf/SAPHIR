using System.Globalization;
using Saphir.Server.Data;
using Saphir.Server.Services;
using Saphir.Shared.Contracts.Operations;

namespace Saphir.Server.Modules.Operations;

/// <summary>
/// GÉOOPS (8.1) : image de fond (data/geoops/carte.jpg à côté de l'exécutable), calibration (paramètres centraux),
/// positions simulées reçues du serveur FiveM et positions déclarées par les opérateurs.
/// Aucune position n'est présentée comme une position GPS mesurée.
/// </summary>
public sealed class GeoopsStore(SaphirDb saphirDb, ParametresService parametres, OperationsNotifier notifier)
{
    public const string SourceFiveM = "Simulation FiveM";
    public const string SourceDeclaree = "Déclarée";

    private static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".webp"];

    /// <summary>Fichier de la carte : data/geoops/carte.jpg (ou .png / .webp) ; null si absent.</summary>
    public static string? CheminCarte()
    {
        var dossier = Path.Combine(AppContext.BaseDirectory, "data", "geoops");
        foreach (var extension in Extensions)
        {
            var chemin = Path.Combine(dossier, "carte" + extension);
            if (File.Exists(chemin))
                return chemin;
        }
        return null;
    }

    public static string TypeContenu(string chemin) => Path.GetExtension(chemin).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg"
    };

    /// <summary>Version du fichier (taille + date) : le poste ne retélécharge l'image que si elle change.</summary>
    public static string VersionCarte(string chemin)
    {
        var info = new FileInfo(chemin);
        return info.Length.ToString(CultureInfo.InvariantCulture) + "-" + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
    }

    public async Task<GeoopsCarteDto?> CarteAsync(CancellationToken ct)
    {
        var chemin = CheminCarte();
        if (chemin is null)
            return null;
        var octets = await File.ReadAllBytesAsync(chemin, ct);
        return new GeoopsCarteDto(TypeContenu(chemin), Convert.ToBase64String(octets), VersionCarte(chemin));
    }

    public async Task<GeoopsCalibrationDto> CalibrationAsync(CancellationToken ct)
    {
        var xMin = OperationsRegles.LireDouble(await parametres.ValeurAsync(OperationsRegles.ParamCarteXMin, ct), OperationsRegles.XMinDefaut);
        var xMax = OperationsRegles.LireDouble(await parametres.ValeurAsync(OperationsRegles.ParamCarteXMax, ct), OperationsRegles.XMaxDefaut);
        var yMin = OperationsRegles.LireDouble(await parametres.ValeurAsync(OperationsRegles.ParamCarteYMin, ct), OperationsRegles.YMinDefaut);
        var yMax = OperationsRegles.LireDouble(await parametres.ValeurAsync(OperationsRegles.ParamCarteYMax, ct), OperationsRegles.YMaxDefaut);
        if (xMax <= xMin || yMax <= yMin)
            (xMin, xMax, yMin, yMax) = (OperationsRegles.XMinDefaut, OperationsRegles.XMaxDefaut, OperationsRegles.YMinDefaut, OperationsRegles.YMaxDefaut);
        var peremption = (int)Math.Clamp(OperationsRegles.LireDouble(await parametres.ValeurAsync(OperationsRegles.ParamPeremption, ct),
            OperationsRegles.PeremptionDefaut), 10, 3600);
        return new GeoopsCalibrationDto(xMin, xMax, yMin, yMax, CheminCarte() is not null, peremption);
    }

    /// <summary>Positions envoyées par le script FiveM (toutes les 5 s). Retourne le nombre de positions enregistrées.</summary>
    public async Task<int> RecevoirPositionsAsync(IReadOnlyList<GeoopsPositionInput>? positions, CancellationToken ct)
    {
        if (positions is null || positions.Count == 0)
            return 0;
        if (positions.Count > 512)
            throw ApiException.Saisie("Trop de positions dans un seul envoi (512 au plus).");
        var maintenant = DateTime.UtcNow;
        var nombre = 0;
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        try
        {
            foreach (var p in positions)
            {
                var cle = OperationsRegles.ClePosition(p);
                if (cle is null || !OperationsRegles.PositionPlausible(p.X, p.Y))
                    continue;
                await db.ExecuteAsync(
                    """
                    INSERT INTO geo_positions (cle, discord_id, server_id, equipage_id, nom, x, y, z, cap, vitesse, vehicule, source, auteur, maj_le)
                    VALUES (@cle, @discord, @server, NULL, @nom, @x, @y, @z, @cap, @vitesse, @vehicule, @source, NULL, @maintenant)
                    ON DUPLICATE KEY UPDATE discord_id = VALUES(discord_id), server_id = VALUES(server_id), nom = VALUES(nom), x = VALUES(x),
                        y = VALUES(y), z = VALUES(z), cap = VALUES(cap), vitesse = VALUES(vitesse), vehicule = VALUES(vehicule),
                        source = VALUES(source), maj_le = VALUES(maj_le)
                    """, ct,
                    ("@cle", cle), ("@discord", OperationsRegles.NettoyerDiscordId(p.DiscordId)), ("@server", p.ServerId),
                    ("@nom", OpSql.T(p.Nom, 120)), ("@x", p.X), ("@y", p.Y), ("@z", double.IsFinite(p.Z) ? p.Z : 0),
                    ("@cap", p.Cap is { } c && double.IsFinite(c) ? (double?)c : null), ("@vitesse", p.Vitesse is { } v && double.IsFinite(v) ? (double?)v : null),
                    ("@vehicule", OpSql.T(p.Vehicule, 60)), ("@source", SourceFiveM), ("@maintenant", maintenant));
                nombre++;
            }
            // Ménage : positions FiveM de plus d'une heure.
            await db.ExecuteAsync("DELETE FROM geo_positions WHERE source = @source AND maj_le < @limite", ct,
                ("@source", SourceFiveM), ("@limite", maintenant.AddHours(-1)));
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        if (nombre > 0)
            await notifier.PositionsAsync(nombre);
        return nombre;
    }

    /// <summary>Position déclarée d'un équipage (« Actualiser une position simulée »).</summary>
    public async Task DeclarerPositionAsync(AgentContext ctx, long equipageId, PositionInput input, CancellationToken ct)
    {
        if (!OperationsRegles.PositionPlausible(input.X, input.Y))
            throw ApiException.Saisie("Position invalide.");
        await using var db = await saphirDb.OpenAsync(ct);
        await db.BeginAsync(ct);
        try
        {
            var equipage = await MainCouranteStore.LireEquipageAsync(db, equipageId, ct) ?? throw ApiException.Introuvable("Équipage introuvable.");
            if (!equipage.Actif)
                throw ApiException.Conflit("L'équipage est dissous.");
            var cle = "equipage:" + equipageId.ToString(CultureInfo.InvariantCulture);
            await db.ExecuteAsync(
                """
                INSERT INTO geo_positions (cle, discord_id, server_id, equipage_id, nom, x, y, z, cap, vitesse, vehicule, source, auteur, maj_le)
                VALUES (@cle, NULL, NULL, @equipage, @nom, @x, @y, 0, NULL, NULL, NULL, @source, @auteur, @maintenant)
                ON DUPLICATE KEY UPDATE x = VALUES(x), y = VALUES(y), auteur = VALUES(auteur), maj_le = VALUES(maj_le)
                """, ct,
                ("@cle", cle), ("@equipage", equipageId), ("@nom", equipage.Indicatif), ("@x", input.X), ("@y", input.Y),
                ("@source", SourceDeclaree), ("@auteur", ctx.Auteur.Nom), ("@maintenant", DateTime.UtcNow));
            await Historique.EcrireAsync(db, ctx.Auteur, OperationsRegles.ObjetEquipage, equipage.Indicatif + " #" + equipageId, "Position déclarée",
                "position", null, FormattableString.Invariant($"{input.X:0.#} ; {input.Y:0.#}"), "GÉOOPS", ct);
            await db.CommitAsync(ct);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
        await notifier.PositionsAsync(1);
    }

    private sealed record PositionLue(string Cle, string? DiscordId, long? EquipageId, string? Nom, double X, double Y, double? Cap, double? Vitesse,
        string? Vehicule, string Source, DateTimeOffset Le);

    /// <summary>Situation de la carte : équipages positionnés, autres agents en service, interventions actives.</summary>
    public async Task<GeoopsSituationDto> SituationAsync(CancellationToken ct)
    {
        var calibration = await CalibrationAsync(ct);
        var maintenant = DateTimeOffset.UtcNow;
        var limite = maintenant.AddSeconds(-calibration.PeremptionSecondes);

        await using var db = await saphirDb.OpenAsync(ct);
        var positions = await db.QueryAsync("SELECT * FROM geo_positions",
            r => new PositionLue(r.Text("cle"), r.NullableText("discord_id"), r.NullableLong("equipage_id"), r.NullableText("nom"),
                OpSql.Double(r, "x") ?? 0, OpSql.Double(r, "y") ?? 0, OpSql.Double(r, "cap"), OpSql.Double(r, "vitesse"), r.NullableText("vehicule"),
                r.Text("source"), r.Date("maj_le")), ct);
        var fiveM = positions.Where(p => p.Source == SourceFiveM && p.Le >= limite && p.DiscordId is not null)
            .GroupBy(p => p.DiscordId!).ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Le).First());
        var declarees = positions.Where(p => p.Source == SourceDeclaree && p.EquipageId is not null)
            .GroupBy(p => p.EquipageId!.Value).ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Le).First());

        // Membres des équipages actifs, avec leur identifiant Discord (répertoire RH).
        var membres = await db.QueryAsync(
            """
            SELECT m.equipage_id, m.libelle, m.role, a.discord_id FROM mc_equipage_membres m
            JOIN mc_equipages e ON e.id = m.equipage_id AND e.actif = 1
            JOIN rh_agents a ON a.id = m.agent_id
            """,
            r => (EquipageId: r.Long("equipage_id"), Libelle: r.Text("libelle"), Chef: r.Text("role") == MainCouranteListes.ChefDeBord,
                DiscordId: r.Text("discord_id")), ct);
        var equipages = await MainCouranteStore.ListerEquipagesAsync(db, null, true, null, ct);

        var resultat = new List<GeoopsEquipageDto>();
        var rattaches = new HashSet<string>(StringComparer.Ordinal);
        foreach (var equipage in equipages)
        {
            // Position du chef de bord en priorité, sinon du membre vu le plus récemment.
            var candidats = membres.Where(m => m.EquipageId == equipage.Id)
                .Select(m => (m.Chef, m.Libelle, Position: fiveM.TryGetValue(m.DiscordId, out var p) ? p : null))
                .Where(m => m.Position is not null)
                .OrderByDescending(m => m.Chef).ThenByDescending(m => m.Position!.Le)
                .ToList();
            foreach (var c in membres.Where(m => m.EquipageId == equipage.Id))
                rattaches.Add(c.DiscordId);

            if (candidats.Count > 0)
            {
                var (_, libelle, p) = candidats[0];
                resultat.Add(new GeoopsEquipageDto(equipage.Id, equipage.Indicatif, equipage.Statut, equipage.InterventionId,
                    equipage.InterventionNumero, p!.X, p.Y, p.Cap, p.Vitesse, p.Vehicule, SourceFiveM, libelle, p.Le));
            }
            else if (declarees.TryGetValue(equipage.Id, out var d))
            {
                resultat.Add(new GeoopsEquipageDto(equipage.Id, equipage.Indicatif, equipage.Statut, equipage.InterventionId,
                    equipage.InterventionNumero, d.X, d.Y, null, null, null, SourceDeclaree, null, d.Le));
            }
        }

        var autres = fiveM.Values.Where(p => !rattaches.Contains(p.DiscordId!))
            .Select(p => new GeoopsAgentDto(p.Nom ?? "Agent", p.X, p.Y, p.Le))
            .Concat(positions.Where(p => p.Source == SourceFiveM && p.DiscordId is null && p.Le >= limite)
                .Select(p => new GeoopsAgentDto(p.Nom ?? "Joueur", p.X, p.Y, p.Le)))
            .ToList();

        var interventions = await db.QueryAsync(
            """
            SELECT id, numero, nature, priorite, statut, adresse, x, y FROM pg_interventions
            WHERE statut NOT IN (@cloturee, @annulee) ORDER BY priorite, debut_le DESC LIMIT 300
            """,
            r => (Dto: new GeoopsInterventionDto(r.Long("id"), r.Text("numero"), r.Text("nature"), r.Text("priorite"), r.Text("statut"),
                r.NullableText("adresse"), OpSql.Double(r, "x") ?? 0, OpSql.Double(r, "y") ?? 0), Placee: r.Value("x") is not null && r.Value("y") is not null),
            ct, ("@cloturee", InterventionStatuts.Cloturee), ("@annulee", InterventionStatuts.Annulee));

        return new GeoopsSituationDto(calibration, resultat, autres,
            interventions.Where(i => i.Placee).Select(i => i.Dto).ToList(),
            interventions.Where(i => !i.Placee).Select(i => i.Dto).ToList(),
            maintenant);
    }
}
