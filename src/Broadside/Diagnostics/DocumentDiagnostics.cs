namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about the document catalog, page labels, document information and metadata.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.7.2, §7.9.4, §12.4.2, §14.3. Each code is reported under <see cref="DiagnosticArea.Document"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class DocumentDiagnostics
{
    /// <summary>Gets the <c>CatalogEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11</remarks>
    public static DiagnosticCode CatalogEntryInvalid { get; } = new(nameof(CatalogEntryInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11",
        "A catalog entry has the wrong type or an invalid value, such as a malformed language string or a relative base URI. The entry is ignored.");

    /// <summary>Gets the <c>PageLayoutInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11</remarks>
    public static DiagnosticCode PageLayoutInvalid { get; } = new(nameof(PageLayoutInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11",
        "The catalog PageLayout entry is not a recognized value. The default single page layout is used.");

    /// <summary>Gets the <c>PageModeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11</remarks>
    public static DiagnosticCode PageModeInvalid { get; } = new(nameof(PageModeInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11",
        "The catalog PageMode entry is not a recognized value. The default of no panel is used.");

    /// <summary>Gets the <c>ViewerPreferencesInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11</remarks>
    public static DiagnosticCode ViewerPreferencesInvalid { get; } = new(nameof(ViewerPreferencesInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11",
        "The catalog ViewerPreferences entry is not a dictionary. The entry is ignored.");

    /// <summary>Gets the <c>ViewerPreferenceInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11</remarks>
    public static DiagnosticCode ViewerPreferenceInvalid { get; } = new(nameof(ViewerPreferenceInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11",
        "An entry of the viewer preferences dictionary has the wrong type or an unrecognized value. The entry is ignored and its default applies.");

    /// <summary>Gets the <c>ExtensionsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11</remarks>
    public static DiagnosticCode ExtensionsInvalid { get; } = new(nameof(ExtensionsInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11",
        "The catalog Extensions entry is not a dictionary. The entry is ignored.");

    /// <summary>Gets the <c>ExtensionsNotDirect</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11</remarks>
    public static DiagnosticCode ExtensionsNotDirect { get; } = new(nameof(ExtensionsNotDirect), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11",
        "The extensions dictionary or a developer extensions dictionary is an indirect object, although it shall be direct. It is read through its reference.");

    /// <summary>Gets the <c>DeveloperExtensionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11</remarks>
    public static DiagnosticCode DeveloperExtensionInvalid { get; } = new(nameof(DeveloperExtensionInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11",
        "A developer extension is not a dictionary or an array of dictionaries with a BaseVersion name and an ExtensionLevel integer. The extension is not recorded as declared.");

    /// <summary>Gets the <c>DeveloperExtensionBaseVersionLater</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11</remarks>
    public static DiagnosticCode DeveloperExtensionBaseVersionLater { get; } = new(nameof(DeveloperExtensionBaseVersionLater), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11",
        "A developer extension declares a base version later than the document version. Saving raises the document version.");

    /// <summary>Gets the <c>RequirementInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11</remarks>
    public static DiagnosticCode RequirementInvalid { get; } = new(nameof(RequirementInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11",
        "A catalog requirements entry is malformed. The invalid requirement is ignored.");

    /// <summary>Gets the <c>OutputIntentInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.11.5</remarks>
    public static DiagnosticCode OutputIntentInvalid { get; } = new(nameof(OutputIntentInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §14.11.5",
        "An output intent has an invalid entry, such as a subtype that is not a name or a malformed text value. The entry is ignored.");

    /// <summary>Gets the <c>PageLabelsMissingZeroKey</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.4.2</remarks>
    public static DiagnosticCode PageLabelsMissingZeroKey { get; } = new(nameof(PageLabelsMissingZeroKey), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §12.4.2",
        "The page labels number tree has no range for page index 0. Pages before the first range are numbered in decimal from 1.");

    /// <summary>Gets the <c>PageLabelInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.4.2</remarks>
    public static DiagnosticCode PageLabelInvalid { get; } = new(nameof(PageLabelInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §12.4.2",
        "A page label dictionary or number tree entry is invalid, such as a wrong Type, a negative key or a value that is not a dictionary. The entry is ignored.");

    /// <summary>Gets the <c>PageLabelStyleInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.4.2</remarks>
    public static DiagnosticCode PageLabelStyleInvalid { get; } = new(nameof(PageLabelStyleInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §12.4.2",
        "A page label numbering style is not one of the defined values. The range is numbered in decimal.");

    /// <summary>Gets the <c>PageLabelStartInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.4.2</remarks>
    public static DiagnosticCode PageLabelStartInvalid { get; } = new(nameof(PageLabelStartInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §12.4.2",
        "A page label start value is not an integer of 1 or more. An integral real is used as that integer, otherwise the range starts at 1.");

    /// <summary>Gets the <c>PageLabelPrefixInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.4.2</remarks>
    public static DiagnosticCode PageLabelPrefixInvalid { get; } = new(nameof(PageLabelPrefixInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §12.4.2",
        "A page label prefix is not a text string or contains a null character. The prefix is ignored or cut at the null character.");

    /// <summary>Gets the <c>PageLabelTooLong</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.4.2</remarks>
    public static DiagnosticCode PageLabelTooLong { get; } = new(nameof(PageLabelTooLong), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §12.4.2",
        "A page number is too large to write in letters or roman numerals. It is written in decimal.");

    /// <summary>Gets the <c>InfoDictionaryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.4, §14.3, §14.4</remarks>
    public static DiagnosticCode InfoDictionaryInvalid { get; } = new(nameof(InfoDictionaryInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.4, §14.3, §14.4",
        "The trailer Info entry does not refer to a document information dictionary. The entry is ignored.");

    /// <summary>Gets the <c>InfoValueNotTextString</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.4, §14.3, §14.4</remarks>
    public static DiagnosticCode InfoValueNotTextString { get; } = new(nameof(InfoValueNotTextString), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.4, §14.3, §14.4",
        "A document information entry is not a text string. The entry is read as text when possible or ignored.");

    /// <summary>Gets the <c>InfoTrappedInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.4, §14.3, §14.4</remarks>
    public static DiagnosticCode InfoTrappedInvalid { get; } = new(nameof(InfoTrappedInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.4, §14.3, §14.4",
        "The Info dictionary Trapped entry is not True, False or Unknown, or is not a name. A boolean or string is read as the name it spells, otherwise the value is read as Unknown.");

    /// <summary>Gets the <c>DateInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.4, §14.3, §14.4</remarks>
    public static DiagnosticCode DateInvalid { get; } = new(nameof(DateInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.4, §14.3, §14.4",
        "A date entry does not follow the PDF date format. The date is read with the deviating fields repaired.");

    /// <summary>Gets the <c>DateUnreadable</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.4, §14.3, §14.4</remarks>
    public static DiagnosticCode DateUnreadable { get; } = new(nameof(DateUnreadable), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.4, §14.3, §14.4",
        "A date entry cannot be read as a date. The original text is kept where possible and the date is unknown.");

    /// <summary>Gets the <c>MetadataStreamInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.4, §14.3, §14.4</remarks>
    public static DiagnosticCode MetadataStreamInvalid { get; } = new(nameof(MetadataStreamInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.4, §14.3, §14.4",
        "A Metadata entry is not a metadata stream, or the stream lacks the required Type or Subtype. A non-stream is ignored, and a stream with wrong entries is read as XMP anyway.");

    /// <summary>Gets the <c>MetadataStreamTypeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.4, §14.3, §14.4</remarks>
    public static DiagnosticCode MetadataStreamTypeInvalid { get; } = new(nameof(MetadataStreamTypeInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.4, §14.3, §14.4",
        "A metadata stream does not have Type Metadata and Subtype XML. The stream is read as XMP anyway.");

    /// <summary>Gets the <c>XmpMalformed</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.4, §14.3, §14.4</remarks>
    public static DiagnosticCode XmpMalformed { get; } = new(nameof(XmpMalformed), DiagnosticArea.Document, DiagnosticSeverity.Error, "ISO 32000-2 §7.9.4, §14.3, §14.4",
        "The XMP packet is not usable, for example it has no XML markup, no rdf:RDF element, or contains a document type declaration. The packet is ignored.");

    /// <summary>Gets the <c>XmpDtdProhibited</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.4, §14.3, §14.4</remarks>
    public static DiagnosticCode XmpDtdProhibited { get; } = new(nameof(XmpDtdProhibited), DiagnosticArea.Document, DiagnosticSeverity.Error, "ISO 32000-2 §7.9.4, §14.3, §14.4",
        "The XMP packet contains a document type declaration, which is not read because it could expand entities or fetch files. The packet is ignored.");

    /// <summary>Gets the <c>XmpLeadingJunk</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.4, §14.3, §14.4</remarks>
    public static DiagnosticCode XmpLeadingJunk { get; } = new(nameof(XmpLeadingJunk), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.4, §14.3, §14.4",
        "The XMP packet has bytes other than white-space before its first markup. The bytes are skipped.");

    /// <summary>Gets the <c>XmpPropertyDuplicate</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.4, §14.3, §14.4</remarks>
    public static DiagnosticCode XmpPropertyDuplicate { get; } = new(nameof(XmpPropertyDuplicate), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.4, §14.3, §14.4",
        "An XMP property is given more than once. The first value is used.");

    /// <summary>Gets the <c>FileIdentifierInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.4, §14.3, §14.4</remarks>
    public static DiagnosticCode FileIdentifierInvalid { get; } = new(nameof(FileIdentifierInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.4, §14.3, §14.4",
        "The trailer ID entry is not an array of two byte strings, or an identifier is shorter than 16 bytes. An invalid entry is ignored and short identifiers are used as they are.");

    /// <summary>Gets the <c>CatalogTypeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2</remarks>
    public static DiagnosticCode CatalogTypeInvalid { get; } = new(nameof(CatalogTypeInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.2",
        "The catalog dictionary Type entry is missing or is not Catalog. The dictionary is used as the catalog anyway.");

    /// <summary>Gets the <c>CatalogVersionNotName</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2</remarks>
    public static DiagnosticCode CatalogVersionNotName { get; } = new(nameof(CatalogVersionNotName), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.2",
        "The catalog Version entry is a number instead of a name. It is read as that version.");

    /// <summary>Gets the <c>CatalogVersionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2</remarks>
    public static DiagnosticCode CatalogVersionInvalid { get; } = new(nameof(CatalogVersionInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.2",
        "The catalog Version entry is not a version name. The entry is ignored.");

    /// <summary>Gets the <c>PagesMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2</remarks>
    public static DiagnosticCode PagesMissing { get; } = new(nameof(PagesMissing), DiagnosticArea.Document, DiagnosticSeverity.Error, "ISO 32000-2 §7.7.2",
        "The catalog has no usable Pages entry or the page tree holds no page objects. The file is searched for pages, and if none are found the document has no pages.");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        CatalogEntryInvalid,
        PageLayoutInvalid,
        PageModeInvalid,
        ViewerPreferencesInvalid,
        ViewerPreferenceInvalid,
        ExtensionsInvalid,
        ExtensionsNotDirect,
        DeveloperExtensionInvalid,
        DeveloperExtensionBaseVersionLater,
        RequirementInvalid,
        OutputIntentInvalid,
        PageLabelsMissingZeroKey,
        PageLabelInvalid,
        PageLabelStyleInvalid,
        PageLabelStartInvalid,
        PageLabelPrefixInvalid,
        PageLabelTooLong,
        InfoDictionaryInvalid,
        InfoValueNotTextString,
        InfoTrappedInvalid,
        DateInvalid,
        DateUnreadable,
        MetadataStreamInvalid,
        MetadataStreamTypeInvalid,
        XmpMalformed,
        XmpDtdProhibited,
        XmpLeadingJunk,
        XmpPropertyDuplicate,
        FileIdentifierInvalid,
        CatalogTypeInvalid,
        CatalogVersionNotName,
        CatalogVersionInvalid,
        PagesMissing,
    ];
}
