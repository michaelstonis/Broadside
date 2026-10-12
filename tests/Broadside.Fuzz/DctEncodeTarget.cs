using System.Buffers;
using Broadside.Filters;
using Broadside.Images;
using Broadside.Objects;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>filter-dct-encode</c> target (issue #143): byte 0 selects the component count (1, 3 or 4), the <c>ColorTransform</c>
/// parameter (absent, 0 or 1) and the chroma subsampling; byte 1 the quality (1 to 100); bytes 2 and 3 the width and height (1 to
/// 64); the rest are the samples, repeated to fill the image (zeros when there are none). The image is encoded twice, which must
/// give the same bytes, and decoded through <see cref="DctDecodeFilter"/> in strict mode with the same parameters: no diagnostic,
/// a header of the image's size and components, and exactly width x height x components samples. Any input is a valid seed.
/// </summary>
/// <remarks>ISO 32000-2 §7.4.8, Table 13; ITU-T T.81 Annex A, B, F.1 and K.</remarks>
internal static class DctEncodeTarget
{
    public static void Target(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            return;
        }

        int components = (data[0] % 3) switch
        {
            0 => 1,
            1 => 3,
            _ => 4,
        };
        int transform = (data[0] / 3) % 3;
        var subsampling = (DctChromaSubsampling)((data[0] / 9) % 3);
        int quality = 1 + (data[1] % 100);
        int width = 1 + (data[2] % 64);
        int height = 1 + (data[3] % 64);
        ReadOnlySpan<byte> body = data[4..];
        byte[] samples = new byte[width * height * components];
        if (!body.IsEmpty)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = body[i % body.Length];
            }
        }

        var dictionary = new CosDictionary
        {
            [new CosName("Width")] = new CosInteger(width),
            [new CosName("Height")] = new CosInteger(height),
            [new CosName("BitsPerComponent")] = new CosInteger(8),
        };
        CosDictionary? parameters = transform == 0 ? null : new CosDictionary { [new CosName("ColorTransform")] = new CosInteger(transform - 1) };
        var encoder = new DctEncoder(quality, subsampling);
        var context = new StreamEncoderContext { StreamDictionary = dictionary, Parameters = parameters };
        var first = new ArrayBufferWriter<byte>();
        var second = new ArrayBufferWriter<byte>();
        encoder.Encode(samples, first, context);
        encoder.Encode(samples, second, context);
        if (!first.WrittenSpan.SequenceEqual(second.WrittenSpan))
        {
            throw new InvalidOperationException("The same samples encoded to different bytes.");
        }

        byte[] jpeg = first.WrittenSpan.ToArray();
        var filter = new DctDecodeFilter();
        var filterContext = new FilterContext { Parameters = parameters, StreamDictionary = dictionary, ReadingMode = PdfReadingMode.Strict };
        if (!filter.TryReadHeader(jpeg, new ImageFilterContext(filterContext), out ImageHeader header)
            || header.Width != width || header.Height != height || header.Components != components)
        {
            throw new InvalidOperationException($"The encoded header does not describe the {width} x {height} x {components} image.");
        }

        var decoded = new ArrayBufferWriter<byte>();
        filter.Decode(jpeg, decoded, filterContext);
        if (decoded.WrittenCount != samples.Length || filterContext.Diagnostics.Count != 0)
        {
            throw new InvalidOperationException($"The encoded image decoded to {decoded.WrittenCount} bytes, not {samples.Length}, with {filterContext.Diagnostics.Count} diagnostics.");
        }
    }
}
