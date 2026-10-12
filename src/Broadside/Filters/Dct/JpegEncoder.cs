using System.Buffers;
using System.Runtime.CompilerServices;

namespace Broadside.Filters.Dct;

/// <summary>How the samples given to the encoder become JPEG components.</summary>
internal enum JpegColorLayout
{
    /// <summary>One component, JFIF.</summary>
    Gray,

    /// <summary>RGB converted to YCbCr, chroma optionally subsampled, JFIF.</summary>
    YCbCr,

    /// <summary>RGB stored as it is, components identified R, G, B, Adobe APP14 transform 0.</summary>
    Rgb,

    /// <summary>CMYK stored as it is, components identified C, M, Y, K, Adobe APP14 transform 0.</summary>
    Cmyk,

    /// <summary>CMYK converted to YCCK (the complement of CMY to YCbCr, K kept), chroma optionally subsampled, Adobe APP14 transform 2.</summary>
    Ycck,
}

/// <summary>One quantization table and the divisors that apply it, for one quality setting.</summary>
/// <remarks>ITU-T T.81 §A.3.4, §B.2.4.1 (DQT), Annex K.1 (Tables K.1 and K.2); the IJG quality scaling of libjpeg <c>jcparam.c</c>.</remarks>
internal sealed class JpegQuantization
{
    /// <summary>Table K.1, luminance, natural order.</summary>
    private static readonly byte[] Luminance =
    [
        16, 11, 10, 16, 24, 40, 51, 61, 12, 12, 14, 19, 26, 58, 60, 55, 14, 13, 16, 24, 40, 57, 69, 56, 14, 17, 22, 29, 51, 87, 80, 62,
        18, 22, 37, 56, 68, 109, 103, 77, 24, 35, 55, 64, 81, 104, 113, 92, 49, 64, 78, 87, 103, 121, 120, 101, 72, 92, 95, 98, 112, 100, 103, 99,
    ];

    /// <summary>Table K.2, chrominance, natural order.</summary>
    private static readonly byte[] Chrominance =
    [
        17, 18, 24, 47, 99, 99, 99, 99, 18, 21, 26, 66, 99, 99, 99, 99, 24, 26, 56, 99, 99, 99, 99, 99, 47, 66, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99,
    ];

    private JpegQuantization(ReadOnlySpan<byte> basic, int quality)
    {
        // libjpeg jpeg_quality_scaling and jpeg_add_quant_table with force_baseline: 50 keeps the tables, below scales up by
        // 5000 / q percent, above scales down by 200 - 2q percent; values are rounded and kept within 1 to 255.
        int scale = quality < 50 ? 5000 / quality : 200 - (quality * 2);
        for (int i = 0; i < 64; i++)
        {
            int value = Math.Clamp(((basic[i] * scale) + 50) / 100, 1, 255);
            Values[i] = (byte)value;
            (Reciprocals[i], Corrections[i], int shift) = ForwardDct.Divisor(value);
            Shifts[i] = (byte)shift;
        }
    }

    /// <summary>Gets the 64 quantization values in natural order.</summary>
    public byte[] Values { get; } = new byte[64];

    /// <summary>Gets the reciprocals of 8 x each value.</summary>
    public uint[] Reciprocals { get; } = new uint[64];

    /// <summary>Gets the rounding corrections.</summary>
    public uint[] Corrections { get; } = new uint[64];

    /// <summary>Gets the shifts.</summary>
    public byte[] Shifts { get; } = new byte[64];

    /// <summary>Creates the luminance table (Table K.1) scaled for <paramref name="quality"/>.</summary>
    public static JpegQuantization CreateLuminance(int quality) => new(Luminance, quality);

    /// <summary>Creates the chrominance table (Table K.2) scaled for <paramref name="quality"/>.</summary>
    public static JpegQuantization CreateChrominance(int quality) => new(Chrominance, quality);
}

/// <summary>
/// A baseline sequential JPEG encoder (SOF0, 8-bit, Huffman-coded with the Annex K tables, one interleaved scan): colour conversion,
/// chroma downsampling, the forward DCT, quantization and entropy coding, one MCU row at a time in pooled buffers. Its output equals
/// libjpeg-turbo's for the same settings (integer DCT, no Huffman optimization) byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// ITU-T T.81 Annex A (sample precision, MCU geometry §A.2, level shift and FDCT §A.3), Annex B (interchange format: SOI, DQT, SOF0,
/// DHT, SOS, EOI, byte stuffing §B.1.1.5), Annex C (Huffman codes), Annex F §F.1.2 (baseline Huffman encoding: DC differences,
/// zero runs, ZRL, EOB), Annex K (quantization and Huffman tables). JFIF 1.01 APP0 for gray and YCbCr; Adobe Technical Note #5116
/// APP14 (transform 0 for RGB and CMYK, 2 for YCCK) otherwise, as libjpeg writes them.
/// </para>
/// <para>
/// Edges as libjpeg <c>jcprepct.c</c>, <c>jcsample.c</c> and <c>jccoefct.c</c>: the last column and row are replicated to whole
/// blocks, rows are padded to the vertical sampling factor before downsampling and the last downsampled row afterwards, and the
/// blocks of an MCU outside a component's area are dummy blocks with the DC of the block before and no AC. Downsampling averages
/// 2 x 1 or 2 x 2 samples with libjpeg's alternating rounding bias (0, 1 and 1, 2). Colour conversion is libjpeg's
/// <c>jccolor.c</c> 16-bit fixed point (Rec. 601, full range). The Independent JPEG Group's software, see THIRD-PARTY-NOTICES.txt.
/// </para>
/// </remarks>
internal static class JpegEncoder
{
    private const int MaxComponents = 4;

    /// <summary>Writes a complete JPEG interchange-format image.</summary>
    /// <param name="samples">Width x height pixels of interleaved 8-bit samples, top to bottom.</param>
    /// <param name="width">The width, 1 to 65535.</param>
    /// <param name="height">The height, 1 to 65535.</param>
    /// <param name="layout">The colour layout; it decides the component count.</param>
    /// <param name="horizontal">The horizontal sampling factor of luminance (and K) when chroma is subsampled, else 1.</param>
    /// <param name="vertical">The vertical sampling factor of luminance (and K) when chroma is subsampled, else 1.</param>
    /// <param name="luminance">The quantization of luminance and of untransformed components.</param>
    /// <param name="chrominance">The quantization of chrominance.</param>
    /// <param name="destination">Where to write the JPEG.</param>
    [SkipLocalsInit]
    public static void Encode(
        ReadOnlySpan<byte> samples,
        int width,
        int height,
        JpegColorLayout layout,
        int horizontal,
        int vertical,
        JpegQuantization luminance,
        JpegQuantization chrominance,
        IBufferWriter<byte> destination)
    {
        bool transformed = layout is JpegColorLayout.YCbCr or JpegColorLayout.Ycck;
        int hmax = transformed ? horizontal : 1;
        int vmax = transformed ? vertical : 1;
        int count = layout switch
        {
            JpegColorLayout.Gray => 1,
            JpegColorLayout.YCbCr or JpegColorLayout.Rgb => 3,
            _ => 4,
        };

        int mcuWidth = hmax * 8;
        int mcuHeight = vmax * 8;
        int mcusPerRow = (width + mcuWidth - 1) / mcuWidth;
        int mcuRows = (height + mcuHeight - 1) / mcuHeight;
        int paddedWidth = mcusPerRow * mcuWidth;
        int paddedRows = (height + vmax - 1) / vmax * vmax;
        int convertedSize = mcuHeight * paddedWidth;

        // Component geometry (T.81 §A.1.1, §A.2.4): chroma (1 and 2 of YCbCr and YCCK) is 1 x 1, everything else full size.
        Span<Component> components = stackalloc Component[MaxComponents];
        int bufferSize = count * convertedSize;
        for (int c = 0; c < count; c++)
        {
            bool chroma = transformed && c is 1 or 2;
            ref Component component = ref components[c];
            component.Id = Identifier(layout, c);
            component.H = chroma ? 1 : hmax;
            component.V = chroma ? 1 : vmax;
            component.Table = chroma ? 1 : 0;
            component.WidthInBlocks = ((width * component.H) + mcuWidth - 1) / mcuWidth;
            component.HeightInBlocks = ((height * component.V) + mcuHeight - 1) / mcuHeight;
            component.Converted = c * convertedSize;
            if (component.H == hmax && component.V == vmax)
            {
                component.Plane = component.Converted;
                component.Stride = paddedWidth;
            }
            else
            {
                component.Plane = bufferSize;
                component.Stride = mcusPerRow * component.H * 8;
                bufferSize += component.V * 8 * component.Stride;
            }
        }

        var output = new FilterOutput(destination);
        WriteHeaders(ref output, layout, width, height, components[..count], luminance, chrominance);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            var bits = new JpegBitWriter(output);
            Span<int> predictors = stackalloc int[MaxComponents];
            predictors.Clear();
            Span<int> block = stackalloc int[64];
            for (int mcuRow = 0; mcuRow < mcuRows; mcuRow++)
            {
                int top = mcuRow * mcuHeight;
                Convert(samples, width, height, top, layout, count, buffer, convertedSize, paddedWidth, mcuHeight);
                for (int c = 0; c < count; c++)
                {
                    ref Component component = ref components[c];
                    if (component.Plane != component.Converted)
                    {
                        Downsample(buffer, component, vmax, paddedWidth, top, paddedRows);
                    }
                }

                for (int mcu = 0; mcu < mcusPerRow; mcu++)
                {
                    for (int c = 0; c < count; c++)
                    {
                        ref Component component = ref components[c];
                        JpegQuantization quantization = component.Table == 0 ? luminance : chrominance;
                        HuffmanEncodingTable dc = component.Table == 0 ? HuffmanEncodingTable.DcLuminance : HuffmanEncodingTable.DcChrominance;
                        HuffmanEncodingTable ac = component.Table == 0 ? HuffmanEncodingTable.AcLuminance : HuffmanEncodingTable.AcChrominance;
                        for (int y = 0; y < component.V; y++)
                        {
                            int blockRow = (mcuRow * component.V) + y;
                            for (int x = 0; x < component.H; x++)
                            {
                                int blockColumn = (mcu * component.H) + x;
                                if (blockRow >= component.HeightInBlocks || blockColumn >= component.WidthInBlocks)
                                {
                                    // A dummy block (jccoefct.c): the DC of the block before it, no AC, so a zero difference and EOB.
                                    bits.Write(dc.Codes[0], dc.Lengths[0]);
                                    bits.Write(ac.Codes[0], ac.Lengths[0]);
                                    continue;
                                }

                                int start = component.Plane + (y * 8 * component.Stride) + (blockColumn * 8);
                                ForwardDct.Transform(
                                    buffer.AsSpan(start, (7 * component.Stride) + 8),
                                    component.Stride,
                                    quantization.Reciprocals,
                                    quantization.Corrections,
                                    quantization.Shifts,
                                    block);
                                EncodeBlock(ref bits, block, ref predictors[c], dc, ac);
                            }
                        }
                    }
                }
            }

            bits.Flush();
            output = bits.Output;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        output.Write(0xFF);
        output.Write(0xD9);
        output.Flush();
    }

    /// <summary>The component identifier libjpeg writes: 1 to 4 for JFIF and YCCK, the letters for RGB and CMYK.</summary>
    private static byte Identifier(JpegColorLayout layout, int component) => layout switch
    {
        JpegColorLayout.Rgb => (byte)"RGB"[component],
        JpegColorLayout.Cmyk => (byte)"CMYK"[component],
        _ => (byte)(component + 1),
    };

    /// <summary>
    /// Colour-converts the rows of one MCU row into the full-size planes: the last column replicated to the padded width, rows below
    /// the image copies of the row above (libjpeg's <c>expand_right_edge</c> and <c>expand_bottom_edge</c>).
    /// </summary>
    private static void Convert(
        ReadOnlySpan<byte> samples, int width, int height, int top, JpegColorLayout layout, int count, byte[] buffer, int planeSize, int paddedWidth, int rows)
    {
        int stride = width * count;
        for (int row = 0; row < rows; row++)
        {
            int y = top + row;
            int offset = row * paddedWidth;
            if (y >= height)
            {
                for (int c = 0; c < count; c++)
                {
                    int plane = (c * planeSize) + offset;
                    buffer.AsSpan(plane - paddedWidth, paddedWidth).CopyTo(buffer.AsSpan(plane, paddedWidth));
                }

                continue;
            }

            ReadOnlySpan<byte> line = samples.Slice(y * stride, stride);
            Span<byte> p0 = buffer.AsSpan(offset, paddedWidth);
            switch (layout)
            {
                case JpegColorLayout.Gray:
                    line.CopyTo(p0);
                    break;
                case JpegColorLayout.YCbCr:
                    RgbToYcc(line, p0, buffer.AsSpan(planeSize + offset, paddedWidth), buffer.AsSpan((2 * planeSize) + offset, paddedWidth), width, 3, invert: 0);
                    break;
                case JpegColorLayout.Ycck:
                    RgbToYcc(line, p0, buffer.AsSpan(planeSize + offset, paddedWidth), buffer.AsSpan((2 * planeSize) + offset, paddedWidth), width, 4, invert: 255);
                    Deinterleave(line, buffer.AsSpan((3 * planeSize) + offset, paddedWidth), width, 4, 3);
                    break;
                default:
                    for (int c = 0; c < count; c++)
                    {
                        Deinterleave(line, buffer.AsSpan((c * planeSize) + offset, paddedWidth), width, count, c);
                    }

                    break;
            }

            for (int c = 0; c < count; c++)
            {
                Span<byte> plane = buffer.AsSpan((c * planeSize) + offset, paddedWidth);
                plane[width..].Fill(plane[width - 1]);
            }
        }
    }

    /// <summary>One component of interleaved samples.</summary>
    private static void Deinterleave(ReadOnlySpan<byte> line, Span<byte> plane, int width, int count, int component)
    {
        for (int x = 0, i = component; x < width; x++, i += count)
        {
            plane[x] = line[i];
        }
    }

    /// <summary>
    /// libjpeg <c>rgb_ycc_convert</c> (and, with <paramref name="invert"/> 255, the first three components of
    /// <c>cmyk_ycck_convert</c>): FIX(x) = round(x 2^16); Cb and Cr round with 0.5 - epsilon so 255 is never exceeded.
    /// </summary>
    private static void RgbToYcc(ReadOnlySpan<byte> line, Span<byte> y, Span<byte> cb, Span<byte> cr, int width, int count, int invert)
    {
        const int half = 1 << 15;
        const int offset = (128 << 16) + half - 1;
        y = y[..width];
        cb = cb[..width];
        cr = cr[..width];
        for (int x = 0, i = 0; x < width; x++, i += count)
        {
            int r = line[i] ^ invert;
            int g = line[i + 1] ^ invert;
            int b = line[i + 2] ^ invert;
            y[x] = (byte)(((19595 * r) + (38470 * g) + (7471 * b) + half) >> 16);
            cb[x] = (byte)(((-11059 * r) - (21709 * g) + (32768 * b) + offset) >> 16);
            cr[x] = (byte)(((32768 * r) - (27439 * g) - (5329 * b) + offset) >> 16);
        }
    }

    /// <summary>
    /// libjpeg <c>h2v1_downsample</c> and <c>h2v2_downsample</c> for one MCU row of a chroma component: the row groups whose input
    /// starts within the rows padded to the vertical factor, then the last downsampled row repeated to the MCU height.
    /// </summary>
    private static void Downsample(byte[] buffer, in Component component, int vmax, int paddedWidth, int top, int paddedRows)
    {
        int columns = component.Stride;
        for (int group = 0; group < 8; group++)
        {
            Span<byte> output = buffer.AsSpan(component.Plane + (group * columns), columns);
            if (top + (group * vmax) >= paddedRows)
            {
                buffer.AsSpan(component.Plane + ((group - 1) * columns), columns).CopyTo(output);
                continue;
            }

            ReadOnlySpan<byte> first = buffer.AsSpan(component.Converted + (group * vmax * paddedWidth), paddedWidth);
            if (vmax == 1)
            {
                for (int x = 0, bias = 0; x < columns; x++, bias ^= 1)
                {
                    output[x] = (byte)((first[2 * x] + first[(2 * x) + 1] + bias) >> 1);
                }
            }
            else
            {
                ReadOnlySpan<byte> second = buffer.AsSpan(component.Converted + (((group * vmax) + 1) * paddedWidth), paddedWidth);
                for (int x = 0, bias = 1; x < columns; x++, bias ^= 3)
                {
                    output[x] = (byte)((first[2 * x] + first[(2 * x) + 1] + second[2 * x] + second[(2 * x) + 1] + bias) >> 2);
                }
            }
        }
    }

    /// <summary>Huffman-codes one block: the DC difference (T.81 §F.1.2.1), then the AC coefficients in zig-zag order (§F.1.2.2).</summary>
    private static void EncodeBlock(ref JpegBitWriter bits, scoped ReadOnlySpan<int> block, ref int predictor, HuffmanEncodingTable dc, HuffmanEncodingTable ac)
    {
        int difference = block[0] - predictor;
        predictor = block[0];
        WriteValue(ref bits, dc, 0, difference);

        ReadOnlySpan<byte> order = JpegMarkers.NaturalOrder;
        int run = 0;
        for (int k = 1; k < 64; k++)
        {
            int value = block[order[k]];
            if (value == 0)
            {
                run++;
                continue;
            }

            while (run > 15)
            {
                bits.Write(ac.Codes[0xF0], ac.Lengths[0xF0]);
                run -= 16;
            }

            WriteValue(ref bits, ac, run << 4, value);
            run = 0;
        }

        if (run > 0)
        {
            bits.Write(ac.Codes[0], ac.Lengths[0]);
        }
    }

    /// <summary>
    /// Writes the code of (<paramref name="prefix"/> | the magnitude category of <paramref name="value"/>) and then the category's
    /// low bits of the value, one less when negative (T.81 §F.1.2.1.1, Table F.1).
    /// </summary>
    private static void WriteValue(ref JpegBitWriter bits, HuffmanEncodingTable table, int prefix, int value)
    {
        int sign = value >> 31;
        int magnitude = (value ^ sign) - sign;
        int category = 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)magnitude);
        int symbol = prefix | category;
        bits.Write(table.Codes[symbol], table.Lengths[symbol]);
        if (category > 0)
        {
            bits.Write((uint)(value + sign) & ((1u << category) - 1), category);
        }
    }

    /// <summary>SOI, the APP0 or APP14 marker, DQT, SOF0, DHT and SOS, in libjpeg's order.</summary>
    private static void WriteHeaders(
        ref FilterOutput output, JpegColorLayout layout, int width, int height, scoped ReadOnlySpan<Component> components, JpegQuantization luminance, JpegQuantization chrominance)
    {
        bool twoTables = layout is JpegColorLayout.YCbCr or JpegColorLayout.Ycck;
        output.Write([0xFF, 0xD8]);
        if (layout is JpegColorLayout.Gray or JpegColorLayout.YCbCr)
        {
            // JFIF 1.01, no units, 1:1 aspect ratio, no thumbnail.
            output.Write([0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00]);
        }
        else
        {
            // Adobe, version 100, no flags, the transform code.
            byte transform = layout == JpegColorLayout.Ycck ? (byte)2 : (byte)0;
            output.Write([0xFF, 0xEE, 0x00, 0x0E, (byte)'A', (byte)'d', (byte)'o', (byte)'b', (byte)'e', 0x00, 0x64, 0x00, 0x00, 0x00, 0x00, transform]);
        }

        WriteQuantization(ref output, 0, luminance);
        if (twoTables)
        {
            WriteQuantization(ref output, 1, chrominance);
        }

        int frameLength = 8 + (3 * components.Length);
        output.Write([0xFF, 0xC0, (byte)(frameLength >> 8), (byte)frameLength, 8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, (byte)components.Length]);
        foreach (Component component in components)
        {
            output.Write([component.Id, (byte)((component.H << 4) | component.V), (byte)component.Table]);
        }

        WriteHuffman(ref output, 0x00, HuffmanEncodingTable.DcLuminance);
        WriteHuffman(ref output, 0x10, HuffmanEncodingTable.AcLuminance);
        if (twoTables)
        {
            WriteHuffman(ref output, 0x01, HuffmanEncodingTable.DcChrominance);
            WriteHuffman(ref output, 0x11, HuffmanEncodingTable.AcChrominance);
        }

        int scanLength = 6 + (2 * components.Length);
        output.Write([0xFF, 0xDA, (byte)(scanLength >> 8), (byte)scanLength, (byte)components.Length]);
        foreach (Component component in components)
        {
            output.Write([component.Id, (byte)((component.Table << 4) | component.Table)]);
        }

        output.Write([0x00, 0x3F, 0x00]);
    }

    /// <summary>A DQT segment of one 8-bit table, its values in zig-zag order (T.81 §B.2.4.1).</summary>
    private static void WriteQuantization(ref FilterOutput output, byte destination, JpegQuantization table)
    {
        output.Write([0xFF, 0xDB, 0x00, 0x43, destination]);
        ReadOnlySpan<byte> order = JpegMarkers.NaturalOrder;
        for (int k = 0; k < 64; k++)
        {
            output.Write(table.Values[order[k]]);
        }
    }

    /// <summary>A DHT segment of one table (T.81 §B.2.4.2).</summary>
    private static void WriteHuffman(ref FilterOutput output, byte classAndDestination, HuffmanEncodingTable table)
    {
        int length = 2 + 1 + 16 + table.Values.Length;
        output.Write([0xFF, 0xC4, (byte)(length >> 8), (byte)length, classAndDestination]);
        output.Write(table.Counts);
        output.Write(table.Values);
    }

    /// <summary>One frame component: identifier, sampling factors, table, size in blocks and where its samples are.</summary>
    private struct Component
    {
        public byte Id;
        public int H;
        public int V;
        public int Table;
        public int WidthInBlocks;
        public int HeightInBlocks;
        public int Converted;
        public int Plane;
        public int Stride;
    }

    /// <summary>The entropy-coded segment's bit packer: most significant bit first, an <c>FF</c> byte followed by a stuffed zero (T.81 §B.1.1.5, §F.1.2.3).</summary>
    private ref struct JpegBitWriter(FilterOutput output)
    {
        private ulong _bits;
        private int _count;

        public FilterOutput Output = output;

        /// <summary>Appends the low <paramref name="length"/> bits of <paramref name="code"/>.</summary>
        public void Write(uint code, int length)
        {
            _bits = (_bits << length) | code;
            _count += length;
            while (_count >= 8)
            {
                _count -= 8;
                byte value = (byte)(_bits >> _count);
                Output.Write(value);
                if (value == 0xFF)
                {
                    Output.Write(0);
                }
            }
        }

        /// <summary>Fills the last byte with 1-bits, as libjpeg's <c>flush_bits</c> does (§F.1.2.3).</summary>
        public void Flush()
        {
            if (_count > 0)
            {
                Write((1u << (8 - _count)) - 1, 8 - _count);
            }
        }
    }
}
