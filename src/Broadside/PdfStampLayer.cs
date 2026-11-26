namespace Broadside;

/// <summary>Where a <see cref="PdfStamp"/> is drawn relative to the page's existing content.</summary>
/// <remarks>ISO 32000-2 §7.8.2 and §7.7.3.3, Table 31 (<c>Contents</c>): content streams paint in order.</remarks>
public enum PdfStampLayer
{
    /// <summary>Over the content: a content stream after the page's (the default).</summary>
    Foreground,

    /// <summary>Under the content: a content stream before the page's, which an opaque page background hides.</summary>
    Background,
}
