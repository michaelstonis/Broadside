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
/// <item><term><see cref="Linearization"/></term><description>1.1 (Annex F, F.3.2): a save with <see cref="PdfSaveOptions.Linearize"/>, whose hint stream also counts as <see cref="FlateCompression"/> unless compression is off.</description></item>
/// <item><term><see cref="CrossReferenceStream"/></term><description>1.5 (§7.5.8)</description></item>
/// <item><term><see cref="ObjectStreams"/></term><description>1.5 (§7.5.7)</description></item>
/// <item><term><see cref="Rc4Encryption"/></term><description>1.4 (§7.6.2, Table 20, V 2)</description></item>
/// <item><term><see cref="Aes128Encryption"/></term><description>1.6 (§7.6.2, Table 20, V 4; §7.6.5, Table 25, AESV2)</description></item>
/// <item><term><see cref="Aes256Encryption"/></term><description>2.0 (§7.6.2, Table 20, V 5)</description></item>
/// <item><term><see cref="AesGcmEncryption"/></term><description>2.0 and the ISO_ extension level 32003 (ISO/TS 32003, clause 4)</description></item>
/// <item><term><see cref="IntegrityProtection"/></term><description>2.0 and the ISO_ extension level 32004 (ISO/TS 32004, clause 4)</description></item>
/// <item><term><see cref="EmbeddedFileStream"/></term><description>1.3 (§7.11.4)</description></item>
/// <item><term><see cref="EmbeddedFilesTree"/></term><description>1.4 (§7.7.4, Table 32)</description></item>
/// <item><term><see cref="FileDescription"/></term><description>1.6 (§7.11.3, Table 43, <c>Desc</c>)</description></item>
/// <item><term><see cref="UnicodeFileName"/></term><description>1.7 (§7.11.3, Table 43, <c>UF</c>)</description></item>
/// <item><term><see cref="AssociatedFiles"/></term><description>2.0 (§14.13, <c>AF</c> and <c>AFRelationship</c>)</description></item>
/// <item><term><see cref="BoundingBoxDestinations"/></term><description>1.1 (§12.3.2.2, Table 149, <c>FitB</c>, <c>FitBH</c>, <c>FitBV</c>)</description></item>
/// <item><term><see cref="NamedDestinationsTree"/></term><description>1.2 (§12.3.2.4; §7.7.4, Table 32, <c>Dests</c>)</description></item>
/// <item><term><see cref="PageLabels"/></term><description>1.3 (§12.4.2; §7.7.2, Table 29, <c>PageLabels</c>)</description></item>
/// <item><term><see cref="OutlineItemStyle"/></term><description>1.4 (§12.3.3, Table 151, <c>C</c> and <c>F</c>)</description></item>
/// <item><term><see cref="StructureTree"/></term><description>What the tree uses, <see cref="Structure.PdfStructureTreeRoot.MinimumVersion"/>: 1.3 to 1.5 (§14.7.2, Tables 353 to 355)</description></item>
/// <item><term><see cref="StructureNamespaces"/></term><description>2.0 (§14.7.4, Table 356; Tables 354 and 355, <c>Namespaces</c>, <c>NS</c> and the other PDF 2.0 entries)</description></item>
/// <item><term><see cref="Jbig2Compression"/></term><description>1.4 (§7.4.7; §7.4.1, Table 6)</description></item>
/// <item><term><see cref="JpxCompression"/></term><description>1.5 (§7.4.9; §7.4.1, Table 6)</description></item>
/// <item><term><see cref="TransparencyParameters"/></term><description>1.4 (§11.1 NOTE; §11.6.3 and §11.6.4.4, Table 57, <c>CA</c>, <c>ca</c>, <c>BM</c>, <c>AIS</c>, <c>TK</c>)</description></item>
/// <item><term><see cref="SoftMasks"/></term><description>1.4 (§11.6.5; Table 57 and Table 87, <c>SMask</c>)</description></item>
/// <item><term><see cref="TransparencyGroups"/></term><description>1.4 (§11.6.6; Table 31 and Table 93, <c>Group</c>)</description></item>
/// <item><term><see cref="OptionalContent"/></term><description>1.5 (§8.11.1; Table 29, <c>OCProperties</c>; Tables 87, 93 and 166, <c>OC</c>)</description></item>
/// <item><term><see cref="Pdf20AnnotationEntries"/></term><description>2.0 (§12.5.2, Table 166, <c>ca</c>, <c>BM</c>, <c>Lang</c>, <c>CA</c> of a non-markup annotation; §12.5.6.1, Table 171, <c>Projection</c> and <c>RichMedia</c>)</description></item>
/// <item><term><see cref="PageBoundaries"/></term><description>1.3 (§7.7.3.3, Table 31, <c>BleedBox</c>, <c>TrimBox</c>, <c>ArtBox</c>)</description></item>
/// <item><term><see cref="RenderingIntentOperator"/></term><description>1.1 (§8.4.4, Table 56, <c>ri</c>)</description></item>
/// <item><term><see cref="GraphicsStateParameters"/></term><description>1.2 (§8.4.4, Table 56, <c>gs</c>)</description></item>
/// <item><term><see cref="CieBasedColorSpace"/></term><description>1.1 (§8.6.3, Table 61: CalGray, CalRGB, Lab, Indexed)</description></item>
/// <item><term><see cref="SeparationColorSpace"/></term><description>1.2 (§8.6.6.4)</description></item>
/// <item><term><see cref="IccBasedColorSpace"/></term><description>1.3 (§8.6.5.5)</description></item>
/// <item><term><see cref="DeviceNColorSpace"/></term><description>1.3 (§8.6.6.5); <see cref="DeviceNAttributes"/> 1.6 (Table 70)</description></item>
/// <item><term><see cref="SampledFunction"/></term><description>1.2 (§7.10.2)</description></item>
/// <item><term><see cref="CalculatedFunction"/></term><description>1.3 (§7.10.3 to §7.10.5, types 2, 3 and 4)</description></item>
/// <item><term><see cref="InteractiveForm"/></term><description>1.2 (§12.7.3; §7.7.2, Table 29, <c>AcroForm</c>): every field <see cref="Forms.PdfAcroForm"/> creates.</description></item>
/// <item><term><see cref="SignatureFields"/></term><description>1.3 (§12.7.5.5; Table 224, <c>SigFlags</c>)</description></item>
/// <item><term><see cref="FieldAlternateName"/></term><description>1.3 (§12.7.4.1, Table 226, <c>TU</c>)</description></item>
/// <item><term><see cref="FieldFlags14"/></term><description>1.4 (Tables 231 and 233: FileSelect, MultiSelect, DoNotSpellCheck, DoNotScroll)</description></item>
/// <item><term><see cref="FieldFlags15"/></term><description>1.5 (Tables 229, 231 and 233: Comb, RichText, RadiosInUnison, CommitOnSelChange)</description></item>
/// <item><term><see cref="ByteRangeSignature"/></term><description>1.3 (§12.8.3.1, Table 260, <c>adbe.pkcs7.detached</c>)</description></item>
/// <item><term><see cref="CadesSignature"/></term><description>2.0 (§12.8.3.4, <c>ETSI.CAdES.detached</c>)</description></item>
/// <item><term><see cref="LongRsaKeySignature"/></term><description>1.5 (Table 260, RSA keys of 2048 and 4096 bits)</description></item>
/// <item><term><see cref="EcdsaSignature"/></term><description>2.0 (Table 260) and the ISO_ extension level 32002 (ISO/TS 32002, clause 4)</description></item>
/// <item><term><see cref="Sha256SignatureDigest"/></term><description>1.6 (Table 260)</description></item>
/// <item><term><see cref="Sha2SignatureDigest"/></term><description>1.7 (Table 260, SHA-384 and SHA-512)</description></item>
/// <item><term><see cref="Sha3SignatureDigest"/></term><description>2.0 and the ISO_ extension level 32001 (ISO/TS 32001, clause 4 and §5.1.4)</description></item>
/// <item><term><see cref="DeveloperExtension"/></term><description>The extension's <c>BaseVersion</c> (§7.12.4)</description></item>
/// </list>
/// <para>
/// A metadata stream needs PDF 1.4 (§14.3.2); the metadata writer creates none below it (issue #162), so it never raises the version.
/// </para>
/// <para>
/// Objects copied from another document carry the rows <see cref="CarriedFeatures"/> finds in them (issue #290); a row whose
/// objects can be recognised by their entries should be added there too.
/// </para>
/// </remarks>
internal static class VersionedFeatures
{
    private static readonly PdfVersion Pdf11 = new(1, 1);
    private static readonly PdfVersion Pdf12 = new(1, 2);
    private static readonly PdfVersion Pdf14 = new(1, 4);
    private static readonly PdfVersion Pdf15 = new(1, 5);
    private static readonly PdfVersion Pdf16 = new(1, 6);
    private static readonly PdfVersion Pdf20 = new(2, 0);

    /// <summary>Gets streams compressed with the <c>FlateDecode</c> filter: PDF 1.2.</summary>
    public static VersionedFeature FlateCompression { get; } = new("FlateDecode compression", "ISO 32000-2 §7.4.4", Pdf12);

    /// <summary>Gets a linearized file (Annex F): PDF 1.1, the first version linearization may be applied to (F.3.2).</summary>
    public static VersionedFeature Linearization { get; } = new("linearization", "ISO 32000-2 Annex F, F.3.2", Pdf11);

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

    /// <summary>Gets AES-GCM encryption (V 6, R 7), new or kept from the document: PDF 2.0 and the ISO_ extension level 32003.</summary>
    public static VersionedFeature AesGcmEncryption { get; } = new(
        "AES-GCM encryption (V 6)",
        "ISO/TS 32003, clause 4",
        Pdf20,
        new DeveloperExtensionDeclaration(KnownNames.IsoPrefix, Pdf20, 32003, ":2023", "https://www.iso.org/standard/45876.html"));

    /// <summary>
    /// Gets an integrity MAC (a PDF MAC token in the trailer's <c>AuthCode</c>), new or recomputed for the document's own: PDF 2.0
    /// (ISO/TS 32004 clause 4: documents using it "shall conform to ISO 32000-2") and the ISO_ extension level 32004.
    /// </summary>
    public static VersionedFeature IntegrityProtection { get; } = new(
        "an integrity MAC (PDF MAC token)",
        "ISO/TS 32004, clause 4",
        Pdf20,
        new DeveloperExtensionDeclaration(KnownNames.IsoPrefix, Pdf20, 32004, ":2024", "https://www.iso.org/standard/45877.html"));

    /// <summary>Gets embedded file streams: PDF 1.3.</summary>
    public static VersionedFeature EmbeddedFileStream { get; } = new("an embedded file stream", "ISO 32000-2 §7.11.4", new PdfVersion(1, 3));

    /// <summary>Gets the name dictionary's <c>EmbeddedFiles</c> tree: PDF 1.4.</summary>
    public static VersionedFeature EmbeddedFilesTree { get; } = new("the EmbeddedFiles name tree", "ISO 32000-2 §7.7.4, Table 32", Pdf14);

    /// <summary>Gets a file specification's <c>Desc</c>: PDF 1.6.</summary>
    public static VersionedFeature FileDescription { get; } = new("a file specification's description (Desc)", "ISO 32000-2 §7.11.3, Table 43", Pdf16);

    /// <summary>Gets a file specification's <c>UF</c>: PDF 1.7.</summary>
    public static VersionedFeature UnicodeFileName { get; } = new("a file specification's Unicode file name (UF)", "ISO 32000-2 §7.11.3, Table 43", new PdfVersion(1, 7));

    /// <summary>Gets associated files (<c>AF</c>, <c>AFRelationship</c>): PDF 2.0.</summary>
    public static VersionedFeature AssociatedFiles { get; } = new("associated files (AF, AFRelationship)", "ISO 32000-2 §14.13", Pdf20);

    /// <summary>Gets <c>FitB</c>, <c>FitBH</c> and <c>FitBV</c> destinations: PDF 1.1.</summary>
    public static VersionedFeature BoundingBoxDestinations { get; } = new("a FitB, FitBH or FitBV destination", "ISO 32000-2 §12.3.2.2, Table 149", Pdf11);

    /// <summary>Gets the name dictionary's <c>Dests</c> tree, destinations named by strings: PDF 1.2.</summary>
    public static VersionedFeature NamedDestinationsTree { get; } = new("the Dests name tree (destinations named by strings)", "ISO 32000-2 §12.3.2.4", Pdf12);

    /// <summary>Gets page labels (<c>PageLabels</c>): PDF 1.3.</summary>
    public static VersionedFeature PageLabels { get; } = new("page labels (PageLabels)", "ISO 32000-2 §12.4.2", new PdfVersion(1, 3));

    /// <summary>Gets an outline item's colour or style flags (<c>C</c>, <c>F</c>): PDF 1.4.</summary>
    public static VersionedFeature OutlineItemStyle { get; } = new("an outline item's colour or style flags (C, F)", "ISO 32000-2 §12.3.3, Table 151", Pdf14);

    /// <summary>Gets structure namespaces (<c>Namespaces</c>, <c>NS</c>) and the other PDF 2.0 entries of a structure tree: PDF 2.0.</summary>
    public static VersionedFeature StructureNamespaces { get; } = new(
        "structure namespaces or other PDF 2.0 structure entries (Namespaces, NS, Ref, AF, Phoneme, PronunciationLexicon)",
        "ISO 32000-2 §14.7.4, Table 356; Tables 354 and 355",
        Pdf20);

    /// <summary>Gets streams compressed with the <c>JBIG2Decode</c> filter: PDF 1.4.</summary>
    public static VersionedFeature Jbig2Compression { get; } = new("JBIG2Decode compression", "ISO 32000-2 §7.4.7, Table 6", Pdf14);

    /// <summary>Gets streams compressed with the <c>JPXDecode</c> filter: PDF 1.5.</summary>
    public static VersionedFeature JpxCompression { get; } = new("JPXDecode compression", "ISO 32000-2 §7.4.9, Table 6", Pdf15);

    /// <summary>
    /// Gets the transparency parameters of a graphics state parameter dictionary (<c>CA</c>, <c>ca</c>, <c>BM</c>, <c>AIS</c>,
    /// <c>TK</c>), also a markup annotation's <c>CA</c>: PDF 1.4.
    /// </summary>
    public static VersionedFeature TransparencyParameters { get; } = new(
        "transparency parameters (CA, ca, BM, AIS, TK)", "ISO 32000-2 §11.6.3 and §11.6.4.4, Table 57", Pdf14);

    /// <summary>Gets soft masks: a graphics state's or an image's <c>SMask</c>, and soft-mask dictionaries: PDF 1.4.</summary>
    public static VersionedFeature SoftMasks { get; } = new("a soft mask (SMask)", "ISO 32000-2 §11.6.5; Tables 57 and 87", Pdf14);

    /// <summary>Gets transparency groups: a form's or a page's <c>Group</c> of subtype <c>Transparency</c>: PDF 1.4.</summary>
    public static VersionedFeature TransparencyGroups { get; } = new("a transparency group (Group)", "ISO 32000-2 §11.6.6; Tables 31 and 93", Pdf14);

    /// <summary>
    /// Gets optional content: groups (<c>OCG</c>), membership dictionaries (<c>OCMD</c>), an <c>OC</c> entry and the catalog's
    /// <c>OCProperties</c>: PDF 1.5.
    /// </summary>
    public static VersionedFeature OptionalContent { get; } = new("optional content (OCG, OCMD, OC, OCProperties)", "ISO 32000-2 §8.11; Table 29", Pdf15);

    /// <summary>
    /// Gets the PDF 2.0 annotation entries: <c>ca</c>, <c>BM</c> and <c>Lang</c>, <c>CA</c> of a non-markup annotation, and the
    /// <c>Projection</c> and <c>RichMedia</c> types: PDF 2.0.
    /// </summary>
    public static VersionedFeature Pdf20AnnotationEntries { get; } = new(
        "PDF 2.0 annotation entries (ca, BM, Lang, CA of a non-markup annotation, Projection and RichMedia types)",
        "ISO 32000-2 §12.5.2, Table 166; §12.5.6.1, Table 171",
        Pdf20);

    /// <summary>Gets the <c>BleedBox</c>, <c>TrimBox</c> and <c>ArtBox</c> page boundaries: PDF 1.3.</summary>
    public static VersionedFeature PageBoundaries { get; } = new("a page's BleedBox, TrimBox or ArtBox", "ISO 32000-2 §7.7.3.3, Table 31", new PdfVersion(1, 3));

    /// <summary>Gets the <c>ri</c> operator: PDF 1.1.</summary>
    public static VersionedFeature RenderingIntentOperator { get; } = new("the rendering intent operator (ri)", "ISO 32000-2 §8.4.4, Table 56", Pdf11);

    /// <summary>Gets graphics state parameter dictionaries and the <c>gs</c> operator: PDF 1.2.</summary>
    public static VersionedFeature GraphicsStateParameters { get; } = new("a graphics state parameter dictionary (gs)", "ISO 32000-2 §8.4.4, Table 56", Pdf12);

    /// <summary>Gets the CIE-based spaces CalGray, CalRGB and Lab, and Indexed spaces: PDF 1.1.</summary>
    public static VersionedFeature CieBasedColorSpace { get; } = new("a CalGray, CalRGB, Lab or Indexed colour space", "ISO 32000-2 §8.6.3, Table 61", Pdf11);

    /// <summary>Gets Separation colour spaces: PDF 1.2.</summary>
    public static VersionedFeature SeparationColorSpace { get; } = new("a Separation colour space", "ISO 32000-2 §8.6.6.4", Pdf12);

    /// <summary>Gets ICCBased colour spaces: PDF 1.3.</summary>
    public static VersionedFeature IccBasedColorSpace { get; } = new("an ICCBased colour space", "ISO 32000-2 §8.6.5.5", new PdfVersion(1, 3));

    /// <summary>Gets DeviceN colour spaces: PDF 1.3.</summary>
    public static VersionedFeature DeviceNColorSpace { get; } = new("a DeviceN colour space", "ISO 32000-2 §8.6.6.5", new PdfVersion(1, 3));

    /// <summary>Gets a DeviceN colour space's attributes dictionary: PDF 1.6.</summary>
    public static VersionedFeature DeviceNAttributes { get; } = new("a DeviceN colour space's attributes dictionary", "ISO 32000-2 §8.6.6.5, Table 70", Pdf16);

    /// <summary>Gets sampled (type 0) functions: PDF 1.2.</summary>
    public static VersionedFeature SampledFunction { get; } = new("a sampled function (FunctionType 0)", "ISO 32000-2 §7.10.2", Pdf12);

    /// <summary>Gets exponential, stitching and PostScript calculator functions (types 2, 3 and 4): PDF 1.3.</summary>
    public static VersionedFeature CalculatedFunction { get; } = new("an exponential, stitching or PostScript calculator function (FunctionType 2, 3 or 4)", "ISO 32000-2 §7.10.3 to §7.10.5", new PdfVersion(1, 3));

    /// <summary>Returns the feature a colour space stands for (§8.6.3, Table 61), or <see langword="null"/> for a device space (PDF 1.0).</summary>
    /// <param name="space">The space.</param>
    /// <returns>The feature.</returns>
    public static VersionedFeature? ColorSpace(Graphics.PdfColorSpace space) => space.Family switch
    {
        Graphics.PdfColorSpaceFamily.CalGray or Graphics.PdfColorSpaceFamily.CalRgb or Graphics.PdfColorSpaceFamily.Lab
            or Graphics.PdfColorSpaceFamily.Indexed => CieBasedColorSpace,
        Graphics.PdfColorSpaceFamily.Separation => SeparationColorSpace,
        Graphics.PdfColorSpaceFamily.IccBased => IccBasedColorSpace,
        Graphics.PdfColorSpaceFamily.DeviceN => space.MinimumVersion > new PdfVersion(1, 3) ? DeviceNAttributes : DeviceNColorSpace,
        _ => null,
    };

    /// <summary>Gets an interactive form (<c>AcroForm</c>) with its fields, widget annotations and their appearance characteristics: PDF 1.2.</summary>
    public static VersionedFeature InteractiveForm { get; } = new(
        "an interactive form (AcroForm) with fields and widget annotations", "ISO 32000-2 §12.7.3; §7.7.2, Table 29", Pdf12);

    /// <summary>Gets signature fields (<c>FT</c> <c>Sig</c>) and the interactive form's <c>SigFlags</c>: PDF 1.3.</summary>
    public static VersionedFeature SignatureFields { get; } = new(
        "signature fields (FT Sig, SigFlags)", "ISO 32000-2 §12.7.5.5; §12.7.3, Table 224", new PdfVersion(1, 3));

    /// <summary>Gets a field's alternative name (<c>TU</c>): PDF 1.3.</summary>
    public static VersionedFeature FieldAlternateName { get; } = new(
        "a field's alternative name (TU)", "ISO 32000-2 §12.7.4.1, Table 226", new PdfVersion(1, 3));

    /// <summary>Gets the PDF 1.4 field flags: FileSelect, MultiSelect, DoNotSpellCheck and DoNotScroll.</summary>
    public static VersionedFeature FieldFlags14 { get; } = new(
        "the field flags FileSelect, MultiSelect, DoNotSpellCheck or DoNotScroll", "ISO 32000-2 §12.7.5.3, Table 231; §12.7.5.4, Table 233", Pdf14);

    /// <summary>Gets the PDF 1.5 field flags: Comb, RichText, RadiosInUnison and CommitOnSelChange.</summary>
    public static VersionedFeature FieldFlags15 { get; } = new(
        "the field flags Comb, RichText, RadiosInUnison or CommitOnSelChange",
        "ISO 32000-2 §12.7.5.2.4, Table 229; §12.7.5.3, Table 231; §12.7.5.4, Table 233",
        Pdf15);

    /// <summary>Gets a byte range signature (a signature dictionary with <c>ByteRange</c> and a CMS <c>Contents</c>): PDF 1.3.</summary>
    public static VersionedFeature ByteRangeSignature { get; } = new("a byte range signature (adbe.pkcs7.detached)", "ISO 32000-2 §12.8.3.1, Table 260", new PdfVersion(1, 3));

    /// <summary>Gets a PAdES signature, sub-filter <c>ETSI.CAdES.detached</c>: PDF 2.0.</summary>
    public static VersionedFeature CadesSignature { get; } = new("a PAdES signature (ETSI.CAdES.detached)", "ISO 32000-2 §12.8.3.4; Table 260", Pdf20);

    /// <summary>Gets an RSA signature with a key longer than 1024 bits: PDF 1.5.</summary>
    public static VersionedFeature LongRsaKeySignature { get; } = new("an RSA signature with a key over 1024 bits", "ISO 32000-2 §12.8.3.1, Table 260", Pdf15);

    /// <summary>Gets an ECDSA signature: PDF 2.0 and the ISO_ extension level 32002 for the curves of ISO/TS 32002 Table 3.</summary>
    public static VersionedFeature EcdsaSignature { get; } = new(
        "an ECDSA signature",
        "ISO 32000-2 §12.8.3.1, Table 260; ISO/TS 32002, clause 4 and §5.1.3",
        Pdf20,
        new DeveloperExtensionDeclaration(KnownNames.IsoPrefix, Pdf20, 32002, ":2022", "https://www.iso.org/standard/45875.html"));

    /// <summary>Gets a signature with a SHA-256 digest: PDF 1.6.</summary>
    public static VersionedFeature Sha256SignatureDigest { get; } = new("a SHA-256 signature digest", "ISO 32000-2 §12.8.3.1, Table 260", Pdf16);

    /// <summary>Gets a signature with a SHA-384 or SHA-512 digest: PDF 1.7.</summary>
    public static VersionedFeature Sha2SignatureDigest { get; } = new("a SHA-384 or SHA-512 signature digest", "ISO 32000-2 §12.8.3.1, Table 260", new PdfVersion(1, 7));

    /// <summary>Gets a signature with a SHA3-256, SHA3-384 or SHA3-512 digest: PDF 2.0 and the ISO_ extension level 32001.</summary>
    public static VersionedFeature Sha3SignatureDigest { get; } = new(
        "a SHA-3 signature digest",
        "ISO/TS 32001, clause 4 and §5.1.4",
        Pdf20,
        new DeveloperExtensionDeclaration(KnownNames.IsoPrefix, Pdf20, 32001, ":2022", "https://www.iso.org/standard/45874.html"));

    /// <summary>Returns the feature a filter's encoding stands for, or <see langword="null"/> when it has no version row.</summary>
    /// <param name="filter">A filter name.</param>
    /// <returns><see cref="FlateCompression"/>, <see cref="Jbig2Compression"/>, <see cref="JpxCompression"/> or <see langword="null"/>.</returns>
    public static VersionedFeature? Filter(CosName filter) =>
        Filters.FilterNames.FlateDecode.Equals(filter) ? FlateCompression
        : Filters.FilterNames.Jbig2Decode.Equals(filter) ? Jbig2Compression
        : Filters.FilterNames.JpxDecode.Equals(filter) ? JpxCompression
        : null;

    /// <summary>Returns the feature a structure tree stands for: the lowest version that has everything it uses.</summary>
    /// <param name="minimum">The tree's <see cref="Structure.PdfStructureTreeRoot.MinimumVersion"/>.</param>
    /// <returns><see cref="StructureNamespaces"/> for PDF 2.0; otherwise the structure tree and its entries at <paramref name="minimum"/>.</returns>
    public static VersionedFeature StructureTree(PdfVersion minimum) => minimum >= Pdf20
        ? StructureNamespaces
        : new("a structure tree and its entries (StructTreeRoot, MarkInfo, Lang, ActualText, E)", "ISO 32000-2 §14.7.2, Tables 353 to 355", minimum);

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
