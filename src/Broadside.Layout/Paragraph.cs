using System.Text;
using Broadside.Graphics;

namespace Broadside.Layout.Flow;

/// <summary>
/// A paragraph of text in one style, broken into lines greedily: each line takes as many words as fit the content width, breaking
/// after spaces, and a line feed (U+000A) always ends a line. Lines are aligned or justified and placed one line height apart,
/// continuing on the next page when a line would cross the bottom margin.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.4.3 and §9.4.4: each line is one text object positioned with <c>Tm</c> and shown with <c>Tj</c> (or <c>TJ</c>
/// when kerned). Justification stretches the spaces of every line but a paragraph's last (and lines ended by a line feed): with
/// word spacing <c>Tw</c> when the font applies it to its single-byte space code, otherwise with a <c>TJ</c> adjustment after each
/// space. The glyph box of a line ([descent, ascent] of the font) is centred vertically in the line height.
/// </para>
/// <para>A word wider than the content width is placed alone on a line and overflows it (emergency breaks come with UAX #14).</para>
/// </remarks>
public sealed class Paragraph : FlowBlock
{
    /// <summary>Creates a paragraph with structure type <c>P</c>.</summary>
    /// <param name="text">The text. Line feeds force line breaks; carriage returns are ignored at line ends.</param>
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
            if (rune.Value is '\n' or '\r')
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
            for (int candidate = next; candidate < breaks.Count; candidate++)
            {
                int candidateEnd = TrimEnd(Text, start, breaks[candidate].Position);
                double candidateWidth = services.Shaper.Measure(Text.AsSpan(start, candidateEnd - start), style);
                bool fits = candidateWidth <= available + 1e-9;
                if (!fits && chosen >= 0)
                {
                    break;
                }

                (chosen, end, width) = (candidate, candidateEnd, candidateWidth);
                if (breaks[candidate].Mandatory || !fits)
                {
                    break;
                }
            }

            double baseline = context.CursorY - halfLeading - ascent;
            runs[0] = new LineRun(start, end - start, width);
            services.Bidi.ReorderLine(Text, runs);
            DrawLine(context, runs, context.Left + indent, available, baseline, isLast: breaks[chosen].Mandatory);
            context.Advance(lineAdvance);

            start = breaks[chosen].Position;
            next = chosen + 1;
            first = false;
        }

        context.EndBlock();
        context.AddSpace(style.SpaceAfter);
    }

    private void DrawLine(FlowContext context, ReadOnlySpan<LineRun> runs, double left, double available, double baseline, bool isLast)
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
            ShapedRun shaped = context.Services.Shaper.Shape(Text.Substring(run.Start, run.Length), style);
            double[]? adjustments = shaped.Adjustments;
            if (!wordSpacing && extra > 0)
            {
                adjustments ??= new double[CountScalars(shaped.Text)];
                AddSpaceAdjustments(shaped.Text, adjustments, -extra * 1000 / style.FontSize);
            }

            if (adjustments is null)
            {
                canvas.ShowText(shaped.Text);
            }
            else
            {
                canvas.ShowText(shaped.Text, adjustments);
            }
        }

        canvas.EndText();
    }

    /// <summary>The end of the line that breaks at <paramref name="position"/>: trailing spaces and line ends are not shown.</summary>
    private static int TrimEnd(string text, int start, int position)
    {
        int end = position;
        while (end > start && text[end - 1] is ' ' or '\n' or '\r')
        {
            end--;
        }

        return end;
    }

    private static int CountScalars(string text)
    {
        int count = 0;
        foreach (Rune _ in text.EnumerateRunes())
        {
            count++;
        }

        return count;
    }

    private static void AddSpaceAdjustments(string text, double[] adjustments, double adjustment)
    {
        int index = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (rune.Value == ' ')
            {
                adjustments[index] += adjustment;
            }

            index++;
        }
    }
}
