using System.Buffers;
using Broadside.Filters;
using Broadside.Images;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>filter-dct</c> target (issue #61): the input as DCTDecode data, through the plain filter path and the image facet, with
/// a 4 MiB output limit and 2^20 pixels. The decoder looks for the SOI marker anywhere, so whole corpus files that embed a JPEG
/// (<c>dct-baseline.pdf</c>) reach it. Invariants: no exception in lenient mode; the plain path writes nothing or exactly width x
/// height x components bytes, within the limit; a decoded image has 1 to 4 components of 8 bits, the §8.9.3 stride, no more
/// decoded rows than rows, the size its header gave, and the same samples as the plain path. Issue #362: the image is decoded
/// again at a reduction chosen from the input length (1/2, 1/4 or 1/8), which must give ⌈W / s⌉ x ⌈H / s⌉ samples of the same
/// components and layout, the reduction asked for and no more decoded rows than rows.
/// </summary>
/// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81.</remarks>
internal static class DctTarget
{
    private const int MaxBytes = 1 << 22;

    public static void Target(ReadOnlySpan<byte> data)
    {
        var filter = new DctDecodeFilter();
        byte[] input = data.ToArray();
        var output = new ArrayBufferWriter<byte>();
        filter.Decode(input, output, new FilterContext { MaxDecodedLength = MaxBytes });
        if (output.WrittenCount > MaxBytes)
        {
            throw new InvalidOperationException($"DCTDecode wrote {output.WrittenCount} bytes, more than the {MaxBytes} allowed.");
        }

        var context = new ImageFilterContext(new FilterContext { MaxDecodedLength = MaxBytes }) { MaxPixels = 1 << 20 };
        bool hasHeader = filter.TryReadHeader(input, context, out ImageHeader header);
        using DecodedImage? image = filter.DecodeImage(input, context);
        if (image is null)
        {
            return;
        }

        if (!hasHeader || header.Width != image.Width || header.Height != image.Height || header.Components != image.Components)
        {
            throw new InvalidOperationException($"The decoded image ({image.Width} x {image.Height} x {image.Components}) does not match its header ({header}).");
        }

        if (image.Components is < 1 or > 4 || image.BitsPerComponent != 8 || image.Stride != image.Width * image.Components
            || image.Samples.Length != image.Stride * image.Height || image.DecodedRows > image.Height)
        {
            throw new InvalidOperationException($"The decoded image breaks the 8.9.3 layout: {image.Width} x {image.Height} x {image.Components}, stride {image.Stride}, {image.DecodedRows} decoded rows.");
        }

        if (output.WrittenCount > 0 && !output.WrittenSpan.SequenceEqual(image.Samples))
        {
            throw new InvalidOperationException("The plain filter path and the image path decoded different samples.");
        }

        int reduction = 1 + (data.Length % 3);
        var reducedContext = new ImageFilterContext(new FilterContext { MaxDecodedLength = MaxBytes }) { MaxPixels = 1 << 20, Reduction = reduction };
        using DecodedImage? reduced = filter.DecodeImage(input, reducedContext);
        if (reduced is null)
        {
            return;
        }

        int scale = 1 << reduction;
        if (reduced.Reduction != reduction || reduced.Width != (image.Width + scale - 1) / scale || reduced.Height != (image.Height + scale - 1) / scale
            || reduced.Components != image.Components || reduced.Stride != reduced.Width * reduced.Components
            || reduced.Samples.Length != reduced.Stride * reduced.Height || reduced.DecodedRows > reduced.Height)
        {
            throw new InvalidOperationException(
                $"The image decoded at 1/{scale} is {reduced.Width} x {reduced.Height} x {reduced.Components} at reduction {reduced.Reduction}, {reduced.DecodedRows} decoded rows, from {image.Width} x {image.Height}.");
        }
    }
}
