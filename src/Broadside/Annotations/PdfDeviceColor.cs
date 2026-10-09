using System.Globalization;

namespace Broadside.Annotations;

/// <summary>
/// A colour given as an array of 0, 1, 3 or 4 numbers whose count selects the device colour space, as annotation entries such as
/// <c>C</c> and <c>IC</c> and appearance characteristics such as <c>BC</c> and <c>BG</c> give it.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.5.2, Table 166 (<c>C</c>): 0 elements is no colour (transparent), 1 DeviceGray, 3 DeviceRGB, 4 DeviceCMYK, each
/// component from 0.0 to 1.0. The components are kept as the file gives them, never clamped. A snapshot: reading the entry again
/// returns a new instance.
/// </remarks>
public sealed class PdfDeviceColor : IEquatable<PdfDeviceColor>
{
    private readonly double[] _components;

    /// <summary>Initializes a new instance of the <see cref="PdfDeviceColor"/> class.</summary>
    /// <param name="components">0, 1, 3 or 4 components.</param>
    /// <exception cref="ArgumentException"><paramref name="components"/> has 2 or more than 4 elements.</exception>
    public PdfDeviceColor(params ReadOnlySpan<double> components)
    {
        ColorSpace = components.Length switch
        {
            0 => PdfDeviceColorSpace.None,
            1 => PdfDeviceColorSpace.Gray,
            3 => PdfDeviceColorSpace.Rgb,
            4 => PdfDeviceColorSpace.Cmyk,
            _ => throw new ArgumentException("A device colour has 0, 1, 3 or 4 components.", nameof(components)),
        };
        _components = components.ToArray();
    }

    /// <summary>Gets the colour space the number of components selects.</summary>
    public PdfDeviceColorSpace ColorSpace { get; }

    /// <summary>Gets the components, in the order of the colour space (gray; red, green, blue; cyan, magenta, yellow, black).</summary>
    public IReadOnlyList<double> Components => _components;

    /// <summary>Gets a value indicating whether this is no colour at all (an empty array): transparent.</summary>
    public bool IsTransparent => ColorSpace == PdfDeviceColorSpace.None;

    /// <inheritdoc/>
    public bool Equals(PdfDeviceColor? other) => other is not null && _components.AsSpan().SequenceEqual(other._components);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as PdfDeviceColor);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (double component in _components)
        {
            hash.Add(component);
        }

        return hash.ToHashCode();
    }

    /// <summary>Returns the colour as a PDF array would write it, such as <c>[1 0 0]</c>.</summary>
    /// <returns>The colour text.</returns>
    public override string ToString() => "[" + string.Join(' ', _components.Select(component => component.ToString(CultureInfo.InvariantCulture))) + "]";
}
