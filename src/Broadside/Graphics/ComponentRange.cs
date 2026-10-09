namespace Broadside.Graphics;

/// <summary>The interval a colour component's values lie in.</summary>
/// <param name="Minimum">The smallest value.</param>
/// <param name="Maximum">The largest value.</param>
/// <remarks>ISO 32000-2 §8.6.2: a colour value is one number per component, each within the range its colour space defines.</remarks>
public readonly record struct ComponentRange(double Minimum, double Maximum)
{
    /// <summary>Returns <paramref name="value"/> clipped into the range; NaN becomes <see cref="Minimum"/>.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The nearest value in the range.</returns>
    public double Clamp(double value) => value >= Minimum ? Math.Min(value, Maximum) : Minimum;
}
