using Broadside.Filters;

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

    /// <summary>Gets the filters by name: the managed defaults with the options' registrations applied (issue #38).</summary>
    public FilterRegistry Filters { get; private init; } = FilterRegistry.Create([]);

    /// <summary>Gets the most bytes one stream may decode to (issue #38).</summary>
    public long MaxDecodedStreamLength { get; private init; } = PdfOptions.DefaultMaxDecodedStreamLength;

    /// <summary>Copies the current values of <paramref name="options"/>.</summary>
    /// <param name="options">The options.</param>
    /// <returns>The snapshot.</returns>
    public static EngineConfiguration From(PdfOptions options) => new(options.ReadingMode)
    {
        Filters = FilterRegistry.Create(options.Filters),
        MaxDecodedStreamLength = options.MaxDecodedStreamLength,
    };
}
