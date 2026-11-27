using System.Buffers;

namespace Broadside.Images.Import;

/// <summary>
/// The LZW variants of image files, beside the PDF one in <see cref="Filters.LzwDecodeFilter"/>: TIFF's (codes MSB first, the code
/// width growing one code early, TIFF 6.0 §13), the old LZW of libtiff before 5.0 (LSB first, not early), and GIF's (LSB first,
/// not early, a minimum code size of 2 to 8, GIF89a Appendix F). A decoder holds its string table in pooled arrays and is reused
/// for every strip or tile of an image; decoding allocates nothing.
/// </summary>
/// <remarks>ISO 32000-2 §7.4.4.2 (the same algorithm as LZWDecode, with different code framing).</remarks>
internal sealed class ImportLzwDecoder : IDisposable
{
    private const int MaxCodes = 4096;
    private int[] _prefix = ArrayPool<int>.Shared.Rent(MaxCodes);
    private byte[] _suffix = ArrayPool<byte>.Shared.Rent(MaxCodes);
    private byte[] _first = ArrayPool<byte>.Shared.Rent(MaxCodes);
    private int[] _length = ArrayPool<int>.Shared.Rent(MaxCodes);

    /// <summary>Returns the pooled table.</summary>
    public void Dispose()
    {
        ArrayPool<int>.Shared.Return(_prefix);
        ArrayPool<byte>.Shared.Return(_suffix);
        ArrayPool<byte>.Shared.Return(_first);
        ArrayPool<int>.Shared.Return(_length);
        _prefix = [];
        _suffix = [];
        _first = [];
        _length = [];
    }

    /// <summary>Returns whether TIFF LZW data is in the old, LSB-first form (libtiff's test: a first byte of 0 and bit 0 of the second set).</summary>
    public static bool IsOldStyle(ReadOnlySpan<byte> data) => data.Length >= 2 && data[0] == 0 && (data[1] & 1) != 0;

    /// <summary>Decodes into <paramref name="output"/> until the end code, the end of the data, or a full output.</summary>
    /// <param name="data">The codes.</param>
    /// <param name="output">Where the bytes go; decoding stops when it is full.</param>
    /// <param name="minimumCodeSize">The bits of a literal: 8 for TIFF, 2 to 8 for GIF.</param>
    /// <param name="msbFirst">Whether codes are packed MSB first (TIFF) rather than LSB first (GIF, old TIFF).</param>
    /// <param name="earlyChange">Whether the code width grows one code early (TIFF).</param>
    /// <param name="invalid">Whether a code was not in the table, which ends decoding.</param>
    /// <returns>The number of bytes written.</returns>
    public int Decode(ReadOnlySpan<byte> data, Span<byte> output, int minimumCodeSize, bool msbFirst, bool earlyChange, out bool invalid)
    {
        int clear = 1 << minimumCodeSize;
        int end = clear + 1;
        for (int i = 0; i < clear; i++)
        {
            _prefix[i] = -1;
            _suffix[i] = (byte)i;
            _first[i] = (byte)i;
            _length[i] = 1;
        }

        int next = clear + 2;
        int width = minimumCodeSize + 1;
        int previous = -1;
        int written = 0;
        int position = 0;
        uint buffer = 0;
        int bits = 0;
        invalid = false;
        while (written < output.Length)
        {
            while (bits < width && position < data.Length)
            {
                if (msbFirst)
                {
                    buffer = (buffer << 8) | data[position++];
                }
                else
                {
                    buffer |= (uint)data[position++] << bits;
                }

                bits += 8;
            }

            if (bits < width)
            {
                break;
            }

            int code;
            if (msbFirst)
            {
                code = (int)((buffer >> (bits - width)) & ((1u << width) - 1));
                bits -= width;
                buffer &= bits == 0 ? 0 : (1u << bits) - 1;
            }
            else
            {
                code = (int)(buffer & ((1u << width) - 1));
                buffer >>= width;
                bits -= width;
            }

            if (code == clear)
            {
                next = clear + 2;
                width = minimumCodeSize + 1;
                previous = -1;
                continue;
            }

            if (code == end)
            {
                break;
            }

            if (previous < 0)
            {
                if (code >= clear)
                {
                    invalid = true;
                    break;
                }

                output[written++] = (byte)code;
                previous = code;
                continue;
            }

            int emitted;
            if (code < next)
            {
                emitted = code;
                if (next < MaxCodes)
                {
                    Add(next++, previous, _first[code]);
                }
            }
            else if (code == next && next < MaxCodes)
            {
                Add(next++, previous, _first[previous]);
                emitted = code;
            }
            else
            {
                invalid = true;
                break;
            }

            written += Write(emitted, output[written..]);
            previous = code;
            int limit = earlyChange ? (1 << width) - 1 : 1 << width;
            if (next >= limit && width < 12)
            {
                width++;
            }
        }

        return written;
    }

    private void Add(int code, int prefix, byte suffix)
    {
        _prefix[code] = prefix;
        _suffix[code] = suffix;
        _first[code] = _first[prefix];
        _length[code] = _length[prefix] + 1;
    }

    /// <summary>Writes the string of <paramref name="code"/>, as much of it as fits, and returns the bytes written.</summary>
    private int Write(int code, Span<byte> output)
    {
        int length = _length[code];
        int fits = Math.Min(length, output.Length);
        int index = length - 1;
        while (code >= 0)
        {
            if (index < fits)
            {
                output[index] = _suffix[code];
            }

            index--;
            code = _prefix[code];
        }

        return fits;
    }
}
