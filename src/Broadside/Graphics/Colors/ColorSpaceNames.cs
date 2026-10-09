using Broadside.Objects;

namespace Broadside.Graphics.Colors;

/// <summary>The names colour spaces and their dictionaries use (ISO 32000-2 §8.6, Tables 61 to 72), created once.</summary>
/// <remarks>Kept out of <see cref="KnownNames"/> so that the content tickets editing it in parallel do not collide.</remarks>
internal static class ColorSpaceNames
{
    public static readonly CosName DeviceGray = new("DeviceGray");
    public static readonly CosName DeviceRgb = new("DeviceRGB");
    public static readonly CosName DeviceCmyk = new("DeviceCMYK");
    public static readonly CosName CalGray = new("CalGray");
    public static readonly CosName CalRgb = new("CalRGB");
    public static readonly CosName CalCmyk = new("CalCMYK");
    public static readonly CosName Lab = new("Lab");
    public static readonly CosName IccBased = new("ICCBased");
    public static readonly CosName Indexed = new("Indexed");
    public static readonly CosName Pattern = new("Pattern");
    public static readonly CosName Separation = new("Separation");
    public static readonly CosName DeviceN = new("DeviceN");

    // Inline image abbreviations (§8.9.7, Table 92).
    public static readonly CosName G = new("G");
    public static readonly CosName Rgb = new("RGB");
    public static readonly CosName Cmyk = new("CMYK");
    public static readonly CosName I = new("I");

    public static readonly CosName WhitePoint = new("WhitePoint");
    public static readonly CosName BlackPoint = new("BlackPoint");
    public static readonly CosName Gamma = new("Gamma");
    public static readonly CosName Matrix = new("Matrix");
    public static readonly CosName Range = new("Range");
    public static readonly CosName N = new("N");
    public static readonly CosName Alternate = new("Alternate");
    public static readonly CosName Metadata = new("Metadata");
    public static readonly CosName All = new("All");
    public static readonly CosName None = new("None");
    public static readonly CosName Subtype = new("Subtype");
    public static readonly CosName NChannel = new("NChannel");
    public static readonly CosName Colorants = new("Colorants");
    public static readonly CosName Process = new("Process");
    public static readonly CosName ColorSpace = new("ColorSpace");
    public static readonly CosName Components = new("Components");
    public static readonly CosName MixingHints = new("MixingHints");
    public static readonly CosName Solidities = new("Solidities");
    public static readonly CosName PrintingOrder = new("PrintingOrder");
    public static readonly CosName DotGain = new("DotGain");
    public static readonly CosName DefaultGray = new("DefaultGray");
    public static readonly CosName DefaultRgb = new("DefaultRGB");
    public static readonly CosName DefaultCmyk = new("DefaultCMYK");
}
