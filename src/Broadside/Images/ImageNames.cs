using Broadside.Objects;

namespace Broadside.Images;

/// <summary>The names the image model looks up, created once (ISO 32000-2 §8.9.5 Table 87, §8.9.7 Tables 91-92, §11.6.5.2 Table 144).</summary>
internal static class ImageNames
{
    public static readonly CosName Type = new("Type");
    public static readonly CosName Subtype = new("Subtype");
    public static readonly CosName Image = new("Image");
    public static readonly CosName Width = new("Width");
    public static readonly CosName Height = new("Height");
    public static readonly CosName ColorSpace = new("ColorSpace");
    public static readonly CosName BitsPerComponent = new("BitsPerComponent");
    public static readonly CosName Intent = new("Intent");
    public static readonly CosName ImageMask = new("ImageMask");
    public static readonly CosName Mask = new("Mask");
    public static readonly CosName Decode = new("Decode");
    public static readonly CosName Interpolate = new("Interpolate");
    public static readonly CosName Alternates = new("Alternates");
    public static readonly CosName SMask = new("SMask");
    public static readonly CosName SMaskInData = new("SMaskInData");
    public static readonly CosName Name = new("Name");
    public static readonly CosName StructParent = new("StructParent");
    public static readonly CosName Metadata = new("Metadata");
    public static readonly CosName OC = new("OC");
    public static readonly CosName Matte = new("Matte");
    public static readonly CosName DefaultForPrinting = new("DefaultForPrinting");
    public static readonly CosName Filter = new("Filter");
    public static readonly CosName DecodeParms = new("DecodeParms");
    public static readonly CosName Length = new("Length");
    public static readonly CosName DeviceGray = new("DeviceGray");
    public static readonly CosName DeviceRgb = new("DeviceRGB");
    public static readonly CosName DeviceCmyk = new("DeviceCMYK");
    public static readonly CosName Indexed = new("Indexed");
    public static readonly CosName IccBased = new("ICCBased");
    public static readonly CosName N = new("N");
    public static readonly CosName XObject = new("XObject");

    // Table 91 abbreviated keys.
    public static readonly CosName W = new("W");
    public static readonly CosName H = new("H");
    public static readonly CosName Bpc = new("BPC");
    public static readonly CosName CS = new("CS");
    public static readonly CosName D = new("D");
    public static readonly CosName DP = new("DP");
    public static readonly CosName F = new("F");
    public static readonly CosName IM = new("IM");
    public static readonly CosName I = new("I");
    public static readonly CosName L = new("L");
}
