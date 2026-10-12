namespace Broadside.Content;

/// <summary>Where one operator of the content a <see cref="ContentEditor"/> rewrites is: its code and its byte range.</summary>
/// <remarks>
/// ISO 32000-2 §7.8.2. The range runs from the operator's first operand (its keyword when it has none) through the end of its
/// keyword, or through <c>EI</c> for an inline image (§8.9.7). Offsets are in <see cref="ContentEditor.Content"/>: the decoded
/// stream, or the streams of a <c>Contents</c> array joined with a line feed after each (Table 31).
/// </remarks>
/// <param name="Code">The operator, or <see cref="ContentOperatorCode.Unknown"/>.</param>
/// <param name="Offset">The offset of the operator's first byte.</param>
/// <param name="Length">The number of bytes the operator spans.</param>
public readonly record struct ContentOperatorRange(ContentOperatorCode Code, int Offset, int Length);
