using System.Runtime.InteropServices;
using Broadside.Caching;
using Broadside.Content;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Objects;

namespace Broadside.Rendering;

/// <summary>
/// Image XObjects and inline images (ISO 32000-2 §8.9.4, §8.9.5, §8.9.7) with their masks (§8.9.6, §11.6.5), and stencil masks
/// (§8.9.6.2), decoded once per list and interned, and kept across lists in the document's image cache (issue #103).
/// </summary>
internal sealed partial class DisplayListBuilder
{
    private DisplayImage[] _images = new DisplayImage[4];
    private int _imageCount;
    private DisplayImagePaint[] _imagePaints = new DisplayImagePaint[4];
    private int _imagePaintCount;
    private DisplayImageMask[] _imageMasks = new DisplayImageMask[4];
    private int _imageMaskCount;

    // Image XObject streams seen in this build -> index into _images, or -1 when the image cannot be painted.
    private Dictionary<object, int>? _internedImages;

    // Stencil mask XObject streams seen in this build -> index into _imageMasks, or -1 when the mask cannot be painted.
    private Dictionary<object, int>? _internedMasks;

    // Images and stencil masks this build decoded that no cache holds (issue #97: released when the build fails).
    private object[] _owned = new object[4];
    private int _ownedCount;

    // The image a build in slices is decoding before its Do (issue #566), until the Do takes it.
    private ImagePrefetch? _prefetch;

    /// <inheritdoc/>
    /// <remarks>
    /// ISO 32000-2 §8.9.5 and §11.6.5.2; issue #566. An image the list does not hold yet, and the document's image cache does not
    /// either, is decoded here a slice at a time, its soft mask after it, exactly as <see cref="PaintImage"/> would decode them in one
    /// step: the filters before any image codec run a few chunks or rows per slice (see <see cref="PdfImage.LoadJob"/>). The
    /// <c>Do</c> that follows takes the result.
    /// </remarks>
    internal override bool PrepareImage(CosStream stream, PdfImage image, ContentContext context, long deadline)
    {
        if ((_internedImages is not null && _internedImages.ContainsKey(stream))
            || context.Document.Caches.Images.TryGet((stream, DisplayImageEntry.ImageSlot), out _))
        {
            return true;
        }

        if (_prefetch is { } pending && !ReferenceEquals(pending.Stream, stream))
        {
            pending.Dispose();
            _prefetch = null;
        }

        _prefetch ??= new ImagePrefetch(stream, image, context.CancellationToken);
        return _prefetch.Continue(deadline);
    }

    /// <summary>Takes the decode prepared for <paramref name="stream"/> by <see cref="PrepareImage"/>, if any; the caller disposes it.</summary>
    private ImagePrefetch? TakePrefetch(CosStream stream)
    {
        if (_prefetch is not { } prefetch || !ReferenceEquals(prefetch.Stream, stream))
        {
            return null;
        }

        _prefetch = null;
        return prefetch.IsDone ? prefetch : Discard(prefetch);

        static ImagePrefetch? Discard(ImagePrefetch unfinished)
        {
            unfinished.Dispose();
            return null;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// ISO 32000-2 §8.9.4: the image is painted into the unit square of user space under the CTM. An image XObject is decoded the
    /// first time it is painted, with its explicit, colour key or soft mask and Matte (§8.9.6.3, §8.9.6.4, §11.6.5.2, §7.4.9),
    /// and every later paint in the list shares the result; an inline image (§8.9.7) is decoded where it appears. The image's
    /// Intent entry, when present, replaces the graphics state's rendering intent for it (§8.6.5.8). A stencil mask (§8.9.6.2) is
    /// painted with the nonstroking colour; in a Pattern colour (§8.7) it is not in the list yet.
    /// </remarks>
    public override void PaintImage(in ImageEvent image, ContentContext context)
    {
        if (image.Image is not { } model)
        {
            return;
        }

        if (image.IsImageMask)
        {
            PaintStencil(image, model, context);
            return;
        }

        int index = InternImage(image.IsInline ? null : image.Stream, model, context.Document, context.CancellationToken);
        if (index < 0)
        {
            return;
        }

        ref readonly GraphicsState state = ref context.State;
        ColorConversion conversion = ColorConversion.FromState(state, DeviceColorModel.Rgb);
        if (model.Intent is { } intent)
        {
            conversion = conversion with { Intent = intent };
        }

        int paint = _imagePaintCount;
        Append(ref _imagePaints, ref _imagePaintCount, new DisplayImagePaint(context.DefaultColorSpaces, conversion));
        DisplayImage decoded = _images[index];
        SetCompositing(state, context, ownMask: decoded.SoftMask is not null || decoded.Mask is not null || !decoded.ColorKey.IsEmpty);
        SetTransform(image.Ctm);
        AddOperation(new DisplayOperation(DisplayOperationKind.DrawImage, default, index, paint));
    }

    /// <summary>Records a stencil mask filled with the nonstroking colour (§8.9.6.2).</summary>
    private void PaintStencil(in ImageEvent image, PdfImage model, ContentContext context)
    {
        ref readonly GraphicsState state = ref context.State;
        ref readonly PdfColor fill = ref state.FillColor;
        if (fill.ColorSpace.Family == PdfColorSpaceFamily.Pattern)
        {
            return;
        }

        CosStream? stream = image.IsInline ? null : image.Stream;
        int index;
        if (stream is not null && _internedMasks is not null && _internedMasks.TryGetValue(stream, out int known))
        {
            index = known;
        }
        else
        {
            DisplayImageMask? mask;
            if (stream is not null && context.Document.Caches.Images.TryGet((stream, DisplayImageEntry.StencilSlot), out IBudgetedEntry? cached))
            {
                mask = ((DisplayImageEntry)cached).Mask;
            }
            else
            {
                long epoch = context.Document.Epoch;
                mask = DecodeMask(model, context.CancellationToken);
                if (mask is not null && (stream is null || !context.Document.Caches.Images.Add((stream, DisplayImageEntry.StencilSlot), new DisplayImageEntry(context.Document.Owner, epoch, null, mask))))
                {
                    // Only this build holds it until its list does: a failed build gives its samples back (issue #97).
                    Append(ref _owned, ref _ownedCount, mask);
                }
            }

            index = mask is null ? -1 : _imageMaskCount;
            if (mask is not null)
            {
                Append(ref _imageMasks, ref _imageMaskCount, mask);
            }

            if (stream is not null)
            {
                _internedMasks ??= new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
                _internedMasks[stream] = index;
            }
        }

        if (index < 0)
        {
            return;
        }

        int color = AddColor(fill, context.DefaultColorSpaces, ColorConversion.FromState(state, DeviceColorModel.Rgb));
        SetCompositing(state, context);
        SetTransform(image.Ctm);
        AddOperation(new DisplayOperation(DisplayOperationKind.FillImageMask, default, index, color));
    }

    /// <summary>
    /// Returns the index of the decoded image: the one interned earlier in the build, else the document's cached one (issue #103),
    /// else a fresh decode, offered to the document's image cache.
    /// </summary>
    /// <param name="stream">The image XObject's stream, the interning key; <see langword="null"/> for an inline image, never cached.</param>
    /// <param name="model">The image.</param>
    /// <param name="document">The document whose image cache to use.</param>
    /// <param name="cancellationToken">The build's token, given to the image's filters.</param>
    /// <returns>The index into the image table, or −1 when the image cannot be decoded or has no usable colour space.</returns>
    private int InternImage(CosStream? stream, PdfImage model, PdfDocument document, CancellationToken cancellationToken)
    {
        if (stream is not null && _internedImages is not null && _internedImages.TryGetValue(stream, out int known))
        {
            return known;
        }

        DisplayImage? image;
        ImagePrefetch? prefetched = stream is null ? null : TakePrefetch(stream);
        try
        {
            if (stream is not null && document.Caches.Images.TryGet((stream, DisplayImageEntry.ImageSlot), out IBudgetedEntry? cached))
            {
                image = ((DisplayImageEntry)cached).Image;
            }
            else
            {
                long epoch = prefetched?.Epoch ?? document.Epoch;
                image = Decode(model, document, prefetched, cancellationToken);
                if (image is not null && (stream is null || !document.Caches.Images.Add((stream, DisplayImageEntry.ImageSlot), new DisplayImageEntry(document.Owner, epoch, image, null))))
                {
                    // Only this build holds it until its list does: a failed build gives its samples back (issue #97).
                    Append(ref _owned, ref _ownedCount, image);
                }
            }
        }
        finally
        {
            // What the decode did not take (the cache had the image meanwhile) goes back.
            prefetched?.Dispose();
        }

        int index = image is null ? -1 : _imageCount;
        if (image is not null)
        {
            Append(ref _images, ref _imageCount, image);
        }

        if (stream is not null)
        {
            _internedImages ??= new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
            _internedImages[stream] = index;
        }

        return index;
    }

    /// <summary>
    /// Decodes an image and its mask; an image whose codec decodes reduced resolutions is kept encoded instead, to be decoded at the
    /// resolution it is drawn at (issue #362; see <see cref="DisplayImage.GetSamplesAt(int)"/>).
    /// </summary>
    /// <returns>The image, or <see langword="null"/> (the reason is already a diagnostic of the image).</returns>
    private static DisplayImage? Decode(PdfImage model, PdfDocument document, ImagePrefetch? prefetched, CancellationToken cancellationToken)
    {
        PdfImage.PreparedImage prepared = prefetched?.TakeImage() ?? model.Prepare(cancellationToken);
        if (prepared.Encoded is { } encoded)
        {
            // Prepare keeps an image encoded only when its colour space, the dictionary's or the one its codestream's header gives
            // (JPXDecode without a ColorSpace, issue #507), has the codestream's component count.
            PdfColorSpace space = prepared.EncodedColorSpace!;
            if (space.Family == PdfColorSpaceFamily.Pattern)
            {
                return null;
            }

            // The image reduces when drawn small, so a soft mask of its size must too, on the same grid (issue #513).
            MaskParts parts = DecodeMasks(model, null, space, document.Caches.Images, encoded.Header.Geometry.Width, encoded.Header.Geometry.Height, prefetched, cancellationToken);
            return new DisplayImage(encoded, document.Caches.Images, space, model.CreateDecodeMapFor(space.ComponentCount, encoded.Header.Geometry.BitsPerComponent, inverted: false), model.Interpolate)
            {
                Mask = parts.Mask,
                SoftMask = parts.SoftMask,
                ColorKeyRanges = parts.ColorKey,
                MatteColor = parts.Matte,
                ColorTableBytes = space is PdfIndexedColorSpace indexedSpace ? ColorTable(indexedSpace) : [],
            };
        }

        DecodedImage? decoded = prepared.Decoded;
        try
        {
            if (decoded is null
                || model.ResolveColorSpace(decoded) is not { } space
                || space.Family == PdfColorSpaceFamily.Pattern
                || space.ComponentCount != decoded.Geometry.Components)
            {
                return null;
            }

            MaskParts mask = DecodeMasks(model, decoded, space, document.Caches.Images, 0, 0, prefetched, cancellationToken);
            var image = new DisplayImage(decoded, space, model.CreateDecodeMap(decoded), model.Interpolate, document.Caches.Images)
            {
                Mask = mask.Mask,
                SoftMask = mask.SoftMask,
                ColorKeyRanges = mask.ColorKey,
                MatteColor = mask.Matte,
                ColorTableBytes = space is PdfIndexedColorSpace indexed ? ColorTable(indexed) : [],
            };
            decoded = null;
            return image;
        }
        finally
        {
            decoded?.Dispose();
        }
    }

    /// <summary>Returns the colour table of an Indexed space as an array the list can own (§8.6.6.3).</summary>
    private static byte[] ColorTable(PdfIndexedColorSpace indexed)
    {
        ReadOnlyMemory<byte> lookup = indexed.GetLookup();
        return MemoryMarshal.TryGetArray(lookup, out ArraySegment<byte> segment) && segment.Offset == 0 && segment.Count == segment.Array!.Length
            ? segment.Array
            : lookup.ToArray();
    }

    /// <summary>Decodes the mask the image's dictionary names, at the mask's own size; nothing when the mask cannot be used.</summary>
    /// <remarks>
    /// ISO 32000-2 §8.9.6 and §11.6.4.3 (which mask applies: <see cref="PdfImage.MaskKind"/>), §8.9.6.3 (explicit), §8.9.6.4 (colour
    /// key), §11.6.5.2 (soft mask and Matte), §7.4.9 and Table 87 (SMaskInData: 1 opacity, 2 opacity premultiplied into the
    /// colour, which is a Matte of zeros). An unusable mask leaves the image unmasked; its reason is already a diagnostic.
    /// <paramref name="reducedWidth"/> and <paramref name="reducedHeight"/> are the size of an image kept encoded, which reduces
    /// when drawn small (0 otherwise): a soft mask of that size is made reducible too, so that both reduce alike (issue #513).
    /// </remarks>
    private static MaskParts DecodeMasks(PdfImage model, DecodedImage? decoded, PdfColorSpace space, ByteBudgetCache<(object Source, int Slot), IBudgetedEntry> cache, int reducedWidth, int reducedHeight, ImagePrefetch? prefetched, CancellationToken cancellationToken)
    {
        switch (model.MaskKind)
        {
            case PdfImageMaskKind.Explicit when model.Mask is { } explicitMask:
                return new MaskParts(DecodeMask(explicitMask, cancellationToken), null, [], []);
            case PdfImageMaskKind.ColorKey when model.ColorKey is { } ranges:
                return new MaskParts(null, null, [.. ranges], []);
            case PdfImageMaskKind.Soft when model.Mask is { } softMask && DecodeSoftMask(softMask, cache, reducedWidth, reducedHeight, prefetched, cancellationToken) is { } soft:
                return new MaskParts(null, soft, [], model.Matte is { } matte ? [.. matte.Select(static m => (float)m)] : []);
            case PdfImageMaskKind.SoftInData when decoded?.Alpha is { } alpha:
                int components = space is PdfIndexedColorSpace indexed ? indexed.Base.ComponentCount : space.ComponentCount;
                var opacity = new DisplayImage(alpha, PdfDeviceGrayColorSpace.Instance, ImageDecodeMap.Create([0, 1], 1, alpha.Geometry.BitsPerComponent), model.Interpolate, cache);
                return new MaskParts(null, opacity, [], model.SoftMaskInData == PdfSoftMaskInData.PremultipliedAlpha || decoded!.AlphaPremultiplied ? new float[components] : []);
            default:
                return new MaskParts(null, null, [], []);
        }
    }

    /// <summary>Decodes an image mask (a stencil mask, or an image's explicit mask) at its own size.</summary>
    /// <returns>The mask, or <see langword="null"/> when it cannot be decoded (already a diagnostic) or is not one bit per sample.</returns>
    private static DisplayImageMask? DecodeMask(PdfImage model, CancellationToken cancellationToken)
    {
        DecodedImage? decoded = model.DecodeSamples(cancellationToken);
        try
        {
            if (decoded is null || decoded.Geometry.Components != 1 || decoded.Geometry.BitsPerComponent != 1)
            {
                return null;
            }

            var mask = new DisplayImageMask(decoded, model.CreateDecodeMap(decoded).IsInverted, model.Interpolate);
            decoded = null;
            return mask;
        }
        finally
        {
            decoded?.Dispose();
        }
    }

    /// <summary>
    /// Decodes a soft-mask image at its own size, as a one-component image whose Decode mapping gives the opacity; a soft mask whose
    /// codec decodes reduced resolutions is kept encoded instead, to be decoded at the resolution it is drawn at (issue #513).
    /// </summary>
    /// <param name="model">The soft-mask image.</param>
    /// <param name="cache">The document's image cache.</param>
    /// <param name="reducedWidth">The width of the image it masks when that image reduces, else 0.</param>
    /// <param name="reducedHeight">The height of the image it masks when that image reduces, else 0.</param>
    /// <param name="cancellationToken">The build's token.</param>
    /// <param name="prefetched">The decodes a build in slices prepared (issue #566), or <see langword="null"/>.</param>
    /// <returns>The soft mask, or <see langword="null"/> when it cannot be decoded (already a diagnostic) or has more than one component.</returns>
    private static DisplayImage? DecodeSoftMask(PdfImage model, ByteBudgetCache<(object Source, int Slot), IBudgetedEntry> cache, int reducedWidth, int reducedHeight, ImagePrefetch? prefetched, CancellationToken cancellationToken)
    {
        PdfImage.PreparedImage prepared = prefetched?.TakeSoftMask(model.Stream) ?? model.Prepare(cancellationToken);
        if (prepared.Encoded is { } encoded)
        {
            // Prepare keeps a soft mask encoded only when its codestream has the one gray component §11.6.5.2 asks for.
            return new DisplayImage(encoded, cache, PdfDeviceGrayColorSpace.Instance, model.CreateDecodeMapFor(1, encoded.Header.Geometry.BitsPerComponent, inverted: false), model.Interpolate);
        }

        DecodedImage? decoded = prepared.Decoded;
        try
        {
            if (decoded is null || decoded.Geometry.Components != 1)
            {
                return null;
            }

            // A small soft mask of the size of an image that reduces is made reducible too, by a box average, so that both fold at
            // the same reduction on every backend (issue #513); otherwise the image would have to be drawn at its own resolution.
            bool reducible = decoded.Geometry.Width == reducedWidth && decoded.Geometry.Height == reducedHeight;
            var soft = new DisplayImage(decoded, PdfDeviceGrayColorSpace.Instance, model.CreateDecodeMap(decoded), model.Interpolate, cache, reducible);
            decoded = null;
            return soft;
        }
        finally
        {
            decoded?.Dispose();
        }
    }

    /// <summary>
    /// Returns the pooled samples of the images and masks this build decoded and nothing else holds, for a build that failed
    /// (cancelled, or strict mode; issue #97). Images from, or given to, the document's image cache (issue #103) are left alone:
    /// other lists may hold them.
    /// </summary>
    internal void ReleaseImages()
    {
        _prefetch?.Dispose();
        _prefetch = null;
        foreach (object owned in _owned.AsSpan(0, _ownedCount))
        {
            switch (owned)
            {
                case DisplayImage image:
                    image.Release();
                    break;
                case DisplayImageMask mask:
                    mask.Release();
                    break;
            }
        }
    }

    /// <summary>
    /// Forgets the images and masks, without disposing them: the lists built from them, and the document's image cache, hold them,
    /// and the garbage collector frees them with the last holder.
    /// </summary>
    private void ResetImages()
    {
        _prefetch?.Dispose();
        _prefetch = null;
        Array.Clear(_images, 0, _imageCount);
        _imageCount = 0;
        Array.Clear(_imagePaints, 0, _imagePaintCount);
        _imagePaintCount = 0;
        Array.Clear(_imageMasks, 0, _imageMaskCount);
        _imageMaskCount = 0;
        _internedImages?.Clear();
        _internedMasks?.Clear();
        Array.Clear(_owned, 0, _ownedCount);
        _ownedCount = 0;
    }

    /// <summary>
    /// The decode of an image and its soft mask that <see cref="PrepareImage"/> runs in slices before the image's <c>Do</c> (issue
    /// #566): the image's <see cref="PdfImage.Prepare"/>, then, where <see cref="Decode"/> would decode one, its soft mask's.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §8.9.5 and §11.6.5.2. The soft mask is decoded only under the conditions <see cref="Decode"/> checks before it
    /// decodes one, so the diagnostics are those of the one-step build. Owns the samples until <see cref="Decode"/> takes them.
    /// </remarks>
    private sealed class ImagePrefetch(CosStream stream, PdfImage model, CancellationToken cancellationToken) : IDisposable
    {
        private PdfImage.LoadJob? _job;
        private PdfImage.PreparedImage? _image;
        private CosStream? _softStream;
        private PdfImage.PreparedImage? _softMask;
        private int _stage;

        /// <summary>Gets the image XObject.</summary>
        public CosStream Stream { get; } = stream;

        /// <summary>Gets the document's epoch when the decode started: what the document's image cache records for the result.</summary>
        public long Epoch { get; } = model.Document.Epoch;

        /// <summary>Gets a value indicating whether the image, and the soft mask it needs, are decoded.</summary>
        public bool IsDone => _stage == 2;

        /// <summary>Decodes until done or, after at least one unit of work, until <paramref name="deadline"/>.</summary>
        /// <returns><see langword="true"/> when done.</returns>
        public bool Continue(long deadline)
        {
            try
            {
                if (_stage == 0)
                {
                    _job ??= model.BeginPrepare(cancellationToken);
                    if (!_job.Continue(deadline))
                    {
                        return false;
                    }

                    _image = _job.Result;
                    _job.Dispose();
                    _job = null;
                    if (SoftMaskToDecode(_image.Value) is not { } soft)
                    {
                        _stage = 2;
                        return true;
                    }

                    _softStream = soft.Stream;
                    _job = soft.BeginPrepare(cancellationToken);
                    _stage = 1;
                    if (System.Diagnostics.Stopwatch.GetTimestamp() >= deadline)
                    {
                        return false;
                    }
                }

                if (_stage == 1)
                {
                    if (!_job!.Continue(deadline))
                    {
                        return false;
                    }

                    _softMask = _job.Result;
                    _job.Dispose();
                    _job = null;
                    _stage = 2;
                }

                return true;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>Takes the image's prepared samples; the caller owns them.</summary>
        public PdfImage.PreparedImage? TakeImage()
        {
            PdfImage.PreparedImage? image = _image;
            _image = null;
            return image;
        }

        /// <summary>Takes the soft mask's prepared samples when <paramref name="softStream"/> is the one decoded; the caller owns them.</summary>
        public PdfImage.PreparedImage? TakeSoftMask(CosStream? softStream)
        {
            if (softStream is null || !ReferenceEquals(softStream, _softStream))
            {
                return null;
            }

            PdfImage.PreparedImage? softMask = _softMask;
            _softMask = null;
            return softMask;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _stage = 2;
            _job?.Dispose();
            _job = null;
            _image?.Decoded?.Dispose();
            _image = null;
            _softMask?.Decoded?.Dispose();
            _softMask = null;
        }

        /// <summary>The soft mask <see cref="Decode"/> decodes after the image, under the same conditions; <see langword="null"/> when none.</summary>
        private PdfImage? SoftMaskToDecode(PdfImage.PreparedImage prepared)
        {
            if (prepared.Encoded is not null)
            {
                if (prepared.EncodedColorSpace!.Family == PdfColorSpaceFamily.Pattern)
                {
                    return null;
                }
            }
            else if (prepared.Decoded is not { } decoded
                || model.ResolveColorSpace(decoded) is not { } space
                || space.Family == PdfColorSpaceFamily.Pattern
                || space.ComponentCount != decoded.Geometry.Components)
            {
                return null;
            }

            return model.MaskKind == PdfImageMaskKind.Soft ? model.Mask : null;
        }
    }

    /// <summary>The masking an image carries into the list.</summary>
    private readonly record struct MaskParts(DisplayImageMask? Mask, DisplayImage? SoftMask, double[] ColorKey, float[] Matte);
}
