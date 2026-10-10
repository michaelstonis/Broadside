using System.Buffers;
using System.Buffers.Binary;
using Broadside.Filters;
using Broadside.Images;
using Broadside.Objects;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>filter-jbig2</c> target (issues #64 and #65): JBIG2Decode over an embedded page stream and its globals. Bytes 0-1 and 2-3
/// are the image's Width and Height (1 to 512), bytes 4-7 a big-endian globals length (cut to what is left), then the globals
/// stream, then the page stream. An input starting with <c>%PDF-</c> is read as a file: the data after the first <c>stream</c>
/// keyword that follows <c>/JBIG2Decode</c> is the page stream, at 399 x 400 (the pdf.js test files' size), with no globals. Both
/// paths run, the image facet and the plain filter path, with 64 KiB per region (and 2^20 pixels, so the byte limit binds on both).
/// Invariants: no exception in lenient mode; a decoded image is exactly Width x Height, one 1-bit component with a byte-aligned
/// stride and its padding bits 0; the plain path writes nothing or the same samples.
/// </summary>
/// <remarks>ISO 32000-2 §7.4.7; ITU-T T.88.</remarks>
internal static class Jbig2Target
{
    private const int MaxPixels = 1 << 20;
    private const int MaxBytes = 1 << 16;

    public static void Target(ReadOnlySpan<byte> data)
    {
        int width;
        int height;
        byte[] globals;
        byte[] page;
        if (data.StartsWith("%PDF-"u8))
        {
            (width, height, globals) = (399, 400, []);
            page = FromFile(data);
        }
        else
        {
            if (data.Length < 8)
            {
                return;
            }

            width = 1 + (BinaryPrimitives.ReadUInt16BigEndian(data) % 512);
            height = 1 + (BinaryPrimitives.ReadUInt16BigEndian(data[2..]) % 512);
            int length = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(data[4..]), (uint)(data.Length - 8));
            globals = data.Slice(8, length).ToArray();
            page = data[(8 + length)..].ToArray();
        }

        var filter = new Jbig2DecodeFilter();
        var parameters = globals.Length == 0 ? null : new CosDictionary { [new CosName("JBIG2Globals")] = new CosStream(new CosDictionary(), globals) };
        var dictionary = new CosDictionary { [new CosName("Width")] = new CosInteger(width), [new CosName("Height")] = new CosInteger(height) };
        var context = new ImageFilterContext(new FilterContext { Parameters = parameters, StreamDictionary = dictionary, MaxDecodedLength = MaxBytes })
        {
            Width = width,
            Height = height,
            BitsPerComponent = 1,
            ColorComponents = 1,
            MaxPixels = MaxPixels,
        };
        using DecodedImage? image = filter.DecodeImage(page, context);
        var output = new ArrayBufferWriter<byte>();
        filter.Decode(page, output, new FilterContext { Parameters = parameters, StreamDictionary = dictionary, MaxDecodedLength = MaxBytes });
        if (image is null)
        {
            return;
        }

        int stride = (width + 7) / 8;
        if (image.Width != width || image.Height != height || image.Components != 1 || image.BitsPerComponent != 1 || image.Stride != stride || image.Samples.Length != stride * height)
        {
            throw new InvalidOperationException($"The JBIG2 image is {image.Width} x {image.Height} x {image.Components} (stride {image.Stride}) where {width} x {height} was asked for.");
        }

        if ((width & 7) != 0)
        {
            byte padding = (byte)(0xFF >> (width & 7));
            for (int y = 0; y < height; y++)
            {
                if ((image.GetRow(y)[stride - 1] & padding) != 0)
                {
                    throw new InvalidOperationException($"Row {y} of the JBIG2 image has padding bits set.");
                }
            }
        }

        if (output.WrittenCount > 0 && !output.WrittenSpan.SequenceEqual(image.Samples))
        {
            throw new InvalidOperationException("The plain filter path and the image path decoded different samples.");
        }
    }

    private static byte[] FromFile(ReadOnlySpan<byte> file)
    {
        int filter = file.IndexOf("/JBIG2Decode"u8);
        if (filter < 0)
        {
            return [];
        }

        int keyword = file[filter..].IndexOf("stream"u8);
        if (keyword < 0)
        {
            return [];
        }

        int start = filter + keyword + 6;
        start += file[start..].StartsWith("\r\n"u8) ? 2 : file[start..].StartsWith("\n"u8) ? 1 : 0;
        int end = file[start..].IndexOf("endstream"u8);
        return file.Slice(start, end < 0 ? file.Length - start : end).ToArray();
    }
}
