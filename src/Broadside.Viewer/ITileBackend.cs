using Broadside.Graphics;
using Broadside.Rendering;

namespace Broadside.Viewer;

/// <summary>
/// The backend choice of a <see cref="DocumentSession"/>: draws a cached display list into a tile's pixels, in slices a single-threaded
/// host can interleave with its UI. <see cref="SoftwareTileBackend"/> is the default.
/// </summary>
/// <remarks>
/// ISO 32000-2 §10.7.1 (rendering) through the display list: a backend replays the list through an
/// <see cref="IRenderingBackend"/> and never reads content operators. A backend is used by one render at a time; the session keeps a
/// pool of them, one per concurrent render, and disposes them when it closes.
/// </remarks>
public interface ITileBackend : IDisposable
{
    /// <summary>Starts drawing <paramref name="list"/> into <paramref name="target"/>.</summary>
    /// <param name="list">The page's display list.</param>
    /// <param name="target">The tile's pixels, holding the background on entry (white paper, or transparent for an overlay tile).</param>
    /// <param name="deviceTransform">Default user space to the target's pixels.</param>
    /// <param name="cancellationToken">Stops the render; <see cref="ITileRender.RenderSlice"/> then throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The render in progress; the caller disposes it.</returns>
    ITileRender Begin(DisplayList list, Bitmap target, in Matrix deviceTransform, CancellationToken cancellationToken);
}

/// <summary>A tile render in progress: advanced in time slices, disposed when done or abandoned.</summary>
/// <remarks>
/// ISO 32000-2 §10.7.1. Spec #33 viewer capability 2 (time-sliced replay). A backend may leave a finished tile on the device (the
/// WebGL backend keeps it in its canvas) instead of in the target bitmap: <see cref="ReadBack"/> brings it into the bitmap,
/// and disposing the render drops it. The session therefore disposes a render after its tile is presented and before the bitmap is
/// released, and reads the tile back first for a surface that reads pixels (<see cref="IRenderSurface.AcceptsDevicePixels"/>, #380).
/// </remarks>
public interface ITileRender : IDisposable
{
    /// <summary>Draws for about <paramref name="budget"/>, at least one operation.</summary>
    /// <param name="budget">The slice; <see cref="Timeout.InfiniteTimeSpan"/> draws to the end.</param>
    /// <returns><see langword="true"/> when the tile is complete.</returns>
    bool RenderSlice(TimeSpan budget);

    /// <summary>
    /// Puts the finished tile's pixels into the target bitmap: a backend that left them on the device reads them back. Called once
    /// <see cref="RenderSlice"/> has returned <see langword="true"/>, before the tile goes to a surface that reads its pixels.
    /// </summary>
    /// <remarks>ISO 32000-2 §10.7.1. The default does nothing, for backends that draw into the bitmap.</remarks>
    void ReadBack()
    {
    }
}
