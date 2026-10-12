using System.Globalization;
using Broadside.Objects;

namespace Broadside;

/// <summary>
/// The one reader for the entries of document-model dictionaries (catalog views, annotations, actions, forms, structure, files and
/// layers), with one leniency policy. Nothing here writes to COS (ADR 0004).
/// </summary>
/// <remarks>
/// <para>
/// Every entry is resolved, and an entry that is <c>null</c> or a reference to nothing reads as absent (ISO 32000-2 §7.3.9: a
/// dictionary entry whose value is null is equivalent to an absent entry).
/// </para>
/// <para>
/// A typed read accepts its own type. When the caller passes an <see cref="EntryReport"/>, a value of a neighbouring type with one
/// obvious reading is repaired (a name or a number where a text string belongs reads as its text; a real holding a whole number
/// where an integer belongs reads as that integer) and anything else reads as absent, each with a diagnostic (ADR 0005). Without
/// an <see cref="EntryReport"/> the read is a probe: a value of another type reads as absent and nothing is repaired, so no repair
/// ever happens silently.
/// </para>
/// </remarks>
internal static class EntryReader
{
    /// <summary>The entry, resolved; <see langword="null"/> when absent, null, or a reference to nothing.</summary>
    /// <param name="document">The document that resolves references; <see langword="null"/> to read the entry as stored.</param>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The key.</param>
    /// <returns>The resolved value, or <see langword="null"/>.</returns>
    public static CosObject? Get(PdfDocument? document, CosDictionary dictionary, CosName key) =>
        dictionary.TryGetValue(key, out CosObject? value) ? Resolve(document, value) : null;

    /// <summary>The value, resolved; <see langword="null"/> when it is null or a reference to nothing.</summary>
    /// <param name="document">The document that resolves references; <see langword="null"/> to take the value as stored.</param>
    /// <param name="value">The value.</param>
    /// <returns>The resolved value, or <see langword="null"/>.</returns>
    public static CosObject? Resolve(PdfDocument? document, CosObject? value)
    {
        if (value is null)
        {
            return null;
        }

        CosObject resolved = document is null ? value : document.Resolve(value);
        return resolved is CosNull ? null : resolved;
    }

    /// <summary>Reads a text string (§7.9.2.2); with a report, a name or number reads as its text.</summary>
    public static string? Text(CosObject? value, CosName key, EntryReport? report)
    {
        switch (value)
        {
            case null:
                return null;
            case CosString text:
                return text.DecodeText();
            case CosName name when report is { } repair:
                repair.Repaired(key, "a text string", "a name, read as its text");
                return name.Value;
            case CosInteger integer when report is { } repair:
                repair.Repaired(key, "a text string", "a number, read as its text");
                return integer.Value.ToString(CultureInfo.InvariantCulture);
            case CosReal real when report is { } repair:
                repair.Repaired(key, "a text string", "a number, read as its text");
                return real.Value.ToString(CultureInfo.InvariantCulture);
            default:
                report?.Ignored(key, "a text string");
                return null;
        }
    }

    /// <summary>Reads an integer; with a report, a real holding a whole number reads as that integer.</summary>
    public static long? Integer(CosObject? value, CosName key, EntryReport? report)
    {
        switch (value)
        {
            case null:
                return null;
            case CosInteger integer:
                return integer.Value;
            case CosReal real when report is { } repair && real.Value == Math.Floor(real.Value) && Math.Abs(real.Value) < 9.2e18:
                repair.Repaired(key, "an integer", "a real holding a whole number, read as that integer");
                return (long)real.Value;
            default:
                report?.Ignored(key, "an integer");
                return null;
        }
    }

    /// <summary>Reads an integer that fits in 32 bits; anything else as <see cref="Integer"/>, and out of range reads as absent.</summary>
    public static int? Int32(CosObject? value, CosName key, EntryReport? report)
    {
        if (Integer(value, key, report) is not { } integer)
        {
            return null;
        }

        if (integer is >= int.MinValue and <= int.MaxValue)
        {
            return (int)integer;
        }

        report?.Ignored(key, "an integer in the 32-bit range");
        return null;
    }

    /// <summary>Reads a finite number.</summary>
    public static double? Number(CosObject? value, CosName key, EntryReport? report)
    {
        switch (value)
        {
            case null:
                return null;
            case CosNumber number when double.IsFinite(number.ToDouble()):
                return number.ToDouble();
            default:
                report?.Ignored(key, "a number");
                return null;
        }
    }

    /// <summary>Reads a boolean.</summary>
    public static bool? Boolean(CosObject? value, CosName key, EntryReport? report)
    {
        switch (value)
        {
            case null:
                return null;
            case CosBoolean boolean:
                return boolean.Value;
            default:
                report?.Ignored(key, "a boolean");
                return null;
        }
    }

    /// <summary>Reads a value of one COS type (a name, an array, a dictionary, a stream, a string as stored).</summary>
    public static T? Typed<T>(CosObject? value, CosName key, string expected, EntryReport? report)
        where T : CosObject
    {
        switch (value)
        {
            case null:
                return null;
            case T typed:
                return typed;
            default:
                report?.Ignored(key, expected);
                return null;
        }
    }
}
