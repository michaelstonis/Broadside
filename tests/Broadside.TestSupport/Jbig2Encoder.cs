using System.Buffers.Binary;

namespace Broadside.TestSupport;

/// <summary>The AT pixel locations of a generic region (ITU-T T.88 §6.2.5.4): four pairs for template 0, one for the others.</summary>
/// <param name="Pixels">(x, y) for A1 to A4 (template 0) or A1 (templates 1 to 3).</param>
public readonly record struct Jbig2AtPixels(params (int X, int Y)[] Pixels)
{
    /// <summary>The nominal locations of T.88 Table 5.</summary>
    public static Jbig2AtPixels Nominal(int template) => template switch
    {
        0 => new((3, -1), (-3, -1), (2, -2), (-2, -2)),
        1 => new((3, -1)),
        _ => new((2, -1)),
    };
}

/// <summary>
/// A JBIG2 encoder for tests (ITU-T T.88): the MQ arithmetic coder of Annex E.2, the template-based generic region encoder of §6.2.5
/// (every template, any AT pixels, typical prediction), MMR generic regions through <see cref="CcittEncoder"/>, and builders for the
/// segments of an embedded PDF stream (§7.2, §7.4). Bitmaps are rows of booleans, <see langword="true"/> = black (1). The C# twin of
/// <c>jbig2_*</c> in <c>tests/Corpus/generate.py</c>; shared by the JBIG2 tests, the benchmarks and the fuzz seeds.
/// </summary>
/// <remarks>
/// The context is gathered pixel by pixel from a table of template positions (not rolled, as the decoder does), so an encoder and
/// decoder that agree are evidence for both. AT pixels keep the context bit of their nominal location (§6.2.5.7 allows any fixed order).
/// </remarks>
public static class Jbig2Encoder
{
    // T.88 Table E.1: Qe, NMPS, NLPS, SWITCH.
    private static readonly (int Qe, int Nmps, int Nlps, int Switch)[] States =
    [
        (0x5601, 1, 1, 1), (0x3401, 2, 6, 0), (0x1801, 3, 9, 0), (0x0AC1, 4, 12, 0), (0x0521, 5, 29, 0), (0x0221, 38, 33, 0),
        (0x5601, 7, 6, 1), (0x5401, 8, 14, 0), (0x4801, 9, 14, 0), (0x3801, 10, 14, 0), (0x3001, 11, 17, 0), (0x2401, 12, 18, 0),
        (0x1C01, 13, 20, 0), (0x1601, 29, 21, 0), (0x5601, 15, 14, 1), (0x5401, 16, 14, 0), (0x5101, 17, 15, 0), (0x4801, 18, 16, 0),
        (0x3801, 19, 17, 0), (0x3401, 20, 18, 0), (0x3001, 21, 19, 0), (0x2801, 22, 19, 0), (0x2401, 23, 20, 0), (0x2201, 24, 21, 0),
        (0x1C01, 25, 22, 0), (0x1801, 26, 23, 0), (0x1601, 27, 24, 0), (0x1401, 28, 25, 0), (0x1201, 29, 26, 0), (0x1101, 30, 27, 0),
        (0x0AC1, 31, 28, 0), (0x09C1, 32, 29, 0), (0x08A1, 33, 30, 0), (0x0521, 34, 31, 0), (0x0441, 35, 32, 0), (0x02A1, 36, 33, 0),
        (0x0221, 37, 34, 0), (0x0141, 38, 35, 0), (0x0111, 39, 36, 0), (0x0085, 40, 37, 0), (0x0049, 41, 38, 0), (0x0025, 42, 39, 0),
        (0x0015, 43, 40, 0), (0x0009, 44, 41, 0), (0x0005, 45, 42, 0), (0x0001, 45, 43, 0), (0x5601, 46, 46, 0),
    ];

    // T.88 Figures 3 to 6 in reading order: (dx, dy) of a fixed pixel, or (AT index, 0) with IsAt.
    private static readonly (int Dx, int Dy, int At)[][] Templates =
    [
        [(0, 0, 4), (-1, -2, 0), (0, -2, 0), (1, -2, 0), (0, 0, 3), (0, 0, 2), (-2, -1, 0), (-1, -1, 0), (0, -1, 0), (1, -1, 0), (2, -1, 0), (0, 0, 1), (-4, 0, 0), (-3, 0, 0), (-2, 0, 0), (-1, 0, 0)],
        [(-1, -2, 0), (0, -2, 0), (1, -2, 0), (2, -2, 0), (-2, -1, 0), (-1, -1, 0), (0, -1, 0), (1, -1, 0), (2, -1, 0), (0, 0, 1), (-3, 0, 0), (-2, 0, 0), (-1, 0, 0)],
        [(-1, -2, 0), (0, -2, 0), (1, -2, 0), (-2, -1, 0), (-1, -1, 0), (0, -1, 0), (1, -1, 0), (0, 0, 1), (-2, 0, 0), (-1, 0, 0)],
        [(-3, -1, 0), (-2, -1, 0), (-1, -1, 0), (0, -1, 0), (1, -1, 0), (0, 0, 1), (-4, 0, 0), (-3, 0, 0), (-2, 0, 0), (-1, 0, 0)],
    ];

    // T.88 Figures 8 to 11 gathered in the same order.
    private static readonly int[] Sltp = [0x9B25, 0x0795, 0x00E5, 0x0195];

    /// <summary>Encodes a bitmap with template-based arithmetic coding (§6.2.5), terminated by FLUSH and the marker 0xFF 0xAC.</summary>
    public static byte[] EncodeGeneric(bool[][] rows, int template, bool typicalPrediction, Jbig2AtPixels? at = null)
    {
        (int X, int Y)[] pixels = (at ?? Jbig2AtPixels.Nominal(template)).Pixels;
        var coder = new MqEncoder();
        byte[] contexts = new byte[65536];
        int width = rows.Length == 0 ? 0 : rows[0].Length;
        bool ltp = false;
        for (int y = 0; y < rows.Length; y++)
        {
            if (typicalPrediction)
            {
                bool typical = rows[y].AsSpan().SequenceEqual(y == 0 ? new bool[width] : rows[y - 1]);
                coder.Encode(contexts, Sltp[template], typical != ltp ? 1 : 0);
                ltp = typical;
                if (ltp)
                {
                    continue;
                }
            }

            for (int x = 0; x < width; x++)
            {
                int context = 0;
                foreach ((int dx, int dy, int index) in Templates[template])
                {
                    (int px, int py) = index == 0 ? (dx, dy) : pixels[index - 1];
                    context = (context << 1) | Pixel(rows, x + px, y + py);
                }

                coder.Encode(contexts, context, rows[y][x] ? 1 : 0);
            }
        }

        return [.. coder.Flush(), 0xFF, 0xAC];
    }

    /// <summary>Encodes a bitmap with MMR (§6.2.6): T.6 two-dimensional coding, with or without EOFB.</summary>
    public static byte[] EncodeMmr(bool[][] rows, bool endOfBlock = true) => CcittEncoder.Encode(rows, new CcittEncoding(K: -1, EndOfBlock: endOfBlock));

    /// <summary>A segment: header (§7.2) and data. The page association is short when it fits in a byte; no referred-to segments.</summary>
    public static byte[] Segment(uint number, int type, uint page, ReadOnlySpan<byte> data, uint? declaredLength = null)
    {
        bool longPage = page > 255;
        var header = new List<byte>();
        AddUInt32(header, number);
        header.Add((byte)(type | (longPage ? 0x40 : 0)));
        header.Add(0);
        if (longPage)
        {
            AddUInt32(header, page);
        }
        else
        {
            header.Add((byte)page);
        }

        AddUInt32(header, declaredLength ?? (uint)data.Length);
        return [.. header, .. data];
    }

    /// <summary>A page information segment's data (§7.4.8): size, unknown resolution, flags, striping.</summary>
    public static byte[] PageInformation(uint width, uint height, int defaultPixel = 0, int defaultOperator = 0, bool operatorOverridden = false, ushort striping = 0)
    {
        var data = new List<byte>();
        AddUInt32(data, width);
        AddUInt32(data, height);
        AddUInt32(data, 0);
        AddUInt32(data, 0);
        data.Add((byte)((defaultPixel << 2) | (defaultOperator << 3) | (operatorOverridden ? 0x40 : 0)));
        data.Add((byte)(striping >> 8));
        data.Add((byte)striping);
        return [.. data];
    }

    /// <summary>
    /// A generic region segment's data (§7.4.6): region information (§7.4.1), flags, AT bytes and the coded bitmap. With
    /// <paramref name="rowCount"/>, the unknown-length trailer (terminator and row count, §7.4.6.4) follows.
    /// </summary>
    public static byte[] GenericRegion(bool[][] rows, uint x, uint y, int op, bool mmr, int template = 0, bool typicalPrediction = false, Jbig2AtPixels? at = null, uint? rowCount = null)
    {
        var data = new List<byte>();
        AddUInt32(data, (uint)(rows.Length == 0 ? 0 : rows[0].Length));
        AddUInt32(data, (uint)rows.Length);
        AddUInt32(data, x);
        AddUInt32(data, y);
        data.Add((byte)op);
        data.Add((byte)((mmr ? 1 : 0) | (template << 1) | (typicalPrediction ? 8 : 0)));
        if (!mmr)
        {
            foreach ((int ax, int ay) in (at ?? Jbig2AtPixels.Nominal(template)).Pixels)
            {
                data.Add((byte)(sbyte)ax);
                data.Add((byte)(sbyte)ay);
            }
        }

        data.AddRange(mmr ? EncodeMmr(rows) : EncodeGeneric(rows, template, typicalPrediction, at));
        if (rowCount is uint count)
        {
            if (mmr)
            {
                data.AddRange([0x00, 0x00]);
            }
            else if (data[^2] != 0xFF || data[^1] != 0xAC)
            {
                data.AddRange([0xFF, 0xAC]);
            }

            AddUInt32(data, count);
        }

        return [.. data];
    }

    /// <summary>A single-byte coded comment extension segment's data (§7.4.15.1).</summary>
    public static byte[] Comment(string name, string value)
    {
        var data = new List<byte>();
        AddUInt32(data, 0x20000000);
        data.AddRange(System.Text.Encoding.Latin1.GetBytes(name));
        data.Add(0);
        data.AddRange(System.Text.Encoding.Latin1.GetBytes(value));
        data.AddRange([0, 0]);
        return [.. data];
    }

    /// <summary>A JBIG2 file header (D.4): sequential or random-access organisation, one page.</summary>
    public static byte[] FileHeader(bool sequential = true) => [0x97, 0x4A, 0x42, 0x32, 0x0D, 0x0A, 0x1A, 0x0A, (byte)(sequential ? 1 : 0), 0, 0, 0, 1];

    /// <summary>A four-byte big-endian value.</summary>
    public static byte[] BigEndian(uint value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    /// <summary>Packs a bitmap in PDF polarity (0 = black), rows padded to a byte with 0 bits: what JBIG2Decode delivers.</summary>
    public static byte[] PackPdf(bool[][] rows, int width)
    {
        int stride = (width + 7) / 8;
        byte[] packed = new byte[stride * rows.Length];
        for (int y = 0; y < rows.Length; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!rows[y][x])
                {
                    packed[(y * stride) + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
        }

        return packed;
    }

    private static int Pixel(bool[][] rows, int x, int y) => y >= 0 && y < rows.Length && x >= 0 && x < rows[y].Length && rows[y][x] ? 1 : 0;

    private static void AddUInt32(List<byte> bytes, uint value) => bytes.AddRange([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);

    /// <summary>T.88 E.2: INITENC, ENCODE (CODEMPS, CODELPS), RENORME, BYTEOUT and FLUSH, software conventions.</summary>
    private sealed class MqEncoder
    {
        private readonly List<byte> _out = [0];
        private int _a = 0x8000;
        private long _c;
        private int _ct = 12;

        public void Encode(byte[] contexts, int label, int d)
        {
            int state = contexts[label] >> 1;
            int mps = contexts[label] & 1;
            (int qe, int nmps, int nlps, int @switch) = States[state];
            _a -= qe;
            if (d == mps)
            {
                if ((_a & 0x8000) != 0)
                {
                    _c += qe;
                    return;
                }

                if (_a < qe)
                {
                    _a = qe;
                }
                else
                {
                    _c += qe;
                }

                contexts[label] = (byte)((nmps << 1) | mps);
            }
            else
            {
                if (_a < qe)
                {
                    _c += qe;
                }
                else
                {
                    _a = qe;
                }

                contexts[label] = (byte)((nlps << 1) | (mps ^ @switch));
            }

            do
            {
                _a = (_a << 1) & 0xFFFF;
                _c = (_c << 1) & 0xFFFFFFFF;
                if (--_ct == 0)
                {
                    ByteOut();
                }
            }
            while ((_a & 0x8000) == 0);
        }

        public byte[] Flush()
        {
            long temp = _c + _a;
            _c |= 0xFFFF;
            if (_c >= temp)
            {
                _c -= 0x8000;
            }

            _c = (_c << _ct) & 0xFFFFFFFF;
            ByteOut();
            _c = (_c << _ct) & 0xFFFFFFFF;
            ByteOut();
            List<byte> data = _out[1..];
            while (data.Count > 0 && data[^1] == 0xFF)
            {
                data.RemoveAt(data.Count - 1);
            }

            return [.. data];
        }

        private void ByteOut()
        {
            if (_out[^1] == 0xFF)
            {
                Emit20();
            }
            else if ((_c & 0x8000000) == 0)
            {
                Emit19();
            }
            else
            {
                _out[^1]++;
                if (_out[^1] == 0xFF)
                {
                    _c &= 0x7FFFFFF;
                    Emit20();
                }
                else
                {
                    Emit19();
                }
            }
        }

        private void Emit20()
        {
            _out.Add((byte)((_c >> 20) & 0xFF));
            _c &= 0xFFFFF;
            _ct = 7;
        }

        private void Emit19()
        {
            _out.Add((byte)((_c >> 19) & 0xFF));
            _c &= 0x7FFFF;
            _ct = 8;
        }
    }
}
