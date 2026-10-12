using System.Buffers;
using System.Security.Cryptography;
using Broadside.Filters;
using Broadside.Images;
using Broadside.Objects;

namespace Broadside.Tests.Filters;

/// <summary>Calls the JBIG2Decode filter through its public contract with a stand-alone context (ISO 32000-2 §7.4.7).</summary>
internal static class Jbig2Testing
{
    private static readonly CosName Globals = new("JBIG2Globals");

    /// <summary>Decodes an embedded JBIG2 page stream through <see cref="IImageFilter.DecodeImage"/>.</summary>
    public static DecodedImage? TryDecodeImage(
        byte[] page,
        int width,
        int height,
        out string[] codes,
        byte[]? globals = null,
        PdfReadingMode mode = PdfReadingMode.Lenient,
        long maxPixels = PdfOptions.DefaultMaxImagePixels)
    {
        var context = Context(globals, mode, width, height);
        var imageContext = new ImageFilterContext(context) { Width = width, Height = height, BitsPerComponent = 1, ColorComponents = 1, MaxPixels = maxPixels };
        DecodedImage? image = new Jbig2DecodeFilter().DecodeImage(page, imageContext);
        codes = [.. context.Diagnostics.Select(diagnostic => diagnostic.Code)];
        return image;
    }

    /// <summary>Decodes an embedded JBIG2 page stream through <see cref="IImageFilter.DecodeImage"/>; fails when nothing is decoded.</summary>
    public static DecodedImage DecodeImage(byte[] page, int width, int height, out string[] codes, byte[]? globals = null, PdfReadingMode mode = PdfReadingMode.Lenient)
    {
        DecodedImage? image = TryDecodeImage(page, width, height, out codes, globals, mode);
        Assert.NotNull(image);
        return image;
    }

    /// <summary>Decodes an embedded JBIG2 page stream through the plain <see cref="IStreamFilter.Decode"/> path.</summary>
    public static (byte[] Decoded, string[] Codes) Decode(byte[] page, int width, int height, byte[]? globals = null, PdfReadingMode mode = PdfReadingMode.Lenient)
    {
        var context = Context(globals, mode, width, height);
        var output = new ArrayBufferWriter<byte>();
        new Jbig2DecodeFilter().Decode(page, output, context);
        return (output.WrittenSpan.ToArray(), [.. context.Diagnostics.Select(diagnostic => diagnostic.Code)]);
    }

    /// <summary>The SHA-256 of a byte array, lower-case hex.</summary>
    public static string Sha256(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private static FilterContext Context(byte[]? globals, PdfReadingMode mode, int width, int height) => new()
    {
        ReadingMode = mode,
        Parameters = globals is null ? null : new CosDictionary { [Globals] = new CosStream(new CosDictionary(), globals) },
        StreamDictionary = new CosDictionary
        {
            [new CosName("Width")] = new CosInteger(width),
            [new CosName("Height")] = new CosInteger(height),
            [new CosName("BitsPerComponent")] = new CosInteger(1),
        },
    };
}
