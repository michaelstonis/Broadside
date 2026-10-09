using Broadside.IO;

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
        return PdfDocument.Read(PdfSource.FromStream(stream), _configuration);
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
