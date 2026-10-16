using System.Text;
using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Structure;

namespace Broadside.Layout.Flow;

/// <summary>
/// The structure sink of a tagged flow (<see cref="FlowTagging"/>): turns the layout's block and part hooks into structure elements and
/// marked-content sequences, in the order they are drawn.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.7.2 (structure elements), §14.7.5 (marked-content references), §14.8.2.2 (artifacts). Elements are created lazily: a
/// container (<c>L</c>, <c>LI</c>, <c>Table</c>, <c>TR</c>, <c>Sect</c>...) is added to the tree only when its first content arrives, so an
/// empty one leaves nothing behind and a list label (which arrives after <c>LBody</c> has begun) still becomes the item's first child. Only
/// leaves open marked content: plain paragraphs, images, labels and inline parts. A run with nothing to say (<c>Span</c> with no language) is
/// tagged as content of its paragraph. A block or part begun again after a page break (or in the next fragment of a split table row) reuses its
/// element, so it is one element with content on several pages.
/// </para>
/// <para>
/// Only one marked-content sequence is open at a time: a leaf or an artifact that begins inside an open leaf closes it and opens it again (with a
/// new MCID) when it ends, so artifacts never sit inside tagged content and MCIDs never nest. The layout never begins a part inside a text
/// object that it began outside one, so the sequences nest properly with <c>BT</c>/<c>ET</c>.
/// </para>
/// </remarks>
internal sealed class FlowStructure : IStructureSink
{
    /// <summary>The diagnostic code recorded for a figure without alternate text (§14.9.3).</summary>
    public const string FigureWithoutAlternateText = nameof(FigureWithoutAlternateText);

    private static readonly CosName TableOwner = new("Table");
    private static readonly CosName ListOwner = new("List");
    private static readonly CosName ListNumberingKey = new("ListNumbering");
    private static readonly CosName RowSpanKey = new("RowSpan");
    private static readonly CosName ColSpanKey = new("ColSpan");
    private static readonly CosName ScopeKey = new("Scope");
    private static readonly CosName HeadersKey = new("Headers");
    private static readonly CosName KidsKey = new("K");
    private static readonly CosName ParentKey = new("P");

    private readonly PdfStructureTreeRoot _root;
    private readonly FlowTagging _tagging;
    private readonly List<Diagnostic> _diagnostics;
    private readonly List<Node> _stack = [];
    private readonly List<Node> _sequences = [];
    private readonly HashSet<string> _mapped = new(StringComparer.Ordinal);
    private readonly Dictionary<TableCell, PdfStructureElement> _cells = [];
    private PdfPage? _page;
    private int _ids;

    public FlowStructure(PdfStructureTreeRoot root, FlowTagging tagging, List<Diagnostic> diagnostics)
    {
        _root = root;
        _tagging = tagging;
        _diagnostics = diagnostics;
        _stack.Add(new Node("Document", null, null, NodeKind.Container, null));
    }

    /// <summary>Gets the <c>Document</c> element, or <see langword="null"/> when nothing was tagged.</summary>
    public PdfStructureElement? Document => _stack[0].Element;

    public void BeginSection(PageTemplate template)
    {
        while (_stack.Count > 1)
        {
            _stack.RemoveAt(_stack.Count - 1);
        }

        _stack.Add(new Node("Sect", template, null, NodeKind.Container, _stack[0]));
    }

    public void BeginBlock(FlowBlock block, PdfPage page, PdfCanvas canvas)
    {
        NodeKind kind = block switch
        {
            Paragraph { IsPlain: true } or ImageBlock => NodeKind.Leaf,
            _ => NodeKind.Container,
        };
        Begin(block.StructureType, block, block.Language, kind, page, canvas);
    }

    public void EndBlock(FlowBlock block, PdfPage page, PdfCanvas canvas) => End(page, canvas);

    public void BeginPart(FlowPart part, PdfPage page, PdfCanvas canvas)
    {
        NodeKind kind = part switch
        {
            { StructureType: "Artifact" } => NodeKind.Artifact,
            { Owner: TextRun { Link: null }, StructureType: "Span", Language: null } => NodeKind.Transparent,
            { Owner: InlineElement } or { StructureType: "Lbl" } => NodeKind.Leaf,
            _ => NodeKind.Container,
        };
        Begin(part.StructureType, part.Owner, part.Language, kind, page, canvas);
    }

    public void EndPart(FlowPart part, PdfPage page, PdfCanvas canvas) => End(page, canvas);

    public void AddAnnotation(FlowPart part, PdfAnnotation annotation)
    {
        if (annotation.Reference is not { } reference || _page is null)
        {
            return;
        }

        Node parent = _stack[^1];
        for (int index = parent.Children.Count - 1; index >= 0; index--)
        {
            Node child = parent.Children[index];
            if (ReferenceEquals(child.Owner, part.Owner) && child.Type == part.StructureType)
            {
                child.Element!.AddObjectReference(reference, _page);
                if (annotation.Contents is null && part.Owner is TextRun run)
                {
                    // PDF/UA-1 §7.18.5: a link annotation describes itself through Contents.
                    annotation.Contents = run.Text;
                }

                return;
            }
        }
    }

    private void Begin(string type, object? owner, string? language, NodeKind kind, PdfPage page, PdfCanvas canvas)
    {
        _page = page;
        Node parent = _stack[^1];
        if (parent.Kind is NodeKind.Artifact or NodeKind.Ignored)
        {
            _stack.Add(new Node(type, owner, language, NodeKind.Ignored, parent));
            return;
        }

        if (kind == NodeKind.Artifact)
        {
            var artifact = new Node(type, owner, language, kind, parent);
            _stack.Add(artifact);
            Open(artifact, canvas);
            return;
        }

        if (kind == NodeKind.Transparent)
        {
            var span = new Node(type, owner, language, kind, parent);
            _stack.Add(span);
            Open(span, canvas);
            return;
        }

        if (type == "Lbl" && FindItem(owner) is { } item)
        {
            parent = item;
        }

        Node node = Reusable(parent, type, owner) ?? new Node(type, owner, language, kind, parent);
        _stack.Add(node);
        if (kind == NodeKind.Leaf)
        {
            Open(node, canvas);
        }
    }

    private void End(PdfPage page, PdfCanvas canvas)
    {
        _page = page;
        Node node = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        if (node.Kind is NodeKind.Leaf or NodeKind.Transparent or NodeKind.Artifact && _sequences.Count > 0 && ReferenceEquals(_sequences[^1], node))
        {
            canvas.EndMarkedContent();
            _sequences.RemoveAt(_sequences.Count - 1);
            if (_sequences.Count > 0)
            {
                Write(_sequences[^1], canvas);
            }
        }
    }

    /// <summary>Opens <paramref name="node"/>'s marked-content sequence, closing the one open now (it reopens when this one ends).</summary>
    private void Open(Node node, PdfCanvas canvas)
    {
        if (_sequences.Count > 0)
        {
            canvas.EndMarkedContent();
        }

        _sequences.Add(node);
        Write(node, canvas);
    }

    private void Write(Node node, PdfCanvas canvas)
    {
        switch (node.Kind)
        {
            case NodeKind.Artifact:
                canvas.BeginArtifact(node.Owner as PdfArtifactType? ?? PdfArtifactType.Layout);
                break;
            case NodeKind.Transparent:
                canvas.BeginTaggedContent(Materialize(node.Parent!));
                break;
            default:
                canvas.BeginTaggedContent(Materialize(node));
                break;
        }
    }

    /// <summary>The list item a label belongs to: the open <c>LI</c> whose owner is the label's.</summary>
    private Node? FindItem(object? owner)
    {
        for (int index = _stack.Count - 1; index > 0; index--)
        {
            if (_stack[index] is { Type: "LI" } item && ReferenceEquals(item.Owner, owner))
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>
    /// The element begun before for the same owner and type, when this begin continues it: the parent's last child, or any child for table rows
    /// and cells (a split row's next fragment begins its cells again).
    /// </summary>
    private static Node? Reusable(Node parent, string type, object? owner)
    {
        bool anyChild = owner is TableRow or TableCell;
        if (type == "Caption" && parent.Children is [.., { Children: [.., var adopted] }] && ReferenceEquals(adopted.Owner, owner) && adopted.Type == type)
        {
            // A caption continued on the next page, already moved into its figure or table.
            return adopted;
        }

        for (int index = parent.Children.Count - 1; index >= 0; index--)
        {
            Node child = parent.Children[index];
            if (ReferenceEquals(child.Owner, owner) && child.Type == type)
            {
                return child;
            }

            if (!anyChild)
            {
                break;
            }
        }

        return null;
    }

    /// <summary>Adds <paramref name="node"/>'s element (and its ancestors') to the tree on first use.</summary>
    private PdfStructureElement Materialize(Node node)
    {
        if (node.Element is { } existing)
        {
            return existing;
        }

        Node? parent = node.Parent;
        if (parent is not null && node.Type == "Caption" && node.Owner is FlowBlock && parent.Children is [.., { Owner: ImageBlock or Table } captioned] && captioned.Type == ((FlowBlock)captioned.Owner!).StructureType)
        {
            // A caption right after a figure or table belongs to it (§14.8.4.4, Table 368).
            node.Parent = parent = captioned;
        }

        PdfStructureType type = TypeOf(node);
        PdfStructureElement element = parent is null ? _root.AddElement(type) : Materialize(parent).AddElement(type);
        node.Element = element;
        parent?.Children.Add(node);
        if (node.Language is { } language)
        {
            element.Language = language;
        }

        switch (node.Owner)
        {
            case ImageBlock image:
                Describe(element, image.AlternateText);
                break;
            case InlineImage image:
                Describe(element, image.AlternateText);
                break;
            case TableCell cell:
                DescribeCell(node, element, cell);
                break;
            case ListBlock list:
                // §14.8.5.5, Table 382: how the labels number the items; PDF/UA-2 requires it on lists with labels (ISO 14289-2 §8.2.5.25).
                element.AddAttributes(ListOwner).SetValue(ListNumberingKey, new CosName(NumberingOf(list.Style)));
                break;
        }

        if (node.Owner is ImageBlock or Table && node.Type == ((FlowBlock)node.Owner).StructureType && parent is not null)
        {
            AdoptCaption(parent, node);
        }

        return element;
    }

    /// <summary>Moves a caption that came right before a figure or table into it, as its first child (§14.8.4.4, Table 368).</summary>
    private static void AdoptCaption(Node parent, Node captioned)
    {
        // The new element is the parent's last child; the caption would be the one before it.
        if (parent.Children is not [.., { Type: "Caption", Owner: FlowBlock, Element: { Reference: { } reference } caption } node, _])
        {
            return;
        }

        PdfStructureElement target = captioned.Element!;
        CosDictionary from = Materialized(parent).Dictionary;
        if (from.TryGetValue(KidsKey, out CosObject? kids) && kids is CosArray array)
        {
            for (int index = 0; index < array.Count; index++)
            {
                if (array[index] is CosReference kid && kid.ObjectNumber == reference.ObjectNumber)
                {
                    array.RemoveAt(index);
                    break;
                }
            }
        }

        CosDictionary to = target.Dictionary;
        if (to.TryGetValue(KidsKey, out CosObject? existing) && existing is CosArray targetKids)
        {
            targetKids.Insert(0, reference);
        }
        else
        {
            to[KidsKey] = existing is null ? new CosArray([reference]) : new CosArray([reference, existing]);
        }

        caption.Dictionary[ParentKey] = target.Reference!;
        parent.Children.Remove(node);
        captioned.Children.Insert(0, node);
        node.Parent = captioned;
    }

    private static PdfStructureElement Materialized(Node node) => node.Element!;

    private void Describe(PdfStructureElement element, string? alternate)
    {
        if (!string.IsNullOrWhiteSpace(alternate))
        {
            element.AlternateDescription = alternate;
            return;
        }

        _diagnostics.Add(new Diagnostic(
            FigureWithoutAlternateText,
            DiagnosticSeverity.Warning,
            "A figure has no alternate text (ISO 32000-2 §14.9.3); assistive technology has nothing to read for it.",
            objectReference: element.Reference));
    }

    /// <summary>Writes a table cell's <c>Table</c> attributes (§14.8.5.7, Table 384): spans, the header scope and the header cells' IDs.</summary>
    private void DescribeCell(Node node, PdfStructureElement element, TableCell cell)
    {
        _cells[cell] = element;
        PdfAttributeObject? attributes = null;
        if (cell.RowSpan > 1)
        {
            Attributes().SetValue(RowSpanKey, new CosInteger(cell.RowSpan));
        }

        if (cell.ColumnSpan > 1)
        {
            Attributes().SetValue(ColSpanKey, new CosInteger(cell.ColumnSpan));
        }

        Table? table = TableOf(node);
        if (table is null)
        {
            return;
        }

        if (node.Type == "TH")
        {
            string scope = table.ScopeOf(cell) switch
            {
                TableHeaderScope.Row => "Row",
                TableHeaderScope.Both => "Both",
                _ => "Column",
            };
            Attributes().SetValue(ScopeKey, new CosName(scope));
        }

        var headers = new CosArray();
        foreach (TableCell header in table.HeadersOf(cell))
        {
            if (_cells.TryGetValue(header, out PdfStructureElement? headerElement))
            {
                headers.Add(IdOf(headerElement));
            }
        }

        if (headers.Count > 0)
        {
            Attributes().SetValue(HeadersKey, headers);
        }

        PdfAttributeObject Attributes() => attributes ??= element.AddAttributes(TableOwner);
    }

    private static Table? TableOf(Node node)
    {
        for (Node? ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor.Owner is Table table)
            {
                return table;
            }
        }

        return null;
    }

    /// <summary>The element's <c>ID</c>, given a new unique one when it has none (§14.7.2, Table 355).</summary>
    private CosString IdOf(PdfStructureElement element)
    {
        if (element.Id is { } id)
        {
            return id;
        }

        byte[] bytes;
        do
        {
            bytes = Encoding.ASCII.GetBytes($"flow-th-{++_ids}");
        }
        while (_root.FindElementById(bytes) is not null);

        var created = new CosString(bytes);
        element.Id = created;
        return created;
    }

    /// <summary>The element's structure type in the chosen namespace, role-mapping a custom type on first use (§14.8.6.2).</summary>
    private PdfStructureType TypeOf(Node node)
    {
        string name = node.Type;
        if (_tagging.Namespace == FlowStructureNamespace.Pdf20 && PdfStructureNamespace.Pdf20.IsStandardType(name))
        {
            return new PdfStructureType(name, PdfStructureNamespace.Pdf20);
        }

        if (!PdfStructureNamespace.Pdf17.IsStandardType(name) && _mapped.Add(name))
        {
            string target = _tagging.RoleMap.TryGetValue(name, out string? mapped) ? mapped : DefaultType(node.Owner);
            _root.MapRole(new CosName(name), new CosName(target));
        }

        return new PdfStructureType(name, PdfStructureNamespace.Pdf17);
    }

    /// <summary>
    /// The <c>ListNumbering</c> value of a list style (§14.8.5.5, Table 382): the numbering's own name, and for bullets the shape of
    /// the bullet (<c>Disc</c>, <c>Circle</c>, <c>Square</c>), or <c>Unordered</c> (PDF 2.0) for any other marker in the PDF 2.0
    /// namespace and <c>Disc</c> in PDF 1.7's, which has no generic value.
    /// </summary>
    private string NumberingOf(ListStyle style) => style.Numbering switch
    {
        ListNumbering.Decimal => "Decimal",
        ListNumbering.LowerAlpha => "LowerAlpha",
        ListNumbering.UpperAlpha => "UpperAlpha",
        ListNumbering.LowerRoman => "LowerRoman",
        ListNumbering.UpperRoman => "UpperRoman",
        _ => style.Bullet switch
        {
            "•" or "●" => "Disc",
            "◦" or "○" => "Circle",
            "▪" or "■" => "Square",
            _ => _tagging.Namespace == FlowStructureNamespace.Pdf20 ? "Unordered" : "Disc",
        },
    };

    private static string DefaultType(object? owner) => owner switch
    {
        Paragraph => "P",
        ImageBlock or InlineImage => "Figure",
        ListBlock => "L",
        Table => "Table",
        TextRun { Link: not null } => "Link",
        TextRun => "Span",
        _ => "Div",
    };

    private enum NodeKind
    {
        Container,
        Leaf,
        Transparent,
        Artifact,
        Ignored,
    }

    private sealed class Node(string type, object? owner, string? language, NodeKind kind, Node? parent)
    {
        public string Type { get; } = type;

        public object? Owner { get; } = owner;

        public string? Language { get; } = language;

        public NodeKind Kind { get; } = kind;

        public Node? Parent { get; set; } = parent;

        public PdfStructureElement? Element { get; set; }

        public List<Node> Children { get; } = [];
    }
}
