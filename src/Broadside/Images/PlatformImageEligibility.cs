using System.Buffers.Binary;
using System.Globalization;
using Broadside.Filters;

namespace Broadside.Images;

/// <summary>
/// The one rule that decides which images may go to a platform image decoder, over the JPEG markers before the first scan and the
/// image dictionary; and the byte hygiene applied to the data before it is handed over.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.8 (DCTDecode, Table 13 <c>ColorTransform</c>) and §8.9.5.1 Table 87, ITU-T T.81 Annex B (B.2.2 frame header,
/// B.2.4 tables and miscellaneous segments, B.2.5 DNL, B.3 hierarchical DHP), Adobe Technical Note #5116 §18 (APP14), the Exif APP1
/// segment and ICC.1 Annex B.4 (APP2 <c>ICC_PROFILE</c>). Measured hazards (see the image decode research report): platforms convert
/// CMYK and YCCK with their own inversion assumption, cannot all decode 12-bit or arithmetic-coded data, apply YCbCr where
/// <c>ColorTransform 0</c> forbids it, rotate by the Exif orientation and convert embedded profiles.
/// </para>
/// <para>Reads in place and allocates nothing; never throws on malformed data (it is then not offered).</para>
/// </remarks>
internal static class PlatformImageEligibility
{
    /// <summary>Applies the shared rule to a JPEG codestream.</summary>
    /// <param name="jpeg">The codestream the DCT codec would decode.</param>
    /// <param name="colorTransform">The DecodeParms <c>ColorTransform</c>, or -1 when absent.</param>
    /// <param name="dictionaryWidth">The image dictionary's Width.</param>
    /// <param name="dictionaryHeight">The image dictionary's Height.</param>
    /// <param name="request">The request (reduction 0), when eligible.</param>
    /// <param name="reason">Why the image is not eligible, or <see langword="null"/>.</param>
    /// <returns>Whether the image may be offered to a platform decoder.</returns>
    public static bool TryCreateRequest(ReadOnlySpan<byte> jpeg, int colorTransform, int dictionaryWidth, int dictionaryHeight, out PlatformImageRequest request, out string? reason)
    {
        request = default;
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            reason = "the data does not start with an SOI marker";
            return false;
        }

        int position = 2;
        int? adobeTransform = null;
        bool exif = false;
        bool icc = false;
        bool frame = false;
        bool progressive = false;
        bool arithmetic = false;
        bool rgbIdentifiers = false;
        bool subsampled = false;
        int width = 0;
        int height = 0;
        int components = 0;
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
                reason = "the data ends before its first scan";
                return false;
            }

            byte marker = jpeg[position++];
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                continue;
            }

            if (marker == 0xD9 || position + 2 > jpeg.Length)
            {
                reason = "the data ends before its first scan";
                return false;
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(jpeg[position..]);
            if (length < 2 || position + length > jpeg.Length)
            {
                reason = "a marker segment's length is invalid";
                return false;
            }

            ReadOnlySpan<byte> segment = jpeg.Slice(position + 2, length - 2);
            position += length;
            switch (marker)
            {
                case 0xDA:
                    if (!frame)
                    {
                        reason = "a scan comes before the frame header";
                        return false;
                    }

                    return Decide(out request, out reason);
                case 0xDE:
                    reason = "the data is hierarchical (DHP)";
                    return false;
                case 0xEE when segment.Length >= 12 && segment.StartsWith("Adobe"u8):
                    adobeTransform = segment[11];
                    break;
                case 0xE1 when segment.StartsWith("Exif\0"u8):
                    exif = true;
                    break;
                case 0xE2 when segment.StartsWith("ICC_PROFILE\0"u8):
                    icc = true;
                    break;
                case >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC):
                    if (frame)
                    {
                        reason = "the data has a second frame header";
                        return false;
                    }

                    if (marker is not (0xC0 or 0xC1 or 0xC2 or 0xC9 or 0xCA))
                    {
                        reason = string.Create(CultureInfo.InvariantCulture, $"the frame is SOF{marker - 0xC0} (lossless, hierarchical or differential)");
                        return false;
                    }

                    if (segment.Length < 6)
                    {
                        reason = "the frame header is cut short";
                        return false;
                    }

                    if (segment[0] != 8)
                    {
                        reason = string.Create(CultureInfo.InvariantCulture, $"the samples have {segment[0]} bits");
                        return false;
                    }

                    height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]);
                    width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);
                    components = segment[5];
                    if (segment.Length < 6 + (3 * components))
                    {
                        reason = "the frame header is cut short";
                        return false;
                    }

                    rgbIdentifiers = components == 3 && segment[6] == 'R' && segment[9] == 'G' && segment[12] == 'B';
                    for (int c = 1; c < components; c++)
                    {
                        subsampled |= segment[7 + (3 * c)] != segment[7];
                    }

                    progressive = marker is 0xC2 or 0xCA;
                    arithmetic = marker is 0xC9 or 0xCA;
                    frame = true;
                    break;
            }
        }

        bool Decide(out PlatformImageRequest request, out string? reason)
        {
            request = default;
            if (components is not (1 or 3))
            {
                reason = string.Create(CultureInfo.InvariantCulture, $"the frame has {components} components (only gray and YCbCr go to a platform decoder)");
                return false;
            }

            if (height == 0)
            {
                reason = "the frame's height is given by a DNL segment";
                return false;
            }

            if (width == 0)
            {
                reason = "the frame has no samples per line";
                return false;
            }

            if (width != dictionaryWidth || height != dictionaryHeight)
            {
                reason = string.Create(CultureInfo.InvariantCulture, $"the frame is {width} x {height} and the image dictionary says {dictionaryWidth} x {dictionaryHeight}");
                return false;
            }

            if (components == 3)
            {
                // A JFIF decoder transforms YCbCr unless APP14 says not to or the components are named R, G, B; ISO 32000-2 Table 13
                // lets APP14 decide, else ColorTransform. Offer only data both read as YCbCr.
                bool both = adobeTransform is { } adobe ? adobe == 1 : colorTransform != 0 && !rgbIdentifiers;
                if (!both)
                {
                    reason = adobeTransform is { } code
                        ? string.Create(CultureInfo.InvariantCulture, $"the APP14 transform is {code}, not YCbCr")
                        : colorTransform == 0 ? "ColorTransform is 0" : "the components are named R, G, B";
                    return false;
                }
            }

            request = new PlatformImageRequest
            {
                Filter = FilterNames.DctDecode,
                Width = width,
                Height = height,
                Components = components,
                BitsPerComponent = 8,
                Progressive = progressive,
                ArithmeticCoding = arithmetic,
                ColorTransform = components == 3,
                HasExif = exif,
                HasIccProfile = icc,
                ChromaSubsampled = subsampled,
            };
            reason = null;
            return true;
        }
    }

    /// <summary>Zero-fills, in a copy of the data, the payloads of the APP1 Exif and APP2 ICC profile segments a platform would act on.</summary>
    /// <param name="jpeg">The copy to change.</param>
    /// <param name="exif">Whether to zero-fill Exif payloads (after the 6-byte <c>Exif\0\0</c> identifier, so the segment stays well formed).</param>
    /// <param name="icc">Whether to zero-fill ICC profile payloads (after the 14-byte identifier and sequence numbers).</param>
    /// <remarks>
    /// The segments keep their markers and lengths, so every offset in the data is unchanged; an Exif block of zeros has no valid
    /// TIFF header and so no orientation, and a profile of zeros is not a profile (pdf.js does the same since its bug 1942064).
    /// </remarks>
    public static void Sanitize(Span<byte> jpeg, bool exif, bool icc)
    {
        int position = 2;
        while (position < jpeg.Length)
        {
            while (position < jpeg.Length && jpeg[position] != 0xFF)
            {
                position++;
            }

            while (position < jpeg.Length && jpeg[position] == 0xFF)
            {
                position++;
            }

            if (position + 2 >= jpeg.Length)
            {
                return;
            }

            byte marker = jpeg[position++];
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                continue;
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(jpeg[position..]);
            if (marker is 0xDA or 0xD9 || length < 2 || position + length > jpeg.Length)
            {
                return;
            }

            Span<byte> segment = jpeg.Slice(position + 2, length - 2);
            if (exif && marker == 0xE1 && segment.StartsWith("Exif\0"u8) && segment.Length > 6)
            {
                segment[6..].Clear();
            }
            else if (icc && marker == 0xE2 && segment.StartsWith("ICC_PROFILE\0"u8) && segment.Length > 14)
            {
                segment[14..].Clear();
            }

            position += length;
        }
    }
}
