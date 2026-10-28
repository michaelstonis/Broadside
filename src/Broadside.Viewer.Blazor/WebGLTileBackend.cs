using System.Runtime.Versioning;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.Rendering.WebGL;

namespace Broadside.Viewer.Blazor;

/// <summary>
/// The WebGL 2 tile backend for the browser viewer: tiles are drawn on the GPU (<see cref="WebGLRenderer"/>) and copied from its canvas
/// into the viewer's tile canvases without reading the pixels back; a surface that reads pixels (a thumbnail, a PNG) gets them read back
/// (<see cref="IRenderSurface.AcceptsDevicePixels"/>, #380). Select it with
/// <c>new ViewerOptions().UseBackend(WebGLTileBackend.Create)</c> after awaiting <see cref="InitializeAsync"/>; the software
/// rasterizer (<see cref="SoftwareTileBackend"/>) stays the default. Pages or regions the GPU path does not draw fall back to the
/// software rasterizer per tile, with a diagnostic (<see cref="Diagnostics"/>).
/// </summary>
/// <remarks>
/// ISO 32000-2 §10.7.1 through the display list. The WebGL backend is opt-in and falls back to the software rasterizer. A viewer's
/// tiles are for display, so images are uploaded at the resolution they are drawn at unless the constructor says otherwise
/// (<see cref="RenderOptions.ScaledImageDecode"/>; never for printing or export).
/// </remarks>
[SupportedOSPlatform("browser")]
public sealed class WebGLTileBackend : ITileBackend
{
    private readonly WebGLRenderer _renderer = new();

    /// <summary>Initializes a new instance of the <see cref="WebGLTileBackend"/> class that decodes images at the resolution they are drawn at, keeping 2 samples per device pixel.</summary>
    public WebGLTileBackend()
        : this(scaledImageDecode: true, RenderOptions.DefaultImageSamplesPerPixel)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="WebGLTileBackend"/> class.</summary>
    /// <param name="scaledImageDecode">Whether images are decoded at the resolution they are drawn at (<see cref="RenderOptions.ScaledImageDecode"/>).</param>
    /// <param name="imageSamplesPerPixel">The fewest decoded samples per device pixel a reduced image keeps, at least 1 (<see cref="RenderOptions.ImageSamplesPerPixel"/>).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="imageSamplesPerPixel"/> is not a finite number of at least 1.</exception>
    public WebGLTileBackend(bool scaledImageDecode, double imageSamplesPerPixel)
    {
        _renderer.ScaledImageDecode = scaledImageDecode;
        _renderer.ImageSamplesPerPixel = imageSamplesPerPixel;
    }

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
    /// <remarks>
    /// ISO 32000-2 §7.4.8 and §8.9.4. The platform decodes images at the reduction this backend draws them at (at full size in Chrome
    /// and Edge, the YCbCr planes: bit-identical to the managed decoder). In a browser without WebCodecs <c>ImageDecoder</c>
    /// (Safari), simple JPEG paints go to <c>createImageBitmap</c> and are uploaded straight to a texture, never read back
    /// (<see cref="WebGLRenderer.PrefetchImageBitmapsAsync"/>); the rest decode with the managed codec.
    /// </remarks>
    public ValueTask PrefetchImagesAsync(DisplayList list, in Matrix deviceTransform, int width, int height, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(list);
        PdfRectangle? region = TileRegion.Of(deviceTransform, width, height);
        if (BrowserImageDecoder.ImageBitmapOnly && list.Document.UsesPlatformImageDecoder(BrowserImageDecoder.DecoderName))
        {
            return new ValueTask(BothAsync(list, deviceTransform, region, SamplesPerPixel, cancellationToken));
        }

        return list.PrefetchImagesAsync(deviceTransform, region, SamplesPerPixel, cancellationToken);
    }

    /// <summary>Gets the samples per pixel this backend reduces images to, or 0 for their own resolution.</summary>
    private double SamplesPerPixel => _renderer.ScaledImageDecode ? _renderer.ImageSamplesPerPixel : 0;

    private static async Task BothAsync(DisplayList list, Matrix deviceTransform, PdfRectangle? region, double samplesPerPixel, CancellationToken cancellationToken)
    {
        await WebGLRenderer.PrefetchImageBitmapsAsync(list, deviceTransform, region, cancellationToken).ConfigureAwait(true);
        await list.PrefetchImagesAsync(deviceTransform, region, samplesPerPixel, cancellationToken).ConfigureAwait(true);
    }

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

        public void ReadBack() => GpuPresentation.ReadBack(target);

        public void Dispose()
        {
            // The bitmap is released after the render: a tile no surface claimed is dropped, never read back into it later (#380).
            GpuPresentation.Release(target);
            render.Dispose();
        }
    }
}

/// <summary>
/// The tile the WebGL canvas holds from the end of its render until the render is disposed: <see cref="CanvasSurface"/> claims it and
/// copies the canvas; a surface that reads pixels gets them read back first (<see cref="ReadBack"/>), and a new render reads back a
/// tile still held before it overwrites the canvas. Disposing the render drops the hold, before the session releases the bitmap (#380).
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

    public static void ReadBack(Bitmap target)
    {
        if (ReferenceEquals(_held, target))
        {
            Flush();
        }
    }

    public static void Release(Bitmap target)
    {
        if (ReferenceEquals(_held, target))
        {
            _held = null;
        }
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
