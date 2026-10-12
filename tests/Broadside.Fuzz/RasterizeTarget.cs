using Broadside.Content;
using Broadside.Graphics;
using Broadside.Rendering;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>rasterize</c> target (issue #95): runs the input as a page's decoded content through the display-list builder, then
/// renders the list with the software rasterizer into a small transparent bitmap, at a scale and rotation chosen from the input
/// length, twice, and writes the result as PNG. Invariants: nothing throws (whatever coordinates, transforms or colours the content
/// holds); every pixel is valid premultiplied colour (no channel above its alpha); two renders of one list are identical. Each call
/// runs in a new empty document.
/// </summary>
/// <remarks>ADR 0006; ISO 32000-2 §8.5.3, §10.7.4.</remarks>
internal static class RasterizeTarget
{
    private const int Size = 48;

    public static void Target(ReadOnlySpan<byte> data)
    {
        using PdfDocument document = PdfDocument.Create();
        var builder = new DisplayListBuilder();
        ContentInterpreter.RunBytes(data, document, builder, ContentInterpreter.DefaultOptions);
        DisplayList list = builder.ToDisplayList(document);

        // Page space to the bitmap: scale 1/16 to 1/2, quarter turns, so content anywhere on a letter page lands on or near it.
        double scale = (1 + (data.Length % 8)) / 16.0;
        Matrix device = (data.Length / 8 % 4) switch
        {
            1 => new Matrix(0, scale, scale, 0, 0, 0),
            2 => new Matrix(-scale, 0, 0, scale, Size, 0),
            3 => new Matrix(0, -scale, -scale, 0, Size, Size),
            _ => new Matrix(scale, 0, 0, -scale, 0, Size),
        };

        using var rasterizer = new SoftwareRasterizer();
        using var first = new Bitmap(Size, Size);
        using var second = new Bitmap(Size, Size);
        rasterizer.Render(list, first, device);
        rasterizer.Render(list, second, device);

        ReadOnlySpan<byte> pixels = first.Pixels;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte alpha = pixels[i + 3];
            Check(pixels[i] <= alpha && pixels[i + 1] <= alpha && pixels[i + 2] <= alpha, "a colour channel exceeds its alpha");
        }

        Check(pixels.SequenceEqual(second.Pixels), "two renders of one list differ");
        first.SavePng(Stream.Null);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
