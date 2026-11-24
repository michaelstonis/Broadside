using Broadside.Tools.DiagnosticCatalogPage;

// Usage: dotnet run --project tools/DiagnosticCatalogPage -- <page> [--write]
// Without --write, exits 1 when the page's generated block differs from the catalog; with it, regenerates the block.
if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] != "--write"))
{
    Console.Error.WriteLine("usage: dotnet run --project tools/DiagnosticCatalogPage -- <page> [--write]");
    return 2;
}

string page = args[0];
string text = File.ReadAllText(page);
if (args.Length == 2)
{
    File.WriteAllText(page, CatalogPage.Update(text));
    Console.Out.WriteLine($"{page}: the diagnostic catalog is regenerated.");
    return 0;
}

if (!CatalogPage.IsCurrent(text))
{
    Console.Out.WriteLine($"{page}: the diagnostic catalog is stale; regenerate it with dotnet run --project tools/DiagnosticCatalogPage -- {page} --write");
    return 1;
}

Console.Out.WriteLine($"{page}: the diagnostic catalog is current.");
return 0;
