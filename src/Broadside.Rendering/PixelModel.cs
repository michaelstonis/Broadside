using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// What the BGRA pixels the rasterizer paints into hold: RGB (the page), gray (a gray transparency group, the level in all three
/// channels) or one plane of CMYK (a CMYK group: the complements of cyan, magenta and yellow in pass 0, of black in pass 1).
/// </summary>
/// <remarks>
/// ISO 32000-2 §11.3.4: colours are blended in the blending colour space; in a subtractive space the components are complemented
/// first and the results complemented back, so storing complements lets every blend function work unchanged. §11.3.5.3: for CMYK
/// the non-separable modes apply to the complemented C, M, Y as RGB, and K takes the backdrop's (Hue, Saturation, Color) or the
/// source's (Luminosity): exactly what the same formulas give on a plane where all three channels hold the complement of K.
/// </remarks>
internal readonly record struct PixelModel(DeviceColorModel Model, int Pass)
{
    /// <summary>Gets the model of the page: RGB.</summary>
    public static PixelModel Rgb => default;

    /// <summary>Gets the model colours are converted to before <see cref="Pack(ReadOnlySpan{float})"/>.</summary>
    public DeviceColorModel Target => Model == DeviceColorModel.Cmyk ? DeviceColorModel.Cmyk : DeviceColorModel.Rgb;

    /// <summary>Gets the number of components of a colour in <see cref="Target"/>.</summary>
    public int Components => Model == DeviceColorModel.Cmyk ? 4 : 3;

    /// <summary>Returns <paramref name="conversion"/> with <see cref="Target"/> as its target.</summary>
    public ColorConversion Convert(ColorConversion conversion) => conversion with { Target = Target };

    /// <summary>Packs a colour of <see cref="Target"/>, components 0 to 1, as the opaque BGRA pixel this model stores.</summary>
    public uint Pack(ReadOnlySpan<float> components) => Model switch
    {
        DeviceColorModel.Gray => Gray(Channel(components[0]), Channel(components[1]), Channel(components[2])),
        DeviceColorModel.Cmyk => Pass == 0
            ? Bgra(255 - Channel(components[0]), 255 - Channel(components[1]), 255 - Channel(components[2]))
            : Bgra(255 - Channel(components[3]), 255 - Channel(components[3]), 255 - Channel(components[3])),
        _ => Bgra(Channel(components[0]), Channel(components[1]), Channel(components[2])),
    };

    /// <summary>Packs a colour of <see cref="Target"/>, components 0 to 255, as the opaque BGRA pixel this model stores.</summary>
    public uint Pack(ReadOnlySpan<byte> components) => Model switch
    {
        DeviceColorModel.Gray => Gray(components[0], components[1], components[2]),
        DeviceColorModel.Cmyk => Pass == 0
            ? Bgra(255 - components[0], 255 - components[1], 255 - components[2])
            : Bgra(255 - components[3], 255 - components[3], 255 - components[3]),
        _ => Bgra(components[0], components[1], components[2]),
    };

    private static uint Gray(int r, int g, int b)
    {
        int level = (int)((0.3f * r) + (0.59f * g) + (0.11f * b) + 0.5f);
        return Bgra(level, level, level);
    }

    private static uint Bgra(int r, int g, int b) => (uint)b | ((uint)g << 8) | ((uint)r << 16) | 0xFF000000u;

    private static int Channel(float value) => (int)((Math.Clamp(value, 0f, 1f) * 255f) + 0.5f);
}
