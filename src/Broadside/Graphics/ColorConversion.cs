namespace Broadside.Graphics;

/// <summary>What a colour is converted to and how: the device colour model, the rendering intent and the device-dependent controls.</summary>
/// <remarks>
/// ISO 32000-2 §8.6.5.8 (rendering intents), §8.6.5.9 (black point compensation), §10.4.2.3 (black generation and undercolour
/// removal, used only when converting to CMYK) and §10.5 (transfer functions, applied to the device colour last). The default
/// value converts to RGB with the relative colorimetric intent, the processor's choice of black point compensation and no transfer
/// function.
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
    /// Gets the transfer function applied to the device colour after the conversion, or <see langword="null"/> for the device's
    /// default (none). An identity transfer function changes nothing.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §10.5 and Table 57 (<c>TR</c>, <c>TR2</c>; deprecated in PDF 2.0). Each device component goes through its own
    /// function in additive form: an RGB device uses the first three of four functions, a gray device the fourth, a CMYK device all
    /// four (a subtractive component c passes as 1 − c and the result is converted back); a single function applies to every
    /// component. A DeviceGray colour converted to CMYK passes only its black through the gray function.
    /// </remarks>
    public PdfTransferFunction? TransferFunction { get; init; }

    /// <summary>Gets a value indicating whether black point compensation applies: on unless it is off or the intent is absolute colorimetric.</summary>
    /// <remarks>ISO 32000-2 §8.6.5.9: with AbsoluteColorimetric "black point compensation shall not be used".</remarks>
    public bool CompensatesBlackPoint => BlackPointCompensation != BlackPointCompensation.Off && Intent != RenderingIntent.AbsoluteColorimetric;

    /// <summary>
    /// Returns the conversion the graphics state asks for: its rendering intent, black point compensation, black generation,
    /// undercolour removal and transfer function.
    /// </summary>
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
        TransferFunction = state.TransferFunction,
    };
}
