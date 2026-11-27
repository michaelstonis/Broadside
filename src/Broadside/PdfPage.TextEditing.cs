using Broadside.Diagnostics;
using Broadside.Redaction;
using Broadside.Text;
using Broadside.Text.Editing;

namespace Broadside;

/// <content>Replacing and deleting text in existing content (issue #641): ISO 32000-2 §9.4.3, §9.10.3 and §14.9.4.</content>
public sealed partial class PdfPage
{
    /// <summary>
    /// Replaces a match found on this page (<see cref="FindText(TextQuery)"/>) with other text, in the content that shows it: the
    /// matched glyphs go, and the replacement is shown where the first of them was, in the same font, size and text state.
    /// </summary>
    /// <param name="match">The match, from this page's <see cref="FindText(TextQuery)"/> or <see cref="FindText(TextQuery, TextExtractionOptions)"/>.</param>
    /// <param name="replacement">The new text; empty deletes the match (<see cref="DeleteText"/>).</param>
    /// <param name="options">The fallback font, where later glyphs go, and whether replacement text is updated; <see langword="null"/> for the defaults.</param>
    /// <returns>Whether the match was replaced, and the diagnostics recorded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="replacement"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="match"/> names another page.</exception>
    /// <exception cref="DiagnosticException">In strict mode, for the first <see cref="TextEditDiagnostics"/> code; the page is then unchanged.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §9.4.3 (Table 107), §9.4.4, §9.3.2 to §9.3.4, §9.6.5, §9.7.6.2, §9.9.2, §9.10.3 and §14.9.4. The page's text is
    /// extracted again (as <see cref="FindText(TextQuery)"/> extracts it, then with hidden content and artifacts) to find the match's
    /// glyphs, which are then found in the content by identity: their code and the corner of their box, as redaction finds them. The
    /// text-showing operator that holds the first of them is rewritten through a <see cref="Content.ContentEditor"/>: its glyphs before
    /// the match stay, the replacement follows, then its glyphs after the match. A match that spans several operators is replaced in its
    /// first one and removed from the others. Text in a form XObject is replaced in a copy of the form, so other pages that paint it keep
    /// the original.
    /// </para>
    /// <para>
    /// Each character is shown in the original font when the font has a code that draws it (simple fonts: the encoding reversed through
    /// glyph names, then ToUnicode; Type 0 fonts: ToUnicode reversed, then the embedded program's "cmap" and the CIDToGIDMap; Type 3
    /// fonts: a code with a glyph procedure), and the embedded program has a glyph for it, which a subset font often does not. Otherwise
    /// it is shown in a fallback font, switched to inside the same text object at the same size: <see cref="PdfTextReplaceOptions.FallbackFont"/>,
    /// then the font resolver's match for the original font's family, weight and style (<see cref="PdfOptions.UseFontResolver"/>), then
    /// Helvetica. An embedded fallback gets only the glyphs it shows (§9.9.2). A code found through the "cmap" that the font's ToUnicode
    /// CMap does not map is added to it, so extraction reads the new text.
    /// </para>
    /// <para>
    /// The <c>ActualText</c>, <c>Alt</c> and <c>E</c> of the marked content and structure elements that hold the match have the old text
    /// replaced (<see cref="PdfTextReplaceOptions.UpdateReplacementText"/>); one that does not hold it is removed. Out of scope: reflowing
    /// a paragraph, and text in annotation appearances and form fields.
    /// </para>
    /// <para>
    /// Lenient reading: a match whose glyphs cannot all be found again, or whose replacement no font can show, is left as it was, and the
    /// fallback font, a removed replacement text and an extended ToUnicode CMap are recorded (<see cref="TextEditDiagnostics"/>); in
    /// strict mode the first of them throws and the page is unchanged. A mutation. The old text stays in the file's original bytes until
    /// it is saved as a full rewrite.
    /// </para>
    /// </remarks>
    public PdfTextEditResult ReplaceText(TextMatch match, string replacement, PdfTextReplaceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        int pageIndex = _document.Pages.IndexOf(this);
        if (match.PageIndex >= 0 && match.PageIndex != pageIndex)
        {
            throw new ArgumentException($"A match on page index {match.PageIndex} cannot be replaced on page index {pageIndex}.", nameof(match));
        }

        var session = new TextEditSession(_document, options ?? new PdfTextReplaceOptions());
        foreach (TextExtractionOptions candidate in (TextExtractionOptions[])[new TextExtractionOptions(), RedactionSession.DefaultExtraction()])
        {
            TextPage text = ExtractText(candidate, null);
            if (Locates(text, text.Searchable, match))
            {
                session.AddPage(this, text, [(match, replacement)]);
                return session.Run();
            }
        }

        session.Report(
            TextEditDiagnostics.TextEditMatchNotLocated,
            $"The match is not in the text of page {pageIndex + 1} as it reads now; it is left as it was.",
            Reference);
        return session.Run();
    }

    /// <summary>Deletes a match found on this page from the content that shows it: <see cref="ReplaceText"/> with an empty replacement.</summary>
    /// <param name="match">The match, from this page's <see cref="FindText(TextQuery)"/>.</param>
    /// <param name="options">Where later glyphs go (<see cref="PdfTextFit.ShiftLine"/> closes the gap) and whether replacement text is updated; <see langword="null"/> for the defaults.</param>
    /// <returns>Whether the match was deleted, and the diagnostics recorded.</returns>
    /// <exception cref="ArgumentException"><paramref name="match"/> names another page.</exception>
    /// <exception cref="DiagnosticException">In strict mode, for the first <see cref="TextEditDiagnostics"/> code; the page is then unchanged.</exception>
    /// <remarks>ISO 32000-2 §9.4.3 and §14.9.4. As <see cref="ReplaceText"/>.</remarks>
    public PdfTextEditResult DeleteText(TextMatch match, PdfTextReplaceOptions? options = null) => ReplaceText(match, string.Empty, options);
}
