using Broadside.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Broadside;

/// <summary>
/// The immutable snapshot of <see cref="PdfOptions"/> an engine takes at construction and every document it opens reads from.
/// </summary>
/// <remarks>
/// Each option a later issue adds (filters #38, security handlers #42, sources and limits #45, logging #46) gets a field here,
/// copied from the options in <see cref="From"/>, so no engine ever observes a change made to an options object after it was built.
/// </remarks>
internal sealed class EngineConfiguration
{
    private EngineConfiguration(PdfReadingMode readingMode) => ReadingMode = readingMode;

    /// <summary>Gets how deviations are treated.</summary>
    public PdfReadingMode ReadingMode { get; }

    /// <summary>
    /// Gets what sees each diagnostic as it is recorded, before strict mode throws it: the logger, or <see langword="null"/> when
    /// nothing logs, so a document without logging pays nothing.
    /// </summary>
    public Action<Diagnostic>? DiagnosticObserver { get; private init; }

    /// <summary>Copies the current values of <paramref name="options"/>.</summary>
    /// <param name="options">The options.</param>
    /// <param name="hostLoggerFactory">The container's logger factory, used when the options set none.</param>
    /// <returns>The snapshot.</returns>
    public static EngineConfiguration From(PdfOptions options, ILoggerFactory? hostLoggerFactory = null) => new(options.ReadingMode)
    {
        DiagnosticObserver = DiagnosticLog.CreateObserver(options.LoggerFactory ?? hostLoggerFactory),
    };
}
