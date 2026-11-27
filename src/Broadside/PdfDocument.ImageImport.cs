using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Graphics;
using Broadside.Graphics.Colors;
using Broadside.Images;
using Broadside.Images.Import;
using Broadside.Images.Png;
using Broadside.Objects;
using Broadside.Writing;

namespace Broadside;

/// <summary>Importing images (issue #642): TIFF, GIF and BMP files, and any of the supported formats by their signature.</summary>
public sealed partial class PdfDocument
{
    private static readonly CosName CcittK = new("K");
    private static readonly CosName CcittColumns = new("Columns");
    private static readonly CosName CcittRows = new("Rows");
    private static readonly CosName CcittBlackIs1 = new("BlackIs1");
    private static readonly CosName CcittEncodedByteAlign = new("EncodedByteAlign");
    private static readonly CosName CcittEndOfLine = new("EndOfLine");

    // What the files said about each imported image's resolution and orientation, for AppendImagePages, by the image's object.
    private Dictionary<CosReference, ImportedPlacement>? _importPlacements;

    /// <summary>Creates one image XObject per page of a TIFF file, or returns the ones this document already has for the same file.</summary>
    /// <param name="tiff">The TIFF file: baseline TIFF 6.0 with the common extensions (see the remarks).</param>
    /// <returns>The images, one per page (image file directory) in file order; reduced-resolution images (thumbnails) are skipped.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="tiff"/> is not a TIFF file, has no page, is malformed (a directory, strip or tile outside the file, a missing
    /// required field), or has a page with more pixels than <see cref="PdfOptions.MaxImagePixels"/>.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// A page uses what the importer does not read: old-style JPEG (compression 6), BigTIFF, floating-point or signed samples,
    /// samples other than 1, 2, 4, 8 or 16 bits, or a photometric interpretation other than those in the remarks.
    /// </exception>
    /// <remarks>
    /// <para>
    /// TIFF 6.0 (Adobe, 1992) and TIFF Technical Note 2: both byte orders, strips and tiles, chunky and planar samples
    /// (<c>PlanarConfiguration</c> 1 and 2) of 1, 2, 4, 8 and 16 bits; WhiteIsZero and BlackIsZero become <c>DeviceGray</c>, RGB
    /// <c>DeviceRGB</c>, palette an <c>Indexed</c> space (ISO 32000-2 §8.6.6.3), CMYK (<c>PhotometricInterpretation</c> 5)
    /// <c>DeviceCMYK</c>, and YCbCr JPEG data <c>DeviceRGB</c>; an embedded ICC profile of the right number of components becomes an
    /// <c>ICCBased</c> space.
    /// </para>
    /// <para>
    /// CCITT data (compression 2, 3 and 4) passes through as <c>CCITTFaxDecode</c> data (§7.4.6, Table 11) with <c>K</c>,
    /// <c>Columns</c>, <c>Rows</c>, <c>BlackIs1</c>, <c>EndOfLine</c> and <c>EncodedByteAlign</c> set from the compression,
    /// <c>T4Options</c> and the photometric interpretation, bit-reversed when <c>FillOrder</c> is 2, when the page is one strip; Modified
    /// Huffman data (compression 2) in several strips is joined, since its rows are independent and byte-aligned. Group 3 and Group 4
    /// data in several strips cannot be joined (each strip starts from a white reference line and ends with RTC or EOFB) and is
    /// decoded. JPEG data (compression 7) in one strip or tile of the page's size passes through as <c>DCTDecode</c> data (§7.4.8)
    /// with the <c>JPEGTables</c> merged in. Everything else is decoded (none, LZW with or without the horizontal predictor, Deflate,
    /// PackBits, and CCITT or JPEG data that cannot pass through) and written as <c>FlateDecode</c> data with the PNG predictors
    /// (§7.4.4.4). CCITT and LZW are PDF 1.0, Flate 1.2, 16-bit samples 1.5; each feature's version is recorded.
    /// </para>
    /// <para>
    /// The first extra sample, when <c>ExtraSamples</c> says it is associated or unassociated alpha, becomes a soft mask (<c>SMask</c>,
    /// PDF 1.4, §11.6.5.2) unless every pixel is opaque; associated (premultiplied) alpha gets a <c>Matte</c> of 0 (§11.6.5.3). A
    /// strip or tile whose byte count is wrong is read up to the next one, and other benign repairs are made, each recorded as an
    /// <see cref="ImageDiagnostics.ImageImportRepaired"/> diagnostic, which throws in strict mode. A mutation.
    /// </para>
    /// </remarks>
    public IReadOnlyList<PdfImage> CreateImagesFromTiff(ReadOnlySpan<byte> tiff)
    {
        List<long> pages = ListTiffPages(tiff, out bool little);
        string hash = Hash(tiff);
        var images = new PdfImage[pages.Count];
        for (int i = 0; i < images.Length; i++)
        {
            images[i] = TiffPage(tiff, hash, pages[i], little, i);
        }

        return images;
    }

    /// <summary>Creates an image XObject from one page of a TIFF file, or returns the one this document already has for it.</summary>
    /// <param name="tiff">The TIFF file, as for <see cref="CreateImagesFromTiff"/>.</param>
    /// <param name="pageIndex">The page, from 0, counted as <see cref="CreateImagesFromTiff"/> counts them.</param>
    /// <returns>The image, the same object <see cref="CreateImagesFromTiff"/> returns for that page.</returns>
    /// <exception cref="ArgumentException">As for <see cref="CreateImagesFromTiff"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageIndex"/> is negative or not less than the number of pages.</exception>
    /// <exception cref="NotSupportedException">As for <see cref="CreateImagesFromTiff"/>.</exception>
    /// <remarks>ISO 32000-2 §8.9.5. As <see cref="CreateImagesFromTiff"/>, reading only the page asked for. A mutation.</remarks>
    public PdfImage CreateImageFromTiff(ReadOnlySpan<byte> tiff, int pageIndex)
    {
        List<long> pages = ListTiffPages(tiff, out bool little);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, pages.Count);
        return TiffPage(tiff, Hash(tiff), pages[pageIndex], little, pageIndex);
    }

    /// <summary>Creates an image XObject from the first image of a GIF file, or returns the one this document already has for the same file.</summary>
    /// <param name="gif">The GIF file (GIF87a or GIF89a).</param>
    /// <returns>The image: palette indices in an <c>Indexed</c> space over <c>DeviceRGB</c>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="gif"/> is not a GIF file, has no image or color table, is malformed, or has more pixels than
    /// <see cref="PdfOptions.MaxImagePixels"/>.
    /// </exception>
    /// <remarks>
    /// ISO 32000-2 §8.9.5, §8.6.6.3 (Indexed) and §8.9.6.4 (color key masks); GIF89a (CompuServe, 1990). The first image descriptor,
    /// at its own size: LZW-decoded (Appendix F), interlaced rows put back in order (Appendix E), the local color table or else the
    /// global one as the <c>Indexed</c> lookup, the indices at 1, 2, 4 or 8 bits as the table needs, written as <c>FlateDecode</c>
    /// data with the PNG predictors. The Graphic Control Extension's transparent index becomes a color key mask
    /// (<c>Mask [i i]</c>, PDF 1.3). Later frames of an animation are ignored. Image data that ends early keeps the pixels decoded
    /// (the rest are index 0), recorded as an <see cref="ImageDiagnostics.ImageImportRepaired"/> diagnostic. A mutation.
    /// </remarks>
    public PdfImage CreateImageFromGif(ReadOnlySpan<byte> gif)
    {
        string key = "Image|GIF|" + Hash(gif);
        if (Drawing.TryGet(key, out CosReference known))
        {
            return GetImage(known)!;
        }

        ImportedImage image;
        try
        {
            image = GifReader.Read(gif, Streams.MaxImagePixels);
        }
        catch (InvalidDataException e)
        {
            throw new ArgumentException(e.Message, nameof(gif), e);
        }

        using (image)
        {
            return WriteImported(image, key);
        }
    }

    /// <summary>Creates an image XObject from a Windows bitmap (BMP) file, or returns the one this document already has for the same file.</summary>
    /// <param name="bmp">The bitmap file.</param>
    /// <returns>The image.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="bmp"/> is not a BMP file, is malformed (an unknown compression, a bit depth it does not allow, a missing color
    /// table or pixel data), or has more pixels than <see cref="PdfOptions.MaxImagePixels"/>.
    /// </exception>
    /// <exception cref="NotSupportedException">The bitmap holds JPEG or PNG data (BI_JPEG, BI_PNG) or is an OS/2 Huffman or RLE24 bitmap.</exception>
    /// <remarks>
    /// ISO 32000-2 §8.9.5, §8.6.6.3 and §11.6.5.2. Every header from BITMAPCOREHEADER to BITMAPV5HEADER: 1, 4 and 8 bits (and
    /// BI_RLE4, BI_RLE8) become indices into an <c>Indexed</c> space; 16, 24 and 32 bits (and BI_BITFIELDS) become 8-bit
    /// <c>DeviceRGB</c>, 16-bit pixels without bit fields being 5-5-5. An alpha mask (BI_BITFIELDS with a V3 or later header, or
    /// BI_ALPHABITFIELDS) becomes a soft mask unless every pixel is opaque. Bottom-up and top-down rows are both read. An embedded
    /// ICC profile (V5) becomes an <c>ICCBased</c> space. The samples are written as <c>FlateDecode</c> data with the PNG predictors.
    /// A mutation.
    /// </remarks>
    public PdfImage CreateImageFromBmp(ReadOnlySpan<byte> bmp)
    {
        string key = "Image|BMP|" + Hash(bmp);
        if (Drawing.TryGet(key, out CosReference known))
        {
            return GetImage(known)!;
        }

        ImportedImage image;
        try
        {
            image = BmpReader.Read(bmp, Streams.MaxImagePixels);
        }
        catch (InvalidDataException e)
        {
            throw new ArgumentException(e.Message, nameof(bmp), e);
        }

        using (image)
        {
            return WriteImported(image, key);
        }
    }

    /// <summary>Creates the image XObjects of an encoded image file of any supported format, chosen by its signature.</summary>
    /// <param name="encoded">A JPEG, PNG, TIFF, GIF or BMP file.</param>
    /// <returns>The images: one per page of a TIFF file, one for any other format.</returns>
    /// <exception cref="ArgumentException"><paramref name="encoded"/> has none of the five signatures, or the format's reader refuses it.</exception>
    /// <exception cref="NotSupportedException">The TIFF or BMP reader does not support a feature the file uses.</exception>
    /// <remarks>
    /// ISO 32000-2 §8.9.5. The same as <see cref="CreateImageFromJpeg"/>, <see cref="CreateImageFromPng"/>,
    /// <see cref="CreateImagesFromTiff"/>, <see cref="CreateImageFromGif"/> or <see cref="CreateImageFromBmp"/>. A mutation.
    /// </remarks>
    public IReadOnlyList<PdfImage> CreateImages(ReadOnlySpan<byte> encoded) => SniffImage(encoded) switch
    {
        ImageFormat.Jpeg => [CreateImageFromJpeg(encoded)],
        ImageFormat.Png => [CreateImageFromPng(encoded)],
        ImageFormat.Tiff => CreateImagesFromTiff(encoded),
        ImageFormat.Gif => [CreateImageFromGif(encoded)],
        ImageFormat.Bmp => [CreateImageFromBmp(encoded)],
        _ => throw new ArgumentException("The data is not a JPEG, PNG, TIFF, GIF or BMP file: it starts with none of their signatures.", nameof(encoded)),
    };

    /// <summary>Returns the format of an encoded image by its signature.</summary>
    internal static ImageFormat SniffImage(ReadOnlySpan<byte> encoded)
    {
        if (encoded is [0xFF, 0xD8, 0xFF, ..])
        {
            return ImageFormat.Jpeg;
        }

        if (PngReader.IsPng(encoded))
        {
            return ImageFormat.Png;
        }

        if (TiffReader.IsTiff(encoded))
        {
            return ImageFormat.Tiff;
        }

        if (GifReader.IsGif(encoded))
        {
            return ImageFormat.Gif;
        }

        return BmpReader.IsBmp(encoded) ? ImageFormat.Bmp : ImageFormat.Unknown;
    }

    /// <summary>The images of an encoded file with what the file says about their resolution and orientation (for <see cref="PdfImagePageExtensions"/>).</summary>
    internal List<(PdfImage Image, ImportedPlacement Placement)> CreatePlacedImages(ReadOnlySpan<byte> encoded)
    {
        IReadOnlyList<PdfImage> images = CreateImages(encoded);
        var placed = new List<(PdfImage, ImportedPlacement)>(images.Count);
        foreach (PdfImage image in images)
        {
            ImportedPlacement placement = default;
            if (_importPlacements is not null && image.Reference is { } reference)
            {
                _importPlacements.TryGetValue(reference, out placement);
            }

            placed.Add((image, placement));
        }

        return placed;
    }

    private static List<long> ListTiffPages(ReadOnlySpan<byte> tiff, out bool little)
    {
        try
        {
            return TiffReader.ListPages(tiff, out little);
        }
        catch (InvalidDataException e)
        {
            throw new ArgumentException(e.Message, nameof(tiff), e);
        }
    }

    private PdfImage TiffPage(ReadOnlySpan<byte> tiff, string hash, long offset, bool little, int index)
    {
        string key = string.Create(CultureInfo.InvariantCulture, $"Image|TIFF|{hash}|{index}");
        if (Drawing.TryGet(key, out CosReference known))
        {
            return GetImage(known)!;
        }

        ImportedImage image;
        try
        {
            image = TiffReader.ReadPage(tiff, offset, little, Streams.MaxImagePixels);
        }
        catch (InvalidDataException e)
        {
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"TIFF page {index}: {e.Message}"), nameof(tiff), e);
        }

        using (image)
        {
            return WriteImported(image, key);
        }
    }

    /// <summary>Writes an imported image as an image XObject (§8.9.5, Table 87) under <paramref name="key"/>, recording its repairs.</summary>
    private PdfImage WriteImported(ImportedImage image, string key)
    {
        foreach (string repair in image.Repairs)
        {
            _diagnostics.Report(ImageDiagnostics.ImageImportRepaired, DiagnosticSeverity.Warning, repair);
        }

        bool palette = image.ColorModel == ImportedColor.Palette;
        int components = palette ? 3 : image.Components;
        PdfColorSpace space = components switch
        {
            3 => PdfDeviceRgbColorSpace.Instance,
            4 => PdfDeviceCmykColorSpace.Instance,
            _ => PdfDeviceGrayColorSpace.Instance,
        };
        if (image.IccProfile is { } profile && IccProfileHeader.Parse(profile) is { IsSupportedForPdf: true } header && header.ComponentCount == components)
        {
            space = CreateIccBasedColorSpace(profile);
        }

        if (palette)
        {
            space = CreateIndexedColorSpace(space, image.Palette);
        }

        CosStream stream;
        switch (image.PassThrough)
        {
            case ImportedPassThrough.Ccitt:
                ImportedCcitt ccitt = image.Ccitt;
                var parameters = new CosDictionary
                {
                    [CcittK] = new CosInteger(ccitt.K),
                    [CcittColumns] = new CosInteger(image.Width),
                    [CcittRows] = new CosInteger(image.Height),
                };
                if (ccitt.EndOfLine)
                {
                    parameters[CcittEndOfLine] = CosBoolean.True;
                }

                if (ccitt.EncodedByteAlign)
                {
                    parameters[CcittEncodedByteAlign] = CosBoolean.True;
                }

                if (ccitt.BlackIs1)
                {
                    parameters[CcittBlackIs1] = CosBoolean.True;
                }

                stream = ImageStream(image.Width, image.Height, space, 1, FilterNames.CcittFaxDecode, parameters);
                stream.EncodedData = image.Encoded!;
                break;
            case ImportedPassThrough.Dct:
                CosDictionary? transform = image.ColorTransform >= 0 ? new CosDictionary { [FilterNames.ColorTransform] = new CosInteger(image.ColorTransform) } : null;
                stream = ImageStream(image.Width, image.Height, space, 8, FilterNames.DctDecode, transform);
                stream.EncodedData = image.Encoded!;
                break;
            default:
                stream = ImageStream(image.Width, image.Height, space, image.BitsPerComponent, FilterNames.FlateDecode, Predicted(image.Components, image.BitsPerComponent, image.Width));
                EncodeStream(stream, image.Color);
                break;
        }

        if (VersionedFeatures.ColorSpace(space) is { } feature)
        {
            RequireFeature(feature);
        }

        if (image.TransparentIndex >= 0)
        {
            stream.Dictionary[ImageNames.Mask] = new CosArray { new CosInteger(image.TransparentIndex), new CosInteger(image.TransparentIndex) };
            RequireFeature(VersionedFeatures.ImageMasks);
        }

        if (image.HasAlpha)
        {
            CosReference mask = SoftMask(image.Alpha, image.Width, image.Height, image.BitsPerComponent);
            stream.Dictionary[ImageNames.SMask] = mask;
            if (image.Premultiplied && !palette && Resolve(mask) is CosStream maskStream)
            {
                var matte = new CosArray();
                for (int i = 0; i < space.ComponentCount; i++)
                {
                    matte.Add(new CosInteger(0));
                }

                maskStream.Dictionary[ImageNames.Matte] = matte;
            }
        }

        RequireImageFeatures(image.BitsPerComponent);
        CosReference reference = Drawing.Add(key, stream);
        (_importPlacements ??= [])[reference] = new ImportedPlacement(image.DpiX, image.DpiY, image.Orientation);
        return GetImage(reference)!;
    }
}

/// <summary>The formats <see cref="PdfDocument.CreateImages"/> recognizes.</summary>
internal enum ImageFormat
{
    /// <summary>None of them.</summary>
    Unknown,

    /// <summary>JPEG (JFIF, Exif, Adobe).</summary>
    Jpeg,

    /// <summary>PNG.</summary>
    Png,

    /// <summary>TIFF.</summary>
    Tiff,

    /// <summary>GIF.</summary>
    Gif,

    /// <summary>BMP.</summary>
    Bmp,
}

/// <summary>What an image file says about an image's placement: resolution in pixels per inch (0 when unknown) and orientation (1 to 8).</summary>
internal readonly record struct ImportedPlacement(double DpiX, double DpiY, int Orientation);
