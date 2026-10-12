namespace Broadside.Graphics;

/// <summary>The blend mode of the transparent imaging model.</summary>
/// <remarks>ISO 32000-2 §11.3.5, Tables 134 and 135. <c>Compatible</c> (deprecated) is read as <see cref="Normal"/>.</remarks>
public enum BlendMode
{
    /// <summary><c>Normal</c>, the initial value (Table 52).</summary>
    Normal,

    /// <summary><c>Multiply</c>.</summary>
    Multiply,

    /// <summary><c>Screen</c>.</summary>
    Screen,

    /// <summary><c>Overlay</c>.</summary>
    Overlay,

    /// <summary><c>Darken</c>.</summary>
    Darken,

    /// <summary><c>Lighten</c>.</summary>
    Lighten,

    /// <summary><c>ColorDodge</c>.</summary>
    ColorDodge,

    /// <summary><c>ColorBurn</c>.</summary>
    ColorBurn,

    /// <summary><c>HardLight</c>.</summary>
    HardLight,

    /// <summary><c>SoftLight</c>.</summary>
    SoftLight,

    /// <summary><c>Difference</c>.</summary>
    Difference,

    /// <summary><c>Exclusion</c>.</summary>
    Exclusion,

    /// <summary><c>Hue</c> (non-separable).</summary>
    Hue,

    /// <summary><c>Saturation</c> (non-separable).</summary>
    Saturation,

    /// <summary><c>Color</c> (non-separable).</summary>
    Color,

    /// <summary><c>Luminosity</c> (non-separable).</summary>
    Luminosity,
}
