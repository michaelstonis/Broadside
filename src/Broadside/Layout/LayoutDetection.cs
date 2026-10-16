namespace Broadside.Layout;

/// <summary>Which pages of a layout tree are laid out from their geometry rather than from the structure tree.</summary>
/// <remarks>
/// Spec story 36; ISO 32000-2 §14.8.2.5 (logical content order) and §14.8.2.2 (artifacts). Detection finds text blocks, columns
/// from the white space between them, paragraphs from indentation and spacing, the reading order across columns, and headers and
/// footers repeated on most pages, which become artifacts (left out of the tree and flagged
/// <see cref="Text.TextGlyphFlags.Artifact"/> in <see cref="LayoutPage.Text"/>).
/// </remarks>
public enum LayoutDetection
{
    /// <summary>Pages with no tagged content (none of their glyphs belongs to a structure element) are detected; the default.</summary>
    UntaggedPages = 0,

    /// <summary>Every page is detected, tagged or not; the structure tree is not used.</summary>
    AllPages = 1,

    /// <summary>No page is detected: untagged pages contribute no nodes.</summary>
    None = 2,
}
