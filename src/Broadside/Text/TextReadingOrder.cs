namespace Broadside.Text;

/// <summary>The order extracted glyphs are given in.</summary>
/// <remarks>ISO 32000-2 §14.8.2.5.1: page content order and logical content order.</remarks>
public enum TextReadingOrder
{
    /// <summary>
    /// Logical content order when the page is tagged: the depth-first order of the structure tree (§14.7.2), the glyphs of each
    /// content item in the order the content shows them. Content of a tagged page that is not in the tree is an artifact
    /// (§14.8.2.2.2). A page whose content holds no content item of the tree keeps page content order.
    /// </summary>
    /// <remarks>ISO 32000-2 §14.8.2.5.1, §14.7.5 and §14.8.2.2.</remarks>
    Structure = 0,

    /// <summary>Page content order: the order the content shows the glyphs in. The structure tree is not consulted.</summary>
    /// <remarks>ISO 32000-2 §14.8.2.5.1.</remarks>
    Content = 1,
}
