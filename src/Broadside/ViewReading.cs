using Broadside.Diagnostics;
using Broadside.Objects;

namespace Broadside;

/// <summary>
/// Shorthands over <see cref="EntryReader"/> for reading one entry of a dictionary by key: resolved, null filtered, and typed with
/// <see cref="EntryReader"/>'s leniency policy. Reads that take no <see cref="EntryReport"/> are probes (a value of the wrong type
/// reads as absent, the caller decides whether that is a diagnostic). Nothing here writes to COS.
/// </summary>
internal static class ViewReading
{
    /// <summary>The entry, resolved; <see langword="null"/> when absent, null, or a reference to nothing.</summary>
    public static CosObject? Get(PdfDocument? document, CosDictionary dictionary, CosName key) => EntryReader.Get(document, dictionary, key);

    /// <summary>The entry decoded as a text string (§7.9.2.2), or <see langword="null"/>.</summary>
    public static string? Text(PdfDocument? document, CosDictionary dictionary, CosName key, EntryReport? report = null) =>
        EntryReader.Text(Get(document, dictionary, key), key, report);

    /// <summary>The entry as a name, or <see langword="null"/>.</summary>
    public static CosName? Name(PdfDocument? document, CosDictionary dictionary, CosName key, EntryReport? report = null) =>
        EntryReader.Typed<CosName>(Get(document, dictionary, key), key, "a name", report);

    /// <summary>The entry as a boolean, or <see langword="null"/>.</summary>
    public static bool? Boolean(PdfDocument? document, CosDictionary dictionary, CosName key) =>
        EntryReader.Boolean(Get(document, dictionary, key), key, report: null);

    /// <summary>The entry as a boolean, or <paramref name="fallback"/>.</summary>
    public static bool Boolean(PdfDocument? document, CosDictionary dictionary, CosName key, bool fallback) =>
        Boolean(document, dictionary, key) ?? fallback;

    /// <summary>The entry as an integer, or <see langword="null"/>; with a report, a real holding a whole number is repaired.</summary>
    public static long? Integer(PdfDocument? document, CosDictionary dictionary, CosName key, EntryReport? report = null) =>
        EntryReader.Integer(Get(document, dictionary, key), key, report);

    /// <summary>The entry as an integer in the 32-bit range, or <see langword="null"/>.</summary>
    public static int? Int32(PdfDocument? document, CosDictionary dictionary, CosName key, EntryReport? report = null) =>
        EntryReader.Int32(Get(document, dictionary, key), key, report);

    /// <summary>The entry as a finite number, or <see langword="null"/>.</summary>
    public static double? Number(PdfDocument? document, CosDictionary dictionary, CosName key, EntryReport? report = null) =>
        EntryReader.Number(Get(document, dictionary, key), key, report);

    /// <summary>Reads an array of numbers; <see langword="null"/> when absent, not an array, or holding a non-number.</summary>
    public static double[]? Numbers(PdfDocument? document, CosDictionary dictionary, CosName key)
    {
        if (Get(document, dictionary, key) is not CosArray array)
        {
            return null;
        }

        double[] values = new double[array.Count];
        for (int index = 0; index < values.Length; index++)
        {
            if (EntryReader.Number(EntryReader.Resolve(document, array[index]), key, report: null) is not { } number)
            {
                return null;
            }

            values[index] = number;
        }

        return values;
    }

    /// <summary>Reads a rectangle (§7.9.5): four numbers, normalized.</summary>
    public static PdfRectangle? Rectangle(PdfDocument? document, CosDictionary dictionary, CosName key) =>
        Numbers(document, dictionary, key) is [var x1, var y1, var x2, var y2] ? new PdfRectangle(x1, y1, x2, y2) : null;

    /// <summary>The dictionary of an attribute object or property list, which may be a stream (legacy form, §14.7.6.1).</summary>
    public static CosDictionary? DictionaryOf(CosObject value) => value switch
    {
        CosDictionary dictionary => dictionary,
        CosStream stream => stream.Dictionary,
        _ => null,
    };

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
    public static bool HasType(PdfDocument? document, CosDictionary dictionary, CosName type) =>
        Name(document, dictionary, KnownNames.Type) is { } value && value.Equals(type);

    /// <summary>Records a lenient-mode warning; in strict mode it throws.</summary>
    public static void Warn(PdfDocument document, string code, string message, CosReference? reference) =>
        document.DiagnosticSink.Report(code, DiagnosticSeverity.Warning, message, offset: null, reference);
}
