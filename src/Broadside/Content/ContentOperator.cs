using Broadside.Objects;

namespace Broadside.Content;

/// <summary>
/// One operator as it appears in the content stream, with its operands and where it is, reported before it executes. Valid only
/// during the <see cref="ContentProcessor.VisitOperator"/> callback.
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.8.2. Every operator is reported, unknown ones and those inside compatibility sections included, so an editor can
/// map semantic events back to bytes. Operands are those written before the operator, all of them: an operator given too many uses
/// the last ones it takes. An inline image (§8.9.7) is one operator, <see cref="ContentOperatorCode.BeginInlineImage"/>, whose operand
/// is its dictionary and whose <see cref="Data"/> is its image data.
/// </remarks>
public readonly ref struct ContentOperator
{
    /// <summary>Gets the operator, or <see cref="ContentOperatorCode.Unknown"/>.</summary>
    public ContentOperatorCode Code { get; internal init; }

    /// <summary>Gets the keyword as written, such as <c>cm</c>; for a glued keyword such as <c>q1</c>, only the operator's part.</summary>
    public ReadOnlySpan<byte> Keyword { get; internal init; }

    /// <summary>Gets the operands written before the operator.</summary>
    public ContentOperands Operands { get; internal init; }

    /// <summary>Gets the data of an inline image; empty for any other operator.</summary>
    public ReadOnlySpan<byte> Data { get; internal init; }

    /// <summary>Gets the content stream the operator is in, or <see langword="null"/> when that stream is a direct object.</summary>
    public CosReference? Stream { get; internal init; }

    /// <summary>Gets the index of that stream in the page's <c>Contents</c> array; 0 for a single stream.</summary>
    public int PartIndex { get; internal init; }

    /// <summary>Gets the offset in the stream's decoded data where the operator's first operand, or its keyword when it has none, begins.</summary>
    public int Offset { get; internal init; }

    /// <summary>Gets the number of bytes from <see cref="Offset"/> through the end of the keyword (through <c>EI</c> for an inline image).</summary>
    public int Length { get; internal init; }

    /// <summary>Gets the offset of the keyword in the stream's decoded data.</summary>
    public int KeywordOffset { get; internal init; }
}
