using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using Broadside.Diagnostics;
using Broadside.Filters.Ccitt;
using Broadside.Filters.Codecs;
using Broadside.Parsing;

namespace Broadside.Filters.Jbig2;

/// <summary>
/// A decoded symbol dictionary segment (ITU-T T.88 §7.4.2, type 0): its exported symbols and, when the segment retains them, the
/// GB and GR statistics at its end (§7.4.2.2 step 7, E.3.8). Immutable, so dictionaries of a cached <c>JBIG2Globals</c> stream are
/// shared across images and threads; a later dictionary that reuses the statistics copies them.
/// </summary>
/// <remarks>
/// ITU-T T.88 §6.5 (the symbol dictionary decoding procedure: height classes, direct generic coding, refinement/aggregate coding
/// through the text region and refinement procedures, Huffman collective bitmaps uncompressed or MMR, export runs), §7.4.2 (segment
/// syntax, the custom tables taken from referred code table segments in the order DH, DW, BMSIZE, AGGINST, SDINSYMS from the
/// referred symbol dictionaries in referral order).
/// </remarks>
internal sealed class Jbig2SymbolDictionary
{
    /// <summary>The most symbols one dictionary may hold with its inputs, and a text region may refer to (the IAID limit, 2^20).</summary>
    public const int MaxSymbols = 1 << Jbig2IntegerDecoder.MaxSymbolCodeLength;

    private readonly ulong _signature;

    private Jbig2SymbolDictionary(Jbig2Image[] exported, ulong signature, byte[]? generic, byte[]? refinement)
    {
        Exported = exported;
        _signature = signature;
        RetainedGeneric = generic;
        RetainedRefinement = refinement;
    }

    /// <summary>Gets the exported symbols, SDEXSYMS.</summary>
    public Jbig2Image[] Exported { get; }

    /// <summary>Gets the GB statistics retained at the end of the segment, or null.</summary>
    public byte[]? RetainedGeneric { get; }

    /// <summary>Gets the GR statistics retained at the end of the segment, or null.</summary>
    public byte[]? RetainedRefinement { get; }

    /// <summary>Decodes a symbol dictionary segment.</summary>
    /// <param name="segment">The segment, for messages.</param>
    /// <param name="data">Its data part.</param>
    /// <param name="inputs">The referred symbol dictionaries in referral order.</param>
    /// <param name="tables">The referred code tables in referral order.</param>
    /// <param name="statistics">The decode's arithmetic statistics.</param>
    /// <param name="reporter">Where deviations go.</param>
    /// <param name="maxPixels">The most pixels the new symbols may hold together.</param>
    /// <returns>The dictionary; null when the segment cannot be decoded at all.</returns>
    public static Jbig2SymbolDictionary? Decode(
        in Jbig2Segment segment,
        ReadOnlySpan<byte> data,
        IReadOnlyList<Jbig2SymbolDictionary> inputs,
        IReadOnlyList<Jbig2HuffmanTable> tables,
        Jbig2Statistics statistics,
        Jbig2Reporter reporter,
        long maxPixels)
    {
        uint number = segment.Number;
        if (data.Length < 2)
        {
            Invalid(reporter, number, "has no flags (ITU-T T.88 §7.4.2.1.1)");
            return null;
        }

        int flags = BinaryPrimitives.ReadUInt16BigEndian(data);
        bool huffman = (flags & 1) != 0;
        bool refAgg = (flags & 2) != 0;
        int template = (flags >> 10) & 3;
        int refinementTemplate = (flags >> 12) & 1;
        bool contextUsed = (flags & 0x100) != 0;
        bool contextRetained = (flags & 0x200) != 0;
        int position = 2;
        int atBytes = huffman ? 0 : template == 0 ? 8 : 2;
        int refinementAtBytes = refAgg && refinementTemplate == 0 ? 4 : 0;
        if (data.Length < position + atBytes + refinementAtBytes + 8)
        {
            Invalid(reporter, number, "ends inside its data header (ITU-T T.88 §7.4.2.1)");
            return null;
        }

        ReadOnlySpan<byte> at = data.Slice(position, atBytes);
        position += atBytes;
        ReadOnlySpan<byte> refinementAt = data.Slice(position, refinementAtBytes);
        position += refinementAtBytes;
        long exportedCount = BinaryPrimitives.ReadUInt32BigEndian(data[position..]);
        long newCount = BinaryPrimitives.ReadUInt32BigEndian(data[(position + 4)..]);
        position += 8;

        var generic = Jbig2GenericParameters.Nominal(template);
        if (atBytes == 8)
        {
            generic = generic with
            {
                AtX1 = (sbyte)at[0],
                AtY1 = (sbyte)at[1],
                AtX2 = (sbyte)at[2],
                AtY2 = (sbyte)at[3],
                AtX3 = (sbyte)at[4],
                AtY3 = (sbyte)at[5],
                AtX4 = (sbyte)at[6],
                AtY4 = (sbyte)at[7],
            };
        }
        else if (atBytes == 2)
        {
            generic = generic with { AtX1 = (sbyte)at[0], AtY1 = (sbyte)at[1] };
        }

        var refinement = Jbig2RefinementParameters.Nominal(refinementTemplate);
        if (refinementAtBytes == 4)
        {
            refinement = refinement with { AtX1 = (sbyte)refinementAt[0], AtY1 = (sbyte)refinementAt[1], AtX2 = (sbyte)refinementAt[2], AtY2 = (sbyte)refinementAt[3] };
        }

        var symbols = new List<Jbig2Image>();
        long inputTotal = 0;
        foreach (Jbig2SymbolDictionary input in inputs)
        {
            inputTotal += input.Exported.Length;
            if (inputTotal <= MaxSymbols)
            {
                symbols.AddRange(input.Exported);
            }
        }

        int inputCount = symbols.Count;
        if (inputTotal + newCount > MaxSymbols)
        {
            reporter.Report(
                DiagnosticCodes.Jbig2LimitExceeded,
                DiagnosticSeverity.Error,
                string.Create(CultureInfo.InvariantCulture, $"The JBIG2 symbol dictionary segment {number} would hold {inputTotal + newCount} symbols, more than the {MaxSymbols} allowed; it is not decoded."));
            return null;
        }

        int total = inputCount + (int)newCount;
        int symbolCodeLength = CeilLog2(total);

        // §7.4.2.1.6: the Huffman tables, custom ones taken from the referred table segments in order.
        Jbig2HuffmanTable? deltaHeight = null, deltaWidth = null, bitmapSize = null, aggregateInstances = null;
        if (huffman)
        {
            int custom = 0;
            bool ok = true;
            deltaHeight = Select(((flags >> 2) & 3) switch { 0 => 4, 1 => 5, 3 => 0, _ => -1 }, tables, ref custom, ref ok);
            deltaWidth = Select(((flags >> 4) & 3) switch { 0 => 2, 1 => 3, 3 => 0, _ => -1 }, tables, ref custom, ref ok);
            bitmapSize = Select((flags & 0x40) != 0 ? 0 : 1, tables, ref custom, ref ok);
            aggregateInstances = Select((flags & 0x80) != 0 ? 0 : 1, tables, ref custom, ref ok);
            if (!ok)
            {
                reporter.Report(
                    DiagnosticCodes.Jbig2TableInvalid,
                    DiagnosticSeverity.Error,
                    string.Create(CultureInfo.InvariantCulture, $"The JBIG2 symbol dictionary segment {number} selects a reserved Huffman table or a custom table it does not refer to (ITU-T T.88 §7.4.2.1.6); it is not decoded."));
                return null;
            }
        }

        // §7.4.2.2 steps 3 to 5.
        if (contextUsed && TryAdopt(inputs, Signature(flags, at, refinementAt), statistics))
        {
            // GB and GR continue from the last referred dictionary.
        }
        else
        {
            if (contextUsed)
            {
                reporter.Report(
                    DiagnosticCodes.Jbig2ContextReuseInvalid,
                    DiagnosticSeverity.Warning,
                    string.Create(CultureInfo.InvariantCulture, $"The JBIG2 symbol dictionary segment {number} reuses the coding statistics of its last referred symbol dictionary, which did not retain them or differs in its coding parameters (ITU-T T.88 §7.4.2.2 step 3); the statistics are reset."));
            }

            statistics.ResetGeneric();
            statistics.ResetRefinement();
        }

        statistics.ResetIntegers(symbolCodeLength);
        var coder = new Jbig2Coder(data[position..], huffman, statistics);
        var context = new SymbolContext(number, reporter, maxPixels, generic, refinement, symbols, symbolCodeLength);
        DecodeNewSymbols(ref coder, context, refAgg, newCount, deltaHeight, deltaWidth, bitmapSize, aggregateInstances);

        // Symbols the data did not define (damaged data) are not exported.
        Jbig2Image[] exported = Export(ref coder, context, context.Symbols.Count, exportedCount, total);
        byte[]? retainedGeneric = null, retainedRefinement = null;
        if (contextRetained)
        {
            retainedGeneric = statistics.Generic.ToArray();
            retainedRefinement = statistics.Refinement.ToArray();
        }

        return new Jbig2SymbolDictionary(exported, Signature(flags, at, refinementAt), retainedGeneric, retainedRefinement);
    }

    /// <summary>ceil(log2 n), 0 for n &lt;= 1.</summary>
    public static int CeilLog2(long n) => n <= 1 ? 0 : BitOperations.Log2((ulong)(n - 1)) + 1;

    private static int DecodeNewSymbols(
        ref Jbig2Coder coder,
        SymbolContext context,
        bool refAgg,
        long newCount,
        Jbig2HuffmanTable? deltaHeightTable,
        Jbig2HuffmanTable? deltaWidthTable,
        Jbig2HuffmanTable? bitmapSizeTable,
        Jbig2HuffmanTable? aggregateTable)
    {
        bool collective = coder.Huffman && !refAgg;
        var widths = collective ? new List<long>() : null;
        long pixels = 0;
        long height = 0;
        int decoded = 0;
        int emptyClasses = 0;
        while (decoded < newCount)
        {
            if (coder.IsExhausted || emptyClasses > 1024)
            {
                context.Truncated();
                return decoded;
            }

            height += coder.Decode(Jbig2IntegerProcedure.Iadh, deltaHeightTable);
            long width = 0;
            long totalWidth = 0;
            int first = decoded;
            widths?.Clear();
            while (true)
            {
                if (!coder.TryDecode(Jbig2IntegerProcedure.Iadw, deltaWidthTable, out long deltaWidth))
                {
                    break;
                }

                if (decoded == newCount || coder.IsExhausted)
                {
                    context.Report(DiagnosticCodes.Jbig2RegionDataInvalid, DiagnosticSeverity.Error, "has more symbols in a height class than its SDNUMNEWSYMS, or data that ends inside one (ITU-T T.88 §6.5.5 step 4 c)");
                    return decoded;
                }

                width += deltaWidth;
                totalWidth += width;
                if (width < 0 || height < 0 || width > int.MaxValue - 7 || height > int.MaxValue)
                {
                    context.Report(DiagnosticCodes.Jbig2RegionDataInvalid, DiagnosticSeverity.Error, "decodes a symbol of negative or impossible size (ITU-T T.88 §6.5.5)");
                    return decoded;
                }

                // Each symbol also costs a fixed share, so damaged data cannot define millions of empty symbols.
                pixels += (width * height) + 64;
                if (pixels > context.MaxPixels || (long)Jbig2Bitmap.StrideOf((int)width) * height > Array.MaxLength)
                {
                    context.LimitExceeded();
                    return decoded;
                }

                if (collective)
                {
                    widths!.Add(width);
                }
                else
                {
                    Jbig2Image symbol = refAgg
                        ? DecodeAggregate(ref coder, context, (int)width, (int)height, aggregateTable)
                        : DecodeDirect(ref coder, context, (int)width, (int)height);
                    if (coder.Invalid)
                    {
                        context.Truncated();
                        return decoded;
                    }

                    context.Symbols.Add(symbol);
                }

                decoded++;
            }

            if (coder.Invalid)
            {
                context.Truncated();
                return decoded;
            }

            emptyClasses += decoded == first ? 1 : 0;
            if (collective && !DecodeCollective(ref coder, context, widths!, (int)height, totalWidth, bitmapSizeTable))
            {
                return first;
            }
        }

        return decoded;
    }

    /// <summary>Table 16: one generic region, continuing the segment's GB contexts.</summary>
    private static Jbig2Image DecodeDirect(ref Jbig2Coder coder, SymbolContext context, int width, int height)
    {
        var symbol = new Jbig2Image(width, height);
        if (width > 0 && height > 0)
        {
            Jbig2GenericRegion.Decode(ref coder.Mq, coder.Statistics.Generic, context.Generic, symbol.View, default);
        }

        return symbol;
    }

    /// <summary>§6.5.8.2: an aggregation of REFAGGNINST instances, or one refined symbol.</summary>
    private static Jbig2Image DecodeAggregate(ref Jbig2Coder coder, SymbolContext context, int width, int height, Jbig2HuffmanTable? aggregateTable)
    {
        long instances = coder.Decode(Jbig2IntegerProcedure.Iaai, aggregateTable);
        var symbol = new Jbig2Image(width, height);
        if (coder.Invalid)
        {
            return symbol;
        }

        if (instances > 1)
        {
            var parameters = new Jbig2TextParameters
            {
                Refine = true,
                Instances = instances,
                LogStrips = 0,
                Symbols = context.Symbols,
                SymbolCount = context.Symbols.Count,
                SymbolCodeLength = coder.Huffman ? Math.Max(context.SymbolCodeLength, 1) : context.SymbolCodeLength,
                CombinationOperator = Jbig2CombinationOperator.Or,
                ReferenceCorner = 1,
                FirstS = coder.Huffman ? Jbig2HuffmanTable.Standard(6) : null,
                DeltaS = coder.Huffman ? Jbig2HuffmanTable.Standard(8) : null,
                DeltaT = coder.Huffman ? Jbig2HuffmanTable.Standard(11) : null,
                RefinementDeltaWidth = coder.Huffman ? Jbig2HuffmanTable.Standard(15) : null,
                RefinementDeltaHeight = coder.Huffman ? Jbig2HuffmanTable.Standard(15) : null,
                RefinementX = coder.Huffman ? Jbig2HuffmanTable.Standard(15) : null,
                RefinementY = coder.Huffman ? Jbig2HuffmanTable.Standard(15) : null,
                RefinementSize = coder.Huffman ? Jbig2HuffmanTable.Standard(1) : null,
                Refinement = context.Refinement,
            };
            Jbig2TextResult result = Jbig2TextRegion.Decode(ref coder, parameters, symbol.View, context.MaxPixels, 64 * (((long)width * height) + 64));
            context.Report(result);
            return symbol;
        }

        if (instances < 1)
        {
            coder.Invalid = true;
            return symbol;
        }

        // §6.5.8.2.2: REFAGGNINST = 1.
        int id = coder.Huffman
            ? (int)Math.Min(int.MaxValue, coder.Bits.ReadBits(Math.Max(context.SymbolCodeLength, 1)))
            : Jbig2IntegerDecoder.DecodeId(ref coder.Mq, coder.Statistics.Id, context.SymbolCodeLength);
        long rdx = coder.Decode(Jbig2IntegerProcedure.Iardx, coder.Huffman ? Jbig2HuffmanTable.Standard(15) : null);
        long rdy = coder.Decode(Jbig2IntegerProcedure.Iardy, coder.Huffman ? Jbig2HuffmanTable.Standard(15) : null);
        long size = 0;
        if (coder.Huffman)
        {
            size = coder.Decode(Jbig2IntegerProcedure.Iari, Jbig2HuffmanTable.Standard(1));
            coder.Bits.Align();
        }

        if (coder.Invalid)
        {
            return symbol;
        }

        Jbig2Image reference = Jbig2Image.Empty;
        if ((uint)id < (uint)context.Symbols.Count)
        {
            reference = context.Symbols[id];
        }
        else
        {
            context.SymbolIdOutOfRange();
        }

        var parametersOne = context.Refinement with { ReferenceDx = rdx, ReferenceDy = rdy, TypicalPrediction = false };
        if (width > 0 && height > 0)
        {
            if (coder.Huffman)
            {
                ReadOnlySpan<byte> remaining = coder.Bits.Remaining;
                var decoder = new MqDecoder(remaining[..(int)Math.Clamp(size, 0, remaining.Length)]);
                Jbig2RefinementRegion.Decode(ref decoder, coder.Statistics.Refinement, parametersOne, reference.View, symbol.View);
            }
            else
            {
                Jbig2RefinementRegion.Decode(ref coder.Mq, coder.Statistics.Refinement, parametersOne, reference.View, symbol.View);
            }
        }

        if (coder.Huffman)
        {
            coder.Bits.SkipBytes(Math.Max(0, size));
        }

        return symbol;
    }

    /// <summary>§6.5.9 and step 4 d): the height class collective bitmap, split into the class's symbols.</summary>
    private static bool DecodeCollective(ref Jbig2Coder coder, SymbolContext context, List<long> widths, int height, long totalWidth, Jbig2HuffmanTable? bitmapSizeTable)
    {
        long size = coder.Decode(Jbig2IntegerProcedure.Iari, bitmapSizeTable);
        coder.Bits.Align();
        if (coder.Invalid || size < 0)
        {
            context.Truncated();
            return false;
        }

        int stride = Jbig2Bitmap.StrideOf((int)Math.Min(int.MaxValue - 7, totalWidth));
        if (totalWidth > int.MaxValue - 7 || (long)stride * height > Array.MaxLength || totalWidth * height > context.MaxPixels)
        {
            context.LimitExceeded();
            return false;
        }

        var collective = new Jbig2Image((int)totalWidth, height);
        ReadOnlySpan<byte> remaining = coder.Bits.Remaining;
        if (size == 0)
        {
            // Uncompressed: HCHEIGHT rows of ceil(TOTWIDTH / 8) bytes.
            long length = (long)stride * height;
            if (!collective.View.IsEmpty)
            {
                int available = (int)Math.Min(length, remaining.Length);
                remaining[..available].CopyTo(collective.Data);
                collective.View.ClearPadding();
                if (available < length)
                {
                    context.Truncated();
                }
            }

            coder.Bits.SkipBytes(length);
        }
        else
        {
            if (!collective.View.IsEmpty)
            {
                MmrResult result = Jbig2GenericRegion.DecodeMmr(remaining[..(int)Math.Min(size, remaining.Length)], collective.View);
                if (result.Status == MmrStatus.Invalid)
                {
                    context.Report(DiagnosticCodes.Jbig2RegionDataInvalid, DiagnosticSeverity.Error, "has a height class collective bitmap with an invalid MMR code (ITU-T T.88 §6.5.9)");
                }
                else if (result.Rows < height && result.Status != MmrStatus.EndOfBlock)
                {
                    context.Truncated();
                }
            }

            coder.Bits.SkipBytes(size);
        }

        long x = 0;
        foreach (long width in widths)
        {
            context.Symbols.Add(Jbig2Image.CopyColumns(collective.View, (int)x, (int)width));
            x += width;
        }

        return true;
    }

    /// <summary>§6.5.10: the export flags as runs; the exported symbols in input-then-new order.</summary>
    private static Jbig2Image[] Export(ref Jbig2Coder coder, SymbolContext context, int available, long expected, int declaredTotal)
    {
        var exported = new List<Jbig2Image>((int)Math.Min(expected, 4096));
        bool flag = false;
        long index = 0;
        long runs = 0;
        while (index < declaredTotal)
        {
            // Runs alternate between flags; more runs than flags plus a few zero-length ones means damaged data.
            if (coder.IsExhausted || ++runs > (2L * declaredTotal) + 16)
            {
                context.Truncated();
                break;
            }

            long run = coder.Decode(Jbig2IntegerProcedure.Iaex, coder.Huffman ? Jbig2HuffmanTable.Standard(1) : null);
            if (run < 0 || coder.Invalid)
            {
                context.Truncated();
                break;
            }

            if (index + run > declaredTotal)
            {
                context.Report(DiagnosticCodes.Jbig2ExportRunOverflow, DiagnosticSeverity.Warning, "has an export run past its last symbol (ITU-T T.88 §6.5.10); the run is cut there");
                run = declaredTotal - index;
            }

            if (flag)
            {
                for (long i = index; i < index + run && i < available; i++)
                {
                    exported.Add(context.Symbols[(int)i]);
                }
            }

            index += run;
            flag = !flag;
        }

        if (exported.Count != expected)
        {
            context.Report(
                DiagnosticCodes.Jbig2SymbolCountMismatch,
                DiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture, $"exports {exported.Count} symbols where its SDNUMEXSYMS says {expected} (ITU-T T.88 §6.5.10); the flagged symbols are exported"));
        }

        return [.. exported];
    }

    private static Jbig2HuffmanTable? Select(int choice, IReadOnlyList<Jbig2HuffmanTable> tables, ref int custom, ref bool ok)
    {
        if (choice > 0)
        {
            return Jbig2HuffmanTable.Standard(choice);
        }

        if (choice == 0 && custom < tables.Count)
        {
            return tables[custom++];
        }

        ok = false;
        return null;
    }

    /// <summary>The parameters §7.4.2.2 step 3 compares: SDHUFF, SDREFAGG, SDTEMPLATE, SDRTEMPLATE and every AT location.</summary>
    private static ulong Signature(int flags, ReadOnlySpan<byte> at, ReadOnlySpan<byte> refinementAt)
    {
        ulong signature = (ulong)(flags & 0x1C03);
        foreach (byte b in at)
        {
            signature = (signature * 1099511628211UL) ^ b;
        }

        signature = (signature * 1099511628211UL) ^ 0xA5;
        foreach (byte b in refinementAt)
        {
            signature = (signature * 1099511628211UL) ^ b;
        }

        return signature;
    }

    private static bool TryAdopt(IReadOnlyList<Jbig2SymbolDictionary> inputs, ulong signature, Jbig2Statistics statistics)
    {
        if (inputs.Count == 0 || inputs[^1] is not { RetainedGeneric: { } generic, RetainedRefinement: { } refinement } last || last._signature != signature)
        {
            return false;
        }

        generic.CopyTo(statistics.Generic);
        refinement.CopyTo(statistics.Refinement);
        return true;
    }

    private static void Invalid(Jbig2Reporter reporter, uint number, string what) => reporter.Report(
        DiagnosticCodes.Jbig2SegmentInvalid,
        DiagnosticSeverity.Error,
        string.Create(CultureInfo.InvariantCulture, $"The JBIG2 symbol dictionary segment {number} {what}; it is not decoded."));

    /// <summary>What the nested procedures of one dictionary share besides the coder.</summary>
    private sealed class SymbolContext(
        uint number,
        Jbig2Reporter reporter,
        long maxPixels,
        Jbig2GenericParameters generic,
        Jbig2RefinementParameters refinement,
        List<Jbig2Image> symbols,
        int symbolCodeLength)
    {
        public long MaxPixels => maxPixels;

        public Jbig2GenericParameters Generic => generic;

        public Jbig2RefinementParameters Refinement => refinement;

        /// <summary>Gets SDINSYMS followed by the new symbols decoded so far (SBSYMS of Table 17).</summary>
        public List<Jbig2Image> Symbols => symbols;

        public int SymbolCodeLength => symbolCodeLength;

        public void Report(string code, DiagnosticSeverity severity, string what) => reporter.Report(
            code,
            severity,
            string.Create(CultureInfo.InvariantCulture, $"The JBIG2 symbol dictionary segment {number} {what}."));

        public void Truncated() => Report(DiagnosticCodes.Jbig2RegionDataTruncated, DiagnosticSeverity.Warning, "has data that ends or becomes invalid before its last symbol; the symbols decoded so far are kept");

        public void LimitExceeded() => Report(DiagnosticCodes.Jbig2LimitExceeded, DiagnosticSeverity.Error, "holds more symbol pixels than the image limits allow; the symbols decoded so far are kept");

        public void SymbolIdOutOfRange() => Report(DiagnosticCodes.Jbig2SymbolIdOutOfRange, DiagnosticSeverity.Error, "refers to a symbol ID beyond its symbols (ITU-T T.88 §6.5.8.2.2); an empty symbol is used");

        public void Report(Jbig2TextResult result)
        {
            switch (result)
            {
                case Jbig2TextResult.SymbolIdOutOfRange:
                    SymbolIdOutOfRange();
                    break;
                case Jbig2TextResult.LimitExceeded:
                    LimitExceeded();
                    break;
            }
        }
    }
}

