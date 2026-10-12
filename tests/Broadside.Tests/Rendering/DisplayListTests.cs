using System.Collections.Concurrent;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;
using static Broadside.Tests.Colors.ColorTesting;

namespace Broadside.Tests.Rendering;

/// <summary>
/// The display list and the backend contract (ADR 0006; ISO 32000-2 §8.5.3, §8.6): a page builds a list whose fills carry the
/// colour as specified (colour space and components), replayed through <see cref="IRenderingBackend"/> and recorded by
/// <see cref="RecordingBackend"/>.
/// </summary>
public sealed class DisplayListTests
{
    [Fact]
    public void Fills_of_colorspace_families_pdf_carry_their_colour_space_family_and_components_unconverted()
    {
        var counting = new CountingColorManagement(ManagedColorManagement.Default);
        using PdfDocument document = PdfDocument.Open(Corpus.Path("colorspace-families.pdf"), new PdfOptions().UseColorManagement(counting));

        DisplayList list = document.Pages[0].CreateDisplayList();
        var recorder = new RecordingBackend();
        list.Replay(recorder);

        // The twelfth rectangle is painted with a tiling pattern, which a later ticket brings to the display list.
        Assert.Equal(
            [
                "fill nonzero M 40 640 L 140 640 L 140 740 L 40 740 Z | DeviceGray 0.5",
                "fill nonzero M 180 640 L 280 640 L 280 740 L 180 740 Z | DeviceRgb 1 0 0",
                "fill nonzero M 320 640 L 420 640 L 420 740 L 320 740 Z | DeviceCmyk 0 1 0 0",
                "fill nonzero M 460 640 L 560 640 L 560 740 L 460 740 Z | CalGray 0.5",
                "fill nonzero M 40 500 L 140 500 L 140 600 L 40 600 Z | CalRgb 0 0 1",
                "fill nonzero M 180 500 L 280 500 L 280 600 L 180 600 Z | Lab 50 60 40",
                "fill nonzero M 320 500 L 420 500 L 420 600 L 320 600 Z | IccBased 0 1 0",
                "fill nonzero M 460 500 L 560 500 L 560 600 L 460 600 Z | IccBased 0.25",
                "fill nonzero M 40 360 L 140 360 L 140 460 L 40 460 Z | Indexed 2",
                "fill nonzero M 180 360 L 280 360 L 280 460 L 180 460 Z | Separation 1",
                "fill nonzero M 320 360 L 420 360 L 420 460 L 320 460 Z | DeviceN 1 0.4",
            ],
            recorder.Operations);
        PdfSeparationColorSpace separation = Assert.IsType<PdfSeparationColorSpace>(recorder.Fills[9].ColorSpace);
        Assert.Equal("Spot", separation.ColorantName.Value);
        Assert.IsType<PdfIccBasedColorSpace>(recorder.Fills[6].ColorSpace);
        Assert.Empty(counting.Requested);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Fills_of_color_operators_pdf_carry_the_colour_each_operator_set()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("color-operators.pdf"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        // B fills, then strokes (the strokes have a test of their own); the pattern fill is a later ticket's. The inline image (#102)
        // is drawn in its own q/Q after them.
        string[] fills = [.. recorder.Operations.Where(static line => line.StartsWith("fill ", StringComparison.Ordinal))];
        Assert.Equal(
            ["DeviceGray 0.75", "DeviceRgb 0 0 1", "DeviceCmyk 0 1 0 0", "CalGray 0.8", "Separation 1", "Separation 0.25", "DeviceRgb 0 0 0"],
            fills.Select(static line => line[(line.IndexOf('|', StringComparison.Ordinal) + 2)..]));
        Assert.All(fills, static line => Assert.StartsWith("fill nonzero M ", line, StringComparison.Ordinal));
        Assert.Equal(
            ["save", "transform [200 0 0 100 40 300]", "image 2x1 Indexed bpc 8", "restore"],
            recorder.Operations.Where(static line => !line.StartsWith("fill ", StringComparison.Ordinal) && !line.StartsWith("stroke ", StringComparison.Ordinal)));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Colour_is_converted_only_by_the_backend_through_the_colour_management_extension_point()
    {
        var counting = new CountingColorManagement(ManagedColorManagement.Default);
        using PdfDocument document = PdfDocument.Open(Corpus.Path("colorspace-families.pdf"), new PdfOptions().UseColorManagement(counting));
        DisplayList list = document.Pages[0].CreateDisplayList();
        Assert.Empty(counting.Requested);

        var backend = new ConvertingBackend(list.Document);
        list.Replay(backend);

        // The same device colours CorpusColorTests reads through the content processor seam.
        int[][] expected =
        [
            [128, 128, 128], [255, 0, 0], [236, 41, 144], [128, 128, 128], [0, 0, 255], [214, 60, 55], [0, 255, 0], [64, 64, 64],
            [0, 0, 255], [250, 168, 54], [0, 131, 200],
        ];
        Assert.Equal(expected.Length, backend.Colors.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Near([.. expected[i].Select(v => v / 255f)], backend.Colors[i], 12 / 255.0);
        }

        Assert.Equal(
            [PdfColorSpaceFamily.DeviceGray, PdfColorSpaceFamily.DeviceRgb, PdfColorSpaceFamily.DeviceCmyk, PdfColorSpaceFamily.CalGray,
             PdfColorSpaceFamily.CalRgb, PdfColorSpaceFamily.Lab, PdfColorSpaceFamily.IccBased],
            counting.Requested.Keys.Order());
    }

    [Fact]
    public void Default_colour_spaces_travel_with_the_colour_and_apply_when_the_backend_converts()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("default-colorspaces.pdf"));
        var recorder = new RecordingBackend();
        var converting = new ConvertingBackend(document);
        DisplayList list = document.Pages[0].CreateDisplayList();

        list.Replay(recorder);
        list.Replay(converting);

        // The selected space stays DeviceRGB/DeviceGray; the page's CalRGB and CalGray defaults (and inside the form, the form's
        // own DefaultRGB) are carried beside it (§8.6.5.6), as CorpusColorTests reads them through the processor seam.
        Assert.Equal([PdfColorSpaceFamily.DeviceRgb, PdfColorSpaceFamily.DeviceGray, PdfColorSpaceFamily.DeviceRgb], recorder.Fills.Select(f => f.ColorSpace.Family));
        Assert.IsType<PdfCalRgbColorSpace>(recorder.Fills[0].Defaults.Rgb);
        Assert.NotSame(recorder.Fills[0].Defaults.Rgb, recorder.Fills[2].Defaults.Rgb);
        Near([0.7354f, 0.7354f, 0.7354f], converting.Colors[0], 2e-3);
        Near([0.7354f, 0.7354f, 0.7354f], converting.Colors[1], 2e-3);
        Near([0.504f, 0.504f, 0.504f], converting.Colors[2], 3e-3);
    }

    [Fact]
    public void The_rendering_intent_and_black_point_compensation_of_the_fill_travel_with_the_colour()
    {
        using PdfDocument document = PdfDocument.Open(Page("<< /ExtGState << /G0 << /UseBlackPtComp /OFF >> >> >>", "/Saturation ri /G0 gs 0 0 1 1 re f"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        ColorConversion conversion = Assert.Single(recorder.Fills).Conversion;
        Assert.Equal(RenderingIntent.Saturation, conversion.Intent);
        Assert.Equal(BlackPointCompensation.Off, conversion.BlackPointCompensation);
        Assert.Equal(DeviceColorModel.Rgb, conversion.Target);
    }

    [Fact]
    public void Transforms_are_set_before_the_paints_they_apply_to_and_save_restore_pairs_with_nothing_painted_are_dropped()
    {
        using PdfDocument document = PdfDocument.Open(Page(
            "<< >>",
            "q 2 0 0 2 10 20 cm 0 0 1 1 re f Q q Q q 1 0 0 1 5 5 cm Q 0 0 1 1 re f* 3 0 0 3 0 0 cm 1 g 0 0 1 1 re 2 2 m 3 2 l 3 3 l f"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Equal(
            [
                "save",
                "transform [2 0 0 2 10 20]",
                "fill nonzero M 0 0 L 1 0 L 1 1 L 0 1 Z | DeviceGray 0",
                "restore",
                "fill evenodd M 0 0 L 1 0 L 1 1 L 0 1 Z | DeviceGray 0",
                "transform [3 0 0 3 0 0]",
                "fill nonzero M 0 0 L 1 0 L 1 1 L 0 1 Z M 2 2 L 3 2 L 3 3 | DeviceGray 1",
            ],
            recorder.Operations);
    }

    [Fact]
    public void A_form_xobject_paints_inside_its_own_saved_state_through_the_form_matrix()
    {
        using PdfDocument document = PdfDocument.Open(Page(
            "<< /XObject << /Fm0 5 0 R >> >>",
            "q 1 0 0 1 100 100 cm /Fm0 Do Q 0 0 5 5 re f",
            ContentSamples.Stream("0.5 g 0 0 10 10 re f", "/Type /XObject /Subtype /Form /BBox [0 0 10 10] /Matrix [2 0 0 2 0 0]")));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Equal(
            [
                "save",
                "save",
                "transform [2 0 0 2 100 100]",
                "clip nonzero M 0 0 L 10 0 L 10 10 L 0 10 Z",
                "fill nonzero M 0 0 L 10 0 L 10 10 L 0 10 Z | DeviceGray 0.5",
                "restore",
                "restore",
                "fill nonzero M 0 0 L 5 0 L 5 5 L 0 5 Z | DeviceGray 0",
            ],
            recorder.Operations);
    }

    [Fact]
    public void Paths_ended_with_n_paint_nothing_and_w_n_only_clips()
    {
        using PdfDocument document = PdfDocument.Open(Page("<< >>", "0 0 1 1 re n 0 0 1 1 re W n"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Equal(["clip nonzero M 0 0 L 1 0 L 1 1 L 0 1 Z"], recorder.Operations);
    }

    [Fact]
    public void A_stroke_carries_the_path_the_stroking_colour_and_the_line_parameters_of_the_graphics_state()
    {
        using PdfDocument document = PdfDocument.Open(Page(
            "<< /ExtGState << /SA << /SA true >> >> >>",
            "0.5 G 1 0 0 rg 0 0 m 10 0 l S "
            + "2 0 0 2 0 0 cm 3 w 1 J 2 j 4 M [3 1] 0.5 d /SA gs 0 0 1 RG 0 0 m 5 5 l 5 0 l s "
            + "0 0 10 10 re B"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        // §8.5.3.2 and Table 59: S strokes with the stroking colour; s closes first; B fills with the nonstroking colour, then strokes.
        Assert.Equal(
            [
                "stroke 1 Butt Miter 10 [] 0 M 0 0 L 10 0 | DeviceGray 0.5",
                "transform [2 0 0 2 0 0]",
                "stroke 3 Round Bevel 4 [3 1] 0.5 adjust M 0 0 L 5 5 L 5 0 Z | DeviceRgb 0 0 1",
                "fill nonzero M 0 0 L 10 0 L 10 10 L 0 10 Z | DeviceRgb 1 0 0",
                "stroke 3 Round Bevel 4 [3 1] 0.5 adjust M 0 0 L 10 0 L 10 10 L 0 10 Z | DeviceRgb 0 0 1",
            ],
            recorder.Operations);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Strokes_of_color_operators_pdf_carry_the_stroking_colour_each_operator_set()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("color-operators.pdf"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        // Each B is a fill, then a stroke 8 wide; the pattern-coloured pair is a later ticket's.
        Assert.Equal(
            ["DeviceGray 0.25", "DeviceRgb 1 0 0", "DeviceCmyk 0 0 0 1", "CalGray 0.2", "Separation 1", "Separation 0.5", "DeviceCmyk 0 0 0 1"],
            recorder.Operations.Where(static line => line.StartsWith("stroke 8 ", StringComparison.Ordinal)).Select(static line => line[(line.IndexOf('|', StringComparison.Ordinal) + 2)..]));
    }

    [Fact]
    public void The_same_list_replays_identically_on_eight_threads_after_its_document_is_closed()
    {
        var document = PdfDocument.Open(Corpus.Path("colorspace-families.pdf"));
        DisplayList list = document.Pages[0].CreateDisplayList();
        var sequential = new RecordingBackend();
        list.Replay(sequential);
        document.Dispose();

        const int threadCount = 8;
        var results = new ConcurrentBag<List<string>>();
        using var start = new Barrier(threadCount);
        Thread[] threads = [.. Enumerable.Range(0, threadCount).Select(_ => new Thread(() =>
        {
            start.SignalAndWait();
            for (int round = 0; round < 50; round++)
            {
                var recorder = new RecordingBackend();
                list.Replay(recorder);
                results.Add(recorder.Operations);
            }
        }))];
        foreach (Thread thread in threads)
        {
            thread.Start();
        }

        foreach (Thread thread in threads)
        {
            thread.Join();
        }

        Assert.Equal(threadCount * 50, results.Count);
        Assert.All(results, recorded => Assert.Equal(sequential.Operations, recorded));
    }

    [Fact]
    public void Building_honours_the_content_options()
    {
        using PdfDocument document = PdfDocument.Open(Page("<< >>", "0 0 1 1 re f"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => document.Pages[0].CreateDisplayList(new Broadside.Content.ContentOptions().WithCancellation(cancelled.Token)));
        Assert.Equal(1, document.Pages[0].CreateDisplayList(new Broadside.Content.ContentOptions()).Count());
    }

    /// <summary>Converts every fill to RGB the way a backend does: through the document's converter for the colour it was handed.</summary>
    private sealed class ConvertingBackend(PdfDocument document) : IRenderingBackend
    {
        public List<float[]> Colors { get; } = [];

        public void SaveState()
        {
        }

        public void RestoreState()
        {
        }

        public void SetTransform(in Matrix transform)
        {
        }

        public void FillPath(PathView path, FillRule rule, in DisplayColor color)
        {
            PdfColorConverter converter = document.GetColorConverter(color.ColorSpace, color.Conversion, color.DefaultColorSpaces);
            float[] rgb = new float[3];
            converter.Convert(color.Components, rgb);
            Colors.Add(rgb);
        }

        public void StrokePath(PathView path, in StrokeStyle style, in DisplayColor color)
        {
        }

        public void IntersectClip(PathView path, FillRule rule)
        {
        }

        public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion)
        {
        }

        public void DrawShading(in DisplayShading shading)
        {
        }

        public void FillPathWithShading(PathView path, FillRule rule, in DisplayShading shading)
        {
        }

        public void StrokePathWithShading(PathView path, in StrokeStyle style, in DisplayShading shading)
        {
        }
    }

    /// <summary>Counts the converters requested per source family, then defers to the inner management.</summary>
    private sealed class CountingColorManagement(IColorManagement inner) : IColorManagement
    {
        public ConcurrentDictionary<PdfColorSpaceFamily, int> Requested { get; } = new();

        public IColorConverter CreateConverter(PdfColorSpace source, ColorConversion conversion)
        {
            Requested.AddOrUpdate(source.Family, 1, static (_, count) => count + 1);
            return inner.CreateConverter(source, conversion);
        }
    }
}

/// <summary>Helpers for the display-list tests.</summary>
internal static class DisplayListTesting
{
    /// <summary>Returns the number of calls a replay makes.</summary>
    public static int Count(this DisplayList list)
    {
        var recorder = new RecordingBackend();
        list.Replay(recorder);
        return recorder.Operations.Count;
    }
}

