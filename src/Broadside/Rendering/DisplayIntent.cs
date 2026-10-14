namespace Broadside.Rendering;

/// <summary>What a page is rendered for, which decides which annotations are drawn and whether they keep their size and orientation on screen.</summary>
/// <remarks>ISO 32000-2 §12.5.3, Table 167 (<c>NoView</c>, <c>Print</c>, <c>NoZoom</c>, <c>NoRotate</c>).</remarks>
public enum DisplayIntent
{
    /// <summary>
    /// On-screen display: annotations with <c>NoView</c> are not drawn, and <c>NoZoom</c> and <c>NoRotate</c> keep an annotation's size
    /// and orientation on the screen whatever the magnification and rotation.
    /// </summary>
    View,

    /// <summary>
    /// Printing: only annotations with the <c>Print</c> flag are drawn, at their place on the page (<c>NoZoom</c> and
    /// <c>NoRotate</c> describe the screen, §12.5.3).
    /// </summary>
    Print,
}
