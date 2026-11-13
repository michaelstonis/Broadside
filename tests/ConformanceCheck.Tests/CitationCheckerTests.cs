namespace Broadside.Tools.ConformanceCheck.Tests;

/// <summary>
/// The clause-citation check, driven through <see cref="ConformanceChecker.Check(string, bool, CitationOptions?)"/> over a copy of
/// <c>Fixtures/citations</c> (a package <c>Foo</c> with its public API and XML documentation, an unchecked package <c>Bar</c>, a map
/// and a citation policy) that each test edits, plus a run against this checkout and the packages' real documentation.
/// </summary>
public sealed class CitationCheckerTests
{
    private const string Map = "docs/conformance/x.md";
    private const string Policy = "docs/conformance/citation-policy.json";
    private const string Documentation = "xmldoc/Foo.xml";

    [Fact]
    public void Cited_types_rows_and_exemptions_pass()
    {
        using var fixture = new FixtureCopy();

        Assert.Empty(fixture.Check().Violations.Select(static violation => violation.ToString()));
    }

    [Fact]
    public void Removing_a_citation_from_a_public_type_fails_naming_the_type()
    {
        using var fixture = new FixtureCopy();
        fixture.Edit(Documentation, static text => text.Replace("<remarks>ISO 32000-2 §1.2.</remarks>", string.Empty, StringComparison.Ordinal));

        string[] violations = fixture.Check().Violations.Select(static violation => violation.ToString()).ToArray();

        Assert.Contains(
            "src/Foo/PublicAPI.Unshipped.txt:2: public type 'Foo.Foo' cites no specification: add <remarks>ISO 32000-2 §x.y.z</remarks> (or another form in docs/conformance/citation-policy.json), or exempt it there with a reason",
            violations);
        Assert.Contains($"{Map}:7: implementing type 'Foo' does not cite clause 1.2 of ISO 32000-2 in its XML documentation or its members'", violations);

        // The nested type was covered by Foo's citation only.
        Assert.Contains(violations, static violation => violation.StartsWith("src/Foo/PublicAPI.Unshipped.txt:4: public type 'Foo.Foo.Nested' cites no specification", StringComparison.Ordinal));
        Assert.Equal(3, violations.Length);
    }

    [Fact]
    public void A_citation_in_a_form_the_policy_does_not_list_does_not_count()
    {
        using var fixture = new FixtureCopy();
        fixture.Edit(Documentation, static text => text.Replace("ITU-T T.81 §A.1.", "See the JPEG standard.", StringComparison.Ordinal));

        Violation violation = Assert.Single(fixture.Check().Violations);

        Assert.Equal("src/Foo/PublicAPI.Unshipped.txt:9", $"{violation.File}:{violation.Line}");
        Assert.Contains("public type 'Foo.Generic`1' cites no specification", violation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_row_naming_a_type_that_does_not_exist_fails()
    {
        using var fixture = new FixtureCopy();
        fixture.Edit(Map, static text => text.Replace("| `Foo` |", "| `Foo`, `Baz` |", StringComparison.Ordinal));

        Violation violation = Assert.Single(fixture.Check().Violations);

        Assert.Equal($"{Map}:7: implementing type 'Baz' is not declared as a class, struct, interface, enum or record in src/", violation.ToString());
    }

    [Fact]
    public void A_row_naming_a_type_that_does_not_cite_its_clause_fails()
    {
        using var fixture = new FixtureCopy();
        fixture.Edit(Map, static text => text + "| 4.1 | Writing | partial | `Foo` | | |\n");

        Violation violation = Assert.Single(fixture.Check().Violations);

        Assert.Equal($"{Map}:11: implementing type 'Foo' does not cite clause 4.1 of ISO 32000-2 in its XML documentation or its members'", violation.ToString());
    }

    [Fact]
    public void A_clause_cited_after_another_document_is_that_documents_clause()
    {
        // Parser's member cites "ISO 32000-2 §2.1.1 and ITU-T T.81 §3": §3 belongs to ITU-T T.81, not to ISO 32000-2.
        using var fixture = new FixtureCopy();
        fixture.Edit(Map, static text => text.Replace("| 3 | Drawing | partial | `Bar` |", "| 3 | Drawing | partial | `Parser` |", StringComparison.Ordinal));

        Violation violation = Assert.Single(fixture.Check().Violations);

        Assert.Equal($"{Map}:10: implementing type 'Parser' does not cite clause 3 of ISO 32000-2 in its XML documentation or its members'", violation.ToString());
    }

    [Fact]
    public void A_cited_clause_covers_its_parent_row_and_its_children_but_not_siblings_or_grandchildren()
    {
        // Parser cites §2.1.1: that covers rows 2.1.1, 2.1 and 2 (it is one of their sub-clauses) and 2.1.1.4 (its child),
        // but not the sibling 2.1.2 or the grandchild 2.1.1.4.2.
        using var fixture = new FixtureCopy();
        fixture.Edit(
            Map,
            static text => text
                + "| 2 | Syntax | partial | `Parser` | | |\n"
                + "| 2.1 | Grammar | partial | `Parser` | | |\n"
                + "| 2.1.2 | Accepting | partial | `Parser` | | |\n"
                + "| 2.1.1.4.2 | Detail | partial | `Parser` | | |\n");

        Assert.Equal(
            [
                $"{Map}:13: implementing type 'Parser' does not cite clause 2.1.2 of ISO 32000-2 in its XML documentation or its members'",
                $"{Map}:14: implementing type 'Parser' does not cite clause 2.1.1.4.2 of ISO 32000-2 in its XML documentation or its members'",
            ],
            fixture.Check().Violations.Select(static violation => violation.ToString()).ToArray());
    }

    [Fact]
    public void Removing_an_exemption_fails_for_the_uncited_type()
    {
        using var fixture = new FixtureCopy();
        fixture.Edit(Policy, static text => text.Replace("{ \"symbol\": \"Foo.FooOptions\", \"reason\": \"Options.\" }", string.Empty, StringComparison.Ordinal));

        Violation violation = Assert.Single(fixture.Check().Violations);

        Assert.Equal("src/Foo/PublicAPI.Unshipped.txt:6", $"{violation.File}:{violation.Line}");
        Assert.Contains("public type 'Foo.FooOptions' cites no specification", violation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_exemption_without_a_reason_or_matching_no_type_fails()
    {
        using var fixture = new FixtureCopy();
        fixture.Edit(
            Policy,
            static text => text.Replace("\"reason\": \"Options.\" }", "\"reason\": \"\" }, { \"symbol\": \"Foo.Gone\", \"reason\": \"Removed.\" }", StringComparison.Ordinal));

        Assert.Equal(
            [
                $"{Policy}:15: exemption 'Foo.FooOptions' must say why in its reason",
                $"{Policy}:15: exemption 'Foo.Gone' matches no public type of a checked package; remove it",
            ],
            fixture.Check().Violations.Select(static violation => violation.ToString()).ToArray());
    }

    [Fact]
    public void A_package_missing_from_the_policy_fails()
    {
        using var fixture = new FixtureCopy();
        fixture.Write("src/Qux/Qux.csproj", "<Project />");

        Violation violation = Assert.Single(fixture.Check().Violations);

        Assert.Equal(
            $"{Policy}:10: package 'Qux' under src/ is not listed in the policy's packages: list it as checked, or unchecked with a reason",
            violation.ToString());
    }

    [Fact]
    public void A_checked_package_without_documentation_fails()
    {
        using var fixture = new FixtureCopy();
        fixture.Delete(Documentation);

        Violation violation = Assert.Single(fixture.Check().Violations, static violation => violation.File == "src/Foo");

        Assert.Contains("no XML documentation at 'xmldoc/Foo.xml'", violation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_map_missing_from_the_policy_fails()
    {
        using var fixture = new FixtureCopy();
        fixture.Edit(Policy, static text => text.Replace("\"map\": \"x.md\"", "\"map\": \"y.md\"", StringComparison.Ordinal));

        Violation violation = Assert.Single(fixture.Check().Violations);

        Assert.Equal($"{Map}:1: the map is not listed in {Policy} 'maps', so its rows' citations cannot be checked", violation.ToString());
    }

    [Theory]
    [InlineData("Broadside.Cos.CosArray", "Broadside.Cos.CosArray")]
    [InlineData("abstract Broadside.Rendering.Backend<TSurface>", "Broadside.Rendering.Backend`1")]
    [InlineData("Broadside.Outer<T>.Inner<U, V>", "Broadside.Outer`1.Inner`2")]
    [InlineData("static Broadside.Diagnostics.FontDiagnostics", "Broadside.Diagnostics.FontDiagnostics")]
    [InlineData("Broadside.Cos.CosArray.Count.get -> int", null)]
    [InlineData("Broadside.Cos.CosKind.Name = 3 -> Broadside.Cos.CosKind", null)]
    [InlineData("Broadside.Cos.CosArray.Add(Broadside.Cos.CosObject! item) -> void", null)]
    [InlineData("#nullable enable", null)]
    [InlineData("*REMOVED*Broadside.Gone", null)]
    public void Public_api_type_lines_become_documentation_names(string line, string? expected)
    {
        Assert.Equal(expected, PublicApiReader.ParseTypeLine(line));
    }

    [Fact]
    public void Repository_public_types_and_rows_cite_their_clauses()
    {
        // The project references every checked package, so their XML documentation sits next to this test assembly.
        string root = RepositoryLocator.FindRoot(AppContext.BaseDirectory)
            ?? throw new DirectoryNotFoundException($"Could not find {RepositoryLocator.Marker} above '{AppContext.BaseDirectory}'.");

        ConformanceReport report = ConformanceChecker.Check(root, strict: false, new CitationOptions(DocumentationDirectory: AppContext.BaseDirectory));

        Assert.Empty(report.Violations.Select(static violation => violation.ToString()));
    }

    /// <summary>A copy of <c>Fixtures/citations</c> in a temporary directory, deleted on dispose.</summary>
    private sealed class FixtureCopy : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "broadside-citations-" + Guid.NewGuid().ToString("N"));

        public FixtureCopy()
        {
            string source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "citations");
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(_root, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }

        public ConformanceReport Check() => ConformanceChecker.Check(_root, strict: false, new CitationOptions(DocumentationDirectory: Path.Combine(_root, "xmldoc")));

        public void Edit(string relativePath, Func<string, string> edit)
        {
            string path = Path.Combine(_root, relativePath);
            string before = File.ReadAllText(path);
            string after = edit(before);
            Assert.NotEqual(before, after);
            File.WriteAllText(path, after);
        }

        public void Write(string relativePath, string text)
        {
            string path = Path.Combine(_root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        public void Delete(string relativePath) => File.Delete(Path.Combine(_root, relativePath));

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
