using Saphir.Server.Modules.Judiciaire;
using Saphir.Shared.Contracts.Judiciaire;

namespace Saphir.Tests;

/// <summary>Règles pures de la déclaration d'identité en jeu (ressource FiveM saphir-identite).</summary>
public class CitoyenTests
{
    private static readonly DateOnly Jour = new(2026, 11, 3);

    private static CitoyenIdentiteInput Identite(string nom = "Martin", string prenom = "léa", string? naissance = "14/03/1998",
        string? sexe = "f", int? taille = 172, string? corpulence = "mince", string? telephone = "06 12 34 56 78")
        => new("560037927695024139", "ABC12345", "Lea", nom, prenom, naissance, "Créteil", sexe, "Française", "3 rue de Paris, Créteil",
            telephone, "Infirmière", taille, corpulence, "Tatouage poignet gauche");

    [Theory]
    [InlineData(null, true)]
    [InlineData("oui", true)]
    [InlineData(" OUI ", true)]
    [InlineData("non", false)]
    [InlineData("désactivé", false)]
    public void Portail_ouvert_seulement_si_oui(string? valeur, bool attendu)
        => Assert.Equal(attendu, CitoyenRegles.PortailActif(valeur));

    [Theory]
    [InlineData("5", 5)]
    [InlineData("2", 2)]
    [InlineData("0", 5)]
    [InlineData("abc", 5)]
    [InlineData(null, 5)]
    [InlineData("51", 5)]
    public void Maximum_de_personnages(string? valeur, int attendu)
        => Assert.Equal(attendu, CitoyenRegles.Maximum(valeur));

    [Theory]
    [InlineData("560037927695024139", true)]
    [InlineData("12345", false)]
    [InlineData("56003792769502413a", false)]
    [InlineData(null, false)]
    public void Identifiant_discord(string? id, bool attendu) => Assert.Equal(attendu, CitoyenRegles.DiscordValide(id));

    [Theory]
    [InlineData("ABC12345", true)]
    [InlineData("char1:1a2b3c4d5e", true)]
    [InlineData("ab", false)]
    [InlineData("avec espace", false)]
    public void Identifiant_personnage(string id, bool attendu) => Assert.Equal(attendu, CitoyenRegles.PersonnageValide(id));

    [Fact]
    public void Auteur_historique_joueur()
    {
        Assert.Equal("Joueur (Discord Lea, 560037927695024139)", CitoyenRegles.Auteur("560037927695024139", " Lea "));
        Assert.Equal("Joueur (Discord 560037927695024139)", CitoyenRegles.Auteur("560037927695024139", null));
    }

    [Theory]
    [InlineData("m", "M")]
    [InlineData("0", "M")]
    [InlineData("1", "F")]
    [InlineData("Femme", "F")]
    [InlineData("x", "X")]
    [InlineData(null, null)]
    public void Sexe_normalise(string? saisie, string? attendu) => Assert.Equal(attendu, CitoyenRegles.Sexe(saisie));

    [Fact]
    public void Sexe_illisible_refuse() => Assert.Throws<ArgumentException>(() => CitoyenRegles.Sexe("z"));

    [Fact]
    public void Identite_valide() => Assert.Empty(CitoyenRegles.ControlerIdentite(Identite(), Jour));

    [Fact]
    public void Identite_invalide_messages()
    {
        Assert.NotEmpty(CitoyenRegles.ControlerIdentite(Identite(nom: "X"), Jour));
        Assert.NotEmpty(CitoyenRegles.ControlerIdentite(Identite(prenom: "J3an"), Jour));
        Assert.NotEmpty(CitoyenRegles.ControlerIdentite(Identite(naissance: null), Jour));
        Assert.NotEmpty(CitoyenRegles.ControlerIdentite(Identite(naissance: "01/01/2025"), Jour));
        Assert.NotEmpty(CitoyenRegles.ControlerIdentite(Identite(naissance: "31/02/1990"), Jour));
        Assert.NotEmpty(CitoyenRegles.ControlerIdentite(Identite(sexe: "?"), Jour));
        Assert.NotEmpty(CitoyenRegles.ControlerIdentite(Identite(taille: 40), Jour));
        Assert.NotEmpty(CitoyenRegles.ControlerIdentite(Identite(corpulence: "énorme"), Jour));
        Assert.NotEmpty(CitoyenRegles.ControlerIdentite(Identite(telephone: "appelle-moi"), Jour));
    }

    [Fact]
    public void Date_ESX_et_QBCore_acceptees()
    {
        Assert.Empty(CitoyenRegles.ControlerIdentite(Identite(naissance: "1998-03-14"), Jour));
        Assert.Empty(CitoyenRegles.ControlerIdentite(Identite(naissance: "14/3/1998"), Jour));
    }

    [Fact]
    public void Signalement_compose()
    {
        Assert.Equal("1,80 m — corpulence mince — tatouage", CitoyenRegles.ComposerSignalement(180, "Mince", "tatouage"));
        Assert.Equal("corpulence forte", CitoyenRegles.ComposerSignalement(null, "Forte", "  "));
        Assert.Null(CitoyenRegles.ComposerSignalement(null, null, null));
    }

    [Fact]
    public void Nettoyage_texte()
    {
        Assert.Equal("a b c", CitoyenRegles.Nettoyer("  a \n b\t\tc ", 50));
        Assert.Equal("abc", CitoyenRegles.Nettoyer("abcdef", 3));
        Assert.Null(CitoyenRegles.Nettoyer(" \r\n ", 10));
    }

    [Fact]
    public void Identite_figee_changements_detectes()
    {
        var avant = ("MARTIN", "Léa", (DateOnly?)new DateOnly(1998, 3, 14), (string?)"Créteil");
        Assert.Empty(CitoyenRegles.ChangementsIdentite(avant, avant));
        Assert.Empty(CitoyenRegles.ChangementsIdentite(avant, ("MARTIN", "Léa", new DateOnly(1998, 3, 14), null)));
        Assert.Equal(["NOM", "date de naissance"],
            CitoyenRegles.ChangementsIdentite(avant, ("DUPONT", "Léa", new DateOnly(1998, 3, 15), "Créteil")));
        Assert.Equal(["prénom(s)", "lieu de naissance"],
            CitoyenRegles.ChangementsIdentite(avant, ("MARTIN", "Léa Marie", new DateOnly(1998, 3, 14), "Vitry")));
    }

    [Fact]
    public void Permis_probatoire_six_points_moins_de_trois_ans()
    {
        Assert.Equal((6, PermisSituations.Probatoire), CitoyenRegles.PermisInitial(new DateOnly(2025, 1, 10), Jour));
        Assert.Equal((6, PermisSituations.Probatoire), CitoyenRegles.PermisInitial(new DateOnly(2023, 11, 4), Jour));
        Assert.Equal((12, PermisSituations.Valide), CitoyenRegles.PermisInitial(new DateOnly(2023, 11, 3), Jour));
        Assert.Equal((12, PermisSituations.Valide), CitoyenRegles.PermisInitial(new DateOnly(2010, 6, 1), Jour));
    }

    [Fact]
    public void Categories_permis_normalisees()
    {
        Assert.Equal("A2, B", CitoyenRegles.NormaliserCategories(["b", "A2", "B"]));
        Assert.Equal("A, B, C", CitoyenRegles.NormaliserCategories(["C, a; B"]));
        Assert.Throws<ArgumentException>(() => CitoyenRegles.NormaliserCategories(["Z"]));
        Assert.Throws<ArgumentException>(() => CitoyenRegles.NormaliserCategories([]));
    }

    [Fact]
    public void Obtention_du_permis_controlee()
    {
        var naissance = new DateOnly(1998, 3, 14);
        Assert.Null(CitoyenRegles.ControlerObtention(new DateOnly(2017, 5, 2), naissance, Jour));
        Assert.NotNull(CitoyenRegles.ControlerObtention(null, naissance, Jour));
        Assert.NotNull(CitoyenRegles.ControlerObtention(new DateOnly(2027, 1, 1), naissance, Jour));
        Assert.NotNull(CitoyenRegles.ControlerObtention(new DateOnly(2005, 1, 1), naissance, Jour));
    }

    [Fact]
    public void Assurance_facultative_mais_complete()
    {
        Assert.Null(CitoyenRegles.ControlerAssurance(null, null, null, true));
        Assert.Null(CitoyenRegles.ControlerAssurance("MAIF", "C-1", new DateOnly(2027, 1, 1), true));
        Assert.NotNull(CitoyenRegles.ControlerAssurance("MAIF", null, null, true));
        Assert.NotNull(CitoyenRegles.ControlerAssurance("MAIF", "C-1", null, false));
    }

    [Fact]
    public void Plaque_disponible_seulement_sans_titulaire_ou_au_meme()
    {
        Assert.True(CitoyenRegles.PlaqueDisponible(null, null, 7));
        Assert.True(CitoyenRegles.PlaqueDisponible(7, null, 7));
        Assert.False(CitoyenRegles.PlaqueDisponible(8, null, 7));
        Assert.False(CitoyenRegles.PlaqueDisponible(null, "SARL Transports", 7));
        Assert.True(CitoyenRegles.PlaqueValide("ABC 123"));
        Assert.False(CitoyenRegles.PlaqueValide("-"));
    }
}
