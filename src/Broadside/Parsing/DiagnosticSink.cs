using System.Diagnostics.CodeAnalysis;
using Broadside.Diagnostics;
using Broadside.Objects;

namespace Broadside.Parsing;

/// <summary>
/// The one place a document's reader records deviations. Lenient: append and continue. Strict: append, then throw a
/// <see cref="DiagnosticException"/> for the first deviation (ADR 0005).
/// </summary>
/// <remarks>
/// Thread-safe: objects load lazily after open, possibly on several threads (issue #45), and every load reports here. The
/// optional observer sees each diagnostic before strict mode throws (issue #46 logs through it).
/// </remarks>
internal sealed class DiagnosticSink(bool strict, Action<Diagnostic>? observer = null)
{
    private readonly Lock _gate = new();
    private readonly List<Diagnostic> _diagnostics = [];

    /// <summary>Gets a value indicating whether the first deviation throws.</summary>
    public bool IsStrict => strict;

    /// <summary>Records a deviation; in strict mode, throws it.</summary>
    /// <param name="code">The diagnostic code, from <see cref="DiagnosticCodes"/> or <see cref="CosRepairCodes"/>.</param>
    /// <param name="severity">How much the repair may have changed the document.</param>
    /// <param name="message">What was wrong and what the reader did.</param>
    /// <param name="offset">The absolute byte offset in the file, when known.</param>
    /// <param name="objectReference">The object the deviation is in, when known.</param>
    /// <exception cref="DiagnosticException">The sink is strict.</exception>
    public void Report(string code, DiagnosticSeverity severity, string message, long? offset = null, CosReference? objectReference = null)
    {
        var diagnostic = new Diagnostic(code, severity, message, offset, objectReference);
        Add(diagnostic);
        if (strict)
        {
            throw new DiagnosticException(diagnostic);
        }
    }

    /// <summary>Records a deviation the reader cannot repair, and throws it in either mode.</summary>
    /// <param name="code">The diagnostic code.</param>
    /// <param name="message">What was wrong.</param>
    /// <param name="offset">The absolute byte offset in the file, when known.</param>
    /// <exception cref="DiagnosticException">Always.</exception>
    [DoesNotReturn]
    public void Fail(string code, string message, long? offset = null)
    {
        var diagnostic = new Diagnostic(code, DiagnosticSeverity.Error, message, offset);
        Add(diagnostic);
        throw new DiagnosticException(diagnostic);
    }

    /// <summary>Returns the diagnostics recorded so far, in the order they were reported.</summary>
    /// <returns>A snapshot; later reports do not change it.</returns>
    public Diagnostic[] Snapshot()
    {
        lock (_gate)
        {
            return [.. _diagnostics];
        }
    }

    private void Add(Diagnostic diagnostic)
    {
        lock (_gate)
        {
            _diagnostics.Add(diagnostic);
        }

        observer?.Invoke(diagnostic);
    }
}
