// COM activation spike: read-only search and upgrade listing. Never installs anything.
using System.Diagnostics;
using Microsoft.Management.Deployment;

var pm = new PackageManager();
Console.WriteLine($"PackageManager created, version {pm.Version}");

var catalogs = pm.GetPackageCatalogs();
var refs = new List<PackageCatalogReference>();
for (var i = 0; i < catalogs.Count; i++)
{
    Console.WriteLine($"Catalog: {catalogs[i].Info.Name} ({catalogs[i].Info.Id}) type={catalogs[i].Info.Type}");
    refs.Add(catalogs[i]);
}

var sw = Stopwatch.StartNew();
foreach (var r in refs)
{
    r.AcceptSourceAgreements = true;
    var conn = r.Connect();
    Console.WriteLine($"[{r.Info.Name}] connect: {conn.Status} ({sw.ElapsedMilliseconds} ms)");
    if (conn.Status != ConnectResultStatus.Ok)
    {
        continue;
    }

    var opts = new FindPackagesOptions { ResultLimit = 10 };
    opts.Selectors.Add(new PackageMatchFilter { Field = PackageMatchField.CatalogDefault, Option = PackageFieldMatchOption.ContainsCaseInsensitive, Value = "7zip" });
    var res = conn.PackageCatalog.FindPackages(opts);
    Console.WriteLine($"[{r.Info.Name}] search status {res.Status}, {res.Matches.Count} matches ({sw.ElapsedMilliseconds} ms)");
    for (var i = 0; i < res.Matches.Count; i++)
    {
        var p = res.Matches[i].CatalogPackage;
        Console.WriteLine($"   {p.Id} | {p.Name} | {p.DefaultInstallVersion?.Version} | src={p.DefaultInstallVersion?.PackageCatalog?.Info.Name}");
    }
}

sw.Restart();
var co = new CreateCompositePackageCatalogOptions { CompositeSearchBehavior = CompositeSearchBehavior.LocalCatalogs };
foreach (var r in refs)
{
    co.Catalogs.Add(r);
}

var composite = pm.CreateCompositePackageCatalog(co).Connect();
Console.WriteLine($"Composite connect: {composite.Status} ({sw.ElapsedMilliseconds} ms)");
var all = composite.PackageCatalog.FindPackages(new FindPackagesOptions());
Console.WriteLine($"Installed: status {all.Status}, {all.Matches.Count} packages ({sw.ElapsedMilliseconds} ms)");
var upgrades = 0;
var unknown = 0;
for (var i = 0; i < all.Matches.Count; i++)
{
    var p = all.Matches[i].CatalogPackage;
    if (!p.IsUpdateAvailable)
    {
        continue;
    }

    upgrades++;
    var installed = p.InstalledVersion?.Version;
    if (installed == "Unknown")
    {
        unknown++;
    }

    Console.WriteLine($"   UPGRADE {p.Id} | {p.Name} | {installed} -> {p.DefaultInstallVersion?.Version} | src={p.DefaultInstallVersion?.PackageCatalog?.Info.Name}");
}

Console.WriteLine($"{upgrades} upgrades ({unknown} with unknown installed version)");
