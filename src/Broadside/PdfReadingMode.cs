namespace Broadside;

/// <summary>How a reader treats a deviation from the specification.</summary>
/// <remarks>ADR 0005.</remarks>
public enum PdfReadingMode
{
    /// <summary>The default: repair what can be repaired, record a <see cref="Diagnostics.Diagnostic"/> on the document, continue.</summary>
    Lenient = 0,

    /// <summary>The first deviation throws a <see cref="Diagnostics.DiagnosticException"/>.</summary>
    Strict = 1,
}
