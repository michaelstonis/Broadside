using System.Buffers;
using System.Runtime.CompilerServices;
using Broadside.Filters.Codecs;

namespace Broadside.Filters.Jpx;

/// <summary>How one code-block is coded and reconstructed.</summary>
/// <param name="Width">The code-block width.</param>
/// <param name="Height">The code-block height.</param>
/// <param name="Orientation">The sub-band: 0 LL, 1 HL, 2 LH, 3 HH.</param>
/// <param name="Style">The code-block style bits (Table A.19).</param>
/// <param name="TopPlane">The bit-plane of the first cleanup pass, Mb + s - 1 - P.</param>
/// <param name="RoiShift">The region-of-interest shift s (H.1), 0 without one.</param>
/// <param name="Scale">Half the irreversible quantization step size (E-3); 0 for the reversible path.</param>
internal readonly record struct JpxBlockCoding(int Width, int Height, int Orientation, int Style, int TopPlane, int RoiShift, float Scale);

/// <summary>
/// Tier-1 decoding of one code-block: the significance propagation, magnitude refinement and cleanup passes of EBCOT driven by the
/// MQ decoder (or read raw in the bypass mode), then the reconstruction of the quantization indices.
/// </summary>
/// <remarks>
/// <para>
/// ITU-T T.800 Annex D (D.1 to D.7, Tables D.1 to D.9), E.1.1 (equations E-6 to E-8) and H.1 (Maxshift). Magnitudes are kept doubled: a
/// coefficient that becomes significant in bit-plane p gets 3 &lt;&lt; p (the midpoint of its interval) and each refinement bit moves
/// it by 1 &lt;&lt; p, so halving with truncation at the end gives E-7 when every plane was decoded and E-8 with r = 1/2 when the
/// code-block was truncated; the irreversible path multiplies the doubled value by half the step size (E-6, r = 1/2). That needs
/// Mb + s + 1 &lt; 31.
/// </para>
/// <para>
/// Code-block styles (Table A.19): selective bypass (raw significance and refinement passes from the eleventh pass, D.6), reset of the
/// context probabilities after each pass (D.3), termination on each pass and the codeword segments it makes (D.4), vertically causal
/// contexts (D.7), predictable termination (nothing to do in a decoder) and segmentation symbols (D.5, checked). Codeword segments
/// restart the arithmetic or raw decoder on their own bytes; the contexts carry over (D.4.1).
/// </para>
/// <para>Allocation-free per code-block: one instance per decode call reuses its pooled arrays until <see cref="Dispose"/>.</para>
/// </remarks>
internal sealed class JpxCodeBlockDecoder : IDisposable
{
    private const int MaxBlockSamples = 4096;
    private const int MaxPaddedSamples = (1024 + 2) * (4 + 2);

    private const byte Significant = 1;
    private const byte Negative = 2;
    private const byte Visited = 4;
    private const byte Refined = 8;

    private const int SignContexts = 9;
    private const int RefinementContexts = 14;
    private const int RunLengthContext = 17;
    private const int UniformContext = 18;
    private const int ContextCount = 19;

    // Table D.1: zero-coding context by orientation (LL, HL, LH, HH), h (0-2), v (0-2), d (0-4).
    private static readonly byte[] ZeroCoding = BuildZeroCoding();

    // Tables D.2 and D.3: (context label - 9) << 1 | XOR bit, by (H + 1) * 3 + (V + 1).
    private static readonly byte[] SignCoding = [(4 << 1) | 1, (3 << 1) | 1, (2 << 1) | 1, (1 << 1) | 1, 0, 1 << 1, 2 << 1, 3 << 1, 4 << 1];

    private int[] _magnitudes = ArrayPool<int>.Shared.Rent(MaxBlockSamples);
    private byte[] _flags = ArrayPool<byte>.Shared.Rent(MaxPaddedSamples);
    private readonly byte[] _contexts = new byte[ContextCount];
    private int _width;
    private int _height;
    private int _stride;
    private bool _causal;

    /// <summary>
    /// Decodes the coding passes of a code-block from its codeword <paramref name="segments"/> (slices of <paramref name="data"/>) and
    /// writes the reconstructed coefficients into <paramref name="output"/> (row pitch <paramref name="pitch"/>): integers for the
    /// reversible path, the bits of <see cref="float"/> values for the irreversible one.
    /// </summary>
    /// <returns><see langword="false"/> when a segmentation symbol was not 1010 (D.5).</returns>
    public bool Decode(ReadOnlySpan<byte> data, ReadOnlySpan<JpxSegment> segments, in JpxBlockCoding coding, Span<int> output, int pitch)
    {
        int width = coding.Width;
        int height = coding.Height;
        int style = coding.Style;
        _width = width;
        _height = height;
        _stride = width + 2;
        _causal = (style & JpxComponentStyle.VerticallyCausal) != 0;
        _magnitudes.AsSpan(0, width * height).Clear();
        _flags.AsSpan(0, _stride * (height + 2)).Clear();
        ResetContexts();

        int total = 0;
        foreach (JpxSegment segment in segments)
        {
            total += segment.Passes;
        }

        total = Math.Min(total, (3 * coding.TopPlane) + 1);
        bool bypass = (style & JpxComponentStyle.Bypass) != 0;
        bool symbolsOk = true;
        int plane = coding.TopPlane;
        int kind = 2;
        int pass = 0;
        foreach (JpxSegment segment in segments)
        {
            ReadOnlySpan<byte> bytes = data.Slice(segment.Start, segment.Length);
            var mq = new MqDecoder(bytes);
            var raw = new JpxRawDecoder(bytes);
            for (int i = 0; i < segment.Passes && pass < total; i++, pass++)
            {
                bool rawPass = bypass && pass >= 10 && kind != 2;
                switch (kind)
                {
                    case 0 when rawPass:
                        SignificancePassRaw(ref raw, plane, coding.Orientation);
                        break;
                    case 0:
                        SignificancePass(ref mq, plane, coding.Orientation);
                        break;
                    case 1 when rawPass:
                        RefinementPassRaw(ref raw, plane);
                        break;
                    case 1:
                        RefinementPass(ref mq, plane);
                        break;
                    default:
                        CleanupPass(ref mq, plane, coding.Orientation);
                        if ((style & JpxComponentStyle.SegmentationSymbols) != 0)
                        {
                            int symbol = 0;
                            for (int b = 0; b < 4; b++)
                            {
                                symbol = (symbol << 1) | mq.Decode(ref _contexts[UniformContext]);
                            }

                            symbolsOk &= symbol == 0b1010;
                        }

                        break;
                }

                if ((style & JpxComponentStyle.ResetContexts) != 0)
                {
                    ResetContexts();
                }

                if (kind == 2)
                {
                    kind = 0;
                    plane--;
                }
                else
                {
                    kind++;
                }
            }
        }

        WriteOutput(coding, output, pitch);
        return symbolsOk;
    }

    /// <summary>Returns the pooled scratch arrays.</summary>
    public void Dispose()
    {
        ArrayPool<int>.Shared.Return(_magnitudes);
        ArrayPool<byte>.Shared.Return(_flags);
        _magnitudes = [];
        _flags = [];
    }

    private static byte[] BuildZeroCoding()
    {
        byte[] table = new byte[4 * 3 * 3 * 5];
        for (int orientation = 0; orientation < 4; orientation++)
        {
            for (int h = 0; h < 3; h++)
            {
                for (int v = 0; v < 3; v++)
                {
                    for (int d = 0; d < 5; d++)
                    {
                        table[(((((orientation * 3) + h) * 3) + v) * 5) + d] = (byte)(orientation switch
                        {
                            1 => LowContext(v, h, d),
                            3 => HighContext(h + v, d),
                            _ => LowContext(h, v, d),
                        });
                    }
                }
            }
        }

        return table;

        static int LowContext(int h, int v, int d) => h switch
        {
            2 => 8,
            1 => v >= 1 ? 7 : d >= 1 ? 6 : 5,
            _ => v == 2 ? 4 : v == 1 ? 3 : d >= 2 ? 2 : d,
        };

        static int HighContext(int hv, int d) => d switch
        {
            >= 3 => 8,
            2 => hv >= 1 ? 7 : 6,
            1 => hv >= 2 ? 5 : hv == 1 ? 4 : 3,
            _ => hv >= 2 ? 2 : hv,
        };
    }

    /// <summary>
    /// H.1 (H-1) then E.1.1: down-shifts the region-of-interest coefficients (doubled magnitude of at least 2^(s+1), i.e. |q| at least
    /// 2^s), then writes q (reversible) or its reconstruction (irreversible) at each sample.
    /// </summary>
    private void WriteOutput(in JpxBlockCoding coding, Span<int> output, int pitch)
    {
        int roi = coding.RoiShift;
        int threshold = roi > 0 ? 1 << (roi + 1) : int.MaxValue;
        float scale = coding.Scale;
        for (int y = 0; y < _height; y++)
        {
            Span<int> row = output.Slice(y * pitch, _width);
            int flagRow = ((y + 1) * _stride) + 1;
            for (int x = 0; x < _width; x++)
            {
                int doubled = _magnitudes[(y * _width) + x];
                if (doubled >= threshold)
                {
                    doubled >>= roi;
                }

                bool negative = (_flags[flagRow + x] & Negative) != 0;
                if (scale == 0)
                {
                    int magnitude = doubled / 2;
                    row[x] = negative ? -magnitude : magnitude;
                }
                else
                {
                    float value = doubled * scale;
                    row[x] = BitConverter.SingleToInt32Bits(negative ? -value : value);
                }
            }
        }
    }

    /// <summary>Table D.7: every context starts at state 0, MPS 0, except zero-coding label 0, run-length and uniform.</summary>
    private void ResetContexts()
    {
        _contexts.AsSpan().Clear();
        _contexts[0] = MqDecoder.Context(4);
        _contexts[RunLengthContext] = MqDecoder.Context(3);
        _contexts[UniformContext] = MqDecoder.Context(46);
    }

    /// <summary>The zero-coding context (Table D.1); <paramref name="causal"/> ignores the row below (D.7).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ZeroContext(int index, int orientation, bool causal)
    {
        byte[] f = _flags;
        int s = _stride;
        int h = (f[index - 1] & Significant) + (f[index + 1] & Significant);
        int v = f[index - s] & Significant;
        int d = (f[index - s - 1] & Significant) + (f[index - s + 1] & Significant);
        if (!causal)
        {
            v += f[index + s] & Significant;
            d += (f[index + s - 1] & Significant) + (f[index + s + 1] & Significant);
        }

        return ZeroCoding[(((((orientation * 3) + h) * 3) + v) * 5) + d];
    }

    /// <summary>Whether sample row <paramref name="y"/> is the last of its stripe under vertically causal contexts (D.7).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Causal(int y) => _causal && (y & 3) == 3;

    private int SignContext(int index, bool causal)
    {
        byte[] f = _flags;
        int h = Math.Clamp(Contribution(f[index - 1]) + Contribution(f[index + 1]), -1, 1);
        int v = Math.Clamp(Contribution(f[index - _stride]) + (causal ? 0 : Contribution(f[index + _stride])), -1, 1);
        return SignCoding[((h + 1) * 3) + v + 1];

        static int Contribution(byte flags) => (flags & Significant) == 0 ? 0 : (flags & Negative) == 0 ? 1 : -1;
    }

    private void BecomeSignificant(ref MqDecoder mq, int index, int sample, int plane, bool causal)
    {
        int entry = SignContext(index, causal);
        int sign = mq.Decode(ref _contexts[SignContexts + (entry >> 1)]) ^ (entry & 1);
        _flags[index] |= sign == 1 ? (byte)(Significant | Negative) : Significant;
        _magnitudes[sample] = 3 << plane;
    }

    /// <summary>D.3.1: insignificant coefficients with a significant neighbour.</summary>
    private void SignificancePass(ref MqDecoder mq, int plane, int orientation)
    {
        for (int y0 = 0; y0 < _height; y0 += 4)
        {
            int y1 = Math.Min(y0 + 4, _height);
            for (int x = 0; x < _width; x++)
            {
                for (int y = y0; y < y1; y++)
                {
                    int index = ((y + 1) * _stride) + x + 1;
                    if ((_flags[index] & Significant) != 0)
                    {
                        continue;
                    }

                    bool causal = Causal(y);
                    int context = ZeroContext(index, orientation, causal);
                    if (context == 0)
                    {
                        continue;
                    }

                    _flags[index] |= Visited;
                    if (mq.Decode(ref _contexts[context]) == 1)
                    {
                        BecomeSignificant(ref mq, index, (y * _width) + x, plane, causal);
                    }
                }
            }
        }
    }

    /// <summary>D.3.1 and D.6: the significance pass in the bypass mode, its bits and signs read raw.</summary>
    private void SignificancePassRaw(ref JpxRawDecoder raw, int plane, int orientation)
    {
        for (int y0 = 0; y0 < _height; y0 += 4)
        {
            int y1 = Math.Min(y0 + 4, _height);
            for (int x = 0; x < _width; x++)
            {
                for (int y = y0; y < y1; y++)
                {
                    int index = ((y + 1) * _stride) + x + 1;
                    if ((_flags[index] & Significant) != 0 || ZeroContext(index, orientation, Causal(y)) == 0)
                    {
                        continue;
                    }

                    _flags[index] |= Visited;
                    if (raw.Decode() == 1)
                    {
                        _flags[index] |= raw.Decode() == 1 ? (byte)(Significant | Negative) : Significant;
                        _magnitudes[(y * _width) + x] = 3 << plane;
                    }
                }
            }
        }
    }

    /// <summary>The magnitude refinement context (Table D.4); <paramref name="causal"/> ignores the row below (D.7).</summary>
    private int RefinementContext(int index, byte flags, bool causal)
    {
        if ((flags & Refined) != 0)
        {
            return RefinementContexts + 2;
        }

        byte[] f = _flags;
        int s = _stride;
        int neighbours = f[index - 1] | f[index + 1] | f[index - s] | f[index - s - 1] | f[index - s + 1];
        if (!causal)
        {
            neighbours |= f[index + s] | f[index + s - 1] | f[index + s + 1];
        }

        return RefinementContexts + (neighbours & Significant);
    }

    /// <summary>D.3.3: significant coefficients not just coded in the significance pass.</summary>
    private void RefinementPass(ref MqDecoder mq, int plane)
    {
        byte[] f = _flags;
        int s = _stride;
        for (int y0 = 0; y0 < _height; y0 += 4)
        {
            int y1 = Math.Min(y0 + 4, _height);
            for (int x = 0; x < _width; x++)
            {
                for (int y = y0; y < y1; y++)
                {
                    int index = ((y + 1) * s) + x + 1;
                    byte flags = f[index];
                    if ((flags & (Significant | Visited)) != Significant)
                    {
                        continue;
                    }

                    int context = RefinementContext(index, flags, Causal(y));
                    int sample = (y * _width) + x;
                    _magnitudes[sample] += mq.Decode(ref _contexts[context]) == 1 ? 1 << plane : -(1 << plane);
                    f[index] = (byte)(flags | Refined);
                }
            }
        }
    }

    /// <summary>D.3.3 and D.6: the refinement pass in the bypass mode, its bits read raw.</summary>
    private void RefinementPassRaw(ref JpxRawDecoder raw, int plane)
    {
        byte[] f = _flags;
        int s = _stride;
        for (int y0 = 0; y0 < _height; y0 += 4)
        {
            int y1 = Math.Min(y0 + 4, _height);
            for (int x = 0; x < _width; x++)
            {
                for (int y = y0; y < y1; y++)
                {
                    int index = ((y + 1) * s) + x + 1;
                    byte flags = f[index];
                    if ((flags & (Significant | Visited)) != Significant)
                    {
                        continue;
                    }

                    int sample = (y * _width) + x;
                    _magnitudes[sample] += raw.Decode() == 1 ? 1 << plane : -(1 << plane);
                    f[index] = (byte)(flags | Refined);
                }
            }
        }
    }

    /// <summary>D.3.4: the remaining coefficients, with run-length coding of all-insignificant stripe columns.</summary>
    private void CleanupPass(ref MqDecoder mq, int plane, int orientation)
    {
        byte[] f = _flags;
        int s = _stride;
        for (int y0 = 0; y0 < _height; y0 += 4)
        {
            int y1 = Math.Min(y0 + 4, _height);
            for (int x = 0; x < _width; x++)
            {
                int y = y0;
                int top = ((y0 + 1) * s) + x + 1;
                if (y1 - y0 == 4
                    && (f[top] | f[top + s] | f[top + (2 * s)] | f[top + (3 * s)]) == 0
                    && ZeroContext(top, orientation, false) == 0
                    && ZeroContext(top + s, orientation, false) == 0
                    && ZeroContext(top + (2 * s), orientation, false) == 0
                    && ZeroContext(top + (3 * s), orientation, _causal) == 0)
                {
                    if (mq.Decode(ref _contexts[RunLengthContext]) == 0)
                    {
                        continue;
                    }

                    int run = mq.Decode(ref _contexts[UniformContext]) << 1;
                    run |= mq.Decode(ref _contexts[UniformContext]);
                    y = y0 + run;
                    BecomeSignificant(ref mq, top + (run * s), (y * _width) + x, plane, Causal(y));
                    y++;
                }

                for (; y < y1; y++)
                {
                    int index = ((y + 1) * s) + x + 1;
                    bool causal = Causal(y);
                    if ((f[index] & (Significant | Visited)) == 0 && mq.Decode(ref _contexts[ZeroContext(index, orientation, causal)]) == 1)
                    {
                        BecomeSignificant(ref mq, index, (y * _width) + x, plane, causal);
                    }
                }
            }
        }

        for (int y = 0; y < _height; y++)
        {
            Span<byte> row = f.AsSpan(((y + 1) * s) + 1, _width);
            for (int x = 0; x < row.Length; x++)
            {
                row[x] &= unchecked((byte)~Visited);
            }
        }
    }
}

/// <summary>Reads the raw bits of a bypass codeword segment, MSB first, skipping the stuffed bit after a 0xFF byte (D.6).</summary>
/// <remarks>ITU-T T.800 D.6; past the end, or at a marker (0xFF followed by a byte above 0x8F), it returns 1-bits as OpenJPEG does.</remarks>
internal ref struct JpxRawDecoder
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;
    private int _byte;
    private int _bits;

    public JpxRawDecoder(ReadOnlySpan<byte> data) => _data = data;

    /// <summary>Reads one bit.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Decode()
    {
        if (_bits == 0)
        {
            if (_byte == 0xFF)
            {
                if (_position < _data.Length && _data[_position] <= 0x8F)
                {
                    _byte = _data[_position++];
                    _bits = 7;
                }
                else
                {
                    _byte = 0xFF;
                    _bits = 8;
                }
            }
            else
            {
                _byte = _position < _data.Length ? _data[_position++] : 0xFF;
                _bits = 8;
            }
        }

        _bits--;
        return (_byte >> _bits) & 1;
    }
}
