namespace Broadside.Rendering;

/// <summary>How <see cref="VisualComparison.Compare"/> renders and compares two pages, set fluently with <c>With*</c> methods.</summary>
/// <remarks>ISO 32000-2 §8.3.2.2 (device space) and §10.7.4 (pixels painted). Options may be shared between threads while no thread changes them.</remarks>
public sealed class VisualComparisonOptions
{
    /// <summary>The default <see cref="Dpi"/>.</summary>
    public const double DefaultDpi = 96;

    /// <summary>The default <see cref="Tolerance"/>.</summary>
    public const int DefaultTolerance = 16;

    /// <summary>The default <see cref="MinRegionArea"/>.</summary>
    public const int DefaultMinRegionArea = 16;

    private double _dpi = DefaultDpi;
    private int _tolerance = DefaultTolerance;
    private int _minRegionArea = DefaultMinRegionArea;

    /// <summary>Gets or sets the resolution both pages are rendered at, in pixels per inch. Default <see cref="DefaultDpi"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive finite number.</exception>
    /// <remarks>ISO 32000-2 §8.3.2.3: 72 user space units an inch at a <c>UserUnit</c> of 1.</remarks>
    public double Dpi
    {
        get => _dpi;
        set => _dpi = double.IsFinite(value) && value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "The resolution must be a positive finite number.");
    }

    /// <summary>
    /// Gets or sets the largest difference, in levels of 0 to 255, a pixel's channels may have and still count as equal: it absorbs
    /// anti-aliasing noise and rounding. Default <see cref="DefaultTolerance"/>; 0 counts every difference.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 255.</exception>
    public int Tolerance
    {
        get => _tolerance;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 255);
            _tolerance = value;
        }
    }

    /// <summary>
    /// Gets or sets the fewest changed pixels a region needs to be reported in <see cref="VisualComparison.ChangedRegions"/>.
    /// Default <see cref="DefaultMinRegionArea"/>. Smaller regions still count in <see cref="VisualComparison.ChangedFraction"/> and
    /// the bitmaps.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int MinRegionArea
    {
        get => _minRegionArea;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _minRegionArea = value;
        }
    }

    /// <summary>Gets or sets where each page is placed when the pages differ in size. Default <see cref="VisualAlignment.TopLeft"/>.</summary>
    public VisualAlignment Alignment { get; set; }

    /// <summary>Sets <see cref="Dpi"/>.</summary>
    /// <param name="dpi">The resolution, in pixels per inch.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="dpi"/> is not a positive finite number.</exception>
    public VisualComparisonOptions WithDpi(double dpi)
    {
        Dpi = dpi;
        return this;
    }

    /// <summary>Sets <see cref="Tolerance"/>.</summary>
    /// <param name="levels">The largest channel difference that still counts as equal.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="levels"/> is not between 0 and 255.</exception>
    public VisualComparisonOptions WithTolerance(int levels)
    {
        Tolerance = levels;
        return this;
    }

    /// <summary>Sets <see cref="MinRegionArea"/>.</summary>
    /// <param name="pixels">The fewest changed pixels a reported region has.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pixels"/> is negative.</exception>
    public VisualComparisonOptions WithMinRegionArea(int pixels)
    {
        MinRegionArea = pixels;
        return this;
    }

    /// <summary>Sets <see cref="Alignment"/>.</summary>
    /// <param name="alignment">Where each page is placed.</param>
    /// <returns>These options.</returns>
    public VisualComparisonOptions WithAlignment(VisualAlignment alignment)
    {
        Alignment = alignment;
        return this;
    }
}
