namespace Broadside.Tools.DocSnippets.Tests;

/// <summary>
/// Guide code is included from compiled samples: a <c>snippet</c> marker in a page names a region of a sample file, and the fenced
/// block after it must equal that region. <c>--write</c> regenerates the blocks; the check fails while any block differs.
/// </summary>
public sealed class SnippetTests
{
    private const string Sample =
        "using Broadside;\n" +
        "\n" +
        "static void Open()\n" +
        "{\n" +
        "    #region open\n" +
        "    using PdfDocument document = PdfDocument.Open(\"a.pdf\");\n" +
        "    #region inner\n" +
        "    if (document.Pages.Count > 0)\n" +
        "    {\n" +
        "        Console.WriteLine(document.Pages.Count);\n" +
        "    }\n" +
        "    #endregion\n" +
        "    #endregion\n" +
        "}\n";

    private const string Inner =
        "if (document.Pages.Count > 0)\n" +
        "{\n" +
        "    Console.WriteLine(document.Pages.Count);\n" +
        "}";

    private const string SamplePath = "samples/guides/Reading/Program.cs";

    private static string? Read(string path) => path == SamplePath ? Sample : null;

    [Fact]
    public void A_region_is_dedented_and_loses_the_markers_of_regions_inside_it()
    {
        Assert.Equal(
            "using PdfDocument document = PdfDocument.Open(\"a.pdf\");\n" +
            "if (document.Pages.Count > 0)\n" +
            "{\n" +
            "    Console.WriteLine(document.Pages.Count);\n" +
            "}",
            SnippetSync.ExtractRegion(Sample, "open"));
    }

    [Fact]
    public void A_block_that_matches_its_region_is_current()
    {
        string page = Page("```csharp\n" + Inner + "\n```\n", "inner");

        SyncResult result = SnippetSync.Sync(page, "guide.md", Read, "samples/guides/");

        Assert.Empty(result.Problems);
        Assert.Equal(1, result.SnippetCount);
        Assert.Equal(page, result.Text);
    }

    [Fact]
    public void A_stale_or_empty_block_is_regenerated_from_its_region()
    {
        string stale = Page("```csharp\nConsole.WriteLine(\"old\");\n```\n", "inner");
        string empty = Page(string.Empty, "inner");
        string current = Page("```csharp\n" + Inner + "\n```\n", "inner");

        Assert.Equal(current, SnippetSync.Sync(stale, "guide.md", Read, "samples/guides/").Text);
        Assert.Equal(current, SnippetSync.Sync(empty, "guide.md", Read, "samples/guides/").Text);
    }

    [Fact]
    public void A_missing_region_file_or_end_marker_is_a_problem()
    {
        SyncResult region = SnippetSync.Sync(Page(string.Empty, "absent"), "guide.md", Read, "samples/guides/");
        SyncResult file = SnippetSync.Sync(
            "<!-- snippet: samples/guides/Other/Program.cs#open -->\n<!-- endsnippet -->\n", "guide.md", Read, "samples/guides/");
        SyncResult end = SnippetSync.Sync($"<!-- snippet: {SamplePath}#open -->\ntext\n", "guide.md", Read, "samples/guides/");

        Assert.Contains("no region 'absent'", Assert.Single(region.Problems).Message, StringComparison.Ordinal);
        Assert.Contains("not found", Assert.Single(file.Problems).Message, StringComparison.Ordinal);
        Assert.Contains("endsnippet", Assert.Single(end.Problems).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_marker_shown_inside_a_code_block_is_left_alone()
    {
        string page = $"```md\n<!-- snippet: {SamplePath}#absent -->\n<!-- endsnippet -->\n```\n";

        SyncResult result = SnippetSync.Sync(page, "guide.md", Read, "samples/guides/");

        Assert.Empty(result.Problems);
        Assert.Equal(0, result.SnippetCount);
        Assert.Equal(page, result.Text);
    }

    [Fact]
    public void A_snippet_from_outside_the_compiled_samples_is_a_problem()
    {
        SyncResult result = SnippetSync.Sync(
            "<!-- snippet: src/Broadside/PdfDocument.cs#open -->\n<!-- endsnippet -->\n", "guide.md", Read, "samples/guides/");

        SnippetProblem problem = Assert.Single(result.Problems);
        Assert.Equal(1, problem.Line);
        Assert.Contains("samples/guides/", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_guide_on_the_site_matches_its_samples()
    {
        string root = FindRepositoryRoot();

        IReadOnlyList<SnippetProblem> problems = SnippetSync.CheckSite(root, write: false, out int snippets);

        Assert.Empty(problems);
        Assert.True(snippets > 0, "the site's guides include no samples");
    }

    private static string Page(string block, string region) =>
        $"# Guide\n\nText.\n\n<!-- snippet: {SamplePath}#{region} -->\n{block}<!-- endsnippet -->\n\nMore text.\n";

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Broadside.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Broadside.slnx not found above the test assembly.");
    }
}
