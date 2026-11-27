using Broadside.Annotations;
using Broadside.Graphics;
using Broadside.Text;

namespace Broadside;

/// <content>Comparing two documents' text and marking the changes up (issue #644).</content>
public sealed partial class PdfDocument
{
    private static readonly PdfDeviceColor InsertionColor = new(1, 0.95, 0.2);
    private static readonly PdfDeviceColor DeletionColor = new(0.9, 0.1, 0.1);

    /// <summary>
    /// Compares this document's words, as the old version, with <paramref name="other"/>'s, as the new one, and returns what was
    /// inserted, deleted and changed, with the words' pages and quads on each side.
    /// </summary>
    /// <param name="other">The new version.</param>
    /// <param name="options">How words compare; <see langword="null"/> for the defaults (case counts, punctuation counts, artifacts ignored).</param>
    /// <param name="cancellationToken">Stops the comparison: checked while each page's content is read and while the diff runs.</param>
    /// <returns>The changes, in reading order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found in a page's content, its fonts or the structure tree.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §14.8.2.6.2 (word breaks) and §14.8.2.2 (artifacts). Each document's pages are extracted in reading order, as
    /// <see cref="PdfPage.ExtractText()"/> extracts them, and flattened into one sequence of words, so text that moves to another
    /// page is not a change. The shortest edit script between the two sequences is found with Myers' O(ND) algorithm in linear
    /// space. A run of deleted words next to a run of inserted words is one <see cref="TextChangeKind.Changed"/>, split into one
    /// change per page when its words are on more than one page.
    /// </para>
    /// <para>
    /// When the documents differ by more than <see cref="TextComparisonOptions.MaxEditDistance"/> words, each page is compared with
    /// the page of the same index in the other document instead (<see cref="TextComparison.ComparedByPage"/>).
    /// </para>
    /// <para>
    /// Extraction's repairs are recorded in each document's <see cref="Diagnostics"/> as usual; the comparison adds none. A read of
    /// both documents: other reads and renders may run concurrently, no mutation may.
    /// </para>
    /// </remarks>
    public TextComparison CompareText(PdfDocument other, TextComparisonOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(other);
        return TextComparer.Compare(this, other, options ?? new TextComparisonOptions(), cancellationToken);
    }

    /// <summary>
    /// Marks the changes of a comparison up on this document's pages: as the old side, a StrikeOut annotation over the words of each
    /// deletion and change; as the new side, a Highlight annotation over the words of each insertion and change.
    /// </summary>
    /// <param name="comparison">A comparison this document took part in, from <see cref="CompareText"/>.</param>
    /// <param name="isNewSide">
    /// <see langword="true"/> when this document was the new version (<c>other</c> of <see cref="CompareText"/>),
    /// <see langword="false"/> when it was the old one.
    /// </param>
    /// <returns>The number of annotations added.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="comparison"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A change is on a page this document does not have; nothing is added.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.5.6.10 (text markup annotations). Each annotation has one quadrilateral per line of the change, its
    /// <c>Subj</c> is the change's kind (<c>Inserted</c>, <c>Deleted</c> or <c>Changed</c>), and its <c>Contents</c> is the other
    /// side's text when there is one: on the old side what a changed run became, on the new side what it replaced. A mutation: no
    /// read may run concurrently.
    /// </remarks>
    public int AddComparisonMarkup(TextComparison comparison, bool isNewSide)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        foreach (TextChange change in comparison.Changes)
        {
            int page = isNewSide ? change.NewPageIndex : change.OldPageIndex;
            if (page >= Pages.Count)
            {
                throw new ArgumentException($"The comparison has a change on page {page}, which this document does not have.", nameof(comparison));
            }
        }

        int added = 0;
        foreach (TextChange change in comparison.Changes)
        {
            int page = isNewSide ? change.NewPageIndex : change.OldPageIndex;
            IReadOnlyList<Quad> quads = isNewSide ? change.NewQuads : change.OldQuads;
            if (page < 0 || quads.Count == 0)
            {
                continue;
            }

            string counterpart = isNewSide ? change.OldText : change.NewText;
            var marks = new PdfQuadrilateral[quads.Count];
            for (int i = 0; i < quads.Count; i++)
            {
                // The order most writers and viewers use for text markup: top-left, top-right, bottom-left, bottom-right.
                Quad quad = quads[i];
                marks[i] = new PdfQuadrilateral(quad.TopLeft, quad.TopRight, quad.BottomLeft, quad.BottomRight);
            }

            PdfAnnotationOptions annotation = new PdfAnnotationOptions()
                .WithColor(isNewSide ? InsertionColor : DeletionColor)
                .WithSubject(change.Kind.ToString())
                .WithContents(counterpart.Length > 0 ? counterpart : null);
            PdfPage target = Pages[page];
            if (isNewSide)
            {
                target.AddHighlightAnnotation(marks, annotation);
            }
            else
            {
                target.AddStrikeOutAnnotation(marks, annotation);
            }

            added++;
        }

        return added;
    }
}
