using Broadside.Graphics.Colors;

namespace Broadside.Graphics;

/// <summary>The DeviceCMYK colour space: cyan, magenta, yellow and black, each 0 to 1.</summary>
/// <remarks>
/// ISO 32000-2 §8.6.4.4. Its initial colour is 0 0 0 1 (black). A <c>CalCMYK</c> space, removed from PDF, is read as this one
/// (§8.6.5.1).
/// </remarks>
public sealed class PdfDeviceCmykColorSpace : PdfColorSpace
{
    private PdfDeviceCmykColorSpace()
        : base(null, ColorSpaceNames.DeviceCmyk, null)
    {
    }

    /// <summary>Gets the one instance.</summary>
    public static PdfDeviceCmykColorSpace Instance { get; } = new();

    /// <inheritdoc/>
    public override PdfColorSpaceFamily Family => PdfColorSpaceFamily.DeviceCmyk;

    /// <inheritdoc/>
    public override int ComponentCount => 4;

    /// <inheritdoc/>
    private protected override double GetInitialComponent(int index) => index == 3 ? 1 : 0;
}
