namespace Broadside.Annotations;

/// <summary>The style of a border effect, the <c>S</c> entry of a border effect dictionary.</summary>
/// <remarks>ISO 32000-2 §12.5.4, Table 169. Any other name reads as <see cref="None"/>.</remarks>
public enum PdfBorderEffectStyle
{
    /// <summary><c>S</c>: no effect (the default).</summary>
    None,

    /// <summary><c>C</c>: the border is drawn as a series of convex curves, as a cloud.</summary>
    Cloudy,
}
