namespace Broadside.Graphics;

/// <summary>A CIE 1931 XYZ tristimulus value, such as the white or black point of a CIE-based colour space.</summary>
/// <param name="X">X.</param>
/// <param name="Y">Y.</param>
/// <param name="Z">Z.</param>
/// <remarks>ISO 32000-2 §8.6.5.1 and Tables 62 to 64 (<c>WhitePoint</c>, <c>BlackPoint</c>).</remarks>
public readonly record struct CieXyz(double X, double Y, double Z)
{
    /// <summary>Gets the CIE standard illuminant D65, the white of sRGB (IEC 61966-2-1), used when a white point is unusable.</summary>
    public static CieXyz D65 { get; } = new(0.95047, 1.0, 1.08883);

    /// <summary>Gets the CIE standard illuminant D50, the white of the ICC profile connection space (ICC.1:2022 §6.3.2).</summary>
    public static CieXyz D50 { get; } = new(0.9642, 1.0, 0.8249);
}
