namespace Broadside.Objects;

/// <summary>An indirect reference, <c>N G R</c>: the object and generation numbers of an indirect object.</summary>
/// <remarks>
/// ISO 32000-2 §7.3.10. A reference is a value; it does not hold or resolve the object it names. A reference to an object that
/// does not exist resolves to <see cref="CosNull"/>, which is the document's job, not this type's.
/// </remarks>
public sealed class CosReference : CosObject, IEquatable<CosReference>
{
    /// <summary>The largest generation number a cross-reference table can record (five decimal digits, ISO 32000-2 §7.5.4).</summary>
    public const int MaxGeneration = 65535;

    /// <summary>Initializes a new instance of the <see cref="CosReference"/> class.</summary>
    /// <param name="objectNumber">The object number. Must be positive.</param>
    /// <param name="generation">The generation number, from 0 to <see cref="MaxGeneration"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">A number is outside its range.</exception>
    public CosReference(int objectNumber, int generation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(objectNumber);
        ArgumentOutOfRangeException.ThrowIfNegative(generation);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(generation, MaxGeneration);
        ObjectNumber = objectNumber;
        Generation = generation;
    }

    /// <summary>Gets the object number.</summary>
    public int ObjectNumber { get; }

    /// <summary>Gets the generation number.</summary>
    public int Generation { get; }

    /// <inheritdoc/>
    public bool Equals(CosReference? other) => other is not null && other.ObjectNumber == ObjectNumber && other.Generation == Generation;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as CosReference);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(ObjectNumber, Generation);
}
