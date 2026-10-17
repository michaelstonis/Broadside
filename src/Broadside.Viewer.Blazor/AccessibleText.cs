using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.AspNetCore.Components.Rendering;

namespace Broadside.Viewer.Blazor;

/// <summary>
/// Renders the accessibility bridge's tree as DOM: one <c>section</c> per loaded page, holding the page's nodes in reading order with
/// ARIA roles, levels, table coordinates and states, and their text. Elements are positioned in the page's user space and the section
/// carries the page's transform (set each frame by the JavaScript half), so explore-by-touch and focus outlines land on the page
/// content while the text itself stays transparent over the canvas.
/// </summary>
[SupportedOSPlatform("browser")]
internal static class AccessibleText
{
    public static void Render(RenderTreeBuilder builder, ViewerController controller, AccessibilityBridge bridge)
    {
        controller.NodeIds.Clear();
        controller.NodesById.Clear();
        foreach (int pageIndex in controller.AccessiblePages)
        {
            AccessibleNode page = bridge.GetPage(pageIndex);
            string pageId = string.Create(CultureInfo.InvariantCulture, $"{controller.ElementId}-p{pageIndex}");
            Remember(controller, page, pageId);
            builder.OpenElement(0, "section");
            builder.SetKey(pageIndex);
            builder.AddAttribute(1, "id", pageId);
            builder.AddAttribute(2, "data-bs-page", pageIndex.ToString(CultureInfo.InvariantCulture));
            builder.AddAttribute(3, "tabindex", "-1");
            builder.AddAttribute(4, "aria-label", page.Name ?? string.Create(CultureInfo.InvariantCulture, $"Page {pageIndex + 1}"));
            int ordinal = 0;
            foreach (AccessibleNode child in page.Children)
            {
                RenderNode(builder, controller, child, pageId, 0, 0, ref ordinal);
            }

            builder.CloseElement();
        }
    }

    private static void RenderNode(RenderTreeBuilder builder, ViewerController controller, AccessibleNode node, string pageId, double originX, double originY, ref int ordinal)
    {
        (string tag, string? role) = Element(node);
        string id = string.Create(CultureInfo.InvariantCulture, $"{pageId}-{ordinal++}");
        Remember(controller, node, id);
        builder.OpenElement(10, tag);
        builder.AddAttribute(11, "id", id);
        builder.AddAttribute(12, "data-bs-node", "");
        builder.AddAttribute(13, "tabindex", "-1");
        builder.AddAttribute(14, "role", role);
        if (node.Language is { } language)
        {
            builder.AddAttribute(15, "lang", language);
        }

        switch (node.Role)
        {
            case AccessibleRole.Heading:
                builder.AddAttribute(16, "aria-level", Math.Clamp(node.Level, 1, 6).ToString(CultureInfo.InvariantCulture));
                break;
            case AccessibleRole.Cell or AccessibleRole.ColumnHeader or AccessibleRole.RowHeader:
                builder.AddAttribute(17, "aria-rowindex", (node.Row + 1).ToString(CultureInfo.InvariantCulture));
                builder.AddAttribute(18, "aria-colindex", (node.Column + 1).ToString(CultureInfo.InvariantCulture));
                builder.AddAttribute(19, "aria-rowspan", node.RowSpan > 1 ? node.RowSpan.ToString(CultureInfo.InvariantCulture) : null);
                builder.AddAttribute(20, "aria-colspan", node.ColumnSpan > 1 ? node.ColumnSpan.ToString(CultureInfo.InvariantCulture) : null);
                break;
            case AccessibleRole.Table when node.RowCount > 0:
                builder.AddAttribute(21, "aria-rowcount", node.RowCount.ToString(CultureInfo.InvariantCulture));
                builder.AddAttribute(22, "aria-colcount", node.ColumnCount.ToString(CultureInfo.InvariantCulture));
                break;
        }

        if (NeedsName(node.Role) && node.Name is { } name)
        {
            builder.AddAttribute(23, "aria-label", name);
        }

        AccessibleStates states = controller.Bridge!.GetStates(node);
        if (node.Role is AccessibleRole.CheckBox or AccessibleRole.RadioButton)
        {
            builder.AddAttribute(24, "aria-checked", (states & AccessibleStates.Checked) != 0 ? "true" : "false");
        }

        builder.AddAttribute(25, "aria-readonly", (states & AccessibleStates.ReadOnly) != 0 ? "true" : null);
        builder.AddAttribute(26, "aria-required", (states & AccessibleStates.Required) != 0 ? "true" : null);
        builder.AddAttribute(27, "aria-disabled", (states & AccessibleStates.Disabled) != 0 ? "true" : null);

        if (node.PageBounds is { } bounds)
        {
            builder.AddAttribute(28, "data-bs-box", "");
            builder.AddAttribute(29, "style", string.Create(
                CultureInfo.InvariantCulture,
                $"left:{bounds.Left - originX:0.##}px;top:{bounds.Bottom - originY:0.##}px;width:{bounds.Width:0.##}px;height:{bounds.Height:0.##}px"));
            originX = bounds.Left;
            originY = bounds.Bottom;
        }

        if (node.Children.Count == 0 || node.Role == AccessibleRole.Figure)
        {
            builder.AddContent(30, node.Role == AccessibleRole.Figure ? null : node.Value ?? node.Text);
        }
        else
        {
            foreach (AccessibleNode child in node.Children)
            {
                RenderNode(builder, controller, child, pageId, originX, originY, ref ordinal);
            }
        }

        builder.CloseElement();
    }

    private static void Remember(ViewerController controller, AccessibleNode node, string id)
    {
        controller.NodeIds[node] = id;
        controller.NodesById[id] = node;
    }

    private static bool NeedsName(AccessibleRole role) => role is AccessibleRole.Figure or AccessibleRole.Link or AccessibleRole.Button
        or AccessibleRole.CheckBox or AccessibleRole.RadioButton or AccessibleRole.TextBox or AccessibleRole.ComboBox or AccessibleRole.ListBox
        or AccessibleRole.Annotation or AccessibleRole.Section;

    private static (string Tag, string? Role) Element(AccessibleNode node) => node.Role switch
    {
        AccessibleRole.Section => ("section", null),
        AccessibleRole.Group => ("div", "group"),
        AccessibleRole.Paragraph => ("p", null),
        AccessibleRole.Heading => ("div", "heading"),
        AccessibleRole.List => ("div", "list"),
        AccessibleRole.ListItem => ("div", "listitem"),
        AccessibleRole.Table => ("div", "table"),
        AccessibleRole.Row => ("div", "row"),
        AccessibleRole.ColumnHeader => ("div", "columnheader"),
        AccessibleRole.RowHeader => ("div", "rowheader"),
        AccessibleRole.Cell => ("div", "cell"),
        AccessibleRole.Figure => ("div", "img"),
        AccessibleRole.Caption => ("div", "caption"),
        AccessibleRole.Link => ("div", "link"),
        AccessibleRole.Note or AccessibleRole.Annotation => ("div", "note"),
        AccessibleRole.Button => ("div", "button"),
        AccessibleRole.CheckBox => ("div", "checkbox"),
        AccessibleRole.RadioButton => ("div", "radio"),
        AccessibleRole.TextBox => ("div", "textbox"),
        AccessibleRole.ComboBox => ("div", "combobox"),
        AccessibleRole.ListBox => ("div", "listbox"),
        _ => ("span", null),
    };
}
