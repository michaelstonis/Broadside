using System.Globalization;
using Broadside.Annotations;
using Broadside.Objects;
using Broadside.Structure;
using Broadside.Text;

namespace Broadside.Layout;

/// <summary>Builds a layout tree from a document's structure tree, over a set of its pages.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.8.4 and ISO/TS 32005. One node per structure element whose standard type (§14.8.6.2) has a node kind, in the
/// depth-first order of the hierarchy (§14.8.2.5.1); <c>NonStruct</c>, <c>Private</c>, the table row groups and unresolved types
/// pass their children and content up; <c>Artifact</c> subtrees are left out (§14.8.4.8.7).
/// </para>
/// <para>
/// Each page is extracted once in structure order (<see cref="PdfPage.ExtractText(TextExtractionOptions)"/>), with
/// <see cref="MarkGeometry"/> in the same pass. A glyph belongs to the node of its structure element; marks to the node of their
/// content item's element; annotations (object references) to their element's node. A node's extent on a page is the union of its
/// own and its descendants', which are consecutive in logical order. Nodes with no extent on the pages are pruned, except empty
/// cells of a row that is kept.
/// </para>
/// </remarks>
internal static class StructureLayout
{
    private static readonly CosName Headers = new("Headers");
    private static readonly CosName RowSpan = new("RowSpan");
    private static readonly CosName ColSpan = new("ColSpan");
    private static readonly CosName ListNumbering = new("ListNumbering");

    /// <summary>The widest a column span is taken to be, as HTML caps <c>colspan</c>, so a damaged value cannot blow up the grid.</summary>
    private const int MaxColumnSpan = 1000;

    public static LayoutTree Build(PdfDocument document, IReadOnlyList<PdfPage> pages, LayoutOptions options)
    {
        var textOptions = new TextExtractionOptions
        {
            IncludeHiddenContent = options.IncludeHiddenContent,
            ContentOptions = options.ContentOptions,
        };
        PdfStructureTreeRoot? root = options.Detection == LayoutDetection.AllPages ? null : document.StructureTree;
        var builder = new Builder(document, root);
        var layoutPages = new LayoutPage[pages.Count];
        var geometry = new MarkGeometry(options.IncludeHiddenContent);
        RulingCollector? rulings = options.Detection == LayoutDetection.None ? null : new RulingCollector(options.IncludeHiddenContent);

        // Rulings (for detected tables) are collected beside the text, with the marks' geometry when a structure tree is read too.
        Content.ContentProcessor? companion = root is null ? rulings : rulings is null ? geometry : new Content.CompositeContentProcessor(geometry, rulings);
        GeometryLayout? detector = null;
        try
        {
            return Build(document, pages, options, textOptions, root, builder, layoutPages, geometry, (companion, rulings), ref detector);
        }
        finally
        {
            if (detector is not null)
            {
                GeometryLayout.Return(detector);
            }
        }
    }

    private static LayoutTree Build(
        PdfDocument document, IReadOnlyList<PdfPage> pages, LayoutOptions options, TextExtractionOptions textOptions, PdfStructureTreeRoot? root,
        Builder builder, LayoutPage[] layoutPages, MarkGeometry geometry, (Content.ContentProcessor? Companion, RulingCollector? Rulings) collectors,
        ref GeometryLayout? detector)
    {
        for (int index = 0; index < pages.Count; index++)
        {
            PdfPage page = pages[index];
            TextPage text = page.ExtractText(textOptions, collectors.Companion);
            int pageIndex = document.Pages.IndexOf(page);
            if (options.Detection != LayoutDetection.None && (root is null || (text.Glyphs.Count > 0 && !IsTagged(text))))
            {
                // Spec story 36: a page with no tagged content is laid out from its geometry once every page is known.
                (detector ??= GeometryLayout.Rent()).Analyze(index, page, pageIndex, text, collectors.Rulings);
                continue;
            }

            layoutPages[index] = new LayoutPage(page, pageIndex, text);
            if (root is not null)
            {
                builder.AddPage(layoutPages[index], geometry);
            }
        }

        LayoutNode rootNode = builder.Finish();
        detector?.Finish(rootNode, layoutPages);
        return new LayoutTree(rootNode, layoutPages);
    }

    /// <summary>Whether any of a page's glyphs belongs to a structure element.</summary>
    private static bool IsTagged(TextPage text)
    {
        foreach (ref readonly TextGlyph glyph in text.GlyphSpan)
        {
            if (glyph.StructureElement is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The node kind of a standard structure type, or <see langword="null"/> when it makes no node.</summary>
    private static LayoutNodeKind? KindOf(string type) => type switch
    {
        "Document" or "DocumentFragment" => LayoutNodeKind.Document,
        "Part" or "Sect" or "Div" or "Aside" or "Art" or "BlockQuote" or "Index" => LayoutNodeKind.Section,
        "P" => LayoutNodeKind.Paragraph,
        "H" or "Title" => LayoutNodeKind.Heading,
        "FENote" or "Note" => LayoutNodeKind.Note,
        "Lbl" => LayoutNodeKind.Label,
        "Link" => LayoutNodeKind.Link,
        "Span" or "Em" or "Strong" or "Sub" or "Annot" or "Form" or "Quote" or "Code" or "Reference" or "BibEntry"
            or "Ruby" or "RB" or "RT" or "RP" or "Warichu" or "WT" or "WP" => LayoutNodeKind.Span,
        "L" or "TOC" => LayoutNodeKind.List,
        "LI" or "TOCI" => LayoutNodeKind.ListItem,
        "LBody" => LayoutNodeKind.ListBody,
        "Table" => LayoutNodeKind.Table,
        "TR" => LayoutNodeKind.TableRow,
        "TH" or "TD" => LayoutNodeKind.TableCell,
        "Caption" => LayoutNodeKind.Caption,
        "Figure" or "Formula" => LayoutNodeKind.Figure,
        _ when StandardStructureTypes.IsHn(type) => LayoutNodeKind.Heading,
        _ => null,
    };

    /// <summary>The standard type name of an element in one of the standard namespaces, or <see langword="null"/>.</summary>
    private static string? StandardName(PdfStructureElement element) =>
        element.StandardType is { IsStandard: true } type && type.Namespace.Kind is PdfStructureNamespaceKind.Pdf17 or PdfStructureNamespaceKind.Pdf20 ? type.Name : null;

    /// <summary>What a node collects on one page while it is extracted.</summary>
    private sealed class Accumulator
    {
        public int First = -1;
        public int Last = -1;
        public int Position = int.MaxValue;
        public PdfRectangle? Bounds;

        public void AddGlyph(int index, PdfRectangle bounds)
        {
            if (First < 0 || index < First)
            {
                First = index;
            }

            Last = Math.Max(Last, index);
            AddBounds(bounds);
        }

        public void AddBounds(PdfRectangle bounds) => Bounds = Bounds is { } known ? MarkGeometry.Union(known, bounds) : bounds;

        public void Merge(Accumulator other)
        {
            if (other.First >= 0)
            {
                First = First < 0 ? other.First : Math.Min(First, other.First);
                Last = Math.Max(Last, other.Last);
            }

            Position = Math.Min(Position, other.Position);
            if (other.Bounds is { } bounds)
            {
                AddBounds(bounds);
            }
        }
    }

    /// <summary>A content item of an element: its page, its position in logical content order, and the annotation it is, if any.</summary>
    private readonly record struct Item(LayoutNode Node, CosDictionary Page, int Order, CosDictionary? Annotation);

    private sealed class Builder
    {
        private readonly PdfDocument _document;
        private readonly PdfStructureTreeRoot? _root;
        private readonly LayoutNode _rootNode;

        /// <summary>The node each element's content goes to; <see langword="null"/> for an element left out (an artifact).</summary>
        private readonly Dictionary<CosDictionary, LayoutNode?> _nodes = new(ReferenceEqualityComparer.Instance);

        /// <summary>Every node made, in depth-first pre-order (the root not included).</summary>
        private readonly List<LayoutNode> _order = [];

        /// <summary>The content items on each page, by page object.</summary>
        private readonly Dictionary<CosDictionary, List<Item>> _items = new(ReferenceEqualityComparer.Instance);

        public Builder(PdfDocument document, PdfStructureTreeRoot? root)
        {
            _document = document;
            _root = root;
            PdfStructureElement? documentElement = SingleDocumentElement(root);
            _rootNode = LayoutNode.Create(LayoutNodeKind.Document, documentElement);
            _rootNode.Language = documentElement is null ? ViewReading.Text(document, document.Catalog, StructureNames.Lang) : documentElement.EffectiveLanguage;
            _rootNode.AlternateDescription = documentElement?.AlternateDescription;
            if (root is null)
            {
                return;
            }

            if (_rootNode.StructureElement is { } rootElement)
            {
                _nodes.Add(rootElement.Dictionary, _rootNode);
            }

            foreach (PdfStructureElement element in root.Walk.Elements)
            {
                Visit(element);
            }

            CollectItems(root.Walk);
            foreach (LayoutNode node in _order)
            {
                if (node is LayoutTable table)
                {
                    LayOut(table);
                }
                else if (node is LayoutTableCell cell)
                {
                    FindHeaders(cell);
                }
            }
        }

        public void AddPage(LayoutPage layoutPage, MarkGeometry geometry)
        {
            StructureWalk walk = _root!.Walk;
            TextPage text = layoutPage.Text;
            ReadOnlySpan<TextGlyph> glyphs = text.GlyphSpan;
            int[]? orders = text.ContentOrders;
            var accumulators = new Dictionary<LayoutNode, Accumulator>(ReferenceEqualityComparer.Instance);
            Accumulator Of(LayoutNode node)
            {
                if (!accumulators.TryGetValue(node, out Accumulator? accumulator))
                {
                    accumulator = new Accumulator();
                    accumulators.Add(node, accumulator);
                }

                return accumulator;
            }

            for (int index = 0; index < glyphs.Length; index++)
            {
                if (glyphs[index].StructureElement is { } element && NodeOf(element) is { } node)
                {
                    Of(node).AddGlyph(index, glyphs[index].Quad.Bounds);
                }
            }

            foreach ((ContentItemKey key, PdfRectangle bounds) in geometry.Bounds)
            {
                bool found = key.Mcid >= 0
                    ? walk.TryGetMarkedContent(key.Owner, key.Mcid, out PdfStructureElement? element, out int order)
                    : walk.TryGetObject(key.Owner, out element, out order);
                if (found && NodeOf(element!) is { } node)
                {
                    Accumulator accumulator = Of(node);
                    accumulator.AddBounds(bounds);
                    accumulator.Position = Math.Min(accumulator.Position, PositionOf(orders, glyphs.Length, order));
                }
            }

            PdfPage page = layoutPage.Page;
            if (_items.TryGetValue(page.Dictionary, out List<Item>? items))
            {
                IReadOnlyList<PdfAnnotation>? annotations = null;
                foreach (Item item in items)
                {
                    Accumulator accumulator = Of(item.Node);
                    accumulator.Position = Math.Min(accumulator.Position, PositionOf(orders, glyphs.Length, item.Order));
                    if (item.Annotation is { } dictionary
                        && (annotations ??= page.Annotations).FirstOrDefault(annotation => ReferenceEquals(annotation.Dictionary, dictionary)) is { } annotation)
                    {
                        item.Node.AddAnnotation(annotation);
                        accumulator.AddBounds(annotation.Rect);
                    }
                }
            }

            // §14.8.5.4.3: a figure that paints nothing measurable is placed by its BBox.
            foreach ((LayoutNode node, Accumulator accumulator) in accumulators)
            {
                if (node.Kind == LayoutNodeKind.Figure && accumulator.Bounds is null && node.StructureElement is { } figure
                    && figure.GetAttributeValue(StructureNames.Layout, StructureNames.BBox) is { } value
                    && _document.Resolve(value) is CosArray { Count: 4 } box && Rectangle(box) is { } rectangle)
                {
                    accumulator.Bounds = rectangle;
                }
            }

            // Descendants before ancestors: each node's accumulator is complete when it is merged into its parent's.
            for (int index = _order.Count - 1; index >= 0; index--)
            {
                LayoutNode node = _order[index];
                if (accumulators.TryGetValue(node, out Accumulator? accumulator))
                {
                    Of(node.Parent!).Merge(accumulator);
                }
            }

            foreach (LayoutNode node in _order.Prepend(_rootNode))
            {
                if (accumulators.TryGetValue(node, out Accumulator? accumulator))
                {
                    node.AddExtent(ExtentOf(layoutPage, accumulator));
                }
            }
        }

        public LayoutNode Finish()
        {
            var kept = new HashSet<LayoutNode>(ReferenceEqualityComparer.Instance) { _rootNode };
            foreach (LayoutNode node in _order)
            {
                // Pre-order: the parent's fate is known. An empty cell of a kept row keeps the grid whole.
                if (kept.Contains(node.Parent!) && (node.Extents.Count > 0 || (node.Kind == LayoutNodeKind.TableCell && node.Parent!.Kind == LayoutNodeKind.TableRow)))
                {
                    kept.Add(node);
                }
            }

            foreach (LayoutNode node in _order.Prepend(_rootNode))
            {
                if (kept.Contains(node))
                {
                    node.Prune(kept.Contains);
                    (node as LayoutTableCell)?.KeepHeaders(kept.Contains);
                }
            }

            return _rootNode;
        }

        /// <summary>The structure tree's only top-level element when it is a <c>Document</c> or <c>DocumentFragment</c> (as the walk holds it), else <see langword="null"/>.</summary>
        private static PdfStructureElement? SingleDocumentElement(PdfStructureTreeRoot? root)
        {
            if (root is null)
            {
                return null;
            }

            PdfStructureElement? single = null;
            foreach (PdfStructureElement element in root.Walk.Elements)
            {
                if (element.Parent is null)
                {
                    if (single is not null)
                    {
                        return null;
                    }

                    single = element;
                }
            }

            return single is not null && KindOf(StandardName(single) ?? string.Empty) == LayoutNodeKind.Document ? single : null;
        }

        /// <summary>The node an element's content belongs to, or <see langword="null"/> when it is left out.</summary>
        private LayoutNode? NodeOf(PdfStructureElement element) => _nodes.TryGetValue(element.Dictionary, out LayoutNode? node) ? node : null;

        private void Visit(PdfStructureElement element)
        {
            if (_nodes.ContainsKey(element.Dictionary))
            {
                return;
            }

            LayoutNode? parent = element.Parent is { } above ? (_nodes.TryGetValue(above.Dictionary, out LayoutNode? known) ? known : _rootNode) : _rootNode;
            string? type = StandardName(element);
            if (parent is null || type == "Artifact")
            {
                _nodes.Add(element.Dictionary, null);
                return;
            }

            if (type is null || KindOf(type) is not { } kind)
            {
                // NonStruct, Private, row groups and types with no standard meaning: their content belongs to the node above (Table 365).
                _nodes.Add(element.Dictionary, parent);
                return;
            }

            LayoutNode node = kind switch
            {
                LayoutNodeKind.Heading => new LayoutHeading(element, HeadingLevel(element, type)),
                LayoutNodeKind.Table => new LayoutTable(element),
                LayoutNodeKind.Figure => new LayoutFigure(element, BoundingBox(element)),
                LayoutNodeKind.List => new LayoutList(element, element.GetAttributeValue(StructureNames.List, ListNumbering) is { } value && _document.Resolve(value) is CosName numbering ? numbering.Value : "None"),
                LayoutNodeKind.TableCell => new LayoutTableCell(element, type == "TH", SpanOf(element, RowSpan, int.MaxValue), SpanOf(element, ColSpan, MaxColumnSpan)),
                _ => LayoutNode.Create(kind, element),
            };
            node.Language = element.EffectiveLanguage;
            node.AlternateDescription = element.AlternateDescription;
            parent.Add(node);
            _nodes.Add(element.Dictionary, node);
            _order.Add(node);
        }

        /// <summary>Records every content item of the elements that make or feed a node, by page.</summary>
        private void CollectItems(StructureWalk walk)
        {
            foreach (PdfStructureElement element in walk.Elements)
            {
                if (NodeOf(element) is not { } node)
                {
                    continue;
                }

                foreach (PdfStructureItem child in element.Children)
                {
                    switch (child)
                    {
                        case PdfMarkedContentReference reference when reference.PageObject is { } page && reference.StreamKey is { } stream:
                            if (walk.TryGetMarkedContent(stream, reference.Mcid, out PdfStructureElement? owner, out int order) && owner.Equals(element))
                            {
                                ItemsOn(page).Add(new Item(node, page, order, null));
                            }

                            break;
                        case PdfObjectReference reference when reference.Page is { } page:
                            if (walk.TryGetObject(reference.ReferencedObject, out owner, out order) && owner.Equals(element))
                            {
                                ItemsOn(page.Dictionary).Add(new Item(node, page.Dictionary, order, reference.ReferencedObject as CosDictionary));
                            }

                            break;
                    }
                }
            }
        }

        private List<Item> ItemsOn(CosDictionary page)
        {
            if (!_items.TryGetValue(page, out List<Item>? items))
            {
                items = [];
                _items.Add(page, items);
            }

            return items;
        }

        /// <summary>Places each cell of the table's rows in its grid (Table 384 <c>RowSpan</c>, <c>ColSpan</c>).</summary>
        private static void LayOut(LayoutTable table)
        {
            LayoutNode[] rows = [.. table.Children.Where(static child => child.Kind == LayoutNodeKind.TableRow)];

            // For each column, the number of rows below the current one that a cell from above still occupies.
            var occupied = new List<int>();
            int rowCount = rows.Length;
            int columnCount = 0;
            for (int row = 0; row < rows.Length; row++)
            {
                int column = 0;
                foreach (LayoutTableCell cell in rows[row].Children.OfType<LayoutTableCell>())
                {
                    while (column < occupied.Count && occupied[column] > 0)
                    {
                        column++;
                    }

                    cell.RowSpan = Math.Min(cell.RowSpan, rows.Length - row);
                    cell.Row = row;
                    cell.Column = column;
                    for (int spanned = column; spanned < column + cell.ColumnSpan; spanned++)
                    {
                        if (spanned == occupied.Count)
                        {
                            occupied.Add(0);
                        }

                        occupied[spanned] = Math.Max(occupied[spanned], cell.RowSpan);
                    }

                    column += cell.ColumnSpan;
                    columnCount = Math.Max(columnCount, column);
                }

                for (int index = 0; index < occupied.Count; index++)
                {
                    occupied[index] = Math.Max(0, occupied[index] - 1);
                }
            }

            table.RowCount = rowCount;
            table.ColumnCount = columnCount;
        }

        /// <summary>Resolves the cell's <c>Headers</c> IDs to header cells (Table 384).</summary>
        private void FindHeaders(LayoutTableCell cell)
        {
            if (cell.StructureElement!.GetAttributeValue(StructureNames.Table, Headers) is not { } value || _document.Resolve(value) is not CosArray ids)
            {
                return;
            }

            foreach (CosObject id in ids)
            {
                if (_document.Resolve(id) is CosString name && _root!.FindElementById(name.Bytes) is { } element
                    && NodeOf(element) is LayoutTableCell { IsHeader: true } header && !ReferenceEquals(header, cell))
                {
                    cell.AddHeader(header);
                }
            }
        }

        /// <summary>A cell's span attribute, from 1 to <paramref name="maximum"/>.</summary>
        private int SpanOf(PdfStructureElement element, CosName name, int maximum) =>
            element.GetAttributeValue(StructureNames.Table, name) is { } value && _document.Resolve(value) is CosInteger span
                ? (int)Math.Clamp(span.Value, 1, maximum)
                : 1;

        /// <summary>The level of a heading (Table 366).</summary>
        private static int HeadingLevel(PdfStructureElement element, string type)
        {
            if (type == "Title")
            {
                return 0;
            }

            if (type != "H")
            {
                return int.TryParse(type.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int level) ? level : int.MaxValue;
            }

            // H: the level comes from the nesting of sections within the document or fragment.
            int sections = 0;
            int depth = 0;
            for (PdfStructureElement? above = element.Parent; above is not null && depth <= StructureContext.MaxDepth; above = above.Parent, depth++)
            {
                string? name = StandardName(above);
                if (name is "Document" or "DocumentFragment")
                {
                    break;
                }

                if (name == "Sect")
                {
                    sections++;
                }
            }

            return Math.Max(1, sections);
        }

        private PdfRectangle? Rectangle(CosArray box)
        {
            Span<double> values = stackalloc double[4];
            for (int index = 0; index < 4; index++)
            {
                if (_document.Resolve(box[index]) is not CosNumber number || !double.IsFinite(number.ToDouble()))
                {
                    return null;
                }

                values[index] = number.ToDouble();
            }

            return new PdfRectangle(values[0], values[1], values[2], values[3]);
        }

        /// <summary>The index of the first glyph after content item position <paramref name="order"/> (glyph positions ascend).</summary>
        private static int PositionOf(int[]? orders, int glyphCount, int order)
        {
            if (orders is null)
            {
                return glyphCount;
            }

            int low = 0;
            int high = glyphCount;
            while (low < high)
            {
                int middle = (low + high) >>> 1;
                if (orders[middle] >= 0 && orders[middle] <= order)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }

        /// <summary>A figure element's <c>BBox</c> attribute (§14.8.5.4.3, Table 379), when it is a well-formed rectangle.</summary>
        private PdfRectangle? BoundingBox(PdfStructureElement element) =>
            element.GetAttributeValue(StructureNames.Layout, StructureNames.BBox) is { } value && _document.Resolve(value) is CosArray { Count: 4 } box
                ? Rectangle(box)
                : null;

        private static LayoutExtent ExtentOf(LayoutPage page, Accumulator accumulator)
        {
            ReadOnlySpan<TextGlyph> glyphs = page.Text.GlyphSpan;
            if (accumulator.First >= 0)
            {
                ref readonly TextGlyph first = ref glyphs[accumulator.First];
                ref readonly TextGlyph last = ref glyphs[accumulator.Last];
                return new LayoutExtent
                {
                    Page = page,
                    Bounds = accumulator.Bounds,
                    GlyphStart = accumulator.First,
                    GlyphCount = accumulator.Last - accumulator.First + 1,
                    TextStart = first.TextStart,
                    TextLength = last.TextStart + last.TextLength - first.TextStart,
                };
            }

            int position = Math.Min(accumulator.Position, glyphs.Length);
            return new LayoutExtent
            {
                Page = page,
                Bounds = accumulator.Bounds,
                GlyphStart = position,
                TextStart = position < glyphs.Length ? glyphs[position].TextStart : page.Text.Text.Length,
            };
        }
    }
}
