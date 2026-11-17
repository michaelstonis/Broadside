using System.Globalization;
using System.Text;
using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Objects;

namespace Broadside.Layout.Flow;

/// <summary>
/// The cross-references of one <see cref="FlowDocument.Render"/>: the anchors (blocks with an <see cref="FlowBlock.Id"/>, and the headings
/// a table of contents or the outline lists), where each lands in a layout pass, the links to them and the page numbers shown for them.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.3.2.2 (explicit destinations), §12.3.2.4 (named destinations, PDF 1.2), §12.3.3 (document outline) and §12.5.6.5 (link
/// annotations). A flow with page references (<see cref="PageReference"/>, <see cref="TableOfContents"/>) is laid out in measuring passes
/// (<see cref="FlowContext"/> adding no page) until the page numbers it shows are the ones it finds, then drawn with them.
/// </para>
/// <para>
/// Links are drawn before every target is on a page, so a link to a block of the flow gets its destination once the flow is drawn: the
/// block's name when it has an <see cref="FlowBlock.Id"/> (written as a named destination), else an explicit <c>XYZ</c> destination. In
/// a flow tagged in the PDF 2.0 namespace every such destination (links, outline items, named destinations) also carries a structure
/// destination to the block's element (§12.3.2.3, <c>SD</c>), as ISO 14289-2 §8.8 requires.
/// </para>
/// </remarks>
internal sealed class FlowReferences
{
    private static readonly CosName DestKey = new("Dest");
    private static readonly CosName ActionKey = new("A");
    private static readonly CosName ActionTypeKey = new("S");
    private static readonly CosName GoTo = new("GoTo");
    private static readonly CosName DKey = new("D");
    private static readonly CosName SDKey = new("SD");
    private static readonly CosName Xyz = new("XYZ");

    private readonly PdfDocument _document;
    private readonly Dictionary<string, FlowBlock> _ids = new(StringComparer.Ordinal);
    private readonly HashSet<FlowBlock> _tracked = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _external = new(StringComparer.Ordinal);
    private readonly List<(Paragraph Block, int Level)> _headings = [];
    private readonly List<(PdfLinkAnnotation Annotation, FlowBlock Target)> _links = [];
    private Dictionary<FlowBlock, Placement> _placed = new(ReferenceEqualityComparer.Instance);
    private Dictionary<FlowBlock, int> _numbers = new(ReferenceEqualityComparer.Instance);

    private FlowReferences(PdfDocument document)
    {
        _document = document;
    }

    /// <summary>Gets whether something shows page numbers of the flow, so it is measured before it is drawn.</summary>
    public bool NeedsPageNumbers { get; private set; }

    /// <summary>Gets the headings of the flow (paragraphs tagged <c>H1</c> to <c>H6</c>), in flow order, with their levels.</summary>
    public IReadOnlyList<(Paragraph Block, int Level)> Headings => _headings;

    /// <summary>
    /// Finds the anchors, links and page references of <paramref name="blocks"/> (and of the blocks in their list items, table cells,
    /// groups and columns),
    /// reporting duplicate ids and references to ids nothing has; null when the flow has none, so it lays out as before. Also checks what
    /// can only be checked across blocks before anything is added.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Two field blocks have the same name or the document's form already has a field of that name, or a table of contents' font cannot
    /// show a heading it lists.
    /// </exception>
    public static FlowReferences? Scan(IReadOnlyList<FlowBlock> blocks, PdfDocument document, int outlineDepth, List<Diagnostic> diagnostics)
    {
        var references = new FlowReferences(document);
        var scan = new ScanState(diagnostics);
        foreach (FlowBlock block in blocks)
        {
            references.Visit(block, scan);
        }

        foreach (TableOfContents table in scan.Contents)
        {
            table.ValidateEntries(references);
        }

        if (scan.Contents.Count > 0 || outlineDepth > 0)
        {
            foreach ((Paragraph heading, _) in references._headings)
            {
                references._tracked.Add(heading);
            }
        }

        references.CheckTargets(scan);
        references.NeedsPageNumbers = scan.Contents.Count > 0 || scan.PageReferences.Count > 0;
        return references._tracked.Count > 0 || scan.Destinations.Count > 0 || references.NeedsPageNumbers ? references : null;
    }

    /// <summary>The blocks <paramref name="block"/> holds: its list items', groups', columns' or table cells' blocks.</summary>
    private static IEnumerable<FlowBlock> ChildrenOf(FlowBlock block) => block switch
    {
        ListBlock list => list.Items.SelectMany(static item => item.Blocks),
        BlockGroup group => group.Blocks,
        ColumnsBlock columns => columns.Columns.SelectMany(static column => column.Blocks),
        Table table => table.Rows.SelectMany(static row => row.Cells).SelectMany(static cell => cell.Blocks),
        _ => [],
    };

    /// <summary>Records what <paramref name="block"/> and the blocks it holds name, link to and show.</summary>
    private void Visit(FlowBlock block, ScanState scan)
    {
        if (!scan.Seen.Add(block))
        {
            if (block is FieldBlock repeated)
            {
                throw new ArgumentException($"The field block '{repeated.Name}' is in the flow twice; a field is placed once (ISO 32000-2 §12.7.4.2).");
            }

            return;
        }

        if (block.Id is { } id)
        {
            AddAnchor(id, block, scan.Diagnostics);
        }

        switch (block)
        {
            case Paragraph paragraph:
                VisitParagraph(paragraph, scan);
                break;
            case TableOfContents table:
                scan.Contents.Add(table);
                break;
            case FieldBlock field:
                if (!scan.Fields.Add(field.Name) || _document.AcroForm?.FindField(field.Name) is not null)
                {
                    throw new ArgumentException($"The form field name '{field.Name}' is used twice; a field's fully qualified name is unique in its form (ISO 32000-2 §12.7.4.2).");
                }

                break;
        }

        foreach (FlowBlock child in ChildrenOf(block))
        {
            Visit(child, scan);
        }
    }

    private void AddAnchor(string id, FlowBlock block, List<Diagnostic> diagnostics)
    {
        if (_ids.TryAdd(id, block))
        {
            _tracked.Add(block);
            return;
        }

        diagnostics.Add(new Diagnostic(
            LayoutDiagnostics.AnchorDuplicate.Name,
            DiagnosticSeverity.Warning,
            $"Two blocks have the anchor id '{id}' (ISO 32000-2 §12.3.2.4: a name names one destination); the first keeps it."));
    }

    private void VisitParagraph(Paragraph paragraph, ScanState scan)
    {
        int level = LevelOf(paragraph.StructureType);
        if (level > 0)
        {
            _headings.Add((paragraph, level));
        }

        foreach (InlineElement element in paragraph.Content)
        {
            if (element is TextRun { Link: null, Destination: { } destination })
            {
                scan.Destinations.Add(destination);
            }
            else if (element is PageReference reference)
            {
                scan.PageReferences.Add(reference.Id);
            }
        }
    }

    /// <summary>Sorts the link destinations into the flow's anchors and the document's names, reporting each unknown id once.</summary>
    private void CheckTargets(ScanState scan)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (string destination in scan.Destinations)
        {
            if (_ids.ContainsKey(destination))
            {
                continue;
            }

            if (_document.GetNamedDestination(destination) is not null)
            {
                _external.Add(destination);
            }
            else if (reported.Add(destination))
            {
                scan.Diagnostics.Add(NotFound(destination, "A link"));
            }
        }

        foreach (string id in scan.PageReferences)
        {
            if (!_ids.ContainsKey(id) && reported.Add(id))
            {
                scan.Diagnostics.Add(NotFound(id, "A page reference"));
            }
        }
    }

    /// <summary>The heading level of a structure type, <c>H1</c> to <c>H6</c>; 0 for any other.</summary>
    public static int LevelOf(string structureType) =>
        structureType.Length == 2 && structureType[0] == 'H' && structureType[1] is >= '1' and <= '6' ? structureType[1] - '0' : 0;

    /// <summary>Gets whether <paramref name="block"/> (or the paragraph it was copied from) is an anchor whose place is recorded.</summary>
    public bool Tracks(FlowBlock block) => _tracked.Contains(Key(block));

    /// <summary>Records where <paramref name="block"/> starts in this pass: the first time only, as a block continued on the next page begins again.</summary>
    public void Place(FlowBlock block, int pageNumber, PdfPage? page, double left, double top) =>
        _placed.TryAdd(Key(block), new Placement(pageNumber, page, left, top));

    /// <summary>Starts a layout pass: nothing is placed yet, and the page numbers shown are the ones the last pass found.</summary>
    public void BeginPass() => _placed = new Dictionary<FlowBlock, Placement>(ReferenceEqualityComparer.Instance);

    /// <summary>Ends a measuring pass: whether every anchor landed on the page number the pass showed for it. Its numbers are shown next.</summary>
    public bool EndMeasuringPass()
    {
        bool converged = _placed.Count == _numbers.Count;
        var numbers = new Dictionary<FlowBlock, int>(ReferenceEqualityComparer.Instance);
        foreach ((FlowBlock block, Placement placement) in _placed)
        {
            numbers[block] = placement.PageNumber;
            converged &= _numbers.TryGetValue(block, out int shown) && shown == placement.PageNumber;
        }

        _numbers = numbers;
        return converged;
    }

    /// <summary>The text a page reference shows: the page number found for its block, 0 before one is found, <c>?</c> for an unknown id.</summary>
    public string PageNumberText(PageReference reference)
    {
        FlowBlock? target = reference.Target ?? _ids.GetValueOrDefault(reference.Id);
        if (target is null)
        {
            return "?";
        }

        return _numbers.TryGetValue(target, out int number) ? number.ToString(CultureInfo.InvariantCulture) : "0";
    }

    /// <summary>Gets whether <paramref name="run"/>'s destination is something the flow can link to.</summary>
    public bool CanLink(TextRun run) =>
        run.Target is { } target ? _tracked.Contains(target) : run.Destination is { } name && (_ids.ContainsKey(name) || _external.Contains(name));

    /// <summary>Adds a link annotation over <paramref name="bounds"/> for <paramref name="run"/>; null when its destination is unknown.</summary>
    public PdfLinkAnnotation? AddLink(PdfPage page, PdfRectangle bounds, TextRun run)
    {
        FlowBlock? target = run.Target ?? (run.Destination is { } name ? _ids.GetValueOrDefault(name) : null);
        if (target is null && (run.Destination is null || !_external.Contains(run.Destination)))
        {
            return null;
        }

        // The destination is set once the flow is drawn (Finish); until then the link shows its own page.
        PdfLinkAnnotation annotation = page.AddLinkAnnotation(bounds, page);
        if (target is not null)
        {
            _links.Add((annotation, target));
        }
        else
        {
            annotation.Dictionary[DestKey] = TextString(run.Destination!);
        }

        return annotation;
    }

    /// <summary>
    /// After the drawing pass: writes a named destination for each anchor with an id, gives the links their destinations, adds the headings
    /// down to <paramref name="outlineDepth"/> to the outline and returns the anchors by id.
    /// </summary>
    /// <param name="outlineDepth">The deepest heading level the outline lists; 0 for no outline.</param>
    /// <param name="structure">
    /// The structure of a flow tagged in the PDF 2.0 namespace, or <see langword="null"/>: with it, every destination to a tagged block is
    /// also a structure destination (<c>SD</c>) to the block's element, the page destination kept as the go-to action's <c>D</c>.
    /// </param>
    /// <remarks>
    /// ISO 32000-2 §12.3.2.3 (structure destinations, PDF 2.0), §12.3.2.4 (a named destination's value: a dictionary whose <c>D</c> is
    /// the destination and whose <c>SD</c> is a structure destination) and §12.6.4.2 (Table 202, the go-to action's <c>D</c> and
    /// <c>SD</c>). ISO 14289-2 §8.8: every destination within the document is a structure destination.
    /// </remarks>
    public IReadOnlyDictionary<string, FlowAnchor> Finish(int outlineDepth, FlowStructure? structure)
    {
        var anchors = new Dictionary<string, FlowAnchor>(StringComparer.Ordinal);
        var named = new Dictionary<FlowBlock, PdfDestination>(ReferenceEqualityComparer.Instance);
        foreach ((string id, FlowBlock block) in _ids)
        {
            if (_placed.TryGetValue(block, out Placement placement) && placement.Page is { } page)
            {
                anchors[id] = new FlowAnchor(id, page, placement.PageNumber, placement.Left, placement.Top);
                PdfExplicitDestination explicitDestination = Explicit(placement);
                PdfNamedDestination name = _document.SetNamedDestination(id, explicitDestination);
                named[block] = name;
                if (StructureDestination(block, structure) is { } element && name.Name is CosString key && _document.Names?.Dests is { } tree)
                {
                    tree.Set(key, new CosDictionary { [DKey] = explicitDestination.Value, [SDKey] = element });
                }
            }
        }

        foreach ((PdfLinkAnnotation annotation, FlowBlock target) in _links)
        {
            if (DestinationOf(target, named) is { } destination)
            {
                Point(annotation.Dictionary, destination, target, structure);
            }
        }

        if (outlineDepth > 0)
        {
            AddOutline(outlineDepth, named, structure);
        }

        return anchors;
    }

    /// <summary>
    /// Makes <paramref name="dictionary"/> (a link annotation's or an outline item's) go to <paramref name="destination"/>: a <c>Dest</c>
    /// entry, or, when <paramref name="target"/> has a structure element, a go-to action with the destination as <c>D</c> and the structure
    /// destination as <c>SD</c>.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.5.6.5 (Table 176, <c>A</c> and <c>Dest</c>), §12.3.3 (Table 151) and §12.6.4.2 (Table 202).</remarks>
    private void Point(CosDictionary dictionary, PdfDestination destination, FlowBlock target, FlowStructure? structure)
    {
        if (StructureDestination(target, structure) is { } element)
        {
            _ = dictionary.Remove(DestKey);
            dictionary[ActionKey] = new CosDictionary { [ActionTypeKey] = GoTo, [DKey] = destination.Value, [SDKey] = element };
        }
        else
        {
            dictionary[DestKey] = destination.Value;
        }
    }

    /// <summary>
    /// A structure destination to <paramref name="target"/>'s element, with the view of its page destination (<c>XYZ</c> at the top left
    /// of its first line); <see langword="null"/> without a structure, or when the block was not tagged or never placed.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.3.2.3: the destination syntax of Table 149 with the structure element in place of the page.</remarks>
    private CosArray? StructureDestination(FlowBlock target, FlowStructure? structure)
    {
        if (structure?.ElementOf(target) is not { Reference: { } element } || !_placed.TryGetValue(Key(target), out Placement placement) || placement.Page is null)
        {
            return null;
        }

        return [element, Xyz, Coordinate(placement.Left), Coordinate(placement.Top), CosNull.Instance];
    }

    /// <summary>A coordinate as <see cref="PdfDocument.CreateDestination"/> writes it: rounded to hundredths, an integer when whole.</summary>
    private static CosObject Coordinate(double value)
    {
        double rounded = Math.Round(value, 2);
        return double.IsInteger(rounded) ? new CosInteger((long)rounded) : new CosReal(rounded);
    }

    /// <summary>Where a link or outline item to <paramref name="target"/> goes: its name when it has one, else where it is; null when it was never placed.</summary>
    private PdfDestination? DestinationOf(FlowBlock target, Dictionary<FlowBlock, PdfDestination> named)
    {
        if (named.TryGetValue(target, out PdfDestination? name))
        {
            return name;
        }

        return _placed.TryGetValue(target, out Placement placement) && placement.Page is not null ? Explicit(placement) : null;
    }

    /// <summary>Adds the headings down to <paramref name="depth"/> to the document outline, nested by level.</summary>
    private void AddOutline(int depth, Dictionary<FlowBlock, PdfDestination> named, FlowStructure? structure)
    {
        PdfOutline? outline = null;
        var open = new List<(int Level, PdfOutlineItem Item)>();
        foreach ((Paragraph heading, int level) in _headings)
        {
            if (level > depth || DestinationOf(heading, named) is not { } destination)
            {
                continue;
            }

            while (open.Count > 0 && open[^1].Level >= level)
            {
                open.RemoveAt(open.Count - 1);
            }

            string title = TitleOf(heading);
            PdfOutlineItem item;
            if (open.Count > 0)
            {
                item = open[^1].Item.Add(title);
            }
            else
            {
                outline ??= _document.Outline ?? _document.CreateOutline();
                item = outline.Add(title);
            }

            Point(item.Dictionary, destination, heading, structure);
            open.Add((level, item));
        }
    }

    /// <summary>The text a heading is listed by: its text without the placeholders of inline images, spaces collapsed.</summary>
    public static string TitleOf(Paragraph heading)
    {
        var builder = new StringBuilder(heading.Text.Length);
        bool space = false;
        foreach (char character in heading.Text)
        {
            if (character is '￼' or '­')
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static Diagnostic NotFound(string id, string what) => new(
        LayoutDiagnostics.AnchorNotFound.Name,
        DiagnosticSeverity.Warning,
        $"{what} names '{id}', which no block of the flow has as its id and the document has no named destination for (ISO 32000-2 §12.3.2.4); it is shown as plain text.");

    private static FlowBlock Key(FlowBlock block) => block is Paragraph { Origin: { } origin } ? origin : block;

    private PdfExplicitDestination Explicit(Placement placement) =>
        _document.CreateDestination(placement.Page!, PdfDestinationView.Xyz, left: Math.Round(placement.Left, 2), top: Math.Round(placement.Top, 2));

    /// <summary>A text string (§7.9.2.2), as <see cref="PdfDocument.SetNamedDestination(string, PdfExplicitDestination)"/> writes a new name.</summary>
    private static CosString TextString(string text) =>
        new(text.All(static c => c is >= ' ' and <= '~') ? Encoding.ASCII.GetBytes(text) : [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(text)]);

    /// <summary>Where a block starts: the flow's page number, the page (null in a measuring pass) and the top left of its first line.</summary>
    private readonly record struct Placement(int PageNumber, PdfPage? Page, double Left, double Top);

    /// <summary>What a scan collects before it can check it: the links, page references, tables of contents and field names met.</summary>
    private sealed class ScanState(List<Diagnostic> diagnostics)
    {
        public List<Diagnostic> Diagnostics { get; } = diagnostics;

        public List<string> Destinations { get; } = [];

        public List<string> PageReferences { get; } = [];

        public List<TableOfContents> Contents { get; } = [];

        public HashSet<string> Fields { get; } = new(StringComparer.Ordinal);

        public HashSet<FlowBlock> Seen { get; } = new(ReferenceEqualityComparer.Instance);
    }
}
