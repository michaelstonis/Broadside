namespace Broadside.Filters.Dct;

/// <summary>
/// Decodes the blocks of Huffman-coded scans into quantized coefficients: sequential blocks, and the four kinds of progressive
/// scan (DC first and refinement, AC first and refinement, with end-of-band runs).
/// </summary>
/// <remarks>
/// ITU-T T.81 §F.2.2 (sequential Huffman decoding: Figures F.12 to F.16), §G.1.2 (progressive Huffman decoding: Table G.1 EOBn,
/// Figure G.7). The decoders only write coefficients; the caller reports invalid codes and handles restart intervals.
/// <para>ISO 32000-2 §7.4.8.</para>
/// </remarks>
internal static class JpegHuffmanDecoding
{
    /// <summary>Decodes one block of a sequential scan: the DC difference and the AC run/size codes up to EOB.</summary>
    /// <remarks>ITU-T T.81 §F.2.2.1 (DC), §F.2.2.2 (Figures F.13 and F.14: AC, ZRL 0xF0, EOB 0x00).</remarks>
    /// <param name="reader">The scan's entropy-coded data.</param>
    /// <param name="component">The block's component: its tables and DC predictor.</param>
    /// <param name="block">The block's coefficients, natural order.</param>
    /// <returns><see langword="false"/> when an invalid code was read (a zero is used in its place).</returns>
    public static bool DecodeSequential(ref JpegBitReader reader, JpegComponent component, Span<short> block)
    {
        bool valid = true;
        int category = reader.Decode(component.DcTable!, ref valid);
        component.Predictor += reader.ReceiveExtend(category);
        block[0] = (short)component.Predictor;
        HuffmanTable ac = component.AcTable!;
        byte[] natural = JpegMarkers.NaturalOrder;
        for (int k = 1; k < 64; k++)
        {
            int symbol = reader.Decode(ac, ref valid);
            int run = symbol >> 4;
            int size = symbol & 15;
            if (size != 0)
            {
                k += run;
                block[natural[k]] = (short)reader.ReceiveExtend(size);
            }
            else if (run == 15)
            {
                k += 15;
            }
            else
            {
                break;
            }
        }

        return valid;
    }

    /// <summary>Decodes the DC coefficient of a block in a first progressive DC scan: the difference, then the predictor shifted by Al.</summary>
    /// <remarks>ITU-T T.81 §G.1.2.1 (the DC first scan is coded as in §F.1.2.1, on values point-transformed by Al, §A.4).</remarks>
    /// <param name="reader">The scan's entropy-coded data.</param>
    /// <param name="scan">The scan: its point transform Al.</param>
    /// <param name="component">The block's component: its DC table and predictor.</param>
    /// <param name="block">The block's coefficients, natural order.</param>
    /// <returns><see langword="false"/> when an invalid code was read (a zero is used in its place).</returns>
    public static bool DecodeDcFirst(ref JpegBitReader reader, JpegScan scan, JpegComponent component, Span<short> block)
    {
        bool valid = true;
        int category = reader.Decode(component.DcTable!, ref valid);
        component.Predictor += reader.ReceiveExtend(category);
        block[0] = (short)(component.Predictor << scan.ApproximationLow);
        return valid;
    }

    /// <summary>Decodes one more bit of a block's DC coefficient in a progressive DC refinement scan.</summary>
    /// <remarks>ITU-T T.81 §G.1.2.1 (the correction bit is appended uncoded).</remarks>
    /// <param name="reader">The scan's entropy-coded data.</param>
    /// <param name="scan">The scan: its point transform Al.</param>
    /// <param name="block">The block's coefficients, natural order.</param>
    public static void DecodeDcRefine(ref JpegBitReader reader, JpegScan scan, Span<short> block)
    {
        if (reader.ReadBit() != 0)
        {
            block[0] |= (short)(1 << scan.ApproximationLow);
        }
    }

    /// <summary>Decodes the band Ss..Se of a block in a first progressive AC scan, with end-of-band runs.</summary>
    /// <remarks>ITU-T T.81 §G.1.2.2, Table G.1 (EOBn: a run of 2^n + n appended bits end-of-bands, this block included).</remarks>
    /// <param name="reader">The scan's entropy-coded data.</param>
    /// <param name="scan">The scan: its band, point transform and end-of-band run.</param>
    /// <param name="component">The block's component: its AC table.</param>
    /// <param name="block">The block's coefficients, natural order.</param>
    /// <returns><see langword="false"/> when an invalid code was read (a zero is used in its place).</returns>
    public static bool DecodeAcFirst(ref JpegBitReader reader, JpegScan scan, JpegComponent component, Span<short> block)
    {
        if (scan.EobRun > 0)
        {
            scan.EobRun--;
            return true;
        }

        bool valid = true;
        HuffmanTable ac = component.AcTable!;
        byte[] natural = JpegMarkers.NaturalOrder;
        int end = scan.SpectralEnd;
        int shift = scan.ApproximationLow;
        for (int k = scan.SpectralStart; k <= end; k++)
        {
            int symbol = reader.Decode(ac, ref valid);
            int run = symbol >> 4;
            int size = symbol & 15;
            if (size != 0)
            {
                k += run;
                block[natural[k]] = (short)(reader.ReceiveExtend(size) << shift);
            }
            else if (run == 15)
            {
                k += 15;
            }
            else
            {
                scan.EobRun = (1 << run) - 1 + reader.Receive(run);
                break;
            }
        }

        return valid;
    }

    /// <summary>
    /// Decodes one more bit of the band Ss..Se of a block in a progressive AC refinement scan: newly non-zero coefficients (size 1,
    /// sign bit) and a correction bit for each coefficient that was already non-zero, inside zero runs and after the end of band.
    /// </summary>
    /// <remarks>ITU-T T.81 §G.1.2.3 (Figure G.7, reversed as §G.2 says); libjpeg-turbo <c>decode_mcu_AC_refine</c> (src/jdphuff.c).</remarks>
    /// <param name="reader">The scan's entropy-coded data.</param>
    /// <param name="scan">The scan: its band, point transform and end-of-band run.</param>
    /// <param name="component">The block's component: its AC table.</param>
    /// <param name="block">The block's coefficients, natural order.</param>
    /// <returns><see langword="false"/> when an invalid code was read (a zero is used in its place).</returns>
    public static bool DecodeAcRefine(ref JpegBitReader reader, JpegScan scan, JpegComponent component, Span<short> block)
    {
        int end = scan.SpectralEnd;
        int plusOne = 1 << scan.ApproximationLow;
        int minusOne = -1 << scan.ApproximationLow;
        byte[] natural = JpegMarkers.NaturalOrder;
        int k = scan.SpectralStart;
        bool valid = true;
        if (scan.EobRun == 0)
        {
            HuffmanTable ac = component.AcTable!;
            for (; k <= end; k++)
            {
                int symbol = reader.Decode(ac, ref valid);
                int run = symbol >> 4;
                int size = symbol & 15;
                int value = 0;
                if (size != 0)
                {
                    // A newly non-zero coefficient always has size 1; its sign bit comes before any correction bit.
                    valid &= size == 1;
                    value = reader.ReadBit() != 0 ? plusOne : minusOne;
                }
                else if (run != 15)
                {
                    // EOBn: this block ends here and n more; the rest of this band gets correction bits below.
                    scan.EobRun = (1 << run) + reader.Receive(run);
                    break;
                }

                // Skip `run` zero-history coefficients (and, for ZRL, 16), giving a correction bit to each non-zero one.
                do
                {
                    int z = natural[k];
                    if (block[z] != 0)
                    {
                        if (reader.ReadBit() != 0 && (block[z] & plusOne) == 0)
                        {
                            block[z] += (short)(block[z] >= 0 ? plusOne : minusOne);
                        }
                    }
                    else if (--run < 0)
                    {
                        break;
                    }

                    k++;
                }
                while (k <= end);

                if (value != 0 && k <= end)
                {
                    block[natural[k]] = (short)value;
                }
            }
        }

        if (scan.EobRun > 0)
        {
            for (; k <= end; k++)
            {
                int z = natural[k];
                if (block[z] != 0 && reader.ReadBit() != 0 && (block[z] & plusOne) == 0)
                {
                    block[z] += (short)(block[z] >= 0 ? plusOne : minusOne);
                }
            }

            scan.EobRun--;
        }

        return valid;
    }
}
