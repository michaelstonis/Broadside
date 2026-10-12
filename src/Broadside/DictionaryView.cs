using Broadside.Diagnostics;
using Broadside.Objects;

namespace Broadside;

/// <summary>
/// Reads the entries of one dictionary for a document-model view through <see cref="EntryReader"/>: resolves references, applies
/// defaults without writing them, and reports a deviation against the dictionary's object (ADR 0004: views read through, never copy;
/// ADR 0005: lenient with diagnostics).
/// </summary>
/// <param name="Document">The document the dictionary belongs to.</param>
/// <param name="Dictionary">The dictionary.</param>
/// <param name="Reference">The object diagnostics are reported against: the dictionary's own reference, or the object holding it directly.</param>
internal readonly record struct DictionaryView(PdfDocument Document, CosDictionary Dictionary, CosReference? Reference)
{
    /// <summary>Returns the entry for <paramref name="key"/>, resolved; <see langword="null"/> when absent, null, or a reference to nothing.</summary>
    public CosObject? Get(CosName key) => EntryReader.Get(Document, Dictionary, key);

    /// <summary>Reports a deviation in the dictionary.</summary>
    public void Report(string code, string message, DiagnosticSeverity severity = DiagnosticSeverity.Warning) =>
        Document.DiagnosticSink.Report(code, severity, message, objectReference: Reference);

    /// <summary>Reads a boolean entry; any other type reads as <paramref name="fallback"/> with a diagnostic.</summary>
    public bool? ReadBoolean(CosName key, string code, bool? fallback) => EntryReader.Boolean(Get(key), key, Issue(code)) ?? fallback;

    /// <summary>Reads a name entry from a fixed set; an unknown name or another type reads as <paramref name="fallback"/> with a diagnostic.</summary>
    public T? ReadName<T>(CosName key, string code, T? fallback, params ReadOnlySpan<(string Name, T Value)> names)
        where T : struct, Enum
    {
        CosObject? value = Get(key);
        if (value is null)
        {
            return fallback;
        }

        if (value is CosName name)
        {
            foreach ((string text, T member) in names)
            {
                if (string.Equals(name.Value, text, StringComparison.Ordinal))
                {
                    return member;
                }
            }
        }

        Report(code, $"The {key.Value} entry is not one of the names the specification defines for it; the default is used.");
        return fallback;
    }

    /// <summary>Reads a text string entry (§7.9.2.2) with <see cref="EntryReader"/>'s repairs, reported under <paramref name="code"/>.</summary>
    public string? ReadText(CosName key, string code) => EntryReader.Text(Get(key), key, Issue(code));

    /// <summary>Reads an integer entry that fits in 32 bits with <see cref="EntryReader"/>'s repairs, reported under <paramref name="code"/>.</summary>
    public int? ReadInteger(CosName key, string code) => EntryReader.Int32(Get(key), key, Issue(code));

    /// <summary>The report for <paramref name="code"/> against this dictionary.</summary>
    public EntryReport Issue(string code) => new(Document, code, Reference);
}
