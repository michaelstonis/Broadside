using System.Globalization;
using System.Text;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.Text;

namespace Broadside.Viewer;

/// <summary>
/// One page's text as a selection sees it: the glyphs of its hit-test index in reading order, each with its box and its logical
/// text (<c>ActualText</c> applied), and the word, line and paragraph boundaries between them. Positions (carets) are offsets
/// 0 to <see cref="Length"/> between glyphs. Immutable, so safe to share between threads.
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.4.4 (glyph positions), §14.8.2.5 (reading order), §14.9.4 (replacement text). Spec #33 viewer capability 4.
/// Every query a pointer move makes (<see cref="GetOffset(PathPoint)"/>, <see cref="GetGlyphAt"/>, <see cref="GetQuads"/>) allocates nothing.
/// </remarks>
public sealed class SelectableText
{
    private const byte NoBreak = 0;
    private const byte SpaceBreak = 1;
    private const byte LineBreak = 2;
    private const byte ParagraphBreak = 3;

    private const byte SpaceClass = 0;
    private const byte WordClass = 1;
    private const byte PunctuationClass = 2;

    private readonly int[] _order;
    private readonly int[] _positions;
    private readonly Frame[] _frames;
    private readonly byte[] _breaks;
    private readonly byte[] _classes;
    private readonly string _text;
    private readonly int[] _textStarts;
    private readonly int[] _owners;

    private SelectableText(PdfPage page, HitTestIndex index, int[] order, int[] positions, Frame[] frames, string text, int[] textStarts, int[] owners)
    {
        Page = page;
        Index = index;
        _order = order;
        _positions = positions;
        _frames = frames;
        _text = text;
        _textStarts = textStarts;
        _owners = owners;
        _breaks = new byte[order.Length];
        _classes = new byte[order.Length];
        for (int position = 0; position < order.Length; position++)
        {
            _classes[position] = Classify(GetGlyphText(position), position > 0 ? _classes[position - 1] : SpaceClass);
            if (position > 0)
            {
                _breaks[position] = Break(frames[position - 1], frames[position]);
            }
        }
    }

    /// <summary>Gets the page.</summary>
    public PdfPage Page { get; }

    /// <summary>Gets the page's hit-test index, whose glyphs this text orders.</summary>
    public HitTestIndex Index { get; }

    /// <summary>Gets the number of selectable glyphs: the largest caret offset.</summary>
    public int Length => _order.Length;

    /// <summary>Builds a page's selectable text.</summary>
    /// <param name="page">The page.</param>
    /// <param name="index">The page's hit-test index (<see cref="PdfPage.CreateHitTestIndex()"/>).</param>
    /// <param name="readingOrder">The reading order; <see langword="null"/> for <see cref="ContentReadingOrder"/>.</param>
    /// <returns>The selectable text.</returns>
    /// <exception cref="InvalidOperationException">The provider listed a glyph the index does not have, or one twice.</exception>
    /// <remarks>ISO 32000-2 §14.8.2.5, §14.9.4.</remarks>
    public static SelectableText Create(PdfPage page, HitTestIndex index, IReadingOrderProvider? readingOrder = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(index);
        int[] order = (readingOrder ?? ContentReadingOrder.Instance).GetReadingOrder(page, index)
            ?? throw new InvalidOperationException("The reading-order provider returned no order.");
        int[] positions = new int[index.GlyphCount];
        positions.AsSpan().Fill(-1);
        var frames = new Frame[order.Length];
        for (int position = 0; position < order.Length; position++)
        {
            int glyph = order[position];
            if ((uint)glyph >= (uint)positions.Length || positions[glyph] >= 0)
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The reading-order provider listed glyph {glyph}, which the index (0 to {positions.Length - 1}) does not have or which it listed before."));
            }

            positions[glyph] = position;
            frames[position] = Frame.Of(index.GetGlyph(glyph).Quad);
        }

        // Logical text per glyph: the extraction applies the ActualText of marked content and structure elements (§14.9.4), the
        // first glyph of a replaced sequence carrying the replacement; the index's own per-code text stands for glyphs it lacks.
        TextPage extracted = page.ExtractText(new TextExtractionOptions().WithArtifacts());
        int[] matches = GlyphMatching.Match(index, extracted);
        var replacements = new int[index.GlyphCount];
        replacements.AsSpan().Fill(-1);
        int[] ownerGlyph = new int[index.GlyphCount];
        ownerGlyph.AsSpan().Fill(-1);
        int carrier = -1;
        bool previousReplaced = false;
        for (int i = 0; i < matches.Length; i++)
        {
            TextGlyph glyph = extracted.Glyphs[i];
            bool replaced = (glyph.Flags & TextGlyphFlags.ActualText) != 0;
            bool continuation = replaced && previousReplaced && glyph.TextLength == 0;
            previousReplaced = replaced;
            if (replaced && !continuation)
            {
                carrier = matches[i];
            }

            if (matches[i] < 0)
            {
                continue;
            }

            replacements[matches[i]] = i;
            if (continuation && carrier >= 0)
            {
                ownerGlyph[matches[i]] = carrier;
            }
        }

        var text = new StringBuilder(index.Text.Length);
        int[] textStarts = new int[order.Length + 1];
        int[] owners = new int[order.Length];
        for (int position = 0; position < order.Length; position++)
        {
            int glyph = order[position];
            textStarts[position] = text.Length;
            if (replacements[glyph] is int extractedGlyph and >= 0)
            {
                text.Append(extracted.GetGlyphText(extracted.Glyphs[extractedGlyph]));
            }
            else
            {
                HitTestGlyph hit = index.GetGlyph(glyph);
                text.Append(index.GetGlyphText(hit));
            }

            owners[position] = ownerGlyph[glyph] >= 0 ? positions[ownerGlyph[glyph]] : -1;
        }

        textStarts[order.Length] = text.Length;
        return new SelectableText(page, index, order, positions, frames, text.ToString(), textStarts, owners);
    }

    /// <summary>Gets the hit-test glyph at a reading position.</summary>
    /// <param name="position">0 to <see cref="Length"/> − 1.</param>
    /// <returns>The glyph's index in <see cref="Index"/>.</returns>
    public int GetGlyphIndex(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(position, _order.Length);
        return _order[position];
    }

    /// <summary>Gets the reading position of a hit-test glyph.</summary>
    /// <param name="glyphIndex">A glyph index of <see cref="Index"/>.</param>
    /// <returns>Its position, or -1 when the reading order leaves it out.</returns>
    public int GetPosition(int glyphIndex) => (uint)glyphIndex < (uint)_positions.Length ? _positions[glyphIndex] : -1;

    /// <summary>Gets the logical text of the glyph at a reading position (empty for a glyph whose sequence's replacement text another glyph carries).</summary>
    /// <param name="position">0 to <see cref="Length"/> − 1.</param>
    /// <returns>The text.</returns>
    /// <remarks>ISO 32000-2 §9.10.2, §14.9.4.</remarks>
    public ReadOnlySpan<char> GetGlyphText(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(position, _order.Length);
        return _text.AsSpan(_textStarts[position], _textStarts[position + 1] - _textStarts[position]);
    }

    /// <summary>Gets the selectable glyph under a point, in default user space.</summary>
    /// <param name="point">The point (§8.3.2.3).</param>
    /// <returns>The glyph's reading position, or -1 when no selectable glyph covers the point (a shell shows an I-beam over one).</returns>
    public int GetGlyphAt(PathPoint point)
    {
        foreach (HitTestItem item in Index.Find(point, HitTestKinds.Glyphs))
        {
            int position = _positions[item.Index];
            if (position >= 0)
            {
                return position;
            }
        }

        return -1;
    }

    /// <summary>Gets the link annotation under a point, for hover feedback and activation.</summary>
    /// <param name="point">The point in default user space.</param>
    /// <returns>The topmost visible link there, or <see langword="null"/>.</returns>
    /// <remarks>ISO 32000-2 §12.5.6.5.</remarks>
    public HitTestLink? FindLink(PathPoint point)
    {
        HitTestLink? found = null;
        foreach (HitTestItem item in Index.Find(point, HitTestKinds.Links))
        {
            found = Index.Links[item.Index];
        }

        return found;
    }

    /// <summary>
    /// Gets the caret offset nearest a point: on a glyph, before or after it by which half the point falls in; elsewhere, by the
    /// nearest glyph, preferring the line level with the point (so a point past a line's end selects to its end).
    /// </summary>
    /// <param name="point">The point in default user space.</param>
    /// <returns>An offset from 0 to <see cref="Length"/>; 0 when the page has no selectable glyph.</returns>
    public int GetOffset(PathPoint point) => GetOffset(point, 0, _order.Length);

    /// <summary>Gets the reading position of the glyph nearest a point (on it, or the nearest by <see cref="GetOffset(PathPoint)"/>'s rule).</summary>
    /// <param name="point">The point in default user space.</param>
    /// <returns>The position, or -1 when the page has no selectable glyph.</returns>
    public int GetNearestGlyph(PathPoint point)
    {
        int position = GetGlyphAt(point);
        return position >= 0 ? position : Nearest(point, 0, _order.Length, out _);
    }

    /// <summary>Gets the word around a glyph: a run of letters and digits, of punctuation, or of white space, on one line.</summary>
    /// <param name="position">A reading position, 0 to <see cref="Length"/> − 1.</param>
    /// <returns>The word's first position and the position after its last.</returns>
    /// <remarks>A double-click selects it.</remarks>
    public (int Start, int End) GetWordRange(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(position, _order.Length);
        byte kind = _classes[position];
        int start = position;
        while (start > 0 && _breaks[start] == NoBreak && _classes[start - 1] == kind && kind != PunctuationClass)
        {
            start--;
        }

        int end = position + 1;
        while (end < _order.Length && _breaks[end] == NoBreak && _classes[end] == kind && kind != PunctuationClass)
        {
            end++;
        }

        return (start, end);
    }

    /// <summary>Gets the line around a glyph: glyphs in reading order on one baseline.</summary>
    /// <param name="position">A reading position, 0 to <see cref="Length"/> − 1.</param>
    /// <returns>The line's first position and the position after its last.</returns>
    /// <remarks>A triple-click selects it.</remarks>
    public (int Start, int End) GetLineRange(int position) => Range(position, LineBreak);

    /// <summary>Gets the paragraph around a glyph: lines up to a gap wider than a line, or a change of direction.</summary>
    /// <param name="position">A reading position, 0 to <see cref="Length"/> − 1.</param>
    /// <returns>The paragraph's first position and the position after its last.</returns>
    public (int Start, int End) GetParagraphRange(int position) => Range(position, ParagraphBreak);

    /// <summary>
    /// Gets the quadrilaterals that cover the glyphs from <paramref name="start"/> to <paramref name="end"/>: one per run of
    /// glyphs on a line, turned with the text (a rotated line gives a rotated quad), in default user space.
    /// </summary>
    /// <param name="start">The first position (clamped to 0 to <see cref="Length"/>).</param>
    /// <param name="end">The position after the last (clamped likewise).</param>
    /// <param name="destination">Receives as many quads as fit.</param>
    /// <returns>The number of quads the range needs, which may exceed what was written.</returns>
    /// <remarks>ISO 32000-2 §9.4.4. Allocates nothing.</remarks>
    public int GetQuads(int start, int end, Span<Quad> destination)
    {
        start = Math.Clamp(start, 0, _order.Length);
        end = Math.Clamp(end, start, _order.Length);
        int count = 0;
        int runStart = start;
        for (int position = start + 1; position <= end; position++)
        {
            if (position == end || _breaks[position] >= LineBreak)
            {
                if (count < destination.Length)
                {
                    destination[count] = RunQuad(runStart, position);
                }

                count++;
                runStart = position;
            }
        }

        return count;
    }

    /// <summary>Gets the text from <paramref name="start"/> to <paramref name="end"/> in reading order, for copying.</summary>
    /// <param name="start">The first position (clamped to 0 to <see cref="Length"/>).</param>
    /// <param name="end">The position after the last (clamped likewise).</param>
    /// <returns>The logical text: a space between words set apart by a gap, a line feed between lines.</returns>
    /// <remarks>ISO 32000-2 §14.8.2.5, §14.9.4.</remarks>
    public string GetText(int start, int end)
    {
        var builder = new StringBuilder();
        AppendText(builder, start, end);
        return builder.ToString();
    }

    internal void AppendText(StringBuilder builder, int start, int end)
    {
        start = Math.Clamp(start, 0, _order.Length);
        end = Math.Clamp(end, start, _order.Length);
        int emittedOwner = -1;
        for (int position = start; position < end; position++)
        {
            ReadOnlySpan<char> text = GetGlyphText(position);
            int owner = _owners[position];
            if (text.IsEmpty && owner >= 0 && (owner < start || owner >= end) && owner != emittedOwner)
            {
                // A replaced sequence selected without the glyph that carries its replacement still copies the replacement.
                text = GetGlyphText(owner);
                emittedOwner = owner;
            }

            if (position > start && !text.IsEmpty)
            {
                switch (_breaks[position])
                {
                    case SpaceBreak when builder.Length > 0 && !char.IsWhiteSpace(builder[^1]) && !char.IsWhiteSpace(text[0]):
                        builder.Append(' ');
                        break;
                    case LineBreak or ParagraphBreak when builder.Length > 0 && builder[^1] != '\n':
                        builder.Append('\n');
                        break;
                }
            }

            builder.Append(text);
        }
    }

    /// <summary>Whether a line or paragraph break comes before a position.</summary>
    internal bool StartsLine(int position) => position > 0 && position < _order.Length && _breaks[position] >= LineBreak;

    /// <summary>Whether the glyph at a position is white space.</summary>
    internal bool IsSpace(int position) => _classes[position] == SpaceClass;

    /// <summary>The caret point of an offset: the start of the glyph after it, or the end of the last glyph.</summary>
    internal PathPoint GetCaretPoint(int offset)
    {
        if (_order.Length == 0)
        {
            return default;
        }

        if (offset < _order.Length)
        {
            Frame frame = _frames[Math.Max(offset, 0)];
            return frame.At(0, frame.Height / 2);
        }

        Frame last = _frames[^1];
        return last.At(last.Width, last.Height / 2);
    }

    /// <summary>The caret offset nearest a point among the glyphs from <paramref name="start"/> to <paramref name="end"/>.</summary>
    internal int GetOffset(PathPoint point, int start, int end)
    {
        if (end <= start)
        {
            return start;
        }

        foreach (HitTestItem item in Index.Find(point, HitTestKinds.Glyphs))
        {
            int position = _positions[item.Index];
            if (position >= start && position < end)
            {
                Frame frame = _frames[position];
                return frame.Along(point) > frame.Width / 2 ? position + 1 : position;
            }
        }

        int nearest = Nearest(point, start, end, out double along);
        return along > _frames[nearest].Width / 2 ? nearest + 1 : nearest;
    }

    private int Nearest(PathPoint point, int start, int end, out double along)
    {
        int best = -1;
        double bestCost = double.PositiveInfinity;
        along = 0;
        for (int position = start; position < end; position++)
        {
            Frame frame = _frames[position];
            double s = frame.Along(point);
            double t = frame.Across(point);
            double outsideAlong = s < 0 ? -s : s > frame.Width ? s - frame.Width : 0;
            double outsideAcross = t < 0 ? -t : t > frame.Height ? t - frame.Height : 0;

            // Being off the line counts far more than being off along it, so the line level with the point wins.
            double cost = (outsideAcross * 16) + outsideAlong;
            if (cost < bestCost)
            {
                bestCost = cost;
                best = position;
                along = s;
            }
        }

        return best;
    }

    private (int Start, int End) Range(int position, byte level)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(position, _order.Length);
        int start = position;
        while (start > 0 && _breaks[start] < level)
        {
            start--;
        }

        int end = position + 1;
        while (end < _order.Length && _breaks[end] < level)
        {
            end++;
        }

        return (start, end);
    }

    private Quad RunQuad(int start, int end)
    {
        Frame first = _frames[start];
        double minAlong = 0, maxAlong = first.Width, minAcross = 0, maxAcross = first.Height;
        for (int position = start + 1; position < end; position++)
        {
            Frame frame = _frames[position];
            Extend(first, frame.At(0, 0), ref minAlong, ref maxAlong, ref minAcross, ref maxAcross);
            Extend(first, frame.At(frame.Width, 0), ref minAlong, ref maxAlong, ref minAcross, ref maxAcross);
            Extend(first, frame.At(frame.Width, frame.Height), ref minAlong, ref maxAlong, ref minAcross, ref maxAcross);
            Extend(first, frame.At(0, frame.Height), ref minAlong, ref maxAlong, ref minAcross, ref maxAcross);
        }

        return new Quad(
            first.At(minAlong, minAcross),
            first.At(maxAlong, minAcross),
            first.At(maxAlong, maxAcross),
            first.At(minAlong, maxAcross));
    }

    private static void Extend(Frame frame, PathPoint corner, ref double minAlong, ref double maxAlong, ref double minAcross, ref double maxAcross)
    {
        double s = frame.Along(corner);
        double t = frame.Across(corner);
        minAlong = Math.Min(minAlong, s);
        maxAlong = Math.Max(maxAlong, s);
        minAcross = Math.Min(minAcross, t);
        maxAcross = Math.Max(maxAcross, t);
    }

    private static byte Break(Frame previous, Frame next)
    {
        double height = Math.Max(Math.Max(previous.Height, next.Height), 1e-6);
        bool parallel = (previous.UX * next.UX) + (previous.UY * next.UY) > 0.95;
        double across = previous.Across(next.Origin);
        if (!parallel)
        {
            return ParagraphBreak;
        }

        if (Math.Abs(across) > height / 2)
        {
            // A baseline further away than one and a half lines starts a paragraph; so does one going back up the page.
            return across > 0 || -across > height * 1.6 ? ParagraphBreak : LineBreak;
        }

        double gap = previous.Along(next.Origin) - previous.Width;
        return gap > height * 0.2 || gap < -height * 2 ? SpaceBreak : NoBreak;
    }

    private static byte Classify(ReadOnlySpan<char> text, byte previous)
    {
        if (text.IsEmpty)
        {
            return previous;
        }

        char c = text[0];
        if (char.IsWhiteSpace(c))
        {
            return SpaceClass;
        }

        return char.GetUnicodeCategory(c) switch
        {
            UnicodeCategory.ConnectorPunctuation or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark => WordClass,
            _ when char.IsLetterOrDigit(c) || char.IsSurrogate(c) || c is '\'' or '’' => WordClass,
            _ => PunctuationClass,
        };
    }

    /// <summary>A glyph box as an origin (bottom-left), unit vectors along and across the baseline, and its width and height.</summary>
    private readonly record struct Frame(PathPoint Origin, double UX, double UY, double VX, double VY, double Width, double Height)
    {
        public static Frame Of(Quad quad)
        {
            PathPoint o = quad.BottomLeft;
            double ux = quad.BottomRight.X - o.X, uy = quad.BottomRight.Y - o.Y;
            double vx = quad.TopLeft.X - o.X, vy = quad.TopLeft.Y - o.Y;
            double width = Math.Sqrt((ux * ux) + (uy * uy));
            double height = Math.Sqrt((vx * vx) + (vy * vy));
            if (!double.IsFinite(width) || !double.IsFinite(height))
            {
                return new Frame(o, 1, 0, 0, 1, 0, 0);
            }

            if (height > 0)
            {
                (vx, vy) = (vx / height, vy / height);
            }

            if (width > 0)
            {
                (ux, uy) = (ux / width, uy / width);
            }
            else if (height > 0)
            {
                // A zero-width glyph: the baseline runs a quarter turn clockwise from the across vector.
                (ux, uy) = (vy, -vx);
            }
            else
            {
                (ux, uy, vx, vy) = (1, 0, 0, 1);
            }

            if (height == 0)
            {
                (vx, vy) = (-uy, ux);
            }

            return new Frame(o, ux, uy, vx, vy, width, height);
        }

        public double Along(PathPoint p) => ((p.X - Origin.X) * UX) + ((p.Y - Origin.Y) * UY);

        public double Across(PathPoint p) => ((p.X - Origin.X) * VX) + ((p.Y - Origin.Y) * VY);

        public PathPoint At(double along, double across) =>
            new(Origin.X + (along * UX) + (across * VX), Origin.Y + (along * UY) + (across * VY));
    }
}
