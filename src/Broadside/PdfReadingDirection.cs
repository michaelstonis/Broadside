namespace Broadside;

/// <summary>The predominant order of text, which places pages side by side: the viewer preference <c>Direction</c>.</summary>
/// <remarks>ISO 32000-2 §12.2, Table 147 (PDF 1.3). The default is <see cref="LeftToRight"/>.</remarks>
public enum PdfReadingDirection
{
    /// <summary>Left to right (<c>L2R</c>, the default).</summary>
    LeftToRight,

    /// <summary>Right to left, vertical writing systems included (<c>R2L</c>).</summary>
    RightToLeft,
}
