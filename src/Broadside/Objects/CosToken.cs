namespace Broadside.Objects;

/// <summary>The kind of a <see cref="CosToken"/>.</summary>
/// <remarks>ISO 32000-2 §7.2.</remarks>
internal enum CosTokenKind : byte
{
    /// <summary>No more tokens: only white-space and comments remain.</summary>
    EndOfInput,

    /// <summary>A run of regular characters made of digits, signs and periods with no period: an integer, possibly malformed (§7.3.3).</summary>
    Integer,

    /// <summary>A run of regular characters made of digits, signs and periods with a period: a real, possibly malformed (§7.3.3).</summary>
    Real,

    /// <summary><c>(…)</c>, from the opening to the matching closing parenthesis or the end of input (§7.3.4.2).</summary>
    LiteralString,

    /// <summary><c>&lt;…&gt;</c>, from the opening to the closing angle bracket or the end of input (§7.3.4.3).</summary>
    HexString,

    /// <summary><c>/</c> followed by regular characters (§7.3.5).</summary>
    Name,

    /// <summary><c>[</c> (§7.3.6).</summary>
    ArrayStart,

    /// <summary><c>]</c> (§7.3.6).</summary>
    ArrayEnd,

    /// <summary><c>&lt;&lt;</c> (§7.3.7).</summary>
    DictionaryStart,

    /// <summary><c>&gt;&gt;</c> (§7.3.7).</summary>
    DictionaryEnd,

    /// <summary>Any other run of regular characters (<c>true</c>, <c>null</c>, <c>R</c>, <c>obj</c>, <c>stream</c>, garbage), or <c>{</c> or <c>}</c>.</summary>
    Keyword,

    /// <summary>A delimiter that cannot start a token: <c>)</c> or a lone <c>&gt;</c>.</summary>
    Invalid,
}

/// <summary>One token: its kind and the byte range it occupies in the source, delimiters included.</summary>
/// <remarks>ISO 32000-2 §7.2. A token holds no copy of its bytes, so producing one allocates nothing.</remarks>
internal readonly record struct CosToken(CosTokenKind Kind, int Start, int Length)
{
    /// <summary>Gets the offset one past the token's last byte.</summary>
    public int End => Start + Length;
}
