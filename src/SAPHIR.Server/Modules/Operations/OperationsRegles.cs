using System.Globalization;
using System.Text.Json;
using Saphir.Shared.Contracts.Operations;

namespace Saphir.Server.Modules.Operations;

/// <summary>Règles pures du module Opérations (sans base). Testées dans SAPHIR.Tests/OperationsTests.cs.</summary>
public static class OperationsRegles
{
    // ----- Objets de l'historique -----
    public const string ObjetVacation = "mc-vacation";
    public const string ObjetEquipage = "mc-equipage";
    public const string ObjetVehicule = "mc-vehicule";
    public const string ObjetMention = "mc-mention";
    public const string ObjetJournal = "mc-journal";
    public const string ObjetAppel = "pg-appel";
    public const string ObjetIntervention = "pg-intervention";
    public const string ObjetCompteRendu = "pg-compte-rendu";
    public const string ObjetTn = "pg-tn";

    public static readonly IReadOnlyList<string> ObjetsMainCourante = [ObjetVacation, ObjetEquipage, ObjetVehicule, ObjetMention, ObjetJournal];
    public static readonly IReadOnlyList<string> ObjetsPegase = [ObjetAppel, ObjetIntervention, ObjetCompteRendu, ObjetTn];

    // ----- Paramètres centraux créés par migration -----
    public const string ParamWebhook = "pegase.discord.webhook";
    public const string ParamCarteXMin = "geoops.carte.x_min";
    public const string ParamCarteXMax = "geoops.carte.x_max";
    public const string ParamCarteYMin = "geoops.carte.y_min";
    public const string ParamCarteYMax = "geoops.carte.y_max";
    public const string ParamPeremption = "geoops.positions.peremption";

    /// <summary>Calibration par défaut : carte GTA V complète (atlas), à ajuster à l'image fournie.</summary>
    public const double XMinDefaut = -4140, XMaxDefaut = 4860, YMinDefaut = -4300, YMaxDefaut = 8700;
    public const int PeremptionDefaut = 60;

    // ----- Numérotation -----

    /// <summary>Numéro lisible : « INT-2026-000042 » (préfixe, année de création, identifiant sur 6 chiffres).</summary>
    public static string Numero(string prefixe, DateTimeOffset date, long id)
        => $"{prefixe}-{date.Year.ToString(CultureInfo.InvariantCulture)}-{id.ToString("D6", CultureInfo.InvariantCulture)}";

    public const string PrefixeAppel = "APL";
    public const string PrefixeIntervention = "INT";
    public const string PrefixeMention = "MC";
    public const string PrefixeVacation = "VAC";
    public const string PrefixeVehicule = "VS";
    public const string PrefixeCompteRendu = "CR";

    // ----- Interventions -----

    /// <summary>Statut initial d'une intervention créée depuis un appel.</summary>
    public static string StatutInitial(string? motif, bool engagementNecessaire)
        => string.IsNullOrWhiteSpace(motif) ? InterventionStatuts.AQualifier
            : engagementNecessaire ? InterventionStatuts.EnAttente
            : InterventionStatuts.AQualifier;

    /// <summary>
    /// Changement manuel du statut global. Retourne le message d'erreur, ou null si autorisé.
    /// Clôture et annulation passent par cette règle ; une intervention terminée ne change plus.
    /// </summary>
    public static string? ControlerStatutIntervention(string actuel, string nouveau, string? motif, bool compteRenduValide)
    {
        if (!InterventionStatuts.All.Contains(nouveau))
            return "Statut d'intervention inconnu.";
        if (actuel == nouveau)
            return "L'intervention a déjà ce statut.";
        if (InterventionStatuts.EstTerminee(actuel))
            return "L'intervention est terminée : son statut ne peut plus changer.";
        if (nouveau == InterventionStatuts.Annulee && string.IsNullOrWhiteSpace(motif))
            return "L'annulation doit être justifiée.";
        if (nouveau == InterventionStatuts.Cloturee && !compteRenduValide && string.IsNullOrWhiteSpace(motif))
            return "Aucun compte rendu validé : indique le motif de clôture sans compte rendu.";
        return null;
    }

    /// <summary>
    /// Statut global déduit après un changement de statut d'équipage : premier équipage en route ou sur place →
    /// « En cours » ; plus aucun engagement actif sur une intervention en cours → « Compte rendu attendu ».
    /// Plus aucun engagement actif sans que personne ne soit allé sur place (désengagement) → « En attente d'engagement ».
    /// Retourne null si le statut global ne change pas.
    /// </summary>
    public static string? StatutDeduit(string actuel, IReadOnlyCollection<string> statutsEngagementsActifs, bool dejaSurLeTerrain)
    {
        if (InterventionStatuts.EstTerminee(actuel) || actuel == InterventionStatuts.CompteRenduAttendu)
            return null;
        if (statutsEngagementsActifs.Count == 0)
        {
            if (dejaSurLeTerrain)
                return actuel is InterventionStatuts.EnCours or InterventionStatuts.MoyensAffectes ? InterventionStatuts.CompteRenduAttendu : null;
            return actuel == InterventionStatuts.MoyensAffectes ? InterventionStatuts.EnAttente : null;
        }
        var surLeTerrain = statutsEngagementsActifs.Any(s => s is EquipageStatuts.EnRoute or EquipageStatuts.SurPlace
            or EquipageStatuts.Transport or EquipageStatuts.AuService);
        if (surLeTerrain && actuel != InterventionStatuts.EnCours)
            return InterventionStatuts.EnCours;
        if (!surLeTerrain && InterventionStatuts.EstEnAttente(actuel))
            return InterventionStatuts.MoyensAffectes;
        return null;
    }

    /// <summary>Événement du journal de service correspondant à un statut d'équipage (null : pas d'événement dédié).</summary>
    public static string TypeJournalPourStatut(string statut) => statut switch
    {
        EquipageStatuts.SurPlace => JournalTypes.ArriveeSurPlace,
        EquipageStatuts.MissionTerminee => JournalTypes.RetourMission,
        _ => JournalTypes.StatutEquipage
    };

    // ----- Équipages -----

    /// <summary>Contrôle la composition : au moins un membre, un seul chef de bord, pas de doublon, rôles connus.</summary>
    public static string? ControlerMembres(IReadOnlyList<EquipageMembreInput>? membres)
    {
        if (membres is null || membres.Count == 0)
            return "L'équipage doit compter au moins un agent.";
        if (membres.Count > 8)
            return "Un équipage compte au plus 8 agents.";
        if (membres.Select(m => m.AgentId).Distinct().Count() != membres.Count)
            return "Un agent figure deux fois dans l'équipage.";
        if (membres.Any(m => !MainCouranteListes.RolesMembre.Contains(m.Role)))
            return "Rôle d'équipage inconnu.";
        var chefs = membres.Count(m => m.Role == MainCouranteListes.ChefDeBord);
        if (chefs != 1)
            return "L'équipage doit avoir un et un seul chef de bord.";
        return null;
    }

    /// <summary>Indicatif radio : 2 à 20 caractères, lettres, chiffres, espaces et tirets (« PS 12 », « BAC-94 »).</summary>
    public static bool IndicatifValide(string? indicatif)
    {
        if (string.IsNullOrWhiteSpace(indicatif))
            return false;
        var t = indicatif.Trim();
        return t.Length is >= 2 and <= 20 && t.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '/');
    }

    // ----- Véhicules -----

    /// <summary>Immatriculation normalisée : majuscules, tirets entre les blocs (« AB-123-CD ») si le format SIV est reconnu.</summary>
    public static string NormaliserImmatriculation(string? immatriculation)
    {
        var brut = new string((immatriculation ?? string.Empty).Trim().ToUpperInvariant()
            .Where(c => char.IsLetterOrDigit(c)).ToArray());
        if (brut.Length == 7 && char.IsLetter(brut[0]) && char.IsLetter(brut[1]) && brut[2..5].All(char.IsDigit) &&
            char.IsLetter(brut[5]) && char.IsLetter(brut[6]))
            return $"{brut[..2]}-{brut[2..5]}-{brut[5..]}";
        return brut;
    }

    /// <summary>Kilométrage d'un mouvement : positif, jamais inférieur au précédent, saut maximal de 5 000 km.</summary>
    public static string? ControlerKilometrage(int precedent, int nouveau)
    {
        if (nouveau < 0)
            return "Le kilométrage ne peut pas être négatif.";
        if (nouveau < precedent)
            return $"Le kilométrage saisi ({nouveau}) est inférieur au dernier relevé ({precedent}).";
        if (nouveau - precedent > 5000)
            return "Écart de kilométrage invraisemblable (plus de 5 000 km) : vérifie la saisie.";
        return null;
    }

    // ----- Mentions -----

    /// <summary>Une mention peut être modifiée : brouillon uniquement (une mention validée est figée, règle 10).</summary>
    public static bool MentionModifiable(string statut) => statut == MentionStatuts.Brouillon;

    // ----- Discord (8.3) -----

    /// <summary>URL de webhook Discord exploitable (sinon : notification désactivée).</summary>
    public static bool WebhookValide(string? url)
        => Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
           (uri.Host is "discord.com" or "discordapp.com" or "ptb.discord.com" or "canary.discord.com") &&
           uri.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.Ordinal);

    /// <summary>
    /// Message Discord d'une nouvelle intervention : référence, heure, motif, commune, priorité, moyens demandés, statut.
    /// Aucune donnée personnelle (requérant, personnes, véhicules, description restent dans SAPHIR — règle 18).
    /// </summary>
    public static string MessageDiscord(string numero, DateTimeOffset debut, string nature, string? commune, string priorite,
        int? moyens, string statut, string organisation)
    {
        var couleur = priorite switch { "P1" => 0xC7503B, "P2" => 0xE39A2D, _ => 0x2F6FBF };
        var heure = TimeZoneInfo.ConvertTime(debut, Fuseau).ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("fr-FR"));
        var payload = new
        {
            username = "PÉGASE",
            embeds = new[]
            {
                new
                {
                    title = $"Nouvelle intervention {numero}",
                    color = couleur,
                    fields = new object[]
                    {
                        new { name = "Heure", value = heure, inline = true },
                        new { name = "Priorité", value = PegaseListes.LibellePriorite(priorite), inline = true },
                        new { name = "Statut", value = statut, inline = true },
                        new { name = "Motif", value = Limiter(nature, 200), inline = true },
                        new { name = "Commune", value = Limiter(string.IsNullOrWhiteSpace(commune) ? "Non précisée" : commune, 100), inline = true },
                        new { name = "Moyens demandés", value = moyens is > 0 ? moyens.Value.ToString(CultureInfo.InvariantCulture) : "—", inline = true }
                    },
                    footer = new { text = $"{organisation} — détails dans SAPHIR › PÉGASE" }
                }
            },
            allowed_mentions = new { parse = Array.Empty<string>() }
        };
        return JsonSerializer.Serialize(payload);
    }

    private static string Limiter(string texte, int max) => texte.Length <= max ? texte : texte[..max];

    /// <summary>Fuseau d'affichage des heures (France métropolitaine) ; UTC si le fuseau est absent du système.</summary>
    public static TimeZoneInfo Fuseau { get; } = TrouverFuseau();

    private static TimeZoneInfo TrouverFuseau()
    {
        foreach (var id in new[] { "Europe/Paris", "Romance Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }
        return TimeZoneInfo.Utc;
    }

    // ----- GÉOOPS -----

    /// <summary>Lit un nombre décimal de paramètre (point ou virgule) ; valeur par défaut si illisible.</summary>
    public static double LireDouble(string? valeur, double parDefaut)
        => double.TryParse((valeur ?? string.Empty).Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
           && double.IsFinite(v) ? v : parDefaut;

    /// <summary>Clé d'une position : « discord:123… » si connu, sinon « joueur:12 » ; null si aucun identifiant.</summary>
    public static string? ClePosition(GeoopsPositionInput position)
    {
        var discord = NettoyerDiscordId(position.DiscordId);
        if (discord is not null)
            return "discord:" + discord;
        return position.ServerId is > 0 ? "joueur:" + position.ServerId.Value.ToString(CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Identifiant Discord (chiffres seuls), « discord:123 » accepté ; null si invalide.</summary>
    public static string? NettoyerDiscordId(string? discordId)
    {
        if (string.IsNullOrWhiteSpace(discordId))
            return null;
        var t = discordId.Trim();
        if (t.StartsWith("discord:", StringComparison.OrdinalIgnoreCase))
            t = t["discord:".Length..];
        return t.Length is >= 5 and <= 32 && t.All(char.IsDigit) ? t : null;
    }

    /// <summary>Position plausible dans le monde du jeu (bornes larges).</summary>
    public static bool PositionPlausible(double x, double y)
        => double.IsFinite(x) && double.IsFinite(y) && Math.Abs(x) <= 20000 && Math.Abs(y) <= 20000;

    /// <summary>Coordonnées du jeu → fraction (0..1) de l'image ; l'axe Y du jeu monte, celui de l'image descend.</summary>
    public static (double U, double V) VersImage(double x, double y, GeoopsCalibrationDto c)
    {
        var largeur = c.XMax - c.XMin;
        var hauteur = c.YMax - c.YMin;
        if (Math.Abs(largeur) < 1e-6 || Math.Abs(hauteur) < 1e-6)
            return (0.5, 0.5);
        return ((x - c.XMin) / largeur, (c.YMax - y) / hauteur);
    }

    /// <summary>Fraction (0..1) de l'image → coordonnées du jeu.</summary>
    public static (double X, double Y) VersJeu(double u, double v, GeoopsCalibrationDto c)
        => (c.XMin + u * (c.XMax - c.XMin), c.YMax - v * (c.YMax - c.YMin));

    // ----- Divers -----

    /// <summary>Adresse affichée : adresse complète saisie, sinon « numéro rue, code postal commune ».</summary>
    public static string? AdresseAffichee(AppelLocalisation? l)
    {
        if (l is null)
            return null;
        if (!string.IsNullOrWhiteSpace(l.AdresseComplete))
            return l.AdresseComplete.Trim();
        var voie = string.Join(" ", new[] { l.Numero, l.Rue }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
        var ville = string.Join(" ", new[] { l.CodePostal, l.Commune }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
        var adresse = string.Join(", ", new[] { voie, ville }.Where(s => s.Length > 0));
        return adresse.Length == 0 ? null : adresse;
    }

    /// <summary>Libellé court d'une personne saisie (« NOM Prénom » ou « Identité inconnue »).</summary>
    public static string LibellePersonne(string? nom, string? prenom)
    {
        var t = $"{(nom ?? string.Empty).Trim().ToUpperInvariant()} {(prenom ?? string.Empty).Trim()}".Trim();
        return t.Length == 0 ? "Identité inconnue" : t;
    }

    /// <summary>
    /// Différences champ par champ entre deux objets (propriétés publiques sérialisées en JSON) pour l'historique.
    /// Les listes et sous-objets sont comparés en bloc.
    /// </summary>
    public static List<(string Champ, string? Ancienne, string? Nouvelle)> Differences(string prefixe, object? ancien, object? nouveau)
    {
        var resultat = new List<(string, string?, string?)>();
        var typeAncien = ancien?.GetType();
        var typeNouveau = nouveau?.GetType();
        var type = typeNouveau ?? typeAncien;
        if (type is null)
            return resultat;
        foreach (var propriete in type.GetProperties())
        {
            if (propriete.GetIndexParameters().Length > 0)
                continue;
            // Chaque objet est lu par son propre type (les deux objets peuvent être de types anonymes différents).
            var a = ancien is null ? null : Texte(typeAncien!.GetProperty(propriete.Name)?.GetValue(ancien));
            var n = nouveau is null ? null : Texte(typeNouveau!.GetProperty(propriete.Name)?.GetValue(nouveau));
            if (!string.Equals(a, n, StringComparison.Ordinal))
                resultat.Add((prefixe.Length == 0 ? propriete.Name : prefixe + "." + propriete.Name, a, n));
        }
        return resultat;
    }

    private static readonly JsonSerializerOptions JsonHistorique = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static string? Texte(object? valeur) => valeur switch
    {
        null => null,
        string s => string.IsNullOrWhiteSpace(s) ? null : s,
        bool b => b ? "oui" : "non",
        DateTimeOffset d => d.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => JsonSerializer.Serialize(valeur, valeur.GetType(), JsonHistorique)
    };
}
