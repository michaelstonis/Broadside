using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// Widths of simple fonts: <c>FirstChar</c>, <c>LastChar</c>, <c>Widths</c>, <c>MissingWidth</c>, and the repairs of malformed
/// entries. ISO 32000-2 §9.6.2.1 Table 109, §9.8.1 Table 120, §9.2.4.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public class WidthTests
{
    private const string Descriptor =
        "<< /Type /FontDescriptor /FontName /Plain /Flags 32 /FontBBox [ 0 -200 1000 800 ] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /MissingWidth 99 >>";

    [Fact]
    public void Codes_in_range_take_their_widths_and_the_others_take_MissingWidth()
    {
        using PdfDocument document = FontPdf.Open("<< /Type /Font /Subtype /Type1 /BaseFont /Plain /FirstChar 65 /LastChar 67 /Widths [ 500 600.5 6 0 R ] /FontDescriptor 5 0 R >>", Descriptor, "650");
        PdfSimpleFont font = FontPdf.SimpleFont(document);

        Assert.Equal([99.0, 500, 600.5, 650, 99], new[] { font.GetWidth(64), font.GetWidth(65), font.GetWidth(66), font.GetWidth(67), font.GetWidth(68) });
        Assert.Null(font.Standard14);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_missing_FirstChar_is_read_as_0_and_a_missing_LastChar_from_the_number_of_widths()
    {
        using PdfDocument document = FontPdf.Open("<< /Type /Font /Subtype /Type1 /BaseFont /Plain /Widths [ 10 20 ] /FontDescriptor 5 0 R >>", Descriptor);
        PdfSimpleFont font = FontPdf.SimpleFont(document);

        Assert.Equal([10.0, 20, 99], new[] { font.GetWidth(0), font.GetWidth(1), font.GetWidth(2) });
        Assert.Equal(["FontWidthsInvalid"], FontPdf.Codes(document));
    }

    [Fact]
    public void A_widths_array_shorter_than_its_range_or_with_a_non_number_falls_back_to_MissingWidth()
    {
        using PdfDocument document = FontPdf.Open("<< /Type /Font /Subtype /Type1 /BaseFont /Plain /FirstChar 65 /LastChar 68 /Widths [ 500 /Bad 700 ] /FontDescriptor 5 0 R >>", Descriptor);
        PdfSimpleFont font = FontPdf.SimpleFont(document);

        Assert.Equal([500.0, 99, 700, 99], new[] { font.GetWidth(65), font.GetWidth(66), font.GetWidth(67), font.GetWidth(68) });
        Assert.Equal(["FontWidthsInvalid"], FontPdf.Codes(document));
    }

    [Fact]
    public void A_font_without_widths_that_is_not_a_standard_14_font_has_zero_widths_and_a_diagnostic()
    {
        using PdfDocument document = FontPdf.Open("<< /Type /Font /Subtype /TrueType /BaseFont /Plain /Widths 12 /FontDescriptor 5 0 R >>", Descriptor);
        PdfSimpleFont font = FontPdf.SimpleFont(document);

        Assert.Equal(0, font.GetWidth(65));
        Assert.Equal(["FontWidthsInvalid", "FontWidthsMissing"], FontPdf.Codes(document));
    }

    [Fact]
    public void A_standard_14_font_with_widths_but_no_descriptor_takes_its_metrics_outside_the_range_with_a_diagnostic()
    {
        using PdfDocument document = FontPdf.Open("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /FirstChar 65 /LastChar 65 /Widths [ 1000 ] >>");
        PdfSimpleFont font = FontPdf.SimpleFont(document);

        Assert.Equal(1000, font.GetWidth(65));
        Assert.Equal(667, font.GetWidth(66));
        Assert.True(font.Descriptor!.IsSynthesized);
        Assert.Equal(["FontStandard14EntriesIncomplete"], FontPdf.Codes(document));
    }

    [Fact]
    public void Widths_follow_changes_to_the_widths_array_and_the_font_dictionary()
    {
        using PdfDocument document = FontPdf.Open("<< /Type /Font /Subtype /Type1 /BaseFont /Plain /FirstChar 65 /LastChar 65 /Widths [ 500 ] /FontDescriptor 5 0 R >>", Descriptor);
        PdfSimpleFont font = FontPdf.SimpleFont(document);
        Assert.Equal(500, font.GetWidth(65));

        var widths = (CosArray)document.Resolve(font.Dictionary[new CosName("Widths")]);
        widths[0] = new CosInteger(321);
        Assert.Equal(321, font.GetWidth(65));

        font.Dictionary[new CosName("FirstChar")] = new CosInteger(66);
        font.Dictionary[new CosName("LastChar")] = new CosInteger(66);
        Assert.Equal(99, font.GetWidth(65));
        Assert.Equal(321, font.GetWidth(66));

        var descriptor = (CosDictionary)document.Resolve(font.Dictionary[new CosName("FontDescriptor")]);
        descriptor[new CosName("MissingWidth")] = new CosInteger(5);
        Assert.Equal(5, font.GetWidth(65));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Glyph_names_follow_changes_to_the_differences_array()
    {
        using PdfDocument document = FontPdf.Open("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding << /Differences [ 65 /B ] >> >>");
        PdfSimpleFont font = FontPdf.SimpleFont(document);
        Assert.Equal("B", font.GetGlyphName(65));
        Assert.Equal(667, font.GetWidth(65));

        var encoding = (CosDictionary)font.Dictionary[new CosName("Encoding")];
        ((CosArray)encoding[new CosName("Differences")])[1] = new CosName("i");
        Assert.Equal("i", font.GetGlyphName(65));
        Assert.Equal(222, font.GetWidth(65));

        font.Dictionary[new CosName("Encoding")] = new CosName("WinAnsiEncoding");
        Assert.Equal("A", font.GetGlyphName(65));
    }

    [Fact]
    public void Strict_mode_throws_from_the_first_lookup_of_a_font_with_malformed_widths()
    {
        using PdfDocument document = FontPdf.Open("<< /Type /Font /Subtype /Type1 /BaseFont /Plain /Widths [ 1 ] /FontDescriptor 5 0 R >>", new PdfOptions().UseStrict(), Descriptor);
        PdfSimpleFont font = FontPdf.SimpleFont(document);

        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => font.GetWidth(0));
        Assert.Equal("FontWidthsInvalid", exception.Diagnostic.Code);
        Assert.Equal(new CosReference(4, 0), exception.Diagnostic.ObjectReference);
    }

    [Fact]
    public void Concurrent_lookups_on_a_new_font_agree()
    {
        using PdfDocument document = FontPdf.Open("<< /Type /Font /Subtype /Type1 /BaseFont /Times-Roman /Encoding /WinAnsiEncoding >>");
        PdfSimpleFont font = FontPdf.SimpleFont(document);
        double[][] results = new double[64][];

        Parallel.For(0, results.Length, index => results[index] = [.. Enumerable.Range(0, 256).Select(code => font.GetWidth((byte)code))]);

        Assert.All(results, widths => Assert.Equal(results[0], widths));
        Assert.Equal(250, results[0][' ']);
        Assert.Equal(722, results[0]['A']);
    }

    [Fact]
    public void Looking_up_a_glyph_name_and_width_allocates_nothing()
    {
        using PdfDocument document = FontPdf.Open("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding << /Differences [ 65 /B ] >> >>");
        PdfSimpleFont font = FontPdf.SimpleFont(document);
        double total = 0;

        long allocated = Allocations.Measure(() => total += Sum(font), warmUpCalls: 50);

        Assert.Equal(0, allocated);
        Assert.True(total > 0);
    }

    [Fact]
    public void A_type_1_code_in_range_without_a_width_takes_the_advance_of_its_glyph_in_the_embedded_program()
    {
        // B (66) is in FirstChar-LastChar but Widths stops at A: the program's hsbw gives 550 (pdf.js does the same). C (67) has no
        // glyph in the program and 68 is outside the range: both take MissingWidth.
        using PdfDocument document = FontPdf.Open(
            "<< /Type /Font /Subtype /Type1 /BaseFont /BroadsideTest /FirstChar 65 /LastChar 67 /Widths [ 700 ] /FontDescriptor 5 0 R >>",
            "<< /Type /FontDescriptor /FontName /BroadsideTest /Flags 32 /FontBBox [ 0 0 1000 1000 ] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /MissingWidth 99 /FontFile 6 0 R >>",
            Type1Stream());
        PdfSimpleFont font = FontPdf.SimpleFont(document);

        Assert.Equal([700.0, 550, 99, 99], new[] { font.GetWidth(65), font.GetWidth(66), font.GetWidth(67), font.GetWidth(68) });
        Assert.Equal(["FontWidthsInvalid"], FontPdf.Codes(document));
        Assert.Contains("glyph's own width", document.Diagnostics[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_type_1_font_without_Widths_takes_the_advances_of_its_embedded_program()
    {
        using PdfDocument document = FontPdf.Open(
            "<< /Type /Font /Subtype /Type1 /BaseFont /BroadsideTest /FontDescriptor 5 0 R >>",
            "<< /Type /FontDescriptor /FontName /BroadsideTest /Flags 32 /FontBBox [ 0 0 1000 1000 ] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile 6 0 R >>",
            Type1Stream());
        PdfSimpleFont font = FontPdf.SimpleFont(document);

        Assert.Equal([640.0, 550, 0], new[] { font.GetWidth(65), font.GetWidth(66), font.GetWidth(67) });
        Assert.Equal(["FontWidthsMissing"], FontPdf.Codes(document));
    }

    [Fact]
    public void A_type_3_code_in_range_without_a_width_takes_the_width_its_d0_or_d1_operator_gives()
    {
        // ISO 32000-2 §9.6.4 Table 111: wx of d0 or d1 is the glyph's width in glyph space. Widths [700 null]: a has its width, b
        // (an element that is not a number) and c (past the end of Widths) take 550 (d1) and 320 (d0).
        using PdfDocument document = FontPdf.Open(
            "<< /Type /Font /Subtype /Type3 /FontBBox [ 0 0 1000 1000 ] /FontMatrix [ 0.001 0 0 0.001 0 0 ] /CharProcs << /a 5 0 R /b 6 0 R /c 7 0 R >> /Encoding << /Differences [ 97 /a /b /c ] >> /FirstChar 97 /LastChar 99 /Widths [ 700 null ] >>",
            Stream("640 0 d0 0 0 100 100 re f"),
            Stream("550 0 0 0 500 500 d1 0 0 100 100 re f"),
            Stream("320 0 d0"));
        PdfSimpleFont font = FontPdf.SimpleFont(document);

        Assert.Equal([700.0, 550, 320], new[] { font.GetWidth(97), font.GetWidth(98), font.GetWidth(99) });
        Assert.Equal(["FontWidthsInvalid"], FontPdf.Codes(document));
    }

    private static string Stream(string content) => $"<< /Length {content.Length} >>\nstream\n{content}\nendstream";

    private static string Type1Stream()
    {
        (byte[] program, int length1, int length2, int length3) = new Type1Builder()
            .Glyph(".notdef", "0 250 hsbw endchar")
            .Glyph("A", "0 640 hsbw 0 0 rmoveto 100 hlineto 100 vlineto closepath endchar")
            .Glyph("B", "0 550 hsbw 0 0 rmoveto 100 hlineto 100 vlineto closepath endchar")
            .Build();
        string data = System.Text.Encoding.Latin1.GetString(program);
        return $"<< /Length {program.Length} /Length1 {length1} /Length2 {length2} /Length3 {length3} >>\nstream\n{data}\nendstream";
    }

    private static double Sum(PdfSimpleFont font)
    {
        double sum = 0;
        for (int code = 0; code < 256; code++)
        {
            sum += font.GetWidth((byte)code) + font.GetGlyphName((byte)code).Length;
        }

        return sum;
    }
}
