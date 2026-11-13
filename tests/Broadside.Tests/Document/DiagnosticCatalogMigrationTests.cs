using System.Reflection;
using System.Text.RegularExpressions;
using Broadside.Diagnostics;
using Broadside.Parsing;

namespace Broadside.Tests.Document;

/// <summary>
/// The internal diagnostic catalog is being replaced by the public one area by area (issues #243 to #246). These architecture tests
/// show that the areas already moved have no report site, and no internal forwarder, left on the internal catalog.
/// </summary>
public sealed class DiagnosticCatalogMigrationTests
{
    /// <summary>The catalog classes issue #243 moved: file structure, objects, security and the document catalog.</summary>
    private static readonly Type[] FileStructureAndCatalogAreas =
    [
        typeof(FileStructureDiagnostics),
        typeof(ObjectSyntaxDiagnostics),
        typeof(SecurityDiagnostics),
        typeof(DocumentDiagnostics),
        typeof(PageDiagnostics),
        typeof(SavingDiagnostics),
        typeof(NavigationDiagnostics),
        typeof(OptionalContentDiagnostics),
        typeof(FileSpecificationDiagnostics),
    ];

    [Fact]
    public void No_report_site_for_file_structure_objects_security_and_the_catalog_uses_the_internal_catalog()
    {
        AssertMigrated(FileStructureAndCatalogAreas);
    }

    [Fact]
    public void The_object_syntax_internal_catalog_is_gone()
    {
        Assert.Null(typeof(DiagnosticCodes).Assembly.GetType("Broadside.Objects.CosRepairCodes"));
    }

    /// <summary>
    /// Asserts that none of the codes of <paramref name="catalogClasses"/> is a member of the internal catalog and that no source file
    /// outside the internal catalog files names one through <c>DiagnosticCodes.</c> or <c>CosRepairCodes.</c>. Tickets that move more
    /// areas pass their own class list from their own test.
    /// </summary>
    internal static void AssertMigrated(IEnumerable<Type> catalogClasses)
    {
        string[] names = [.. catalogClasses.SelectMany(CodeNames)];
        Assert.NotEmpty(names);

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        string[] stillInternal = [.. typeof(DiagnosticCodes).GetProperties(flags).Select(p => p.Name).Intersect(names, StringComparer.Ordinal)];
        Assert.Empty(stillInternal);

        var reference = new Regex(@"\b(?:DiagnosticCodes|CosRepairCodes)\.(" + string.Join('|', names) + @")\b", RegexOptions.CultureInvariant);
        string sourceRoot = Path.Combine(RepositoryRoot(), "src");
        var offenders = new List<string>();
        foreach (string file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file).StartsWith("DiagnosticCodes", StringComparison.Ordinal) || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in reference.Matches(File.ReadAllText(file)))
            {
                offenders.Add($"{Path.GetRelativePath(sourceRoot, file)}: {match.Value}");
            }
        }

        Assert.Empty(offenders);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Broadside.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find Broadside.slnx above the test output directory.");
    }

    private static IEnumerable<string> CodeNames(Type catalogClass) =>
        catalogClass.GetProperties(BindingFlags.Public | BindingFlags.Static).Where(p => p.PropertyType == typeof(DiagnosticCode)).Select(p => p.Name);
}
