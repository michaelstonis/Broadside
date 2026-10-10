using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Graphics;

/// <summary>
/// Records the diagnostics of one graphics object (a colour space, shading or pattern) on the reference that names it: the nearest
/// indirect object, so a direct object inside a resource dictionary is reported on its owner. Allocation-free to copy.
/// </summary>
/// <remarks>
/// ADR 0005: reading repairs and records a <see cref="Diagnostic"/>. File offsets are not known once an object is parsed, so none is
/// given. A missing sink (a model built outside a document) records nothing.
/// </remarks>
/// <param name="sink">The document's sink, or <see langword="null"/>.</param>
/// <param name="reference">The reference the diagnostics are recorded on.</param>
internal readonly struct ObjectDiagnostics(DiagnosticSink? sink, CosReference? reference)
{
    /// <summary>Records a deviation; Warning (a repair: the object stays usable) unless given.</summary>
    /// <param name="code">The diagnostic code.</param>
    /// <param name="message">What was found and what is done about it.</param>
    /// <param name="severity">The severity.</param>
    public void Report(string code, string message, DiagnosticSeverity severity = DiagnosticSeverity.Warning) =>
        sink?.Report(code, severity, message, offset: null, reference);

    /// <summary>As <see cref="Report"/>, recorded once per code and reference: for deviations found on a hot path.</summary>
    /// <param name="code">The diagnostic code.</param>
    /// <param name="message">What was found and what is done about it.</param>
    /// <param name="severity">The severity.</param>
    public void ReportOnce(string code, string message, DiagnosticSeverity severity = DiagnosticSeverity.Warning) =>
        sink?.ReportOnce(code, severity, message, offset: null, reference);
}
