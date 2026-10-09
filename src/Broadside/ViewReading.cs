using Broadside.Diagnostics;
using Broadside.Objects;

namespace Broadside;

/// <summary>
/// Small lenient readers the issue #76 views share: every entry is resolved, a reference to nothing reads as absent, and a value of
/// the wrong type reads as absent too (the caller decides whether that is a diagnostic). Nothing here writes to COS.
/// </summary>
internal static class ViewReading
{
    /// <summary>The entry, resolved; <see langword="null"/> when absent or a reference to nothing.</summary>
    public static CosObject? Get(PdfDocument document, CosDictionary dictionary, CosName key) =>
        dictionary.TryGetValue(key, out CosObject? value) && document.Resolve(value) is not CosNull and var resolved ? resolved : null;

    /// <summary>The entry decoded as a text string (§7.9.2.2), or <see langword="null"/> when absent or not a string.</summary>
    public static string? Text(PdfDocument document, CosDictionary dictionary, CosName key) =>
        Get(document, dictionary, key) is CosString value ? value.DecodeText() : null;

    /// <summary>The entry as a name, or <see langword="null"/>.</summary>
    public static CosName? Name(PdfDocument document, CosDictionary dictionary, CosName key) =>
        Get(document, dictionary, key) as CosName;

    /// <summary>The entry as a boolean, or <paramref name="fallback"/>.</summary>
    public static bool Boolean(PdfDocument document, CosDictionary dictionary, CosName key, bool fallback) =>
        Get(document, dictionary, key) is CosBoolean value ? value.Value : fallback;

    /// <summary>The entry as an integer, or <see langword="null"/>; a real holding an integral value is accepted.</summary>
    public static long? Integer(PdfDocument document, CosDictionary dictionary, CosName key) => Get(document, dictionary, key) switch
    {
        CosInteger value => value.Value,
        CosReal value when value.Value == Math.Floor(value.Value) && Math.Abs(value.Value) < long.MaxValue => (long)value.Value,
        _ => null,
    };

    /// <summary>The entry as a number, or <see langword="null"/>.</summary>
    public static double? Number(PdfDocument document, CosDictionary dictionary, CosName key) =>
        Get(document, dictionary, key) is CosNumber value ? value.ToDouble() : null;

    /// <summary>
    /// The entry read as a date (ISO 32000-2 §7.9.4): a repaired date is reported as <c>DateInvalid</c>, an unreadable one as
    /// <c>DateUnreadable</c> (and read as <see langword="null"/>); a value that is not a string reads as <see langword="null"/>.
    /// </summary>
    public static PdfDate? Date(PdfDocument document, CosDictionary dictionary, CosName key, CosReference? reference)
    {
        if (Get(document, dictionary, key) is not CosString value)
        {
            return null;
        }

        switch (PdfDate.Parse(value.DecodeText(), out PdfDate date))
        {
            case DateParseOutcome.Valid:
                return date;
            case DateParseOutcome.Repaired:
                Warn(document, Parsing.DiagnosticCodes.DateInvalid, $"The {key.Value} entry does not follow the date format of §7.9.4; it is read with the deviating fields repaired.", reference);
                return date;
            default:
                Warn(document, Parsing.DiagnosticCodes.DateUnreadable, $"The {key.Value} entry is not a date (§7.9.4); its date is unknown.", reference);
                return null;
        }
    }

    /// <summary>The indirect reference an entry holds, or <see langword="null"/> when the entry is direct or absent.</summary>
    public static CosReference? ReferenceOf(CosDictionary dictionary, CosName key) =>
        dictionary.TryGetValue(key, out CosObject? value) ? value as CosReference : null;

    /// <summary>Whether the dictionary's <c>Type</c> is <paramref name="type"/>.</summary>
    public static bool HasType(PdfDocument document, CosDictionary dictionary, CosName type) =>
        Name(document, dictionary, KnownNames.Type) is { } value && value.Equals(type);

    /// <summary>Records a lenient-mode warning; in strict mode it throws.</summary>
    public static void Warn(PdfDocument document, string code, string message, CosReference? reference) =>
        document.DiagnosticSink.Report(code, DiagnosticSeverity.Warning, message, offset: null, reference);
}
