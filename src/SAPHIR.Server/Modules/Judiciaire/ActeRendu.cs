using System.Globalization;
using System.Text;
using Saphir.Shared.Contracts.Judiciaire;

namespace Saphir.Server.Modules.Judiciaire;

/// <summary>Heure de Paris pour les documents (le serveur stocke en UTC).</summary>
public static class HeureLocale
{
    private static readonly TimeZoneInfo Zone = Trouver();

    private static TimeZoneInfo Trouver()
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

    public static DateTimeOffset Convertir(DateTimeOffset date) => TimeZoneInfo.ConvertTime(date, Zone);

    public static string Date(DateTimeOffset date) => Convertir(date).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    public static string Heure(DateTimeOffset date) => Convertir(date).ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string DateHeure(DateTimeOffset date) => Convertir(date).ToString("dd/MM/yyyy à HH:mm", CultureInfo.InvariantCulture);
}

/// <summary>Données nécessaires à la génération du texte d'un acte.</summary>
public sealed record ActeRenduContexte(
    TypeActeInfo Type,
    string NumeroProcedure,
    string IntituleProcedure,
    string Numero,
    string Titre,
    string Redacteur,
    string? Grade,
    string? Rio,
    string? Service,
    DateTimeOffset Date,
    string Modele,
    string? Personne,
    string? MisEnCause,
    IReadOnlyDictionary<string, string?> Champs,
    IReadOnlyList<ActeAgentDto> Agents,
    IReadOnlyList<QuestionReponseDto> QuestionsReponses,
    IReadOnlyList<string> Pieces,
    string? ValidePar,
    DateTimeOffset? ValideLe,
    string Organisation);

/// <summary>
/// Texte d'un acte, balisé pour le PDF (« # » titre, « ## » intertitre). Ce texte est figé à la validation (règle 10) :
/// une modification ultérieure de RH (RIO, grade) ne le change plus.
/// </summary>
public static class ActeRendu
{
    public static string Rendre(ActeRenduContexte c)
    {
        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(c.Titre.ToUpperInvariant());
        sb.AppendLine($"Procédure n° {c.NumeroProcedure} — {c.IntituleProcedure}");
        sb.AppendLine($"Acte n° {c.Numero}");
        sb.AppendLine(c.Organisation);
        sb.AppendLine();

        var variables = new Dictionary<string, string?>
        {
            ["annee"] = JudiciaireRegles.AnneeEnLettres(HeureLocale.Convertir(c.Date).Year),
            ["date"] = HeureLocale.Date(c.Date),
            ["heure"] = HeureLocale.Heure(c.Date),
            ["redacteur"] = c.Redacteur,
            ["grade"] = c.Grade ?? "grade non renseigné",
            ["rio"] = c.Rio ?? "non attribué",
            ["service"] = c.Service ?? "service non renseigné",
            ["procedure"] = c.NumeroProcedure
        };
        sb.AppendLine(JudiciaireRegles.RemplacerVariables(c.Modele, variables));

        foreach (var section in c.Type.Sections)
        {
            var bloc = new StringBuilder();
            if (section == c.Type.SectionPersonne && !string.IsNullOrWhiteSpace(c.Personne))
                bloc.AppendLine($"{c.Type.LibellePersonne ?? "Personne"} : {c.Personne}");
            if (section == c.Type.SectionMisEnCause && !string.IsNullOrWhiteSpace(c.MisEnCause))
                bloc.AppendLine($"{c.Type.LibelleMisEnCause ?? "Mis en cause"} : {c.MisEnCause}");
            if (section == c.Type.SectionAgents && c.Agents.Count > 0)
            {
                bloc.AppendLine("Agents :");
                foreach (var agent in c.Agents)
                    bloc.AppendLine("  - " + Agent(agent));
            }
            foreach (var champ in c.Type.Champs.Where(ch => ch.Section == section))
            {
                if (!c.Champs.TryGetValue(champ.Code, out var valeur) || string.IsNullOrWhiteSpace(valeur))
                    continue;
                if (champ.Type == ChampTypes.TexteLong || valeur.Contains('\n'))
                {
                    bloc.AppendLine(champ.Libelle + " :");
                    foreach (var ligne in valeur.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
                        bloc.AppendLine("    " + ligne);
                }
                else
                {
                    bloc.AppendLine($"{champ.Libelle} : {valeur}");
                }
            }
            if (section == c.Type.SectionQuestions && c.QuestionsReponses.Count > 0)
            {
                var n = 1;
                foreach (var qr in c.QuestionsReponses)
                {
                    bloc.AppendLine($"Question {n} : {qr.Question}");
                    bloc.AppendLine($"Réponse : {qr.Reponse}");
                    bloc.AppendLine();
                    n++;
                }
            }
            if (section == c.Type.SectionPieces && c.Pieces.Count > 0)
            {
                bloc.AppendLine("Pièces jointes :");
                foreach (var piece in c.Pieces)
                    bloc.AppendLine("  - " + piece);
            }
            if (bloc.Length == 0)
                continue;
            sb.Append("## ").AppendLine(section);
            sb.Append(bloc);
        }

        sb.AppendLine("## Clôture");
        var fin = c.ValideLe ?? c.Date;
        sb.AppendLine($"Dont acte, clos le {HeureLocale.Date(fin)} à {HeureLocale.Heure(fin)}.");
        sb.AppendLine($"Le rédacteur : {c.Redacteur}" + (c.Grade is null ? string.Empty : $", {c.Grade}") +
                      (c.Rio is null ? string.Empty : $", RIO {c.Rio}") + (c.Service is null ? string.Empty : $", {c.Service}"));
        if (c.ValidePar is not null && c.ValideLe is { } valide)
            sb.AppendLine($"Acte validé le {HeureLocale.DateHeure(valide)} par {c.ValidePar}.");
        else
            sb.AppendLine("PROJET — acte non validé, sans valeur procédurale.");
        sb.AppendLine("Document de simulation (Val-de-Marne Simulation) — données fictives.");
        return sb.ToString().TrimEnd();
    }

    public static string Agent(ActeAgentDto a)
    {
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(a.Grade)) details.Add(a.Grade);
        if (!string.IsNullOrWhiteSpace(a.Rio)) details.Add("RIO " + a.Rio);
        if (!string.IsNullOrWhiteSpace(a.Service)) details.Add(a.Service);
        return $"{a.Role} : {a.Libelle}" + (details.Count == 0 ? string.Empty : " (" + string.Join(", ", details) + ")");
    }

    /// <summary>Texte brut sans balises (indexation ANADOC).</summary>
    public static string SansBalises(string texte)
        => string.Join('\n', texte.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Select(l => l.StartsWith("## ", StringComparison.Ordinal) ? l[3..].ToUpperInvariant()
                : l.StartsWith("# ", StringComparison.Ordinal) ? l[2..] : l));
}
