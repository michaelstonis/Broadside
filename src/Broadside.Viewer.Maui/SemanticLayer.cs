using System.Globalization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Layouts;

namespace Broadside.Viewer.Maui;

/// <summary>
/// The <see cref="AccessibilityBridge"/>'s tree as MAUI semantics: one transparent, input-transparent view per readable node of
/// each loaded page, in reading order, positioned over the text it stands for, so VoiceOver, TalkBack and Narrator read the page
/// and explore it by touch. Headings carry their level; links, form fields and annotations say what they are.
/// </summary>
/// <remarks>ISO 32000-2 §14.8 (tagged PDF: reading order and structure types), §14.9.3 (alternate descriptions).</remarks>
internal sealed class SemanticLayer : AbsoluteLayout
{
    private readonly Dictionary<AccessibleNode, Label> _views = new(ReferenceEqualityComparer.Instance);

    public SemanticLayer()
    {
        InputTransparent = true;
        CascadeInputTransparent = true;
    }

    /// <summary>Rebuilds the views for the loaded pages, in page and reading order.</summary>
    public void Rebuild(ViewerController controller)
    {
        Children.Clear();
        _views.Clear();
        if (controller.Bridge is not { } bridge)
        {
            return;
        }

        foreach (int page in controller.AccessiblePages)
        {
            foreach (AccessibleNode node in bridge.GetPage(page).Children)
            {
                Add(bridge, node);
            }
        }

        Reposition(controller);
    }

    /// <summary>Moves every view over its node (hidden when the node is off screen).</summary>
    public void Reposition(ViewerController controller)
    {
        foreach ((AccessibleNode node, Label view) in _views)
        {
            if (controller.TryGetNodeBounds(node, out LayoutRectangle bounds) && bounds.Right > 0 && bounds.Bottom > 0 && bounds.X < Width && bounds.Y < Height)
            {
                SetLayoutBounds((BindableObject)view, new Rect(bounds.X, bounds.Y, Math.Max(bounds.Width, 1), Math.Max(bounds.Height, 1)));
                view.IsVisible = true;
            }
            else
            {
                view.IsVisible = false;
            }
        }
    }

    /// <summary>Moves the screen reader's focus to <paramref name="node"/>'s view.</summary>
    public void Focus(AccessibleNode? node)
    {
        if (node is not null && _views.TryGetValue(node, out Label? view) && view.IsVisible)
        {
            view.SetSemanticFocus();
        }
    }

    private void Add(AccessibilityBridge bridge, AccessibleNode node)
    {
        if (IsContainer(node.Role) && node.Children.Count > 0)
        {
            foreach (AccessibleNode child in node.Children)
            {
                Add(bridge, child);
            }

            return;
        }

        string description = Describe(bridge, node);
        if (description.Length == 0)
        {
            return;
        }

        var view = new Label
        {
            Text = string.Empty,
            BackgroundColor = Colors.Transparent,
            TextColor = Colors.Transparent,
            InputTransparent = true,
            IsVisible = false,
        };
        SemanticProperties.SetDescription(view, description);
        if (node.Role == AccessibleRole.Heading)
        {
            SemanticProperties.SetHeadingLevel(view, (SemanticHeadingLevel)Math.Clamp(node.Level <= 0 ? 1 : node.Level, 1, 9));
        }

        _views.Add(node, view);
        Children.Add(view);
    }

    private static bool IsContainer(AccessibleRole role) =>
        role is AccessibleRole.Document or AccessibleRole.Page or AccessibleRole.Section or AccessibleRole.Group or AccessibleRole.List
            or AccessibleRole.ListItem or AccessibleRole.Table or AccessibleRole.Row;

    internal static string Describe(AccessibilityBridge bridge, AccessibleNode node)
    {
        string name = (node.Name ?? node.Text).Trim();
        AccessibleStates states = bridge.GetStates(node);
        string state = (states & AccessibleStates.Checked) != 0 ? ", checked" : string.Empty;
        return node.Role switch
        {
            AccessibleRole.Link => Join(name, "link"),
            AccessibleRole.Figure => name.Length == 0 ? "figure" : Join(name, "figure"),
            AccessibleRole.Button => Join(name, "button"),
            AccessibleRole.CheckBox => Join(name, "check box") + (state.Length > 0 ? state : ", not checked"),
            AccessibleRole.RadioButton => Join(name, "radio button") + (state.Length > 0 ? state : ", not selected"),
            AccessibleRole.TextBox => Join(Join(name, "text field"), node.Value ?? string.Empty),
            AccessibleRole.ComboBox or AccessibleRole.ListBox => Join(Join(name, "pop-up button"), node.Value ?? string.Empty),
            AccessibleRole.Annotation or AccessibleRole.Note => Join(name, "comment"),
            AccessibleRole.Cell or AccessibleRole.ColumnHeader or AccessibleRole.RowHeader when node.Row >= 0 && node.Column >= 0 =>
                Join(name, string.Create(CultureInfo.InvariantCulture, $"row {node.Row + 1}, column {node.Column + 1}")),
            _ => name,
        };

        static string Join(string a, string b) => a.Length == 0 ? b : b.Length == 0 ? a : a + ", " + b;
    }
}
