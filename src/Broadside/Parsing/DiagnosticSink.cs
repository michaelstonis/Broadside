using System.Diagnostics.CodeAnalysis;
using Broadside.Diagnostics;
using Broadside.Objects;

namespace Broadside.Parsing;

/// <summary>
/// The one place a document's reader records deviations. Lenient: append and continue. Strict: append, then throw a
/// <see cref="DiagnosticException"/> for the first deviation of severity <see cref="DiagnosticSeverity.Warning"/> or
/// <see cref="DiagnosticSeverity.Error"/> (ADR 0005); <see cref="DiagnosticSeverity.Information"/> never throws.
/// </summary>
/// <remarks>
/// <para>
/// Thread-safe: objects load lazily after open, possibly on several threads (issue #45), and every load reports here. The
/// optional observer sees each recorded diagnostic before strict mode throws (issue #46 logs through it).
/// </para>
/// <para>
/// Each repair is recorded once. <see cref="Report"/> drops a diagnostic equal in code, offset and object to one already recorded
/// (the same object loaded on two threads, a lazy value computed twice, a structure validated before and after reconstruction).
/// <see cref="ReportOnce"/> keeps only the first diagnostic of a code per object, whatever its offset: for deviations that can
/// repeat many times inside one object, such as an operator error in every line of a page's content. A dropped duplicate is not
/// observed and not added, but strict mode still throws it: the operation that met it failed again.
/// </para>
/// </remarks>
internal sealed class DiagnosticSink(bool strict, Action<Diagnostic>? observer = null)
{
    private readonly Lock _gate = new();
    private readonly List<Diagnostic> _diagnostics = [];
    private readonly HashSet<(string Code, long? Offset, CosReference? Reference)> _recorded = [];
    private HashSet<(string Code, CosReference? Reference)>? _recordedPerObject;

    /// <summary>Gets a value indicating whether the first deviation throws.</summary>
    public bool IsStrict => strict;

    /// <summary>Records a deviation, unless an equal one is already recorded; in strict mode, throws it.</summary>
    /// <param name="code">The diagnostic code, from <see cref="DiagnosticCodes"/> or <see cref="CosRepairCodes"/>.</param>
    /// <param name="severity">How much the repair may have changed the document.</param>
    /// <param name="message">What was wrong and what the reader did.</param>
    /// <param name="offset">The absolute byte offset in the file, when known.</param>
    /// <param name="objectReference">The object the deviation is in, when known.</param>
    /// <exception cref="DiagnosticException">The sink is strict and the severity is not <see cref="DiagnosticSeverity.Information"/>.</exception>
    public void Report(string code, DiagnosticSeverity severity, string message, long? offset = null, CosReference? objectReference = null)
    {
        var diagnostic = new Diagnostic(code, severity, message, offset, objectReference);
        bool added;
        lock (_gate)
        {
            added = _recorded.Add((code, offset, objectReference));
            if (added)
            {
                _diagnostics.Add(diagnostic);
            }
        }

        Publish(diagnostic, added);
    }

    /// <summary>
    /// Records a deviation unless one with the same code is already recorded for <paramref name="objectReference"/>; in strict mode,
    /// throws it. For deviations that repeat inside one object (one per code per object, or per page when the object is the page).
    /// </summary>
    /// <param name="code">The diagnostic code.</param>
    /// <param name="severity">How much the repair may have changed the document.</param>
    /// <param name="message">What was wrong and what the reader did.</param>
    /// <param name="offset">The absolute byte offset in the file, when known; not part of the key.</param>
    /// <param name="objectReference">The object the deviation is in, when known.</param>
    /// <exception cref="DiagnosticException">The sink is strict and the severity is not <see cref="DiagnosticSeverity.Information"/>.</exception>
    public void ReportOnce(string code, DiagnosticSeverity severity, string message, long? offset = null, CosReference? objectReference = null)
    {
        var diagnostic = new Diagnostic(code, severity, message, offset, objectReference);
        bool added;
        lock (_gate)
        {
            added = (_recordedPerObject ??= []).Add((code, objectReference)) && _recorded.Add((code, offset, objectReference));
            if (added)
            {
                _diagnostics.Add(diagnostic);
            }
        }

        Publish(diagnostic, added);
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
        bool added;
        lock (_gate)
        {
            added = _recorded.Add((code, offset, null));
            if (added)
            {
                _diagnostics.Add(diagnostic);
            }
        }

        if (added)
        {
            observer?.Invoke(diagnostic);
        }

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

    private void Publish(Diagnostic diagnostic, bool added)
    {
        if (added)
        {
            observer?.Invoke(diagnostic);
        }

        if (strict && diagnostic.Severity != DiagnosticSeverity.Information)
        {
            throw new DiagnosticException(diagnostic);
        }
    }
}
