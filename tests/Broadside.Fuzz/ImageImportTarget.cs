using Broadside.Images;
using Broadside.TestSupport;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>tiff</c>, <c>gif</c> and <c>bmp</c> targets (issue #642): an input that starts with the format's signature is imported as it
/// is; any other input becomes a valid file of that format first (byte 0 picks the layout and compression, bytes 1 and 2 the width
/// and height, 1 to 32, the rest the samples), so that the decoders see well-formed structure with the fuzzer's data. The file goes
/// through <see cref="PdfDocument.CreateImagesFromTiff"/>, <see cref="PdfDocument.CreateImageFromGif"/> or
/// <see cref="PdfDocument.CreateImageFromBmp"/>; refusals are an <see cref="ArgumentException"/> made from the reader's
/// <see cref="InvalidDataException"/> or a <see cref="NotSupportedException"/>, and anything else is a finding. Every image made must
/// decode to exactly its width x height, as must its soft mask, and be the same image when made again.
/// </summary>
/// <remarks>TIFF 6.0; GIF89a; the Windows GDI bitmap formats; ISO 32000-2 §8.9.5, §7.4.6, §7.4.8, §11.6.5.2.</remarks>
internal static class ImageImportTarget
{
    // A pixel limit that keeps a mutated header from asking for gigabytes; the readers check it before allocating.
    private static readonly PdfOptions Options = new PdfOptions().WithMaxImagePixels(1 << 22);

    // (samples, bits, photometric, compression): the TIFF layouts the synthesizer writes.
    private static readonly (int Samples, int Bits, int Photometric, int Compression)[] TiffLayouts =
    [
        (1, 1, 0, 1), (1, 1, 0, 2), (1, 1, 0, 3), (1, 1, 1, 4), (1, 1, 0, 32773), (1, 4, 1, 5), (1, 8, 1, 5), (1, 8, 3, 8),
        (3, 8, 2, 5), (3, 16, 2, 8), (4, 8, 2, 32773), (4, 8, 5, 5), (2, 16, 1, 1), (1, 2, 3, 1),
    ];

    public static void Tiff(ReadOnlySpan<byte> data)
    {
        byte[] tiff = data is [(byte)'I', (byte)'I', ..] or [(byte)'M', (byte)'M', ..] ? data.ToArray() : SynthesizeTiff(data);
        using PdfDocument document = PdfDocument.Create(Options);
        IReadOnlyList<PdfImage> images;
        try
        {
            images = document.CreateImagesFromTiff(tiff);
        }
        catch (Exception e) when (Refused(e))
        {
            return;
        }

        foreach (PdfImage image in images)
        {
            Check(image);
        }

        if (!images.Select(i => i.Stream).SequenceEqual(document.CreateImagesFromTiff(tiff).Select(i => i.Stream)))
        {
            throw new InvalidOperationException("The same TIFF made other images.");
        }
    }

    public static void Gif(ReadOnlySpan<byte> data)
    {
        byte[] gif = data.StartsWith("GIF8"u8) ? data.ToArray() : SynthesizeGif(data);
        using PdfDocument document = PdfDocument.Create(Options);
        PdfImage image;
        try
        {
            image = document.CreateImageFromGif(gif);
        }
        catch (Exception e) when (Refused(e))
        {
            return;
        }

        Check(image);
        if (!ReferenceEquals(image.Stream, document.CreateImageFromGif(gif).Stream))
        {
            throw new InvalidOperationException("The same GIF made a second image.");
        }
    }

    public static void Bmp(ReadOnlySpan<byte> data)
    {
        byte[] bmp = data.StartsWith("BM"u8) ? data.ToArray() : SynthesizeBmp(data);
        using PdfDocument document = PdfDocument.Create(Options);
        PdfImage image;
        try
        {
            image = document.CreateImageFromBmp(bmp);
        }
        catch (Exception e) when (Refused(e))
        {
            return;
        }

        Check(image);
        if (!ReferenceEquals(image.Stream, document.CreateImageFromBmp(bmp).Stream))
        {
            throw new InvalidOperationException("The same BMP made a second image.");
        }
    }

    /// <summary>The documented refusals: an <see cref="ArgumentException"/> wrapping the reader's reason, or an unsupported feature.</summary>
    private static bool Refused(Exception e) =>
        e is NotSupportedException || (e.GetType() == typeof(ArgumentException) && e.InnerException is InvalidDataException or null);

    private static void Check(PdfImage image)
    {
        using DecodedImage decoded = image.Decode() ?? throw new InvalidOperationException("A made image does not decode.");
        if (decoded.Geometry.Width != image.Width || decoded.DecodedRows != image.Height)
        {
            throw new InvalidOperationException($"A made {image.Width} x {image.Height} image decodes to {decoded.Geometry.Width} x {decoded.DecodedRows}.");
        }

        if (image.MaskKind == PdfImageMaskKind.Soft)
        {
            Check(image.Mask!);
        }
    }

    private static byte[] Samples(ReadOnlySpan<byte> data, int length)
    {
        byte[] samples = new byte[length];
        for (int i = 0; i < length && data.Length > 0; i++)
        {
            samples[i] = data[i % data.Length];
        }

        return samples;
    }

    private static byte[] SynthesizeTiff(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            return [];
        }

        (int samples, int bits, int photometric, int compression) = TiffLayouts[data[0] % TiffLayouts.Length];
        int width = 1 + (data[1] % 32);
        int height = 1 + (data[2] % 32);
        int stride = ((width * samples * bits) + 7) / 8;
        byte[] pixels = Samples(data[4..], stride * height);
        ushort[]? map = photometric == 3 ? [.. Enumerable.Range(0, 3 << bits).Select(i => (ushort)(i * 997))] : null;
        int flags = data[3];
        var page = new TiffPage(width, height, samples, bits, photometric, pixels)
        {
            Compression = compression,
            ColorMap = map,
            RowsPerStrip = (flags & 1) != 0 ? 1 + (flags >> 4) : 0,
            Tile = (flags & 2) != 0 && compression is not (2 or 3 or 4) ? 16 : 0,
            Planar = (flags & 4) != 0 && samples > 1 ? 2 : 1,
            Predictor = (flags & 8) != 0 && bits >= 8 && compression is 5 or 8 ? 2 : 1,
            ExtraSamples = samples is 2 or 4 && photometric != 5 ? [1 + (flags >> 7)] : null,
            T4Options = compression == 3 ? flags & 5 : 0,
            Orientation = 1 + (flags % 8),
            Resolution = (72 + flags, 300),
        };
        return ImportSamples.Tiff((flags & 0x40) != 0, page);
    }

    private static byte[] SynthesizeGif(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            return [];
        }

        int bits = 1 + (data[0] % 8);
        int width = 1 + (data[1] % 32);
        int height = 1 + (data[2] % 32);
        byte[] indices = Samples(data[4..], width * height);
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] &= (byte)((1 << bits) - 1);
        }

        byte[] palette = [.. Enumerable.Range(0, 3 << bits).Select(i => (byte)(i * 37))];
        return ImportSamples.Gif(width, height, indices, palette, interlaced: (data[3] & 1) != 0, transparent: (data[3] & 2) != 0 ? data[3] >> 4 : -1, local: (data[3] & 4) != 0);
    }

    private static byte[] SynthesizeBmp(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            return [];
        }

        (int bits, int compression) = (data[0] % 7) switch
        {
            0 => (1, 0),
            1 => (4, 0),
            2 => (8, 0),
            3 => (8, 1),
            4 => (4, 2),
            5 => (16, 0),
            _ => (data[3] % 2 == 0 ? 24 : 32, 0),
        };
        int width = 1 + (data[1] % 32);
        int height = 1 + (data[2] % 32);
        int entries = bits <= 8 ? 1 << bits : 0;
        byte[] table = [.. Enumerable.Range(0, entries * 4).Select(i => (byte)(i * 13))];
        int stride = ((width * bits) + 31) / 32 * 4;
        byte[] pixels = Samples(data[4..], compression == 0 ? stride * height : Math.Max(data.Length - 4, 2));
        return ImportSamples.Bmp(width, (data[3] & 0x80) != 0 ? -height : height, bits, compression, table, pixels);
    }
}
