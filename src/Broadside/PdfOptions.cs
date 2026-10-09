using Broadside.Filters;
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

    private readonly List<IStreamFilter> _filters = [];
    private readonly List<ISecurityHandler> _securityHandlers = [];
    private long _maxDecodedStreamLength = DefaultMaxDecodedStreamLength;
    private long _streamBufferLimit = DefaultStreamBufferLimit;

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
    /// <remarks>ISO 32000-2 §7.4.1. The filter extension point (ADR 0001).</remarks>
    public PdfOptions UseFilter(IStreamFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        CosName name = filter.Name ?? throw new ArgumentException("The filter has no name.", nameof(filter));
        if (name.Equals(FilterNames.Crypt) || FilterNames.TryExpandAbbreviation(name, out _))
        {
            throw new ArgumentException(
                $"A filter cannot be registered under /{name.Value}: Crypt belongs to the security handler and abbreviations are read as full names.",
                nameof(filter));
        }

        _filters.Add(filter);
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
    /// <remarks>ISO 32000-2 §7.6.2, Table 20. The security handler extension point (ADR 0001).</remarks>
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
