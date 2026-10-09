namespace Broadside.Graphics;

/// <summary>The device colour model a colour is converted to.</summary>
/// <remarks>ISO 32000-2 §10.2 and §10.4: an output device's native colour space is gray, RGB or CMYK.</remarks>
public enum DeviceColorModel
{
    /// <summary>Additive red, green and blue, as for a display (three components, 0 to 1); the default.</summary>
    Rgb,

    /// <summary>A single gray level (one component, 0 black to 1 white).</summary>
    Gray,

    /// <summary>Subtractive cyan, magenta, yellow and black (four components, 0 to 1).</summary>
    Cmyk,
}
