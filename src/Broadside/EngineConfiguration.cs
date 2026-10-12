using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Fonts;
using Broadside.Fonts.Resolution;
using Broadside.Graphics;
using Broadside.IO;
using Broadside.Security;
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
    public Action<Diagnostic>? DiagnosticObserver { get; private set; }

    /// <summary>
    /// Gets the logger lazy loading is traced through (issue #45: <c>ObjectParsed</c>, <c>ObjectStreamDecoded</c>), or
    /// <see langword="null"/> when nothing logs.
    /// </summary>
    public ILogger? Logger { get; private init; }

    /// <summary>Gets the filters by name: the managed defaults with the options' registrations applied (issue #38).</summary>
    public FilterRegistry Filters { get; private init; } = FilterRegistry.Create([]);

    /// <summary>Gets the encoders by filter name: the managed defaults with the options' registrations applied (issue #142).</summary>
    public StreamEncoderRegistry StreamEncoders { get; private init; } = StreamEncoderRegistry.Default;

    /// <summary>Gets the most bytes one stream may decode to (issue #38).</summary>
    public long MaxDecodedStreamLength { get; private init; } = PdfOptions.DefaultMaxDecodedStreamLength;

    /// <summary>Gets the security handlers: the standard handler with the options' registrations applied (issue #42).</summary>
    public SecurityHandlerRegistry SecurityHandlers { get; private init; } = SecurityHandlerRegistry.Default;

    /// <summary>Gets the credentials offered to encrypted documents opened without their own (issue #42).</summary>
    public PdfCredentials? Credentials { get; private init; }

    /// <summary>Gets the font program parsers: the options' registrations, newest first, then the managed defaults (issue #50).</summary>
    public FontProgramParserRegistry FontProgramParsers { get; private init; } = FontProgramParserRegistry.Default;

    /// <summary>Gets the font resolvers: the options' registrations, in order, then the operating system's (issue #59).</summary>
    public FontResolverChain FontResolvers { get; private init; } = FontResolverChain.Default;

    /// <summary>Gets the colour management colours are converted through (issue #77).</summary>
    public IColorManagement ColorManagement { get; private init; } = ManagedColorManagement.Default;

    /// <summary>Gets how many bytes of a non-seekable stream are copied into memory before a temporary file is used (issue #45).</summary>
    public long StreamBufferLimit { get; private init; } = PdfOptions.DefaultStreamBufferLimit;

    /// <summary>Gets how a remote file is fetched and cached (issue #207).</summary>
    public RangeSourceSettings RangeSource { get; private init; } = new(PdfOptions.DefaultRangeBlockSize, PdfOptions.DefaultRangeCacheLimit, SynchronousFetch: true);

    /// <summary>Gets the most pixels one image may have (issue #60).</summary>
    public long MaxImagePixels { get; private init; } = PdfOptions.DefaultMaxImagePixels;

    /// <summary>Returns a copy whose diagnostics are seen by <paramref name="observer"/> instead.</summary>
    /// <param name="observer">The observer, or <see langword="null"/> for none.</param>
    /// <returns>The copy.</returns>
    public EngineConfiguration WithDiagnosticObserver(Action<Diagnostic>? observer)
    {
        var copy = (EngineConfiguration)MemberwiseClone();
        copy.DiagnosticObserver = observer;
        return copy;
    }

    /// <summary>Copies the current values of <paramref name="options"/>.</summary>
    /// <param name="options">The options.</param>
    /// <param name="hostLoggerFactory">The container's logger factory, used when the options set none.</param>
    /// <returns>The snapshot.</returns>
    public static EngineConfiguration From(PdfOptions options, ILoggerFactory? hostLoggerFactory = null) => new(options.ReadingMode)
    {
        DiagnosticObserver = DiagnosticLog.CreateObserver(options.LoggerFactory ?? hostLoggerFactory),
        Logger = ObjectLog.CreateLogger(options.LoggerFactory ?? hostLoggerFactory),
        Filters = FilterRegistry.Create(options.Filters),
        StreamEncoders = options.StreamEncoders.Count == 0 ? StreamEncoderRegistry.Default : StreamEncoderRegistry.Create(options.StreamEncoders),
        MaxDecodedStreamLength = options.MaxDecodedStreamLength,
        SecurityHandlers = options.SecurityHandlers.Count == 0 ? SecurityHandlerRegistry.Default : SecurityHandlerRegistry.Create(options.SecurityHandlers),
        Credentials = options.Credentials,
        StreamBufferLimit = options.StreamBufferLimit,
        MaxImagePixels = options.MaxImagePixels,
        RangeSource = new(
            options.RangeBlockSize,
            Math.Max(options.RangeCacheLimit, options.RangeBlockSize),
            options.SynchronousRangeFetch && !OperatingSystem.IsBrowser() && !OperatingSystem.IsWasi()),
        FontProgramParsers = FontProgramParserRegistry.Create(options.FontProgramParsers),
        ColorManagement = options.ColorManagement,
        FontResolvers = CreateFontResolvers(options),
    };

    private static FontResolverChain CreateFontResolvers(PdfOptions options)
    {
        IFontResolver? system = options.SystemFontResolver;
        if (ReferenceEquals(system, SystemFontResolver.Shared) && !string.IsNullOrEmpty(options.SystemFontIndexPath))
        {
            system = SystemFontResolver.ForIndexPath(options.SystemFontIndexPath);
        }

        return options.FontResolvers.Count == 0 && ReferenceEquals(system, SystemFontResolver.Shared)
            ? FontResolverChain.Default
            : new FontResolverChain(options.FontResolvers, system);
    }
}
