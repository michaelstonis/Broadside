using Broadside.Filters;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Objects;
using Broadside.Security;
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
    /// <summary>The default of <see cref="MaxDecodedStreamLength"/>: 1 GiB.</summary>
    public const long DefaultMaxDecodedStreamLength = 1L << 30;

    /// <summary>The default of <see cref="StreamBufferLimit"/>: 64 MiB.</summary>
    public const long DefaultStreamBufferLimit = 64L << 20;

    /// <summary>The default of <see cref="RangeBlockSize"/>: 4 KiB.</summary>
    public const int DefaultRangeBlockSize = 4 * 1024;

    /// <summary>The default of <see cref="RangeCacheLimit"/>: 64 MiB.</summary>
    public const long DefaultRangeCacheLimit = 64L << 20;

    /// <summary>The default of <see cref="MaxImagePixels"/>: 2^28 (268,435,456) pixels, a 16384 x 16384 image.</summary>
    public const long DefaultMaxImagePixels = 1L << 28;

    /// <summary>The default of <see cref="ContentCacheBudget"/>: 32 MiB.</summary>
    public const long DefaultContentCacheBudget = 32L << 20;

    /// <summary>The default of <see cref="ImageCacheBudget"/>: 64 MiB.</summary>
    public const long DefaultImageCacheBudget = 64L << 20;

    /// <summary>The default of <see cref="GlyphCacheBudget"/>: 8 MiB.</summary>
    public const long DefaultGlyphCacheBudget = 8L << 20;

    /// <summary>The default of <see cref="DisplayListCacheBudget"/>: 32 MiB.</summary>
    public const long DefaultDisplayListCacheBudget = 32L << 20;

    /// <summary>The default of <see cref="MaxMeshVertices"/>: 2^22 (4,194,304) vertices.</summary>
    public const int DefaultMaxMeshVertices = 1 << 22;

    private readonly List<IStreamFilter> _filters = [];
    private readonly List<IStreamEncoder> _streamEncoders = [];
    private readonly List<ISecurityHandler> _securityHandlers = [];
    private readonly List<IFontProgramParser> _fontProgramParsers = [];
    private readonly List<IFontResolver> _fontResolvers = [];
    private long _maxDecodedStreamLength = DefaultMaxDecodedStreamLength;
    private long _streamBufferLimit = DefaultStreamBufferLimit;
    private int _rangeBlockSize = DefaultRangeBlockSize;
    private long _rangeCacheLimit = DefaultRangeCacheLimit;
    private long _maxImagePixels = DefaultMaxImagePixels;
    private long _contentCacheBudget = DefaultContentCacheBudget;
    private long _imageCacheBudget = DefaultImageCacheBudget;
    private long _glyphCacheBudget = DefaultGlyphCacheBudget;
    private long _displayListCacheBudget = DefaultDisplayListCacheBudget;
    private int _maxMeshVertices = DefaultMaxMeshVertices;

    /// <summary>Gets or sets how deviations from the specification are treated. The default is <see cref="PdfReadingMode.Lenient"/>.</summary>
    /// <remarks>Lenient by default: a deviation is repaired and recorded as a diagnostic; strict mode throws instead.</remarks>
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

    /// <summary>Gets or sets the most bytes one stream may decode to; longer output is truncated with a diagnostic. Default 1 GiB.</summary>
    /// <remarks>ISO 32000-2 §7.4. Guards against decompression bombs; a stream's <c>DL</c> entry is only a hint (§7.3.8.2, Table 5).</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public long MaxDecodedStreamLength
    {
        get => _maxDecodedStreamLength;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxDecodedStreamLength = value;
        }
    }

    /// <summary>Gets the filters registered with <see cref="UseFilter"/>, in registration order.</summary>
    /// <remarks>ISO 32000-2 §7.4.1. Not public, so configuration binding never sees it; filters are code, set with <see cref="UseFilter"/>.</remarks>
    internal IReadOnlyList<IStreamFilter> Filters => _filters;

    /// <summary>
    /// Uses <paramref name="filter"/> for every stream whose <c>Filter</c> entry names <see cref="IStreamFilter.Name"/>, replacing the
    /// managed default of that name or adding a filter the defaults lack. A later registration of the same name wins.
    /// </summary>
    /// <param name="filter">The filter. Shared by every document and thread of the engine: it must keep no state between calls.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentException">
    /// The filter's name is <c>Crypt</c>, which the security handler decodes (§7.4.10), or an inline-image abbreviation such as
    /// <c>Fl</c> (§8.9.7, Table 92), which is always read as the full name it stands for.
    /// </exception>
    /// <remarks>ISO 32000-2 §7.4.1. The filter extension point.</remarks>
    public PdfOptions UseFilter(IStreamFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        CosName name = filter.Name ?? throw new ArgumentException("The filter has no name.", nameof(filter));
        if (name.Equals(FilterNames.Crypt) || InlineImageAbbreviations.TryExpandFilter(name, out _))
        {
            throw new ArgumentException(
                $"A filter cannot be registered under /{name.Value}: Crypt belongs to the security handler and abbreviations are read as full names.",
                nameof(filter));
        }

        _filters.Add(filter);
        return this;
    }

    /// <summary>Gets the encoders registered with <see cref="UseStreamEncoder"/>, in registration order.</summary>
    /// <remarks>ISO 32000-2 §7.4.1. Not public, so configuration binding never sees it; encoders are code, set with <see cref="UseStreamEncoder"/>.</remarks>
    internal IReadOnlyList<IStreamEncoder> StreamEncoders => _streamEncoders;

    /// <summary>
    /// Uses <paramref name="encoder"/> to write data for the filter its <see cref="IStreamEncoder.Name"/> names, replacing the managed
    /// default of that name (<see cref="FlateEncoder"/>, <see cref="AsciiHexEncoder"/>, <see cref="Ascii85Encoder"/>,
    /// <see cref="RunLengthEncoder"/>) or adding an encoder the defaults lack. A later registration of the same name wins. Saving
    /// compresses streams with the encoder registered for <c>FlateDecode</c>.
    /// </summary>
    /// <param name="encoder">The encoder. Shared by every document and thread of the engine: it must keep no state between calls.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentException">
    /// The encoder's name is <c>Crypt</c>, which the security handler applies (§7.4.10), or an inline-image abbreviation such as
    /// <c>Fl</c> (§8.9.7, Table 92).
    /// </exception>
    /// <remarks>ISO 32000-2 §7.4.1. The encoding half of the filter extension point, separate from <see cref="UseFilter"/>.</remarks>
    public PdfOptions UseStreamEncoder(IStreamEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        CosName name = encoder.Name ?? throw new ArgumentException("The encoder has no name.", nameof(encoder));
        if (name.Equals(FilterNames.Crypt) || InlineImageAbbreviations.TryExpandFilter(name, out _))
        {
            throw new ArgumentException(
                $"An encoder cannot be registered under /{name.Value}: Crypt belongs to the security handler and abbreviations are not stream filter names.",
                nameof(encoder));
        }

        _streamEncoders.Add(encoder);
        return this;
    }

    /// <summary>
    /// Gets or sets the most pixels (width x height) one image may have; a larger image is not decoded, with a diagnostic. Default
    /// 2^28. The bytes of its samples are also limited by <see cref="MaxDecodedStreamLength"/>.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §8.9.5.1, Table 87: <c>Width</c> and <c>Height</c> come from the file, so a few bytes can claim an image of any
    /// size; the limit is checked before any memory is taken.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public long MaxImagePixels
    {
        get => _maxImagePixels;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxImagePixels = value;
        }
    }

    /// <summary>Sets <see cref="MaxImagePixels"/>.</summary>
    /// <param name="maxPixels">The most pixels one image may have.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxPixels"/> is not positive.</exception>
    /// <remarks>ISO 32000-2 §8.9.5.1.</remarks>
    public PdfOptions WithMaxImagePixels(long maxPixels)
    {
        MaxImagePixels = maxPixels;
        return this;
    }

    /// <summary>
    /// Gets or sets the most bytes of decoded content streams (pages, form XObjects, tiling patterns, Type 3 glyphs) a document keeps
    /// for its next content run; the least recently used are dropped beyond it and decoded again when needed. Zero keeps none. Default
    /// 32 MiB.
    /// </summary>
    /// <remarks>
    /// Spec #33 viewer capability 13; ISO 32000-2 §7.8.2 (content streams, decoded through their filters, §7.4). Observed through
    /// <see cref="PdfDocument.CacheStatistics"/> (<see cref="PdfDocumentCacheStatistics.Content"/>). Changes nothing a run produces.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public long ContentCacheBudget
    {
        get => _contentCacheBudget;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _contentCacheBudget = value;
        }
    }

    /// <summary>Sets <see cref="ContentCacheBudget"/>.</summary>
    /// <param name="budget">The most bytes of decoded content a document keeps; zero for none.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="budget"/> is negative.</exception>
    /// <remarks>Spec #33 viewer capability 13; ISO 32000-2 §7.8.2.</remarks>
    public PdfOptions WithContentCacheBudget(long budget)
    {
        ContentCacheBudget = budget;
        return this;
    }

    /// <summary>
    /// Gets or sets the most bytes of decoded images (image XObjects with their masks, soft masks and colour tables, and stencil masks)
    /// a document keeps for its next display lists; the least recently used are dropped beyond it and decoded again when painted. An
    /// image larger than the budget is decoded for each list that paints it. Zero keeps none: each list decodes its own. Default
    /// 64 MiB.
    /// </summary>
    /// <remarks>
    /// Spec #33 viewer capability 13; ISO 32000-2 §8.9.5 (image XObjects), §8.9.6 and §11.6.5 (masks). Observed through
    /// <see cref="PdfDocument.CacheStatistics"/> (<see cref="PdfDocumentCacheStatistics.Images"/>). A display list holds the images it
    /// paints whatever this budget, so a list kept by the caller keeps its images alive after they leave the cache.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public long ImageCacheBudget
    {
        get => _imageCacheBudget;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _imageCacheBudget = value;
        }
    }

    /// <summary>Sets <see cref="ImageCacheBudget"/>.</summary>
    /// <param name="budget">The most bytes of decoded images a document keeps; zero for none.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="budget"/> is negative.</exception>
    /// <remarks>Spec #33 viewer capability 13; ISO 32000-2 §8.9.5.</remarks>
    public PdfOptions WithImageCacheBudget(long budget)
    {
        ImageCacheBudget = budget;
        return this;
    }

    /// <summary>
    /// Gets or sets the most bytes of glyph outlines a document keeps once read from its font programs; the least recently used are
    /// dropped beyond it and read again when shown. Zero keeps none: each display list reads its own. Default 8 MiB.
    /// </summary>
    /// <remarks>
    /// Spec #33 viewer capability 13; ISO 32000-2 §9.2.4 (glyph descriptions in font programs). Observed through
    /// <see cref="PdfDocument.CacheStatistics"/> (<see cref="PdfDocumentCacheStatistics.Glyphs"/>).
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public long GlyphCacheBudget
    {
        get => _glyphCacheBudget;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _glyphCacheBudget = value;
        }
    }

    /// <summary>Sets <see cref="GlyphCacheBudget"/>.</summary>
    /// <param name="budget">The most bytes of glyph outlines a document keeps; zero for none.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="budget"/> is negative.</exception>
    /// <remarks>Spec #33 viewer capability 13; ISO 32000-2 §9.2.4.</remarks>
    public PdfOptions WithGlyphCacheBudget(long budget)
    {
        GlyphCacheBudget = budget;
        return this;
    }

    /// <summary>
    /// Gets or sets the most bytes of display lists a document keeps, so that rendering a page again (another zoom, another tile)
    /// reuses its list while the document is unchanged; the least recently used are dropped beyond it. A list is charged its tables
    /// and the images it holds. Zero keeps none: <see cref="PdfPage.CreateDisplayList()"/> builds a new list on every call. Default
    /// 32 MiB.
    /// </summary>
    /// <remarks>
    /// Spec #33 viewer capability 13. Observed through <see cref="PdfDocument.CacheStatistics"/>
    /// (<see cref="PdfDocumentCacheStatistics.DisplayLists"/>).
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public long DisplayListCacheBudget
    {
        get => _displayListCacheBudget;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _displayListCacheBudget = value;
        }
    }

    /// <summary>Sets <see cref="DisplayListCacheBudget"/>.</summary>
    /// <param name="budget">The most bytes of display lists a document keeps; zero for none.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="budget"/> is negative.</exception>
    /// <remarks>Spec #33 viewer capability 13.</remarks>
    public PdfOptions WithDisplayListCacheBudget(long budget)
    {
        DisplayListCacheBudget = budget;
        return this;
    }

    /// <summary>
    /// Gets or sets the most vertices one mesh shading (Types 4 to 7) decodes, a patch counting as 16; the rest of a larger mesh is
    /// dropped with a <c>MeshSizeExceeded</c> diagnostic. Default 2^22.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §8.7.4.5.5 to §8.7.4.5.8: a mesh is as long as its stream, so a small compressed stream can decode to more
    /// triangles or patches than a page can usefully show; the limit bounds the memory and the time one shading takes. The data is
    /// also limited by <see cref="MaxDecodedStreamLength"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int MaxMeshVertices
    {
        get => _maxMeshVertices;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxMeshVertices = value;
        }
    }

    /// <summary>Sets <see cref="MaxMeshVertices"/>.</summary>
    /// <param name="maxVertices">The most vertices one mesh shading decodes.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxVertices"/> is not positive.</exception>
    /// <remarks>ISO 32000-2 §8.7.4.5.5 to §8.7.4.5.8.</remarks>
    public PdfOptions WithMaxMeshVertices(int maxVertices)
    {
        MaxMeshVertices = maxVertices;
        return this;
    }

    /// <summary>Sets <see cref="MaxDecodedStreamLength"/>.</summary>
    /// <param name="maxLength">The most bytes one stream may decode to.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxLength"/> is not positive.</exception>
    /// <remarks>ISO 32000-2 §7.4.</remarks>
    public PdfOptions WithMaxDecodedStreamLength(long maxLength)
    {
        MaxDecodedStreamLength = maxLength;
        return this;
    }

    /// <summary>
    /// Gets or sets how many bytes of a non-seekable stream are copied into memory when a document is opened from it; a longer stream
    /// is copied into a temporary file, deleted when the document is disposed, and read from there. Default 64 MiB.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §7.5.1: a file is read at random through its cross-reference table, starting from its end, which a non-seekable
    /// stream cannot do, so it is copied once at the open boundary. A seekable stream, a path and bytes are never copied: they are
    /// read in place or memory-mapped as objects are used.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public long StreamBufferLimit
    {
        get => _streamBufferLimit;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _streamBufferLimit = value;
        }
    }

    /// <summary>Sets <see cref="StreamBufferLimit"/>.</summary>
    /// <param name="limit">How many bytes of a non-seekable stream to copy into memory; 0 always uses a temporary file.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is negative.</exception>
    /// <remarks>ISO 32000-2 §7.5.1.</remarks>
    public PdfOptions WithStreamBufferLimit(long limit)
    {
        StreamBufferLimit = limit;
        return this;
    }

    /// <summary>
    /// Gets or sets the unit a remote file is fetched and cached in, in bytes: every request covers whole blocks. Default 4 KiB.
    /// </summary>
    /// <remarks>
    /// Spec #33, viewer capability 5; ISO 32000-2 §7.5.4 (random access through the cross-reference table). Applies to documents opened
    /// with <see cref="PdfEngine.OpenAsync(System.Net.Http.HttpClient, Uri, CancellationToken)"/>. A small block wastes little around
    /// each small object (a page object is a few hundred bytes); a large object is fetched in one request whatever the block size.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 512 or more than 16 MiB.</exception>
    public int RangeBlockSize
    {
        get => _rangeBlockSize;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 512);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 16 << 20);
            _rangeBlockSize = value;
        }
    }

    /// <summary>Sets <see cref="RangeBlockSize"/>.</summary>
    /// <param name="blockSize">The size of a fetched and cached block, in bytes.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="blockSize"/> is less than 512 or more than 16 MiB.</exception>
    /// <remarks>Spec #33, viewer capability 5.</remarks>
    public PdfOptions WithRangeBlockSize(int blockSize)
    {
        RangeBlockSize = blockSize;
        return this;
    }

    /// <summary>
    /// Gets or sets the most bytes of a remote file a document keeps cached; the least recently used blocks are dropped beyond it and
    /// fetched again when needed. Default 64 MiB.
    /// </summary>
    /// <remarks>
    /// Spec #33, viewer capability 5. Parsed objects are kept by the document whatever this limit; the cache holds the bytes stream data
    /// is read from. Where <see cref="SynchronousRangeFetch"/> is off, a page's objects must fit in the cache between its
    /// <see cref="PdfPage.PrefetchAsync"/> and its reading.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public long RangeCacheLimit
    {
        get => _rangeCacheLimit;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _rangeCacheLimit = value;
        }
    }

    /// <summary>Sets <see cref="RangeCacheLimit"/>.</summary>
    /// <param name="limit">The most bytes of a remote file kept cached.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is not positive.</exception>
    /// <remarks>Spec #33, viewer capability 5.</remarks>
    public PdfOptions WithRangeCacheLimit(long limit)
    {
        RangeCacheLimit = limit;
        return this;
    }

    /// <summary>
    /// Gets or sets whether a synchronous read of a remote document that needs bytes not yet fetched blocks on the request. When off,
    /// the read throws <see cref="PdfRangeNotLoadedException"/>, and a page is read after awaiting its <see cref="PdfPage.PrefetchAsync"/>.
    /// On by default, except on the browser platform (and WASI), which cannot block on HTTP; turning it on there has no effect.
    /// </summary>
    /// <remarks>Spec #33, viewer capability 5 (the asynchronous prefetch boundary).</remarks>
    public bool SynchronousRangeFetch { get; set; } = !OperatingSystem.IsBrowser() && !OperatingSystem.IsWasi();

    /// <summary>Sets <see cref="SynchronousRangeFetch"/>.</summary>
    /// <param name="enabled">Whether a synchronous read may block on fetching what it misses.</param>
    /// <returns>These options.</returns>
    /// <remarks>Spec #33, viewer capability 5.</remarks>
    public PdfOptions WithSynchronousRangeFetch(bool enabled)
    {
        SynchronousRangeFetch = enabled;
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
    /// Spec #33, user story 45. Diagnostics are logged under the category <c>Broadside.PdfDocument</c> with event id 1,
    /// <c>Diagnostic</c>, at <see cref="LogLevel.Warning"/> or <see cref="LogLevel.Error"/> after their severity, with the structured
    /// values <c>Code</c>, <c>Severity</c>, <c>Offset</c>, <c>ObjectReference</c> and <c>Message</c>. In strict mode the deviation is
    /// logged before it is thrown.
    /// </remarks>
    public PdfOptions WithLoggerFactory(ILoggerFactory? loggerFactory)
    {
        LoggerFactory = loggerFactory;
        return this;
    }

    /// <summary>Gets the clock set with <see cref="UseTimeProvider"/>; <see cref="TimeProvider.System"/> otherwise.</summary>
    /// <remarks>Not public, so configuration binding never sees it.</remarks>
    internal TimeProvider TimeProvider { get; private set; } = TimeProvider.System;

    /// <summary>
    /// Uses <paramref name="timeProvider"/> as the clock that dates what the engine writes, such as the modification date a save
    /// records (ISO 32000-2 §14.3.4), in place of <see cref="TimeProvider.System"/>.
    /// </summary>
    /// <param name="timeProvider">The clock; dates are written in its local time zone.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §14.3.3 and §14.3.4. A fixed clock makes saved files reproducible.</remarks>
    public PdfOptions UseTimeProvider(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        TimeProvider = timeProvider;
        return this;
    }

    /// <summary>Gets the colour management set with <see cref="UseColorManagement"/>; <see cref="ManagedColorManagement.Default"/> otherwise.</summary>
    /// <remarks>Not public, so configuration binding never sees it.</remarks>
    internal IColorManagement ColorManagement { get; private set; } = ManagedColorManagement.Default;

    /// <summary>
    /// Uses <paramref name="colorManagement"/> to convert colours of device, CIE-based and ICCBased spaces to device colours, in place
    /// of <see cref="ManagedColorManagement.Default"/>.
    /// </summary>
    /// <param name="colorManagement">The colour management. Shared by every document and thread of the engine.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §8.6.5, §10.3 and §10.4. The colour-management extension point.</remarks>
    public PdfOptions UseColorManagement(IColorManagement colorManagement)
    {
        ArgumentNullException.ThrowIfNull(colorManagement);
        ColorManagement = colorManagement;
        return this;
    }

    /// <summary>Gets the security handlers registered with <see cref="UseSecurityHandler"/>, in order.</summary>
    /// <remarks>Not public, so configuration binding never sees it.</remarks>
    internal IReadOnlyList<ISecurityHandler> SecurityHandlers => _securityHandlers;

    /// <summary>Gets the credentials set by <see cref="WithPassword"/> or <see cref="WithCredentials"/>, or <see langword="null"/>.</summary>
    /// <remarks>Not public, so configuration binding never sees a password.</remarks>
    internal PdfCredentials? Credentials { get; private set; }

    /// <summary>
    /// Uses <paramref name="handler"/> for encrypted documents whose encryption dictionary's <c>Filter</c> is
    /// <see cref="ISecurityHandler.Filter"/>, or whose <c>SubFilter</c> it lists, replacing the default of that name
    /// (<see cref="StandardSecurityHandler"/> for <c>Standard</c>). A later registration of the same name wins.
    /// </summary>
    /// <param name="handler">The handler. Shared by every document and thread of the engine: it must keep no state between calls.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §7.6.2, Table 20. The security handler extension point.</remarks>
    public PdfOptions UseSecurityHandler(ISecurityHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _ = handler.Filter ?? throw new ArgumentException("The security handler has no Filter name.", nameof(handler));
        _securityHandlers.Add(handler);
        return this;
    }

    /// <summary>Offers <paramref name="password"/> to every encrypted document opened without credentials of its own.</summary>
    /// <param name="password">The user or owner password; <see langword="null"/> or empty for the default user password only.</param>
    /// <returns>These options.</returns>
    /// <remarks>
    /// ISO 32000-2 §7.6.4.1. To give each document its own password, pass it to <see cref="PdfEngine.Open(string, PdfCredentials)"/>
    /// instead. A password that opens neither as the user nor as the owner password makes opening throw <see cref="PdfPasswordException"/>.
    /// </remarks>
    public PdfOptions WithPassword(string? password)
    {
        Credentials = string.IsNullOrEmpty(password) ? null : new PdfPassword(password);
        return this;
    }

    /// <summary>Gets the font program parsers registered with <see cref="UseFontProgramParser"/>, in registration order.</summary>
    /// <remarks>ISO 32000-2 §9.9. Not public, so configuration binding never sees it; parsers are code.</remarks>
    internal IReadOnlyList<IFontProgramParser> FontProgramParsers => _fontProgramParsers;

    /// <summary>
    /// Uses <paramref name="parser"/> for embedded font programs, tried before the managed defaults and before parsers registered
    /// earlier: the first parser whose <see cref="IFontProgramParser.CanParse"/> accepts a program's bytes reads it, so a parser for a
    /// format the defaults read replaces them, and a parser for a new format adds it.
    /// </summary>
    /// <param name="parser">The parser. Shared by every document and thread of the engine: it must keep no state between calls.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §9.9. The font program parser extension point; the managed default reads TrueType programs.</remarks>
    public PdfOptions UseFontProgramParser(IFontProgramParser parser)
    {
        ArgumentNullException.ThrowIfNull(parser);
        _fontProgramParsers.Add(parser);
        return this;
    }

    /// <summary>Gets the font resolvers registered with <see cref="UseFontResolver"/>, in registration order.</summary>
    /// <remarks>ISO 32000-2 §9.6.2.2. Not public, so configuration binding never sees it; resolvers are code.</remarks>
    internal IReadOnlyList<IFontResolver> FontResolvers => _fontResolvers;

    /// <summary>Gets the operating system's font resolver, asked after the registered ones; <see langword="null"/> when turned off.</summary>
    /// <remarks>Not public, so configuration binding never sees it.</remarks>
    internal IFontResolver? SystemFontResolver { get; private set; } = Fonts.SystemFontResolver.Shared;

    /// <summary>
    /// Uses <paramref name="resolver"/> to find font programs for fonts that are not embedded, and named font resources (predefined
    /// CMaps, CID-to-Unicode tables). Resolvers are asked in registration order, all before the operating system's fonts; the first
    /// answer wins.
    /// </summary>
    /// <param name="resolver">The resolver. Shared by every document and thread of the engine: it must keep no state between calls.</param>
    /// <returns>These options.</returns>
    /// <remarks>
    /// ISO 32000-2 §9.6.2.2, §9.7.5.2 and §9.8. The font resolver extension point. The Standard 14
    /// fonts package registers its resolver with one call, <c>UseStandard14Fonts()</c>.
    /// </remarks>
    public PdfOptions UseFontResolver(IFontResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _fontResolvers.Add(resolver);
        return this;
    }

    /// <summary>
    /// Uses <paramref name="resolver"/> in place of the operating system's fonts (<see cref="Fonts.SystemFontResolver"/>), asked after
    /// every resolver registered with <see cref="UseFontResolver"/>; <see langword="null"/> asks no operating-system fonts at all, so
    /// output does not depend on the machine.
    /// </summary>
    /// <param name="resolver">The resolver, or <see langword="null"/> for none.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §9.5 NOTE 5 and §9.6.2.2. The default reads the platform's font directories, lazily, on the first font no other resolver has.</remarks>
    public PdfOptions UseSystemFontResolver(IFontResolver? resolver)
    {
        SystemFontResolver = resolver;
        return this;
    }

    /// <summary>
    /// Gets or sets the file where the default operating-system font resolver keeps the index of the font files it has read, so a
    /// later process finds installed fonts without reading their files again; <see langword="null"/> (the default) keeps no index and
    /// writes nothing.
    /// </summary>
    /// <remarks>
    /// The file is created, with its directory, after the first font the operating system's fonts are asked for, and
    /// rewritten when more font files have been read. Font files added, removed or changed since are read again, so the index never
    /// changes which font is found. Engines with the same path share one resolver in the process. Applies only to the default
    /// resolver: one set with <see cref="UseSystemFontResolver"/> keeps its own index (<see cref="Fonts.SystemFontResolver.IndexPath"/>).
    /// </remarks>
    public string? SystemFontIndexPath { get; set; }

    /// <summary>Sets <see cref="SystemFontIndexPath"/>.</summary>
    /// <param name="path">The index file, or <see langword="null"/> to keep none.</param>
    /// <returns>These options.</returns>
    /// <remarks>See <see cref="SystemFontIndexPath"/>.</remarks>
    public PdfOptions WithSystemFontIndexPath(string? path)
    {
        SystemFontIndexPath = path;
        return this;
    }

    /// <summary>Offers <paramref name="credentials"/> to every encrypted document opened without credentials of its own.</summary>
    /// <param name="credentials">The credentials, or <see langword="null"/> for none.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §7.6.4 and §7.6.5: a password for the standard security handler, a certificate for a public-key handler.</remarks>
    public PdfOptions WithCredentials(PdfCredentials? credentials)
    {
        Credentials = credentials;
        return this;
    }
}
