using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// A colour in a display list: the colour space it was specified in, its components, and what its conversion depends on. Never
/// converted: a backend converts it to its own device model. Valid only during the backend call that hands it over.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.6.1: the display list carries the colour specification, device independent; colour rendering (§10) is the
/// backend's. Keeping the specified space (DeviceCMYK, Separation, Lab, ICCBased and so on) rather than a device colour leaves CMYK,
/// separation and colour-managed output possible.
/// </para>
/// <para>
/// Convert with <c>list.Document.GetColorConverter(color.ColorSpace, color.Conversion with { Target = … }, color.DefaultColorSpaces)</c>
/// (<see cref="PdfDocument.GetColorConverter(PdfColorSpace, ColorConversion, PdfDefaultColorSpaces?)"/>), which expands the special
/// spaces, applies the default colour spaces and hands the rest to the engine's <see cref="IColorManagement"/>.
/// </para>
/// </remarks>
public readonly ref struct DisplayColor
{
    internal DisplayColor(PdfColorSpace colorSpace, ReadOnlySpan<float> components, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion)
    {
        ColorSpace = colorSpace;
        Components = components;
        DefaultColorSpaces = defaultColorSpaces;
        Conversion = conversion;
    }

    /// <summary>Gets the colour space the colour was selected in, resolved from the resources, before default colour spaces apply.</summary>
    /// <remarks>ISO 32000-2 §8.6.3 (colour space families), §8.6.8 (<c>CS</c>, <c>cs</c> and the device-space operators).</remarks>
    public PdfColorSpace ColorSpace { get; }

    /// <summary>Gets the components as the colour operator gave them, not yet clipped into their ranges.</summary>
    /// <remarks>ISO 32000-2 §8.6.2.</remarks>
    public ReadOnlySpan<float> Components { get; }

    /// <summary>Gets the default colour spaces current where the colour was painted, which replace the device spaces on conversion.</summary>
    /// <remarks>ISO 32000-2 §8.6.5.6: looked up in the resources current at the paint, so a form XObject's own defaults win inside it.</remarks>
    public PdfDefaultColorSpaces DefaultColorSpaces { get; }

    /// <summary>
    /// Gets the conversion the graphics state asked for at the paint: rendering intent, black point compensation, black generation and
    /// undercolour removal, with <see cref="ColorConversion.Target"/> RGB; a backend for another device model replaces the target.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.6.5.8, §8.6.5.9 and §10.4.2.3.</remarks>
    public ColorConversion Conversion { get; }
}
