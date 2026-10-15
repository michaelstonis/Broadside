using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// The render options that decide what a page render shows and how its pixels are covered (ISO 32000-2 §8.9.5.4, §8.11.4,
/// §10.7.4, §11.4.7, §12.5.3): the optional-content state, the view or print intent, anti-aliasing per category and the background.
/// </summary>
public sealed class RenderOptionTests
{
    private static readonly Rgba White = new(255, 255, 255, 255);

    [Fact]
    public void Optional_content_pdf_renders_differently_under_its_default_and_alternate_configurations_and_when_printed()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("optional-content.pdf"));
        PdfOptionalContentProperties properties = document.OptionalContent!;
        PdfPage page = document.Pages[0];
        using var rasterizer = new SoftwareRasterizer();

        // The 10 x 10 squares at x = 0, 20, 40, 60, 80 and 100 along the bottom of the Letter page (device rows 782..791).
        bool[] screen = Painted(rasterizer, page, new RenderOptions());
        bool[] alternate = Painted(rasterizer, page, new RenderOptions().WithOptionalContentState(properties.GetStates(properties.Configurations[0])));
        bool[] printed = Painted(rasterizer, page, new RenderOptions().WithIntent(DisplayIntent.Print));

        // Default: /a and /m2 visible, /m1 and the nested /a hidden, the form (OC B) hidden, /d2 (D2 ON) visible.
        Assert.Equal([true, false, true, false, false, true], screen);

        // "Only C" (BaseState OFF, ON [C]): A and B off, so /a hidden, /m1 (all of A, B off) and /m2 (not B) visible; D2 off.
        Assert.Equal([false, true, true, false, false, false], alternate);

        // Printing applies D's AS entry for the Print event: D2's PrintState OFF hides /d2 (§8.11.4.4).
        Assert.Equal([true, false, true, false, false, false], printed);
    }

    [Fact]
    public void An_explicit_state_is_kept_on_screen_and_printing_applies_the_print_auto_states_over_it()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("optional-content.pdf"));
        PdfOptionalContentProperties properties = document.OptionalContent!;
        PdfOptionalContentState state = properties.GetDefaultStates().With(properties.Groups[1], on: true);
        using var rasterizer = new SoftwareRasterizer();

        bool[] screen = Painted(rasterizer, document.Pages[0], new RenderOptions().WithOptionalContentState(state));
        bool[] printed = Painted(rasterizer, document.Pages[0], new RenderOptions().WithOptionalContentState(state).WithIntent(DisplayIntent.Print));

        // With B on: the /a nested in /b is visible, /m1 still hidden (A is on). The form runs too, but its square lies outside its
        // BBox [0 0 10 10], which clips it away (§8.10.1).
        Assert.Equal([true, false, true, true, false, true], screen);
        Assert.Equal([true, false, true, true, false, false], printed);
    }

    [Fact]
    public void Image_alternates_pdf_paints_the_base_images_on_screen_and_the_print_alternates_when_printed()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("image-alternates.pdf"));
        using var rasterizer = new SoftwareRasterizer();

        using Bitmap screen = rasterizer.Render(document.Pages[0]);
        using Bitmap printed = rasterizer.Render(document.Pages[0], new RenderOptions().WithIntent(DisplayIntent.Print));

        // Cells: Im1 top left, Im2 top right, Im3 bottom left, Im4 bottom right (§8.9.5.4 rules d, a/b, c, d with a hidden alternate).
        Assert.Equal([new(255, 0, 0, 255), new(0, 0, 255, 255), new(0, 255, 255, 255), new(128, 128, 128, 255)], Cells(screen));
        Assert.Equal([new(0, 255, 0, 255), new(0, 0, 255, 255), new(0, 255, 255, 255), White], Cells(printed));

        static Rgba[] Cells(Bitmap bitmap) => [bitmap.At(50, 50), bitmap.At(150, 50), bitmap.At(50, 150), bitmap.At(150, 150)];
    }

    [Fact]
    public void Anti_aliasing_off_for_paths_leaves_only_fully_covered_or_uncovered_pixels_on_paths_fill_rules_pdf()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("paths-fill-rules.pdf"));
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap smooth = rasterizer.Render(document.Pages[0]);
        using Bitmap aliased = rasterizer.Render(document.Pages[0], new RenderOptions().WithAntiAliasing(AntiAliasing.All & ~AntiAliasing.Paths));

        // The page's colours, read inside each shape of the anti-aliased render (where every pixel is fully covered): the white page,
        // the red and blue pentagrams, the CMYK ring, the green squares, the grey ring and the purple triangle.
        HashSet<Rgba> colors =
        [
            smooth.At(0, 0), smooth.At(65, 75), smooth.At(195, 30), smooth.At(285, 75), smooth.At(30, 270), smooth.At(155, 270), smooth.At(322, 252),
        ];
        Assert.Equal(7, colors.Count);

        HashSet<Rgba> painted = Colors(aliased);
        Assert.Subset(colors, painted);
        Assert.Equal(colors.Count, painted.Count);
        Assert.True(Colors(smooth).Count > 50, "anti-aliased edges blend the colours");
    }

    [Fact]
    public void Anti_aliasing_off_for_text_leaves_only_the_fill_and_stroke_colours_and_the_paths_switch_does_not_touch_text()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("text-render-modes.pdf"));
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap smooth = rasterizer.Render(document.Pages[0]);
        using Bitmap aliased = rasterizer.Render(document.Pages[0], new RenderOptions().WithAntiAliasing(AntiAliasing.Paths | AntiAliasing.Images));
        using Bitmap pathsOff = rasterizer.Render(document.Pages[0], new RenderOptions().WithAntiAliasing(AntiAliasing.Text | AntiAliasing.Images));

        // White, the blue fill and the red stroke (mode 2 strokes over its fill), nothing in between.
        Assert.Equal(3, Colors(aliased).Count);
        Assert.True(Colors(smooth).Count > 3);
        Assert.True(smooth.Pixels.SequenceEqual(pathsOff.Pixels));
    }

    [Fact]
    public void Anti_aliasing_off_for_images_samples_without_averaging_and_the_other_switches_do_not_touch_images()
    {
        // A 150 x 48 bilevel image reduced into 100 x 100 pixels at (72, 600): averaged, its reduced columns turn grey.
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("jbig2-generic.pdf"));
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap smooth = rasterizer.Render(document.Pages[0]);
        using Bitmap aliased = rasterizer.Render(document.Pages[0], new RenderOptions().WithAntiAliasing(AntiAliasing.Paths | AntiAliasing.Text));
        using Bitmap othersOff = rasterizer.Render(document.Pages[0], new RenderOptions().WithAntiAliasing(AntiAliasing.Images));

        Assert.Equal([new Rgba(0, 0, 0, 255), White], Colors(aliased).OrderBy(color => color.R));
        Assert.True(Colors(smooth).Count > 2);
        Assert.True(smooth.Pixels.SequenceEqual(othersOff.Pixels));
    }

    [Fact]
    public void A_display_list_replayed_without_anti_aliasing_draws_what_a_page_render_without_it_draws()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("clipping-paths.pdf"));
        PdfPage page = document.Pages[0];
        RenderOptions options = new RenderOptions().WithScale(1.3).WithAntiAliasing(AntiAliasing.None);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap whole = rasterizer.Render(page, options);
        PixelSize size = options.GetPixelSize(page);
        using var replayed = new Bitmap(size.Width, size.Height);
        replayed.Pixels.Fill(0xFF);

        rasterizer.Render(page.CreateDisplayList(DisplayLayers.All), replayed, options.GetDeviceTransform(page), AntiAliasing.None);

        Assert.True(whole.Pixels.SequenceEqual(replayed.Pixels));

        // Clipped paints included, every pixel is fully covered or not at all: no colour is a blend with the white page.
        using Bitmap smooth = rasterizer.Render(page, new RenderOptions().WithScale(1.3));
        Assert.True(Colors(whole).Count < Colors(smooth).Count / 4);
    }

    [Fact]
    public void A_transparent_background_leaves_the_pixels_nothing_paints_at_zero_alpha()
    {
        // paths-fill-rules.pdf has no page group (§11.4.7: the page is then an isolated group over a transparent backdrop).
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("paths-fill-rules.pdf"));
        PdfPage page = document.Pages[0];
        Assert.False(page.Dictionary.ContainsKey(new Objects.CosName("Group")));
        var options = new RenderOptions().WithTransparentBackground(true);
        using var rasterizer = new SoftwareRasterizer();

        using Bitmap bitmap = rasterizer.Render(page, options);
        using var target = new Bitmap(410, 310);
        target.Pixels.Fill(0xFF);
        rasterizer.Render(page, target, options);

        foreach (Bitmap rendered in (Bitmap[])[bitmap, target])
        {
            Assert.Equal(new Rgba(0, 0, 0, 0), rendered.At(0, 0));
            Assert.Equal(new Rgba(0, 0, 0, 0), rendered.At(399, 299));
            Assert.Equal(new Rgba(255, 0, 0, 255), rendered.At(65, 75));

            // Anti-aliased edges are partly transparent rather than blended with white; premultiplied, no channel exceeds alpha.
            HashSet<Rgba> colors = Colors(rendered);
            Assert.Contains(colors, color => color.A is > 0 and < 255);
            Assert.DoesNotContain(colors, color => color.R > color.A || color.G > color.A || color.B > color.A);
        }

        // Pixels outside the page area are left as they are.
        Assert.Equal(White, target.At(405, 305));
        Assert.True(bitmap.Pixels.Slice(0, 400 * 4).SequenceEqual(target.Pixels.Slice(0, 400 * 4)));
    }

    private static HashSet<Rgba> Colors(Bitmap bitmap)
    {
        var colors = new HashSet<Rgba>();
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                colors.Add(bitmap.At(x, y));
            }
        }

        return colors;
    }

    private static bool[] Painted(SoftwareRasterizer rasterizer, PdfPage page, RenderOptions options)
    {
        using Bitmap bitmap = rasterizer.Render(page, options);
        return [.. Enumerable.Range(0, 6).Select(index => bitmap.At((index * 20) + 5, 787) != White)];
    }
}
