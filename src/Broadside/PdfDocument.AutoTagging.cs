using Broadside.Layout;

namespace Broadside;

/// <content>Auto-tagging (issue #191): ISO 32000-2 §14.8; spec story 38.</content>
public sealed partial class PdfDocument
{
    /// <summary>Tags an untagged document from its detected layout tree.</summary>
    /// <returns>What was done.</returns>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found in the content, its fonts or the structure tree.</exception>
    /// <remarks>ISO 32000-2 §14.8. As <see cref="AutoTag(AutoTaggingOptions)"/> with default options.</remarks>
    public AutoTaggingResult AutoTag() => AutoTag(new AutoTaggingOptions());

    /// <summary>Tags a document from its detected layout tree, with options.</summary>
    /// <param name="options">Whether to tag an already tagged document, the language, the figures' placeholder description.</param>
    /// <returns>What was done.</returns>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found in the content, its fonts or the structure tree.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §14.8 (tagged PDF), §14.7 (logical structure), §14.6 (marked content). The layout tree is detected from the pages'
    /// geometry (<see cref="ExtractLayout(LayoutOptions)"/>) and written as a structure tree under one <c>Document</c> element:
    /// paragraphs <c>P</c>, headings <c>H1</c>-<c>H6</c>, lists <c>L</c>/<c>LI</c>/<c>Lbl</c>/<c>LBody</c> with <c>ListNumbering</c>,
    /// tables <c>Table</c>/<c>TR</c>/<c>TH</c>/<c>TD</c> with <c>RowSpan</c>, <c>ColSpan</c>, <c>Scope</c> and <c>Headers</c>
    /// (Table 384), figures <c>Figure</c> with a <c>Layout</c> <c>BBox</c> (Table 378) and a placeholder <c>Alt</c>, captions
    /// <c>Caption</c>. The PDF 1.7 standard structure namespace is used, so nothing needs PDF 2.0.
    /// </para>
    /// <para>
    /// Each page's own content is rewritten (<see cref="PdfPage.RewriteContent(Content.ContentEditor)"/>): runs of operators that show
    /// one element's content are wrapped in <c>BDC</c>/<c>EMC</c> with a new MCID (§14.7.5.2), registered in the parent tree; running
    /// headers and footers become <c>Pagination</c> artifacts, other graphics outside figures <c>Layout</c> artifacts (§14.8.2.2).
    /// Operators are never split or reordered and marked content nests properly with <c>BT</c>/<c>ET</c> and existing sequences
    /// (§14.6.1), so the pages render exactly as before. A form XObject is tagged as a whole through its <c>Do</c>; an operator that
    /// shows glyphs of two elements belongs to the one first in reading order. <c>MarkInfo</c> <c>Marked</c> is set; the changes can
    /// be saved incrementally.
    /// </para>
    /// </remarks>
    public AutoTaggingResult AutoTag(AutoTaggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return AutoTagger.Run(this, options);
    }
}
