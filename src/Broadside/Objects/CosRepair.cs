using System.Globalization;

using Broadside.Diagnostics;

namespace Broadside.Objects;

/// <summary>
/// One deviation from ISO 32000-2 §7.2 or §7.3 the parser found, and repaired, while reading objects.
/// </summary>
/// <remarks>
/// The object loader turns each repair into a public <see cref="Diagnostics.Diagnostic"/> with the same <see cref="Code"/>, the
/// absolute offset and the object (issue #41). Codes are stable PascalCase identifiers, listed in <see cref="CosRepairCodes"/>.
/// </remarks>
/// <param name="Code">The stable identifier of the kind of deviation.</param>
/// <param name="Offset">The byte offset in the parsed source where the deviation was found.</param>
/// <param name="Message">A human-readable description of the deviation and the repair.</param>
internal readonly record struct CosRepair(string Code, int Offset, string Message)
{
    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Code} at offset {Offset}: {Message}");
}

/// <summary>The codes of <see cref="CosRepair"/>, one per kind of deviation.</summary>
internal static class CosRepairCodes
{
    public static string UnexpectedEndOfInput => ObjectSyntaxDiagnostics.UnexpectedEndOfInput.Name;
    public static string UnexpectedToken => ObjectSyntaxDiagnostics.UnexpectedToken.Name;
    public static string TrailingContent => ObjectSyntaxDiagnostics.TrailingContent.Name;
    public static string NumberMalformed => ObjectSyntaxDiagnostics.NumberMalformed.Name;
    public static string NumberOutOfRange => ObjectSyntaxDiagnostics.NumberOutOfRange.Name;
    public static string ReferenceInvalid => ObjectSyntaxDiagnostics.ReferenceInvalid.Name;
    public static string StringUnterminated => ObjectSyntaxDiagnostics.StringUnterminated.Name;
    public static string HexStringUnterminated => ObjectSyntaxDiagnostics.HexStringUnterminated.Name;
    public static string HexStringInvalidDigit => ObjectSyntaxDiagnostics.HexStringInvalidDigit.Name;
    public static string NameInvalidEscape => ObjectSyntaxDiagnostics.NameInvalidEscape.Name;
    public static string NameContainsNull => ObjectSyntaxDiagnostics.NameContainsNull.Name;
    public static string ArrayUnterminated => ObjectSyntaxDiagnostics.ArrayUnterminated.Name;
    public static string DictionaryUnterminated => ObjectSyntaxDiagnostics.DictionaryUnterminated.Name;
    public static string DictionaryKeyNotName => ObjectSyntaxDiagnostics.DictionaryKeyNotName.Name;
    public static string DictionaryValueMissing => ObjectSyntaxDiagnostics.DictionaryValueMissing.Name;
    public static string DictionaryDuplicateKey => ObjectSyntaxDiagnostics.DictionaryDuplicateKey.Name;
    public static string NestingTooDeep => ObjectSyntaxDiagnostics.NestingTooDeep.Name;
    public static string StreamKeywordEndOfLine => ObjectSyntaxDiagnostics.StreamKeywordEndOfLine.Name;
    public static string StreamLengthInvalid => ObjectSyntaxDiagnostics.StreamLengthInvalid.Name;
    public static string EndstreamMissing => ObjectSyntaxDiagnostics.EndstreamMissing.Name;
}

/// <summary>Collects the <see cref="CosRepair"/> records a parse produces.</summary>
/// <param name="keepAll"><see langword="true"/> to keep every repair in <see cref="All"/>; otherwise only the first and the count are kept.</param>
internal sealed class CosRepairLog(bool keepAll = false)
{
    private List<CosRepair>? _all;

    /// <summary>Gets the first repair, or <see langword="null"/> when there was none.</summary>
    public CosRepair? First { get; private set; }

    /// <summary>Gets the number of repairs.</summary>
    public int Count { get; private set; }

    /// <summary>Gets every repair, in order, when the log keeps them all; otherwise only the first.</summary>
    public IReadOnlyList<CosRepair> All => _all ?? (First is { } first ? [first] : []);

    /// <summary>Records a repair.</summary>
    public void Report(string code, int offset, string message)
    {
        var repair = new CosRepair(code, offset, message);
        First ??= repair;
        Count++;
        if (keepAll)
        {
            (_all ??= []).Add(repair);
        }
    }
}
