using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>
/// Reads the entries of one dictionary for a document-model view: resolves references, applies defaults without writing them, and
/// reports a deviation against the dictionary's object (ADR 0004: views read through, never copy; ADR 0005: lenient with diagnostics).
/// </summary>
/// <param name="Document">The document the dictionary belongs to.</param>
/// <param name="Dictionary">The dictionary.</param>
/// <param name="Reference">The object diagnostics are reported against: the dictionary's own reference, or the object holding it directly.</param>
internal readonly record struct DictionaryView(PdfDocument Document, CosDictionary Dictionary, CosReference? Reference)
{
    /// <summary>Returns the entry for <paramref name="key"/>, resolved; <see langword="null"/> when absent or a reference to nothing.</summary>
    public CosObject? Get(CosName key) =>
        Dictionary.TryGetValue(key, out CosObject? value) && Document.Resolve(value) is not CosNull and var resolved ? resolved : null;

    /// <summary>Reports a deviation in the dictionary.</summary>
    public void Report(string code, string message, DiagnosticSeverity severity = DiagnosticSeverity.Warning) =>
        Document.DiagnosticSink.Report(code, severity, message, objectReference: Reference);

    /// <summary>Reads a boolean entry; any other type reads as <paramref name="fallback"/> with a diagnostic.</summary>
    public bool? ReadBoolean(CosName key, string code, bool? fallback)
    {
        switch (Get(key))
        {
            case null:
                return fallback;
            case CosBoolean boolean:
                return boolean.Value;
            default:
                Report(code, $"The {key.Value} entry shall be a boolean; it is ignored.");
                return fallback;
        }
    }

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

    /// <summary>Reads a text string entry (§7.9.2.2); a name or number reads as its text and anything else as null, with a diagnostic.</summary>
    public string? ReadText(CosName key, string code)
    {
        switch (Get(key))
        {
            case null:
                return null;
            case CosString text:
                return text.DecodeText();
            case CosName name:
                Report(code, $"The {key.Value} entry shall be a text string; it is a name, read as its text.");
                return name.Value;
            case CosInteger integer:
                Report(code, $"The {key.Value} entry shall be a text string; it is a number, read as its text.");
                return integer.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case CosReal real:
                Report(code, $"The {key.Value} entry shall be a text string; it is a number, read as its text.");
                return real.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            default:
                Report(code, $"The {key.Value} entry shall be a text string; it is ignored.");
                return null;
        }
    }

    /// <summary>Reads an integer entry; anything else reads as null with a diagnostic.</summary>
    public int? ReadInteger(CosName key, string code)
    {
        switch (Get(key))
        {
            case null:
                return null;
            case CosInteger { Value: >= int.MinValue and <= int.MaxValue } integer:
                return (int)integer.Value;
            default:
                Report(code, $"The {key.Value} entry shall be an integer; it is ignored.");
                return null;
        }
    }
}
