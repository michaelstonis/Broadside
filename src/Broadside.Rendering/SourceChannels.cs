namespace Broadside.Rendering;

/// <summary>
/// What the pixels of an <see cref="ImageSource"/> or the table of a <see cref="ShadingSource"/> hold: RGB for display, or, for an
/// overprint preview (ISO 32000-2 §8.6.7, §11.7.4), the process colourants of the colours converted to DeviceCMYK, as additive
/// values (1 − tint) in the red, green and blue channels so the painters resample and composite them like any pixel.
/// </summary>
internal enum SourceChannels
{
    /// <summary>Red, green and blue.</summary>
    Rgb,

    /// <summary>1 − cyan, 1 − magenta and 1 − yellow in red, green and blue.</summary>
    Cmy,

    /// <summary>1 − black in red, green and blue.</summary>
    Black,
}
