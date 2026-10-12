using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Broadside.Objects;

namespace Broadside.Graphics;

/// <summary>
/// A colour: a colour space and one number per component, as a colour operator sets it; for the Pattern colour space, also the
/// pattern. A value type with its components stored inline, so setting a colour allocates nothing.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.6.2 (colour values), §8.6.8 (colour operators, Table 73) and §8.7.3 (patterns). The components are kept as the
/// content stream gave them; conversion (<see cref="PdfColorConverter"/>) clips each into its component's range. The colour space is
/// the one the operator selected: a <c>DefaultRGB</c> resource replaces DeviceRGB only when the colour is painted (§8.6.5.6), through
/// <see cref="PdfDefaultColorSpaces"/>.
/// </para>
/// <para>
/// A colour in the Pattern colour space names its pattern (<see cref="PatternName"/>, <see cref="Pattern"/>) and carries the matrix
/// of the content stream that named it (<see cref="PatternMatrix"/>), the space the pattern matrix maps into (§8.7.2); for an
/// uncoloured tiling pattern the components are in the underlying colour space (§8.7.3.3).
/// </para>
/// </remarks>
public readonly struct PdfColor : IEquatable<PdfColor>
{
    /// <summary>The most components a colour stores; a DeviceN colour space with more is painted through its alternate.</summary>
    /// <remarks>ISO 32000-2 Annex C, Table C.1: earlier PDF versions limited DeviceN to 32 colourants.</remarks>
    public const int MaxComponents = 32;

    private readonly PdfColorSpace? _colorSpace;
    private readonly ComponentBuffer _components;

    /// <summary>Initializes a new instance of the <see cref="PdfColor"/> struct.</summary>
    /// <param name="colorSpace">The colour space.</param>
    /// <param name="components">The component values; values are not clipped.</param>
    /// <exception cref="ArgumentException">There are more than <see cref="MaxComponents"/> components.</exception>
    public PdfColor(PdfColorSpace colorSpace, ReadOnlySpan<float> components)
    {
        ArgumentNullException.ThrowIfNull(colorSpace);
        if (components.Length > MaxComponents)
        {
            throw new ArgumentException($"A colour has at most {MaxComponents} components.", nameof(components));
        }

        _colorSpace = colorSpace;
        components.CopyTo(_components);
        ComponentCount = components.Length;
        PatternMatrix = Matrix.Identity;
    }

    /// <summary>Initializes a new instance of the <see cref="PdfColor"/> struct in the Pattern colour space.</summary>
    internal PdfColor(PdfColorSpace colorSpace, ReadOnlySpan<float> components, CosName? patternName, CosObject? pattern, Matrix patternMatrix)
        : this(colorSpace, components)
    {
        PatternName = patternName;
        Pattern = pattern;
        PatternMatrix = patternMatrix;
    }

    /// <summary>Gets the colour space; DeviceGray for a default instance.</summary>
    public PdfColorSpace ColorSpace => _colorSpace ?? PdfDeviceGrayColorSpace.Instance;

    /// <summary>Gets the number of components.</summary>
    public int ComponentCount { get; }

    /// <summary>Gets the component values.</summary>
    [UnscopedRef]
    public ReadOnlySpan<float> Components => ((ReadOnlySpan<float>)_components)[..ComponentCount];

    /// <summary>Gets the name of the pattern in the <c>Pattern</c> subdictionary of the resources, for a Pattern colour.</summary>
    /// <remarks>ISO 32000-2 §8.6.8 (<c>scn</c>): <see langword="null"/> for other colours and for the initial Pattern colour.</remarks>
    public CosName? PatternName { get; }

    /// <summary>Gets the pattern dictionary or stream the name resolved to, or <see langword="null"/> when there is none.</summary>
    /// <remarks>ISO 32000-2 §8.7.3: a pattern colour with no pattern paints nothing (the initial colour of the Pattern space, Table 73).</remarks>
    public CosObject? Pattern { get; }

    /// <summary>
    /// Gets the matrix of the content stream in which the pattern was selected (its CTM at the start of that stream), which the
    /// pattern matrix maps pattern space into; the identity for colours that are not patterns.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.7.2.</remarks>
    public Matrix PatternMatrix { get; }

    /// <summary>Gets the value of component <paramref name="index"/>.</summary>
    /// <param name="index">The component, from 0.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not less than <see cref="ComponentCount"/>.</exception>
    public float this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, ComponentCount);
            return _components[index];
        }
    }

    /// <summary>Compares two colours by space, components and pattern.</summary>
    /// <param name="left">The first colour.</param>
    /// <param name="right">The second colour.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    public static bool operator ==(PdfColor left, PdfColor right) => left.Equals(right);

    /// <summary>Compares two colours by space, components and pattern.</summary>
    /// <param name="left">The first colour.</param>
    /// <param name="right">The second colour.</param>
    /// <returns><see langword="true"/> when different.</returns>
    public static bool operator !=(PdfColor left, PdfColor right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(PdfColor other) =>
        ReferenceEquals(ColorSpace, other.ColorSpace)
        && Components.SequenceEqual(other.Components)
        && Equals(PatternName, other.PatternName)
        && ReferenceEquals(Pattern, other.Pattern)
        && PatternMatrix == other.PatternMatrix;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PdfColor other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(ColorSpace);
        foreach (float component in Components)
        {
            hash.Add(component);
        }

        hash.Add(PatternName);
        return hash.ToHashCode();
    }

    /// <summary>Storage for up to 32 components, so a colour is a value with no heap object behind it.</summary>
    [InlineArray(MaxComponents)]
    private struct ComponentBuffer
    {
        private float _element;
    }
}
