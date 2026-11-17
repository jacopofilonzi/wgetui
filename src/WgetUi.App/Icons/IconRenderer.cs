using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace WgetUi.App.Icons;

/// <summary>Renders <see cref="IconArt"/> to PNG bytes and packs them into ICO files. Needs an initialized Avalonia platform.</summary>
public static class IconRenderer
{
    /// <summary>Sizes written into Assets/wgetui.ico.</summary>
    public static readonly IReadOnlyList<int> AppSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    /// <summary>Tray sizes: the notification area uses 16 px at 100% scaling up to 32 px at 200%.</summary>
    public static readonly IReadOnlyList<int> TraySizes = [16, 20, 24, 32];

    public static byte[] RenderPng(int pixelSize, Action<DrawingContext> draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        using var bitmap = new RenderTargetBitmap(new PixelSize(pixelSize, pixelSize), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext(true))
        {
            draw(context);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    public static byte[] AppIco() =>
        IcoFile.Pack([.. AppSizes.Select(s => (s, RenderPng(s, c => IconArt.DrawApp(c, s))))]);

    public static byte[] TrayIco(Color glyph, Color? dot) =>
        IcoFile.Pack([.. TraySizes.Select(s => (s, RenderPng(s, c => IconArt.DrawTray(c, s, glyph, dot))))]);
}
