namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics flow layout and the document builder record while they lay content out on pages.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.7.3.3 (page objects), §8.4.2 (graphics state stack) and §14.6 (marked content). Each code is reported under
/// <see cref="DiagnosticArea.Layout"/>; see <see cref="DiagnosticCatalog"/> for every area. Layout records them on the document it lays
/// out into (<see cref="PdfDocument.ReportDiagnostic"/>), so they appear in <see cref="PdfDocument.Diagnostics"/>, and a document opened
/// or created in strict mode throws the first one.
/// </para>
/// </remarks>
public static class LayoutDiagnostics
{
    /// <summary>Gets the <c>DrawingStateUnbalanced</c> code: a drawing callback left graphics states saved, marked-content sequences or a
    /// text object open (or closed more than it opened); the layout closed or reopened them to keep the content stream balanced.</summary>
    /// <remarks>ISO 32000-2 §8.4.2</remarks>
    public static DiagnosticCode DrawingStateUnbalanced { get; } = new(nameof(DrawingStateUnbalanced), DiagnosticArea.Layout, DiagnosticSeverity.Warning, "ISO 32000-2 §8.4.2",
        "Drawing content in the layout leaves the canvas unbalanced, with an unpainted path, an open text object, or unended graphics states or marked-content sequences. The layout balances it.");

    /// <summary>Gets the <c>KeepTogetherTooTall</c> code: content asked to stay on one page is taller than a page, so it breaks across
    /// pages anyway.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3</remarks>
    public static DiagnosticCode KeepTogetherTooTall { get; } = new(nameof(KeepTogetherTooTall), DiagnosticArea.Layout, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3.3",
        "A group of blocks marked to stay together is taller than the body of a page. It breaks across pages.");

    /// <summary>Gets the <c>HeadingLevelSkipped</c> code: a heading the document builder wrote is more than one level below the previous heading (an H3 after an H1).</summary>
    /// <remarks>ISO 32000-2 §14.8.4.3.6</remarks>
    public static DiagnosticCode HeadingLevelSkipped { get; } = new(nameof(HeadingLevelSkipped), DiagnosticArea.Layout, DiagnosticSeverity.Warning, "ISO 32000-2 §14.8.4.3.6",
        "A heading level is skipped, such as a level 3 heading directly after level 1. The document is built as written, which fails PDF/UA conformance.");

    /// <summary>Gets the <c>AlternateTextLooksLikeFileName</c> code: an image's alternate text looks like a file name rather than a description of what it shows.</summary>
    /// <remarks>ISO 32000-2 §14.9.3</remarks>
    public static DiagnosticCode AlternateTextLooksLikeFileName { get; } = new(nameof(AlternateTextLooksLikeFileName), DiagnosticArea.Layout, DiagnosticSeverity.Warning, "ISO 32000-2 §14.9.3",
        "The alternate text of an image looks like a file name instead of a description of the image. The text is used as given.");

    /// <summary>Gets the <c>FontFaceMissing</c> code: a font family has no face for the requested weight and style, so its regular face is used (a face is never synthesized).</summary>
    /// <remarks>ISO 32000-2 §9.6.2</remarks>
    public static DiagnosticCode FontFaceMissing { get; } = new(nameof(FontFaceMissing), DiagnosticArea.Layout, DiagnosticSeverity.Warning, "ISO 32000-2 §9.6.2",
        "A font family has no face for the requested weight or style. Its regular face is used and no face is synthesized.");

    /// <summary>Gets the <c>FontNotFound</c> code: a font family named for the document's font resolvers was not found, so Helvetica (embedded Liberation Sans) is used.</summary>
    /// <remarks>ISO 32000-2 §9.6.2</remarks>
    public static DiagnosticCode FontNotFound { get; } = new(nameof(FontNotFound), DiagnosticArea.Layout, DiagnosticSeverity.Warning, "ISO 32000-2 §9.6.2",
        "The font resolvers find no font with the requested family name. Helvetica, using the embedded Liberation Sans, is used instead.");

    /// <summary>Gets the <c>CharacterNotEncodable</c> code: a character is in no face of a font family or its fallbacks, so a question mark is drawn instead.</summary>
    /// <remarks>ISO 32000-2 §9.4.3</remarks>
    public static DiagnosticCode CharacterNotEncodable { get; } = new(nameof(CharacterNotEncodable), DiagnosticArea.Layout, DiagnosticSeverity.Warning, "ISO 32000-2 §9.4.3",
        "A character is in no face of the font family or its fallbacks. A question mark is drawn instead.");

    /// <summary>Gets the <c>DocumentLanguageMissing</c> code: a tagged document the document builder wrote has no natural language.</summary>
    /// <remarks>ISO 32000-2 §14.9.2</remarks>
    public static DiagnosticCode DocumentLanguageMissing { get; } = new(nameof(DocumentLanguageMissing), DiagnosticArea.Layout, DiagnosticSeverity.Warning, "ISO 32000-2 §14.9.2",
        "A tagged document has no language set. The document is written without one.");

    /// <summary>Gets the <c>DocumentTitleMissing</c> code: a tagged document the document builder wrote has no title.</summary>
    /// <remarks>ISO 32000-2 §14.3.3</remarks>
    public static DiagnosticCode DocumentTitleMissing { get; } = new(nameof(DocumentTitleMissing), DiagnosticArea.Layout, DiagnosticSeverity.Warning, "ISO 32000-2 §14.3.3",
        "A tagged document has no title set. The document is written without one.");

    /// <summary>Gets the <c>AppendedToUntaggedDocument</c> code: tagged pages were appended to an untagged document, whose earlier pages stay untagged.</summary>
    /// <remarks>ISO 32000-2 §14.8</remarks>
    public static DiagnosticCode AppendedToUntaggedDocument { get; } = new(nameof(AppendedToUntaggedDocument), DiagnosticArea.Layout, DiagnosticSeverity.Warning, "ISO 32000-2 §14.8",
        "Tagged pages are appended to an untagged document, so its earlier pages stay untagged. They can be tagged first with AutoTag.");

    /// <summary>Gets the <c>DocumentMetadataKept</c> code: a title or language given for pages appended to a document was ignored because the document already has one.</summary>
    /// <remarks>ISO 32000-2 §14.3.3</remarks>
    public static DiagnosticCode DocumentMetadataKept { get; } = new(nameof(DocumentMetadataKept), DiagnosticArea.Layout, DiagnosticSeverity.Information, "ISO 32000-2 §14.3.3",
        "Content is appended to a document that already has a title or language. The existing value is kept and the appended content's value is ignored.");

    /// <summary>Gets the <c>AnchorNotFound</c> code: a link (<c>TextRun.Destination</c>) or page reference names an anchor id no block of
    /// the flow has and, for a link, the document has no named destination of; the link is drawn as plain text, the page number as
    /// <c>?</c>.</summary>
    /// <remarks>ISO 32000-2 §12.3.2.4</remarks>
    public static DiagnosticCode AnchorNotFound { get; } = new(nameof(AnchorNotFound), DiagnosticArea.Layout, DiagnosticSeverity.Warning, "ISO 32000-2 §12.3.2.4",
        "A reference names an anchor id that no block has and that is not a named destination in the document. The reference is shown as plain text.");

    /// <summary>Gets the <c>AnchorDuplicate</c> code: two blocks of a flow have the same anchor id; the first keeps it and is the named
    /// destination.</summary>
    /// <remarks>ISO 32000-2 §12.3.2.4</remarks>
    public static DiagnosticCode AnchorDuplicate { get; } = new(nameof(AnchorDuplicate), DiagnosticArea.Layout, DiagnosticSeverity.Warning, "ISO 32000-2 §12.3.2.4",
        "Two blocks have the same anchor id. The first block keeps the id.");

    /// <summary>Gets the <c>ForwardReferencesNotConverged</c> code: the page numbers a flow shows (page references, a table of contents)
    /// still changed after the last layout pass allowed; the last pass's numbers are drawn, and some may be wrong.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3</remarks>
    public static DiagnosticCode ForwardReferencesNotConverged { get; } = new(nameof(ForwardReferencesNotConverged), DiagnosticArea.Layout, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3.3",
        "The page numbers shown by forward references still changed after the maximum number of layout passes. The last pass's numbers are shown, and some may be wrong.");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        DrawingStateUnbalanced,
        KeepTogetherTooTall,
        HeadingLevelSkipped,
        AlternateTextLooksLikeFileName,
        FontFaceMissing,
        FontNotFound,
        CharacterNotEncodable,
        DocumentLanguageMissing,
        DocumentTitleMissing,
        AppendedToUntaggedDocument,
        DocumentMetadataKept,
        AnchorNotFound,
        AnchorDuplicate,
        ForwardReferencesNotConverged,
    ];
}
