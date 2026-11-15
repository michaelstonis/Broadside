using System.Globalization;

namespace Broadside.Viewer.Uno;

/// <summary>
/// What the automation peers say for the <see cref="AccessibilityBridge"/>'s nodes: each node's spoken name (with its role word and
/// state where the control type alone does not carry it) and a page's text in reading order.
/// </summary>
/// <remarks>ISO 32000-2 §14.8.2.5 (reading order), §14.9.3 (alternate descriptions), §14.9.4 (replacement text).</remarks>
internal static class AccessibleText
{
    /// <summary>The node's spoken name: its name or text, its role word for links, figures and controls, and a check box's state.</summary>
    public static string Describe(AccessibilityBridge bridge, AccessibleNode node)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(node);
        string name = (node.Name ?? node.Text).Trim();
        AccessibleStates states = bridge.GetStates(node);
        string state = (states & AccessibleStates.Checked) != 0 ? ", checked" : string.Empty;
        return node.Role switch
        {
            AccessibleRole.Page => name.Length == 0 ? string.Create(CultureInfo.InvariantCulture, $"Page {node.PageIndex + 1}") : name,
            AccessibleRole.Figure => name.Length == 0 ? "figure" : name,
            AccessibleRole.CheckBox => name + (state.Length > 0 ? state : ", not checked"),
            AccessibleRole.RadioButton => name + (state.Length > 0 ? state : ", not selected"),
            AccessibleRole.Cell or AccessibleRole.ColumnHeader or AccessibleRole.RowHeader when node.Row >= 0 && node.Column >= 0 =>
                Join(name, string.Create(CultureInfo.InvariantCulture, $"row {node.Row + 1}, column {node.Column + 1}")),
            _ => name,
        };

        static string Join(string a, string b) => a.Length == 0 ? b : b.Length == 0 ? a : a + ", " + b;
    }

    /// <summary>The text of a page's readable nodes in reading order (depth first, the order a screen reader walks the peers).</summary>
    public static List<string> ReadingOrder(AccessibilityBridge bridge, int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        var text = new List<string>();
        foreach (AccessibleNode child in bridge.GetPage(pageIndex).Children)
        {
            Walk(bridge, child, text);
        }

        return text;

        static void Walk(AccessibilityBridge bridge, AccessibleNode node, List<string> text)
        {
            if (node.Children.Count == 0)
            {
                string description = Describe(bridge, node);
                if (description.Length > 0)
                {
                    text.Add(description);
                }

                return;
            }

            foreach (AccessibleNode child in node.Children)
            {
                Walk(bridge, child, text);
            }
        }
    }
}
