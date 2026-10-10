using System.Buffers;
using Broadside.Filters.Codecs;

namespace Broadside.Filters.Jbig2;

/// <summary>The parameters of the text region decoding procedure (ITU-T T.88 §6.4.2, Table 9) other than the region size.</summary>
internal readonly struct Jbig2TextParameters
{
    /// <summary>Gets a value indicating whether symbol instances may be refined (SBREFINE).</summary>
    public bool Refine { get; init; }

    /// <summary>Gets SBNUMINSTANCES.</summary>
    public long Instances { get; init; }

    /// <summary>Gets log2 SBSTRIPS (LOGSBSTRIPS).</summary>
    public int LogStrips { get; init; }

    /// <summary>Gets SBSYMS: the first <see cref="SymbolCount"/> entries are the symbols.</summary>
    public IReadOnlyList<Jbig2Image> Symbols { get; init; }

    /// <summary>Gets SBNUMSYMS.</summary>
    public int SymbolCount { get; init; }

    /// <summary>Gets SBSYMCODELEN: the IAID length (arithmetic), or the fixed code length when <see cref="SymbolCodes"/> is null (Huffman).</summary>
    public int SymbolCodeLength { get; init; }

    /// <summary>Gets SBSYMCODES (Huffman); null for fixed-length codes of <see cref="SymbolCodeLength"/> bits (§6.5.8.2.3).</summary>
    public Jbig2HuffmanTable? SymbolCodes { get; init; }

    /// <summary>Gets SBDEFPIXEL.</summary>
    public int DefaultPixel { get; init; }

    /// <summary>Gets SBCOMBOP.</summary>
    public Jbig2CombinationOperator CombinationOperator { get; init; }

    /// <summary>Gets a value indicating whether the S axis is the Y axis (TRANSPOSED).</summary>
    public bool Transposed { get; init; }

    /// <summary>Gets REFCORNER: 0 BOTTOMLEFT, 1 TOPLEFT, 2 BOTTOMRIGHT, 3 TOPRIGHT.</summary>
    public int ReferenceCorner { get; init; }

    /// <summary>Gets SBDSOFFSET.</summary>
    public int DsOffset { get; init; }

    /// <summary>Gets SBHUFFFS.</summary>
    public Jbig2HuffmanTable? FirstS { get; init; }

    /// <summary>Gets SBHUFFDS.</summary>
    public Jbig2HuffmanTable? DeltaS { get; init; }

    /// <summary>Gets SBHUFFDT.</summary>
    public Jbig2HuffmanTable? DeltaT { get; init; }

    /// <summary>Gets SBHUFFRDW.</summary>
    public Jbig2HuffmanTable? RefinementDeltaWidth { get; init; }

    /// <summary>Gets SBHUFFRDH.</summary>
    public Jbig2HuffmanTable? RefinementDeltaHeight { get; init; }

    /// <summary>Gets SBHUFFRDX.</summary>
    public Jbig2HuffmanTable? RefinementX { get; init; }

    /// <summary>Gets SBHUFFRDY.</summary>
    public Jbig2HuffmanTable? RefinementY { get; init; }

    /// <summary>Gets SBHUFFRSIZE.</summary>
    public Jbig2HuffmanTable? RefinementSize { get; init; }

    /// <summary>Gets SBRTEMPLATE and SBRATX1 to SBRATY2 (the reference offsets are set per instance).</summary>
    public Jbig2RefinementParameters Refinement { get; init; }
}

/// <summary>How a text region decode ended.</summary>
internal enum Jbig2TextResult
{
    /// <summary>Every instance was decoded.</summary>
    Complete,

    /// <summary>The data ran out or became invalid before the last instance.</summary>
    Truncated,

    /// <summary>A symbol instance's ID was out of range (the instance was skipped).</summary>
    SymbolIdOutOfRange,

    /// <summary>A refined instance was larger than the limits allow (decoding stopped).</summary>
    LimitExceeded,
}

/// <summary>
/// The text region decoding procedure (ITU-T T.88 §6.4): strips of symbol instances placed by reference corner, optionally transposed,
/// optionally refined, drawn into SBREG with SBCOMBOP. Called by text region segments and by refinement/aggregate symbol dictionaries
/// (Table 17), each with its own coder. Allocates nothing per instance (refined instances use a pooled buffer).
/// </summary>
/// <remarks>
/// ITU-T T.88 §6.4.5 (decoding steps; CURS moves by W - 1 or H - 1 before or after the placement as the corner and TRANSPOSED say),
/// §6.4.6-6.4.10 (the fields), §6.4.11 and Table 12 (refinement with GRREFERENCEDX = floor(RDW / 2) + RDX, GRREFERENCEDY = floor(RDH /
/// 2) + RDY; in Huffman coding each refinement is its own MQ stream of RSIZE bytes, the GR contexts continuing across them).
/// </remarks>
internal static class Jbig2TextRegion
{
    /// <summary>Decodes the region into <paramref name="region"/>, which the caller filled with SBDEFPIXEL.</summary>
    /// <param name="coder">The segment's coder.</param>
    /// <param name="parameters">The procedure's parameters.</param>
    /// <param name="region">SBREG.</param>
    /// <param name="maxPixels">The largest refined instance bitmap allowed.</param>
    /// <param name="work">The budget of all instances: each costs its area plus 64, so damaged data cannot draw billions of them.</param>
    /// <returns>How decoding ended (the first problem met).</returns>
    public static Jbig2TextResult Decode(ref Jbig2Coder coder, in Jbig2TextParameters parameters, Jbig2Bitmap region, long maxPixels, long work)
    {
        Jbig2TextResult result = Jbig2TextResult.Complete;
        int strips = 1 << parameters.LogStrips;
        bool right = (parameters.ReferenceCorner & 2) != 0;
        bool top = (parameters.ReferenceCorner & 1) != 0;
        bool transposed = parameters.Transposed;
        long stripT = -coder.Decode(Jbig2IntegerProcedure.Iadt, parameters.DeltaT) * strips;

        long firstS = 0;
        long instances = 0;
        while (instances < parameters.Instances)
        {
            if (coder.IsExhausted)
            {
                return Jbig2TextResult.Truncated;
            }

            stripT += coder.Decode(Jbig2IntegerProcedure.Iadt, parameters.DeltaT) * strips;
            long curS = 0;
            bool first = true;
            while (true)
            {
                if (first)
                {
                    firstS += coder.Decode(Jbig2IntegerProcedure.Iafs, parameters.FirstS);
                    curS = firstS;
                    first = false;
                }
                else
                {
                    // §6.4.5 step 3 c ii: a strip ends with OOB, also after the region's last instance.
                    if (!coder.TryDecode(Jbig2IntegerProcedure.Iads, parameters.DeltaS, out long ds))
                    {
                        break;
                    }

                    if (instances >= parameters.Instances)
                    {
                        return Jbig2TextResult.Truncated;
                    }

                    curS += ds + parameters.DsOffset;
                }

                if (coder.IsExhausted)
                {
                    return Jbig2TextResult.Truncated;
                }

                long curT = strips == 1 ? 0
                    : coder.Huffman ? coder.Bits.ReadBits(parameters.LogStrips)
                    : coder.Decode(Jbig2IntegerProcedure.Iait, null);
                long t = stripT + curT;
                int id = DecodeSymbolId(ref coder, parameters);
                Jbig2Image symbol;
                if ((uint)id < (uint)parameters.SymbolCount)
                {
                    symbol = parameters.Symbols[id];
                }
                else
                {
                    symbol = Jbig2Image.Empty;
                    result = result == Jbig2TextResult.Complete ? Jbig2TextResult.SymbolIdOutOfRange : result;
                }

                int ri = !parameters.Refine ? 0 : coder.Huffman ? coder.Bits.ReadBit() : (int)coder.Decode(Jbig2IntegerProcedure.Iari, null);
                byte[]? rented = null;
                try
                {
                    Jbig2Bitmap bitmap = symbol.View;
                    int width = symbol.Width;
                    int height = symbol.Height;
                    if (ri != 0)
                    {
                        if (!Refine(ref coder, parameters, symbol, maxPixels, out rented, out width, out height))
                        {
                            return coder.Invalid ? Jbig2TextResult.Truncated : Jbig2TextResult.LimitExceeded;
                        }

                        bitmap = rented is null ? default : new Jbig2Bitmap(rented, width, height, Jbig2Bitmap.StrideOf(width));
                    }

                    work -= ((long)width * height) + 64;
                    if (work < 0)
                    {
                        return Jbig2TextResult.LimitExceeded;
                    }

                    if (!transposed && right)
                    {
                        curS += width - 1;
                    }
                    else if (transposed && !top)
                    {
                        curS += height - 1;
                    }

                    long s = curS;
                    long x = !transposed ? s - (right ? width - 1 : 0) : t - (right ? width - 1 : 0);
                    long y = !transposed ? t - (top ? 0 : height - 1) : s - (top ? 0 : height - 1);
                    if (!bitmap.IsEmpty)
                    {
                        region.Compose(bitmap, x, y, parameters.CombinationOperator);
                    }

                    if (!transposed && !right)
                    {
                        curS += width - 1;
                    }
                    else if (transposed && top)
                    {
                        curS += height - 1;
                    }
                }
                finally
                {
                    if (rented is not null)
                    {
                        ArrayPool<byte>.Shared.Return(rented);
                    }
                }

                instances++;
            }
        }

        return coder.Invalid ? Jbig2TextResult.Truncated : result;
    }

    private static int DecodeSymbolId(ref Jbig2Coder coder, in Jbig2TextParameters parameters)
    {
        if (!coder.Huffman)
        {
            return Jbig2IntegerDecoder.DecodeId(ref coder.Mq, coder.Statistics.Id, parameters.SymbolCodeLength);
        }

        if (parameters.SymbolCodes is null)
        {
            return (int)Math.Min(int.MaxValue, coder.Bits.ReadBits(parameters.SymbolCodeLength));
        }

        long id = parameters.SymbolCodes.Decode(ref coder.Bits);
        if (id is Jbig2HuffmanTable.Invalid or Jbig2HuffmanTable.Oob)
        {
            coder.Invalid = true;
            return -1;
        }

        return (int)id;
    }

    /// <summary>§6.4.11 steps 1-7: the refined instance bitmap, in a pooled buffer the caller returns.</summary>
    private static bool Refine(ref Jbig2Coder coder, in Jbig2TextParameters parameters, Jbig2Image symbol, long maxPixels, out byte[]? rented, out int refinedWidth, out int refinedHeight)
    {
        rented = null;
        refinedWidth = 0;
        refinedHeight = 0;
        long rdw = coder.Decode(Jbig2IntegerProcedure.Iardw, parameters.RefinementDeltaWidth);
        long rdh = coder.Decode(Jbig2IntegerProcedure.Iardh, parameters.RefinementDeltaHeight);
        long rdx = coder.Decode(Jbig2IntegerProcedure.Iardx, parameters.RefinementX);
        long rdy = coder.Decode(Jbig2IntegerProcedure.Iardy, parameters.RefinementY);
        long size = 0;
        if (coder.Huffman)
        {
            size = coder.Decode(Jbig2IntegerProcedure.Iari, parameters.RefinementSize);
            coder.Bits.Align();
        }

        if (coder.Invalid)
        {
            return false;
        }

        long width = symbol.Width + rdw;
        long height = symbol.Height + rdh;
        var refinement = parameters.Refinement with { ReferenceDx = (rdw >> 1) + rdx, ReferenceDy = (rdh >> 1) + rdy, TypicalPrediction = false };
        if (width <= 0 || height <= 0)
        {
            // An empty refined bitmap decodes no pixels: nothing is drawn and CURS moves by W - 1 or H - 1 of 0.
            if (coder.Huffman)
            {
                coder.Bits.SkipBytes(Math.Max(0, size));
            }

            return true;
        }

        if (width * height > maxPixels || width > int.MaxValue - 7 || height > int.MaxValue / Math.Max(1, Jbig2Bitmap.StrideOf((int)width)))
        {
            return false;
        }

        int stride = Jbig2Bitmap.StrideOf((int)width);
        int length = stride * (int)height;
        rented = ArrayPool<byte>.Shared.Rent(length);
        rented.AsSpan(0, length).Clear();
        var bitmap = new Jbig2Bitmap(rented, (int)width, (int)height, stride);
        refinedWidth = (int)width;
        refinedHeight = (int)height;
        if (coder.Huffman)
        {
            ReadOnlySpan<byte> remaining = coder.Bits.Remaining;
            var decoder = new MqDecoder(remaining[..(int)Math.Clamp(size, 0, remaining.Length)]);
            Jbig2RefinementRegion.Decode(ref decoder, coder.Statistics.Refinement, refinement, symbol.View, bitmap);
            coder.Bits.SkipBytes(Math.Max(0, size));
        }
        else
        {
            Jbig2RefinementRegion.Decode(ref coder.Mq, coder.Statistics.Refinement, refinement, symbol.View, bitmap);
        }

        return true;
    }
}
