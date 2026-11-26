using Broadside.Content;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Images;

/// <summary>
/// The content processor that extracts painted images: every image XObject and inline image a run paints, with its <see cref="PdfImage"/>,
/// its quad and bounds in page space, and the colour state in effect. Runs alone through <see cref="PdfPage.ExtractImages()"/>, or
/// beside other processors in one run through <see cref="CompositeContentProcessor"/>.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.8 (XObjects), §8.9.4 (image coordinate system), §8.9.5 (image XObjects, including alternates selected by §8.9.5.4),
/// §8.9.6.2 (image masks), §8.9.7 (inline images), §8.10 (form XObjects), §8.11.3.1 (optional content) and §14.8.2.2 (artifacts).
/// Images painted inside form XObjects are extracted where the form paints them. Images in <c>Artifact</c> marked-content sequences
/// are left out unless <see cref="ImageExtractionOptions.IncludeArtifacts"/> asks for them; images hidden by optional content are left
/// out unless <see cref="ImageExtractionOptions.IncludeHiddenContent"/> asks for them.
/// </para>
/// <para>
/// Images accumulate over runs until <see cref="Clear"/>; the buffer is kept, so an extractor reused for page after page allocates
/// nothing per image once its buffer has grown. Not thread-safe: use one extractor per thread.
/// </para>
/// </remarks>
public sealed class ImageExtractor : ContentProcessor
{
    /// <summary>The most images an extractor <see cref="Return"/>ed to its thread may hold room for.</summary>
    internal const int SharedImageLimit = 1 << 12;

    [ThreadStatic]
    private static ImageExtractor? _shared;

    private bool _includeHidden;
    private bool _includeArtifacts;
    private bool _includeImageMasks = true;
    private ExtractedImage[] _images = new ExtractedImage[16];
    private int _imageCount;
    private int _markedDepth;
    private int _artifactDepth;

    /// <summary>Creates an extractor with the default options.</summary>
    public ImageExtractor()
        : this(new ImageExtractionOptions())
    {
    }

    /// <summary>Creates an extractor.</summary>
    /// <param name="options">What to extract; read once here.</param>
    public ImageExtractor(ImageExtractionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Configure(options);
    }

    /// <inheritdoc/>
    public override ContentEvents Events =>
        ContentEvents.Images | ContentEvents.MarkedContent | ContentEvents.Forms | (_includeHidden ? ContentEvents.HiddenContent : ContentEvents.None);

    /// <summary>Gets the images extracted since the last <see cref="Clear"/>, in content order. Valid until the extractor next runs or is cleared.</summary>
    /// <remarks>ISO 32000-2 §8.9.</remarks>
    public ReadOnlySpan<ExtractedImage> Images => _images.AsSpan(0, _imageCount);

    /// <summary>Forgets the extracted images, keeping the buffer.</summary>
    public void Clear()
    {
        Array.Clear(_images, 0, _imageCount);
        _imageCount = 0;
    }

    /// <summary>
    /// Takes this thread's shared extractor, empty and configured with <paramref name="options"/>, or a new one when it is in use
    /// or was dropped. Give it back with <see cref="Return"/> once its output has been copied.
    /// </summary>
    internal static ImageExtractor Rent(ImageExtractionOptions options)
    {
        ImageExtractor? extractor = _shared;
        if (extractor is null)
        {
            return new ImageExtractor(options);
        }

        _shared = null;
        extractor.Configure(options);
        return extractor;
    }

    /// <summary>Empties <paramref name="extractor"/> and keeps it as this thread's shared extractor unless its buffer grew past <see cref="SharedImageLimit"/>.</summary>
    internal static void Return(ImageExtractor extractor)
    {
        extractor.Clear();
        if (extractor._images.Length <= SharedImageLimit)
        {
            _shared = extractor;
        }
    }

    /// <inheritdoc/>
    public override void BeginRun(ContentContext context)
    {
        _markedDepth = 0;
        _artifactDepth = 0;
    }

    /// <inheritdoc/>
    public override void BeginMarkedContent(in MarkedContentEvent mc, ContentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _markedDepth++;
        if (_artifactDepth == 0 && mc.Tag.SequenceEqual("Artifact"u8))
        {
            _artifactDepth = _markedDepth;
        }
    }

    /// <inheritdoc/>
    public override void EndMarkedContent(in MarkedContentEvent mc, ContentContext context)
    {
        if (_markedDepth == 0)
        {
            return;
        }

        if (_markedDepth == _artifactDepth)
        {
            _artifactDepth = 0;
        }

        _markedDepth--;
    }

    /// <inheritdoc/>
    public override void PaintImage(in ImageEvent image, ContentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (image.Image is not { } model)
        {
            return;
        }

        if (image.IsHidden && !_includeHidden)
        {
            return;
        }

        bool isArtifact = _artifactDepth > 0;
        if (isArtifact && !_includeArtifacts)
        {
            return;
        }

        bool isMask = image.IsImageMask || model.IsImageMask;
        if (isMask && !_includeImageMasks)
        {
            return;
        }

        var flags = ExtractedImageFlags.None;
        if (image.IsInline)
        {
            flags |= ExtractedImageFlags.Inline;
        }

        if (isMask)
        {
            flags |= ExtractedImageFlags.ImageMask;
        }

        if (image.IsHidden)
        {
            flags |= ExtractedImageFlags.Hidden;
        }

        if (isArtifact)
        {
            flags |= ExtractedImageFlags.Artifact;
        }

        ref readonly GraphicsState state = ref context.State;
        RenderingIntent intent = model.Intent ?? state.RenderingIntent;
        CosName? name = image.IsInline || image.ResourceName.IsEmpty ? null : new CosName(image.ResourceName);
        if (_imageCount == _images.Length)
        {
            Array.Resize(ref _images, _images.Length * 2);
        }

        _images[_imageCount++] = new ExtractedImage
        {
            Image = model,
            Name = name,
            Quad = Quad.FromRectangle(image.Ctm, 0, 0, 1, 1),
            Ctm = image.Ctm,
            FillColor = state.FillColor,
            RenderingIntent = intent,
            DefaultColorSpaces = context.DefaultColorSpaces,
            Flags = flags,
        };
    }

    private void Configure(ImageExtractionOptions options)
    {
        _includeHidden = options.IncludeHiddenContent;
        _includeArtifacts = options.IncludeArtifacts;
        _includeImageMasks = options.IncludeImageMasks;
    }
}
