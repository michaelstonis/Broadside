using System.Globalization;
using Broadside.Objects;

namespace Broadside.Writing;

/// <summary>
/// The developer extensions dictionary an ISO/TS feature declares in the catalog's <c>Extensions</c> dictionary, as an element of
/// the array under its prefix (ISO 32000-2 §7.12, Tables 48 and 49).
/// </summary>
/// <param name="Prefix">The registered prefix, such as <c>ISO_</c> (Annex E).</param>
/// <param name="BaseVersion">The version the extension applies to (§7.12.4).</param>
/// <param name="ExtensionLevel">The extension level (§7.12.5).</param>
/// <param name="Revision">The <c>ExtensionRevision</c> text, or <see langword="null"/>.</param>
/// <param name="Url">The <c>URL</c> of the extension's documentation (§7.12.6), or <see langword="null"/>.</param>
internal sealed record DeveloperExtensionDeclaration(CosName Prefix, PdfVersion BaseVersion, int ExtensionLevel, string? Revision, string? Url)
{
    private static readonly CosName DeveloperExtensions = new("DeveloperExtensions");
    private static readonly CosName BaseVersionKey = new("BaseVersion");
    private static readonly CosName ExtensionRevisionKey = new("ExtensionRevision");
    private static readonly CosName UrlKey = new("URL");

    /// <summary>Builds the developer extensions dictionary, every entry direct (§7.12.1).</summary>
    /// <returns>A new dictionary.</returns>
    public CosDictionary ToDictionary()
    {
        var dictionary = new CosDictionary
        {
            [KnownNames.Type] = DeveloperExtensions,
            [BaseVersionKey] = new CosName(BaseVersion.ToString()),
            [KnownNames.ExtensionLevel] = new CosInteger(ExtensionLevel),
        };
        if (Revision is not null)
        {
            dictionary[ExtensionRevisionKey] = new CosString(System.Text.Encoding.ASCII.GetBytes(Revision));
        }

        if (Url is not null)
        {
            dictionary[UrlKey] = new CosString(System.Text.Encoding.ASCII.GetBytes(Url));
        }

        return dictionary;
    }
}

/// <summary>
/// A feature a saved file can use, and the earliest PDF version that has it: a row of the version table that saving raises the
/// version by (ADR 0003).
/// </summary>
/// <param name="Name">What the feature is, as the raise diagnostic and the strict-mode refusal name it.</param>
/// <param name="Clause">Where the version is stated.</param>
/// <param name="MinimumVersion">The earliest version that has the feature.</param>
/// <param name="Extension">The developer extension an ISO/TS feature declares (§7.12), or <see langword="null"/>.</param>
internal sealed record VersionedFeature(string Name, string Clause, PdfVersion MinimumVersion, DeveloperExtensionDeclaration? Extension = null)
{
    /// <summary>Returns the feature's name with its clause, such as <c>object streams (ISO 32000-2 §7.5.7)</c>.</summary>
    /// <returns>The description.</returns>
    public override string ToString() => $"{Name} ({Clause})";
}

/// <summary>The writable features and the minimum version each needs: the table a save raises the version by.</summary>
/// <remarks>
/// <para>ADR 0003; ISO 32000-2 §7.5.2 and §7.7.2, Table 29. One row per feature the library writes:</para>
/// <list type="table">
/// <listheader><term>Feature</term><description>Minimum version (clause)</description></listheader>
/// <item><term><see cref="FlateCompression"/></term><description>1.2 (§7.4.4): a save that compresses a stream, or <see cref="PdfDocument.EncodeStream"/> through <c>FlateDecode</c>.</description></item>
/// <item><term><see cref="CrossReferenceStream"/></term><description>1.5 (§7.5.8)</description></item>
/// <item><term><see cref="ObjectStreams"/></term><description>1.5 (§7.5.7)</description></item>
/// <item><term><see cref="Rc4Encryption"/></term><description>1.4 (§7.6.2, Table 20, V 2)</description></item>
/// <item><term><see cref="Aes128Encryption"/></term><description>1.6 (§7.6.2, Table 20, V 4; §7.6.5, Table 25, AESV2)</description></item>
/// <item><term><see cref="Aes256Encryption"/></term><description>2.0 (§7.6.2, Table 20, V 5)</description></item>
/// <item><term><see cref="AesGcmEncryption"/></term><description>2.0 and the ISO_ extension level 32003 (ISO/TS 32003, clause 4)</description></item>
/// <item><term><see cref="DeveloperExtension"/></term><description>The extension's <c>BaseVersion</c> (§7.12.4)</description></item>
/// </list>
/// <para>
/// A metadata stream needs PDF 1.4 (§14.3.2); the metadata writer creates none below it (issue #162), so it never raises the version.
/// </para>
/// </remarks>
internal static class VersionedFeatures
{
    private static readonly PdfVersion Pdf12 = new(1, 2);
    private static readonly PdfVersion Pdf14 = new(1, 4);
    private static readonly PdfVersion Pdf15 = new(1, 5);
    private static readonly PdfVersion Pdf16 = new(1, 6);
    private static readonly PdfVersion Pdf20 = new(2, 0);

    /// <summary>Gets streams compressed with the <c>FlateDecode</c> filter: PDF 1.2.</summary>
    public static VersionedFeature FlateCompression { get; } = new("FlateDecode compression", "ISO 32000-2 §7.4.4", Pdf12);

    /// <summary>Gets a cross-reference stream: PDF 1.5.</summary>
    public static VersionedFeature CrossReferenceStream { get; } = new("a cross-reference stream", "ISO 32000-2 §7.5.8", Pdf15);

    /// <summary>Gets object streams: PDF 1.5.</summary>
    public static VersionedFeature ObjectStreams { get; } = new("object streams", "ISO 32000-2 §7.5.7", Pdf15);

    /// <summary>Gets new RC4 encryption with a 128-bit key (V 2, R 3): PDF 1.4.</summary>
    public static VersionedFeature Rc4Encryption { get; } = new("RC4 encryption with a 128-bit key (V 2)", "ISO 32000-2 §7.6.2, Table 20", Pdf14);

    /// <summary>Gets new AES-128 encryption (V 4, R 4, <c>AESV2</c>): PDF 1.6.</summary>
    public static VersionedFeature Aes128Encryption { get; } = new("AES-128 encryption (V 4, AESV2)", "ISO 32000-2 §7.6.5, Table 25", Pdf16);

    /// <summary>Gets new AES-256 encryption (V 5, R 6): PDF 2.0.</summary>
    public static VersionedFeature Aes256Encryption { get; } = new("AES-256 encryption (V 5)", "ISO 32000-2 §7.6.2, Table 20", Pdf20);

    /// <summary>Gets AES-GCM encryption (V 6, R 7), kept from the document: PDF 2.0 and the ISO_ extension level 32003.</summary>
    public static VersionedFeature AesGcmEncryption { get; } = new(
        "AES-GCM encryption (V 6)",
        "ISO/TS 32003, clause 4",
        Pdf20,
        new DeveloperExtensionDeclaration(KnownNames.IsoPrefix, Pdf20, 32003, ":2023", "https://www.iso.org/standard/45876.html"));

    /// <summary>Returns the feature a developer extension the document declares stands for: the version it extends (§7.12.4).</summary>
    /// <param name="prefix">The extension's prefix.</param>
    /// <param name="level">The extension level, or <see langword="null"/> when missing.</param>
    /// <param name="baseVersion">The extension's <c>BaseVersion</c>.</param>
    /// <returns>The feature.</returns>
    public static VersionedFeature DeveloperExtension(CosName prefix, int? level, PdfVersion baseVersion) => new(
        level is { } value
            ? string.Create(CultureInfo.InvariantCulture, $"the {prefix.Value} developer extension level {value}")
            : $"the {prefix.Value} developer extension",
        "ISO 32000-2 §7.12.4",
        baseVersion);
}
