using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Saphir.Shared.Contracts;

namespace Saphir.Screenshots;

/// <summary>Faux serveur SAPHIR pour les captures : données fictives, aucune écriture.</summary>
public sealed class SampleApi : HttpMessageHandler
{
    public const string Version = "SAPHIR - v0.2.0.0";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 41, 0, TimeSpan.FromHours(1));

    public static readonly AgentDto Admin = new(1, "AG-000001", "560037927695024139", "DURAND", "Jean-Pierre",
        "1234567", "94000001", "Brigadier-chef", "CSP de Créteil", "BPS / BAC", "Chef de bord", AgentStatuts.Actif,
        Now.AddDays(-12), Now.AddHours(-2));

    private static readonly List<AgentDto> Agents =
    [
        Admin,
        new(2, "AG-000002", "301245778812340001", "GIRARD", "Valentin", "2045871", "94000002", "Commandant", "CSP de Créteil", "Direction", "Adjoint au chef de service", AgentStatuts.Actif, Now.AddDays(-11), Now.AddDays(-1)),
        new(3, "AG-000003", "301245778812340002", "TABOULOT", "Louis", "3301458", "94000003", "Major", "CSP de Créteil", "BPS / BAC", "Chef d'unité", AgentStatuts.Actif, Now.AddDays(-10), Now.AddDays(-3)),
        new(4, "AG-000004", "301245778812340003", "BORDINI", "Nolan", "4410256", "94000004", "Major", "CSP de Créteil", "CSI", "Chef d'unité", AgentStatuts.Actif, Now.AddDays(-9), Now.AddDays(-2)),
        new(5, "AG-000005", "301245778812340004", "MARTIN", "Léa", null, "94000005", null, null, null, null, AgentStatuts.ProfilACompleter, Now.AddHours(-5), Now.AddHours(-5)),
        new(6, "AG-000006", "301245778812340005", "NGUYEN", "Thomas", "5120447", "94000006", "Gardien de la paix", "CSP de Créteil", "BPS / BAC", "Équipier", AgentStatuts.Suspendu, Now.AddDays(-8), Now.AddDays(-1)),
    ];

    private static readonly Dictionary<long, string[]> AgentProfils = new()
    {
        [1] = [DefaultProfiles.AdministrateurTechnique, "chef-de-bord"],
        [2] = ["responsable-service", "opj"],
        [3] = ["chef-de-bord", "agent"],
        [4] = ["operateur-cic", "agent"],
        [5] = [],
        [6] = ["agent"],
    };

    public static SessionInfoDto Session(bool premiereConnexion = false) => new(
        premiereConnexion ? null : Admin, premiereConnexion, true,
        premiereConnexion ? Array.Empty<string>() : AgentProfils[1],
        premiereConnexion ? Array.Empty<string>() : Permissions.All.Select(p => p.Code).OrderBy(c => c, StringComparer.Ordinal).ToList(),
        Version, null);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');
        object? body = path switch
        {
            "api/version" => new { version = Version },
            "api/session" => Session(),
            "api/admin/profils" => Profils(),
            "api/admin/comptes" => Agents.Select(a => new CompteDto(a, AgentProfils[a.Id], a.MajLe)).ToList(),
            "api/admin/parametres" => Parametres(),
            "api/admin/historique" => Historique(),
            "api/admin/journal" => Journal(),
            _ => ModuleSample(request, path),
        };

        var response = body is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = JsonContent.Create(new { error = "Route d'exemple absente." }) }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body, body.GetType(), options: Json) };
        return Task.FromResult(response);
    }

    private static object? ModuleSample(HttpRequestMessage request, string path)
    {
        var method = request.Method.Method;
        var query = request.RequestUri!.Query;
        var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
        return RhCaptures.Sample(method, path, query, body)
               ?? OperationsCaptures.Sample(method, path, query, body)
               ?? JudiciaireCaptures.Sample(method, path, query, body)
               ?? ComplementsCaptures.Sample(method, path, query, body);
    }

    private static List<ProfilDto> Profils()
        => DefaultProfiles.All.Select((p, i) => new ProfilDto(i + 1, p.Code, p.Nom, p.Description, p.Permissions, true,
            AgentProfils.Values.Count(v => v.Contains(p.Code)))).ToList();

    private static List<ParametreDto> Parametres() =>
    [
        new(ParametresCles.Version, "0.2.0.0", "Version affichée", "Affichée « SAPHIR - v0.2.0.0 » sur l'ouverture de session et le bureau.", true),
        new(ParametresCles.NomOrganisation, "Commissariat central de Créteil", "Nom de l'organisation", null, true),
        new(ParametresCles.RioFormat, @"^\d{7}$", "Format du RIO", "Expression régulière : 7 chiffres.", true),
        new(ParametresCles.RioExemple, "1234567", "Exemple de RIO", "Montré aux agents RH lors de l'attribution.", true),
    ];

    private static List<HistoriqueDto> Historique() =>
    [
        new(6, Now.AddMinutes(-12), "DURAND Jean-Pierre", "agent", "AG-000006", "Statut", "statut", AgentStatuts.Actif, AgentStatuts.Suspendu, "Absence prolongée non justifiée"),
        new(5, Now.AddMinutes(-40), "DURAND Jean-Pierre", "agent", "AG-000004", "Profils", "profils", "agent", "operateur-cic, agent", null),
        new(4, Now.AddHours(-2), "DURAND Jean-Pierre", "parametre", ParametresCles.NomOrganisation, "Modification", "valeur", "SAPHIR", "Commissariat central de Créteil", null),
        new(3, Now.AddHours(-5), "MARTIN Léa", "agent", "AG-000005", "Création", null, null, null, "Première connexion"),
        new(2, Now.AddDays(-9), "BORDINI Nolan", "agent", "AG-000004", "Création", null, null, null, "Première connexion"),
    ];

    private static List<JournalAccesDto> Journal() =>
    [
        new(9, Now.AddMinutes(-2), "DURAND Jean-Pierre", "Ouverture de session", "SAPHIR", "Autorisé"),
        new(8, Now.AddMinutes(-15), "NGUYEN Thomas", "Ouverture de session", "SAPHIR", "Refusé : compte suspendu"),
        new(7, Now.AddMinutes(-31), "MARTIN Léa", "Accès", "Ressources humaines", "Refusé : rôle RH absent"),
        new(6, Now.AddHours(-1), "GIRARD Valentin", "Ouverture de session", "SAPHIR", "Autorisé"),
        new(5, Now.AddHours(-3), "Inconnu (Discord 4410…)", "Ouverture de session", "SAPHIR", "Refusé : rôle SAPHIR absent"),
    ];
}
