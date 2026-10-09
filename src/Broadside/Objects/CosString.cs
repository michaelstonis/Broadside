namespace Broadside.Objects;

/// <summary>A string object: a sequence of zero or more bytes, written either as a literal <c>(…)</c> or as hexadecimal <c>&lt;…&gt;</c>.</summary>
/// <remarks>
/// ISO 32000-2 §7.3.4. The value is the bytes; the written form is kept in <see cref="IsHexadecimal"/> only so a re-written string
/// keeps the form it was read in, and it takes no part in equality. Whether the bytes are text, ASCII or binary depends on where
/// the string is used (§7.9.2); <see cref="DecodeText"/> reads them as a text string.
/// </remarks>
public sealed class CosString : CosObject, IEquatable<CosString>
{
    private readonly byte[] _bytes;

    /// <summary>Initializes a new instance of the <see cref="CosString"/> class.</summary>
    /// <param name="bytes">The string's bytes, copied.</param>
    /// <param name="hexadecimal"><see langword="true"/> to write the string in hexadecimal form, <see langword="false"/> for literal form.</param>
    public CosString(ReadOnlySpan<byte> bytes, bool hexadecimal = false)
        : this(bytes.ToArray(), hexadecimal)
    {
    }

    private CosString(byte[] bytes, bool hexadecimal)
    {
        _bytes = bytes;
        IsHexadecimal = hexadecimal;
    }

    /// <summary>Gets the string's bytes, after literal escapes or hexadecimal digits are decoded.</summary>
    public ReadOnlySpan<byte> Bytes => _bytes;

    /// <summary>Gets a value indicating whether the string is written in hexadecimal form (§7.3.4.3) rather than literal form (§7.3.4.2).</summary>
    public bool IsHexadecimal { get; }

    /// <summary>Decodes the bytes as a text string.</summary>
    /// <returns>The text.</returns>
    /// <remarks>
    /// ISO 32000-2 §7.9.2.2 and Annex D. A string that starts with the byte order marker <c>FE FF</c> is UTF-16BE, one that starts
    /// with <c>EF BB BF</c> is UTF-8 (PDF 2.0), and any other is PDFDocEncoding (Table D.3). The marker is not part of the text.
    /// Language escape sequences in Unicode strings (§7.9.2.2.2: ESC, a two-letter language code, an optional two-letter country
    /// code, ESC) are removed. Malformed UTF-16 or UTF-8 decodes to U+FFFD; a trailing odd byte of a UTF-16BE string is ignored;
    /// the PDFDocEncoding codes Table D.3 leaves undefined decode to the Unicode code point with the same number.
    /// </remarks>
    public string DecodeText() => TextStringDecoder.Decode(_bytes);

    /// <inheritdoc/>
    public bool Equals(CosString? other) => other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as CosString);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(_bytes);
        return hash.ToHashCode();
    }

    /// <summary>Wraps an array the caller gives up ownership of, without copying it.</summary>
    internal static CosString FromOwnedBytes(byte[] bytes, bool hexadecimal) => new(bytes, hexadecimal);
}
