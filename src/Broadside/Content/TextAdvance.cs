using Broadside.Graphics;

namespace Broadside.Content;

/// <summary>
/// Writes the replacement of a text-showing operator that shows nothing and moves as far as it did: a <c>TJ</c> of one adjustment,
/// after the line move of <c>'</c> and the spacing and line move of <c>"</c>. Redaction and sanitizing use it so that the glyphs
/// after a removed operator stay where they were.
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.4.3 and §9.4.4: a number in a <c>TJ</c> array moves the text matrix by <c>-n / 1000 × T<sub>fs</sub> × T<sub>h</sub></c>
/// horizontally (<c>-n / 1000 × T<sub>fs</sub></c> vertically), and changes nothing else; <c>T*</c>, <c>Tw</c> and <c>Tc</c> keep
/// the effects <c>'</c> and <c>"</c> have on the line matrix and the spacing (§9.4.2, Table 106; §9.3.1, Table 102).
/// </remarks>
internal static class TextAdvance
{
    /// <summary>
    /// Writes the replacement into <paramref name="writer"/>, which is cleared first. Called after the operator ran: the move is read
    /// from the text matrix it started from and the one it left in <paramref name="context"/>.
    /// </summary>
    /// <param name="writer">The writer the replacement goes to.</param>
    /// <param name="code">The text-showing operator replaced.</param>
    /// <param name="start">The text matrix before the operator (for <c>'</c> and <c>"</c>, the line matrix it moves to is used).</param>
    /// <param name="vertical">Whether the font writes vertically.</param>
    /// <param name="context">The run, after the operator.</param>
    public static void Write(ContentWriter writer, ContentOperatorCode code, Matrix start, bool vertical, ContentContext context)
    {
        ref readonly GraphicsState state = ref context.State;
        writer.Clear();
        if (code is ContentOperatorCode.NextLineShowText or ContentOperatorCode.NextLineShowTextWithSpacing)
        {
            if (code == ContentOperatorCode.NextLineShowTextWithSpacing)
            {
                writer.Number(state.WordSpacing).Operator("Tw").Number(state.CharacterSpacing).Operator("Tc");
            }

            writer.Operator("T*");
            start = context.TextLineMatrix;
        }

        double adjustment = 0;
        if (start.TryInvert(out Matrix inverse))
        {
            Matrix move = context.TextMatrix * inverse;
            double size = state.FontSize;
            double scaling = state.HorizontalScaling;
            adjustment = vertical
                ? (size == 0 ? 0 : -move.F * 1000 / size)
                : (size * scaling == 0 ? 0 : -move.E * 1000 / (size * scaling));
        }

        writer.Write((byte)'[');
        writer.Number(adjustment);
        writer.Write((byte)']');
        writer.Write((byte)' ');
        writer.Operator("TJ");
    }
}
