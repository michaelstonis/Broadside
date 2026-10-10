using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Fonts;

/// <summary>
/// What <see cref="CMap.Parse(ReadOnlySpan{byte}, CMapContext)"/> needs beyond the CMap file's bytes: how deviations are treated,
/// the limits on hostile data, and where diagnostics go.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.7.5; Adobe TN 5014 §7. A context created with the public constructor stands alone: its diagnostics are kept in
/// <see cref="Diagnostics"/> only, which is how a CMap is parsed outside a document. Inside a document (the <c>Encoding</c> of a
/// Type 0 font), diagnostics are recorded on the document against the CMap stream, once per code.
/// </para>
/// <para>Thread safety: a context may be shared by parses on several threads.</para>
/// </remarks>
public sealed class CMapContext
{
    /// <summary>The default of <see cref="MaxEntries"/>.</summary>
    public const int DefaultMaxEntries = 1_000_000;

    /// <summary>The default of <see cref="MaxUseCMapDepth"/>: Adobe TN 5014 §7.4 allows <c>usecmap</c> to nest five levels.</summary>
    public const int DefaultMaxUseCMapDepth = 5;

    private readonly ContextDiagnostics _diagnostics;

    /// <summary>Initializes a new instance of the <see cref="CMapContext"/> class that stands alone, outside any document.</summary>
    public CMapContext()
    {
        _diagnostics = new ContextDiagnostics(null, null);
    }

    /// <summary>Initializes a new instance of the <see cref="CMapContext"/> class for a CMap stream of a document.</summary>
    internal CMapContext(DiagnosticSink sink, CosReference? objectReference)
    {
        _diagnostics = new ContextDiagnostics(sink, objectReference);
        ReadingMode = _diagnostics.DefaultReadingMode;
    }

    /// <summary>Gets how deviations are treated: in <see cref="PdfReadingMode.Strict"/> mode the first one throws.</summary>
    /// <remarks>ADR 0005.</remarks>
    public PdfReadingMode ReadingMode { get; init; } = PdfReadingMode.Lenient;

    /// <summary>
    /// Gets how many mapping and codespace entries a CMap file may hold; the rest are dropped with a diagnostic. Default 1,000,000.
    /// A range counts once however many codes it covers.
    /// </summary>
    public int MaxEntries { get; init; } = DefaultMaxEntries;

    /// <summary>Gets how many CMaps may be chained through <c>usecmap</c> above the one parsed; deeper ones are ignored with a diagnostic. Default 5.</summary>
    /// <remarks>Adobe TN 5014 §7.4, <c>usecmap</c>.</remarks>
    public int MaxUseCMapDepth { get; init; } = DefaultMaxUseCMapDepth;

    /// <summary>Gets the diagnostics reported through this context: the first of each code, in order.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.Items;

    /// <summary>Records a deviation; only the first of each code is kept.</summary>
    /// <exception cref="DiagnosticException">In strict mode, unless the severity is <see cref="DiagnosticSeverity.Information"/>.</exception>
    internal void Report(string code, DiagnosticSeverity severity, string message)
    {
        _diagnostics.Report(code, severity, message, ReadingMode);
    }
}
