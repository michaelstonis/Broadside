using System.Buffers;
using System.Runtime.InteropServices;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Text;

namespace Broadside.Layout;

/// <summary>Headings and lists among the detected paragraphs (spec story 36, issue #188).</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.8.4.5 (headings, Table 366) and §14.8.4.8.2 (lists, Table 370; labels, Table 368) are what this part recovers;
/// the numbering it reports is a <c>ListNumbering</c> value (§14.8.5.5, Table 385). A paragraph's style is the size and weight
/// most of its visible glyphs are set in (size: the length of the text rendering matrix's y axis, to the nearest half point; weight:
/// the font's <see cref="FontQuery.IsBold"/>). The body style is the one most glyphs of all the detected pages are set in.
/// </para>
/// <para>
/// A paragraph is a heading when it is short (at most <see cref="HeadingRows"/> rows and <see cref="MaxHeadingGlyphs"/> glyphs), not
/// a form row, has at least two letters, does not end with a colon, is set at least <see cref="DominantShare"/> in one style, and that
/// style is <see cref="HeadingScale"/> times the body size or larger, or bold over a regular body at about its size (then at most
/// <see cref="BoldHeadingRows"/> rows, not ending with a full stop). Heading styles are ranked over the document, largest first and
/// bold before regular at one size: the first is level 1.
/// </para>
/// <para>
/// A row starts a list item when its first word is a marker: a bullet (alone, or glued to the word it marks), or a number of up to
/// three digits, a letter or a roman numeral followed by <c>.</c> or <c>)</c> or enclosed in parentheses. Following rows that start
/// where the item's body does continue it. Items whose markers line up (within <see cref="ListAlign"/>) form a list while their
/// markers are of one kind; an item whose marker sits further right, within <see cref="NestReach"/> of the body of the item above,
/// starts a list nested in that item's body; a paragraph that starts where an open item's body does is more of that body. A single
/// letter is read as a roman numeral when the list's numbers count better that way (<c>i</c>, <c>ii</c>) and as a letter otherwise
/// (<c>a</c>, <c>b</c>, <c>c</c>). A list of one item is kept only for a bullet or when it is nested and numbered from 1; a numbered
/// list whose numbers mostly do not count up is not a list. Headings take precedence over list items (<c>1. Introduction</c>).
/// </para>
/// </remarks>
internal sealed partial class GeometryLayout
{
    /// <summary>How much larger than the body text, at least, a heading's type is when it is not set apart by weight alone.</summary>
    internal const double HeadingScale = 1.15;

    /// <summary>The most rows a heading has.</summary>
    internal const int HeadingRows = 3;

    /// <summary>The most rows a heading set apart by weight alone has.</summary>
    internal const int BoldHeadingRows = 2;

    /// <summary>The most visible glyphs a heading has.</summary>
    internal const int MaxHeadingGlyphs = 200;

    /// <summary>The smallest share of a paragraph's visible glyphs its style covers, for the paragraph to be a heading.</summary>
    internal const double DominantShare = 0.6;

    /// <summary>How far apart, in <c>h</c>, markers (or bodies) of items of one list may start.</summary>
    internal const double ListAlign = 0.5;

    /// <summary>How far, in <c>h</c>, past the body of the item above a nested item's marker may start.</summary>
    internal const double NestReach = 4;

    /// <summary>Characters that mark a list item.</summary>
    private static readonly SearchValues<char> Bullets = SearchValues.Create(
        "•◦▪▫■□●○‣⁃►▸▶➢➤✓✔∙·–—-*");

    /// <summary>Bullets that also mark an item glued to its first word, or alone.</summary>
    private static readonly SearchValues<char> StrongBullets = SearchValues.Create(
        "•◦▪▫■□●○‣⁃►▸▶➢➤✓✔∙");

    private readonly Dictionary<(double Size, bool Bold), int> _styles = [];
    private readonly Dictionary<PdfFont, bool> _bold = new(ReferenceEqualityComparer.Instance);
    private readonly List<(double Size, bool Bold, int Count)> _histogram = [];
    private readonly List<(double Size, bool Bold)> _headingStyles = [];

    private enum MarkerKind : byte
    {
        None,
        Bullet,
        Number,
        Letter,
        Roman,
    }

    /// <summary>Records the list marker a fragment's first word is, if it is one.</summary>
    private static void FindMarker(ref Fragment fragment, in TextWord word, ReadOnlySpan<TextGlyph> glyphs, TextPage text)
    {
        ReadOnlySpan<char> chars = text.Text.AsSpan(word.TextStart, word.TextLength);
        Marker marker = ParseMarker(chars, out bool glued);
        if (marker.Kind == MarkerKind.None)
        {
            return;
        }

        if (!glued)
        {
            fragment.MarkerEnd = word.GlyphStart + word.GlyphCount;
            fragment.MarkerRight = word.Quad.Bounds.Right;
        }
        else if (word.GlyphCount >= 2)
        {
            fragment.MarkerEnd = word.GlyphStart + 1;
            fragment.MarkerRight = glyphs[word.GlyphStart].Quad.Bounds.Right;
            fragment.BodyLeft = glyphs[word.GlyphStart + 1].Quad.Bounds.Left;
        }
        else
        {
            return;
        }

        fragment.Marker = marker;
    }

    /// <summary>Reads a word as a list marker; <paramref name="glued"/> when only its first character, a bullet, is the marker.</summary>
    private static Marker ParseMarker(ReadOnlySpan<char> word, out bool glued)
    {
        glued = false;
        if (word.IsEmpty)
        {
            return default;
        }

        char first = word[0];
        if (Bullets.Contains(first))
        {
            if (word.Length == 1)
            {
                return new Marker(MarkerKind.Bullet, first, false, 0, 0, 0);
            }

            glued = StrongBullets.Contains(first) && char.IsLetterOrDigit(word[1]);
            return glued ? new Marker(MarkerKind.Bullet, first, false, 0, 0, 0) : default;
        }

        ReadOnlySpan<char> token;
        byte style;
        if (word.Length >= 3 && first == '(' && word[^1] == ')')
        {
            token = word[1..^1];
            style = 2;
        }
        else if (word.Length >= 2 && word[^1] is '.' or ')')
        {
            token = word[..^1];
            style = word[^1] == '.' ? (byte)0 : (byte)1;
        }
        else
        {
            return default;
        }

        if (token.Length <= 3 && !token.ContainsAnyExceptInRange('0', '9'))
        {
            return new Marker(MarkerKind.Number, '\0', false, style, int.Parse(token, provider: System.Globalization.CultureInfo.InvariantCulture), 0);
        }

        bool upper = !token.ContainsAnyExceptInRange('A', 'Z');
        if (!upper && token.ContainsAnyExceptInRange('a', 'z'))
        {
            return default;
        }

        int roman = RomanValue(token);
        if (token.Length == 1)
        {
            return new Marker(MarkerKind.Letter, '\0', upper, style, (upper ? token[0] - 'A' : token[0] - 'a') + 1, roman);
        }

        return roman > 0 ? new Marker(MarkerKind.Roman, '\0', upper, style, 0, roman) : default;
    }

    /// <summary>The value of a roman numeral written the usual way (no <c>IIII</c>, no <c>IC</c>), up to 3999; 0 for anything else.</summary>
    private static int RomanValue(ReadOnlySpan<char> token)
    {
        if (token.IsEmpty || token.Length > 15)
        {
            return 0;
        }

        int total = 0;
        int largest = 0;
        for (int index = token.Length - 1; index >= 0; index--)
        {
            int digit = (token[index] | 0x20) switch
            {
                'i' => 1,
                'v' => 5,
                'x' => 10,
                'l' => 50,
                'c' => 100,
                'd' => 500,
                'm' => 1000,
                _ => 0,
            };
            if (digit == 0)
            {
                return 0;
            }

            if (digit < largest)
            {
                total -= digit;
            }
            else
            {
                total += digit;
                largest = digit;
            }
        }

        if (total is <= 0 or > 3999)
        {
            return 0;
        }

        // Only the canonical spelling of the value counts.
        Span<char> canonical = stackalloc char[16];
        int length = 0;
        int rest = total;
        ReadOnlySpan<int> values = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        ReadOnlySpan<string> symbols = ["m", "cm", "d", "cd", "c", "xc", "l", "xl", "x", "ix", "v", "iv", "i"];
        for (int index = 0; index < values.Length; index++)
        {
            while (rest >= values[index])
            {
                symbols[index].AsSpan().CopyTo(canonical[length..]);
                length += symbols[index].Length;
                rest -= values[index];
            }
        }

        return token.Equals(canonical[..length], StringComparison.OrdinalIgnoreCase) ? total : 0;
    }

    /// <summary>The <c>ListNumbering</c> a bullet stands for.</summary>
    private static string BulletNumbering(char bullet) => bullet switch
    {
        '•' or '●' or '∙' or '' or '' => "Disc",
        '◦' or '○' => "Circle",
        '▪' or '■' or '▫' or '□' or '' or '' => "Square",
        _ => "Unordered",
    };

    /// <summary>Whether two markers can number items of one list.</summary>
    private static bool Compatible(in Marker list, in Marker item) => list.Kind switch
    {
        MarkerKind.Bullet => item.Kind == MarkerKind.Bullet && item.Bullet == list.Bullet,
        MarkerKind.Number => item.Kind == MarkerKind.Number && item.Style == list.Style,
        _ => item.Kind is MarkerKind.Letter or MarkerKind.Roman && item.Upper == list.Upper && item.Style == list.Style,
    };

    /// <summary>Saves the page's reading order and paragraphs, with each paragraph's style, and counts the styles into the document's.</summary>
    private void Describe(Analysis analysis)
    {
        Span<Paragraph> paragraphs = CollectionsMarshal.AsSpan(_paragraphs);
        TextPage text = analysis.Text;
        ReadOnlySpan<TextGlyph> glyphs = text.GlyphSpan;
        for (int index = 0; index < paragraphs.Length; index++)
        {
            ref Paragraph paragraph = ref paragraphs[index];
            int end = index + 1 < paragraphs.Length ? paragraphs[index + 1].Start : _order.Count;
            ref readonly Fragment head = ref _fragments[_order[paragraph.Start]];
            paragraph.MarkerLeft = head.Left;
            paragraph.Marker = head.Marker;
            paragraph.Left = double.PositiveInfinity;
            _histogram.Clear();
            for (int position = paragraph.Start; position < end; position++)
            {
                ref readonly Fragment fragment = ref _fragments[_order[position]];
                paragraph.Left = Math.Min(paragraph.Left, fragment.Left);
                for (int glyph = fragment.GlyphStart; glyph < fragment.GlyphEnd; glyph++)
                {
                    bool visible = false;
                    foreach (char character in text.GetGlyphText(glyphs[glyph]))
                    {
                        if (!char.IsWhiteSpace(character))
                        {
                            visible = true;
                            paragraph.Last = character;
                            paragraph.Letters += char.IsLetter(character) ? 1 : 0;
                        }
                    }

                    if (visible)
                    {
                        Matrix matrix = glyphs[glyph].TextRenderingMatrix;
                        double size = Math.Round(Math.Sqrt((matrix.C * matrix.C) + (matrix.D * matrix.D)) * 2, MidpointRounding.AwayFromZero) / 2;
                        Count(size, IsBold(glyphs[glyph].Font));
                        paragraph.Glyphs++;
                    }
                }
            }

            (double Size, bool Bold, int Count) dominant = default;
            foreach ((double size, bool bold, int count) in _histogram)
            {
                ref int total = ref CollectionsMarshal.GetValueRefOrAddDefault(_styles, (size, bold), out _);
                total += count;
                if (count > dominant.Count || (count == dominant.Count && (size > dominant.Size || (size == dominant.Size && bold))))
                {
                    dominant = (size, bold, count);
                }
            }

            paragraph.Size = dominant.Size;
            paragraph.Bold = dominant.Bold;
            paragraph.Share = paragraph.Glyphs == 0 ? 0 : (double)dominant.Count / paragraph.Glyphs;
        }

        analysis.Order = [.. _order];
        analysis.Paragraphs = paragraphs.ToArray();
    }

    private void Count(double size, bool bold)
    {
        Span<(double Size, bool Bold, int Count)> histogram = CollectionsMarshal.AsSpan(_histogram);
        for (int index = 0; index < histogram.Length; index++)
        {
            if (histogram[index].Size == size && histogram[index].Bold == bold)
            {
                histogram[index].Count++;
                return;
            }
        }

        _histogram.Add((size, bold, 1));
    }

    /// <summary>Whether a font is bold, asked once per font.</summary>
    private bool IsBold(PdfFont font)
    {
        if (!_bold.TryGetValue(font, out bool bold))
        {
            PdfFontDescriptor? descriptor = font.Descriptor;
            bold = new FontQuery(font.BaseFont is { Length: > 0 } name ? name : "Unnamed")
            {
                Flags = descriptor?.Flags ?? PdfFontFlags.None,
                FontWeight = descriptor?.FontWeight,
                StemV = descriptor?.StemV ?? 0,
            }.IsBold;
            _bold.Add(font, bold);
        }

        return bold;
    }

    /// <summary>Finds the body style over every detected page, then the headings and their levels.</summary>
    private void Classify()
    {
        (double Size, bool Bold) body = default;
        int most = -1;
        foreach (((double size, bool bold), int count) in _styles)
        {
            if (count > most || (count == most && (size < body.Size || (size == body.Size && !bold))))
            {
                body = (size, bold);
                most = count;
            }
        }

        _headingStyles.Clear();
        foreach (Analysis analysis in _pages)
        {
            foreach (ref readonly Paragraph paragraph in analysis.Paragraphs.AsSpan())
            {
                if (IsHeading(paragraph, body) && !_headingStyles.Contains((paragraph.Size, paragraph.Bold)))
                {
                    _headingStyles.Add((paragraph.Size, paragraph.Bold));
                }
            }
        }

        // Larger first; at one size, bold first.
        _headingStyles.Sort(static (x, y) => x.Size != y.Size ? y.Size.CompareTo(x.Size) : y.Bold.CompareTo(x.Bold));
        foreach (Analysis analysis in _pages)
        {
            foreach (ref Paragraph paragraph in analysis.Paragraphs.AsSpan())
            {
                paragraph.Level = IsHeading(paragraph, body) ? _headingStyles.IndexOf((paragraph.Size, paragraph.Bold)) + 1 : 0;
            }
        }
    }

    private static bool IsHeading(in Paragraph paragraph, (double Size, bool Bold) body)
    {
        if (paragraph.Tabular || paragraph.Letters < 2 || paragraph.Last == ':' || paragraph.Share < DominantShare || paragraph.Glyphs > MaxHeadingGlyphs || body.Size <= 0)
        {
            return false;
        }

        if (paragraph.Size >= body.Size * HeadingScale)
        {
            return paragraph.Rows <= HeadingRows;
        }

        return paragraph.Bold && !body.Bold && paragraph.Size >= body.Size * 0.95 && paragraph.Rows <= BoldHeadingRows && paragraph.Last != '.';
    }

    /// <summary>Adds the page's paragraphs to <paramref name="root"/> as headings, paragraphs and lists.</summary>
    private static void AddNodes(Placement placement, LayoutNode root)
    {
        Paragraph[] paragraphs = placement.Paragraphs;
        double align = ListAlign * placement.Height;
        var entries = new List<Entry>();
        var open = new List<ListPlan>();
        for (int index = 0; index < paragraphs.Length; index++)
        {
            ref readonly Paragraph paragraph = ref paragraphs[index];
            if (paragraph.IsItem)
            {
                // Close the lists this item is not in: those it neither lines up with nor nests under.
                while (open.Count > 0)
                {
                    ItemPlan last = open[^1].Items[^1];
                    if (Aligned(last, paragraph, align)
                        || (paragraph.MarkerLeft > last.MarkerLeft + align && paragraph.MarkerLeft <= last.BodyLeft + (NestReach * placement.Height)))
                    {
                        break;
                    }

                    open.RemoveAt(open.Count - 1);
                }

                var item = new ItemPlan(index, paragraph.Marker, paragraph.MarkerLeft, paragraph.Body);
                if (open.Count > 0 && Aligned(open[^1].Items[^1], paragraph, align) && Compatible(open[^1].Items[0].Marker, paragraph.Marker))
                {
                    open[^1].Items.Add(item);
                }
                else
                {
                    if (open.Count > 0 && Aligned(open[^1].Items[^1], paragraph, align))
                    {
                        // Same indentation, another kind of marker: a new list beside it.
                        open.RemoveAt(open.Count - 1);
                    }

                    var list = new ListPlan();
                    list.Items.Add(item);
                    (open.Count > 0 ? open[^1].Items[^1].Contents : entries).Add(new Entry(-1, list));
                    open.Add(list);
                }

                foreach (ListPlan list in open)
                {
                    list.Items[^1].Last = index;
                }

                continue;
            }

            if (paragraph.Level == 0)
            {
                // A paragraph that starts where an open item's body does is more of that body.
                while (open.Count > 0 && Math.Abs(paragraph.Left - open[^1].Items[^1].BodyLeft) > align)
                {
                    open.RemoveAt(open.Count - 1);
                }

                if (open.Count > 0)
                {
                    open[^1].Items[^1].Contents.Add(new Entry(index, null));
                    foreach (ListPlan list in open)
                    {
                        list.Items[^1].Last = index;
                    }

                    continue;
                }
            }

            open.Clear();
            entries.Add(new Entry(index, null));
        }

        Add(placement, entries, root, nested: false);
    }

    private static bool Aligned(ItemPlan item, in Paragraph paragraph, double align) =>
        Math.Abs(item.MarkerLeft - paragraph.MarkerLeft) <= align || Math.Abs(item.BodyLeft - paragraph.Body) <= align;

    private static void Add(Placement placement, List<Entry> entries, LayoutNode parent, bool nested)
    {
        foreach (Entry entry in entries)
        {
            if (entry.List is { } list)
            {
                AddList(placement, list, parent, nested);
            }
            else
            {
                parent.Add(Node(placement, entry.Paragraph));
            }
        }
    }

    private static LayoutNode Node(Placement placement, int index)
    {
        int level = placement.Paragraphs[index].Level;
        LayoutNode node = level > 0 ? new LayoutHeading(null, level) : LayoutNode.Create(LayoutNodeKind.Paragraph, null);
        node.AddExtent(Covering(placement, index, index));
        return node;
    }

    /// <summary>Adds a list, or, when it is not one after all, its items and their contents as paragraphs.</summary>
    private static void AddList(Placement placement, ListPlan list, LayoutNode parent, bool nested)
    {
        if (Numbering(list, nested) is not { } numbering)
        {
            foreach (ItemPlan item in list.Items)
            {
                parent.Add(Node(placement, item.Paragraph));
                Add(placement, item.Contents, parent, nested);
            }

            return;
        }

        var node = new LayoutList(null, numbering);
        node.AddExtent(Covering(placement, list.Items[0].Paragraph, list.Items[^1].Last));
        parent.Add(node);
        foreach (ItemPlan item in list.Items)
        {
            LayoutNode itemNode = LayoutNode.Create(LayoutNodeKind.ListItem, null);
            LayoutExtent whole = Covering(placement, item.Paragraph, item.Last);
            itemNode.AddExtent(whole);
            ref readonly Placed head = ref placement.Placed[item.Paragraph];
            LayoutNode label = LayoutNode.Create(LayoutNodeKind.Label, null);
            label.AddExtent(new LayoutExtent
            {
                Page = placement.Page,
                Bounds = head.Label,
                GlyphStart = head.GlyphStart,
                GlyphCount = head.LabelGlyphs,
                TextStart = head.TextStart,
                TextLength = head.LabelText,
            });
            PdfRectangle bounds = head.Body;
            for (int index = item.Paragraph + 1; index <= item.Last; index++)
            {
                bounds = MarkGeometry.Union(bounds, placement.Placed[index].Bounds);
            }

            LayoutNode body = LayoutNode.Create(LayoutNodeKind.ListBody, null);
            body.AddExtent(whole with
            {
                Bounds = bounds,
                GlyphStart = whole.GlyphStart + head.LabelGlyphs,
                GlyphCount = whole.GlyphCount - head.LabelGlyphs,
                TextStart = whole.TextStart + head.LabelText,
                TextLength = whole.TextLength - head.LabelText,
            });
            foreach (Entry entry in item.Contents)
            {
                if (entry.List is { } nestedList)
                {
                    AddList(placement, nestedList, body, nested: true);
                }
            }

            itemNode.Add(label);
            itemNode.Add(body);
            node.Add(itemNode);
        }
    }

    /// <summary>The list's <c>ListNumbering</c>, or <see langword="null"/> when its markers do not make a list.</summary>
    private static string? Numbering(ListPlan list, bool nested)
    {
        List<ItemPlan> items = list.Items;
        Marker first = items[0].Marker;
        if (first.Kind == MarkerKind.Bullet)
        {
            return items.Count >= 2 || nested || StrongBullets.Contains(first.Bullet) ? BulletNumbering(first.Bullet) : null;
        }

        bool roman = false;
        if (first.Kind != MarkerKind.Number)
        {
            bool letters = items.TrueForAll(static item => item.Marker.Kind == MarkerKind.Letter);
            bool romans = items.TrueForAll(static item => item.Marker.Roman > 0);
            int letterSteps = Steps(items, roman: false);
            int romanSteps = Steps(items, roman: true);
            roman = !letters || (romans && (romanSteps > letterSteps || (romanSteps == letterSteps && first.Roman == 1)));
        }

        int steps = Steps(items, roman);
        bool counts = items.Count >= 2 ? steps * 2 >= items.Count - 1 : nested && (roman ? first.Roman : first.Value) == 1;
        return !counts ? null
            : first.Kind == MarkerKind.Number ? "Decimal"
            : roman ? (first.Upper ? "UpperRoman" : "LowerRoman")
            : first.Upper ? "UpperAlpha" : "LowerAlpha";
    }

    /// <summary>How many items are numbered one more than the item before.</summary>
    private static int Steps(List<ItemPlan> items, bool roman)
    {
        int steps = 0;
        for (int index = 1; index < items.Count; index++)
        {
            int previous = roman ? items[index - 1].Marker.Roman : items[index - 1].Marker.Value;
            int current = roman ? items[index].Marker.Roman : items[index].Marker.Value;
            steps += current == previous + 1 ? 1 : 0;
        }

        return steps;
    }

    /// <summary>The extent of paragraphs <paramref name="first"/> to <paramref name="last"/>, consecutive in the rebuilt text.</summary>
    private static LayoutExtent Covering(Placement placement, int first, int last)
    {
        Placed[] placed = placement.Placed;
        PdfRectangle bounds = placed[first].Bounds;
        for (int index = first + 1; index <= last; index++)
        {
            bounds = MarkGeometry.Union(bounds, placed[index].Bounds);
        }

        return new LayoutExtent
        {
            Page = placement.Page,
            Bounds = bounds,
            GlyphStart = placed[first].GlyphStart,
            GlyphCount = placed[last].GlyphStart + placed[last].GlyphCount - placed[first].GlyphStart,
            TextStart = placed[first].TextStart,
            TextLength = placed[last].TextStart + placed[last].TextLength - placed[first].TextStart,
        };
    }

    /// <summary>A list marker: its kind, the bullet, the case of a letter or numeral, how it is written (0 <c>1.</c>, 1 <c>1)</c>, 2 <c>(1)</c>), its number or letter's place, and its value as a roman numeral (0 when it is not one).</summary>
    private readonly record struct Marker(MarkerKind Kind, char Bullet, bool Upper, byte Style, int Value, int Roman);

    /// <summary>A paragraph of a page: where it starts in the reading order, and what it looks like.</summary>
    private struct Paragraph(int start)
    {
        /// <summary>The paragraph's first fragment's position in the reading order.</summary>
        public int Start = start;

        public int Rows;

        /// <summary>Whether a row of the paragraph has several fragments (a row of a form or table).</summary>
        public bool Tabular;

        /// <summary>Where the body of the list item the paragraph starts begins; NaN when it starts none.</summary>
        public double Body = double.NaN;

        /// <summary>The leftmost edge of the paragraph's fragments.</summary>
        public double Left;

        /// <summary>The left edge of the paragraph's first fragment, where a list item's marker is.</summary>
        public double MarkerLeft;

        public Marker Marker;

        /// <summary>The size of the style most of the paragraph's visible glyphs are set in.</summary>
        public double Size;

        public bool Bold;

        /// <summary>The share of the paragraph's visible glyphs set in its style.</summary>
        public double Share;

        public int Letters;

        public int Glyphs;

        /// <summary>The last visible character.</summary>
        public char Last;

        /// <summary>The heading level; 0 when the paragraph is not a heading.</summary>
        public int Level;

        /// <summary>Gets a value indicating whether the paragraph is a list item: it starts with a marker and is not a heading.</summary>
        public readonly bool IsItem => !double.IsNaN(Body) && Level == 0;
    }

    /// <summary>Where a paragraph went in the rebuilt text, its bounds, and those of its label and body when it is a list item.</summary>
    private struct Placed
    {
        public int GlyphStart;
        public int GlyphCount;
        public int TextStart;
        public int TextLength;
        public PdfRectangle Bounds;
        public PdfRectangle Body;
        public PdfRectangle Label;
        public int LabelGlyphs;
        public int LabelText;
    }

    /// <summary>A page's rebuilt text and its paragraphs, which nodes are made from.</summary>
    private readonly record struct Placement(LayoutPage Page, Paragraph[] Paragraphs, Placed[] Placed, double Height);

    /// <summary>A paragraph (its index) or a list, in the order they are read.</summary>
    private readonly record struct Entry(int Paragraph, ListPlan? List);

    /// <summary>A list being gathered: its items.</summary>
    private sealed class ListPlan
    {
        public List<ItemPlan> Items { get; } = [];
    }

    /// <summary>A list item being gathered: its paragraph, the last paragraph it covers, and the paragraphs and lists its body holds after the first.</summary>
    private sealed class ItemPlan(int paragraph, Marker marker, double markerLeft, double bodyLeft)
    {
        public int Paragraph { get; } = paragraph;

        public Marker Marker { get; } = marker;

        public double MarkerLeft { get; } = markerLeft;

        public double BodyLeft { get; } = bodyLeft;

        public int Last { get; set; } = paragraph;

        public List<Entry> Contents { get; } = [];
    }
}
