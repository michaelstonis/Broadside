using Broadside.Graphics;
using Broadside.Rendering;

namespace Broadside.Viewer;

/// <summary>The default <see cref="ITileBackend"/>: the fully managed <see cref="SoftwareRasterizer"/>, sliced through <see cref="ProgressiveRender"/>.</summary>
/// <remarks>
/// ISO 32000-2 §10.7.1. Spec #33 story 54 (software by default) and viewer capability 2. A viewer's tiles are for display, so images
/// are decoded at the resolution they are drawn at unless the constructor says otherwise (<see cref="RenderOptions.ScaledImageDecode"/>).
/// </remarks>
public sealed class SoftwareTileBackend : ITileBackend
{
    private readonly SoftwareRasterizer _rasterizer = new();

    /// <summary>Initializes a new instance of the <see cref="SoftwareTileBackend"/> class that decodes images at the resolution they are drawn at, keeping 2 samples per device pixel.</summary>
    public SoftwareTileBackend()
        : this(scaledImageDecode: true, RenderOptions.DefaultImageSamplesPerPixel)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SoftwareTileBackend"/> class.</summary>
    /// <param name="scaledImageDecode">Whether images are decoded at the resolution they are drawn at (<see cref="RenderOptions.ScaledImageDecode"/>).</param>
    /// <param name="imageSamplesPerPixel">The fewest decoded samples per device pixel a reduced image keeps, at least 1 (<see cref="RenderOptions.ImageSamplesPerPixel"/>).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="imageSamplesPerPixel"/> is not a finite number of at least 1.</exception>
    public SoftwareTileBackend(bool scaledImageDecode, double imageSamplesPerPixel)
    {
        _rasterizer.ScaledImageDecode = scaledImageDecode;
        _rasterizer.ImageSamplesPerPixel = imageSamplesPerPixel;
    }

    /// <inheritdoc/>
    public ITileRender Begin(DisplayList list, Bitmap target, in Matrix deviceTransform, CancellationToken cancellationToken) =>
        new Rendering(_rasterizer.BeginRender(list, target, deviceTransform, cancellationToken, null));

    /// <summary>Releases the rasterizer and its pooled scratch buffers.</summary>
    public void Dispose() => _rasterizer.Dispose();

    private sealed class Rendering(ProgressiveRender render) : ITileRender
    {
        public bool RenderSlice(TimeSpan budget) => render.Continue(budget);

        public void Dispose() => render.Dispose();
    }
}
