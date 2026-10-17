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
/// backend suits <see cref="ViewerThreading.Background"/> sessions, where a tile renders off the UI thread.
/// </remarks>
public sealed class SkiaTileBackend : ITileBackend
{
    private readonly SkiaBackend _backend = new();

    /// <inheritdoc/>
    public ITileRender Begin(DisplayList list, Bitmap target, in Matrix deviceTransform, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(target);
        return new Rendering(_backend, list, target, deviceTransform, cancellationToken);
    }

    /// <summary>Releases the Skia backend's cached paths and paints.</summary>
    public void Dispose() => _backend.Dispose();

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
