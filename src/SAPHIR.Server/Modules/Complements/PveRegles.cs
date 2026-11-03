using System.Globalization;
using Saphir.Shared.Contracts.Complements;

namespace Saphir.Server.Modules.Complements;

/// <summary>Règles pures du PVe (sans base) : classes, montants, numéro d'avis, validation de saisie.</summary>
public static class PveRegles
{
    public static readonly IReadOnlyList<string> Classes = ["1", "2", "3", "4", "5", "AFD"];

    /// <summary>« Contravention de 4e classe », « Amende forfaitaire délictuelle ».</summary>
    public static string LibelleClasse(string classe) => classe switch
    {
        "1" => "Contravention de 1re classe",
        "2" => "Contravention de 2e classe",
        "3" => "Contravention de 3e classe",
        "4" => "Contravention de 4e classe",
        "5" => "Contravention de 5e classe",
        "AFD" => "Amende forfaitaire délictuelle",
        _ => "Classe " + classe
    };

    /// <summary>
    /// Numéro d'avis fictif : « 94 » (département) + année sur 2 chiffres + rang sur 7 chiffres + clé sur 2 chiffres
    /// (97 − (nombre mod 97)), soit 13 chiffres.
    /// </summary>
    public static string NumeroAvis(int annee, long rang)
    {
        if (rang is < 1 or > 9_999_999)
            throw new ArgumentOutOfRangeException(nameof(rang));
        var corps = "94" + (annee % 100).ToString("D2", CultureInfo.InvariantCulture) + rang.ToString("D7", CultureInfo.InvariantCulture);
        return corps + Cle(corps).ToString("D2", CultureInfo.InvariantCulture);
    }

    /// <summary>Clé de contrôle d'un numéro d'avis (1 à 97).</summary>
    public static int Cle(string corps)
    {
        var nombre = long.Parse(corps, NumberStyles.None, CultureInfo.InvariantCulture);
        return (int)(97 - nombre % 97);
    }

    /// <summary>Vérifie la clé d'un numéro d'avis de 13 chiffres.</summary>
    public static bool NumeroAvisValide(string? numero)
        => numero is { Length: 13 } && numero.All(char.IsAsciiDigit) &&
           Cle(numero[..11]) == int.Parse(numero[11..], NumberStyles.None, CultureInfo.InvariantCulture);

    /// <summary>Contrôles d'une infraction du référentiel ; renvoie le premier problème ou null.</summary>
    public static string? ValiderInfraction(InfractionInput i)
    {
        if (string.IsNullOrWhiteSpace(i.Natinf) || i.Natinf.Trim().Length > 10 || !i.Natinf.Trim().All(char.IsAsciiDigit))
            return "Le code NATINF est composé de 1 à 10 chiffres.";
        if (string.IsNullOrWhiteSpace(i.Libelle))
            return "Le libellé de l'infraction est obligatoire.";
        if (string.IsNullOrWhiteSpace(i.Categorie))
            return "La catégorie est obligatoire.";
        if (!Classes.Contains(i.Classe))
            return "Classe inconnue (1 à 5 ou AFD).";
        if (i.MontantForfaitaire <= 0 || i.MontantForfaitaire > 10000)
            return "Le montant forfaitaire doit être compris entre 0 et 10 000 €.";
        if (i.MontantMinore is { } minore && (minore <= 0 || minore >= i.MontantForfaitaire))
            return "Le montant minoré doit être inférieur au montant forfaitaire.";
        if (i.MontantMajore is { } majore && majore <= i.MontantForfaitaire)
            return "Le montant majoré doit être supérieur au montant forfaitaire.";
        if (i.Points is < 0 or > 12)
            return "Le retrait de points est compris entre 0 et 12.";
        return null;
    }

    /// <summary>Contrôles d'un PVe avant enregistrement ; renvoie le premier problème ou null.</summary>
    public static string? ValiderSaisie(PveInput p, DateTimeOffset maintenant)
    {
        if (p.InfractionId <= 0)
            return "Choisis l'infraction relevée.";
        if (string.IsNullOrWhiteSpace(p.Lieu))
            return "Le lieu de l'infraction est obligatoire.";
        if (p.DateInfraction > maintenant.AddMinutes(10))
            return "La date de l'infraction ne peut pas être dans le futur.";
        if (p.DateInfraction < maintenant.AddDays(-30))
            return "Un PVe se rédige dans les 30 jours suivant l'infraction.";
        if (string.IsNullOrWhiteSpace(p.ContrevenantNom) && string.IsNullOrWhiteSpace(p.Immatriculation))
            return "Indique le contrevenant ou l'immatriculation du véhicule.";
        if (p.ContrevenantNaissance is { } naissance && naissance > DateOnly.FromDateTime(maintenant.UtcDateTime))
            return "La date de naissance du contrevenant est dans le futur.";
        if (!string.IsNullOrWhiteSpace(p.Immatriculation) && !ImmatriculationPlausible(p.Immatriculation))
            return "Immatriculation invalide (lettres, chiffres et tirets, 4 à 12 caractères).";
        return null;
    }

    /// <summary>« ab-123-cd » → « AB-123-CD » (espaces retirés).</summary>
    public static string? NormaliserImmatriculation(string? immatriculation)
    {
        if (string.IsNullOrWhiteSpace(immatriculation))
            return null;
        return new string(immatriculation.Trim().ToUpperInvariant().Where(c => c != ' ').ToArray());
    }

    public static bool ImmatriculationPlausible(string immatriculation)
    {
        var i = NormaliserImmatriculation(immatriculation) ?? string.Empty;
        return i.Length is >= 4 and <= 12 && i.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    }

    /// <summary>Montant en euros : « 135,00 € ».</summary>
    public static string Euros(decimal? montant)
        => montant is { } m ? m.ToString("N2", CultureInfo.GetCultureInfo("fr-FR")) + " €" : "—";
}
