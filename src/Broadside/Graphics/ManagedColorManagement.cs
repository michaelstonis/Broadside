using Broadside.Graphics.Colors;

namespace Broadside.Graphics;

/// <summary>
/// The managed default of the colour-management extension point: the device formulas of §10.4.2, the CIE equations of §8.6.5 to
/// sRGB, and ICCBased colours through their alternate space, until the ICC engine arrives (Phase 3).
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §10.3 and §10.4. Device to device conversions follow §10.4.2, except DeviceCMYK to RGB, which by default uses a
/// characterisation of SWOP printing (<see cref="CmykConversion.Characterized"/>) because the formula of §10.4.2.5 turns 100 % cyan
/// into (0, 1, 1) where every viewer shows about (0, 0.68, 0.94); <see cref="Classic"/> keeps the formula. RGB to CMYK uses the
/// graphics state's black generation and undercolour removal, or BG(k) = k and UCR(k) = k when it has none.
/// </para>
/// <para>
/// CalGray, CalRGB and Lab go through XYZ, Bradford adaptation to D65 and the sRGB primaries, with black point compensation unless
/// it is off. An ICCBased space whose profile is a Lab profile is converted as Lab (its values would be meaningless to an RGB
/// alternate); any other converts through <see cref="PdfIccBasedColorSpace.Alternate"/>.
/// </para>
/// </remarks>
public sealed class ManagedColorManagement : IColorManagement
{
    /// <summary>Initializes a new instance of the <see cref="ManagedColorManagement"/> class.</summary>
    /// <param name="cmykConversion">How DeviceCMYK converts to RGB and gray.</param>
    public ManagedColorManagement(CmykConversion cmykConversion = CmykConversion.Characterized) => CmykConversion = cmykConversion;

    /// <summary>Gets the default instance, with the characterised CMYK conversion.</summary>
    public static ManagedColorManagement Default { get; } = new();

    /// <summary>Gets an instance that converts DeviceCMYK by the formulas of §10.4.2 exactly.</summary>
    public static ManagedColorManagement Classic { get; } = new(CmykConversion.Classic);

    /// <summary>Gets how DeviceCMYK converts to RGB and gray.</summary>
    public CmykConversion CmykConversion { get; }

    /// <inheritdoc/>
    /// <exception cref="ArgumentException"><paramref name="source"/> is an Indexed, Pattern, Separation or DeviceN space.</exception>
    public IColorConverter CreateConverter(PdfColorSpace source, ColorConversion conversion)
    {
        ArgumentNullException.ThrowIfNull(source);
        switch (source)
        {
            case PdfDeviceGrayColorSpace or PdfDeviceRgbColorSpace or PdfDeviceCmykColorSpace:
                return new DeviceConverter(source.Family, conversion, CmykConversion);
            case PdfCalGrayColorSpace calGray:
                return CieConverter.ForCalGray(calGray, conversion);
            case PdfCalRgbColorSpace calRgb:
                return CieConverter.ForCalRgb(calRgb, conversion);
            case PdfLabColorSpace lab:
                return CieConverter.ForLab(lab, conversion);
            case PdfIccBasedColorSpace icc when icc.IsLabProfile:
                return CieConverter.ForIccLab(icc, conversion);
            case PdfIccBasedColorSpace icc:
                PdfColorSpace alternate = icc.Alternate;
                return new IccAlternateConverter(icc, alternate, CreateConverter(alternate, conversion));
            default:
                throw new ArgumentException($"Colour management converts device, CIE-based and ICCBased spaces, not {source.Family}.", nameof(source));
        }
    }
}
