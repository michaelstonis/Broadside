using System.Runtime.Versioning;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.Rendering.WebGL;

namespace Broadside.Viewer.Blazor;

/// <summary>
/// The WebGL 2 tile backend for the browser viewer: tiles are drawn on the GPU (<see cref="WebGLRenderer"/>) and copied from its canvas
/// into the viewer's tile canvases without reading the pixels back. Select it with
/// <c>new ViewerOptions().UseBackend(WebGLTileBackend.Create)</c> after awaiting <see cref="InitializeAsync"/>; the software
/// rasterizer (<see cref="SoftwareTileBackend"/>) stays the default. Pages or regions the GPU path does not draw fall back to the
/// software rasterizer per tile, with a diagnostic (<see cref="Diagnostics"/>).
/// </summary>
/// <remarks>ISO 32000-2 §10.7.1 through the display list (ADR 0006); ADR 0012 (the WebGL backend and its fallbacks).</remarks>
[SupportedOSPlatform("browser")]
public sealed class WebGLTileBackend : ITileBackend
{
    private readonly WebGLRenderer _renderer = new();

    /// <summary>Gets a value indicating whether WebGL 2 is available (after <see cref="InitializeAsync"/>); without it tiles are drawn by the software rasterizer.</summary>
    public static bool IsAvailable => WebGLRenderer.IsAvailable;

    /// <summary>Gets the diagnostics of this backend's last tile.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics => _renderer.Diagnostics;

    /// <summary>Loads the WebGL backend's script and creates its context; completes with false when WebGL 2 is unavailable.</summary>
    /// <returns>Whether WebGL 2 is available.</returns>
    public static Task<bool> InitializeAsync() => WebGLRenderer.InitializeAsync();

    /// <summary>Creates a backend (for <see cref="ViewerOptions.UseBackend"/>).</summary>
    /// <returns>A new backend.</returns>
    public static ITileBackend Create() => new WebGLTileBackend();

    /// <inheritdoc />
    public ITileRender Begin(DisplayList list, Bitmap target, in Matrix deviceTransform, CancellationToken cancellationToken)
    {
        // The canvas holds at most one finished tile; one not presented yet is read back before the next render overwrites it.
        GpuPresentation.Flush();
        return new Rendering(_renderer.BeginRender(list, target, deviceTransform, readBack: false, cancellationToken), target);
    }

    /// <inheritdoc />
    public void Dispose() => _renderer.Dispose();

    private sealed class Rendering(WebGLRender render, Bitmap target) : ITileRender
    {
        public bool RenderSlice(TimeSpan budget)
        {
            if (!render.Continue(budget))
            {
                return false;
            }

            if (render.IsInCanvas)
            {
                GpuPresentation.Hold(target);
            }

            return true;
        }

        public void Dispose() => render.Dispose();
    }
}

/// <summary>
/// The tile the WebGL canvas holds until the surface presents it: <see cref="CanvasSurface"/> claims it and copies the canvas; any
/// other use of the pixels (a new render, a surface that reads them) gets them read back first.
/// </summary>
[SupportedOSPlatform("browser")]
internal static class GpuPresentation
{
    private static Bitmap? _held;

    public static void Hold(Bitmap target)
    {
        Flush();
        _held = target;
    }

    public static bool TryClaim(Bitmap pixels)
    {
        if (!ReferenceEquals(_held, pixels))
        {
            Flush();
            return false;
        }

        _held = null;
        return true;
    }

    public static void Flush()
    {
        if (_held is { } held)
        {
            _held = null;
            WebGLRenderer.ReadCanvas(held);
        }
    }
}
