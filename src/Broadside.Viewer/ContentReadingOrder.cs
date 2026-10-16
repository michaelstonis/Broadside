using Broadside.Rendering;

namespace Broadside.Viewer;

/// <summary>Selects glyphs in page content order: the order the content streams show them in, every glyph selectable.</summary>
/// <remarks>ISO 32000-2 §14.8.2.5.1 (page content order). Spec #33 viewer capability 4.</remarks>
public sealed class ContentReadingOrder : IReadingOrderProvider
{
    private ContentReadingOrder()
    {
    }

    /// <summary>Gets the provider.</summary>
    public static ContentReadingOrder Instance { get; } = new();

    /// <inheritdoc/>
    public int[] GetReadingOrder(PdfPage page, HitTestIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        int[] order = new int[index.GlyphCount];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        return order;
    }
}
