using System.Text;
using Broadside.Diagnostics;

namespace Broadside.Tests.Document;

/// <summary>
/// The line layout ISO 32000-2 requires of the header, the cross-reference subsection headers and the end of the file, and the
/// trailer's ID in a PDF 2.0 file. Each case is a deviation the veraPDF agreement check of the real-world corpus gate (issue #47)
/// found strict mode accepting; each is read leniently with one diagnostic, and strict mode throws it. ISO 32000-2 §7.5.2, §7.5.4,
/// §7.5.5 Table 15.
/// </summary>
public class FileLayoutTests
{
    public static TheoryData<string, string> Deviations => new()
    {
        { "header followed by spaces", "HeaderInvalid" },
        { "two spaces in a subsection header", "XrefSubsectionHeaderInvalid" },
        { "space before a subsection header", "XrefSubsectionHeaderInvalid" },
        { "data after the last %%EOF", "EndOfFileMarkerNotLast" },
        { "PDF 2.0 trailer without ID", "TrailerIdMissing" },
    };

    public static TheoryData<string, string, string> TrailerIdCases => new()
    {
        { "%PDF-1.7", "", "" },
        { "%PDF-2.0", "", "/ID [<0123> <0123>]" },
        { "%PDF-1.7", "/Version /2.0", "/ID [<0123> <0123>]" },
    };

    [Theory]
    [MemberData(nameof(Deviations))]
    public void A_layout_deviation_is_read_with_one_diagnostic(string deviation, string code)
    {
        using PdfDocument document = PdfDocument.Open(File(deviation));

        _ = Assert.Single(document.Pages);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal(code, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Theory]
    [MemberData(nameof(Deviations))]
    public void Strict_mode_throws_on_a_layout_deviation(string deviation, string code)
    {
        DiagnosticException error = Assert.Throws<DiagnosticException>(() => PdfDocument.Open(File(deviation), new PdfOptions().UseStrict()));

        Assert.Equal(code, error.Diagnostic.Code);
    }

    [Theory]
    [InlineData("%PDF-1.7\r\n")]
    [InlineData("%PDF-1.7\r")]
    public void A_header_ended_by_any_end_of_line_marker_has_no_diagnostic(string header)
    {
        byte[] file = Replace(TestPdf.OnePage("/MediaBox [0 0 612 792]"), "%PDF-1.7\n", header);

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [InlineData("xref\r\n0 4\r\n")]
    [InlineData("xref \n0 4\n")]
    [InlineData("xref\n\n0 4\n")]
    [InlineData("xref\n0 4 \n")]
    public void A_subsection_header_on_its_own_line_has_no_diagnostic_whatever_ends_the_xref_line(string layout)
    {
        // §7.5.4 asks for "a line containing only two integers separated by a SPACE"; of the xref line it asks only that it contain
        // the keyword. White-space after the keyword, and a blank line, are PDF/A-1 rules (ISO 19005-1 6.1.4), not ISO 32000-2 ones.
        // A space before the end of the subsection header line is tolerated: Acrobat writes it (pdf.js TAMReview.pdf, qpdf enc-base.pdf).
        byte[] file = Replace(TestPdf.OnePage("/MediaBox [0 0 612 792]"), "xref\n0 4\n", layout);

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r\n\0\0 \n")]
    [InlineData("")]
    public void White_space_after_the_last_end_of_file_marker_has_no_diagnostic(string tail)
    {
        byte[] file = [.. Trimmed(TestPdf.OnePage("/MediaBox [0 0 612 792]")), .. Encoding.Latin1.GetBytes(tail)];

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Data_far_after_the_last_end_of_file_marker_is_found()
    {
        byte[] file = [.. TestPdf.OnePage("/MediaBox [0 0 612 792]"), .. new byte[5000], .. "x"u8];

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Equal("EndOfFileMarkerNotLast", Assert.Single(document.Diagnostics).Code);
    }

    [Theory]
    [MemberData(nameof(TrailerIdCases))]
    public void A_trailer_id_is_required_only_from_pdf_2_0(string header, string catalogEntries, string trailerEntries)
    {
        byte[] file = new TestPdf { Header = header, TrailerEntries = trailerEntries }.Build(
            $"<< /Type /Catalog /Pages 2 0 R {catalogEntries} >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>");

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_catalog_version_of_2_0_requires_the_trailer_id_too()
    {
        using PdfDocument document = PdfDocument.Open(TestPdf.OnePage("/MediaBox [0 0 612 792]", "/Version /2.0"));

        Assert.Equal("TrailerIdMissing", Assert.Single(document.Diagnostics).Code);
    }

    private static byte[] File(string deviation)
    {
        byte[] file = TestPdf.OnePage("/MediaBox [0 0 612 792]");
        return deviation switch
        {
            "header followed by spaces" => Replace(file, "%PDF-1.7\n", "%PDF-1.7   \n"),
            "two spaces in a subsection header" => Replace(file, "xref\n0 4\n", "xref\n0  4\n"),
            "space before a subsection header" => Replace(file, "xref\n0 4\n", "xref\n 0 4\n"),
            "data after the last %%EOF" => [.. file, .. "SomeData"u8],
            "PDF 2.0 trailer without ID" => TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-2.0"),
            _ => throw new ArgumentOutOfRangeException(nameof(deviation)),
        };
    }

    /// <summary>Replaces <paramref name="before"/>, which must occur once, keeping every offset before it.</summary>
    private static byte[] Replace(byte[] file, string before, string after)
    {
        string text = Encoding.Latin1.GetString(file);
        int at = text.IndexOf(before, StringComparison.Ordinal);
        Assert.True(at >= 0 && text.IndexOf(before, at + 1, StringComparison.Ordinal) < 0, $"'{before}' must occur once.");
        string replaced = text[..at] + after + text[(at + before.Length)..];

        // Cross-reference offsets count from the header; a header of another length moves every object.
        int shift = after.Length - before.Length;
        if (at == 0 && shift != 0)
        {
            replaced = Shift(replaced, shift);
        }

        return Encoding.Latin1.GetBytes(replaced);
    }

    /// <summary>Rewrites every 10-digit in-use offset and the startxref offset of a <see cref="TestPdf"/> file by <paramref name="shift"/>.</summary>
    private static string Shift(string text, int shift)
    {
        string shifted = System.Text.RegularExpressions.Regex.Replace(
            text,
            "(?m)^(\\d{10}) 00000 n",
            match => $"{long.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) + shift:D10} 00000 n");
        return System.Text.RegularExpressions.Regex.Replace(
            shifted,
            "startxref\\n(\\d+)",
            match => $"startxref\n{long.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) + shift}");
    }

    private static byte[] Trimmed(byte[] file) => Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(file).TrimEnd('\n'));
}
