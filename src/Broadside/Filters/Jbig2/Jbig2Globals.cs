using System.Runtime.CompilerServices;
using Broadside.Diagnostics;
using Broadside.Objects;

namespace Broadside.Filters.Jbig2;

/// <summary>
/// The decoded segments of one <c>JBIG2Globals</c> stream (ISO 32000-2 Table 12, ITU-T T.88 Annex D.3): symbol dictionaries, pattern
/// dictionaries and code tables by segment number, and the deviations met decoding them. Decoded once per stream and limits, then
/// shared by every image that names the stream, on any thread: the results are immutable and the deviations are replayed into each
/// image's context.
/// </summary>
internal sealed class Jbig2Globals
{
    private static readonly ConditionalWeakTable<CosStream, Jbig2Globals> Cache = [];

    private readonly int _version;
    private readonly int _dictionaryVersion;
    private readonly long _maxPixels;
    private readonly long _maxBytes;

    private Jbig2Globals(IReadOnlyDictionary<uint, object> results, IReadOnlyList<(string, DiagnosticSeverity, string, string?)> diagnostics, bool undecodable, int version, int dictionaryVersion, long maxPixels, long maxBytes)
    {
        Results = results;
        Diagnostics = diagnostics;
        Undecodable = undecodable;
        _version = version;
        _dictionaryVersion = dictionaryVersion;
        _maxPixels = maxPixels;
        _maxBytes = maxBytes;
    }

    /// <summary>Gets the decoded dictionaries and tables by segment number.</summary>
    public IReadOnlyDictionary<uint, object> Results { get; }

    /// <summary>Gets the deviations met decoding the stream, in order.</summary>
    public IReadOnlyList<(string Code, DiagnosticSeverity Severity, string Message, string? Key)> Diagnostics { get; }

    /// <summary>Gets a value indicating whether a global segment uses a feature that paints the page and is not decoded.</summary>
    public bool Undecodable { get; }

    /// <summary>
    /// The decoded globals of <paramref name="stream"/>, from the cache when the stream and the limits are unchanged; else decodes
    /// <paramref name="data"/> (the stream's decoded bytes, read only when needed) and caches the result.
    /// </summary>
    /// <param name="stream">The globals stream, the cache key.</param>
    /// <param name="data">Reads the stream's decoded bytes.</param>
    /// <param name="maxPixels">The image pixel limit.</param>
    /// <param name="maxBytes">The image byte limit.</param>
    /// <returns>The decoded globals.</returns>
    public static Jbig2Globals Get(CosStream stream, Func<ReadOnlyMemory<byte>> data, long maxPixels, long maxBytes)
    {
        if (Cache.TryGetValue(stream, out Jbig2Globals? cached)
            && cached._version == stream.Version
            && cached._dictionaryVersion == stream.Dictionary.Version
            && cached._maxPixels == maxPixels
            && cached._maxBytes == maxBytes)
        {
            return cached;
        }

        Jbig2Globals decoded = Decode(data().Span, maxPixels, maxBytes, stream.Version, stream.Dictionary.Version);
        Cache.AddOrUpdate(stream, decoded);
        return decoded;
    }

    /// <summary>Decodes a globals stream without caching it.</summary>
    /// <param name="data">The decoded stream bytes.</param>
    /// <param name="maxPixels">The image pixel limit.</param>
    /// <param name="maxBytes">The image byte limit.</param>
    /// <param name="version">The stream version the result belongs to.</param>
    /// <param name="dictionaryVersion">The stream dictionary version the result belongs to.</param>
    /// <returns>The decoded globals.</returns>
    public static Jbig2Globals Decode(ReadOnlySpan<byte> data, long maxPixels, long maxBytes, int version = 0, int dictionaryVersion = 0)
    {
        var reporter = new Jbig2Reporter(null);
        using var decoder = new Jbig2PageDecoder(reporter, maxPixels, maxBytes, null);
        decoder.ProcessGlobals(data);
        return new Jbig2Globals(
            new Dictionary<uint, object>(decoder.Results),
            [.. reporter.Recorded],
            reporter.Undecodable,
            version,
            dictionaryVersion,
            maxPixels,
            maxBytes);
    }
}
