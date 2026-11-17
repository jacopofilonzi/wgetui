using Avalonia;
using Avalonia.Media;

namespace WgetUi.App.Icons;

/// <summary>
/// The app's artwork, drawn in a 64×64 design space: a "package with arrow" glyph (white strokes, round caps
/// and joins, no fill) on a rounded <c>#5B4BDB</c> tile for the app icon, and the bare glyph plus an optional
/// state dot for the tray. Shared by <c>tools/WgetUi.IconGen</c> (which writes Assets/wgetui.ico) and the
/// runtime tray icon, so both always match.
/// </summary>
/// <remarks>
/// Small sizes use thicker strokes so the glyph still reads. The tray dot sits bottom-right with a
/// transparent ring around it (the glyph is clipped away there) so it stays legible on any taskbar colour.
/// </remarks>
public static class IconArt
{
    public const double DesignSize = 64;

    public static readonly Color TileColor = Color.Parse("#5B4BDB");

    public static readonly Color DarkGlyph = Color.Parse("#18181B");

    private const string Arrow = "M32 9 V20 M27 15.5 L32 20.5 L37 15.5";
    private const string Box = "M18 31 L32 24 L46 31 L46 45 L32 52 L18 45 Z M18 31 L32 38 L46 31 M32 38 V52";
    private static readonly Point DotCenter = new(51, 51);
    private const double DotRadius = 9.5;
    private const double DotCutoutRadius = 13;

    /// <summary>Stroke width (design units) of the app icon at a pixel size.</summary>
    public static double AppStroke(int pixelSize) => pixelSize >= 48 ? 3.6 : 5;

    /// <summary>Stroke width (design units) of the tray glyph at a pixel size.</summary>
    public static double TrayStroke(int pixelSize) => pixelSize <= 24 ? 6 : 5;

    /// <summary>Draws the app icon (tile + white glyph) filling a square of <paramref name="pixelSize"/>.</summary>
    public static void DrawApp(DrawingContext context, int pixelSize)
    {
        ArgumentNullException.ThrowIfNull(context);
        using (context.PushTransform(Matrix.CreateScale(pixelSize / DesignSize, pixelSize / DesignSize)))
        {
            context.DrawRectangle(new SolidColorBrush(TileColor), null, new Rect(2, 2, 60, 60), 14, 14);
            DrawGlyph(context, Colors.White, AppStroke(pixelSize));
        }
    }

    /// <summary>Draws the tray glyph in <paramref name="glyph"/>, with a state dot when <paramref name="dot"/> is set.</summary>
    public static void DrawTray(DrawingContext context, int pixelSize, Color glyph, Color? dot)
    {
        ArgumentNullException.ThrowIfNull(context);
        using (context.PushTransform(Matrix.CreateScale(pixelSize / DesignSize, pixelSize / DesignSize)))
        {
            if (dot is not { } dotColor)
            {
                DrawGlyph(context, glyph, TrayStroke(pixelSize));
                return;
            }

            var outside = new CombinedGeometry(
                GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(0, 0, DesignSize, DesignSize)),
                new EllipseGeometry(new Rect(DotCenter.X - DotCutoutRadius, DotCenter.Y - DotCutoutRadius, DotCutoutRadius * 2, DotCutoutRadius * 2)));
            using (context.PushGeometryClip(outside))
            {
                DrawGlyph(context, glyph, TrayStroke(pixelSize));
            }

            context.DrawEllipse(new SolidColorBrush(dotColor), null, DotCenter, DotRadius, DotRadius);
        }
    }

    private static void DrawGlyph(DrawingContext context, Color color, double stroke)
    {
        var pen = new Pen(new SolidColorBrush(color), stroke, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        context.DrawGeometry(null, pen, StreamGeometry.Parse(Arrow));
        context.DrawGeometry(null, pen, StreamGeometry.Parse(Box));
    }
}
