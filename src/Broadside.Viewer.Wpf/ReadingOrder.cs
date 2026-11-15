using System.Globalization;
using System.Text;

namespace Broadside.Viewer.Wpf;

/// <summary>What the automation peers say about the <see cref="AccessibilityBridge"/>'s nodes: their text in reading order and their descriptions.</summary>
/// <remarks>ISO 32000-2 §14.8 (logical structure order is the reading order of a tagged page), §14.9.3 (alternate descriptions).</remarks>
internal static class ReadingOrder
{
    /// <summary>
    /// The text of a node's subtree in reading order: each leaf's text (its alternate description when it has no text, as a figure),
    /// one leaf per line. The bridge's children are already in reading order (structure order on tagged pages, the detected order otherwise).
    /// </summary>
    public static string TextOf(AccessibleNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var text = new StringBuilder();
        Append(node, text);
        return text.ToString();
    }

    private static void Append(AccessibleNode node, StringBuilder text)
    {
        if (node.Children.Count > 0)
        {
            foreach (AccessibleNode child in node.Children)
            {
                Append(child, text);
            }

            return;
        }

        string leaf = (node.Text.Length > 0 ? node.Text : node.Name ?? string.Empty).Trim();
        if (leaf.Length > 0)
        {
            if (text.Length > 0)
            {
                text.Append('\n');
            }

            text.Append(leaf);
        }
    }

    /// <summary>A node's accessible name: "Page n" for a page, otherwise its name (alternate description) or its text.</summary>
    public static string NameOf(AccessibleNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Role == AccessibleRole.Page
            ? string.Create(CultureInfo.InvariantCulture, $"Page {node.PageIndex + 1}")
            : (node.Name ?? node.Text).Trim();
    }

    /// <summary>A node's states in words (checked, read only, required, unavailable), comma-separated; empty when it has none.</summary>
    public static string StatusOf(AccessibleStates states)
    {
        var words = new List<string>(4);
        if ((states & AccessibleStates.Checked) != 0)
        {
            words.Add("checked");
        }

        if ((states & AccessibleStates.ReadOnly) != 0)
        {
            words.Add("read only");
        }

        if ((states & AccessibleStates.Required) != 0)
        {
            words.Add("required");
        }

        if ((states & AccessibleStates.Disabled) != 0)
        {
            words.Add("unavailable");
        }

        return string.Join(", ", words);
    }
}
