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
    /// Gets or sets the layers a page render draws: the content, the annotations and the form fields, in that order;
    /// <see cref="DisplayLayers.All"/> by default.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §12.5 and §12.7, through <see cref="PdfPage.CreateDisplayList(DisplayLayers, DisplayIntent, Content.ContentOptions)"/>.
    /// A viewer that redraws overlays without re-rasterizing the page builds each layer's list itself and replays them with
    /// <see cref="SoftwareRasterizer.Render(DisplayList, Bitmap, in Matrix)"/>.
    /// </remarks>
    public DisplayLayers Layers { get; set; } = DisplayLayers.All;

    /// <summary>
    /// Gets or sets what the render is for, which decides the annotations drawn, the optional content shown and the alternate images
    /// painted; <see cref="DisplayIntent.View"/> by default.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §12.5.3, Table 167 (<c>NoView</c>, <c>Print</c>, <c>NoZoom</c>, <c>NoRotate</c>); §8.11.4.4 (the auto-state
    /// entries for the Print event apply when printing); §8.9.5.4 (an image without <c>OC</c> is printed as its alternate marked
    /// <c>DefaultForPrinting</c>).
    /// </remarks>
    public DisplayIntent Intent { get; set; }

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

    /// <summary>
    /// Gets or sets the states of the optional content groups that decide what the render shows, computed from any configuration
    /// of the document (<see cref="PdfOptionalContentProperties.GetStates"/>); <see langword="null"/>, the default, for the states
    /// of the default configuration.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §8.11.4.3 (configurations) and §8.11.4.5 (the initial states). It decides the page content's optional sections
    /// and XObjects (§8.11.3), the annotations' <c>OC</c> entries (§8.11.3.3) and which alternate image is painted (§8.9.5.4). For
    /// <see cref="DisplayIntent.Print"/> the configuration's auto-state entries for the Print event are applied over these states
    /// (§8.11.4.4); see <see cref="PdfPage.CreateDisplayList(DisplayLayers, DisplayIntent, Content.ContentOptions)"/>.
    /// </remarks>
    public PdfOptionalContentState? OptionalContentState { get; set; }

    /// <summary>Sets <see cref="OptionalContentState"/>.</summary>
    /// <param name="state">The group states, or <see langword="null"/> for those of the default configuration.</param>
    /// <returns>This instance.</returns>
    public RenderOptions WithOptionalContentState(PdfOptionalContentState? state)
    {
        OptionalContentState = state;
        return this;
    }

    /// <summary>
    /// Gets or sets the kinds of painting that are anti-aliased; <see cref="Rendering.AntiAliasing.All"/> by default. A kind left out
    /// paints whole pixels only.
    /// </summary>
    /// <remarks>ISO 32000-2 §10.7.4 (scan conversion rules); see <see cref="Rendering.AntiAliasing"/>.</remarks>
    public AntiAliasing AntiAliasing { get; set; } = AntiAliasing.All;

    /// <summary>Sets <see cref="AntiAliasing"/>.</summary>
    /// <param name="antiAliasing">The kinds anti-aliased.</param>
    /// <returns>This instance.</returns>
    public RenderOptions WithAntiAliasing(AntiAliasing antiAliasing)
    {
        AntiAliasing = antiAliasing;
        return this;
    }

    /// <summary>
    /// Gets or sets a value indicating whether a page render starts from transparent black instead of opaque white, so that the
    /// pixels nothing paints keep zero alpha and partly covered ones are partly transparent; <see langword="false"/> by default.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §11.4.7: the page group is composited over a transparent backdrop, (C<sub>g</sub>, α<sub>g</sub>), and the
    /// second step that composites it with the medium's white, C = (1 − α<sub>g</sub>) × W + α<sub>g</sub> × C<sub>g</sub>, is left
    /// to whoever shows the bitmap (premultiplied BGRA). An opaque render is that step done over white.
    /// </remarks>
    public bool TransparentBackground { get; set; }

    /// <summary>Sets <see cref="TransparentBackground"/>.</summary>
    /// <param name="transparent">Whether the background is transparent rather than white.</param>
    /// <returns>This instance.</returns>
    public RenderOptions WithTransparentBackground(bool transparent)
    {
        TransparentBackground = transparent;
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
