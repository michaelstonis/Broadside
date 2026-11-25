using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Broadside.Diagnostics;

/// <summary>
/// Traces lazy loading through the logging abstractions: each object parsed (event 2, <c>ObjectParsed</c>) and each object stream
/// decoded (event 3, <c>ObjectStreamDecoded</c>), at <see cref="LogLevel.Trace"/>, under the same category as diagnostics. With
/// lazy loading (ISO 32000-2 §7.5.4) and the parse-once cache these show what a read actually touched. Nothing is formatted unless
/// trace logging is enabled, and nothing at all runs when the engine has no logger factory.
/// </summary>
/// <remarks>
/// Written with <see cref="LoggerMessage.Define{T1, T2, T3, T4}(LogLevel, EventId, string, LogDefineOptions)"/>, which is the code the
/// logging source generator would emit, rather than generated: with two generated classes in the assembly the generator wrote them in a
/// different order from one build to the next, so two builds of the same commit produced different assemblies (issue #265).
/// <see cref="DiagnosticLog"/> stays the assembly's one generated logging class.
/// </remarks>
internal static class ObjectLog
{
    private static readonly Action<ILogger, int, int, long?, int?, Exception?> ObjectParsedMessage = LoggerMessage.Define<int, int, long?, int?>(
        LogLevel.Trace,
        new EventId(2, "ObjectParsed"),
        "Parsed object {ObjectNumber} {Generation} at offset {Offset} in object stream {ObjectStream}",
        new LogDefineOptions { SkipEnabledCheck = true });

    private static readonly Action<ILogger, int, int, Exception?> ObjectStreamDecodedMessage = LoggerMessage.Define<int, int>(
        LogLevel.Trace,
        new EventId(3, "ObjectStreamDecoded"),
        "Decoded object stream {ObjectNumber} with {Members} objects",
        new LogDefineOptions { SkipEnabledCheck = true });

    /// <summary>Returns the logger an engine hands its documents' loaders, or <see langword="null"/> when nothing logs.</summary>
    /// <param name="loggerFactory">The logger factory, or <see langword="null"/>.</param>
    /// <returns>The logger, created once per engine.</returns>
    public static ILogger? CreateLogger(ILoggerFactory? loggerFactory) =>
        loggerFactory is null or NullLoggerFactory ? null : loggerFactory.CreateLogger(DiagnosticLog.Category);

    /// <summary>Logs that an indirect object was parsed: from the file body at <paramref name="offset"/>, or from an object stream.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="objectNumber">The object number.</param>
    /// <param name="generation">The generation number.</param>
    /// <param name="offset">The absolute offset of <c>N G obj</c>, for an object in the file body.</param>
    /// <param name="objectStream">The object number of the object stream holding it, for a compressed object.</param>
    public static void ObjectParsed(ILogger logger, int objectNumber, int generation, long? offset, int? objectStream)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            ObjectParsedMessage(logger, objectNumber, generation, offset, objectStream, null);
        }
    }

    /// <summary>Logs that an object stream was decoded and its header read.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="objectNumber">The object stream's object number.</param>
    /// <param name="members">How many members its header lists.</param>
    public static void ObjectStreamDecoded(ILogger logger, int objectNumber, int members)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            ObjectStreamDecodedMessage(logger, objectNumber, members, null);
        }
    }
}
