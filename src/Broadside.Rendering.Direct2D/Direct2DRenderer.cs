using Broadside.Diagnostics;
using Broadside.Graphics;
using Windows.Win32.Foundation;

namespace Broadside.Rendering.Direct2D;

/// <summary>
/// Renders pages and display lists through Direct2D into a <see cref="Bitmap"/> of premultiplied BGRA pixels: a native backend for
/// Windows that matches the <see cref="SoftwareRasterizer"/> within the golden-image thresholds.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0006: it consumes display lists only, replaying them onto a Direct2D device context over a Direct3D 11 device (the graphics
/// hardware or WARP, <see cref="Direct2DDriver"/>). Fills use the non-zero winding or even-odd rule with every subpath closed
/// (ISO 32000-2 §8.5.3.3), strokes follow the line parameters with the elliptical pen of a non-uniform transform, dashes, the
/// thinnest line and stroke adjustment (§8.5.3.2, §8.4.3, §10.7.5), and clips intersect anti-aliased, saved and restored with the
/// graphics state (§8.5.4). Colours convert to RGB through
/// <see cref="PdfDocument.GetColorConverter(PdfColorSpace, ColorConversion, PdfDefaultColorSpaces?)"/>, so the engine's colour
/// management decides them (§8.6, §10). Pages map to pixels as <see cref="RenderOptions"/> says (§8.3.2.3, §7.7.3.3 Table 31,
/// §14.11.2), the same mapping the software rasterizer uses.
/// </para>
/// <para>
/// Text (ISO 32000-2 §9.4.3, §9.3.6): glyphs of TrueType and OpenType programs are filled by DirectWrite from a font face loaded from
/// the program's bytes (§9.9), created once per program and renderer; glyphs of every other program, and every glyph when
/// <see cref="RenderOptions.ForceOutlines"/> is set, are filled from the outlines the display list carries by the non-zero winding
/// rule (§9.2.4). Stroked text is stroked from the outlines like a path. A TrueType or OpenType program DirectWrite cannot read as
/// Broadside does draws from its outlines and is reported (<c>BackendNativeGlyphsUnavailable</c>, information).
/// </para>
/// <para>
/// Images, stencil masks, shadings and tiling patterns are not drawn by this backend yet; a render that meets them leaves them out and
/// reports one <see cref="Diagnostics"/> entry per kind rather than approximating them.
/// </para>
/// <para>
/// Constant alpha (ISO 32000-2 §11.6.4.4) is the brush colour's alpha. Direct2D's primitive blend is source-over only, so blend modes
/// other than Normal (§11.3.5) composite as Normal and are reported (<c>BackendBlendModeUnsupported</c>). Annotations and form fields
/// (§12.5.5) are placed as the software rasterizer places them (NoZoom and NoRotate, §12.5.3), clipped to their rectangle and, when
/// translucent, drawn in a layer with their opacity.
/// </para>
/// <para>
/// Reuse one instance for many renders: its device, device context and target bitmaps are created once (the targets again when the
/// size changes) and released on <see cref="Dispose"/>, and a warm render allocates nothing managed per operation. An instance renders
/// one page at a time; use one per thread to render in parallel (a document is safe for concurrent renders while nobody changes it).
/// </para>
/// </remarks>
public sealed class Direct2DRenderer : IDisposable
{
    private static readonly RenderOptions DefaultOptions = new();
    private static readonly Content.ContentOptions DefaultContentOptions = new();

    private readonly Direct2DDriver _requested;
    private readonly Direct2DBackend _backend = new();
    private readonly DirectWriteFonts _fonts = new();
    private Direct2DSurface _surface;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="Direct2DRenderer"/> class on the graphics hardware, or WARP when there is none.</summary>
    /// <exception cref="System.Runtime.InteropServices.COMException">Direct2D or Direct3D 11 is not available.</exception>
    public Direct2DRenderer()
        : this(Direct2DDriver.Hardware)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="Direct2DRenderer"/> class on the given driver.</summary>
    /// <param name="driver">The Direct3D device to draw with; <see cref="Direct2DDriver.Hardware"/> falls back to WARP when there is no hardware device.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="driver"/> is not a defined value.</exception>
    /// <exception cref="System.Runtime.InteropServices.COMException">Direct2D or Direct3D 11 is not available.</exception>
    public Direct2DRenderer(Direct2DDriver driver)
    {
        if (driver is not (Direct2DDriver.Hardware or Direct2DDriver.Warp))
        {
            throw new ArgumentOutOfRangeException(nameof(driver), driver, "Not a Direct2D driver.");
        }

        _requested = driver;
        _surface = new Direct2DSurface(driver);
    }

    /// <summary>Gets the driver the renderer draws with: <see cref="Direct2DDriver.Warp"/> when it asked for hardware and found none.</summary>
    public Direct2DDriver Driver => _surface.Driver;

    /// <summary>
    /// Gets the diagnostics of the last render: one warning, code <c>RenderingOperationSkipped</c>, for each kind of display-list
    /// operation this backend does not draw yet and left out, and one information entry, code <c>BackendNativeGlyphsUnavailable</c>,
    /// when glyphs of a TrueType or OpenType program were drawn from their outlines because DirectWrite could not read the program.
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics => _backend.Diagnostics;

    /// <summary>Renders <paramref name="page"/> at 72 DPI into a new pooled bitmap, on white.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The bitmap; dispose it to return its buffer to the pool.</returns>
    /// <exception cref="ArgumentException">The page needs more pixels than a bitmap or the device can hold.</exception>
    /// <remarks>ISO 32000-2 §8.3.2.3, §7.7.3.3 Table 31 and §14.11.2 for the page mapping (see <see cref="RenderOptions"/>).</remarks>
    public Bitmap Render(PdfPage page) => Render(page, DefaultOptions);

    /// <summary>Renders <paramref name="page"/> into a new pooled bitmap of <see cref="RenderOptions.GetPixelSize"/> pixels, on white.</summary>
    /// <param name="page">The page.</param>
    /// <param name="options">The scale and rotation.</param>
    /// <returns>The bitmap; dispose it to return its buffer to the pool.</returns>
    /// <exception cref="ArgumentException">The page needs more pixels than a bitmap or the device can hold.</exception>
    /// <remarks>ISO 32000-2 §8.3.2.3, §7.7.3.3 Table 31 and §14.11.2 for the page mapping (see <see cref="RenderOptions"/>).</remarks>
    public Bitmap Render(PdfPage page, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed, this);
        PixelSize size = options.GetPixelSize(page);
        Matrix device = options.GetDeviceTransform(page);
        DisplayList list = page.CreateDisplayList(options.Layers, options.Intent, DefaultContentOptions);
        var bitmap = new Bitmap(size.Width, size.Height);
        try
        {
            Draw(list, bitmap, device, size.Width, size.Height, white: true, options.ForceOutlines);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }

        return bitmap;
    }

    /// <summary>
    /// Renders <paramref name="page"/> into the top-left <see cref="RenderOptions.GetPixelSize"/> pixels of <paramref name="target"/>,
    /// on white; pixels outside that area are left as they are.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="target">The bitmap, for example one over caller memory; at least the page's pixel size.</param>
    /// <param name="options">The scale and rotation.</param>
    /// <exception cref="ArgumentException"><paramref name="target"/> is smaller than the page's pixel size, or the device cannot hold it.</exception>
    public void Render(PdfPage page, Bitmap target, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed, this);
        PixelSize size = options.GetPixelSize(page);
        if (target.Width < size.Width || target.Height < size.Height)
        {
            throw new ArgumentException($"The page needs {size.Width} x {size.Height} pixels; the bitmap has {target.Width} x {target.Height}.", nameof(target));
        }

        Draw(page.CreateDisplayList(options.Layers, options.Intent, DefaultContentOptions), target, options.GetDeviceTransform(page), size.Width, size.Height, white: true, options.ForceOutlines);
    }

    /// <summary>Draws <paramref name="list"/> over the current pixels of <paramref name="target"/>, without a background.</summary>
    /// <param name="list">The display list.</param>
    /// <param name="target">The bitmap; drawing is clipped to it.</param>
    /// <param name="deviceTransform">
    /// The transform from the list's default user space to the bitmap's pixels (origin top-left, y down), such as
    /// <see cref="RenderOptions.GetDeviceTransform"/>; a tile renderer adds its own offset.
    /// </param>
    /// <exception cref="ArgumentException">The device cannot hold a bitmap of <paramref name="target"/>'s size.</exception>
    /// <remarks>ADR 0006; ISO 32000-2 §8.3.2.2 (device space).</remarks>
    public void Render(DisplayList list, Bitmap target, in Matrix deviceTransform)
    {
        Render(list, target, deviceTransform, forceOutlines: false);
    }

    /// <summary>
    /// Draws <paramref name="list"/> over the current pixels of <paramref name="target"/>, without a background, drawing every glyph
    /// from its outline when <paramref name="forceOutlines"/> is set.
    /// </summary>
    /// <param name="list">The display list.</param>
    /// <param name="target">The bitmap; drawing is clipped to it.</param>
    /// <param name="deviceTransform">The transform from the list's default user space to the bitmap's pixels (origin top-left, y down).</param>
    /// <param name="forceOutlines">Whether to draw every glyph from its outline, never through DirectWrite (<see cref="RenderOptions.ForceOutlines"/>).</param>
    /// <exception cref="ArgumentException">The device cannot hold a bitmap of <paramref name="target"/>'s size.</exception>
    /// <remarks>ADR 0006; ISO 32000-2 §8.3.2.2 (device space), §9.2.4 (glyph outlines).</remarks>
    public void Render(DisplayList list, Bitmap target, in Matrix deviceTransform, bool forceOutlines)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(target);
        ObjectDisposedException.ThrowIf(_disposed, this);
        Draw(list, target, deviceTransform, target.Width, target.Height, white: false, forceOutlines);
    }

    /// <summary>Releases the Direct2D device, its device context and bitmaps, and the DirectWrite font faces; the renderer renders no more after this.</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _backend.Dispose();
            _surface.Dispose();
            _fonts.Dispose();
        }
    }

    /// <summary>Gets how many DirectWrite font faces this renderer has created from font programs.</summary>
    internal int FontFacesCreated => _fonts.FacesCreated;

    /// <summary>Gets how many Direct2D bitmaps were made from display-list images and masks (each once per renderer while cached).</summary>
    internal int ImagesCreated => _backend.ImagesCreated;

    /// <summary>Gets the bytes of converted image pixels the renderer keeps.</summary>
    internal long CachedImageBytes => _backend.CachedImageBytes;

    /// <summary>Gets how many tiling-pattern cells were drawn into tiles.</summary>
    internal int CellsRecorded => _backend.CellsRecorded;

    private void Draw(DisplayList list, Bitmap target, in Matrix device, int width, int height, bool white, bool forceOutlines)
    {
        if (width > _surface.MaximumSize || height > _surface.MaximumSize)
        {
            throw new ArgumentException(
                $"Direct2D draws at most {_surface.MaximumSize} x {_surface.MaximumSize} pixels on this device; the render needs {width} x {height}.",
                nameof(target));
        }

        for (int attempt = 0; ; attempt++)
        {
            _surface.Begin(width, height, target, white);
            HRESULT result;
            try
            {
                _backend.ForceOutlines = forceOutlines;
                _backend.Begin(_surface, _fonts, list.Document, device);
                try
                {
                    list.Replay(_backend);
                }
                finally
                {
                    _backend.End();
                }
            }
            finally
            {
                result = _surface.End();
            }

            if (result == HRESULT.D2DERR_RECREATE_TARGET && attempt == 0)
            {
                // The device was lost (a driver update, a removed adapter): recreate everything once and draw again.
                _backend.ReleaseDeviceResources();
                _surface.Dispose();
                _surface = new Direct2DSurface(_requested);
                continue;
            }

            result.ThrowOnFailure();
            _surface.CopyTo(target, width, height);
            return;
        }
    }
}
