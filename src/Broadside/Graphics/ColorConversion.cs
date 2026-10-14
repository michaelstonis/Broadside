namespace Broadside.Graphics;

/// <summary>What a colour is converted to and how: the device colour model, the rendering intent and the device-dependent controls.</summary>
/// <remarks>
/// ISO 32000-2 §8.6.5.8 (rendering intents), §8.6.5.9 (black point compensation) and §10.4.2.3 (black generation and undercolour
/// removal, used only when converting to CMYK). The default value converts to RGB with the relative colorimetric intent and the
/// processor's choice of black point compensation.
/// </remarks>
public readonly record struct ColorConversion
{
    /// <summary>Gets the device colour model converted to.</summary>
    public DeviceColorModel Target { get; init; }

    /// <summary>Gets the rendering intent.</summary>
    /// <remarks>ISO 32000-2 §8.6.5.8, Table 69.</remarks>
    public RenderingIntent Intent { get; init; }

    /// <summary>Gets whether black point compensation is applied.</summary>
    /// <remarks>ISO 32000-2 §8.6.5.9.</remarks>
    public BlackPointCompensation BlackPointCompensation { get; init; }

    /// <summary>Gets the black-generation function (one input k, one output 0 to 1), or <see langword="null"/> for the default BG(k) = k.</summary>
    /// <remarks>ISO 32000-2 §10.4.2.3 and Table 57 (<c>BG</c>, <c>BG2</c>). Used only when converting RGB to CMYK.</remarks>
    public PdfFunction? BlackGeneration { get; init; }

    /// <summary>Gets the undercolour-removal function (one input k, one output −1 to 1), or <see langword="null"/> for the default UCR(k) = k.</summary>
    /// <remarks>ISO 32000-2 §10.4.2.3 and Table 57 (<c>UCR</c>, <c>UCR2</c>). Used only when converting RGB to CMYK.</remarks>
    public PdfFunction? UndercolorRemoval { get; init; }

    /// <summary>
    /// Gets the output intent of the content being converted, or <see langword="null"/> for none: the device or production
    /// condition its device colours are meant for.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §14.11.5. A colour management may treat DeviceCMYK colours (and DeviceRGB or DeviceGray ones when the profile has
    /// that many components) as described by <see cref="PdfOutputIntent.DestinationProfile"/>, and convert to that profile when the
    /// target is its device; it may also disregard it. Default colour spaces (§8.6.5.6) are applied before, so they take precedence.
    /// </remarks>
    public PdfOutputIntent? OutputIntent { get; init; }

    /// <summary>Gets a value indicating whether black point compensation applies: on unless it is off or the intent is absolute colorimetric.</summary>
    /// <remarks>
    /// ISO 32000-2 §8.6.5.9: with AbsoluteColorimetric "black point compensation shall not be used". It matters only to profile
    /// conversions (an ICC engine): CalGray, CalRGB and Lab already reach black at XYZ zero, the source black of ISO 18619, so
    /// compensation leaves them unchanged.
    /// </remarks>
    public bool CompensatesBlackPoint => BlackPointCompensation != BlackPointCompensation.Off && Intent != RenderingIntent.AbsoluteColorimetric;

    /// <summary>Returns the conversion the graphics state asks for: its rendering intent, black point compensation, black generation, undercolour removal and output intent.</summary>
    /// <param name="state">The graphics state.</param>
    /// <param name="target">The device colour model converted to.</param>
    /// <returns>The conversion.</returns>
    public static ColorConversion FromState(in GraphicsState state, DeviceColorModel target) => new()
    {
        Target = target,
        Intent = state.RenderingIntent,
        BlackPointCompensation = state.BlackPointCompensation,
        BlackGeneration = state.BlackGeneration,
        UndercolorRemoval = state.UndercolorRemoval,
        OutputIntent = state.OutputIntent,
    };
}
