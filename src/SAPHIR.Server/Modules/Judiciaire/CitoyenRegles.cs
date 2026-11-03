using System.Globalization;
using System.Text;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Judiciaire;

namespace Saphir.Server.Modules.Judiciaire;

/// <summary>
/// Règles pures de la déclaration d'identité par le joueur (ressource FiveM saphir-identite). Testées dans SAPHIR.Tests (CitoyenTests).
/// </summary>
public static class CitoyenRegles
{
    /// <summary>Valeur de <c>per_personnes.source</c> et <c>veh_vehicules.source</c> d'une fiche créée par le joueur.</summary>
    public const string SourceJoueur = "Joueur";

    public const int MaximumParDefaut = 5;
    public const int PointsProbatoire = 6;
    public const int AnneesProbatoire = 3;

    /// <summary>Catégories de permis acceptées (simulation).</summary>
    public static readonly IReadOnlyList<string> CategoriesPermis = ["AM", "A1", "A2", "A", "B1", "B", "BE", "C1", "C", "CE", "D1", "D", "DE"];

    public static readonly IReadOnlyList<string> Corpulences = ["Mince", "Moyenne", "Forte", "Athlétique", "Corpulente"];

    /// <summary>Portail ouvert seulement si le paramètre vaut « oui » (absent : ouvert).</summary>
    public static bool PortailActif(string? valeur)
        => string.IsNullOrWhiteSpace(valeur) || string.Equals(valeur.Trim(), "oui", StringComparison.OrdinalIgnoreCase);

    /// <summary>Maximum de personnages actifs par compte : entier de 1 à 50, sinon 5.</summary>
    public static int Maximum(string? valeur)
        => int.TryParse(valeur?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 50 ? n : MaximumParDefaut;

    /// <summary>Identifiant Discord : 15 à 22 chiffres.</summary>
    public static bool DiscordValide(string? id)
        => !string.IsNullOrWhiteSpace(id) && id.Trim().Length is >= 15 and <= 22 && id.Trim().All(char.IsAsciiDigit);

    /// <summary>Identifiant de personnage FiveM (citizenid QBCore, identifier ESX « char1:… ») : 3 à 128 caractères imprimables.</summary>
    public static bool PersonnageValide(string? id)
        => !string.IsNullOrWhiteSpace(id) && id.Trim().Length is >= 3 and <= 128 && id.Trim().All(c => c > ' ' && c != 0x7F);

    /// <summary>Auteur inscrit dans l'historique : « Joueur (Discord Pseudo, 5600…) ».</summary>
    public static string Auteur(string discordId, string? nomJoueur)
    {
        var nom = Nettoyer(nomJoueur, 80);
        return nom is null ? $"Joueur (Discord {discordId.Trim()})" : $"Joueur (Discord {nom}, {discordId.Trim()})";
    }

    /// <summary>Texte d'une ligne : espaces superflus et caractères de contrôle retirés, tronqué ; null si vide.</summary>
    public static string? Nettoyer(string? texte, int max)
    {
        if (string.IsNullOrWhiteSpace(texte))
            return null;
        var builder = new StringBuilder();
        var espace = false;
        foreach (var c in texte.Trim())
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c))
            {
                espace = true;
                continue;
            }
            if (espace && builder.Length > 0)
                builder.Append(' ');
            espace = false;
            builder.Append(c);
        }
        var resultat = builder.ToString();
        if (resultat.Length == 0)
            return null;
        return resultat.Length > max ? resultat[..max] : resultat;
    }

    /// <summary>Sexe : M, F ou X ; accepte « m », « homme », « 0 » (QBCore : 0 = homme, 1 = femme). Null si absent ; exception si illisible.</summary>
    public static string? Sexe(string? valeur)
    {
        if (string.IsNullOrWhiteSpace(valeur))
            return null;
        return valeur.Trim().ToLowerInvariant() switch
        {
            "m" or "h" or "homme" or "male" or "masculin" or "0" => "M",
            "f" or "femme" or "female" or "féminin" or "feminin" or "1" => "F",
            "x" or "autre" => "X",
            _ => throw new ArgumentException("Sexe attendu : M, F ou X.")
        };
    }

    /// <summary>Date de naissance plausible : entre 1900 et aujourd'hui, personnage d'au moins 10 ans.</summary>
    public static string? ControlerNaissance(DateOnly? naissance, DateOnly aujourdhui)
    {
        if (naissance is not { } d)
            return "Date de naissance obligatoire (jj/mm/aaaa).";
        if (d.Year < 1900 || d > aujourdhui)
            return "Date de naissance invalide.";
        if (d > aujourdhui.AddYears(-10))
            return "Le personnage doit avoir au moins 10 ans.";
        return null;
    }

    /// <summary>Contrôles de saisie de l'identité. Liste vide = déclaration acceptable.</summary>
    public static List<string> ControlerIdentite(CitoyenIdentiteInput input, DateOnly aujourdhui)
    {
        var erreurs = new List<string>();
        if (!NomsFormat.EstValide(input.Nom))
            erreurs.Add("NOM obligatoire (2 à 60 lettres, espaces, traits d'union ou apostrophes).");
        if (!NomsFormat.EstValide(input.Prenom))
            erreurs.Add("Prénom(s) obligatoire(s) (2 à 60 lettres, espaces, traits d'union ou apostrophes).");
        var naissance = JudiciaireRegles.LireDate(input.DateNaissance);
        if (ControlerNaissance(naissance, aujourdhui) is { } erreurNaissance)
            erreurs.Add(erreurNaissance);
        try
        {
            Sexe(input.Sexe);
        }
        catch (ArgumentException ex)
        {
            erreurs.Add(ex.Message);
        }
        if (input.TailleCm is { } taille && taille is < 100 or > 250)
            erreurs.Add("Taille attendue en centimètres (100 à 250).");
        if (Nettoyer(input.Telephone, 40) is { } tel && !TelephoneValide(tel))
            erreurs.Add("Téléphone fictif invalide (chiffres, espaces, « + », « - », « . » ; 3 à 20 chiffres).");
        if (Nettoyer(input.Corpulence, 40) is { } corpulence && !Corpulences.Contains(Majuscule(corpulence)))
            erreurs.Add("Corpulence attendue : " + string.Join(", ", Corpulences) + ".");
        return erreurs;
    }

    public static bool TelephoneValide(string telephone)
    {
        var chiffres = telephone.Count(char.IsAsciiDigit);
        return chiffres is >= 3 and <= 20 && telephone.All(c => char.IsAsciiDigit(c) || c is ' ' or '+' or '-' or '.' or '(' or ')');
    }

    public static string Majuscule(string texte)
        => texte.Length == 0 ? texte : char.ToUpperInvariant(texte[0]) + texte[1..].ToLowerInvariant();

    /// <summary>Signalement lisible par les policiers, composé des champs saisis : « 1,80 m — corpulence mince — tatouage… ».</summary>
    public static string? ComposerSignalement(int? tailleCm, string? corpulence, string? signes)
    {
        var parties = new List<string>();
        if (tailleCm is { } t)
            parties.Add((t / 100.0).ToString("0.00", CultureInfo.GetCultureInfo("fr-FR")) + " m");
        if (Nettoyer(corpulence, 40) is { } c)
            parties.Add("corpulence " + c.ToLowerInvariant());
        if (Nettoyer(signes, 500) is { } s)
            parties.Add(s);
        return parties.Count == 0 ? null : string.Join(" — ", parties);
    }

    /// <summary>Champs d'identité figés quand la personne figure dans un fichier de police : NOM, prénom(s), naissance (date et lieu).</summary>
    public static List<string> ChangementsIdentite(
        (string Nom, string Prenom, DateOnly? Naissance, string? Lieu) avant,
        (string Nom, string Prenom, DateOnly? Naissance, string? Lieu) apres)
    {
        var champs = new List<string>();
        if (!string.Equals(avant.Nom, apres.Nom, StringComparison.Ordinal))
            champs.Add("NOM");
        if (!string.Equals(avant.Prenom, apres.Prenom, StringComparison.Ordinal))
            champs.Add("prénom(s)");
        if (avant.Naissance != apres.Naissance)
            champs.Add("date de naissance");
        // Lieu non fourni par le joueur : on garde celui de la fiche (pas un changement).
        if (apres.Lieu is not null && !string.Equals(avant.Lieu ?? string.Empty, apres.Lieu, StringComparison.Ordinal))
            champs.Add("lieu de naissance");
        return champs;
    }

    // ===== Permis =====

    /// <summary>Probatoire si obtenu il y a moins de 3 ans.</summary>
    public static bool Probatoire(DateOnly obtention, DateOnly aujourdhui) => obtention > aujourdhui.AddYears(-AnneesProbatoire);

    /// <summary>Points et situation d'un nouveau permis : 6 points en probatoire, 12 sinon.</summary>
    public static (int Points, string Situation) PermisInitial(DateOnly obtention, DateOnly aujourdhui)
        => Probatoire(obtention, aujourdhui)
            ? (PointsProbatoire, PermisSituations.Probatoire)
            : (JudiciaireRegles.PointsMax, PermisSituations.Valide);

    /// <summary>Catégories normalisées, sans doublon, dans l'ordre officiel. Exception si une catégorie est inconnue ou si la liste est vide.</summary>
    public static string NormaliserCategories(IEnumerable<string>? categories)
    {
        var liste = (categories ?? [])
            .SelectMany(c => (c ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(c => c.ToUpperInvariant())
            .Distinct()
            .ToList();
        if (liste.Count == 0)
            throw new ArgumentException("Indique au moins une catégorie de permis (ex. B).");
        var inconnue = liste.FirstOrDefault(c => !CategoriesPermis.Contains(c));
        if (inconnue is not null)
            throw new ArgumentException($"Catégorie de permis inconnue : {inconnue}.");
        return string.Join(", ", CategoriesPermis.Where(liste.Contains));
    }

    public static string? ControlerObtention(DateOnly? obtention, DateOnly? naissance, DateOnly aujourdhui)
    {
        if (obtention is not { } d)
            return "Date d'obtention du permis obligatoire (jj/mm/aaaa).";
        if (d > aujourdhui || d.Year < 1920)
            return "Date d'obtention invalide.";
        if (naissance is { } n && d < n.AddYears(14))
            return "Date d'obtention incompatible avec la date de naissance.";
        return null;
    }

    // ===== Véhicules =====

    /// <summary>Plaque acceptée : 2 à 12 lettres ou chiffres (plaques FiveM comprises) ; « AB123CD » est affichée « AB-123-CD ».</summary>
    public static bool PlaqueValide(string? plaque) => JudiciaireRegles.ImmatriculationValide(plaque);

    public static string? ControlerAssurance(string? assureur, string? contrat, DateOnly? fin, bool finLisible)
    {
        var a = Nettoyer(assureur, 100);
        var c = Nettoyer(contrat, 60);
        if (a is null && c is null && fin is null && finLisible)
            return null;
        if (!finLisible)
            return "Date de fin d'assurance attendue au format jj/mm/aaaa.";
        if (a is null || c is null)
            return "Assurance : assureur et numéro de contrat obligatoires.";
        return null;
    }

    /// <summary>Titulaire d'une plaque existante : le véhicule peut être rattaché au personnage s'il est sans titulaire ou déjà à lui.</summary>
    public static bool PlaqueDisponible(long? titulaireActuel, string? titulaireLibre, long personneId)
        => titulaireActuel == personneId || (titulaireActuel is null && string.IsNullOrWhiteSpace(titulaireLibre));
}
