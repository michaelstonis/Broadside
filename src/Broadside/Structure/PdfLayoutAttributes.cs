using Broadside.Objects;

namespace Broadside.Structure;

/// <summary>An attribute object owned by <c>Layout</c>: how an element's content is laid out.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.8.5.4, Tables 377-381 (with the corrected Table 377 of the 2020 errata). Each property is this object's own value,
/// <see langword="null"/> when absent or of the wrong type; it is not inherited and has no default applied. For the value in effect on
/// an element use <see cref="PdfStructureElement.GetAttributeValue(CosName, CosName)"/>.
/// </para>
/// <para>
/// Attributes with several forms read as follows: a side value (<c>BorderStyle</c>, <c>TBorderStyle</c>, <c>BorderThickness</c>,
/// <c>Padding</c>, <c>TPadding</c>) is four entries in the order before, after, start, end, a single value repeated; <c>Width</c>,
/// <c>Height</c>, <c>LineHeight</c> and <c>GlyphOrientationVertical</c> read <see langword="null"/> for their name forms (<c>Auto</c>,
/// <c>Normal</c>). <c>BorderColor</c> and other forms are available through <see cref="PdfAttributeObject.GetValue(CosName)"/>.
/// </para>
/// </remarks>
public sealed class PdfLayoutAttributes : PdfAttributeObject
{
    private static readonly CosName PlacementName = new("Placement");
    private static readonly CosName WritingModeName = new("WritingMode");
    private static readonly CosName BackgroundColorName = new("BackgroundColor");
    private static readonly CosName BorderStyleName = new("BorderStyle");
    private static readonly CosName BorderThicknessName = new("BorderThickness");
    private static readonly CosName PaddingName = new("Padding");
    private static readonly CosName ColorName = new("Color");
    private static readonly CosName SpaceBeforeName = new("SpaceBefore");
    private static readonly CosName SpaceAfterName = new("SpaceAfter");
    private static readonly CosName StartIndentName = new("StartIndent");
    private static readonly CosName EndIndentName = new("EndIndent");
    private static readonly CosName TextIndentName = new("TextIndent");
    private static readonly CosName TextAlignName = new("TextAlign");
    private static readonly CosName WidthName = new("Width");
    private static readonly CosName HeightName = new("Height");
    private static readonly CosName BlockAlignName = new("BlockAlign");
    private static readonly CosName InlineAlignName = new("InlineAlign");
    private static readonly CosName TBorderStyleName = new("TBorderStyle");
    private static readonly CosName TPaddingName = new("TPadding");
    private static readonly CosName BaselineShiftName = new("BaselineShift");
    private static readonly CosName LineHeightName = new("LineHeight");
    private static readonly CosName TextPositionName = new("TextPosition");
    private static readonly CosName TextDecorationColorName = new("TextDecorationColor");
    private static readonly CosName TextDecorationThicknessName = new("TextDecorationThickness");
    private static readonly CosName TextDecorationTypeName = new("TextDecorationType");
    private static readonly CosName RubyAlignName = new("RubyAlign");
    private static readonly CosName RubyPositionName = new("RubyPosition");
    private static readonly CosName GlyphOrientationVerticalName = new("GlyphOrientationVertical");
    private static readonly CosName ColumnCountName = new("ColumnCount");
    private static readonly CosName ColumnGapName = new("ColumnGap");
    private static readonly CosName ColumnWidthsName = new("ColumnWidths");

    internal PdfLayoutAttributes(StructureContext context, CosObject source, CosReference? reference, int revision)
        : base(context, source, reference, revision)
    {
    }

    /// <summary>Gets <c>Placement</c>: Block, Inline, Before, Start or End.</summary>
    /// <remarks>ISO 32000-2 Table 378.</remarks>
    public string? Placement => NameValue(PlacementName);

    /// <summary>Gets <c>WritingMode</c>: LrTb, RlTb, TbRl, TbLr, LrBt, RlBt, BtRl or BtLr.</summary>
    /// <remarks>ISO 32000-2 Table 378.</remarks>
    public string? WritingMode => NameValue(WritingModeName);

    /// <summary>Gets <c>BackgroundColor</c>, three RGB components (PDF 1.5).</summary>
    /// <remarks>ISO 32000-2 Table 378.</remarks>
    public IReadOnlyList<double>? BackgroundColor => NumbersValue(BackgroundColorName) is { Count: 3 } rgb ? rgb : null;

    /// <summary>Gets <c>BorderStyle</c> per side (before, after, start, end): None, Hidden, Dotted, Dashed, Solid, Double, Groove, Ridge, Inset, Outset; a side may be null.</summary>
    /// <remarks>ISO 32000-2 Table 378 (PDF 1.5).</remarks>
    public IReadOnlyList<string?>? BorderStyle => Sides(BorderStyleName);

    /// <summary>Gets <c>BorderThickness</c> per side (before, after, start, end) (PDF 1.5).</summary>
    /// <remarks>ISO 32000-2 Table 378.</remarks>
    public IReadOnlyList<double>? BorderThickness => SideNumbers(BorderThicknessName);

    /// <summary>Gets <c>Padding</c> per side (before, after, start, end) (PDF 1.5).</summary>
    /// <remarks>ISO 32000-2 Table 378.</remarks>
    public IReadOnlyList<double>? Padding => SideNumbers(PaddingName);

    /// <summary>Gets <c>Color</c>, three RGB components (PDF 1.5).</summary>
    /// <remarks>ISO 32000-2 Table 378.</remarks>
    public IReadOnlyList<double>? Color => NumbersValue(ColorName) is { Count: 3 } rgb ? rgb : null;

    /// <summary>Gets <c>SpaceBefore</c>.</summary>
    /// <remarks>ISO 32000-2 Table 379.</remarks>
    public double? SpaceBefore => NumberValue(SpaceBeforeName);

    /// <summary>Gets <c>SpaceAfter</c>.</summary>
    /// <remarks>ISO 32000-2 Table 379.</remarks>
    public double? SpaceAfter => NumberValue(SpaceAfterName);

    /// <summary>Gets <c>StartIndent</c>.</summary>
    /// <remarks>ISO 32000-2 Table 379.</remarks>
    public double? StartIndent => NumberValue(StartIndentName);

    /// <summary>Gets <c>EndIndent</c>.</summary>
    /// <remarks>ISO 32000-2 Table 379.</remarks>
    public double? EndIndent => NumberValue(EndIndentName);

    /// <summary>Gets <c>TextIndent</c>.</summary>
    /// <remarks>ISO 32000-2 Table 379.</remarks>
    public double? TextIndent => NumberValue(TextIndentName);

    /// <summary>Gets <c>TextAlign</c>: Start, Center, End or Justify.</summary>
    /// <remarks>ISO 32000-2 Table 379.</remarks>
    public string? TextAlign => NameValue(TextAlignName);

    /// <summary>Gets <c>BBox</c>, the element's bounding box (Figure, Form, Formula, Table, Artifact).</summary>
    /// <remarks>ISO 32000-2 Table 379 (errata Table 377).</remarks>
    public PdfRectangle? BoundingBox => RectangleValue(StructureNames.BBox);

    /// <summary>Gets <c>Width</c> when it is a number; <see langword="null"/> for <c>Auto</c> or absent.</summary>
    /// <remarks>ISO 32000-2 Table 379.</remarks>
    public double? Width => NumberValue(WidthName);

    /// <summary>Gets <c>Height</c> when it is a number; <see langword="null"/> for <c>Auto</c> or absent.</summary>
    /// <remarks>ISO 32000-2 Table 379.</remarks>
    public double? Height => NumberValue(HeightName);

    /// <summary>Gets <c>BlockAlign</c> (table cells): Before, Middle, After or Justify.</summary>
    /// <remarks>ISO 32000-2 Table 379.</remarks>
    public string? BlockAlign => NameValue(BlockAlignName);

    /// <summary>Gets <c>InlineAlign</c> (table cells): Start, Center or End.</summary>
    /// <remarks>ISO 32000-2 Table 379.</remarks>
    public string? InlineAlign => NameValue(InlineAlignName);

    /// <summary>Gets <c>TBorderStyle</c> per side (table cells, PDF 1.5).</summary>
    /// <remarks>ISO 32000-2 Table 379.</remarks>
    public IReadOnlyList<string?>? TableBorderStyle => Sides(TBorderStyleName);

    /// <summary>Gets <c>TPadding</c> per side (table cells, PDF 1.5).</summary>
    /// <remarks>ISO 32000-2 Table 379.</remarks>
    public IReadOnlyList<double>? TablePadding => SideNumbers(TPaddingName);

    /// <summary>Gets <c>BaselineShift</c>.</summary>
    /// <remarks>ISO 32000-2 Table 380.</remarks>
    public double? BaselineShift => NumberValue(BaselineShiftName);

    /// <summary>Gets <c>LineHeight</c> when it is a number; <see langword="null"/> for <c>Normal</c>, <c>Auto</c> or absent.</summary>
    /// <remarks>ISO 32000-2 Table 380.</remarks>
    public double? LineHeight => NumberValue(LineHeightName);

    /// <summary>Gets <c>TextPosition</c>: Sup, Sub or Normal (PDF 2.0).</summary>
    /// <remarks>ISO 32000-2 Table 380.</remarks>
    public string? TextPosition => NameValue(TextPositionName);

    /// <summary>Gets <c>TextDecorationColor</c>, three RGB components (PDF 1.5).</summary>
    /// <remarks>ISO 32000-2 Table 380.</remarks>
    public IReadOnlyList<double>? TextDecorationColor => NumbersValue(TextDecorationColorName) is { Count: 3 } rgb ? rgb : null;

    /// <summary>Gets <c>TextDecorationThickness</c> (PDF 1.5).</summary>
    /// <remarks>ISO 32000-2 Table 380.</remarks>
    public double? TextDecorationThickness => NumberValue(TextDecorationThicknessName);

    /// <summary>Gets <c>TextDecorationType</c>: None, Underline, Overline or LineThrough.</summary>
    /// <remarks>ISO 32000-2 Table 380.</remarks>
    public string? TextDecorationType => NameValue(TextDecorationTypeName);

    /// <summary>Gets <c>RubyAlign</c>: Start, Center, End, Justify or Distribute (PDF 1.5).</summary>
    /// <remarks>ISO 32000-2 Table 380.</remarks>
    public string? RubyAlign => NameValue(RubyAlignName);

    /// <summary>Gets <c>RubyPosition</c>: Before, After, Warichu or Inline (PDF 1.5).</summary>
    /// <remarks>ISO 32000-2 Table 380.</remarks>
    public string? RubyPosition => NameValue(RubyPositionName);

    /// <summary>Gets <c>GlyphOrientationVertical</c> in degrees; <see langword="null"/> for <c>Auto</c> or absent (PDF 1.5).</summary>
    /// <remarks>ISO 32000-2 Table 380.</remarks>
    public double? GlyphOrientationVertical => NumberValue(GlyphOrientationVerticalName);

    /// <summary>Gets <c>ColumnCount</c> (PDF 1.6).</summary>
    /// <remarks>ISO 32000-2 Table 381.</remarks>
    public int? ColumnCount => IntegerValue(ColumnCountName);

    /// <summary>Gets <c>ColumnGap</c>: one gap, or one per pair of columns (the last repeats) (PDF 1.6).</summary>
    /// <remarks>ISO 32000-2 Table 381.</remarks>
    public IReadOnlyList<double>? ColumnGap => NumberOrNumbers(ColumnGapName);

    /// <summary>Gets <c>ColumnWidths</c>: one width, or one per column (the last repeats) (PDF 1.6).</summary>
    /// <remarks>ISO 32000-2 Table 381.</remarks>
    public IReadOnlyList<double>? ColumnWidths => NumberOrNumbers(ColumnWidthsName);

    private IReadOnlyList<double>? NumberOrNumbers(CosName name) =>
        NumberValue(name) is { } single ? [single] : NumbersValue(name);

    private IReadOnlyList<double>? SideNumbers(CosName name) => NumberValue(name) is { } single
        ? [single, single, single, single]
        : NumbersValue(name) is { Count: 4 } sides ? sides : null;

    private string?[]? Sides(CosName name)
    {
        switch (GetValue(name))
        {
            case CosName single:
                return [single.Value, single.Value, single.Value, single.Value];
            case CosArray { Count: 4 } array:
                string?[] sides = new string?[4];
                for (int index = 0; index < 4; index++)
                {
                    sides[index] = (Document.Resolve(array[index]) as CosName)?.Value;
                }

                return sides;
            default:
                return null;
        }
    }
}
