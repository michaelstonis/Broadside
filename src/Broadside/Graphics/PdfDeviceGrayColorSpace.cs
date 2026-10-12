using Broadside.Graphics.Colors;

namespace Broadside.Graphics;

/// <summary>The DeviceGray colour space: one component, 0 black to 1 white.</summary>
/// <remarks>ISO 32000-2 §8.6.4.2. The initial colour space of the graphics state (Table 51); its initial colour is 0.</remarks>
public sealed class PdfDeviceGrayColorSpace : PdfColorSpace
{
    private PdfDeviceGrayColorSpace()
        : base(null, ColorSpaceNames.DeviceGray, null)
    {
    }

    /// <summary>Gets the one instance.</summary>
    public static PdfDeviceGrayColorSpace Instance { get; } = new();

    /// <inheritdoc/>
    public override PdfColorSpaceFamily Family => PdfColorSpaceFamily.DeviceGray;

    /// <inheritdoc/>
    public override int ComponentCount => 1;
}
