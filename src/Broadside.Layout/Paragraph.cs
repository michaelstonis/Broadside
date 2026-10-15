using System.Globalization;
using System.Text;
using Broadside.Fonts;
using Broadside.Graphics;

namespace Broadside.Layout.Flow;

/// <summary>
/// A paragraph of text in one style, broken into lines greedily: each line takes as much text as fits the content width, ending at
/// a line break opportunity of the Unicode Line Breaking Algorithm, and a hard line break (line feed, carriage return, next line,
/// line or paragraph separator, vertical tab, form feed) always ends a line. Lines are aligned or justified and placed one line
/// height apart, continuing on the next page when a line would cross the bottom margin.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.4.3 and §9.4.4: each line is one text object positioned with <c>Tm</c> and shown with <c>Tj</c> (or <c>TJ</c>
/// when kerned). Justification stretches the spaces of every line but a paragraph's last (and lines ended by a hard line break):
/// with word spacing <c>Tw</c> when the font applies it to its single-byte space code, otherwise with a <c>TJ</c> adjustment after
/// each space. The glyph box of a line ([descent, ascent] of the font) is centred vertically in the line height.
/// </para>
/// <para>
/// UAX #14, Unicode Line Breaking Algorithm (revision 53, Unicode 16.0.0), default rules untailored: lines may break after spaces
/// and hyphens, between ideographs and before opening punctuation, never at a no-break space or word joiner. A soft hyphen (U+00AD)
/// is not shown unless the line breaks after it, where a hyphen-minus is shown in its place (UAX #14 §5.3). Spaces and hard line
/// breaks at a line end are not shown. A word wider than the content width breaks at the last extended grapheme cluster boundary
/// (UAX #29) that fits, at least one cluster per line: the emergency break UAX #14 describes under rule LB3.
/// </para>
/// </remarks>
public sealed class Paragraph : FlowBlock
{
    private const char SoftHyphen = '\u00AD';

    /// <summary>Creates a paragraph with structure type <c>P</c>.</summary>
    /// <param name="text">The text. Hard line breaks force line breaks and are not shown; soft hyphens show only at a break.</param>
    /// <param name="style">The style.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="style"/> is null.</exception>
    public Paragraph(string text, ParagraphStyle style)
        : base("P")
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(style);
        Text = text;
        Style = style;
    }

    /// <summary>The text.</summary>
    public string Text { get; }

    /// <summary>The style.</summary>
    public ParagraphStyle Style { get; }

    internal override void Validate(int index)
    {
        Span<char> buffer = stackalloc char[2];
        foreach (Rune rune in Text.EnumerateRunes())
        {
            if (rune.IsBmp && ((char)rune.Value == SoftHyphen || IsHardBreak((char)rune.Value)))
            {
                continue;
            }

            int length = rune.EncodeToUtf16(buffer);
            if (!Style.Font.CanEncode(buffer[..length]))
            {
                throw new ArgumentException(
                    $"The text of paragraph {index} has U+{rune.Value:X4}, which the font {Style.Font.Name} cannot encode.");
            }
        }

        if (Text.Contains(SoftHyphen, StringComparison.Ordinal) && !Style.Font.CanEncode("-"))
        {
            throw new ArgumentException(
                $"The text of paragraph {index} has soft hyphens, and the font {Style.Font.Name} cannot encode the hyphen shown at a break.");
        }
    }

    internal override void LayOut(FlowContext context)
    {
        FlowServices services = context.Services;
        ParagraphStyle style = Style;
        var breaks = new List<BreakOpportunity>();
        services.LineBreaker.FindBreaks(Text, breaks);

        context.AddSpace(style.SpaceBefore);
        context.BeginBlock(this);
        double lineAdvance = style.LineAdvance;
        double ascent = style.Font.Ascent * style.FontSize / 1000;
        double descent = style.Font.Descent * style.FontSize / 1000;
        double halfLeading = (lineAdvance - (ascent - descent)) / 2;
        Span<LineRun> runs = new LineRun[1];

        int start = 0;
        int next = 0;
        bool first = true;
        while (start < Text.Length && next < breaks.Count)
        {
            // Room first: the line is measured against the width of the page it lands on.
            context.EnsureRoom(lineAdvance);
            double indent = first ? style.FirstLineIndent : 0;
            double available = context.Width - indent;
            int chosen = -1;
            int end = start;
            double width = 0;
            bool hyphen = false;
            for (int candidate = next; candidate < breaks.Count; candidate++)
            {
                BreakOpportunity opportunity = breaks[candidate];
                int candidateEnd = TrimEnd(Text, start, opportunity.Position);
                bool candidateHyphen = !opportunity.Mandatory && Text[opportunity.Position - 1] == SoftHyphen;
                double candidateWidth = Measure(services.Shaper, start, candidateEnd, candidateHyphen);
                bool fits = candidateWidth <= available + 1e-9;
                if (!fits && chosen >= 0)
                {
                    break;
                }

                (chosen, end, width, hyphen) = (candidate, candidateEnd, candidateWidth, candidateHyphen);
                if (opportunity.Mandatory || !fits)
                {
                    break;
                }
            }

            int resume = breaks[chosen].Position;
            bool isLast = breaks[chosen].Mandatory;
            if (width > available + 1e-9)
            {
                // Nothing up to the first opportunity fits: an emergency break inside it, at a grapheme cluster boundary.
                int emergency = EmergencyBreak(services.Shaper, start, end, available, out double emergencyWidth);
                if (emergency < end)
                {
                    (end, resume, width, hyphen, isLast) = (emergency, emergency, emergencyWidth, false, false);
                }
            }

            double baseline = context.CursorY - halfLeading - ascent;
            runs[0] = new LineRun(start, end - start, width);
            services.Bidi.ReorderLine(Text, runs);
            DrawLine(context, runs, context.Left + indent, available, baseline, isLast, hyphen ? end : -1);
            context.Advance(lineAdvance);

            next = resume == breaks[chosen].Position ? chosen + 1 : chosen;
            start = resume;
            first = false;
        }

        context.EndBlock();
        context.AddSpace(style.SpaceAfter);
    }

    /// <summary>Draws one line; <paramref name="hyphenAt"/> is the line end when it breaks at a soft hyphen, otherwise -1.</summary>
    private void DrawLine(FlowContext context, ReadOnlySpan<LineRun> runs, double left, double available, double baseline, bool isLast, int hyphenAt)
    {
        ParagraphStyle style = Style;
        double width = 0;
        int spaces = 0;
        foreach (LineRun run in runs)
        {
            width += run.Width;
            spaces += Text.AsSpan(run.Start, run.Length).Count(' ');
        }

        if (width == 0 && spaces == 0)
        {
            return;
        }

        double x = style.Alignment switch
        {
            TextAlignment.Right => left + available - width,
            TextAlignment.Center => left + ((available - width) / 2),
            _ => left,
        };
        double extra = style.Alignment == TextAlignment.Justify && !isLast && spaces > 0 ? Math.Max(0, available - width) / spaces : 0;
        bool wordSpacing = style.Font.AppliesWordSpacing;
        if (wordSpacing)
        {
            // Content streams carry four decimals: round Tw down so a justified line never passes the right edge.
            extra = Math.Floor(extra * 10000) / 10000;
        }

        PdfCanvas canvas = context.Canvas;
        canvas.BeginText();
        canvas.SetFont(style.Font, style.FontSize);
        if (wordSpacing && extra != context.WordSpacing)
        {
            canvas.SetWordSpacing(extra);
            context.WordSpacing = extra;
        }

        canvas.SetTextMatrix(new Matrix(1, 0, 0, 1, x, baseline));
        foreach (LineRun run in runs)
        {
            int runEnd = run.Start + run.Length;
            ShapedRun shaped = context.Services.Shaper.Shape(Shown(run.Start, runEnd, runEnd == hyphenAt), style);
            double[]? adjustments = shaped.Adjustments;
            if (!wordSpacing && extra > 0)
            {
                adjustments ??= new double[shaped.Glyphs.Glyphs.Length];
                AddSpaceAdjustments(shaped.Glyphs, adjustments, -extra * 1000 / style.FontSize);
            }

            canvas.ShowText(shaped.Glyphs, adjustments);
        }

        canvas.EndText();
    }

    /// <summary>The end of the line that breaks at <paramref name="position"/>: trailing spaces and hard line breaks are not shown.</summary>
    private static int TrimEnd(string text, int start, int position)
    {
        int end = position;
        while (end > start && (text[end - 1] == ' ' || IsHardBreak(text[end - 1])))
        {
            end--;
        }

        return end;
    }

    /// <summary>The characters of the mandatory breaks of UAX #14 (classes BK, CR, LF and NL).</summary>
    private static bool IsHardBreak(char character) => character is '\n' or '\r' or '\v' or '\f' or '\u0085' or '\u2028' or '\u2029';

    /// <summary>The advance of the text from <paramref name="start"/> to <paramref name="end"/> as shown.</summary>
    private double Measure(ITextShaper shaper, int start, int end, bool hyphen)
    {
        ReadOnlySpan<char> text = Text.AsSpan(start, end - start);
        return !hyphen && !text.Contains(SoftHyphen) ? shaper.Measure(text, Style) : shaper.Measure(Shown(start, end, hyphen), Style);
    }

    /// <summary>
    /// The text from <paramref name="start"/> to <paramref name="end"/> as shown: without soft hyphens, and with a hyphen-minus at
    /// the end when <paramref name="hyphen"/> (the line breaks at a soft hyphen).
    /// </summary>
    private string Shown(int start, int end, bool hyphen)
    {
        ReadOnlySpan<char> text = Text.AsSpan(start, end - start);
        if (!hyphen && !text.Contains(SoftHyphen))
        {
            return text.ToString();
        }

        var builder = new StringBuilder(text.Length + 1);
        foreach (char character in text)
        {
            if (character != SoftHyphen)
            {
                builder.Append(character);
            }
        }

        return (hyphen ? builder.Append('-') : builder).ToString();
    }

    /// <summary>
    /// The end of the most extended grapheme clusters (UAX #29) from <paramref name="start"/> that fit <paramref name="available"/>,
    /// at least one.
    /// </summary>
    private int EmergencyBreak(ITextShaper shaper, int start, int end, double available, out double width)
    {
        int first = start + StringInfo.GetNextTextElementLength(Text.AsSpan(start, end - start));
        int fitting = first;
        double sum = 0;
        for (int position = start; position < end;)
        {
            int following = position + StringInfo.GetNextTextElementLength(Text.AsSpan(position, end - position));
            sum += Measure(shaper, position, following, hyphen: false);
            if (sum > available + 1e-9)
            {
                break;
            }

            fitting = following;
            position = following;
        }

        // Kerning between clusters is not in the sum: measure the line as shown and give back clusters until it fits.
        width = Measure(shaper, start, fitting, hyphen: false);
        while (width > available + 1e-9 && fitting > first)
        {
            int previous = first;
            for (int position = first; position < fitting;)
            {
                previous = position;
                position += StringInfo.GetNextTextElementLength(Text.AsSpan(position, end - position));
            }

            fitting = previous;
            width = Measure(shaper, start, fitting, hyphen: false);
        }

        return fitting;
    }

    /// <summary>Adds <paramref name="adjustment"/> after every glyph that shows a space.</summary>
    private static void AddSpaceAdjustments(ShapedText shaped, double[] adjustments, double adjustment)
    {
        ReadOnlySpan<ShapedGlyph> glyphs = shaped.Glyphs;
        for (int index = 0; index < glyphs.Length; index++)
        {
            if (shaped.Text[glyphs[index].Cluster] == ' ')
            {
                adjustments[index] += adjustment;
            }
        }
    }
}
