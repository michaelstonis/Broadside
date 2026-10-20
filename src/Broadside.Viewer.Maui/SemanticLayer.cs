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
    private static readonly TimeSpan BuildSlice = TimeSpan.FromMilliseconds(4);
    private readonly Dictionary<int, PageLayer> _pages = [];
    private readonly Dictionary<AccessibleNode, Label> _views = new(ReferenceEqualityComparer.Instance);
    private readonly Queue<PageLayer> _pending = new();
    private readonly List<int> _drop = [];
    private ViewerController? _controller;
    private bool _building;

    public SemanticLayer()
    {
        InputTransparent = true;
        CascadeInputTransparent = true;
    }

    /// <summary>
    /// Follows the controller's loaded pages: a page that left is dropped, a new page (or <paramref name="changed"/>'s page, rebuilt
    /// after an edit) gets its views. Views are made a few milliseconds at a time between frames, never all at once (#357).
    /// </summary>
    public void Rebuild(ViewerController controller, AccessibleNode? changed = null)
    {
        _controller = controller;
        if (controller.Bridge is null)
        {
            ClearPages();
            return;
        }

        IReadOnlyList<int> loaded = controller.AccessiblePages;
        int rotation = RotationOf(controller);
        foreach ((int page, PageLayer existing) in _pages)
        {
            // A layer's label rectangles hold the rotation it was built at: another rotation rebuilds it.
            if (!loaded.Contains(page) || existing.Rotation != rotation || (changed is { Role: AccessibleRole.Page } && changed.PageIndex == page))
            {
                _drop.Add(page);
            }
        }

        foreach (int page in _drop)
        {
            Remove(page);
        }

        _drop.Clear();
        foreach (int page in loaded)
        {
            if (!_pages.ContainsKey(page) && controller.TryGetNodeBounds(controller.Bridge.GetPage(page), out LayoutRectangle bounds) && bounds.Width > 0)
            {
                var layer = new PageLayer(page, bounds, rotation);

                // In page order, so screen readers traverse the pages in order whichever was (re)built last.
                int before = 0;
                foreach (int other in _pages.Keys)
                {
                    before += other < page ? 1 : 0;
                }

                _pages.Add(page, layer);
                Children.Insert(before, layer.View);
                SetLayoutBounds((BindableObject)layer.View, new Rect(0, 0, layer.Width, layer.Height));
                _pending.Enqueue(layer);
            }
        }

        BuildSome();
        Reposition(controller);
    }

    /// <summary>Moves each page's views with its page: one transform per page, so a scroll or zoom lays nothing out.</summary>
    public void Reposition(ViewerController controller)
    {
        if (controller.Bridge is not { } bridge)
        {
            return;
        }

        if (_controller is not null && _pages.Values.Any(layer => layer.Rotation != RotationOf(controller)))
        {
            Rebuild(controller); // the view rotated: the layers are rebuilt at the new rotation (Rebuild repositions)
            return;
        }

        foreach (PageLayer layer in _pages.Values)
        {
            bool shown = controller.TryGetNodeBounds(bridge.GetPage(layer.PageIndex), out LayoutRectangle page)
                && page.Width > 0 && page.Right > 0 && page.Bottom > 0 && page.X < Width && page.Y < Height;
            if (shown)
            {
                layer.View.TranslationX = page.X;
                layer.View.TranslationY = page.Y;
                layer.View.Scale = page.Width / layer.Width;
            }

            if (layer.View.IsVisible != shown)
            {
                layer.View.IsVisible = shown;
            }
        }
    }

    private static int RotationOf(ViewerController controller) => controller.Zoom?.Layout.Rotation ?? 0;

    public void Focus(AccessibleNode? node)
    {
        if (node is not null && _views.TryGetValue(node, out Label? view) && _pages.TryGetValue(node.PageIndex, out PageLayer? layer) && layer.View.IsVisible)
        {
            view.SetSemanticFocus();
        }
    }

    private void ClearPages()
    {
        Children.Clear();
        _pages.Clear();
        _views.Clear();
        _pending.Clear();
    }

    private void Remove(int page)
    {
        if (_pages.Remove(page, out PageLayer? layer))
        {
            Children.Remove(layer.View);
            foreach (AccessibleNode node in layer.Nodes)
            {
                _views.Remove(node);
            }
        }
    }

    /// <summary>Makes views for the pending pages until the slice is spent, then continues on a later dispatch.</summary>
    private void BuildSome()
    {
        if (_controller?.Bridge is not { } bridge)
        {
            return;
        }

        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        while (_pending.TryPeek(out PageLayer? layer))
        {
            if (!_pages.TryGetValue(layer.PageIndex, out PageLayer? current) || current != layer)
            {
                _pending.Dequeue(); // dropped meanwhile
                continue;
            }

            if (layer.Nodes.Count == 0 && !layer.Listed)
            {
                layer.Listed = true;
                foreach (AccessibleNode node in bridge.GetPage(layer.PageIndex).Children)
                {
                    List(node, layer.Nodes);
                }
            }

            if (!_controller.TryGetNodeBounds(bridge.GetPage(layer.PageIndex), out LayoutRectangle page) || page.Width <= 0)
            {
                _pending.Dequeue();
                continue;
            }

            double scale = layer.Width / page.Width;
            while (layer.Next < layer.Nodes.Count)
            {
                AccessibleNode node = layer.Nodes[layer.Next++];
                Add(bridge, layer, node, page, scale);
                if (System.Diagnostics.Stopwatch.GetElapsedTime(start) >= BuildSlice)
                {
                    break;
                }
            }

            if (layer.Next >= layer.Nodes.Count)
            {
                _pending.Dequeue();
            }

            if (System.Diagnostics.Stopwatch.GetElapsedTime(start) >= BuildSlice)
            {
                break;
            }
        }

        if (_pending.Count > 0 && !_building && Dispatcher is { } dispatcher)
        {
            _building = true;
            dispatcher.Dispatch(() =>
            {
                _building = false;
                BuildSome();
            });
        }
    }

    private static void List(AccessibleNode node, List<AccessibleNode> nodes)
    {
        if (IsContainer(node.Role) && node.Children.Count > 0)
        {
            foreach (AccessibleNode child in node.Children)
            {
                List(child, nodes);
            }

            return;
        }

        nodes.Add(node);
    }

    private void Add(AccessibilityBridge bridge, PageLayer layer, AccessibleNode node, in LayoutRectangle page, double scale)
    {
        string description = Describe(bridge, node);
        if (description.Length == 0 || !_controller!.TryGetNodeBounds(node, out LayoutRectangle bounds))
        {
            return;
        }

        var view = new Label
        {
            Text = string.Empty,
            BackgroundColor = Colors.Transparent,
            TextColor = Colors.Transparent,
            InputTransparent = true,
        };
        SemanticProperties.SetDescription(view, description);
        if (node.Role == AccessibleRole.Heading)
        {
            SemanticProperties.SetHeadingLevel(view, (SemanticHeadingLevel)Math.Clamp(node.Level <= 0 ? 1 : node.Level, 1, 9));
        }

        // In the page's own units (its width when it was added), so the page's transform alone places it at any scroll and zoom.
        AbsoluteLayout.SetLayoutBounds(
            view,
            new Rect((bounds.X - page.X) * scale, (bounds.Y - page.Y) * scale, Math.Max(bounds.Width * scale, 1), Math.Max(bounds.Height * scale, 1)));
        _views[node] = view;
        layer.View.Children.Add(view);
    }

    /// <summary>One loaded page's views, in a layer the page's size when it was added, moved and scaled as the page moves.</summary>
    private sealed class PageLayer(int pageIndex, LayoutRectangle bounds, int rotation)
    {
        public int PageIndex { get; } = pageIndex;

        public int Rotation { get; } = rotation;

        public double Width { get; } = bounds.Width;

        public double Height { get; } = bounds.Height;

        public AbsoluteLayout View { get; } = new() { InputTransparent = true, CascadeInputTransparent = true, AnchorX = 0, AnchorY = 0, IsVisible = false };

        public List<AccessibleNode> Nodes { get; } = [];

        public bool Listed { get; set; }

        public int Next { get; set; }
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
