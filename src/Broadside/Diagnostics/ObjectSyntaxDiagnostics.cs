namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about object syntax: the repairs the parser makes while reading objects.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.2, §7.3. Each code is reported under <see cref="DiagnosticArea.ObjectSyntax"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class ObjectSyntaxDiagnostics
{
    /// <summary>Gets the <c>UnexpectedEndOfInput</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode UnexpectedEndOfInput { get; } = new(nameof(UnexpectedEndOfInput), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3",
        "The input ends where an object was expected. The missing object is read as null or omitted.");

    /// <summary>Gets the <c>UnexpectedToken</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode UnexpectedToken { get; } = new(nameof(UnexpectedToken), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3",
        "A token that cannot start an object appears where an object is expected. The token is skipped.");

    /// <summary>Gets the <c>TrailingContent</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode TrailingContent { get; } = new(nameof(TrailingContent), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3",
        "Extra content follows an object where the input should end. The trailing content is ignored.");

    /// <summary>Gets the <c>NumberMalformed</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode NumberMalformed { get; } = new(nameof(NumberMalformed), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3",
        "A number is not made of digits with an optional sign and at most one period. The number is read as far as it is valid.");

    /// <summary>Gets the <c>NumberOutOfRange</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode NumberOutOfRange { get; } = new(nameof(NumberOutOfRange), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3",
        "A number is too large to represent. It is clamped to the largest finite value.");

    /// <summary>Gets the <c>ReferenceInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode ReferenceInvalid { get; } = new(nameof(ReferenceInvalid), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3",
        "An indirect reference has a non-positive object number or a generation number outside 0 to 65535. The reference is read as null.");

    /// <summary>Gets the <c>StringUnterminated</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode StringUnterminated { get; } = new(nameof(StringUnterminated), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3",
        "A literal string has no closing parenthesis. The string runs to the end of the input.");

    /// <summary>Gets the <c>HexStringUnterminated</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode HexStringUnterminated { get; } = new(nameof(HexStringUnterminated), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3",
        "A hexadecimal string has no closing angle bracket. The string runs to the end of the input.");

    /// <summary>Gets the <c>HexStringInvalidDigit</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode HexStringInvalidDigit { get; } = new(nameof(HexStringInvalidDigit), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3",
        "A hexadecimal string contains a byte that is neither a hexadecimal digit nor white-space. The byte is ignored.");

    /// <summary>Gets the <c>NameInvalidEscape</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode NameInvalidEscape { get; } = new(nameof(NameInvalidEscape), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3",
        "A number sign in a name is not followed by two hexadecimal digits. It is read as a literal number sign.");

    /// <summary>Gets the <c>NameContainsNull</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode NameContainsNull { get; } = new(nameof(NameContainsNull), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3",
        "A name contains the byte 0, which is not allowed. The byte is dropped.");

    /// <summary>Gets the <c>ArrayUnterminated</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode ArrayUnterminated { get; } = new(nameof(ArrayUnterminated), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3",
        "An array has no closing bracket. The array ends at the end of the input or at the next keyword.");

    /// <summary>Gets the <c>DictionaryUnterminated</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode DictionaryUnterminated { get; } = new(nameof(DictionaryUnterminated), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3",
        "A dictionary has no closing angle brackets. The dictionary ends at that point.");

    /// <summary>Gets the <c>DictionaryKeyNotName</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode DictionaryKeyNotName { get; } = new(nameof(DictionaryKeyNotName), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3",
        "A dictionary key is not a name. The offending object is skipped.");

    /// <summary>Gets the <c>DictionaryValueMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode DictionaryValueMissing { get; } = new(nameof(DictionaryValueMissing), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3",
        "The last dictionary key has no value. The key is dropped.");

    /// <summary>Gets the <c>DictionaryDuplicateKey</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode DictionaryDuplicateKey { get; } = new(nameof(DictionaryDuplicateKey), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3",
        "A dictionary has two entries with the same key. The last entry is kept.");

    /// <summary>Gets the <c>NestingTooDeep</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode NestingTooDeep { get; } = new(nameof(NestingTooDeep), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3",
        "Arrays and dictionaries nest deeper than the allowed depth. The inner container is read as null.");

    /// <summary>Gets the <c>StreamKeywordEndOfLine</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode StreamKeywordEndOfLine { get; } = new(nameof(StreamKeywordEndOfLine), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3",
        "The stream keyword is not followed by CRLF or LF as required. The data is read from the position after the keyword.");

    /// <summary>Gets the <c>StreamLengthInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode StreamLengthInvalid { get; } = new(nameof(StreamLengthInvalid), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3",
        "The stream Length entry is missing or does not match the data. The data is taken to run up to the endstream keyword.");

    /// <summary>Gets the <c>EndstreamMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode EndstreamMissing { get; } = new(nameof(EndstreamMissing), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3",
        "A stream has no endstream keyword. The stream data is read as far as it can be determined.");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        UnexpectedEndOfInput,
        UnexpectedToken,
        TrailingContent,
        NumberMalformed,
        NumberOutOfRange,
        ReferenceInvalid,
        StringUnterminated,
        HexStringUnterminated,
        HexStringInvalidDigit,
        NameInvalidEscape,
        NameContainsNull,
        ArrayUnterminated,
        DictionaryUnterminated,
        DictionaryKeyNotName,
        DictionaryValueMissing,
        DictionaryDuplicateKey,
        NestingTooDeep,
        StreamKeywordEndOfLine,
        StreamLengthInvalid,
        EndstreamMissing,
    ];
}
