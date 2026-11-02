using Microsoft.Extensions.Options;
using Saphir.Server.Data;
using Saphir.Shared.Contracts;

namespace Saphir.Server.Services;

/// <summary>Session SAPHIR d'un compte Discord : qui il est, ce qu'il peut faire, première connexion.</summary>
public sealed class SessionService(
    AgentStore agents,
    PermissionService droits,
    ParametresService parametres,
    JournalAcces journal,
    IOptionsMonitor<SaphirServerOptions> options)
{
    /// <summary>Une connexion n'est journalisée qu'une fois par période (le poste relit sa session souvent).</summary>
    private static readonly TimeSpan IntervalleJournal = TimeSpan.FromMinutes(10);

    public async Task<SessionInfoDto> LireAsync(SaphirUserDto user, CancellationToken ct)
    {
        var version = await parametres.VersionAffichageAsync(ct);
        var agent = await agents.TrouverParDiscordAsync(user.UserId, ct);
        if (agent is null)
            return new SessionInfoDto(null, true, user.RoleRh, [], [], version, null);

        if (!AgentStatuts.PeutSeConnecter(agent.Statut))
        {
            await journal.EcrireAsync(agent.Id, SocleRegles.LibelleAgent(agent), "Connexion", "SAPHIR", $"Refusée ({agent.Statut})");
            return new SessionInfoDto(agent, false, user.RoleRh, [], [], version, SocleRegles.MessageRefus(agent.Statut));
        }

        var precedente = await agents.NoterConnexionAsync(agent.Id, ct);
        if (precedente is null || DateTimeOffset.UtcNow - precedente.Value > IntervalleJournal)
            await journal.EcrireAsync(agent.Id, SocleRegles.LibelleAgent(agent), "Connexion", "SAPHIR", "Autorisée");

        var agentDroits = await droits.ObtenirAsync(agent.Id, ct);
        return new SessionInfoDto(agent, false, user.RoleRh, agentDroits.Profils, agentDroits.Codes, version, null);
    }

    /// <summary>
    /// Crée l'agent du compte Discord connecté (NOM en majuscules, Prénom capitalisé), avec le profil « agent »,
    /// plus « administrateur » pour les comptes de SAPHIR:AdminDiscordIds.
    /// </summary>
    public async Task<SessionInfoDto> PremiereConnexionAsync(SaphirUserDto user, PremiereConnexionInput input, CancellationToken ct)
    {
        if (!NomsFormat.EstValide(input.Nom))
            throw ApiException.Saisie("Nom invalide : 2 à 60 caractères, lettres, espaces, traits d'union ou apostrophes.");
        if (!NomsFormat.EstValide(input.Prenom))
            throw ApiException.Saisie("Prénom invalide : 2 à 60 caractères, lettres, espaces, traits d'union ou apostrophes.");

        var nom = NomsFormat.Nom(input.Nom);
        var prenom = NomsFormat.Prenom(input.Prenom);
        var profils = new List<string> { DefaultProfiles.Agent };
        if (options.CurrentValue.ObtenirAdministrateursDiscord().Contains(user.UserId, StringComparer.Ordinal))
            profils.Add(DefaultProfiles.AdministrateurTechnique);

        var agent = await agents.CreerAsync(user.UserId, nom, prenom, profils, ct);
        droits.Invalider();
        await journal.EcrireAsync(agent.Id, SocleRegles.LibelleAgent(agent), "Première connexion", "SAPHIR",
            $"Compte créé ({SocleRegles.Liste(profils)})");
        return await LireAsync(user, ct);
    }
}
