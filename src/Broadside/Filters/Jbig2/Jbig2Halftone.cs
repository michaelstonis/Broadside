using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Filters.Ccitt;
using Broadside.Filters.Codecs;
using Broadside.Parsing;

namespace Broadside.Filters.Jbig2;

/// <summary>
/// A decoded pattern dictionary segment (ITU-T T.88 §7.4.4, type 16): GRAYMAX + 1 patterns of HDPW x HDPH, cut from one collective
/// bitmap (§6.7.5). Immutable.
/// </summary>
internal sealed class Jbig2PatternDictionary
{
    private Jbig2PatternDictionary(Jbig2Image[] patterns, int width, int height)
    {
        Patterns = patterns;
        Width = width;
        Height = height;
    }

    /// <summary>Gets the patterns HDPATS, HNUMPATS = GRAYMAX + 1 of them.</summary>
    public Jbig2Image[] Patterns { get; }

    /// <summary>Gets HDPW.</summary>
    public int Width { get; }

    /// <summary>Gets HDPH.</summary>
    public int Height { get; }

    /// <summary>Decodes a pattern dictionary segment (§7.4.4.2, §6.7.5).</summary>
    /// <param name="segment">The segment, for messages.</param>
    /// <param name="data">Its data part.</param>
    /// <param name="statistics">The decode's statistics (GB is reset, §7.4.4.2).</param>
    /// <param name="reporter">Where deviations go.</param>
    /// <param name="maxPixels">The most pixels the collective bitmap may hold.</param>
    /// <returns>The dictionary; null when it cannot be decoded.</returns>
    public static Jbig2PatternDictionary? Decode(in Jbig2Segment segment, ReadOnlySpan<byte> data, Jbig2Statistics statistics, Jbig2Reporter reporter, long maxPixels)
    {
        if (data.Length < 7)
        {
            Report(reporter, segment.Number, DiagnosticCodes.Jbig2SegmentInvalid, DiagnosticSeverity.Error, "ends inside its 7-byte data header (ITU-T T.88 §7.4.4.1); it is not decoded");
            return null;
        }

        bool mmr = (data[0] & 1) != 0;
        int template = (data[0] >> 1) & 3;
        int width = data[1];
        int height = data[2];
        long grayMax = BinaryPrimitives.ReadUInt32BigEndian(data[3..]);
        if (width == 0 || height == 0)
        {
            Report(reporter, segment.Number, DiagnosticCodes.Jbig2SegmentInvalid, DiagnosticSeverity.Error, "has a pattern width or height of 0 (ITU-T T.88 §7.4.4.1.2-3); it is not decoded");
            return null;
        }

        long totalWidth = (grayMax + 1) * width;
        if ((grayMax + 1) * ((width * height) + 64) > maxPixels || totalWidth > int.MaxValue - 7 || (long)Jbig2Bitmap.StrideOf((int)Math.Min(int.MaxValue - 7, totalWidth)) * height > Array.MaxLength)
        {
            Report(reporter, segment.Number, DiagnosticCodes.Jbig2LimitExceeded, DiagnosticSeverity.Error, "holds more pattern pixels than the image limits allow; it is not decoded");
            return null;
        }

        var collective = new Jbig2Image((int)totalWidth, height);
        ReadOnlySpan<byte> coded = data[7..];
        if (mmr)
        {
            MmrResult result = Jbig2GenericRegion.DecodeMmr(coded, collective.View);
            if (result.Status == MmrStatus.Invalid)
            {
                Report(reporter, segment.Number, DiagnosticCodes.Jbig2RegionDataInvalid, DiagnosticSeverity.Error, "has an invalid MMR code in its collective bitmap; the rows after it are 0");
            }
        }
        else
        {
            statistics.ResetGeneric();
            var decoder = new MqDecoder(coded);
            var parameters = Jbig2GenericParameters.Nominal(template) with { AtX1 = -width, AtY1 = 0 };
            Jbig2GenericRegion.Decode(ref decoder, statistics.Generic, parameters, collective.View, default);
        }

        var patterns = new Jbig2Image[grayMax + 1];
        for (int g = 0; g < patterns.Length; g++)
        {
            patterns[g] = Jbig2Image.CopyColumns(collective.View, g * width, width);
        }

        return new Jbig2PatternDictionary(patterns, width, height);
    }

    internal static void Report(Jbig2Reporter reporter, uint number, string code, DiagnosticSeverity severity, string what) => reporter.Report(
        code,
        severity,
        string.Create(CultureInfo.InvariantCulture, $"The JBIG2 pattern dictionary segment {number} {what}."));
}

/// <summary>The halftone region segment's data header fields (ITU-T T.88 §7.4.5.1) after its region information.</summary>
internal readonly struct Jbig2HalftoneParameters
{
    /// <summary>Gets a value indicating whether the gray-scale image is MMR coded (HMMR).</summary>
    public bool Mmr { get; init; }

    /// <summary>Gets HTEMPLATE.</summary>
    public int Template { get; init; }

    /// <summary>Gets a value indicating whether cells outside the region are skipped (HENABLESKIP).</summary>
    public bool EnableSkip { get; init; }

    /// <summary>Gets HCOMBOP.</summary>
    public Jbig2CombinationOperator CombinationOperator { get; init; }

    /// <summary>Gets HDEFPIXEL.</summary>
    public int DefaultPixel { get; init; }

    /// <summary>Gets HGW.</summary>
    public long GridWidth { get; init; }

    /// <summary>Gets HGH.</summary>
    public long GridHeight { get; init; }

    /// <summary>Gets HGX (x 256).</summary>
    public long GridX { get; init; }

    /// <summary>Gets HGY (x 256).</summary>
    public long GridY { get; init; }

    /// <summary>Gets HRX (x 256).</summary>
    public long VectorX { get; init; }

    /// <summary>Gets HRY (x 256).</summary>
    public long VectorY { get; init; }
}

/// <summary>
/// The halftone region decoding procedure (ITU-T T.88 §6.6) with the gray-scale image decoding procedure of Annex C: Gray-coded
/// bitplanes decoded most significant first with the generic region procedure (one decoder, one GB context set for all planes) or
/// MMR, then each grid cell's pattern drawn at <c>((HGX + m HRY + n HRX) &gt;&gt; 8, (HGY + m HRX - n HRY) &gt;&gt; 8)</c> with HCOMBOP.
/// </summary>
/// <remarks>Grid arithmetic is 64-bit with arithmetic shifts; gray values beyond the last pattern use the last one (reported).</remarks>
internal static class Jbig2HalftoneRegion
{
    /// <summary>Decodes HTREG into <paramref name="region"/>, which the caller filled with HDEFPIXEL.</summary>
    /// <param name="coded">The coded gray-scale image after the data header.</param>
    /// <param name="parameters">The data header.</param>
    /// <param name="patterns">The referred pattern dictionary.</param>
    /// <param name="statistics">The decode's statistics (GB is reset, §7.4.5.2).</param>
    /// <param name="region">HTREG.</param>
    /// <param name="report">Reports a deviation: code, severity, text.</param>
    /// <param name="maxPixels">The most grid cells allowed.</param>
    /// <returns><see langword="false"/> when the grid is larger than the limits allow (nothing is drawn).</returns>
    public static bool Decode(
        ReadOnlySpan<byte> coded,
        in Jbig2HalftoneParameters parameters,
        Jbig2PatternDictionary patterns,
        Jbig2Statistics statistics,
        Jbig2Bitmap region,
        Action<string, DiagnosticSeverity, string> report,
        long maxPixels)
    {
        long gridWidth = parameters.GridWidth;
        long gridHeight = parameters.GridHeight;
        if (gridWidth == 0 || gridHeight == 0)
        {
            return true;
        }

        int patternCount = patterns.Patterns.Length;
        int bitsPerPixel = Jbig2SymbolDictionary.CeilLog2(patternCount);
        long cells = gridWidth * gridHeight;
        long planeBytes = Jbig2Bitmap.StrideOf((int)Math.Min(int.MaxValue - 7, gridWidth)) * gridHeight;
        long work = cells * ((patterns.Width * (long)patterns.Height) + 64);
        if (cells > maxPixels || gridWidth > int.MaxValue - 7 || planeBytes * (bitsPerPixel + 1) > Array.MaxLength || work > 4 * maxPixels)
        {
            return false;
        }

        int width = (int)gridWidth;
        int height = (int)gridHeight;
        int stride = Jbig2Bitmap.StrideOf(width);
        int planeLength = stride * height;

        // HBPP gray-scale bitplanes, GSPLANES[J] at J * planeLength, then the SKIP bitmap.
        byte[] planes = ArrayPool<byte>.Shared.Rent(planeLength * (bitsPerPixel + 1));
        try
        {
            planes.AsSpan(0, planeLength * (bitsPerPixel + 1)).Clear();
            var skip = default(Jbig2Bitmap);
            if (parameters.EnableSkip && !parameters.Mmr)
            {
                skip = new Jbig2Bitmap(planes.AsSpan(bitsPerPixel * planeLength, planeLength), width, height, stride);
                for (int m = 0; m < height; m++)
                {
                    Span<byte> row = skip.Row(m);
                    for (int n = 0; n < width; n++)
                    {
                        (long x, long y) = Cell(parameters, m, n);
                        if (x + patterns.Width <= 0 || x >= region.Width || y + patterns.Height <= 0 || y >= region.Height)
                        {
                            row[n >> 3] |= (byte)(0x80 >> (n & 7));
                        }
                    }
                }
            }

            if (bitsPerPixel > 0)
            {
                DecodeGrayScale(coded, parameters, statistics, planes, width, height, stride, bitsPerPixel, skip, report);
            }

            bool outOfRange = false;
            for (int m = 0; m < height; m++)
            {
                for (int n = 0; n < width; n++)
                {
                    int offset = (m * stride) + (n >> 3);
                    int shift = ~n & 7;
                    if (!skip.IsEmpty && ((skip.Data[offset] >> shift) & 1) != 0)
                    {
                        continue;
                    }

                    long value = 0;
                    for (int j = 0; j < bitsPerPixel; j++)
                    {
                        value |= (long)((planes[(j * planeLength) + offset] >> shift) & 1) << j;
                    }

                    if (value >= patternCount)
                    {
                        outOfRange = true;
                        value = patternCount - 1;
                    }

                    (long x, long y) = Cell(parameters, m, n);
                    region.Compose(patterns.Patterns[value].View, x, y, parameters.CombinationOperator);
                }
            }

            if (outOfRange)
            {
                report(DiagnosticCodes.Jbig2GrayValueOutOfRange, DiagnosticSeverity.Warning, "has gray-scale values beyond its pattern dictionary's GRAYMAX (ITU-T T.88 §6.6.5.2); the last pattern is used for them");
            }

            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(planes);
        }
    }

    /// <summary>The top left of grid cell (m, n) (§6.6.5.2 step 3 c).</summary>
    private static (long X, long Y) Cell(in Jbig2HalftoneParameters parameters, long m, long n) =>
        ((parameters.GridX + (m * parameters.VectorY) + (n * parameters.VectorX)) >> 8, (parameters.GridY + (m * parameters.VectorX) - (n * parameters.VectorY)) >> 8);

    /// <summary>Annex C.5: the bitplanes from the most significant, each but the first XORed with the one above (Gray decoding).</summary>
    private static void DecodeGrayScale(
        ReadOnlySpan<byte> coded,
        in Jbig2HalftoneParameters parameters,
        Jbig2Statistics statistics,
        byte[] planes,
        int width,
        int height,
        int stride,
        int bitsPerPixel,
        Jbig2Bitmap skip,
        Action<string, DiagnosticSeverity, string> report)
    {
        int planeLength = stride * height;
        var decoder = parameters.Mmr ? default : new MqDecoder(coded);
        if (!parameters.Mmr)
        {
            statistics.ResetGeneric();
        }

        var generic = Jbig2GenericParameters.Nominal(parameters.Template) with
        {
            AtX1 = parameters.Template <= 1 ? 3 : 2,
            AtY1 = -1,
            UseSkip = parameters.EnableSkip,
        };
        ReadOnlySpan<byte> mmrData = coded;
        bool reported = false;
        for (int j = bitsPerPixel - 1; j >= 0; j--)
        {
            var bitmap = new Jbig2Bitmap(planes.AsSpan(j * planeLength, planeLength), width, height, stride);
            if (parameters.Mmr)
            {
                MmrResult result = Jbig2GenericRegion.DecodeMmr(mmrData, bitmap);
                if (!reported && (result.Status == MmrStatus.Invalid || result.Rows < height))
                {
                    reported = true;
                    report(DiagnosticCodes.Jbig2RegionDataTruncated, DiagnosticSeverity.Warning, "has MMR gray-scale bitplanes that end early or are invalid (ITU-T T.88 Annex C.5); the missing rows are 0");
                }

                mmrData = mmrData[Math.Min(mmrData.Length, result.BytesConsumed)..];
            }
            else
            {
                Jbig2GenericRegion.Decode(ref decoder, statistics.Generic, generic, bitmap, skip);
            }

            if (j < bitsPerPixel - 1)
            {
                // C.5 step 3 b: GSPLANES[J] = GSPLANES[J + 1] XOR GSPLANES[J].
                Span<byte> here = bitmap.Data;
                ReadOnlySpan<byte> above = planes.AsSpan((j + 1) * planeLength, planeLength);
                for (int i = 0; i < planeLength; i++)
                {
                    here[i] ^= above[i];
                }
            }
        }
    }
}
