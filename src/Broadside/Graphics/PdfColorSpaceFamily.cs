namespace Broadside.Graphics;

/// <summary>The eleven colour space families of PDF.</summary>
/// <remarks>ISO 32000-2 §8.6.3, Table 61. <c>CalCMYK</c>, removed from PDF, is read as <see cref="DeviceCmyk"/> (§8.6.5.1).</remarks>
public enum PdfColorSpaceFamily
{
    /// <summary><c>DeviceGray</c> (§8.6.4.2).</summary>
    DeviceGray,

    /// <summary><c>DeviceRGB</c> (§8.6.4.3).</summary>
    DeviceRgb,

    /// <summary><c>DeviceCMYK</c> (§8.6.4.4).</summary>
    DeviceCmyk,

    /// <summary><c>CalGray</c> (§8.6.5.2).</summary>
    CalGray,

    /// <summary><c>CalRGB</c> (§8.6.5.3).</summary>
    CalRgb,

    /// <summary><c>Lab</c> (§8.6.5.4).</summary>
    Lab,

    /// <summary><c>ICCBased</c> (§8.6.5.5).</summary>
    IccBased,

    /// <summary><c>Indexed</c> (§8.6.6.3).</summary>
    Indexed,

    /// <summary><c>Pattern</c> (§8.6.6.2).</summary>
    Pattern,

    /// <summary><c>Separation</c> (§8.6.6.4).</summary>
    Separation,

    /// <summary><c>DeviceN</c> (§8.6.6.5).</summary>
    DeviceN,
}
