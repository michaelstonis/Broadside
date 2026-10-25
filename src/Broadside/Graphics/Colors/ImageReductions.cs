namespace Broadside.Graphics.Colors;

/// <summary>
/// How far image samples may be averaged in their own colour space, before a conversion, as a power of two (2^n samples to one).
/// Averaging then converting equals converting then averaging only for an affine conversion; for a curved one the two differ more
/// the more contrast a block holds, so the reduction before conversion is capped and the rest is left to the rasterizer, which
/// averages converted pixels.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.6 (colour spaces) and §8.9.5 (image dictionaries). Measured (f(mean) against mean(f) over photographs): a strongly
/// curved tint transform gives 56.6 dB at 1/2, 47.0 dB at 1/4 and 40.5 dB at 1/8; gamma tone curves of the kind ICC and Cal spaces
/// apply stay at or above 54 dB at 1/4 on photographs, but a CalRGB gamma of 2.2 over fine high-contrast texture measured 36.7 dB
/// at 1/4. Every curved conversion is therefore capped at 1/2, which keeps every case measured at or above 45 dB.
/// </remarks>
internal static class ImageReductions
{
    /// <summary>No limit: the conversion is affine.</summary>
    public const int Any = 15;

    /// <summary>1/2: a curved conversion (a tint transform, CalGray, CalRGB, Lab, a colour-management transform, a transfer function).</summary>
    public const int Curved = 1;

    /// <summary>Never: the samples are indices (Indexed, §8.6.6.3).</summary>
    public const int None = 0;

    /// <summary>Returns the cap for a converter from the colour-management extension point.</summary>
    /// <param name="converter">The converter.</param>
    /// <returns>The cap.</returns>
    public static int Of(IColorConverter converter) => converter switch
    {
        DeviceConverter device => device.IsAffine ? Any : Curved,
        IccAlternateConverter alternate => Of(alternate.Alternate),
        _ => Curved,
    };
}
