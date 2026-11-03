using System.Text.Json;
using Saphir.Server.Modules.Operations;
using Saphir.Shared.Contracts.Operations;

namespace Saphir.Tests;

/// <summary>Règles pures du module Opérations (MAIN COURANTE, PÉGASE, GÉOOPS).</summary>
public class OperationsTests
{
    // ----- Numérotation -----

    [Theory]
    [InlineData("INT", 42, "INT-2026-000042")]
    [InlineData("APL", 1, "APL-2026-000001")]
    [InlineData("MC", 1234567, "MC-2026-1234567")]
    public void Numero_lisible(string prefixe, long id, string attendu)
        => Assert.Equal(attendu, OperationsRegles.Numero(prefixe, new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero), id));

    // ----- Statuts des interventions -----

    [Fact]
    public void Statut_initial_selon_qualification()
    {
        Assert.Equal(InterventionStatuts.AQualifier, OperationsRegles.StatutInitial(null, true));
        Assert.Equal(InterventionStatuts.EnAttente, OperationsRegles.StatutInitial("Vol", true));
        Assert.Equal(InterventionStatuts.AQualifier, OperationsRegles.StatutInitial("Vol", false));
    }

    [Fact]
    public void Annulation_exige_une_justification()
    {
        Assert.NotNull(OperationsRegles.ControlerStatutIntervention(InterventionStatuts.EnCours, InterventionStatuts.Annulee, null, false));
        Assert.Null(OperationsRegles.ControlerStatutIntervention(InterventionStatuts.EnCours, InterventionStatuts.Annulee, "Doublon", false));
    }

    [Fact]
    public void Cloture_sans_compte_rendu_exige_un_motif()
    {
        Assert.NotNull(OperationsRegles.ControlerStatutIntervention(InterventionStatuts.CompteRenduAttendu, InterventionStatuts.Cloturee, null, false));
        Assert.Null(OperationsRegles.ControlerStatutIntervention(InterventionStatuts.CompteRenduAttendu, InterventionStatuts.Cloturee, null, true));
        Assert.Null(OperationsRegles.ControlerStatutIntervention(InterventionStatuts.CompteRenduAttendu, InterventionStatuts.Cloturee, "Fausse alerte", false));
    }

    [Fact]
    public void Intervention_terminee_figee()
    {
        Assert.NotNull(OperationsRegles.ControlerStatutIntervention(InterventionStatuts.Cloturee, InterventionStatuts.EnCours, "x", true));
        Assert.NotNull(OperationsRegles.ControlerStatutIntervention(InterventionStatuts.Annulee, InterventionStatuts.Cloturee, "x", true));
        Assert.NotNull(OperationsRegles.ControlerStatutIntervention(InterventionStatuts.EnCours, "Inventé", null, true));
    }

    [Fact]
    public void Statut_deduit_des_equipages()
    {
        // Premier engagement : moyens affectés.
        Assert.Equal(InterventionStatuts.MoyensAffectes,
            OperationsRegles.StatutDeduit(InterventionStatuts.EnAttente, [EquipageStatuts.Affecte], false));
        // Un équipage en route : en cours.
        Assert.Equal(InterventionStatuts.EnCours,
            OperationsRegles.StatutDeduit(InterventionStatuts.MoyensAffectes, [EquipageStatuts.Affecte, EquipageStatuts.EnRoute], true));
        // Plus d'engagement après passage sur le terrain : compte rendu attendu.
        Assert.Equal(InterventionStatuts.CompteRenduAttendu, OperationsRegles.StatutDeduit(InterventionStatuts.EnCours, [], true));
        // Désengagement avant d'être allé sur place : retour en attente.
        Assert.Equal(InterventionStatuts.EnAttente, OperationsRegles.StatutDeduit(InterventionStatuts.MoyensAffectes, [], false));
        // Rien ne change sur une intervention close.
        Assert.Null(OperationsRegles.StatutDeduit(InterventionStatuts.Cloturee, [EquipageStatuts.SurPlace], true));
        Assert.Null(OperationsRegles.StatutDeduit(InterventionStatuts.EnCours, [EquipageStatuts.SurPlace], true));
    }

    [Fact]
    public void Statuts_equipage_independants()
    {
        Assert.True(EquipageStatuts.PeutEtreEngage(EquipageStatuts.Disponible));
        Assert.True(EquipageStatuts.PeutEtreEngage(EquipageStatuts.MissionTerminee));
        Assert.False(EquipageStatuts.PeutEtreEngage(EquipageStatuts.SurPlace));
        Assert.False(EquipageStatuts.PeutEtreEngage(EquipageStatuts.HorsService));
        Assert.True(EquipageStatuts.EstEngage(EquipageStatuts.Transport));
        Assert.False(EquipageStatuts.EstEngage(EquipageStatuts.Indisponible));
        Assert.Equal(9, EquipageStatuts.All.Count);
        Assert.Equal(7, InterventionStatuts.All.Count);
    }

    [Fact]
    public void Journal_pour_statut()
    {
        Assert.Equal(JournalTypes.ArriveeSurPlace, OperationsRegles.TypeJournalPourStatut(EquipageStatuts.SurPlace));
        Assert.Equal(JournalTypes.RetourMission, OperationsRegles.TypeJournalPourStatut(EquipageStatuts.MissionTerminee));
        Assert.Equal(JournalTypes.StatutEquipage, OperationsRegles.TypeJournalPourStatut(EquipageStatuts.EnRoute));
    }

    // ----- Équipages -----

    [Fact]
    public void Composition_d_equipage()
    {
        Assert.NotNull(OperationsRegles.ControlerMembres([]));
        Assert.NotNull(OperationsRegles.ControlerMembres([new EquipageMembreInput(1, null, "Équipier")]));
        Assert.NotNull(OperationsRegles.ControlerMembres([new EquipageMembreInput(1, null, "Chef de bord"), new EquipageMembreInput(2, null, "Chef de bord")]));
        Assert.NotNull(OperationsRegles.ControlerMembres([new EquipageMembreInput(1, null, "Chef de bord"), new EquipageMembreInput(1, null, "Conducteur")]));
        Assert.NotNull(OperationsRegles.ControlerMembres([new EquipageMembreInput(1, null, "Pilote")]));
        Assert.Null(OperationsRegles.ControlerMembres([new EquipageMembreInput(1, null, "Chef de bord"), new EquipageMembreInput(2, null, "Conducteur")]));
    }

    [Theory]
    [InlineData("PS 12", true)]
    [InlineData("BAC-94", true)]
    [InlineData("TV/3", true)]
    [InlineData("P", false)]
    [InlineData("", false)]
    [InlineData("PS 12 ; DROP", false)]
    public void Indicatif_radio(string indicatif, bool attendu) => Assert.Equal(attendu, OperationsRegles.IndicatifValide(indicatif));

    // ----- Véhicules -----

    [Theory]
    [InlineData("ab123cd", "AB-123-CD")]
    [InlineData(" AB-123-cd ", "AB-123-CD")]
    [InlineData("1234 ZX 94", "1234ZX94")]
    public void Immatriculation_normalisee(string saisie, string attendu) => Assert.Equal(attendu, OperationsRegles.NormaliserImmatriculation(saisie));

    [Fact]
    public void Kilometrage_coherent()
    {
        Assert.Null(OperationsRegles.ControlerKilometrage(12000, 12000));
        Assert.Null(OperationsRegles.ControlerKilometrage(12000, 12085));
        Assert.NotNull(OperationsRegles.ControlerKilometrage(12000, 11999));
        Assert.NotNull(OperationsRegles.ControlerKilometrage(12000, 20000));
        Assert.NotNull(OperationsRegles.ControlerKilometrage(0, -1));
    }

    // ----- Mentions -----

    [Fact]
    public void Mention_validee_figee()
    {
        Assert.True(OperationsRegles.MentionModifiable(MentionStatuts.Brouillon));
        Assert.False(OperationsRegles.MentionModifiable(MentionStatuts.Validee));
    }

    // ----- Discord -----

    [Theory]
    [InlineData("https://discord.com/api/webhooks/123/abc", true)]
    [InlineData("https://discordapp.com/api/webhooks/123/abc", true)]
    [InlineData("http://discord.com/api/webhooks/123/abc", false)]
    [InlineData("https://exemple.fr/api/webhooks/123/abc", false)]
    [InlineData("désactivé", false)]
    [InlineData("", false)]
    public void Webhook_discord(string url, bool attendu) => Assert.Equal(attendu, OperationsRegles.WebhookValide(url));

    [Fact]
    public void Message_discord_sans_donnees_personnelles()
    {
        var json = OperationsRegles.MessageDiscord("INT-2026-000042", new DateTimeOffset(2026, 11, 2, 21, 5, 0, TimeSpan.Zero),
            "Violences intrafamiliales", "Créteil", "P1", 2, InterventionStatuts.EnAttente, "Police nationale — VDMS");
        using var document = JsonDocument.Parse(json);
        var embed = document.RootElement.GetProperty("embeds")[0];
        Assert.Contains("INT-2026-000042", embed.GetProperty("title").GetString());
        var champs = embed.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("name").GetString()).ToList();
        Assert.Equal(new string?[] { "Heure", "Priorité", "Statut", "Motif", "Commune", "Moyens demandés" }, champs);
        // Aucune mention ne peut notifier un rôle ou @everyone.
        Assert.Equal(0, document.RootElement.GetProperty("allowed_mentions").GetProperty("parse").GetArrayLength());
    }

    // ----- GÉOOPS -----

    [Theory]
    [InlineData("123456789012345678", "discord:123456789012345678")]
    [InlineData("discord:123456789012345678", "discord:123456789012345678")]
    [InlineData("abc", null)]
    public void Cle_de_position(string? discordId, string? attendu)
        => Assert.Equal(attendu, OperationsRegles.ClePosition(new GeoopsPositionInput(discordId, null, "x", 0, 0, 0, null, null, null)));

    [Fact]
    public void Cle_de_position_numero_de_joueur()
        => Assert.Equal("joueur:7", OperationsRegles.ClePosition(new GeoopsPositionInput(null, 7, "x", 0, 0, 0, null, null, null)));

    [Fact]
    public void Calibration_aller_retour()
    {
        var c = new GeoopsCalibrationDto(-4000, 4000, -4000, 8000, true, 60);
        var (u, v) = OperationsRegles.VersImage(-4000, 8000, c);
        Assert.Equal(0, u, 6);
        Assert.Equal(0, v, 6);
        (u, v) = OperationsRegles.VersImage(0, 2000, c);
        Assert.Equal(0.5, u, 6);
        Assert.Equal(0.5, v, 6);
        var (x, y) = OperationsRegles.VersJeu(0.25, 0.75, c);
        Assert.Equal(-2000, x, 6);
        Assert.Equal(-1000, y, 6);
    }

    [Theory]
    [InlineData("4860", 0, 4860)]
    [InlineData("-4140,5", 0, -4140.5)]
    [InlineData("n'importe quoi", 12, 12)]
    public void Lecture_parametre_decimal(string valeur, double parDefaut, double attendu)
        => Assert.Equal(attendu, OperationsRegles.LireDouble(valeur, parDefaut), 6);

    [Fact]
    public void Position_plausible()
    {
        Assert.True(OperationsRegles.PositionPlausible(215.4, -810.2));
        Assert.False(OperationsRegles.PositionPlausible(double.NaN, 0));
        Assert.False(OperationsRegles.PositionPlausible(50000, 0));
    }

    // ----- Divers -----

    [Fact]
    public void Adresse_affichee()
    {
        var l = new AppelLocalisation("Créteil", "94000", "12", "rue du Général-Leclerc", null, null, null, null, null, null, null, null, null, null, null);
        Assert.Equal("12 rue du Général-Leclerc, 94000 Créteil", OperationsRegles.AdresseAffichee(l));
        Assert.Equal("Centre commercial Créteil Soleil", OperationsRegles.AdresseAffichee(l with { AdresseComplete = " Centre commercial Créteil Soleil " }));
        Assert.Null(OperationsRegles.AdresseAffichee(new AppelLocalisation(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null)));
    }

    [Fact]
    public void Differences_pour_historique()
    {
        var avant = new { Nature = "Vol", Priorite = "P2", X = (double?)null };
        var apres = new { Nature = "Vol", Priorite = "P1", X = (double?)12.5 };
        var differences = OperationsRegles.Differences("motif", avant, apres);
        Assert.Equal(2, differences.Count);
        Assert.Contains(differences, d => d.Champ == "motif.Priorite" && d.Ancienne == "P2" && d.Nouvelle == "P1");
        Assert.Contains(differences, d => d.Champ == "motif.X" && d.Ancienne is null && d.Nouvelle == "12.5");
    }

    [Fact]
    public void Appel_et_intervention_distincts()
    {
        Assert.True(AppelEtats.EstOuvert(AppelEtats.Brouillon));
        Assert.True(AppelEtats.EstOuvert(AppelEtats.TransmisTn));
        Assert.False(AppelEtats.EstOuvert(AppelEtats.InterventionCreee));
        Assert.False(AppelEtats.EstOuvert(AppelEtats.ClosSansSuite));
        Assert.Equal(8, PegaseListes.Canaux.Count);
        Assert.Equal(17, PegaseListes.Motifs.Count);
    }
}
