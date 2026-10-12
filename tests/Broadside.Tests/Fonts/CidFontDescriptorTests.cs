using Broadside.Diagnostics;
using Broadside.Fonts;

namespace Broadside.Tests.Fonts;

/// <summary>The font descriptor entries only CIDFonts have: Style, Lang, FD and CIDSet. ISO 32000-2 §9.8.3, Tables 122 and 123.</summary>
public sealed class CidFontDescriptorTests
{
    private const string Type0 = "<< /Type /Font /Subtype /Type0 /BaseFont /HeiseiMin-W3 /Encoding /UniJIS-UCS2-H /DescendantFonts [5 0 R] >>";
    private const string CidFont = "<< /Type /Font /Subtype /CIDFontType0 /BaseFont /HeiseiMin-W3 /CIDSystemInfo << /Registry (Adobe) /Ordering (Japan1) /Supplement 2 >> /FontDescriptor 6 0 R >>";
    private const string Common = "/Type /FontDescriptor /FontName /HeiseiMin-W3 /Flags 6 /FontBBox [-123 -257 1001 910] /ItalicAngle 0 /Ascent 723 /Descent -241 /CapHeight 709 /StemV 69";

    [Fact]
    public void Style_Lang_FD_and_CIDSet_are_read_from_a_CIDFonts_descriptor()
    {
        // §9.8.3.2 EXAMPLE and §9.8.3.3 EXAMPLE 2; CIDSet 0xA0: CIDs 0 and 2 present, 1 absent (high-order bit first).
        using PdfDocument document = Open(
            $"<< {Common} /Style << /Panose <010502020300000000000000> >> /Lang /ja /FD << /Proportional 7 0 R /HKana << /Type /FontDescriptor /FontName /HeiseiMin-W3-HKana /Flags 3 /MissingWidth 500 >> >> /CIDSet 8 0 R >>",
            "<< /Type /FontDescriptor /FontName /HeiseiMin-W3-Proportional /Flags 2 /AvgWidth 478 /MaxWidth 1212 /MissingWidth 250 /StemV 105 >>",
            "<< /Length 1 >>\nstream\n\xA0\nendstream");
        PdfFontDescriptor descriptor = Descriptor(document);

        Assert.Equal(Convert.FromHexString("010502020300000000000000"), descriptor.Panose?.ToArray());
        Assert.Equal("ja", descriptor.Language);
        Assert.Equal(["HKana", "Proportional"], descriptor.ClassDescriptors.Keys.Order(StringComparer.Ordinal));
        PdfFontDescriptor proportional = descriptor.ClassDescriptors["Proportional"];
        Assert.Equal(("HeiseiMin-W3-Proportional", 478.0, 1212.0, 250.0, 105.0), (proportional.FontName, proportional.AvgWidth, proportional.MaxWidth, proportional.MissingWidth, proportional.StemV));
        Assert.Equal(500, descriptor.ClassDescriptors["HKana"].MissingWidth);
        Assert.NotNull(descriptor.CidSet);
        Assert.Equal<bool?>([true, false, true, false], new[] { 0, 1, 2, 100 }.Select(descriptor.ContainsCid));
        Assert.Empty(Problems(document));
    }

    [Fact]
    public void Absent_entries_read_as_absent()
    {
        using PdfDocument document = Open($"<< {Common} >>");
        PdfFontDescriptor descriptor = Descriptor(document);

        Assert.Null(descriptor.Panose);
        Assert.Null(descriptor.Language);
        Assert.Empty(descriptor.ClassDescriptors);
        Assert.Null(descriptor.CidSet);
        Assert.Null(descriptor.ContainsCid(0));
        Assert.Empty(Problems(document));
    }

    [Fact]
    public void Malformed_entries_are_ignored_with_one_diagnostic_on_the_CIDFont()
    {
        using PdfDocument document = Open(
            $"<< {Common} /Style << /Panose <0105> >> /Lang 5 /FD << /Kana 12 /Kanji << /FontFile 7 0 R /MissingWidth 1000 >> >> /CIDSet << >> >>",
            "<< /Length 0 >>\nstream\n\nendstream");
        PdfFontDescriptor descriptor = Descriptor(document);
        PdfCidFont font = Assert.IsType<PdfType0Font>(FontPdf.Font(document)).DescendantFont!;

        _ = font.GetWidth(1);

        Assert.Null(descriptor.Panose);
        Assert.Null(descriptor.Language);
        Assert.Empty(descriptor.ClassDescriptors);
        Assert.Null(descriptor.CidSet);
        Diagnostic diagnostic = Assert.Single(Problems(document));
        Assert.Equal(("FontDescriptorInvalid", DiagnosticSeverity.Warning, 5), (diagnostic.Code, diagnostic.Severity, diagnostic.ObjectReference?.ObjectNumber));
        Assert.All(["Panose", "Lang", "FD", "CIDSet"], key => Assert.Contains(key, diagnostic.Message, StringComparison.Ordinal));
    }

    private static PdfDocument Open(string descriptor, params string[] objects) =>
        FontPdf.Open(Type0, new PdfOptions().UseSystemFontResolver(null), [CidFont, descriptor, .. objects]);

    private static PdfFontDescriptor Descriptor(PdfDocument document) =>
        Assert.IsType<PdfFontDescriptor>(Assert.IsType<PdfType0Font>(FontPdf.Font(document)).DescendantFont!.Descriptor);

    /// <summary>The diagnostics other than those of a CIDFont without a program and a predefined CMap without the package.</summary>
    private static Diagnostic[] Problems(PdfDocument document) =>
        [.. document.Diagnostics.Where(diagnostic => diagnostic.Code is not ("FontProgramNotFound" or "CMapUnavailable"))];
}
