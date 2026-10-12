namespace Broadside.Objects;

/// <summary>An integer object.</summary>
/// <remarks>
/// ISO 32000-2 §7.3.3. The range is that of <see cref="long"/>; a parser reads an integer literal beyond that range as a
/// <see cref="CosReal"/> (Annex C lets a processor limit the range).
/// </remarks>
/// <param name="value">The value.</param>
public sealed class CosInteger(long value) : CosNumber, IEquatable<CosInteger>
{
    /// <summary>Gets the value.</summary>
    public long Value { get; } = value;

    /// <inheritdoc/>
    public override double ToDouble() => Value;

    /// <inheritdoc/>
    public bool Equals(CosInteger? other) => other is not null && other.Value == Value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as CosInteger);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();
}
