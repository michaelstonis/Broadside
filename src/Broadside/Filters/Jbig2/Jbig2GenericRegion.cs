using System.Runtime.CompilerServices;
using Broadside.Filters.Ccitt;
using Broadside.Filters.Codecs;

namespace Broadside.Filters.Jbig2;

/// <summary>
/// The parameters of the generic region decoding procedure (ITU-T T.88 §6.2.2, Table 2) other than the size, which is the
/// destination bitmap's. AT locations are stored as <see langword="int"/>: the pattern dictionary procedure (§6.7.5) places A1 at
/// (-HDPW, 0), outside Figure 7's range.
/// </summary>
internal readonly struct Jbig2GenericParameters
{
    /// <summary>Gets the template, 0 to 3 (GBTEMPLATE).</summary>
    public int Template { get; init; }

    /// <summary>Gets a value indicating whether typical prediction is on (TPGDON).</summary>
    public bool TypicalPrediction { get; init; }

    /// <summary>Gets a value indicating whether pixels set in the SKIP bitmap are skipped (USESKIP).</summary>
    public bool UseSkip { get; init; }

    /// <summary>Gets GBATX1.</summary>
    public int AtX1 { get; init; }

    /// <summary>Gets GBATY1.</summary>
    public int AtY1 { get; init; }

    /// <summary>Gets GBATX2 (template 0 only).</summary>
    public int AtX2 { get; init; }

    /// <summary>Gets GBATY2 (template 0 only).</summary>
    public int AtY2 { get; init; }

    /// <summary>Gets GBATX3 (template 0 only).</summary>
    public int AtX3 { get; init; }

    /// <summary>Gets GBATY3 (template 0 only).</summary>
    public int AtY3 { get; init; }

    /// <summary>Gets GBATX4 (template 0 only).</summary>
    public int AtX4 { get; init; }

    /// <summary>Gets GBATY4 (template 0 only).</summary>
    public int AtY4 { get; init; }

    /// <summary>The parameters of <paramref name="template"/> with its AT pixels at their nominal locations (Table 5).</summary>
    public static Jbig2GenericParameters Nominal(int template, bool typicalPrediction = false) => template switch
    {
        0 => new() { Template = 0, TypicalPrediction = typicalPrediction, AtX1 = 3, AtY1 = -1, AtX2 = -3, AtY2 = -1, AtX3 = 2, AtY3 = -2, AtX4 = -2, AtY4 = -2 },
        1 => new() { Template = 1, TypicalPrediction = typicalPrediction, AtX1 = 3, AtY1 = -1 },
        _ => new() { Template = template, TypicalPrediction = typicalPrediction, AtX1 = 2, AtY1 = -1 },
    };
}

/// <summary>
/// The generic region decoding procedure (ITU-T T.88 §6.2): template-based arithmetic decoding with the four templates, AT pixels,
/// typical prediction and skipped pixels (§6.2.5), or MMR (§6.2.6). Not tied to a segment: generic region segments, symbol
/// dictionaries, pattern dictionaries and halftone gray-scale planes call it with their own decoder, contexts and parameters.
/// </summary>
/// <remarks>
/// <para>
/// Contexts gather the template pixels in reading order (rows top to bottom, left to right), which §6.2.5.7 allows. An AT pixel keeps
/// the context bit of its nominal location wherever it is moved, so the SLTP contexts of Figures 8 to 11 (0x9B25, 0x0795, 0x00E5 and
/// 0x0195 in this order) hold whatever the AT locations (§6.2.5.7 step 3 b, note). The context of the next pixel is rolled from the
/// previous one: two new pixels from the rows above per step, nominal AT pixels included; moved AT pixels override their bit.
/// </para>
/// <para>Allocates nothing: the destination, the contexts and the skip bitmap belong to the caller.</para>
/// </remarks>
internal static class Jbig2GenericRegion
{
    /// <summary>The number of GB contexts every template fits in (16 template pixels).</summary>
    public const int ContextCount = 65536;

    /// <summary>
    /// Decodes <paramref name="bitmap"/> (§6.2.5.7) with template-based arithmetic coding, continuing <paramref name="decoder"/> and
    /// <paramref name="contexts"/> (which the caller resets when a segment begins, §7.4.6.4 step 2 and E.3.7).
    /// </summary>
    /// <param name="decoder">The MQ decoder positioned at the region's data.</param>
    /// <param name="contexts">The GB contexts, at least <see cref="ContextCount"/> bytes.</param>
    /// <param name="parameters">GBTEMPLATE, TPGDON, USESKIP and the AT locations.</param>
    /// <param name="bitmap">The destination GBREG, all 0 on entry, its size GBW x GBH.</param>
    /// <param name="skip">The SKIP bitmap, of the same size, when <see cref="Jbig2GenericParameters.UseSkip"/>.</param>
    public static void Decode(ref MqDecoder decoder, Span<byte> contexts, in Jbig2GenericParameters parameters, Jbig2Bitmap bitmap, Jbig2Bitmap skip)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        int stride = bitmap.Stride;
        Span<byte> data = bitmap.Data;
        (int mask, int dx2, int bit2, int dx1, int bit1, int sltp) = parameters.Template switch
        {
            0 => (0x7BF7, 3, 11, 4, 4, 0x9B25),
            1 => (0x0EFB, 3, 9, 4, 3, 0x0795),
            2 => (0x01BD, 2, 7, 3, 2, 0x00E5),
            _ => (0x01F7, 0, 0, 3, 4, 0x0195),
        };

        // Moved AT pixels: (x offset, y offset, context bit) for those not at their nominal location.
        Span<int> atX = stackalloc int[4];
        Span<int> atY = stackalloc int[4];
        Span<int> atBit = stackalloc int[4];
        Span<int> atRow = stackalloc int[4];
        int moved = 0;
        switch (parameters.Template)
        {
            case 0:
                Add(parameters.AtX1, parameters.AtY1, 3, -1, 4, ref moved, atX, atY, atBit);
                Add(parameters.AtX2, parameters.AtY2, -3, -1, 10, ref moved, atX, atY, atBit);
                Add(parameters.AtX3, parameters.AtY3, 2, -2, 11, ref moved, atX, atY, atBit);
                Add(parameters.AtX4, parameters.AtY4, -2, -2, 15, ref moved, atX, atY, atBit);
                break;
            case 1:
                Add(parameters.AtX1, parameters.AtY1, 3, -1, 3, ref moved, atX, atY, atBit);
                break;
            case 2:
                Add(parameters.AtX1, parameters.AtY1, 2, -1, 2, ref moved, atX, atY, atBit);
                break;
            default:
                Add(parameters.AtX1, parameters.AtY1, 2, -1, 4, ref moved, atX, atY, atBit);
                break;
        }

        bool useSkip = parameters.UseSkip && !skip.IsEmpty;
        int ltp = 0;
        for (int y = 0; y < height; y++)
        {
            Span<byte> row = data.Slice(y * stride, stride);
            if (parameters.TypicalPrediction)
            {
                ltp ^= decoder.Decode(ref contexts[sltp]);
                if (ltp != 0)
                {
                    if (y > 0)
                    {
                        data.Slice((y - 1) * stride, stride).CopyTo(row);
                    }

                    continue;
                }
            }

            ReadOnlySpan<byte> row1 = y >= 1 ? data.Slice((y - 1) * stride, stride) : default;
            ReadOnlySpan<byte> row2 = y >= 2 && dx2 != 0 ? data.Slice((y - 2) * stride, stride) : default;
            for (int i = 0; i < moved; i++)
            {
                int atY0 = y + atY[i];
                atRow[i] = (uint)atY0 < (uint)height ? atY0 * stride : -1;
            }

            int context = 0;
            for (int px = 0; px < dx2; px++)
            {
                context |= Bit(row2, px, width) << (bit2 + dx2 - 1 - px);
            }

            for (int px = 0; px < dx1; px++)
            {
                context |= Bit(row1, px, width) << (bit1 + dx1 - 1 - px);
            }

            ReadOnlySpan<byte> skipRow = useSkip ? skip.Row(y) : default;
            for (int x = 0; x < width; x++)
            {
                int pixel;
                if (useSkip && ((skipRow[x >> 3] >> (~x & 7)) & 1) != 0)
                {
                    pixel = 0;
                }
                else
                {
                    int cx = context;
                    for (int i = 0; i < moved; i++)
                    {
                        int ax = x + atX[i];
                        int value = atRow[i] >= 0 && (uint)ax < (uint)width ? (data[atRow[i] + (ax >> 3)] >> (~ax & 7)) & 1 : 0;
                        cx = (cx & ~(1 << atBit[i])) | (value << atBit[i]);
                    }

                    pixel = decoder.Decode(ref contexts[cx]);
                    if (pixel != 0)
                    {
                        row[x >> 3] |= (byte)(0x80 >> (x & 7));
                    }
                }

                context = ((context & mask) << 1) | (Bit(row2, x + dx2, width) << bit2) | (Bit(row1, x + dx1, width) << bit1) | pixel;
            }
        }
    }

    /// <summary>Decodes <paramref name="bitmap"/> from MMR data (§6.2.6): T.6 two-dimensional coding, black = 1, whole bytes consumed.</summary>
    /// <param name="data">The MMR data; EOFB optional when the caller knows the byte count.</param>
    /// <param name="bitmap">The destination, all 0 on entry; rows the data does not reach stay 0.</param>
    /// <returns>How decoding ended, the rows written and the bytes consumed.</returns>
    public static MmrResult DecodeMmr(ReadOnlySpan<byte> data, Jbig2Bitmap bitmap) =>
        bitmap.IsEmpty ? new MmrResult(MmrStatus.Ok, 0, 0) : MmrDecoder.Decode(data, bitmap.Width, bitmap.Height, bitmap.Data, bitmap.Stride);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Bit(ReadOnlySpan<byte> row, int x, int width) =>
        (uint)x < (uint)width && !row.IsEmpty ? (row[x >> 3] >> (~x & 7)) & 1 : 0;

    private static void Add(int x, int y, int nominalX, int nominalY, int bit, ref int moved, Span<int> atX, Span<int> atY, Span<int> atBit)
    {
        if (x == nominalX && y == nominalY)
        {
            return;
        }

        atX[moved] = x;
        atY[moved] = y;
        atBit[moved] = bit;
        moved++;
    }
}
