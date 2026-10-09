namespace Broadside.Tools.ConformanceCheck.Tests;

/// <summary>
/// One fixture repository per rule under <c>Fixtures/</c> (copied next to the test assembly), each with a <c>docs/conformance/x.md</c>,
/// a <c>tests/Foo.Tests/FooTests.cs</c> and a <c>src/Foo/Foo.cs</c>, plus a run against the real checkout so <c>dotnet test</c> fails when the map is wrong.
/// </summary>
public class ConformanceCheckerTests
{
    private const string FixtureFile = "docs/conformance/x.md";

    [Fact]
    public void Valid_map_has_no_violations()
    {
        ConformanceReport report = Check("valid");

        Assert.Empty(report.Violations);
        Assert.True(report.IsValid);
        ConformanceFile file = Assert.Single(report.Files);
        Assert.Equal(FixtureFile, file.File);
        Assert.Equal(4, file.Rows.Count);
        Assert.Equal(1, file.Count(ConformanceStatus.Done));
        Assert.Equal(1, file.Count(ConformanceStatus.Partial));
        Assert.Equal(1, file.Count(ConformanceStatus.NotApplicable));
        Assert.Equal(1, file.Count(ConformanceStatus.NotStarted));
    }

    [Fact]
    public void Valid_map_summary_lists_every_status_and_a_total()
    {
        ConformanceReport report = Check("valid");

        string expected = string.Join(
            Environment.NewLine,
            "| Specification | not started | partial | done | n/a | Rows |",
            "|---|---:|---:|---:|---:|---:|",
            "| x.md | 1 | 1 | 1 | 1 | 4 |",
            "| **Total** | 1 | 1 | 1 | 1 | 4 |");
        Assert.Equal(expected, report.ToMarkdownSummary());
    }

    [Fact]
    public void Done_row_without_tests_is_a_violation()
    {
        AssertSingleViolation("done-without-tests", $"{FixtureFile}:5: done row must name at least one test in the Tests cell");
    }

    [Fact]
    public void Done_row_naming_a_test_that_does_not_exist_is_a_violation()
    {
        AssertSingleViolation(
            "done-with-missing-test",
            $"{FixtureFile}:7: test 'Foo.Tests.FooTests.Helper' was not found in tests/: no class 'Foo.Tests.FooTests.Helper' and no [Fact]/[Theory] method 'Helper' on class 'Foo.Tests.FooTests'");
    }

    [Fact]
    public void Unknown_status_is_a_violation()
    {
        AssertSingleViolation("bad-status", $"{FixtureFile}:5: status 'in progress' is not one of: not started, partial, done, n/a");
    }

    [Fact]
    public void Row_with_wrong_cell_count_is_a_violation()
    {
        AssertSingleViolation("wrong-cell-count", $"{FixtureFile}:5: expected 6 cells (Clause, Title, Status, Implementing types, Tests, Notes) but found 5");
    }

    [Fact]
    public void Na_row_without_notes_is_a_violation()
    {
        AssertSingleViolation("na-without-notes", $"{FixtureFile}:5: n/a row must say why in the Notes cell");
    }

    [Fact]
    public void Duplicate_clause_is_a_violation()
    {
        AssertSingleViolation("duplicate-clause", $"{FixtureFile}:7: duplicate clause '1' (first at line 5)");
    }

    [Fact]
    public void Implementing_type_that_does_not_exist_is_a_violation()
    {
        AssertSingleViolation("missing-implementing-type", $"{FixtureFile}:5: implementing type 'Bar' is not declared as a class, struct, interface, enum or record in src/");
    }

    [Fact]
    public void Partial_row_without_tests_or_notes_passes_unless_strict()
    {
        Assert.Empty(Check("partial-without-tests").Violations);

        ConformanceReport strict = Check("partial-without-tests", strict: true);

        Assert.Equal(
            [
                $"{FixtureFile}:7: partial row must name at least one test in the Tests cell (--strict)",
                $"{FixtureFile}:7: partial row must say what is missing in the Notes cell (--strict)",
            ],
            strict.Violations.Select(static violation => violation.ToString()).ToArray());
    }

    [Fact]
    public void Root_without_a_conformance_directory_throws()
    {
        Assert.Throws<DirectoryNotFoundException>(() => ConformanceChecker.Check(Path.Combine(FixturesDirectory, "valid", "src"), strict: false));
    }

    [Fact]
    public void Repository_conformance_map_is_valid()
    {
        string root = RepositoryLocator.FindRoot(AppContext.BaseDirectory)
            ?? throw new DirectoryNotFoundException($"Could not find {RepositoryLocator.Marker} above '{AppContext.BaseDirectory}'.");

        ConformanceReport report = ConformanceChecker.Check(root, strict: false);

        Assert.Empty(report.Violations.Select(static violation => violation.ToString()));
        Assert.NotEmpty(report.Files);
        Assert.Contains(report.Files, static file => file.File == "docs/conformance/iso-32000-2.md");
        Assert.DoesNotContain(report.Files, static file => file.File.EndsWith("README.md", StringComparison.Ordinal));
    }

    private static string FixturesDirectory => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static ConformanceReport Check(string fixture, bool strict = false) => ConformanceChecker.Check(Path.Combine(FixturesDirectory, fixture), strict);

    private static void AssertSingleViolation(string fixture, string expected)
    {
        ConformanceReport report = Check(fixture);

        Violation violation = Assert.Single(report.Violations);
        Assert.Equal(expected, violation.ToString());
        Assert.False(report.IsValid);
    }
}
