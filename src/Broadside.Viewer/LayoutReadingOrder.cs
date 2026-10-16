using Broadside.Layout;
using Broadside.Rendering;
using Broadside.Text;

namespace Broadside.Viewer;

/// <summary>
/// Selects glyphs in the layout tree's reading order (<see cref="PdfPage.ExtractLayout(LayoutOptions)"/>): logical structure order
/// on a tagged page, detected columns and paragraphs on an untagged one. Glyphs the layout does not place (artifacts of a tagged
/// page, text of annotation appearances) follow in content order.
/// </summary>
/// <remarks>ISO 32000-2 §14.8.2.5 (logical content order), §14.7 (structure). Spec #33 viewer capability 4.</remarks>
public sealed class LayoutReadingOrder : IReadingOrderProvider
{
    private readonly LayoutOptions _options;

    /// <summary>Creates a provider with the default layout options.</summary>
    public LayoutReadingOrder()
        : this(new LayoutOptions())
    {
    }

    /// <summary>Creates a provider.</summary>
    /// <param name="options">The layout options, for example <see cref="LayoutOptions.Detection"/>.</param>
    public LayoutReadingOrder(LayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>Gets a provider with the default layout options.</summary>
    public static LayoutReadingOrder Default { get; } = new();

    /// <inheritdoc/>
    public int[] GetReadingOrder(PdfPage page, HitTestIndex index)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(index);
        LayoutTree tree = page.ExtractLayout(_options);
        if (tree.Pages.Count == 0)
        {
            return ContentReadingOrder.Instance.GetReadingOrder(page, index);
        }

        TextPage text = tree.Pages[0].Text;
        int[] matches = GlyphMatching.Match(index, text);
        int[] order = new int[index.GlyphCount];
        bool[] placed = new bool[index.GlyphCount];
        int count = 0;
        foreach (int glyph in matches)
        {
            if (glyph >= 0 && !placed[glyph])
            {
                placed[glyph] = true;
                order[count++] = glyph;
            }
        }

        for (int glyph = 0; glyph < placed.Length; glyph++)
        {
            if (!placed[glyph])
            {
                order[count++] = glyph;
            }
        }

        return order;
    }
}
