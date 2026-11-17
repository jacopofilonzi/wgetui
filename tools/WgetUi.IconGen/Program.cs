// make icons: writes the multi-size app icon (and, with --preview <dir>, one PNG per size and tray state for
// a visual check). Console output is the purpose of this tool.
using Avalonia;
using Avalonia.Media;
using WgetUi.App.Icons;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: WgetUi.IconGen <output.ico> [--preview <dir>]");
    return 2;
}

AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithoutStarting();

var output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var ico = IconRenderer.AppIco();
File.WriteAllBytes(output, ico);
Console.WriteLine($"Wrote {output} ({ico.Length} bytes, sizes {string.Join(", ", IconRenderer.AppSizes)})");

var previewIndex = Array.IndexOf(args, "--preview");
if (previewIndex >= 0 && previewIndex + 1 < args.Length)
{
    var dir = Path.GetFullPath(args[previewIndex + 1]);
    Directory.CreateDirectory(dir);
    foreach (var size in IconRenderer.AppSizes)
    {
        File.WriteAllBytes(Path.Combine(dir, $"app-{size}.png"), IconRenderer.RenderPng(size, c => IconArt.DrawApp(c, size)));
    }

    var dots = new (string Name, Color? Dot)[]
    {
        ("none", null), ("updates", Color.Parse("#3B82F6")), ("running", Color.Parse("#16A34A")),
        ("admin", Color.Parse("#F59E0B")), ("failure", Color.Parse("#DC2626")),
    };
    foreach (var (name, dot) in dots)
    {
        foreach (var size in IconRenderer.TraySizes)
        {
            File.WriteAllBytes(Path.Combine(dir, $"tray-{name}-light-{size}.png"), IconRenderer.RenderPng(size, c => IconArt.DrawTray(c, size, IconArt.DarkGlyph, dot)));
            File.WriteAllBytes(Path.Combine(dir, $"tray-{name}-dark-{size}.png"), IconRenderer.RenderPng(size, c => IconArt.DrawTray(c, size, Colors.White, dot)));
        }
    }

    Console.WriteLine($"Wrote previews to {dir}");
}

return 0;
