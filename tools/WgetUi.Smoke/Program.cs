// Opt-in live check: activates winget over COM, searches and lists upgrades. Read-only: never installs.
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

return 0;
