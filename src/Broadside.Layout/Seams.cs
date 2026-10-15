using Broadside.Annotations;
using Broadside.Fonts;
using Broadside.Graphics;

namespace Broadside.Layout.Flow;

/// <summary>
/// The replaceable steps of flow layout. Each follow-up swaps one implementation here without touching the paginator:
/// <see cref="LineBreaker"/> (#172, UAX #14), <see cref="Shaper"/> (#174 GSUB, #175 GPOS), <see cref="Bidi"/> (#173, UAX #9) and
/// <see cref="Structure"/> (#179, tagging by construction).
/// </summary>
internal sealed record FlowServices(ILineBreaker LineBreaker, ITextShaper Shaper, IBidiResolver Bidi, IStructureSink? Structure)
{
    /// <summary>Breaks lines by UAX #14, measures with the canvas font, keeps logical order and writes no structure.</summary>
    public static FlowServices Default { get; } = new(UnicodeLineBreaker.Instance, CanvasFontShaper.Instance, LogicalOrder.Instance, null);
}

/// <summary>A position a line may (or, after a mandatory break, must) end at: the index the next line starts from.</summary>
internal readonly record struct BreakOpportunity(int Position, bool Mandatory);

/// <summary>Finds where lines may break. The last opportunity is always the end of the text, mandatory.</summary>
internal interface ILineBreaker
{
    /// <summary>Appends the break opportunities of <paramref name="text"/> in ascending position.</summary>
    void FindBreaks(ReadOnlySpan<char> text, List<BreakOpportunity> breaks);
}

/// <summary>
/// Text as it will be shown: the shaped glyphs and, when needed, one extra <c>TJ</c> adjustment per glyph in thousandths of the font
/// size (null when there are none). The shaping step between text and what the canvas writes.
/// </summary>
internal readonly record struct ShapedRun(ShapedText Glyphs, double[]? Adjustments);

/// <summary>Measures and shapes runs of one style: GSUB (#174) and the positioning step (#175 GPOS) through <see cref="PdfCanvasFont.Shape"/>.</summary>
internal interface ITextShaper
{
    /// <summary>The advance of <paramref name="text"/> in points as shown, at character spacing 0, word spacing 0, scaling 100.</summary>
    double Measure(ReadOnlySpan<char> text, TextStyle style);

    /// <summary>Shapes <paramref name="text"/> into what is shown; its advance equals <see cref="Measure"/>.</summary>
    ShapedRun Shape(string text, TextStyle style);
}

/// <summary>Shapes with the canvas font under the style's shaping (<see cref="TextStyle.Shaping"/>, <c>kern</c> from <see cref="TextStyle.Kerning"/>).</summary>
internal sealed class CanvasFontShaper : ITextShaper
{
    public static CanvasFontShaper Instance { get; } = new();

    public double Measure(ReadOnlySpan<char> text, TextStyle style) => style.Font.MeasureWidth(text, style.FontSize, style.EffectiveShaping);

    public ShapedRun Shape(string text, TextStyle style) => new(style.Font.Shape(text, style.EffectiveShaping), null);
}

/// <summary>
/// A run of a composed line: a slice of the paragraph text with its measured advance in points, inside one piece of the paragraph's
/// content (<see cref="Piece"/>: the index of the <see cref="InlineElement"/> it comes from, so each run has one style).
/// </summary>
internal readonly record struct LineRun(int Start, int Length, double Width, int Piece = 0);

/// <summary>Reorders the runs of one broken line from logical into visual order. #173 implements UAX #9 here.</summary>
internal interface IBidiResolver
{
    /// <summary>Reorders <paramref name="runs"/> in place; they arrive in logical order.</summary>
    void ReorderLine(ReadOnlySpan<char> paragraph, Span<LineRun> runs);
}

/// <summary>Leaves runs in logical order: correct for left-to-right text, the only direction before #173.</summary>
internal sealed class LogicalOrder : IBidiResolver
{
    public static LogicalOrder Instance { get; } = new();

    public void ReorderLine(ReadOnlySpan<char> paragraph, Span<LineRun> runs)
    {
    }
}

/// <summary>
/// Receives each block's portion on each page, so tagging (#179) can open a structure element with marked content around it. A block
/// that breaks across pages is ended at the break and begun again on the next page.
/// </summary>
internal interface IStructureSink
{
    /// <summary>The block starts drawing on <paramref name="page"/> through <paramref name="canvas"/>.</summary>
    void BeginBlock(FlowBlock block, PdfPage page, PdfCanvas canvas);

    /// <summary>The block stops drawing on <paramref name="page"/>, at its end or at a page break.</summary>
    void EndBlock(FlowBlock block, PdfPage page, PdfCanvas canvas);

    /// <summary>
    /// A part inside a block starts drawing: a list item (<c>LI</c>), its label (<c>Lbl</c>) or body (<c>LBody</c>), or an inline
    /// element of a paragraph (<c>Span</c>, <c>Link</c>, <c>Figure</c>). Parts nest inside the innermost open block or part and are
    /// ended and begun again at a page break like blocks. Inline parts begin and end within one line (inside its text object). A
    /// list item's label arrives after its body has begun, on the item's first line: <see cref="FlowPart.Owner"/> names the item.
    /// </summary>
    void BeginPart(FlowPart part, PdfPage page, PdfCanvas canvas);

    /// <summary>The part stops drawing on <paramref name="page"/>.</summary>
    void EndPart(FlowPart part, PdfPage page, PdfCanvas canvas);

    /// <summary>An annotation was added for <paramref name="part"/> (a link annotation over a <c>Link</c> run's portion on one line).</summary>
    void AddAnnotation(FlowPart part, PdfAnnotation annotation);
}

/// <summary>
/// A structure part below a block: <paramref name="StructureType"/> is the standard structure type to tag it with,
/// <paramref name="Owner"/> the object it comes from (a <see cref="ListItem"/> for <c>LI</c>, <c>Lbl</c> and <c>LBody</c>; the
/// <see cref="InlineElement"/> for inline parts) and <paramref name="Language"/> its BCP 47 language, or null to inherit.
/// </summary>
internal readonly record struct FlowPart(string StructureType, object Owner, string? Language);
