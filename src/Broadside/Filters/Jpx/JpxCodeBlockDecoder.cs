using System.Buffers;
using System.Runtime.CompilerServices;
using Broadside.Filters.Codecs;

namespace Broadside.Filters.Jpx;

/// <summary>
/// Tier-1 decoding of one code-block: the significance propagation, magnitude refinement and cleanup passes of EBCOT driven by the
/// MQ decoder, then the reversible reconstruction of the quantization indices.
/// </summary>
/// <remarks>
/// <para>
/// ITU-T T.800 Annex D (D.1 to D.4, Tables D.1 to D.7) and E.1.1.2 (equations E-7, E-8). Magnitudes are kept doubled: a coefficient
/// that becomes significant in bit-plane p gets 3 &lt;&lt; p (the midpoint of its interval) and each refinement bit moves it by
/// 1 &lt;&lt; p, so halving with truncation at the end gives E-7 when every plane was decoded and E-8 with r = 1/2 when the
/// code-block was truncated. That needs Mb + 1 &lt; 31.
/// </para>
/// <para>
/// Code-block styles: "reset context probabilities" and "segmentation symbols" are decoded here; "predictable termination" needs
/// nothing from a decoder. Bypass, termination on each pass and vertically causal contexts are refused before decoding starts.
/// Allocation-free per code-block: one instance per decode call reuses its pooled arrays until <see cref="Dispose"/>.
/// </para>
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

    /// <summary>
    /// Decodes <paramref name="passes"/> coding passes of a <paramref name="width"/> x <paramref name="height"/> code-block from
    /// <paramref name="data"/> and writes the reconstructed coefficients into <paramref name="output"/> (row pitch <paramref name="pitch"/>).
    /// </summary>
    /// <param name="data">The code-block's codeword segment.</param>
    /// <param name="width">The code-block width.</param>
    /// <param name="height">The code-block height.</param>
    /// <param name="orientation">The sub-band: 0 LL, 1 HL, 2 LH, 3 HH.</param>
    /// <param name="style">The code-block style bits (Table A.19).</param>
    /// <param name="topPlane">The bit-plane of the first cleanup pass, Mb - 1 - P.</param>
    /// <param name="passes">The number of coding passes received.</param>
    /// <param name="output">The code-block's first sample in the tile-component buffer.</param>
    /// <param name="pitch">The buffer's row pitch.</param>
    public void Decode(ReadOnlySpan<byte> data, int width, int height, int orientation, int style, int topPlane, int passes, Span<int> output, int pitch)
    {
        _width = width;
        _height = height;
        _stride = width + 2;
        _magnitudes.AsSpan(0, width * height).Clear();
        _flags.AsSpan(0, _stride * (height + 2)).Clear();
        ResetContexts();
        passes = Math.Min(passes, (3 * topPlane) + 1);

        var mq = new MqDecoder(data);
        int plane = topPlane;
        int kind = 2;
        for (int pass = 0; pass < passes; pass++)
        {
            switch (kind)
            {
                case 0:
                    SignificancePass(ref mq, plane, orientation);
                    break;
                case 1:
                    RefinementPass(ref mq, plane);
                    break;
                default:
                    CleanupPass(ref mq, plane, orientation);
                    if ((style & JpxComponentStyle.SegmentationSymbols) != 0)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            mq.Decode(ref _contexts[UniformContext]);
                        }
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

        for (int y = 0; y < height; y++)
        {
            Span<int> row = output.Slice(y * pitch, width);
            int flagRow = ((y + 1) * _stride) + 1;
            for (int x = 0; x < width; x++)
            {
                int magnitude = _magnitudes[(y * width) + x] / 2;
                row[x] = (_flags[flagRow + x] & Negative) != 0 ? -magnitude : magnitude;
            }
        }
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

    /// <summary>Table D.7: every context starts at state 0, MPS 0, except zero-coding label 0, run-length and uniform.</summary>
    private void ResetContexts()
    {
        _contexts.AsSpan().Clear();
        _contexts[0] = MqDecoder.Context(4);
        _contexts[RunLengthContext] = MqDecoder.Context(3);
        _contexts[UniformContext] = MqDecoder.Context(46);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ZeroContext(int index, int orientation)
    {
        byte[] f = _flags;
        int s = _stride;
        int h = (f[index - 1] & Significant) + (f[index + 1] & Significant);
        int v = (f[index - s] & Significant) + (f[index + s] & Significant);
        int d = (f[index - s - 1] & Significant) + (f[index - s + 1] & Significant) + (f[index + s - 1] & Significant) + (f[index + s + 1] & Significant);
        return ZeroCoding[(((((orientation * 3) + h) * 3) + v) * 5) + d];
    }

    private int DecodeSign(ref MqDecoder mq, int index)
    {
        byte[] f = _flags;
        int h = Math.Clamp(Contribution(f[index - 1]) + Contribution(f[index + 1]), -1, 1);
        int v = Math.Clamp(Contribution(f[index - _stride]) + Contribution(f[index + _stride]), -1, 1);
        int entry = SignCoding[((h + 1) * 3) + v + 1];
        return mq.Decode(ref _contexts[SignContexts + (entry >> 1)]) ^ (entry & 1);

        static int Contribution(byte flags) => (flags & Significant) == 0 ? 0 : (flags & Negative) == 0 ? 1 : -1;
    }

    private void BecomeSignificant(ref MqDecoder mq, int index, int sample, int plane)
    {
        _flags[index] |= DecodeSign(ref mq, index) == 1 ? (byte)(Significant | Negative) : Significant;
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

                    int context = ZeroContext(index, orientation);
                    if (context == 0)
                    {
                        continue;
                    }

                    _flags[index] |= Visited;
                    if (mq.Decode(ref _contexts[context]) == 1)
                    {
                        BecomeSignificant(ref mq, index, (y * _width) + x, plane);
                    }
                }
            }
        }
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

                    int context;
                    if ((flags & Refined) != 0)
                    {
                        context = RefinementContexts + 2;
                    }
                    else
                    {
                        int neighbours = (f[index - 1] | f[index + 1] | f[index - s] | f[index + s] | f[index - s - 1] | f[index - s + 1] | f[index + s - 1] | f[index + s + 1]) & Significant;
                        context = RefinementContexts + neighbours;
                    }

                    int sample = (y * _width) + x;
                    _magnitudes[sample] += mq.Decode(ref _contexts[context]) == 1 ? 1 << plane : -(1 << plane);
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
                    && ZeroContext(top, orientation) == 0
                    && ZeroContext(top + s, orientation) == 0
                    && ZeroContext(top + (2 * s), orientation) == 0
                    && ZeroContext(top + (3 * s), orientation) == 0)
                {
                    if (mq.Decode(ref _contexts[RunLengthContext]) == 0)
                    {
                        continue;
                    }

                    int run = mq.Decode(ref _contexts[UniformContext]) << 1;
                    run |= mq.Decode(ref _contexts[UniformContext]);
                    y = y0 + run;
                    BecomeSignificant(ref mq, top + (run * s), (y * _width) + x, plane);
                    y++;
                }

                for (; y < y1; y++)
                {
                    int index = ((y + 1) * s) + x + 1;
                    if ((f[index] & (Significant | Visited)) == 0 && mq.Decode(ref _contexts[ZeroContext(index, orientation)]) == 1)
                    {
                        BecomeSignificant(ref mq, index, (y * _width) + x, plane);
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
