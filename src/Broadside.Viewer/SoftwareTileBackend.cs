using Broadside.Graphics;
using Broadside.Rendering;

namespace Broadside.Viewer;

/// <summary>The default <see cref="ITileBackend"/>: the fully managed <see cref="SoftwareRasterizer"/>, sliced through <see cref="ProgressiveRender"/>.</summary>
/// <remarks>ISO 32000-2 §10.7.1. Spec #33 story 54 (software by default) and viewer capability 2.</remarks>
public sealed class SoftwareTileBackend : ITileBackend
{
    private readonly SoftwareRasterizer _rasterizer = new();

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
