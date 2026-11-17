// Opt-in live check: activates winget over COM, searches, lists upgrades and their install locations, the
// installed packages and the details of a few packages. Read-only: never installs, upgrades or uninstalls.
// Console output is the purpose of this tool; the app itself logs through ILogger.
using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using WgetUi.Managers.WinGet;

var query = args.Length > 0 ? args[0] : "7zip";
using var manager = new WinGetManager(NullLogger<WinGetManager>.Instance);
var sw = Stopwatch.StartNew();

var availability = await manager.InitializeAsync(CancellationToken.None);
Console.WriteLine($"Activation: available={availability.IsAvailable} code={availability.ErrorCode} ({sw.ElapsedMilliseconds} ms)");
if (!availability.IsAvailable)
{
    Console.WriteLine(availability.Message);
    return 1;
}

sw.Restart();
var found = await manager.SearchAsync(query, CancellationToken.None);
Console.WriteLine($"Search '{query}': {found.Count} results ({sw.ElapsedMilliseconds} ms)");
foreach (var p in found.Take(15))
{
    Console.WriteLine($"  [{p.SourceId}] {p.Id} | {p.Name} | {p.Version ?? "?"}");
}

var storeHits = await manager.SearchAsync("whatsapp", CancellationToken.None);
foreach (var hit in storeHits.Where(p => p.SourceId == "msstore").Take(1).Concat(found.Take(1)))
{
    var agreements = await manager.GetAgreementsAsync(hit, CancellationToken.None);
    Console.WriteLine($"Agreements of [{hit.SourceId}] {hit.Id}: requires acceptance={agreements.RequiresAcceptance}, license={agreements.License}");
    foreach (var a in agreements.PackageTerms.Concat(agreements.SourceTerms))
    {
        Console.WriteLine($"  {a.Label} | {a.Text} | {a.Url}");
    }
}

foreach (var includeUnknown in new[] { false, true })
{
    sw.Restart();
    var upgrades = await manager.GetUpgradesAsync(includeUnknown, CancellationToken.None);
    Console.WriteLine($"Upgrades (includeUnknown={includeUnknown}): {upgrades.Count} ({sw.ElapsedMilliseconds} ms)");
    foreach (var p in upgrades)
    {
        Console.WriteLine($"  [{p.SourceId}] {p.Id} | {p.Version ?? "unknown"} -> {p.AvailableVersion}");
    }
}

var upgradable = await manager.GetUpgradesAsync(false, CancellationToken.None);
foreach (var p in upgradable.Take(8))
{
    Console.WriteLine($"Install location of {p.Id}: {await manager.GetInstallLocationAsync(p, CancellationToken.None) ?? "(unknown)"}");
}

sw.Restart();
var installed = await manager.GetInstalledAsync(CancellationToken.None);
Console.WriteLine($"Installed: {installed.Count} ({sw.ElapsedMilliseconds} ms)");
foreach (var group in installed.GroupBy(p => p.SourceId).OrderBy(g => g.Key, StringComparer.Ordinal))
{
    Console.WriteLine($"  [{group.Key}] {group.Count()}, e.g. {string.Join(", ", group.Take(3).Select(p => $"{p.Id} {p.Version ?? "?"}"))}");
}

var detailTargets = found.Where(p => p.Id == "7zip.7zip").Take(1)
    .Concat(storeHits.Where(p => p.SourceId == "msstore").Take(1))
    .Concat(installed.Where(p => p.SourceId == "local").Take(1));
foreach (var target in detailTargets)
{
    sw.Restart();
    var d = await manager.GetDetailsAsync(target, CancellationToken.None);
    Console.WriteLine($"Details of [{target.SourceId}] {target.Id} ({sw.ElapsedMilliseconds} ms):");
    Console.WriteLine($"  publisher={d.Publisher} | homepage={d.HomepageUrl} | license={d.License} ({d.LicenseUrl}) | notes={d.ReleaseNotesUrl}");
    Console.WriteLine($"  installed={d.InstalledVersion ?? "-"} available={d.AvailableVersion ?? "-"} versions={d.Versions.Count}: {string.Join(", ", d.Versions.Take(6))}");
    Console.WriteLine($"  description={d.Description?.ReplaceLineEndings(" ")[..Math.Min(d.Description.Length, 120)]}");
}

return 0;
