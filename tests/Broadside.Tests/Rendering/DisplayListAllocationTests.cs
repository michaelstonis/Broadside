using System.Text;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Rendering;

/// <summary>
/// The display list is on the hot path (ADR 0006, CLAUDE.md "Code conventions"): building allocates per page, not per operation,
/// and replaying allocates nothing. <c>DisplayListBenchmarks</c> shows the same in its <c>Allocated</c> column.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class DisplayListAllocationTests
{
    [Fact]
    public void Replaying_a_display_list_allocates_nothing()
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(ContentSamples.PathHeavy(64 * 1024)));
        DisplayList list = document.Pages[0].CreateDisplayList();
        var backend = new CountingBackend();
        Action replay = () => list.Replay(backend);

        long allocated = Allocations.Measure(replay);

        Assert.Equal(0, allocated);
        Assert.True(backend.Fills > 1000);
        Assert.True(backend.Strokes > 1000);
    }

    [Fact]
    public void Replaying_a_region_allocates_nothing_once_the_list_has_its_bounds()
    {
        // The bounds are computed by the first region replay (the warm-up); every later one only reads them.
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(ContentSamples.PathHeavy(64 * 1024)));
        DisplayList list = document.Pages[0].CreateDisplayList();
        var backend = new CountingBackend();
        var region = new PdfRectangle(100, 100, 300, 300);
        Action replay = () => list.Replay(backend, region);

        long allocated = Allocations.Measure(replay);

        Assert.Equal(0, allocated);
        Assert.True(backend.Fills > 0);
    }

    [Fact]
    public void Replaying_glyph_runs_allocates_nothing()
    {
        // Text in an embedded TrueType font in modes 0, 1 and 2, with every text state operator: runs of filled, stroked and both.
        using PdfDocument document = PdfDocument.Open(TextSamples.Page(ContentSamples.TextHeavy(2000)));
        DisplayList list = document.Pages[0].CreateDisplayList();
        var backend = new CountingBackend();
        Action replay = () => list.Replay(backend);

        long allocated = Allocations.Measure(replay);

        Assert.Equal(0, allocated);
        Assert.True(backend.Glyphs > 2000);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(2000)]
    public void Building_a_display_list_of_text_allocates_the_glyph_records_and_nothing_else_per_glyph(int glyphs)
    {
        // Each glyph's record (glyph id, text matrix, scale and outline index: 72 bytes) is copied into the list, with its hit-test
        // position (box and text slice: 40 bytes) and its Unicode text (a char or two); outlines are read once per glyph id, so
        // nothing else may grow with the glyph count.
        using PdfDocument document = PdfDocument.Open(TextSamples.Page(ContentSamples.TextHeavy(glyphs)));
        PdfPage page = document.Pages[0];
        Action build = () => page.CreateDisplayList();

        long allocated = Allocations.Measure(build);

        Assert.InRange(allocated, 1, 8192 + (120 * glyphs));
    }

    [Theory]
    [InlineData(28)]
    [InlineData(2016)]
    public void Building_a_display_list_of_type3_text_runs_each_glyph_description_once_and_allocates_only_the_records_of_each_show(int glyphs)
    {
        // Four Type 3 glyphs (a d1 shape, a d1 inline image mask, a d0 glyph, a d1 glyph with a form) in three colours and two sizes:
        // each description runs once per build, then every show copies its sub-list's records into the list: 16-byte operations and
        // 48-byte transforms, 80 bytes for a, 112 for b, 96 for c and 208 for d (7 operations, 2 transforms), 124 on average.
        using PdfDocument document = PdfDocument.Open(Type3Samples.Type3Heavy(glyphs));
        PdfPage page = document.Pages[0];
        Action build = () => page.CreateDisplayList();

        long allocated = Allocations.Measure(build);

        Assert.InRange(allocated, 1, 8192 + (144 * glyphs));
    }

    [Fact]
    public void Replaying_type3_glyphs_allocates_nothing()
    {
        using PdfDocument document = PdfDocument.Open(Type3Samples.Type3Heavy(2000));
        DisplayList list = document.Pages[0].CreateDisplayList();
        var backend = new CountingBackend();
        Action replay = () => list.Replay(backend);

        long allocated = Allocations.Measure(replay);

        Assert.Equal(0, allocated);
        Assert.True(backend.Fills > 2000);
        Assert.True(backend.Strokes > 400);
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [InlineData(48)]
    [InlineData(2000)]
    public void Building_a_display_list_of_text_clips_allocates_the_glyph_and_clip_outline_records_and_nothing_else_per_glyph(int glyphs)
    {
        // Each glyph's record (72 bytes) and its outline in the text clip's path (up to 12 points of 16 bytes and 9 verbs) are copied
        // into the list; outlines are read once per glyph id and the clip path is built in a reused buffer.
        using PdfDocument document = PdfDocument.Open(TextSamples.Page(TextSamples.TextClipHeavy(glyphs)));
        PdfPage page = document.Pages[0];
        Action build = () => page.CreateDisplayList();

        long allocated = Allocations.Measure(build);

        Assert.InRange(allocated, 1, 16384 + (600 * glyphs));
    }

    [Fact]
    public void Replaying_text_clips_allocates_nothing()
    {
        using PdfDocument document = PdfDocument.Open(TextSamples.Page(TextSamples.TextClipHeavy(2000)));
        DisplayList list = document.Pages[0].CreateDisplayList();
        var backend = new CountingBackend();
        Action replay = () => list.Replay(backend);

        long allocated = Allocations.Measure(replay);

        Assert.Equal(0, allocated);
        Assert.True(backend.Glyphs > 2000);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(2000)]
    public void Building_a_display_list_allocates_per_page_not_per_operation(int fills)
    {
        // The same rectangle in the same colour: one interned path, one colour, one record per fill. Anything allocated per fill
        // (a path copy, a colour object, a boxed operation) would cost far more than the 16 bytes per operation allowed here.
        var content = new StringBuilder("0.5 0 0.25 0.1 k ");
        for (int i = 0; i < fills; i++)
        {
            content.Append("10 10 100 50 re f\n");
        }

        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(Encoding.ASCII.GetBytes(content.ToString())));
        PdfPage page = document.Pages[0];
        Action build = () => page.CreateDisplayList();

        long allocated = Allocations.Measure(build);

        Assert.InRange(allocated, 1, 2048 + (16 * fills));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(2000)]
    public void Building_a_display_list_of_dashed_strokes_allocates_per_page_not_per_operation(int strokes)
    {
        // The same dashed rectangle in the same colour and line parameters: one path, one colour, one stroke record.
        var content = new StringBuilder("0 0 1 RG 3 w 1 J 1 j [6 2 1 2] 1 d ");
        for (int i = 0; i < strokes; i++)
        {
            content.Append("10 10 100 50 re S\n");
        }

        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(Encoding.ASCII.GetBytes(content.ToString())));
        PdfPage page = document.Pages[0];
        Action build = () => page.CreateDisplayList();

        long allocated = Allocations.Measure(build);

        Assert.InRange(allocated, 1, 2048 + (16 * strokes));
    }

    /// <summary>Counts fills and strokes and touches what each call hands over, without allocating.</summary>
    private sealed class CountingBackend : IRenderingBackend
    {
        public int Fills { get; private set; }

        public int Strokes { get; private set; }

        public int Glyphs { get; private set; }

        public double Checksum { get; private set; }

        public void SaveState() => Checksum++;

        public void RestoreState() => Checksum--;

        public void SetTransform(in Matrix transform) => Checksum += transform.E;

        public void FillPath(PathView path, FillRule rule, in DisplayColor color)
        {
            Fills++;
            Checksum += path.Points.Length + color.Components.Length + (int)color.ColorSpace.Family;
        }

        public void StrokePath(PathView path, in StrokeStyle style, in DisplayColor color)
        {
            Strokes++;
            Checksum += path.Points.Length + style.LineWidth + style.DashArray.Length + color.Components.Length;
        }

        public void IntersectClip(PathView path, FillRule rule) => Checksum += path.Points.Length + (int)rule;

        public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion) =>
            Checksum += image.Width + image.Height;

        public void DrawGlyphs(in GlyphRun run)
        {
            Glyphs += run.Glyphs.Length;
            for (int i = 0; i < run.Glyphs.Length; i++)
            {
                Checksum += run.Glyphs[i].GlyphId + run.GetOutline(i).Points.Length + run.GetGlyphTransform(i).E;
            }
        }

        public void DrawShading(in DisplayShading shading) => Checksum += shading.Matrix.A;

        public void FillPathWithShading(PathView path, FillRule rule, in DisplayShading shading)
        {
            Fills++;
            Checksum += path.Points.Length + shading.Matrix.A;
        }

        public void StrokePathWithShading(PathView path, in StrokeStyle style, in DisplayShading shading)
        {
            Strokes++;
            Checksum += path.Points.Length + style.LineWidth + shading.Matrix.A;
        }

        public void FillPathWithTilingPattern(PathView path, FillRule rule, in DisplayTilingPattern pattern)
        {
            Fills++;
            Checksum += path.Points.Length + pattern.Matrix.A;
        }

        public void StrokePathWithTilingPattern(PathView path, in StrokeStyle style, in DisplayTilingPattern pattern)
        {
            Strokes++;
            Checksum += path.Points.Length + style.LineWidth + pattern.Matrix.A;
        }

        public void FillImageMask(DisplayImageMask mask, in DisplayColor color) =>
            Checksum += mask.Width + mask.Height + color.Components.Length;

        public void SetCompositing(double strokeAlpha, double fillAlpha, BlendMode blendMode) =>
            Checksum += strokeAlpha + fillAlpha + (int)blendMode;

        public void BeginAnnotation(in DisplayAnnotation annotation) => Checksum += annotation.Rectangle.Width + annotation.Opacity;

        public void EndAnnotation() => Checksum++;
    }
}
