namespace Broadside.Graphics;

/// <summary>The colour rendering intent: how colours are reproduced on a device whose gamut differs from the source.</summary>
/// <remarks>ISO 32000-2 §8.6.5.8, Table 69. A name a reader does not recognise selects <see cref="RelativeColorimetric"/>.</remarks>
public enum RenderingIntent
{
    /// <summary><c>RelativeColorimetric</c>, the initial value (Table 52).</summary>
    RelativeColorimetric,

    /// <summary><c>AbsoluteColorimetric</c>.</summary>
    AbsoluteColorimetric,

    /// <summary><c>Saturation</c>.</summary>
    Saturation,

    /// <summary><c>Perceptual</c>.</summary>
    Perceptual,
}
