using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.IO;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Security;

/// <summary>Opens the encryption of a document while it opens: handler, authentication, decryption hooks, integrity check.</summary>
/// <remarks>
/// ISO 32000-2 §7.6.2: a document is encrypted when its trailer has an <c>Encrypt</c> entry. Runs after the cross-reference
/// information is read and before the catalog loads, so the encryption dictionary (and anything it refers to) loads undecrypted and
/// every object loaded afterwards is decrypted. The newest trailer's <c>Encrypt</c> and <c>ID</c> apply (§7.6.2: the entry "should be
/// identical in all copies of the trailer").
/// <para>
/// A document whose strings and streams are not encrypted (<c>StmF</c> and <c>StrF</c> <c>Identity</c>) and whose embedded files
/// use a crypt filter with <c>AuthEvent /EFOpen</c> opens without the user password (§7.6.5, Table 25: the user is authenticated
/// "when accessing embedded files"); its embedded files then stay encrypted, each with a <c>CryptFilterNotAuthorized</c>
/// Information diagnostic when it loads.
/// </para>
/// </remarks>
internal static class DocumentSecurity
{
    private static readonly CosName SubFilterName = new("SubFilter");
    private static readonly CosName StandardName = new("Standard");
    private static readonly CosName R = new("R");
    private static readonly CosName P = new("P");
    private static readonly CosName CF = new("CF");
    private static readonly CosName StmF = new("StmF");
    private static readonly CosName StrF = new("StrF");
    private static readonly CosName EFF = new("EFF");
    private static readonly CosName AuthEvent = new("AuthEvent");
    private static readonly CosName EFOpen = new("EFOpen");

    /// <summary>Reads the trailer's <c>Encrypt</c> entry and, for an encrypted document, installs decryption on the loader and pipeline.</summary>
    /// <param name="source">The file.</param>
    /// <param name="loader">The document's loader; its decryptor hook is set.</param>
    /// <param name="streams">The document's pipeline; its crypt filter slot is set.</param>
    /// <param name="handlers">The engine's security handlers.</param>
    /// <param name="credentials">The credentials offered, or <see langword="null"/>.</param>
    /// <param name="diagnostics">Where to report deviations.</param>
    /// <param name="reportUnusable">
    /// Whether an <c>Encrypt</c> entry that does not resolve to a dictionary is reported and the document read as not encrypted;
    /// otherwise it sets <paramref name="unusable"/> so the caller can rebuild the cross-reference information first.
    /// </param>
    /// <param name="unusable">Set when the <c>Encrypt</c> entry does not resolve and <paramref name="reportUnusable"/> is false.</param>
    /// <returns>The document's security, or <see langword="null"/> when it is not encrypted.</returns>
    /// <exception cref="PdfPasswordException">The password does not open the document.</exception>
    /// <exception cref="PdfCertificateException">The certificates do not open the document.</exception>
    /// <exception cref="PdfEncryptionNotSupportedException">No handler or method for the document's encryption.</exception>
    public static PdfSecurity? Open(
        PdfSource source,
        ObjectLoader loader,
        StreamDecoder streams,
        SecurityHandlerRegistry handlers,
        PdfCredentials? credentials,
        DiagnosticSink diagnostics,
        bool reportUnusable,
        out bool unusable)
    {
        unusable = false;
        CosDictionary trailer = loader.CrossReference.Trailer;
        if (!trailer.TryGetValue(KnownNames.Encrypt, out CosObject? entry))
        {
            return null;
        }

        if (loader.Resolve(entry) is not CosDictionary encryption)
        {
            if (!reportUnusable)
            {
                unusable = true;
                return null;
            }

            diagnostics.Report(
                DiagnosticCodes.EncryptDictionaryInvalid,
                DiagnosticSeverity.Error,
                "The trailer's Encrypt entry is not a dictionary; the document is read as not encrypted.");
            return null;
        }

        CosName? filter = loader.Resolve(encryption.TryGetValue(FilterNames.Filter, out CosObject? named) ? named : null) as CosName;
        CosName? subFilter = loader.Resolve(encryption.TryGetValue(SubFilterName, out CosObject? sub) ? sub : null) as CosName;
        if (filter is null && encryption.ContainsKey(R))
        {
            diagnostics.Report(
                DiagnosticCodes.EncryptDictionaryInvalid,
                DiagnosticSeverity.Warning,
                "The encryption dictionary has no Filter entry; its R entry marks it as the standard security handler's.");
            filter = StandardName;
        }

        ISecurityHandler handler = handlers.Find(filter, subFilter) ?? throw new PdfEncryptionNotSupportedException(
            PdfEncryptionNotSupportedReason.Handler,
            $"The document is encrypted by the security handler /{filter?.Value ?? "(none)"}{(subFilter is null ? string.Empty : $" (format /{subFilter.Value})")}, which no registered handler implements.");

        int version = loader.Resolve(encryption.TryGetValue(KnownNames.V, out CosObject? v) ? v : null) is CosInteger { Value: >= 0 and <= int.MaxValue } integer
            ? (int)integer.Value
            : 0;
        ReadOnlyMemory<byte> documentId = ReadDocumentId(trailer, diagnostics);
        var context = new SecurityHandlerContext(encryption, documentId, credentials, diagnostics, loader.Resolve);
        SecurityHandlerResult result;
        try
        {
            result = handler.Authenticate(context);
        }
        catch (PdfPasswordException) when (version >= 4 && OnlyEmbeddedFilesNeedThePassword(encryption, loader.Resolve))
        {
            return OpenWithoutAuthentication(source, loader, streams, encryption, handler.Filter, subFilter, version, diagnostics);
        }

        if (version == 0)
        {
            // No V: the standard handler's revision implies it; another handler's key length does (RC4 below 256 bits, AES-256 at 256).
            version = result.Revision switch
            {
                2 => 1,
                3 => 2,
                4 => 4,
                7 => 6,
                null when result.FileEncryptionKey.Length < 32 => 2,
                _ => 5,
            };
        }

        DocumentDecryptor decryptor = DocumentDecryptor.Create(encryption, version, result, diagnostics, loader.Resolve);
        loader.Hooks.Decryptor = decryptor;
        streams.CryptFilter = decryptor;

        PdfIntegrityStatus integrity = IntegrityVerifier.Verify(source, loader, version, result.RawPermissions, result.FileEncryptionKey.Span, diagnostics);
        return new PdfSecurity(
            encryption,
            handler.Filter,
            subFilter,
            version,
            result.Revision,
            result.FileEncryptionKey.Length * 8,
            result.Access,
            result.Permissions,
            result.RawPermissions,
            decryptor.EncryptMetadata,
            integrity);
    }

    /// <summary>
    /// Whether nothing but embedded files is encrypted, with a crypt filter that authenticates when they are accessed: <c>StmF</c>
    /// and <c>StrF</c> are <c>Identity</c> (or absent, Table 20) and <c>EFF</c> names a <c>CF</c> filter whose <c>AuthEvent</c> is
    /// <c>EFOpen</c> (Table 25).
    /// </summary>
    private static bool OnlyEmbeddedFilesNeedThePassword(CosDictionary encryption, Func<CosObject?, CosObject> resolve)
    {
        bool IsIdentity(CosName key) =>
            resolve(encryption.TryGetValue(key, out CosObject? entry) ? entry : null) is CosNull or CosName { Value: "Identity" };

        return IsIdentity(StmF)
            && IsIdentity(StrF)
            && resolve(encryption.TryGetValue(EFF, out CosObject? eff) ? eff : null) is CosName embedded
            && resolve(encryption.TryGetValue(CF, out CosObject? cf) ? cf : null) is CosDictionary filters
            && resolve(filters.TryGetValue(embedded, out CosObject? definition) ? definition : null) is CosDictionary filter
            && EFOpen.Equals(resolve(filter.TryGetValue(AuthEvent, out CosObject? authEvent) ? authEvent : null));
    }

    /// <summary>
    /// Installs decryption for a document opened without its user password: every crypt filter is locked, so embedded files stay
    /// encrypted; the permissions are the stated <c>P</c>, unverified without the file encryption key.
    /// </summary>
    private static PdfSecurity OpenWithoutAuthentication(
        PdfSource source,
        ObjectLoader loader,
        StreamDecoder streams,
        CosDictionary encryption,
        CosName handler,
        CosName? subFilter,
        int version,
        DiagnosticSink diagnostics)
    {
        DocumentDecryptor decryptor = DocumentDecryptor.Create(encryption, version, result: null, diagnostics, loader.Resolve);
        loader.Hooks.Decryptor = decryptor;
        streams.CryptFilter = decryptor;

        int? revision = loader.Resolve(encryption.TryGetValue(R, out CosObject? r) ? r : null) is CosInteger { Value: >= 0 and <= int.MaxValue } rValue
            ? (int)rValue.Value
            : null;
        int rawPermissions = loader.Resolve(encryption.TryGetValue(P, out CosObject? p) ? p : null) is CosInteger pValue ? unchecked((int)pValue.Value) : 0;
        int keyLength = loader.Resolve(encryption.TryGetValue(KnownNames.Length, out CosObject? length) ? length : null) is CosInteger { Value: > 0 and <= 4096 } bits
            ? (int)bits.Value
            : 0;
        PdfIntegrityStatus integrity = IntegrityVerifier.Verify(source, loader, version, rawPermissions, ReadOnlySpan<byte>.Empty, diagnostics);
        return new PdfSecurity(
            encryption,
            handler,
            subFilter,
            version,
            revision,
            keyLength,
            PdfAccessLevel.User,
            StandardSecurityHandler.UserPermissions(rawPermissions, revision ?? 0),
            rawPermissions,
            decryptor.EncryptMetadata,
            integrity);
    }

    /// <summary>
    /// Checks, once the catalog is loaded, that a document using ISO/TS 32003 or ISO/TS 32004 declares the extension (their clause 4:
    /// the developer extensions dictionary "shall be included").
    /// </summary>
    /// <param name="security">The document's security.</param>
    /// <param name="catalog">The catalog.</param>
    /// <param name="resolve">Resolves references.</param>
    /// <param name="diagnostics">Where to report deviations.</param>
    public static void CheckExtensions(PdfSecurity security, CosDictionary catalog, Func<CosObject?, CosObject> resolve, DiagnosticSink diagnostics)
    {
        if (security.Version >= 6)
        {
            CheckExtension(32003, catalog, resolve, diagnostics);
        }

        if (security.Integrity != PdfIntegrityStatus.None)
        {
            CheckExtension(32004, catalog, resolve, diagnostics);
        }
    }

    private static void CheckExtension(int level, CosDictionary catalog, Func<CosObject?, CosObject> resolve, DiagnosticSink diagnostics)
    {
        if (resolve(catalog.TryGetValue(KnownNames.Extensions, out CosObject? extensions) ? extensions : null) is CosDictionary dictionary
            && resolve(dictionary.TryGetValue(KnownNames.IsoPrefix, out CosObject? iso) ? iso : null) is CosObject entries)
        {
            IEnumerable<CosObject> list = entries is CosArray array ? array : [entries];
            foreach (CosObject item in list)
            {
                if (resolve(item) is CosDictionary extension
                    && resolve(extension.TryGetValue(KnownNames.ExtensionLevel, out CosObject? value) ? value : null) is CosInteger { Value: var found }
                    && found == level)
                {
                    return;
                }
            }
        }

        diagnostics.Report(
            DiagnosticCodes.EncryptionExtensionMissing,
            DiagnosticSeverity.Warning,
            string.Create(CultureInfo.InvariantCulture, $"The document uses ISO/TS {level} but its catalog's Extensions dictionary has no ISO_ entry with ExtensionLevel {level} (clause 4); it is read anyway."));
    }

    /// <summary>The first element of the trailer's <c>ID</c> array (§7.5.5 Table 15: required when the document is encrypted).</summary>
    private static ReadOnlyMemory<byte> ReadDocumentId(CosDictionary trailer, DiagnosticSink diagnostics)
    {
        if (trailer.TryGetValue(KnownNames.ID, out CosObject? id) && id is CosArray { Count: >= 1 } array && array[0] is CosString first)
        {
            return first.Bytes.ToArray();
        }

        diagnostics.Report(
            DiagnosticCodes.EncryptionIdMissing,
            DiagnosticSeverity.Warning,
            "The trailer of an encrypted document shall have an ID array of two strings; an empty identifier is used.");
        return ReadOnlyMemory<byte>.Empty;
    }
}
