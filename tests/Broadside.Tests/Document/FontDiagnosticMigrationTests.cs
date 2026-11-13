using System.Text.RegularExpressions;
using Broadside.Diagnostics;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>
/// The report sites of the font, CharString, CFF, Type 1, Type 3, CMap and text areas use the public catalog (issue #245): the
/// internal catalog no longer has a group for these areas, and nothing outside it names one of their codes through it.
/// </summary>
public sealed partial class FontDiagnosticMigrationTests
{
    private static readonly DiagnosticArea[] Areas =
    [
        DiagnosticArea.Font, DiagnosticArea.CharString, DiagnosticArea.Cff, DiagnosticArea.Type1,
        DiagnosticArea.Type3, DiagnosticArea.CMap, DiagnosticArea.Text,
    ];

    [GeneratedRegex(@"\b(?:DiagnosticCodes|CosRepairCodes)\.([A-Za-z0-9_]+)")]
    private static partial Regex InternalCatalogUse();

    [Fact]
    public void No_source_file_reaches_a_font_text_or_cmap_code_through_the_internal_catalog()
    {
        string source = Path.Combine(Corpus.Directory, "..", "..", "src", "Broadside");
        HashSet<string> names = [.. DiagnosticCatalog.All.Where(code => Areas.Contains(code.Area)).Select(code => code.Name)];
        Assert.NotEmpty(names);

        var offenders = new List<string>();
        foreach (string file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file).Replace('\\', '/');
            if (relative.StartsWith("Parsing/DiagnosticCodes", StringComparison.Ordinal))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            foreach (Match match in InternalCatalogUse().Matches(text))
            {
                if (names.Contains(match.Groups[1].Value) || relative.StartsWith("Fonts/", StringComparison.Ordinal))
                {
                    offenders.Add($"{relative}: {match.Value}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_internal_catalog_has_no_group_left_for_these_areas()
    {
        string parsing = Path.Combine(Corpus.Directory, "..", "..", "src", "Broadside", "Parsing");
        string[] groups = ["Fonts", "FontResolver", "CMaps", "Cff", "CidCff", "CharStrings", "Type1", "Type3", "Text"];

        Assert.All(groups, group => Assert.False(File.Exists(Path.Combine(parsing, $"DiagnosticCodes.{group}.cs")), group));
    }
}
