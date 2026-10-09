namespace Broadside.Annotations;

/// <summary>
/// The <c>RD</c> entry of a free text, square, circle or caret annotation: the distances from each edge of the annotation rectangle
/// inward to the rectangle where the annotation is drawn.
/// </summary>
/// <remarks>ISO 32000-2 §12.5.6.6 Table 177, §12.5.6.8 Table 180, §12.5.6.11 Table 183 (PDF 1.5). Each difference shall be at least 0.</remarks>
/// <param name="Left">The difference at the left edge.</param>
/// <param name="Top">The difference at the top edge.</param>
/// <param name="Right">The difference at the right edge.</param>
/// <param name="Bottom">The difference at the bottom edge.</param>
public readonly record struct PdfRectangleDifferences(double Left, double Top, double Right, double Bottom);
