namespace Broadside.Graphics;

/// <summary>Converts colours from one source space to a device colour model; made by an <see cref="IColorManagement"/>.</summary>
/// <remarks>
/// ISO 32000-2 §10.3 and §10.4. Implementations are immutable and thread-safe, and should not allocate per call: the library calls
/// <see cref="Convert"/> once per run of colours (an image row, a batch of tints), never per pixel.
/// </remarks>
public interface IColorConverter
{
    /// <summary>Gets the number of components of a source colour.</summary>
    int InputCount { get; }

    /// <summary>Gets the number of components of a device colour: 1 for gray, 3 for RGB, 4 for CMYK, each 0 to 1.</summary>
    int OutputCount { get; }

    /// <summary>Converts <paramref name="count"/> colours stored one after the other.</summary>
    /// <param name="source">At least <paramref name="count"/> × <see cref="InputCount"/> component values, each within its range.</param>
    /// <param name="destination">At least <paramref name="count"/> × <see cref="OutputCount"/> values; receives the device colours.</param>
    /// <param name="count">The number of colours.</param>
    void Convert(ReadOnlySpan<float> source, Span<float> destination, int count);
}
