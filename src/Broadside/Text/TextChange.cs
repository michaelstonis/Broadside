using Broadside.Graphics;

namespace Broadside.Text;

/// <summary>
/// One difference between two documents' words: a run of words deleted, inserted, or replaced by another run, on one page of each
/// side.
/// </summary>
/// <remarks>
/// ISO 32000-2 §14.8.2.6.2 (word breaks) and §9.4.4 (glyph boxes). Quads are in each page's own default user space, as
/// <see cref="TextWord.Quad"/>; a side that has no words in the change has page -1, an empty text and no quads.
/// </remarks>
/// <param name="Kind">What the change did.</param>
/// <param name="OldPageIndex">The zero-based page of the old document the deleted or replaced words are on; -1 for an insertion.</param>
/// <param name="OldText">The old document's words, as the page shows them, joined by single spaces; empty for an insertion.</param>
/// <param name="OldQuads">The old words' quads, one per line, from the first word's left edge to the last word's right edge; empty for an insertion.</param>
/// <param name="NewPageIndex">The zero-based page of the new document the inserted or replacing words are on; -1 for a deletion.</param>
/// <param name="NewText">The new document's words, joined by single spaces; empty for a deletion.</param>
/// <param name="NewQuads">The new words' quads, one per line; empty for a deletion.</param>
public sealed record TextChange(
    TextChangeKind Kind,
    int OldPageIndex,
    string OldText,
    IReadOnlyList<Quad> OldQuads,
    int NewPageIndex,
    string NewText,
    IReadOnlyList<Quad> NewQuads);
