using Broadside.IO;
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
    public async Task<PdfDocument> OpenAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        PdfSource source = await PdfSource.FromStreamAsync(stream, _configuration.StreamBufferLimit, cancellationToken).ConfigureAwait(false);
        return PdfDocument.Read(source, _configuration);
    }

    /// <summary>Opens the PDF file held in <paramref name="bytes"/>.</summary>
    /// <param name="bytes">The file. Not copied: do not change it until the document is disposed.</param>
    /// <returns>The document. Dispose it when done.</returns>
    /// <exception cref="Diagnostics.DiagnosticException">
    /// The file cannot be read at all, or, in strict mode, deviates from ISO 32000-2.
    /// </exception>
    /// <remarks>ISO 32000-2 §7.5.</remarks>
    public PdfDocument Open(ReadOnlyMemory<byte> bytes) => PdfDocument.Read(PdfSource.FromMemory(bytes), _configuration);

    /// <summary>Creates a new, empty document.</summary>
    /// <returns>The document.</returns>
    /// <exception cref="NotSupportedException">Always, for now: writing arrives in a later version.</exception>
    public PdfDocument Create() => throw new NotSupportedException("Creating a document is not implemented yet.");
}
