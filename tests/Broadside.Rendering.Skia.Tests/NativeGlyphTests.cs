using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Fonts.TrueType;
using Broadside.GoldenImages;
using Broadside.Graphics;
using SkiaSharp;

namespace Broadside.Rendering.Skia.Tests;

/// <summary>
/// Glyph runs of TrueType and OpenType programs draw through an <see cref="SKTypeface"/> loaded from the embedded bytes, created once
/// per font program per backend; every other program draws from its outlines (issue #123, open decision 7).
/// </summary>
public sealed class NativeGlyphTests
{
    [Theory]
    [InlineData("text-truetype-embedded.pdf", 1)]
    [InlineData("text-truetype-composite.pdf", 1)]
    [InlineData("text-opentype-cff-embedded.pdf", 1)]
    [InlineData("text-cid-identity-h.pdf", 1)]
    [InlineData("text-type1-embedded.pdf", 0)]
    [InlineData("text-cff-embedded.pdf", 0)]
    [InlineData("text-standard14.pdf", 1)]
    [InlineData("text-type3.pdf", 0)]
    public void A_typeface_is_created_once_per_sfnt_program_per_backend(string fileName, int typefaces)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);
        using var backend = new SkiaBackend();

        using (backend.Render(document.Pages[0], GoldenCorpus.CreateRenderOptions()))
        {
        }

        using (backend.Render(document.Pages[0], GoldenCorpus.CreateRenderOptions()))
        {
        }

        Assert.Equal(typefaces, backend.TypefacesCreated);
    }

    [Fact]
    public void Forcing_outlines_creates_no_typeface()
    {
        using PdfDocument document = GoldenCorpus.Open("text-truetype-embedded.pdf");
        using var backend = new SkiaBackend();

        using (backend.Render(document.Pages[0], GoldenCorpus.CreateRenderOptions().WithForceOutlines(true)))
        {
        }

        Assert.Equal(0, backend.TypefacesCreated);
    }

    [Fact]
    public void Native_glyphs_are_drawn_by_the_font_engine_not_from_the_outlines()
    {
        using PdfDocument document = GoldenCorpus.Open("text-truetype-embedded.pdf");
        using var backend = new SkiaBackend();
        using SKBitmap native = backend.Render(document.Pages[0], GoldenCorpus.CreateRenderOptions());
        using SKBitmap outlines = backend.Render(document.Pages[0], GoldenCorpus.CreateRenderOptions().WithForceOutlines(true));

        Assert.False(native.GetPixelSpan().SequenceEqual(outlines.GetPixelSpan()));
    }

    [Fact]
    public void A_program_skia_cannot_load_is_drawn_from_its_outlines_and_reported_in_every_render()
    {
        // A parser (the public extension point) whose programs hand over bytes no font engine reads: the glyphs still draw, from
        // their outlines, exactly as the software rasterizer draws them.
        PdfOptions options = new PdfOptions().UseSystemFontResolver(null).UseFontProgramParser(new UnloadableParser());
        using PdfDocument document = new PdfEngine(options).Open(Broadside.TestSupport.Corpus.Bytes("text-truetype-embedded.pdf"));
        using var backend = new SkiaBackend();

        for (int render = 0; render < 2; render++)
        {
            using SKBitmap bitmap = backend.Render(document.Pages[0], GoldenCorpus.CreateRenderOptions());

            Diagnostic diagnostic = Assert.Single(backend.Diagnostics);
            Assert.Equal("BackendNativeGlyphsUnavailable", diagnostic.Code);
            Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity);
            GoldenCorpus.AssertMatchesReference(
                "skia",
                "text-truetype-embedded.pdf",
                0,
                GoldenImage.FromPixels(bitmap.GetPixelSpan(), bitmap.Width, bitmap.Height, bitmap.RowBytes, GoldenPixelFormat.Bgra8Premultiplied));
        }

        Assert.Equal(1, backend.TypefacesCreated);
    }

    /// <summary>Reads TrueType programs with the managed default, then claims bytes no font engine can load as their sfnt file.</summary>
    private sealed class UnloadableParser : IFontProgramParser
    {
        private readonly TrueTypeFontProgramParser _inner = new();

        public IReadOnlyList<FontProgramFormat> Formats => _inner.Formats;

        public bool CanParse(ReadOnlySpan<byte> data) => _inner.CanParse(data);

        public FontProgram? Parse(ReadOnlyMemory<byte> data, FontProgramContext context) =>
            _inner.Parse(data, context) is { } program ? new UnloadableProgram(program) : null;
    }

    private sealed class UnloadableProgram(FontProgram inner) : FontProgram
    {
        private static readonly byte[] NotAFont = [0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xBA, 0xD0];

        public override FontProgramFormat Format => inner.Format;

        public override int GlyphCount => inner.GlyphCount;

        public override Matrix FontMatrix => inner.FontMatrix;

        public override IReadOnlyList<FontCharacterMap> CharacterMaps => inner.CharacterMaps;

        public override string? PostScriptName => inner.PostScriptName;

        public override GlyphOutlineStatus GetOutline(int glyphId, GlyphOutline outline) => inner.GetOutline(glyphId, outline);

        public override GlyphMetrics GetMetrics(int glyphId) => inner.GetMetrics(glyphId);

        public override bool TryGetGlyphId(string glyphName, out int glyphId) => inner.TryGetGlyphId(glyphName, out glyphId);

        public override string? GetGlyphName(int glyphId) => inner.GetGlyphName(glyphId);

        public override bool TryGetSfntData(out ReadOnlyMemory<byte> data, out int faceIndex)
        {
            data = NotAFont;
            faceIndex = 0;
            return true;
        }
    }
}
