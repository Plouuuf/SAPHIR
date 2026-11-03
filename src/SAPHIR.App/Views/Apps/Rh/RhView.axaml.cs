using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Saphir.Shared.Contracts.Rh;
using Saphir.ViewModels;
using Saphir.ViewModels.Apps.Rh;

namespace Saphir.Views.Apps.Rh;

/// <summary>
/// RESSOURCES HUMAINES. Fournit au modèle de vue les sélecteurs de fichiers (TopLevel.StorageProvider)
/// et bascule l'affichage étroit (téléphone) selon la largeur.
/// </summary>
public partial class RhView : UserControl
{
    private const double LargeurCompacte = 760;

    public RhView()
    {
        InitializeComponent();
        SizeChanged += (_, e) =>
        {
            if (DataContext is RhViewModel vm && e.NewSize.Width > 0)
                vm.Compact = e.NewSize.Width < LargeurCompacte;
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is RhViewModel vm)
        {
            vm.EnregistrerFichier = EnregistrerAsync;
            vm.ChoisirFichier = ChoisirAsync;
        }
        (DataContext as AppViewModelBase)?.Resume();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as AppViewModelBase)?.Suspend();
    }

    /// <summary>Sélecteur d'enregistrement : écrit le fichier téléchargé là où l'agent le choisit.</summary>
    private async Task<bool> EnregistrerAsync(string nom, byte[] contenu)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null || !top.StorageProvider.CanSave)
            throw new InvalidOperationException("L'enregistrement de fichiers n'est pas disponible sur cet appareil.");
        var extension = Path.GetExtension(nom).TrimStart('.');
        var fichier = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Enregistrer le document",
            SuggestedFileName = nom,
            DefaultExtension = extension.Length == 0 ? null : extension,
            ShowOverwritePrompt = true
        });
        if (fichier is null)
            return false;
        await using var flux = await fichier.OpenWriteAsync();
        await flux.WriteAsync(contenu);
        await flux.FlushAsync();
        return true;
    }

    /// <summary>Sélecteur d'ouverture : lit le fichier choisi (refusé au-delà de 10 Mo).</summary>
    private async Task<RhFichierChoisi?> ChoisirAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null || !top.StorageProvider.CanOpen)
            throw new InvalidOperationException("Le choix de fichiers n'est pas disponible sur cet appareil.");
        var fichiers = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choisir un document",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Documents RH (PDF, images, DOCX, texte)")
                {
                    Patterns = RhFichiers.Extensions.Select(e => "*" + e).ToList()
                }
            ]
        });
        if (fichiers.Count == 0)
            return null;

        var choisi = fichiers[0];
        await using var flux = await choisi.OpenReadAsync();
        using var memoire = new MemoryStream();
        var tampon = new byte[81920];
        int lus;
        while ((lus = await flux.ReadAsync(tampon)) > 0)
        {
            memoire.Write(tampon, 0, lus);
            if (memoire.Length > RhFichiers.TailleMax)
            {
                (DataContext as RhViewModel)?.Erreur($"« {choisi.Name} » dépasse 10 Mo : choisis un fichier plus léger.");
                return null;
            }
        }
        return new RhFichierChoisi(choisi.Name, memoire.ToArray());
    }
}
