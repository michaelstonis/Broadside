using Broadside.Diagnostics;
using Broadside.Objects;

namespace Broadside;

/// <summary>Where and under which code <see cref="EntryReader"/> reports what it repairs or ignores.</summary>
/// <param name="Document">The document whose diagnostics receive the report.</param>
/// <param name="Code">The diagnostic code.</param>
/// <param name="Reference">The object the diagnostic is reported against.</param>
/// <param name="Subject">How the dictionary is named in messages, such as "The annotation"; <see langword="null"/> for "The {key} entry".</param>
internal readonly record struct EntryReport(PdfDocument Document, string Code, CosReference? Reference, string? Subject = null)
{
    /// <summary>Reports an entry of the wrong type that is read as absent.</summary>
    public void Ignored(CosName key, string expected) =>
        Report($"{Entry(key)} shall be {expected}; it is ignored.");

    /// <summary>Reports an entry of the wrong type that is read through a repair.</summary>
    public void Repaired(CosName key, string expected, string how) =>
        Report($"{Entry(key)} shall be {expected}; it is {how}.");

    /// <summary>Reports a deviation under this report's code.</summary>
    public void Report(string message, DiagnosticSeverity severity = DiagnosticSeverity.Warning) =>
        Document.DiagnosticSink.Report(Code, severity, message, offset: null, Reference);

    private string Entry(CosName key) => Subject is null ? $"The {key.Value} entry" : $"{Subject}'s {key.Value} entry";
}
