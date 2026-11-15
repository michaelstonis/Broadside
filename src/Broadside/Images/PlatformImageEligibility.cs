using System.Buffers.Binary;
using System.Globalization;
using Broadside.Filters;
using Broadside.Filters.Codecs;
using Broadside.Filters.Jpx;

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
        int chromaX = 1;
        int chromaY = 1;
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

                    (chromaX, chromaY) = components == 3 ? ChromaRatios(segment[7], segment[10], segment[13]) : (1, 1);

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
                ChromaHorizontalSubsampling = chromaX,
                ChromaVerticalSubsampling = chromaY,
            };
            reason = null;
            return true;
        }
    }

    /// <summary>Applies the shared rule to a JPEG 2000 file or codestream whose header the codec has read.</summary>
    /// <param name="data">The JPXDecode data.</param>
    /// <param name="header">The header the codec read for this image and its dictionary (so its channels already follow the dictionary's color space and <c>SMaskInData</c>).</param>
    /// <param name="indexed">Whether the image dictionary's color space is <c>Indexed</c>, whose samples are palette indices that a decoder must return exactly.</param>
    /// <param name="request">The request (reduction 0), when eligible.</param>
    /// <param name="reason">Why the image is not eligible, or <see langword="null"/>.</param>
    /// <returns>Whether the image may be offered to a platform decoder.</returns>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §7.4.9 and Table 87; ITU-T T.800 Annexes A (SIZ), B.5 (resolution levels) and I.5.3 (the JP2 header boxes). A
    /// codec decides the output channels from the image dictionary, the file's palette, component mapping, channel definition and
    /// color specification boxes and the codestream's components; a platform decoder applies its own reading of the boxes. So the
    /// rule offers only the images where there is nothing to read: 8-bit unsigned components that all cover the image grid, as many
    /// as the image has channels (1 or 3), no opacity channel, no palette, component mapping or channel definition box, and no
    /// color specification other than enumerated sRGB or gray (an sYCC or ICC specification is converted, or not, as the platform
    /// pleases). The codestream must lie in one piece: a file that stores it in fragments is not offered, because the data handed
    /// over is the file and a platform decoder would not reassemble it.
    /// </para>
    /// <para>
    /// A request's <see cref="PlatformImageRequest.Reduction"/> is then the number of resolution levels dropped (T.800 B.5), which
    /// gives ⌈W / 2^d⌉ × ⌈H / 2^d⌉ samples; <see cref="ImageHeader.MaxReduction"/> of the header is at least 1, so an image whose
    /// codestream has no decomposition level is decoded when its list is built and never offered. Reads the boxes and markers in
    /// place; a fragmented file is joined once to find that out. Never throws on malformed data (it is then not offered).
    /// </para>
    /// </remarks>
    public static bool TryCreateJpxRequest(ReadOnlySpan<byte> data, in ImageHeader header, bool indexed, out PlatformImageRequest request, out string? reason)
    {
        request = default;
        if (indexed)
        {
            reason = "the color space is Indexed";
            return false;
        }

        if (header.HasAlpha)
        {
            reason = "the image has an opacity channel (SMaskInData)";
            return false;
        }

        ImageGeometry geometry = header.Geometry;
        if (geometry.Components is not (1 or 3) || geometry.BitsPerComponent != 8)
        {
            reason = string.Create(CultureInfo.InvariantCulture, $"the image has {geometry.Components} channels of {geometry.BitsPerComponent} bits (only 8-bit gray and RGB go to a platform decoder)");
            return false;
        }

        var quiet = new CodecReporter(null, silent: true);
        if (JpxCodestreamReader.Locate(data, quiet, out byte[]? assembled) is not { } range)
        {
            reason = "the data holds no codestream";
            return false;
        }

        if (assembled is not null)
        {
            reason = "the codestream is stored in fragments";
            return false;
        }

        JpxFileHeader file = JpxFileHeader.Read(data, quiet);
        if (file.Palette is not null || file.Mapping is not null)
        {
            reason = "the file has a palette or component mapping box";
            return false;
        }

        if (file.Channels is not null)
        {
            reason = "the file has a channel definition box";
            return false;
        }

        foreach (JpxColorSpecification color in file.Colors)
        {
            if (color.Method != 1 || color.Enumerated is not (16 or 17))
            {
                reason = color.Method == 1
                    ? string.Create(CultureInfo.InvariantCulture, $"the file's color specification is enumerated space {color.Enumerated}, not sRGB or gray")
                    : "the file's color specification is an ICC profile";
                return false;
            }
        }

        if (JpxCodestreamReader.ReadSize(data.Slice(range.Offset, range.Length), quiet) is not { } size)
        {
            reason = "the codestream's SIZ marker segment is unreadable";
            return false;
        }

        if (size.Components.Length != geometry.Components)
        {
            reason = string.Create(CultureInfo.InvariantCulture, $"the codestream has {size.Components.Length} components and the image {geometry.Components} channels");
            return false;
        }

        foreach (JpxComponentInfo component in size.Components)
        {
            if (component.Depth != 8 || component.Signed || component.Dx != 1 || component.Dy != 1)
            {
                reason = "a component is not 8-bit unsigned and sampled at every position of the image grid";
                return false;
            }
        }

        request = new PlatformImageRequest
        {
            Filter = FilterNames.JpxDecode,
            Width = geometry.Width,
            Height = geometry.Height,
            Components = geometry.Components,
            BitsPerComponent = 8,
        };
        reason = null;
        return true;
    }

    /// <summary>
    /// The luma samples per chroma sample across and down (Hmax / Hc, Vmax / Vc) of a three-component frame from its components'
    /// sampling factor bytes (ITU-T T.81 B.2.2: Hi in the high nibble, Vi in the low): (1, 1) unsubsampled, (2, 2) for 4:2:0, (2, 1)
    /// for 4:2:2; 0 in a direction whose pattern is another (the first component not sampled at the highest rate, the two chroma
    /// components unlike each other, or a factor that does not divide the first's).
    /// </summary>
    private static (int X, int Y) ChromaRatios(byte luma, byte blue, byte red)
    {
        return (Ratio(luma >> 4, blue >> 4, red >> 4), Ratio(luma & 0xF, blue & 0xF, red & 0xF));

        static int Ratio(int y, int cb, int cr) => cb != cr || cb == 0 || cb > y || y % cb != 0 ? 0 : y / cb;
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
