using System.Text;
using Saphir.Server.Modules.Rh;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Rh;

namespace Saphir.Tests;

/// <summary>Règles pures du module RH (sans base de données).</summary>
public class RhTests
{
    // ===== Statuts =====

    [Fact]
    public void Profil_complet_exige_rio_grade_service_et_unite()
    {
        Assert.True(RhRegles.ProfilComplet("1234567", 1, 2, 3));
        Assert.False(RhRegles.ProfilComplet(null, 1, 2, 3));
        Assert.False(RhRegles.ProfilComplet(" ", 1, 2, 3));
        Assert.False(RhRegles.ProfilComplet("1234567", null, 2, 3));
        Assert.False(RhRegles.ProfilComplet("1234567", 1, null, 3));
        Assert.False(RhRegles.ProfilComplet("1234567", 1, 2, null));
    }

    [Theory]
    [InlineData(AgentStatuts.ProfilACompleter, true, AgentStatuts.Actif)]
    [InlineData(AgentStatuts.ProfilACompleter, false, AgentStatuts.ProfilACompleter)]
    [InlineData(AgentStatuts.Suspendu, true, AgentStatuts.Suspendu)]
    [InlineData(AgentStatuts.Archive, true, AgentStatuts.Archive)]
    [InlineData(AgentStatuts.Actif, true, AgentStatuts.Actif)]
    public void Passage_automatique_a_actif(string statut, bool complet, string attendu)
        => Assert.Equal(attendu, RhRegles.StatutApresMiseAJour(statut, complet));

    [Fact]
    public void Reactivation_selon_completude()
    {
        Assert.Equal(AgentStatuts.Actif, RhRegles.StatutReactivation(true));
        Assert.Equal(AgentStatuts.ProfilACompleter, RhRegles.StatutReactivation(false));
    }

    [Theory]
    [InlineData(AgentStatuts.Suspendu, true)]
    [InlineData(AgentStatuts.Inactif, true)]
    [InlineData(AgentStatuts.Archive, true)]
    [InlineData(AgentStatuts.Actif, false)]
    [InlineData(AgentStatuts.ProfilACompleter, false)]
    public void Motif_obligatoire_pour_bloquer(string statut, bool attendu)
        => Assert.Equal(attendu, RhRegles.MotifObligatoirePourStatut(statut));

    // ===== Identifiants =====

    [Theory]
    [InlineData("94000123", "94000123")]
    [InlineData(" ab-12 ", "AB-12")]
    [InlineData("A", null)]
    [InlineData("94 000", null)]
    [InlineData("-1234", null)]
    [InlineData("", null)]
    public void Matricule_saisi(string saisie, string? attendu)
        => Assert.Equal(attendu, RhRegles.NormaliserMatricule(saisie));

    [Theory]
    [InlineData("BPS / BAC", "BPS-BAC")]
    [InlineData("Chef d'unité", "CHEF-D-UNITE")]
    [InlineData("Élève gardien de la paix", "ELEVE-GARDIEN-DE-LA-PAIX")]
    public void Code_de_referentiel(string libelle, string attendu)
        => Assert.Equal(attendu, RhRegles.CodeReferentiel(libelle));

    [Fact]
    public void Code_de_referentiel_limite_a_32_caracteres()
        => Assert.True(RhRegles.CodeReferentiel("Document de changement de fonction administrative").Length <= 32);

    [Fact]
    public void Reference_document()
        => Assert.Equal("DOC-RH-000042", RhRegles.ReferenceDocument(42));

    // ===== Confidentialité =====

    [Fact]
    public void Confidentiel_rh_reserve_aux_permissions_documentaires()
    {
        Assert.True(RhRegles.PeutVoirConfidentiel(true, false));
        Assert.True(RhRegles.PeutVoirConfidentiel(false, true));
        Assert.False(RhRegles.PeutVoirConfidentiel(false, false));
    }

    [Theory]
    [InlineData("confidentiel rh", RhConfidentialites.Confidentiel)]
    [InlineData("Diffusion restreinte", RhConfidentialites.Restreint)]
    [InlineData("n'importe quoi", RhConfidentialites.Normal)]
    [InlineData(null, RhConfidentialites.Normal)]
    public void Confidentialite_normalisee(string? saisie, string attendu)
        => Assert.Equal(attendu, RhRegles.NormaliserConfidentialite(saisie));

    // ===== Modèles =====

    private static ValeursModele Valeurs() => new("DURAND", "Jean-Pierre", "1234567", "Brigadier-chef", "CSP de Créteil", "BPS / BAC",
        "Chef de bord", "94000001", "AG-000001", new DateOnly(2026, 11, 2), "TABOULOT Louis", "DOC-RH-000007", "Commissariat central de Créteil");

    [Fact]
    public void Variables_du_cahier_et_alias_courts()
    {
        var (texte, inconnues) = RhRegles.RemplirModele(
            "{{agent_grade}} {{NOM}} {{ prenom }} — RIO {{agent_rio}}, {{SERVICE}} / {{agent_unite}}, le {{DATE}} (n° {{numero_document}})",
            RhRegles.Variables(Valeurs()));
        Assert.Equal("Brigadier-chef DURAND Jean-Pierre — RIO 1234567, CSP de Créteil / BPS / BAC, le 02/11/2026 (n° DOC-RH-000007)", texte);
        Assert.Empty(inconnues);
    }

    [Fact]
    public void Variable_inconnue_laissee_et_signalee()
    {
        var (texte, inconnues) = RhRegles.RemplirModele("Bonjour {{agent_nom}} {{agent_age}} {{agent_age}}", RhRegles.Variables(Valeurs()));
        Assert.Equal("Bonjour DURAND {{agent_age}} {{agent_age}}", texte);
        Assert.Equal(new[] { "agent_age" }, inconnues);
    }

    [Fact]
    public void Valeur_absente_affichee_par_un_tiret()
    {
        var valeurs = Valeurs() with { Rio = null, Fonction = " " };
        var (texte, _) = RhRegles.RemplirModele("{{RIO}}|{{FONCTION}}", RhRegles.Variables(valeurs));
        Assert.Equal($"{RhRegles.ValeurAbsente}|{RhRegles.ValeurAbsente}", texte);
    }

    [Fact]
    public void Toutes_les_variables_publiees_sont_reconnues()
    {
        var variables = RhRegles.Variables(Valeurs());
        foreach (var v in RhModeleVariables.All)
        {
            Assert.True(variables.ContainsKey(v.Code), v.Code);
            Assert.True(variables.ContainsKey(v.Alias), v.Alias);
        }
    }

    [Fact]
    public void Modeles_de_depart_sans_variable_inconnue()
    {
        foreach (var modele in RhMigrations.Modeles)
        {
            var (_, inconnues) = RhRegles.RemplirModele(modele.Texte, RhRegles.Variables(Valeurs()));
            Assert.Empty(inconnues);
        }
    }

    // ===== Fichiers =====

    [Fact]
    public void Pdf_accepte_avec_signature()
    {
        var (controle, erreur) = RhRegles.ControlerFichier("note.pdf", Encoding.ASCII.GetBytes("%PDF-1.7 contenu"));
        Assert.Null(erreur);
        Assert.NotNull(controle);
        Assert.Equal("application/pdf", controle!.TypeMime);
        Assert.Equal(64, controle.Sha256.Length);
    }

    [Fact]
    public void Extension_et_contenu_incoherents_refuses()
    {
        var (controle, erreur) = RhRegles.ControlerFichier("photo.png", Encoding.ASCII.GetBytes("%PDF-1.7"));
        Assert.Null(controle);
        Assert.NotNull(erreur);
    }

    [Theory]
    [InlineData("programme.exe")]
    [InlineData("script.js")]
    [InlineData("sans-extension")]
    public void Extension_interdite(string nom)
        => Assert.NotNull(RhRegles.ControlerFichier(nom, [1, 2, 3]).Erreur);

    [Fact]
    public void Fichier_vide_ou_trop_gros_refuse()
    {
        Assert.NotNull(RhRegles.ControlerFichier("a.txt", []).Erreur);
        Assert.NotNull(RhRegles.ControlerFichier("a.txt", new byte[RhFichiers.TailleMax + 1]).Erreur);
    }

    [Fact]
    public void Images_et_docx_reconnus()
    {
        Assert.Null(RhRegles.ControlerFichier("a.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0]).Erreur);
        Assert.Null(RhRegles.ControlerFichier("a.JPG", [0xFF, 0xD8, 0xFF, 0xE0]).Erreur);
        Assert.Null(RhRegles.ControlerFichier("a.docx", [0x50, 0x4B, 0x03, 0x04, 0]).Erreur);
        Assert.Null(RhRegles.ControlerFichier("a.txt", Encoding.UTF8.GetBytes("Note de service — été")).Erreur);
        Assert.NotNull(RhRegles.ControlerFichier("a.txt", [0x41, 0x00, 0x42]).Erreur);
    }

    [Theory]
    [InlineData("..\\..\\secret.pdf", "secret.pdf")]
    [InlineData("/etc/passwd.pdf", "passwd.pdf")]
    [InlineData("note:de|service?.pdf", "note_de_service_.pdf")]
    [InlineData("   ", "document")]
    public void Nom_de_fichier_nettoye(string nom, string attendu)
        => Assert.Equal(attendu, RhRegles.NettoyerNomFichier(nom));

    [Fact]
    public void Nom_pdf_genere()
        => Assert.Equal("DOC-RH-000001 - Attestation.pdf", RhRegles.NomFichierPdf("Attestation", "DOC-RH-000001"));

    // ===== PDF =====

    [Fact]
    public void Pdf_genere_valide_et_accepte_par_le_controle()
    {
        var pdf = RhPdf.Generer("Commissariat central de Créteil", "Attestation (test)", "Ligne 1\nLigne é à ç € — « guillemets »\n\nFin", "DOC-RH-000001");
        var texte = Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF-1.4", texte);
        Assert.Contains("%%EOF", texte);
        Assert.Contains("/Type /Catalog", texte);
        Assert.Contains("Attestation \\(test\\)", texte);
        Assert.Null(RhRegles.ControlerFichier("a.pdf", pdf).Erreur);
    }

    [Fact]
    public void Pdf_long_sur_plusieurs_pages()
    {
        var texte = string.Join("\n", Enumerable.Range(1, 200).Select(i => $"Ligne {i}"));
        var pdf = Encoding.Latin1.GetString(RhPdf.Generer("Org", "Titre", texte, "REF"));
        Assert.Contains("/Count 5", pdf);
    }

    [Fact]
    public void Coupure_des_lignes_longues()
    {
        var lignes = RhPdf.Couper(string.Join(" ", Enumerable.Repeat("mot", 60)));
        Assert.True(lignes.Count > 1);
        Assert.All(lignes, l => Assert.True(l.Length <= 92));
    }

    // ===== Données de départ =====

    [Fact]
    public void Grades_de_depart_complets_et_uniques()
    {
        Assert.Equal(16, RhMigrations.Grades.Count);
        Assert.Equal(RhMigrations.Grades.Count, RhMigrations.Grades.Select(g => g.Abrege).Distinct().Count());
        Assert.Contains(RhMigrations.Grades, g => g.Abrege == "GPX" && g.Libelle == "Gardien de la paix");
        Assert.Contains(RhMigrations.Grades, g => g.Abrege == "RULP" && g.Libelle == "Major RULP");
        Assert.All(RhMigrations.Grades, g => Assert.Contains(g.Categorie, RhGradeCategories.All));
    }

    [Fact]
    public void Unites_de_creteil()
        => Assert.Equal(new[] { "Direction", "BPS / BAC", "CSI", "BMU", "Accueil", "CIC" }, RhMigrations.UnitesCreteil.Select(u => u.Libelle).ToArray());

    [Fact]
    public void Codes_de_depart_uniques_et_courts()
    {
        var fonctions = RhMigrations.Fonctions.Select(f => RhRegles.CodeReferentiel(f.Libelle)).ToList();
        Assert.Equal(12, fonctions.Count);
        Assert.Equal(fonctions.Count, fonctions.Distinct().Count());
        var categories = RhMigrations.Categories.Select(c => RhRegles.CodeReferentiel(c.Libelle)).ToList();
        Assert.Equal(categories.Count, categories.Distinct().Count());
        var modeles = RhMigrations.Modeles.Select(m => RhRegles.CodeReferentiel(m.Type)).ToList();
        Assert.Equal(modeles.Count, modeles.Distinct().Count());
        Assert.All(RhMigrations.Modeles, m => Assert.Contains(m.Categorie, RhMigrations.Categories.Select(c => c.Libelle)));
    }

    [Fact]
    public void Migrations_dans_la_plage_rh()
        => Assert.All(RhModule.Migrations, m => Assert.InRange(m.Version, 100, 199));

    [Fact]
    public void Applications_accessibles_depuis_les_permissions()
    {
        var apps = Saphir.Server.Modules.Rh.RhStore.ApplicationsAccessibles([Permissions.RhConsulter, Permissions.PegaseConsulter]);
        Assert.Contains("PÉGASE", apps);
        Assert.Contains(apps, a => a.StartsWith("RESSOURCES HUMAINES", StringComparison.Ordinal));
        Assert.Contains("Centre de paramètres", apps);
        Assert.DoesNotContain("LRPPN", apps);
    }
}
