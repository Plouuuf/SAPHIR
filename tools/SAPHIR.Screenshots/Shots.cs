using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;

namespace Saphir.Screenshots;

/// <summary>Outils de capture : affichage sans écran, rendu, enregistrement PNG.</summary>
public static class Shots
{
    public static string Output { get; set; } = "captures";
    private static Window? _current;

    /// <summary>Fait avancer l'interface (liaisons, chargements asynchrones, rendu).</summary>
    public static void Pump(int milliseconds = 800)
    {
        var end = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < end)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(15);
        }
    }

    /// <summary>Sélectionne l'onglet n du premier TabControl de la fenêtre affichée.</summary>
    public static void SelectTab(int index)
    {
        if (_current?.Content is Control content &&
            content.GetLogicalDescendants().OfType<TabControl>().FirstOrDefault() is { } tabs)
            tabs.SelectedIndex = index;
    }

    /// <summary>Affiche <paramref name="content"/>, exécute <paramref name="afterShow"/> puis enregistre <paramref name="name"/>.</summary>
    public static void Capture(Control content, int width, int height, string name, Action? afterShow = null)
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = content,
            SystemDecorations = SystemDecorations.None,
            FontFamily = new FontFamily("Segoe UI, Helvetica Neue, Roboto, DejaVu Sans, Arial")
        };
        _current = window;
        window.Show();
        Pump();
        if (afterShow is not null)
        {
            afterShow();
            Pump(1500);
        }
        var frame = window.CaptureRenderedFrame();
        if (frame is null)
            Console.Error.WriteLine("Capture vide : " + name);
        else
            frame.Save(Path.Combine(Output, name));
        window.Content = null;
        window.Close();
        _current = null;
        Pump(100);
        Console.WriteLine("✓ " + name);
    }
}
