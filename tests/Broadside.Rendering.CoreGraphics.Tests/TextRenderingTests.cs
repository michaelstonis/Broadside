using System.Text;
using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Fonts.TrueType;
using Broadside.GoldenImages;
using Broadside.Graphics;
using Broadside.TestSupport;
using CoreGraphics;

namespace Broadside.Rendering.CoreGraphics.Tests;

/// <summary>
/// Glyph runs on CoreGraphics (issue #127; ISO 32000-2 §9.2.4, §9.3.6, §9.4.3, §9.9): TrueType and OpenType programs drawn by
/// CoreGraphics' own font engine from the program's bytes, every other program and every glyph under
/// <see cref="RenderOptions.ForceOutlines"/> from the outlines the display list carries, through the public renderer only.
/// </summary>
/// <remarks>
/// Thresholds: native text against the software rasterizer's golden images within the <c>coregraphics-text</c> rows of
/// <see cref="GoldenThresholds"/> (edges 0.6, flat areas 0.05, no differing pixel but the reviewed CFF glyph I); forced outlines against the golden images within the default;
/// forced outlines against CoreGraphics' own rendering of the same outlines as path objects within <see cref="GoldenThreshold.Tight"/>.
/// </remarks>
public sealed class TextRenderingTests
{
    /// <summary>The threshold-table name of this backend's renders with glyphs drawn natively.</summary>
    private const string NativeText = "coregraphics-text";

    [Theory]
    [MemberData(nameof(TextPages))]
    public void A_text_page_matches_its_golden_image_within_the_text_threshold(string fileName, int page)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);

        GoldenImage render = BitmapRendering.Render(document.Pages[page], GoldenCorpus.CreateRenderOptions());

        GoldenCorpus.AssertMatchesReference(NativeText, fileName, page, render);
    }

    [Theory]
    [MemberData(nameof(TextPages))]
    public void With_outlines_forced_a_text_page_matches_its_golden_image_within_the_default_threshold(string fileName, int page)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);

        GoldenImage render = BitmapRendering.Render(document.Pages[page], GoldenCorpus.CreateRenderOptions().WithForceOutlines(true));

        GoldenComparison comparison = GoldenComparison.Compare(GoldenCorpus.LoadReference(fileName, page), render, GoldenThreshold.Default);
        Assert.True(comparison.Matches, comparison.Describe());
    }

    [Theory]
    [InlineData("synthetic")]
    [InlineData("text-standard14.pdf")]
    [InlineData("text-truetype-composite.pdf")]
    [InlineData("text-opentype-cff-embedded.pdf")]
    [InlineData("text-type1-embedded.pdf")]
    [InlineData("text-cid-identity-v.pdf")]
    [InlineData("text-lines-rotated.pdf")]
    public void With_outlines_forced_glyphs_render_as_the_same_outlines_drawn_as_paths(string name)
    {
        using PdfDocument document = name == "synthetic" ? PdfDocument.Open(TextSamples.Page(Encoding.ASCII.GetBytes(Filled))) : GoldenCorpus.Open(name);
        PdfPage page = document.Pages[0];
        RenderOptions options = GoldenCorpus.CreateRenderOptions().WithForceOutlines(true);
        using PdfDocument paths = PdfDocument.Open(GlyphOutlinePages.Create(page.CreateDisplayList()));

        GoldenImage outlines = BitmapRendering.Render(page, options);
        GoldenImage reference = BitmapRendering.Render(paths.Pages[0], options);

        GoldenComparison comparison = GoldenComparison.Compare(reference, outlines, GoldenThreshold.Tight);
        Assert.True(comparison.Matches, comparison.Describe());
        Assert.False(GoldenComparison.Compare(reference, Blank(reference.Width, reference.Height), GoldenThreshold.Default).Matches);
    }

    [Fact]
    public void Without_forced_outlines_TrueType_glyphs_are_drawn_by_the_font_engine_not_from_the_outlines()
    {
        // The Standard 14 substitutes are Liberation TrueType programs, which CoreGraphics loads; its glyph rasterizer and the
        // outline fill disagree at some anti-aliased pixel, while both stay within the text threshold of the golden image.
        using PdfDocument document = GoldenCorpus.Open("text-standard14.pdf");
        PdfPage page = document.Pages[0];

        GoldenImage native = BitmapRendering.Render(page, GoldenCorpus.CreateRenderOptions());
        GoldenImage outlines = BitmapRendering.Render(page, GoldenCorpus.CreateRenderOptions().WithForceOutlines(true));

        Assert.False(GoldenComparison.Compare(outlines, native, GoldenThreshold.Exact).Matches);
        Assert.True(GoldenComparison.Compare(outlines, native, GoldenThreshold.Text).Matches);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Native_glyphs_render_as_the_software_rasterizer_renders_them_at_any_transform_size_colour_and_alpha(double scale)
    {
        // An embedded TrueType program drawn natively: sizes from 4 to 300 pt, horizontal scaling, rise, a skewed and a rotated text
        // matrix, a mirrored CTM, colours, constant alpha (§11.6.4.4) and a blend mode (§11.3.5), fill then stroke (mode 2). Only
        // the sample font's rectangle glyphs (even codes): its odd glyphs' second contour runs against the rectangle's, so where they
        // overlap the windings +1 and -1 cancel and the non-zero rule (§8.5.3.3.2) correctly leaves the overlap empty, as the
        // rasterizer does; CoreGraphics' glyph scaler fills it, which is the platform's choice, not a defect of either.
        using PdfDocument document = PdfDocument.Open(TextSamples.Page(Encoding.ASCII.GetBytes(Lines(Rectangles)), Alpha));
        PdfPage page = document.Pages[0];
        RenderOptions options = new RenderOptions().WithScale(scale);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap reference = rasterizer.Render(page, options);

        GoldenImage render = BitmapRendering.Render(page, options);

        GoldenComparison comparison = GoldenComparison.Compare(GoldenImage.FromBitmap(reference), render, GoldenThreshold.Text);
        Assert.True(comparison.Matches, comparison.Describe());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Native_glyphs_with_quadratic_contours_render_as_their_outlines_render_on_coregraphics(double scale)
    {
        // Every glyph of the sample font, the odd ones with a contour of quadratic curves that crosses their rectangle, natively
        // and from the outlines through the same CoreGraphics fill rule.
        using PdfDocument document = PdfDocument.Open(TextSamples.Page(Encoding.ASCII.GetBytes(Lines(Printable)), Alpha));
        PdfPage page = document.Pages[0];
        RenderOptions options = new RenderOptions().WithScale(scale);

        GoldenImage outlines = BitmapRendering.Render(page, options.WithForceOutlines(true));
        GoldenImage native = BitmapRendering.Render(page, options.WithForceOutlines(false));

        GoldenComparison comparison = GoldenComparison.Compare(outlines, native, GoldenThreshold.Text);
        Assert.True(comparison.Matches, comparison.Describe());
    }

    [Fact]
    public void The_display_list_overload_honours_forced_outlines()
    {
        using PdfDocument document = GoldenCorpus.Open("text-standard14.pdf");
        PdfPage page = document.Pages[0];
        RenderOptions options = GoldenCorpus.CreateRenderOptions().WithForceOutlines(true);
        PixelSize size = options.GetPixelSize(page);
        using CGBitmapContext context = BitmapRendering.CreateContext(size.Width, size.Height, out byte[] pixels, out int stride);
        using var white = new CGColor(1, 1, 1, 1);
        context.SetFillColor(white);
        context.FillRect(new CGRect(0, 0, size.Width, size.Height));
        context.ConcatCTM(new CGAffineTransform(1, 0, 0, -1, 0, size.Height));
        using var renderer = new CoreGraphicsRenderer();

        renderer.Render(page.CreateDisplayList(), context, options.GetDeviceTransform(page), forceOutlines: true);

        context.Flush();
        GoldenImage render = GoldenImage.FromPixels(pixels, size.Width, size.Height, stride, GoldenPixelFormat.Bgra8Premultiplied);
        Assert.True(GoldenComparison.Compare(BitmapRendering.Render(page, options), render, GoldenThreshold.Exact).Matches);
    }

    [Fact]
    public void Rendering_text_leaves_the_context_text_matrix_and_graphics_state_as_it_found_them()
    {
        using PdfDocument document = GoldenCorpus.Open("text-truetype-embedded.pdf");
        PdfPage page = document.Pages[0];
        using CGBitmapContext context = BitmapRendering.CreateContext(700, 900, out _, out _);
        var textMatrix = new CGAffineTransform(2, 0, 0, 3, 5, 7);
        context.TextMatrix = textMatrix;
        CGAffineTransform before = context.GetCTM();
        using var renderer = new CoreGraphicsRenderer();

        renderer.Render(page, context, GoldenCorpus.CreateRenderOptions());

        Assert.Equal(textMatrix, context.TextMatrix);
        Assert.Equal(before, context.GetCTM());
    }

    [Fact]
    public void A_program_coregraphics_cannot_load_is_drawn_from_its_outlines_and_reported_once_in_every_render()
    {
        // A parser (the public extension point) whose programs hand over bytes no font engine reads: the glyphs still draw, from
        // their outlines, as the software rasterizer draws them, and each render reports it once (ADR 0005).
        PdfOptions pdfOptions = new PdfOptions().UseSystemFontResolver(null).UseFontProgramParser(new UnloadableParser());
        using PdfDocument document = new PdfEngine(pdfOptions).Open(Corpus.Bytes("text-truetype-embedded.pdf"));
        PdfPage page = document.Pages[0];
        RenderOptions options = GoldenCorpus.CreateRenderOptions();
        PixelSize size = options.GetPixelSize(page);
        using var renderer = new CoreGraphicsRenderer();

        for (int render = 0; render < 2; render++)
        {
            using CGBitmapContext context = BitmapRendering.CreateContext(size.Width, size.Height, out byte[] pixels, out int stride);
            renderer.Render(page, context, options);
            context.Flush();

            Diagnostic diagnostic = Assert.Single(renderer.Diagnostics);
            Assert.Equal("BackendNativeGlyphsUnavailable", diagnostic.Code);
            Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity);
            GoldenCorpus.AssertMatchesReference(
                "coregraphics",
                "text-truetype-embedded.pdf",
                0,
                GoldenImage.FromPixels(pixels, size.Width, size.Height, stride, GoldenPixelFormat.Bgra8Premultiplied));
        }
    }

    /// <summary>Every page of every text corpus file (<c>text-*.pdf</c>); images and masks included (#126).</summary>
    public static IEnumerable<TheoryDataRow<string, int>> TextPages()
    {
        foreach (string fileName in Corpus.AllFileNames.Where(name => name.StartsWith("text-", StringComparison.Ordinal)))
        {
            using PdfDocument document = GoldenCorpus.Open(fileName);
            for (int page = 0; page < document.Pages.Count; page++)
            {
                yield return new TheoryDataRow<string, int>(fileName, page);
            }
        }
    }

    private static GoldenImage Blank(int width, int height) =>
        GoldenImage.FromPixels(Enumerable.Repeat((byte)255, width * height * 4).ToArray(), width, height, width * 4, GoldenPixelFormat.Bgra8Premultiplied);

    /// <summary>The ExtGState <c>/A</c> the sample lines use: constant alpha 0.5 and Multiply.</summary>
    private const string Alpha = "/ExtGState << /A << /ca 0.5 /BM /Multiply >> >>";

    /// <summary>The sample font's rectangle glyphs: the even printable codes (no parenthesis or backslash).</summary>
    private const string Rectangles = "BDFHJLNPRTVXZ bdfhjlnprtvxz 02468 \"$&*,.:<>@^|~";

    /// <summary>Every printable code the sample font has, but the parentheses and backslash.</summary>
    private const string Printable = "ABCDEFGHIJKLMNOPQRSTUVWXYZ abcdefghijklmnopqrstuvwxyz 0123456789 !\"#$%&'*+,-./:;<=>?@[]^_`{|}~";

    /// <summary>Filled text (mode 0) in the sample TrueType font at sizes from 4 to 300 pt and many transforms, opaque.</summary>
    private const string Filled =
        "BT /F1 4 Tf 36 760 Td (Four point text, the smallest: ABCDEFGHIJKLMNOPQRSTUVWXYZ) Tj ET\n" +
        "BT /F1 9 Tf 36 740 Td (Nine point text with 0123456789 and !@#$%^&*) Tj ET\n" +
        "BT /F1 12 Tf 150 Tz 2 Ts 36 715 Td (Twelve point, 150 percent wide, risen) Tj ET\n" +
        "BT 0.8 0.1 0.1 rg /F1 18 Tf 1 0.2 0.3 1 36 660 Tm (Skewed red eighteen) Tj ET\n" +
        "BT 0 0.4 0 rg /F1 16 Tf 0.866 0.5 -0.5 0.866 330 560 Tm (Rotated green) Tj ET\n" +
        "q -1 0 0 1 600 0 cm BT /F1 20 Tf 36 500 Td (Mirrored by the CTM) Tj ET Q\n" +
        "BT /F1 300 Tf 36 60 Td (Big) Tj ET\n";

    /// <summary>
    /// <paramref name="text"/> in the sample TrueType font at sizes from 4 to 300 pt, 150 % wide and risen, skewed, rotated, mirrored by
    /// the CTM, in colour, through <see cref="Alpha"/>, and filled then stroked (mode 2).
    /// </summary>
    private static string Lines(string text) =>
        $"BT /F1 4 Tf 36 760 Td ({text}) Tj ET\n" +
        $"BT /F1 9 Tf 36 740 Td ({text}) Tj ET\n" +
        $"BT /F1 12 Tf 150 Tz 2 Ts 36 715 Td ({text}) Tj ET\n" +
        $"BT 0.8 0.1 0.1 rg /F1 18 Tf 1 0.2 0.3 1 36 660 Tm ({text}) Tj ET\n" +
        $"BT 0 0.4 0 rg /F1 16 Tf 0.866 0.5 -0.5 0.866 330 460 Tm ({text}) Tj ET\n" +
        $"q -1 0 0 1 600 0 cm BT /F1 20 Tf 36 420 Td ({text}) Tj ET Q\n" +
        $"q /A gs BT 0 0 1 rg /F1 40 Tf 36 360 Td ({text}) Tj ET Q\n" +
        $"BT 2 Tr 1 0 0 RG 0.5 w 1 1 0 rg /F1 36 Tf 36 300 Td ({text}) Tj ET\n" +
        $"BT /F1 300 Tf 36 60 Td ({text[..3]}) Tj ET\n";

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
