namespace Broadside.Parsing;

/// <summary>Diagnostic codes of the font resolver: substitution for fonts that are not embedded (issue #59).</summary>
internal static partial class DiagnosticCodes
{
    // Font substitution (§9.6.2.2, §9.8.1, §9.8.2). Information: which fonts a machine has is not a deviation of the file.
    public const string FontSubstituted = nameof(FontSubstituted);
    public const string FontProgramNotFound = nameof(FontProgramNotFound);
    public const string FontSubstituteUnreadable = nameof(FontSubstituteUnreadable);
}
