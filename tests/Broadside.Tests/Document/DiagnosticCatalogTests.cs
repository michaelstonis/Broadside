using System.Buffers;
using System.Globalization;
using System.Reflection;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.Tests.Filters;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>
/// The public diagnostic catalog (issue #242): typed codes with a stable name, area, default severity and clause, one catalog class
/// per area.
/// </summary>
public sealed class DiagnosticCatalogTests
{
    [Fact]
    public void A_repaired_file_reports_the_typed_code_beside_the_same_string_code()
    {
        string text = Encoding.Latin1.GetString(Corpus.Bytes("empty-page.pdf")).Replace("startxref", "startxrex", StringComparison.Ordinal);

        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("StartxrefMissing", diagnostic.Code);
        Assert.Equal(FileStructureDiagnostics.StartxrefMissing, diagnostic.Kind);
        Assert.Same(FileStructureDiagnostics.StartxrefMissing, diagnostic.Kind);
        Assert.Equal(DiagnosticArea.FileStructure, diagnostic.Kind.Area);
        Assert.Equal("ISO 32000-2 §7.5.5", diagnostic.Kind.Clause);
    }

    [Fact]
    public void A_code_a_third_party_filter_reports_is_outside_the_catalog_and_keeps_its_name()
    {
        using PdfDocument document = PdfDocument.Open(
            FilterTesting.FileWithStream("/Filter /NotedDecode", "data"u8),
            new PdfOptions().UseFilter(new NotingFilter()));

        _ = document.DecodeStream((CosStream)document.Resolve(FilterTesting.StreamReference));

        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("Tests.FeatureNoted", diagnostic.Code);
        Assert.Equal("Tests.FeatureNoted", diagnostic.Kind.Name);
        Assert.Equal(DiagnosticArea.None, diagnostic.Kind.Area);
        Assert.Equal(DiagnosticSeverity.Information, diagnostic.Kind.DefaultSeverity);
        Assert.Null(diagnostic.Kind.Clause);
        Assert.False(DiagnosticCatalog.TryGet("Tests.FeatureNoted", out _));
    }

    [Fact]
    public void A_diagnostic_made_from_a_typed_code_has_its_name_as_the_string_code()
    {
        var diagnostic = new Diagnostic(FontDiagnostics.FontWidthsInvalid, DiagnosticSeverity.Warning, "Widths is not an array.");

        Assert.Equal("FontWidthsInvalid", diagnostic.Code);
        Assert.Same(FontDiagnostics.FontWidthsInvalid, diagnostic.Kind);
        Assert.Equal("Warning FontWidthsInvalid: Widths is not an array.", diagnostic.ToString());
    }

    [Fact]
    public void Typed_codes_are_equal_by_name_and_found_by_name()
    {
        Assert.True(DiagnosticCatalog.TryGet("PageTreeCountMismatch", out DiagnosticCode? code));
        Assert.Same(PageDiagnostics.PageTreeCountMismatch, code);
        Assert.True(code == PageDiagnostics.PageTreeCountMismatch);
        Assert.False(code != PageDiagnostics.PageTreeCountMismatch);
        Assert.NotEqual(PageDiagnostics.PageTreeCycle, code);
        Assert.Equal("PageTreeCountMismatch", code.ToString());
        Assert.Equal(new Diagnostic("PageTreeCountMismatch", DiagnosticSeverity.Warning, "m").Kind.GetHashCode(), code.GetHashCode());
    }

    /// <summary>
    /// The parity record: every code with its area, default severity and clause, recorded when the public catalog was made (issue #242)
    /// and changed only by adding, removing or re-rating a code.
    /// </summary>
    [Fact]
    public Task The_catalog_lists_every_code_with_its_area_default_severity_and_clause()
    {
        var text = new StringBuilder();
        foreach (DiagnosticCode code in DiagnosticCatalog.All)
        {
            text.Append(CultureInfo.InvariantCulture, $"{code.Area}\t{code.Name}\t{code.DefaultSeverity}\t{code.Clause}").Append('\n');
        }

        return Verify(text.ToString());
    }

    [Fact]
    public void Every_code_has_a_description_of_what_it_means_and_how_it_is_repaired()
    {
        Assert.All(DiagnosticCatalog.All, code =>
        {
            Assert.False(string.IsNullOrWhiteSpace(code.Description), code.Name);
            Assert.EndsWith(".", code.Description, StringComparison.Ordinal);
            Assert.DoesNotContain("|", code.Description, StringComparison.Ordinal);
        });
        Assert.Equal("The file has no startxref keyword. The cross-reference information is rebuilt by scanning the file.", FileStructureDiagnostics.StartxrefMissing.Description);
    }

    [Fact]
    public void Codes_outside_the_catalog_have_no_description()
    {
        Assert.Null(DiagnosticCode.Create("Tests", "FeatureNoted", DiagnosticSeverity.Information).Description);
        Assert.Null(new Diagnostic("Tests.Unknown", DiagnosticSeverity.Warning, "m").Kind.Description);
    }

    /// <summary>
    /// The published catalog page (issue #263) is generated from the catalog: a code added, renamed, re-rated or re-described without
    /// regenerating <c>site/articles/diagnostics.md</c> fails here as well as in the documentation build.
    /// </summary>
    [Fact]
    public void The_published_catalog_page_is_current()
    {
        string page = Path.Combine(RepositoryRoot(), "site", "articles", "diagnostics.md");

        Assert.True(
            Broadside.Tools.DiagnosticCatalogPage.CatalogPage.IsCurrent(File.ReadAllText(page)),
            "site/articles/diagnostics.md is stale; regenerate it with dotnet run --project tools/DiagnosticCatalogPage -- site/articles/diagnostics.md --write");
    }

    [Fact]
    public void The_page_lists_every_code_with_its_severity_clause_and_description()
    {
        string block = Broadside.Tools.DiagnosticCatalogPage.CatalogPage.Render();

        Assert.All(DiagnosticCatalog.All, code => Assert.Contains($"| `{code.Name}` | {code.DefaultSeverity} | {code.Clause} | {code.Description} |", block, StringComparison.Ordinal));
        string page = string.Concat(Broadside.Tools.DiagnosticCatalogPage.CatalogPage.BeginMarker, "\n", block, Broadside.Tools.DiagnosticCatalogPage.CatalogPage.EndMarker, "\n");
        Assert.True(Broadside.Tools.DiagnosticCatalogPage.CatalogPage.IsCurrent(page));
        Assert.False(Broadside.Tools.DiagnosticCatalogPage.CatalogPage.IsCurrent(page.Replace("`StartxrefMissing`", "`StartxrefAbsent`", StringComparison.Ordinal)));
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Broadside.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (Broadside.slnx) is not above the test output.");
    }

    [Fact]
    public void Each_catalog_class_lists_its_own_codes_once_under_its_area()
    {
        Type[] catalogClasses = [.. typeof(DiagnosticCode).Assembly.GetExportedTypes()
            .Where(type => type.Namespace == "Broadside.Diagnostics" && type.IsAbstract && type.IsSealed && type.Name.EndsWith("Diagnostics", StringComparison.Ordinal))];
        var declared = new List<DiagnosticCode>();
        foreach (Type type in catalogClasses)
        {
            var area = Enum.Parse<DiagnosticArea>(type.Name[..^"Diagnostics".Length]);
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Static).Where(p => p.PropertyType == typeof(DiagnosticCode)))
            {
                var code = (DiagnosticCode)property.GetValue(null)!;
                Assert.Equal(property.Name, code.Name);
                Assert.Equal(area, code.Area);
                Assert.False(string.IsNullOrEmpty(code.Clause), code.Name);
                declared.Add(code);
            }
        }

        Assert.Equal(Enum.GetValues<DiagnosticArea>().Length - 1, catalogClasses.Length);
        Assert.Equal(declared.Count, DiagnosticCatalog.All.Count);
        Assert.Equal(declared.Count, DiagnosticCatalog.All.Distinct().Count());
        Assert.All(declared, code => Assert.Contains(code, DiagnosticCatalog.All));
    }

    private sealed class NotingFilter : IStreamFilter
    {
        public CosName Name { get; } = new("NotedDecode");

        public void Decode(ReadOnlyMemory<byte> encoded, IBufferWriter<byte> output, FilterContext context)
        {
            context.Report(DiagnosticCode.Create("Tests", "FeatureNoted", DiagnosticSeverity.Information), DiagnosticSeverity.Information, "The stream uses a feature that is read and preserved but not supported.");
            output.Write(encoded.Span);
        }
    }
}
