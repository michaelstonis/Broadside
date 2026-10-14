using System.Buffers.Binary;
using System.Globalization;

namespace Broadside.Images;

/// <summary>
/// What a JPEG file's markers say before its first scan: the frame's size, components and coding process, the Adobe APP14
/// transform and an ICC profile carried in APP2 segments. Enough to place the file as a <c>DCTDecode</c> image without decoding it.
/// </summary>
/// <param name="Width">The frame's number of samples per line (X).</param>
/// <param name="Height">The frame's number of lines (Y).</param>
/// <param name="Components">The frame's number of components (Nf): 1, 3 or 4.</param>
/// <param name="Progressive">Whether the frame is progressive (SOF2).</param>
/// <param name="AdobeTransform">The APP14 Adobe segment's transform flag, or <see langword="null"/> when the file has none.</param>
/// <param name="RgbIdentifiers">Whether three components are identified as 'R', 'G' and 'B'.</param>
/// <param name="IccProfile">The ICC profile of the APP2 <c>ICC_PROFILE</c> segments, put together; <see langword="null"/> when there is none or a part is missing.</param>
/// <remarks>
/// ITU-T T.81 Annex B (marker segments, B.2.2 frame header), Adobe Technical Note #5116 §18 (APP14) and ICC.1 Annex B.4
/// (embedding in JPEG). ISO 32000-2 §7.4.8 allows baseline and (PDF 1.3) progressive Huffman-coded frames of 8-bit samples with 1,
/// 3 or 4 components.
/// </remarks>
internal sealed record JpegInfo(int Width, int Height, int Components, bool Progressive, int? AdobeTransform, bool RgbIdentifiers, byte[]? IccProfile)
{
    /// <summary>Reads the markers up to the first scan; never throws.</summary>
    /// <param name="jpeg">The JPEG file (JFIF, Exif or Adobe).</param>
    /// <param name="readProfile">Whether to put the ICC profile together.</param>
    /// <param name="error">Why the file cannot be placed as a DCTDecode image, or <see langword="null"/>.</param>
    /// <returns>The information, or <see langword="null"/>.</returns>
    public static JpegInfo? Read(ReadOnlySpan<byte> jpeg, bool readProfile, out string? error)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            error = "The data does not start with a JPEG SOI marker (ITU-T T.81 B.2.1).";
            return null;
        }

        int position = 2;
        int? transform = null;
        (int Width, int Height, int Components, bool Progressive, bool Rgb)? frame = null;
        List<(int Sequence, int Count, int Start, int Length)>? profile = null;
        while (true)
        {
            while (position < jpeg.Length && jpeg[position] != 0xFF)
            {
                position++;
            }

            while (position < jpeg.Length && jpeg[position] == 0xFF)
            {
                position++;
            }

            if (position >= jpeg.Length)
            {
                error = "The JPEG data ends before its first scan (ITU-T T.81 B.2.1).";
                return null;
            }

            byte marker = jpeg[position++];
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                continue;
            }

            if (marker == 0xD9)
            {
                error = "The JPEG data ends (EOI) before its first scan (ITU-T T.81 B.2.1).";
                return null;
            }

            if (position + 2 > jpeg.Length)
            {
                error = "A JPEG marker segment is cut short (ITU-T T.81 B.1.1.4).";
                return null;
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(jpeg[position..]);
            if (length < 2 || position + length > jpeg.Length)
            {
                error = "A JPEG marker segment's length is invalid or runs past the end of the data (ITU-T T.81 B.1.1.4).";
                return null;
            }

            ReadOnlySpan<byte> segment = jpeg.Slice(position + 2, length - 2);
            int segmentStart = position + 2;
            position += length;
            if (marker == 0xDA)
            {
                if (frame is not { } found)
                {
                    error = "The JPEG data has a scan before any frame header (ITU-T T.81 B.2.2).";
                    return null;
                }

                error = null;
                byte[]? icc = readProfile && profile is not null ? Assemble(jpeg, profile) : null;
                return new JpegInfo(found.Width, found.Height, found.Components, found.Progressive, transform, found.Rgb, icc);
            }

            if (marker == 0xEE && segment.Length >= 12 && segment.StartsWith("Adobe"u8))
            {
                transform = segment[11];
            }
            else if (marker == 0xE2 && readProfile && segment.Length > 14 && segment.StartsWith("ICC_PROFILE\0"u8))
            {
                profile ??= [];
                profile.Add((segment[12], segment[13], segmentStart + 14, segment.Length - 14));
            }
            else if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
            {
                if (frame is not null)
                {
                    error = "The JPEG data has a second frame header (ITU-T T.81 B.2.2).";
                    return null;
                }

                if (marker is not (0xC0 or 0xC1 or 0xC2))
                {
                    error = string.Create(
                        CultureInfo.InvariantCulture,
                        $"The JPEG frame is SOF{marker - 0xC0}: DCTDecode takes baseline and progressive Huffman-coded JPEG (ISO 32000-2 §7.4.8), not lossless, hierarchical or arithmetic-coded frames.");
                    return null;
                }

                if (segment.Length < 6)
                {
                    error = "The JPEG frame header is cut short (ITU-T T.81 B.2.2).";
                    return null;
                }

                int precision = segment[0];
                int height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]);
                int width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);
                int components = segment[5];
                if (precision != 8 || width == 0 || height == 0 || components is not (1 or 3 or 4) || segment.Length < 6 + (3 * components))
                {
                    error = string.Create(
                        CultureInfo.InvariantCulture,
                        $"The JPEG frame has {precision}-bit samples, {width} x {height} samples and {components} components: DCTDecode takes 8-bit samples of 1, 3 or 4 components (ISO 32000-2 §7.4.8), and the height shall be in the frame header (ISO 32000-2 Table 87).");
                    return null;
                }

                bool rgb = components == 3 && segment[6] == 'R' && segment[9] == 'G' && segment[12] == 'B';
                frame = (width, height, components, marker == 0xC2, rgb);
            }
        }
    }

    /// <summary>Puts the APP2 parts of an ICC profile together in sequence order; <see langword="null"/> when a part is missing or repeated.</summary>
    private static byte[]? Assemble(ReadOnlySpan<byte> jpeg, List<(int Sequence, int Count, int Start, int Length)> parts)
    {
        int count = parts[0].Count;
        if (count == 0 || parts.Count != count)
        {
            return null;
        }

        parts.Sort(static (a, b) => a.Sequence.CompareTo(b.Sequence));
        int total = 0;
        for (int i = 0; i < count; i++)
        {
            if (parts[i].Sequence != i + 1 || parts[i].Count != count)
            {
                return null;
            }

            total += parts[i].Length;
        }

        byte[] profile = new byte[total];
        int offset = 0;
        foreach ((_, _, int start, int length) in parts)
        {
            jpeg.Slice(start, length).CopyTo(profile.AsSpan(offset));
            offset += length;
        }

        return profile;
    }
}
