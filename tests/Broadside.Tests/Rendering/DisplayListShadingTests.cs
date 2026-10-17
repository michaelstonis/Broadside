using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;
using static Broadside.Tests.Colors.ColorTesting;

namespace Broadside.Tests.Rendering;

/// <summary>
/// Shadings in the display list (ISO 32000-2 §8.7.4; ADR 0006): <c>sh</c> reaches the backend as
/// <see cref="IRenderingBackend.DrawShading"/>, filling the current clip without the shading's Background; a fill or stroke in a
/// shading pattern reaches it as <see cref="IRenderingBackend.FillPathWithShading"/> or
/// <see cref="IRenderingBackend.StrokePathWithShading"/>, with the pattern space and the Background. The shading arrives as its
/// resolved model, its colours unconverted.
/// </summary>
public sealed class DisplayListShadingTests
{
    private const string AxialShading =
        "<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 100 0] /Background [0.5 0.5 0.5] "
        + "/Function << /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >> >>";

    [Fact]
    public void Sh_fills_the_current_clip_with_the_shading_in_user_space_and_without_its_background()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("shading-type2-axial.pdf"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Equal(
            [
                "save",
                "clip nonzero M 0 300 L 612 300 L 612 500 L 0 500 Z",
                "shading Axial [1 0 0 1 0 0]",
                "restore",
            ],
            recorder.Operations);
        PdfAxialShading shading = Assert.IsType<PdfAxialShading>(Assert.Single(recorder.Shadings).Shading);
        Assert.Equal(new PathPoint(72, 400), shading.Start);
        Assert.IsType<PdfDeviceRgbColorSpace>(shading.ColorSpace);
    }

    [Fact]
    public void Sh_maps_the_shading_through_the_ctm_and_carries_the_state_s_conversion_and_default_spaces()
    {
        using PdfDocument document = PdfDocument.Open(Page(
            "<< /Shading << /Sh0 5 0 R >> >>",
            "/Perceptual ri 2 0 0 2 10 20 cm /Sh0 sh",
            AxialShading));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Equal(["shading Axial [2 0 0 2 10 20]"], recorder.Operations);
        (_, _, _, PdfDefaultColorSpaces defaults, ColorConversion conversion) = Assert.Single(recorder.Shadings);
        Assert.Equal(RenderingIntent.Perceptual, conversion.Intent);
        Assert.Equal(DeviceColorModel.Rgb, conversion.Target);
        Assert.True(defaults.IsEmpty);
    }

    [Fact]
    public void A_fill_in_a_shading_pattern_fills_the_path_with_the_shading_in_pattern_space_and_its_background()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("pattern-shading-axial.pdf"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Equal(["fill nonzero M 50 50 L 550 50 L 550 550 L 50 550 Z | shading Axial [0.5 0 0 0.5 0 0] background"], recorder.Operations);
    }

    [Fact]
    public void The_pattern_space_is_fixed_where_the_pattern_was_selected_not_by_the_ctm_at_the_paint()
    {
        // §8.7.2: pattern space maps to the default space of the stream that selected the pattern; a later cm moves the path only.
        using PdfDocument document = PdfDocument.Open(Page(
            "<< /Pattern << /P0 5 0 R >> >>",
            "/Pattern cs /P0 scn 2 0 0 2 0 0 cm 0 0 10 10 re f",
            "<< /Type /Pattern /PatternType 2 /Matrix [1 0 0 1 5 5] /Shading 6 0 R >>",
            AxialShading));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Equal(
            ["transform [2 0 0 2 0 0]", "fill nonzero M 0 0 L 10 0 L 10 10 L 0 10 Z | shading Axial [1 0 0 1 5 5] background"],
            recorder.Operations);
    }

    [Fact]
    public void A_stroke_in_a_shading_pattern_strokes_the_path_with_the_shading()
    {
        using PdfDocument document = PdfDocument.Open(Page(
            "<< /Pattern << /P0 5 0 R >> >>",
            "/Pattern CS /P0 SCN 4 w 0 0 m 100 0 l S",
            "<< /Type /Pattern /PatternType 2 /Shading 6 0 R >>",
            AxialShading));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Equal(["stroke 4 Butt Miter 10 [] 0 M 0 0 L 100 0 | shading Axial [1 0 0 1 0 0] background"], recorder.Operations);
    }

    [Fact]
    public void Unusable_shadings_and_tiling_patterns_paint_nothing_in_the_list()
    {
        // An axial shading with no Function is not valid (§8.7.4.5.3, Table 79); a missing name paints nothing; a tiling pattern
        // with neither steps nor a bounding box is not valid (§8.7.3.1, Table 74).
        using PdfDocument document = PdfDocument.Open(Page(
            "<< /Shading << /Bad 5 0 R >> /Pattern << /T 6 0 R >> >>",
            "/Bad sh /Missing sh /Pattern cs /T scn 0 0 10 10 re f",
            "<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 1 0] >>",
            ContentSamples.Stream("0 0 1 1 re f", "/Type /Pattern /PatternType 1 /PaintType 1 /TilingType 1 /Resources << >>")));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Empty(recorder.Operations);
    }

    [Fact]
    public void A_shading_painted_inside_a_saved_state_keeps_the_save_and_restore()
    {
        using PdfDocument document = PdfDocument.Open(Page("<< /Shading << /Sh0 5 0 R >> >>", "q /Sh0 sh Q", AxialShading));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Equal(["save", "shading Axial [1 0 0 1 0 0]", "restore"], recorder.Operations);
    }
}
