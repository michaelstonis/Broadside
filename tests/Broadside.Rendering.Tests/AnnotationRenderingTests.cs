using Broadside.Graphics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Annotation and form-field layers through the software rasterizer (ISO 32000-2 §12.5.3, §12.5.5, §12.7), observed as pixels:
/// the layers composite in order to the whole page, NoZoom and NoRotate keep an annotation's size and orientation on screen, and
/// an annotation's opacity applies to its appearance as a group.
/// </summary>
public sealed class AnnotationRenderingTests
{
    private static readonly Rgba Black = new(0, 0, 0, 255);
    private static readonly Rgba Red = new(255, 0, 0, 255);
    private static readonly Rgba Blue = new(0, 0, 255, 255);
    private static readonly Rgba Green = new(0, 153, 0, 255);
    private static readonly Rgba Grey = new(128, 128, 128, 255);
    private static readonly Rgba Yellow = new(255, 204, 0, 255);

    [Theory]
    [InlineData("annotations-appearance.pdf", 0)]
    [InlineData("annotations-subtypes.pdf", 0)]
    [InlineData("acroform-fields.pdf", 0)]
    [InlineData("acroform-fields.pdf", 1)]
    [InlineData("annotations-nozoom.pdf", 1)]
    public void The_whole_page_is_the_content_annotation_and_form_field_layers_composited_in_order(string fileName, int pageIndex)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(fileName));
        PdfPage page = document.Pages[pageIndex];
        var options = new RenderOptions().WithScale(2);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap whole = rasterizer.Render(page, options);

        PixelSize size = options.GetPixelSize(page);
        using var layered = new Bitmap(size.Width, size.Height);
        layered.Pixels.Fill(0xFF);
        Matrix device = options.GetDeviceTransform(page);
        foreach (DisplayLayers layer in (DisplayLayers[])[DisplayLayers.Content, DisplayLayers.Annotations, DisplayLayers.FormFields])
        {
            rasterizer.Render(page.CreateDisplayList(layer), layered, device);
        }

        Assert.True(whole.Pixels.SequenceEqual(layered.Pixels));
        using Bitmap content = rasterizer.Render(page, new RenderOptions().WithScale(2).WithLayers(DisplayLayers.Content));
        Assert.False(whole.Pixels.SequenceEqual(content.Pixels), "the overlays draw something on this page");
    }

    [Fact]
    public void Each_layer_draws_only_its_own_annotations()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("acroform-fields.pdf"));
        using var rasterizer = new SoftwareRasterizer();

        // The name field (widget 10, Rect [50 700 250 720]) shows "Ada" in black: only in the form-field layer.
        using Bitmap fields = rasterizer.Render(document.Pages[0], new RenderOptions().WithLayers(DisplayLayers.FormFields));
        using Bitmap others = rasterizer.Render(document.Pages[0], new RenderOptions().WithLayers(DisplayLayers.Content | DisplayLayers.Annotations));

        Assert.True(Dark(fields, 50, 792 - 720, 250, 792 - 700) > 20);
        Assert.Equal(0, Dark(others, 50, 792 - 720, 250, 792 - 700));
    }

    [Fact]
    public void NoZoom_annotations_keep_their_size_at_400_percent_and_the_others_scale()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("annotations-nozoom.pdf"));
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[0], new RenderOptions().WithScale(4));

        // NoZoom (red): pivots about its upper-left corner (40, 260) -> (160, 160) and stays 40 x 40 with its 10-pixel band on top.
        Assert.Equal((160, 170, 200, 200), Bounds(bitmap, Red));
        Assert.Equal(Black, bitmap.At(180, 165));

        // NoZoom and NoRotate (green) and the text annotation (yellow, implicitly both, §12.5.6.4) keep their size too.
        Assert.Equal((160, 730, 200, 760), Bounds(bitmap, Green));
        Assert.Equal((480, 525, 500, 540), Bounds(bitmap, Yellow));

        // NoRotate only (blue) and no flags (grey) scale with the page: 160 x 160 pixels below a 40-pixel band.
        Assert.Equal((720, 200, 880, 320), Bounds(bitmap, Blue));
        Assert.Equal((720, 760, 880, 880), Bounds(bitmap, Grey));
    }

    [Fact]
    public void NoRotate_annotations_stay_upright_about_their_upper_left_corner_when_the_page_is_rotated()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("annotations-nozoom.pdf"));
        using var rasterizer = new SoftwareRasterizer();

        // Page 2 is page 1 with /Rotate 90: default user space (x, y) lands on pixel (y, x).
        using Bitmap bitmap = rasterizer.Render(document.Pages[1]);

        // NoRotate (blue): its upper-left corner (180, 260) -> (260, 180); the square hangs right and down from it, band on top.
        Assert.Equal((260, 190, 300, 220), Bounds(bitmap, Blue));
        Assert.Equal(Black, bitmap.At(280, 185));

        // No flags (grey): turned with the page, so its band is on the right.
        Assert.Equal((80, 180, 110, 220), Bounds(bitmap, Grey));
        Assert.Equal(Black, bitmap.At(115, 200));

        // A rotation asked of the renderer is the same as the page's own.
        using Bitmap turned = rasterizer.Render(document.Pages[0], new RenderOptions().WithRotation(90));
        Assert.True(turned.Pixels.SequenceEqual(bitmap.Pixels));
    }

    [Fact]
    public void An_annotation_with_NoZoom_and_NoRotate_keeps_its_size_and_orientation_at_400_percent_on_a_rotated_page()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("annotations-nozoom.pdf"));
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[1], new RenderOptions().WithScale(4));

        // Upper-left corner (40, 120) -> (480, 160); 40 x 40, upright, band on top.
        Assert.Equal((480, 170, 520, 200), Bounds(bitmap, Green));
        Assert.Equal(Black, bitmap.At(500, 165));
    }

    [Fact]
    public void Printing_places_NoZoom_annotations_like_any_other()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("annotations-nozoom.pdf"));
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[0], new RenderOptions().WithScale(4).WithIntent(DisplayIntent.Print));

        Assert.Equal((160, 200, 320, 320), Bounds(bitmap, Red));
    }

    [Fact]
    public void An_annotation_with_an_opacity_composites_its_appearance_with_it()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("annotations-appearance.pdf"));
        document.Pages[0].Annotations[0].Dictionary[new CosName("CA")] = new CosReal(0.5);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);

        // Red at half opacity over white (§12.5.5; ca defaults to CA). The rectangle [100 100 150 200] is y 592..692 on the page.
        Rgba pixel = bitmap.At(125, 792 - 150);
        Assert.Equal(255, pixel.R);
        Assert.InRange(pixel.G, 126, 129);
        Assert.Equal(pixel.G, pixel.B);
        Assert.Equal(new Rgba(255, 255, 255, 255), bitmap.At(125, 792 - 205));
    }

    /// <summary>The bounds (left, top, right, bottom; right and bottom exclusive) of the pixels exactly of <paramref name="color"/>.</summary>
    private static (int Left, int Top, int Right, int Bottom) Bounds(Bitmap bitmap, Rgba color)
    {
        int left = int.MaxValue;
        int top = int.MaxValue;
        int right = -1;
        int bottom = -1;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.At(x, y) == color)
                {
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x + 1);
                    bottom = Math.Max(bottom, y + 1);
                }
            }
        }

        return (left, top, right, bottom);
    }

    private static int Dark(Bitmap bitmap, int left, int top, int right, int bottom)
    {
        int count = 0;
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                Rgba pixel = bitmap.At(x, y);
                count += pixel.R + pixel.G + pixel.B < 300 ? 1 : 0;
            }
        }

        return count;
    }
}
