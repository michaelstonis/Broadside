using System.Globalization;

namespace Broadside.Viewer.WinUI;

/// <summary>What the automation peers expose of the accessibility bridge's nodes: names and the reading order.</summary>
/// <remarks>ISO 32000-2 §14.8 (tagged PDF: logical structure order), §14.9.3 (alternate descriptions).</remarks>
internal static class AccessibleText
{
    /// <summary>
    /// A node's accessible name: "Page n" for a page, otherwise its alternate description or its text (a link's, a figure's, a
    /// field's name when it has one). UI Automation conveys the role through the control type, so the name carries no role words.
    /// </summary>
    public static string GetName(AccessibleNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Role == AccessibleRole.Page
            ? string.Create(CultureInfo.InvariantCulture, $"Page {node.PageIndex + 1}")
            : (node.Name ?? node.Text).Trim();
    }

    /// <summary>A page's nodes depth first, in the bridge's reading order (the order UI Automation walks the peers).</summary>
    public static IEnumerable<AccessibleNode> ReadingOrder(AccessibleNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        foreach (AccessibleNode child in node.Children)
        {
            yield return child;
            foreach (AccessibleNode descendant in ReadingOrder(child))
            {
                yield return descendant;
            }
        }
    }
}
