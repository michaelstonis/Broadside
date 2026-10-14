using System.Globalization;
using System.Security.Cryptography;
using Broadside.Filters;
using Broadside.Graphics;
using Broadside.Graphics.Colors;
using Broadside.Images;
using Broadside.Images.Png;
using Broadside.Objects;
using Broadside.Writing;

namespace Broadside;

/// <summary>Writing images (issue #153): image XObjects from PNG files, JPEG files and sample buffers, which a <see cref="PdfCanvas"/> places.</summary>
public sealed partial class PdfDocument
{
    private static readonly CosName XObjectType = new("XObject");
    private static readonly CosInteger PngOptimum = new(15);

    /// <summary>Creates an image XObject from a PNG file, or returns the one this document already has for the same file.</summary>
    /// <param name="png">The PNG file: any colour type and bit depth, interlaced or not.</param>
    /// <returns>The image, an indirect stream; place it with <see cref="PdfCanvas.DrawImage(PdfImage, double, double, double, double)"/>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="png"/> is not a PNG file a decoder may read (a bad signature, IHDR, critical chunk or CRC, too little image
    /// data, an unknown filter type), or has more pixels than <see cref="PdfOptions.MaxImagePixels"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §8.9.5 (Table 87), §7.4.4 (FlateDecode with the PNG predictors, Table 8) and §11.6.5.2 (soft-mask images); ISO/IEC
    /// 15948. The samples keep their bit depth (1 to 16). A file that is not interlaced and has no alpha channel keeps its own
    /// compressed data, which is already <c>FlateDecode</c> data with PNG predictors (Predictor 15); any other is decoded and
    /// compressed again the same way, without interlacing. Greyscale is <c>DeviceGray</c>, truecolour <c>DeviceRGB</c>,
    /// indexed-colour an <c>Indexed</c> space over <c>DeviceRGB</c> with the PLTE table; an iCCP profile of the right number of
    /// components replaces the device space with an <c>ICCBased</c> space (one PDF allows, Table 67; any other profile is ignored).
    /// Gamma, chromaticity and sRGB chunks are not applied.
    /// </para>
    /// <para>
    /// An alpha channel, or a tRNS table of an indexed-colour image, becomes a soft-mask image (<c>SMask</c>, PDF 1.4) of the same
    /// size: 8-bit for a palette, the image's depth otherwise; an image whose every pixel is opaque gets none. The tRNS colour of a
    /// greyscale or truecolour image becomes a colour key mask (<c>Mask</c>, PDF 1.3, §8.9.6.4). 16-bit samples are PDF 1.5. Each
    /// feature's version is recorded (ADR 0003). A mutation.
    /// </para>
    /// </remarks>
    public PdfImage CreateImageFromPng(ReadOnlySpan<byte> png)
    {
        string key = "Image|PNG|" + Hash(png);
        if (Drawing.TryGet(key, out CosReference known))
        {
            return GetImage(known)!;
        }

        using PngImage image = PngReader.Read(png, Streams.MaxImagePixels, keepSamples: false, out string? error)
            ?? throw new ArgumentException(error, nameof(png));
        bool palette = image.ColorType == PngColorType.Palette;
        int components = palette ? 3 : image.ColorComponents;
        PdfColorSpace space = components == 3 ? PdfDeviceRgbColorSpace.Instance : PdfDeviceGrayColorSpace.Instance;
        if (image.IccProfile is { } profile && IccProfileHeader.Parse(profile) is { IsSupportedForPdf: true } header && header.ComponentCount == components)
        {
            space = CreateIccBasedColorSpace(profile);
        }

        if (palette)
        {
            space = CreateIndexedColorSpace(space, image.Palette);
        }

        CosStream stream = ImageStream(image.Width, image.Height, space, image.BitDepth, FilterNames.FlateDecode, Predicted(image.ColorComponents, image.BitDepth, image.Width));
        if (image.PassThrough is { } data)
        {
            stream.EncodedData = data;
            RequireFeature(VersionedFeatures.FlateCompression);
        }
        else
        {
            EncodeStream(stream, image.Color);
        }

        if (image.ColorKey is { } colorKey)
        {
            var ranges = new CosArray();
            foreach (ushort value in colorKey)
            {
                ranges.Add(new CosInteger(value));
                ranges.Add(new CosInteger(value));
            }

            stream.Dictionary[ImageNames.Mask] = ranges;
            RequireFeature(VersionedFeatures.ImageMasks);
        }

        if (image.HasAlpha)
        {
            stream.Dictionary[ImageNames.SMask] = SoftMask(image.Alpha, image.Width, image.Height, image.AlphaBits);
        }

        RequireImageFeatures(image.BitDepth);
        return GetImage(Drawing.Add(key, stream))!;
    }

    /// <summary>Creates an image XObject from a JPEG file, passed through unchanged, or returns the one this document already has for the same file.</summary>
    /// <param name="jpeg">The JPEG file: baseline or progressive, Huffman-coded, 8-bit, with 1, 3 or 4 components.</param>
    /// <returns>The image: a <c>DCTDecode</c> stream holding exactly <paramref name="jpeg"/>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="jpeg"/> does not start with a JPEG SOI marker, its markers are damaged before the first scan, or its frame is
    /// one DCTDecode does not take (lossless, hierarchical, arithmetic-coded, 12-bit, 2 components, or a height set by DNL).
    /// </exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §7.4.8 (DCTDecode, Table 13) and §8.9.5 (Table 87). Only the markers before the first scan are read: the
    /// width, height and components come from the frame header. 1 component is <c>DeviceGray</c>, 3 <c>DeviceRGB</c>, 4
    /// <c>DeviceCMYK</c>; an ICC profile in APP2 segments of the right number of components makes it <c>ICCBased</c>.
    /// </para>
    /// <para>
    /// A four-component file with an Adobe APP14 segment is read as Adobe writes CMYK (and YCCK) JPEG, with every component
    /// inverted, so the image gets <c>Decode [1 0 1 0 1 0 1 0]</c> (§8.9.5.2), as other PDF writers and readers treat such files;
    /// a four-component file without one is not inverted. NOTE <see cref="DctEncoder"/> writes CMYK not inverted with an APP14
    /// segment, for a stream whose dictionary it controls; place its output with <see cref="CreateImage(ReadOnlySpan{byte}, PdfImageLayout, PdfImageCompression)"/> instead.
    /// Three components identified as R, G and B without an APP14 segment get <c>DecodeParms &lt;&lt; /ColorTransform 0 &gt;&gt;</c>,
    /// so they are not converted from YCbCr (Table 13). A progressive file records PDF 1.3. A mutation.
    /// </para>
    /// </remarks>
    public PdfImage CreateImageFromJpeg(ReadOnlySpan<byte> jpeg)
    {
        string key = "Image|JPEG|" + Hash(jpeg);
        if (Drawing.TryGet(key, out CosReference known))
        {
            return GetImage(known)!;
        }

        JpegInfo info = JpegInfo.Read(jpeg, readProfile: true, out string? error) ?? throw new ArgumentException(error, nameof(jpeg));
        PdfColorSpace space = info.Components switch
        {
            1 => PdfDeviceGrayColorSpace.Instance,
            3 => PdfDeviceRgbColorSpace.Instance,
            _ => PdfDeviceCmykColorSpace.Instance,
        };
        if (info.IccProfile is { } profile && IccProfileHeader.Parse(profile) is { IsSupportedForPdf: true } header && header.ComponentCount == info.Components)
        {
            space = CreateIccBasedColorSpace(profile);
        }

        CosDictionary? parameters = info is { Components: 3, AdobeTransform: null, RgbIdentifiers: true }
            ? new CosDictionary { [FilterNames.ColorTransform] = new CosInteger(0) }
            : null;
        CosStream stream = ImageStream(info.Width, info.Height, space, 8, FilterNames.DctDecode, parameters);
        if (info is { Components: 4, AdobeTransform: not null })
        {
            stream.Dictionary[ImageNames.Decode] = new CosArray
            {
                new CosInteger(1), new CosInteger(0), new CosInteger(1), new CosInteger(0),
                new CosInteger(1), new CosInteger(0), new CosInteger(1), new CosInteger(0),
            };
        }

        stream.EncodedData = jpeg.ToArray();
        if (info.Progressive)
        {
            RequireFeature(VersionedFeatures.ProgressiveDct);
        }

        return GetImage(Drawing.Add(key, stream))!;
    }

    /// <summary>Creates an image XObject from a buffer of samples, or returns the one this document already has for the same samples.</summary>
    /// <param name="samples">The samples, in the layout <paramref name="layout"/> describes: exactly ceil(Width × components × BitsPerComponent / 8) × Height bytes.</param>
    /// <param name="layout">The size, colour space and bits per component.</param>
    /// <param name="compression">How to compress the samples.</param>
    /// <returns>The image, an indirect stream.</returns>
    /// <exception cref="ArgumentException">
    /// The layout is invalid (a size below 1, bits per component not 1, 2, 4, 8 or 16, more than 8 for an Indexed space, a Pattern
    /// space or one of another document), the buffer has the wrong length, or <see cref="PdfImageCompression.Dct"/> was asked for
    /// samples it cannot take (not 8-bit, an Indexed space, or not 1, 3 or 4 components).
    /// </exception>
    /// <remarks>
    /// ISO 32000-2 §8.9.5 (Table 87), §7.4.4 (FlateDecode with Predictor 15, Table 8) or §7.4.8 (DCTDecode, through the document's
    /// DCT encoder). Records the versions of the compression, the colour space and 16-bit samples (PDF 1.5). A mutation.
    /// </remarks>
    public PdfImage CreateImage(ReadOnlySpan<byte> samples, PdfImageLayout layout, PdfImageCompression compression) =>
        CreateImage(samples, [], hasAlpha: false, layout, compression);

    /// <summary>Creates an image XObject with a soft mask from buffers of samples and alpha, or returns the one this document already has for the same buffers.</summary>
    /// <param name="samples">The colour samples, as for <see cref="CreateImage(ReadOnlySpan{byte}, PdfImageLayout, PdfImageCompression)"/>.</param>
    /// <param name="alpha">The opacity of each pixel, one sample per pixel at the layout's bits per component (0 transparent, the maximum opaque), rows byte-aligned: ceil(Width × BitsPerComponent / 8) × Height bytes.</param>
    /// <param name="layout">The size, colour space and bits per component of both buffers.</param>
    /// <param name="compression">How to compress the colour samples; the alpha is always compressed with Flate.</param>
    /// <returns>The image, an indirect stream whose <c>SMask</c> is a DeviceGray image of the alpha.</returns>
    /// <exception cref="ArgumentException">As for the other overload, or <paramref name="alpha"/> has the wrong length.</exception>
    /// <remarks>ISO 32000-2 §8.9.5 and §11.6.5.2 (soft-mask images, PDF 1.4). As the other overload; also records PDF 1.4. A mutation.</remarks>
    public PdfImage CreateImage(ReadOnlySpan<byte> samples, ReadOnlySpan<byte> alpha, PdfImageLayout layout, PdfImageCompression compression) =>
        CreateImage(samples, alpha, hasAlpha: true, layout, compression);

    /// <summary>Creates an Indexed colour space over <paramref name="baseSpace"/>, or returns the one this document already has for the same table.</summary>
    /// <param name="baseSpace">The space of the table's colours: a device, CIE-based, ICCBased, Separation or DeviceN space of this document; not Pattern or Indexed.</param>
    /// <param name="lookup">The colour table: one entry per index from 0, each the base space's components as bytes (0 the minimum of a component's range, 255 the maximum); 1 to 256 entries.</param>
    /// <returns>The space: <c>[/Indexed base hival lookup]</c>, an indirect array whose lookup is a stream.</returns>
    /// <exception cref="ArgumentException"><paramref name="baseSpace"/> is Pattern, Indexed or another document's, or <paramref name="lookup"/> is not 1 to 256 whole entries.</exception>
    /// <remarks>ISO 32000-2 §8.6.6.3 (PDF 1.1; the lookup is a stream, which every version allows). Records PDF 1.1 and the base space's version. A mutation.</remarks>
    public PdfIndexedColorSpace CreateIndexedColorSpace(PdfColorSpace baseSpace, ReadOnlySpan<byte> lookup)
    {
        ArgumentNullException.ThrowIfNull(baseSpace);
        if (baseSpace.Family is PdfColorSpaceFamily.Pattern or PdfColorSpaceFamily.Indexed)
        {
            throw new ArgumentException("The base of an Indexed colour space shall not be a Pattern or Indexed space (ISO 32000-2 §8.6.6.3).", nameof(baseSpace));
        }

        ThrowIfOtherDocument(baseSpace, nameof(baseSpace));
        int components = baseSpace.ComponentCount;
        if (lookup.IsEmpty || lookup.Length % components != 0 || lookup.Length / components > 256)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"The colour table shall hold 1 to 256 entries of {components} bytes each (ISO 32000-2 §8.6.6.3); it has {lookup.Length} bytes."),
                nameof(lookup));
        }

        RequireFeature(VersionedFeatures.CieBasedColorSpace);
        if (VersionedFeatures.ColorSpace(baseSpace) is { } feature)
        {
            RequireFeature(feature);
        }

        byte[] table = lookup.ToArray();
        string? key = DrawingCache.Key(baseSpace) is { } baseKey ? $"Indexed|{baseKey}|{Hash(table)}" : null;
        CosReference reference = Drawing.GetOrAdd(key, () => new CosArray
        {
            ColorSpaceNames.Indexed,
            DrawingCache.Value(baseSpace),
            new CosInteger((table.Length / components) - 1),
            AddObject(new CosStream([], table)),
        });
        return (PdfIndexedColorSpace)GetColorSpace(reference);
    }

    private static string Hash(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data));

    /// <summary>The DecodeParms of PNG predictors chosen row by row (§7.4.4.4, Table 8).</summary>
    private static CosDictionary Predicted(int colors, int bitsPerComponent, int columns) => new()
    {
        [FilterNames.Predictor] = PngOptimum,
        [FilterNames.Colors] = new CosInteger(colors),
        [FilterNames.BitsPerComponent] = new CosInteger(bitsPerComponent),
        [FilterNames.Columns] = new CosInteger(columns),
    };

    /// <summary>An image XObject stream dictionary (Table 87) with one filter, and no data yet.</summary>
    private static CosStream ImageStream(int width, int height, PdfColorSpace space, int bitsPerComponent, CosName filter, CosDictionary? parameters)
    {
        var dictionary = new CosDictionary
        {
            [ImageNames.Type] = XObjectType,
            [ImageNames.Subtype] = ImageNames.Image,
            [ImageNames.Width] = new CosInteger(width),
            [ImageNames.Height] = new CosInteger(height),
            [ImageNames.ColorSpace] = DrawingCache.Value(space),
            [ImageNames.BitsPerComponent] = new CosInteger(bitsPerComponent),
            [FilterNames.Filter] = filter,
        };
        if (parameters is not null)
        {
            dictionary[FilterNames.DecodeParms] = parameters;
        }

        return new CosStream(dictionary, ReadOnlyMemory<byte>.Empty);
    }

    /// <summary>Adds a DeviceGray soft-mask image of <paramref name="alpha"/> (§11.6.5.2, Table 144) and returns its reference.</summary>
    private CosReference SoftMask(ReadOnlySpan<byte> alpha, int width, int height, int bitsPerComponent)
    {
        CosStream mask = ImageStream(width, height, PdfDeviceGrayColorSpace.Instance, bitsPerComponent, FilterNames.FlateDecode, Predicted(1, bitsPerComponent, width));
        EncodeStream(mask, alpha);
        RequireFeature(VersionedFeatures.SoftMasks);
        return AddObject(mask);
    }

    private void RequireImageFeatures(int bitsPerComponent)
    {
        if (bitsPerComponent == 16)
        {
            RequireFeature(VersionedFeatures.SixteenBitImages);
        }
    }

    private PdfImage CreateImage(ReadOnlySpan<byte> samples, ReadOnlySpan<byte> alpha, bool hasAlpha, PdfImageLayout layout, PdfImageCompression compression)
    {
        PdfColorSpace space = layout.ColorSpace ?? throw new ArgumentException("The layout has no colour space.", nameof(layout));
        if (layout.Width < 1 || layout.Height < 1 || layout.BitsPerComponent is not (1 or 2 or 4 or 8 or 16)
            || space.Family == PdfColorSpaceFamily.Pattern || (space.Family == PdfColorSpaceFamily.Indexed && layout.BitsPerComponent > 8))
        {
            throw new ArgumentException(
                "The layout shall have a size of at least 1 x 1, 1, 2, 4, 8 or 16 bits per component (at most 8 for an Indexed space), and a colour space that is not Pattern (ISO 32000-2 §8.9.5.1, Table 87).",
                nameof(layout));
        }

        if (!Enum.IsDefined(compression))
        {
            throw new ArgumentOutOfRangeException(nameof(compression), compression, "Not a defined PdfImageCompression.");
        }

        ThrowIfOtherDocument(space, nameof(layout));
        int components = space.ComponentCount;
        long expected = layout.Stride(components) * layout.Height;
        if (samples.Length != expected)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"The samples are {samples.Length} bytes; a {layout.Width} x {layout.Height} image of {components} components at {layout.BitsPerComponent} bits is {expected} (ISO 32000-2 §8.9.3)."),
                nameof(samples));
        }

        long expectedAlpha = layout.Stride(1) * layout.Height;
        if (hasAlpha && alpha.Length != expectedAlpha)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"The alpha is {alpha.Length} bytes; one {layout.BitsPerComponent}-bit sample per pixel is {expectedAlpha}."),
                nameof(alpha));
        }

        bool dct = compression == PdfImageCompression.Dct;
        if (dct && (layout.BitsPerComponent != 8 || space.Family == PdfColorSpaceFamily.Indexed || components is not (1 or 3 or 4)))
        {
            throw new ArgumentException("DCT compression takes 8-bit samples of 1, 3 or 4 components in a space that is not Indexed (ISO 32000-2 §7.4.8).", nameof(compression));
        }

        string? spaceKey = DrawingCache.Key(space);
        string? key = spaceKey is null
            ? null
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Image|Raw|{layout.Width}|{layout.Height}|{layout.BitsPerComponent}|{spaceKey}|{compression}|{Hash(samples)}|{(hasAlpha ? Hash(alpha) : "-")}");
        if (key is not null && Drawing.TryGet(key, out CosReference known))
        {
            return GetImage(known)!;
        }

        CosStream stream = dct
            ? ImageStream(layout.Width, layout.Height, space, 8, FilterNames.DctDecode, null)
            : ImageStream(layout.Width, layout.Height, space, layout.BitsPerComponent, FilterNames.FlateDecode, Predicted(components, layout.BitsPerComponent, layout.Width));
        EncodeStream(stream, samples);
        if (VersionedFeatures.ColorSpace(space) is { } feature)
        {
            RequireFeature(feature);
        }

        if (hasAlpha)
        {
            stream.Dictionary[ImageNames.SMask] = SoftMask(alpha, layout.Width, layout.Height, layout.BitsPerComponent);
        }

        RequireImageFeatures(layout.BitsPerComponent);
        CosReference reference = key is null ? AddObject(stream) : Drawing.Add(key, stream);
        return GetImage(reference)!;
    }

    private void ThrowIfOtherDocument(PdfColorSpace space, string name)
    {
        if (space.Cache is { } cache && !ReferenceEquals(cache.Document, this))
        {
            throw new ArgumentException("The colour space belongs to another document.", name);
        }
    }
}
