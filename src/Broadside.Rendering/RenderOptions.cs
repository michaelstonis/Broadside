using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// How a page maps to pixels when it is rendered: the scale (or resolution) and an extra rotation. The page's own crop box,
/// <c>Rotate</c> and <c>UserUnit</c> always apply.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.3.2.3 (default user space, one unit = <c>UserUnit</c>/72 inch), §7.7.3.3 Table 31 (<c>Rotate</c>,
/// <c>UserUnit</c>) and §14.11.2 (the crop box is the region displayed). <see cref="GetDeviceTransform"/> maps the crop box onto a
/// bitmap of <see cref="GetPixelSize"/> pixels whose origin is the top-left corner and whose y axis points down; any backend can
/// use the same mapping.
/// </para>
/// <para>Mutable and fluent like the other options objects: set properties or chain the <c>With*</c> methods. Not safe to change while a render reads it.</para>
/// </remarks>
public sealed class RenderOptions
{
    /// <summary>The resolution of one pixel per point: 72 dots per inch, scale 1.</summary>
    public const double DefaultDpi = 72;

    private double _scale = 1;
    private int _rotation;

    /// <summary>Gets or sets device pixels per unit of default user space (before <c>UserUnit</c>): 1 is 72 DPI, 2 is 144 DPI.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a finite number greater than zero.</exception>
    public double Scale
    {
        get => _scale;
        set
        {
            if (!double.IsFinite(value) || value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The scale must be a finite number greater than zero.");
            }

            _scale = value;
        }
    }

    /// <summary>Gets or sets the resolution in dots per inch, another way of setting <see cref="Scale"/> (<see cref="Scale"/> × 72).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a finite number greater than zero.</exception>
    public double Dpi
    {
        get => _scale * DefaultDpi;
        set
        {
            if (!double.IsFinite(value) || value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The resolution must be a finite number greater than zero.");
            }

            _scale = value / DefaultDpi;
        }
    }

    /// <summary>
    /// Gets or sets a rotation in degrees, clockwise, added to the page's own <c>Rotate</c>: a multiple of 90, stored as 0, 90, 180
    /// or 270.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a multiple of 90.</exception>
    public int Rotation
    {
        get => _rotation;
        set
        {
            if (value % 90 != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The rotation must be a multiple of 90 degrees.");
            }

            _rotation = ((value % 360) + 360) % 360;
        }
    }

    /// <summary>Sets <see cref="Scale"/>.</summary>
    /// <param name="scale">Device pixels per unit of default user space.</param>
    /// <returns>This instance.</returns>
    public RenderOptions WithScale(double scale)
    {
        Scale = scale;
        return this;
    }

    /// <summary>Sets <see cref="Dpi"/>.</summary>
    /// <param name="dpi">The resolution in dots per inch.</param>
    /// <returns>This instance.</returns>
    public RenderOptions WithDpi(double dpi)
    {
        Dpi = dpi;
        return this;
    }

    /// <summary>Sets <see cref="Rotation"/>.</summary>
    /// <param name="degrees">A multiple of 90, clockwise.</param>
    /// <returns>This instance.</returns>
    public RenderOptions WithRotation(int degrees)
    {
        Rotation = degrees;
        return this;
    }

    /// <summary>
    /// Gets or sets a value indicating whether every glyph is drawn from the outline the display list carries rather than by the
    /// backend's own font rendering; <see langword="false"/> by default.
    /// </summary>
    /// <remarks>
    /// ADR 0006: a display list's glyph runs carry the font program and glyph ids, so a native backend (SkiaSharp, CoreGraphics,
    /// Direct2D, Android) may draw glyphs with its platform's font engine, whose hinting and anti-aliasing differ from the outlines'.
    /// With this set, such a backend fills and strokes <see cref="GlyphRun.GetOutline"/> like any path, so its output matches the
    /// software rasterizer's within the golden-image threshold. The software rasterizer always draws outlines (ISO 32000-2 §9.2.4).
    /// </remarks>
    public bool ForceOutlines { get; set; }

    /// <summary>Sets <see cref="ForceOutlines"/>.</summary>
    /// <param name="forceOutlines">Whether glyphs are always drawn from their outlines.</param>
    /// <returns>This instance.</returns>
    public RenderOptions WithForceOutlines(bool forceOutlines)
    {
        ForceOutlines = forceOutlines;
        return this;
    }

    /// <summary>Gets or sets the token that cancels a render of a page: its display-list build and its drawing.</summary>
    /// <remarks>
    /// Viewer capability 2 (issue #97). The build checks it as <see cref="Content.ContentOptions.CancellationToken"/> does (every
    /// 1,024 operators, every nested stream, and inside the decoding of content and images); the drawing before every operation of
    /// the display list. A cancelled render throws <see cref="OperationCanceledException"/>; the bitmap keeps what was drawn, and
    /// nothing is left behind that changes a later render.
    /// </remarks>
    public CancellationToken CancellationToken { get; set; }

    /// <summary>Sets <see cref="CancellationToken"/>.</summary>
    /// <param name="cancellationToken">The token.</param>
    /// <returns>This instance.</returns>
    public RenderOptions WithCancellation(CancellationToken cancellationToken)
    {
        CancellationToken = cancellationToken;
        return this;
    }

    /// <summary>
    /// Gets or sets what receives a page render's progress, from 0 to 1; <see langword="null"/>, the default, for none.
    /// </summary>
    /// <remarks>
    /// Viewer capability 2 (issue #97). Reported synchronously on the rendering thread: building the display list moves it from 0
    /// to 0.5 (<see cref="Content.ContentOptions.Progress"/>, halved), drawing from 0.5 to 1 (the share of the list's operations
    /// drawn, every 1,024 operations and at the end of every slice of a <see cref="ProgressiveRender"/>). Values never decrease, and
    /// the last report of a render that completes is exactly 1, reported once. The receiver may cancel
    /// <see cref="CancellationToken"/>'s source.
    /// </remarks>
    public IProgress<double>? Progress { get; set; }

    /// <summary>Sets <see cref="Progress"/>.</summary>
    /// <param name="progress">What receives the render's progress, or <see langword="null"/>.</param>
    /// <returns>This instance.</returns>
    public RenderOptions WithProgress(IProgress<double>? progress)
    {
        Progress = progress;
        return this;
    }

    /// <summary>Returns the size in pixels of <paramref name="page"/> rendered with these options.</summary>
    /// <param name="page">The page.</param>
    /// <returns>
    /// The crop box's width and height times <c>UserUnit</c> and <see cref="Scale"/>, each rounded up to whole pixels (at least 1),
    /// swapped when the total rotation is 90 or 270 degrees.
    /// </returns>
    /// <exception cref="ArgumentException">The page would need more pixels than a bitmap can hold.</exception>
    /// <remarks>
    /// ISO 32000-2 §14.11.2 and §7.7.3.3 Table 31. Rounding up keeps every pixel the crop box touches (the spirit of §10.7.4, which
    /// paints every pixel a shape intersects).
    /// </remarks>
    public PixelSize GetPixelSize(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        PageGeometry geometry = Measure(page);
        return new PixelSize(geometry.PixelWidth, geometry.PixelHeight);
    }

    /// <summary>
    /// Returns the transform from <paramref name="page"/>'s default user space to the pixels of a bitmap of
    /// <see cref="GetPixelSize"/>: origin at the top-left corner of the rotated crop box, y down, one unit = one pixel.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>The device transform; a display list's own transforms are applied before it.</returns>
    /// <remarks>
    /// ISO 32000-2 §8.3.2.2 (device space), §8.3.2.3 (default user space and <c>UserUnit</c>), §7.7.3.3 Table 31 (<c>Rotate</c>: the
    /// page is shown turned clockwise) and §14.11.2 (crop box).
    /// </remarks>
    public Matrix GetDeviceTransform(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return Measure(page).Transform;
    }

    /// <summary>Computes the bitmap size and the transform together.</summary>
    internal PageGeometry Measure(PdfPage page)
    {
        PdfRectangle crop = page.CropBox;
        double k = page.UserUnit * _scale;
        double width = crop.Width * k;
        double height = crop.Height * k;
        int rotation = (page.Rotation + _rotation) % 360;
        (double left, double bottom, double right, double top) = (crop.Left * k, crop.Bottom * k, crop.Right * k, crop.Top * k);
        Matrix transform = rotation switch
        {
            // X = (x - L)k, Y = (T - y)k; each rotation turns that image clockwise inside its own bounds.
            90 => new Matrix(0, k, k, 0, -bottom, -left),
            180 => new Matrix(-k, 0, 0, k, right, -bottom),
            270 => new Matrix(0, -k, -k, 0, top, right),
            _ => new Matrix(k, 0, 0, -k, -left, top),
        };
        int pixelWidth = Pixels(width, page);
        int pixelHeight = Pixels(height, page);
        if (rotation is 90 or 270)
        {
            (pixelWidth, pixelHeight) = (pixelHeight, pixelWidth);
        }

        if ((long)pixelWidth * pixelHeight * 4 > Array.MaxLength)
        {
            throw new ArgumentException($"The page would render to {pixelWidth} x {pixelHeight} pixels, more than a bitmap can hold.", nameof(page));
        }

        return new PageGeometry(transform, pixelWidth, pixelHeight);
    }

    private static int Pixels(double extent, PdfPage page)
    {
        // A tolerance keeps 1275.0000000001 at 1275 pixels rather than 1276.
        double pixels = Math.Ceiling(extent - 1e-6);
        if (pixels > int.MaxValue / 4)
        {
            throw new ArgumentException($"The page would render to {extent} pixels across, more than a bitmap can hold.", nameof(page));
        }

        return Math.Max(1, (int)pixels);
    }
}
