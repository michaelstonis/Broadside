using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// A shading paint in a display list: the resolved shading, the space its coordinates are in, whether its Background is painted,
/// and what its colours' conversion depends on. Never converted: a backend evaluates and converts the colours itself. Valid only
/// during the backend call that hands it over.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.7.4: a shading reaches the backend from the <c>sh</c> operator (§8.7.4.2, Table 76) or as the paint of a fill
/// or stroke in a shading pattern (§8.7.4.1, Table 75). The model (<see cref="PdfShading"/>, one of its seven subclasses) is an
/// immutable snapshot, safe to read from any thread; its colours are in <see cref="PdfShading.InterpolationColorSpace"/> and
/// <see cref="PdfShading.EvaluateFunction"/> gives them for a parametric value.
/// </para>
/// <para>
/// Convert with <c>list.Document.GetColorConverter(shading.Shading.InterpolationColorSpace, shading.Conversion with { Target = … },
/// shading.DefaultColorSpaces)</c> (<see cref="PdfDocument.GetColorConverter(PdfColorSpace, ColorConversion, PdfDefaultColorSpaces?)"/>),
/// and the Background in <see cref="PdfShading.ColorSpace"/> the same way.
/// </para>
/// </remarks>
public readonly ref struct DisplayShading
{
    internal DisplayShading(PdfShading shading, Matrix matrix, bool paintsBackground, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion)
    {
        Shading = shading;
        Matrix = matrix;
        PaintsBackground = paintsBackground;
        DefaultColorSpaces = defaultColorSpaces;
        Conversion = conversion;
    }

    /// <summary>Gets the shading: its type, geometry, colour space, function, Background, BBox and AntiAlias hint. Always valid.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.3 to §8.7.4.5.</remarks>
    public PdfShading Shading { get; }

    /// <summary>
    /// Gets the transform from the shading's target space, where its coordinates and BBox are given, to the page's default user
    /// space: the CTM at <c>sh</c>, or pattern space (the pattern matrix followed by the space of the stream that selected the
    /// pattern) for a shading pattern. It does not depend on the transform <see cref="IRenderingBackend.SetTransform"/> set.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.7.4.2 (<c>sh</c>: the current user space), §8.7.2 and §8.7.4.1 (pattern space).</remarks>
    public Matrix Matrix { get; }

    /// <summary>
    /// Gets a value indicating whether the shading's <see cref="PdfShading.Background"/>, when it has one, fills the painted area
    /// outside the shading's bounds before the shading: true for a shading pattern, false for <c>sh</c>, which ignores it.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.7.4.3, Table 77 (<c>Background</c>), and Table 76.</remarks>
    public bool PaintsBackground { get; }

    /// <summary>Gets the default colour spaces current at the paint, which replace the device spaces on conversion.</summary>
    /// <remarks>ISO 32000-2 §8.6.5.6.</remarks>
    public PdfDefaultColorSpaces DefaultColorSpaces { get; }

    /// <summary>Gets the conversion the graphics state asked for at the paint, with <see cref="ColorConversion.Target"/> RGB.</summary>
    /// <remarks>ISO 32000-2 §8.6.5.8, §8.6.5.9 and §10.4.2.3.</remarks>
    public ColorConversion Conversion { get; }
}
