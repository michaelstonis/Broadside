using System.Runtime.CompilerServices;
using Broadside.Filters.Codecs;

namespace Broadside.Filters.Jbig2;

/// <summary>
/// The parameters of the generic refinement region decoding procedure (ITU-T T.88 §6.3.2, Table 6) other than the size, which is the
/// destination bitmap's, and the reference bitmap.
/// </summary>
internal readonly struct Jbig2RefinementParameters
{
    /// <summary>Gets the template, 0 (13 pixels) or 1 (10 pixels) (GRTEMPLATE).</summary>
    public int Template { get; init; }

    /// <summary>Gets a value indicating whether typical prediction is on (TPGRON).</summary>
    public bool TypicalPrediction { get; init; }

    /// <summary>Gets GRREFERENCEDX: the reference pixel for (x, y) is GRREFERENCE[x - DX, y - DY].</summary>
    public long ReferenceDx { get; init; }

    /// <summary>Gets GRREFERENCEDY.</summary>
    public long ReferenceDy { get; init; }

    /// <summary>Gets GRATX1 (template 0 only), relative to the pixel being decoded.</summary>
    public int AtX1 { get; init; }

    /// <summary>Gets GRATY1.</summary>
    public int AtY1 { get; init; }

    /// <summary>Gets GRATX2 (template 0 only), relative to the reference pixel.</summary>
    public int AtX2 { get; init; }

    /// <summary>Gets GRATY2.</summary>
    public int AtY2 { get; init; }

    /// <summary>The parameters of <paramref name="template"/> with the AT pixels at their nominal locations (-1, -1).</summary>
    public static Jbig2RefinementParameters Nominal(int template) => new() { Template = template, AtX1 = -1, AtY1 = -1, AtX2 = -1, AtY2 = -1 };
}

/// <summary>
/// The generic refinement region decoding procedure (ITU-T T.88 §6.3): a bitmap coded with template-based arithmetic coding against a
/// reference bitmap, with the two templates of Figures 12 and 13, the AT pixels RA1 and RA2, and typical prediction (TPGR, §6.3.5.5).
/// Used by refinement region segments, refined text region symbol instances and refinement/aggregate symbol dictionaries, each with
/// its own decoder and the GR contexts their segment shares.
/// </summary>
/// <remarks>
/// <para>
/// Contexts gather, from the most significant bit: the refined bitmap's pixels (template 0: RA1, (0, -1), (1, -1), (-1, 0); template 1:
/// (-1, -1), (0, -1), (1, -1), (-1, 0)), then the reference pixels around (x - DX, y - DY) (template 0: RA2, (0, -1), (1, -1), (-1, 0),
/// (0, 0), (1, 0), (-1, 1), (0, 1), (1, 1); template 1: (0, -1), (-1, 0), (0, 0), (1, 0), (0, 1), (1, 1)). The SLTP context of
/// Figures 14 and 15 is the reference centre alone: 0x0010 and 0x0008 in this order. Pixels outside either bitmap are 0 (§6.3.5.2).
/// </para>
/// <para>Allocates nothing.</para>
/// </remarks>
internal static class Jbig2RefinementRegion
{
    /// <summary>The number of GR contexts (13 template pixels).</summary>
    public const int ContextCount = 8192;

    /// <summary>Decodes <paramref name="bitmap"/> (§6.3.5.6) continuing <paramref name="decoder"/> and <paramref name="contexts"/>.</summary>
    /// <param name="decoder">The MQ decoder positioned at the refinement data.</param>
    /// <param name="contexts">The GR contexts, at least <see cref="ContextCount"/> bytes.</param>
    /// <param name="parameters">GRTEMPLATE, TPGRON, the reference offsets and the AT locations.</param>
    /// <param name="reference">GRREFERENCE.</param>
    /// <param name="bitmap">The destination GRREG, all 0 on entry, its size GRW x GRH.</param>
    public static void Decode(ref MqDecoder decoder, Span<byte> contexts, in Jbig2RefinementParameters parameters, Jbig2Bitmap reference, Jbig2Bitmap bitmap)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        long dx = parameters.ReferenceDx;
        long dy = parameters.ReferenceDy;
        bool template0 = parameters.Template == 0;
        int sltp = template0 ? 0x0010 : 0x0008;
        int ltp = 0;
        for (int y = 0; y < height; y++)
        {
            if (parameters.TypicalPrediction)
            {
                ltp ^= decoder.Decode(ref contexts[sltp]);
            }

            Span<byte> row = bitmap.Row(y);
            long ry = y - dy;
            for (int x = 0; x < width; x++)
            {
                long rx = x - dx;
                if (ltp != 0)
                {
                    int block = Block(reference, rx, ry);
                    if (block >= 0)
                    {
                        if (block != 0)
                        {
                            row[x >> 3] |= (byte)(0x80 >> (x & 7));
                        }

                        continue;
                    }
                }

                int context;
                if (template0)
                {
                    context = (Pixel(bitmap, x + parameters.AtX1, y + (long)parameters.AtY1) << 12)
                        | (Pixel(bitmap, x, y - 1L) << 11)
                        | (Pixel(bitmap, x + 1L, y - 1L) << 10)
                        | (Pixel(bitmap, x - 1L, y) << 9)
                        | (Pixel(reference, rx + parameters.AtX2, ry + parameters.AtY2) << 8)
                        | (Pixel(reference, rx, ry - 1) << 7)
                        | (Pixel(reference, rx + 1, ry - 1) << 6)
                        | (Pixel(reference, rx - 1, ry) << 5)
                        | (Pixel(reference, rx, ry) << 4)
                        | (Pixel(reference, rx + 1, ry) << 3)
                        | (Pixel(reference, rx - 1, ry + 1) << 2)
                        | (Pixel(reference, rx, ry + 1) << 1)
                        | Pixel(reference, rx + 1, ry + 1);
                }
                else
                {
                    context = (Pixel(bitmap, x - 1L, y - 1L) << 9)
                        | (Pixel(bitmap, x, y - 1L) << 8)
                        | (Pixel(bitmap, x + 1L, y - 1L) << 7)
                        | (Pixel(bitmap, x - 1L, y) << 6)
                        | (Pixel(reference, rx, ry - 1) << 5)
                        | (Pixel(reference, rx - 1, ry) << 4)
                        | (Pixel(reference, rx, ry) << 3)
                        | (Pixel(reference, rx + 1, ry) << 2)
                        | (Pixel(reference, rx, ry + 1) << 1)
                        | Pixel(reference, rx + 1, ry + 1);
                }

                if (decoder.Decode(ref contexts[context]) != 0)
                {
                    row[x >> 3] |= (byte)(0x80 >> (x & 7));
                }
            }
        }
    }

    /// <summary>The value of the 3 x 3 reference block centred at (x, y) when all nine pixels agree (Figure 16), else -1.</summary>
    private static int Block(Jbig2Bitmap reference, long x, long y)
    {
        int first = Pixel(reference, x - 1, y - 1);
        for (long j = y - 1; j <= y + 1; j++)
        {
            for (long i = x - 1; i <= x + 1; i++)
            {
                if (Pixel(reference, i, j) != first)
                {
                    return -1;
                }
            }
        }

        return first;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Pixel(Jbig2Bitmap bitmap, long x, long y) =>
        (ulong)x < (ulong)bitmap.Width && (ulong)y < (ulong)bitmap.Height ? (bitmap.Data[((int)y * bitmap.Stride) + ((int)x >> 3)] >> (~(int)x & 7)) & 1 : 0;
}
