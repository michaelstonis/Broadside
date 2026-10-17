namespace Broadside.Viewer;

/// <summary>How a <see cref="PageLayout"/> arranges pages: the mode, the cover page, the gaps, the padding and the user's rotation.</summary>
/// <remarks>
/// ISO 32000-2 §7.7.2, Table 29 (<c>PageLayout</c>; <see cref="For(PdfPageLayout)"/>) and §7.7.3.3 (<c>Rotate</c>). Spec #33 story 54
/// (viewer capability 14). Mutable and fluent like the other options objects: <c>With*</c> sets a value. A layout copies the options
/// when it is created, so changing them afterwards does not affect it. Gaps and padding are in points, so they scale with the zoom and
/// a layout is the same at every zoom.
/// </remarks>
public sealed class PageLayoutOptions
{
    /// <summary>The default <see cref="Gap"/> and <see cref="Padding"/>: 8 points.</summary>
    public const double DefaultSpacing = 8;

    private double _gap = DefaultSpacing;
    private double _padding = DefaultSpacing;
    private int _rotation;

    /// <summary>Gets or sets the arrangement (<see cref="PageLayoutMode.Continuous"/> by default).</summary>
    public PageLayoutMode Mode { get; set; } = PageLayoutMode.Continuous;

    /// <summary>
    /// Gets or sets whether the first page stands alone on the right as a cover in the two-up modes, so even-numbered pages (1-based) are
    /// on the left (<c>TwoColumnRight</c>, <c>TwoPageRight</c>). <see langword="false"/> by default.
    /// </summary>
    public bool ShowCover { get; set; }

    /// <summary>Gets or sets the space between spreads and between the two pages of a spread, in points (<see cref="DefaultSpacing"/> by default).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double Gap
    {
        get => _gap;
        set => _gap = Spacing(value);
    }

    /// <summary>Gets or sets the space around the content, in points (<see cref="DefaultSpacing"/> by default).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double Padding
    {
        get => _padding;
        set => _padding = Spacing(value);
    }

    /// <summary>
    /// Gets or sets the rotation the user adds to every page's own <c>Rotate</c>, in degrees clockwise: a multiple of 90, stored as 0,
    /// 90, 180 or 270 (<see cref="Tile.Rotation"/>).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a multiple of 90.</exception>
    public int Rotation
    {
        get => _rotation;
        set
        {
            if (value % 90 != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A rotation is a multiple of 90 degrees.");
            }

            _rotation = ((value % 360) + 360) % 360;
        }
    }

    /// <summary>Returns options for a document's <c>PageLayout</c> entry (ISO 32000-2 §7.7.2, Table 29).</summary>
    /// <param name="layout">The entry, <see cref="PdfDocument.PageLayout"/>.</param>
    /// <returns>New options: <c>SinglePage</c> and <c>OneColumn</c> one page per spread, the <c>Two*</c> names two-up, <c>*Right</c> with a cover.</returns>
    public static PageLayoutOptions For(PdfPageLayout layout) => layout switch
    {
        PdfPageLayout.OneColumn => new PageLayoutOptions { Mode = PageLayoutMode.Continuous },
        PdfPageLayout.TwoColumnLeft => new PageLayoutOptions { Mode = PageLayoutMode.TwoUpContinuous },
        PdfPageLayout.TwoColumnRight => new PageLayoutOptions { Mode = PageLayoutMode.TwoUpContinuous, ShowCover = true },
        PdfPageLayout.TwoPageLeft => new PageLayoutOptions { Mode = PageLayoutMode.TwoUp },
        PdfPageLayout.TwoPageRight => new PageLayoutOptions { Mode = PageLayoutMode.TwoUp, ShowCover = true },
        _ => new PageLayoutOptions { Mode = PageLayoutMode.SinglePage },
    };

    /// <summary>Sets <see cref="Mode"/>.</summary>
    /// <param name="mode">The arrangement.</param>
    /// <returns>These options.</returns>
    public PageLayoutOptions WithMode(PageLayoutMode mode)
    {
        Mode = mode;
        return this;
    }

    /// <summary>Sets <see cref="ShowCover"/>.</summary>
    /// <param name="showCover">Whether the first page stands alone.</param>
    /// <returns>These options.</returns>
    public PageLayoutOptions WithCover(bool showCover)
    {
        ShowCover = showCover;
        return this;
    }

    /// <summary>Sets <see cref="Gap"/>.</summary>
    /// <param name="gap">The gap in points.</param>
    /// <returns>These options.</returns>
    public PageLayoutOptions WithGap(double gap)
    {
        Gap = gap;
        return this;
    }

    /// <summary>Sets <see cref="Padding"/>.</summary>
    /// <param name="padding">The padding in points.</param>
    /// <returns>These options.</returns>
    public PageLayoutOptions WithPadding(double padding)
    {
        Padding = padding;
        return this;
    }

    /// <summary>Sets <see cref="Rotation"/>.</summary>
    /// <param name="rotation">The rotation in degrees clockwise, a multiple of 90.</param>
    /// <returns>These options.</returns>
    public PageLayoutOptions WithRotation(int rotation)
    {
        Rotation = rotation;
        return this;
    }

    private static double Spacing(double value)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A spacing is a finite number of points, zero or more.");
        }

        return value;
    }
}
