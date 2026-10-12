using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Fonts;

/// <summary>
/// The diagnostics of a parse context (<see cref="FontProgramContext"/>, <see cref="CMapContext"/>): the first diagnostic of each
/// code is kept for the context's own list; inside a document every report also goes to the document's sink, once per code against
/// the parsed stream; outside one, strict mode throws.
/// </summary>
/// <remarks>ADR 0005. Thread-safe: a context is shared by every thread reading the font or CMap it belongs to.</remarks>
internal sealed class ContextDiagnostics(DiagnosticSink? sink, CosReference? objectReference)
{
    private readonly List<Diagnostic> _diagnostics = [];
    private readonly HashSet<string> _codes = [];

    /// <summary>Gets the reading mode a context with these diagnostics starts with: the document's, or lenient outside one.</summary>
    public PdfReadingMode DefaultReadingMode => sink?.IsStrict == true ? PdfReadingMode.Strict : PdfReadingMode.Lenient;

    /// <summary>Gets the diagnostics reported so far: the first of each code, in order.</summary>
    public IReadOnlyList<Diagnostic> Items
    {
        get
        {
            lock (_diagnostics)
            {
                return [.. _diagnostics];
            }
        }
    }

    /// <summary>Records a deviation.</summary>
    /// <exception cref="DiagnosticException">In strict mode, unless the severity is <see cref="DiagnosticSeverity.Information"/>.</exception>
    public void Report(string code, DiagnosticSeverity severity, string message, PdfReadingMode readingMode)
    {
        var diagnostic = new Diagnostic(code, severity, message, objectReference: objectReference);
        lock (_diagnostics)
        {
            if (_codes.Add(code))
            {
                _diagnostics.Add(diagnostic);
            }
        }

        if (sink is not null)
        {
            sink.ReportOnce(code, severity, message, objectReference: objectReference);
        }
        else if (readingMode == PdfReadingMode.Strict && severity != DiagnosticSeverity.Information)
        {
            throw new DiagnosticException(diagnostic);
        }
    }
}
