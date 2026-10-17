using System.Runtime.InteropServices;
using System.Text;
using Broadside.Text;

namespace Broadside.Layout;

/// <summary>Figures and captions on pages laid out from their geometry (spec story 36, issue #190).</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.8.4.8.5 (<c>Figure</c>) and §14.8.4.8.4 (<c>Caption</c>) are what the detection recovers. The page's images
/// (§8.9) and painted paths (§8.5.3), collected outside artifacts beside the text, are clustered: graphics closer than
/// <see cref="ClusterGap"/> body heights join, and a cluster is a figure when it holds an image or a <see cref="GraphicKind.Shape"/>
/// (rules and frames alone are tables' or the page's lines), is at least <see cref="MinFigure"/> heights across both ways, and is not
/// the page's background (a path over <see cref="BackgroundArea"/> of the crop box) or a ruled table found before. A rule (an
/// axis, a tick, a frame) joins a figure's box only when at least <see cref="RuleInside"/> of it lies within a body height of the
/// box so far, so a page-wide rule does not stretch it. Text whose centre lies within <see cref="LabelReach"/> of the box, and rotated text touching it within
/// one height, is the figure's own (labels, axes, legends) and leaves the cut; a cluster whose visible text covers more than
/// <see cref="MaxTextShare"/> of it is a text box or a table's shading, not a figure, and so is one over
/// <see cref="PageFigureArea"/> of the page with <see cref="BackgroundRows"/> or more pieces of visible text (a page laid out over a
/// background image). Invisible text (rendering mode 3, §9.3.6) over an image is the text a recogniser laid over a scan: it stays
/// in the figure, which is how a scanned page reads. Type 3 glyph procedures (§9.6.4) paint glyphs, which the text has, and are not
/// graphics here.
/// </para>
/// <para>
/// A figure enters the cut as one proxy fragment, like a table, so it takes its place in the reading order. A paragraph starting
/// with a caption label (<c>Figure 3.</c>, <c>Fig. 2:</c>, <c>Table IV</c>, <c>Chart 1 Sales</c>: a known word, a number, roman
/// numeral or letter, then punctuation, a capital or the end) within <see cref="CaptionReach"/> heights above or below a figure or
/// table, overlapping at least half the narrower of the two across, is its caption: the nearest wins, below for a figure and above
/// for a table on a tie. The caption moves next to its figure or table in the reading order and becomes its
/// <see cref="LayoutNodeKind.Caption"/> child. Figures without a caption of their own lying within the same reach of a captioned
/// figure, across the caption's width and not beyond it, are its parts (<c>(a)</c>, <c>(b)</c>) and join it, with the paragraphs
/// between them and the caption (sub-captions, labels set apart).
/// </para>
/// </remarks>
internal sealed partial class GeometryLayout
{
    /// <summary>The widest gap, in body heights, between graphics of one figure.</summary>
    internal const double ClusterGap = 1.0;

    /// <summary>The smallest width and height of a figure, in body heights.</summary>
    internal const double MinFigure = 2.0;

    /// <summary>How far outside a figure's graphics, in body heights, the centre of a label may lie.</summary>
    internal const double LabelReach = 1.0;

    /// <summary>The share of a path's bounds' area of the crop box from which it is the page's background or border.</summary>
    internal const double BackgroundArea = 0.9;

    /// <summary>The share of a rule that must lie within a body height of a figure's box so far for it to join the box.</summary>
    internal const double RuleInside = 0.5;

    /// <summary>The largest share of a figure's area its visible text may cover.</summary>
    internal const double MaxTextShare = 0.35;

    /// <summary>The share of the crop box from which a figure holding visible text is the page's background.</summary>
    internal const double PageFigureArea = 0.5;

    /// <summary>The pieces of visible text that make a page-sized figure a background.</summary>
    internal const int BackgroundRows = 3;

    /// <summary>The widest gap, in body heights, between a figure or table and its caption.</summary>
    internal const double CaptionReach = 5.0;

    private static readonly string[] CaptionWords =
    [
        "Abb", "Abbildung", "Algorithm", "Chart", "Diagram", "Exhibit", "Fig", "Figura", "Figure", "Graph", "Illustration", "Image",
        "Listing", "Map", "Photo", "Plate", "Scheme", "Tab", "Tabla", "Table", "Tableau", "Tabelle",
    ];

    private readonly List<(double Score, int Owner, int Caption, bool Above)> _captionPairs = [];
    private readonly List<int> _absorbing = [];
    private List<FigurePlan>? _figures;
    private int[] _graphicItems = [];
    private bool[] _free = [];
    private int _freeCount;
    private int[] _graphicParents = [];
    private PdfRectangle[] _graphicBoxes = [];
    private PdfRectangle?[] _clusterBoxes = [];

    /// <summary>Finds the page's figures, takes the text inside them out of <paramref name="items"/>'s first <paramref name="count"/> and adds a proxy for each; returns how many items there are.</summary>
    private int Figures(Analysis analysis, Span<int> items, int count)
    {
        Graphic[] graphics = analysis.Graphics;
        if (graphics.Length == 0)
        {
            return count;
        }

        double gap = ClusterGap * _height;
        PdfRectangle crop = analysis.Page.CropBox;
        double cropArea = Math.Max(crop.Width * crop.Height, 1);
        Span<int> candidates = Grow(ref _graphicItems, graphics.Length);
        Span<PdfRectangle> boxes = Grow(ref _graphicBoxes, graphics.Length);
        int n = 0;
        foreach (Graphic graphic in graphics)
        {
            if (Clip(graphic.Box, crop) is not { } box
                || (graphic.Kind != GraphicKind.Image && Area(box) >= BackgroundArea * cropArea)
                || InTable((box.Left + box.Right) / 2, (box.Bottom + box.Top) / 2))
            {
                continue;
            }

            boxes[n] = box;
            candidates[n++] = (int)graphic.Kind;
        }

        if (n == 0)
        {
            return count;
        }

        boxes = boxes[..n];
        Span<int> parents = Grow(ref _graphicParents, n);
        for (int index = 0; index < n; index++)
        {
            parents[index] = index;
        }

        // Graphics closer than the gap join: a sweep over the boxes by left edge.
        Span<int> sorted = Grow(ref _scratch, n);
        for (int index = 0; index < n; index++)
        {
            sorted[index] = index;
        }

        sorted.Sort(new ByBoxLeft(_graphicBoxes));
        for (int first = 0; first < n; first++)
        {
            PdfRectangle a = boxes[sorted[first]];
            for (int second = first + 1; second < n; second++)
            {
                PdfRectangle b = boxes[sorted[second]];
                if (b.Left > a.Right + gap)
                {
                    break;
                }

                if (b.Bottom <= a.Top + gap && a.Bottom <= b.Top + gap)
                {
                    int x = Find(parents, sorted[first]);
                    int y = Find(parents, sorted[second]);
                    if (x != y)
                    {
                        parents[Math.Max(x, y)] = Math.Min(x, y);
                    }
                }
            }
        }

        // Each cluster's images and shapes, then the rules lying by them.
        Span<PdfRectangle?> clusters = Grow(ref _clusterBoxes, n);
        clusters.Clear();
        for (int index = 0; index < n; index++)
        {
            if ((GraphicKind)candidates[index] != GraphicKind.Rule)
            {
                int root = Find(parents, index);
                clusters[root] = Union(clusters[root], boxes[index]);
            }
        }

        // A rule joins when most of it lies by the box so far (an axis, a tick, a frame), until no more do.
        for (bool grew = true; grew;)
        {
            grew = false;
            for (int index = 0; index < n; index++)
            {
                if ((GraphicKind)candidates[index] != GraphicKind.Rule)
                {
                    continue;
                }

                int root = Find(parents, index);
                if (clusters[root] is { } box && Inside(boxes[index], Grow(box, gap)) >= RuleInside)
                {
                    clusters[root] = MarkGeometry.Union(box, boxes[index]);
                    candidates[index] = -1;
                    grew = true;
                }
            }
        }

        // Clusters whose boxes overlap are one figure.
        for (bool merged = true; merged;)
        {
            merged = false;
            for (int first = 0; first < n; first++)
            {
                if (clusters[first] is not { } a)
                {
                    continue;
                }

                for (int second = first + 1; second < n; second++)
                {
                    if (clusters[second] is { } b && Overlaps(a, b))
                    {
                        a = MarkGeometry.Union(a, b);
                        clusters[first] = a;
                        clusters[second] = null;
                        merged = true;
                    }
                }
            }
        }

        // Figures take only text still in the cut (not a table's cells) and text off the horizontal.
        _freeCount = _fragmentCount;
        Span<bool> free = Grow(ref _free, _freeCount);
        free.Clear();
        foreach (int item in items[..count])
        {
            free[item] = !_fragments[item].IsProxy;
        }

        for (int index = 0; index < _fragmentCount; index++)
        {
            free[index] |= !_fragments[index].Horizontal && !_fragments[index].Artifact && !_fragments[index].IsProxy;
        }

        int added = 0;
        int first0 = _figures?.Count ?? 0;
        for (int root = 0; root < n; root++)
        {
            if (clusters[root] is { } box && Accept(analysis, ref box, cropArea))
            {
                AddFigureProxy(box);
                added++;
            }
        }

        if (added == 0)
        {
            return count;
        }

        // The text inside figures leaves the cut; their proxies join it.
        int kept = 0;
        for (int position = 0; position < count; position++)
        {
            if (!_fragments[items[position]].Absorbed)
            {
                items[kept++] = items[position];
            }
        }

        for (int figure = first0; figure < first0 + added; figure++)
        {
            items[kept++] = _figures![figure].Proxy;
        }

        return kept;
    }

    /// <summary>Whether a cluster is a figure; if it is, takes the text inside it and grows <paramref name="box"/> over that text.</summary>
    private bool Accept(Analysis analysis, ref PdfRectangle box, double cropArea)
    {
        double size = MinFigure * _height;
        if (box.Width < size || box.Height < size || InTableBox(box))
        {
            return false;
        }

        PdfRectangle labels = Grow(box, LabelReach * _height);
        PdfRectangle rotated = Grow(box, _height);
        _absorbing.Clear();
        double visibleArea = 0;
        int visible = 0;
        for (int index = 0; index < _freeCount; index++)
        {
            ref readonly Fragment fragment = ref _fragments[index];
            if (!_free[index] || fragment.Artifact || fragment.Absorbed)
            {
                continue;
            }

            bool inside = fragment.Horizontal
                ? Contains(labels, (fragment.Left + fragment.Right) / 2, (fragment.Bottom + fragment.Top) / 2)
                : fragment.Left < rotated.Right && fragment.Right > rotated.Left && fragment.Bottom < rotated.Top && fragment.Top > rotated.Bottom;
            if (!inside || (fragment.Horizontal && !Contains(box, (fragment.Left + fragment.Right) / 2, (fragment.Bottom + fragment.Top) / 2)
                && StartsWithCaptionLabel(analysis.Text, index)))
            {
                continue;
            }

            _absorbing.Add(index);
            if (!fragment.Invisible)
            {
                visible++;
                visibleArea += (fragment.Right - fragment.Left) * (fragment.Top - fragment.Bottom);
            }
        }

        double area = Area(box);
        if (visibleArea > MaxTextShare * area || (area >= PageFigureArea * cropArea && visible >= BackgroundRows))
        {
            return false;
        }

        var plan = new FigurePlan();
        foreach (int index in _absorbing)
        {
            ref Fragment fragment = ref _fragments[index];
            fragment.Absorbed = true;
            plan.Fragments.Add(index);
            box = MarkGeometry.Union(box, new PdfRectangle(fragment.Left, fragment.Bottom, fragment.Right, fragment.Top));
        }

        plan.Box = box;
        _figures ??= [];
        _figures.Add(plan);
        return true;
    }

    /// <summary>Appends a fragment standing for the last figure in the cut, and makes room for it.</summary>
    private void AddFigureProxy(PdfRectangle box)
    {
        FigurePlan figure = _figures![^1];
        if (_fragmentCount == _fragments.Length)
        {
            Array.Resize(ref _fragments, Math.Max(4, _fragments.Length * 2));
        }

        _fragments[_fragmentCount] = new Fragment
        {
            Left = box.Left,
            Bottom = box.Bottom,
            Right = box.Right,
            Top = box.Top,
            Horizontal = true,
            BodyLeft = double.NaN,
            Figure = _figures.Count,
        };
        figure.Proxy = _fragmentCount++;
    }

    /// <summary>Whether a point lies in one of the page's tables found so far.</summary>
    private bool InTable(double x, double y)
    {
        if (_tables is not null)
        {
            foreach (TablePlan table in _tables)
            {
                if (x >= table.Left && x <= table.Right && y >= table.Bottom && y <= table.Top)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether a table found so far has its centre in a box.</summary>
    private bool InTableBox(PdfRectangle box)
    {
        if (_tables is not null)
        {
            foreach (TablePlan table in _tables)
            {
                if (Contains(box, (table.Left + table.Right) / 2, (table.Bottom + table.Top) / 2))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Appends a figure's text, its fragments top to bottom.</summary>
    private void EmitFigure(FigurePlan figure, ReadOnlySpan<TextGlyph> glyphs, string source, TextGlyph[] ordered, ref int count, StringBuilder text)
    {
        figure.Fragments.Sort(new ByTop(_fragments));
        foreach (int item in figure.Fragments)
        {
            ref readonly Fragment fragment = ref _fragments[item];
            Copy(glyphs, source, ordered, ref count, text, fragment.GlyphStart, fragment.GlyphEnd, artifact: false);
        }
    }

    /// <summary>Pairs the page's figures and tables with the paragraphs that caption them, and moves each caption next to its owner in the reading order.</summary>
    private void Captions(Analysis analysis)
    {
        Paragraph[] paragraphs = analysis.Paragraphs;
        int[] order = analysis.Order;
        if ((analysis.Figures is null && analysis.Tables is null) || paragraphs.Length < 2)
        {
            return;
        }

        var bounds = new PdfRectangle[paragraphs.Length];
        var labelled = new bool[paragraphs.Length];
        for (int index = 0; index < paragraphs.Length; index++)
        {
            int end = index + 1 < paragraphs.Length ? paragraphs[index + 1].Start : order.Length;
            PdfRectangle? box = null;
            for (int position = paragraphs[index].Start; position < end; position++)
            {
                ref readonly Fragment fragment = ref _fragments[order[position]];
                box = Union(box, new PdfRectangle(fragment.Left, fragment.Bottom, fragment.Right, fragment.Top));
            }

            bounds[index] = box!.Value;
            labelled[index] = paragraphs[index].Table < 0 && paragraphs[index].Figure < 0 && _fragments[order[paragraphs[index].Start]].Horizontal
                && StartsWithCaptionLabel(analysis.Text, order[paragraphs[index].Start]);
        }

        double reach = CaptionReach * _height;
        double slack = 0.5 * _height;
        _captionPairs.Clear();
        for (int owner = 0; owner < paragraphs.Length; owner++)
        {
            bool figure = paragraphs[owner].Figure >= 0;
            if (!figure && paragraphs[owner].Table < 0)
            {
                continue;
            }

            PdfRectangle box = bounds[owner];
            for (int caption = 0; caption < paragraphs.Length; caption++)
            {
                PdfRectangle text = bounds[caption];
                if (!labelled[caption] || Math.Min(box.Right, text.Right) - Math.Max(box.Left, text.Left) < 0.5 * Math.Min(box.Width, text.Width))
                {
                    continue;
                }

                double below = box.Bottom - text.Top;
                double above = text.Bottom - box.Top;
                if (below >= -slack && below <= reach)
                {
                    _captionPairs.Add((Math.Max(below, 0) + (figure ? 0 : slack), owner, caption, false));
                }

                if (above >= -slack && above <= reach)
                {
                    _captionPairs.Add((Math.Max(above, 0) + (figure ? slack : 0), owner, caption, true));
                }
            }
        }

        if (_captionPairs.Count == 0)
        {
            return;
        }

        _captionPairs.Sort();
        bool any = false;
        foreach ((_, int owner, int caption, bool above) in _captionPairs)
        {
            if (paragraphs[owner].Caption < 0 && paragraphs[caption].Owner < 0)
            {
                paragraphs[owner].Caption = caption;
                paragraphs[owner].CaptionAbove = above;
                paragraphs[caption].Owner = owner;
                any = true;
            }
        }

        if (!any)
        {
            return;
        }

        for (int owner = 0; owner < paragraphs.Length; owner++)
        {
            if (paragraphs[owner].Figure >= 0 && paragraphs[owner].Caption >= 0)
            {
                Gather(analysis, bounds, owner, reach);
            }
        }

        Reorder(analysis);
    }

    /// <summary>
    /// Joins to a captioned figure the figures without a caption of their own that lie by it across the caption's width (the
    /// parts of one figure), and the paragraphs between them and the caption (their labels and sub-captions).
    /// </summary>
    private void Gather(Analysis analysis, PdfRectangle[] bounds, int owner, double reach)
    {
        Paragraph[] paragraphs = analysis.Paragraphs;
        int[] order = analysis.Order;
        FigurePlan figure = analysis.Figures![paragraphs[owner].Figure];
        PdfRectangle caption = bounds[paragraphs[owner].Caption];
        bool below = !paragraphs[owner].CaptionAbove;
        double slack = 0.5 * _height;
        PdfRectangle union = bounds[owner];
        for (bool grew = true; grew;)
        {
            grew = false;
            double left = Math.Min(union.Left, caption.Left);
            double right = Math.Max(union.Right, caption.Right);
            for (int other = 0; other < paragraphs.Length; other++)
            {
                ref Paragraph part = ref paragraphs[other];
                if (other == owner || part.Figure < 0 || part.Caption >= 0 || part.Merged)
                {
                    continue;
                }

                PdfRectangle box = bounds[other];
                double across = Math.Min(right, box.Right) - Math.Max(left, box.Left);
                double down = Math.Max(box.Bottom - union.Top, union.Bottom - box.Top);
                double aside = Math.Max(box.Left - union.Right, union.Left - box.Right);
                bool beyond = below ? box.Top < caption.Bottom + slack : box.Bottom > caption.Top - slack;
                if (across >= 0.5 * box.Width && !beyond && down <= reach && aside <= reach)
                {
                    FigurePlan joined = analysis.Figures[part.Figure];
                    figure.Fragments.AddRange(joined.Fragments);
                    union = MarkGeometry.Union(union, box);
                    part.Merged = true;
                    grew = true;
                }
            }
        }

        // What lies between the parts and the caption are their labels.
        PdfRectangle region = Grow(MarkGeometry.Union(union, caption), slack);
        for (int other = 0; other < paragraphs.Length; other++)
        {
            ref Paragraph text = ref paragraphs[other];
            PdfRectangle box = bounds[other];
            if (text.Figure >= 0 || text.Table >= 0 || text.Owner >= 0 || text.Merged
                || box.Left < region.Left || box.Right > region.Right || box.Bottom < region.Bottom || box.Top > region.Top
                || (below ? box.Top < caption.Bottom : box.Bottom > caption.Top))
            {
                continue;
            }

            int end = other + 1 < paragraphs.Length ? paragraphs[other + 1].Start : order.Length;
            for (int position = text.Start; position < end; position++)
            {
                figure.Fragments.Add(order[position]);
            }

            union = MarkGeometry.Union(union, box);
            text.Merged = true;
        }

        figure.Box = union;
        bounds[owner] = union;
    }

    /// <summary>Moves each caption next to its owner (before it when above), keeping everything else in order.</summary>
    private static void Reorder(Analysis analysis)
    {
        Paragraph[] paragraphs = analysis.Paragraphs;
        int[] order = analysis.Order;
        var sequence = new List<int>(paragraphs.Length);
        for (int index = 0; index < paragraphs.Length; index++)
        {
            ref readonly Paragraph paragraph = ref paragraphs[index];
            if (paragraph.Owner >= 0 || paragraph.Merged)
            {
                continue;
            }

            if (paragraph.Caption >= 0 && paragraph.CaptionAbove)
            {
                sequence.Add(paragraph.Caption);
            }

            sequence.Add(index);
            if (paragraph.Caption >= 0 && !paragraph.CaptionAbove)
            {
                sequence.Add(paragraph.Caption);
            }
        }

        var moved = new int[paragraphs.Length];
        for (int index = 0; index < sequence.Count; index++)
        {
            moved[sequence[index]] = index;
        }

        var newOrder = new int[order.Length];
        var newParagraphs = new Paragraph[sequence.Count];
        int next = 0;
        for (int index = 0; index < sequence.Count; index++)
        {
            int old = sequence[index];
            int start = paragraphs[old].Start;
            int end = old + 1 < paragraphs.Length ? paragraphs[old + 1].Start : order.Length;
            Paragraph paragraph = paragraphs[old];
            paragraph.Start = next;
            paragraph.Caption = paragraph.Caption >= 0 ? moved[paragraph.Caption] : -1;
            paragraph.Owner = paragraph.Owner >= 0 ? moved[paragraph.Owner] : -1;
            newParagraphs[index] = paragraph;
            order.AsSpan(start, end - start).CopyTo(newOrder.AsSpan(next));
            next += end - start;
        }

        analysis.Order = newOrder[..next];
        analysis.Paragraphs = newParagraphs;
    }

    /// <summary>Whether text starts with a caption label (<c>Figure 3.</c>, <c>Fig. 2:</c>, <c>TABLE IV</c>, <c>Chart 1 Sales</c>).</summary>
    internal static bool IsCaptionLabel(ReadOnlySpan<char> text)
    {
        text = text.TrimStart();
        int word = 0;
        while (word < text.Length && char.IsAsciiLetter(text[word]))
        {
            word++;
        }

        if (word == 0 || !char.IsUpper(text[0]) || !IsCaptionWord(text[..word]))
        {
            return false;
        }

        ReadOnlySpan<char> rest = text[word..];
        if (!rest.IsEmpty && rest[0] == '.')
        {
            rest = rest[1..];
        }

        rest = rest.TrimStart();
        int label = 0;
        bool digit = false;
        while (label < rest.Length && label < 12 && (char.IsAsciiLetterOrDigit(rest[label]) || rest[label] is '.' or '-'))
        {
            digit |= char.IsAsciiDigit(rest[label]);
            label++;
        }

        ReadOnlySpan<char> token = rest[..label].TrimEnd(".-");
        if (token.IsEmpty || !(digit || IsRoman(token) || (token.Length == 1 && char.IsAsciiLetterUpper(token[0]))))
        {
            return false;
        }

        if (rest[..label].EndsWith('.'))
        {
            return true;
        }

        ReadOnlySpan<char> after = rest[label..];
        if (after.Trim().IsEmpty || after[0] is ':' or '.' or '\u2014' or '\u2013' or '|' or ')')
        {
            return true;
        }

        after = after.TrimStart();
        return char.IsUpper(after[0]) || after[0] is '\u2014' or '\u2013' or ':' or '-' or '(';
    }

    private static bool IsCaptionWord(ReadOnlySpan<char> word)
    {
        foreach (string candidate in CaptionWords)
        {
            if (word.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a fragment's text, spaced as text extraction spaces it, starts with a caption label.</summary>
    private bool StartsWithCaptionLabel(TextPage text, int fragment)
    {
        ref readonly Fragment piece = ref _fragments[fragment];
        if (piece.GlyphEnd <= piece.GlyphStart)
        {
            return false;
        }

        TextLayout layout = TextLayout.Rent();
        try
        {
            layout.Build(text.GlyphSpan[piece.GlyphStart..piece.GlyphEnd], text.Text);
            return IsCaptionLabel(layout.PlainText);
        }
        finally
        {
            TextLayout.Return(layout);
        }
    }

    private static bool IsRoman(ReadOnlySpan<char> token)
    {
        foreach (char character in token)
        {
            if (character is not ('I' or 'V' or 'X' or 'L' or 'C'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The layout node of a detected figure, with its caption.</summary>
    private static LayoutFigure FigureNode(Placement placement, int index)
    {
        FigurePlan figure = placement.Figures![placement.Paragraphs[index].Figure];
        var node = new LayoutFigure(null, figure.Box);
        node.AddExtent(OwnerExtent(placement, index));
        AddCaption(placement, index, node, above: true);
        AddCaption(placement, index, node, above: false);
        return node;
    }

    /// <summary>The extent of a figure or table and its caption, which are consecutive in the rebuilt text.</summary>
    private static LayoutExtent OwnerExtent(Placement placement, int index)
    {
        int caption = placement.Paragraphs[index].Caption;
        return caption < 0 ? Covering(placement, index, index) : Covering(placement, Math.Min(index, caption), Math.Max(index, caption));
    }

    /// <summary>Adds a figure's or table's caption to its node when it is on the given side.</summary>
    private static void AddCaption(Placement placement, int index, LayoutNode node, bool above)
    {
        ref readonly Paragraph paragraph = ref placement.Paragraphs[index];
        if (paragraph.Caption >= 0 && paragraph.CaptionAbove == above)
        {
            LayoutNode caption = LayoutNode.Create(LayoutNodeKind.Caption, null);
            caption.AddExtent(Covering(placement, paragraph.Caption, paragraph.Caption));
            node.Add(caption);
        }
    }

    private static PdfRectangle? Clip(PdfRectangle box, PdfRectangle crop)
    {
        double left = Math.Max(box.Left, crop.Left);
        double bottom = Math.Max(box.Bottom, crop.Bottom);
        double right = Math.Min(box.Right, crop.Right);
        double top = Math.Min(box.Top, crop.Top);
        return right >= left && top >= bottom ? new PdfRectangle(left, bottom, right, top) : null;
    }

    private static double Area(PdfRectangle box) => box.Width * box.Height;

    private static PdfRectangle Grow(PdfRectangle box, double by) => new(box.Left - by, box.Bottom - by, box.Right + by, box.Top + by);

    private static bool Contains(PdfRectangle box, double x, double y) => x >= box.Left && x <= box.Right && y >= box.Bottom && y <= box.Top;

    private static bool Overlaps(PdfRectangle a, PdfRectangle b) => a.Left < b.Right && b.Left < a.Right && a.Bottom < b.Top && b.Bottom < a.Top;

    /// <summary>The share of a box (half a point wider each way, so a line has area) inside another.</summary>
    private static double Inside(PdfRectangle box, PdfRectangle region)
    {
        PdfRectangle widened = Grow(box, 0.5);
        double width = Math.Min(widened.Right, region.Right) - Math.Max(widened.Left, region.Left);
        double height = Math.Min(widened.Top, region.Top) - Math.Max(widened.Bottom, region.Bottom);
        return width <= 0 || height <= 0 ? 0 : width * height / Area(widened);
    }

    /// <summary>A figure found on a page: its box and the fragments of text inside it.</summary>
    private sealed class FigurePlan
    {
        public PdfRectangle Box { get; set; }

        /// <summary>Gets the fragments of text inside the figure.</summary>
        public List<int> Fragments { get; } = [];

        /// <summary>Gets or sets the fragment standing for the figure in the cut.</summary>
        public int Proxy { get; set; }
    }

    /// <summary>Boxes left to right, then by index: a total order.</summary>
    private readonly struct ByBoxLeft(PdfRectangle[] boxes) : IComparer<int>
    {
        public int Compare(int x, int y)
        {
            int order = boxes[x].Left.CompareTo(boxes[y].Left);
            return order != 0 ? order : x.CompareTo(y);
        }
    }
}
