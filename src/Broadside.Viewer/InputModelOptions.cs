namespace Broadside.Viewer;

/// <summary>The clock, pixel ratio, animation and snapping settings of an <see cref="InputModel"/>.</summary>
/// <remarks>
/// Spec #33 viewer capability 14. Mutable and fluent like the other options objects: <c>Use*</c> selects an implementation, <c>With*</c>
/// sets a value. An input model copies the values when it is created.
/// </remarks>
public sealed class InputModelOptions
{
    /// <summary>Gets the clock that times taps, fling velocity and animations (<see cref="TimeProvider.System"/> by default).</summary>
    public TimeProvider TimeProvider { get; private set; } = TimeProvider.System;

    /// <summary>
    /// Gets the device pixels per device-independent pixel the model starts with (1 by default): events arrive in device-independent
    /// pixels, the <see cref="Viewport"/> is in device pixels.
    /// </summary>
    public double PixelRatio { get; private set; } = 1;

    /// <summary>Gets how long a double-tap zoom, a fit zoom, a page turn or a snap animates (250 ms by default; zero jumps at once).</summary>
    public TimeSpan AnimationDuration { get; private set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Gets the time constant of a fling's exponential deceleration (325 ms by default): a fling travels its release velocity times this
    /// before it stops.
    /// </summary>
    public TimeSpan FlingTimeConstant { get; private set; } = TimeSpan.FromMilliseconds(325);

    /// <summary>Gets the zoom a double-tap goes to from fit-width, as a multiple of the fit-width zoom (2 by default).</summary>
    public double DoubleTapZoom { get; private set; } = 2;

    /// <summary>
    /// Gets a value indicating whether a released drag in a continuous mode snaps to page edges as in a paged mode (<see langword="false"/>
    /// by default; paged modes always snap).
    /// </summary>
    public bool SnapToPages { get; private set; }

    /// <summary>Selects the clock (a manual one in tests; share it with the <see cref="ProgressiveZoomOptions"/>).</summary>
    /// <param name="timeProvider">The clock.</param>
    /// <returns>These options.</returns>
    public InputModelOptions UseTimeProvider(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        TimeProvider = timeProvider;
        return this;
    }

    /// <summary>Sets <see cref="PixelRatio"/>.</summary>
    /// <param name="pixelRatio">Device pixels per device-independent pixel (the DOM's <c>devicePixelRatio</c>).</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pixelRatio"/> is not positive and finite.</exception>
    public InputModelOptions WithPixelRatio(double pixelRatio)
    {
        PixelRatio = InputModel.CheckPixelRatio(pixelRatio);
        return this;
    }

    /// <summary>Sets <see cref="AnimationDuration"/>.</summary>
    /// <param name="duration">The duration; zero jumps at once.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration"/> is negative.</exception>
    public InputModelOptions WithAnimationDuration(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        AnimationDuration = duration;
        return this;
    }

    /// <summary>Sets <see cref="FlingTimeConstant"/>.</summary>
    /// <param name="timeConstant">The time constant; zero turns flings off.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeConstant"/> is negative.</exception>
    public InputModelOptions WithFlingTimeConstant(TimeSpan timeConstant)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeConstant, TimeSpan.Zero);
        FlingTimeConstant = timeConstant;
        return this;
    }

    /// <summary>Sets <see cref="DoubleTapZoom"/>.</summary>
    /// <param name="multiple">The multiple of the fit-width zoom.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="multiple"/> is not finite and above 1.</exception>
    public InputModelOptions WithDoubleTapZoom(double multiple)
    {
        if (!double.IsFinite(multiple) || multiple <= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(multiple), multiple, "A double-tap zooms in: the multiple is finite and above 1.");
        }

        DoubleTapZoom = multiple;
        return this;
    }

    /// <summary>Sets <see cref="SnapToPages"/>.</summary>
    /// <param name="snap">Whether continuous modes snap to page edges.</param>
    /// <returns>These options.</returns>
    public InputModelOptions WithPageSnapping(bool snap)
    {
        SnapToPages = snap;
        return this;
    }
}
