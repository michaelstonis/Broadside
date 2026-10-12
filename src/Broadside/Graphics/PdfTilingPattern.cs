using Broadside.Graphics.Shadings;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Graphics;

/// <summary>A tiling pattern (PatternType 1): a pattern cell, painted by a content stream, replicated at fixed intervals.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.7.3.1, Table 74. The cell is clipped to <see cref="BoundingBox"/> and repeated at multiples of
/// <see cref="XStep"/> and <see cref="YStep"/> in pattern space. Its content runs with
/// <see cref="Content.ContentContext.RunPatternCell"/>: from the graphics state at the beginning of the stream that selected the
/// pattern, with the CTM mapping pattern space and the resources of <see cref="Resources"/>. A coloured pattern
/// (<see cref="PdfTilingPaintType.Colored"/>) sets its own colours; an uncoloured one is a stencil painted in the colour given with
/// <c>scn</c>, and colour operators in it are ignored (§8.7.3.3).
/// </para>
/// <para>
/// Repairs: a missing or zero step uses the bounding box's width or height; a missing bounding box uses [0 0 XStep YStep]; a
/// missing or invalid PaintType reads as coloured (an uncoloured colour, with components, still paints it as a stencil); a missing
/// or invalid TilingType reads as 1; missing Resources make the cell use the resources of the stream that paints with it. Each is
/// recorded as a diagnostic; a pattern with neither usable steps nor a bounding box is invalid.
/// </para>
/// </remarks>
public sealed class PdfTilingPattern : PdfPattern
{
    private readonly PdfDocument _document;

    internal PdfTilingPattern(ShadingReader reader)
        : base(reader, PdfPatternType.Tiling)
    {
        _document = reader.Document;
        Stream = reader.CosObject as CosStream ?? new CosStream(reader.Dictionary, ReadOnlyMemory<byte>.Empty);
        if (reader.CosObject is not CosStream)
        {
            reader.Invalid(DiagnosticCodes.PatternTypeInvalid, "A tiling pattern shall be a stream; it is a dictionary.");
        }

        int? paintType = reader.Integer(ShadingNames.PaintType);
        if (paintType is 1 or 2)
        {
            PaintType = (PdfTilingPaintType)paintType.Value;
            IsPaintTypeKnown = true;
        }
        else
        {
            reader.Report(DiagnosticCodes.PatternEntryInvalid, "A tiling pattern's PaintType is missing or not 1 or 2; it is decided by the colour that uses it.");
            PaintType = PdfTilingPaintType.Colored;
        }

        int? tilingType = reader.Integer(ShadingNames.TilingType);
        if (tilingType is >= 1 and <= 3)
        {
            TilingType = (PdfTilingType)tilingType.Value;
        }
        else
        {
            reader.Report(DiagnosticCodes.PatternEntryInvalid, "A tiling pattern's TilingType is missing or not 1, 2 or 3; 1 is used.");
            TilingType = PdfTilingType.ConstantSpacing;
        }

        double? xStep = reader.Number(ShadingNames.XStep) is { } x and not 0 ? x : null;
        double? yStep = reader.Number(ShadingNames.YStep) is { } y and not 0 ? y : null;
        PdfRectangle? box = reader.Rectangle(ShadingNames.BBox, DiagnosticCodes.TilingPatternBBoxMissing);
        if (box is null && xStep is { } bx && yStep is { } by)
        {
            reader.Report(DiagnosticCodes.TilingPatternBBoxMissing, "A tiling pattern has no usable BBox; [0 0 XStep YStep] is used.");
            box = new PdfRectangle(0, 0, bx, by);
        }

        if (box is { } bounds && (xStep is null || yStep is null))
        {
            reader.Report(DiagnosticCodes.TilingPatternStepInvalid, "A tiling pattern's XStep or YStep is missing or zero; the bounding box's width or height is used.");
            xStep ??= bounds.Width;
            yStep ??= bounds.Height;
        }

        if (box is null || xStep is null or 0 || yStep is null or 0)
        {
            reader.Invalid(DiagnosticCodes.TilingPatternStepInvalid, "A tiling pattern has neither usable steps nor a bounding box; it paints nothing.");
        }

        BoundingBox = box ?? default;
        XStep = xStep ?? 0;
        YStep = yStep ?? 0;
        Resources = reader.Resolve(reader.Get(ShadingNames.Resources)) as CosDictionary;
        if (Resources is null)
        {
            reader.Report(DiagnosticCodes.PatternResourcesMissing, "A tiling pattern has no Resources dictionary; the cell uses the resources of the stream that paints with it.");
        }

        IsValid = reader.IsValid;
    }

    /// <summary>Gets the pattern's content stream, which paints one cell.</summary>
    /// <remarks>ISO 32000-2 §8.7.3.1: a tiling pattern is a stream.</remarks>
    public CosStream Stream { get; }

    /// <summary>Gets how the pattern's colour is given: by its content (coloured) or with the pattern (uncoloured).</summary>
    /// <remarks>ISO 32000-2 §8.7.3.1, Table 74 (<c>PaintType</c>, required).</remarks>
    public PdfTilingPaintType PaintType { get; }

    /// <summary>Gets how the spacing of tiles may be adjusted to the device.</summary>
    /// <remarks>ISO 32000-2 §8.7.3.1, Table 74 (<c>TilingType</c>, required).</remarks>
    public PdfTilingType TilingType { get; }

    /// <summary>Gets the pattern cell's bounding box in pattern space, which clips the cell; it may have no area (one device pixel is still painted).</summary>
    /// <remarks>ISO 32000-2 §8.7.3.1, Table 74 (<c>BBox</c>, required).</remarks>
    public PdfRectangle BoundingBox { get; }

    /// <summary>Gets the horizontal spacing between cells in pattern space; never zero for a valid pattern, possibly negative.</summary>
    /// <remarks>ISO 32000-2 §8.7.3.1, Table 74 (<c>XStep</c>, required).</remarks>
    public double XStep { get; }

    /// <summary>Gets the vertical spacing between cells in pattern space; never zero for a valid pattern, possibly negative.</summary>
    /// <remarks>ISO 32000-2 §8.7.3.1, Table 74 (<c>YStep</c>, required).</remarks>
    public double YStep { get; }

    /// <summary>Gets the resources the cell's content uses, or <see langword="null"/> when the pattern has none.</summary>
    /// <remarks>ISO 32000-2 §8.7.3.1, Table 74 (<c>Resources</c>, required).</remarks>
    public CosDictionary? Resources { get; }

    /// <summary>Gets a value indicating whether <see cref="PaintType"/> came from the dictionary rather than a repair.</summary>
    internal bool IsPaintTypeKnown { get; }

    /// <summary>Returns the cell's content stream data, decoded through the document's filters.</summary>
    /// <returns>The decoded content.</returns>
    /// <remarks>ISO 32000-2 §8.7.3.1.</remarks>
    public ReadOnlyMemory<byte> GetContent() => _document.DecodeStream(Stream);
}
