using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
/// <para>
/// State is one 32-bit word per stripe column (four samples of a column in one stripe, D.1 and Figure D.1), with a border of words
/// around the code-block so neighbors need no bounds tests. Each word carries the significance of the 3 x 6 neighborhood the
/// column's four samples see (rows -1 to 4, columns -1 to +1: bit 3 * (row + 1) + column + 1), the signs of rows -1 to 4 of its own
/// column, and whether each of its four samples was refined or coded in this plane's significance pass. A sample that becomes
/// significant sets its bit in its own word and in the up to five neighboring words that see it, so the context of any sample is a
/// table lookup on nine bits of one word (Tables D.1 to D.4) and a column with nothing to code is skipped on one test. Under vertically
/// causal contexts (D.7) a sample of a stripe's first row does not tell the stripe above, which is the same as treating it as
/// insignificant there. The MQ registers stay in locals across a pass (<see cref="MqDecoder.Decode(ref byte, ReadOnlySpan{byte}, ref uint, ref uint, ref int, ref int)"/>).
/// </para>
/// <para>Allocation-free per code-block: one instance per decode call reuses its pooled arrays until <see cref="Dispose"/>.</para>
/// </remarks>
internal sealed class JpxCodeBlockDecoder : IDisposable
{
    // Table A.18: code-block exponents 2 to 10 and their sum at most 12, so at most 4,096 samples and 1,024 on a side.
    private const int MaxBlockSamples = 4096;
    private const int MaxBlockSide = 1024;

    // (stripes + 2) * (width + 2) words; the largest for exponents within Table A.18 is a 1,024 x 4 block: 3 * 1,026. Clipped
    // blocks of other shapes grow the arrays in Decode.
    private const int MaxStateWords = 3 * (MaxBlockSide + 2);

    // Magnitudes by stripe column, four per column: width * 4 * stripes.
    private const int MaxMagnitudes = MaxBlockSamples;

    // The significance of the 3 x 6 neighborhood (rows -1 to 4 of columns -1, 0 and +1), and of the column's own four samples.
    private const uint SigmaAll = 0x3FFFF;
    private const uint SelfAll = (1u << 4) | (1u << 7) | (1u << 10) | (1u << 13);

    // The 3 x 3 neighborhood of row r after shifting the word right by 3r, without the sample itself.
    private const uint Neighbors = 0x1EF;

    // Sign (negative) of rows -1 to 4 of the word's own column: bit ChiShift + row + 1.
    private const int ChiShift = 18;

    // Refined at least once (Table D.4), rows 0 to 3.
    private const int MuShift = 24;

    // Coded in this bit-plane's significance pass, rows 0 to 3; cleared by the cleanup pass.
    private const int PiShift = 28;
    private const uint PiAll = 0xFu << PiShift;

    private const int SignContexts = 9;
    private const int RefinementContexts = 14;
    private const int RunLengthContext = 17;
    private const int UniformContext = 18;
    private const int ContextCount = 19;

    // Table D.1: zero-coding context by orientation (LL, HL, LH, HH) and the nine neighborhood bits of a sample (NW, N, NE, W, self,
    // E, SW, S, SE from bit 0).
    private static readonly byte[] ZeroCoding = BuildZeroCoding();

    private int[] _magnitudes = ArrayPool<int>.Shared.Rent(MaxMagnitudes);
    private uint[] _state = ArrayPool<uint>.Shared.Rent(MaxStateWords);
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
        if ((uint)width > MaxBlockSide || (uint)height > MaxBlockSide || width * height > MaxBlockSamples
            || (uint)coding.Orientation > 3)
        {
            // The codestream reader never makes such a block (Table A.18).
            throw new ArgumentOutOfRangeException(nameof(coding));
        }

        // The passes index the state and the magnitudes without bounds checks, so both always hold the whole block and its border.
        int stripes = (height + 3) >> 2;
        int words = (width + 2) * (stripes + 2);
        if (_state.Length < words)
        {
            ArrayPool<uint>.Shared.Return(_state);
            _state = ArrayPool<uint>.Shared.Rent(words);
        }

        if (_magnitudes.Length < stripes * 4 * width)
        {
            ArrayPool<int>.Shared.Return(_magnitudes);
            _magnitudes = ArrayPool<int>.Shared.Rent(stripes * 4 * width);
        }

        _width = width;
        _height = height;
        _stride = width + 2;
        _causal = (style & JpxComponentStyle.VerticallyCausal) != 0;
        // Magnitudes are written when a sample becomes significant and read only for significant ones, so they need no clearing.
        _state.AsSpan(0, words).Clear();
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
                            var coder = new Coder(mq);
                            int symbol = 0;
                            for (int b = 0; b < 4; b++)
                            {
                                symbol = (symbol << 1) | coder.Decode(ref _contexts[UniformContext]);
                            }

                            coder.Store(ref mq);
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
        ArrayPool<uint>.Shared.Return(_state);
        _magnitudes = [];
        _state = [];
    }

    private static byte[] BuildZeroCoding()
    {
        byte[] table = new byte[4 * 512];
        for (int orientation = 0; orientation < 4; orientation++)
        {
            for (int bits = 0; bits < 512; bits++)
            {
                int h = ((bits >> 3) & 1) + ((bits >> 5) & 1);
                int v = ((bits >> 1) & 1) + ((bits >> 7) & 1);
                int d = (bits & 1) + ((bits >> 2) & 1) + ((bits >> 6) & 1) + ((bits >> 8) & 1);
                table[(orientation * 512) + bits] = (byte)(orientation switch
                {
                    1 => LowContext(v, h, d),
                    3 => HighContext(h + v, d),
                    _ => LowContext(h, v, d),
                });
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
        int width = _width;
        int height = _height;
        if ((uint)pitch < (uint)width || output.Length < ((height - 1) * pitch) + width)
        {
            throw new ArgumentOutOfRangeException(nameof(output));
        }

        int roi = coding.RoiShift;
        int threshold = roi > 0 ? 1 << (roi + 1) : int.MaxValue;
        float scale = coding.Scale;
        ref uint column = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_state), _stride + 1);
        ref int magnitude = ref MemoryMarshal.GetArrayDataReference(_magnitudes);
        ref int target = ref MemoryMarshal.GetReference(output);
        for (int y0 = 0; y0 < height; y0 += 4)
        {
            int rows = Math.Min(4, height - y0);
            ref int top = ref Unsafe.Add(ref target, y0 * pitch);
            for (int x = 0; x < width; x++, magnitude = ref Unsafe.Add(ref magnitude, 4))
            {
                uint word = Unsafe.Add(ref column, x);
                ref int sample = ref Unsafe.Add(ref top, x);
                for (int r = 0; r < rows; r++, sample = ref Unsafe.Add(ref sample, pitch))
                {
                    int doubled = (word & (1u << ((3 * r) + 4))) != 0 ? Unsafe.Add(ref magnitude, r) : 0;
                    if (doubled >= threshold)
                    {
                        doubled >>= roi;
                    }

                    int negative = -(int)((word >> (ChiShift + r + 1)) & 1);
                    sample = scale == 0
                        ? ((doubled >> 1) ^ negative) - negative
                        : BitConverter.SingleToInt32Bits(doubled * scale) ^ (negative & int.MinValue);
                }
            }

            column = ref Unsafe.Add(ref column, _stride);
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

    /// <summary>
    /// Tables D.2 and D.3: (context label - 9) &lt;&lt; 1 | XOR bit, by the significance and sign of W (bits 0 and 1), E (2 and 3),
    /// N (4 and 5) and S (6 and 7).
    /// </summary>
    private static ReadOnlySpan<byte> SignCoding =>
    [
        0, 6, 0, 7, 6, 6, 6, 0, 0, 6, 0, 7, 7, 0, 7, 7, 2, 8, 2, 5, 8, 8, 8, 2, 2, 8, 2, 5, 5, 2, 5, 5,
        0, 6, 0, 7, 6, 6, 6, 0, 0, 6, 0, 7, 7, 0, 7, 7, 3, 4, 3, 9, 4, 4, 4, 3, 3, 4, 3, 9, 9, 3, 9, 9,
        2, 8, 2, 5, 8, 8, 8, 2, 2, 8, 2, 5, 5, 2, 5, 5, 2, 8, 2, 5, 8, 8, 8, 2, 2, 8, 2, 5, 5, 2, 5, 5,
        2, 8, 2, 5, 8, 8, 8, 2, 2, 8, 2, 5, 5, 2, 5, 5, 0, 6, 0, 7, 6, 6, 6, 0, 0, 6, 0, 7, 7, 0, 7, 7,
        0, 6, 0, 7, 6, 6, 6, 0, 0, 6, 0, 7, 7, 0, 7, 7, 2, 8, 2, 5, 8, 8, 8, 2, 2, 8, 2, 5, 5, 2, 5, 5,
        0, 6, 0, 7, 6, 6, 6, 0, 0, 6, 0, 7, 7, 0, 7, 7, 3, 4, 3, 9, 4, 4, 4, 3, 3, 4, 3, 9, 9, 3, 9, 9,
        3, 4, 3, 9, 4, 4, 4, 3, 3, 4, 3, 9, 9, 3, 9, 9, 0, 6, 0, 7, 6, 6, 6, 0, 0, 6, 0, 7, 7, 0, 7, 7,
        3, 4, 3, 9, 4, 4, 4, 3, 3, 4, 3, 9, 9, 3, 9, 9, 3, 4, 3, 9, 4, 4, 4, 3, 3, 4, 3, 9, 9, 3, 9, 9,
    ];

    /// <summary>
    /// The sign context and XOR bit (Tables D.2 and D.3) of row <paramref name="r"/> of the column whose word is <paramref name="column"/>:
    /// W and E from the significance bits of the word and the sign bits of the neighboring columns' words, N and S from the word.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SignEntry(ref uint column, uint word, int r)
    {
        int shift = 3 * r;
        uint west = Unsafe.Add(ref column, -1) >> (ChiShift + r + 1);
        uint east = Unsafe.Add(ref column, 1) >> (ChiShift + r + 1);
        uint bits = ((word >> (shift + 3)) & 1)
            | ((west & 1) << 1)
            | (((word >> (shift + 5)) & 1) << 2)
            | ((east & 1) << 3)
            | (((word >> (shift + 1)) & 1) << 4)
            | (((word >> (ChiShift + r)) & 1) << 5)
            | (((word >> (shift + 7)) & 1) << 6)
            | (((word >> (ChiShift + r + 2)) & 1) << 7);
        return Unsafe.Add(ref MemoryMarshal.GetReference(SignCoding), (int)bits);
    }

    /// <summary>
    /// Makes row <paramref name="r"/> of a column significant: its own bits go into <paramref name="word"/> (the caller's copy of the
    /// column's word), the neighborhood bits into the words of the columns beside it and, for the first and last row, of the stripes
    /// above and below (not above under vertically causal contexts, D.7).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void MarkSignificant(ref uint column, ref uint word, int r, bool negative, int stride, bool causal)
    {
        int shift = 3 * r;
        uint sign = negative ? 1u : 0u;
        word |= (1u << (shift + 4)) | (sign << (ChiShift + r + 1));
        Unsafe.Add(ref column, -1) |= 1u << (shift + 5);
        Unsafe.Add(ref column, 1) |= 1u << (shift + 3);
        if (r == 0)
        {
            if (!causal)
            {
                ref uint above = ref Unsafe.Subtract(ref column, stride);
                Unsafe.Add(ref above, -1) |= 1u << 17;
                above |= (1u << 16) | (sign << (ChiShift + 5));
                Unsafe.Add(ref above, 1) |= 1u << 15;
            }
        }
        else if (r == 3)
        {
            ref uint below = ref Unsafe.Add(ref column, stride);
            Unsafe.Add(ref below, -1) |= 1u << 2;
            below |= (1u << 1) | (sign << ChiShift);
            Unsafe.Add(ref below, 1) |= 1u;
        }
    }

    /// <summary>D.3.1: insignificant coefficients with a significant neighbor.</summary>
    private void SignificancePass(ref MqDecoder mq, int plane, int orientation)
    {
        ref uint column = ref MemoryMarshal.GetArrayDataReference(_state);
        ref int magnitude = ref MemoryMarshal.GetArrayDataReference(_magnitudes);
        ref byte contexts = ref MemoryMarshal.GetArrayDataReference(_contexts);
        ref byte zeroCoding = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(ZeroCoding), orientation * 512);
        int half = 3 << plane;
        int stride = _stride;
        bool causal = _causal;
        var coder = new Coder(mq);
        column = ref Unsafe.Add(ref column, stride + 1);
        for (int y0 = 0; y0 < _height; y0 += 4)
        {
            int rows = Math.Min(4, _height - y0);
            ref uint end = ref Unsafe.Add(ref column, _width);
            for (; Unsafe.IsAddressLessThan(ref column, ref end); column = ref Unsafe.Add(ref column, 1), magnitude = ref Unsafe.Add(ref magnitude, 4))
            {
                uint word = column;
                if ((word & SigmaAll) == 0 || (word & SelfAll) == SelfAll)
                {
                    // Nothing significant around, or all four samples significant already.
                    continue;
                }

                if (rows == 4)
                {
                    SignificanceStep(ref coder, ref contexts, ref zeroCoding, ref column, ref word, ref magnitude, half, stride, causal, 0);
                    SignificanceStep(ref coder, ref contexts, ref zeroCoding, ref column, ref word, ref magnitude, half, stride, causal, 1);
                    SignificanceStep(ref coder, ref contexts, ref zeroCoding, ref column, ref word, ref magnitude, half, stride, causal, 2);
                    SignificanceStep(ref coder, ref contexts, ref zeroCoding, ref column, ref word, ref magnitude, half, stride, causal, 3);
                }
                else
                {
                    for (int r = 0; r < rows; r++)
                    {
                        SignificanceStep(ref coder, ref contexts, ref zeroCoding, ref column, ref word, ref magnitude, half, stride, causal, r);
                    }
                }

                column = word;
            }

            column = ref Unsafe.Add(ref column, 2);
        }

        coder.Store(ref mq);
    }

    /// <summary>Row <paramref name="r"/> of a column in the significance pass (D.3.1).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SignificanceStep(ref Coder coder, ref byte contexts, ref byte zeroCoding, ref uint column, ref uint word, ref int magnitude, int half, int stride, bool causal, int r)
    {
        // Insignificant with a significant neighbor (otherwise context 0, not coded), tested on one branch.
        uint around = word >> (3 * r);
        if (((around & (1u << 4)) == 0) & ((around & Neighbors) != 0))
        {
            word |= 1u << (PiShift + r);
            if (coder.Decode(ref Unsafe.Add(ref contexts, Unsafe.Add(ref zeroCoding, (int)(around & Neighbors)))) == 1)
            {
                DecodeSign(ref coder, ref contexts, ref column, ref word, stride, causal, r);
                Unsafe.Add(ref magnitude, r) = half;
            }
        }
    }

    /// <summary>Decodes the sign of row <paramref name="r"/> (D.3.2) and makes it significant.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DecodeSign(ref Coder coder, ref byte contexts, ref uint column, ref uint word, int stride, bool causal, int r)
    {
        int entry = SignEntry(ref column, word, r);
        int sign = coder.Decode(ref Unsafe.Add(ref contexts, SignContexts + (entry >> 1))) ^ (entry & 1);
        MarkSignificant(ref column, ref word, r, sign == 1, stride, causal);
    }

    /// <summary>D.3.1 and D.6: the significance pass in the bypass mode, its bits and signs read raw.</summary>
    private void SignificancePassRaw(ref JpxRawDecoder raw, int plane, int orientation)
    {
        ref uint column = ref MemoryMarshal.GetArrayDataReference(_state);
        ref int magnitude = ref MemoryMarshal.GetArrayDataReference(_magnitudes);
        int half = 3 << plane;
        column = ref Unsafe.Add(ref column, _stride + 1);
        for (int y0 = 0; y0 < _height; y0 += 4)
        {
            int rows = Math.Min(4, _height - y0);
            ref uint end = ref Unsafe.Add(ref column, _width);
            for (; Unsafe.IsAddressLessThan(ref column, ref end); column = ref Unsafe.Add(ref column, 1), magnitude = ref Unsafe.Add(ref magnitude, 4))
            {
                uint word = column;
                if ((word & SigmaAll) == 0)
                {
                    continue;
                }

                for (int r = 0; r < rows; r++)
                {
                    uint around = word >> (3 * r);
                    if ((around & (1u << 4)) != 0 || (around & Neighbors) == 0)
                    {
                        continue;
                    }

                    word |= 1u << (PiShift + r);
                    if (raw.Decode() == 1)
                    {
                        MarkSignificant(ref column, ref word, r, raw.Decode() == 1, _stride, _causal);
                        Unsafe.Add(ref magnitude, r) = half;
                    }
                }

                column = word;
            }

            column = ref Unsafe.Add(ref column, 2);
        }
    }

    /// <summary>D.3.3: significant coefficients not just coded in the significance pass.</summary>
    private void RefinementPass(ref MqDecoder mq, int plane)
    {
        ref uint column = ref MemoryMarshal.GetArrayDataReference(_state);
        ref int magnitude = ref MemoryMarshal.GetArrayDataReference(_magnitudes);
        ref byte contexts = ref MemoryMarshal.GetArrayDataReference(_contexts);
        int step = 1 << plane;
        var coder = new Coder(mq);
        column = ref Unsafe.Add(ref column, _stride + 1);
        for (int y0 = 0; y0 < _height; y0 += 4)
        {
            // Rows past the block's height are never significant, so a partial stripe needs no limit here.
            ref uint end = ref Unsafe.Add(ref column, _width);
            for (; Unsafe.IsAddressLessThan(ref column, ref end); column = ref Unsafe.Add(ref column, 1), magnitude = ref Unsafe.Add(ref magnitude, 4))
            {
                uint word = column;
                uint rows = SelfRows(word) & ~(word >> PiShift);
                if (rows == 0)
                {
                    continue;
                }

                do
                {
                    int r = BitOperations.TrailingZeroCount(rows);
                    rows &= rows - 1;

                    // Table D.4: 16 after the first refinement, else 15 with a significant neighbor and 14 without.
                    uint refined = (word >> (MuShift + r)) & 1;
                    uint neighbor = (((word >> (3 * r)) & Neighbors) + 0x1FF) >> 9;
                    int context = RefinementContexts + (int)((refined << 1) | (neighbor & ~refined));
                    int negative = coder.Decode(ref Unsafe.Add(ref contexts, context)) - 1;
                    Unsafe.Add(ref magnitude, r) += (step ^ negative) - negative;
                    word |= 1u << (MuShift + r);
                }
                while (rows != 0);

                column = word;
            }

            column = ref Unsafe.Add(ref column, 2);
        }

        coder.Store(ref mq);
    }

    /// <summary>The significance of a column's four samples as bits 0 to 3.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint SelfRows(uint word) =>
        ((word >> 4) & 1) | ((word >> 6) & 2) | ((word >> 8) & 4) | ((word >> 10) & 8);

    /// <summary>D.3.3 and D.6: the refinement pass in the bypass mode, its bits read raw.</summary>
    private void RefinementPassRaw(ref JpxRawDecoder raw, int plane)
    {
        ref uint column = ref MemoryMarshal.GetArrayDataReference(_state);
        ref int magnitude = ref MemoryMarshal.GetArrayDataReference(_magnitudes);
        int step = 1 << plane;
        column = ref Unsafe.Add(ref column, _stride + 1);
        for (int y0 = 0; y0 < _height; y0 += 4)
        {
            int rows = Math.Min(4, _height - y0);
            ref uint end = ref Unsafe.Add(ref column, _width);
            for (; Unsafe.IsAddressLessThan(ref column, ref end); column = ref Unsafe.Add(ref column, 1), magnitude = ref Unsafe.Add(ref magnitude, 4))
            {
                uint word = column;
                if ((word & SelfAll) == 0)
                {
                    continue;
                }

                for (int r = 0; r < rows; r++)
                {
                    if ((word & (1u << ((3 * r) + 4))) == 0 || (word & (1u << (PiShift + r))) != 0)
                    {
                        continue;
                    }

                    Unsafe.Add(ref magnitude, r) += raw.Decode() == 1 ? step : -step;
                    word |= 1u << (MuShift + r);
                }

                column = word;
            }

            column = ref Unsafe.Add(ref column, 2);
        }
    }

    /// <summary>D.3.4: the remaining coefficients, with run-length coding of all-insignificant stripe columns.</summary>
    private void CleanupPass(ref MqDecoder mq, int plane, int orientation)
    {
        ref uint column = ref MemoryMarshal.GetArrayDataReference(_state);
        ref int magnitude = ref MemoryMarshal.GetArrayDataReference(_magnitudes);
        ref byte contexts = ref MemoryMarshal.GetArrayDataReference(_contexts);
        ref byte zeroCoding = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(ZeroCoding), orientation * 512);
        int half = 3 << plane;
        int stride = _stride;
        bool causal = _causal;
        var coder = new Coder(mq);
        column = ref Unsafe.Add(ref column, stride + 1);
        for (int y0 = 0; y0 < _height; y0 += 4)
        {
            int rows = Math.Min(4, _height - y0);
            ref uint end = ref Unsafe.Add(ref column, _width);
            for (; Unsafe.IsAddressLessThan(ref column, ref end); column = ref Unsafe.Add(ref column, 1), magnitude = ref Unsafe.Add(ref magnitude, 4))
            {
                uint word = column;
                if (rows == 4)
                {
                    if ((word & (SigmaAll | PiAll)) == 0)
                    {
                        // D.3.4: all four samples insignificant with insignificant neighborhoods (so not coded in this plane's
                        // significance pass either): one run-length decision for the column.
                        if (coder.Decode(ref Unsafe.Add(ref contexts, RunLengthContext)) == 0)
                        {
                            continue;
                        }

                        int run = coder.Decode(ref Unsafe.Add(ref contexts, UniformContext)) << 1;
                        run |= coder.Decode(ref Unsafe.Add(ref contexts, UniformContext));
                        DecodeSign(ref coder, ref contexts, ref column, ref word, stride, causal, run);
                        Unsafe.Add(ref magnitude, run) = half;
                        for (int r = run + 1; r < 4; r++)
                        {
                            CleanupStep(ref coder, ref contexts, ref zeroCoding, ref column, ref word, ref magnitude, half, stride, causal, r);
                        }
                    }
                    else
                    {
                        CleanupStep(ref coder, ref contexts, ref zeroCoding, ref column, ref word, ref magnitude, half, stride, causal, 0);
                        CleanupStep(ref coder, ref contexts, ref zeroCoding, ref column, ref word, ref magnitude, half, stride, causal, 1);
                        CleanupStep(ref coder, ref contexts, ref zeroCoding, ref column, ref word, ref magnitude, half, stride, causal, 2);
                        CleanupStep(ref coder, ref contexts, ref zeroCoding, ref column, ref word, ref magnitude, half, stride, causal, 3);
                    }
                }
                else
                {
                    for (int r = 0; r < rows; r++)
                    {
                        CleanupStep(ref coder, ref contexts, ref zeroCoding, ref column, ref word, ref magnitude, half, stride, causal, r);
                    }
                }

                column = word & ~PiAll;
            }

            column = ref Unsafe.Add(ref column, 2);
        }

        coder.Store(ref mq);
    }

    /// <summary>Row <paramref name="r"/> of a column in the cleanup pass, outside a run (D.3.4).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CleanupStep(ref Coder coder, ref byte contexts, ref byte zeroCoding, ref uint column, ref uint word, ref int magnitude, int half, int stride, bool causal, int r)
    {
        uint around = word >> (3 * r);
        if ((word & ((1u << ((3 * r) + 4)) | (1u << (PiShift + r)))) != 0)
        {
            // Significant, or coded in this plane's significance pass.
            return;
        }

        if (coder.Decode(ref Unsafe.Add(ref contexts, Unsafe.Add(ref zeroCoding, (int)(around & Neighbors)))) == 1)
        {
            DecodeSign(ref coder, ref contexts, ref column, ref word, stride, causal, r);
            Unsafe.Add(ref magnitude, r) = half;
        }
    }

    /// <summary>The MQ decoder's registers and read position, held in locals for the length of a pass.</summary>
    private ref struct Coder
    {
        private readonly ReadOnlySpan<byte> _data;
        private uint _a;
        private uint _c;
        private int _ct;
        private int _position;

        public Coder(scoped in MqDecoder mq)
        {
            _data = mq.Data;
            mq.GetRegisters(out _a, out _c, out _ct, out _position);
        }

        /// <summary>DECODE in <paramref name="context"/> (C.3.2).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Decode(ref byte context) => MqDecoder.Decode(ref context, _data, ref _a, ref _c, ref _ct, ref _position);

        /// <summary>Stores the registers back into the decoder.</summary>
        public readonly void Store(ref MqDecoder mq) => mq.SetRegisters(_a, _c, _ct, _position);
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
