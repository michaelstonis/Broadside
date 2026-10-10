using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Broadside.Graphics;

namespace Broadside.Images;

/// <summary>
/// Row operations over decoded image samples: unpacking packed components, scaling them to 8 bits, colour key and stencil
/// coverage, Matte un-premultiplication and mask sampling. None of them allocates.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.9.3 (sample layout), §8.9.5.2 (Decode), §8.9.6 (masks), §11.6.5.2 (Matte). Each works on one row at a time, from
/// its byte-aligned start, so the padding bits at the end of a row are never read as samples. Spans are the caller's: these methods
/// keep nothing.
/// </remarks>
public static class ImageRows
{
    private static readonly ulong[] Expand1 = BuildExpand1();
    private static readonly uint[] Expand2 = BuildExpand2();
    private static readonly ushort[] Expand4 = BuildExpand4();

    /// <summary>Unpacks <paramref name="count"/> components of a row stored at 1, 2, 4 or 8 bits into one byte each, as raw values.</summary>
    /// <param name="row">The row, from its first byte.</param>
    /// <param name="storageBits">The storage width: 1, 2, 4 or 8 (<see cref="DecodedImage.StorageBits"/>).</param>
    /// <param name="count">The number of components to unpack: width × components.</param>
    /// <param name="destination">At least <paramref name="count"/> bytes.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="storageBits"/> is not 1, 2, 4 or 8, or a span is too short.</exception>
    /// <remarks>ISO 32000-2 §8.9.3: most significant bit first.</remarks>
    public static void Unpack(ReadOnlySpan<byte> row, int storageBits, int count, Span<byte> destination)
    {
        CheckSpans(row.Length, storageBits, count, destination.Length, maxBits: 8);
        Span<byte> output = destination[..count];
        switch (storageBits)
        {
            case 8:
                row[..count].CopyTo(output);
                return;
            case 1:
                Unpack1(row, output);
                return;
            case 2:
                Unpack2(row, output);
                return;
            default:
                Unpack4(row, output);
                return;
        }
    }

    /// <summary>Unpacks <paramref name="count"/> components of a row stored at 1, 2, 4, 8 or 16 bits into one <see cref="ushort"/> each, as raw values.</summary>
    /// <param name="row">The row, from its first byte.</param>
    /// <param name="storageBits">The storage width: 1, 2, 4, 8 or 16.</param>
    /// <param name="count">The number of components to unpack.</param>
    /// <param name="destination">At least <paramref name="count"/> values.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="storageBits"/> is not a storage width, or a span is too short.</exception>
    /// <remarks>ISO 32000-2 §8.9.3: 16-bit components are big-endian.</remarks>
    public static void Unpack(ReadOnlySpan<byte> row, int storageBits, int count, Span<ushort> destination)
    {
        CheckSpans(row.Length, storageBits, count, destination.Length, maxBits: 16);
        Span<ushort> output = destination[..count];
        if (storageBits == 16)
        {
            ReadOnlySpan<ushort> source = MemoryMarshal.Cast<byte, ushort>(row[..(count * 2)]);
            if (BitConverter.IsLittleEndian)
            {
                BinaryPrimitives.ReverseEndianness(source, output);
            }
            else
            {
                source.CopyTo(output);
            }

            return;
        }

        if (storageBits == 8)
        {
            for (int i = 0; i < output.Length; i++)
            {
                output[i] = row[i];
            }

            return;
        }

        int mask = (1 << storageBits) - 1;
        for (int byteIndex = 0, i = 0; i < output.Length; byteIndex++)
        {
            int value = row[byteIndex];
            for (int shift = 8 - storageBits; shift >= 0 && i < output.Length; shift -= storageBits)
            {
                output[i++] = (ushort)((value >> shift) & mask);
            }
        }
    }

    /// <summary>
    /// Unpacks a row and scales each component to 0-255: x × 255 / (2^n − 1), rounded, where n is the logical depth. The usual input
    /// to a byte colour conversion.
    /// </summary>
    /// <param name="row">The row, from its first byte.</param>
    /// <param name="storageBits">The storage width: 1, 2, 4, 8 or 16.</param>
    /// <param name="bitsPerComponent">The logical depth (<see cref="DecodedImage.BitsPerComponent"/>), at most the storage width.</param>
    /// <param name="count">The number of components.</param>
    /// <param name="destination">At least <paramref name="count"/> bytes.</param>
    /// <exception cref="ArgumentOutOfRangeException">A width or span is out of range.</exception>
    /// <remarks>ISO 32000-2 §8.9.5.2: the default Decode maps 0 and 2^n − 1 onto the ends of the component's range.</remarks>
    public static void UnpackScaled(ReadOnlySpan<byte> row, int storageBits, int bitsPerComponent, int count, Span<byte> destination)
    {
        CheckSpans(row.Length, storageBits, count, destination.Length, maxBits: 16);
        ArgumentOutOfRangeException.ThrowIfLessThan(bitsPerComponent, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bitsPerComponent, storageBits);
        Span<byte> output = destination[..count];
        if (storageBits == 16)
        {
            int max = (1 << bitsPerComponent) - 1;
            for (int i = 0; i < output.Length; i++)
            {
                int value = BinaryPrimitives.ReadUInt16BigEndian(row.Slice(i * 2, 2));
                output[i] = (byte)((((long)value * 510) + max) / (2L * max));
            }

            return;
        }

        if (storageBits < 8)
        {
            Unpack(row, storageBits, count, output);
        }
        else
        {
            row[..count].CopyTo(output);
        }

        if (bitsPerComponent == 8)
        {
            return;
        }

        if (bitsPerComponent == storageBits && storageBits < 8)
        {
            byte factor = storageBits switch { 1 => 255, 2 => 85, _ => 17 };
            for (int i = 0; i < output.Length; i++)
            {
                output[i] = (byte)(output[i] * factor);
            }

            return;
        }

        int top = (1 << bitsPerComponent) - 1;
        for (int i = 0; i < output.Length; i++)
        {
            int value = Math.Min(output[i], top);
            output[i] = (byte)(((value * 510) + top) / (2 * top));
        }
    }

    /// <summary>
    /// Computes colour key coverage for a row of raw samples: 0 where every component of a sample lies in its key range (masked out),
    /// 255 elsewhere (painted).
    /// </summary>
    /// <param name="raw">Raw components, interleaved: width × <paramref name="components"/> values (from <see cref="Unpack(ReadOnlySpan{byte}, int, int, Span{ushort})"/>).</param>
    /// <param name="components">The number of components per sample.</param>
    /// <param name="ranges">The key ranges, min and max per component (<see cref="PdfImage.ColorKey"/>).</param>
    /// <param name="coverage">One byte per sample.</param>
    /// <exception cref="ArgumentOutOfRangeException">The spans do not fit the sample count.</exception>
    /// <remarks>
    /// ISO 32000-2 §8.9.6.4: the comparison is on the raw sample values, before the Decode array is applied, and a sample is masked only
    /// when all of its components are in range.
    /// </remarks>
    public static void ColorKey(ReadOnlySpan<ushort> raw, int components, ReadOnlySpan<double> ranges, Span<byte> coverage)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(components, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(ranges.Length, 2 * components);
        int samples = raw.Length / components;
        ArgumentOutOfRangeException.ThrowIfLessThan(coverage.Length, samples);
        for (int s = 0, i = 0; s < samples; s++)
        {
            bool masked = true;
            for (int c = 0; c < components; c++, i++)
            {
                double value = raw[i];
                masked &= value >= ranges[2 * c] && value <= ranges[(2 * c) + 1];
            }

            coverage[s] = masked ? (byte)0 : (byte)255;
        }
    }

    /// <summary>
    /// Computes the coverage of a row of a stencil mask or explicit mask (1 bit per sample): 255 where the sample paints, 0 where it
    /// leaves the backdrop.
    /// </summary>
    /// <param name="row">The packed row.</param>
    /// <param name="width">The number of samples.</param>
    /// <param name="inverted">
    /// Whether the mask's Decode array is <c>[1 0]</c> (combined with <see cref="DecodedImage.SamplesInverted"/>; see
    /// <see cref="ImageDecodeMap.IsInverted"/>): then a sample 1 paints. Otherwise a sample 0 paints.
    /// </param>
    /// <param name="coverage">One byte per sample.</param>
    /// <exception cref="ArgumentOutOfRangeException">The spans do not fit <paramref name="width"/>.</exception>
    /// <remarks>ISO 32000-2 §8.9.6.2 and §8.9.6.3.</remarks>
    public static void Stencil(ReadOnlySpan<byte> row, int width, bool inverted, Span<byte> coverage)
    {
        CheckSpans(row.Length, 1, width, coverage.Length, maxBits: 1);
        Unpack1(row, coverage[..width]);
        byte paint = inverted ? (byte)1 : (byte)0;
        for (int i = 0; i < width; i++)
        {
            coverage[i] = coverage[i] == paint ? (byte)255 : (byte)0;
        }
    }

    /// <summary>
    /// Undoes the pre-blending of colour against a matte colour (<c>Matte</c>): c = m + (c′ − m) / α, clamped to each component's range;
    /// a sample with α = 0 becomes the matte colour.
    /// </summary>
    /// <param name="colors">Decoded components (after the Decode array; palette entries for an Indexed image), interleaved; changed in place.</param>
    /// <param name="components">The number of components per sample: the length of the matte array.</param>
    /// <param name="alpha">The soft mask's decoded value for each sample, 0 to 1.</param>
    /// <param name="matte">The matte colour, one value per component.</param>
    /// <param name="ranges">The range of each component, for the clamp.</param>
    /// <exception cref="ArgumentOutOfRangeException">The spans do not fit the sample count.</exception>
    /// <remarks>ISO 32000-2 §11.6.5.2, Table 144 and the formula after it: done in the image's own colour space, before conversion.</remarks>
    public static void Unpremultiply(Span<float> colors, int components, ReadOnlySpan<float> alpha, ReadOnlySpan<float> matte, ReadOnlySpan<ComponentRange> ranges)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(components, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(matte.Length, components);
        ArgumentOutOfRangeException.ThrowIfLessThan(ranges.Length, components);
        int samples = colors.Length / components;
        ArgumentOutOfRangeException.ThrowIfLessThan(alpha.Length, samples);
        for (int s = 0, i = 0; s < samples; s++)
        {
            float a = alpha[s];
            for (int c = 0; c < components; c++, i++)
            {
                double value = a <= 0 ? matte[c] : matte[c] + ((colors[i] - matte[c]) / a);
                colors[i] = (float)ranges[c].Clamp(value);
            }
        }
    }

    /// <summary>Returns the column (or row) of a mask that sample <paramref name="index"/> of the base image falls on, by its centre.</summary>
    /// <param name="index">The base image's column (or row).</param>
    /// <param name="size">The base image's width (or height).</param>
    /// <param name="maskSize">The mask's width (or height).</param>
    /// <returns>⌊(2 × index + 1) × maskSize / (2 × size)⌋, within the mask.</returns>
    /// <remarks>
    /// ISO 32000-2 §8.9.6.3 and §11.6.5.2: a mask and its image both map onto the unit square, whatever their resolutions, so a mask
    /// is sampled at its own resolution when drawn rather than resampled when decoded.
    /// </remarks>
    public static int MaskIndex(int index, int size, int maskSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maskSize, 1);
        long position = (((2L * index) + 1) * maskSize) / (2L * size);
        return (int)Math.Clamp(position, 0, maskSize - 1);
    }

    private static void CheckSpans(int rowLength, int storageBits, int count, int destinationLength, int maxBits)
    {
        if (storageBits is not (1 or 2 or 4 or 8 or 16) || storageBits > maxBits)
        {
            throw new ArgumentOutOfRangeException(nameof(storageBits), storageBits, "The storage width is not one this operation reads.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfLessThan(destinationLength, count, "destination");
        ArgumentOutOfRangeException.ThrowIfLessThan(rowLength, (int)(((long)count * storageBits + 7) >> 3), "row");
    }

    private static void Unpack1(ReadOnlySpan<byte> row, Span<byte> output)
    {
        int whole = output.Length >> 3;
        for (int i = 0; i < whole; i++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(output.Slice(i << 3, 8), Expand1[row[i]]);
        }

        for (int i = whole << 3; i < output.Length; i++)
        {
            output[i] = (byte)((row[i >> 3] >> (7 - (i & 7))) & 1);
        }
    }

    private static void Unpack2(ReadOnlySpan<byte> row, Span<byte> output)
    {
        int whole = output.Length >> 2;
        for (int i = 0; i < whole; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(i << 2, 4), Expand2[row[i]]);
        }

        for (int i = whole << 2; i < output.Length; i++)
        {
            output[i] = (byte)((row[i >> 2] >> (6 - (2 * (i & 3)))) & 3);
        }
    }

    private static void Unpack4(ReadOnlySpan<byte> row, Span<byte> output)
    {
        int whole = output.Length >> 1;
        for (int i = 0; i < whole; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(output.Slice(i << 1, 2), Expand4[row[i]]);
        }

        if ((output.Length & 1) != 0)
        {
            output[^1] = (byte)(row[whole] >> 4);
        }
    }

    private static ulong[] BuildExpand1()
    {
        ulong[] table = new ulong[256];
        for (int b = 0; b < 256; b++)
        {
            ulong value = 0;
            for (int i = 0; i < 8; i++)
            {
                value |= (ulong)((b >> (7 - i)) & 1) << (8 * i);
            }

            table[b] = value;
        }

        return table;
    }

    private static uint[] BuildExpand2()
    {
        uint[] table = new uint[256];
        for (int b = 0; b < 256; b++)
        {
            uint value = 0;
            for (int i = 0; i < 4; i++)
            {
                value |= (uint)((b >> (6 - (2 * i))) & 3) << (8 * i);
            }

            table[b] = value;
        }

        return table;
    }

    private static ushort[] BuildExpand4()
    {
        ushort[] table = new ushort[256];
        for (int b = 0; b < 256; b++)
        {
            table[b] = (ushort)((b >> 4) | ((b & 15) << 8));
        }

        return table;
    }
}
