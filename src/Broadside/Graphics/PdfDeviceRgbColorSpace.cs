using Broadside.Graphics.Colors;

namespace Broadside.Graphics;

/// <summary>The DeviceRGB colour space: red, green and blue, each 0 to 1.</summary>
/// <remarks>ISO 32000-2 §8.6.4.3. Its initial colour is 0 0 0 (black).</remarks>
public sealed class PdfDeviceRgbColorSpace : PdfColorSpace
{
    private PdfDeviceRgbColorSpace()
        : base(null, ColorSpaceNames.DeviceRgb, null)
    {
    }

    /// <summary>Gets the one instance.</summary>
    public static PdfDeviceRgbColorSpace Instance { get; } = new();

    /// <inheritdoc/>
    public override PdfColorSpaceFamily Family => PdfColorSpaceFamily.DeviceRgb;

    /// <inheritdoc/>
    public override int ComponentCount => 3;
}
