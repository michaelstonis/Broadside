using Broadside.Graphics;
using Broadside.Rendering;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>rasterize-page</c> target (issue #109): opens the input as a whole file and renders its first two pages' display lists
/// with the software rasterizer into a small transparent bitmap, twice, at a scale and quarter turn chosen from the input length.
/// Unlike <c>rasterize</c>, whose content has no resources, names resolve here, so the shadings of <c>sh</c> and shading patterns
/// (their coordinates, radii, Domain, Extend, Background, BBox and functions, mutated from the <c>shading-*</c> and
/// <c>pattern-*</c> corpus files), forms and image XObjects reach the rasterizer, and so do the annotation and form-field layers
/// (issue #98: appearances, flags, NoZoom/NoRotate placement, opacity layers) for the view or the print intent. The first page is
/// also rendered with overprint preview (issue #227). Invariants: nothing throws besides an unreadable input; no colour channel
/// exceeds its alpha; both renders are identical.
/// </summary>
/// <remarks>ADR 0006; ISO 32000-2 §8.7.4.1, §8.7.4.3, §8.7.4.5.3, §8.7.4.5.4, §8.9.4, §10.7.4.</remarks>
internal static class RasterizePageTarget
{
    private const int Size = 48;

    // Small limits keep a mutated image or stream from costing gigabytes.
    private static readonly PdfEngine Engine = new(new PdfOptions().WithMaxImagePixels(1 << 16).WithMaxDecodedStreamLength(1 << 20));
    private static readonly Content.ContentOptions Options = new();

    public static void Target(ReadOnlySpan<byte> data)
    {
        using PdfDocument? document = FuzzTargets.OpenOrNull(data, Engine);
        if (document is null)
        {
            return;
        }

        double scale = (1 + (data.Length % 8)) / 32.0;
        Matrix device = (data.Length / 8 % 4) switch
        {
            1 => new Matrix(0, scale, scale, 0, 0, 0),
            2 => new Matrix(-scale, 0, 0, scale, Size, 0),
            3 => new Matrix(0, -scale, -scale, 0, Size, Size),
            _ => new Matrix(scale, 0, 0, -scale, 0, Size),
        };

        // Issue #98: the annotation and form-field layers too, for the screen or for printing; NoZoom/NoRotate pivot them.
        DisplayIntent intent = data.Length / 32 % 2 == 0 ? DisplayIntent.View : DisplayIntent.Print;
        using var rasterizer = new SoftwareRasterizer();
        using var first = new Bitmap(Size, Size);
        using var second = new Bitmap(Size, Size);
        for (int index = 0; index < Math.Min(2, document.Pages.Count); index++)
        {
            DisplayList list = document.Pages[index].CreateDisplayList(DisplayLayers.All, intent, Options);
            rasterizer.Render(list, first, device);
            rasterizer.Render(list, second, device);
        }

        ReadOnlySpan<byte> pixels = first.Pixels;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte alpha = pixels[i + 3];
            Check(pixels[i] <= alpha && pixels[i + 1] <= alpha && pixels[i + 2] <= alpha, "a colour channel exceeds its alpha");
        }

        Check(pixels.SequenceEqual(second.Pixels), "two renders of one list differ");

        // Issue #227: the first page again with overprint preview (colourant planes, spot colourants, the OP/op/OPM state), scaled
        // to fit the bitmap; the result is opaque and the same twice.
        if (document.Pages.Count > 0 && PreviewOptions(document.Pages[0]) is { } preview)
        {
            rasterizer.Render(document.Pages[0], first, preview);
            rasterizer.Render(document.Pages[0], second, preview);
            Check(first.Pixels.SequenceEqual(second.Pixels), "two overprint previews of one page differ");
        }
    }

    private static RenderOptions? PreviewOptions(PdfPage page)
    {
        PdfRectangle crop = page.CropBox;
        double extent = Math.Max(crop.Width, crop.Height) * page.UserUnit;
        double scale = Size / 2.0 / extent;
        if (!(extent > 0) || !double.IsFinite(extent) || !double.IsFinite(scale) || !(scale > 0))
        {
            return null;
        }

        var options = new RenderOptions { Scale = scale, OverprintPreview = true };
        PixelSize size = options.GetPixelSize(page);
        return size.Width <= Size && size.Height <= Size ? options : null;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
