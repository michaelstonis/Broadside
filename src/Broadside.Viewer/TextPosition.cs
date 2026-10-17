namespace Broadside.Viewer;

/// <summary>
/// A caret position in a document's selectable text: between two glyphs of one page, counted in the page's reading order.
/// </summary>
/// <param name="PageIndex">The zero-based page.</param>
/// <param name="Offset">
/// The number of glyphs before the caret on the page, in reading order: 0 is the page's start; an offset at or past the page's
/// <see cref="SelectableText.Length"/> stands for its end.
/// </param>
/// <remarks>ISO 32000-2 §14.8.2.5 (reading order). Spec #33 viewer capability 4.</remarks>
public readonly record struct TextPosition(int PageIndex, int Offset) : IComparable<TextPosition>
{
    /// <summary>Compares two positions in document order: by page, then by offset.</summary>
    /// <param name="other">The other position.</param>
    /// <returns>Negative when this position comes first, 0 when equal, positive when it comes after.</returns>
    public int CompareTo(TextPosition other) => PageIndex != other.PageIndex ? PageIndex.CompareTo(other.PageIndex) : Offset.CompareTo(other.Offset);

    /// <summary>Whether <paramref name="left"/> comes before <paramref name="right"/>.</summary>
    /// <param name="left">A position.</param>
    /// <param name="right">Another position.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> comes first.</returns>
    public static bool operator <(TextPosition left, TextPosition right) => left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> comes after <paramref name="right"/>.</summary>
    /// <param name="left">A position.</param>
    /// <param name="right">Another position.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> comes after.</returns>
    public static bool operator >(TextPosition left, TextPosition right) => left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> comes before or equals <paramref name="right"/>.</summary>
    /// <param name="left">A position.</param>
    /// <param name="right">Another position.</param>
    /// <returns><see langword="true"/> unless <paramref name="left"/> comes after.</returns>
    public static bool operator <=(TextPosition left, TextPosition right) => left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> comes after or equals <paramref name="right"/>.</summary>
    /// <param name="left">A position.</param>
    /// <param name="right">Another position.</param>
    /// <returns><see langword="true"/> unless <paramref name="left"/> comes first.</returns>
    public static bool operator >=(TextPosition left, TextPosition right) => left.CompareTo(right) >= 0;
}
