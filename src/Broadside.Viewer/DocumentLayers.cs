namespace Broadside.Viewer;

/// <summary>
/// The document's layers (optional content groups) as a layers panel shows them: the default configuration's <c>Order</c> tree, each
/// layer with whether it is visible and whether it is locked, and the toggles that show and hide them through
/// <see cref="DocumentSession.OptionalContentState"/>.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.11.2 (optional content groups) and §8.11.4.3 (configuration dictionaries, Table 99): <c>Order</c> gives the tree, a
/// labelled nested array a collection that cannot be toggled, an unlabelled one the sublayers of the group before it; <c>Locked</c>
/// groups cannot be toggled; turning on a group of a <c>RBGroups</c> radio-button set turns the others of the set off. Without an
/// <c>Order</c> array the groups are listed flat, in <c>OCGs</c> order.
/// </para>
/// <para>
/// A view over the live document (ADR 0004): items hold the groups, and a toggle changes only the content options the session builds
/// display lists with (ADR 0006). Use it on the UI thread; <see cref="ReadAsync"/> reads through the session's worker reads.
/// </para>
/// </remarks>
public sealed class DocumentLayers
{
    private readonly DocumentSession _session;
    private readonly IReadOnlyList<Node> _tree;
    private readonly HashSet<PdfOptionalContentGroup> _locked;
    private readonly IReadOnlyList<IReadOnlyList<PdfOptionalContentGroup>> _radioGroups;
    private readonly PdfOptionalContentState? _defaults;
    private PdfOptionalContentState? _built;
    private List<LayerItem> _roots = [];

    private DocumentLayers(
        DocumentSession session,
        IReadOnlyList<Node> tree,
        HashSet<PdfOptionalContentGroup> locked,
        IReadOnlyList<IReadOnlyList<PdfOptionalContentGroup>> radioGroups,
        PdfOptionalContentState? defaults)
    {
        _session = session;
        _tree = tree;
        _locked = locked;
        _radioGroups = radioGroups;
        _defaults = defaults;
        Rebuild();
        session.OptionalContentChanged += OnOptionalContentChanged;
    }

    /// <summary>Raised on the UI thread when a layer was shown or hidden, here or through <see cref="DocumentSession.OptionalContentState"/>.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the top of the layer tree, rebuilt with the current visibility after each change.</summary>
    public IReadOnlyList<LayerItem> Roots => _roots;

    /// <summary>Gets a value indicating whether the document has no optional content.</summary>
    public bool IsEmpty => _tree.Count == 0;

    /// <summary>Reads a session's layers from its document's default configuration.</summary>
    /// <param name="session">The session.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The layers; <see cref="IsEmpty"/> when the document has no optional content.</returns>
    /// <remarks>ISO 32000-2 §8.11.4.2 and §8.11.4.3.</remarks>
    public static async ValueTask<DocumentLayers> ReadAsync(DocumentSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        (List<Node> tree, HashSet<PdfOptionalContentGroup> locked, IReadOnlyList<IReadOnlyList<PdfOptionalContentGroup>> radio, PdfOptionalContentState? defaults) =
            await session.ReadAsync(_ => Read(session.Document), cancellationToken).ConfigureAwait(true);
        return new DocumentLayers(session, tree, locked, radio, defaults);
    }

    /// <summary>Shows or hides a layer.</summary>
    /// <param name="item">A layer of <see cref="Roots"/>.</param>
    /// <param name="visible">Whether it shows.</param>
    /// <returns>Whether the state changed: <see langword="false"/> for a collection label, a locked layer, or no change.</returns>
    /// <remarks>ISO 32000-2 §8.11.4.3, Table 99 (<c>Locked</c>, <c>RBGroups</c>): showing a radio-button layer hides the others of its set.</remarks>
    public bool SetVisible(LayerItem item, bool visible)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Group is not { } group || _locked.Contains(group) || Current() is not { } state || state.IsOn(group) == visible)
        {
            return false;
        }

        state = state.With(group, visible);
        if (visible)
        {
            foreach (IReadOnlyList<PdfOptionalContentGroup> set in _radioGroups)
            {
                if (set.Contains(group))
                {
                    foreach (PdfOptionalContentGroup other in set)
                    {
                        if (!ReferenceEquals(other, group) && !_locked.Contains(other))
                        {
                            state = state.With(other, false);
                        }
                    }
                }
            }
        }

        _session.OptionalContentState = state;
        return true;
    }

    /// <summary>Returns to the default configuration's states (<see cref="DocumentSession.OptionalContentState"/> back to <see langword="null"/>).</summary>
    /// <remarks>ISO 32000-2 §8.11.4.5.</remarks>
    public void Reset() => _session.OptionalContentState = null;

    private PdfOptionalContentState? Current() => _session.OptionalContentState ?? _defaults;

    private void OnOptionalContentChanged(object? sender, EventArgs e)
    {
        Rebuild();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Rebuild()
    {
        PdfOptionalContentState? state = Current();
        if (_roots.Count > 0 && ReferenceEquals(state, _built))
        {
            return;
        }

        _built = state;
        _roots = Build(_tree, state);
    }

    private List<LayerItem> Build(IReadOnlyList<Node> nodes, PdfOptionalContentState? state)
    {
        var items = new List<LayerItem>(nodes.Count);
        foreach (Node node in nodes)
        {
            List<LayerItem> children = Build(node.Children, state);
            items.Add(node.Group is { } group
                ? new LayerItem(node.Title, group, children, state?.IsOn(group) ?? true, _locked.Contains(group))
                : new LayerItem(node.Title, null, children, IsVisible: true, IsLocked: true));
        }

        return items;
    }

    private static (List<Node>, HashSet<PdfOptionalContentGroup>, IReadOnlyList<IReadOnlyList<PdfOptionalContentGroup>>, PdfOptionalContentState?) Read(PdfDocument document)
    {
        var locked = new HashSet<PdfOptionalContentGroup>(ReferenceEqualityComparer.Instance);
        if (document.OptionalContent is not { } properties)
        {
            return ([], locked, [], null);
        }

        PdfOptionalContentConfiguration configuration = properties.DefaultConfiguration;
        foreach (PdfOptionalContentGroup group in configuration.Locked)
        {
            locked.Add(group);
        }

        IReadOnlyList<PdfOptionalContentOrderItem> order = configuration.Order;
        List<Node> tree = order.Count > 0
            ? Convert(order)
            : [.. properties.Groups.Select(group => new Node(group.Name, group, []))];
        return (tree, locked, configuration.RadioButtonGroups, properties.GetDefaultStates());
    }

    // Groups become layers; a labelled collection a node of its own; an unlabelled list (not a group's sublayers) is flattened into its parent.
    private static List<Node> Convert(IReadOnlyList<PdfOptionalContentOrderItem> items)
    {
        var nodes = new List<Node>(items.Count);
        foreach (PdfOptionalContentOrderItem item in items)
        {
            if (item.Group is { } group)
            {
                nodes.Add(new Node(group.Name, group, Convert(item.Children)));
            }
            else if (item.Label is { } label)
            {
                nodes.Add(new Node(label, null, Convert(item.Children)));
            }
            else
            {
                nodes.AddRange(Convert(item.Children));
            }
        }

        return nodes;
    }

    private sealed record Node(string Title, PdfOptionalContentGroup? Group, IReadOnlyList<Node> Children);
}

/// <summary>One node of the layers panel: a layer (an optional content group) or a collection label, with what is under it.</summary>
/// <param name="Title">The group's name (<c>Name</c>) or the collection's label.</param>
/// <param name="Group">The group, or <see langword="null"/> for a collection label.</param>
/// <param name="Children">The sublayers or the collection's members, in <c>Order</c> order.</param>
/// <param name="IsVisible">Whether the group is ON in the session's state (always <see langword="true"/> for a label).</param>
/// <param name="IsLocked">Whether it cannot be toggled: a group in <c>Locked</c>, or a label.</param>
/// <remarks>ISO 32000-2 §8.11.2.1 and §8.11.4.3, Table 99.</remarks>
public sealed record LayerItem(string Title, PdfOptionalContentGroup? Group, IReadOnlyList<LayerItem> Children, bool IsVisible, bool IsLocked);
