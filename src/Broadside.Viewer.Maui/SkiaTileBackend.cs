using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.Rendering.Skia;
using SkiaSharp;

namespace Broadside.Viewer.Maui;

/// <summary>
/// An <see cref="ITileBackend"/> that draws tiles with SkiaSharp (<see cref="SkiaBackend"/>): native glyphs, images, shadings and
/// transparency through Skia's own pipeline, into the session's pooled tile pixels. <see cref="PdfView"/> uses it by default.
/// </summary>
/// <remarks>
/// ISO 32000-2 §10.7.1 (rendering), §8.3.2.2 (device space). A tile is drawn in one slice: Skia's replay is not resumable, so this
/// backend suits <see cref="ViewerThreading.Background"/> sessions, where a tile renders off the UI thread. A viewer's tiles are for
/// display, so images are decoded at the resolution they are drawn at unless the constructor says otherwise
/// (<see cref="RenderOptions.ScaledImageDecode"/>; never for printing or export). The backends of a session's pool draw tiles at
/// the same time and share the images they convert (<see cref="SkiaBackend.ShareConvertedImages"/>): each image is converted once,
/// within the document's image cache budget, whichever backend draws it.
/// </remarks>
public sealed class SkiaTileBackend : ITileBackend
{
    private readonly SkiaBackend _backend = new();

    /// <summary>Initializes a new instance of the <see cref="SkiaTileBackend"/> class that decodes images at the resolution they are drawn at, keeping 2 samples per device pixel.</summary>
    public SkiaTileBackend()
        : this(scaledImageDecode: true, RenderOptions.DefaultImageSamplesPerPixel)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SkiaTileBackend"/> class.</summary>
    /// <param name="scaledImageDecode">Whether images are decoded at the resolution they are drawn at (<see cref="RenderOptions.ScaledImageDecode"/>).</param>
    /// <param name="imageSamplesPerPixel">The fewest decoded samples per device pixel a reduced image keeps, at least 1 (<see cref="RenderOptions.ImageSamplesPerPixel"/>).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="imageSamplesPerPixel"/> is not a finite number of at least 1.</exception>
    public SkiaTileBackend(bool scaledImageDecode, double imageSamplesPerPixel)
    {
        _backend.ScaledImageDecode = scaledImageDecode;
        _backend.ImageSamplesPerPixel = imageSamplesPerPixel;
        _backend.ShareConvertedImages = true;
    }

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.8 and §8.9.4: at the reductions this backend will ask for.</remarks>
    public ValueTask PrefetchImagesAsync(DisplayList list, in Matrix deviceTransform, int width, int height, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(list);
        double samplesPerPixel = _backend.ScaledImageDecode ? _backend.ImageSamplesPerPixel : 0;
        return list.PrefetchImagesAsync(deviceTransform, TryGetRegion(deviceTransform, width, height, out PdfRectangle region) ? region : null, samplesPerPixel, cancellationToken);
    }

    /// <inheritdoc/>
    public ITileRender Begin(DisplayList list, Bitmap target, in Matrix deviceTransform, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(target);
        return new Rendering(_backend, list, target, deviceTransform, cancellationToken);
    }

    /// <summary>Releases the Skia backend's cached paths and paints.</summary>
    public void Dispose() => _backend.Dispose();

    /// <summary>The part of default user space a <paramref name="width"/> × <paramref name="height"/> tile shows, two pixels wider; false without an inverse.</summary>
    private static bool TryGetRegion(in Matrix device, int width, int height, out PdfRectangle region)
    {
        region = default;
        if (!device.TryInvert(out Matrix toPage))
        {
            return false;
        }

        PathPoint a = toPage.Transform(-2, -2);
        PathPoint b = toPage.Transform(width + 2, -2);
        PathPoint c = toPage.Transform(width + 2, height + 2);
        PathPoint d = toPage.Transform(-2, height + 2);
        double left = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
        double bottom = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
        double right = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
        double top = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
        if (!(double.IsFinite(left) && double.IsFinite(bottom) && double.IsFinite(right) && double.IsFinite(top)))
        {
            return false;
        }

        region = new PdfRectangle(left, bottom, right, top);
        return true;
    }

    private sealed class Rendering(SkiaBackend backend, DisplayList list, Bitmap target, Matrix deviceTransform, CancellationToken cancellationToken)
        : ITileRender
    {
        private bool _done;

        public unsafe bool RenderSlice(TimeSpan budget)
        {
            if (_done)
            {
                return true;
            }

            _done = true;
            cancellationToken.ThrowIfCancellationRequested();
            var info = new SKImageInfo(target.Width, target.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            fixed (byte* pixels = target.Pixels)
            {
                using SKSurface surface = SKSurface.Create(info, (IntPtr)pixels, target.Stride);
                backend.Render(list, surface.Canvas, deviceTransform);
                surface.Canvas.Flush();
            }

            return true;
        }

        public void Dispose()
        {
        }
    }
}
