namespace Broadside;

/// <summary>Which streams a save compresses with the <c>FlateDecode</c> encoder.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.1 and §7.4.4. Only streams without a <c>Filter</c> entry (and without <c>DecodeParms</c> or an external file,
/// §7.3.8.2 Table 5) are compressed; a stream that already has filters is written as it is. Metadata streams (§14.3.2) are never
/// compressed, so that tools which scan a file for its XMP packet still find it (PDF/A-1, ISO 19005-1, forbids filtering them).
/// A stream is written compressed only when that makes it smaller, its <c>Filter</c> entry included.
/// </para>
/// <para>
/// The document itself is not changed: the compressed copy exists only in the written file. The streams a save creates (object
/// streams, the cross-reference stream) are always compressed, whatever this setting.
/// </para>
/// </remarks>
public enum PdfStreamCompression
{
    /// <summary>Writes every stream's data as it is.</summary>
    None = 0,

    /// <summary>
    /// Compresses unfiltered streams that are new or have changed since the document was read. Unchanged objects keep the exact bytes
    /// they were read from. The default.
    /// </summary>
    ChangedStreams = 1,

    /// <summary>Compresses every unfiltered stream, changed or not; unchanged unfiltered streams are written anew instead of copied.</summary>
    AllStreams = 2,
}
