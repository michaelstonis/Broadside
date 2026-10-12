namespace Broadside.Annotations;

/// <summary>
/// The border of an annotation: its width, style, dash pattern and corner radii, from the border style dictionary (<c>BS</c>) when
/// there is one, else from the <c>Border</c> array, else the default (solid, 1 point wide, square corners).
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.5.4, Table 168, and §12.5.2, Table 166 (<c>Border</c>, default <c>[0 0 1]</c>). "If an annotation dictionary
/// includes the BS entry, then the Border entry is ignored." A snapshot computed when <see cref="PdfAnnotation.Border"/> is read.
/// A conforming reader ignores the border when it renders an appearance stream (§12.5.2).
/// </remarks>
public sealed class PdfAnnotationBorder
{
    private readonly double[] _dashes;

    internal PdfAnnotationBorder(double width, PdfBorderStyle style, ReadOnlySpan<double> dashes, double horizontalCornerRadius, double verticalCornerRadius, PdfBorderSource source)
    {
        Width = width;
        Style = style;
        _dashes = dashes.ToArray();
        HorizontalCornerRadius = horizontalCornerRadius;
        VerticalCornerRadius = verticalCornerRadius;
        Source = source;
    }

    /// <summary>Gets the border width in points; 0 draws no border.</summary>
    /// <remarks>ISO 32000-2 §12.5.4, Table 168 (<c>W</c>, default 1), or the third element of <c>Border</c>.</remarks>
    public double Width { get; }

    /// <summary>Gets the border style.</summary>
    /// <remarks>ISO 32000-2 §12.5.4, Table 168 (<c>S</c>, default solid); a <c>Border</c> array with a dash array is dashed.</remarks>
    public PdfBorderStyle Style { get; }

    /// <summary>Gets the dash pattern of a dashed border: alternating dash and gap lengths, phase 0; empty for a solid one.</summary>
    /// <remarks>ISO 32000-2 §12.5.4, Table 168 (<c>D</c>, default <c>[3]</c>), or the fourth element of <c>Border</c> (PDF 1.1).</remarks>
    public IReadOnlyList<double> DashPattern => _dashes;

    /// <summary>Gets the horizontal corner radius (the first element of <c>Border</c>); 0 for square corners and for a <c>BS</c> border.</summary>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166.</remarks>
    public double HorizontalCornerRadius { get; }

    /// <summary>Gets the vertical corner radius (the second element of <c>Border</c>); 0 for square corners and for a <c>BS</c> border.</summary>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166.</remarks>
    public double VerticalCornerRadius { get; }

    /// <summary>Gets the entry the border was read from.</summary>
    public PdfBorderSource Source { get; }
}
