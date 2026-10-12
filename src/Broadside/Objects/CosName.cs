using System.Text;

namespace Broadside.Objects;

/// <summary>A name object: an atomic symbol defined by a sequence of bytes, written <c>/Name</c>.</summary>
/// <remarks>
/// ISO 32000-2 §7.3.5. The identity of a name is its bytes after <c>#xx</c> escapes are expanded: <c>/A#42</c> and <c>/AB</c> are the
/// same name. A name may hold any byte except 0. When a name must be read as text (a font name, a colorant, a structure type) its
/// bytes are UTF-8, which is what <see cref="Value"/> returns.
/// </remarks>
public sealed class CosName : CosObject, IEquatable<CosName>
{
    private readonly byte[] _bytes;
    private readonly int _hashCode;
    private string? _value;

    /// <summary>Initializes a new instance of the <see cref="CosName"/> class from text, encoded as UTF-8.</summary>
    /// <param name="value">The name without the leading solidus and without escapes, for example <c>Type</c> or <c>Lime Green</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="value"/> contains U+0000.</exception>
    public CosName(string value)
        : this(Encoding.UTF8.GetBytes(value ?? throw new ArgumentNullException(nameof(value))), nameof(value))
    {
        _value = value;
    }

    /// <summary>Initializes a new instance of the <see cref="CosName"/> class from its bytes.</summary>
    /// <param name="bytes">The name's bytes without the leading solidus and with escapes expanded; copied.</param>
    /// <exception cref="ArgumentException"><paramref name="bytes"/> contains the byte 0.</exception>
    public CosName(ReadOnlySpan<byte> bytes)
        : this(bytes.ToArray(), nameof(bytes))
    {
    }

    private CosName(byte[] bytes, string parameterName)
    {
        if (bytes.AsSpan().Contains((byte)0))
        {
            throw new ArgumentException("A PDF name cannot contain the byte 0 (ISO 32000-2 §7.3.5).", parameterName);
        }

        _bytes = bytes;
        var hash = new HashCode();
        hash.AddBytes(bytes);
        _hashCode = hash.ToHashCode();
    }

    /// <summary>Gets the name's bytes, without the leading solidus and with <c>#xx</c> escapes expanded.</summary>
    public ReadOnlySpan<byte> Bytes => _bytes;

    /// <summary>Gets the name's bytes decoded as UTF-8; invalid sequences decode to U+FFFD.</summary>
    public string Value => _value ??= Encoding.UTF8.GetString(_bytes);

    /// <inheritdoc/>
    public bool Equals(CosName? other) => other is not null && (ReferenceEquals(this, other) || _bytes.AsSpan().SequenceEqual(other._bytes));

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as CosName);

    /// <inheritdoc/>
    public override int GetHashCode() => _hashCode;

    /// <summary>Wraps an array the caller gives up ownership of, without copying it. The array must not contain 0.</summary>
    internal static CosName FromOwnedBytes(byte[] bytes) => new(bytes, nameof(bytes));
}
