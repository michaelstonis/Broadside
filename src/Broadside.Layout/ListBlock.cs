using System.Globalization;
using System.Text;

namespace Broadside.Layout.Flow;

/// <summary>How a list numbers its items.</summary>
/// <remarks>ISO 32000-2 §14.8.5.5 (the <c>ListNumbering</c> attribute of list elements: <c>Disc</c>, <c>Decimal</c>, <c>UpperRoman</c>, <c>LowerRoman</c>, <c>UpperAlpha</c>, <c>LowerAlpha</c>).</remarks>
public enum ListNumbering
{
    /// <summary>Every item gets the same marker (<see cref="ListStyle.Bullet"/>).</summary>
    Bullet,

    /// <summary>1, 2, 3, …</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "The ListNumbering value of ISO 32000-2 Table 382.")]
    Decimal,

    /// <summary>a, b, … z, aa, ab, …</summary>
    LowerAlpha,

    /// <summary>A, B, … Z, AA, AB, …</summary>
    UpperAlpha,

    /// <summary>i, ii, iii, iv, … (decimal past 3999).</summary>
    LowerRoman,

    /// <summary>I, II, III, IV, … (decimal past 3999).</summary>
    UpperRoman,
}

/// <summary>
/// The look of a list: numbering, marker text and style, and the indent of the items' content. Immutable; the <c>With*</c> methods
/// return a changed copy.
/// </summary>
/// <remarks>ISO 32000-2 §14.8.4.8.2 (list elements <c>L</c>, <c>LI</c>, <c>Lbl</c>, <c>LBody</c>) and §14.8.5.5 (<c>ListNumbering</c>).</remarks>
public sealed class ListStyle
{
    /// <summary>Creates a bulleted style whose markers are drawn in <paramref name="markerStyle"/>.</summary>
    /// <param name="markerStyle">The style of the markers.</param>
    /// <exception cref="ArgumentNullException"><paramref name="markerStyle"/> is null.</exception>
    public ListStyle(TextStyle markerStyle)
    {
        ArgumentNullException.ThrowIfNull(markerStyle);
        MarkerStyle = markerStyle;
    }

    private ListStyle(ListStyle source)
    {
        MarkerStyle = source.MarkerStyle;
        Numbering = source.Numbering;
        Bullet = source.Bullet;
        Prefix = source.Prefix;
        Suffix = source.Suffix;
        StartAt = source.StartAt;
        Indent = source.Indent;
        MarkerGap = source.MarkerGap;
    }

    /// <summary>The style of the markers.</summary>
    public TextStyle MarkerStyle { get; private init; }

    /// <summary>The numbering; <see cref="ListNumbering.Bullet"/> by default.</summary>
    public ListNumbering Numbering { get; private init; }

    /// <summary>The marker of a bulleted list; U+2022 BULLET by default.</summary>
    public string Bullet { get; private init; } = "•";

    /// <summary>The text before a number; empty by default.</summary>
    public string Prefix { get; private init; } = string.Empty;

    /// <summary>The text after a number; <c>.</c> by default.</summary>
    public string Suffix { get; private init; } = ".";

    /// <summary>The number of the first item; 1 by default.</summary>
    public int StartAt { get; private init; } = 1;

    /// <summary>How far the items' content is indented from the list's left edge, in points; 18 by default. Markers sit in this space.</summary>
    public double Indent { get; private init; } = 18;

    /// <summary>The space between a marker's right edge and the item's content, in points; 6 by default.</summary>
    public double MarkerGap { get; private init; } = 6;

    /// <summary>Returns a copy with markers in another style.</summary>
    /// <param name="markerStyle">The style.</param>
    /// <returns>The changed style.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="markerStyle"/> is null.</exception>
    public ListStyle WithMarkerStyle(TextStyle markerStyle)
    {
        ArgumentNullException.ThrowIfNull(markerStyle);
        return new(this) { MarkerStyle = markerStyle };
    }

    /// <summary>Returns a copy with another numbering.</summary>
    /// <param name="numbering">The numbering.</param>
    /// <returns>The changed style.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="numbering"/> is not defined.</exception>
    public ListStyle WithNumbering(ListNumbering numbering) =>
        Enum.IsDefined(numbering) ? new(this) { Numbering = numbering } : throw new ArgumentOutOfRangeException(nameof(numbering), numbering, "Unknown numbering.");

    /// <summary>Returns a copy with another bullet (a custom marker shown for every item of a bulleted list).</summary>
    /// <param name="bullet">The marker text.</param>
    /// <returns>The changed style.</returns>
    /// <exception cref="ArgumentException"><paramref name="bullet"/> is null or empty.</exception>
    public ListStyle WithBullet(string bullet)
    {
        ArgumentException.ThrowIfNullOrEmpty(bullet);
        return new(this) { Bullet = bullet };
    }

    /// <summary>Returns a copy with another text before numbers.</summary>
    /// <param name="prefix">The text.</param>
    /// <returns>The changed style.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="prefix"/> is null.</exception>
    public ListStyle WithPrefix(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        return new(this) { Prefix = prefix };
    }

    /// <summary>Returns a copy with another text after numbers.</summary>
    /// <param name="suffix">The text.</param>
    /// <returns>The changed style.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="suffix"/> is null.</exception>
    public ListStyle WithSuffix(string suffix)
    {
        ArgumentNullException.ThrowIfNull(suffix);
        return new(this) { Suffix = suffix };
    }

    /// <summary>Returns a copy whose first item has another number.</summary>
    /// <param name="startAt">The number, at least 1.</param>
    /// <returns>The changed style.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="startAt"/> is less than 1.</exception>
    public ListStyle WithStartAt(int startAt) =>
        startAt >= 1 ? new(this) { StartAt = startAt } : throw new ArgumentOutOfRangeException(nameof(startAt), startAt, "The first number shall be at least 1.");

    /// <summary>Returns a copy with another content indent.</summary>
    /// <param name="indent">The indent in points.</param>
    /// <returns>The changed style.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="indent"/> is negative or not finite.</exception>
    public ListStyle WithIndent(double indent) => new(this) { Indent = ImageBlock.NotNegative(indent, nameof(indent)) };

    /// <summary>Returns a copy with another gap between markers and content.</summary>
    /// <param name="gap">The gap in points.</param>
    /// <returns>The changed style.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gap"/> is negative or not finite.</exception>
    public ListStyle WithMarkerGap(double gap) => new(this) { MarkerGap = ImageBlock.NotNegative(gap, nameof(gap)) };

    /// <summary>The marker of the item numbered <paramref name="number"/>: the bullet, or the prefix, number and suffix.</summary>
    /// <param name="number">The item's number (<see cref="StartAt"/> for the first item).</param>
    /// <returns>The marker text.</returns>
    public string FormatMarker(int number) => Numbering switch
    {
        ListNumbering.Bullet => Bullet,
        ListNumbering.Decimal => Prefix + number.ToString(CultureInfo.InvariantCulture) + Suffix,
        ListNumbering.LowerAlpha => Prefix + Alphabetic(number, 'a') + Suffix,
        ListNumbering.UpperAlpha => Prefix + Alphabetic(number, 'A') + Suffix,
        ListNumbering.LowerRoman => Prefix + Roman(number).ToLowerInvariant() + Suffix,
        _ => Prefix + Roman(number) + Suffix,
    };

    private static string Alphabetic(int number, char first)
    {
        if (number < 1)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        var builder = new StringBuilder();
        for (int value = number; value > 0; value = (value - 1) / 26)
        {
            builder.Insert(0, (char)(first + ((value - 1) % 26)));
        }

        return builder.ToString();
    }

    private static string Roman(int number)
    {
        if (number is < 1 or > 3999)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        ReadOnlySpan<int> values = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        string[] symbols = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];
        var builder = new StringBuilder();
        int rest = number;
        for (int index = 0; index < values.Length; index++)
        {
            while (rest >= values[index])
            {
                builder.Append(symbols[index]);
                rest -= values[index];
            }
        }

        return builder.ToString();
    }
}

/// <summary>One item of a <see cref="ListBlock"/>: blocks (paragraphs, images, nested lists) laid out beside one marker.</summary>
/// <remarks>ISO 32000-2 §14.8.4.8.2: tagged as <c>LI</c> holding the marker as <c>Lbl</c> and the blocks in <c>LBody</c>.</remarks>
public sealed class ListItem
{
    /// <summary>Creates an item holding <paramref name="blocks"/>.</summary>
    /// <param name="blocks">The blocks, in order; a nested <see cref="ListBlock"/> nests a list.</param>
    /// <exception cref="ArgumentNullException"><paramref name="blocks"/> or one of them is null.</exception>
    public ListItem(params IEnumerable<FlowBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        FlowBlock[] array = [.. blocks];
        foreach (FlowBlock block in array)
        {
            ArgumentNullException.ThrowIfNull(block, nameof(blocks));
        }

        Blocks = array;
    }

    /// <summary>Creates an item holding one paragraph.</summary>
    /// <param name="text">The paragraph's text.</param>
    /// <param name="style">The paragraph's style.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="style"/> is null.</exception>
    public ListItem(string text, ParagraphStyle style)
        : this(new Paragraph(text, style))
    {
    }

    /// <summary>The blocks, in order.</summary>
    public IReadOnlyList<FlowBlock> Blocks { get; }

    /// <summary>A marker for this item instead of the list style's, or null.</summary>
    public string? Marker { get; init; }
}

/// <summary>
/// A bulleted or numbered list. Each item's blocks are indented by <see cref="ListStyle.Indent"/> from the list's left edge, and its
/// marker is drawn right-aligned in that indent on the baseline of the item's first line. Lists nest through items holding lists;
/// items break across pages like their blocks.
/// </summary>
/// <remarks>ISO 32000-2 §14.8.4.8.2 (list elements: the block is <c>L</c>; items are <c>LI</c> with <c>Lbl</c> and <c>LBody</c>) and §14.8.5.5 (<c>ListNumbering</c>).</remarks>
public sealed class ListBlock : FlowBlock
{
    /// <summary>The deepest nesting of lists and items accepted.</summary>
    private const int MaxDepth = 64;

    private readonly List<ListItem> _items = [];

    /// <summary>Creates an empty list with structure type <c>L</c>.</summary>
    /// <param name="style">The list's style.</param>
    /// <exception cref="ArgumentNullException"><paramref name="style"/> is null.</exception>
    public ListBlock(ListStyle style)
        : base("L")
    {
        ArgumentNullException.ThrowIfNull(style);
        Style = style;
    }

    /// <summary>The style.</summary>
    public ListStyle Style { get; }

    /// <summary>The items, in order.</summary>
    public IReadOnlyList<ListItem> Items => _items;

    /// <summary>Appends an item.</summary>
    /// <param name="item">The item.</param>
    /// <returns>This list, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public ListBlock Add(ListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
        return this;
    }

    internal override void Validate(int index) => Validate(index, 0);

    internal override void LayOut(FlowContext context)
    {
        context.BeginBlock(this);
        for (int position = 0; position < _items.Count; position++)
        {
            ListItem item = _items[position];
            context.BeginPart(new FlowPart("LI", item, null));
            context.PushIndent(Style.Indent);
            context.AddMarker(new ListMarker(item, MarkerOf(item, position), Style.MarkerStyle, context.Indent, Style.MarkerGap));
            context.BeginPart(new FlowPart("LBody", item, null));
            foreach (FlowBlock block in item.Blocks)
            {
                block.LayOut(context);
            }

            if (context.HasPendingMarkers)
            {
                // Nothing in the item drew a line: the marker takes a line of its own.
                TextStyle marker = Style.MarkerStyle;
                double advance = marker.FontSize * 1.2;
                context.EnsureRoom(advance);
                context.PlaceMarkers(context.CursorY - ((advance - (marker.Ascent - marker.Descent)) / 2) - marker.Ascent);
                context.Advance(advance);
            }

            context.EndPart();
            context.PopIndent();
            context.EndPart();
        }

        context.EndBlock();
    }

    private string MarkerOf(ListItem item, int position) => item.Marker ?? Style.FormatMarker(Style.StartAt + position);

    private void Validate(int index, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new ArgumentException($"The list of block {index} nests more than {MaxDepth} levels deep (or contains itself).");
        }

        for (int position = 0; position < _items.Count; position++)
        {
            ListItem item = _items[position];
            string marker = MarkerOf(item, position);
            if (!Style.MarkerStyle.Font.CanEncode(marker))
            {
                throw new ArgumentException(
                    $"The marker '{marker}' of item {position} of the list of block {index} has a character the font {Style.MarkerStyle.Font.Name} cannot encode.");
            }

            foreach (FlowBlock block in item.Blocks)
            {
                if (block is ListBlock nested)
                {
                    nested.Validate(index, depth + 1);
                }
                else
                {
                    block.Validate(index);
                }
            }
        }
    }
}
