using System.Globalization;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.Tests.Writing;

namespace Broadside.Fonts.Standard14.Tests;

/// <summary>
/// Issue #229: converting existing documents to PDF/A through <see cref="PdfDocument.ConvertToPdfA"/>, the result saved with the
/// conversion's options and checked by veraPDF (the tests skip when it is not installed; CI's veraPDF job runs them).
/// </summary>
[Trait("Category", "VeraPdf")]
public sealed class PdfAConversionTests
{
    private static readonly PdfOptions Fonts = new PdfOptions().UseStandard14Fonts().UseSystemFontResolver(null);

    [Theory]
    [InlineData(PdfAConformance.PdfA2b, "2b")]
    [InlineData(PdfAConformance.PdfA2u, "2u")]
    [InlineData(PdfAConformance.PdfA4, "4")]
    public void Non_embedded_standard_14_and_resolver_fonts_are_embedded_as_subsets_and_extract_the_same_text(PdfAConformance level, string flavour)
    {
        ReadOnlyMemory<byte> serif = Standard14FontResolver.GetProgram(Standard14Font.TimesRoman);
        PdfOptions options = new PdfOptions().UseStandard14Fonts().UseSystemFontResolver(new OneFontResolver("Georgia", serif));
        byte[] source = TextDocument(
            "/F1 5 0 R /F2 6 0 R /F3 7 0 R",
            "BT /F1 18 Tf 72 700 Td (Hello, PDF/A!) Tj ET BT /F2 14 Tf 72 660 Td (Courier \\(mono\\)) Tj ET BT /F3 16 Tf 72 620 Td (Georgia stands in) Tj ET",
            extra:
            [
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>",
                GeorgiaFont(serif, "Georgia stands in"),
                "<< /Type /FontDescriptor /FontName /Georgia /Flags 34 /FontBBox [-200 -250 1200 900] /ItalicAngle 0 /Ascent 900 /Descent -250 /CapHeight 700 /StemV 80 >>",
            ]);
        string before;
        using (PdfDocument original = PdfDocument.Open(source, options))
        {
            before = original.Pages[0].ExtractText().Text;
        }

        using PdfDocument document = PdfDocument.Open(source, options);
        PdfAConversion conversion = document.ConvertToPdfA(level);

        Assert.True(conversion.IsConformant, string.Join("; ", conversion.Violations.Select(violation => $"{violation.Rule}: {violation.Message}")));
        Assert.Equal(3, conversion.Changes.Count(change => change.Contains("is embedded as a subset", StringComparison.Ordinal)));
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "PdfAConverted" && diagnostic.Severity == DiagnosticSeverity.Information
            && diagnostic.Message.Contains("Helvetica is embedded as a subset of LiberationSans", StringComparison.Ordinal));
        byte[] saved = Save(document, conversion);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(before, reopened.Pages[0].ExtractText().Text);
        Assert.All(reopened.Pages[0].Resources!.GetValueOrDefault(new CosName("Font")) is { } fonts && reopened.Resolve(fonts) is CosDictionary dictionary
            ? dictionary.Select(entry => reopened.GetFont(entry.Value)!)
            : [], font => Assert.True(font is PdfType0Font composite ? composite.DescendantFont!.IsEmbedded : font.IsEmbedded, $"{font.BaseFont} is not embedded"));
        Assert.True(saved.Length < 60_000, $"the converted file is {saved.Length} bytes: the fonts are not subsets");
        Assert.Empty(VeraPdf.FailedRules(saved, flavour));
    }

    [Fact]
    public void JavaScript_actions_encryption_and_multimedia_are_removed_with_information_diagnostics()
    {
        byte[] plain = TextDocument(
            "/F1 5 0 R",
            "BT /F1 12 Tf 72 700 Td (Archive me) Tj ET",
            catalog: "/OpenAction << /S /JavaScript /JS (app.alert\\('hi'\\);) >> /Names << /JavaScript << /Names [(init) << /S /JavaScript /JS (1;) >>] >> >>",
            page: "/AA << /O << /S /JavaScript /JS (2;) >> >> /Annots [6 0 R 7 0 R 8 0 R]",
            extra:
            [
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
                "<< /Type /Annot /Subtype /Screen /Rect [72 400 272 500] /F 4 /A << /S /Rendition /OP 0 >> >>",
                "<< /Type /Annot /Subtype /Link /Rect [72 300 272 320] /F 4 /Border [0 0 0] /A << /S /Launch /F (calc.exe) >> >>",
                "<< /Type /Annot /Subtype /Link /Rect [72 200 272 220] /F 4 /Border [0 0 0] /A << /S /URI /URI (https://example.org/) >> >>",
            ]);
        byte[] encrypted;
        using (PdfDocument document = PdfDocument.Open(plain))
        {
            using var stream = new MemoryStream();
            document.Save(stream, new PdfSaveOptions().WithEncryption(new PdfEncryptionOptions("user", "owner")));
            encrypted = stream.ToArray();
        }

        using PdfDocument opened = PdfDocument.Open(encrypted, new PdfOptions().UseStandard14Fonts().UseSystemFontResolver(null).WithPassword("owner"));
        Assert.True(opened.IsEncrypted);
        PdfAConversion conversion = opened.ConvertToPdfA(PdfAConformance.PdfA2b);

        Assert.True(conversion.IsConformant, string.Join("; ", conversion.Violations.Select(violation => $"{violation.Rule}: {violation.Message}")));
        Diagnostic converted = Assert.Single(opened.Diagnostics, diagnostic => diagnostic.Code == "PdfAConverted");
        Assert.Equal(DiagnosticSeverity.Information, converted.Severity);
        string[] expected =
        [
            "the encryption is removed",
            "the document's open action's JavaScript action is removed",
            "the document-level JavaScript",
            "a page's additional actions (AA) are removed",
            "a Screen annotation (multimedia",
            "a Link annotation's Launch action is removed",
        ];
        Assert.All(expected, text => Assert.Contains(text, converted.Message, StringComparison.Ordinal));
        Assert.All(expected, text => Assert.Contains(conversion.Changes, change => change.StartsWith(text, StringComparison.Ordinal)));

        byte[] saved = Save(opened, conversion);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.False(reopened.IsEncrypted);
        Assert.False(reopened.Catalog.ContainsKey(new CosName("OpenAction")));
        CosArray annotations = (CosArray)reopened.Resolve(reopened.Pages[0].Dictionary[new CosName("Annots")]);
        Assert.Equal(2, annotations.Count);
        Assert.Equal("URI", ((CosName)((CosDictionary)reopened.Resolve(((CosDictionary)reopened.Resolve(annotations[1]))[new CosName("A")]))[new CosName("S")]).Value);
        Assert.Contains("<pdfaid:part>2</pdfaid:part>", Encoding.Latin1.GetString(saved), StringComparison.Ordinal);
        Assert.Empty(VeraPdf.FailedRules(saved, "2b"));
    }

    [Fact]
    public void Converting_to_pdf_a_3b_keeps_embedded_files_as_associated_files_with_a_relationship()
    {
        using PdfDocument document = PdfDocument.Open(TextDocument("/F1 5 0 R", "BT /F1 12 Tf 72 700 Td (Invoice) Tj ET", extra: ["<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"]), Fonts);
        document.AddEmbeddedFile("invoice.xml", "<invoice/>"u8);
        document.AddEmbeddedFile("notes.bin", [1, 2, 3]);

        PdfAConversion conversion = document.ConvertToPdfA(PdfAConformance.PdfA3b);

        Assert.True(conversion.IsConformant, string.Join("; ", conversion.Violations.Select(violation => $"{violation.Rule}: {violation.Message}")));
        byte[] saved = Save(document, conversion);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(["invoice.xml", "notes.bin"], reopened.EmbeddedFiles.Select(entry => entry.Name).Order());
        CosArray associated = (CosArray)reopened.Resolve(reopened.Catalog[new CosName("AF")]);
        Assert.Equal(2, associated.Count);
        Assert.All(associated, item => Assert.Equal("Unspecified", ((CosName)((CosDictionary)reopened.Resolve(item))[new CosName("AFRelationship")]).Value));
        Assert.Empty(VeraPdf.FailedRules(saved, "3b"));
    }

    [Fact]
    public void What_cannot_be_converted_is_reported_by_rule_and_the_save_does_not_claim_conformance()
    {
        using PdfDocument document = PdfDocument.Open(
            TextDocument("/F1 5 0 R", "BT /F1 12 Tf 72 700 Td (No program) Tj ET", extra: ["<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"]),
            new PdfOptions().UseSystemFontResolver(null));
        document.AddEmbeddedFile("data.csv", "a,b"u8);

        PdfAConversion conversion = document.ConvertToPdfA(PdfAConformance.PdfA2b);

        Assert.False(conversion.IsConformant);
        Assert.Equal(["6.2.11.4.1-1", "6.8-5"], conversion.Violations.Select(violation => violation.Rule).Order());
        Assert.Contains(conversion.NotConverted, reason => reason.Contains("the font Helvetica is not embedded: the document's font resolvers have no program", StringComparison.Ordinal));
        Assert.Contains(conversion.NotConverted, reason => reason.Contains("convert to PDF/A-3 or PDF/A-4f", StringComparison.Ordinal));
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "PdfANonConformant" && diagnostic.Message.Contains("veraPDF rule 6.2.11.4.1-1", StringComparison.Ordinal));
        byte[] saved = Save(document, conversion);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "PdfANotIdentified");
        Assert.DoesNotContain("pdfaid:part", Encoding.Latin1.GetString(saved), StringComparison.Ordinal);
    }

    [Fact]
    public void A_strict_conversion_that_cannot_conform_throws_and_leaves_the_document_unchanged()
    {
        byte[] source = TextDocument(
            "/F1 5 0 R",
            "BT /F1 12 Tf 72 700 Td (Strict) Tj ET",
            catalog: "/OpenAction << /S /JavaScript /JS (1;) >>",
            extra: ["<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"]);
        using PdfDocument document = PdfDocument.Open(source, new PdfOptions().UseStrict().UseSystemFontResolver(null));
        string before = CosSnapshot.Of(document);

        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => document.ConvertToPdfA(PdfAConformance.PdfA2b));

        Assert.Contains("6.2.11.4.1-1", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, CosSnapshot.Of(document));
    }

    [Fact]
    public void None_is_not_a_level_to_convert_to()
    {
        using PdfDocument document = PdfDocument.Create();
        Assert.Throws<ArgumentOutOfRangeException>(() => document.ConvertToPdfA(PdfAConformance.None));
    }

    private static byte[] Save(PdfDocument document, PdfAConversion conversion)
    {
        using var stream = new MemoryStream();
        document.Save(stream, conversion.CreateSaveOptions());
        return stream.ToArray();
    }

    /// <summary>A one-page document drawing <paramref name="content"/> with the fonts <paramref name="fonts"/>; objects of <paramref name="extra"/> are numbered from 5.</summary>
    private static byte[] TextDocument(string fonts, string content, string catalog = "", string page = "", string[]? extra = null) =>
        new TestPdf().Build(
        [
            $"<< /Type /Catalog /Pages 2 0 R {catalog} >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << {fonts} >> >> /Contents 4 0 R {page} >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            .. extra ?? [],
        ]);

    /// <summary>A TrueType font dictionary for "Georgia", whose widths are the stand-in program's for the characters of <paramref name="text"/>.</summary>
    private static string GeorgiaFont(ReadOnlyMemory<byte> program, string text)
    {
        using PdfDocument measuring = PdfDocument.Create();
        PdfCanvasFont font = measuring.EmbedTrueTypeFont(program);
        char first = text.Min(), last = text.Max();
        IEnumerable<string> widths = Enumerable.Range(first, last - first + 1)
            .Select(code => text.Contains((char)code, StringComparison.Ordinal) ? font.MeasureWidth([(char)code], 1000).ToString("0.###", CultureInfo.InvariantCulture) : "0");
        return string.Create(CultureInfo.InvariantCulture, $"<< /Type /Font /Subtype /TrueType /BaseFont /Georgia /Encoding /WinAnsiEncoding /FirstChar {(int)first} /LastChar {(int)last} /Widths [{string.Join(' ', widths)}] /FontDescriptor 8 0 R >>");
    }

    /// <summary>A font resolver standing in for the machine's fonts: one program, for one name.</summary>
    private sealed class OneFontResolver(string name, ReadOnlyMemory<byte> program) : IFontResolver
    {
        public FontResolution? ResolveFont(FontQuery query) =>
            query.PostScriptName == name ? new FontResolution(program, name, FontMatchKind.Exact) : null;
    }
}
