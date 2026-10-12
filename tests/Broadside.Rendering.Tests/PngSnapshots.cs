using System.Runtime.CompilerServices;
using StbImageSharp;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Rendering snapshots are PNG files compared by their decoded pixels, not their bytes, so a different zlib build (the compressed
/// bytes may differ between runtimes) never fails a test whose image is the same. Decoding goes through StbImageSharp, a decoder
/// independent of the rasterizer's encoder. The golden-image harness with perceptual thresholds replaces this with #D47.
/// </summary>
internal static class PngSnapshots
{
    [ModuleInitializer]
    public static void Initialize() => VerifierSettings.RegisterStreamComparer("png", Compare);

    /// <summary>Decodes a PNG to straight (not premultiplied) RGBA, 8 bits per channel.</summary>
    public static ImageResult Decode(Stream png) => ImageResult.FromStream(png, ColorComponents.RedGreenBlueAlpha);

    /// <summary>Encodes a bitmap as PNG in memory.</summary>
    public static MemoryStream Encode(Bitmap bitmap)
    {
        var stream = new MemoryStream();
        bitmap.SavePng(stream);
        stream.Position = 0;
        return stream;
    }

    private static Task<CompareResult> Compare(Stream received, Stream verified, IReadOnlyDictionary<string, object> context)
    {
        ImageResult a = Decode(received);
        ImageResult b = Decode(verified);
        if (a.Width != b.Width || a.Height != b.Height)
        {
            return Task.FromResult(CompareResult.NotEqual($"The image is {a.Width} x {a.Height}; the snapshot is {b.Width} x {b.Height}."));
        }

        int differing = 0;
        int first = -1;
        for (int i = 0; i < a.Data.Length; i++)
        {
            if (a.Data[i] != b.Data[i])
            {
                differing++;
                first = first < 0 ? i : first;
            }
        }

        return Task.FromResult(differing == 0
            ? CompareResult.Equal
            : CompareResult.NotEqual($"{differing} channel values differ; the first at pixel ({first / 4 % a.Width}, {first / 4 / a.Width})."));
    }
}
