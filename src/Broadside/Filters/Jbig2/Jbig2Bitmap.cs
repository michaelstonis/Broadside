using System.Runtime.InteropServices;

namespace Broadside.Filters.Jbig2;

/// <summary>The combination operators of T.88 §7.4.1.5 (region segment flags) and §7.4.8.5 (page default operator).</summary>
internal enum Jbig2CombinationOperator
{
    /// <summary>OR.</summary>
    Or = 0,

    /// <summary>AND.</summary>
    And = 1,

    /// <summary>XOR.</summary>
    Xor = 2,

    /// <summary>XNOR.</summary>
    Xnor = 3,

    /// <summary>REPLACE.</summary>
    Replace = 4,
}

/// <summary>
/// A view of a packed 1-bit JBIG2 bitmap: rows of <see cref="Stride"/> bytes, MSB first, 1 = black (T.88 §5.4: pixel (0, 0) is the top
/// left), the bits after the last column always 0. The memory belongs to the caller (a pooled region buffer, a symbol arena, or the
/// decoded image itself for the page).
/// </summary>
/// <remarks>ITU-T T.88 §4.3 (the combination operators), §5.4 (bit packing), §6.2.5.2 (pixels outside a bitmap are 0).</remarks>
internal readonly ref struct Jbig2Bitmap
{
    /// <summary>Initializes a new instance of the <see cref="Jbig2Bitmap"/> struct.</summary>
    /// <param name="data">At least <paramref name="height"/> rows of <paramref name="stride"/> bytes.</param>
    /// <param name="width">The pixels in a row.</param>
    /// <param name="height">The rows.</param>
    /// <param name="stride">The bytes per row, at least <c>ceil(width / 8)</c>.</param>
    public Jbig2Bitmap(Span<byte> data, int width, int height, int stride)
    {
        Data = data[..(stride * height)];
        Width = width;
        Height = height;
        Stride = stride;
    }

    /// <summary>Gets the rows.</summary>
    public Span<byte> Data { get; }

    /// <summary>Gets the pixels in a row.</summary>
    public int Width { get; }

    /// <summary>Gets the rows.</summary>
    public int Height { get; }

    /// <summary>Gets the bytes per row.</summary>
    public int Stride { get; }

    /// <summary>Gets a value indicating whether the bitmap has no pixels (also the "no SKIP bitmap" value).</summary>
    public bool IsEmpty => Width == 0 || Height == 0;

    /// <summary>The bytes per row of a packed bitmap <paramref name="width"/> pixels wide.</summary>
    public static int StrideOf(int width) => (int)(((long)width + 7) >> 3);

    /// <summary>Gets row <paramref name="y"/>.</summary>
    public Span<byte> Row(int y) => Data.Slice(y * Stride, Stride);

    /// <summary>The pixel at (<paramref name="x"/>, <paramref name="y"/>); 0 outside the bitmap (T.88 §6.2.5.2).</summary>
    public int GetPixel(int x, int y) =>
        (uint)x < (uint)Width && (uint)y < (uint)Height ? (Data[(y * Stride) + (x >> 3)] >> (~x & 7)) & 1 : 0;

    /// <summary>Sets every pixel to <paramref name="pixel"/>, keeping the bits after the last column 0.</summary>
    public void Fill(int pixel)
    {
        Data.Fill(pixel == 0 ? (byte)0 : (byte)0xFF);
        if (pixel != 0)
        {
            ClearPadding();
        }
    }

    /// <summary>Inverts every pixel (JBIG2's 1 = black to the PDF's 0 = black, ISO 32000-2 §7.4.7) and clears the bits after the last column.</summary>
    public void Invert()
    {
        Span<byte> data = Data;
        Span<ulong> words = MemoryMarshal.Cast<byte, ulong>(data);
        for (int i = 0; i < words.Length; i++)
        {
            words[i] = ~words[i];
        }

        for (int i = words.Length * sizeof(ulong); i < data.Length; i++)
        {
            data[i] = (byte)~data[i];
        }

        ClearPadding();
    }

    /// <summary>Sets the bits after the last column of every row to 0, and the bytes past <c>ceil(width / 8)</c>.</summary>
    public void ClearPadding()
    {
        int used = StrideOf(Width);
        int rest = Width & 7;
        byte keep = rest == 0 ? (byte)0xFF : (byte)(0xFF << (8 - rest));
        if (rest == 0 && used == Stride)
        {
            return;
        }

        for (int y = 0; y < Height; y++)
        {
            Span<byte> row = Row(y);
            if (used > 0)
            {
                row[used - 1] &= keep;
            }

            row[used..].Clear();
        }
    }

    /// <summary>
    /// Combines <paramref name="source"/> into this bitmap with its top left at (<paramref name="x"/>, <paramref name="y"/>), clipped
    /// to this bitmap: under the source rectangle each pixel becomes <c>op(this, source)</c>; REPLACE takes the source pixel.
    /// </summary>
    /// <remarks>ITU-T T.88 §4.3, §7.4.1.5 and §8.2 step 5 a). Byte at a time with edge masks; allocates nothing.</remarks>
    public void Compose(Jbig2Bitmap source, long x, long y, Jbig2CombinationOperator op)
    {
        long left = Math.Max(0, x);
        long right = Math.Min(Width, x + source.Width);
        long top = Math.Max(0, y);
        long bottom = Math.Min(Height, y + source.Height);
        if (left >= right || top >= bottom)
        {
            return;
        }

        int x0 = (int)left;
        int x1 = (int)right;
        int firstByte = x0 >> 3;
        int lastByte = (x1 - 1) >> 3;
        byte firstMask = (byte)(0xFF >> (x0 & 7));
        byte lastMask = (byte)(0xFF << (7 - ((x1 - 1) & 7)));
        for (int row = (int)top; row < (int)bottom; row++)
        {
            ReadOnlySpan<byte> sourceRow = source.Row((int)(row - y));
            Span<byte> destination = Row(row);
            for (int b = firstByte; b <= lastByte; b++)
            {
                int mask = 0xFF;
                if (b == firstByte)
                {
                    mask &= firstMask;
                }

                if (b == lastByte)
                {
                    mask &= lastMask;
                }

                int s = SourceByte(sourceRow, (int)((b << 3) - x));
                int d = destination[b];
                int combined = op switch
                {
                    Jbig2CombinationOperator.Or => d | s,
                    Jbig2CombinationOperator.And => d & s,
                    Jbig2CombinationOperator.Xor => d ^ s,
                    Jbig2CombinationOperator.Xnor => ~(d ^ s),
                    _ => s,
                };
                destination[b] = (byte)((d & ~mask) | (combined & mask));
            }
        }
    }

    /// <summary>The eight source pixels starting at column <paramref name="x"/> (which may be negative), MSB first; 0 outside the row.</summary>
    private static int SourceByte(ReadOnlySpan<byte> row, int x)
    {
        int q = x >> 3;
        int r = x & 7;
        int high = (uint)q < (uint)row.Length ? row[q] : 0;
        if (r == 0)
        {
            return high;
        }

        int low = (uint)(q + 1) < (uint)row.Length ? row[q + 1] : 0;
        return (((high << 8) | low) >> (8 - r)) & 0xFF;
    }
}
