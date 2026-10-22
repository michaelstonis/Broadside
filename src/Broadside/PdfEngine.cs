using Broadside.IO;
using Broadside.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Broadside;

/// <summary>
/// Opens and creates documents under one configuration. The static <see cref="PdfDocument.Open(string)"/> methods use an engine
/// too, so both paths behave identically.
/// </summary>
/// <remarks>
/// An engine is immutable and safe to share between threads; it holds no global or static state, so two engines with different
/// options never interfere. Its options are copied at construction.
/// </remarks>
public sealed class PdfEngine
{
    private readonly EngineConfiguration _configuration;

    /// <summary>Initializes a new instance of the <see cref="PdfEngine"/> class with default options.</summary>
    public PdfEngine()
        : this(new PdfOptions())
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfEngine"/> class.</summary>
    /// <param name="options">The options; copied, so later changes to them do not affect this engine.</param>
    public PdfEngine(PdfOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _configuration = EngineConfiguration.From(options);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfEngine"/> class from the options pattern: the constructor dependency injection
    /// uses, which <see cref="Microsoft.Extensions.DependencyInjection.BroadsideServiceCollectionExtensions.AddBroadside(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>
    /// registers.
    /// </summary>
    /// <param name="options">
    /// The options. Their <see cref="IOptions{TOptions}.Value"/> is read once, here, and copied, so the engine behaves exactly like
    /// one built with <see cref="PdfEngine(PdfOptions)"/> from the same values.
    /// </param>
    /// <param name="loggerFactory">
    /// Where diagnostics are logged, unless the options name their own logger factory through
    /// <see cref="PdfOptions.WithLoggerFactory"/>; <see langword="null"/> logs nothing.
    /// </param>
    /// <remarks>Spec #33, user stories 43 and 45: one engine for the fluent and the hosted paths.</remarks>
    public PdfEngine(IOptions<PdfOptions> options, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        PdfOptions value = options.Value ?? throw new ArgumentException("The options have no value.", nameof(options));
        _configuration = EngineConfiguration.From(value, loggerFactory);
    }

    /// <summary>Gets the engine the static entry points use when no options are given: default options, no state.</summary>
    internal static PdfEngine Default { get; } = new();

    /// <summary>Gets the engine's configuration, for documents made from others (<see cref="PdfDocument.Merge"/>).</summary>
    internal EngineConfiguration Configuration => _configuration;

    /// <summary>Opens the PDF file at <paramref name="path"/>.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The document. Dispose it when done.</returns>
    /// <exception cref="Diagnostics.DiagnosticException">
    /// The file cannot be read at all, or, in strict mode, deviates from ISO 32000-2.
    /// </exception>
    /// <remarks>ISO 32000-2 §7.5.</remarks>
    public PdfDocument Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return PdfDocument.Read(PdfSource.FromFile(path), _configuration);
    }

    /// <summary>Opens the PDF file held by <paramref name="stream"/>, from its current position.</summary>
    /// <param name="stream">The stream. It is not disposed; keep it open and unchanged until the document is disposed.</param>
    /// <returns>The document. Dispose it when done.</returns>
    /// <exception cref="Diagnostics.DiagnosticException">
    /// The file cannot be read at all, or, in strict mode, deviates from ISO 32000-2.
    /// </exception>
    /// <remarks>ISO 32000-2 §7.5.</remarks>
    public PdfDocument Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return PdfDocument.Read(PdfSource.FromStream(stream, _configuration.StreamBufferLimit), _configuration);
    }

    /// <summary>Opens the PDF file held by <paramref name="stream"/>, from its current position, reading a non-seekable stream asynchronously.</summary>
    /// <param name="stream">
    /// The stream. It is not disposed. A seekable stream is read in place as objects are used: keep it open and unchanged until the
    /// document is disposed. A non-seekable stream is read to its end, asynchronously, before the file is parsed.
    /// </param>
    /// <returns>The document. Dispose it when done.</returns>
    /// <exception cref="Diagnostics.DiagnosticException">
    /// The file cannot be read at all, or, in strict mode, deviates from ISO 32000-2.
    /// </exception>
    /// <remarks>ISO 32000-2 §7.5. As <see cref="OpenAsync(Stream, CancellationToken)"/>, without cancellation.</remarks>
    public Task<PdfDocument> OpenAsync(Stream stream) => OpenAsync(stream, CancellationToken.None);

    /// <summary>Opens the PDF file held by <paramref name="stream"/>, from its current position, reading a non-seekable stream asynchronously.</summary>
    /// <param name="stream">
    /// The stream. It is not disposed. A seekable stream is read in place as objects are used: keep it open and unchanged until the
    /// document is disposed. A non-seekable stream is read to its end, asynchronously, before the file is parsed.
    /// </param>
    /// <param name="cancellationToken">Cancels reading a non-seekable stream.</param>
    /// <returns>The document. Dispose it when done.</returns>
    /// <exception cref="Diagnostics.DiagnosticException">
    /// The file cannot be read at all, or, in strict mode, deviates from ISO 32000-2.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <remarks>
    /// ISO 32000-2 §7.5. Reading is synchronous once the bytes are available (spec #33: <c>Async</c> only at the open and save
    /// boundaries), so this differs from <see cref="Open(Stream)"/> only in how a non-seekable stream is copied.
    /// </remarks>
    public async Task<PdfDocument> OpenAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        PdfSource source = await PdfSource.FromStreamAsync(stream, _configuration.StreamBufferLimit, cancellationToken).ConfigureAwait(false);
        return PdfDocument.Read(source, _configuration);
    }

    /// <summary>Opens the remote PDF file at <paramref name="uri"/> through HTTP range requests.</summary>
    /// <param name="httpClient">The client the requests go through; not disposed. It stays in use until the document is disposed.</param>
    /// <param name="uri">The file's address.</param>
    /// <returns>The document. Dispose it when done.</returns>
    /// <exception cref="Diagnostics.DiagnosticException">
    /// The file cannot be read at all, or, in strict mode, deviates from ISO 32000-2.
    /// </exception>
    /// <exception cref="HttpRequestException">The server answered with an error status, or could not be reached.</exception>
    /// <exception cref="IOException">The server's answers are inconsistent: the file changed while it was read, for instance.</exception>
    /// <remarks>Spec #33, viewer capability 5. As <see cref="OpenAsync(HttpClient, Uri, CancellationToken)"/>, without cancellation.</remarks>
    public Task<PdfDocument> OpenAsync(HttpClient httpClient, Uri uri) => OpenAsync(httpClient, uri, CancellationToken.None);

    /// <summary>
    /// Opens the remote PDF file at <paramref name="uri"/> through HTTP range requests: the end of the file first, then only the byte
    /// ranges of the objects in use. A server that does not serve ranges sends the whole file, which is then read like a stream.
    /// </summary>
    /// <param name="httpClient">The client the requests go through; not disposed. It stays in use until the document is disposed.</param>
    /// <param name="uri">The file's address.</param>
    /// <param name="cancellationToken">Cancels the requests made while opening.</param>
    /// <returns>
    /// The document, with its structure, catalog and page tree fetched. Dispose it when done. Before reading a page synchronously
    /// where <see cref="PdfOptions.SynchronousRangeFetch"/> is off (always on the browser), await <see cref="PdfPage.PrefetchAsync"/>.
    /// </returns>
    /// <exception cref="Diagnostics.DiagnosticException">
    /// The file cannot be read at all, or, in strict mode, deviates from ISO 32000-2.
    /// </exception>
    /// <exception cref="HttpRequestException">The server answered with an error status, or could not be reached.</exception>
    /// <exception cref="IOException">The server's answers are inconsistent: the file changed while it was read, for instance.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <remarks>
    /// ISO 32000-2 §7.5.1 and §7.5.4 (random access through the cross-reference table, read from the end of the file); spec #33,
    /// viewer capability 5; RFC 9110 §14 (range requests). Reading stays synchronous; fetching is asynchronous only at open and prefetch. Bytes are fetched in blocks
    /// of <see cref="PdfOptions.RangeBlockSize"/> and kept up to <see cref="PdfOptions.RangeCacheLimit"/>. Credentials for an
    /// encrypted file are taken from the options.
    /// </remarks>
    public Task<PdfDocument> OpenAsync(HttpClient httpClient, Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(uri);
        return PdfDocument.OpenRemoteAsync(httpClient, uri, _configuration, cancellationToken);
    }

    /// <summary>Opens the PDF file held in <paramref name="bytes"/>.</summary>
    /// <param name="bytes">The file. Not copied: do not change it until the document is disposed.</param>
    /// <returns>The document. Dispose it when done.</returns>
    /// <exception cref="Diagnostics.DiagnosticException">
    /// The file cannot be read at all, or, in strict mode, deviates from ISO 32000-2.
    /// </exception>
    /// <remarks>ISO 32000-2 §7.5.</remarks>
    public PdfDocument Open(ReadOnlyMemory<byte> bytes) => PdfDocument.Read(PdfSource.FromMemory(bytes), _configuration);

    /// <summary>Opens the encrypted PDF file at <paramref name="path"/> with <paramref name="credentials"/>.</summary>
    /// <param name="path">The file path.</param>
    /// <param name="credentials">The credentials, such as a <see cref="PdfPassword"/>; they override those set on the options.</param>
    /// <returns>The document. Dispose it when done.</returns>
    /// <exception cref="Diagnostics.DiagnosticException">
    /// The file cannot be read at all, or, in strict mode, deviates from ISO 32000-2.
    /// </exception>
    /// <exception cref="PdfPasswordException">The document is encrypted with a password and the credentials do not open it.</exception>
    /// <exception cref="PdfCertificateException">The document is encrypted for certificate recipients and the credentials do not open it.</exception>
    /// <exception cref="PdfEncryptionNotSupportedException">The document is encrypted in a way this engine cannot decrypt.</exception>
    /// <remarks>ISO 32000-2 §7.5 and §7.6. For an unencrypted file the credentials are ignored.</remarks>
    public PdfDocument Open(string path, PdfCredentials credentials)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(credentials);
        return PdfDocument.Read(PdfSource.FromFile(path), _configuration, credentials);
    }

    /// <summary>Opens the encrypted PDF file held by <paramref name="stream"/>, from its current position, with <paramref name="credentials"/>.</summary>
    /// <param name="stream">The stream. It is not disposed; keep it open and unchanged until the document is disposed.</param>
    /// <param name="credentials">The credentials, such as a <see cref="PdfPassword"/>; they override those set on the options.</param>
    /// <returns>The document. Dispose it when done.</returns>
    /// <exception cref="Diagnostics.DiagnosticException">
    /// The file cannot be read at all, or, in strict mode, deviates from ISO 32000-2.
    /// </exception>
    /// <exception cref="PdfPasswordException">The document is encrypted with a password and the credentials do not open it.</exception>
    /// <exception cref="PdfCertificateException">The document is encrypted for certificate recipients and the credentials do not open it.</exception>
    /// <exception cref="PdfEncryptionNotSupportedException">The document is encrypted in a way this engine cannot decrypt.</exception>
    /// <remarks>ISO 32000-2 §7.5 and §7.6. For an unencrypted file the credentials are ignored.</remarks>
    public PdfDocument Open(Stream stream, PdfCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(credentials);
        return PdfDocument.Read(PdfSource.FromStream(stream, _configuration.StreamBufferLimit), _configuration, credentials);
    }

    /// <summary>Opens the encrypted PDF file held in <paramref name="bytes"/> with <paramref name="credentials"/>.</summary>
    /// <param name="bytes">The file. Not copied: do not change it until the document is disposed.</param>
    /// <param name="credentials">The credentials, such as a <see cref="PdfPassword"/>; they override those set on the options.</param>
    /// <returns>The document. Dispose it when done.</returns>
    /// <exception cref="Diagnostics.DiagnosticException">
    /// The file cannot be read at all, or, in strict mode, deviates from ISO 32000-2.
    /// </exception>
    /// <exception cref="PdfPasswordException">The document is encrypted with a password and the credentials do not open it.</exception>
    /// <exception cref="PdfCertificateException">The document is encrypted for certificate recipients and the credentials do not open it.</exception>
    /// <exception cref="PdfEncryptionNotSupportedException">The document is encrypted in a way this engine cannot decrypt.</exception>
    /// <remarks>ISO 32000-2 §7.5 and §7.6. For an unencrypted file the credentials are ignored.</remarks>
    public PdfDocument Open(ReadOnlyMemory<byte> bytes, PdfCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        return PdfDocument.Read(PdfSource.FromMemory(bytes), _configuration, credentials);
    }

    /// <summary>Creates a new document with one empty US Letter page (612 by 792 points).</summary>
    /// <returns>The document. Dispose it when done.</returns>
    /// <remarks>
    /// ISO 32000-2 §7.7.2 (catalog), §7.7.3 (page tree) and §7.5.2: the document is PDF 2.0, has a catalog, a page tree root and one
    /// page with empty resources and no content, and reads like a file of those three objects.
    /// </remarks>
    public PdfDocument Create() => PdfDocument.CreateNew(_configuration);
}
