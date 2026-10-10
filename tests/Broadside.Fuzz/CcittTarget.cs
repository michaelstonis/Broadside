using System.Buffers;
using Broadside.Filters;
using Broadside.Filters.Ccitt;
using Broadside.Objects;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>filter-ccitt</c> target (issue #63): CCITTFaxDecode over a raw stream body with parameters taken from the first five
/// bytes. Byte 0: bits 0-1 the sign of K (-1, 0, 1, 1), bits 2-5 EndOfLine, EncodedByteAlign, EndOfBlock, BlackIs1; bytes 1-2:
/// Columns 1 to 4096; byte 3: Rows 0 to 64; byte 4: DamagedRowsBeforeError 0 to 3 (low bits) and the image's Width and Height
/// offsets (high bits). The rest is the data, decoded three ways: through <see cref="IStreamFilter.Decode"/>, through the image
/// facet at a Width and Height near Columns and Rows, and as JBIG2 MMR (<see cref="MmrDecoder"/>). Invariants: no exception in
/// lenient mode; the filter writes whole rows, at most Rows of them when EndOfBlock is false, and no more rows than the data has
/// bits (every row consumes at least one); the image is exactly Width x Height with a byte-aligned stride; MMR writes at most
/// Rows rows and consumes no more bytes than it was given.
/// </summary>
/// <remarks>ISO 32000-2 §7.4.6; ITU-T T.4, T.6, T.88 §6.2.6.</remarks>
internal static class CcittTarget
{
    private static readonly CcittFaxDecodeFilter Filter = new();

    public static void Target(ReadOnlySpan<byte> data)
    {
        if (data.Length < 5)
        {
            return;
        }

        int k = (data[0] & 3) switch { 0 => -1, 1 => 0, _ => 1 };
        bool endOfLine = (data[0] & 4) != 0;
        bool byteAlign = (data[0] & 8) != 0;
        bool endOfBlock = (data[0] & 16) != 0;
        bool blackIs1 = (data[0] & 32) != 0;
        int columns = 1 + (((data[1] << 8) | data[2]) % 4096);
        int rows = data[3] % 65;
        int damaged = data[4] & 3;
        var parameters = new CosDictionary
        {
            [new CosName("K")] = new CosInteger(k),
            [new CosName("EndOfLine")] = (endOfLine ? CosBoolean.True : CosBoolean.False),
            [new CosName("EncodedByteAlign")] = (byteAlign ? CosBoolean.True : CosBoolean.False),
            [new CosName("EndOfBlock")] = (endOfBlock ? CosBoolean.True : CosBoolean.False),
            [new CosName("BlackIs1")] = (blackIs1 ? CosBoolean.True : CosBoolean.False),
            [new CosName("Columns")] = new CosInteger(columns),
            [new CosName("Rows")] = new CosInteger(rows),
            [new CosName("DamagedRowsBeforeError")] = new CosInteger(damaged),
        };
        byte[] body = data[5..].ToArray();
        int rowBytes = (columns + 7) / 8;

        var output = new ArrayBufferWriter<byte>();
        Filter.Decode(body, output, new FilterContext { Parameters = parameters });
        int decodedRows = output.WrittenCount / rowBytes;
        if (output.WrittenCount % rowBytes != 0)
        {
            throw new InvalidOperationException($"CCITTFaxDecode wrote {output.WrittenCount} bytes, not whole rows of {rowBytes}.");
        }

        if ((!endOfBlock && rows > 0 && decodedRows > rows) || decodedRows > (8L * body.Length) + 1)
        {
            throw new InvalidOperationException($"CCITTFaxDecode wrote {decodedRows} rows from {body.Length} bytes (Rows {rows}, EndOfBlock {endOfBlock}).");
        }

        int width = Math.Max(1, columns + ((data[4] >> 2) & 7) - 3);
        int height = Math.Max(1, rows + ((data[4] >> 5) & 7));
        var context = new ImageFilterContext(new FilterContext { Parameters = parameters }) { Width = width, Height = height, BitsPerComponent = 1, ColorComponents = 1, MaxPixels = 1 << 22 };
        using (Images.DecodedImage? image = Filter.DecodeImage(body, context))
        {
            if (image is not null && (image.Width != width || image.Height != height || image.Stride != (width + 7) / 8 || image.Samples.Length != image.Stride * height || image.DecodedRows > height))
            {
                throw new InvalidOperationException($"The CCITT image is {image.Width} x {image.Height} (stride {image.Stride}) where {width} x {height} was asked for.");
            }
        }

        byte[] bitmap = new byte[rowBytes * rows];
        MmrResult mmr = MmrDecoder.Decode(body, columns, rows, bitmap, rowBytes);
        if (mmr.Rows > rows || mmr.BytesConsumed > body.Length || mmr.BytesConsumed < 0)
        {
            throw new InvalidOperationException($"MMR reported {mmr.Rows} rows and {mmr.BytesConsumed} bytes for {rows} rows of {body.Length} bytes.");
        }
    }
}
