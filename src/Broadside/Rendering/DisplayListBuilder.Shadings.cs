using Broadside.Content;
using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>Shadings (ISO 32000-2 §8.7.4): the <c>sh</c> operator and fills and strokes in a shading pattern.</summary>
internal sealed partial class DisplayListBuilder
{
    private DisplayShadingPaint[] _shadingPaints = new DisplayShadingPaint[4];
    private int _shadingPaintCount;

    /// <inheritdoc/>
    /// <remarks>
    /// ISO 32000-2 §8.7.4.2, Table 76: the shading fills the current clip in user space, so it is recorded with the CTM as its
    /// target space and without its Background. A shading that cannot be painted (<see cref="PdfShading.IsValid"/> false, or a
    /// name that is not a shading) adds nothing; its diagnostics are already recorded.
    /// </remarks>
    public override void PaintShading(in ShadingEvent shading, ContentContext context)
    {
        if (shading.Model is not { IsValid: true } model)
        {
            return;
        }

        int paint = AddShadingPaint(model, shading.Ctm, paintsBackground: false, context);
        SetCompositing(context.State, context);
        AddOperation(new DisplayOperation(DisplayOperationKind.DrawShading, default, paint, 0));
    }

    /// <summary>
    /// Returns the shading paint of a fill or stroke in <paramref name="color"/>, a Pattern colour, or −1 when it selects no usable
    /// shading pattern (a tiling pattern, painted by <c>DisplayListBuilder.Tiling.cs</c>, or an invalid one).
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §8.7.4.1, Table 75: the shading is in pattern space, which <see cref="PdfPattern.GetPatternSpace"/> maps to the
    /// default space of the stream that selected the pattern (§8.7.2), and paints its Background. The pattern's ExtGState (§11.6.7)
    /// is not applied yet.
    /// </remarks>
    private int AddPatternShading(in PdfColor color, ContentContext context)
    {
        if (context.GetPattern(color) is not PdfShadingPattern { IsValid: true, Shading: { IsValid: true } shading } pattern)
        {
            return -1;
        }

        return AddShadingPaint(shading, pattern.GetPatternSpace(color), paintsBackground: true, context);
    }

    private int AddShadingPaint(PdfShading shading, in Matrix matrix, bool paintsBackground, ContentContext context)
    {
        ref readonly GraphicsState state = ref context.State;
        var paint = new DisplayShadingPaint(shading, Relative(matrix), paintsBackground, context.DefaultColorSpaces, ColorConversion.FromState(state, DeviceColorModel.Rgb));
        if (_shadingPaintCount > 0 && _shadingPaints[_shadingPaintCount - 1] == paint)
        {
            return _shadingPaintCount - 1;
        }

        Append(ref _shadingPaints, ref _shadingPaintCount, paint);
        return _shadingPaintCount - 1;
    }

    /// <summary>Forgets the shading paints, releasing the shadings they referenced.</summary>
    private void ResetShadings()
    {
        Array.Clear(_shadingPaints, 0, _shadingPaintCount);
        _shadingPaintCount = 0;
    }
}
