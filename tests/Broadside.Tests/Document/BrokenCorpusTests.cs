using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>
/// The deliberately broken corpus files (tests/Corpus/README.md, "Deliberately broken files"; ADR 0005): each opens leniently with
/// exactly the documented diagnostics and the same pages as its intact counterpart, and strict mode throws the first of them.
/// ISO 32000-2 §7.3.8.2, §7.3.10, §7.5.4, §7.5.5.
/// </summary>
public class BrokenCorpusTests
{
    /// <summary>File, intact counterpart, then the expected diagnostics as "Code object offset" ("-" when unknown), in order.</summary>
    public static TheoryData<string, string, string[]> BrokenFiles => new()
    {
        { "broken-xref-offsets.pdf", "empty-page.pdf", ["XrefEntryOffsetInvalid 1 19", "XrefEntryOffsetInvalid 2 68", "XrefEntryOffsetInvalid 3 125"] },
        { "missing-endobj.pdf", "empty-page.pdf", ["MissingEndobj 3 196"] },
        { "wrong-stream-length.pdf", "text-standard14.pdf", ["StreamLengthInvalid 4 267"] },
        { "no-xref.pdf", "empty-page.pdf", ["StartxrefMissing - -", "TrailerMissing - -"] },
        { "startxref-wrong.pdf", "empty-page.pdf", ["StartxrefInvalid - 303"] },
    };

    [Theory]
    [MemberData(nameof(BrokenFiles))]
    public void A_broken_file_opens_leniently_with_exactly_the_documented_diagnostics_and_the_intact_pages(
        string fileName,
        string intactFileName,
        string[] expected)
    {
        using PdfDocument intact = PdfDocument.Open(Corpus.Path(intactFileName));
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName));

        Assert.Equal(Pages(intact), Pages(document));
        foreach (PdfPage page in document.Pages)
        {
            _ = document.Resolve(page.Dictionary.TryGetValue(new CosName("Contents"), out CosObject? contents) ? contents : null);
        }

        Assert.Equal(expected, document.Diagnostics.Select(Describe));
        Assert.All(document.Diagnostics, static diagnostic => Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity));
    }

    [Theory]
    [MemberData(nameof(BrokenFiles))]
    public void Strict_mode_throws_on_a_broken_file_with_the_code_of_its_first_diagnostic(string fileName, string intactFileName, string[] expected)
    {
        _ = intactFileName;

        DiagnosticException error = Assert.Throws<DiagnosticException>(() =>
        {
            using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName), new PdfOptions().UseStrict());
            foreach (PdfPage page in document.Pages)
            {
                _ = document.Resolve(page.Dictionary.TryGetValue(new CosName("Contents"), out CosObject? contents) ? contents : null);
            }
        });

        Assert.Equal(expected[0], Describe(error.Diagnostic));
    }

    [Fact]
    public void A_wrong_stream_length_is_recovered_from_endstream_without_changing_the_length_entry()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("wrong-stream-length.pdf"));

        CosStream contents = Assert.IsType<CosStream>(document.Resolve(new CosReference(4, 0)));

        Assert.Equal("BT /F1 24 Tf 72 700 Td (Length is wrong) Tj ET", Encoding.Latin1.GetString(contents.EncodedData.Span));
        Assert.Equal(46, contents.EncodedData.Length); // §7.3.8.1: the end-of-line marker before endstream is not data.
        Assert.Equal(new CosInteger(10), contents.Dictionary[new CosName("Length")]);
        Assert.False(contents.IsDirty);
    }

    private static string Pages(PdfDocument document) => DocumentProjection.Of(document).Pages;

    private static string Describe(Diagnostic diagnostic) =>
        $"{diagnostic.Code} {diagnostic.ObjectReference?.ObjectNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"} "
        + $"{diagnostic.Offset?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}";
}
