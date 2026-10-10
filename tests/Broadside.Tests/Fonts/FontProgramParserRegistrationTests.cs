using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Fonts.TrueType;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// The font program parser extension point: parsers registered through <see cref="PdfOptions.UseFontProgramParser"/> come before
/// the managed TrueType default, per engine, with no static state. ISO 32000-2 §9.9; ADR 0001.
/// </summary>
public class FontProgramParserRegistrationTests
{
    private const string Expected = "M 100,0 L 100,700 L 300,700 L 300,0 Z";

    [Fact]
    public void A_decorated_default_parser_parses_the_program_once_and_gives_the_same_outlines()
    {
        var counting = new CountingParser(new TrueTypeFontProgramParser());
        using PdfDocument document = TrueTypeCorpusTests.Open("text-truetype-embedded.pdf", new PdfOptions().UseFontProgramParser(counting));
        PdfTrueTypeFont font = TrueTypeCorpusTests.Font(document, "F1");

        Assert.Equal(Expected, OutlineText.Of(font.Program!, font.GetGlyphId(0x49)));
        Assert.Equal(2, font.GetGlyphId(0x49));
        Assert.Equal(1, font.GetGlyphId(0x48));
        Assert.Same(font.Program, document.GetFont(font.Reference)!.Program);
        Parallel.For(0, 64, _ => Assert.Equal(Expected, OutlineText.Of(font.Program!, 2)));
        Assert.Equal(1, counting.Count);
    }

    [Fact]
    public void A_registered_parser_replaces_the_default_for_the_programs_it_accepts()
    {
        using PdfDocument document = TrueTypeCorpusTests.Open("text-truetype-embedded.pdf", new PdfOptions().UseFontProgramParser(new SquareParser()));
        PdfTrueTypeFont font = TrueTypeCorpusTests.Font(document, "F1");

        Assert.IsType<SquareProgram>(font.Program);
        Assert.Equal("M 0,0 L 1,0 L 1,1 L 0,1 Z", OutlineText.Of(font.Program!, font.GetGlyphId(0x48)));
        Assert.Equal("M 0,0 L 1,0 L 1,1 L 0,1 Z", OutlineText.Of(font.Program!, font.GetGlyphId(0x49)));
    }

    [Fact]
    public void Another_engine_without_the_registration_still_uses_the_default()
    {
        using PdfDocument replaced = new PdfEngine(new PdfOptions().UseFontProgramParser(new SquareParser())).Open(Corpus.Bytes("text-truetype-embedded.pdf"));
        using PdfDocument standard = new PdfEngine().Open(Corpus.Bytes("text-truetype-embedded.pdf"));

        Assert.IsType<SquareProgram>(TrueTypeCorpusTests.Font(replaced, "F1").Program);
        PdfTrueTypeFont font = TrueTypeCorpusTests.Font(standard, "F1");
        Assert.Equal(Expected, OutlineText.Of(font.Program!, font.GetGlyphId(0x49)));
    }

    [Fact]
    public void A_parser_that_throws_leaves_the_font_without_a_program_and_a_diagnostic()
    {
        using PdfDocument document = TrueTypeCorpusTests.Open("text-truetype-embedded.pdf", new PdfOptions().UseFontProgramParser(new ThrowingParser()).UseSystemFontResolver(null));
        PdfTrueTypeFont font = TrueTypeCorpusTests.Font(document, "F1");

        Assert.Null(font.Program);
        Assert.Equal(0, font.GetGlyphId(0x48));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics, d => d.Severity > DiagnosticSeverity.Information);
        Assert.Equal("FontProgramInvalid", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);

        // An unusable embedded program is treated as not embedded: the resolvers are asked (issue #59), here with none.
        Assert.Contains(document.Diagnostics, d => d.Code == "FontProgramNotFound");
    }

    [Fact]
    public void A_program_no_parser_reads_is_reported_as_unsupported_information()
    {
        using PdfDocument document = FontPdf.Open(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Embedded /FirstChar 65 /LastChar 65 /Widths [500] /FontDescriptor 5 0 R >>",
            "<< /Type /FontDescriptor /FontName /Embedded /Flags 32 /FontBBox [0 0 1 1] /ItalicAngle 0 /Ascent 1 /Descent 0 /StemV 1 /FontFile3 6 0 R >>",
            "<< /Length 12 /Subtype /Unknown >>\nstream\nnot-a-font!!\nendstream");

        Assert.Null(FontPdf.Font(document).Program);
        Assert.Contains(document.Diagnostics, d => d.Code == "FontProgramUnsupported" && d.Severity == DiagnosticSeverity.Information);
    }

    [Fact]
    public void Registering_a_null_parser_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PdfOptions().UseFontProgramParser(null!));
    }

    private sealed class CountingParser(IFontProgramParser inner) : IFontProgramParser
    {
        private int _count;

        public int Count => _count;

        public IReadOnlyList<FontProgramFormat> Formats => inner.Formats;

        public bool CanParse(ReadOnlySpan<byte> data) => inner.CanParse(data);

        public FontProgram? Parse(ReadOnlyMemory<byte> data, FontProgramContext context)
        {
            Interlocked.Increment(ref _count);
            return inner.Parse(data, context);
        }
    }

    private sealed class SquareParser : IFontProgramParser
    {
        public IReadOnlyList<FontProgramFormat> Formats { get; } = [FontProgramFormat.TrueType];

        public bool CanParse(ReadOnlySpan<byte> data) => true;

        public FontProgram? Parse(ReadOnlyMemory<byte> data, FontProgramContext context) => new SquareProgram();
    }

    private sealed class SquareProgram : FontProgram
    {
        public override FontProgramFormat Format => FontProgramFormat.TrueType;

        public override int GlyphCount => 3;

        public override Matrix FontMatrix => Matrix.Identity;

        public override IReadOnlyList<FontCharacterMap> CharacterMaps { get; } = [new IdentityMap()];

        public override GlyphOutlineStatus GetOutline(int glyphId, GlyphOutline outline)
        {
            outline.Clear();
            outline.MoveTo(0, 0);
            outline.LineTo(1, 0);
            outline.LineTo(1, 1);
            outline.LineTo(0, 1);
            outline.Close();
            return GlyphOutlineStatus.Complete;
        }
    }

    private sealed class IdentityMap : FontCharacterMap
    {
        public override int PlatformId => 3;

        public override int EncodingId => 1;

        public override int Format => 4;

        public override int GetGlyphId(int code) => code is 'H' or 'I' ? code - 'G' : 0;
    }

    private sealed class ThrowingParser : IFontProgramParser
    {
        public IReadOnlyList<FontProgramFormat> Formats { get; } = [FontProgramFormat.TrueType];

        public bool CanParse(ReadOnlySpan<byte> data) => true;

        public FontProgram? Parse(ReadOnlyMemory<byte> data, FontProgramContext context) => throw new InvalidOperationException("broken parser");
    }
}
