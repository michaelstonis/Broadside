namespace Broadside.Annotations;

/// <summary>A border effect: how the border of a square, circle, polygon or free text annotation is drawn (plain or cloudy).</summary>
/// <remarks>ISO 32000-2 §12.5.4, Table 169 (PDF 1.5). A snapshot computed when the annotation's <c>BorderEffect</c> is read.</remarks>
public sealed class PdfBorderEffect
{
    internal PdfBorderEffect(PdfBorderEffectStyle style, double intensity)
    {
        Style = style;
        Intensity = intensity;
    }

    /// <summary>Gets the effect (<c>S</c>, default no effect).</summary>
    /// <remarks>ISO 32000-2 §12.5.4, Table 169.</remarks>
    public PdfBorderEffectStyle Style { get; }

    /// <summary>Gets the intensity of the effect, from 0 to 2 (<c>I</c>, default 0); meaningful only for <see cref="PdfBorderEffectStyle.Cloudy"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.4, Table 169.</remarks>
    public double Intensity { get; }
}
