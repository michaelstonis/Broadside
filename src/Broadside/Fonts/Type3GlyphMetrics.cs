using Broadside.Content;

namespace Broadside.Fonts;

/// <summary>
/// What a Type 3 glyph description declares with its first operator (<c>d0</c> or <c>d1</c>): whether it specifies its colour, the
/// width it gives and, for <c>d1</c>, its bounding box, all in glyph space.
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.6.4, Table 111. Read from the description's first operator without running it, once per description while its
/// stream is unchanged; see <see cref="GlyphEvent.Type3Metrics"/>. The advance of a shown glyph comes from the font's <c>Widths</c>
/// (Table 110), never from <see cref="WidthX"/>. The default value (kind <see cref="Type3GlyphKind.None"/>) stands for a glyph that is
/// not a Type 3 glyph or has no description.
/// </remarks>
public readonly record struct Type3GlyphMetrics
{
    /// <summary>Gets how the description declares itself.</summary>
    public Type3GlyphKind Kind { get; internal init; }

    /// <summary>Gets <c>wx</c>, the horizontal displacement the description declares, in glyph space; 0 when it declares none.</summary>
    /// <remarks>ISO 32000-2 §9.6.4, Table 111: it shall be consistent with the font's <c>Widths</c> entry.</remarks>
    public double WidthX { get; internal init; }

    /// <summary>Gets <c>wy</c>, the vertical displacement the description declares, in glyph space; 0 when it declares none.</summary>
    /// <remarks>ISO 32000-2 §9.6.4, Table 111: it shall be 0.</remarks>
    public double WidthY { get; internal init; }

    /// <summary>
    /// Gets the glyph bounding box a <c>d1</c> description declares (<c>llx lly urx ury</c>), in glyph space: the smallest rectangle
    /// enclosing every mark the description makes. All zero for any other kind.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.6.4, Table 111 (<c>d1</c>).</remarks>
    public PdfRectangle BoundingBox { get; internal init; }

    /// <summary>
    /// Reads the metrics from a description's first operator the way the content interpreter takes its operands (§7.8.2): too many
    /// use the last ones; too few, or one that is not a number, make the operator unusable and the description
    /// <see cref="Type3GlyphKind.Undeclared"/>.
    /// </summary>
    /// <param name="content">The decoded description.</param>
    /// <param name="arena">A scratch operand arena, cleared before and after.</param>
    /// <returns>The metrics; <see cref="Type3GlyphKind.Undeclared"/> when the first operator is not a usable <c>d0</c> or <c>d1</c>.</returns>
    internal static Type3GlyphMetrics Read(ReadOnlySpan<byte> content, OperandArena arena)
    {
        arena.Clear();
        try
        {
            var reader = new ContentReader(content, arena);
            int expected = !reader.Next(out ReadOperator op) ? 0 : op.Code switch
            {
                ContentOperatorCode.SetGlyphWidth => 2,
                ContentOperatorCode.SetGlyphWidthAndBoundingBox => 6,
                _ => 0,
            };
            if (expected == 0 || arena.Count < expected)
            {
                return new Type3GlyphMetrics { Kind = Type3GlyphKind.Undeclared };
            }

            ContentOperands operands = arena.Last(expected);
            for (int index = 0; index < expected; index++)
            {
                if (!operands[index].IsNumber)
                {
                    return new Type3GlyphMetrics { Kind = Type3GlyphKind.Undeclared };
                }
            }

            return new Type3GlyphMetrics
            {
                Kind = expected == 2 ? Type3GlyphKind.ShapeAndColor : Type3GlyphKind.ShapeOnly,
                WidthX = operands[0].Number,
                WidthY = operands[1].Number,
                BoundingBox = expected == 2 ? default : new PdfRectangle(operands[2].Number, operands[3].Number, operands[4].Number, operands[5].Number),
            };
        }
        finally
        {
            arena.Clear();
        }
    }
}
