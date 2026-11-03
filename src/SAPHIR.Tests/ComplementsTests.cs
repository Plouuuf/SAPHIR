using Saphir.Server.Modules.Complements;
using Saphir.Shared.Contracts;
using Saphir.Shared.Contracts.Complements;

namespace Saphir.Tests;

/// <summary>Règles pures des applications complémentaires : MRZ, PVe, pièces jointes, références SAPHIR.</summary>
public class ComplementsTests
{
    private static readonly DateOnly Aujourdhui = new(2026, 11, 3);

    // ===== MRZ : chiffres de contrôle =====

    [Theory]
    [InlineData("L898902C3", 6)]
    [InlineData("740812", 2)]
    [InlineData("120415", 9)]
    [InlineData("ZE184226B<<<<<", 1)]
    [InlineData("<<<<<<<<<<<<<<", 0)]
    public void Chiffre_de_controle_7_3_1(string donnee, int attendu)
        => Assert.Equal(attendu, MrzParser.ChiffreControle(donnee));

    [Fact]
    public void Caractere_invalide_donne_moins_un()
        => Assert.Equal(-1, MrzParser.ChiffreControle("AB-12"));

    [Theory]
    [InlineData('0', 0)]
    [InlineData('9', 9)]
    [InlineData('A', 10)]
    [InlineData('Z', 35)]
    [InlineData('<', 0)]
    [InlineData('a', -1)]
    public void Valeur_des_caracteres(char c, int attendu)
        => Assert.Equal(attendu, MrzParser.Valeur(c));

    // ===== MRZ : passeport TD3 =====

    private const string Td3Specimen =
        "P<UTOERIKSSON<<ANNA<MARIA<<<<<<<<<<<<<<<<<<<\nL898902C36UTO7408122F1204159ZE184226B<<<<<10";

    private const string Td3Fictif =
        "P<FRADURAND<<JEAN<PIERRE<<<<<<<<<<<<<<<<<<<<\n24FV123451FRA8511032M3109203<<<<<<<<<<<<<<<0";

    [Fact]
    public void Passeport_specimen_OACI_valide()
    {
        var r = MrzParser.Analyser(Td3Specimen, Aujourdhui);
        Assert.True(r.Reconnu);
        Assert.True(r.ControlesOk, string.Join(" | ", r.Erreurs));
        Assert.Equal(MrzParser.FormatTd3, r.Format);
        Assert.Equal("P", r.TypeDocument);
        Assert.Equal("UTO", r.PaysEmetteur);
        Assert.Equal("ERIKSSON", r.Nom);
        Assert.Equal("ANNA MARIA", r.Prenoms);
        Assert.Equal("L898902C3", r.NumeroDocument);
        Assert.Equal(new DateOnly(1974, 8, 12), r.DateNaissance);
        Assert.Equal(new DateOnly(2012, 4, 15), r.DateExpiration);
        Assert.Equal("F", r.Sexe);
        Assert.Equal("ZE184226B", r.DonneesOptionnelles);
        Assert.Equal(5, r.Controles.Count);
    }

    [Fact]
    public void Passeport_fictif_francais_valide()
    {
        var r = MrzParser.Analyser(Td3Fictif, Aujourdhui);
        Assert.True(r.ControlesOk, string.Join(" | ", r.Erreurs));
        Assert.Equal("DURAND", r.Nom);
        Assert.Equal("JEAN PIERRE", r.Prenoms);
        Assert.Equal("FRA", r.Nationalite);
        Assert.Equal(new DateOnly(1985, 11, 3), r.DateNaissance);
        Assert.Equal(new DateOnly(2031, 9, 20), r.DateExpiration);
        Assert.Equal("M", r.Sexe);
        Assert.Null(r.DonneesOptionnelles);
    }

    [Fact]
    public void Passeport_chiffre_de_naissance_altere()
    {
        var altere = Td3Fictif.Replace("8511032M", "8511033M", StringComparison.Ordinal);
        var r = MrzParser.Analyser(altere, Aujourdhui);
        Assert.True(r.Reconnu);
        Assert.False(r.ControlesOk);
        Assert.Contains(r.Controles, c => c.Libelle == "Date de naissance" && !c.Ok && c.Attendu == "2" && c.Lu == "3");
    }

    [Fact]
    public void Passeport_numero_altere_echoue_au_controle_global()
    {
        var altere = Td3Fictif.Replace("24FV12345", "24FV12346", StringComparison.Ordinal);
        var r = MrzParser.Analyser(altere, Aujourdhui);
        Assert.False(r.ControlesOk);
        Assert.Contains(r.Controles, c => c.Libelle == "Numéro du document" && !c.Ok);
        Assert.Contains(r.Controles, c => c.Libelle == "Contrôle global" && !c.Ok);
    }

    [Fact]
    public void Passeport_colle_sur_une_ligne_et_minuscules()
    {
        var uneLigne = Td3Fictif.Replace("\n", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        var r = MrzParser.Analyser("  " + uneLigne + "  ", Aujourdhui);
        Assert.True(r.ControlesOk, string.Join(" | ", r.Erreurs));
        Assert.Equal(2, r.Lignes.Count);
    }

    // ===== MRZ : carte d'identité TD1 =====

    private const string Td1Specimen =
        "I<UTOD231458907<<<<<<<<<<<<<<<\n7408122F1204159UTO<<<<<<<<<<<6\nERIKSSON<<ANNA<MARIA<<<<<<<<<<";

    private const string Td1Fictif =
        "IDFRAX4RTBPFW46<<<<<<<<<<<<<<<\r\n9002155F3306149FRA<<<<<<<<<<<0\r\nMARTIN<<LEA<CHLOE<<<<<<<<<<<<<";

    [Fact]
    public void Carte_TD1_specimen_OACI_valide()
    {
        var r = MrzParser.Analyser(Td1Specimen, Aujourdhui);
        Assert.True(r.ControlesOk, string.Join(" | ", r.Erreurs));
        Assert.Equal(MrzParser.FormatTd1, r.Format);
        Assert.Equal("D23145890", r.NumeroDocument);
        Assert.Equal("ERIKSSON", r.Nom);
        Assert.Equal("ANNA MARIA", r.Prenoms);
    }

    [Fact]
    public void Carte_TD1_fictive_francaise_valide()
    {
        var r = MrzParser.Analyser(Td1Fictif, Aujourdhui);
        Assert.True(r.ControlesOk, string.Join(" | ", r.Erreurs));
        Assert.Equal("ID", r.TypeDocument);
        Assert.Equal("X4RTBPFW4", r.NumeroDocument);
        Assert.Equal("MARTIN", r.Nom);
        Assert.Equal("LEA CHLOE", r.Prenoms);
        Assert.Equal(new DateOnly(1990, 2, 15), r.DateNaissance);
        Assert.Equal(new DateOnly(2033, 6, 14), r.DateExpiration);
    }

    [Fact]
    public void Carte_TD1_controle_global_altere()
    {
        var altere = Td1Fictif.Replace("FRA<<<<<<<<<<<0", "FRA<<<<<<<<<<<7", StringComparison.Ordinal);
        var r = MrzParser.Analyser(altere, Aujourdhui);
        Assert.False(r.ControlesOk);
        Assert.Contains(r.Controles, c => c.Libelle == "Contrôle global" && !c.Ok);
        Assert.All(r.Controles.Where(c => c.Libelle != "Contrôle global"), c => Assert.True(c.Ok));
    }

    // ===== MRZ : ancienne CNI française =====

    private const string CniFictive =
        "IDFRABERNARD<<<<<<<<<<<<<<<<<<094021\n1505940123451LUCAS<<THEO<<<0203177M3";

    [Fact]
    public void Ancienne_CNI_fictive_valide()
    {
        var r = MrzParser.Analyser(CniFictive, Aujourdhui);
        Assert.True(r.ControlesOk, string.Join(" | ", r.Erreurs));
        Assert.Equal(MrzParser.FormatCniFr, r.Format);
        Assert.Equal("BERNARD", r.Nom);
        Assert.Equal("LUCAS, THEO", r.Prenoms);
        Assert.Equal("150594012345", r.NumeroDocument);
        Assert.Equal(new DateOnly(2002, 3, 17), r.DateNaissance);
        Assert.Equal("M", r.Sexe);
        Assert.Equal("FRA", r.Nationalite);
        Assert.Equal(new DateOnly(2030, 4, 30), r.DateExpiration);
    }

    [Fact]
    public void Ancienne_CNI_cle_finale_alteree()
    {
        var r = MrzParser.Analyser(CniFictive[..^1] + "4", Aujourdhui);
        Assert.False(r.ControlesOk);
        Assert.Contains(r.Controles, c => c.Libelle == "Contrôle global" && !c.Ok);
    }

    // ===== MRZ : saisies refusées =====

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("P<FRADURAND<<JEAN")]
    [InlineData("IDFRA X4RTBPFW46 / 9002155F")]
    public void Bandes_non_reconnues(string texte)
    {
        var r = MrzParser.Analyser(texte, Aujourdhui);
        Assert.False(r.Reconnu);
        Assert.False(r.ControlesOk);
        Assert.NotEmpty(r.Erreurs);
    }

    [Fact]
    public void Date_impossible_signalee()
    {
        // 31 février : la ligne garde des chiffres de contrôle cohérents mais la date est refusée.
        var l2 = "24FV123451FRA850231" + MrzParser.ChiffreControle("850231") + "M3109203<<<<<<<<<<<<<<<";
        var composite = l2[..10] + l2[13..20] + l2[21..43];
        var texte = "P<FRADURAND<<JEAN<PIERRE<<<<<<<<<<<<<<<<<<<<\n" + l2 + MrzParser.ChiffreControle(composite);
        var r = MrzParser.Analyser(texte, Aujourdhui);
        Assert.True(r.Reconnu);
        Assert.Null(r.DateNaissance);
        Assert.False(r.ControlesOk);
        Assert.Contains(r.Erreurs, e => e.Contains("Date de naissance invalide", StringComparison.Ordinal));
    }

    [Fact]
    public void Separation_des_noms()
    {
        Assert.Equal(("DE LA FONTAINE", "MARIE ANNE"), MrzParser.Noms("DE<LA<FONTAINE<<MARIE<ANNE<<<<<"));
        Assert.Equal(("DUPONT", string.Empty), MrzParser.Noms("DUPONT<<<<<<"));
    }

    // ===== PVe =====

    [Fact]
    public void Numero_d_avis_et_cle()
    {
        var numero = PveRegles.NumeroAvis(2026, 123);
        Assert.Equal(13, numero.Length);
        Assert.StartsWith("94260000123", numero, StringComparison.Ordinal);
        Assert.True(PveRegles.NumeroAvisValide(numero));
        Assert.Equal(97 - (94260000123L % 97), int.Parse(numero[11..], System.Globalization.CultureInfo.InvariantCulture));
        Assert.False(PveRegles.NumeroAvisValide(numero[..12] + ((numero[12] - '0' + 1) % 10)));
        Assert.NotEqual(PveRegles.NumeroAvis(2026, 1), PveRegles.NumeroAvis(2026, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => PveRegles.NumeroAvis(2026, 0));
    }

    private static InfractionInput Infraction(string natinf = "11300", string classe = "4", decimal forfait = 135m, decimal? minore = 90m,
        decimal? majore = 375m, int points = 2)
        => new(natinf, "Excès de vitesse", "Art. R413-14 C. route", "Vitesse", classe, forfait, minore, majore, points, true, 10);

    [Fact]
    public void Infraction_valide() => Assert.Null(PveRegles.ValiderInfraction(Infraction()));

    [Theory]
    [InlineData("12A4")]
    [InlineData("")]
    [InlineData("12345678901")]
    public void Natinf_invalide(string natinf) => Assert.NotNull(PveRegles.ValiderInfraction(Infraction(natinf: natinf)));

    [Fact]
    public void Montants_et_points_controles()
    {
        Assert.NotNull(PveRegles.ValiderInfraction(Infraction(classe: "6")));
        Assert.NotNull(PveRegles.ValiderInfraction(Infraction(minore: 150m)));
        Assert.NotNull(PveRegles.ValiderInfraction(Infraction(majore: 100m)));
        Assert.NotNull(PveRegles.ValiderInfraction(Infraction(points: 13)));
        Assert.NotNull(PveRegles.ValiderInfraction(Infraction(forfait: 0m)));
        Assert.Null(PveRegles.ValiderInfraction(Infraction(classe: "AFD", forfait: 500m, minore: 400m, majore: 1000m, points: 0)));
        Assert.Null(PveRegles.ValiderInfraction(Infraction(classe: "1", forfait: 11m, minore: null, majore: 33m, points: 0)));
    }

    [Fact]
    public void Referentiel_de_depart_coherent()
    {
        var infractions = ComplementsDonnees.Infractions;
        Assert.InRange(infractions.Count, 30, 40);
        Assert.Equal(infractions.Count, infractions.Select(i => i.Natinf).Distinct().Count());
        foreach (var i in infractions)
        {
            var input = new InfractionInput(i.Natinf, i.Libelle, i.TexteReference, i.Categorie, i.Classe, i.MontantForfaitaire,
                i.MontantMinore, i.MontantMajore, i.Points, true, 0);
            Assert.Null(PveRegles.ValiderInfraction(input));
        }
    }

    private static PveInput Pve(DateTimeOffset date, string? nom = "MARTIN", string? immat = "AB-123-CD", DateOnly? naissance = null)
        => new(1, date, "Avenue du Général-de-Gaulle", "Créteil", null, null, nom, "Paul", naissance, null, null, null, immat,
            "Peugeot", "208", "Gris", null, null, null, 0);

    [Fact]
    public void Saisie_pve()
    {
        var maintenant = new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
        Assert.Null(PveRegles.ValiderSaisie(Pve(maintenant.AddMinutes(-20)), maintenant));
        Assert.Null(PveRegles.ValiderSaisie(Pve(maintenant.AddHours(-1), nom: null), maintenant));
        Assert.Null(PveRegles.ValiderSaisie(Pve(maintenant.AddHours(-1), immat: null), maintenant));
        Assert.NotNull(PveRegles.ValiderSaisie(Pve(maintenant.AddHours(-1), nom: null, immat: null), maintenant));
        Assert.NotNull(PveRegles.ValiderSaisie(Pve(maintenant.AddHours(2)), maintenant));
        Assert.NotNull(PveRegles.ValiderSaisie(Pve(maintenant.AddDays(-31)), maintenant));
        Assert.NotNull(PveRegles.ValiderSaisie(Pve(maintenant.AddHours(-1), immat: "AB 123 CD !"), maintenant));
        Assert.NotNull(PveRegles.ValiderSaisie(Pve(maintenant.AddHours(-1), naissance: new DateOnly(2030, 1, 1)), maintenant));
        Assert.NotNull(PveRegles.ValiderSaisie(Pve(maintenant) with { Lieu = " " }, maintenant));
        Assert.NotNull(PveRegles.ValiderSaisie(Pve(maintenant) with { InfractionId = 0 }, maintenant));
    }

    [Theory]
    [InlineData(" ab-123-cd ", "AB-123-CD")]
    [InlineData("ab 123 cd", "AB123CD")]
    [InlineData("", null)]
    public void Immatriculation_normalisee(string saisie, string? attendu)
        => Assert.Equal(attendu, PveRegles.NormaliserImmatriculation(saisie));

    [Fact]
    public void Libelles_des_classes()
    {
        Assert.Equal("Contravention de 4e classe", PveRegles.LibelleClasse("4"));
        Assert.Equal("Contravention de 1re classe", PveRegles.LibelleClasse("1"));
        Assert.Equal("Amende forfaitaire délictuelle", PveRegles.LibelleClasse("AFD"));
    }

    // ===== Pièces jointes et références =====

    [Theory]
    [InlineData("rapport.PDF", true)]
    [InlineData("photo.jpeg", true)]
    [InlineData("script.exe", false)]
    [InlineData("archive.zip", false)]
    [InlineData("sans-extension", false)]
    public void Extensions_autorisees(string nom, bool attendu)
        => Assert.Equal(attendu, PiecesJointes.ExtensionAutorisee(nom));

    [Fact]
    public void Signatures_de_fichiers()
    {
        Assert.True(ComplementsOutils.SignatureCoherente("pdf", "%PDF-1.7"u8));
        Assert.False(ComplementsOutils.SignatureCoherente("pdf", "MZ\u0090"u8));
        Assert.True(ComplementsOutils.SignatureCoherente("png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A }));
        Assert.True(ComplementsOutils.SignatureCoherente("mp4", new byte[] { 0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', 0x6D }));
        Assert.True(ComplementsOutils.SignatureCoherente("webm", new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x01 }));
        Assert.False(ComplementsOutils.SignatureCoherente("mp4", new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x01, 0, 0, 0 }));
        Assert.True(ComplementsOutils.SignatureCoherente("txt", "Compte rendu"u8));
        Assert.False(ComplementsOutils.SignatureCoherente("txt", new byte[] { 0x41, 0x00, 0x42 }));
    }

    [Fact]
    public void Nom_de_fichier_nettoye()
    {
        Assert.Equal("photo.jpg", ComplementsOutils.NomFichier("../../etc/photo.jpg"));
        Assert.Equal("rapport.pdf", ComplementsOutils.NomFichier(@"C:\Users\agent\rapport.pdf"));
        Assert.Throws<Saphir.Server.Data.ApiException>(() => ComplementsOutils.NomFichier(".."));
    }

    [Theory]
    [InlineData("PER-000012", SaphirApps.Cheops)]
    [InlineData("pve-2026-000004", SaphirApps.Pve)]
    [InlineData("INT-2026-000031", SaphirApps.Pegase)]
    [InlineData("PROC-2026-000002", SaphirApps.Lrppn)]
    [InlineData("BC-2026-000001", SaphirApps.Bodycam)]
    [InlineData("XYZ-1", null)]
    public void Application_d_une_reference(string reference, string? attendu)
        => Assert.Equal(attendu, LiensSaphir.Application(reference));

    [Theory]
    [InlineData("PER-000012", true)]
    [InlineData("AB", false)]
    [InlineData("PER 000012", false)]
    [InlineData("PER-000012;DROP", false)]
    public void Validite_d_une_reference(string reference, bool attendu)
        => Assert.Equal(attendu, LiensSaphir.Valide(reference));

    [Fact]
    public void Donnees_de_depart_echappees()
    {
        Assert.Equal("'l''arrêt'", ComplementsMigrations.Texte("l'arrêt"));
        Assert.Equal("NULL", ComplementsMigrations.Texte(null));
        Assert.Throws<InvalidOperationException>(() => ComplementsMigrations.Texte("a\\b"));
    }

    [Fact]
    public void Migrations_dans_la_plage_du_module()
    {
        Assert.All(ComplementsModule.Migrations, m => Assert.InRange(m.Version, 800, 899));
        Assert.Equal(ComplementsModule.Migrations.Count, ComplementsModule.Migrations.Select(m => m.Version).Distinct().Count());
    }
}
