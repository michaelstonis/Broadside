namespace Broadside.Filters.Dct;

/// <summary>
/// Decodes the blocks of arithmetic-coded scans into quantized coefficients: sequential blocks and the four kinds of progressive
/// scan, with the adaptive statistics of the scan's conditioning tables.
/// </summary>
/// <remarks>
/// ITU-T T.81 §F.1.4.4 and §F.2.4 (sequential arithmetic decoding: Figures F.18 to F.24, Tables F.4 and F.5), §G.1.3 (progressive
/// arithmetic decoding: Figures G.10 and G.11, Table G.2); the QM decoder of Annex D is <see cref="ArithmeticDecoder"/>. Each
/// decoder returns <see langword="false"/> on an impossible value; the caller reports it and abandons the restart interval.
/// <para>ISO 32000-2 §7.4.8.</para>
/// </remarks>
internal static class JpegArithmeticDecoding
{
    /// <summary>Decodes one block of a sequential arithmetic-coded scan.</summary>
    /// <remarks>ITU-T T.81 §F.2.4 (Figure F.18 decode_DC, Figure F.20 decode_AC).</remarks>
    /// <param name="decoder">The scan's arithmetic decoder.</param>
    /// <param name="scan">The scan: its band, point transform and statistics.</param>
    /// <param name="tables">The conditioning tables.</param>
    /// <param name="component">The block's component: its conditioning tables, DC predictor and context.</param>
    /// <param name="block">The block's coefficients, natural order.</param>
    /// <returns><see langword="false"/> on an impossible value.</returns>
    public static bool DecodeSequential(ref ArithmeticDecoder decoder, JpegScan scan, JpegTables tables, JpegComponent component, Span<short> block)
    {
        if (!DecodeDc(ref decoder, scan, tables, component))
        {
            return false;
        }

        block[0] = (short)component.Predictor;
        return DecodeAc(ref decoder, scan, tables, component, block, 1, 0);
    }

    /// <summary>Decodes the DC coefficient of a block in a first progressive arithmetic DC scan, shifted left by Al.</summary>
    /// <remarks>ITU-T T.81 §G.1.3.1 (coded as the sequential DC difference, §F.1.4.1).</remarks>
    /// <param name="decoder">The scan's arithmetic decoder.</param>
    /// <param name="scan">The scan: its band, point transform and statistics.</param>
    /// <param name="tables">The conditioning tables.</param>
    /// <param name="component">The block's component: its conditioning tables, DC predictor and context.</param>
    /// <param name="block">The block's coefficients, natural order.</param>
    /// <returns><see langword="false"/> on an impossible value.</returns>
    public static bool DecodeDcFirst(ref ArithmeticDecoder decoder, JpegScan scan, JpegTables tables, JpegComponent component, Span<short> block)
    {
        if (!DecodeDc(ref decoder, scan, tables, component))
        {
            return false;
        }

        block[0] = (short)(component.Predictor << scan.ApproximationLow);
        return true;
    }

    /// <summary>Decodes one more bit of a block's DC coefficient in an arithmetic DC refinement scan (fixed probability).</summary>
    /// <remarks>ITU-T T.81 §G.1.3.1 (the correction bit is coded with a fixed Qe of X'5A1D', no adaptation).</remarks>
    /// <param name="decoder">The scan's arithmetic decoder.</param>
    /// <param name="scan">The scan: its band, point transform and statistics.</param>
    /// <param name="block">The block's coefficients, natural order.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public static bool DecodeDcRefine(ref ArithmeticDecoder decoder, JpegScan scan, Span<short> block)
    {
        if (decoder.Decode(ref scan.FixedBin) != 0)
        {
            block[0] |= (short)(1 << scan.ApproximationLow);
        }

        return true;
    }

    /// <summary>Decodes the band of a block in a first progressive arithmetic AC scan.</summary>
    /// <remarks>ITU-T T.81 §G.1.3.2 (as §F.1.4.4.2 over Ss..Se, values point-transformed by Al).</remarks>
    /// <param name="decoder">The scan's arithmetic decoder.</param>
    /// <param name="scan">The scan: its band, point transform and statistics.</param>
    /// <param name="tables">The conditioning tables.</param>
    /// <param name="component">The block's component: its conditioning tables, DC predictor and context.</param>
    /// <param name="block">The block's coefficients, natural order.</param>
    /// <returns><see langword="false"/> on an impossible value.</returns>
    public static bool DecodeAcFirst(ref ArithmeticDecoder decoder, JpegScan scan, JpegTables tables, JpegComponent component, Span<short> block) =>
        DecodeAc(ref decoder, scan, tables, component, block, scan.SpectralStart, scan.ApproximationLow);

    /// <summary>
    /// Decodes one more bit of the band of a block in an arithmetic AC refinement scan: past the previous end of block (EOBx) an
    /// end-of-block decision, then per coefficient a correction bit (already non-zero) or a new-coefficient decision and its sign.
    /// </summary>
    /// <remarks>ITU-T T.81 §G.1.3.3 (Figures G.10 and G.11 reversed; Table G.2 statistics bins).</remarks>
    /// <param name="decoder">The scan's arithmetic decoder.</param>
    /// <param name="scan">The scan: its band, point transform and statistics.</param>
    /// <param name="component">The block's component: its conditioning tables, DC predictor and context.</param>
    /// <param name="block">The block's coefficients, natural order.</param>
    /// <returns><see langword="false"/> on an impossible value.</returns>
    public static bool DecodeAcRefine(ref ArithmeticDecoder decoder, JpegScan scan, JpegComponent component, Span<short> block)
    {
        Span<byte> bins = scan.AcStatistics.AsSpan(component.AcConditioning * JpegScan.AcStatisticsBins, JpegScan.AcStatisticsBins);
        byte[] natural = JpegMarkers.NaturalOrder;
        int end = scan.SpectralEnd;
        int plusOne = 1 << scan.ApproximationLow;
        int minusOne = -1 << scan.ApproximationLow;
        int previousEnd = end;
        while (previousEnd > 0 && block[natural[previousEnd]] == 0)
        {
            previousEnd--;
        }

        for (int k = scan.SpectralStart; k <= end; k++)
        {
            int s = 3 * (k - 1);
            if (k > previousEnd && decoder.Decode(ref bins[s]) != 0)
            {
                break; // end of block
            }

            while (true)
            {
                int z = natural[k];
                if (block[z] != 0)
                {
                    if (decoder.Decode(ref bins[s + 2]) != 0)
                    {
                        block[z] += (short)(block[z] < 0 ? minusOne : plusOne);
                    }

                    break;
                }

                if (decoder.Decode(ref bins[s + 1]) != 0)
                {
                    block[z] = (short)(decoder.Decode(ref scan.FixedBin) != 0 ? minusOne : plusOne);
                    break;
                }

                s += 3;
                if (++k > end)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Decodes a DC difference with the arithmetic coder and adds it to the predictor (modulo 2^16), updating the DC context.
    /// Returns <see langword="false"/> when the magnitude category overflows.
    /// </summary>
    /// <remarks>
    /// ITU-T T.81 §F.2.4.1 (Figure F.19 Decode_DC_DIFF; Figures F.21 to F.24: zero, sign, magnitude category, magnitude bits) and
    /// §F.1.4.4.1, Table F.4 (statistics bins S0 at the DC context, SS, SP, SN, X1 = 20, M = X + 14); §F.1.4.4.1.2 (the context
    /// from the conditioning bounds L and U).
    /// </remarks>
    /// <param name="decoder">The scan's arithmetic decoder.</param>
    /// <param name="scan">The scan: its band, point transform and statistics.</param>
    /// <param name="tables">The conditioning tables.</param>
    /// <param name="component">The block's component: its conditioning tables, DC predictor and context.</param>
    /// <returns><see langword="false"/> on an impossible value.</returns>
    private static bool DecodeDc(ref ArithmeticDecoder decoder, JpegScan scan, JpegTables tables, JpegComponent component)
    {
        int table = component.DcConditioning;
        Span<byte> bins = scan.DcStatistics.AsSpan(table * JpegScan.DcStatisticsBins, JpegScan.DcStatisticsBins);
        int s = component.DcContext;
        if (decoder.Decode(ref bins[s]) == 0)
        {
            component.DcContext = 0;
            return true;
        }

        int sign = decoder.Decode(ref bins[s + 1]);
        s += 2 + sign;
        int m = decoder.Decode(ref bins[s]);
        if (m != 0)
        {
            s = 20;
            while (decoder.Decode(ref bins[s]) != 0)
            {
                if ((m <<= 1) == 0x8000)
                {
                    return false;
                }

                s++;
            }
        }

        component.DcContext = m < (1 << tables.DcLower[table]) >> 1 ? 0
            : m > (1 << tables.DcUpper[table]) >> 1 ? 12 + (sign * 4)
            : 4 + (sign * 4);
        int v = m;
        s += 14;
        while ((m >>= 1) != 0)
        {
            if (decoder.Decode(ref bins[s]) != 0)
            {
                v |= m;
            }
        }

        v++;
        component.Predictor = (component.Predictor + (sign != 0 ? -v : v)) & 0xFFFF;
        return true;
    }

    /// <summary>
    /// Decodes the AC coefficients Ss..Se of a block with the arithmetic coder: end-of-block decisions, zero runs, sign and
    /// magnitude, each value shifted left by <paramref name="shift"/>. Returns <see langword="false"/> on an overflow.
    /// </summary>
    /// <remarks>
    /// ITU-T T.81 §F.2.4.2 (Figure F.20 Decode_AC_coefficients) and §F.1.4.4.2, Table F.5 (SE = 3 (K - 1), S0 = SE + 1, SN = S0 + 1,
    /// X1 = SN; X2 = 189 below Kx and 217 above; M = X + 14; the sign at a fixed probability of one half); §G.1.3.2 for the band
    /// of a progressive first scan.
    /// </remarks>
    /// <param name="decoder">The scan's arithmetic decoder.</param>
    /// <param name="scan">The scan: its band, point transform and statistics.</param>
    /// <param name="tables">The conditioning tables.</param>
    /// <param name="component">The block's component: its conditioning tables, DC predictor and context.</param>
    /// <param name="block">The block's coefficients, natural order.</param>
    /// <param name="start">The first coefficient: 1, or Ss.</param>
    /// <param name="shift">The point transform: 0, or Al.</param>
    /// <returns><see langword="false"/> on an impossible value.</returns>
    private static bool DecodeAc(ref ArithmeticDecoder decoder, JpegScan scan, JpegTables tables, JpegComponent component, Span<short> block, int start, int shift)
    {
        int table = component.AcConditioning;
        Span<byte> bins = scan.AcStatistics.AsSpan(table * JpegScan.AcStatisticsBins, JpegScan.AcStatisticsBins);
        byte[] natural = JpegMarkers.NaturalOrder;
        int end = scan.SpectralEnd;
        int split = tables.AcSplit[table];
        for (int k = start; k <= end; k++)
        {
            int s = 3 * (k - 1);
            if (decoder.Decode(ref bins[s]) != 0)
            {
                break; // end of block
            }

            while (decoder.Decode(ref bins[s + 1]) == 0)
            {
                s += 3;
                if (++k > end)
                {
                    return false;
                }
            }

            int sign = decoder.Decode(ref scan.FixedBin);
            s += 2;
            int m = decoder.Decode(ref bins[s]);
            if (m != 0 && decoder.Decode(ref bins[s]) != 0)
            {
                m <<= 1;
                s = k <= split ? 189 : 217;
                while (decoder.Decode(ref bins[s]) != 0)
                {
                    if ((m <<= 1) == 0x8000)
                    {
                        return false;
                    }

                    s++;
                }
            }

            int v = m;
            s += 14;
            while ((m >>= 1) != 0)
            {
                if (decoder.Decode(ref bins[s]) != 0)
                {
                    v |= m;
                }
            }

            v++;
            block[natural[k]] = (short)((sign != 0 ? -v : v) << shift);
        }

        return true;
    }
}
