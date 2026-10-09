using System.Globalization;

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
    public const string UnexpectedEndOfInput = nameof(UnexpectedEndOfInput);
    public const string UnexpectedToken = nameof(UnexpectedToken);
    public const string TrailingContent = nameof(TrailingContent);
    public const string NumberMalformed = nameof(NumberMalformed);
    public const string NumberOutOfRange = nameof(NumberOutOfRange);
    public const string ReferenceInvalid = nameof(ReferenceInvalid);
    public const string StringUnterminated = nameof(StringUnterminated);
    public const string HexStringUnterminated = nameof(HexStringUnterminated);
    public const string HexStringInvalidDigit = nameof(HexStringInvalidDigit);
    public const string NameInvalidEscape = nameof(NameInvalidEscape);
    public const string NameContainsNull = nameof(NameContainsNull);
    public const string ArrayUnterminated = nameof(ArrayUnterminated);
    public const string DictionaryUnterminated = nameof(DictionaryUnterminated);
    public const string DictionaryKeyNotName = nameof(DictionaryKeyNotName);
    public const string DictionaryValueMissing = nameof(DictionaryValueMissing);
    public const string DictionaryDuplicateKey = nameof(DictionaryDuplicateKey);
    public const string NestingTooDeep = nameof(NestingTooDeep);
    public const string StreamKeywordEndOfLine = nameof(StreamKeywordEndOfLine);
    public const string StreamLengthInvalid = nameof(StreamLengthInvalid);
    public const string EndstreamMissing = nameof(EndstreamMissing);
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
