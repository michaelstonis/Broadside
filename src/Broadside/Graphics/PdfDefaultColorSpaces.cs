namespace Broadside.Graphics;

/// <summary>
/// The default colour spaces of a resource dictionary: the <c>DefaultGray</c>, <c>DefaultRGB</c> and <c>DefaultCMYK</c> entries of its
/// <c>ColorSpace</c> subdictionary, which replace the device spaces when colours are painted.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.6.5.6. When a device colour space is selected, the default of the current resource dictionary, if any, is used
/// in its place, with the colour values unchanged; this also applies to the base of an Indexed space, the underlying space of a
/// Pattern space and the alternate of a Separation or DeviceN space when the alternate is used. The look-up happens when an object
/// is painted, with the resources current then, so a form XObject's own defaults win inside it. Get the defaults current in a
/// content stream from <see cref="Content.ContentContext.DefaultColorSpaces"/>, or for any resource dictionary from
/// <see cref="PdfDocument.GetDefaultColorSpaces"/>; <see cref="PdfColorConverter"/> applies them.
/// </para>
/// <para>
/// A default with the wrong number of components, or of the Lab, Indexed or Pattern family, is ignored with a diagnostic. A default
/// is not itself remapped (its own device base or alternate stays the device space).
/// </para>
/// </remarks>
public sealed class PdfDefaultColorSpaces
{
    internal PdfDefaultColorSpaces(PdfColorSpace? gray, PdfColorSpace? rgb, PdfColorSpace? cmyk)
    {
        Gray = gray;
        Rgb = rgb;
        Cmyk = cmyk;
    }

    /// <summary>Gets the defaults of a resource dictionary that has none.</summary>
    public static PdfDefaultColorSpaces None { get; } = new(null, null, null);

    /// <summary>Gets the space that replaces DeviceGray, or <see langword="null"/>.</summary>
    public PdfColorSpace? Gray { get; }

    /// <summary>Gets the space that replaces DeviceRGB, or <see langword="null"/>.</summary>
    public PdfColorSpace? Rgb { get; }

    /// <summary>Gets the space that replaces DeviceCMYK, or <see langword="null"/>.</summary>
    public PdfColorSpace? Cmyk { get; }

    /// <summary>Gets a value indicating whether no device space is replaced.</summary>
    public bool IsEmpty => Gray is null && Rgb is null && Cmyk is null;

    /// <summary>Returns the space a colour selected in <paramref name="colorSpace"/> is painted in.</summary>
    /// <param name="colorSpace">The selected space.</param>
    /// <returns>The default for a device space that has one; <paramref name="colorSpace"/> otherwise.</returns>
    /// <remarks>ISO 32000-2 §8.6.5.6. Only the space itself is replaced; nested spaces are replaced when converted.</remarks>
    public PdfColorSpace Remap(PdfColorSpace colorSpace)
    {
        ArgumentNullException.ThrowIfNull(colorSpace);
        return colorSpace.Family switch
        {
            PdfColorSpaceFamily.DeviceGray => Gray ?? colorSpace,
            PdfColorSpaceFamily.DeviceRgb => Rgb ?? colorSpace,
            PdfColorSpaceFamily.DeviceCmyk => Cmyk ?? colorSpace,
            _ => colorSpace,
        };
    }
}
