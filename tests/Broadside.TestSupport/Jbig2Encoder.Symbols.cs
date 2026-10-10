namespace Broadside.TestSupport;

/// <summary>One symbol instance of a text region: the symbol, where its bitmap's top left goes, and optionally a refined bitmap.</summary>
/// <param name="Symbol">The symbol ID (index into the text region's symbols).</param>
/// <param name="X">The column of the instance bitmap's top left pixel in the region.</param>
/// <param name="Y">The row of the instance bitmap's top left pixel in the region.</param>
/// <param name="Refined">The refined instance bitmap (RI = 1), coded against the symbol; null for the symbol itself.</param>
/// <param name="RefinementX">RDX for a refined instance.</param>
/// <param name="RefinementY">RDY for a refined instance.</param>
public sealed record Jbig2Instance(int Symbol, int X, int Y, bool[][]? Refined = null, int RefinementX = 0, int RefinementY = 0);

/// <summary>The text region parameters a test chooses (ITU-T T.88 §7.4.3.1.1).</summary>
public sealed record Jbig2TextOptions
{
    /// <summary>Gets REFCORNER: 0 BOTTOMLEFT, 1 TOPLEFT, 2 BOTTOMRIGHT, 3 TOPRIGHT.</summary>
    public int Corner { get; init; } = 1;

    /// <summary>Gets a value indicating whether the region is TRANSPOSED.</summary>
    public bool Transposed { get; init; }

    /// <summary>Gets LOGSBSTRIPS.</summary>
    public int LogStrips { get; init; }

    /// <summary>Gets SBDSOFFSET (-16 to 15).</summary>
    public int DsOffset { get; init; }

    /// <summary>Gets SBCOMBOP: 0 OR, 1 AND, 2 XOR, 3 XNOR.</summary>
    public int CombinationOperator { get; init; }

    /// <summary>Gets SBDEFPIXEL.</summary>
    public int DefaultPixel { get; init; }

    /// <summary>Gets a value indicating whether instances may be refined (SBREFINE).</summary>
    public bool Refine { get; init; }

    /// <summary>Gets SBRTEMPLATE.</summary>
    public int RefinementTemplate { get; init; }

    /// <summary>
    /// Gets a value indicating whether the region is Huffman coded: FS with Table B.6, DS with Table B.8, DT with the custom table
    /// of the referred code table segment (the B.4 example, Table B.1 coded per B.2), RDW to RDY with B.15, RSIZE with B.1, and a
    /// symbol ID table giving every symbol the same code length.
    /// </summary>
    public bool Huffman { get; init; }
}

/// <summary>Symbol dictionaries, text regions, pattern dictionaries, halftone regions and refinement regions for tests (ITU-T T.88 §6.3-§6.7).</summary>
public static partial class Jbig2Encoder
{
    /// <summary>The ITU-T T.88 B.4 example: Table B.1 coded as a code table segment's data.</summary>
    public static byte[] TableB1Segment { get; } = [0x42, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x01, 0x10, 0x49, 0x23, 0x81, 0x80];

    // Annex B.5 rows (PREFLEN, RANGELEN, RANGELOW, kind: 0 normal, 1 lower, 2 upper, 3 OOB) for the tables these tests code with.
    private static readonly (int Prefix, int Range, long Low, int Kind)[] B1 = [(1, 4, 0, 0), (2, 8, 16, 0), (3, 16, 272, 0), (3, 32, 65808, 2)];
    private static readonly (int Prefix, int Range, long Low, int Kind)[] B2 = [(1, 0, 0, 0), (2, 0, 1, 0), (3, 0, 2, 0), (4, 3, 3, 0), (5, 6, 11, 0), (6, 32, 75, 2), (6, 0, 0, 3)];
    private static readonly (int Prefix, int Range, long Low, int Kind)[] B4 = [(1, 0, 1, 0), (2, 0, 2, 0), (3, 0, 3, 0), (4, 3, 4, 0), (5, 6, 12, 0), (5, 32, 76, 2)];
    private static readonly (int Prefix, int Range, long Low, int Kind)[] B6 =
    [
        (5, 10, -2048, 0), (4, 9, -1024, 0), (4, 8, -512, 0), (4, 7, -256, 0), (5, 6, -128, 0), (5, 5, -64, 0), (4, 5, -32, 0), (2, 7, 0, 0),
        (3, 7, 128, 0), (3, 8, 256, 0), (4, 9, 512, 0), (4, 10, 1024, 0), (6, 32, -2049, 1), (6, 32, 2048, 2),
    ];

    private static readonly (int Prefix, int Range, long Low, int Kind)[] B8 =
    [
        (8, 3, -15, 0), (9, 1, -7, 0), (8, 1, -5, 0), (9, 0, -3, 0), (7, 0, -2, 0), (4, 0, -1, 0), (2, 1, 0, 0), (5, 0, 2, 0), (6, 0, 3, 0),
        (3, 4, 4, 0), (6, 1, 20, 0), (4, 4, 22, 0), (4, 5, 38, 0), (5, 6, 70, 0), (5, 7, 134, 0), (6, 7, 262, 0), (7, 8, 390, 0), (6, 10, 646, 0),
        (9, 32, -16, 1), (9, 32, 1670, 2), (2, 0, 0, 3),
    ];

    private static readonly (int Prefix, int Range, long Low, int Kind)[] B15 =
    [
        (7, 4, -24, 0), (6, 2, -8, 0), (5, 1, -4, 0), (4, 0, -2, 0), (3, 0, -1, 0), (1, 0, 0, 0), (3, 0, 1, 0), (4, 0, 2, 0), (5, 1, 3, 0),
        (6, 2, 5, 0), (7, 4, 9, 0), (7, 32, -25, 1), (7, 32, 25, 2),
    ];

    private enum Ia
    {
        Aai, Adh, Ads, Adt, Adw, Aex, Afs, Ait, Ardh, Ardw, Ardx, Ardy, Ari,
    }

    /// <summary>
    /// A segment with referred-to segments (§7.2.4, short form for up to 4 references; 1-byte numbers when the segment number is at
    /// most 256).
    /// </summary>
    public static byte[] Segment(uint number, int type, uint page, uint[] referredTo, ReadOnlySpan<byte> data)
    {
        if (referredTo.Length > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(referredTo));
        }

        var header = new List<byte>();
        AddUInt32(header, number);
        header.Add((byte)type);
        header.Add((byte)(referredTo.Length << 5));
        foreach (uint referred in referredTo)
        {
            if (number <= 256)
            {
                header.Add((byte)referred);
            }
            else
            {
                header.Add((byte)(referred >> 8));
                header.Add((byte)referred);
            }
        }

        header.Add((byte)page);
        AddUInt32(header, (uint)data.Length);
        return [.. header, .. data];
    }

    /// <summary>
    /// An arithmetic symbol dictionary segment's data (§6.5, §7.4.2) defining <paramref name="symbols"/> by direct generic coding with
    /// <paramref name="template"/> (nominal AT pixels), one height class per height in increasing order (symbols are reordered so;
    /// the result maps the new order), exporting every new symbol.
    /// </summary>
    /// <param name="symbols">The symbol bitmaps.</param>
    /// <param name="template">SDTEMPLATE.</param>
    /// <param name="order">The input index of each defined symbol, in symbol ID order.</param>
    /// <param name="retain">Whether the "bitmap coding context retained" bit is set.</param>
    /// <param name="contexts">The GB contexts to continue (and leave as they end); null for fresh ones.</param>
    /// <param name="contextUsed">Whether the "bitmap coding context used" bit is set (pass the retained <paramref name="contexts"/>).</param>
    /// <param name="inputCount">SDNUMINSYMS: the symbols of referred dictionaries, none of them exported.</param>
    public static byte[] SymbolDictionary(IReadOnlyList<bool[][]> symbols, int template, out int[] order, bool retain = false, byte[]? contexts = null, bool contextUsed = false, int inputCount = 0)
    {
        order = [.. Enumerable.Range(0, symbols.Count).OrderBy(i => symbols[i].Length).ThenBy(i => i)];
        var coder = new MqEncoder();
        var integers = new IntegerContexts();
        byte[] generic = contexts ?? new byte[65536];
        int height = 0;
        int index = 0;
        while (index < order.Length)
        {
            int classHeight = symbols[order[index]].Length;
            integers.Encode(coder, Ia.Adh, classHeight - height);
            height = classHeight;
            int width = 0;
            while (index < order.Length && symbols[order[index]].Length == classHeight)
            {
                bool[][] symbol = symbols[order[index]];
                int symbolWidth = symbol.Length == 0 ? 0 : symbol[0].Length;
                integers.Encode(coder, Ia.Adw, symbolWidth - width);
                width = symbolWidth;
                EncodeGeneric(coder, generic, symbol, template, typicalPrediction: false, null, skip: null);
                index++;
            }

            integers.EncodeOob(coder, Ia.Adw);
        }

        integers.Encode(coder, Ia.Aex, inputCount);
        integers.Encode(coder, Ia.Aex, symbols.Count);
        int flags = (template << 10) | (retain ? 0x200 : 0) | (contextUsed ? 0x100 : 0);
        var data = new List<byte> { (byte)(flags >> 8), (byte)flags };
        foreach ((int x, int y) in Jbig2AtPixels.Nominal(template).Pixels)
        {
            data.Add((byte)(sbyte)x);
            data.Add((byte)(sbyte)y);
        }

        AddUInt32(data, (uint)symbols.Count);
        AddUInt32(data, (uint)symbols.Count);
        data.AddRange(coder.Flush());
        data.AddRange([0xFF, 0xAC]);
        return [.. data];
    }

    /// <summary>
    /// A Huffman symbol dictionary segment's data (§6.5.9, §7.4.2): DH with Table B.4, DW with B.2, BMSIZE and the export runs with
    /// B.1; each height class's collective bitmap uncompressed (BMSIZE 0) or MMR coded without EOFB. Symbols are ordered by height,
    /// then width.
    /// </summary>
    public static byte[] HuffmanSymbolDictionary(IReadOnlyList<bool[][]> symbols, bool mmr, out int[] order)
    {
        // Table B.2 codes no negative delta width: within a height class the symbols go narrowest first.
        order = [.. Enumerable.Range(0, symbols.Count).OrderBy(i => symbols[i].Length).ThenBy(i => symbols[i][0].Length).ThenBy(i => i)];
        var bits = new BitWriter();
        int height = 0;
        int index = 0;
        while (index < order.Length)
        {
            int classHeight = symbols[order[index]].Length;
            HuffmanEncode(bits, B4, classHeight - height);
            height = classHeight;
            int width = 0;
            var members = new List<bool[][]>();
            while (index < order.Length && symbols[order[index]].Length == classHeight)
            {
                bool[][] symbol = symbols[order[index]];
                HuffmanEncode(bits, B2, symbol[0].Length - width);
                width = symbol[0].Length;
                members.Add(symbol);
                index++;
            }

            HuffmanEncodeOob(bits, B2);
            bool[][] collective = [.. Enumerable.Range(0, classHeight).Select(y => members.SelectMany(m => m[y]).ToArray())];
            byte[] coded = mmr ? EncodeMmr(collective, endOfBlock: false) : PackJbig2(collective);
            HuffmanEncode(bits, B1, mmr ? coded.Length : 0);
            bits.Align();
            bits.AddBytes(coded);
        }

        HuffmanEncode(bits, B1, 0);
        HuffmanEncode(bits, B1, symbols.Count);
        bits.Align();
        var data = new List<byte> { 0x00, 0x01 };
        AddUInt32(data, (uint)symbols.Count);
        AddUInt32(data, (uint)symbols.Count);
        data.AddRange(bits.ToArray());
        return [.. data];
    }

    /// <summary>
    /// A text region segment's data (§6.4, §7.4.3): region information (<paramref name="width"/> x <paramref name="height"/> at
    /// (<paramref name="x"/>, <paramref name="y"/>), operator <paramref name="op"/>), flags, and the instances coded so that each
    /// instance bitmap's top left lands where <see cref="Jbig2Instance"/> says.
    /// </summary>
    public static byte[] TextRegion(
        int width,
        int height,
        uint x,
        uint y,
        int op,
        IReadOnlyList<bool[][]> symbols,
        IReadOnlyList<Jbig2Instance> instances,
        Jbig2TextOptions options)
    {
        int strips = 1 << options.LogStrips;
        bool right = (options.Corner & 2) != 0;
        bool top = (options.Corner & 1) != 0;
        int codeLength = symbols.Count <= 1 ? 0 : (int)Math.Ceiling(Math.Log2(symbols.Count));
        int huffmanCodeLength = Math.Max(codeLength, 1);

        var placed = new List<(Jbig2Instance Instance, long S, long T, int W, int H)>();
        foreach (Jbig2Instance instance in instances)
        {
            bool[][] bitmap = instance.Refined ?? symbols[instance.Symbol];
            int w = bitmap.Length == 0 ? 0 : bitmap[0].Length;
            int h = bitmap.Length;
            (long s, long t) = !options.Transposed ? (instance.X + (right ? w - 1 : 0), instance.Y + (top ? 0 : h - 1)) : (instance.Y + (top ? 0 : h - 1), instance.X + (right ? w - 1 : 0));
            placed.Add((instance, s, t, w, h));
        }

        var groups = placed.GroupBy(p => (long)Math.Floor(p.T / (double)strips)).OrderBy(g => g.Key).ToList();
        var coder = new MqEncoder();
        var integers = new IntegerContexts();
        byte[] idContexts = new byte[1 << Math.Max(codeLength, 0)];
        byte[] refinement = new byte[8192];
        var bits = new BitWriter();

        void Int(Ia procedure, (int, int, long, int)[] table, long value)
        {
            if (options.Huffman)
            {
                HuffmanEncode(bits, table, value);
            }
            else
            {
                integers.Encode(coder, procedure, value);
            }
        }

        if (options.Huffman)
        {
            // §7.4.3.1.7: RUNCODE<len> gets the only code ("0"); every symbol is then that run code once.
            for (int run = 0; run < 35; run++)
            {
                bits.Add(run == huffmanCodeLength ? 1 : 0, 4);
            }

            for (int i = 0; i < symbols.Count; i++)
            {
                bits.Add(0, 1);
            }

            bits.Align();
        }

        Int(Ia.Adt, B1, 0);
        long stripT = 0;
        long firstS = 0;
        foreach (var group in groups)
        {
            long stripBase = group.Key * strips;
            Int(Ia.Adt, B1, (stripBase - stripT) / strips);
            stripT = stripBase;
            long curS = 0;
            bool first = true;
            foreach (var p in group.OrderBy(p => p.S))
            {
                long pre = !options.Transposed && right ? p.W - 1 : options.Transposed && !top ? p.H - 1 : 0;
                long post = !options.Transposed && !right ? p.W - 1 : options.Transposed && top ? p.H - 1 : 0;
                long before = p.S - pre;
                if (first)
                {
                    Int(Ia.Afs, B6, before - firstS);
                    firstS = before;
                    first = false;
                }
                else
                {
                    Int(Ia.Ads, B8, before - curS - options.DsOffset);
                }

                if (strips > 1)
                {
                    if (options.Huffman)
                    {
                        bits.Add((int)(p.T - stripT), options.LogStrips);
                    }
                    else
                    {
                        integers.Encode(coder, Ia.Ait, p.T - stripT);
                    }
                }

                if (options.Huffman)
                {
                    bits.Add(p.Instance.Symbol, huffmanCodeLength);
                }
                else
                {
                    EncodeId(coder, idContexts, p.Instance.Symbol, codeLength);
                }

                if (options.Refine)
                {
                    int ri = p.Instance.Refined is null ? 0 : 1;
                    if (options.Huffman)
                    {
                        bits.Add(ri, 1);
                    }
                    else
                    {
                        integers.Encode(coder, Ia.Ari, ri);
                    }

                    if (ri == 1)
                    {
                        bool[][] reference = symbols[p.Instance.Symbol];
                        int rdw = p.W - (reference.Length == 0 ? 0 : reference[0].Length);
                        int rdh = p.H - reference.Length;
                        Int(Ia.Ardw, B15, rdw);
                        Int(Ia.Ardh, B15, rdh);
                        Int(Ia.Ardx, B15, p.Instance.RefinementX);
                        Int(Ia.Ardy, B15, p.Instance.RefinementY);
                        int dx = (rdw >> 1) + p.Instance.RefinementX;
                        int dy = (rdh >> 1) + p.Instance.RefinementY;
                        if (options.Huffman)
                        {
                            var sub = new MqEncoder();
                            EncodeRefinement(sub, refinement, p.Instance.Refined!, reference, dx, dy, options.RefinementTemplate, Jbig2RefinementAt.Nominal, typicalPrediction: false);
                            byte[] coded = [.. sub.Flush(), 0xFF, 0xAC];
                            HuffmanEncode(bits, B1, coded.Length);
                            bits.Align();
                            bits.AddBytes(coded);
                        }
                        else
                        {
                            EncodeRefinement(coder, refinement, p.Instance.Refined!, reference, dx, dy, options.RefinementTemplate, Jbig2RefinementAt.Nominal, typicalPrediction: false);
                        }
                    }
                }

                curS = p.S + post;
            }

            if (options.Huffman)
            {
                HuffmanEncodeOob(bits, B8);
            }
            else
            {
                integers.EncodeOob(coder, Ia.Ads);
            }
        }

        var data = new List<byte>();
        AddUInt32(data, (uint)width);
        AddUInt32(data, (uint)height);
        AddUInt32(data, x);
        AddUInt32(data, y);
        data.Add((byte)op);
        int flags = (options.Huffman ? 1 : 0) | (options.Refine ? 2 : 0) | (options.LogStrips << 2) | (options.Corner << 4) | (options.Transposed ? 0x40 : 0)
            | (options.CombinationOperator << 7) | (options.DefaultPixel << 9) | ((options.DsOffset & 31) << 10) | (options.RefinementTemplate << 15);
        data.Add((byte)(flags >> 8));
        data.Add((byte)flags);
        if (options.Huffman)
        {
            // FS B.6 (0), DS B.8 (0), DT custom (3), RDW/RDH/RDX/RDY B.15 (1), RSIZE B.1 (0).
            int huffmanFlags = (3 << 4) | (options.Refine ? (1 << 6) | (1 << 8) | (1 << 10) | (1 << 12) : 0);
            data.Add((byte)(huffmanFlags >> 8));
            data.Add((byte)huffmanFlags);
        }

        if (options.Refine && options.RefinementTemplate == 0)
        {
            data.AddRange([0xFF, 0xFF, 0xFF, 0xFF]);
        }

        AddUInt32(data, (uint)instances.Count);
        if (options.Huffman)
        {
            bits.Align();
            data.AddRange(bits.ToArray());
        }
        else
        {
            data.AddRange(coder.Flush());
            data.AddRange([0xFF, 0xAC]);
        }

        return [.. data];
    }

    /// <summary>
    /// A pattern dictionary segment's data (§6.7, §7.4.4): <paramref name="patterns"/> (all <paramref name="size"/> x
    /// <paramref name="size"/>) in one collective bitmap, coded with <paramref name="template"/> and A1 at (-HDPW, 0), or MMR.
    /// </summary>
    public static byte[] PatternDictionary(IReadOnlyList<bool[][]> patterns, int size, int template, bool mmr)
    {
        bool[][] collective = [.. Enumerable.Range(0, size).Select(y => patterns.SelectMany(p => p[y]).ToArray())];
        var data = new List<byte> { (byte)((mmr ? 1 : 0) | (template << 1)), (byte)size, (byte)size };
        AddUInt32(data, (uint)(patterns.Count - 1));
        if (mmr)
        {
            data.AddRange(EncodeMmr(collective));
        }
        else
        {
            Jbig2AtPixels at = template == 0 ? new((-size, 0), (-3, -1), (2, -2), (-2, -2)) : new((-size, 0));
            data.AddRange(EncodeGeneric(collective, template, typicalPrediction: false, at));
        }

        return [.. data];
    }

    /// <summary>
    /// A halftone region segment's data (§6.6, §7.4.5) for a square grid: cell (m, n) at (<paramref name="gridX"/> + n
    /// <paramref name="cell"/>, <paramref name="gridY"/> + m <paramref name="cell"/>) shows pattern <c>gray[m][n]</c>; the gray-scale
    /// image is coded as Gray-coded bitplanes (Annex C) with <paramref name="template"/>, or MMR.
    /// </summary>
    public static byte[] HalftoneRegion(int width, int height, int op, int[][] gray, int patternCount, int cell, int gridX, int gridY, int template, bool mmr, int combination = 0, bool enableSkip = false, int defaultPixel = 0)
    {
        int gridHeight = gray.Length;
        int gridWidth = gray[0].Length;
        int bitsPerPixel = patternCount <= 1 ? 0 : (int)Math.Ceiling(Math.Log2(patternCount));
        var data = new List<byte>();
        AddUInt32(data, (uint)width);
        AddUInt32(data, (uint)height);
        AddUInt32(data, 0);
        AddUInt32(data, 0);
        data.Add((byte)op);
        data.Add((byte)((mmr ? 1 : 0) | (template << 1) | (enableSkip ? 8 : 0) | (combination << 4) | (defaultPixel << 7)));
        AddUInt32(data, (uint)gridWidth);
        AddUInt32(data, (uint)gridHeight);
        AddUInt32(data, (uint)(gridX * 256));
        AddUInt32(data, (uint)(gridY * 256));
        data.AddRange([(byte)((cell * 256) >> 8), (byte)(cell * 256), 0, 0]);
        bool[][]? skip = null;
        if (enableSkip)
        {
            skip = [.. Enumerable.Range(0, gridHeight).Select(m => Enumerable.Range(0, gridWidth).Select(n =>
            {
                int px = gridX + (n * cell);
                int py = gridY + (m * cell);
                return px + cell <= 0 || px >= width || py + cell <= 0 || py >= height;
            }).ToArray())];
        }

        var coder = new MqEncoder();
        byte[] contexts = new byte[65536];
        var mmrPlanes = new List<byte>();
        Jbig2AtPixels at = template == 0 ? new((3, -1), (-3, -1), (2, -2), (-2, -2)) : template == 1 ? new((3, -1)) : new((2, -1));
        for (int j = bitsPerPixel - 1; j >= 0; j--)
        {
            int plane = j;
            bool[][] coded = [.. Enumerable.Range(0, gridHeight).Select(m => Enumerable.Range(0, gridWidth).Select(n =>
            {
                if (skip is not null && skip[m][n])
                {
                    return false;
                }

                int value = gray[m][n];
                int bit = (value >> plane) & 1;
                int above = plane == bitsPerPixel - 1 ? 0 : (value >> (plane + 1)) & 1;
                return (bit ^ above) != 0;
            }).ToArray())];
            if (mmr)
            {
                mmrPlanes.AddRange(EncodeMmr(coded, endOfBlock: true));
            }
            else
            {
                EncodeGeneric(coder, contexts, coded, template, typicalPrediction: false, at, skip);
            }
        }

        if (mmr)
        {
            data.AddRange(mmrPlanes);
        }
        else if (bitsPerPixel > 0)
        {
            data.AddRange(coder.Flush());
            data.AddRange([0xFF, 0xAC]);
        }

        return [.. data];
    }

    /// <summary>
    /// A generic refinement region segment's data (§6.3, §7.4.7): region information, flags, AT bytes for template 0, and
    /// <paramref name="target"/> coded against <paramref name="reference"/> (GRREFERENCEDX = GRREFERENCEDY = 0).
    /// </summary>
    public static byte[] RefinementRegion(bool[][] target, bool[][] reference, uint x, uint y, int op, int template, bool typicalPrediction, Jbig2RefinementAt? at = null)
    {
        Jbig2RefinementAt pixels = at ?? Jbig2RefinementAt.Nominal;
        var data = new List<byte>();
        AddUInt32(data, (uint)target[0].Length);
        AddUInt32(data, (uint)target.Length);
        AddUInt32(data, x);
        AddUInt32(data, y);
        data.Add((byte)op);
        data.Add((byte)(template | (typicalPrediction ? 2 : 0)));
        if (template == 0)
        {
            data.AddRange([(byte)(sbyte)pixels.X1, (byte)(sbyte)pixels.Y1, (byte)(sbyte)pixels.X2, (byte)(sbyte)pixels.Y2]);
        }

        var coder = new MqEncoder();
        EncodeRefinement(coder, new byte[8192], target, reference, 0, 0, template, pixels, typicalPrediction);
        data.AddRange(coder.Flush());
        data.AddRange([0xFF, 0xAC]);
        return [.. data];
    }

    /// <summary>Packs a bitmap in JBIG2 polarity (1 = black), rows padded with 0 bits (an uncompressed collective bitmap).</summary>
    public static byte[] PackJbig2(bool[][] rows)
    {
        int width = rows.Length == 0 ? 0 : rows[0].Length;
        int stride = (width + 7) / 8;
        byte[] packed = new byte[stride * rows.Length];
        for (int y = 0; y < rows.Length; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (rows[y][x])
                {
                    packed[(y * stride) + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
        }

        return packed;
    }

    /// <summary>The generic refinement encoder (§6.3.5), contexts gathered pixel by pixel in reading order, refined bitmap first.</summary>
    private static void EncodeRefinement(MqEncoder coder, byte[] contexts, bool[][] target, bool[][] reference, int dx, int dy, int template, Jbig2RefinementAt at, bool typicalPrediction)
    {
        (int X, int Y, bool Reference)[] pixels = template == 0
            ?
            [
                (at.X1, at.Y1, false), (0, -1, false), (1, -1, false), (-1, 0, false),
                (at.X2, at.Y2, true), (0, -1, true), (1, -1, true), (-1, 0, true), (0, 0, true), (1, 0, true), (-1, 1, true), (0, 1, true), (1, 1, true),
            ]
            :
            [
                (-1, -1, false), (0, -1, false), (1, -1, false), (-1, 0, false),
                (0, -1, true), (-1, 0, true), (0, 0, true), (1, 0, true), (0, 1, true), (1, 1, true),
            ];
        int sltp = template == 0 ? 0x0010 : 0x0008;
        int width = target[0].Length;
        bool ltp = false;
        for (int y = 0; y < target.Length; y++)
        {
            if (typicalPrediction)
            {
                bool typical = true;
                for (int x = 0; x < width && typical; x++)
                {
                    int block = Block(reference, x - dx, y - dy);
                    typical = block < 0 || (block == 1) == target[y][x];
                }

                coder.Encode(contexts, sltp, typical != ltp ? 1 : 0);
                ltp = typical;
            }

            for (int x = 0; x < width; x++)
            {
                if (ltp && Block(reference, x - dx, y - dy) >= 0)
                {
                    continue;
                }

                int context = 0;
                foreach ((int px, int py, bool isReference) in pixels)
                {
                    context = (context << 1) | (isReference ? Pixel(reference, x - dx + px, y - dy + py) : Pixel(target, x + px, y + py));
                }

                coder.Encode(contexts, context, target[y][x] ? 1 : 0);
            }
        }
    }

    private static int Block(bool[][] reference, int x, int y)
    {
        int first = Pixel(reference, x - 1, y - 1);
        for (int j = -1; j <= 1; j++)
        {
            for (int i = -1; i <= 1; i++)
            {
                if (Pixel(reference, x + i, y + j) != first)
                {
                    return -1;
                }
            }
        }

        return first;
    }

    /// <summary>IAID (A.3).</summary>
    private static void EncodeId(MqEncoder coder, byte[] contexts, int id, int codeLength)
    {
        int prev = 1;
        for (int i = codeLength - 1; i >= 0; i--)
        {
            int bit = (id >> i) & 1;
            coder.Encode(contexts, prev, bit);
            prev = (prev << 1) | bit;
        }
    }

    private static void HuffmanEncode(BitWriter bits, (int Prefix, int Range, long Low, int Kind)[] table, long value)
    {
        long[] codes = AssignCodes(table);
        for (int i = 0; i < table.Length; i++)
        {
            (int prefix, int range, long low, int kind) = table[i];
            bool fits = kind switch
            {
                0 => value >= low && value < low + (1L << range),
                1 => value <= low,
                2 => value >= low,
                _ => false,
            };
            if (fits && prefix > 0)
            {
                bits.Add(codes[i], prefix);
                bits.Add(kind == 1 ? low - value : value - low, range);
                return;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(value), value, "The table cannot code the value.");
    }

    private static void HuffmanEncodeOob(BitWriter bits, (int Prefix, int Range, long Low, int Kind)[] table)
    {
        long[] codes = AssignCodes(table);
        int i = Array.FindIndex(table, line => line.Kind == 3);
        bits.Add(codes[i], table[i].Prefix);
    }

    /// <summary>B.3: canonical codes in line order within each prefix length.</summary>
    private static long[] AssignCodes((int Prefix, int Range, long Low, int Kind)[] table)
    {
        int max = table.Max(line => line.Prefix);
        long[] codes = new long[table.Length];
        long first = 0;
        int previousCount = 0;
        for (int length = 1; length <= max; length++)
        {
            first = (first + previousCount) << 1;
            long code = first;
            previousCount = 0;
            for (int i = 0; i < table.Length; i++)
            {
                if (table[i].Prefix == length)
                {
                    codes[i] = code++;
                    previousCount++;
                }
            }
        }

        return codes;
    }

    /// <summary>The thirteen integer coders of Annex A.2, inverted (encoding the prefix, magnitude class and bits).</summary>
    private sealed class IntegerContexts
    {
        private readonly byte[][] _contexts = [.. Enumerable.Range(0, 13).Select(_ => new byte[512])];

        public void Encode(MqEncoder coder, Ia procedure, long value)
        {
            byte[] contexts = _contexts[(int)procedure];
            long magnitude = Math.Abs(value);
            (int prefixOnes, int bits, long offset) = magnitude switch
            {
                < 4 => (0, 2, 0L),
                < 20 => (1, 4, 4L),
                < 84 => (2, 6, 20L),
                < 340 => (3, 8, 84L),
                < 4436 => (4, 12, 340L),
                _ => (5, 32, 4436L),
            };
            int prev = 1;
            Bit(coder, contexts, ref prev, value < 0 ? 1 : 0);
            for (int i = 0; i < prefixOnes; i++)
            {
                Bit(coder, contexts, ref prev, 1);
            }

            if (prefixOnes < 5)
            {
                Bit(coder, contexts, ref prev, 0);
            }

            long v = magnitude - offset;
            for (int i = bits - 1; i >= 0; i--)
            {
                Bit(coder, contexts, ref prev, (int)((v >> i) & 1));
            }
        }

        public void EncodeOob(MqEncoder coder, Ia procedure)
        {
            // OOB = sign 1, value 0 (the 2-bit class).
            byte[] contexts = _contexts[(int)procedure];
            int prev = 1;
            Bit(coder, contexts, ref prev, 1);
            Bit(coder, contexts, ref prev, 0);
            Bit(coder, contexts, ref prev, 0);
            Bit(coder, contexts, ref prev, 0);
        }

        private static void Bit(MqEncoder coder, byte[] contexts, ref int prev, int bit)
        {
            coder.Encode(contexts, prev, bit);
            prev = prev < 256 ? (prev << 1) | bit : (((prev << 1) | bit) & 511) | 256;
        }
    }

    /// <summary>An MSB-first bit writer.</summary>
    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = [];
        private int _bit;

        public void Add(long value, int count)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                if (_bit == 0)
                {
                    _bytes.Add(0);
                }

                if (((value >> i) & 1) != 0)
                {
                    _bytes[^1] |= (byte)(0x80 >> _bit);
                }

                _bit = (_bit + 1) & 7;
            }
        }

        public void Align() => _bit = 0;

        public void AddBytes(byte[] bytes)
        {
            Align();
            _bytes.AddRange(bytes);
        }

        public byte[] ToArray() => [.. _bytes];
    }
}

/// <summary>The AT pixels of generic refinement template 0 (ITU-T T.88 §6.3.5.3): RA1 in the refined bitmap, RA2 in the reference.</summary>
public readonly record struct Jbig2RefinementAt(int X1, int Y1, int X2, int Y2)
{
    /// <summary>The nominal locations, both (-1, -1).</summary>
    public static Jbig2RefinementAt Nominal => new(-1, -1, -1, -1);
}
