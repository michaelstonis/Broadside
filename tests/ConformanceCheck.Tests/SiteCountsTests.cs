namespace Broadside.Tools.ConformanceCheck.Tests;

/// <summary>
/// The per-specification counts generated into the site's conformance page (<c>--site-page</c>): rendered from the maps, current
/// only while the block between the markers equals them, and regenerated in place by <c>--write-site-page</c>.
/// </summary>
public sealed class SiteCountsTests
{
    private const string ValidCounts =
        "| Specification | Map | done | partial | not started | n/a | Rows |\n" +
        "|---|---|---:|---:|---:|---:|---:|\n" +
        "| x.md | `x.md` | 1 | 1 | 1 | 1 | 4 |\n" +
        "| **Total** |  | 1 | 1 | 1 | 1 | 4 |\n";

    [Fact]
    public void The_counts_have_a_row_per_map_in_status_order_and_a_total()
    {
        (ConformanceReport report, string root) = Check("valid");

        Assert.Equal(ValidCounts, SiteCounts.Render(report, root));
    }

    [Fact]
    public void A_map_listed_in_the_conformance_readme_is_named_by_its_specification()
    {
        string root = Directory.CreateTempSubdirectory("site-counts-").FullName;
        try
        {
            CopyDirectory(Path.Combine(FixturesDirectory, "valid"), root);
            File.WriteAllText(
                Path.Combine(root, "docs", "conformance", "README.md"),
                "| File | Specification |\n|---|---|\n| [x.md](./x.md) | Fixture specification X, cited by rule (open decision 12) |\n");

            ConformanceReport report = ConformanceChecker.Check(root, strict: false);

            Assert.StartsWith(
                "| Specification | Map | done | partial | not started | n/a | Rows |\n|---|---|---:|---:|---:|---:|---:|\n| Fixture specification X, cited by rule | `x.md` | 1 | 1 | 1 | 1 | 4 |\n",
                SiteCounts.Render(report, root),
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_page_is_current_only_while_its_block_equals_the_counts()
    {
        string current = Page(ValidCounts);
        string stale = Page(ValidCounts.Replace("| 1 | 1 | 1 | 1 | 4 |\n| **Total**", "| 2 | 0 | 1 | 1 | 4 |\n| **Total**", StringComparison.Ordinal));

        Assert.True(SiteCounts.IsCurrent(current, ValidCounts));
        Assert.True(SiteCounts.IsCurrent(current.Replace("\n", "\r\n", StringComparison.Ordinal), ValidCounts));
        Assert.False(SiteCounts.IsCurrent(stale, ValidCounts));
        Assert.False(SiteCounts.IsCurrent("# Conformance\n\nNo markers.\n", ValidCounts));
    }

    [Fact]
    public void Updating_replaces_only_the_block_between_the_markers()
    {
        string updated = SiteCounts.Update(Page(string.Empty), ValidCounts);

        Assert.Equal(Page(ValidCounts), updated);
        Assert.Throws<InvalidDataException>(() => SiteCounts.Update("# Conformance\n", ValidCounts));
    }

    private static string FixturesDirectory => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static string Page(string block) =>
        $"# Conformance\n\nBefore.\n\n{SiteCounts.BeginMarker}\n{block}{SiteCounts.EndMarker}\n\nAfter.\n";

    private static (ConformanceReport Report, string Root) Check(string fixture)
    {
        string root = Path.Combine(FixturesDirectory, fixture);
        return (ConformanceChecker.Check(root, strict: false), root);
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
        }
    }
}
