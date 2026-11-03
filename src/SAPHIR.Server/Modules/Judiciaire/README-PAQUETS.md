# Module Judiciaire — paquets NuGet

**Aucun paquet supplémentaire n'est nécessaire.**

L'export PDF (actes LRPPN et procédures) est produit par un générateur interne sans dépendance,
`PdfSimple.cs` : pages A4, polices standard PDF Helvetica et Helvetica-Bold (aucune police à embarquer),
encodage WinAnsi (accents français), retour à la ligne, en-tête, pied de page numéroté et filigrane « PROJET »
pour un acte non validé.

Ce choix évite une dépendance externe (licence, taille, compatibilité net9.0 / Linux) pour un besoin limité à du
texte mis en page. Si une mise en page plus riche devient nécessaire (tableaux, images), QuestPDF 2024.12.3
(licence Community, `QuestPDF.Settings.License = LicenseType.Community`) pourra remplacer `PdfSimple` sans changer
les routes : seules `ActesStore.Pdf` et `ActesStore.ProcedurePdfAsync` produisent les octets.

Lecture FiveM : `MySqlConnector` (déjà référencé par le serveur).
