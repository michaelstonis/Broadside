namespace Broadside.Graphics;

/// <summary>
/// The colour-management extension point: creates the converters that turn colours of device, CIE-based and ICCBased spaces into
/// device colours. The managed default is <see cref="ManagedColorManagement"/>; replace it with
/// <see cref="PdfOptions.UseColorManagement"/>, for instance with an ICC engine or a decorator over the default.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.6.5 (CIE-based spaces and ICC profiles), §10.3 (CIE-based to device colour, per ICC.1:2010) and §10.4 (device to
/// device). The library expands the special spaces itself before calling an implementation: an Indexed colour becomes its base
/// colour, a Separation or DeviceN tint goes through its tint transform to the alternate space, default colour spaces replace device
/// spaces. An implementation therefore only ever receives a source of the families DeviceGray, DeviceRGB, DeviceCMYK, CalGray,
/// CalRGB, Lab and ICCBased, with every component already clipped into its range.
/// </para>
/// <para>
/// Converters are requested once per document, source space and conversion, and then used from any number of threads at once.
/// </para>
/// </remarks>
public interface IColorManagement
{
    /// <summary>Creates a converter from <paramref name="source"/> to the device colour model of <paramref name="conversion"/>.</summary>
    /// <param name="source">The source space: a device, CIE-based or ICCBased space.</param>
    /// <param name="conversion">The target model, rendering intent and device controls.</param>
    /// <returns>
    /// A converter taking <see cref="PdfColorSpace.ComponentCount"/> values per colour and giving 1, 3 or 4 (gray, RGB, CMYK); immutable
    /// and safe to use from several threads at once.
    /// </returns>
    IColorConverter CreateConverter(PdfColorSpace source, ColorConversion conversion);
}
