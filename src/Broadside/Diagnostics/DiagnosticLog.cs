using Broadside.Objects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Broadside.Diagnostics;

/// <summary>
/// Logs diagnostics through the logging abstractions, in addition to recording them on the document (ADR 0005; spec #33, user
/// story 45). The log methods are source-generated, so nothing is formatted or boxed unless the level is enabled.
/// </summary>
internal static partial class DiagnosticLog
{
    /// <summary>The logging category: diagnostics belong to the document being read.</summary>
    public const string Category = "Broadside.PdfDocument";

    /// <summary>Returns the observer an engine hands every document's diagnostic sink, or <see langword="null"/> when nothing logs.</summary>
    /// <param name="loggerFactory">The logger factory, or <see langword="null"/>.</param>
    /// <returns>The observer. The logger is created once, here, per engine.</returns>
    public static Action<Diagnostic>? CreateObserver(ILoggerFactory? loggerFactory)
    {
        if (loggerFactory is null or NullLoggerFactory)
        {
            return null;
        }

        ILogger logger = loggerFactory.CreateLogger(Category);
        return diagnostic => Log(logger, diagnostic);
    }

    /// <summary>Logs <paramref name="diagnostic"/> at the level its severity maps to.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="diagnostic">The diagnostic.</param>
    public static void Log(ILogger logger, Diagnostic diagnostic) => DiagnosticRecorded(
        logger,
        diagnostic.Severity == DiagnosticSeverity.Error ? LogLevel.Error : LogLevel.Warning,
        diagnostic.Severity,
        diagnostic.Code,
        diagnostic.ObjectReference,
        diagnostic.Offset,
        diagnostic.Message);

    [LoggerMessage(EventId = 1, EventName = "Diagnostic", Message = "{Severity} {Code} in object {ObjectReference} at offset {Offset}: {Message}")]
    private static partial void DiagnosticRecorded(
        ILogger logger,
        LogLevel level,
        DiagnosticSeverity severity,
        string code,
        CosReference? objectReference,
        long? offset,
        string message);
}
