using System.Globalization;
using Broadside.Graphics.Colors;
using Broadside.Objects;
using Broadside.Writing;

namespace Broadside.Graphics;

/// <summary>Shadings and patterns on the canvas (issue #155): <c>sh</c>, pattern colours, and the canvas of a tiling pattern's cell.</summary>
public sealed partial class PdfCanvas
{
    private static readonly CosName PatternCategory = new("Pattern");
    private static readonly CosName ShadingCategory = new("Shading");

    /// <summary>The tiling pattern's stream this canvas draws the cell of, or <see langword="null"/>.</summary>
    private readonly CosStream? _cell;

    /// <summary>Whether the canvas draws the cell of an uncoloured tiling pattern, where colours shall not be specified (§8.7.3.3).</summary>
    private readonly bool _stencil;

    internal PdfCanvas(PdfDocument document, PdfTilingPattern pattern)
    {
        _document = document;
        _cell = pattern.Stream;
        _stencil = pattern.PaintType == PdfTilingPaintType.Uncolored;
        if (pattern.Resources is not { } resources)
        {
            resources = [];
            pattern.Stream.Dictionary[KnownNames.Resources] = resources;
        }

        _resources = new CanvasResources(document, resources);
    }

    /// <summary>Paints a shading over the current clipping region (<c>sh</c>), in the current user space.</summary>
    /// <param name="shading">A valid shading of this document, such as one <see cref="PdfDocument.CreateAxialShading(PathPoint, PathPoint, IReadOnlyList{PdfGradientStop}, PdfShadingOptions?)"/> creates.</param>
    /// <exception cref="ArgumentException">The shading belongs to another document or is not valid.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, the canvas draws an uncoloured pattern's cell, or the canvas is disposed.</exception>
    /// <remarks>
    /// ISO 32000-2 §8.7.4.2 (<c>sh</c>, PDF 1.3) and Table 77: the shading's coordinates are in the current user space, its
    /// <c>Background</c> is ignored and its <c>BBox</c> clips. The whole clipping region is painted, so set a clip first to shade one
    /// area. The shading is named in the <c>Shading</c> resources. An uncoloured tiling pattern's cell shall not paint a shading
    /// (§8.7.3.3). Records PDF 1.3.
    /// </remarks>
    public void PaintShading(PdfShading shading)
    {
        ArgumentNullException.ThrowIfNull(shading);
        ThrowIfNotAtPageLevel();
        ThrowIfStencil();
        if (!ReferenceEquals(shading.Document, _document))
        {
            throw new ArgumentException("The shading belongs to another document.", nameof(shading));
        }

        if (!shading.IsValid)
        {
            throw new ArgumentException("The shading is not valid; it would paint nothing.", nameof(shading));
        }

        _document.RequireFeature(VersionedFeatures.Shadings);
        _writer.Name(_resources.Name(ShadingCategory, "Sh", (CosObject?)shading.Reference ?? shading.CosObject)).Operator("sh");
    }

    /// <summary>Sets a coloured tiling pattern or a shading pattern as the fill colour (<c>/Pattern cs /name scn</c>).</summary>
    /// <param name="pattern">A valid pattern of this document: a shading pattern, or a coloured tiling pattern.</param>
    /// <exception cref="ArgumentException">The pattern belongs to another document, is not valid, or is an uncoloured tiling pattern (which needs a colour: use the other overload).</exception>
    /// <exception cref="InvalidOperationException">A path is being built, the pattern is the one whose cell this canvas draws, the canvas draws an uncoloured pattern's cell, or the canvas is disposed.</exception>
    /// <remarks>
    /// ISO 32000-2 §8.7.3.2 and §8.7.4.2: the Pattern colour space is selected with <c>cs</c> when it is not the current one, and the
    /// pattern, named in the <c>Pattern</c> resources, with <c>scn</c>. The pattern's matrix maps pattern space to the default space of
    /// the page or form this canvas draws on (§8.7.2), whatever the current transformation. Records PDF 1.2, and 1.3 for a shading pattern.
    /// </remarks>
    public void SetFillPattern(PdfPattern pattern) => SetPattern(pattern, stroking: false);

    /// <summary>Sets a coloured tiling pattern or a shading pattern as the stroke colour (<c>/Pattern CS /name SCN</c>).</summary>
    /// <param name="pattern">A valid pattern of this document: a shading pattern, or a coloured tiling pattern.</param>
    /// <exception cref="ArgumentException">The pattern belongs to another document, is not valid, or is an uncoloured tiling pattern.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, the pattern is the one whose cell this canvas draws, the canvas draws an uncoloured pattern's cell, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.7.3.2 and §8.7.4.2. As <see cref="SetFillPattern(PdfPattern)"/>, for the stroking colour.</remarks>
    public void SetStrokePattern(PdfPattern pattern) => SetPattern(pattern, stroking: true);

    /// <summary>Sets an uncoloured tiling pattern, painted in <paramref name="color"/>, as the fill colour (<c>/CSn cs c1 … cn /name scn</c>).</summary>
    /// <param name="pattern">A valid uncoloured tiling pattern of this document.</param>
    /// <param name="color">The colour the cell is painted in: a colour of a device space, or of a space of this document, not a Pattern space.</param>
    /// <exception cref="ArgumentException">The pattern belongs to another document, is not valid or is not uncoloured; the colour's space is a Pattern space or another document's, or the colour has the wrong number of components or one that is not finite.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, the pattern is the one whose cell this canvas draws, the canvas draws an uncoloured pattern's cell, or the canvas is disposed.</exception>
    /// <remarks>
    /// ISO 32000-2 §8.7.3.3: the colour space is a Pattern space with the colour's space as its underlying space,
    /// <c>[/Pattern underlying]</c>, named in the <c>ColorSpace</c> resources (one object per underlying space in a document); the
    /// colour's components precede the pattern's name as operands of <c>scn</c>. Records PDF 1.2 and the underlying space's version.
    /// </remarks>
    public void SetFillPattern(PdfTilingPattern pattern, PdfColor color) => SetUncoloredPattern(pattern, color, stroking: false);

    /// <summary>Sets an uncoloured tiling pattern, painted in <paramref name="color"/>, as the stroke colour (<c>/CSn CS c1 … cn /name SCN</c>).</summary>
    /// <param name="pattern">A valid uncoloured tiling pattern of this document.</param>
    /// <param name="color">The colour the cell is painted in, as for <see cref="SetFillPattern(PdfTilingPattern, PdfColor)"/>.</param>
    /// <exception cref="ArgumentException">The pattern or the colour cannot be used, as for <see cref="SetFillPattern(PdfTilingPattern, PdfColor)"/>.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, the pattern is the one whose cell this canvas draws, the canvas draws an uncoloured pattern's cell, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.7.3.3. As <see cref="SetFillPattern(PdfTilingPattern, PdfColor)"/>, for the stroking colour.</remarks>
    public void SetStrokePattern(PdfTilingPattern pattern, PdfColor color) => SetUncoloredPattern(pattern, color, stroking: true);

    private void SetPattern(PdfPattern pattern, bool stroking)
    {
        CheckPattern(pattern);
        if (pattern is PdfTilingPattern { PaintType: PdfTilingPaintType.Uncolored })
        {
            throw new ArgumentException("An uncoloured tiling pattern is painted in a colour given with it: use the overload that takes a colour (ISO 32000-2 §8.7.3.3).", nameof(pattern));
        }

        _document.RequireFeature(VersionedFeatures.PatternColorSpace);
        if (pattern.PatternType == PdfPatternType.Shading)
        {
            _document.RequireFeature(VersionedFeatures.Shadings);
        }

        PdfColorSpace space = PdfPatternColorSpace.Colored;
        if (!ReferenceEquals(stroking ? _colors.Stroke : _colors.Fill, space))
        {
            _writer.Name(ColorSpaceNames.Pattern).Operator(stroking ? "CS" : "cs");
        }

        _writer.Name(PatternName(pattern)).Operator(stroking ? "SCN" : "scn");
        _colors = stroking ? _colors with { Stroke = space } : _colors with { Fill = space };
    }

    private void SetUncoloredPattern(PdfTilingPattern pattern, PdfColor color, bool stroking)
    {
        CheckPattern(pattern);
        if (pattern.PaintType != PdfTilingPaintType.Uncolored)
        {
            throw new ArgumentException("A coloured tiling pattern sets its own colours: use the overload without a colour (ISO 32000-2 §8.7.3.2).", nameof(pattern));
        }

        PdfColorSpace underlying = color.ColorSpace;
        if (underlying.Family == PdfColorSpaceFamily.Pattern)
        {
            throw new ArgumentException("An uncoloured pattern's colour shall not be in a Pattern space (ISO 32000-2 §8.6.6.2).", nameof(color));
        }

        if (underlying.Cache is { } cache && !ReferenceEquals(cache.Document, _document))
        {
            throw new ArgumentException("The colour space belongs to another document.", nameof(color));
        }

        if (color.ComponentCount != underlying.ComponentCount)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"The colour has {color.ComponentCount} components; its space has {underlying.ComponentCount}."),
                nameof(color));
        }

        foreach (float component in color.Components)
        {
            if (!float.IsFinite(component))
            {
                throw new ArgumentException("Colour components shall be finite numbers.", nameof(color));
            }
        }

        _document.RequireFeature(VersionedFeatures.PatternColorSpace);
        if (VersionedFeatures.ColorSpace(underlying) is { } feature)
        {
            _document.RequireFeature(feature);
        }

        string? key = DrawingCache.Key(underlying) is { } underlyingKey ? "Pattern|" + underlyingKey : null;
        CosReference reference = _document.Drawing.GetOrAdd(key, () => new CosArray { ColorSpaceNames.Pattern, DrawingCache.Value(underlying) });
        PdfColorSpace space = _document.GetColorSpace(reference);
        if (!ReferenceEquals(stroking ? _colors.Stroke : _colors.Fill, space))
        {
            _writer.Name(_resources.Name(CanvasResources.ColorSpace, "CS", reference)).Operator(stroking ? "CS" : "cs");
        }

        foreach (float component in color.Components)
        {
            _writer.Number(component);
        }

        _writer.Name(PatternName(pattern)).Operator(stroking ? "SCN" : "scn");
        _colors = stroking ? _colors with { Stroke = space } : _colors with { Fill = space };
    }

    private void CheckPattern(PdfPattern pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ThrowIfNotAtPageLevel();
        ThrowIfStencil();
        if (!ReferenceEquals(pattern.Document, _document))
        {
            throw new ArgumentException("The pattern belongs to another document.", nameof(pattern));
        }

        if (!pattern.IsValid)
        {
            throw new ArgumentException("The pattern is not valid; it would paint nothing.", nameof(pattern));
        }

        if (_cell is not null && pattern is PdfTilingPattern tiling && ReferenceEquals(tiling.Stream, _cell))
        {
            throw new InvalidOperationException("A tiling pattern's cell cannot paint with the pattern itself (ISO 32000-2 §8.7.3.1).");
        }
    }

    private CosName PatternName(PdfPattern pattern) =>
        _resources.Name(PatternCategory, "P", (CosObject?)pattern.Reference ?? pattern.CosObject);

    private void ThrowIfStencil()
    {
        if (_stencil)
        {
            throw new InvalidOperationException(
                "An uncoloured tiling pattern's cell shall not specify colours or colour-related parameters; it is painted in the colour given when the pattern is selected (ISO 32000-2 §8.7.3.3).");
        }
    }
}
