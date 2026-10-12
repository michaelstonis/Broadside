using Broadside.Content;
using Broadside.Graphics;
using Broadside.Images;

namespace Broadside.Rendering;

/// <summary>Image XObjects and inline images (ISO 32000-2 §8.9.4, §8.9.5, §8.9.7), decoded once per list and interned.</summary>
internal sealed partial class DisplayListBuilder
{
    private DisplayImage[] _images = new DisplayImage[4];
    private int _imageCount;
    private DisplayImagePaint[] _imagePaints = new DisplayImagePaint[4];
    private int _imagePaintCount;

    // Image XObject streams seen in this build -> index into _images, or -1 when the image cannot be painted.
    private Dictionary<object, int>? _internedImages;

    /// <inheritdoc/>
    /// <remarks>
    /// ISO 32000-2 §8.9.4: the image is painted into the unit square of user space under the CTM. An image XObject is decoded the
    /// first time it is painted and every later paint in the list shares the result; an inline image (§8.9.7) is decoded where it
    /// appears. The image's Intent entry, when present, replaces the graphics state's rendering intent for it (§8.6.5.8). Image masks
    /// (§8.9.6.2) are not in the list yet; a later Phase 3 ticket adds them with the other masks.
    /// </remarks>
    public override void PaintImage(in ImageEvent image, ContentContext context)
    {
        if (image.IsStencil || image.Image is not { } model)
        {
            return;
        }

        int index = InternImage(image.IsInline ? null : image.Stream, model);
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
        SetTransform(image.Ctm);
        AddOperation(new DisplayOperation(DisplayOperationKind.DrawImage, default, index, paint));
    }

    /// <summary>Returns the index of the decoded image, decoding it unless this image XObject was decoded earlier in the build.</summary>
    /// <param name="stream">The image XObject's stream, the interning key; <see langword="null"/> for an inline image.</param>
    /// <param name="model">The image.</param>
    /// <returns>The index into the image table, or −1 when the image cannot be decoded or has no usable colour space.</returns>
    private int InternImage(object? stream, PdfImage model)
    {
        if (stream is not null && _internedImages is not null && _internedImages.TryGetValue(stream, out int known))
        {
            return known;
        }

        int index = Decode(model);
        if (stream is not null)
        {
            _internedImages ??= new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
            _internedImages[stream] = index;
        }

        return index;
    }

    /// <summary>Decodes an image into the image table.</summary>
    /// <returns>Its index, or −1 (the reason is already a diagnostic of the image).</returns>
    private int Decode(PdfImage model)
    {
        DecodedImage? decoded = model.Decode();
        try
        {
            if (decoded is null
                || model.ResolveColorSpace(decoded) is not { } space
                || space.Family == PdfColorSpaceFamily.Pattern
                || space.ComponentCount != decoded.Components)
            {
                return -1;
            }

            int index = _imageCount;
            Append(ref _images, ref _imageCount, new DisplayImage(decoded, space, model.CreateDecodeMap(decoded), model.Interpolate));
            decoded = null;
            return index;
        }
        finally
        {
            decoded?.Dispose();
        }
    }

    /// <summary>Forgets the images, without disposing them: the lists built from them own them.</summary>
    private void ResetImages()
    {
        Array.Clear(_images, 0, _imageCount);
        _imageCount = 0;
        Array.Clear(_imagePaints, 0, _imagePaintCount);
        _imagePaintCount = 0;
        _internedImages?.Clear();
    }
}
