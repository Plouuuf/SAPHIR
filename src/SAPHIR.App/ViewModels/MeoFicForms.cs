using System.Globalization;
using Saphir.Shared.Contracts;

namespace Saphir.ViewModels;

// Formulaires de MEO-FIC. Champs simples liés en TwoWay ; pour vider un formulaire,
// le ViewModel en crée simplement un nouveau.

public enum MeoFile { Taj, Fpr, Snpc, Foves, Siv, Fva, Journal, Faed, Fnaeg, Fijais, Fijait, Traces }

/// <summary>MEO-FIC (fichiers de police) ou PTS (police technique et scientifique) : même écran, fichiers différents.</summary>
public enum MeoMode { Fichiers, Pts }

/// <summary>Formulaire ouvert dans le volet de détail.</summary>
public enum MeoForm { None, NewPerson, Identity, Taj, Fpr, Permis, Vehicle, Foves, Signalisation, Inscription, Trace }

public sealed class IdentityForm
{
    public string Nom { get; set; } = string.Empty;
    public string Prenom { get; set; } = string.Empty;
    public string? DateNaissance { get; set; }
    public string? LieuNaissance { get; set; }
    public string? Sexe { get; set; }
    public string? Nationalite { get; set; } = "Française";
    public string? Adresse { get; set; }
    public string? Telephone { get; set; }
    public string? Taille { get; set; }
    public string? Signalement { get; set; }
    public string? Alias { get; set; }

    public static IReadOnlyList<string> Sexes { get; } = ["M", "F"];

    public static IdentityForm From(IdentityDto dto) => new()
    {
        Nom = dto.Nom,
        Prenom = dto.Prenom,
        DateNaissance = dto.DateNaissance,
        LieuNaissance = dto.LieuNaissance,
        Sexe = dto.Sexe,
        Nationalite = dto.Nationalite,
        Adresse = dto.Adresse,
        Telephone = dto.Telephone,
        Taille = dto.Taille,
        Signalement = dto.Signalement,
        Alias = dto.Alias
    };

    public IdentityDto ToDto() => new(Nom.Trim(), Prenom.Trim(), DateNaissance, LieuNaissance, Sexe, Nationalite, Adresse,
        Telephone, Taille, Signalement, Alias);
}

public sealed class TajForm
{
    public string Qualite { get; set; } = MeoFicReferentiel.TajQualites[0];
    public string Infraction { get; set; } = string.Empty;
    public string? Natinf { get; set; }
    public string DateFaits { get; set; } = DateTime.Now.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("fr-FR"));
    public string? Lieu { get; set; }
    public string? Service { get; set; }
    public string? Procedure { get; set; }
    public string? Details { get; set; }

    public TajInput ToInput() => new(Qualite, Infraction, Natinf, DateFaits, Lieu, Service, Procedure, Details);
}

public sealed class FprForm
{
    public string Motif { get; set; } = MeoFicReferentiel.FprMotifs[0];
    public string Conduite { get; set; } = MeoFicReferentiel.ConduitesATenir[0];
    public string? Autorite { get; set; }
    public string? Details { get; set; }
    public bool Dangereux { get; set; }
    public bool Arme { get; set; }

    public FprInput ToInput() => new(Motif, Conduite, Autorite, Details, Dangereux, Arme);
}

public sealed class PermisForm
{
    public string? Numero { get; set; }
    public string Categories { get; set; } = "B";
    public string Points { get; set; } = "12";
    public string Statut { get; set; } = MeoFicReferentiel.PermisStatuts[0];
    public string? DateObtention { get; set; }
    public string? Restrictions { get; set; }

    public static PermisForm From(PermisDto? dto) => dto is null
        ? new PermisForm()
        : new PermisForm
        {
            Numero = dto.Numero,
            Categories = dto.Categories,
            Points = dto.Points.ToString(CultureInfo.InvariantCulture),
            Statut = MeoFicReferentiel.PermisStatuts.Contains(dto.Statut) ? dto.Statut : MeoFicReferentiel.PermisStatuts[0],
            DateObtention = dto.DateObtention,
            Restrictions = dto.Restrictions
        };

    public PermisInput ToInput()
    {
        if (!int.TryParse(Points?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var points))
            throw new ArgumentException("Le solde de points doit être un nombre.");
        return new PermisInput(Numero, Categories, points, Statut, DateObtention, Restrictions);
    }
}

public sealed class VehicleForm
{
    public string Plaque { get; set; } = string.Empty;
    public string? Marque { get; set; }
    public string? Modele { get; set; }
    public string? Couleur { get; set; }
    public string? Vin { get; set; }
    public string? DateMiseEnCirculation { get; set; }
    public string? TitulaireKey { get; set; }
    public string? TitulaireNom { get; set; }
    public string? ControleTechnique { get; set; }
    public string? Opposition { get; set; }
    public string? Assureur { get; set; }
    public string? NumeroContrat { get; set; }
    public string? AssuranceValideJusquau { get; set; }

    public static VehicleForm From(VehicleDetailDto dto) => new()
    {
        Plaque = dto.Plaque,
        Marque = dto.Siv.Marque,
        Modele = dto.Siv.Modele,
        Couleur = dto.Siv.Couleur,
        Vin = dto.Siv.Vin,
        DateMiseEnCirculation = dto.Siv.DateMiseEnCirculation,
        TitulaireKey = dto.Siv.TitulaireKey,
        TitulaireNom = dto.Siv.Titulaire,
        ControleTechnique = dto.Siv.ControleTechnique,
        Opposition = dto.Siv.Opposition,
        Assureur = dto.Fva.Assureur,
        NumeroContrat = dto.Fva.NumeroContrat,
        AssuranceValideJusquau = dto.Fva.ValideJusquau
    };

    public VehicleInput ToInput() => new(Plaque, Marque, Modele, Couleur, Vin, DateMiseEnCirculation, TitulaireKey, TitulaireNom,
        ControleTechnique, Opposition, Assureur, NumeroContrat, AssuranceValideJusquau);
}

public sealed class FovesForm
{
    public string Plaque { get; set; } = string.Empty;
    public string Motif { get; set; } = MeoFicReferentiel.FovesMotifs[0];
    public string Conduite { get; set; } = MeoFicReferentiel.ConduitesATenir[0];
    public string? Details { get; set; }

    public FovesInput ToInput() => new(Motif, Conduite, Details);
}

public sealed class SignalisationForm
{
    public string Fichier { get; set; } = PtsFichiers.Faed;
    public string Reference { get; set; } = string.Empty;
    public string DateSignalisation { get; set; } = DateTime.Now.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("fr-FR"));
    public string? Service { get; set; }
    public string? Motif { get; set; }
    public string? Details { get; set; }

    public SignalisationInput ToInput() => new(Fichier, Reference, DateSignalisation, Service, Motif, Details);
}

public sealed class InscriptionForm
{
    public string Fichier { get; set; } = PtsFichiers.Fijais;
    public string Infraction { get; set; } = string.Empty;
    public string Decision { get; set; } = PtsReferentiel.Decisions[0];
    public string? DateDecision { get; set; }
    public string? Juridiction { get; set; }
    public string? Obligations { get; set; } = PtsReferentiel.Obligations[0];
    public string? FinInscription { get; set; }
    public string? Details { get; set; }

    public InscriptionInput ToInput() => new(Fichier, Infraction, Decision, DateDecision, Juridiction, Obligations, FinInscription, Details);
}

public sealed class TraceForm
{
    public string Fichier { get; set; } = PtsFichiers.Faed;
    public string Reference { get; set; } = string.Empty;
    public string? Affaire { get; set; }
    public string? Lieu { get; set; }
    public string? DatePrelevement { get; set; } = DateTime.Now.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("fr-FR"));
    public string? Description { get; set; }

    public TraceInput ToInput() => new(Fichier, Reference, Affaire, Lieu, DatePrelevement, Description);
}
