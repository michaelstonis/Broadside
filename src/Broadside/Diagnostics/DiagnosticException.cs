namespace Broadside.Diagnostics;

/// <summary>
/// The exception a reader throws for a <see cref="Diagnostics.Diagnostic"/>: in strict mode for the first deviation found, and in
/// either mode when the file cannot be read at all.
/// </summary>
/// <remarks>ADR 0005. The <see cref="Diagnostic"/> carries the code, offset and object of the deviation.</remarks>
public sealed class DiagnosticException : FormatException
{
    private const string UnspecifiedCode = "Unspecified";

    /// <summary>Initializes a new instance of the <see cref="DiagnosticException"/> class for a diagnostic.</summary>
    /// <param name="diagnostic">The deviation that stopped reading.</param>
    public DiagnosticException(Diagnostic diagnostic)
        : base(diagnostic?.ToString())
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        Diagnostic = diagnostic;
    }

    /// <summary>Initializes a new instance of the <see cref="DiagnosticException"/> class with an unspecified diagnostic.</summary>
    public DiagnosticException()
        : this(UnspecifiedMessage(null))
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DiagnosticException"/> class with an unspecified diagnostic.</summary>
    /// <param name="message">The message.</param>
    public DiagnosticException(string? message)
        : this(message, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DiagnosticException"/> class with an unspecified diagnostic.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public DiagnosticException(string? message, Exception? innerException)
        : base(UnspecifiedMessage(message), innerException) =>
        Diagnostic = new Diagnostic(UnspecifiedCode, DiagnosticSeverity.Error, UnspecifiedMessage(message));

    /// <summary>Gets the deviation that stopped reading.</summary>
    public Diagnostic Diagnostic { get; }

    private static string UnspecifiedMessage(string? message) => message ?? "The file deviates from ISO 32000-2.";
}
