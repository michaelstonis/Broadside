using System.IO.Compression;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Objects;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>image-optimize</c> target (issue #625): random image dictionaries through the save-time image pass. Input: one mode byte,
/// then a COS dictionary in PDF syntax, a line feed, and the image data. The dictionary and data become an image XObject that page 0
/// paints into a square of 8 to 2048 points (mode bits 0 to 2), and the pass runs with a resolution cap of 18 to 288 dpi for colour
/// and bitonal images, a JPEG quality or none (bit 3), and lossless images allowed to become JPEG or not (bit 4). A whole corpus file
/// (<c>%PDF-</c>) runs the pass over every page. The engine's limits are tiny (2^16 pixels, 1 MiB) so mutated sizes stay cheap.
/// Invariants: no exception in lenient mode; an image the pass changed is no longer than before, has the planned size, and decodes
/// to samples of that size; an image it kept is byte for byte what it was.
/// </summary>
/// <remarks>ISO 32000-2 §8.9.5, §8.9.5.3, §8.9.6, §7.4.4 and §7.4.8.</remarks>
internal static class ImageOptimizeTarget
{
    private static readonly PdfEngine Engine = new(new PdfOptions().WithMaxImagePixels(1 << 16).WithMaxDecodedLength(1 << 20));

    public static void Target(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith("%PDF-"u8))
        {
            using PdfDocument? file = FuzzTargets.OpenOrNull(data, Engine);
            if (file is not null)
            {
                Check(file, ImageOptimizer.Apply(file, new PdfImageOptimization().WithMaxResolution(72).WithMaxBitonalResolution(150).WithJpegQuality(50).WithRecompressLossless(), CompressionLevel.Fastest), []);
            }

            return;
        }

        if (data.IsEmpty)
        {
            return;
        }

        byte mode = data[0];
        ReadOnlySpan<byte> rest = data[1..];
        int newline = rest.IndexOf((byte)'\n');
        ReadOnlySpan<byte> dictionaryText = newline < 0 ? rest : rest[..newline];
        ReadOnlySpan<byte> samples = newline < 0 ? [] : rest[(newline + 1)..];
        if (new CosParser(dictionaryText, repairs: null).ParseObject() is not CosDictionary dictionary)
        {
            return;
        }

        using PdfDocument document = Engine.Create();
        var stream = new CosStream(dictionary, samples.ToArray());
        CosReference reference = document.AddObject(stream);
        int size = 8 << (mode & 7);
        PdfPage page = document.Pages[0];
        page.Dictionary[new CosName("Resources")] = new CosDictionary { [new CosName("XObject")] = new CosDictionary { [new CosName("Im0")] = reference } };
        page.Dictionary[new CosName("Contents")] = document.AddObject(new CosStream(new CosDictionary(), System.Text.Encoding.ASCII.GetBytes($"q {size} 0 0 {size} 0 0 cm /Im0 Do Q")));
        double cap = 18 << ((mode >> 5) & 3);
        var options = new PdfImageOptimization()
            .WithMaxResolution(cap)
            .WithMaxBitonalResolution(cap * 2)
            .WithResampleThreshold(1)
            .WithJpegQuality((mode & 8) != 0 ? 1 + (mode % 100) : null)
            .WithRecompressLossless((mode & 16) != 0);
        var before = new Dictionary<CosStream, byte[]>(ReferenceEqualityComparer.Instance) { [stream] = stream.EncodedData.ToArray() };
        Check(document, ImageOptimizer.Apply(document, options, CompressionLevel.Fastest), before);
    }

    private static void Check(PdfDocument document, List<PdfImageOptimizationResult> results, Dictionary<CosStream, byte[]> before)
    {
        foreach (PdfImageOptimizationResult result in results)
        {
            if (result.Image.Stream is not { } stream)
            {
                continue;
            }

            if (result.Actions == PdfImageOptimizationActions.None)
            {
                if (before.TryGetValue(stream, out byte[]? original) && !original.AsSpan().SequenceEqual(stream.EncodedData.Span))
                {
                    throw new InvalidOperationException($"The pass kept the image ({result.Reason}) but its data changed.");
                }

                continue;
            }

            if (result.OptimizedLength >= result.OriginalLength)
            {
                throw new InvalidOperationException($"The pass grew the image from {result.OriginalLength} to {result.OptimizedLength} bytes.");
            }

            PdfImage image = document.GetImage(stream)!;
            if (image.Width != result.TargetWidth || image.Height != result.TargetHeight)
            {
                throw new InvalidOperationException($"The image is {image.Width} x {image.Height}; the pass planned {result.TargetWidth} x {result.TargetHeight}.");
            }

            using DecodedImage? decoded = image.Decode();
            if (decoded is null || decoded.Geometry.Width != result.TargetWidth || decoded.Geometry.Height != result.TargetHeight || decoded.DecodedRows != decoded.Geometry.Height)
            {
                throw new InvalidOperationException("The image the pass wrote does not decode whole at its new size.");
            }
        }
    }
}
