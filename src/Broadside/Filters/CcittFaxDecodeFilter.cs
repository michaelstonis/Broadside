using System.Buffers;
using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Filters.Ccitt;
using Broadside.Images;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Filters;

/// <summary>
/// The <c>CCITTFaxDecode</c> filter: bilevel images coded with the CCITT facsimile schemes, Group 3 one-dimensional (Modified
/// Huffman), Group 3 two-dimensional (Modified READ) and Group 4 (Modified Modified READ). Decode only.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.6, Table 11; ITU-T T.4 §4 (Group 3: code tables, EOL, fill, tag bits, RTC); ITU-T T.6 §2 (Group 4: EOFB). Every
/// Table 11 parameter is honoured: <c>K</c> (only its sign: the tag bits decide which lines are one-dimensional), <c>EndOfLine</c>,
/// <c>EncodedByteAlign</c>, <c>Columns</c>, <c>Rows</c>, <c>EndOfBlock</c>, <c>BlackIs1</c> and <c>DamagedRowsBeforeError</c>. The
/// output is <c>ceil(Columns / 8)</c> bytes per row, MSB first: 0 is black and 1 white (the PDF convention) unless <c>BlackIs1</c>
/// is true; the pad bits after the last column are white. As Adobe's decoder does, EOLs are accepted whether or not
/// <c>EndOfLine</c> asks for them and EOFB/RTC end the data whatever <c>EndOfBlock</c> says; with <c>EndOfBlock</c> false and
/// <c>Rows</c> given, decoding stops after <c>Rows</c> rows. An image's <c>Height</c> caps the rows decoded.
/// </para>
/// <para>
/// As an image codec (<see cref="IImageFilter"/>) it decodes into a 1-component, 1-bit image of the dictionary's <c>Width</c> and
/// <c>Height</c>: rows are decoded at <c>Columns</c> and cut or padded with white to <c>Width</c> (<c>CcittWidthMismatch</c>), and rows
/// the data does not reach are white (<see cref="DecodedImage.DecodedRows"/> says how many were decoded). The samples have the
/// polarity the parameters give them; the image's <c>Decode</c> array maps them, as for any image.
/// </para>
/// <para>
/// Lenient repair: an invalid code damages its row, which keeps what decoded and is white after (<c>CcittDataInvalid</c>), or is
/// replaced by the previous row (or a white row after another damaged one) within <c>DamagedRowsBeforeError</c>
/// (<c>CcittDamagedRowReplaced</c>); decoding resumes at the next EOL, or stops when the data has none. Runs past the last column
/// are cut (<c>CcittRunTooLong</c>); data that ends inside a row, or before <c>Rows</c> rows, keeps what decoded
/// (<c>FilterDataTruncated</c>). Uncompressed mode (T.4 Table 5) is not decoded: the rest of that row is white
/// (<c>CcittUncompressedMode</c>, Information). Invalid parameters take their defaults (<c>DecodeParmsInvalid</c>).
/// </para>
/// <para>Stateless; each call allocates only its two pooled changing-element arrays, nothing per row.</para>
/// </remarks>
public sealed class CcittFaxDecodeFilter : IImageFilter
{
    private static readonly CosName K = new("K");
    private static readonly CosName EndOfLine = new("EndOfLine");
    private static readonly CosName EncodedByteAlign = new("EncodedByteAlign");
    private static readonly CosName Columns = new("Columns");
    private static readonly CosName Rows = new("Rows");
    private static readonly CosName EndOfBlock = new("EndOfBlock");
    private static readonly CosName BlackIs1 = new("BlackIs1");
    private static readonly CosName DamagedRowsBeforeError = new("DamagedRowsBeforeError");

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.6: <c>CCITTFaxDecode</c>; abbreviated <c>CCF</c> in inline images (§8.9.7).</remarks>
    public CosName Name => FilterNames.CcittFaxDecode;

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.6, Table 11; ITU-T T.4 §4; ITU-T T.6 §2.</remarks>
    public void Decode(ReadOnlyMemory<byte> encoded, IBufferWriter<byte> output, FilterContext context)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(context);
        int width = ReadDimension(context, ImageNames.Width);
        CcittParameters parameters = ReadParameters(context, width, report: true);
        if (!FitsLimit(parameters, context.MaxDecodedLength, context))
        {
            return;
        }

        int height = ReadDimension(context, ImageNames.Height);
        int rowBytes = parameters.RowBytes;
        int limit = height > 0 ? height : (int)Math.Min(int.MaxValue, (context.MaxDecodedLength / rowBytes) + 1);
        int work = CcittLineDecoder.WorkLength(parameters.Columns);
        int[] first = ArrayPool<int>.Shared.Rent(work);
        int[] second = ArrayPool<int>.Shared.Rent(work);
        try
        {
            var decoder = new CcittFaxDecoder(encoded.Span, parameters, limit, first, second);
            var writer = new FilterOutput(output);
            while (decoder.MoveNext())
            {
                decoder.WriteRow(writer.Reserve(rowBytes));
            }

            writer.Flush();
            Report(context, decoder, expectedRows: 0);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(first);
            ArrayPool<int>.Shared.Return(second);
        }
    }

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.6 and §8.9.5: the image is the dictionary's <c>Width</c> (else <c>Columns</c>) by its <c>Height</c> (else <c>Rows</c>, 0 when unknown), one 1-bit component.</remarks>
    public bool TryReadHeader(ReadOnlySpan<byte> encoded, ImageFilterContext context, out ImageHeader header)
    {
        ArgumentNullException.ThrowIfNull(context);
        CcittParameters parameters = ReadParameters(context.Filter, context.Width, report: false);
        header = new ImageHeader(context.Width > 0 ? context.Width : parameters.Columns, context.Height > 0 ? context.Height : parameters.Rows, 1, 1)
        {
            ColorModel = ImageColorModel.Gray,
        };
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.6, Table 11, and §8.9.3; ITU-T T.4 §4; ITU-T T.6 §2.</remarks>
    public DecodedImage? DecodeImage(ReadOnlyMemory<byte> encoded, ImageFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        FilterContext filter = context.Filter;
        CcittParameters parameters = ReadParameters(filter, context.Width, report: false);
        int width = context.Width > 0 ? context.Width : parameters.Columns;
        int height = context.Height > 0 ? context.Height : parameters.Rows;

        // With no height known, the stream path decodes and reports; otherwise report the parameters here.
        if (height > 0)
        {
            parameters = ReadParameters(filter, context.Width, report: true);
            if (!FitsLimit(parameters, context.MaxDecodedLength, filter))
            {
                return null;
            }
        }

        if (width != parameters.Columns)
        {
            filter.Report(DiagnosticCodes.CcittWidthMismatch, DiagnosticSeverity.Warning, string.Create(
                CultureInfo.InvariantCulture,
                $"The image is {width} pixels wide but CCITTFaxDecode Columns is {parameters.Columns}; rows are decoded at Columns and {(width < parameters.Columns ? "cut" : "padded with white")} to the width."));
        }

        return height > 0
            ? DecodeKnownHeight(encoded.Span, filter, context, parameters, width, height)
            : DecodeUnknownHeight(encoded, filter, context, parameters, width);
    }

    private static DecodedImage? DecodeKnownHeight(ReadOnlySpan<byte> encoded, FilterContext filter, ImageFilterContext context, CcittParameters parameters, int width, int height)
    {
        int work = CcittLineDecoder.WorkLength(parameters.Columns);
        int rowBytes = parameters.RowBytes;
        bool restride = width != parameters.Columns;
        int[] first = ArrayPool<int>.Shared.Rent(work);
        int[] second = ArrayPool<int>.Shared.Rent(work);
        byte[]? scratch = restride ? ArrayPool<byte>.Shared.Rent(rowBytes) : null;
        DecodedImageBuilder? image = null;
        try
        {
            if (!context.TryCreateImage(width, height, 1, 1, out image))
            {
                return null;
            }

            byte white = parameters.BlackIs1 ? (byte)0x00 : (byte)0xFF;
            var decoder = new CcittFaxDecoder(encoded, parameters, height, first, second);
            int y = 0;
            while (decoder.MoveNext())
            {
                Span<byte> row = image.GetRow(y++);
                if (restride)
                {
                    decoder.WriteRow(scratch);
                    CopyRow(scratch.AsSpan(0, rowBytes), parameters.Columns, row, width, white);
                }
                else
                {
                    decoder.WriteRow(row);
                }
            }

            image.Samples[(y * image.Stride)..].Fill(white);
            Report(filter, decoder, expectedRows: height);
            if (y < height)
            {
                image.DecodedRows = y;
            }

            return image.Build();
        }
        finally
        {
            image?.Dispose();
            ArrayPool<int>.Shared.Return(first);
            ArrayPool<int>.Shared.Return(second);
            if (scratch is not null)
            {
                ArrayPool<byte>.Shared.Return(scratch);
            }
        }
    }

    private DecodedImage? DecodeUnknownHeight(ReadOnlyMemory<byte> encoded, FilterContext filter, ImageFilterContext context, CcittParameters parameters, int width)
    {
        using var rows = new PooledBufferWriter(parameters.RowBytes * 64L, context.MaxDecodedLength);
        try
        {
            Decode(encoded, rows, filter);
        }
        catch (DecodedLengthExceededException)
        {
            // Keep the rows that fit; TryCreateImage reports the size.
        }

        int rowBytes = parameters.RowBytes;
        int height = rows.WrittenSpan.Length / rowBytes;
        if (height == 0)
        {
            return null;
        }

        DecodedImageBuilder? image = null;
        try
        {
            if (!context.TryCreateImage(width, height, 1, 1, out image))
            {
                return null;
            }

            byte white = parameters.BlackIs1 ? (byte)0x00 : (byte)0xFF;
            for (int y = 0; y < height; y++)
            {
                CopyRow(rows.WrittenSpan.Slice(y * rowBytes, rowBytes), parameters.Columns, image.GetRow(y), width, white);
            }

            return image.Build();
        }
        finally
        {
            image?.Dispose();
        }
    }

    /// <summary>Copies a row decoded at <paramref name="columns"/> into one of <paramref name="width"/> pixels: cut, or padded with white.</summary>
    private static void CopyRow(ReadOnlySpan<byte> source, int columns, Span<byte> destination, int width, byte white)
    {
        int bits = Math.Min(columns, width);
        int whole = bits >> 3;
        source[..whole].CopyTo(destination);
        destination[whole..].Fill(white);
        int rest = bits & 7;
        if (rest != 0)
        {
            byte keep = (byte)(0xFF << (8 - rest));
            destination[whole] = (byte)((source[whole] & keep) | (white & ~keep));
        }
    }

    private static bool FitsLimit(CcittParameters parameters, long limit, FilterContext context)
    {
        long work = 2L * 4 * CcittLineDecoder.WorkLength(parameters.Columns);
        if (parameters.RowBytes <= limit && work <= limit)
        {
            return true;
        }

        context.Report(DiagnosticCodes.DecodeParmsInvalid, DiagnosticSeverity.Error, string.Create(
            CultureInfo.InvariantCulture,
            $"CCITTFaxDecode Columns {parameters.Columns} needs more memory than the {limit}-byte decoding limit allows; nothing is decoded."));
        return false;
    }

    private static void Report(FilterContext context, in CcittFaxDecoder decoder, int expectedRows)
    {
        CcittIssues issues = decoder.Issues;
        if (issues == CcittIssues.None && decoder.RowsDecoded >= expectedRows)
        {
            return;
        }

        string at = string.Create(CultureInfo.InvariantCulture, $"row {decoder.FirstIssueRow + 1}");
        if ((issues & CcittIssues.Uncompressed) != 0)
        {
            context.Report(DiagnosticCodes.CcittUncompressedMode, DiagnosticSeverity.Information, $"CCITTFaxDecode data uses uncompressed mode (ITU-T T.4 Table 5), which is not decoded; the rest of the row is white (first issue at {at}).");
        }

        if ((issues & CcittIssues.MissingEol) != 0)
        {
            context.Report(DiagnosticCodes.CcittMissingEol, DiagnosticSeverity.Warning, $"CCITTFaxDecode EndOfLine is true but lines have no EOL before them; they are decoded without (first issue at {at}).");
        }

        if ((issues & CcittIssues.RunTooLong) != 0)
        {
            context.Report(DiagnosticCodes.CcittRunTooLong, DiagnosticSeverity.Warning, $"CCITTFaxDecode runs go past the last column; they are cut there (first issue at {at}).");
        }

        if ((issues & CcittIssues.DamagedRowReplaced) != 0)
        {
            context.Report(DiagnosticCodes.CcittDamagedRowReplaced, DiagnosticSeverity.Warning, string.Create(
                CultureInfo.InvariantCulture,
                $"CCITTFaxDecode data has damaged rows ({decoder.DamagedRows}); within DamagedRowsBeforeError they are replaced by the previous row or a white row (first issue at {at})."));
        }

        if ((issues & (CcittIssues.DamagedRowResynchronized | CcittIssues.DamagedData)) != 0)
        {
            context.Report(DiagnosticCodes.CcittDataInvalid, DiagnosticSeverity.Error, (issues & CcittIssues.DamagedData) != 0
                ? $"CCITTFaxDecode data has an invalid code and no EOL to resume at; the row is kept as far as it decodes, white after, and decoding stops ({at})."
                : $"CCITTFaxDecode data has invalid codes; the damaged rows are kept as far as they decode, white after, and decoding resumes at the next EOL (first issue at {at}).");
        }

        bool cut = (issues & (CcittIssues.Truncated | CcittIssues.DamagedData)) != 0;
        if ((issues & CcittIssues.Truncated) != 0)
        {
            context.Report(DiagnosticCodes.FilterDataTruncated, DiagnosticSeverity.Warning, string.Create(
                CultureInfo.InvariantCulture,
                $"CCITTFaxDecode data ends inside row {decoder.RowsDecoded}; the row is white after the last code."));
        }
        else if (!cut && ((issues & CcittIssues.FewerRows) != 0 || decoder.RowsDecoded < expectedRows))
        {
            context.Report(DiagnosticCodes.FilterDataTruncated, DiagnosticSeverity.Warning, string.Create(
                CultureInfo.InvariantCulture,
                $"CCITTFaxDecode data ends after {decoder.RowsDecoded} rows where {Math.Max(expectedRows, 0)} are expected{(expectedRows > 0 ? "; the rest are white" : string.Empty)}."));
        }
    }

    private static int ReadDimension(FilterContext context, CosName key) =>
        context.Resolve(context.StreamDictionary.TryGetValue(key, out CosObject? value) ? value : null) is CosInteger { Value: > 0 and <= int.MaxValue } integer
            ? (int)integer.Value
            : 0;

    /// <summary>Reads Table 11's parameters, each invalid one replaced by its default (Columns: the image's Width when it has one).</summary>
    private static CcittParameters ReadParameters(FilterContext context, int width, bool report)
    {
        int k = Read(K) switch
        {
            CosNull => 0,
            CosInteger { Value: var value } => Math.Sign(value),
            CosReal { Value: var value } => Invalid(Math.Sign(value), "K shall be an integer; the sign of the number is used"),
            _ => Invalid(0, "K shall be an integer; the default 0 is used"),
        };

        int columns = Read(Columns) switch
        {
            CosNull => 1728,
            CosInteger { Value: >= 1 and <= int.MaxValue - 64 } integer => (int)integer.Value,
            _ => Invalid(width > 0 ? width : 1728, width > 0 ? "Columns shall be a positive integer; the image's Width is used" : "Columns shall be a positive integer; the default 1728 is used"),
        };

        return new CcittParameters(
            K: k,
            EndOfLine: ReadBoolean(EndOfLine, false),
            EncodedByteAlign: ReadBoolean(EncodedByteAlign, false),
            Columns: columns,
            Rows: ReadCount(Rows),
            EndOfBlock: ReadBoolean(EndOfBlock, true),
            BlackIs1: ReadBoolean(BlackIs1, false),
            DamagedRowsBeforeError: ReadCount(DamagedRowsBeforeError));

        CosObject Read(CosName key) =>
            context.Parameters is { } parameters && parameters.TryGetValue(key, out CosObject? entry) ? context.Resolve(entry) : CosNull.Instance;

        bool ReadBoolean(CosName key, bool defaultValue) => Read(key) switch
        {
            CosNull => defaultValue,
            CosBoolean { Value: var value } => value,
            _ => Invalid(defaultValue, $"{key.Value} shall be a boolean; the default {(defaultValue ? "true" : "false")} is used"),
        };

        int ReadCount(CosName key) => Read(key) switch
        {
            CosNull => 0,
            CosInteger { Value: >= 0 and <= int.MaxValue } integer => (int)integer.Value,
            _ => Invalid(0, $"{key.Value} shall be a non-negative integer; the default 0 is used"),
        };

        T Invalid<T>(T value, string message)
        {
            if (report)
            {
                context.Report(DiagnosticCodes.DecodeParmsInvalid, DiagnosticSeverity.Warning, $"CCITTFaxDecode {message}.");
            }

            return value;
        }
    }
}
