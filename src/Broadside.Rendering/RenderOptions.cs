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

    /// <summary>
    /// Gets or sets the part of the page to render, in default user space (the coordinates of the page's crop box), or
    /// <see langword="null"/> (the default) for the whole page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Spec #33 story 14 (viewer capability 1). With a region, <see cref="GetPixelSize"/> is the size of the pixels the region
    /// covers at <see cref="Scale"/> and <see cref="Rotation"/> (rounded outwards to whole pixels and cut to the page), and
    /// <see cref="GetDeviceTransform"/> maps the page so that those pixels are the bitmap's: each one is the pixel at the same place
    /// in a render of the whole page, which <see cref="GetPixelRectangle"/> locates. Paints entirely outside the region are skipped
    /// (<see cref="DisplayList.Replay(IRenderingBackend, PdfRectangle)"/>), so a small region of a complex page renders quickly at any
    /// scale.
    /// </para>
    /// <para>
    /// ISO 32000-2 §8.3.2.3 (default user space) and §14.11.2 (the crop box is what is shown; a region outside it shows nothing).
    /// </para>
    /// </remarks>
    public PdfRectangle? Region { get; set; }

    /// <summary>Sets <see cref="Region"/>.</summary>
    /// <param name="region">The part of the page to render in default user space, or <see langword="null"/> for all of it.</param>
    /// <returns>This instance.</returns>
    public RenderOptions WithRegion(PdfRectangle? region)
    {
        Region = region;
        return this;
    }

    /// <summary>
    /// Returns where a render of <paramref name="page"/> with these options lies within a render of the whole page at the same scale
    /// and rotation: the whole page's pixels without a <see cref="Region"/>, else the pixels the region covers.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>The pixel rectangle, its size equal to <see cref="GetPixelSize"/>.</returns>
    /// <exception cref="ArgumentException">The region does not overlap the page, or the render would need more pixels than a bitmap can hold.</exception>
    /// <remarks>ISO 32000-2 §8.3.2.2 (device space; pixel boundaries on integer coordinates, §10.7.4).</remarks>
    public PixelRectangle GetPixelRectangle(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        PageGeometry geometry = Measure(page);
        return new PixelRectangle(geometry.OffsetX, geometry.OffsetY, geometry.PixelWidth, geometry.PixelHeight);
    }

    /// <summary>
    /// Gets or sets the layers a page render draws: the content, the annotations and the form fields, in that order;
    /// <see cref="DisplayLayers.All"/> by default.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §12.5 and §12.7, through <see cref="PdfPage.CreateDisplayList(DisplayLayers, DisplayIntent, Content.ContentOptions)"/>.
    /// A viewer that redraws overlays without re-rasterizing the page builds each layer's list itself and replays them with
    /// <see cref="SoftwareRasterizer.Render(DisplayList, Bitmap, in Matrix)"/>.
    /// </remarks>
    public DisplayLayers Layers { get; set; } = DisplayLayers.All;

    /// <summary>Gets or sets what the render is for, which decides the annotations drawn; <see cref="DisplayIntent.View"/> by default.</summary>
    /// <remarks>ISO 32000-2 §12.5.3, Table 167 (<c>NoView</c>, <c>Print</c>, <c>NoZoom</c>, <c>NoRotate</c>).</remarks>
    public DisplayIntent Intent { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the page is rendered with overprint preview, as a print-preview viewer shows it;
    /// <see langword="false"/> by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §8.6.7 and §11.7.4. Off, the rasterizer is a composite RGB device that does not overprint, and §8.6.7 says such a
    /// device ignores the overprint parameters: every paint knocks out what is below it. On, the page content is painted into a
    /// buffer of device colourants, the four process colourants (cyan, magenta, yellow, black) and one more per spot colourant a
    /// Separation or DeviceN colour names, and <c>OP</c>, <c>op</c> and <c>OPM</c> decide which colourants a paint replaces
    /// (§11.7.4.5 Table 146): with overprint off, every colourant the paint does not specify is erased; with it on, they are left
    /// as they are, and with overprint mode 1 a DeviceCMYK component of 0 leaves its colourant unchanged too. At the end each
    /// pixel's colourants are converted for display: every spot adds its tint times its process equivalent at tint 1, clipped at 1,
    /// and the result converts as DeviceCMYK through the document's colour management (the output intent's profile, when it
    /// has one and the engine uses output intents). Every colour goes through the device colourants, so RGB, gray and CIE-based
    /// colours show as their CMYK conversion prints. Annotations and form fields are drawn over the result as usual.
    /// </para>
    /// <para>
    /// Deviations: images and shadings in a Separation or DeviceN space are painted as their process equivalent; a tiling pattern's
    /// cell is rendered in RGB and painted as its DeviceCMYK conversion; non-separable blend modes, and on spot colourants blend modes
    /// that are not white-preserving, composite as Normal (§11.7.4.2).
    /// </para>
    /// </remarks>
    public bool OverprintPreview { get; set; }

    /// <summary>Sets <see cref="OverprintPreview"/>.</summary>
    /// <param name="overprintPreview">Whether the page is rendered with overprint preview.</param>
    /// <returns>This instance.</returns>
    public RenderOptions WithOverprintPreview(bool overprintPreview)
    {
        OverprintPreview = overprintPreview;
        return this;
    }

    /// <summary>Sets <see cref="Layers"/>.</summary>
    /// <param name="layers">The layers drawn.</param>
    /// <returns>This instance.</returns>
    public RenderOptions WithLayers(DisplayLayers layers)
    {
        Layers = layers;
        return this;
    }

    /// <summary>Sets <see cref="Intent"/>.</summary>
    /// <param name="intent">Screen or print.</param>
    /// <returns>This instance.</returns>
    public RenderOptions WithIntent(DisplayIntent intent)
    {
        Intent = intent;
        return this;
    }

    /// <summary>Returns the size in pixels of <paramref name="page"/> rendered with these options.</summary>
    /// <param name="page">The page.</param>
    /// <returns>
    /// The crop box's width and height times <c>UserUnit</c> and <see cref="Scale"/>, each rounded up to whole pixels (at least 1),
    /// swapped when the total rotation is 90 or 270 degrees; with a <see cref="Region"/>, the size of the pixels the region covers.
    /// </returns>
    /// <exception cref="ArgumentException">The page would need more pixels than a bitmap can hold, or the region does not overlap it.</exception>
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
    /// <see cref="GetPixelSize"/>: origin at the top-left corner of the rotated crop box (or of the <see cref="Region"/>'s pixels), y
    /// down, one unit = one pixel.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>The device transform; a display list's own transforms are applied before it.</returns>
    /// <exception cref="ArgumentException">The region does not overlap the page.</exception>
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
        if (rotation is 90 or 270)
        {
            (width, height) = (height, width);
        }

        if (Region is { } region)
        {
            return MeasureRegion(region, transform, width, height, page);
        }

        int pixelWidth = Pixels(width, page);
        int pixelHeight = Pixels(height, page);
        if ((long)pixelWidth * pixelHeight * 4 > Array.MaxLength)
        {
            throw new ArgumentException($"The page would render to {pixelWidth} x {pixelHeight} pixels, more than a bitmap can hold.", nameof(page));
        }

        return new PageGeometry(transform, pixelWidth, pixelHeight);
    }

    /// <summary>
    /// The pixels of a region of the page: its corners mapped to the device, rounded outwards to whole pixels (with the same tolerance
    /// as the page size) and cut to the page's pixels; the transform moves them to the bitmap's origin.
    /// </summary>
    private static PageGeometry MeasureRegion(PdfRectangle region, Matrix transform, double width, double height, PdfPage page)
    {
        PathPoint a = transform.Transform(region.Left, region.Bottom);
        PathPoint b = transform.Transform(region.Right, region.Top);
        double pageRight = Math.Max(1, Math.Ceiling(width - 1e-6));
        double pageBottom = Math.Max(1, Math.Ceiling(height - 1e-6));
        double left = Math.Clamp(Math.Floor(Math.Min(a.X, b.X) + 1e-6), 0, pageRight);
        double top = Math.Clamp(Math.Floor(Math.Min(a.Y, b.Y) + 1e-6), 0, pageBottom);
        double right = Math.Clamp(Math.Ceiling(Math.Max(a.X, b.X) - 1e-6), 0, pageRight);
        double bottom = Math.Clamp(Math.Ceiling(Math.Max(a.Y, b.Y) - 1e-6), 0, pageBottom);
        if (!(right > left && bottom > top))
        {
            throw new ArgumentException("The region does not overlap the page.", nameof(page));
        }

        if (right > int.MaxValue || bottom > int.MaxValue || (right - left) * (bottom - top) * 4 > Array.MaxLength)
        {
            throw new ArgumentException($"The region would render to {right - left} x {bottom - top} pixels at {left}, {top}, more than a bitmap can hold.", nameof(page));
        }

        Matrix shifted = transform * Matrix.CreateTranslation(-left, -top);
        return new PageGeometry(shifted, (int)(right - left), (int)(bottom - top)) { OffsetX = (int)left, OffsetY = (int)top };
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
