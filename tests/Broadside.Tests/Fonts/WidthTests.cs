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
