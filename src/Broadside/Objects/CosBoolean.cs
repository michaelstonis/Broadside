namespace Broadside.Objects;

/// <summary>A boolean object: <c>true</c> or <c>false</c>. There are exactly two instances, <see cref="True"/> and <see cref="False"/>.</summary>
/// <remarks>ISO 32000-2 §7.3.2.</remarks>
public sealed class CosBoolean : CosObject, IEquatable<CosBoolean>
{
    private CosBoolean(bool value) => Value = value;

    /// <summary>Gets the <c>true</c> object.</summary>
    public static CosBoolean True { get; } = new(value: true);

    /// <summary>Gets the <c>false</c> object.</summary>
    public static CosBoolean False { get; } = new(value: false);

    /// <summary>Gets the value.</summary>
    public bool Value { get; }

    /// <inheritdoc/>
    public bool Equals(CosBoolean? other) => other is not null && other.Value == Value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as CosBoolean);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();
}
