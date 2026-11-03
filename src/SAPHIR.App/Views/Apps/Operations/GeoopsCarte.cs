using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using Saphir.Shared.Contracts.Operations;
using Saphir.ViewModels.Apps.Operations;

namespace Saphir.Views.Apps.Operations;

/// <summary>
/// Carte GÉOOPS : image de fond calibrée en coordonnées du jeu (ou grille de repérage sans image), repères des équipages,
/// des interventions et des autres agents. Molette : zoom ; glisser : déplacement ; clic : sélection d'un repère ou point de la carte.
/// </summary>
public sealed class GeoopsCarte : Control, ICustomHitTest
{
    public static readonly StyledProperty<IImage?> ImageProperty =
        AvaloniaProperty.Register<GeoopsCarte, IImage?>(nameof(Image));

    public static readonly StyledProperty<GeoopsCalibrationDto?> CalibrationProperty =
        AvaloniaProperty.Register<GeoopsCarte, GeoopsCalibrationDto?>(nameof(Calibration));

    public static readonly StyledProperty<IReadOnlyList<MarqueurCarte>?> MarqueursProperty =
        AvaloniaProperty.Register<GeoopsCarte, IReadOnlyList<MarqueurCarte>?>(nameof(Marqueurs));

    public static readonly StyledProperty<PositionCarte?> CentreProperty =
        AvaloniaProperty.Register<GeoopsCarte, PositionCarte?>(nameof(Centre));

    public static readonly StyledProperty<ICommand?> ClicCommandProperty =
        AvaloniaProperty.Register<GeoopsCarte, ICommand?>(nameof(ClicCommand));

    public static readonly StyledProperty<ICommand?> SelectionCommandProperty =
        AvaloniaProperty.Register<GeoopsCarte, ICommand?>(nameof(SelectionCommand));

    private const double ZoomMax = 16;
    private double _zoom = 1;
    private Vector _decalage;
    private Point? _appui;
    private Point _dernier;
    private bool _glisse;
    private PositionCarte? _centreEnAttente;

    static GeoopsCarte()
    {
        AffectsRender<GeoopsCarte>(ImageProperty, CalibrationProperty, MarqueursProperty);
        ClipToBoundsProperty.OverrideDefaultValue<GeoopsCarte>(true);
    }

    public IImage? Image { get => GetValue(ImageProperty); set => SetValue(ImageProperty, value); }
    public GeoopsCalibrationDto? Calibration { get => GetValue(CalibrationProperty); set => SetValue(CalibrationProperty, value); }
    public IReadOnlyList<MarqueurCarte>? Marqueurs { get => GetValue(MarqueursProperty); set => SetValue(MarqueursProperty, value); }
    public PositionCarte? Centre { get => GetValue(CentreProperty); set => SetValue(CentreProperty, value); }
    public ICommand? ClicCommand { get => GetValue(ClicCommandProperty); set => SetValue(ClicCommandProperty, value); }
    public ICommand? SelectionCommand { get => GetValue(SelectionCommandProperty); set => SetValue(SelectionCommandProperty, value); }

    // ===== Géométrie =====

    /// <summary>Taille du « monde » dessiné : l'image, ou une surface aux proportions de la zone calibrée.</summary>
    private Size Monde(GeoopsCalibrationDto c)
    {
        if (Image is { } image && image.Size.Width > 0 && image.Size.Height > 0)
            return image.Size;
        var largeur = Math.Max(1, c.XMax - c.XMin);
        var hauteur = Math.Max(1, c.YMax - c.YMin);
        return new Size(1000, 1000 * hauteur / largeur);
    }

    private double Echelle(Size monde)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return 1;
        return Math.Min(Bounds.Width / monde.Width, Bounds.Height / monde.Height) * _zoom;
    }

    private Point Milieu => new(Bounds.Width / 2, Bounds.Height / 2);

    private Point MondeVersEcran(Point monde, Size taille, double echelle)
        => Milieu + _decalage + new Vector((monde.X - taille.Width / 2) * echelle, (monde.Y - taille.Height / 2) * echelle);

    private Point EcranVersMonde(Point ecran, Size taille, double echelle)
    {
        var v = ecran - Milieu - _decalage;
        return new Point(v.X / echelle + taille.Width / 2, v.Y / echelle + taille.Height / 2);
    }

    /// <summary>Coordonnées du jeu → point du monde (l'axe Y du jeu monte, celui de l'image descend).</summary>
    private static Point JeuVersMonde(double x, double y, GeoopsCalibrationDto c, Size monde)
    {
        var u = (x - c.XMin) / Math.Max(1e-6, c.XMax - c.XMin);
        var v = (c.YMax - y) / Math.Max(1e-6, c.YMax - c.YMin);
        return new Point(u * monde.Width, v * monde.Height);
    }

    private static PositionCarte MondeVersJeu(Point p, GeoopsCalibrationDto c, Size monde)
        => new(c.XMin + p.X / monde.Width * (c.XMax - c.XMin), c.YMax - p.Y / monde.Height * (c.YMax - c.YMin));

    // ===== Rendu =====

    public override void Render(DrawingContext context)
    {
        var zone = new Rect(Bounds.Size);
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(0xE4, 0xEA, 0xF1)), zone);
        if (Calibration is not { } c || zone.Width <= 0 || zone.Height <= 0)
            return;
        var monde = Monde(c);
        if (_centreEnAttente is { } centre)
        {
            _centreEnAttente = null;
            _zoom = Math.Max(_zoom, 3);
            var cible = JeuVersMonde(centre.X, centre.Y, c, monde);
            var e = Echelle(monde);
            _decalage = new Vector(-(cible.X - monde.Width / 2) * e, -(cible.Y - monde.Height / 2) * e);
        }
        var echelle = Echelle(monde);
        var hautGauche = MondeVersEcran(new Point(0, 0), monde, echelle);
        var surface = new Rect(hautGauche, new Size(monde.Width * echelle, monde.Height * echelle));

        if (Image is { } image)
        {
            context.DrawImage(image, new Rect(image.Size), surface);
        }
        else
        {
            context.FillRectangle(new SolidColorBrush(Color.FromRgb(0xF7, 0xF9, 0xFB)), surface);
            DessinerGrille(context, c, monde, echelle);
        }
        context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0x8A, 0x9B, 0xB0)), 1), surface);

        if (Marqueurs is not { } marqueurs)
            return;
        // Agents d'abord, puis interventions, puis équipages (au-dessus).
        foreach (var m in marqueurs.OrderBy(m => m.Forme switch { "agent" => 0, "intervention" => 1, _ => 2 }).ThenBy(m => m.Selectionne))
        {
            var p = MondeVersEcran(JeuVersMonde(m.X, m.Y, c, monde), monde, echelle);
            if (p.X < -20 || p.Y < -20 || p.X > zone.Width + 20 || p.Y > zone.Height + 20)
                continue;
            DessinerMarqueur(context, m, p);
        }
    }

    private void DessinerGrille(DrawingContext context, GeoopsCalibrationDto c, Size monde, double echelle)
    {
        var trait = new Pen(new SolidColorBrush(Color.FromRgb(0xD3, 0xDB, 0xE5)), 1);
        var texte = new SolidColorBrush(Color.FromRgb(0x7C, 0x8C, 0xA0));
        var pas = (c.XMax - c.XMin) / echelle / monde.Width * 120 > 1000 ? 2000 : 1000;
        for (var x = Math.Ceiling(c.XMin / pas) * pas; x <= c.XMax; x += pas)
        {
            var haut = MondeVersEcran(JeuVersMonde(x, c.YMax, c, monde), monde, echelle);
            var bas = MondeVersEcran(JeuVersMonde(x, c.YMin, c, monde), monde, echelle);
            context.DrawLine(trait, haut, bas);
            context.DrawText(Texte(x.ToString("0", CultureInfo.InvariantCulture), 10, texte), new Point(haut.X + 2, Math.Max(haut.Y, 0) + 2));
        }
        for (var y = Math.Ceiling(c.YMin / pas) * pas; y <= c.YMax; y += pas)
        {
            var gauche = MondeVersEcran(JeuVersMonde(c.XMin, y, c, monde), monde, echelle);
            var droite = MondeVersEcran(JeuVersMonde(c.XMax, y, c, monde), monde, echelle);
            context.DrawLine(trait, gauche, droite);
            context.DrawText(Texte(y.ToString("0", CultureInfo.InvariantCulture), 10, texte), new Point(Math.Max(gauche.X, 0) + 2, gauche.Y + 2));
        }
    }

    private static void DessinerMarqueur(DrawingContext context, MarqueurCarte m, Point p)
    {
        var couleur = Color.TryParse(m.Couleur, out var lue) ? lue : Colors.SteelBlue;
        var remplissage = new SolidColorBrush(couleur);
        switch (m.Forme)
        {
            case "agent":
                context.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x8A, 0x8F, 0x98)), new Pen(Brushes.White, 1), p, 3.5, 3.5);
                return;
            case "intervention":
            {
                var losange = new StreamGeometry();
                using (var g = losange.Open())
                {
                    g.BeginFigure(new Point(p.X, p.Y - 9), true);
                    g.LineTo(new Point(p.X + 9, p.Y));
                    g.LineTo(new Point(p.X, p.Y + 9));
                    g.LineTo(new Point(p.X - 9, p.Y));
                    g.EndFigure(true);
                }
                context.DrawGeometry(remplissage, new Pen(Brushes.White, 1.5), losange);
                break;
            }
            default:
                context.DrawEllipse(remplissage, m.Declaree
                    ? new Pen(Brushes.Black, 1.5, new DashStyle(new double[] { 2, 2 }, 0))
                    : new Pen(Brushes.White, 2), p, 7.5, 7.5);
                break;
        }
        if (m.Selectionne)
            context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(0x1E, 0x39, 0x5B)), 2.5), p, 13, 13);

        var etiquette = Texte(m.Libelle, 11, Brushes.Black);
        var origine = new Point(p.X + 11, p.Y - etiquette.Height / 2);
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)),
            new Rect(origine.X - 2, origine.Y - 1, etiquette.Width + 4, etiquette.Height + 2), 2);
        context.DrawText(etiquette, origine);
    }

    private static FormattedText Texte(string texte, double taille, IBrush couleur)
        => new(texte, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, taille, couleur);

    // ===== Interaction =====

    /// <summary>Toute la surface reçoit les clics, la molette et le glisser.</summary>
    public bool HitTest(Point point) => point.X >= 0 && point.Y >= 0 && point.X <= Bounds.Width && point.Y <= Bounds.Height;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CentreProperty && change.NewValue is PositionCarte centre)
        {
            _centreEnAttente = centre;
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        _appui = e.GetPosition(this);
        _dernier = _appui.Value;
        _glisse = false;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_appui is not { } appui)
            return;
        var position = e.GetPosition(this);
        if (!_glisse && Math.Abs(position.X - appui.X) + Math.Abs(position.Y - appui.Y) > 5)
            _glisse = true;
        if (_glisse)
        {
            _decalage += position - _dernier;
            InvalidateVisual();
        }
        _dernier = position;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_appui is null)
            return;
        var position = e.GetPosition(this);
        var clic = !_glisse;
        _appui = null;
        _glisse = false;
        e.Pointer.Capture(null);
        if (clic)
            Cliquer(position);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Calibration is not { } c)
            return;
        var monde = Monde(c);
        var curseur = e.GetPosition(this);
        var sousCurseur = EcranVersMonde(curseur, monde, Echelle(monde));
        _zoom = Math.Clamp(_zoom * (e.Delta.Y > 0 ? 1.25 : 0.8), 1, ZoomMax);
        if (_zoom <= 1.0001)
        {
            _decalage = default;
        }
        else
        {
            var echelle = Echelle(monde);
            _decalage = curseur - Milieu - new Vector((sousCurseur.X - monde.Width / 2) * echelle, (sousCurseur.Y - monde.Height / 2) * echelle);
        }
        InvalidateVisual();
        e.Handled = true;
    }

    private void Cliquer(Point position)
    {
        if (Calibration is not { } c)
            return;
        var monde = Monde(c);
        var echelle = Echelle(monde);
        var proche = (Marqueurs ?? Array.Empty<MarqueurCarte>())
            .Where(m => m.Forme != "agent")
            .Select(m => (Marqueur: m, Distance: Distance(MondeVersEcran(JeuVersMonde(m.X, m.Y, c, monde), monde, echelle), position)))
            .Where(x => x.Distance <= 14)
            .OrderBy(x => x.Distance)
            .Select(x => x.Marqueur)
            .FirstOrDefault();
        if (proche is not null)
        {
            if (SelectionCommand?.CanExecute(proche) == true)
                SelectionCommand.Execute(proche);
            // Un clic sur un repère reste aussi un point de la carte pour le placement.
        }
        var point = MondeVersJeu(EcranVersMonde(position, monde, echelle), c, monde);
        if (proche is null && ClicCommand?.CanExecute(point) == true)
            ClicCommand.Execute(point);
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
