using System.Globalization;

namespace Broadside;

/// <summary>A colour in the DeviceRGB colour space, each component from 0.0 to 1.0, as interactive features such as outline items give it.</summary>
/// <remarks>ISO 32000-2 §8.6.4.3 (DeviceRGB) and §12.3.3, Table 151 (<c>C</c>).</remarks>
public readonly struct PdfRgbColor : IEquatable<PdfRgbColor>
{
    /// <summary>Initializes a new instance of the <see cref="PdfRgbColor"/> struct.</summary>
    /// <param name="red">The red component, from 0.0 to 1.0.</param>
    /// <param name="green">The green component, from 0.0 to 1.0.</param>
    /// <param name="blue">The blue component, from 0.0 to 1.0.</param>
    public PdfRgbColor(double red, double green, double blue)
    {
        Red = red;
        Green = green;
        Blue = blue;
    }

    /// <summary>Gets black, <c>[0 0 0]</c>.</summary>
    public static PdfRgbColor Black => default;

    /// <summary>Gets the red component.</summary>
    public double Red { get; }

    /// <summary>Gets the green component.</summary>
    public double Green { get; }

    /// <summary>Gets the blue component.</summary>
    public double Blue { get; }

    /// <summary>Returns whether two colours have the same components.</summary>
    /// <param name="left">The first colour.</param>
    /// <param name="right">The second colour.</param>
    /// <returns><see langword="true"/> when they are equal.</returns>
    public static bool operator ==(PdfRgbColor left, PdfRgbColor right) => left.Equals(right);

    /// <summary>Returns whether two colours differ.</summary>
    /// <param name="left">The first colour.</param>
    /// <param name="right">The second colour.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    public static bool operator !=(PdfRgbColor left, PdfRgbColor right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(PdfRgbColor other) => Red.Equals(other.Red) && Green.Equals(other.Green) && Blue.Equals(other.Blue);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PdfRgbColor other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Red, Green, Blue);

    /// <summary>Returns the colour as a PDF array would write it, such as <c>[1 0 0]</c>.</summary>
    /// <returns>The colour text.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"[{Red} {Green} {Blue}]");
}
