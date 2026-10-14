using System.Runtime.InteropServices;
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

        if (image.IsStencil)
        {
            PaintStencil(image, model, context);
            return;
        }

        int index = InternImage(image.IsInline ? null : image.Stream, model, context.Document);
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
        SetCompositing(state);
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
            if (stream is not null && context.Document.Caches.Images.TryGet((stream, true), out DisplayImageEntry? cached))
            {
                mask = cached.Mask;
            }
            else
            {
                long epoch = CosMutation.Epoch;
                mask = DecodeMask(model);
                if (stream is not null)
                {
                    context.Document.Caches.Images.Add((stream, true), new DisplayImageEntry(epoch, null, mask));
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
        SetCompositing(state);
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
    /// <returns>The index into the image table, or −1 when the image cannot be decoded or has no usable colour space.</returns>
    private int InternImage(CosStream? stream, PdfImage model, PdfDocument document)
    {
        if (stream is not null && _internedImages is not null && _internedImages.TryGetValue(stream, out int known))
        {
            return known;
        }

        DisplayImage? image;
        if (stream is not null && document.Caches.Images.TryGet((stream, false), out DisplayImageEntry? cached))
        {
            image = cached.Image;
        }
        else
        {
            long epoch = CosMutation.Epoch;
            image = Decode(model);
            if (stream is not null)
            {
                document.Caches.Images.Add((stream, false), new DisplayImageEntry(epoch, image, null));
            }
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

    /// <summary>Decodes an image and its mask.</summary>
    /// <returns>The image, or <see langword="null"/> (the reason is already a diagnostic of the image).</returns>
    private static DisplayImage? Decode(PdfImage model)
    {
        DecodedImage? decoded = model.Decode();
        try
        {
            if (decoded is null
                || model.ResolveColorSpace(decoded) is not { } space
                || space.Family == PdfColorSpaceFamily.Pattern
                || space.ComponentCount != decoded.Components)
            {
                return null;
            }

            MaskParts mask = DecodeMasks(model, decoded, space);
            var image = new DisplayImage(decoded, space, model.CreateDecodeMap(decoded), model.Interpolate)
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
    /// </remarks>
    private static MaskParts DecodeMasks(PdfImage model, DecodedImage decoded, PdfColorSpace space)
    {
        switch (model.MaskKind)
        {
            case PdfImageMaskKind.Explicit when model.Mask is { } explicitMask:
                return new MaskParts(DecodeMask(explicitMask), null, [], []);
            case PdfImageMaskKind.ColorKey when model.ColorKey is { } ranges:
                return new MaskParts(null, null, [.. ranges], []);
            case PdfImageMaskKind.Soft when model.Mask is { } softMask && DecodeSoftMask(softMask) is { } soft:
                return new MaskParts(null, soft, [], model.Matte is { } matte ? [.. matte.Select(static m => (float)m)] : []);
            case PdfImageMaskKind.SoftInData when decoded.Alpha is { } alpha:
                int components = space is PdfIndexedColorSpace indexed ? indexed.Base.ComponentCount : space.ComponentCount;
                var opacity = new DisplayImage(alpha, PdfDeviceGrayColorSpace.Instance, ImageDecodeMap.Create([0, 1], 1, alpha.BitsPerComponent), model.Interpolate);
                return new MaskParts(null, opacity, [], model.SoftMaskInData == 2 || decoded.AlphaPremultiplied ? new float[components] : []);
            default:
                return new MaskParts(null, null, [], []);
        }
    }

    /// <summary>Decodes an image mask (a stencil mask, or an image's explicit mask) at its own size.</summary>
    /// <returns>The mask, or <see langword="null"/> when it cannot be decoded (already a diagnostic) or is not one bit per sample.</returns>
    private static DisplayImageMask? DecodeMask(PdfImage model)
    {
        DecodedImage? decoded = model.Decode();
        try
        {
            if (decoded is null || decoded.Components != 1 || decoded.BitsPerComponent != 1)
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

    /// <summary>Decodes a soft-mask image at its own size, as a one-component image whose Decode mapping gives the opacity.</summary>
    /// <returns>The soft mask, or <see langword="null"/> when it cannot be decoded (already a diagnostic) or has more than one component.</returns>
    private static DisplayImage? DecodeSoftMask(PdfImage model)
    {
        DecodedImage? decoded = model.Decode();
        try
        {
            if (decoded is null || decoded.Components != 1)
            {
                return null;
            }

            var soft = new DisplayImage(decoded, PdfDeviceGrayColorSpace.Instance, model.CreateDecodeMap(decoded), model.Interpolate);
            decoded = null;
            return soft;
        }
        finally
        {
            decoded?.Dispose();
        }
    }

    /// <summary>
    /// Forgets the images and masks, without disposing them: the lists built from them, and the document's image cache, hold them,
    /// and the garbage collector frees them with the last holder.
    /// </summary>
    private void ResetImages()
    {
        Array.Clear(_images, 0, _imageCount);
        _imageCount = 0;
        Array.Clear(_imagePaints, 0, _imagePaintCount);
        _imagePaintCount = 0;
        Array.Clear(_imageMasks, 0, _imageMaskCount);
        _imageMaskCount = 0;
        _internedImages?.Clear();
        _internedMasks?.Clear();
    }

    /// <summary>The masking an image carries into the list.</summary>
    private readonly record struct MaskParts(DisplayImageMask? Mask, DisplayImage? SoftMask, double[] ColorKey, float[] Matte);
}
