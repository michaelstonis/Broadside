using System.Collections.ObjectModel;
using Broadside.Diagnostics;
using Broadside.Graphics;
using SkiaSharp;

namespace Broadside.Rendering.Skia;

/// <summary>
/// The SkiaSharp backend: renders a page or a <see cref="DisplayList"/> onto an <see cref="SKCanvas"/> (an <see cref="SKBitmap"/>, a
/// surface, or the canvas a MAUI, Avalonia or Uno view hands its paint handler).
/// </summary>
/// <remarks>
/// <para>
/// ADR 0006: it consumes display lists only, replaying them through its own <see cref="IRenderingBackend"/>. Paths fill with the
/// non-zero winding or even-odd rule (ISO 32000-2 §8.5.3.3), strokes follow the line parameters of §8.4.3 and the stroking rules of
/// §8.5.3.2 (each subpath on its own, dashes restarted per subpath and measured in user space, a degenerate subpath a dot only with
/// round caps, the thinnest line one device pixel, stroke adjustment of §10.7.5), and clipping paths intersect the clip, which
/// <see cref="IRenderingBackend.RestoreState"/> restores (§8.5.4). Edges are anti-aliased by Skia. Colours convert to RGB through
/// <see cref="PdfDocument.GetColorConverter(PdfColorSpace, ColorConversion, PdfDefaultColorSpaces?)"/>, so the engine's colour
/// management decides them (§8.6, §10). The page maps to pixels as the software rasterizer maps it (<see cref="RenderOptions"/>), so
/// both backends' images line up.
/// </para>
/// <para>
/// Constant alpha (§11.6.4.4) goes into the paints' colours and the blend mode (§11.3.5) into the paints: Skia's sixteen blend
/// modes are the functions of Tables 134 and 135. Annotations and form fields (§12.5.5) are placed as the software rasterizer places
/// them (NoZoom and NoRotate, §12.5.3), clipped to their rectangle and, when translucent or blending, drawn in a layer composited
/// with their opacity and blend mode. Transparency groups and soft masks are not drawn yet (#124).
/// </para>
/// <para>
/// Images, stencil masks, glyph runs, shadings and tiling patterns are not drawn by this backend yet: each is skipped, never
/// approximated, and the first one of each kind in a render adds an <see cref="DiagnosticSeverity.Information"/> diagnostic to
/// <see cref="Diagnostics"/> (codes <c>BackendImageUnsupported</c>, <c>BackendImageMaskUnsupported</c>, <c>BackendGlyphsUnsupported</c>,
/// <c>BackendShadingUnsupported</c> and <c>BackendTilingPatternUnsupported</c>). Invisible text (rendering mode 3) paints nothing
/// and is not reported.
/// </para>
/// <para>
/// Reuse one instance for many renders (one per surface): it keeps its paints and caches every Skia path it builds, keyed by the
/// path's geometry, so replaying the same list again (a repaint, a cached display list) allocates nothing per operation. The cache
/// holds at most 2^21 coordinates and 2^16 paths and is emptied when it would grow past them; <see cref="Dispose"/> releases it. An
/// instance renders one page at a time; use one per thread to render in parallel (a document is safe for concurrent renders while
/// nobody changes it).
/// </para>
/// </remarks>
public sealed partial class SkiaBackend : IDisposable
{
    private static readonly RenderOptions DefaultOptions = new();
    private static readonly Content.ContentOptions DefaultContentOptions = new();

    private readonly Replayer _replayer = new();
    private readonly ReadOnlyCollection<Diagnostic> _diagnostics;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="SkiaBackend"/> class.</summary>
    public SkiaBackend() => _diagnostics = _replayer.Diagnostics.AsReadOnly();

    /// <summary>
    /// Gets what the last render could not draw: one <see cref="DiagnosticSeverity.Information"/> diagnostic per kind of operation it
    /// skipped. The list is cleared when the next render starts.
    /// </summary>
    /// <remarks>ADR 0005: nothing is skipped silently.</remarks>
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    /// <summary>Renders <paramref name="page"/> at 72 DPI into a new bitmap (premultiplied BGRA), on white.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The bitmap; the caller disposes it.</returns>
    /// <remarks>ISO 32000-2 §8.3.2.3, §7.7.3.3 Table 31 and §14.11.2 for the page mapping (see <see cref="RenderOptions"/>).</remarks>
    public SKBitmap Render(PdfPage page) => Render(page, DefaultOptions);

    /// <summary>Renders <paramref name="page"/> into a new bitmap (premultiplied BGRA) of <see cref="RenderOptions.GetPixelSize"/> pixels, on white.</summary>
    /// <param name="page">The page.</param>
    /// <param name="options">The scale and rotation.</param>
    /// <returns>The bitmap; the caller disposes it.</returns>
    /// <exception cref="ArgumentException">The page needs more pixels than a bitmap can hold.</exception>
    /// <remarks>ISO 32000-2 §8.3.2.3, §7.7.3.3 Table 31 and §14.11.2 for the page mapping (see <see cref="RenderOptions"/>).</remarks>
    public SKBitmap Render(PdfPage page, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed, this);
        PixelSize size = options.GetPixelSize(page);
        var bitmap = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        try
        {
            if (bitmap.GetPixels() == IntPtr.Zero)
            {
                throw new ArgumentException($"Skia could not allocate a bitmap of {size.Width} x {size.Height} pixels for the page.", nameof(page));
            }

            using var canvas = new SKCanvas(bitmap);
            Render(page, canvas, options);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }

        return bitmap;
    }

    /// <summary>
    /// Renders <paramref name="page"/> onto <paramref name="canvas"/>: on white, into the area of <see cref="RenderOptions.GetPixelSize"/>
    /// pixels at the canvas's origin, under the canvas's current transform and within its current clip. The canvas's state is the same
    /// afterwards.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="canvas">The canvas.</param>
    /// <param name="options">The scale and rotation.</param>
    /// <remarks>ISO 32000-2 §8.3.2.3, §7.7.3.3 Table 31 and §14.11.2 for the page mapping; §8.5.4: the initial clip is the page area.</remarks>
    public void Render(PdfPage page, SKCanvas canvas, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed, this);
        PixelSize size = options.GetPixelSize(page);
        Matrix device = options.GetDeviceTransform(page);
        Draw(page.CreateDisplayList(options.Layers, options.Intent, DefaultContentOptions), canvas, device, new SKRect(0, 0, size.Width, size.Height));
    }

    /// <summary>
    /// Draws <paramref name="list"/> onto <paramref name="canvas"/> over what it already shows, without a background, under the canvas's
    /// current transform and within its current clip. The canvas's state is the same afterwards.
    /// </summary>
    /// <param name="list">The display list.</param>
    /// <param name="canvas">The canvas.</param>
    /// <param name="deviceTransform">
    /// The transform from the list's default user space to the canvas's coordinates (origin top-left, y down), such as
    /// <see cref="RenderOptions.GetDeviceTransform"/>; a tile renderer adds its own offset.
    /// </param>
    /// <remarks>ADR 0006; ISO 32000-2 §8.3.2.2 (device space).</remarks>
    public void Render(DisplayList list, SKCanvas canvas, in Matrix deviceTransform)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(canvas);
        ObjectDisposedException.ThrowIf(_disposed, this);
        Draw(list, canvas, deviceTransform, null);
    }

    /// <summary>Releases the backend's cached Skia paths and its paints; it renders no more after this.</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _replayer.Dispose();
        }
    }

    private void Draw(DisplayList list, SKCanvas canvas, in Matrix device, SKRect? page)
    {
        int saveCount = canvas.Save();
        try
        {
            if (page is SKRect area)
            {
                canvas.ClipRect(area, SKClipOperation.Intersect, antialias: false);
                canvas.DrawColor(SKColors.White, SKBlendMode.Src);
            }

            _replayer.Begin(list.Document, canvas, device);
            try
            {
                list.Replay(_replayer);
            }
            finally
            {
                _replayer.End();
            }
        }
        finally
        {
            canvas.RestoreToCount(saveCount);
        }
    }
}
