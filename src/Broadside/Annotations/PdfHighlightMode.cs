namespace Broadside.Annotations;

/// <summary>The visual effect when a link or widget annotation is activated, its <c>H</c> entry.</summary>
/// <remarks>
/// ISO 32000-2 §12.5.6.5, Table 176 (PDF 1.2), and §12.5.6.19, Table 191. Default <see cref="Invert"/>; widgets also accept
/// <c>T</c>, the same as <c>P</c>. Any other name reads as the default.
/// </remarks>
public enum PdfHighlightMode
{
    /// <summary><c>N</c>: no highlighting.</summary>
    None,

    /// <summary><c>I</c>: invert the colours of the annotation rectangle (the default).</summary>
    Invert,

    /// <summary><c>O</c>: invert the annotation's border.</summary>
    Outline,

    /// <summary><c>P</c> (or <c>T</c> for a widget): display the down appearance, or offset the annotation as if pushed below the page.</summary>
    Push,
}
