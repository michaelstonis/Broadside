namespace Broadside.Graphics;

/// <summary>Whether conversions between colour spaces compensate for the difference between their black points.</summary>
/// <remarks>
/// ISO 32000-2 §8.6.5.9 and Table 57 (<c>UseBlackPtComp</c>, PDF 2.0): <c>ON</c>, <c>OFF</c> or <c>Default</c>, which leaves it to
/// the processor; Broadside's managed colour management compensates by default. With the <c>AbsoluteColorimetric</c> intent black
/// point compensation is never applied.
/// </remarks>
public enum BlackPointCompensation
{
    /// <summary><c>Default</c>, the initial value (Table 52): the processor decides.</summary>
    Default,

    /// <summary><c>ON</c>.</summary>
    On,

    /// <summary><c>OFF</c>.</summary>
    Off,
}
