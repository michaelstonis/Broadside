using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Security;

/// <summary>
/// A security handler: given a document's encryption dictionary and the reader's credentials, it authenticates them and yields the
/// file encryption key and the permissions. The extension point every encrypted document opens through.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.6.2 (Table 20) and §7.6.3. A handler is registered with <see cref="PdfOptions.UseSecurityHandler"/>; the engine
/// picks the one whose <see cref="Filter"/> is the document's <c>Filter</c>, else one that lists the document's <c>SubFilter</c> in
/// <see cref="SubFilters"/>. <see cref="StandardSecurityHandler"/> is registered by default; registering another handler for
/// <c>Standard</c> replaces it.
/// </para>
/// <para>
/// The handler only authenticates. The engine applies the encryption the dictionary describes (<c>V</c>, <c>Length</c>, <c>CF</c>,
/// <c>StmF</c>, <c>StrF</c>, <c>EFF</c>, <c>EncryptMetadata</c>) with the key the handler returns: Algorithm 1 (RC4 and AES-128 with
/// per-object keys), Algorithm 1.A (AES-256) and AES-GCM (ISO/TS 32003), the crypt filters of §7.6.6, and the integrity MAC of
/// ISO/TS 32004. Handlers are shared by every document an engine opens, from any thread: keep them stateless.
/// </para>
/// </remarks>
public interface ISecurityHandler
{
    /// <summary>Gets the handler name a document's <c>Filter</c> entry selects it by, such as <c>Standard</c>.</summary>
    CosName Filter { get; }

    /// <summary>Gets the <c>SubFilter</c> formats the handler implements, by which it may open documents another handler encrypted.</summary>
    IReadOnlyCollection<CosName> SubFilters { get; }

    /// <summary>Authenticates the credentials against the encryption dictionary.</summary>
    /// <param name="context">The encryption dictionary, the file identifier, the credentials and the diagnostics.</param>
    /// <returns>The file encryption key, the access level and the permissions.</returns>
    /// <exception cref="PdfPasswordException">The password does not open the document.</exception>
    /// <exception cref="PdfCertificateException">The certificates do not open the document.</exception>
    /// <exception cref="PdfEncryptionNotSupportedException">The handler does not implement what the dictionary asks for.</exception>
    /// <exception cref="DiagnosticException">The dictionary is too damaged to authenticate against.</exception>
    SecurityHandlerResult Authenticate(SecurityHandlerContext context);
}

/// <summary>What a security handler authenticates against.</summary>
/// <remarks>ISO 32000-2 §7.6.2. Created by the engine for one document while it opens.</remarks>
public sealed class SecurityHandlerContext
{
    private readonly DiagnosticSink _diagnostics;
    private readonly Func<CosObject?, CosObject> _resolve;

    internal SecurityHandlerContext(
        CosDictionary encryptionDictionary,
        ReadOnlyMemory<byte> documentId,
        PdfCredentials? credentials,
        DiagnosticSink diagnostics,
        Func<CosObject?, CosObject> resolve)
    {
        EncryptionDictionary = encryptionDictionary;
        DocumentId = documentId;
        Credentials = credentials;
        _diagnostics = diagnostics;
        _resolve = resolve;
    }

    /// <summary>Gets the encryption dictionary. Its strings are never encrypted (§7.6.2).</summary>
    public CosDictionary EncryptionDictionary { get; }

    /// <summary>Gets the first element of the trailer's <c>ID</c> array, or empty when the trailer has none.</summary>
    /// <remarks>ISO 32000-2 §7.5.5, Table 15; Algorithm 2 step e.</remarks>
    public ReadOnlyMemory<byte> DocumentId { get; }

    /// <summary>Gets the credentials the reader offered, or <see langword="null"/> for none (try the default user password).</summary>
    public PdfCredentials? Credentials { get; }

    /// <summary>Gets the reading mode; in strict mode <see cref="Report"/> throws.</summary>
    public PdfReadingMode ReadingMode => _diagnostics.IsStrict ? PdfReadingMode.Strict : PdfReadingMode.Lenient;

    /// <summary>Records a deviation found in the encryption dictionary. In strict mode, throws it as a <see cref="DiagnosticException"/>.</summary>
    /// <param name="code">A PascalCase code.</param>
    /// <param name="severity">The severity.</param>
    /// <param name="message">What deviates and what was done about it.</param>
    public void Report(string code, DiagnosticSeverity severity, string message) => _diagnostics.Report(code, severity, message);

    /// <summary>Returns <paramref name="value"/>, or the object it refers to, read without decryption.</summary>
    /// <param name="value">An entry of the encryption dictionary, or <see langword="null"/>.</param>
    /// <returns>The direct object; <see cref="CosNull"/> for an absent entry.</returns>
    public CosObject Resolve(CosObject? value) => _resolve(value);
}

/// <summary>What a security handler returns when the credentials open the document.</summary>
/// <remarks>ISO 32000-2 §7.6.3: the file encryption key the handler computes, from which every string and stream key derives.</remarks>
public sealed class SecurityHandlerResult
{
    private readonly byte[] _fileEncryptionKey;

    /// <summary>Initializes a new instance of the <see cref="SecurityHandlerResult"/> class.</summary>
    /// <param name="fileEncryptionKey">The file encryption key, copied.</param>
    /// <param name="access">Which credential opened the document.</param>
    /// <param name="permissions">The permissions that apply to that access (<see cref="PdfPermissions.All"/> for the owner).</param>
    /// <param name="rawPermissions">The permissions word as the document states it.</param>
    public SecurityHandlerResult(ReadOnlySpan<byte> fileEncryptionKey, PdfAccessLevel access, PdfPermissions permissions, int rawPermissions)
    {
        if (fileEncryptionKey.IsEmpty)
        {
            throw new ArgumentException("The file encryption key cannot be empty.", nameof(fileEncryptionKey));
        }

        _fileEncryptionKey = fileEncryptionKey.ToArray();
        Access = access;
        Permissions = permissions;
        RawPermissions = rawPermissions;
    }

    /// <summary>Gets the file encryption key.</summary>
    public ReadOnlyMemory<byte> FileEncryptionKey => _fileEncryptionKey;

    /// <summary>Gets which credential opened the document.</summary>
    public PdfAccessLevel Access { get; }

    /// <summary>Gets the permissions that apply.</summary>
    public PdfPermissions Permissions { get; }

    /// <summary>Gets the permissions word as the document states it.</summary>
    public int RawPermissions { get; }

    /// <summary>Gets the handler's revision, for the standard security handler the <c>R</c> entry; otherwise <see langword="null"/>.</summary>
    public int? Revision { get; init; }

    /// <summary>
    /// Gets keys for named crypt filters that do not use the file encryption key, such as a public-key handler's crypt filters with
    /// their own recipients (§7.6.6, Table 27); <see langword="null"/> when every crypt filter uses the file encryption key. An empty
    /// key marks a crypt filter the credentials are not authorized for: streams that name it stay encrypted (§7.6.6).
    /// </summary>
    public IReadOnlyDictionary<CosName, ReadOnlyMemory<byte>>? CryptFilterKeys { get; init; }
}
