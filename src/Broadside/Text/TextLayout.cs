using Broadside.Graphics;

namespace Broadside.Text;

/// <summary>
/// Assembles a sequence of extracted glyphs into words and lines and writes the plain text: words separated by one space, lines by
/// one line feed. Works on any glyph sequence, content order or another order, reusing its buffers; not thread-safe.
/// </summary>
/// <remarks>
/// ISO 32000-2 §14.8.2.6.2 (word breaks), §14.9.4 (no word break between consecutive <c>ActualText</c> sequences), §9.4.4 (glyph
/// positions) and §9.7.4.3 (vertical writing). Each break is decided between two neighbouring glyphs from their text rendering
/// matrices, origins and advances; see <see cref="TextWord"/> and <see cref="TextLine"/> for the rules.
/// </remarks>
internal sealed class TextLayout
{
    /// <summary>A gap along the baseline wider than this many ems breaks the word (poppler and pdf.js use about 0.1).</summary>
    internal const double WordGap = 0.1;

    /// <summary>An offset across the baseline larger than this many ems starts a new line.</summary>
    internal const double LineOffset = 0.5;

    /// <summary>A move back along the baseline larger than this many ems starts a new line.</summary>
    internal const double LineBacktrack = 1.0;

    /// <summary>The cosine of the largest angle between two baselines of one line (about 5 degrees).</summary>
    internal const double SameDirection = 0.996;

    private const double Degenerate = 1e-9;

    /// <summary>The most words a layout <see cref="Return"/>ed to its thread may hold room for; a larger one is dropped.</summary>
    private const int SharedWordLimit = 1 << 14;

    /// <summary>
    /// The layout <see cref="TextPage"/> assembles with on this thread (issue #284), so pages assembled one after another grow its
    /// buffers once; <see langword="null"/> while it is rented.
    /// </summary>
    [ThreadStatic]
    private static TextLayout? _shared;

    /// <summary>The flags of glyphs whose text an element's alternate description or expansion replaces, a whole word each (§14.9.3, §14.9.5).</summary>
    private const TextGlyphFlags Substituted = TextGlyphFlags.AlternateDescription | TextGlyphFlags.Expansion;

    private TextWord[] _words = new TextWord[16];
    private TextLine[] _lines = new TextLine[4];
    private char[] _plain = new char[64];
    private int _wordCount;
    private int _lineCount;
    private int _plainLength;

    /// <summary>What separates a glyph from the one before it.</summary>
    internal enum Break
    {
        None,
        Word,
        Line,
    }

    private enum Kind
    {
        Text,
        Empty,
        WhiteSpace,
    }

    public ReadOnlySpan<TextWord> Words => _words.AsSpan(0, _wordCount);

    public ReadOnlySpan<TextLine> Lines => _lines.AsSpan(0, _lineCount);

    public ReadOnlySpan<char> PlainText => _plain.AsSpan(0, _plainLength);

    /// <summary>Takes this thread's shared layout, or a new one when it is in use or was dropped; give it back with <see cref="Return"/>.</summary>
    internal static TextLayout Rent()
    {
        TextLayout layout = _shared ?? new TextLayout();
        _shared = null;
        return layout;
    }

    /// <summary>Keeps <paramref name="layout"/> as this thread's shared layout unless its buffers grew past <see cref="SharedWordLimit"/>; it holds no references.</summary>
    internal static void Return(TextLayout layout)
    {
        if (layout._words.Length <= SharedWordLimit && layout._plain.Length <= SharedWordLimit * 16)
        {
            _shared = layout;
        }
    }

    /// <summary>Assembles <paramref name="glyphs"/>, whose text ranges index <paramref name="text"/>.</summary>
    public void Build(ReadOnlySpan<TextGlyph> glyphs, ReadOnlySpan<char> text)
    {
        _wordCount = 0;
        _lineCount = 0;
        _plainLength = 0;
        int wordStart = -1;
        int wordEnd = -1;
        int lineWordStart = -1;
        int anchor = -1;
        Break pending = Break.Line;
        for (int index = 0; index < glyphs.Length; index++)
        {
            ref readonly TextGlyph glyph = ref glyphs[index];
            if (index > 0)
            {
                Break between = Separate(glyphs[index - 1], glyph, glyphs[anchor < 0 ? index - 1 : anchor]);
                if (between > pending)
                {
                    pending = between;
                }
            }

            Kind kind = Classify(glyph, text);
            if (kind == Kind.WhiteSpace)
            {
                if (pending < Break.Word)
                {
                    pending = Break.Word;
                }

                continue;
            }

            if (wordStart >= 0 && pending == Break.None)
            {
                wordEnd = index;
                continue;
            }

            if (kind == Kind.Empty)
            {
                // A glyph standing for no text extends the word it touches, and starts none.
                continue;
            }

            if (wordStart >= 0)
            {
                AddWord(glyphs, text, wordStart, wordEnd, lineWordStart);
            }

            if (pending == Break.Line && lineWordStart >= 0)
            {
                AddLine(glyphs, lineWordStart);
                lineWordStart = -1;
            }

            if (lineWordStart < 0)
            {
                lineWordStart = _wordCount;
                anchor = index;
            }

            wordStart = index;
            wordEnd = index;
            pending = Break.None;
        }

        if (wordStart >= 0)
        {
            AddWord(glyphs, text, wordStart, wordEnd, lineWordStart);
            AddLine(glyphs, lineWordStart);
        }
    }

    /// <summary>
    /// Decides what separates <paramref name="next"/> from <paramref name="previous"/>, the glyph before it in the sequence, on a line
    /// whose baseline is <paramref name="anchor"/>'s (the glyph that started the line, so a superscript followed by a subscript
    /// stays on it).
    /// </summary>
    internal static Break Separate(in TextGlyph previous, in TextGlyph next, in TextGlyph anchor)
    {
        bool replaced = (previous.Flags & next.Flags & TextGlyphFlags.ActualText) != 0;
        if (replaced && next.TextLength == 0)
        {
            // The rest of one ActualText sequence: the replacement on its first glyph stands for all of them.
            return Break.None;
        }

        TextGlyphFlags substituted = (previous.Flags | next.Flags) & Substituted;
        if (substituted != 0 && next.TextLength == 0 && (previous.Flags & next.Flags & Substituted) != 0)
        {
            // The rest of one element's Alt or E: the substitution on its first glyph stands for all of them.
            return Break.None;
        }

        Break geometric = Measure(previous, next, anchor);
        if (substituted != 0)
        {
            // §14.9.3 and §14.9.5: an alternate description or expansion is a whole word or phrase, set apart from its neighbours.
            return geometric == Break.None ? Break.Word : geometric;
        }

        // §14.9.4: consecutive ActualText sequences are read as if no word break were present between them.
        return replaced && geometric == Break.Word ? Break.None : geometric;
    }

    private static Break Measure(in TextGlyph previous, in TextGlyph next, in TextGlyph anchor)
    {
        if (previous.IsVertical != next.IsVertical || anchor.IsVertical != next.IsVertical)
        {
            return Break.Line;
        }

        bool vertical = previous.IsVertical;
        (PathPoint along, double alongSize, double crossSize) = Axes(previous.TextRenderingMatrix, vertical);
        (PathPoint nextAlong, _, double nextCrossSize) = Axes(next.TextRenderingMatrix, vertical);
        (PathPoint anchorAlong, _, double anchorCrossSize) = Axes(anchor.TextRenderingMatrix, vertical);
        if (alongSize <= Degenerate || crossSize <= Degenerate || nextCrossSize <= Degenerate || anchorCrossSize <= Degenerate)
        {
            // No size to measure against: a glyph shown at size 0 or through a singular matrix.
            return previous.Origin == next.Origin ? Break.None : Break.Line;
        }

        if ((along.X * nextAlong.X) + (along.Y * nextAlong.Y) < SameDirection)
        {
            return Break.Line;
        }

        double dx = next.Origin.X - (previous.Origin.X + previous.Advance.X);
        double dy = next.Origin.Y - (previous.Origin.Y + previous.Advance.Y);
        double gap = (dx * along.X) + (dy * along.Y);

        // How far off the line's baseline the glyph lies, across it.
        double offset = ((next.Origin.Y - anchor.Origin.Y) * anchorAlong.X) - ((next.Origin.X - anchor.Origin.X) * anchorAlong.Y);
        if (Math.Abs(offset) > LineOffset * Math.Max(anchorCrossSize, nextCrossSize) || gap < -LineBacktrack * alongSize)
        {
            return Break.Line;
        }

        return gap > WordGap * alongSize ? Break.Word : Break.None;
    }

    /// <summary>
    /// The unit writing direction in page space (the text space x axis; down the y axis for vertical writing) and the page-space
    /// length of one em along it and across it.
    /// </summary>
    private static (PathPoint Along, double AlongSize, double CrossSize) Axes(Matrix matrix, bool vertical)
    {
        PathPoint x = matrix.TransformVector(1, 0);
        PathPoint y = matrix.TransformVector(0, 1);
        double xLength = Math.Sqrt((x.X * x.X) + (x.Y * x.Y));
        double yLength = Math.Sqrt((y.X * y.X) + (y.Y * y.Y));
        return vertical
            ? (yLength > Degenerate ? new PathPoint(-y.X / yLength, -y.Y / yLength) : default, yLength, xLength)
            : (xLength > Degenerate ? new PathPoint(x.X / xLength, x.Y / xLength) : default, xLength, yLength);
    }

    private static Kind Classify(in TextGlyph glyph, ReadOnlySpan<char> text)
    {
        if (glyph.TextLength == 0)
        {
            return Kind.Empty;
        }

        foreach (char unit in text.Slice(glyph.TextStart, glyph.TextLength))
        {
            if (!char.IsWhiteSpace(unit))
            {
                return Kind.Text;
            }
        }

        return Kind.WhiteSpace;
    }

    private void AddWord(ReadOnlySpan<TextGlyph> glyphs, ReadOnlySpan<char> text, int first, int last, int lineWordStart)
    {
        int textStart = glyphs[first].TextStart;
        int textEnd = glyphs[last].TextStart + glyphs[last].TextLength;
        if (_wordCount == lineWordStart)
        {
            if (_lineCount > 0)
            {
                Append('\n');
            }
        }
        else
        {
            Append(' ');
        }

        Append(text[textStart..textEnd]);
        if (_wordCount == _words.Length)
        {
            Array.Resize(ref _words, _words.Length * 2);
        }

        _words[_wordCount++] = new TextWord
        {
            GlyphStart = first,
            GlyphCount = last - first + 1,
            TextStart = textStart,
            TextLength = textEnd - textStart,
            Quad = Bound(glyphs, first, last),
        };
    }

    private void AddLine(ReadOnlySpan<TextGlyph> glyphs, int firstWord)
    {
        TextWord first = _words[firstWord];
        TextWord last = _words[_wordCount - 1];
        int glyphEnd = last.GlyphStart + last.GlyphCount;
        int plainStart = _lineCount == 0 ? 0 : _lines[_lineCount - 1].PlainTextStart + _lines[_lineCount - 1].PlainTextLength + 1;
        if (_lineCount == _lines.Length)
        {
            Array.Resize(ref _lines, _lines.Length * 2);
        }

        _lines[_lineCount++] = new TextLine
        {
            GlyphStart = first.GlyphStart,
            GlyphCount = glyphEnd - first.GlyphStart,
            WordStart = firstWord,
            WordCount = _wordCount - firstWord,
            PlainTextStart = plainStart,
            PlainTextLength = _plainLength - plainStart,
            Quad = BoundLine(glyphs, firstWord),
            IsVertical = glyphs[first.GlyphStart].IsVertical,
        };
    }

    private Quad BoundLine(ReadOnlySpan<TextGlyph> glyphs, int firstWord)
    {
        var bounds = new Bounds(glyphs[_words[firstWord].GlyphStart]);
        for (int word = firstWord; word < _wordCount; word++)
        {
            TextWord current = _words[word];
            for (int index = current.GlyphStart; index < current.GlyphStart + current.GlyphCount; index++)
            {
                bounds.Add(glyphs[index].Quad);
            }
        }

        return bounds.ToQuad();
    }

    private static Quad Bound(ReadOnlySpan<TextGlyph> glyphs, int first, int last)
    {
        var bounds = new Bounds(glyphs[first]);
        for (int index = first; index <= last; index++)
        {
            bounds.Add(glyphs[index].Quad);
        }

        return bounds.ToQuad();
    }

    private void Append(char unit)
    {
        if (_plainLength == _plain.Length)
        {
            Array.Resize(ref _plain, _plain.Length * 2);
        }

        _plain[_plainLength++] = unit;
    }

    private void Append(ReadOnlySpan<char> units)
    {
        if (_plain.Length - _plainLength < units.Length)
        {
            Array.Resize(ref _plain, Math.Max(_plain.Length * 2, _plainLength + units.Length));
        }

        units.CopyTo(_plain.AsSpan(_plainLength));
        _plainLength += units.Length;
    }

    /// <summary>The smallest box, in the frame of one glyph's baseline, holding a set of quads.</summary>
    private struct Bounds
    {
        private readonly PathPoint _origin;
        private readonly PathPoint _along;
        private readonly PathPoint _up;
        private double _minAlong;
        private double _maxAlong;
        private double _minUp;
        private double _maxUp;

        public Bounds(in TextGlyph frame)
        {
            // An orthonormal frame on the baseline: x along the text space x axis, y perpendicular to it on the side text space y points to.
            Matrix matrix = frame.TextRenderingMatrix;
            PathPoint x = matrix.TransformVector(1, 0);
            PathPoint y = matrix.TransformVector(0, 1);
            double length = Math.Sqrt((x.X * x.X) + (x.Y * x.Y));
            if (length <= Degenerate)
            {
                double yLength = Math.Sqrt((y.X * y.X) + (y.Y * y.Y));
                x = yLength <= Degenerate ? new PathPoint(1, 0) : new PathPoint(y.Y, -y.X);
                length = Math.Sqrt((x.X * x.X) + (x.Y * x.Y));
            }

            _along = new PathPoint(x.X / length, x.Y / length);
            _up = (-_along.Y * y.X) + (_along.X * y.Y) < 0 ? new PathPoint(_along.Y, -_along.X) : new PathPoint(-_along.Y, _along.X);
            _origin = frame.Quad.BottomLeft;
            _minAlong = double.PositiveInfinity;
            _maxAlong = double.NegativeInfinity;
            _minUp = double.PositiveInfinity;
            _maxUp = double.NegativeInfinity;
        }

        public void Add(Quad quad)
        {
            Add(quad.BottomLeft);
            Add(quad.BottomRight);
            Add(quad.TopRight);
            Add(quad.TopLeft);
        }

        public readonly Quad ToQuad() => new(At(_minAlong, _minUp), At(_maxAlong, _minUp), At(_maxAlong, _maxUp), At(_minAlong, _maxUp));

        private void Add(PathPoint point)
        {
            double dx = point.X - _origin.X;
            double dy = point.Y - _origin.Y;
            double along = (dx * _along.X) + (dy * _along.Y);
            double up = (dx * _up.X) + (dy * _up.Y);
            _minAlong = Math.Min(_minAlong, along);
            _maxAlong = Math.Max(_maxAlong, along);
            _minUp = Math.Min(_minUp, up);
            _maxUp = Math.Max(_maxUp, up);
        }

        private readonly PathPoint At(double along, double up) =>
            new(_origin.X + (along * _along.X) + (up * _up.X), _origin.Y + (along * _along.Y) + (up * _up.Y));
    }
}
