using System.Buffers;
using System.IO.Compression;
using Broadside.Annotations;
using Broadside.Content;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Images;

/// <summary>
/// The save-time image pass (issue #625): finds where every image XObject is painted largest, decides from
/// <see cref="PdfImageOptimization"/> whether to resample and re-encode it, and, when applying, swaps in a smaller stream in place.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.9.4 (placement), §8.9.5 (image dictionaries), §8.9.5.3 (reduction, through <see cref="ImageResampler"/>), §8.9.6
/// (masks), §7.4.4 (FlateDecode with PNG predictors) and §7.4.8 (DCTDecode, through <see cref="DctEncoder"/>).
/// </para>
/// <para>
/// The walk runs every page's content with hidden content, image masks and artifacts, forms where they are painted and the cells of
/// the tiling patterns paths are filled or stroked with (once per pattern and pattern matrix), then every appearance of every
/// annotation (each mode and state). A placement's size is the length of the image's unit square edges in default user space times
/// the page's <c>UserUnit</c>; the largest along each axis is kept. Images painted only where the walk does not look (a soft mask's
/// group, a Type 3 glyph, text filled with a pattern) count as not placed there.
/// </para>
/// <para>
/// Applying keeps each stream's object number and changes its data and its <c>Width</c>, <c>Height</c>, <c>BitsPerComponent</c>,
/// <c>Filter</c> and <c>DecodeParms</c>; the colour space, <c>Decode</c>, <c>Intent</c>, <c>Alternates</c>, <c>OPI</c> and every other
/// entry are kept, so every placement changes at once and a PDF/A checker sees the same colour. A soft or explicit mask is resampled
/// by the same factor as its image (each maps onto the unit square on its own, §8.9.6.3 and §11.6.5.2, so a mask that cannot be
/// resampled is kept as it is). A new encoding replaces the old one only when it is shorter. The document's caches follow the change
/// through the streams' versions (ADR 0004).
/// </para>
/// </remarks>
internal static class ImageOptimizer
{
    private static readonly PdfVersion FlateVersion = new(1, 2);
    private static readonly CosInteger PngOptimum = new(15);

    /// <summary>Decides what the pass would do to each painted image, changing nothing.</summary>
    /// <param name="document">The document.</param>
    /// <param name="options">The pass's options.</param>
    /// <returns>One result per image XObject painted, then one per inline image painted.</returns>
    public static List<PdfImageOptimizationResult> Plan(PdfDocument document, PdfImageOptimization options)
    {
        var collector = new PlacementCollector();
        foreach (PdfPage page in document.Pages)
        {
            collector.BeginPage(page.UserUnit);
            page.ProcessContent(collector);
            foreach (PdfAnnotation annotation in page.Annotations)
            {
                foreach (PdfFormXObject form in Appearances(annotation))
                {
                    annotation.ProcessAppearance(form, collector);
                }
            }
        }

        var results = new List<PdfImageOptimizationResult>(collector.Placements.Count + collector.InlineImages.Count);
        foreach (Placement placement in collector.Placements)
        {
            var result = new PdfImageOptimizationResult(placement.Image, placement.Count, placement.EffectiveResolution)
            {
                OriginalLength = placement.Stream.EncodedLength,
            };
            result.OptimizedLength = result.OriginalLength;
            Decide(document, result, options);
            results.Add(result);
        }

        foreach (PdfImage inline in collector.InlineImages)
        {
            results.Add(new PdfImageOptimizationResult(inline, 1, null) { Reason = PdfImageOptimizationReason.InlineImage });
        }

        return results;
    }

    /// <summary>Plans the pass, then resamples and re-encodes the images it planned to change.</summary>
    /// <param name="document">The document.</param>
    /// <param name="options">The pass's options.</param>
    /// <param name="level">How hard <c>FlateDecode</c> compresses.</param>
    /// <returns>The results, with what was actually done.</returns>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found while decoding an image.</exception>
    public static List<PdfImageOptimizationResult> Apply(PdfDocument document, PdfImageOptimization options, CompressionLevel level)
    {
        List<PdfImageOptimizationResult> results = Plan(document, options);
        var masksDone = new HashSet<CosStream>(ReferenceEqualityComparer.Instance);
        foreach (PdfImageOptimizationResult result in results)
        {
            if (result.Actions != PdfImageOptimizationActions.None && result.Image.Stream is { } stream)
            {
                ApplyOne(document, result, stream, level, masksDone);
            }
        }

        return results;
    }

    /// <summary>Runs the pass for a save and says whether an image changed; what <see cref="PdfDocument.ImagePass"/> holds.</summary>
    public static bool ApplyForSave(PdfDocument document, PdfImageOptimization options, CompressionLevel level) =>
        Apply(document, options, level).Exists(static result => result.Actions != PdfImageOptimizationActions.None);

    /// <summary>Every appearance stream of an annotation: each mode, and each state of a mode that has states.</summary>
    private static IEnumerable<PdfFormXObject> Appearances(PdfAnnotation annotation)
    {
        if (annotation.AppearanceDictionary is not { } dictionary)
        {
            yield break;
        }

        PdfAppearanceEntry?[] entries =
        [
            dictionary.Normal,
            dictionary.HasRollover ? dictionary.Rollover : null,
            dictionary.HasDown ? dictionary.Down : null,
        ];
        foreach (PdfAppearanceEntry? entry in entries)
        {
            if (entry is null)
            {
                continue;
            }

            if (!entry.HasStates)
            {
                if (entry.Form is { } form)
                {
                    yield return form;
                }

                continue;
            }

            foreach (CosName state in entry.StateNames)
            {
                if (entry.GetAppearance(state) is { } form)
                {
                    yield return form;
                }
            }
        }
    }

    /// <summary>The encoding family of an image's filter chain.</summary>
    private enum Encoding
    {
        /// <summary>Unfiltered, or only lossless general-purpose filters.</summary>
        Lossless,

        /// <summary>Ends with <c>DCTDecode</c>.</summary>
        Dct,

        /// <summary>Ends with <c>CCITTFaxDecode</c> or <c>JBIG2Decode</c>.</summary>
        Bitonal,

        /// <summary><c>JPXDecode</c>, <c>Crypt</c>, an external file, or a filter the pass does not know.</summary>
        Unsupported,
    }

    private static void Decide(PdfDocument document, PdfImageOptimizationResult result, PdfImageOptimization options)
    {
        PdfImage image = result.Image;
        int width = image.Width;
        int height = image.Height;
        bool isMask = image.IsImageMask;
        int bits = isMask ? 1 : image.BitsPerComponent;
        PdfColorSpace? space = isMask ? null : image.ColorSpace;
        int components = isMask ? 1 : space?.ComponentCount ?? 0;
        bool bitonal = isMask || (bits == 1 && components == 1);
        Encoding encoding = Classify(document, image.Dictionary);

        // What the options ask for.
        double? cap = bitonal ? options.MaxBitonalResolution : options.MaxResolution;
        int targetWidth = width;
        int targetHeight = height;
        if (cap is { } dpi && result.EffectiveResolution is { } effective && effective > dpi * options.ResampleThreshold && width > 0 && height > 0)
        {
            double scale = dpi / effective;
            targetWidth = Math.Clamp((int)Math.Round(width * scale), 1, width);
            targetHeight = Math.Clamp((int)Math.Round(height * scale), 1, height);
        }

        bool resample = targetWidth < width || targetHeight < height;
        bool dctable = !bitonal && bits == 8 && IsDctSpace(space);
        bool recompress = options.JpegQuality is not null && dctable
            && (encoding == Encoding.Dct || (encoding == Encoding.Lossless && options.RecompressLossless));
        if (!resample && !recompress)
        {
            result.Reason = result.EffectiveResolution is null ? PdfImageOptimizationReason.NotPlaced : PdfImageOptimizationReason.WithinLimits;
            return;
        }

        // What the image allows.
        PdfImageOptimizationReason refusal = Refusal(image, encoding, space, bits, components, isMask);
        if (refusal != PdfImageOptimizationReason.None)
        {
            result.Reason = refusal;
            return;
        }

        bool dct = dctable && (recompress || encoding == Encoding.Dct);
        if (!dct && document.Version < FlateVersion)
        {
            result.Reason = PdfImageOptimizationReason.VersionTooLow;
            return;
        }

        result.Actions = (resample ? PdfImageOptimizationActions.Resample : PdfImageOptimizationActions.None) | PdfImageOptimizationActions.Recompress;
        result.TargetWidth = targetWidth;
        result.TargetHeight = targetHeight;
        result.TargetFilter = dct ? FilterNames.DctDecode : FilterNames.FlateDecode;
        result.Quality = dct ? options.JpegQuality ?? DctEncoder.DefaultQuality : 0;
        result.Bitonal = bitonal;
        result.TargetBits = dct ? 8 : resample ? ImageResampler.OutputBits(bits, bitonal) : bits;
    }

    /// <summary>Why an image whose change the options ask for cannot be changed faithfully, or <see cref="PdfImageOptimizationReason.None"/>.</summary>
    private static PdfImageOptimizationReason Refusal(PdfImage image, Encoding encoding, PdfColorSpace? space, int bits, int components, bool isMask)
    {
        if (encoding == Encoding.Unsupported || image.SoftMaskInData != PdfSoftMaskInData.None)
        {
            return PdfImageOptimizationReason.Unsupported;
        }

        if (!isMask && (space is null || space.Family is PdfColorSpaceFamily.Indexed or PdfColorSpaceFamily.Pattern))
        {
            return PdfImageOptimizationReason.Unsupported;
        }

        if (bits is not (1 or 2 or 4 or 8 or 16) || (bits == 1 && components != 1) || components < 1)
        {
            return PdfImageOptimizationReason.Unsupported;
        }

        PdfImageMaskKind mask = image.MaskKind;
        if (mask == PdfImageMaskKind.ColorKey)
        {
            return PdfImageOptimizationReason.ColorKeyMask;
        }

        return mask == PdfImageMaskKind.Soft && image.Matte is not null ? PdfImageOptimizationReason.Matte : PdfImageOptimizationReason.None;
    }

    /// <summary>Whether DCTDecode can carry the space's samples: 1, 3 or 4 components of a device, CIE-based or ICC-based space.</summary>
    private static bool IsDctSpace(PdfColorSpace? space) =>
        space is not null
        && space.Family is PdfColorSpaceFamily.DeviceGray or PdfColorSpaceFamily.DeviceRgb or PdfColorSpaceFamily.DeviceCmyk
            or PdfColorSpaceFamily.CalGray or PdfColorSpaceFamily.CalRgb or PdfColorSpaceFamily.IccBased
        && space.ComponentCount is 1 or 3 or 4;

    /// <summary>Classifies a stream's filter chain (§7.4.1, Table 6).</summary>
    private static Encoding Classify(PdfDocument document, CosDictionary dictionary)
    {
        if (dictionary.ContainsKey(FilterNames.F))
        {
            return Encoding.Unsupported;
        }

        CosObject filter = document.Resolve(dictionary.TryGetValue(FilterNames.Filter, out CosObject? value) ? value : null);
        CosName? last = null;
        switch (filter)
        {
            case CosNull:
                return Encoding.Lossless;
            case CosName name:
                last = name;
                if (!IsLossless(name) && !IsLastOnly(name))
                {
                    return Encoding.Unsupported;
                }

                break;
            case CosArray array:
                for (int i = 0; i < array.Count; i++)
                {
                    if (document.Resolve(array[i]) is not CosName name)
                    {
                        return Encoding.Unsupported;
                    }

                    bool isLast = i == array.Count - 1;
                    if (!IsLossless(name) && !(isLast && IsLastOnly(name)))
                    {
                        return Encoding.Unsupported;
                    }

                    last = name;
                }

                break;
            default:
                return Encoding.Unsupported;
        }

        if (last is null || IsLossless(last))
        {
            return Encoding.Lossless;
        }

        return last.Equals(FilterNames.DctDecode) ? Encoding.Dct : Encoding.Bitonal;
    }

    private static bool IsLossless(CosName name) =>
        name.Equals(FilterNames.FlateDecode) || name.Equals(FilterNames.LzwDecode) || name.Equals(FilterNames.RunLengthDecode)
        || name.Equals(FilterNames.AsciiHexDecode) || name.Equals(FilterNames.Ascii85Decode);

    /// <summary>The image codecs the pass reads (JPX is not among them: its colour can come from the codestream).</summary>
    private static bool IsLastOnly(CosName name) =>
        name.Equals(FilterNames.DctDecode) || name.Equals(FilterNames.CcittFaxDecode) || name.Equals(FilterNames.Jbig2Decode);

    private static void ApplyOne(PdfDocument document, PdfImageOptimizationResult result, CosStream stream, CompressionLevel level, HashSet<CosStream> masksDone)
    {
        PdfImage image = result.Image;
        using DecodedImage? decoded = image.Decode();
        if (decoded is null)
        {
            Skip(document, result, PdfImageOptimizationReason.DecodeFailed, "The image could not be decoded; the save-time image pass leaves it unchanged.");
            return;
        }

        int width = image.Width;
        int height = image.Height;
        int bits = image.IsImageMask ? 1 : image.BitsPerComponent;
        int components = image.IsImageMask ? 1 : image.ColorSpace?.ComponentCount ?? 0;
        if (!Matches(decoded, width, height, components, bits))
        {
            Skip(document, result, PdfImageOptimizationReason.Unsupported, "The decoded samples do not match the image dictionary (truncated data, a codec's own size or colour); the save-time image pass leaves the image unchanged.");
            return;
        }

        bool resample = (result.Actions & PdfImageOptimizationActions.Resample) != 0;
        byte[]? buffer = null;
        try
        {
            ReadOnlySpan<byte> samples = decoded.Samples;
            int outBits = bits;
            if (resample)
            {
                outBits = ImageResampler.OutputBits(bits, result.Bitonal);
                int length = ImageResampler.Stride(result.TargetWidth, components, outBits) * result.TargetHeight;
                buffer = ArrayPool<byte>.Shared.Rent(length);
                ImageResampler.Resample(decoded.Samples, width, height, components, decoded.StorageBits, bits, result.TargetWidth, result.TargetHeight, outBits, buffer);
                samples = buffer.AsSpan(0, length);
            }
            else if (decoded.StorageBits != bits)
            {
                Skip(document, result, PdfImageOptimizationReason.Unsupported, "The samples are stored at another depth than the image declares; the save-time image pass leaves the image unchanged.");
                return;
            }

            bool dct = result.TargetFilter is { } filter && filter.Equals(FilterNames.DctDecode);
            using var output = new PooledBufferWriter(Math.Max(256, samples.Length / 4), Array.MaxLength);
            CosDictionary? parameters = dct ? null : Predicted(components, outBits, result.TargetWidth);
            if (dct)
            {
                var scratch = new CosDictionary
                {
                    [ImageNames.Width] = new CosInteger(result.TargetWidth),
                    [ImageNames.Height] = new CosInteger(result.TargetHeight),
                    [ImageNames.BitsPerComponent] = new CosInteger(8),
                };
                new DctEncoder(result.Quality).Encode(samples, output, new StreamEncoderContext { StreamDictionary = scratch });
            }
            else
            {
                var scratch = new CosDictionary { [FilterNames.Filter] = FilterNames.FlateDecode, [FilterNames.DecodeParms] = parameters! };
                new StreamEncoder(document.StreamEncoders, document.Resolve).Encode(samples, scratch, output, level);
            }

            if (output.WrittenSpan.Length >= result.OriginalLength)
            {
                result.Actions = PdfImageOptimizationActions.None;
                result.Reason = PdfImageOptimizationReason.WouldGrow;
                result.TargetWidth = width;
                result.TargetHeight = height;
                result.TargetFilter = null;
                return;
            }

            Replace(stream, result.TargetWidth, result.TargetHeight, image.IsImageMask, outBits, result.TargetFilter!, parameters, output.WrittenSpan.ToArray());
            result.OptimizedLength = stream.EncodedLength;
            if (!resample)
            {
                result.Actions = PdfImageOptimizationActions.Recompress;
            }
        }
        finally
        {
            if (buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        if (resample && image.Mask is { Stream: { } maskStream } mask && masksDone.Add(maskStream))
        {
            ResampleMask(document, mask, maskStream, (double)result.TargetWidth / width, (double)result.TargetHeight / height, level);
        }
    }

    /// <summary>Resamples a soft or explicit mask by its image's factors, losslessly, when that is smaller; otherwise leaves it.</summary>
    private static void ResampleMask(PdfDocument document, PdfImage mask, CosStream stream, double scaleX, double scaleY, CompressionLevel level)
    {
        Encoding encoding = Classify(document, mask.Dictionary);
        bool isMask = mask.IsImageMask;
        int bits = isMask ? 1 : mask.BitsPerComponent;
        int width = mask.Width;
        int height = mask.Height;
        int targetWidth = Math.Clamp((int)Math.Round(width * scaleX), 1, width);
        int targetHeight = Math.Clamp((int)Math.Round(height * scaleY), 1, height);
        if (encoding is Encoding.Unsupported || bits is not (1 or 2 or 4 or 8 or 16) || (targetWidth == width && targetHeight == height)
            || document.Version < FlateVersion || mask.Matte is not null)
        {
            return;
        }

        using DecodedImage? decoded = mask.Decode();
        if (decoded is null)
        {
            Report(document, mask.Reference, "The image's mask could not be decoded; the save-time image pass leaves it unchanged.");
            return;
        }

        if (!Matches(decoded, width, height, 1, bits))
        {
            return;
        }

        bool bitonal = isMask || bits == 1;
        int outBits = ImageResampler.OutputBits(bits, bitonal);
        int length = ImageResampler.Stride(targetWidth, 1, outBits) * targetHeight;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            ImageResampler.Resample(decoded.Samples, width, height, 1, decoded.StorageBits, bits, targetWidth, targetHeight, outBits, buffer);
            CosDictionary parameters = Predicted(1, outBits, targetWidth);
            using var output = new PooledBufferWriter(Math.Max(256, length / 4), Array.MaxLength);
            var scratch = new CosDictionary { [FilterNames.Filter] = FilterNames.FlateDecode, [FilterNames.DecodeParms] = parameters };
            new StreamEncoder(document.StreamEncoders, document.Resolve).Encode(buffer.AsSpan(0, length), scratch, output, level);
            if (output.WrittenSpan.Length < stream.EncodedLength)
            {
                Replace(stream, targetWidth, targetHeight, isMask, outBits, FilterNames.FlateDecode, parameters, output.WrittenSpan.ToArray());
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Whether the decode is the image the dictionary describes, whole, at full resolution, without a codec's own colour or alpha.</summary>
    private static bool Matches(DecodedImage decoded, int width, int height, int components, int bits) =>
        decoded.Geometry.Width == width && decoded.Geometry.Height == height && decoded.Geometry.Components == components
        && decoded.Geometry.BitsPerComponent == bits && decoded.Reduction == 0 && decoded.DecodedRows == height
        && !decoded.SamplesInverted && decoded.Alpha is null && decoded.Palette.IsEmpty;

    /// <summary>The DecodeParms of PNG predictors chosen row by row (§7.4.4.4, Table 8).</summary>
    private static CosDictionary Predicted(int colors, int bitsPerComponent, int columns) => new()
    {
        [FilterNames.Predictor] = PngOptimum,
        [FilterNames.Colors] = new CosInteger(colors),
        [FilterNames.BitsPerComponent] = new CosInteger(bitsPerComponent),
        [FilterNames.Columns] = new CosInteger(columns),
    };

    /// <summary>Swaps the stream's data and the dictionary entries that describe it (§8.9.5.1, Table 87; §7.3.8.2, Table 5).</summary>
    private static void Replace(CosStream stream, int width, int height, bool isMask, int bits, CosName filter, CosDictionary? parameters, byte[] data)
    {
        CosDictionary dictionary = stream.Dictionary;
        dictionary[ImageNames.Width] = new CosInteger(width);
        dictionary[ImageNames.Height] = new CosInteger(height);
        if (!isMask || dictionary.ContainsKey(ImageNames.BitsPerComponent))
        {
            dictionary[ImageNames.BitsPerComponent] = new CosInteger(bits);
        }

        dictionary[FilterNames.Filter] = filter;
        if (parameters is null)
        {
            dictionary.Remove(FilterNames.DecodeParms);
        }
        else
        {
            dictionary[FilterNames.DecodeParms] = parameters;
        }

        // §7.3.8.2, Table 5: DL is the decoded length, which the new samples change.
        dictionary.Remove(FilterNames.DL);
        stream.EncodedData = data;
    }

    private static void Skip(PdfDocument document, PdfImageOptimizationResult result, PdfImageOptimizationReason reason, string message)
    {
        result.Actions = PdfImageOptimizationActions.None;
        result.Reason = reason;
        result.TargetWidth = result.Image.Width;
        result.TargetHeight = result.Image.Height;
        result.TargetFilter = null;
        Report(document, result.Reference, message);
    }

    private static void Report(PdfDocument document, CosReference? reference, string message) =>
        document.DiagnosticSink.Report(ImageDiagnostics.ImageOptimizationSkipped, DiagnosticSeverity.Information, message, offset: null, reference);

    /// <summary>One image XObject the walk found painted, with its largest placement along each axis.</summary>
    private sealed class Placement(PdfImage image, CosStream stream)
    {
        public PdfImage Image { get; } = image;

        public CosStream Stream { get; } = stream;

        public int Count { get; set; }

        /// <summary>Gets or sets the longest the image's horizontal edge was painted, in points (1/72 inch).</summary>
        public double Horizontal { get; set; }

        /// <summary>Gets or sets the longest the image's vertical edge was painted, in points.</summary>
        public double Vertical { get; set; }

        public double? EffectiveResolution => Horizontal > 0 && Vertical > 0 && Image.Width > 0 && Image.Height > 0
            ? Math.Min(Image.Width * 72 / Horizontal, Image.Height * 72 / Vertical)
            : null;
    }

    /// <summary>Records every image painted and its size in device-independent inches; runs tiling pattern cells.</summary>
    private sealed class PlacementCollector : ContentProcessor
    {
        private readonly Dictionary<CosStream, Placement> _byStream = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<CosObject, List<Matrix>> _patternsRun = new(ReferenceEqualityComparer.Instance);
        private double _userUnit = 1;

        public List<Placement> Placements { get; } = [];

        public List<PdfImage> InlineImages { get; } = [];

        public override ContentEvents Events => ContentEvents.Images | ContentEvents.Paths | ContentEvents.Forms | ContentEvents.HiddenContent;

        public void BeginPage(double userUnit)
        {
            _userUnit = userUnit;
            _patternsRun.Clear();
        }

        public override void PaintImage(in ImageEvent image, ContentContext context)
        {
            if (image.Image is not { } model)
            {
                return;
            }

            if (image.IsInline || model.Stream is not { } stream)
            {
                InlineImages.Add(model);
                return;
            }

            if (!_byStream.TryGetValue(stream, out Placement? placement))
            {
                placement = new Placement(model, stream);
                _byStream.Add(stream, placement);
                Placements.Add(placement);
            }

            Matrix ctm = image.Ctm;
            double horizontal = Math.Sqrt((ctm.A * ctm.A) + (ctm.B * ctm.B)) * _userUnit;
            double vertical = Math.Sqrt((ctm.C * ctm.C) + (ctm.D * ctm.D)) * _userUnit;
            placement.Count++;
            if (double.IsFinite(horizontal) && double.IsFinite(vertical))
            {
                placement.Horizontal = Math.Max(placement.Horizontal, horizontal);
                placement.Vertical = Math.Max(placement.Vertical, vertical);
            }
        }

        public override void PaintPath(in PathEvent path, ContentContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            ref readonly GraphicsState state = ref context.State;
            if (path.Paint is PathPaint.Fill or PathPaint.FillAndStroke)
            {
                RunPattern(state.FillColor, context);
            }

            if (path.Paint is PathPaint.Stroke or PathPaint.FillAndStroke)
            {
                RunPattern(context.State.StrokeColor, context);
            }
        }

        private void RunPattern(in PdfColor color, ContentContext context)
        {
            if (color.Pattern is not { } pattern)
            {
                return;
            }

            if (!_patternsRun.TryGetValue(pattern, out List<Matrix>? matrices))
            {
                matrices = [];
                _patternsRun.Add(pattern, matrices);
            }

            if (matrices.Contains(color.PatternMatrix))
            {
                return;
            }

            matrices.Add(color.PatternMatrix);
            context.RunPatternCell(color, this);
        }
    }
}
