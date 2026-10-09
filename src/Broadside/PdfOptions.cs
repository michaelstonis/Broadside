using Microsoft.Extensions.Logging;

namespace Broadside;

/// <summary>
/// The configuration of a <see cref="PdfEngine"/>, set fluently: <c>Use*</c> methods select an implementation or behavior,
/// <c>With*</c> methods set a value.
/// </summary>
/// <remarks>
/// An engine copies its options when it is constructed, so changing an options object afterwards does not change any engine built
/// from it. The properties are settable so the same type binds through the options pattern of dependency injection.
/// </remarks>
public sealed class PdfOptions
{
    /// <summary>Gets or sets how deviations from the specification are treated. The default is <see cref="PdfReadingMode.Lenient"/>.</summary>
    /// <remarks>ADR 0005.</remarks>
    public PdfReadingMode ReadingMode { get; set; } = PdfReadingMode.Lenient;

    /// <summary>Repairs deviations and records them as diagnostics on the document. The default.</summary>
    /// <returns>These options.</returns>
    public PdfOptions UseLenient()
    {
        ReadingMode = PdfReadingMode.Lenient;
        return this;
    }

    /// <summary>Throws a <see cref="Diagnostics.DiagnosticException"/> for the first deviation.</summary>
    /// <returns>These options.</returns>
    public PdfOptions UseStrict()
    {
        ReadingMode = PdfReadingMode.Strict;
        return this;
    }

    /// <summary>Gets the logger factory set by <see cref="WithLoggerFactory"/>, or <see langword="null"/>.</summary>
    /// <remarks>Not a property, so configuration binding never sees it.</remarks>
    internal ILoggerFactory? LoggerFactory { get; private set; }

    /// <summary>
    /// Logs every diagnostic through <paramref name="loggerFactory"/> as well as recording it on the document. Without it, an engine
    /// built through dependency injection logs through the container's logger factory and any other engine logs nothing.
    /// </summary>
    /// <param name="loggerFactory">The logger factory, or <see langword="null"/> to use the container's or none.</param>
    /// <returns>These options.</returns>
    /// <remarks>
    /// ADR 0005 and spec #33, user story 45. Diagnostics are logged under the category <c>Broadside.PdfDocument</c> with event id 1,
    /// <c>Diagnostic</c>, at <see cref="LogLevel.Warning"/> or <see cref="LogLevel.Error"/> after their severity, with the structured
    /// values <c>Code</c>, <c>Severity</c>, <c>Offset</c>, <c>ObjectReference</c> and <c>Message</c>. In strict mode the deviation is
    /// logged before it is thrown.
    /// </remarks>
    public PdfOptions WithLoggerFactory(ILoggerFactory? loggerFactory)
    {
        LoggerFactory = loggerFactory;
        return this;
    }
}
