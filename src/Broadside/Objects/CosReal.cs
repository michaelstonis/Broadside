namespace Broadside.Objects;

/// <summary>A real object, held as an IEEE 754 double.</summary>
/// <remarks>
/// ISO 32000-2 §7.3.3 and Annex C. PDF has no exponent notation and no infinities or NaN, so only finite values are allowed. A real
/// is always written with a decimal point and without an exponent, so it parses back as a real and never as an integer.
/// </remarks>
public sealed class CosReal : CosNumber, IEquatable<CosReal>
{
    /// <summary>Initializes a new instance of the <see cref="CosReal"/> class.</summary>
    /// <param name="value">The value. Must be finite.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is infinite or NaN.</exception>
    public CosReal(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A PDF real number must be finite.");
        }

        Value = value;
    }

    /// <summary>Gets the value.</summary>
    public double Value { get; }

    /// <inheritdoc/>
    public override double ToDouble() => Value;

    /// <inheritdoc/>
    public bool Equals(CosReal? other) => other is not null && other.Value.Equals(Value);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as CosReal);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();
}
