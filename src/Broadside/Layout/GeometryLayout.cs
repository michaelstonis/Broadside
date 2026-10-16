using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Broadside.Graphics;
using Broadside.Text;

namespace Broadside.Layout;

/// <summary>Lays out pages with no tagged content from their geometry (spec story 36, issue #187).</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.8.2.5 (logical content order) and §14.8.2.2 (artifacts) are what the detection recovers. Each page's glyphs are
/// assembled into words and lines (<see cref="TextLayout"/>); a line splits into <em>fragments</em> where a gap wider than
/// <see cref="FragmentGap"/> line heights separates two words (a column gutter crossed by a content stream that draws row by row).
/// Every length below is in units of the page's body height <c>h</c>, the median fragment height.
/// </para>
/// <para>
/// Running headers and footers come first, over all the detected pages: a fragment in the top or bottom <see cref="RunningRows"/>
/// rows of its page whose text (digits folded, white space dropped) recurs at about the same height on two other pages within
/// <see cref="RunningWindow"/> pages (one in a two-page document), and is set off from the body by a block gap, is an artifact. Then each page is cut recursively (an XY cut): a region
/// splits into bands where a horizontal band of white space wider than the page's typical line gap plus <see cref="BlockGap"/>
/// crosses it, read top to bottom; otherwise into columns where a vertical band of white space at least <see cref="Gutter"/> wide
/// crosses all of it, read left to right.
/// Consecutive bands with the same columns (as many gutters, each inside the previous band's and at least half as wide) are cut
/// together, columns first, so a paragraph break that happens to line up across columns does not interleave them, while a full-width title or
/// footer stays apart; a column that is a single row of a region with several is a piece of a row, not a column, and does not cut.
/// A region with no cut is a block: its fragments form rows. A row with several fragments (a row of a form or a table) is a
/// paragraph of its own, a row shorter than <see cref="ShortRow"/> of the block's width ends one, and a row starts one after extra space
/// (<see cref="ParagraphGap"/> over the block's median line gap), a change of size (<see cref="SizeChange"/>), a first-line indent (<see cref="Indent"/>, when most rows
/// sit on the block's margin) or, in a justified block, after a line that ends <see cref="ShortLine"/> short of the right edge.
/// Lines that are not horizontal are paragraphs of their own, after the rest. Text entirely outside the crop box is left out as an
/// artifact.
/// </para>
/// <para>
/// The page's text is rebuilt in that reading order (each paragraph's glyphs contiguous, artifacts last and flagged), and each
/// paragraph becomes a <see cref="LayoutNodeKind.Paragraph"/> child of the root, after any structure-tree nodes. Everything is
/// sorted with total orders on coordinates and indices, so the result does not depend on sort stability. Scratch buffers are
/// reused from page to page.
/// </para>
/// </remarks>
internal sealed partial class GeometryLayout
{
    /// <summary>A word gap, in line heights, beyond which a line splits into two fragments.</summary>
    internal const double FragmentGap = 1.5;

    /// <summary>The narrowest vertical white space, in <c>h</c>, that separates columns.</summary>
    internal const double Gutter = 1.5;

    /// <summary>How much wider than the typical line gap, in <c>h</c>, horizontal white space is to separate blocks.</summary>
    internal const double BlockGap = 0.6;

    /// <summary>How much wider than the typical line gap, in <c>h</c>, the space before a row is to start a paragraph in a block.</summary>
    internal const double ParagraphGap = 0.3;

    /// <summary>The smallest first-line indent, in <c>h</c>, that starts a paragraph.</summary>
    internal const double Indent = 0.5;

    /// <summary>The largest first-line indent, in <c>h</c>, taken as one.</summary>
    internal const double MaxIndent = 10;

    /// <summary>How far, in <c>h</c>, a row may sit from the block's margin and still be on it.</summary>
    internal const double Margin = 0.3;

    /// <summary>The relative change in row height that starts a paragraph.</summary>
    internal const double SizeChange = 0.2;

    /// <summary>How far, in <c>h</c>, a justified block's line ends before the right edge to end its paragraph.</summary>
    internal const double ShortLine = 2.0;

    /// <summary>How far, in <c>h</c>, a band with no columns may start from a column's left edge and still continue that column.</summary>
    internal const double AlignedColumn = 2.0;

    /// <summary>The fraction of a block's width under which a row ends its paragraph (a last line, a label, a heading).</summary>
    internal const double ShortRow = 0.5;

    /// <summary>The largest slope (rise over run, about 5 degrees) of a horizontal line.</summary>
    internal const double Horizontal = 0.0875;

    /// <summary>The rows at the top and at the bottom of a page where running headers and footers are looked for.</summary>
    internal const int RunningRows = 2;

    /// <summary>How many pages before and after a page a running header or footer is looked for on.</summary>
    internal const int RunningWindow = 4;

    /// <summary>The deepest the cut recurses; a region deeper than this is one block.</summary>
    private const int MaxDepth = 64;

    /// <summary>The detector this thread reuses, so its scratch buffers grow once per thread rather than once per layout.</summary>
    [ThreadStatic]
    private static GeometryLayout? _cached;

    private readonly List<Analysis> _pages = [];
    private readonly List<Fragment> _build = [];
    private readonly List<int> _bands = [];
    private readonly List<(double Low, double High)> _gutters = [];
    private readonly List<(double Low, double High)> _unionGutters = [];
    private readonly List<int> _order = [];
    private readonly List<Paragraph> _paragraphs = [];
    private readonly StringBuilder _key = new();
    private int[] _items = [];
    private int[] _scratch = [];
    private double[] _values = [];
    private (double Near, double Far)[] _spans = [];
    private Row[] _rows = [];
    private Fragment[] _fragments = [];
    private double _height;
    private double _lineGap;

    /// <summary>Takes this thread's detector, or a new one.</summary>
    /// <returns>An empty detector.</returns>
    public static GeometryLayout Rent()
    {
        GeometryLayout? detector = _cached;
        _cached = null;
        return detector ?? new GeometryLayout();
    }

    /// <summary>Gives a detector back to this thread, holding nothing of the document it laid out.</summary>
    /// <param name="detector">The detector.</param>
    public static void Return(GeometryLayout detector)
    {
        detector._pages.Clear();
        detector._build.Clear();
        detector._order.Clear();
        detector._paragraphs.Clear();
        detector._fragments = [];
        detector._tables = null;
        detector._figures = null;
        detector._current = null;
        detector._styles.Clear();
        detector._bold.Clear();
        _cached = detector;
    }

    /// <summary>Splits a page's text into fragments; the page is laid out by <see cref="Finish"/>.</summary>
    /// <param name="slot">Where the page goes in the tree's page list.</param>
    /// <param name="page">The page.</param>
    /// <param name="pageIndex">The page's index in the document.</param>
    /// <param name="text">The page's text.</param>
    /// <param name="rulings">The page's ruling lines, collected in the same pass as its text; <see langword="null"/> when none were.</param>
    public void Analyze(int slot, PdfPage page, int pageIndex, TextPage text, RulingCollector? rulings)
    {
        ReadOnlySpan<TextGlyph> glyphs = text.GlyphSpan;
        Ruling[] horizontalRules = Sorted(rulings?.Horizontal);
        Ruling[] verticalRules = Sorted(rulings?.Vertical);
        _build.Clear();
        TextLayout layout = TextLayout.Rent();
        try
        {
            layout.Build(glyphs, text.Text);
            ReadOnlySpan<TextWord> words = layout.Words;
            foreach (TextLine line in layout.Lines)
            {
                Quad quad = line.Quad;
                double run = quad.BottomRight.X - quad.BottomLeft.X;
                double rise = quad.BottomRight.Y - quad.BottomLeft.Y;
                bool horizontal = !line.IsVertical && run > 0 && Math.Abs(rise) <= Horizontal * run;
                double lineHeight = Math.Sqrt(Square(quad.TopLeft.X - quad.BottomLeft.X) + Square(quad.TopLeft.Y - quad.BottomLeft.Y));
                Fragment current = default;
                bool open = false;
                for (int index = line.WordStart; index < line.WordStart + line.WordCount; index++)
                {
                    TextWord word = words[index];
                    PdfRectangle bounds = word.Quad.Bounds;
                    bool artifact = (glyphs[word.GlyphStart].Flags & TextGlyphFlags.Artifact) != 0;
                    bool invisible = (glyphs[word.GlyphStart].Flags & TextGlyphFlags.Invisible) != 0;
                    if (open && artifact == current.Artifact && invisible == current.Invisible && (!horizontal || (bounds.Left - current.Right <= FragmentGap * lineHeight
                        && !CrossesRule(verticalRules, current.Right, bounds.Left, (bounds.Bottom + bounds.Top) / 2))))
                    {
                        if (current.MarkerEnd > 0 && double.IsNaN(current.BodyLeft))
                        {
                            current.BodyLeft = bounds.Left;
                        }

                        current.Left = Math.Min(current.Left, bounds.Left);
                        current.Bottom = Math.Min(current.Bottom, bounds.Bottom);
                        current.Right = Math.Max(current.Right, bounds.Right);
                        current.Top = Math.Max(current.Top, bounds.Top);
                        continue;
                    }

                    if (open)
                    {
                        _build.Add(current);
                    }

                    current = new Fragment
                    {
                        GlyphStart = word.GlyphStart,
                        Left = bounds.Left,
                        Bottom = bounds.Bottom,
                        Right = bounds.Right,
                        Top = bounds.Top,
                        Horizontal = horizontal,
                        Artifact = artifact,
                        Invisible = invisible,
                        BodyLeft = double.NaN,
                    };
                    if (horizontal && !artifact)
                    {
                        FindMarker(ref current, word, glyphs, text);
                    }

                    open = true;
                }

                if (open)
                {
                    _build.Add(current);
                }
            }
        }
        finally
        {
            TextLayout.Return(layout);
        }

        // Text outside the crop box is not shown (§14.11.2): it is left out like an artifact.
        PdfRectangle crop = page.CropBox;
        foreach (ref Fragment fragment in CollectionsMarshal.AsSpan(_build))
        {
            if (fragment.Right <= crop.Left || fragment.Left >= crop.Right || fragment.Top <= crop.Bottom || fragment.Bottom >= crop.Top)
            {
                fragment.Artifact = true;
            }
        }

        // The fragments partition the glyphs: white space and empty glyphs between words go with the fragment before them.
        Span<Fragment> fragments = CollectionsMarshal.AsSpan(_build);
        for (int index = 0; index < fragments.Length; index++)
        {
            fragments[index].GlyphEnd = index + 1 < fragments.Length ? fragments[index + 1].GlyphStart : glyphs.Length;
        }

        if (!fragments.IsEmpty)
        {
            fragments[0].GlyphStart = 0;
        }

        int count = 0;
        Span<double> heights = Grow(ref _values, fragments.Length);
        foreach (ref readonly Fragment fragment in fragments)
        {
            if (fragment.Horizontal && !fragment.Artifact)
            {
                heights[count++] = fragment.Top - fragment.Bottom;
            }
        }

        _fragments = fragments.ToArray();
        _height = Median(heights[..count], 1);
        count = 0;
        Span<int> items = Grow(ref _items, _fragments.Length);
        for (int index = 0; index < _fragments.Length; index++)
        {
            if (Eligible(_fragments[index]))
            {
                items[count++] = index;
            }
        }

        _pages.Add(new Analysis(slot, page, pageIndex, text, _fragments, _height, TypicalLineGap(items[..count]))
        {
            Horizontal = horizontalRules,
            Vertical = verticalRules,
            Graphics = rulings is null ? [] : [.. rulings.Graphics],
        });
    }

    /// <summary>Marks running artifacts over every analysed page, then lays each out and adds its paragraphs to <paramref name="root"/>.</summary>
    /// <param name="root">The tree's root.</param>
    /// <param name="pages">The tree's pages; each analysed page's slot is filled.</param>
    public void Finish(LayoutNode root, LayoutPage[] pages)
    {
        MarkRunningArtifacts();
        foreach (Analysis analysis in _pages)
        {
            LayOut(analysis);
            Describe(analysis);
            Captions(analysis);
        }

        Classify();
        foreach (Analysis analysis in _pages)
        {
            pages[analysis.Slot] = Emit(analysis, root);
        }

        LinkContinuedTables();
    }

    private static double Square(double value) => value * value;

    private static Span<T> Grow<T>(ref T[] buffer, int length)
    {
        if (buffer.Length < length)
        {
            buffer = new T[Math.Max(length, buffer.Length * 2)];
        }

        return buffer.AsSpan(0, length);
    }

    private static double Median(Span<double> values, double fallback)
    {
        if (values.IsEmpty)
        {
            return fallback;
        }

        values.Sort();
        double median = values[values.Length / 2];
        return median > 0 ? median : fallback;
    }

    private static bool Eligible(in Fragment fragment) => fragment.Horizontal && !fragment.Artifact;

    private void MarkRunningArtifacts()
    {
        if (_pages.Count < 2)
        {
            return;
        }

        var occurrences = new Dictionary<string, List<(int Page, double Top)>>(StringComparer.Ordinal);
        var candidates = new List<(int Page, int Fragment, string Key)>();
        for (int page = 0; page < _pages.Count; page++)
        {
            Analysis analysis = _pages[page];
            Fragment[] fragments = analysis.Fragments;
            double top = RowEdge(analysis, fromTop: true);
            double bottom = RowEdge(analysis, fromTop: false);
            for (int index = 0; index < fragments.Length; index++)
            {
                ref readonly Fragment fragment = ref fragments[index];
                if (!Eligible(fragment) || (-fragment.Top >= top && fragment.Bottom >= bottom) || Key(analysis, fragment) is not { } key)
                {
                    continue;
                }

                if (!occurrences.TryGetValue(key, out List<(int Page, double Top)>? list))
                {
                    occurrences.Add(key, list = []);
                }

                list.Add((page, fragment.Top));
                candidates.Add((page, index, key));
            }
        }

        int needed = Math.Min(2, _pages.Count - 1);
        foreach ((int page, int index, string key) in candidates)
        {
            Analysis analysis = _pages[page];
            ref Fragment fragment = ref analysis.Fragments[index];
            double tolerance = Math.Max(fragment.Top - fragment.Bottom, 1);
            int pages = 0;
            int last = -1;
            foreach ((int other, double top) in occurrences[key])
            {
                if (other != page && other != last && Math.Abs(other - page) <= RunningWindow && Math.Abs(top - fragment.Top) <= tolerance)
                {
                    pages++;
                    last = other;
                }
            }

            if (pages >= needed)
            {
                fragment.Artifact = true;
            }
        }
    }

    /// <summary>
    /// How far from the top (or bottom) edge a fragment starts when it is in the running rows: among the first <see cref="RunningRows"/>
    /// rows, those before the last block gap (white space as wide as <see cref="Bands"/> cuts at). Negative infinity, so no fragment,
    /// when no such gap follows them.
    /// </summary>
    private double RowEdge(Analysis analysis, bool fromTop)
    {
        // Distances from the page edge: a fragment spans [Near, Far], the near side being its top (from the top) or bottom.
        int count = 0;
        Span<(double Near, double Far)> spans = Grow(ref _spans, analysis.Fragments.Length);
        foreach (ref readonly Fragment fragment in analysis.Fragments.AsSpan())
        {
            if (Eligible(fragment))
            {
                spans[count++] = fromTop ? (-fragment.Top, -fragment.Bottom) : (fragment.Bottom, fragment.Top);
            }
        }

        spans = spans[..count];
        spans.Sort();
        double gap = analysis.LineGap + (BlockGap * analysis.Height);
        double edge = double.NegativeInfinity;
        int rows = 0;
        double rowNear = 0;
        double rowFar = 0;
        foreach ((double near, double far) in spans)
        {
            if (rows > 0 && near <= rowNear + (0.5 * analysis.Height))
            {
                rowFar = Math.Max(rowFar, far);
                continue;
            }

            if (rows > 0 && near - rowFar >= gap)
            {
                edge = near;
            }

            if (rows++ == RunningRows)
            {
                break;
            }

            rowNear = near;
            rowFar = far;
        }

        return edge;
    }

    /// <summary>A fragment's text with white space dropped and digits folded to <c>#</c>, or <see langword="null"/> when that is empty.</summary>
    private string? Key(Analysis analysis, in Fragment fragment)
    {
        _key.Clear();
        ReadOnlySpan<TextGlyph> glyphs = analysis.Text.GlyphSpan;
        for (int index = fragment.GlyphStart; index < fragment.GlyphEnd; index++)
        {
            foreach (char character in analysis.Text.GetGlyphText(glyphs[index]))
            {
                if (!char.IsWhiteSpace(character))
                {
                    _key.Append(char.IsDigit(character) ? '#' : character);
                }
            }
        }

        return _key.Length == 0 ? null : _key.ToString();
    }

    /// <summary>Puts the page's eligible fragments in reading order (<see cref="_order"/>) and marks where paragraphs start (<see cref="_paragraphs"/>).</summary>
    private void LayOut(Analysis analysis)
    {
        _order.Clear();
        _paragraphs.Clear();
        _fragments = analysis.Fragments;
        _fragmentCount = _fragments.Length;
        _current = analysis;
        _tables = null;
        _figures = null;
        _height = analysis.Height;
        int original = _fragments.Length;
        int count = 0;

        // Room for a proxy per table and per figure beside the text's fragments.
        Span<int> items = Grow(ref _items, original + 1 + analysis.Horizontal.Length + analysis.Graphics.Length);
        for (int index = 0; index < original; index++)
        {
            if (Eligible(_fragments[index]))
            {
                items[count++] = index;
            }
        }

        _lineGap = analysis.LineGap;
        count = Lattices(analysis, items[..count]);
        count = Figures(analysis, items, count);
        Cut(items[..count], 0, columnsFirst: false);
        for (int index = 0; index < original; index++)
        {
            if (!_fragments[index].Horizontal && !_fragments[index].Artifact && !_fragments[index].Absorbed)
            {
                _paragraphs.Add(new Paragraph(_order.Count) { Rows = 1 });
                _order.Add(index);
            }
        }

        if (_fragmentCount != original)
        {
            // Proxies stand for the tables and figures in the reading order.
            analysis.Fragments = _fragments[.._fragmentCount];
            _fragments = analysis.Fragments;
            analysis.Tables = _tables;
            analysis.Figures = _figures;
        }
    }

    /// <summary>The median white space between a fragment and the nearest one below it that it overlaps horizontally.</summary>
    private double TypicalLineGap(ReadOnlySpan<int> items)
    {
        Span<int> sorted = Grow(ref _scratch, items.Length);
        items.CopyTo(sorted);
        sorted.Sort(new ByTop(_fragments));
        Span<double> gaps = Grow(ref _values, items.Length);
        int count = 0;
        for (int index = 0; index < sorted.Length; index++)
        {
            ref readonly Fragment above = ref _fragments[sorted[index]];
            double best = double.PositiveInfinity;
            for (int next = index + 1; next < sorted.Length; next++)
            {
                ref readonly Fragment below = ref _fragments[sorted[next]];
                if (below.Top < above.Bottom - (3 * _height))
                {
                    break;
                }

                if (below.Top < (above.Top + above.Bottom) / 2 && Math.Min(above.Right, below.Right) > Math.Max(above.Left, below.Left))
                {
                    best = Math.Min(best, above.Bottom - below.Top);
                }
            }

            if (!double.IsPositiveInfinity(best))
            {
                gaps[count++] = best;
            }
        }

        if (count == 0)
        {
            return 0.25 * _height;
        }

        Span<double> found = gaps[..count];
        found.Sort();
        return Math.Max(found[count / 2], 0);
    }

    /// <summary>Cuts a region, by bands first unless it is a group of bands with the same columns (<paramref name="columnsFirst"/>).</summary>
    private void Cut(Span<int> items, int depth, bool columnsFirst)
    {
        if (items.IsEmpty)
        {
            return;
        }

        if (depth < MaxDepth && items.Length > 1
            && (columnsFirst ? Table(items) || Columns(items, depth) || Bands(items, depth) : Bands(items, depth) || Table(items) || Columns(items, depth)))
        {
            return;
        }

        Block(items);
    }

    /// <summary>Takes a whole region as a table found from white space, if it is one.</summary>
    private bool Table(Span<int> items)
    {
        int proxy = StreamTable(items);
        if (proxy < 0)
        {
            return false;
        }

        AddTableParagraph(proxy);
        return true;
    }

    /// <summary>Cuts <paramref name="items"/> into columns left to right, if white space crosses it from top to bottom.</summary>
    private bool Columns(Span<int> items, int depth)
    {
        items.Sort(new ByLeft(_fragments));
        double gutter = Gutter * _height;
        double right = _fragments[items[0]].Right;
        bool markers = _fragments[items[0]].IsMarkerOnly;
        bool any = false;
        for (int index = 1; index < items.Length && !any; index++)
        {
            any = _fragments[items[index]].Left - right >= gutter && !markers;
            right = Math.Max(right, _fragments[items[index]].Right);
            markers &= _fragments[items[index]].IsMarkerOnly;
        }

        if (!any || !Columnar(items))
        {
            return false;
        }

        int start = 0;
        right = _fragments[items[0]].Right;
        markers = _fragments[items[0]].IsMarkerOnly;
        for (int index = 1; index < items.Length; index++)
        {
            ref readonly Fragment fragment = ref _fragments[items[index]];
            if (fragment.Left - right >= gutter && !markers)
            {
                Cut(items[start..index], depth + 1, columnsFirst: false);
                start = index;
                markers = true;
            }

            right = Math.Max(right, fragment.Right);
            markers &= fragment.IsMarkerOnly;
        }

        Cut(items[start..], depth + 1, columnsFirst: false);
        return true;
    }

    /// <summary>
    /// Whether the columns of <paramref name="items"/> (sorted left to right) are columns: when the region has more than one row, no
    /// column is a single row, which is rather a piece of a row of the other columns (a word or a field label past a gap).
    /// </summary>
    private bool Columnar(ReadOnlySpan<int> items)
    {
        Extent region = default;
        Extent column = default;
        double gutter = Gutter * _height;
        double right = double.NegativeInfinity;
        bool singleRowColumn = false;
        for (int index = 0; index < items.Length; index++)
        {
            ref readonly Fragment fragment = ref _fragments[items[index]];
            if (index > 0 && fragment.Left - right >= gutter)
            {
                singleRowColumn |= column.IsOneRow;
                column = default;
            }

            column.Add(fragment);
            region.Add(fragment);
            right = Math.Max(right, fragment.Right);
        }

        singleRowColumn |= column.IsOneRow;
        return region.IsOneRow || !singleRowColumn;
    }

    /// <summary>Cuts <paramref name="items"/> into bands top to bottom, if white space crosses it from side to side; bands with the same columns go together.</summary>
    private bool Bands(Span<int> items, int depth)
    {
        items.Sort(new ByTop(_fragments));
        double gap = _lineGap + (BlockGap * _height);
        int mark = _bands.Count;
        double bottom = _fragments[items[0]].Bottom;
        for (int index = 1; index < items.Length; index++)
        {
            ref readonly Fragment fragment = ref _fragments[items[index]];
            if (bottom - fragment.Top >= gap)
            {
                _bands.Add(index);
            }

            bottom = Math.Min(bottom, fragment.Bottom);
        }

        if (_bands.Count == mark)
        {
            return false;
        }

        _bands.Add(items.Length);
        int bandCount = _bands.Count - mark;
        int groupStart = 0;
        for (int band = 0; band < bandCount; band++)
        {
            // The recursion below appends to _bands past this call's entries and removes what it added, so these stay put.
            int groupEnd = _bands[mark + band];
            if (band + 1 < bandCount && SameColumns(items[groupStart..groupEnd], items[groupEnd.._bands[mark + band + 1]], items[groupStart.._bands[mark + band + 1]]))
            {
                continue;
            }

            Cut(items[groupStart..groupEnd], depth + 1, columnsFirst: groupStart != (band == 0 ? 0 : _bands[mark + band - 1]));
            groupStart = groupEnd;
        }

        _bands.RemoveRange(mark, _bands.Count - mark);
        return true;
    }

    /// <summary>
    /// Whether <paramref name="union"/> (a group of bands and the next <paramref name="band"/>) has the same columns as
    /// <paramref name="group"/>. A band with no columns of its own (the rest of one column, past the others' end) also has to start
    /// within <see cref="AlignedColumn"/> of a column's left edge, so a footer or a centred line under the columns stays apart.
    /// </summary>
    private bool SameColumns(ReadOnlySpan<int> group, ReadOnlySpan<int> band, ReadOnlySpan<int> union)
    {
        Gutters(group, _gutters);
        if (_gutters.Count == 0)
        {
            return false;
        }

        Gutters(band, _unionGutters);
        if (_unionGutters.Count == 0)
        {
            double groupLeft = double.PositiveInfinity;
            foreach (int item in group)
            {
                groupLeft = Math.Min(groupLeft, _fragments[item].Left);
            }

            double bandLeft = double.PositiveInfinity;
            foreach (int item in band)
            {
                bandLeft = Math.Min(bandLeft, _fragments[item].Left);
            }

            bool aligned = Math.Abs(bandLeft - groupLeft) <= AlignedColumn * _height;
            foreach ((_, double high) in _gutters)
            {
                aligned |= Math.Abs(bandLeft - high) <= AlignedColumn * _height;
            }

            if (!aligned)
            {
                return false;
            }
        }

        Gutters(union, _unionGutters);
        if (_unionGutters.Count != _gutters.Count)
        {
            return false;
        }

        for (int index = 0; index < _gutters.Count; index++)
        {
            (double low, double high) = _gutters[index];
            (double unionLow, double unionHigh) = _unionGutters[index];
            if (unionLow >= high || unionHigh <= low || unionHigh - unionLow < 0.5 * (high - low))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The vertical white-space bands at least <see cref="Gutter"/> wide that cross every fragment of <paramref name="items"/>, left to right.</summary>
    private void Gutters(ReadOnlySpan<int> items, List<(double Low, double High)> gutters)
    {
        gutters.Clear();
        Span<int> sorted = Grow(ref _scratch, items.Length);
        items.CopyTo(sorted);
        sorted.Sort(new ByLeft(_fragments));
        double right = _fragments[sorted[0]].Right;
        bool markers = _fragments[sorted[0]].IsMarkerOnly;
        for (int index = 1; index < sorted.Length; index++)
        {
            ref readonly Fragment fragment = ref _fragments[sorted[index]];
            if (fragment.Left - right >= Gutter * _height && !markers)
            {
                gutters.Add((right, fragment.Left));
                markers = true;
            }

            right = Math.Max(right, fragment.Right);
            markers &= fragment.IsMarkerOnly;
        }
    }

    /// <summary>Splits a block (a region with no cut) into rows and the rows into paragraphs.</summary>
    private void Block(Span<int> items)
    {
        items.Sort(new ByTop(_fragments));
        int rowCount = 0;
        Span<Row> rows = Grow(ref _rows, items.Length);
        for (int index = 0; index < items.Length;)
        {
            int start = index;
            ref readonly Fragment first = ref _fragments[items[index++]];
            double top = first.Top;
            double bottom = first.Bottom;
            while (index < items.Length && !first.IsProxy)
            {
                ref readonly Fragment next = ref _fragments[items[index]];
                double overlap = Math.Min(top, next.Top) - Math.Max(bottom, next.Bottom);
                if (next.IsProxy || overlap < 0.5 * Math.Min(top - bottom, next.Top - next.Bottom))
                {
                    break;
                }

                top = Math.Max(top, next.Top);
                bottom = Math.Min(bottom, next.Bottom);
                index++;
            }

            Span<int> row = items[start..index];
            row.Sort(new ByLeft(_fragments));
            double right = double.NegativeInfinity;
            double height = 0;
            foreach (int item in row)
            {
                right = Math.Max(right, _fragments[item].Right);
                height = Math.Max(height, _fragments[item].Top - _fragments[item].Bottom);
            }

            rows[rowCount++] = new Row(start, row.Length, _fragments[row[0]].Left, right, top, bottom, height);
        }

        rows = rows[..rowCount];
        double margin = double.PositiveInfinity;
        double edge = double.NegativeInfinity;
        foreach (Row row in rows)
        {
            margin = Math.Min(margin, row.Left);
            edge = Math.Max(edge, row.Right);
        }

        int onMargin = 0;
        int justified = 0;
        for (int index = 0; index < rows.Length; index++)
        {
            onMargin += rows[index].Left - margin <= Margin * _height ? 1 : 0;
            justified += index + 1 < rows.Length && edge - rows[index].Right <= 0.5 * _height ? 1 : 0;
        }

        // The block's own line gap when it has enough rows to tell (a form's rows can be spaced wider than its prose).
        double lineGap = _lineGap;
        if (rows.Length >= 4)
        {
            Span<double> gaps = Grow(ref _values, rows.Length - 1);
            for (int index = 1; index < rows.Length; index++)
            {
                gaps[index - 1] = rows[index - 1].Bottom - rows[index].Top;
            }

            gaps.Sort();
            lineGap = Math.Max(gaps[gaps.Length / 2], 0);
        }

        bool indents = rows.Length >= 2 && onMargin * 2 >= rows.Length;
        bool shortLines = rows.Length >= 3 && justified * 5 >= (rows.Length - 1) * 3;
        int previousCount = 0;
        int tried = 0;
        double body = double.NaN;
        for (int index = 0; index < rows.Length; index++)
        {
            Row row = rows[index];
            if (_fragments[items[row.Start]].IsProxy)
            {
                AddProxyParagraph(items[row.Start]);
                previousCount = 2;
                continue;
            }

            // A run of rows of several fragments may be a table found from white space (each run is tried once).
            int run = index >= tried ? TabularRun(items, rows, index) : index;
            if (run > index)
            {
                tried = run;
                int proxy = StreamTable(items[rows[index].Start..(rows[run - 1].Start + rows[run - 1].Count)]);
                if (proxy >= 0)
                {
                    AddTableParagraph(proxy);
                    previousCount = 2;
                    index = run - 1;
                    continue;
                }
            }

            // A list item's marker set apart by a gutter-wide gap is a fragment of its own that does not make the row a form row.
            ref readonly Fragment head = ref _fragments[items[row.Start]];
            bool markerOnly = head.IsMarkerOnly;
            int count = markerOnly && row.Count > 1 ? row.Count - 1 : row.Count;
            bool item = head.MarkerEnd > 0 && (!markerOnly || row.Count > 1);
            if (index == 0 || item || (!ContinuesItem(rows[index - 1], row, previousCount, count, body, margin, edge, lineGap)
                && StartsParagraph(rows[index - 1], row, previousCount, count, margin, edge, lineGap, indents, shortLines)))
            {
                body = !item ? double.NaN : markerOnly ? _fragments[items[row.Start + 1]].Left : head.BodyLeft;
                _paragraphs.Add(new Paragraph(_order.Count) { Body = body });
            }

            ref Paragraph paragraph = ref CollectionsMarshal.AsSpan(_paragraphs)[^1];
            paragraph.Rows++;
            paragraph.Tabular |= count > 1;
            previousCount = count;
            foreach (int fragment in items.Slice(row.Start, row.Count))
            {
                _order.Add(fragment);
            }
        }
    }

    /// <summary>The end of the run of at least <see cref="MinTableRows"/> rows of several fragments starting at <paramref name="start"/>; <paramref name="start"/> when there is none.</summary>
    private int TabularRun(Span<int> items, ReadOnlySpan<Row> rows, int start)
    {
        int end = start;
        while (end < rows.Length && !_fragments[items[rows[end].Start]].IsProxy
            && rows[end].Count - (_fragments[items[rows[end].Start]].IsMarkerOnly ? 1 : 0) > 1)
        {
            end++;
        }

        return end - start >= MinTableRows ? end : start;
    }

    /// <summary>Whether a row continues the list item above: it starts where the item's body does, with no break in spacing or size.</summary>
    private bool ContinuesItem(in Row previous, in Row row, int previousCount, int count, double body, double margin, double edge, double lineGap) =>
        !double.IsNaN(body) && previousCount == 1 && count == 1
        && Math.Abs(row.Left - body) <= Margin * _height
        && previous.Right - margin >= ShortRow * (edge - margin)
        && previous.Bottom - row.Top <= lineGap + (ParagraphGap * _height)
        && Math.Abs(row.Height - previous.Height) <= SizeChange * Math.Max(row.Height, previous.Height);

    private bool StartsParagraph(in Row previous, in Row row, int previousCount, int count, double margin, double edge, double lineGap, bool indents, bool shortLines)
    {
        double indent = row.Left - margin;
        return previousCount > 1 || count > 1
            || previous.Right - margin < ShortRow * (edge - margin)
            || previous.Bottom - row.Top > lineGap + (ParagraphGap * _height)
            || Math.Abs(row.Height - previous.Height) > SizeChange * Math.Max(row.Height, previous.Height)
            || (indents && indent >= Indent * _height && indent <= MaxIndent * _height)
            || (shortLines && edge - previous.Right >= ShortLine * _height && indent <= Margin * _height);
    }

    /// <summary>Rebuilds the page's text in reading order and adds its headings, paragraphs and lists to <paramref name="root"/>.</summary>
    private LayoutPage Emit(Analysis analysis, LayoutNode root)
    {
        _fragments = analysis.Fragments;
        int[] order = analysis.Order;
        Paragraph[] paragraphs = analysis.Paragraphs;
        TextPage source = analysis.Text;
        ReadOnlySpan<TextGlyph> glyphs = source.GlyphSpan;
        var ordered = new TextGlyph[glyphs.Length];
        var text = new StringBuilder(source.Text.Length);
        int count = 0;
        var placed = new Placed[paragraphs.Length];
        for (int paragraph = 0; paragraph < paragraphs.Length; paragraph++)
        {
            int start = paragraphs[paragraph].Start;
            int end = paragraph + 1 < paragraphs.Length ? paragraphs[paragraph + 1].Start : order.Length;
            ref Placed place = ref placed[paragraph];
            place.GlyphStart = count;
            place.TextStart = text.Length;
            if (paragraphs[paragraph].Table >= 0)
            {
                TablePlan table = analysis.Tables![paragraphs[paragraph].Table];
                EmitTable(table, glyphs, source.Text, ordered, ref count, text);
                place.GlyphCount = count - place.GlyphStart;
                place.TextLength = text.Length - place.TextStart;
                place.Bounds = place.Body = table.Bounds;
                continue;
            }

            if (paragraphs[paragraph].Figure >= 0)
            {
                FigurePlan figure = analysis.Figures![paragraphs[paragraph].Figure];
                EmitFigure(figure, glyphs, source.Text, ordered, ref count, text);
                place.GlyphCount = count - place.GlyphStart;
                place.TextLength = text.Length - place.TextStart;
                place.Bounds = place.Body = figure.Box;
                continue;
            }

            PdfRectangle? bounds = null;
            PdfRectangle? body = null;
            for (int position = start; position < end; position++)
            {
                ref readonly Fragment fragment = ref _fragments[order[position]];
                var box = new PdfRectangle(fragment.Left, fragment.Bottom, fragment.Right, fragment.Top);
                bounds = Union(bounds, box);
                if (position == start && paragraphs[paragraph].IsItem)
                {
                    // The marker is the item's label; the rest of the fragment starts its body.
                    Copy(glyphs, source.Text, ordered, ref count, text, fragment.GlyphStart, fragment.MarkerEnd, artifact: false);
                    place.LabelGlyphs = count - place.GlyphStart;
                    place.LabelText = text.Length - place.TextStart;
                    place.Label = new PdfRectangle(fragment.Left, fragment.Bottom, Math.Max(fragment.MarkerRight, fragment.Left), fragment.Top);
                    Copy(glyphs, source.Text, ordered, ref count, text, fragment.MarkerEnd, fragment.GlyphEnd, artifact: false);
                    if (!fragment.IsMarkerOnly)
                    {
                        body = new PdfRectangle(Math.Min(fragment.BodyLeft, fragment.Right), fragment.Bottom, fragment.Right, fragment.Top);
                    }

                    continue;
                }

                Copy(glyphs, source.Text, ordered, ref count, text, fragment.GlyphStart, fragment.GlyphEnd, artifact: false);
                body = Union(body, box);
            }

            place.GlyphCount = count - place.GlyphStart;
            place.TextLength = text.Length - place.TextStart;
            place.Bounds = bounds!.Value;
            place.Body = body ?? bounds.Value;
        }

        if (_fragments.Length == 0)
        {
            // No words: the page holds only white space or empty glyphs, which no paragraph takes.
            Copy(glyphs, source.Text, ordered, ref count, text, 0, glyphs.Length, artifact: false);
        }

        foreach (ref readonly Fragment fragment in _fragments.AsSpan())
        {
            if (fragment.Artifact)
            {
                Copy(glyphs, source.Text, ordered, ref count, text, fragment.GlyphStart, fragment.GlyphEnd, artifact: true);
            }
        }

        Debug.Assert(count == glyphs.Length, "The fragments partition the page's glyphs.");
        var page = new LayoutPage(analysis.Page, analysis.PageIndex, new TextPage(ordered, text.ToString()), isDetected: true);
        var placement = new Placement(page, paragraphs, placed, analysis.Height, analysis.Tables, analysis.Figures);
        AddNodes(placement, root);
        if (placed.Length > 0)
        {
            // The root covers its descendants, as a structure-built node does.
            root.AddExtent(Covering(placement, 0, placed.Length - 1));
        }

        return page;
    }

    /// <summary>Appends a table's glyphs cell by cell, row by row, each cell's fragments top to bottom.</summary>
    private void EmitTable(TablePlan table, ReadOnlySpan<TextGlyph> glyphs, string source, TextGlyph[] ordered, ref int count, StringBuilder text)
    {
        foreach (CellPlan cell in table.Cells)
        {
            cell.Fragments.Sort(new ByTop(_fragments));
            cell.GlyphStart = count;
            cell.TextStart = text.Length;
            PdfRectangle? bounds = cell.Box;
            foreach (int item in cell.Fragments)
            {
                ref readonly Fragment fragment = ref _fragments[item];
                Copy(glyphs, source, ordered, ref count, text, fragment.GlyphStart, fragment.GlyphEnd, artifact: false);
                if (cell.Box is null)
                {
                    bounds = Union(bounds, new PdfRectangle(fragment.Left, fragment.Bottom, fragment.Right, fragment.Top));
                }
            }

            cell.GlyphCount = count - cell.GlyphStart;
            cell.TextLength = text.Length - cell.TextStart;
            cell.Bounds = bounds;
        }
    }

    private static PdfRectangle Union(PdfRectangle? known, PdfRectangle box) => known is { } value ? MarkGeometry.Union(value, box) : box;

    /// <summary>Appends glyphs <c>[start, end)</c> to the reordered page, their text ranges moved to the rebuilt text.</summary>
    private static void Copy(ReadOnlySpan<TextGlyph> glyphs, string source, TextGlyph[] ordered, ref int count, StringBuilder text, int start, int end, bool artifact)
    {
        for (int index = start; index < end; index++)
        {
            ref readonly TextGlyph glyph = ref glyphs[index];
            ordered[count++] = glyph with
            {
                TextStart = text.Length,
                Flags = artifact ? glyph.Flags | TextGlyphFlags.Artifact : glyph.Flags,
            };
            text.Append(source, glyph.TextStart, glyph.TextLength);
        }
    }

    /// <summary>The vertical extent of some fragments and the tallest of them.</summary>
    private struct Extent
    {
        private bool _any;
        private double _top;
        private double _bottom;
        private double _height;

        /// <summary>Gets a value indicating whether the fragments make one row: they span less than one and a half of the tallest.</summary>
        public readonly bool IsOneRow => _any && _top - _bottom < 1.5 * _height;

        public void Add(in Fragment fragment)
        {
            _top = _any ? Math.Max(_top, fragment.Top) : fragment.Top;
            _bottom = _any ? Math.Min(_bottom, fragment.Bottom) : fragment.Bottom;
            _height = Math.Max(_height, fragment.Top - fragment.Bottom);
            _any = true;
        }
    }

    /// <summary>A run of words on one line, with no gap wider than <see cref="FragmentGap"/> line heights.</summary>
    private struct Fragment
    {
        /// <summary>The first glyph (with the white space and empty glyphs before the next fragment's first word).</summary>
        public int GlyphStart;

        /// <summary>The glyph after the last.</summary>
        public int GlyphEnd;

        public double Left;
        public double Bottom;
        public double Right;
        public double Top;

        /// <summary>Whether the fragment's line runs left to right along the page's x axis.</summary>
        public bool Horizontal;

        /// <summary>Whether the fragment is an artifact: marked so in the content, or a running header or footer.</summary>
        public bool Artifact;

        /// <summary>The glyph after the list marker the fragment starts with; 0 when it starts with none.</summary>
        public int MarkerEnd;

        /// <summary>The right edge of the list marker.</summary>
        public double MarkerRight;

        /// <summary>The left edge of the first word after the list marker; NaN when the marker is the whole fragment.</summary>
        public double BodyLeft;

        /// <summary>The list marker the fragment starts with.</summary>
        public Marker Marker;

        /// <summary>For a fragment standing for a table in the cut (it has no glyphs), the table's number from 1; 0 for text.</summary>
        public int Table;

        /// <summary>For a fragment standing for a figure in the cut (it has no glyphs), the figure's number from 1; 0 otherwise.</summary>
        public int Figure;

        /// <summary>Whether the fragment's text is invisible (rendering mode 3 or 7, §9.3.6), as the text a recogniser lays over a scan is.</summary>
        public bool Invisible;

        /// <summary>Whether a figure took the fragment as text inside it, out of the cut.</summary>
        public bool Absorbed;

        /// <summary>Gets a value indicating whether the fragment stands for a table or a figure and has no glyphs.</summary>
        public readonly bool IsProxy => Table != 0 || Figure != 0;

        /// <summary>Gets a value indicating whether the fragment is a list marker and nothing else.</summary>
        public readonly bool IsMarkerOnly => MarkerEnd > 0 && double.IsNaN(BodyLeft);
    }

    /// <summary>The fragments of a block at <c>[Start, Start + Count)</c> of its items that share a line, and their extent.</summary>
    private readonly record struct Row(int Start, int Count, double Left, double Right, double Top, double Bottom, double Height);

    /// <summary>A page to lay out: where it goes, its text, its fragments and its body height.</summary>
    private sealed record Analysis(int Slot, PdfPage Page, int PageIndex, TextPage Text, Fragment[] Fragments, double Height, double LineGap)
    {
        /// <summary>Gets or sets the fragments, with those standing for tables after the text's once the page is laid out.</summary>
        public Fragment[] Fragments { get; set; } = Fragments;

        /// <summary>Gets the page's horizontal rulings, by position.</summary>
        public Ruling[] Horizontal { get; init; } = [];

        /// <summary>Gets the page's vertical rulings, by position.</summary>
        public Ruling[] Vertical { get; init; } = [];

        /// <summary>Gets or sets the page's tables, in the order their proxies were made; null when it has none.</summary>
        public List<TablePlan>? Tables { get; set; }

        /// <summary>Gets the page's images and painted paths, for figures.</summary>
        public Graphic[] Graphics { get; init; } = [];

        /// <summary>Gets or sets the page's figures, in the order their proxies were made; null when it has none.</summary>
        public List<FigurePlan>? Figures { get; set; }

        /// <summary>Gets or sets the eligible fragments in reading order.</summary>
        public int[] Order { get; set; } = [];

        /// <summary>Gets or sets the paragraphs, each starting at its <see cref="Paragraph.Start"/> in <see cref="Order"/>.</summary>
        public Paragraph[] Paragraphs { get; set; } = [];
    }

    /// <summary>Top to bottom, then left to right, then by index: a total order.</summary>
    private readonly struct ByTop(Fragment[] fragments) : IComparer<int>
    {
        public int Compare(int x, int y)
        {
            int order = fragments[y].Top.CompareTo(fragments[x].Top);
            if (order == 0)
            {
                order = fragments[x].Left.CompareTo(fragments[y].Left);
            }

            return order != 0 ? order : x.CompareTo(y);
        }
    }

    /// <summary>Left to right, then top to bottom, then by index: a total order.</summary>
    private readonly struct ByLeft(Fragment[] fragments) : IComparer<int>
    {
        public int Compare(int x, int y)
        {
            int order = fragments[x].Left.CompareTo(fragments[y].Left);
            if (order == 0)
            {
                order = fragments[y].Top.CompareTo(fragments[x].Top);
            }

            return order != 0 ? order : x.CompareTo(y);
        }
    }
}
