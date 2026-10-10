using System.Globalization;
using System.Text;
using Broadside.Objects;

namespace Broadside.Diagnostics;

/// <summary>A record of one deviation from the specification found while reading a file, and what was done about it.</summary>
/// <remarks>
/// <para>
/// ADR 0005: in lenient mode a reader repairs what it can and records a diagnostic on the document; in strict mode the first
/// <see cref="DiagnosticSeverity.Warning"/> or <see cref="DiagnosticSeverity.Error"/> is thrown as a <see cref="DiagnosticException"/>.
/// A well-formed file produces no warnings and no errors: one means a "shall" of ISO 32000-2 was violated or the reader changed what
/// it read, never that a "should" was not followed. <see cref="DiagnosticSeverity.Information"/> notes a valid feature the library
/// preserves without supporting it, and is never thrown.
/// </para>
/// <para>
/// Codes are stable PascalCase identifiers, such as <c>StreamLengthInvalid</c> or <c>PageTreeCountMismatch</c>; match on
/// <see cref="Code"/>, <see cref="Offset"/> and <see cref="ObjectReference"/>, never on <see cref="Message"/>. A document records each
/// repair once: the same code at the same offset in the same object is not recorded twice, however often it is met.
/// </para>
/// <para>
/// ISO 32000-2 §7.5 describes the file structure a reader repairs (cross-reference table, trailer, <c>startxref</c>) but does not
/// specify repair; the codes for it are <c>StartxrefMissing</c>, <c>StartxrefInvalid</c>, <c>TrailerMissing</c>,
/// <c>RootMissing</c>, <c>XrefEntryOffsetInvalid</c>, <c>MissingEndobj</c> and <c>StreamLengthInvalid</c>, among others.
/// </para>
/// </remarks>
public sealed class Diagnostic
{
    /// <summary>Initializes a new instance of the <see cref="Diagnostic"/> class.</summary>
    /// <param name="code">The stable identifier of the kind of deviation.</param>
    /// <param name="severity">How much the repair may have changed the document.</param>
    /// <param name="message">A human-readable description of the deviation and the repair.</param>
    /// <param name="offset">The byte offset in the file where the deviation was found, when known.</param>
    /// <param name="objectReference">The indirect object the deviation is in, when known.</param>
    public Diagnostic(string code, DiagnosticSeverity severity, string message, long? offset = null, CosReference? objectReference = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentNullException.ThrowIfNull(message);
        Code = code;
        Severity = severity;
        Message = message;
        Offset = offset;
        ObjectReference = objectReference;
    }

    /// <summary>Gets the stable identifier of the kind of deviation.</summary>
    public string Code { get; }

    /// <summary>Gets how much the repair may have changed the document.</summary>
    public DiagnosticSeverity Severity { get; }

    /// <summary>Gets a human-readable description of the deviation and the repair.</summary>
    public string Message { get; }

    /// <summary>Gets the byte offset from the start of the file where the deviation was found, or <see langword="null"/> when unknown.</summary>
    public long? Offset { get; }

    /// <summary>Gets the indirect object the deviation is in, or <see langword="null"/> when unknown or not in an object.</summary>
    public CosReference? ObjectReference { get; }

    /// <inheritdoc/>
    public override string ToString()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{Severity} {Code}");
        if (ObjectReference is { } reference)
        {
            text.Append(CultureInfo.InvariantCulture, $" in object {reference.ObjectNumber} {reference.Generation}");
        }

        if (Offset is { } offset)
        {
            text.Append(CultureInfo.InvariantCulture, $" at offset {offset}");
        }

        return text.Append(": ").Append(Message).ToString();
    }
}
