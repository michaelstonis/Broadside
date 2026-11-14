namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about object syntax: the repairs the parser makes while reading objects.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.2, §7.3. Each code is reported under <see cref="DiagnosticArea.ObjectSyntax"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class ObjectSyntaxDiagnostics
{
    /// <summary>Gets the <c>UnexpectedEndOfInput</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode UnexpectedEndOfInput { get; } = new(nameof(UnexpectedEndOfInput), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3");

    /// <summary>Gets the <c>UnexpectedToken</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode UnexpectedToken { get; } = new(nameof(UnexpectedToken), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3");

    /// <summary>Gets the <c>TrailingContent</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode TrailingContent { get; } = new(nameof(TrailingContent), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3");

    /// <summary>Gets the <c>NumberMalformed</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode NumberMalformed { get; } = new(nameof(NumberMalformed), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3");

    /// <summary>Gets the <c>NumberOutOfRange</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode NumberOutOfRange { get; } = new(nameof(NumberOutOfRange), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3");

    /// <summary>Gets the <c>ReferenceInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode ReferenceInvalid { get; } = new(nameof(ReferenceInvalid), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3");

    /// <summary>Gets the <c>StringUnterminated</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode StringUnterminated { get; } = new(nameof(StringUnterminated), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3");

    /// <summary>Gets the <c>HexStringUnterminated</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode HexStringUnterminated { get; } = new(nameof(HexStringUnterminated), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3");

    /// <summary>Gets the <c>HexStringInvalidDigit</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode HexStringInvalidDigit { get; } = new(nameof(HexStringInvalidDigit), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3");

    /// <summary>Gets the <c>NameInvalidEscape</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode NameInvalidEscape { get; } = new(nameof(NameInvalidEscape), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3");

    /// <summary>Gets the <c>NameContainsNull</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode NameContainsNull { get; } = new(nameof(NameContainsNull), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3");

    /// <summary>Gets the <c>ArrayUnterminated</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode ArrayUnterminated { get; } = new(nameof(ArrayUnterminated), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3");

    /// <summary>Gets the <c>DictionaryUnterminated</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode DictionaryUnterminated { get; } = new(nameof(DictionaryUnterminated), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3");

    /// <summary>Gets the <c>DictionaryKeyNotName</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode DictionaryKeyNotName { get; } = new(nameof(DictionaryKeyNotName), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3");

    /// <summary>Gets the <c>DictionaryValueMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode DictionaryValueMissing { get; } = new(nameof(DictionaryValueMissing), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3");

    /// <summary>Gets the <c>DictionaryDuplicateKey</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode DictionaryDuplicateKey { get; } = new(nameof(DictionaryDuplicateKey), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3");

    /// <summary>Gets the <c>NestingTooDeep</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode NestingTooDeep { get; } = new(nameof(NestingTooDeep), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3");

    /// <summary>Gets the <c>StreamKeywordEndOfLine</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode StreamKeywordEndOfLine { get; } = new(nameof(StreamKeywordEndOfLine), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3");

    /// <summary>Gets the <c>StreamLengthInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode StreamLengthInvalid { get; } = new(nameof(StreamLengthInvalid), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3");

    /// <summary>Gets the <c>EndstreamMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.2, §7.3</remarks>
    public static DiagnosticCode EndstreamMissing { get; } = new(nameof(EndstreamMissing), DiagnosticArea.ObjectSyntax, DiagnosticSeverity.Warning, "ISO 32000-2 §7.2, §7.3");

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
