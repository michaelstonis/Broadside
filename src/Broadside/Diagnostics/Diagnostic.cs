using System.Globalization;
using System.Text;
using Broadside.Objects;

namespace Broadside.Diagnostics;

/// <summary>A record of one deviation from the specification found while reading a file, and what was done about it.</summary>
/// <remarks>
/// <para>
/// ADR 0005: in lenient mode a reader repairs what it can and records a diagnostic on the document; in strict mode the first
/// diagnostic is thrown as a <see cref="DiagnosticException"/>. A well-formed file produces no diagnostics: a diagnostic means a
/// "shall" of ISO 32000-2 was violated or the reader changed what it read, never that a "should" was not followed.
/// </para>
/// <para>Codes are stable PascalCase identifiers, such as <c>StreamLengthInvalid</c> or <c>PageTreeCountMismatch</c>.</para>
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
