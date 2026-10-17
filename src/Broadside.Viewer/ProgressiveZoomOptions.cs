namespace Broadside.Viewer;

/// <summary>The zoom range of a <see cref="ProgressiveZoom"/> and how long zoom steps pause before sharp tiles render.</summary>
/// <remarks>
/// Spec #33 viewer capability 2. Mutable and fluent like the other options objects: <c>Use*</c> selects an implementation, <c>With*</c>
/// sets a value. A zoom copies the values when it is created.
/// </remarks>
public sealed class ProgressiveZoomOptions
{
    /// <summary>The default <see cref="MinZoom"/>: a tenth of a pixel per point.</summary>
    public const double DefaultMinZoom = 0.1;

    /// <summary>The default <see cref="MaxZoom"/>: 64 pixels per point (6400 % at one device pixel per point).</summary>
    public const double DefaultMaxZoom = 64;

    private TimeSpan _settleDelay = TimeSpan.FromMilliseconds(150);

    /// <summary>Gets the smallest zoom, in pixels per point (<see cref="DefaultMinZoom"/> by default).</summary>
    public double MinZoom { get; private set; } = DefaultMinZoom;

    /// <summary>
    /// Gets the largest zoom, in pixels per point (<see cref="DefaultMaxZoom"/> by default): the cap on the scale tiles render at,
    /// whatever a gesture asks for.
    /// </summary>
    public double MaxZoom { get; private set; } = DefaultMaxZoom;

    /// <summary>
    /// Gets or sets how long zoom steps outside an explicit gesture (mouse-wheel or keyboard steps) must pause before the zoom settles
    /// and sharp tiles render at it (150 ms by default; zero settles at the next frame).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public TimeSpan SettleDelay
    {
        get => _settleDelay;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            _settleDelay = value;
        }
    }

    /// <summary>Gets the clock that times <see cref="SettleDelay"/> (<see cref="TimeProvider.System"/> by default).</summary>
    public TimeProvider TimeProvider { get; private set; } = TimeProvider.System;

    /// <summary>Sets <see cref="MinZoom"/> and <see cref="MaxZoom"/>.</summary>
    /// <param name="minZoom">The smallest zoom.</param>
    /// <param name="maxZoom">The largest zoom.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A zoom is not positive and finite, or <paramref name="minZoom"/> exceeds <paramref name="maxZoom"/>.
    /// </exception>
    public ProgressiveZoomOptions WithZoomRange(double minZoom, double maxZoom)
    {
        CheckZoom(minZoom, nameof(minZoom));
        CheckZoom(maxZoom, nameof(maxZoom));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minZoom, maxZoom);
        MinZoom = minZoom;
        MaxZoom = maxZoom;
        return this;
    }

    /// <summary>Sets <see cref="SettleDelay"/>.</summary>
    /// <param name="delay">The pause after the last step.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="delay"/> is negative.</exception>
    public ProgressiveZoomOptions WithSettleDelay(TimeSpan delay)
    {
        SettleDelay = delay;
        return this;
    }

    /// <summary>Selects the clock that times <see cref="SettleDelay"/> (a manual one in tests).</summary>
    /// <param name="timeProvider">The clock.</param>
    /// <returns>These options.</returns>
    public ProgressiveZoomOptions UseTimeProvider(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        TimeProvider = timeProvider;
        return this;
    }

    private static void CheckZoom(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "A zoom is a positive, finite number of pixels per point.");
        }
    }
}
