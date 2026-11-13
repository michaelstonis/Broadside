using System.Buffers;
using System.Globalization;
using System.Reflection;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.Parsing;
using Broadside.TestSupport;
using Broadside.Tests.Filters;

namespace Broadside.Tests.Document;

/// <summary>
/// The public diagnostic catalog (issue #242): typed codes with a stable name, area, default severity and clause, one catalog class
/// per area, and the internal catalog that forwards to it until the report sites move over.
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
        Assert.Equal("FeatureNoted", diagnostic.Code);
        Assert.Equal("FeatureNoted", diagnostic.Kind.Name);
        Assert.Equal(DiagnosticArea.None, diagnostic.Kind.Area);
        Assert.Equal(DiagnosticSeverity.Information, diagnostic.Kind.DefaultSeverity);
        Assert.Null(diagnostic.Kind.Clause);
        Assert.False(DiagnosticCatalog.TryGet("FeatureNoted", out _));
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
    /// The parity record: every code with its area, default severity and clause, generated from the internal catalog when the public
    /// one was made (issue #242). Moving report sites to the public catalog (issues #243 to #246) leaves it unchanged; a code removed
    /// because it is never reported is listed in its PR.
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
    public void Every_internal_code_forwards_to_the_public_code_of_the_same_name()
    {
        string[] internalNames = [.. InternalCodes(typeof(DiagnosticCodes)), .. InternalCodes(typeof(CosRepairCodes))];

        Assert.NotEmpty(internalNames);
        foreach (string name in internalNames)
        {
            Assert.True(DiagnosticCatalog.TryGet(name, out DiagnosticCode? code), name);
            Assert.Equal(name, code.Name);
        }

        Assert.Equal(internalNames.Length, internalNames.Distinct(StringComparer.Ordinal).Count());
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

    private static IEnumerable<string> InternalCodes(Type holder)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        foreach (PropertyInfo property in holder.GetProperties(flags).Where(p => p.PropertyType == typeof(string)))
        {
            string value = (string)property.GetValue(null)!;
            Assert.Equal(property.Name, value);
            yield return value;
        }

        foreach (FieldInfo field in holder.GetFields(flags).Where(f => f.IsLiteral && f.FieldType == typeof(string)))
        {
            string value = (string)field.GetRawConstantValue()!;
            Assert.Equal(field.Name, value);
            yield return value;
        }
    }

    private sealed class NotingFilter : IStreamFilter
    {
        public CosName Name { get; } = new("NotedDecode");

        public void Decode(ReadOnlyMemory<byte> encoded, IBufferWriter<byte> output, FilterContext context)
        {
            context.Report("FeatureNoted", DiagnosticSeverity.Information, "The stream uses a feature that is read and preserved but not supported.");
            output.Write(encoded.Span);
        }
    }
}
