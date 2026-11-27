namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics applying a redaction records when it removes more than the areas cover, or changes what others rely on.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.5.6.23 (Table 195), §8.9, §8.10 and §9.4. Each code is reported under <see cref="DiagnosticArea.Redaction"/>; see
/// <see cref="DiagnosticCatalog"/> for every area. They are recorded on the document (<see cref="PdfDocument.Diagnostics"/>) and in
/// <see cref="PdfRedactionResult.Diagnostics"/>. Content is never left in place: what cannot be cut exactly is removed whole, and the
/// warnings below say so. In strict mode a warning throws, and the redaction is undone before the exception leaves.
/// </para>
/// </remarks>
public static class RedactionDiagnostics
{
    /// <summary>Gets the <c>RedactionImageRemovedWhole</c> code: an image an area covers only in part was removed whole.</summary>
    /// <remarks>ISO 32000-2 §8.9.5</remarks>
    public static DiagnosticCode RedactionImageRemovedWhole { get; } = new(nameof(RedactionImageRemovedWhole), DiagnosticArea.Redaction, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.5",
        "An image that a redaction area covers only in part could not have just its covered samples blanked. The whole image is removed.");

    /// <summary>Gets the <c>RedactionTextRemovedWhole</c> code: a text-showing operator whose glyphs could not be matched to its strings was removed whole.</summary>
    /// <remarks>ISO 32000-2 §9.4.3</remarks>
    public static DiagnosticCode RedactionTextRemovedWhole { get; } = new(nameof(RedactionTextRemovedWhole), DiagnosticArea.Redaction, DiagnosticSeverity.Warning, "ISO 32000-2 §9.4.3",
        "A text-showing operator with glyphs under a redaction area could not be rebuilt without them. The whole operator is removed.");

    /// <summary>Gets the <c>RedactionFormRemovedWhole</c> code: a form XObject painted over an area was nested too deeply to rewrite, so its <c>Do</c> was removed.</summary>
    /// <remarks>ISO 32000-2 §8.10.1</remarks>
    public static DiagnosticCode RedactionFormRemovedWhole { get; } = new(nameof(RedactionFormRemovedWhole), DiagnosticArea.Redaction, DiagnosticSeverity.Warning, "ISO 32000-2 §8.10.1",
        "A form XObject painted over a redaction area is nested deeper than the content options allow, so it cannot be rewritten. The Do that paints it is removed.");

    /// <summary>Gets the <c>RedactionFormCopied</c> code: a form XObject painted over an area was copied before it was rewritten, so other uses of it keep their content.</summary>
    /// <remarks>ISO 32000-2 §8.10.1</remarks>
    public static DiagnosticCode RedactionFormCopied { get; } = new(nameof(RedactionFormCopied), DiagnosticArea.Redaction, DiagnosticSeverity.Information, "ISO 32000-2 §8.10.1",
        "A form XObject painted over a redaction area is copied and the copy rewritten; other pages and forms that paint the original keep it.");

    /// <summary>Gets the <c>RedactionInvalidatesSignatures</c> code: the document is signed, and the redaction changes what the signatures cover.</summary>
    /// <remarks>ISO 32000-2 §12.8</remarks>
    public static DiagnosticCode RedactionInvalidatesSignatures { get; } = new(nameof(RedactionInvalidatesSignatures), DiagnosticArea.Redaction, DiagnosticSeverity.Information, "ISO 32000-2 §12.8",
        "The document is signed. Applying the redaction changes the content the signatures cover, so they no longer verify.");

    /// <summary>
    /// Gets the <c>RedactionMatchNotLocated</c> code: the glyphs of a search match could not all be found again in the content, so
    /// everything under the match's quads was removed instead, neighbors included.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.4.3</remarks>
    public static DiagnosticCode RedactionMatchNotLocated { get; } = new(nameof(RedactionMatchNotLocated), DiagnosticArea.Redaction, DiagnosticSeverity.Warning, "ISO 32000-2 §9.4.3",
        "The glyphs of a search match could not all be found again in the page's content (a ToUnicode map that reads differently, or content drawn twice at one spot). Everything under the match's quads is removed instead.");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        RedactionImageRemovedWhole,
        RedactionTextRemovedWhole,
        RedactionFormRemovedWhole,
        RedactionFormCopied,
        RedactionInvalidatesSignatures,
        RedactionMatchNotLocated,
    ];
}
