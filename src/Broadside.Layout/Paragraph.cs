using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Broadside.Annotations;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Structure;

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
/// <para>
/// A paragraph of inline content (<see cref="TextRun"/>s and <see cref="InlineImage"/>s) breaks its lines over the text of all its
/// runs together, an image counting as U+FFFC OBJECT REPLACEMENT CHARACTER. Each run is shown in its own font, size and colour
/// (<c>Tf</c> and the colour operators change inside the line's text object, §9.3 and §8.6.8) on the line's common baseline: the
/// line is as tall as its tallest content, each run's glyph box centred in its own line height and an image standing on the baseline,
/// and never shorter than a line of the paragraph's style. Images end the text object and are painted with <c>Do</c> (§8.9.5).
/// Underlines and strike-throughs are filled rectangles after the text; links are link annotations (§12.5.6.5) over each run's
/// portion of a line.
/// </para>
/// </remarks>
public sealed class Paragraph : FlowBlock
{
    private const char SoftHyphen = '\u00AD';
    private const char ObjectReplacement = '\uFFFC';

    /// <summary>The centre of an underline below the baseline, in ems (the AFM <c>UnderlinePosition</c> of the standard fonts).</summary>
    private const double UnderlinePosition = -0.1;

    /// <summary>The thickness of underlines and strike-throughs, in ems (the AFM <c>UnderlineThickness</c> of the standard fonts).</summary>
    private const double UnderlineThickness = 0.05;

    /// <summary>The pieces of the content: runs and images, empty runs left out.</summary>
    private readonly InlineElement[] _pieces;

    /// <summary>The text style of each piece (null for images).</summary>
    private readonly TextStyle?[] _styles;

    /// <summary>Where each piece starts in <see cref="Text"/>, with the text's length at the end.</summary>
    private readonly int[] _starts;

    /// <summary>True for a paragraph made from a string: its one run is not an inline structure part.</summary>
    private readonly bool _plain;

    /// <summary>Gets whether the paragraph was made from a string: one run, no inline parts, so it is tagged as one leaf.</summary>
    internal bool IsPlain => _plain;

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
        _plain = true;
        TextRun run = new(text);
        Content = [run];
        _pieces = text.Length == 0 ? [] : [run];
        _styles = text.Length == 0 ? [] : [style.TextStyle];
        _starts = text.Length == 0 ? [0] : [0, text.Length];
    }

    /// <summary>Creates a paragraph of inline content with structure type <c>P</c>: runs of text in their own styles, and images.</summary>
    /// <param name="content">The runs and images, in order. Runs without a style use <paramref name="style"/>'s text style.</param>
    /// <param name="style">The paragraph's style: line height, alignment, indent, spacing, and the text style of unstyled runs.</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/>, one of its elements, or <paramref name="style"/> is null.</exception>
    public Paragraph(IEnumerable<InlineElement> content, ParagraphStyle style)
        : base("P")
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(style);
        Style = style;
        InlineElement[] elements = [.. content];
        List<InlineElement> pieces = [];
        List<TextStyle?> styles = [];
        List<int> starts = [];
        var text = new StringBuilder();
        foreach (InlineElement element in elements)
        {
            ArgumentNullException.ThrowIfNull(element, nameof(content));
            if (element is TextRun run)
            {
                if (run.Text.Length == 0)
                {
                    continue;
                }

                starts.Add(text.Length);
                pieces.Add(run);
                styles.Add(run.Style ?? style.TextStyle);
                text.Append(run.Text);
            }
            else
            {
                starts.Add(text.Length);
                pieces.Add(element);
                styles.Add(null);
                text.Append(ObjectReplacement);
            }
        }

        starts.Add(text.Length);
        Content = elements;
        Text = text.ToString();
        _pieces = [.. pieces];
        _styles = [.. styles];
        _starts = [.. starts];
    }

    /// <summary>The runs and images; for a paragraph made from a string, one run of its text.</summary>
    public IReadOnlyList<InlineElement> Content { get; }

    /// <summary>The text; for inline content, the text of every run with U+FFFC in place of each image.</summary>
    public string Text { get; }

    /// <summary>The style.</summary>
    public ParagraphStyle Style { get; }

    internal override void Validate(int index)
    {
        Span<char> buffer = stackalloc char[2];
        for (int piece = 0; piece < _pieces.Length; piece++)
        {
            if (_styles[piece] is not { } style)
            {
                continue;
            }

            ReadOnlySpan<char> text = Text.AsSpan(_starts[piece], _starts[piece + 1] - _starts[piece]);
            foreach (Rune rune in text.EnumerateRunes())
            {
                if (rune.IsBmp && ((char)rune.Value == SoftHyphen || IsHardBreak((char)rune.Value)))
                {
                    continue;
                }

                int length = rune.EncodeToUtf16(buffer);
                if (!style.Font.CanEncode(buffer[..length]))
                {
                    throw new ArgumentException(
                        $"The text of paragraph {index} has U+{rune.Value:X4}, which the font {style.Font.Name} cannot encode.");
                }
            }

            if (text.Contains(SoftHyphen) && !style.Font.CanEncode("-"))
            {
                throw new ArgumentException(
                    $"The text of paragraph {index} has soft hyphens, and the font {style.Font.Name} cannot encode the hyphen shown at a break.");
            }
        }
    }

    internal override ContentWidths MeasureWidths(FlowServices services)
    {
        var breaks = new List<BreakOpportunity>();
        services.LineBreaker.FindBreaks(Text, breaks);
        double min = 0;
        double max = 0;
        int start = 0;
        int lineStart = 0;
        foreach (BreakOpportunity opportunity in breaks)
        {
            // The pieces between break opportunities are unbreakable; the lines between mandatory breaks are the unwrapped widths.
            double indent = start == 0 ? Style.FirstLineIndent : 0;
            bool hyphen = !opportunity.Mandatory && Text[opportunity.Position - 1] == SoftHyphen;
            min = Math.Max(min, indent + Measure(services.Shaper, start, TrimEnd(Text, start, opportunity.Position), hyphen));
            if (opportunity.Mandatory)
            {
                double lineIndent = lineStart == 0 ? Style.FirstLineIndent : 0;
                max = Math.Max(max, lineIndent + Measure(services.Shaper, lineStart, TrimEnd(Text, lineStart, opportunity.Position), false));
                lineStart = opportunity.Position;
            }

            start = opportunity.Position;
        }

        return new(min, Math.Max(min, max));
    }

    internal override void LayOut(FlowContext context)
    {
        FlowServices services = context.Services;
        ParagraphStyle style = Style;
        var breaks = new List<BreakOpportunity>();
        services.LineBreaker.FindBreaks(Text, breaks);

        context.AddSpace(style.SpaceBefore);
        double strutAdvance = style.LineAdvance;
        double strutAscent = style.Font.Ascent * style.FontSize / 1000;
        double strutDescent = style.Font.Descent * style.FontSize / 1000;
        double strutLeading = (strutAdvance - (strutAscent - strutDescent)) / 2;
        var runs = new List<LineRun>();
        bool begun = false;

        int start = 0;
        int next = 0;
        bool first = true;
        while (start < Text.Length && next < breaks.Count)
        {
            // Room first: the line is measured against the width of the page it lands on.
            context.EnsureRoom(strutAdvance);
            int page = context.PageCount;
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

            Segment(services.Shaper, start, end, width, hyphen, runs);
            (double above, double below) = LineBox(runs);
            double lineAdvance = strutAdvance;
            double baseline = context.CursorY - strutLeading - strutAscent;
            if (above > strutLeading + strutAscent || below > strutAdvance - (strutLeading + strutAscent))
            {
                // Content taller than the paragraph's own line: the line grows, and may no longer fit on this page.
                above = Math.Max(above, strutLeading + strutAscent);
                lineAdvance = above + Math.Max(below, strutAdvance - (strutLeading + strutAscent));
                context.EnsureRoom(lineAdvance);
                if (context.PageCount != page)
                {
                    continue;
                }

                baseline = context.CursorY - above;
            }

            services.Bidi.ReorderLine(Text, CollectionsMarshal.AsSpan(runs));
            context.PlaceMarkers(baseline);
            if (!begun)
            {
                context.BeginBlock(this);
                begun = true;
            }

            if (context.Drawing)
            {
                DrawLine(context, CollectionsMarshal.AsSpan(runs), context.Left + indent, available, baseline, isLast, hyphen ? end : -1);
            }

            context.Advance(lineAdvance);

            next = resume == breaks[chosen].Position ? chosen + 1 : chosen;
            start = resume;
            first = false;
        }

        if (!begun)
        {
            context.BeginBlock(this);
        }

        context.EndBlock();
        context.AddSpace(style.SpaceAfter);
    }

    /// <summary>Splits the line from <paramref name="start"/> to <paramref name="end"/> into one run per piece, in logical order.</summary>
    private void Segment(ITextShaper shaper, int start, int end, double width, bool hyphen, List<LineRun> runs)
    {
        runs.Clear();
        int firstPiece = PieceAt(start);
        if (start < end && _starts[firstPiece + 1] >= end)
        {
            runs.Add(new LineRun(start, end - start, width, firstPiece));
            return;
        }

        for (int piece = firstPiece; piece < _pieces.Length && _starts[piece] < end; piece++)
        {
            int from = Math.Max(start, _starts[piece]);
            int to = Math.Min(end, _starts[piece + 1]);
            runs.Add(new LineRun(from, to - from, MeasurePiece(shaper, piece, from, to, hyphen && to == end), piece));
        }
    }

    /// <summary>How far the runs reach above and below the baseline: each text run's glyph box centred in its own line height, images standing on it.</summary>
    private (double Above, double Below) LineBox(List<LineRun> runs)
    {
        double above = 0;
        double below = 0;
        foreach (LineRun run in runs)
        {
            if (_styles[run.Piece] is { } style)
            {
                double advance = Style.LineHeight * style.FontSize;
                double leading = (advance - (style.Ascent - style.Descent)) / 2;
                above = Math.Max(above, leading + style.Ascent);
                below = Math.Max(below, leading - style.Descent);
            }
            else
            {
                above = Math.Max(above, ((InlineImage)_pieces[run.Piece]).Height);
            }
        }

        return (above, below);
    }

    /// <summary>Draws one line; <paramref name="hyphenAt"/> is the line end when it breaks at a soft hyphen, otherwise -1.</summary>
    private void DrawLine(FlowContext context, ReadOnlySpan<LineRun> runs, double left, double available, double baseline, bool isLast, int hyphenAt)
    {
        ParagraphStyle style = Style;
        double width = 0;
        int spaces = 0;
        bool wordSpacing = false;
        foreach (LineRun run in runs)
        {
            width += run.Width;
            if (_styles[run.Piece] is { } runStyle)
            {
                spaces += Text.AsSpan(run.Start, run.Length).Count(' ');
                wordSpacing |= runStyle.Font.AppliesWordSpacing;
            }
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
        if (wordSpacing)
        {
            // Content streams carry four decimals: round Tw down so a justified line never passes the right edge.
            extra = Math.Floor(extra * 10000) / 10000;
        }

        PdfCanvas canvas = context.Canvas;
        bool inText = false;
        bool spacingSet = false;
        bool positioned = false;
        PdfCanvasFont? font = null;
        double fontSize = 0;
        PdfColor? color = null;
        List<(double X, double Width, int Piece)>? decorated = null;
        foreach (LineRun run in runs)
        {
            InlineElement element = _pieces[run.Piece];
            if (_styles[run.Piece] is not { } runStyle)
            {
                if (inText)
                {
                    canvas.EndText();
                    inText = false;
                }

                var image = (InlineImage)element;
                context.BeginPart(new FlowPart(element.StructureType, element, element.Language));
                canvas.DrawImage(image.Image, x, baseline, image.Width, image.Height);
                context.EndPart();
                x += run.Width;
                positioned = false;
                font = null;
                continue;
            }

            if (!inText)
            {
                canvas.BeginText();
                inText = true;
            }

            if (!ReferenceEquals(font, runStyle.Font) || fontSize != runStyle.FontSize)
            {
                canvas.SetFont(runStyle.Font, runStyle.FontSize);
                (font, fontSize) = (runStyle.Font, runStyle.FontSize);
            }

            if (!spacingSet)
            {
                if (wordSpacing && extra != context.WordSpacing)
                {
                    canvas.SetWordSpacing(extra);
                    context.WordSpacing = extra;
                }

                spacingSet = true;
            }

            if (runStyle.Color != color)
            {
                canvas.SetFillColor(runStyle.Color ?? PdfColor.FromGray(0));
                color = runStyle.Color;
            }

            if (!positioned)
            {
                canvas.SetTextMatrix(new Matrix(1, 0, 0, 1, x, baseline));
                positioned = true;
            }

            int runEnd = run.Start + run.Length;
            ShapedRun shaped = context.Services.Shaper.Shape(Shown(run.Start, runEnd, runEnd == hyphenAt), runStyle);
            double[]? adjustments = shaped.Adjustments;
            if (!runStyle.Font.AppliesWordSpacing && extra > 0)
            {
                adjustments ??= new double[shaped.Glyphs.Glyphs.Length];
                AddSpaceAdjustments(shaped.Glyphs, adjustments, -extra * 1000 / runStyle.FontSize);
            }

            if (!_plain)
            {
                context.BeginPart(new FlowPart(element.StructureType, element, element.Language));
            }

            canvas.ShowText(shaped.Glyphs, adjustments);
            if (!_plain)
            {
                context.EndPart();
            }

            double advance = run.Width + (extra * Text.AsSpan(run.Start, run.Length).Count(' '));
            if (runStyle.Underline || runStyle.StrikeThrough || element is TextRun { Link: not null })
            {
                (decorated ??= []).Add((x, advance, run.Piece));
            }

            x += advance;
        }

        if (inText)
        {
            canvas.EndText();
        }

        if (color is not null)
        {
            canvas.SetFillColor(PdfColor.FromGray(0));
        }

        if (decorated is not null)
        {
            Decorate(context, decorated, baseline);
        }
    }

    /// <summary>Draws the underlines and strike-throughs of a line and adds the link annotations over its linked runs.</summary>
    private void Decorate(FlowContext context, List<(double X, double Width, int Piece)> decorated, double baseline)
    {
        PdfCanvas canvas = context.Canvas;
        foreach ((double x, double width, int piece) in decorated)
        {
            TextStyle style = _styles[piece]!;
            double thickness = style.FontSize * UnderlineThickness;
            if (style.Underline || style.StrikeThrough)
            {
                // Decorations repeat what the text says: a layout artifact (ISO 32000-2 §14.8.2.2.2).
                context.BeginPart(new FlowPart("Artifact", PdfArtifactType.Layout, null));
                if (style.Underline)
                {
                    Bar(canvas, style, x, baseline + (style.FontSize * UnderlinePosition) - (thickness / 2), width, thickness);
                }

                if (style.StrikeThrough)
                {
                    double middle = style.Font.XHeight > 0 ? style.Font.XHeight * style.FontSize / 2000 : style.FontSize / 4;
                    Bar(canvas, style, x, baseline + middle - (thickness / 2), width, thickness);
                }

                context.EndPart();
            }

            InlineElement element = _pieces[piece];
            if (((TextRun)element).Link is { } link)
            {
                var bounds = new PdfRectangle(x, baseline + style.Descent, x + width, baseline + style.Ascent);
                PdfLinkAnnotation annotation = context.Page.AddLinkAnnotation(bounds, link);
                context.AddAnnotation(new FlowPart(element.StructureType, element, element.Language), annotation);
            }
        }
    }

    /// <summary>Fills a decoration bar in the run's colour.</summary>
    private static void Bar(PdfCanvas canvas, TextStyle style, double x, double y, double width, double thickness)
    {
        canvas.SaveState();
        if (style.Color is { } color)
        {
            canvas.SetFillColor(color);
        }

        canvas.Rectangle(x, y, width, thickness);
        canvas.Fill();
        canvas.RestoreState();
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

    /// <summary>The advance of the content from <paramref name="start"/> to <paramref name="end"/> as shown, piece by piece.</summary>
    private double Measure(ITextShaper shaper, int start, int end, bool hyphen)
    {
        double sum = 0;
        for (int piece = PieceAt(start); piece < _pieces.Length && _starts[piece] < end; piece++)
        {
            int from = Math.Max(start, _starts[piece]);
            int to = Math.Min(end, _starts[piece + 1]);
            sum += MeasurePiece(shaper, piece, from, to, hyphen && to == end);
        }

        return sum;
    }

    /// <summary>The advance of the part of one piece from <paramref name="start"/> to <paramref name="end"/> as shown.</summary>
    private double MeasurePiece(ITextShaper shaper, int piece, int start, int end, bool hyphen)
    {
        if (_styles[piece] is not { } style)
        {
            return ((InlineImage)_pieces[piece]).Width;
        }

        ReadOnlySpan<char> text = Text.AsSpan(start, end - start);
        return !hyphen && !text.Contains(SoftHyphen) ? shaper.Measure(text, style) : shaper.Measure(Shown(start, end, hyphen), style);
    }

    /// <summary>The piece holding <paramref name="position"/>, or the piece count at the end of the text.</summary>
    private int PieceAt(int position)
    {
        if (_pieces.Length == 0 || position >= Text.Length)
        {
            return _pieces.Length;
        }

        int low = 0;
        int high = _pieces.Length - 1;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (_starts[middle] <= position)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
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
