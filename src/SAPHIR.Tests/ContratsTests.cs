using Saphir.Shared.Contracts;

namespace Saphir.Tests;

public class NomsFormatTests
{
    [Theory]
    [InlineData("jean-pierre", "Jean-Pierre")]
    [InlineData("o'neil", "O'Neil")]
    [InlineData("  ÉLOÏSE ", "Éloïse")]
    [InlineData("marie claire", "Marie Claire")]
    [InlineData("jean-éric", "Jean-Éric")]
    public void Prenom_met_une_capitale_a_chaque_partie(string saisie, string attendu)
        => Assert.Equal(attendu, NomsFormat.Prenom(saisie));

    [Theory]
    [InlineData("dupont", "DUPONT")]
    [InlineData(" le goff ", "LE GOFF")]
    [InlineData("lefèvre-d'hérouville", "LEFÈVRE-D'HÉROUVILLE")]
    public void Nom_est_en_majuscules(string saisie, string attendu)
        => Assert.Equal(attendu, NomsFormat.Nom(saisie));

    [Theory]
    [InlineData("Jean-Pierre", true)]
    [InlineData("O'Neil", true)]
    [InlineData("Zoé", true)]
    [InlineData("Lefèvre d'Hérouville", true)]
    [InlineData("J", false)]
    [InlineData("Jean2", false)]
    [InlineData("Jean_Pierre", false)]
    [InlineData("   ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void EstValide(string? saisie, bool attendu)
        => Assert.Equal(attendu, NomsFormat.EstValide(saisie));

    [Fact]
    public void EstValide_refuse_plus_de_60_caracteres()
        => Assert.False(NomsFormat.EstValide(new string('a', 61)));
}

public class SaphirAppsTests
{
    [Fact]
    public void Rh_exige_le_role_discord_rh()
    {
        string[] permissions = [Permissions.RhConsulter];
        Assert.False(SaphirApps.CanOpen(SaphirApps.Rh, permissions, roleRh: false));
        Assert.True(SaphirApps.CanOpen(SaphirApps.Rh, permissions, roleRh: true));
    }

    [Fact]
    public void Rh_exige_aussi_une_permission_rh()
        => Assert.False(SaphirApps.CanOpen(SaphirApps.Rh, [Permissions.AdminComptes], roleRh: true));

    [Fact]
    public void Parametres_toujours_ouvert()
        => Assert.True(SaphirApps.CanOpen(SaphirApps.Parametres, [], roleRh: false));

    [Fact]
    public void Administration_selon_les_permissions_admin()
    {
        Assert.True(SaphirApps.CanOpen(SaphirApps.Administration, [Permissions.AdminJournaux], roleRh: false));
        Assert.False(SaphirApps.CanOpen(SaphirApps.Administration, [Permissions.McConsulter], roleRh: false));
    }

    [Fact]
    public void Main_courante_selon_son_module()
    {
        Assert.True(SaphirApps.CanOpen(SaphirApps.MainCourante, [Permissions.McConsulter], roleRh: false));
        Assert.False(SaphirApps.CanOpen(SaphirApps.MainCourante, [Permissions.PegaseConsulter], roleRh: false));
    }
}

public class HabilitationsTests
{
    [Fact]
    public void Onze_profils_par_defaut()
        => Assert.Equal(11, DefaultProfiles.All.Count);

    [Fact]
    public void Codes_de_profils_uniques()
        => Assert.Equal(DefaultProfiles.All.Count, DefaultProfiles.All.Select(p => p.Code).Distinct().Count());

    [Fact]
    public void Profils_agent_et_administrateur_presents()
    {
        Assert.Contains(DefaultProfiles.All, p => p.Code == DefaultProfiles.Agent);
        Assert.Contains(DefaultProfiles.All, p => p.Code == DefaultProfiles.AdministrateurTechnique);
    }

    [Fact]
    public void Toutes_les_permissions_des_profils_existent()
    {
        var inconnues = DefaultProfiles.All
            .SelectMany(p => p.Permissions)
            .Where(code => !Permissions.Exists(code))
            .ToList();
        Assert.Empty(inconnues);
    }

    [Fact]
    public void Pas_de_permission_en_double_dans_un_profil()
    {
        foreach (var profil in DefaultProfiles.All)
            Assert.Equal(profil.Permissions.Count, profil.Permissions.Distinct().Count());
    }

    [Fact]
    public void Catalogue_de_permissions_sans_doublon()
        => Assert.Equal(Permissions.All.Count, Permissions.All.Select(p => p.Code).Distinct().Count());

    [Fact]
    public void Administrateur_a_toutes_les_permissions()
    {
        var admin = DefaultProfiles.All.Single(p => p.Code == DefaultProfiles.AdministrateurTechnique);
        Assert.True(Permissions.All.Select(p => p.Code).ToHashSet().SetEquals(admin.Permissions));
    }

    [Fact]
    public void Statuts_bloquants()
    {
        Assert.True(AgentStatuts.PeutSeConnecter(AgentStatuts.Actif));
        Assert.True(AgentStatuts.PeutSeConnecter(AgentStatuts.ProfilACompleter));
        Assert.False(AgentStatuts.PeutSeConnecter(AgentStatuts.Suspendu));
        Assert.False(AgentStatuts.PeutSeConnecter(AgentStatuts.Inactif));
        Assert.False(AgentStatuts.PeutSeConnecter(AgentStatuts.Archive));
    }
}
