using System.Globalization;
using System.Runtime.CompilerServices;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Images;

/// <summary>
/// An image: an image XObject, an inline image, or the mask or alternate of one. A live view over the image dictionary that reads
/// its entries with their defaults and decodes its samples on request.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.9. Get one with <see cref="PdfDocument.GetImage"/>, <see cref="PdfPage.GetImage"/> or, for an inline image in a
/// content stream, <see cref="Content.ContentContext.GetInlineImage"/>. Every property reads the dictionary on every call (ADR 0004).
/// Deviations are repaired the way most readers repair them and recorded as diagnostics on the document (a property of an image that
/// cannot be painted at all, such as one without a width, reads as 0 or <see langword="null"/>); in strict mode the first deviation
/// throws from the member that meets it.
/// </para>
/// <para>
/// <see cref="Decode"/> returns the samples in the §8.9.3 layout as a <see cref="DecodedImage"/>; the Decode array, colour
/// conversion and masks are applied by the reader of the samples, with <see cref="CreateDecodeMap"/>, <see cref="ImageRows"/> and
/// <see cref="PdfDocument.GetColorConverter(PdfColorSpace)"/>. Masks are decoded at their own size (<see cref="Mask"/>), never
/// resampled here. Nothing is cached; each <see cref="Decode"/> decodes again.
/// </para>
/// <para>Thread-safe for concurrent reads while the document is not mutated.</para>
/// </remarks>
public sealed class PdfImage
{
    private readonly PdfDocument _document;
    private readonly CosStream _stream;
    private readonly PdfColorSpace? _inlineColorSpace;
    private readonly PdfImage? _parent;
    private readonly ImageRole _role;
    private readonly CosReference? _owner;

    internal PdfImage(PdfDocument document, CosStream stream, CosReference? reference, ImageRole role, PdfImage? parent = null, CosReference? owner = null, PdfColorSpace? inlineColorSpace = null)
    {
        _document = document;
        _stream = stream;
        Reference = reference;
        _role = role;
        _parent = parent;
        _owner = reference ?? owner ?? parent?._owner;
        _inlineColorSpace = inlineColorSpace;
    }

    /// <summary>The part an image plays, which changes the defaults and rules that apply to it.</summary>
    internal enum ImageRole
    {
        /// <summary>An image XObject painted by <c>Do</c>.</summary>
        XObject,

        /// <summary>An inline image (§8.9.7).</summary>
        Inline,

        /// <summary>The <c>Mask</c> stream of another image (§8.9.6.3).</summary>
        ExplicitMask,

        /// <summary>The <c>SMask</c> of another image (§11.6.5.2).</summary>
        SoftMask,

        /// <summary>An alternate image (§8.9.5.4).</summary>
        Alternate,
    }

    /// <summary>Gets the image XObject's stream; <see langword="null"/> for an inline image.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.</remarks>
    public CosStream? Stream => IsInline ? null : _stream;

    /// <summary>Gets the image dictionary; for an inline image, its entries with the abbreviations of Tables 91 and 92 expanded.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87; §8.9.7.</remarks>
    public CosDictionary Dictionary => _stream.Dictionary;

    /// <summary>Gets the indirect reference to the image's stream, or <see langword="null"/> when it is direct or inline.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets a value indicating whether this is an inline image.</summary>
    /// <remarks>ISO 32000-2 §8.9.7.</remarks>
    public bool IsInline => _role == ImageRole.Inline;

    /// <summary>Gets the image's width in samples; 0 when <c>Width</c> is missing or not positive.</summary>
    /// <remarks>
    /// ISO 32000-2 §8.9.5.1, Table 87 (required). A real with an integral value is accepted; another real is truncated with a
    /// diagnostic. A mask without a usable width takes its image's, with a diagnostic.
    /// </remarks>
    public int Width => ReadLayout().Width;

    /// <summary>Gets the image's height in samples; 0 when <c>Height</c> is missing or not positive.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87 (required).</remarks>
    public int Height => ReadLayout().Height;

    /// <summary>
    /// Gets the bits per component: 1 for an image mask, the dictionary's value otherwise; 0 when it is missing or unusable, and for a
    /// JPEG 2000 image that does not state it (the codestream decides).
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §8.9.5.1, Table 87: 1, 2, 4, 8 or (PDF 1.5) 16. Other depths up to 16 are read, with a diagnostic, as other readers
    /// do; the samples are then stored at the next wider depth (<see cref="DecodedImage.StorageBits"/>).
    /// </remarks>
    public int BitsPerComponent => ReadLayout().BitsPerComponent;

    /// <summary>Gets a value indicating whether the image is a stencil mask (<c>ImageMask true</c>), painted with the current fill colour.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87, and §8.9.6.2.</remarks>
    public bool IsStencil => ReadLayout().IsStencil;

    /// <summary>
    /// Gets the colour space of the samples; <see langword="null"/> for an image mask, for a JPEG 2000 image without one (see
    /// <see cref="ResolveColorSpace"/>), and when it is missing or unusable.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §8.9.5.1, Table 87 (any space but Pattern), §8.9.7 for inline images (abbreviated and resource names), §11.6.5.2
    /// for a soft mask (DeviceGray).
    /// </remarks>
    public PdfColorSpace? ColorSpace => ReadLayout().ColorSpace;

    /// <summary>
    /// Gets the Decode array in effect: the dictionary's, else the default for the colour space and depth (Table 88). Two numbers per
    /// component; empty when the image cannot be decoded.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §8.9.5.2, Table 88. An Indexed image defaults to <c>[0 2^n−1]</c>; an image mask to <c>[0 1]</c>. An array too
    /// short or not numeric is replaced by the default, one too long cut, both with a diagnostic. A JPEG 2000 image without a colour
    /// space ignores the array (§7.4.9).
    /// </remarks>
    public IReadOnlyList<double> DecodeArray => ReadLayout().Decode;

    /// <summary>Gets a value indicating whether the image asks to be smoothed when scaled (<c>Interpolate</c>, default false).</summary>
    /// <remarks>ISO 32000-2 §8.9.5.3: a hint the renderer may ignore.</remarks>
    public bool Interpolate => ReadEntry(ImageNames.Interpolate, ImageNames.I) is CosBoolean { Value: true };

    /// <summary>Gets the rendering intent for the image's colours (<c>Intent</c>, PDF 1.1), or <see langword="null"/> to use the graphics state's.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87, and §8.6.5.8: an unknown name reads as relative colorimetric; ignored for an image mask.</remarks>
    public RenderingIntent? Intent => IsStencil || ReadEntry(ImageNames.Intent, null) is not CosName name
        ? null
        : name.Value switch
        {
            "AbsoluteColorimetric" => RenderingIntent.AbsoluteColorimetric,
            "Saturation" => RenderingIntent.Saturation,
            "Perceptual" => RenderingIntent.Perceptual,
            _ => RenderingIntent.RelativeColorimetric,
        };

    /// <summary>Gets which masking applies to the image.</summary>
    /// <remarks>
    /// ISO 32000-2 §8.9.6, §11.6.4.3, Table 87: an image mask is a stencil; otherwise <c>SMaskInData</c> (non-zero, JPEG 2000 only)
    /// wins over <c>SMask</c>, which wins over <c>Mask</c>. A <c>Mask</c> stream that is not an image mask, a non-stream <c>SMask</c>
    /// and a colour key array of the wrong length are ignored with a diagnostic, as other readers do. Masks have no masks of their own.
    /// </remarks>
    public PdfImageMaskKind MaskKind => ReadMask(out _);

    /// <summary>Gets the mask image: the <c>Mask</c> image mask for <see cref="PdfImageMaskKind.Explicit"/>, the <c>SMask</c> for <see cref="PdfImageMaskKind.Soft"/>; otherwise <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §8.9.6.3 and §11.6.5.2: decoded at its own width and height; both images map onto the unit square.</remarks>
    public PdfImage? Mask
    {
        get
        {
            PdfImageMaskKind kind = ReadMask(out CosStream? mask);
            return kind is PdfImageMaskKind.Explicit or PdfImageMaskKind.Soft && mask is not null
                ? new PdfImage(_document, mask, ReadRaw(kind == PdfImageMaskKind.Soft ? ImageNames.SMask : ImageNames.Mask) as CosReference, kind == PdfImageMaskKind.Soft ? ImageRole.SoftMask : ImageRole.ExplicitMask, this)
                : null;
        }
    }

    /// <summary>
    /// Gets the colour key ranges for <see cref="PdfImageMaskKind.ColorKey"/>: a minimum and maximum raw sample value per component;
    /// otherwise <see langword="null"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.9.6.4: compared with the samples before the Decode array; for an Indexed image the values are indices.</remarks>
    public IReadOnlyList<double>? ColorKey => ReadMask(out _) == PdfImageMaskKind.ColorKey
        && Resolve(ReadRaw(ImageNames.Mask)) is CosArray array
        && ReadNumbers(array, 2 * ReadLayout().Components) is { } ranges
            ? ranges
            : null;

    /// <summary>
    /// Gets the colour the samples were pre-blended with (the soft mask's <c>Matte</c>), one value per component of the image's colour
    /// space (of its base for Indexed); <see langword="null"/> when there is none or it cannot apply.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §11.6.5.2, Table 144 (PDF 1.4): undo it with <see cref="ImageRows.Unpremultiply"/>. A Matte of the wrong length, or
    /// on a soft mask whose size differs from the image's (the samples would not line up), is ignored with a diagnostic.
    /// </remarks>
    public IReadOnlyList<double>? Matte
    {
        get
        {
            if (ReadMask(out CosStream? mask) != PdfImageMaskKind.Soft || mask is null || Resolve(Get(mask.Dictionary, ImageNames.Matte)) is not CosArray array)
            {
                return null;
            }

            ImageLayout layout = ReadLayout();
            PdfColorSpace? space = layout.ColorSpace;
            int components = space is PdfIndexedColorSpace indexed ? indexed.Base.ComponentCount : space?.ComponentCount ?? 0;
            double[]? matte = ReadNumbers(array, components);
            if (matte is null || array.Count != components)
            {
                Report(DiagnosticCodes.ImageMatteInvalid, $"The soft mask's Matte has {array.Count} entries where the image's colour space has {components} components; it is ignored.");
                return null;
            }

            var soft = new PdfImage(_document, mask, ReadRaw(ImageNames.SMask) as CosReference, ImageRole.SoftMask, this);
            if (soft.Width != layout.Width || soft.Height != layout.Height)
            {
                Report(DiagnosticCodes.ImageMatteInvalid, FormattableString.Invariant(
                    $"A soft mask with Matte shall have its image's size ({layout.Width} x {layout.Height}), not {soft.Width} x {soft.Height}; the Matte is ignored."));
                return null;
            }

            return matte;
        }
    }

    /// <summary>Gets <c>SMaskInData</c>: 0 (no opacity in the data), 1 (opacity), 2 (opacity premultiplied into the colour).</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87 (PDF 1.5): meaningful only for JPEG 2000 data.</remarks>
    public int SoftMaskInData => ReadEntry(ImageNames.SMaskInData, null) is CosInteger { Value: 1 or 2 } value ? (int)value.Value : 0;

    /// <summary>Gets the image's alternates, in array order; empty when it has none (and always for an alternate).</summary>
    /// <remarks>ISO 32000-2 §8.9.5.4, Table 89 (<c>Alternates</c>, PDF 1.3): an alternate shall not have alternates of its own.</remarks>
    public IReadOnlyList<PdfAlternateImage> Alternates
    {
        get
        {
            if (_role != ImageRole.XObject || Resolve(ReadRaw(ImageNames.Alternates)) is not CosArray array)
            {
                return [];
            }

            var alternates = new List<PdfAlternateImage>(array.Count);
            foreach (CosObject element in array)
            {
                if (Resolve(element) is CosDictionary entry && Get(entry, ImageNames.Image) is { } value && Resolve(value) is CosStream stream)
                {
                    var image = new PdfImage(_document, stream, value as CosReference, ImageRole.Alternate, this);
                    alternates.Add(new PdfAlternateImage(
                        entry,
                        image,
                        Resolve(Get(entry, ImageNames.DefaultForPrinting)) is CosBoolean { Value: true },
                        Resolve(Get(entry, ImageNames.OC)) as CosDictionary));
                }
                else
                {
                    Report(DiagnosticCodes.ImageAlternatesInvalid, "An element of Alternates is not an alternate image dictionary with an Image stream; it is skipped.");
                }
            }

            return alternates;
        }
    }

    /// <summary>Gets the image's optional content group or membership dictionary (<c>OC</c>, PDF 1.5), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87, and §8.11.3.3: when it is off, the image is not painted.</remarks>
    public CosDictionary? OptionalContent => Resolve(ReadRaw(ImageNames.OC)) as CosDictionary;

    /// <summary>Gets the image's metadata stream (<c>Metadata</c>, PDF 1.4), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87, and §14.3.2.</remarks>
    public CosStream? Metadata => Resolve(ReadRaw(ImageNames.Metadata)) as CosStream;

    /// <summary>Gets the image's key in the structural parent tree (<c>StructParent</c>, PDF 1.3), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87, and §14.7.5.4.</remarks>
    public int? StructParent => Resolve(ReadRaw(ImageNames.StructParent)) is CosInteger { Value: >= int.MinValue and <= int.MaxValue } value ? (int)value.Value : null;

    /// <summary>Gets the image's name in the resource dictionary (<c>Name</c>, deprecated in PDF 2.0), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87.</remarks>
    public CosName? Name => Resolve(ReadRaw(ImageNames.Name)) as CosName;

    /// <summary>Gets a value indicating whether the data's last filter is <c>JPXDecode</c>, whose codestream supplies what the dictionary may omit.</summary>
    private bool IsJpx => LastFilter() is { } name && name.Equals(FilterNames.JpxDecode);

    /// <summary>Decodes the image's samples.</summary>
    /// <returns>
    /// The samples in the §8.9.3 layout, which the caller disposes; <see langword="null"/> when the image cannot be decoded: its
    /// dictionary lacks what decoding needs, it is larger than <see cref="PdfOptions.MaxImagePixels"/> or
    /// <see cref="PdfOptions.MaxDecodedStreamLength"/> allow, or a filter it needs is not available. Each is recorded as a diagnostic.
    /// </returns>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §8.9.3 and §7.4. The filters run through the document's pipeline; when the last one has an image facet
    /// (<see cref="IImageFilter"/>), the codec decodes into the image directly and its header is checked against the limits first.
    /// What the codec reports wins over the dictionary for size and depth, with a diagnostic when they differ (§7.4.9).
    /// </para>
    /// <para>
    /// Short data is decoded as far as it goes and the rest is zero (<see cref="DecodedImage.DecodedRows"/> says how many rows were
    /// complete; for an image mask the missing rows paint nothing), with a diagnostic; data beyond the image is ignored. Depths other
    /// than 1, 2, 4, 8 and 16 are stored at the next wider depth, values unchanged.
    /// </para>
    /// </remarks>
    public DecodedImage? Decode()
    {
        ImageLayout layout = ReadLayout();
        if (!layout.IsDecodable)
        {
            return null;
        }

        StreamDecoder streams = _document.Streams;
        if (layout.BitsPerComponent > 0 && layout.Components > 0
            && ImageGeometry.CheckLimits(layout.Width, layout.Height, layout.Components, layout.BitsPerComponent, streams.MaxImagePixels, streams.MaxDecodedLength) is { } reason)
        {
            Report(DiagnosticCodes.ImageTooLarge, $"The image is not decoded: {reason}.", DiagnosticSeverity.Error);
            return null;
        }

        long expected = layout.BitsPerComponent > 0 ? ImageGeometry.Stride(layout.Width, Math.Max(1, layout.Components), layout.BitsPerComponent) * layout.Height : 0;
        using var data = new PooledBufferWriter(Math.Clamp(expected, 256, 1 << 24), streams.MaxDecodedLength);
        streams.Decode(_stream, data, 0, stopBeforeImageFilter: true, out StreamDecoder.ChainOutcome outcome);
        if (!outcome.Complete)
        {
            return null;
        }

        return outcome.ImageFilter is { } filter
            ? DecodeWithFilter(filter, outcome.ImageContext!, data.WrittenMemory, layout)
            : FromSamples(data.WrittenSpan, layout);
    }

    /// <summary>Returns the Decode mapping for samples of this image decoded as <paramref name="decoded"/>.</summary>
    /// <param name="decoded">The samples, from <see cref="Decode"/>.</param>
    /// <returns>The map: the image's <see cref="DecodeArray"/> at the samples' depth, its pairs swapped when <see cref="DecodedImage.SamplesInverted"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="decoded"/> is <see langword="null"/>.</exception>
    /// <remarks>ISO 32000-2 §8.9.5.2, Table 88; §7.4.9 (a JPEG 2000 image without a colour space maps every component onto [0 1]).</remarks>
    public ImageDecodeMap CreateDecodeMap(DecodedImage decoded)
    {
        ArgumentNullException.ThrowIfNull(decoded);
        IReadOnlyList<double> array = DecodeArray;
        int components = decoded.Components;
        double[] decode = new double[2 * components];
        for (int c = 0; c < components; c++)
        {
            bool known = array.Count >= 2 * components;
            double low = known ? array[2 * c] : 0;
            double high = known ? array[(2 * c) + 1] : 1;

            decode[2 * c] = decoded.SamplesInverted ? high : low;
            decode[(2 * c) + 1] = decoded.SamplesInverted ? low : high;
        }

        return ImageDecodeMap.Create(decode, components, decoded.BitsPerComponent);
    }

    /// <summary>
    /// Returns the colour space of samples decoded as <paramref name="decoded"/>: the image's <see cref="ColorSpace"/>, else an
    /// ICCBased space over the ICC profile the codec reported (when its header is usable and its component count matches), else the
    /// colour model the codec reported, else the device space with as many components (1 gray, 3 RGB, 4 CMYK).
    /// </summary>
    /// <param name="decoded">The samples, from <see cref="Decode"/>.</param>
    /// <returns>The space; <see langword="null"/> for an image mask or when none fits.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="decoded"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// ISO 32000-2 §7.4.9: the dictionary's ColorSpace overrides the codestream's colour specification; a JPEG 2000 ICC profile
    /// becomes an ICCBased space (§8.6.5.5) whose conversion goes through the colour-management extension point.
    /// </remarks>
    public PdfColorSpace? ResolveColorSpace(DecodedImage decoded)
    {
        ArgumentNullException.ThrowIfNull(decoded);
        if (IsStencil)
        {
            return null;
        }

        if (ColorSpace is { } space)
        {
            return space;
        }

        if (!decoded.IccProfile.IsEmpty
            && IccProfileHeader.Parse(decoded.IccProfile.Span) is { IsSupportedForPdf: true } header
            && header.ComponentCount == decoded.Components)
        {
            var profile = new CosDictionary { [ImageNames.N] = new CosInteger(header.ComponentCount) };
            return _document.ColorSpaces.Get(new CosArray([ImageNames.IccBased, new CosStream(profile, decoded.IccProfile)]), Reference ?? _owner);
        }

        return decoded.ColorModel switch
        {
            ImageColorModel.Gray => PdfDeviceGrayColorSpace.Instance,
            ImageColorModel.Rgb => PdfDeviceRgbColorSpace.Instance,
            ImageColorModel.Cmyk => PdfDeviceCmykColorSpace.Instance,
            _ => decoded.Components switch
            {
                1 => PdfDeviceGrayColorSpace.Instance,
                3 => PdfDeviceRgbColorSpace.Instance,
                4 => PdfDeviceCmykColorSpace.Instance,
                _ => null,
            },
        };
    }

    /// <summary>Creates the view over an image XObject, or returns <see langword="null"/> when the value is not an image stream.</summary>
    internal static PdfImage? Create(PdfDocument document, CosObject? value)
    {
        if (document.Resolve(value) is not CosStream stream)
        {
            return null;
        }

        if (document.Resolve(Get(stream.Dictionary, ImageNames.Subtype)) is CosName subtype && !subtype.Equals(ImageNames.Image))
        {
            return null;
        }

        return new PdfImage(document, stream, value as CosReference, ImageRole.XObject);
    }

    private static double[] UnitDecode(int components)
    {
        double[] decode = new double[2 * components];
        for (int c = 0; c < components; c++)
        {
            decode[(2 * c) + 1] = 1;
        }

        return decode;
    }

    private static CosObject? Get(CosDictionary dictionary, CosName key) => dictionary.TryGetValue(key, out CosObject? value) ? value : null;

    private static string Describe(CosObject? value) => value?.ToString() ?? "absent";

    private ImageLayout ReadLayout()
    {
        bool soft = _role == ImageRole.SoftMask;
        bool isMask = ReadEntry(ImageNames.ImageMask, ImageNames.IM) is CosBoolean { Value: true };
        if (soft && isMask)
        {
            Report(DiagnosticCodes.ImageSoftMaskInvalid, "A soft mask shall not be an image mask; it is read as a 1-bit DeviceGray image.");
            isMask = false;
        }

        if (_role == ImageRole.ExplicitMask && !isMask)
        {
            isMask = true;
        }

        bool jpx = IsJpx;
        int width = ReadDimension(ImageNames.Width, ImageNames.W, "Width", _parent?.Width ?? 0);
        int height = ReadDimension(ImageNames.Height, ImageNames.H, "Height", _parent?.Height ?? 0);
        CosObject? bpcEntry = ReadEntry(ImageNames.BitsPerComponent, ImageNames.Bpc);
        CosObject? colorEntry = ReadEntry(ImageNames.ColorSpace, ImageNames.CS);
        int bitsPerComponent;
        PdfColorSpace? colorSpace = null;
        bool usable = width > 0 && height > 0;
        if (isMask)
        {
            if (bpcEntry is not null and not CosInteger { Value: 1 })
            {
                Report(DiagnosticCodes.ImageMaskConflict, $"An image mask's BitsPerComponent shall be 1, not {Describe(bpcEntry)}; 1 is used.");
            }

            if (colorEntry is not null || (_role is not ImageRole.ExplicitMask && (ReadRaw(ImageNames.Mask) is not null || ReadRaw(ImageNames.SMask) is not null)))
            {
                Report(DiagnosticCodes.ImageMaskConflict, "An image mask shall have no ColorSpace, Mask or SMask entry; they are ignored.");
            }

            bitsPerComponent = 1;
        }
        else
        {
            bitsPerComponent = ReadBitsPerComponent(bpcEntry, jpx, soft && ReadEntry(ImageNames.ImageMask, ImageNames.IM) is CosBoolean { Value: true });
            colorSpace = ReadColorSpace(colorEntry, jpx, soft);
            usable &= (bitsPerComponent > 0 || jpx) && (colorSpace is not null || (jpx && colorEntry is null));
        }

        int components = isMask ? 1 : colorSpace?.ComponentCount ?? 0;
        double[] decode = usable ? ReadDecode(isMask, soft, jpx, colorSpace, components, bitsPerComponent) : [];
        return new ImageLayout(width, height, bitsPerComponent, components, isMask, colorSpace, decode, usable);
    }

    private int ReadDimension(CosName key, CosName abbreviation, string label, int fallback)
    {
        CosObject? entry = ReadEntry(key, abbreviation);
        double value = entry switch
        {
            CosInteger integer => integer.Value,
            CosReal real => real.Value,
            _ => double.NaN,
        };

        if (value >= 1 && value <= int.MaxValue)
        {
            if (value != Math.Floor(value))
            {
                Report(DiagnosticCodes.ImageDimensionInvalid, string.Create(CultureInfo.InvariantCulture, $"The image's {label} {value} is not an integer; {Math.Floor(value)} is used."));
            }

            return (int)Math.Floor(value);
        }

        if (fallback > 0)
        {
            Report(DiagnosticCodes.ImageDimensionInvalid, string.Create(CultureInfo.InvariantCulture, $"The mask's {label} is {Describe(entry)}; its image's {fallback} is used."));
            return fallback;
        }

        Report(DiagnosticCodes.ImageDimensionInvalid, $"The image's {label} is {Describe(entry)}, not a positive integer; the image cannot be painted.");
        return 0;
    }

    private int ReadBitsPerComponent(CosObject? entry, bool jpx, bool softFromMask)
    {
        if (softFromMask)
        {
            return 1;
        }

        if (entry is CosInteger { Value: >= 1 and <= 16 } integer)
        {
            int value = (int)integer.Value;
            if (value is not (1 or 2 or 4 or 8 or 16) && !jpx)
            {
                Report(DiagnosticCodes.ImageBitsPerComponentInvalid, string.Create(CultureInfo.InvariantCulture, $"BitsPerComponent {value} is not 1, 2, 4, 8 or 16; the samples are read at {value} bits."));
            }

            return value;
        }

        if (jpx)
        {
            return 0;
        }

        Report(DiagnosticCodes.ImageBitsPerComponentInvalid, $"The image's BitsPerComponent is {Describe(entry)}, not 1 to 16; the image cannot be painted.");
        return 0;
    }

    private PdfColorSpace? ReadColorSpace(CosObject? entry, bool jpx, bool soft)
    {
        PdfColorSpace? space;
        if (IsInline)
        {
            space = entry is null ? null : _inlineColorSpace ?? PdfDeviceGrayColorSpace.Instance;
        }
        else
        {
            space = entry is null ? null : _document.ColorSpaces.Get(RawEntry(ImageNames.ColorSpace, ImageNames.CS), _owner);
        }

        if (space is null)
        {
            if (soft)
            {
                return PdfDeviceGrayColorSpace.Instance;
            }

            if (!jpx)
            {
                Report(DiagnosticCodes.ImageColorSpaceMissing, "The image has no ColorSpace, which only image masks and JPEG 2000 images may omit; the image cannot be painted.");
            }

            return null;
        }

        if (space.Family == PdfColorSpaceFamily.Pattern)
        {
            Report(DiagnosticCodes.ImageColorSpaceInvalid, "An image's colour space shall not be a Pattern space; the image cannot be painted.");
            return null;
        }

        if (soft && space.Family != PdfColorSpaceFamily.DeviceGray)
        {
            if (space.ComponentCount != 1)
            {
                Report(DiagnosticCodes.ImageSoftMaskInvalid, $"A soft mask's colour space shall be DeviceGray, not a {space.Family} space of {space.ComponentCount} components; the soft mask cannot be used.");
                return null;
            }

            Report(DiagnosticCodes.ImageSoftMaskInvalid, $"A soft mask's colour space shall be DeviceGray, not {space.Family}; its one component is read as gray.");
            return PdfDeviceGrayColorSpace.Instance;
        }

        return space;
    }

    private double[] ReadDecode(bool isMask, bool soft, bool jpx, PdfColorSpace? space, int components, int bitsPerComponent)
    {
        if (jpx && space is null && !isMask)
        {
            return [];
        }

        double[] defaults = isMask || soft || space is null ? UnitDecode(Math.Max(1, components)) : space.GetDefaultDecode(Math.Max(1, bitsPerComponent));
        if (Resolve(ReadEntry(ImageNames.Decode, ImageNames.D)) is not { } entry || entry is CosNull)
        {
            return defaults;
        }

        double[]? values = entry is CosArray array ? ReadNumbers(array, 2 * components) : null;
        if (values is null)
        {
            Report(DiagnosticCodes.ImageDecodeInvalid, string.Create(CultureInfo.InvariantCulture, $"Decode shall be an array of {2 * components} numbers; the default is used."));
            return defaults;
        }

        if (((CosArray)entry).Count != 2 * components)
        {
            Report(DiagnosticCodes.ImageDecodeInvalid, string.Create(CultureInfo.InvariantCulture, $"Decode has {((CosArray)entry).Count} numbers where {2 * components} are needed; the first {2 * components} are used."));
        }

        if (isMask)
        {
            bool inverted = values[0] > values[1];
            if (values[0] != (inverted ? 1 : 0) || values[1] != (inverted ? 0 : 1))
            {
                Report(DiagnosticCodes.ImageDecodeInvalid, "An image mask's Decode shall be [0 1] or [1 0]; it is read by which end is larger.");
            }

            return inverted ? [1, 0] : [0, 1];
        }

        return values;
    }

    private PdfImageMaskKind ReadMask(out CosStream? mask)
    {
        mask = null;
        if (_role is ImageRole.ExplicitMask or ImageRole.SoftMask)
        {
            return PdfImageMaskKind.None;
        }

        if (IsStencil)
        {
            return PdfImageMaskKind.Stencil;
        }

        if (SoftMaskInData != 0)
        {
            if (IsJpx)
            {
                if (ReadRaw(ImageNames.SMask) is not null)
                {
                    Report(DiagnosticCodes.ImageSoftMaskInDataIgnored, "An image with SMaskInData shall have no SMask; the opacity in the JPEG 2000 data is used.");
                }

                return PdfImageMaskKind.SoftInData;
            }

            Report(DiagnosticCodes.ImageSoftMaskInDataIgnored, "SMaskInData applies only to JPXDecode data; it is ignored.");
        }

        if (ReadRaw(ImageNames.SMask) is { } smask)
        {
            if (Resolve(smask) is CosStream softMask)
            {
                mask = softMask;
                return PdfImageMaskKind.Soft;
            }

            if (Resolve(smask) is not CosNull && !(smask is CosName { Value: "None" }))
            {
                Report(DiagnosticCodes.ImageSoftMaskInvalid, $"SMask shall be a stream, not {Describe(Resolve(smask))}; it is ignored.");
            }
        }

        switch (Resolve(ReadRaw(ImageNames.Mask)))
        {
            case CosStream stream:
                if (Resolve(Get(stream.Dictionary, ImageNames.ImageMask)) is CosBoolean { Value: true })
                {
                    mask = stream;
                    return PdfImageMaskKind.Explicit;
                }

                Report(DiagnosticCodes.ImageMaskInvalid, "A Mask stream shall be an image mask (ImageMask true); it is ignored.");
                return PdfImageMaskKind.None;
            case CosArray array:
                int components = ReadLayout().Components;
                if (components > 0 && ReadNumbers(array, 2 * components) is not null)
                {
                    if (array.Count != 2 * components)
                    {
                        Report(DiagnosticCodes.ImageMaskInvalid, string.Create(CultureInfo.InvariantCulture, $"A colour key Mask has {array.Count} numbers where {2 * components} are needed; the first {2 * components} are used."));
                    }

                    return PdfImageMaskKind.ColorKey;
                }

                Report(DiagnosticCodes.ImageMaskInvalid, string.Create(CultureInfo.InvariantCulture, $"A colour key Mask shall have {2 * components} numbers; it is ignored."));
                return PdfImageMaskKind.None;
            case CosNull:
                return PdfImageMaskKind.None;
            default:
                Report(DiagnosticCodes.ImageMaskInvalid, "Mask shall be a stream or an array; it is ignored.");
                return PdfImageMaskKind.None;
        }
    }

    private DecodedImage? FromSamples(ReadOnlySpan<byte> data, ImageLayout layout)
    {
        int bits = layout.BitsPerComponent;
        if (bits <= 0 || layout.Components <= 0)
        {
            return null;
        }

        int width = layout.Width;
        int height = layout.Height;
        int components = layout.Components;
        int sourceStride = (int)ImageGeometry.Stride(width, components, bits);
        long needed = (long)sourceStride * height;
        var builder = new DecodedImageBuilder(width, height, components, bits);
        try
        {
            int rows = (int)Math.Min(height, data.Length / sourceStride);
            if (builder.StorageBits == bits)
            {
                data[..(int)Math.Min(data.Length, needed)].CopyTo(builder.Samples);
            }
            else
            {
                Repack(data, builder, sourceStride, bits);
            }

            if (data.Length < needed)
            {
                builder.DecodedRows = rows;
                Report(DiagnosticCodes.ImageDataTruncated, string.Create(CultureInfo.InvariantCulture, $"The image data has {data.Length} bytes where {needed} are needed; {rows} of {height} rows are complete and the rest are zero."));
                if (layout.IsStencil && layout.Decode is not [1, 0])
                {
                    // Missing rows of a mask paint nothing: with Decode [0 1] that is sample 1.
                    int start = (int)Math.Min(data.Length, needed);
                    builder.Samples[start..].Fill(0xFF);
                }
            }
            else if (data.Length - needed >= sourceStride)
            {
                Report(DiagnosticCodes.ImageDataTooLong, string.Create(CultureInfo.InvariantCulture, $"The image data has {data.Length} bytes where {needed} are needed; the rest is ignored."), DiagnosticSeverity.Information);
            }

            return builder.Build();
        }
        finally
        {
            builder.Dispose();
        }
    }

    /// <summary>Copies samples of an odd depth into the next wider storage, values unchanged, row by row.</summary>
    private static void Repack(ReadOnlySpan<byte> data, DecodedImageBuilder builder, int sourceStride, int bits)
    {
        int count = builder.Width * builder.Components;
        bool wide = builder.StorageBits == 16;
        for (int y = 0; y < builder.Height; y++)
        {
            long start = (long)y * sourceStride;
            if (start >= data.Length)
            {
                break;
            }

            ReadOnlySpan<byte> source = data.Slice((int)start, (int)Math.Min(sourceStride, data.Length - start));
            Span<byte> row = builder.GetRow(y);
            long available = (long)source.Length * 8 / bits;
            int bitPosition = 0;
            for (int i = 0; i < count && i < available; i++, bitPosition += bits)
            {
                int value = 0;
                for (int b = 0; b < bits; b++)
                {
                    int position = bitPosition + b;
                    value = (value << 1) | ((source[position >> 3] >> (7 - (position & 7))) & 1);
                }

                if (wide)
                {
                    row[2 * i] = (byte)(value >> 8);
                    row[(2 * i) + 1] = (byte)value;
                }
                else
                {
                    row[i] = (byte)value;
                }
            }
        }
    }

    private DecodedImage? DecodeWithFilter(IImageFilter filter, FilterContext filterContext, ReadOnlyMemory<byte> data, ImageLayout layout)
    {
        StreamDecoder streams = _document.Streams;
        var context = new ImageFilterContext(filterContext)
        {
            Width = layout.Width,
            Height = layout.Height,
            BitsPerComponent = layout.BitsPerComponent,
            ColorComponents = layout.Components,
            IsStencil = layout.IsStencil,
            WantsAlpha = SoftMaskInData != 0,
            MaxPixels = streams.MaxImagePixels,
            MaxDecodedLength = streams.MaxDecodedLength,
        };

        if (data.IsEmpty)
        {
            return null;
        }

        StrongBox<ImageHeader>? read = streams.RunImageFilter(
            filter.Name,
            () => filter.TryReadHeader(data.Span, context, out ImageHeader found) ? new StrongBox<ImageHeader>(found) : null);
        if (read?.Value is { Width: > 0, Height: > 0 } header
            && ImageGeometry.CheckLimits(header.Width, header.Height, Math.Clamp(header.Components, 1, ImageGeometry.MaxComponents), Math.Clamp(header.BitsPerComponent, 1, 16), streams.MaxImagePixels, streams.MaxDecodedLength) is { } reason)
        {
            Report(DiagnosticCodes.ImageTooLarge, $"The image is not decoded: its {filter.Name.Value} header declares {reason}.", DiagnosticSeverity.Error);
            return null;
        }

        DecodedImage? decoded = streams.RunImageFilter(filter.Name, () => filter.DecodeImage(data, context));
        if (decoded is null)
        {
            return null;
        }

        if (decoded.Width != layout.Width || decoded.Height != layout.Height)
        {
            Report(DiagnosticCodes.ImageDimensionMismatch, FormattableString.Invariant(
                $"The {filter.Name.Value} data is {decoded.Width} x {decoded.Height} where the dictionary says {layout.Width} x {layout.Height}; the data's size is used."));
        }

        if (layout.Components > 0 && decoded.Components != layout.Components)
        {
            Report(DiagnosticCodes.ImageComponentMismatch, FormattableString.Invariant(
                $"The {filter.Name.Value} data has {decoded.Components} components where the colour space has {layout.Components}; the image cannot be painted."));
            decoded.Dispose();
            return null;
        }

        return decoded;
    }

    private CosName? LastFilter()
    {
        CosObject filter = Resolve(Get(_stream.Dictionary, ImageNames.Filter));
        CosName? name = filter switch
        {
            CosName single => single,
            CosArray { Count: > 0 } array => Resolve(array[^1]) as CosName,
            _ => null,
        };
        return name is not null && InlineImageAbbreviations.TryExpandFilter(name, out CosName? full) ? full : name;
    }

    /// <summary>Reads an entry by its full name, else (with a diagnostic, outside inline images) by its abbreviation; resolved.</summary>
    private CosObject? ReadEntry(CosName key, CosName? abbreviation)
    {
        CosObject? raw = RawEntry(key, abbreviation);
        return raw is null ? null : Resolve(raw) is CosNull ? null : Resolve(raw);
    }

    private CosObject? RawEntry(CosName key, CosName? abbreviation)
    {
        if (Get(_stream.Dictionary, key) is { } value)
        {
            return value;
        }

        if (abbreviation is not null && Get(_stream.Dictionary, abbreviation) is { } abbreviated)
        {
            Report(DiagnosticCodes.ImageKeyAbbreviated, $"The abbreviation /{abbreviation.Value} is valid only in inline images; it is read as /{key.Value}.");
            return abbreviated;
        }

        return null;
    }

    private CosObject? ReadRaw(CosName key) => Get(_stream.Dictionary, key);

    private CosObject Resolve(CosObject? value) => _document.Resolve(value);

    private double[]? ReadNumbers(CosArray array, int count)
    {
        if (count <= 0 || array.Count < count)
        {
            return null;
        }

        double[] values = new double[count];
        for (int i = 0; i < count; i++)
        {
            switch (Resolve(array[i]))
            {
                case CosInteger integer:
                    values[i] = integer.Value;
                    break;
                case CosReal real:
                    values[i] = real.Value;
                    break;
                default:
                    return null;
            }
        }

        return values;
    }

    private void Report(string code, string message, DiagnosticSeverity severity = DiagnosticSeverity.Warning) =>
        _document.DiagnosticSink.Report(code, severity, message, offset: null, _owner);

    /// <summary>The dictionary values decoding needs, read with their defaults.</summary>
    private readonly record struct ImageLayout(
        int Width, int Height, int BitsPerComponent, int Components, bool IsStencil, PdfColorSpace? ColorSpace, double[] Decode, bool IsDecodable);
}
