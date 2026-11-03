using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Saphir.ViewModels;
using Saphir.ViewModels.Apps.Judiciaire;

namespace Saphir.Views.Apps.Judiciaire;

/// <summary>Enregistrement (PDF, pièces) et choix de fichiers via le StorageProvider de la fenêtre.</summary>
public sealed class FichiersPoste(Control controle) : IFichiersPoste
{
    private IStorageProvider Fournisseur => TopLevel.GetTopLevel(controle)?.StorageProvider
                                            ?? throw new Saphir.Services.ApiException("Accès aux fichiers indisponible sur ce poste.");

    public async Task<bool> EnregistrerAsync(string nomSuggere, byte[] contenu)
    {
        var fournisseur = Fournisseur;
        if (!fournisseur.CanSave)
            throw new Saphir.Services.ApiException("Ce poste ne permet pas d'enregistrer des fichiers.");
        var extension = Path.GetExtension(nomSuggere).TrimStart('.');
        var fichier = await fournisseur.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Enregistrer le document",
            SuggestedFileName = nomSuggere,
            DefaultExtension = extension,
            ShowOverwritePrompt = true,
            FileTypeChoices = string.IsNullOrEmpty(extension)
                ? null
                : new[] { new FilePickerFileType(extension.ToUpperInvariant()) { Patterns = new[] { "*." + extension } } }
        });
        if (fichier is null)
            return false;
        await using var flux = await fichier.OpenWriteAsync();
        if (flux.CanSeek)
            flux.SetLength(0);
        await flux.WriteAsync(contenu);
        return true;
    }

    public async Task<(string Nom, byte[] Contenu)?> ChoisirAsync()
    {
        var fournisseur = Fournisseur;
        if (!fournisseur.CanOpen)
            throw new Saphir.Services.ApiException("Ce poste ne permet pas de choisir un fichier.");
        var fichiers = await fournisseur.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Joindre un fichier (10 Mo au plus)",
            AllowMultiple = false
        });
        if (fichiers.Count == 0)
            return null;
        var fichier = fichiers[0];
        await using var flux = await fichier.OpenReadAsync();
        using var memoire = new MemoryStream();
        await flux.CopyToAsync(memoire);
        return (fichier.Name, memoire.ToArray());
    }
}

/// <summary>Code-behind commun des vues judiciaires : Resume/Suspend et accès aux fichiers.</summary>
public static class JudiciaireVues
{
    public static void Attacher(Control vue)
    {
        if (vue.DataContext is CheopsViewModel cheops)
            cheops.Fichiers ??= new FichiersPoste(vue);
        if (vue.DataContext is LrppnViewModel lrppn)
            lrppn.Fichiers ??= new FichiersPoste(vue);
        (vue.DataContext as AppViewModelBase)?.Resume();
    }

    public static void Detacher(Control vue) => (vue.DataContext as AppViewModelBase)?.Suspend();
}
