namespace Broadside.Annotations;

/// <summary>The device colour space of a <see cref="PdfDeviceColor"/>, selected by its number of components.</summary>
/// <remarks>ISO 32000-2 §12.5.2, Table 166 (<c>C</c>), and §8.6.4.</remarks>
public enum PdfDeviceColorSpace
{
    /// <summary>No components: no colour, transparent.</summary>
    None,

    /// <summary>One component: DeviceGray (§8.6.4.2).</summary>
    Gray,

    /// <summary>Three components: DeviceRGB (§8.6.4.3).</summary>
    Rgb,

    /// <summary>Four components: DeviceCMYK (§8.6.4.4).</summary>
    Cmyk,
}
