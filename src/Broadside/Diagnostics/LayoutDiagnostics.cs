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
    public static DiagnosticCode DrawingStateUnbalanced { get; } = new(nameof(DrawingStateUnbalanced), DiagnosticArea.Layout, DiagnosticSeverity.Warning, "ISO 32000-2 §8.4.2");

    /// <summary>Gets the <c>KeepTogetherTooTall</c> code: content asked to stay on one page is taller than a page, so it breaks across
    /// pages anyway.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3</remarks>
    public static DiagnosticCode KeepTogetherTooTall { get; } = new(nameof(KeepTogetherTooTall), DiagnosticArea.Layout, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3.3");

    /// <summary>Gets the <c>HeadingLevelSkipped</c> code: a heading the document builder wrote is more than one level below the previous heading (an H3 after an H1).</summary>
    /// <remarks>ISO 32000-2 §14.8.4.3.6</remarks>
    public static DiagnosticCode HeadingLevelSkipped { get; } = new(nameof(HeadingLevelSkipped), DiagnosticArea.Layout, DiagnosticSeverity.Warning, "ISO 32000-2 §14.8.4.3.6");

    /// <summary>Gets the <c>AlternateTextLooksLikeFileName</c> code: an image's alternate text looks like a file name rather than a description of what it shows.</summary>
    /// <remarks>ISO 32000-2 §14.9.3</remarks>
    public static DiagnosticCode AlternateTextLooksLikeFileName { get; } = new(nameof(AlternateTextLooksLikeFileName), DiagnosticArea.Layout, DiagnosticSeverity.Warning, "ISO 32000-2 §14.9.3");

    /// <summary>Gets the <c>FontFaceMissing</c> code: a font family has no face for the requested weight and style, so its regular face is used (a face is never synthesized).</summary>
    /// <remarks>ISO 32000-2 §9.6.2</remarks>
    public static DiagnosticCode FontFaceMissing { get; } = new(nameof(FontFaceMissing), DiagnosticArea.Layout, DiagnosticSeverity.Warning, "ISO 32000-2 §9.6.2");

    /// <summary>Gets the <c>FontNotFound</c> code: a font family named for the document's font resolvers was not found, so Helvetica (embedded Liberation Sans) is used.</summary>
    /// <remarks>ISO 32000-2 §9.6.2</remarks>
    public static DiagnosticCode FontNotFound { get; } = new(nameof(FontNotFound), DiagnosticArea.Layout, DiagnosticSeverity.Warning, "ISO 32000-2 §9.6.2");

    /// <summary>Gets the <c>CharacterNotEncodable</c> code: a character is in no face of a font family or its fallbacks, so a question mark is drawn instead.</summary>
    /// <remarks>ISO 32000-2 §9.4.3</remarks>
    public static DiagnosticCode CharacterNotEncodable { get; } = new(nameof(CharacterNotEncodable), DiagnosticArea.Layout, DiagnosticSeverity.Warning, "ISO 32000-2 §9.4.3");

    /// <summary>Gets the <c>DocumentLanguageMissing</c> code: a tagged document the document builder wrote has no natural language.</summary>
    /// <remarks>ISO 32000-2 §14.9.2</remarks>
    public static DiagnosticCode DocumentLanguageMissing { get; } = new(nameof(DocumentLanguageMissing), DiagnosticArea.Layout, DiagnosticSeverity.Warning, "ISO 32000-2 §14.9.2");

    /// <summary>Gets the <c>DocumentTitleMissing</c> code: a tagged document the document builder wrote has no title.</summary>
    /// <remarks>ISO 32000-2 §14.3.3</remarks>
    public static DiagnosticCode DocumentTitleMissing { get; } = new(nameof(DocumentTitleMissing), DiagnosticArea.Layout, DiagnosticSeverity.Warning, "ISO 32000-2 §14.3.3");

    /// <summary>Gets the <c>AppendedToUntaggedDocument</c> code: tagged pages were appended to an untagged document, whose earlier pages stay untagged.</summary>
    /// <remarks>ISO 32000-2 §14.8</remarks>
    public static DiagnosticCode AppendedToUntaggedDocument { get; } = new(nameof(AppendedToUntaggedDocument), DiagnosticArea.Layout, DiagnosticSeverity.Warning, "ISO 32000-2 §14.8");

    /// <summary>Gets the <c>DocumentMetadataKept</c> code: a title or language given for pages appended to a document was ignored because the document already has one.</summary>
    /// <remarks>ISO 32000-2 §14.3.3</remarks>
    public static DiagnosticCode DocumentMetadataKept { get; } = new(nameof(DocumentMetadataKept), DiagnosticArea.Layout, DiagnosticSeverity.Information, "ISO 32000-2 §14.3.3");

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
    ];
}
