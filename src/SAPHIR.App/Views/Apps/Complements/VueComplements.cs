using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Saphir.ViewModels;
using Saphir.ViewModels.Apps.Complements;

namespace Saphir.Views.Apps.Complements;

/// <summary>Comportements communs des vues complémentaires : hôte de plateforme, temps réel, disposition compacte.</summary>
public static class VueComplements
{
    /// <summary>Largeur sous laquelle l'application passe en disposition « téléphone ».</summary>
    public const double LargeurCompacte = 720;

    public static void Preparer(UserControl vue)
        => vue.SizeChanged += (_, e) =>
        {
            if (vue.DataContext is ComplementsViewModelBase vm)
                vm.IsCompact = e.NewSize.Width < LargeurCompacte;
        };

    public static void Attacher(UserControl vue)
    {
        if (vue.DataContext is ComplementsViewModelBase vm)
        {
            vm.Hote = new HotePlateforme(vue);
            if (vue.Bounds.Width > 0)
                vm.IsCompact = vue.Bounds.Width < LargeurCompacte;
        }
        (vue.DataContext as AppViewModelBase)?.Resume();
    }

    public static void Detacher(UserControl vue) => (vue.DataContext as AppViewModelBase)?.Suspend();
}

/// <summary>Services de la plateforme (fichiers, navigateur) obtenus depuis la fenêtre qui contient la vue.</summary>
internal sealed class HotePlateforme(Control vue) : IHotePlateforme
{
    private TopLevel? Fenetre => TopLevel.GetTopLevel(vue);

    public async Task<FichierLocal?> ChoisirFichierAsync(string titre, IReadOnlyList<string> extensions)
    {
        var fenetre = Fenetre;
        if (fenetre is null) return null;
        var fichiers = await fenetre.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = titre,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Fichiers autorisés") { Patterns = extensions.Select(e => "*." + e).ToList() }
            ]
        });
        var fichier = fichiers.FirstOrDefault();
        if (fichier is null) return null;
        long taille = 0;
        try
        {
            var proprietes = await fichier.GetBasicPropertiesAsync();
            taille = (long)(proprietes.Size ?? 0);
        }
        catch (Exception)
        {
            // Taille inconnue : elle sera lue sur le flux.
        }
        return new FichierLocal(fichier.Name, taille, () => fichier.OpenReadAsync());
    }

    public async Task<bool> EnregistrerFichierAsync(string nomSuggere, byte[] contenu)
    {
        var fenetre = Fenetre;
        if (fenetre is null) return false;
        var fichier = await fenetre.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Enregistrer sous",
            SuggestedFileName = nomSuggere,
            ShowOverwritePrompt = true
        });
        if (fichier is null) return false;
        await using var flux = await fichier.OpenWriteAsync();
        await flux.WriteAsync(contenu);
        return true;
    }

    public async Task<bool> OuvrirAdresseAsync(Uri adresse)
    {
        var fenetre = Fenetre;
        return fenetre is not null && await fenetre.Launcher.LaunchUriAsync(adresse);
    }

    public async Task<bool> OuvrirFichierLocalAsync(string chemin)
    {
        var fenetre = Fenetre;
        if (fenetre is null) return false;
        var fichier = await fenetre.StorageProvider.TryGetFileFromPathAsync(new Uri(chemin));
        if (fichier is not null)
            return await fenetre.Launcher.LaunchFileAsync(fichier);
        return await fenetre.Launcher.LaunchUriAsync(new Uri(chemin));
    }
}
