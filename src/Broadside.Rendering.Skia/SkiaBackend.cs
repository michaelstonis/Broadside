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
/// with their opacity and blend mode.
/// </para>
/// <para>
/// Transparency groups (§11.4, §11.6.6) Skia can composite are layers: isolated groups, and non-isolated ones whose content does
/// not blend, are transparent layers composited with the group's opacity, blend mode and soft mask; a non-isolated group of
/// blending content at an opacity below 1 is a layer started from the backdrop and interpolated towards it (§11.4.8); the
/// implicit knockout group of a fill and stroke (§11.7.4.4) is a layer its paints replace into. Other knockout groups, and groups
/// that blend in a gray or CMYK group colour space, are drawn by <see cref="SoftwareRasterizer"/> into an image composited in their
/// place and reported once per render (<c>BackendKnockoutGroupRasterized</c>, <c>BackendGroupColorSpaceRasterized</c>, both
/// <see cref="DiagnosticSeverity.Information"/>); a non-isolated group of blending content composited with a blend mode or a soft
/// mask is drawn that way against a transparent backdrop, an approximation reported as the Warning
/// <c>BackendNonIsolatedGroupApproximated</c>. Soft masks (§11.5, §11.6.5.1): their values are rendered by the software
/// rasterizer once per mask, placement and clip, kept across renders (<c>BackendSoftMaskRasterized</c>), and applied to each masked
/// paint or group result through a layer and <see cref="SKBlendMode.DstIn"/>.
/// </para>
/// <para>
/// GPU: the canvas may be a GPU surface's (<c>SKSurface.Create(GRContext, ...)</c>, Metal, OpenGL, Vulkan or Direct3D); every
/// image the backend makes (images, mask values, rasterized groups) is a raster image Skia uploads, so the same calls draw on both.
/// </para>
/// <para>
/// Text (§9.3.6, §9.4.3): glyphs of TrueType and OpenType programs (§9.9) are filled by Skia's font engine from a typeface loaded
/// from the program's bytes (<see cref="Fonts.FontProgram.TryGetSfntData"/>), created once per program per backend, unhinted and
/// positioned exactly; every other program (Type 1, bare CFF), every glyph whose Skia outline does not have the bounds of the
/// program's, and every glyph when <see cref="RenderOptions.ForceOutlines"/> is set are filled from their outlines (§9.2.4) with
/// the non-zero winding rule. A program Skia cannot load, or a glyph it reads differently, adds the
/// <see cref="DiagnosticSeverity.Information"/> diagnostic <c>BackendNativeGlyphsUnavailable</c>, once per render. Strokes of text (modes 1, 2, 5,
/// 6) follow the outlines like any stroke; Type 3 glyphs arrive as the paths their descriptions paint. Invisible text (mode 3)
/// paints nothing.
/// </para>
/// <para>
/// Images (§8.9.4, §8.9.5) are converted once per interned display-list image to a Skia image (colour management as for paths;
/// Decode, Indexed, colour key, Matte; a mask of the image's size folded into its alpha, one of another size drawn at its own
/// resolution) and resampled to the device as the software rasterizer resamples them: a box filter where a pixel spans more than a
/// sample, square samples or a tent (Interpolate, §8.9.5.3) where it does not, for the first four transforms each image is drawn at;
/// further transforms, and enlargements past 256 pixels per sample, are sampled by Skia (mipmaps when reduced). Stencil masks
/// (§8.9.6.2) are alpha-only images painted in the fill colour. Converted pixels are kept within the document's
/// <see cref="PdfOptions.ImageCacheBudget"/>; a budget of zero converts on every paint with the same output. Axial and radial
/// shadings (§8.7.4.5.3, §8.7.4.5.4) are native linear and two-point conical gradients sampled from the function; function-based
/// shadings (§8.7.4.5.2) a texture of the function over its domain; triangle and patch meshes (§8.7.4.5.5 to §8.7.4.5.8) vertices,
/// a function's t interpolated through a colour table; shading patterns fill and stroke over their Background (§8.7.4.3). Tiling
/// patterns (§8.7.3) record their cell once per scale into a picture repeated by a picture shader, laid out on the device as the
/// rasterizer lays them out. What cannot be drawn is reported once per render: <c>BackendImageTooLarge</c> (an image too large
/// to convert in memory), <c>BackendShadingUnsupported</c> (an unknown shading type) and <c>BackendTilingPatternApproximated</c>
/// (cells nested more than eight deep, or overlapping their neighbours more than 256 times per tile).
/// </para>
/// <para>
/// Reuse one instance for many renders (one per surface): it keeps its paints and caches every Skia path it builds, keyed by the
/// path's geometry, so replaying the same list again (a repaint, a cached display list) allocates nothing per operation. The cache
/// holds at most 2^21 coordinates and 2^16 paths and is emptied when it would grow past them; typefaces are kept per font program
/// (at most 256, held weakly, so a closed document's go with it) with at most 2^16 glyphs among them; images, shadings (at most 256,
/// 64 MiB) and pattern cells are kept per display-list instance, held weakly; <see cref="Dispose"/> releases them all. An
/// instance renders one page at a time; use one per thread to render in parallel (a document is safe for concurrent renders while
/// nobody changes it).
/// </para>
/// </remarks>
public sealed partial class SkiaBackend : IDisposable
{
    /// <summary>How far, in device pixels, a paint can reach beyond its outline: anti-aliasing, the thinnest line, stroke adjustment.</summary>
    private const int RegionMargin = 2;

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
        Draw(page.CreateDisplayList(options.Layers, options.Intent, DefaultContentOptions), canvas, device, new SKRect(0, 0, size.Width, size.Height), options.ForceOutlines);
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
    /// <remarks>ADR 0006; ISO 32000-2 §8.3.2.2 (device space). Glyphs of TrueType and OpenType programs are drawn natively.</remarks>
    public void Render(DisplayList list, SKCanvas canvas, in Matrix deviceTransform) => Render(list, canvas, deviceTransform, forceOutlines: false);

    /// <summary>
    /// Draws <paramref name="list"/> onto <paramref name="canvas"/> over what it already shows, without a background, under the canvas's
    /// current transform and within its current clip, drawing every glyph from its outline when <paramref name="forceOutlines"/> is set.
    /// The canvas's state is the same afterwards.
    /// </summary>
    /// <param name="list">The display list.</param>
    /// <param name="canvas">The canvas.</param>
    /// <param name="deviceTransform">
    /// The transform from the list's default user space to the canvas's coordinates (origin top-left, y down), such as
    /// <see cref="RenderOptions.GetDeviceTransform"/>; a tile renderer adds its own offset.
    /// </param>
    /// <param name="forceOutlines">Whether to draw every glyph from its outline, never through Skia's font engine (<see cref="RenderOptions.ForceOutlines"/>).</param>
    /// <remarks>
    /// ADR 0006; ISO 32000-2 §8.3.2.2 (device space), §9.2.4 (glyph outlines). Spec #33 story 14 (viewer capability 1): only the
    /// paints whose bounds reach the canvas's clip are drawn (<see cref="DisplayList.Replay(IRenderingBackend, PdfRectangle)"/>), so
    /// a tile costs what its own content costs, as with the software rasterizer. The list computes the bounds on its first such render.
    /// </remarks>
    public void Render(DisplayList list, SKCanvas canvas, in Matrix deviceTransform, bool forceOutlines)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(canvas);
        ObjectDisposedException.ThrowIf(_disposed, this);
        Draw(list, canvas, deviceTransform, null, forceOutlines, cull: true);
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

    /// <summary>Gets how many typefaces this backend has created from font programs (one per TrueType or OpenType program it drew natively).</summary>
    internal int TypefacesCreated => _replayer.TypefacesCreated;

    /// <summary>Gets how many Skia images this backend has made from display-list images and masks (one per interned instance while kept).</summary>
    internal int ImagesCreated => _replayer.ImagesCreated;

    /// <summary>Gets the bytes of converted image pixels this backend keeps, within the document's image cache budget.</summary>
    internal long CachedImageBytes => _replayer.CachedImageBytes;

    /// <summary>Gets how many tiling-pattern cells this backend has recorded into pictures.</summary>
    internal int CellsRecorded => _replayer.CellsRecorded;

    /// <summary>
    /// Returns the part of default user space that can change the pixels inside <paramref name="canvas"/>'s clip under
    /// <paramref name="device"/>: the clip's device bounds widened by <see cref="RegionMargin"/> pixels, mapped back. False when the
    /// canvas has a perspective transform or a transform has no inverse.
    /// </summary>
    private static bool TryGetRegion(SKCanvas canvas, in Matrix device, out PdfRectangle region)
    {
        region = default;
        SKMatrix total = canvas.TotalMatrix;
        if (total.Persp0 != 0 || total.Persp1 != 0 || total.Persp2 != 1 || !total.TryInvert(out SKMatrix toCanvas) || !device.TryInvert(out Matrix toPage))
        {
            return false;
        }

        SKRectI clip = canvas.DeviceClipBounds;
        SKRect local = toCanvas.MapRect(new SKRect(clip.Left - RegionMargin, clip.Top - RegionMargin, clip.Right + RegionMargin, clip.Bottom + RegionMargin));
        PathPoint a = toPage.Transform(local.Left, local.Top);
        PathPoint b = toPage.Transform(local.Right, local.Top);
        PathPoint c = toPage.Transform(local.Right, local.Bottom);
        PathPoint d = toPage.Transform(local.Left, local.Bottom);
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

    private void Draw(DisplayList list, SKCanvas canvas, in Matrix device, SKRect? page, bool forceOutlines, bool cull = false)
    {
        int saveCount = canvas.Save();
        try
        {
            if (page is SKRect area)
            {
                canvas.ClipRect(area, SKClipOperation.Intersect, antialias: false);
                canvas.DrawColor(SKColors.White, SKBlendMode.Src);
            }

            _replayer.ForceOutlines = forceOutlines;
            _replayer.Begin(list.Document, canvas, device);
            try
            {
                if (cull && TryGetRegion(canvas, device, out PdfRectangle region))
                {
                    list.Replay(_replayer, region);
                }
                else
                {
                    list.Replay(_replayer);
                }
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
