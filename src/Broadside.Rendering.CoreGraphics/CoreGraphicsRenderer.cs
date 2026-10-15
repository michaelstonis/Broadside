using Broadside.Diagnostics;
using Broadside.Graphics;
using CoreGraphics;

namespace Broadside.Rendering.CoreGraphics;

/// <summary>
/// The CoreGraphics backend: renders a page or a <see cref="DisplayList"/> onto a Quartz 2D graphics context (<see cref="CGContext"/>)
/// on macOS, iOS and Mac Catalyst, natively anti-aliased.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0006: it consumes display lists only, replaying them through its own <see cref="IRenderingBackend"/>. Fills, strokes and clips
/// become CoreGraphics paths: fills and clips by the non-zero winding or even-odd rule (ISO 32000-2 §8.5.3.3, §8.5.4), open
/// subpaths closed, a trailing lone moveto ignored and a degenerate subpath given the device pixel under it (§8.5.3.3.1); strokes in
/// the stroke's user space with its line width, caps, joins, miter limit and dash pattern (§8.4.3, §8.5.3.2), so a non-uniform
/// transform gives an elliptical pen; a zero width (and, with stroke adjustment, a width under half a pixel) strokes one device unit
/// wide (§8.4.3.2, §10.7.5). Colours convert to RGB through
/// <see cref="PdfDocument.GetColorConverter(PdfColorSpace, ColorConversion, PdfDefaultColorSpaces?)"/>, so the engine's colour
/// management (<see cref="IColorManagement"/>) decides them (§8.6, §10), and are painted as sRGB.
/// </para>
/// <para>
/// The page mapping is the software rasterizer's (<see cref="RenderOptions.GetDeviceTransform"/>, <see cref="RenderOptions.GetPixelSize"/>),
/// so both backends put every page on the same pixels. Images, shadings and stencil masks are not drawn by this backend yet (issue
/// #126).
/// </para>
/// <para>
/// Text (§9.2.4, §9.3.6, §9.4.3): glyphs of TrueType and OpenType programs (§9.9, embedded or substituted) are filled by CoreGraphics'
/// font engine from the program's bytes (<see cref="Fonts.FontProgram.TryGetSfntData"/>), loaded once per program per renderer; glyphs of every
/// other program (Type 1, CFF), of a program CoreGraphics cannot load as the engine reads it, and every glyph when
/// <see cref="RenderOptions.ForceOutlines"/> is set are filled from the outlines the display list carries. Stroked text (modes 1, 2, 5
/// and 6) is always stroked from the outlines, with the stroke rules of paths; text clips (modes 4 to 7) arrive as clip paths.
/// </para>
/// <para>
/// Constant alpha (§11.6.4.4) is the paint colour's alpha and the blend mode (§11.3.5) the context's; CoreGraphics has all sixteen
/// standard modes, but its ColorDodge, ColorBurn and SoftLight are other functions, reported in <see cref="Diagnostics"/>.
/// Annotations and form fields (§12.5.5) are placed as the software rasterizer places them (NoZoom and NoRotate, §12.5.3), clipped
/// to their rectangle and, when translucent or blending, drawn in a transparency layer composited with their opacity and blend
/// mode. Transparency groups and soft masks are not drawn yet (#128).
/// </para>
/// <para>
/// Reuse one instance for many renders: its working buffers are kept, so a warm replay allocates nothing per path, segment or
/// dash. An instance renders one page at a time; use one per thread to render in parallel (a document is safe for concurrent
/// renders while nobody changes it), each onto its own context.
/// </para>
/// </remarks>
public sealed class CoreGraphicsRenderer : IDisposable
{
    private readonly CoreGraphicsBackend _backend = new();
    private bool _disposed;

    /// <summary>Initializes a renderer; reuse it for many renders.</summary>
    public CoreGraphicsRenderer()
    {
    }

    /// <summary>
    /// Renders <paramref name="page"/> into the top-left <see cref="RenderOptions.GetPixelSize"/> pixels of a bitmap context, on white;
    /// pixels outside that area are left as they are.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="target">
    /// The bitmap context, at least the page's pixel size, whose user space is still its pixels with the origin at the bottom left (as
    /// a new <see cref="CGBitmapContext"/> has it). Its graphics state is as it was when the call returns.
    /// </param>
    /// <param name="options">The scale and rotation.</param>
    /// <exception cref="ArgumentException"><paramref name="target"/> is smaller than the page's pixel size.</exception>
    /// <remarks>ISO 32000-2 §8.3.2.3, §7.7.3.3 Table 31 and §14.11.2 for the page mapping (see <see cref="RenderOptions"/>).</remarks>
    public void Render(PdfPage page, CGBitmapContext target, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed, this);
        PixelSize size = options.GetPixelSize(page);
        long width = (long)target.Width;
        long height = (long)target.Height;
        if (width < size.Width || height < size.Height)
        {
            throw new ArgumentException($"The page needs {size.Width} x {size.Height} pixels; the bitmap context has {width} x {height}.", nameof(target));
        }

        Matrix device = options.GetDeviceTransform(page);
        DisplayList list = page.CreateDisplayList(options.Layers, options.Intent, DefaultContentOptions);
        target.SaveState();
        try
        {
            // The rasterizer's device space has its origin at the top left with y down; a bitmap context's has it at the bottom left.
            target.ConcatCTM(new CGAffineTransform(1, 0, 0, -1, 0, height));
            var area = new CGRect(0, 0, size.Width, size.Height);
            target.ClipToRect(area);
            target.SetFillColor(White);
            target.FillRect(area);
            _backend.ForceOutlines = options.ForceOutlines;
            _backend.Replay(list, target, device);
        }
        finally
        {
            target.RestoreState();
        }
    }

    /// <summary>Draws <paramref name="list"/> over what <paramref name="context"/> already holds, without a background.</summary>
    /// <param name="list">The display list.</param>
    /// <param name="context">The context; drawing is clipped to its current clip. Its graphics state is as it was when the call returns.</param>
    /// <param name="deviceTransform">
    /// The transform from the list's default user space to the context's current user space, such as
    /// <see cref="RenderOptions.GetDeviceTransform"/> for a context whose user space is pixels with the origin at the top left and y
    /// down (a flipped view's); a tile renderer adds its own offset.
    /// </param>
    /// <remarks>ADR 0006; ISO 32000-2 §8.3.2.2 (device space).</remarks>
    public void Render(DisplayList list, CGContext context, in Matrix deviceTransform)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(context);
        Render(list, context, deviceTransform, forceOutlines: false);
    }

    /// <summary>
    /// Draws <paramref name="list"/> over what <paramref name="context"/> already holds, without a background, drawing every glyph from
    /// its outline when <paramref name="forceOutlines"/> is set.
    /// </summary>
    /// <param name="list">The display list.</param>
    /// <param name="context">The context; drawing is clipped to its current clip. Its graphics state is as it was when the call returns.</param>
    /// <param name="deviceTransform">
    /// The transform from the list's default user space to the context's current user space, such as
    /// <see cref="RenderOptions.GetDeviceTransform"/>; a tile renderer adds its own offset.
    /// </param>
    /// <param name="forceOutlines">Whether every glyph is filled from its outline (<see cref="RenderOptions.ForceOutlines"/>).</param>
    /// <remarks>ADR 0006; ISO 32000-2 §8.3.2.2 (device space), §9.2.4 (glyph outlines).</remarks>
    public void Render(DisplayList list, CGContext context, in Matrix deviceTransform, bool forceOutlines)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _backend.ForceOutlines = forceOutlines;
        _backend.Replay(list, context, deviceTransform);
    }

    /// <summary>Gets what the last render could not draw exactly as the file asks: one diagnostic per kind.</summary>
    /// <remarks>ADR 0005: nothing is skipped or approximated silently. CoreGraphics' ColorDodge, ColorBurn and SoftLight are not ISO 32000-2 §11.3.5's; tiling patterns (§8.7.3) are not drawn yet.</remarks>
    public IReadOnlyList<Diagnostic> Diagnostics => _backend.Diagnostics;

    /// <summary>Ends the renderer's use; it renders no more after this.</summary>
    public void Dispose()
    {
        _disposed = true;
        _backend.ClearFonts();
    }

    private static readonly CGColor White = new(1, 1, 1, 1);
    private static readonly Content.ContentOptions DefaultContentOptions = new();
}
