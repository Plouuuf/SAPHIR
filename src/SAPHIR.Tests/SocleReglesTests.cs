using Saphir.Server;
using Saphir.Server.Data;
using Saphir.Shared.Contracts;

namespace Saphir.Tests;

public class SocleReglesTests
{
    [Theory]
    [InlineData("1234567", true)]
    [InlineData(" 7654321 ", true)]
    [InlineData("123456", false)]
    [InlineData("12345678", false)]
    [InlineData("12a4567", false)]
    [InlineData("", false)]
    public void Rio_format_par_defaut_sept_chiffres(string rio, bool attendu)
        => Assert.Equal(attendu, SocleRegles.RioValide(rio, SocleRegles.RioFormatParDefaut));

    [Fact]
    public void Exemple_par_defaut_respecte_le_format_par_defaut()
        => Assert.True(SocleRegles.RioValide(SocleRegles.RioExempleParDefaut, SocleRegles.RioFormatParDefaut));

    [Theory]
    [InlineData(1, "AG-000001")]
    [InlineData(123, "AG-000123")]
    [InlineData(1234567, "AG-1234567")]
    public void Reference_agent(long id, string attendu)
        => Assert.Equal(attendu, SocleRegles.ReferenceAgent(id));

    [Fact]
    public void Matricule_genere()
    {
        Assert.Equal("94000123", SocleRegles.Matricule(123));
        Assert.Equal("94000123-2", SocleRegles.Matricule(123, 2));
        Assert.NotEqual(SocleRegles.Matricule(1), SocleRegles.Matricule(2));
    }

    [Theory]
    [InlineData("0.2.0.0", true)]
    [InlineData("1.12.3.40", true)]
    [InlineData("1.2.3", false)]
    [InlineData("1.2.3.4.5", false)]
    [InlineData("v1.2.3.4", false)]
    [InlineData("", false)]
    public void Version_valide(string version, bool attendu)
        => Assert.Equal(attendu, SocleRegles.VersionValide(version));

    [Fact]
    public void Affichage_version()
        => Assert.Equal("SAPHIR - v1.0.0.0", SocleRegles.AffichageVersion(SocleRegles.VersionParDefaut));

    [Fact]
    public void Validation_des_parametres()
    {
        Assert.Null(SocleRegles.ValiderParametre(ParametresCles.Version, "0.3.0.0", null));
        Assert.NotNull(SocleRegles.ValiderParametre(ParametresCles.Version, "0.3", null));
        Assert.Null(SocleRegles.ValiderParametre(ParametresCles.RioFormat, @"^[A-Z]\d{6}$", null));
        Assert.NotNull(SocleRegles.ValiderParametre(ParametresCles.RioFormat, "^(\\d{7}$", null));
        Assert.NotNull(SocleRegles.ValiderParametre(ParametresCles.RioExemple, "12", SocleRegles.RioFormatParDefaut));
        Assert.Null(SocleRegles.ValiderParametre(ParametresCles.RioExemple, "7654321", SocleRegles.RioFormatParDefaut));
        Assert.NotNull(SocleRegles.ValiderParametre(ParametresCles.NomOrganisation, "  ", null));
    }

    [Fact]
    public void Parametres_par_defaut_valides()
    {
        foreach (var parametre in SocleRegles.ParametresParDefaut)
            Assert.Null(SocleRegles.ValiderParametre(parametre.Cle, parametre.Valeur, SocleRegles.RioFormatParDefaut));
    }

    [Theory]
    [InlineData("Chef d'équipe CIC", "chef-d-equipe-cic")]
    [InlineData("  Opérateur   radio ", "operateur-radio")]
    [InlineData("***", "profil")]
    public void Code_depuis_nom(string nom, string attendu)
        => Assert.Equal(attendu, SocleRegles.CodeDepuisNom(nom));

    [Fact]
    public void Code_avec_suffixe_reste_sous_64_caracteres()
    {
        var code = SocleRegles.CodeDepuisNom(new string('a', 100));
        Assert.Equal(64, code.Length);
        Assert.Equal("profil-3", SocleRegles.CodeAvecSuffixe("profil", 3));
        Assert.True(SocleRegles.CodeAvecSuffixe(code, 12).Length <= 64);
        Assert.EndsWith("-12", SocleRegles.CodeAvecSuffixe(code, 12));
    }

    [Fact]
    public void Echapper_like()
        => Assert.Equal("50\\% a\\_b \\\\", SocleRegles.EchapperLike("50% a_b \\"));

    [Theory]
    [InlineData(null, 200)]
    [InlineData(0, 200)]
    [InlineData(50, 50)]
    [InlineData(5000, 1000)]
    public void Limite(int? demande, int attendu)
        => Assert.Equal(attendu, SocleRegles.Limite(demande));

    [Fact]
    public void Message_de_refus_pour_chaque_statut_bloquant()
    {
        foreach (var statut in AgentStatuts.All.Where(s => !AgentStatuts.PeutSeConnecter(s)))
            Assert.StartsWith("Accès refusé", SocleRegles.MessageRefus(statut));
    }

    [Fact]
    public void Migrations_numerotees_sans_trou()
    {
        var versions = Migrations.All.Select(m => m.Version).ToList();
        Assert.Equal(Enumerable.Range(1, versions.Count), versions);
        Assert.All(Migrations.All, m => Assert.NotEmpty(m.Requetes));
        Assert.DoesNotContain(Migrations.All.SelectMany(m => m.Requetes), q => q.Contains("DROP ", StringComparison.OrdinalIgnoreCase));
    }
}
