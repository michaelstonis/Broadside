using System.Diagnostics.CodeAnalysis;

namespace Broadside.Content;

/// <summary>The object type of a content stream operand.</summary>
/// <remarks>
/// ISO 32000-2 §7.8.2: an operand is a direct object of any basic type except a stream; indirect references are not permitted
/// (<c>1 0 R</c> reads as the numbers 1 and 0 followed by the unknown operator <c>R</c>).
/// </remarks>
[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "The values are the ISO 32000-2 object type names (§7.3).")]
public enum ContentOperandKind
{
    /// <summary>The null object (§7.3.9).</summary>
    Null,

    /// <summary>A boolean (§7.3.2).</summary>
    Boolean,

    /// <summary>An integer (§7.3.3).</summary>
    Integer,

    /// <summary>A real number (§7.3.3).</summary>
    Real,

    /// <summary>A name (§7.3.5); its bytes are decoded, without the solidus.</summary>
    Name,

    /// <summary>A literal or hexadecimal string (§7.3.4); its bytes are decoded.</summary>
    String,

    /// <summary>An array (§7.3.6), such as the operand of <c>TJ</c> or <c>d</c>.</summary>
    Array,

    /// <summary>A dictionary (§7.3.7), such as the property list of <c>BDC</c> or an inline image's parameters; its items alternate key and value.</summary>
    Dictionary,
}
