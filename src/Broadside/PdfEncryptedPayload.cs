using System.Buffers;
using Broadside.IO;
using Broadside.Objects;
using Broadside.Security;

namespace Broadside;

/// <summary>
/// The encrypted payload of an unencrypted wrapper document: the embedded encrypted PDF file, the cryptographic filter it needs,
/// and the way to open it as its own <see cref="PdfDocument"/>. A live view over the payload's file specification.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.6.7 (PDF 2.0). A producer that encrypts with a security handler a reader may lack embeds the encrypted file in an
/// unencrypted wrapper: the wrapper's pages tell the user what is needed, and a reader that has the cryptographic filter shows the
/// payload instead. The payload's file specification is the one entry of the <c>EmbeddedFiles</c> name tree, is listed in the
/// catalog's <c>AF</c> array with <c>AFRelationship</c> <c>EncryptedPayload</c>, is named by the collection's <c>D</c> with
/// <c>View</c> <c>H</c>, and carries an encrypted payload dictionary (<c>EP</c>, Table 28).
/// </para>
/// <para>
/// <see cref="Open()"/> decodes the embedded file and opens it with the wrapper's engine: the same security handlers, filters,
/// font resolvers, limits, logging and reading mode. The payload document owns a copy of the bytes, so it outlives the wrapper.
/// </para>
/// </remarks>
public sealed class PdfEncryptedPayload
{
    private readonly PdfDocument _document;

    internal PdfEncryptedPayload(PdfDocument document, string name, PdfFileSpecification file)
    {
        _document = document;
        Name = name;
        File = file;
    }

    /// <summary>Gets the key the payload has in the wrapper's <c>EmbeddedFiles</c> name tree (also the collection's <c>D</c>).</summary>
    /// <remarks>
    /// ISO 32000-2 §7.6.7 and §12.3.5, Table 153. Table 43: the payload's <c>F</c> and <c>UF</c> shall not be derived from its
    /// actual file name, so this names the payload, typically after its cryptographic filter, not the original file.
    /// </remarks>
    public string Name { get; }

    /// <summary>Gets the payload's file specification.</summary>
    /// <remarks>ISO 32000-2 §7.6.7 and §7.11.3, Table 43 (<c>EF</c>, <c>EP</c>, <c>AFRelationship</c>).</remarks>
    public PdfFileSpecification File { get; }

    /// <summary>Gets the encrypted payload dictionary (<c>EP</c>), or <see langword="null"/> when the file specification has none.</summary>
    /// <remarks>ISO 32000-2 §7.6.7, Table 28.</remarks>
    public CosDictionary? Dictionary => File.EncryptedPayload;

    /// <summary>
    /// Gets the name of the cryptographic filter that encrypted the payload (<c>Subtype</c>), or <see langword="null"/> when the
    /// encrypted payload dictionary does not name one.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §7.6.7, Table 28 (required): it lets a reader tell whether it has the filter. The name is compared with the
    /// <c>Filter</c> and <c>SubFilters</c> of the engine's security handlers (<see cref="IsCryptographicFilterAvailable"/>).
    /// </remarks>
    public CosName? CryptographicFilter => Dictionary is { } dictionary ? ViewReading.Name(_document, dictionary, FileAndLayerNames.Subtype) : null;

    /// <summary>Gets the version of the cryptographic filter (<c>Version</c>), as written, or <see langword="null"/>.</summary>
    /// <remarks>
    /// ISO 32000-2 §7.6.7, Table 28 (optional): a name holding integers separated by periods, such as <c>1.0</c>; it is not a real
    /// number, so it is kept as text.
    /// </remarks>
    public string? CryptographicFilterVersion => Dictionary is { } dictionary ? ViewReading.Name(_document, dictionary, KnownNames.Version)?.Value : null;

    /// <summary>
    /// Gets a value indicating whether the wrapper's engine has a security handler whose <c>Filter</c> or one of whose
    /// <c>SubFilters</c> is <see cref="CryptographicFilter"/>.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §7.6.7: a reader with the filter presents the payload; one without presents the wrapper. Handlers are registered
    /// through <see cref="PdfOptions.UseSecurityHandler"/>. The payload's own encryption dictionary decides which handler opens it,
    /// so <see cref="Open()"/> is attempted either way.
    /// </remarks>
    public bool IsCryptographicFilterAvailable => CryptographicFilter is { } filter && _document.Configuration.SecurityHandlers.Provides(filter);

    /// <summary>Opens the payload with the credentials the wrapper's options carry (<see cref="PdfOptions.WithCredentials"/>).</summary>
    /// <returns>The payload document. Dispose it when done; it does not depend on the wrapper.</returns>
    /// <exception cref="InvalidOperationException">The file specification embeds no file (<c>EF</c> holds no stream).</exception>
    /// <exception cref="Diagnostics.DiagnosticException">
    /// The payload cannot be read at all, or, when the wrapper was opened in strict mode, deviates from ISO 32000-2.
    /// </exception>
    /// <exception cref="PdfPasswordException">The payload is encrypted with a password and the credentials do not open it.</exception>
    /// <exception cref="PdfCertificateException">The payload is encrypted for certificate recipients and the credentials do not open it.</exception>
    /// <exception cref="PdfEncryptionNotSupportedException">The payload is encrypted in a way the wrapper's engine cannot decrypt.</exception>
    /// <remarks>ISO 32000-2 §7.6.7, §7.11.4 and §7.6.</remarks>
    public PdfDocument Open() => Read(credentials: null);

    /// <summary>Opens the payload with <paramref name="credentials"/>.</summary>
    /// <param name="credentials">The credentials, such as a <see cref="PdfPassword"/>; they override those of the wrapper's options.</param>
    /// <returns>The payload document. Dispose it when done; it does not depend on the wrapper.</returns>
    /// <exception cref="InvalidOperationException">The file specification embeds no file (<c>EF</c> holds no stream).</exception>
    /// <exception cref="Diagnostics.DiagnosticException">
    /// The payload cannot be read at all, or, when the wrapper was opened in strict mode, deviates from ISO 32000-2.
    /// </exception>
    /// <exception cref="PdfPasswordException">The payload is encrypted with a password and the credentials do not open it.</exception>
    /// <exception cref="PdfCertificateException">The payload is encrypted for certificate recipients and the credentials do not open it.</exception>
    /// <exception cref="PdfEncryptionNotSupportedException">The payload is encrypted in a way the wrapper's engine cannot decrypt.</exception>
    /// <remarks>ISO 32000-2 §7.6.7, §7.11.4 and §7.6.</remarks>
    public PdfDocument Open(PdfCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        return Read(credentials);
    }

    /// <inheritdoc/>
    public override string ToString() => Name;

    private PdfDocument Read(PdfCredentials? credentials)
    {
        PdfEmbeddedFile file = File.EmbeddedFile
            ?? throw new InvalidOperationException("The encrypted payload's file specification embeds no file (ISO 32000-2 §7.6.7: the EF dictionary is required).");

        // Decoded into a buffer of its own: an unfiltered stream could otherwise be a view of the wrapper's mapped file.
        var bytes = new ArrayBufferWriter<byte>();
        file.Decode(bytes);
        return PdfDocument.Read(PdfSource.FromMemory(bytes.WrittenMemory), _document.Configuration, credentials);
    }
}
