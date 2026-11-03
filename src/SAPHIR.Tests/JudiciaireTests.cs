using System.Text;
using Saphir.Server.Modules.Judiciaire;
using Saphir.Shared.Contracts.Judiciaire;

namespace Saphir.Tests;

public class JudiciaireTests
{
    [Theory]
    [InlineData(1, "PER-000001")]
    [InlineData(123456, "PER-123456")]
    public void Reference_personne_stable(long id, string attendu)
        => Assert.Equal(attendu, JudiciaireRegles.ReferencePersonne(id));

    [Fact]
    public void Numeros_procedure_acte_piece()
    {
        var procedure = JudiciaireRegles.NumeroProcedure(2026, 531);
        Assert.Equal("2026/000531", procedure);
        Assert.Equal("2026/000531-PV004", JudiciaireRegles.NumeroActe(procedure, 4));
        Assert.Equal("2026/000531-P012", JudiciaireRegles.NumeroPiece(procedure, 12));
    }

    [Theory]
    [InlineData("ab-123-cd", "AB123CD", "AB-123-CD")]
    [InlineData(" gh 482 kl ", "GH482KL", "GH-482-KL")]
    [InlineData("1234 ZX 94", "1234ZX94", "1234ZX94")]
    public void Immatriculation_normalisee(string saisie, string normalisee, string affichage)
    {
        Assert.Equal(normalisee, JudiciaireRegles.Normaliser(saisie));
        Assert.Equal(affichage, JudiciaireRegles.FormaterImmatriculation(saisie));
        Assert.True(JudiciaireRegles.ImmatriculationValide(saisie));
    }

    [Theory]
    [InlineData("14/03/1998", 1998, 3, 14)]
    [InlineData("1998-03-14", 1998, 3, 14)]
    public void Lecture_des_dates(string texte, int a, int m, int j)
        => Assert.Equal(new DateOnly(a, m, j), JudiciaireRegles.LireDate(texte));

    [Fact]
    public void Date_illisible_ou_vide()
    {
        Assert.Null(JudiciaireRegles.LireDate("32/13/2020"));
        Assert.Null(JudiciaireRegles.LireDate(""));
        Assert.True(JudiciaireRegles.HeureValide("19:52"));
        Assert.False(JudiciaireRegles.HeureValide("25:00"));
    }

    [Fact]
    public void Points_bornes_et_situation()
    {
        Assert.Equal(0, JudiciaireRegles.AppliquerPoints(4, -6));
        Assert.Equal(12, JudiciaireRegles.AppliquerPoints(10, 4));
        Assert.Equal(PermisSituations.Invalide, JudiciaireRegles.SituationApresPoints(PermisSituations.Valide, 0));
        Assert.Equal(PermisSituations.Valide, JudiciaireRegles.SituationApresPoints(PermisSituations.Invalide, 4));
        Assert.Equal(PermisSituations.Suspendu, JudiciaireRegles.SituationApresPoints(PermisSituations.Suspendu, 6));
    }

    [Fact]
    public void Couverture_assurance()
    {
        var jour = new DateOnly(2026, 11, 2);
        Assert.True(JudiciaireRegles.Couvert(AssuranceStatuts.Valide, new DateOnly(2026, 1, 1), null, jour));
        Assert.False(JudiciaireRegles.Couvert(AssuranceStatuts.Valide, new DateOnly(2025, 7, 1), new DateOnly(2026, 6, 30), jour));
        Assert.False(JudiciaireRegles.Couvert(AssuranceStatuts.Resilie, new DateOnly(2026, 1, 1), null, jour));
    }

    [Fact]
    public void Transitions_fpr()
    {
        Assert.Null(JudiciaireRegles.ControlerTransitionFpr(FprStatuts.Active, FprStatuts.Levee, "Interpellé"));
        Assert.NotNull(JudiciaireRegles.ControlerTransitionFpr(FprStatuts.Active, FprStatuts.Levee, null));
        Assert.NotNull(JudiciaireRegles.ControlerTransitionFpr(FprStatuts.Active, FprStatuts.Archivee, "x"));
        Assert.Null(JudiciaireRegles.ControlerTransitionFpr(FprStatuts.Levee, FprStatuts.Archivee, null));
        Assert.NotNull(JudiciaireRegles.ControlerTransitionFpr(FprStatuts.Archivee, FprStatuts.Active, "x"));
    }

    [Fact]
    public void Acte_valide_fige_et_procedure_archivee()
    {
        Assert.True(JudiciaireRegles.ActeModifiable(ActeStatuts.Brouillon));
        Assert.True(JudiciaireRegles.ActeModifiable(ActeStatuts.EnRelecture));
        Assert.False(JudiciaireRegles.ActeModifiable(ActeStatuts.Valide));
        Assert.NotNull(JudiciaireRegles.ControlerProcedureOuverte(ProcedureStatuts.Archivee));
        Assert.Null(JudiciaireRegles.ControlerProcedureOuverte(ProcedureStatuts.EnCours));
        Assert.NotNull(JudiciaireRegles.ControlerTransitionProcedure(ProcedureStatuts.EnCours, ProcedureStatuts.Archivee));
        Assert.Null(JudiciaireRegles.ControlerTransitionProcedure(ProcedureStatuts.Cloturee, ProcedureStatuts.Archivee));
    }

    [Fact]
    public void Mouvements_de_pieces()
    {
        Assert.Equal("Au greffe", JudiciaireRegles.StatutApresMouvement("Au service", "Dépôt au greffe"));
        Assert.Null(JudiciaireRegles.StatutApresMouvement("Au service", "Retour d'expertise"));
        Assert.Equal("Au service", JudiciaireRegles.StatutApresMouvement("En expertise", "Retour d'expertise"));
        Assert.Null(JudiciaireRegles.StatutApresMouvement("Détruite", "Transfert de service"));
    }

    [Fact]
    public void Controle_pvi_champs_obligatoires()
    {
        var pvi = ActesCatalogue.Trouver(ActesCatalogue.Pvi)!;
        var erreurs = JudiciaireRegles.ControlerActe(pvi, new Dictionary<string, string?>(), null);
        Assert.Contains(erreurs, e => e.Contains("Personne interpellée", StringComparison.Ordinal));
        Assert.Contains(erreurs, e => e.Contains("Chronologie", StringComparison.Ordinal));

        var champs = pvi.Champs.Where(c => c.Obligatoire).ToDictionary(c => c.Code, c => (string?)(c.Type switch
        {
            ChampTypes.Date => "02/11/2026",
            ChampTypes.Heure => "19:52",
            ChampTypes.Choix => c.Choix![0],
            _ => "Texte"
        }));
        Assert.Empty(JudiciaireRegles.ControlerActe(pvi, champs, 1));

        champs["c_heure"] = "7 h";
        Assert.Single(JudiciaireRegles.ControlerActe(pvi, champs, 1));
    }

    [Fact]
    public void Catalogue_coherent()
    {
        Assert.Equal(15, ActesCatalogue.All.Count);
        foreach (var type in ActesCatalogue.All)
        {
            Assert.Equal(type.Champs.Count, type.Champs.Select(c => c.Code).Distinct().Count());
            Assert.All(type.Champs, c => Assert.Contains(c.Section, type.Sections));
            foreach (var section in new[] { type.SectionPersonne, type.SectionMisEnCause, type.SectionAgents, type.SectionQuestions, type.SectionPieces })
                if (section is not null)
                    Assert.Contains(section, type.Sections);
        }
        Assert.Equal(new[] { "A — Personne interpellée", "B — Agents", "C — Circonstances", "D — Déroulement", "E — Transport", "F — Suites", "G — Annexes" },
            ActesCatalogue.Trouver(ActesCatalogue.Pvi)!.Sections);
    }

    [Fact]
    public void Nettoyage_des_champs_ignore_les_codes_inconnus()
    {
        var pvi = ActesCatalogue.Trouver(ActesCatalogue.Pvi)!;
        var nettoyes = JudiciaireRegles.NettoyerChamps(pvi, new Dictionary<string, string?> { ["id_nom"] = "  LEROY ", ["pirate"] = "x", ["c_lieu"] = " " });
        Assert.Single(nettoyes);
        Assert.Equal("LEROY", nettoyes["id_nom"]);
    }

    [Fact]
    public void Controle_des_fichiers()
    {
        var pdf = Convert.ToBase64String(Encoding.ASCII.GetBytes("%PDF-1.4 test"));
        var (contenu, mime, nom) = JudiciaireRegles.ControlerFichier("../../pv.pdf", pdf);
        Assert.Equal("application/pdf", mime);
        Assert.Equal("pv.pdf", nom);
        Assert.NotEmpty(contenu);
        Assert.Throws<ArgumentException>(() => JudiciaireRegles.ControlerFichier("script.exe", pdf));
        Assert.Throws<ArgumentException>(() => JudiciaireRegles.ControlerFichier("faux.png", pdf));
        Assert.Throws<ArgumentException>(() => JudiciaireRegles.ControlerFichier("vide.pdf", ""));
    }

    [Fact]
    public void Variables_et_annee_en_lettres()
    {
        Assert.Equal("deux mille vingt-six", JudiciaireRegles.AnneeEnLettres(2026));
        Assert.Equal("deux mille soixante-et-onze", JudiciaireRegles.AnneeEnLettres(2071));
        Assert.Equal("deux mille quatre-vingts", JudiciaireRegles.AnneeEnLettres(2080));
        Assert.Equal("Nous, DURAND, RIO —", JudiciaireRegles.RemplacerVariables("Nous, {redacteur}, RIO {rio}", new Dictionary<string, string?> { ["redacteur"] = "DURAND", ["rio"] = null }));
    }

    [Fact]
    public void Pdf_genere_valide()
    {
        var octets = new PdfSimple { EnTete = "Essai", PiedDePage = "Pied", Filigrane = "PROJET" }
            .Titre("PROCÈS-VERBAL D'INTERPELLATION")
            .Intertitre("A — Personne interpellée")
            .Paragraphe(string.Join(" ", Enumerable.Repeat("Texte accentué éèàç œ € — (parenthèses) \\", 400)))
            .Generer();
        var texte = Encoding.Latin1.GetString(octets);
        Assert.StartsWith("%PDF-1.4", texte);
        Assert.EndsWith("%%EOF\n", texte);
        Assert.Contains("/Type /Catalog", texte);
        Assert.Contains("/Count ", texte);
        // Plusieurs pages pour un long texte.
        Assert.True(texte.Split("/Type /Page ").Length > 2);
        // La table xref pointe sur le premier objet.
        var xref = int.Parse(texte[(texte.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0]);
        Assert.StartsWith("xref", texte[xref..]);
        var lignes = texte[xref..].Split('\n');
        var premier = int.Parse(lignes[3][..10]);
        Assert.StartsWith("1 0 obj", texte[premier..]);
    }

    [Fact]
    public void Rendu_acte_contient_identite_et_mention_projet()
    {
        var plainte = ActesCatalogue.Trouver(ActesCatalogue.Plainte)!;
        var texte = ActeRendu.Rendre(new ActeRenduContexte(plainte, "2026/000531", "Vol", "2026/000531-PV002", plainte.Libelle, "DURAND Jean-Pierre",
            "Brigadier-chef", "1234567", "CSP de Créteil", new DateTimeOffset(2026, 11, 2, 18, 0, 0, TimeSpan.Zero), plainte.ModeleParDefaut,
            "MOREAU Julie (PER-000003)", null, new Dictionary<string, string?> { ["p_recit"] = "Ligne 1\nLigne 2", ["p_nature"] = "Vol" },
            [new ActeAgentDto(1, "Rédacteur", "DURAND Jean-Pierre", "1234567", null, null)], [], [], null, null, "Commissariat de Créteil"));
        Assert.StartsWith("# PROCÈS-VERBAL DE PLAINTE", texte);
        Assert.Contains("Plaignant : MOREAU Julie (PER-000003)", texte);
        Assert.Contains("RIO 1234567", texte);
        Assert.Contains("PROJET", texte);
        Assert.Contains("    Ligne 2", texte);
        Assert.DoesNotContain("{", ActeRendu.SansBalises(texte));
    }
}
