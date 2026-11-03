using System.Globalization;

namespace Saphir.Shared.Contracts.Judiciaire;

/// <summary>Types de champs d'un formulaire d'acte (rendu du poste, contrôles du serveur).</summary>
public static class ChampTypes
{
    public const string Texte = "texte";
    public const string TexteLong = "long";
    /// <summary>Date « jj/mm/aaaa ».</summary>
    public const string Date = "date";
    /// <summary>Heure « hh:mm ».</summary>
    public const string Heure = "heure";
    public const string Choix = "choix";
    public const string OuiNon = "ouinon";
}

/// <summary>Champ d'un acte : code stable (clé de <see cref="ActeDto.Champs"/>), libellé, section, type.</summary>
public sealed record ChampActe(
    string Code,
    string Libelle,
    string Section,
    string Type = ChampTypes.Texte,
    bool Obligatoire = false,
    IReadOnlyList<string>? Choix = null);

/// <summary>
/// Description d'un type d'acte (14 et 15) : sections dans l'ordre, champs, et blocs spéciaux
/// (personne, mis en cause, agents, questions/réponses, pièces) avec la section où ils s'affichent.
/// </summary>
public sealed record TypeActeInfo(
    string Code,
    string Libelle,
    string Abrege,
    IReadOnlyList<string> Sections,
    IReadOnlyList<ChampActe> Champs,
    string? LibellePersonne,
    string? SectionPersonne,
    string? LibelleMisEnCause,
    string? SectionMisEnCause,
    string? SectionAgents,
    string? SectionQuestions,
    string? SectionPieces,
    bool InscriptionTaj,
    string ModeleParDefaut);

/// <summary>Catalogue des actes LRPPN, commun au serveur et au poste.</summary>
public static class ActesCatalogue
{
    public const string Pvi = "pvi";
    public const string Plainte = "plainte";
    public const string Audition = "audition";
    public const string Constatation = "constatation";
    public const string Saisie = "saisie";
    public const string Complementaire = "pv-complementaire";
    public const string Decouverte = "pv-decouverte";
    public const string CompteRenduEnquete = "cr-enquete";
    public const string RapportInformation = "rapport-information";
    public const string Convocation = "convocation";
    public const string Requisition = "requisition";
    public const string Rectificatif = "acte-rectificatif";
    public const string Bordereau = "bordereau";
    public const string Inventaire = "inventaire";
    public const string MesureProcedurale = "mesure-procedurale";

    /// <summary>Rôles des agents dans un acte.</summary>
    public static readonly IReadOnlyList<string> RolesAgents =
        ["Rédacteur", "Interpellateur", "Assistant", "Autre intervenant", "Chef de bord", "Agent réceptionnaire", "Interprète"];

    private static readonly IReadOnlyList<string> OuiNon = ["Oui", "Non"];

    private const string PreambuleStandard =
        "L'an {annee}, le {date} à {heure},\nNous, {redacteur}, {grade}, RIO {rio}, en fonction au {service},\n" +
        "agissant dans le cadre de la procédure n° {procedure},\n";

    private static ChampActe C(string code, string libelle, string section, string type = ChampTypes.Texte,
        bool obligatoire = false, IReadOnlyList<string>? choix = null) => new(code, libelle, section, type, obligatoire, choix);

    // ----- 14.4 PVI -----
    private const string SA = "A — Personne interpellée";
    private const string SB = "B — Agents";
    private const string SC = "C — Circonstances";
    private const string SD = "D — Déroulement";
    private const string SE = "E — Transport";
    private const string SF = "F — Suites";
    private const string SG = "G — Annexes";

    private static readonly TypeActeInfo PviInfo = new(
        Pvi, "Procès-verbal d'interpellation", "PVI",
        [SA, SB, SC, SD, SE, SF, SG],
        [
            C("id_nom", "NOM", SA, obligatoire: true),
            C("id_prenoms", "Prénoms", SA, obligatoire: true),
            C("id_naissance", "Naissance (date et lieu)", SA),
            C("id_nationalite", "Nationalité", SA),
            C("id_adresse", "Adresse", SA),
            C("id_alias", "Alias", SA),
            C("id_document", "Document d'identité fictif", SA),
            C("id_verification", "État de vérification de l'identité", SA, ChampTypes.Choix, true,
                ["Non vérifiée", "Vérifiée", "Déclarative", "Vérification en cours"]),
            C("id_moyen", "Moyen de vérification", SA),
            C("id_observations", "Observations", SA, ChampTypes.TexteLong),

            C("b_equipage", "Équipage", SB),
            C("b_vehicule", "Véhicule", SB),
            C("b_intervention", "Intervention PÉGASE", SB),

            C("c_date", "Date", SC, ChampTypes.Date, true),
            C("c_heure", "Heure", SC, ChampTypes.Heure, true),
            C("c_commune", "Commune", SC, obligatoire: true),
            C("c_adresse", "Adresse", SC),
            C("c_lieu", "Lieu détaillé", SC),
            C("c_motif", "Motif", SC, obligatoire: true),
            C("c_cadre", "Cadre procédural déclaré", SC, ChampTypes.Choix, true, ProcedureReferentiels.Cadres),
            C("c_faits_signales", "Faits signalés", SC, ChampTypes.TexteLong),
            C("c_faits_constates", "Faits constatés", SC, ChampTypes.TexteLong, true),
            C("c_personnes_presentes", "Personnes présentes", SC, ChampTypes.TexteLong),
            C("c_elements", "Éléments matériels", SC, ChampTypes.TexteLong),
            C("c_observations", "Observations", SC, ChampTypes.TexteLong),

            C("d_chronologie", "Chronologie", SD, ChampTypes.TexteLong, true),
            C("d_agents", "Agents", SD),
            C("d_heure_interpellation", "Heure de l'interpellation", SD, ChampTypes.Heure, true),
            C("d_verification", "Vérification d'identité", SD, ChampTypes.TexteLong),
            C("d_difficultes", "Difficultés constatées", SD, ChampTypes.TexteLong),
            C("d_incidents", "Incidents", SD, ChampTypes.TexteLong),
            C("d_blessures", "Blessures", SD),
            C("d_assistance", "Assistance médicale", SD, ChampTypes.Choix, choix: ["Non nécessaire", "Proposée et refusée", "Effectuée sur place", "Transport en milieu hospitalier"]),
            C("d_temoins", "Témoins", SD, ChampTypes.TexteLong),
            C("d_objets", "Objets", SD, ChampTypes.TexteLong),
            C("d_observations", "Observations", SD, ChampTypes.TexteLong),

            C("e_effectue", "Transport effectué", SE, ChampTypes.OuiNon, choix: OuiNon),
            C("e_heure_depart", "Heure de départ", SE, ChampTypes.Heure),
            C("e_lieu_depart", "Lieu de départ", SE),
            C("e_service_destinataire", "Service destinataire", SE),
            C("e_heure_arrivee", "Heure d'arrivée", SE, ChampTypes.Heure),
            C("e_equipage", "Équipage", SE),
            C("e_agents", "Agents", SE),
            C("e_receptionnaire", "Agent réceptionnaire", SE),
            C("e_observations", "Observations", SE, ChampTypes.TexteLong),

            C("f_situation", "Situation après intervention", SF, obligatoire: true),
            C("f_service_saisi", "Service saisi", SF),
            C("f_autorite", "Autorité informée", SF),
            C("f_date_heure", "Date et heure de l'avis", SF),
            C("f_mesure", "Mesure procédurale liée", SF),
            C("f_pieces", "Pièces", SF),
            C("f_observations", "Observations", SF, ChampTypes.TexteLong),
            C("f_responsable", "Responsable", SF),

            C("g_photographies", "Photographies fictives", SG),
            C("g_documents", "Documents", SG),
            C("g_videos", "Vidéos de simulation", SG),
            C("g_compte_rendu", "Compte rendu PÉGASE", SG, ChampTypes.TexteLong),
            C("g_autres_pv", "Autres PV", SG),
            C("g_objets", "Références d'objets", SG),
            C("g_scelles", "Scellés simulés", SG)
        ],
        "Personne interpellée", SA, null, null, SB, null, SG, true,
        PreambuleStandard + "rapportons les faits suivants relatifs à l'interpellation de la personne désignée ci-après :");

    // ----- 15.1 Plainte -----
    private const string P1 = "Plaignant";
    private const string P2 = "Faits";
    private const string P3 = "Auteur présumé et témoins";
    private const string P4 = "Préjudice et démarches";

    private static readonly TypeActeInfo PlainteInfo = new(
        Plainte, "Procès-verbal de plainte", "PLAINTE",
        [P1, P2, P3, P4],
        [
            C("p_identite", "Identité", P1, obligatoire: true),
            C("p_coordonnees", "Coordonnées", P1),
            C("p_date_faits", "Date des faits", P2, ChampTypes.Date, true),
            C("p_heure_faits", "Heure des faits", P2, ChampTypes.Heure),
            C("p_lieu_faits", "Lieu des faits", P2, obligatoire: true),
            C("p_nature", "Nature", P2, obligatoire: true),
            C("p_recit", "Récit", P2, ChampTypes.TexteLong, true),
            C("p_auteur_declare", "Auteur présumé déclaré", P3, ChampTypes.TexteLong),
            C("p_temoins", "Témoins", P3, ChampTypes.TexteLong),
            C("p_prejudice", "Préjudice", P4, ChampTypes.TexteLong),
            C("p_biens", "Biens", P4, ChampTypes.TexteLong),
            C("p_documents", "Documents", P4),
            C("p_demarches", "Démarches", P4, ChampTypes.TexteLong),
            C("p_observations", "Observations", P4, ChampTypes.TexteLong)
        ],
        "Plaignant", P1, "Auteur présumé identifié", P3, P1, null, P4, true,
        PreambuleStandard + "recevons la plainte de la personne désignée ci-après, qui nous déclare :");

    // ----- 15.2 Audition -----
    private const string AU1 = "Personne entendue";
    private const string AU2 = "Déroulement";
    private const string AU3 = "Questions et réponses";
    private const string AU4 = "Clôture";

    private static readonly TypeActeInfo AuditionInfo = new(
        Audition, "Procès-verbal d'audition", "AUDITION",
        [AU1, AU2, AU3, AU4],
        [
            C("a_type", "Type", AU1, ChampTypes.Choix, true,
                ["Audition de témoin", "Audition de victime", "Audition de mis en cause", "Audition libre", "Audition sous contrainte (simulée)"]),
            C("a_identite", "Identité", AU1, obligatoire: true),
            C("a_qualite", "Qualité", AU1, ChampTypes.Choix, choix: ProcedureReferentiels.QualitesPersonne),
            C("a_interprete", "Interprète", AU2),
            C("a_date", "Date", AU2, ChampTypes.Date, true),
            C("a_heure_debut", "Heure de début", AU2, ChampTypes.Heure, true),
            C("a_heure_fin", "Heure de fin", AU2, ChampTypes.Heure),
            C("a_mentions", "Mentions procédurales applicables", AU2, ChampTypes.TexteLong),
            C("a_interruptions", "Interruptions", AU4, ChampTypes.TexteLong),
            C("a_observations", "Observations", AU4, ChampTypes.TexteLong),
            C("a_relecture", "Relecture", AU4, ChampTypes.Choix, choix: ["Relu et persiste", "Relu, corrections apportées", "Refuse de relire"]),
            C("a_corrections", "Corrections", AU4, ChampTypes.TexteLong),
            C("a_signatures", "Signatures simulées", AU4, ChampTypes.Choix, choix: ["Signé par la personne et l'enquêteur", "Refus de signer", "Signé par l'enquêteur seul"])
        ],
        "Personne entendue", AU1, null, null, AU2, AU3, null, false,
        PreambuleStandard + "procédons à l'audition de la personne désignée ci-après :");

    // ----- 15.3 Constatation -----
    private const string K1 = "Circonstances";
    private const string K2 = "Constatations";
    private const string K3 = "Annexes";

    private static readonly TypeActeInfo ConstatationInfo = new(
        Constatation, "Procès-verbal de constatation", "CONSTAT",
        [K1, K2, K3],
        [
            C("k_date", "Date", K1, ChampTypes.Date, true),
            C("k_heure", "Heure", K1, ChampTypes.Heure, true),
            C("k_lieu", "Lieu", K1, obligatoire: true),
            C("k_contexte", "Contexte", K1, ChampTypes.TexteLong),
            C("k_constatations", "Constatations", K2, ChampTypes.TexteLong, true),
            C("k_personnes", "Personnes", K2, ChampTypes.TexteLong),
            C("k_objets", "Objets", K2, ChampTypes.TexteLong),
            C("k_photographies", "Photographies", K3),
            C("k_documents", "Documents", K3),
            C("k_observations", "Observations", K3, ChampTypes.TexteLong)
        ],
        null, null, null, null, K1, null, K3, false,
        PreambuleStandard + "procédons aux constatations suivantes :");

    // ----- 15.4 Saisie -----
    private const string S1 = "Circonstances";
    private const string S2 = "Objet saisi";
    private const string S3 = "Placement sous scellé";

    private static readonly TypeActeInfo SaisieInfo = new(
        Saisie, "Procès-verbal de saisie", "SAISIE",
        [S1, S2, S3],
        [
            C("s_date", "Date", S1, ChampTypes.Date, true),
            C("s_heure", "Heure", S1, ChampTypes.Heure),
            C("s_lieu", "Lieu", S1, obligatoire: true),
            C("s_objet", "Objet", S2, obligatoire: true),
            C("s_description", "Description", S2, ChampTypes.TexteLong),
            C("s_quantite", "Quantité", S2),
            C("s_etat", "État", S2),
            C("s_provenance", "Provenance", S2),
            C("s_inventaire", "Référence d'inventaire", S3),
            C("s_scelle", "Scellé fictif", S3),
            C("s_destination", "Destination", S3)
        ],
        "Personne concernée", S1, null, null, S1, null, S3, false,
        PreambuleStandard + "procédons à la saisie décrite ci-après :");

    // ----- 15.5 Autres documents -----
    private static TypeActeInfo Autre(string code, string libelle, string abrege, string intro, params ChampActe[] specifiques)
    {
        const string O1 = "Objet";
        const string O2 = "Contenu";
        var champs = new List<ChampActe>(specifiques)
        {
            C("o_objet", "Objet", O1, obligatoire: true),
            C("o_texte", "Texte", O2, ChampTypes.TexteLong, true),
            C("o_observations", "Observations", O2, ChampTypes.TexteLong)
        };
        return new TypeActeInfo(code, libelle, abrege, [O1, O2], champs,
            "Personne concernée", O1, null, null, O1, null, O2, false, PreambuleStandard + intro);
    }

    private static readonly IReadOnlyList<TypeActeInfo> Types =
    [
        PviInfo,
        PlainteInfo,
        AuditionInfo,
        ConstatationInfo,
        SaisieInfo,
        Autre(Complementaire, "Procès-verbal complémentaire", "PV COMPL.", "complétons la procédure ainsi qu'il suit :"),
        Autre(Decouverte, "Procès-verbal de découverte", "DÉCOUVERTE", "rapportons la découverte suivante :",
            C("o_date", "Date de la découverte", "Objet", ChampTypes.Date, true),
            C("o_lieu", "Lieu de la découverte", "Objet")),
        Autre(CompteRenduEnquete, "Compte rendu d'enquête", "CR ENQUÊTE", "rendons compte de l'enquête ainsi qu'il suit :",
            C("o_destinataire", "Destinataire", "Objet")),
        Autre(RapportInformation, "Rapport d'information", "RAPPORT", "avons l'honneur de porter à votre connaissance les éléments suivants :",
            C("o_destinataire", "Destinataire", "Objet")),
        Autre(Convocation, "Convocation", "CONVOCATION", "convoquons la personne désignée ci-après :",
            C("o_date", "Date de convocation", "Objet", ChampTypes.Date, true),
            C("o_heure", "Heure", "Objet", ChampTypes.Heure, true),
            C("o_lieu", "Lieu", "Objet", obligatoire: true)),
        Autre(Requisition, "Réquisition fictive", "RÉQUISITION", "requérons l'organisme désigné ci-après :",
            C("o_requis", "Personne ou organisme requis", "Objet", obligatoire: true),
            C("o_delai", "Délai de réponse", "Objet")),
        Autre(Rectificatif, "Acte rectificatif", "RECTIFICATIF", "rectifions l'acte désigné ci-après :",
            C("o_acte_rectifie", "Numéro de l'acte rectifié", "Objet", obligatoire: true)),
        Autre(Bordereau, "Bordereau de transmission", "BORDEREAU", "transmettons les pièces suivantes :",
            C("o_destinataire", "Destinataire", "Objet", obligatoire: true)),
        Autre(Inventaire, "Inventaire", "INVENTAIRE", "dressons l'inventaire suivant :"),
        Autre(MesureProcedurale, "Acte relatif à une mesure procédurale simulée", "MESURE", "procédons à l'acte suivant :",
            C("o_nature", "Nature de la mesure", "Objet", ChampTypes.Choix, true,
                ["Notification de droits (simulée)", "Placement en garde à vue (simulé)", "Prolongation (simulée)",
                 "Fin de mesure (simulée)", "Avis à magistrat", "Avis à un proche (simulé)"]),
            C("o_date_heure", "Date et heure de la mesure", "Objet", obligatoire: true))
    ];

    public static IReadOnlyList<TypeActeInfo> All => Types;

    public static TypeActeInfo? Trouver(string? code) => Types.FirstOrDefault(t => t.Code == code);

    public static string Libelle(string? code) => Trouver(code)?.Libelle ?? code ?? string.Empty;

    /// <summary>
    /// Champs d'identité préremplis depuis une fiche (14.2). Le rédacteur doit ensuite vérifier l'identité.
    /// Ne renvoie que les champs du type d'acte.
    /// </summary>
    public static Dictionary<string, string?> ChampsIdentite(string type, PersonneFicheDto p)
    {
        var naissance = p.DateNaissance is { } d
            ? d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + (string.IsNullOrWhiteSpace(p.LieuNaissance) ? string.Empty : " à " + p.LieuNaissance)
            : p.LieuNaissance;
        var resume = $"{p.Nom} {p.Prenom}" + (naissance is null ? string.Empty : ", né(e) le " + naissance) +
                     (string.IsNullOrWhiteSpace(p.Nationalite) ? string.Empty : ", de nationalité " + p.Nationalite) +
                     $" — réf. {p.Reference}";
        var coordonnees = string.Join(" — ", new[] { p.Adresse, p.Telephone }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var document = p.Documents.FirstOrDefault() is { } doc ? $"{doc.Type} n° {doc.Numero}" : null;

        var valeurs = new Dictionary<string, string?>
        {
            ["id_nom"] = p.Nom,
            ["id_prenoms"] = p.Prenom,
            ["id_naissance"] = naissance,
            ["id_nationalite"] = p.Nationalite,
            ["id_adresse"] = p.Adresse,
            ["id_alias"] = p.Alias.Count == 0 ? null : string.Join(", ", p.Alias.Select(a => a.Alias)),
            ["id_document"] = document,
            ["p_identite"] = resume,
            ["p_coordonnees"] = coordonnees.Length == 0 ? null : coordonnees,
            ["a_identite"] = resume
        };
        var info = Trouver(type);
        if (info is null)
            return new Dictionary<string, string?>();
        return valeurs.Where(v => info.Champs.Any(c => c.Code == v.Key)).ToDictionary(v => v.Key, v => v.Value);
    }
}
