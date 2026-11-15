using Broadside.Graphics;

namespace Broadside.Tests.Graphics;

/// <summary>
/// <see cref="PdfColor.FromHex"/> (issue #397, C1): CSS-style hexadecimal notation to a DeviceRGB colour (ISO 32000-2 §8.6.4.3), with
/// alpha refused because opacity is a graphics state parameter in PDF (§11.6.4.4).
/// </summary>
public sealed class PdfColorHexTests
{
    [Theory]
    [InlineData("#0B5FFF", 0x0B, 0x5F, 0xFF)]
    [InlineData("#0b5fff", 0x0B, 0x5F, 0xFF)]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#FFFFFF", 255, 255, 255)]
    [InlineData("#F0A", 0xFF, 0x00, 0xAA)]
    [InlineData("#B42318FF", 0xB4, 0x23, 0x18)]
    public void Hex_notation_is_a_DeviceRGB_colour_of_each_byte_over_255(string hex, int red, int green, int blue)
    {
        PdfColor color = PdfColor.FromHex(hex);

        Assert.Same(PdfDeviceRgbColorSpace.Instance, color.ColorSpace);
        Assert.Equal(PdfColor.FromRgb(red / 255f, green / 255f, blue / 255f), color);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("0B5FFF")]
    [InlineData("#0B5FF")]
    [InlineData("#0B5FFG")]
    [InlineData("#0B5FFF0")]
    [InlineData(" #0B5FFF")]
    public void Anything_else_is_refused(string hex)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => PdfColor.FromHex(hex));

        Assert.Equal("hex", error.ParamName);
    }

    [Fact]
    public void An_alpha_other_than_FF_is_refused_with_a_pointer_to_opacity()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => PdfColor.FromHex("#0B5FFF80"));

        Assert.Contains("SetFillAlpha", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Null_is_refused() => Assert.Throws<ArgumentNullException>(() => PdfColor.FromHex(null!));
}
