using System.Reflection;
using System.Text.RegularExpressions;
using Broadside.Diagnostics;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>
/// The filter, DCT, CCITT, JBIG2, JPX and image diagnostic codes live only in their public catalog classes (issue #244): the
/// internal catalog no longer holds them, no report site names the internal catalog, and every code in these classes is reported.
/// </summary>
public sealed class FilterAndImageDiagnosticCodeTests
{
    private static readonly Type[] CatalogClasses =
    [
        typeof(FilterDiagnostics),
        typeof(DctDiagnostics),
        typeof(CcittDiagnostics),
        typeof(Jbig2Diagnostics),
        typeof(JpxDiagnostics),
        typeof(ImageDiagnostics),
    ];

    private static string SourceRoot { get; } = Path.GetFullPath(Path.Combine(Corpus.Directory, "..", "..", "src", "Broadside"));

    [Fact]
    public void No_report_site_for_filters_and_images_uses_the_internal_catalog()
    {
        DiagnosticCatalogMigrationTests.AssertMigrated(CatalogClasses);
    }

    [Fact]
    public void Every_code_in_these_catalog_classes_is_reported_somewhere()
    {
        string diagnosticsFolder = "Diagnostics" + Path.DirectorySeparatorChar;
        string[] sources = [.. Directory.EnumerateFiles(SourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(SourceRoot, file).StartsWith(diagnosticsFolder, StringComparison.Ordinal))
            .Select(File.ReadAllText)];

        var neverReported = new List<string>();
        foreach (Type catalogClass in CatalogClasses)
        {
            foreach (string name in CodeNames(catalogClass))
            {
                string use = catalogClass.Name + "." + name;
                if (!sources.Any(text => Regex.IsMatch(text, @"\b" + Regex.Escape(use) + @"\b")))
                {
                    neverReported.Add(use);
                }
            }
        }

        Assert.Empty(neverReported);
    }

    private static IEnumerable<string> CodeNames(Type catalogClass) =>
        catalogClass.GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(DiagnosticCode))
            .Select(property => property.Name);
}
