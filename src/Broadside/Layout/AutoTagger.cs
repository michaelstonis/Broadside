using System.Globalization;
using System.Text;
using Broadside.Annotations;
using Broadside.Content;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Parsing;
using Broadside.Structure;
using Broadside.Text;
using Broadside.Writing;

namespace Broadside.Layout;

/// <summary>
/// Writes a detected layout tree back as a structure tree (issue #191): one rewrite per page wraps runs of the page's own operators
/// in marked content with MCIDs (ISO 32000-2 §14.6, §14.7.5.2) or as artifacts (§14.8.2.2), and the elements are created in reading
/// order through the structure-writing API (#156), so the parent tree and MCIDs are allocated as for any other tagged content.
/// </summary>
/// <remarks>
/// Glyphs of the detected text are matched to the operators that showed them by character code and device origin, both computed by
/// the same interpreter run as the extraction's. Owner runs are wrapped at the shallowest level where they nest properly with
/// <c>BT</c>/<c>ET</c> and existing marked content (§14.6.1); a container holding several owners is entered and its children are
/// wrapped instead. Existing artifact sequences are left as they are.
/// </remarks>
internal sealed class AutoTagger : ContentEditor
{
    // Owners of an operator: 0 none, > 0 a layout node (index + 1), < 0 an artifact.
    private const int HeaderArtifact = -1;
    private const int FooterArtifact = -2;
    private const int LayoutArtifact = -3;
    private const int SplitOperator = -4;
    private const int UnmatchedText = -5;

    private static readonly CosName ListOwner = new("List");
    private static readonly CosName ListNumbering = new("ListNumbering");
    private static readonly CosName TableOwner = new("Table");
    private static readonly CosName RowSpan = new("RowSpan");
    private static readonly CosName ColSpan = new("ColSpan");
    private static readonly CosName Scope = new("Scope");
    private static readonly CosName Headers = new("Headers");
    private static readonly CosName LayoutOwner = new("Layout");
    private static readonly CosName BBox = new("BBox");
    private static readonly CosName Lang = new("Lang");
    private static readonly CosName Annots = new("Annots");
    private static readonly CosName XObject = new("XObject");
    private static readonly CosName ColumnScope = new("Column");
    private static readonly CosName RowScope = new("Row");

    private readonly PdfDocument _document;
    private readonly AutoTaggingOptions _options;
    private readonly PdfStructureTreeRoot _root;
    private readonly Dictionary<LayoutNode, PdfStructureElement> _elements = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<LayoutNode> _expandedTables = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<LayoutTableCell, CosString> _headerIds = new(ReferenceEqualityComparer.Instance);
    private readonly List<PdfStructureElement> _figures = [];
    private readonly List<Diagnostic> _diagnostics = [];
    private readonly Dictionary<LayoutPage, List<(LayoutNode Node, int Start, int Count)>> _extents = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<LayoutHeading, int> _headingLevels = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<CosDictionary, Dictionary<LayoutNode, PdfStructureElement>> _linkElements = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<CosDictionary> _referenced = new(ReferenceEqualityComparer.Instance);
    private LayoutNode? _documentNode;

    // Per page.
    private readonly Dictionary<GlyphKey, EventQueue> _glyphs = [];
    private readonly List<GlyphRecord> _events = [];
    private readonly Dictionary<int, ShowRecord> _shows = [];
    private readonly List<LayoutNode> _pageNodes = [];
    private readonly List<(PdfAnnotation Link, int Parent)> _pageLinks = [];
    private readonly List<(int Operator, PdfRectangle? Bounds)> _graphics = [];
    private readonly List<(int Operator, ContentOperatorCode Code)> _containers = [];
    private readonly List<int> _paints = [];
    private readonly Dictionary<int, (bool Artifact, bool Mcid, byte[] Tag)> _marked = [];
    private LayoutPage? _page;
    private int _formDepth;
    private int _nextId;

    private AutoTagger(PdfDocument document, AutoTaggingOptions options, PdfStructureTreeRoot root)
    {
        _document = document;
        _options = options;
        _root = root;
    }

    /// <inheritdoc/>
    public override ContentEvents Events => ContentEvents.All;

    /// <summary>Tags the document as <see cref="PdfDocument.AutoTag(AutoTaggingOptions)"/> describes.</summary>
    public static AutoTaggingResult Run(PdfDocument document, AutoTaggingOptions options)
    {
        if (document.StructureTree is not null && !options.Force)
        {
            return new AutoTaggingResult(false, null, [], []);
        }

        LayoutTree layout = document.ExtractLayout(new LayoutOptions().WithDetection(options.Force ? LayoutDetection.AllPages : LayoutDetection.UntaggedPages));
        if (document.StructureTree is not null)
        {
            Detach(document);
        }

        PdfStructureTreeRoot root = document.CreateStructureTree();
        document.MarkInfo.Marked = true;
        if (options.Language is { } language)
        {
            document.Catalog[Lang] = new CosString(Encoding.UTF8.GetBytes(language));
        }

        var tagger = new AutoTagger(document, options, root) { _documentNode = layout.Root };
        tagger.Collect(layout.Root);
        tagger.LevelHeadings(layout.Root);
        // Every page is tagged, a page without text (only graphics, or nothing detected) included: its content becomes artifacts and
        // unmatched text, so no content is left neither tagged nor an artifact (ISO 14289-1 §7.1).
        foreach (LayoutPage page in layout.Pages)
        {
            tagger._page = page;
            IndirectAnnotations(document, page.Page);
            page.Page.RewriteContent(tagger);
            tagger.TagAnnotations(page.Page);
        }

        document.PlaceholderAlternateText ??= new(ReferenceEqualityComparer.Instance);
        foreach (PdfStructureElement figure in tagger._figures)
        {
            document.PlaceholderAlternateText[figure.Dictionary] = options.FigureAlternateText;
        }

        return new AutoTaggingResult(true, layout, tagger._figures, tagger._diagnostics);
    }

    /// <inheritdoc/>
    public override void BeginRun(ContentContext context)
    {
        _glyphs.Clear();
        _events.Clear();
        _shows.Clear();
        _graphics.Clear();
        _containers.Clear();
        _paints.Clear();
        _marked.Clear();
        _formDepth = 0;
    }

    /// <inheritdoc/>
    public override void VisitOperator(in ContentOperator op, ContentContext context)
    {
        if (_formDepth == 0 && op.Code is ContentOperatorCode.BeginText or ContentOperatorCode.EndText or ContentOperatorCode.BeginMarkedContent
            or ContentOperatorCode.BeginMarkedContentWithProperties or ContentOperatorCode.EndMarkedContent)
        {
            _containers.Add((CurrentOperator, op.Code));
        }
        else if (_formDepth == 0 && op.Code is ContentOperatorCode.ShowText or ContentOperatorCode.ShowTextAdjusted or ContentOperatorCode.NextLineShowText
            or ContentOperatorCode.NextLineShowTextWithSpacing && op.Operands.Count > 0)
        {
            _shows[CurrentOperator] = ShowRecord.Read(op);
        }
        else if (_formDepth == 0 && op.Code is (>= ContentOperatorCode.Stroke and <= ContentOperatorCode.CloseFillEvenOddAndStroke)
            or ContentOperatorCode.PaintShading or ContentOperatorCode.PaintXObject)
        {
            _paints.Add(CurrentOperator);
        }
    }

    /// <inheritdoc/>
    public override ContentVisit BeginForm(in FormEvent form, ContentContext context)
    {
        _formDepth++;
        return ContentVisit.Enter;
    }

    /// <inheritdoc/>
    public override void EndForm(in FormEvent form, ContentContext context) => _formDepth--;

    /// <inheritdoc/>
    public override void BeginMarkedContent(in MarkedContentEvent mc, ContentContext context)
    {
        if (_formDepth == 0)
        {
            _marked[CurrentOperator] = (mc.Tag.SequenceEqual("Artifact"u8), mc.Mcid is not null, mc.Tag.ToArray());
        }
    }

    /// <inheritdoc/>
    public override void ShowGlyph(in GlyphEvent glyph, ContentContext context)
    {
        _events.Add(new GlyphRecord(CurrentOperator, _formDepth == 0, glyph.SourceIndex, glyph.CodeLength));
        if (glyph.Font is null)
        {
            return;
        }

        var key = new GlyphKey(glyph.CharacterCode, glyph.DeviceOrigin.X, glyph.DeviceOrigin.Y);
        if (!_glyphs.TryGetValue(key, out EventQueue? queue))
        {
            _glyphs[key] = queue = new EventQueue();
        }

        queue.Events.Add(_events.Count - 1);
    }

    /// <inheritdoc/>
    public override void PaintPath(in PathEvent path, ContentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (path.Paint != PathPaint.None && !path.Path.IsEmpty)
        {
            PdfRectangle box = path.Path.Bounds;
            _graphics.Add((CurrentOperator, Quad.FromRectangle(context.State.Ctm, box.Left, box.Bottom, box.Right, box.Top).Bounds));
        }
    }

    /// <inheritdoc/>
    public override void PaintImage(in ImageEvent image, ContentContext context) =>
        _graphics.Add((CurrentOperator, Quad.FromRectangle(image.Ctm, 0, 0, 1, 1).Bounds));

    /// <inheritdoc/>
    public override void PaintShading(in ShadingEvent shading, ContentContext context) => _graphics.Add((CurrentOperator, null));

    /// <inheritdoc/>
    public override void EndRun(ContentContext context)
    {
        LayoutPage page = _page!;
        int count = OperatorCount;
        if (count == 0)
        {
            return;
        }

        var nodes = _extents.TryGetValue(page, out List<(LayoutNode Node, int Start, int Count)>? found) ? found : [];
        int[] owners = new int[count];
        int[] reading = new int[count];
        Array.Fill(reading, -1);
        var pieces = new Dictionary<int, List<Piece>>();
        int[] lastReadings = new int[count];
        Array.Fill(lastReadings, -1);
        AssignGlyphs(page, nodes, owners, reading, lastReadings, pieces);
        AssignGraphics(nodes, owners);

        // Text the layout has no glyph for (hidden by optional content, clipped away, drawn without a font) is still real content:
        // each run of it becomes a paragraph after the page's other elements (ISO 14289-1 §7.1, veraPDF rule 7.1-3).
        foreach (GlyphRecord record in _events)
        {
            if (owners[record.Operator] == 0)
            {
                owners[record.Operator] = UnmatchedText;
            }
        }

        // A show of no glyph at all (an empty string), or a painting operator that painted nothing (hidden by optional content,
        // an empty path), draws nothing: an artifact.
        foreach (int op in _shows.Keys.Concat(_paints))
        {
            if (owners[op] == 0)
            {
                owners[op] = LayoutArtifact;
            }
        }

        // Keys: consecutive operators of one owner whose glyphs follow each other in reading order; -1 for a split operator.
        int[] keys = new int[count];
        var keyOwners = new List<int> { 0 };
        int lastOwner = 0;
        int lastReading = -1;
        for (int index = 0; index < count; index++)
        {
            int owner = owners[index];
            if (owner == SplitOperator)
            {
                keys[index] = -1;
                lastOwner = 0;
                continue;
            }

            if (owner == 0)
            {
                continue;
            }

            bool same = owner == lastOwner && (owner < 0 || reading[index] < 0 || lastReading < 0 || reading[index] == lastReading + 1);
            if (!same)
            {
                keyOwners.Add(owner);
                lastReading = -1;
            }

            keys[index] = keyOwners.Count - 1;
            lastOwner = owner;
            if (lastReadings[index] >= 0)
            {
                lastReading = lastReadings[index];
            }
        }

        var nesting = new Nesting(count, _containers, _marked);
        var units = new List<Unit>();
        nesting.Collect(0, count, keys, (first, last, key) =>
        {
            int anchor = int.MaxValue;
            for (int index = first; index <= last; index++)
            {
                if (keys[index] == key && reading[index] >= 0)
                {
                    anchor = Math.Min(anchor, reading[index]);
                }
            }

            int owner = keyOwners[key];
            bool glyphs = anchor != int.MaxValue;
            if (!glyphs && owner > 0 && owner <= _pageNodes.Count)
            {
                anchor = nodes.Where(entry => ReferenceEquals(entry.Node, NodeOf(owner))).Select(static entry => entry.Start).DefaultIfEmpty(int.MaxValue).Min();
            }

            units.Add(new Unit(first, last, owner, anchor, glyphs, null));
        });

        foreach ((int op, List<Piece> split) in pieces)
        {
            foreach (Piece piece in split)
            {
                units.Add(new Unit(op, op, piece.Owner, piece.Reading < 0 ? int.MaxValue : piece.Reading, piece.Reading >= 0, piece));
            }
        }

        if (_options.Force)
        {
            foreach ((int op, (bool artifact, bool mcid, byte[] tag)) in _marked)
            {
                if (mcid && !artifact)
                {
                    Replace(op, [(byte)'/', .. tag, .. " BMC"u8]);
                }
            }
        }

        units.Sort(static (a, b) => a.Anchor != b.Anchor ? a.Anchor.CompareTo(b.Anchor)
            : a.Glyphs != b.Glyphs ? (a.Glyphs ? -1 : 1)
            : a.First.CompareTo(b.First));
        foreach (Unit unit in units)
        {
            byte[] opening = Opening(page, unit.Owner);
            if (unit.Piece is { } piece)
            {
                piece.Opening = opening;
            }
            else
            {
                Wrap(unit.First, unit.Last, opening, "EMC"u8);
            }
        }

        foreach ((int op, List<Piece> split) in pieces)
        {
            var output = new List<byte>(_shows[op].Prefix);
            foreach (Piece piece in split)
            {
                output.AddRange(piece.Opening!);
                output.Add((byte)'\n');
                output.AddRange(piece.Show);
                output.AddRange("\nEMC\n"u8);
            }

            Replace(op, [.. output]);
        }
    }

    /// <summary>The layout node of an owner number of the current page.</summary>
    private LayoutNode NodeOf(int owner) => _pageNodes[owner - 1];

    /// <summary>Records every node's extents per page, parents before children, so children override in the glyph map.</summary>
    private void Collect(LayoutNode node)
    {
        foreach (LayoutExtent extent in node.Extents)
        {
            if (!_extents.TryGetValue(extent.Page, out List<(LayoutNode Node, int Start, int Count)>? list))
            {
                _extents[extent.Page] = list = [];
            }

            list.Add((node, extent.GlyphStart, extent.GlyphCount));
        }

        foreach (LayoutNode child in node.Children)
        {
            Collect(child);
        }
    }

    /// <summary>
    /// Gives each glyph shown the owner of the detected glyph it matches, then each operator the owner most of its visible glyphs
    /// have (white space only decides when nothing else does). A page-level text-showing operator whose visible glyphs belong to
    /// several owners is split into one show per owner run instead (§9.4.3: consecutive shows place glyphs exactly as one does).
    /// </summary>
    private void AssignGlyphs(LayoutPage page, List<(LayoutNode Node, int Start, int Count)> nodes, int[] owners, int[] reading, int[] lastReadings, Dictionary<int, List<Piece>> pieces)
    {
        _pageNodes.Clear();
        IReadOnlyList<TextGlyph> glyphs = page.Text.Glyphs;
        int[] glyphOwners = new int[glyphs.Count];
        foreach ((LayoutNode node, int start, int length) in nodes)
        {
            int number = _pageNodes.IndexOf(node) + 1;
            if (number == 0)
            {
                _pageNodes.Add(node);
                number = _pageNodes.Count;
            }

            int end = Math.Min(glyphs.Count, start + length);
            for (int index = Math.Max(0, start); index < end; index++)
            {
                glyphOwners[index] = number;
            }
        }

        AssignLinks(page, glyphs, glyphOwners);
        PdfRectangle crop = page.Page.CropBox;
        double middle = (crop.Bottom + crop.Top) / 2;
        int events = _events.Count;
        int[] eventOwners = new int[events];
        int[] eventReading = new int[events];
        bool[] strong = new bool[events];
        Array.Fill(eventReading, -1);
        for (int index = 0; index < glyphs.Count; index++)
        {
            TextGlyph glyph = glyphs[index];
            if (!_glyphs.TryGetValue(new GlyphKey(glyph.CharacterCode, glyph.Origin.X, glyph.Origin.Y), out EventQueue? queue)
                || queue.Next == queue.Events.Count)
            {
                continue;
            }

            int shown = queue.Events[queue.Next++];
            eventOwners[shown] = (glyph.Flags & TextGlyphFlags.Artifact) != 0 ? glyph.Origin.Y > middle ? HeaderArtifact : FooterArtifact : glyphOwners[index];
            eventReading[shown] = eventOwners[shown] > 0 ? index : -1;
            strong[shown] = !page.Text.GetGlyphText(glyph).IsWhiteSpace();
        }

        int first = 0;
        while (first < events)
        {
            int op = _events[first].Operator;
            int end = first + 1;
            while (end < events && _events[end].Operator == op)
            {
                end++;
            }

            if (!Split(op, first, end, eventOwners, eventReading, strong, pieces))
            {
                Vote(op, first, end, eventOwners, eventReading, strong, owners, reading, lastReadings);
            }
            else
            {
                owners[op] = SplitOperator;
            }

            first = end;
        }
    }

    /// <summary>Gives an operator the owner with the most visible glyphs among its glyphs, ties to the one first in reading order.</summary>
    private static void Vote(int op, int first, int end, int[] eventOwners, int[] eventReading, bool[] strong, int[] owners, int[] reading, int[] lastReadings)
    {
        int bestOwner = 0;
        int bestWeight = 0;
        int bestReading = int.MaxValue;
        for (int candidate = first; candidate < end; candidate++)
        {
            int owner = eventOwners[candidate];
            if (owner == 0 || owner == bestOwner)
            {
                continue;
            }

            int weight = 0;
            int firstReading = int.MaxValue;
            for (int index = first; index < end; index++)
            {
                if (eventOwners[index] == owner)
                {
                    weight += strong[index] ? 1 << 16 : 1;
                    if (eventReading[index] >= 0)
                    {
                        firstReading = Math.Min(firstReading, eventReading[index]);
                    }
                }
            }

            if (weight > bestWeight || (weight == bestWeight && firstReading < bestReading))
            {
                bestOwner = owner;
                bestWeight = weight;
                bestReading = firstReading;
            }
        }

        owners[op] = bestOwner;
        reading[op] = bestOwner > 0 && bestReading != int.MaxValue ? bestReading : -1;
        lastReadings[op] = -1;
        for (int index = first; index < end; index++)
        {
            if (bestOwner > 0 && eventOwners[index] == bestOwner)
            {
                lastReadings[op] = Math.Max(lastReadings[op], eventReading[index]);
            }
        }
    }

    /// <summary>Splits a page-level show whose visible glyphs have several owners into runs; false when it has one, or its operands do not match its glyphs.</summary>
    private bool Split(int op, int first, int end, int[] eventOwners, int[] eventReading, bool[] strong, Dictionary<int, List<Piece>> pieces)
    {
        if (!_events[first].PageLevel || !_shows.TryGetValue(op, out ShowRecord? show))
        {
            return false;
        }

        // The piece of each glyph: a run of one owner whose glyphs follow each other in reading order; unmatched glyphs join the run
        // before them (the first, when leading).
        int[] pieceOf = new int[end - first];
        var owners = new List<int>();
        int current = -1;
        int lastReading = -1;
        for (int index = first; index < end; index++)
        {
            int owner = eventOwners[index];
            if (owner != 0 && (current < 0 || owners[current] != owner || (eventReading[index] >= 0 && lastReading >= 0 && eventReading[index] != lastReading + 1)))
            {
                owners.Add(owner);
                current = owners.Count - 1;
            }

            if (owner != 0)
            {
                lastReading = eventReading[index];
            }

            pieceOf[index - first] = Math.Max(current, 0);
        }

        if (owners.Count < 2)
        {
            return false;
        }

        var elements = new List<CosObject>[owners.Count];
        for (int index = 0; index < elements.Length; index++)
        {
            elements[index] = [];
        }

        int shown = first;
        int piece = 0;
        foreach (CosObject element in show.Elements)
        {
            if (element is CosInteger or CosReal)
            {
                elements[piece].Add(element);
                continue;
            }

            if (element is not CosString text)
            {
                return false;
            }

            ReadOnlySpan<byte> bytes = text.Bytes;
            int offset = 0;
            int segment = 0;
            while (offset < bytes.Length)
            {
                if (shown >= end || _events[shown].SourceIndex != offset || _events[shown].CodeLength <= 0)
                {
                    return false;
                }

                int next = pieceOf[shown - first];
                if (next != piece && offset > segment)
                {
                    elements[piece].Add(new CosString(bytes[segment..offset], hexadecimal: true));
                    segment = offset;
                }

                piece = next;
                offset += _events[shown++].CodeLength;
            }

            if (offset > segment)
            {
                elements[piece].Add(new CosString(bytes[segment..Math.Min(offset, bytes.Length)], hexadecimal: true));
            }
        }

        if (shown != end)
        {
            return false;
        }

        var split = new List<Piece>(owners.Count);
        for (int index = 0; index < owners.Count; index++)
        {
            int firstReading = int.MaxValue;
            for (int glyph = first; glyph < end; glyph++)
            {
                if (pieceOf[glyph - first] == index && eventOwners[glyph] == owners[index] && eventReading[glyph] >= 0)
                {
                    firstReading = Math.Min(firstReading, eventReading[glyph]);
                }
            }

            split.Add(new Piece(owners[index], firstReading == int.MaxValue ? -1 : firstReading, ShowBytes(elements[index])));
        }

        pieces[op] = split;
        return true;
    }

    /// <summary>A <c>TJ</c> showing <paramref name="elements"/>: hexadecimal strings and the adjustments between them.</summary>
    private static byte[] ShowBytes(List<CosObject> elements)
    {
        var output = new StringBuilder("[");
        foreach (CosObject element in elements)
        {
            switch (element)
            {
                case CosString text:
                    output.Append('<').Append(Convert.ToHexString(text.Bytes)).Append('>');
                    break;
                case CosInteger integer:
                    output.Append(' ').Append(integer.Value.ToString(CultureInfo.InvariantCulture)).Append(' ');
                    break;
                case CosReal real:
                    output.Append(' ').Append(real.Value.ToString("R", CultureInfo.InvariantCulture)).Append(' ');
                    break;
            }
        }

        return Encoding.ASCII.GetBytes(output.Append("] TJ").ToString());
    }

    /// <summary>Gives graphics inside a detected figure to the figure, other graphics to a layout artifact; glyphs win.</summary>
    private void AssignGraphics(List<(LayoutNode Node, int Start, int Count)> nodes, int[] owners)
    {
        foreach ((int op, PdfRectangle? bounds) in _graphics)
        {
            if (owners[op] != 0)
            {
                continue;
            }

            owners[op] = LayoutArtifact;
            if (bounds is not { } box)
            {
                continue;
            }

            double x = (box.Left + box.Right) / 2;
            double y = (box.Bottom + box.Top) / 2;
            double best = double.MaxValue;
            foreach ((LayoutNode node, _, _) in nodes)
            {
                if (node is LayoutFigure { BoundingBox: { } figure } && x >= figure.Left - 1 && x <= figure.Right + 1 && y >= figure.Bottom - 1
                    && y <= figure.Top + 1 && figure.Width * figure.Height < best)
                {
                    best = figure.Width * figure.Height;
                    owners[op] = _pageNodes.IndexOf(node) + 1;
                }
            }
        }
    }

    /// <summary>The opening of a unit's marked-content sequence, allocating its MCID and creating its element when needed.</summary>
    private byte[] Opening(LayoutPage page, int owner)
    {
        switch (owner)
        {
            case HeaderArtifact or FooterArtifact:
                _document.RequireFeature(VersionedFeatures.ArtifactSubtype);
                return Encoding.ASCII.GetBytes(owner == HeaderArtifact
                    ? "/Artifact <</Type /Pagination /Subtype /Header>> BDC"
                    : "/Artifact <</Type /Pagination /Subtype /Footer>> BDC");
            case LayoutArtifact:
                return "/Artifact <</Type /Layout>> BDC"u8.ToArray();
            case UnmatchedText:
                int unmatched = Ensure(_documentNode!).AddElement("P").AddMarkedContent(page.Page).Mcid;
                return Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"/P <</MCID {unmatched}>> BDC"));
        }

        if (owner > _pageNodes.Count)
        {
            (PdfAnnotation link, int parent) = _pageLinks[owner - _pageNodes.Count - 1];
            int linkMcid = LinkElement(link, NodeOf(parent)).AddMarkedContent(page.Page).Mcid;
            return Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"/Link <</MCID {linkMcid}>> BDC"));
        }

        PdfStructureElement element = Ensure(NodeOf(owner));
        int mcid = element.AddMarkedContent(page.Page).Mcid;
        return Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"/{TypeOf(NodeOf(owner))} <</MCID {mcid}>> BDC"));
    }

    /// <summary>The element of a node, created with its ancestors on first use; a table's rows and cells are created together.</summary>
    private PdfStructureElement Ensure(LayoutNode node)
    {
        if (_elements.TryGetValue(node, out PdfStructureElement? element))
        {
            return element;
        }

        if (node.Kind is LayoutNodeKind.TableRow && node.Parent is LayoutTable table && _expandedTables.Add(table))
        {
            Expand(table);
            if (_elements.TryGetValue(node, out element))
            {
                return element;
            }
        }

        if (node.Kind is LayoutNodeKind.TableCell && node.Parent is { Kind: LayoutNodeKind.TableRow } row)
        {
            Ensure(row);
            if (_elements.TryGetValue(node, out element))
            {
                return element;
            }
        }

        element = node.Parent is null ? _root.AddElement(TypeOf(node)) : Ensure(node.Parent).AddElement(TypeOf(node));
        _elements[node] = element;
        Describe(node, element);
        return element;
    }

    /// <summary>Creates every row and cell of a table in order, so empty cells keep their places, then describes the cells.</summary>
    private void Expand(LayoutTable table)
    {
        PdfStructureElement parent = Ensure(table);
        foreach (LayoutNode row in table.Children)
        {
            if (row.Kind != LayoutNodeKind.TableRow)
            {
                continue;
            }

            PdfStructureElement rowElement = parent.AddElement(TypeOf(row));
            _elements[row] = rowElement;
            foreach (LayoutNode cell in row.Children)
            {
                _elements[cell] = rowElement.AddElement(TypeOf(cell));
            }
        }

        foreach (LayoutNode row in table.Children)
        {
            if (row.Kind == LayoutNodeKind.TableRow)
            {
                foreach (LayoutNode cell in row.Children)
                {
                    Describe(cell, _elements[cell]);
                }
            }
        }
    }

    /// <summary>The standard structure type of a node (ISO 32000-2 §14.8.4, PDF 1.7 namespace).</summary>
    private string TypeOf(LayoutNode node) => node switch
    {
        LayoutHeading heading => string.Create(CultureInfo.InvariantCulture, $"H{(_headingLevels.TryGetValue(heading, out int level) ? level : Math.Clamp(heading.Level, 1, 6))}"),
        LayoutTableCell cell => cell.IsHeader ? "TH" : "TD",
        _ => node.Kind switch
        {
            LayoutNodeKind.Document => "Document",
            LayoutNodeKind.Section => "Sect",
            LayoutNodeKind.List => "L",
            LayoutNodeKind.ListItem => "LI",
            LayoutNodeKind.Label => "Lbl",
            LayoutNodeKind.ListBody => "LBody",
            LayoutNodeKind.Table => "Table",
            LayoutNodeKind.TableRow => "TR",
            LayoutNodeKind.TableCell => "TD",
            LayoutNodeKind.Figure => "Figure",
            LayoutNodeKind.Caption => "Caption",
            LayoutNodeKind.Span => "Span",
            LayoutNodeKind.Link => "Link",
            LayoutNodeKind.Note => "Note",
            _ => "P",
        },
    };

    /// <summary>Writes a node's attributes: list numbering (Table 382), table cell spans, scope and headers (Table 384), figure box (Table 378) and placeholder <c>Alt</c>.</summary>
    private void Describe(LayoutNode node, PdfStructureElement element)
    {
        switch (node)
        {
            case LayoutList list when list.Numbering != "None":
                element.AddAttributes(ListOwner).SetValue(ListNumbering, new CosName(list.Numbering));
                break;
            case LayoutTableCell cell:
                DescribeCell(cell, element);
                break;
            case LayoutFigure figure:
                if (figure.BoundingBox is { } box)
                {
                    element.AddAttributes(LayoutOwner).SetValue(BBox, new CosArray { Number(box.Left), Number(box.Bottom), Number(box.Right), Number(box.Top) });
                }

                element.AlternateDescription = _options.FigureAlternateText;
                _figures.Add(element);
                _diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.FigureAlternateTextPlaceholder,
                    DiagnosticSeverity.Warning,
                    "Auto-tagging gave a figure placeholder alternate text (ISO 32000-2 §14.9.3); replace it with a description of the figure.",
                    objectReference: element.Reference));
                break;
        }
    }

    private void DescribeCell(LayoutTableCell cell, PdfStructureElement element)
    {
        PdfAttributeObject? attributes = null;
        PdfAttributeObject Attributes() => attributes ??= element.AddAttributes(TableOwner);
        if (cell.RowSpan > 1)
        {
            Attributes().SetValue(RowSpan, new CosInteger(cell.RowSpan));
        }

        if (cell.ColumnSpan > 1)
        {
            Attributes().SetValue(ColSpan, new CosInteger(cell.ColumnSpan));
        }

        if (cell.IsHeader)
        {
            bool rowHeader = cell.Parent!.Children.All(static sibling => sibling is LayoutTableCell { IsHeader: true });
            Attributes().SetValue(Scope, rowHeader ? ColumnScope : RowScope);
        }

        if (cell.Headers.Count > 0)
        {
            var ids = new CosArray();
            foreach (LayoutTableCell header in cell.Headers)
            {
                ids.Add(IdOf(header));
            }

            Attributes().SetValue(Headers, ids);
        }
    }

    /// <summary>The ID of a header cell, given to its element on first reference (§14.7.2, Table 355 <c>ID</c>).</summary>
    private CosString IdOf(LayoutTableCell header)
    {
        if (!_headerIds.TryGetValue(header, out CosString? id))
        {
            do
            {
                id = new CosString(Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"autotag-th-{++_nextId}")));
            }
            while (_root.FindElementById(id.Bytes) is not null);

            _headerIds[header] = id;
            Ensure(header).Id = id;
        }

        return id;
    }

    /// <summary>
    /// Gives every heading the level it is written with: detected levels shifted so the shallowest is H1, and never more than one
    /// deeper than the heading before (ISO 32000-2 §14.8.4.3.5; ISO 14289-1 §7.4.2, veraPDF rule 7.4.2-1: H1 first, no skipped level).
    /// </summary>
    private void LevelHeadings(LayoutNode root)
    {
        List<LayoutHeading> headings = [];
        Gather(root);
        int shift = headings.Count == 0 ? 0 : headings.Min(static heading => Math.Clamp(heading.Level, 1, 6)) - 1;
        int previous = 0;
        foreach (LayoutHeading heading in headings)
        {
            previous = Math.Min(Math.Clamp(heading.Level, 1, 6) - shift, previous + 1);
            _headingLevels[heading] = previous;
        }

        void Gather(LayoutNode node)
        {
            if (node is LayoutHeading heading)
            {
                headings.Add(heading);
            }

            foreach (LayoutNode child in node.Children)
            {
                Gather(child);
            }
        }
    }

    /// <summary>
    /// Gives the glyphs inside a link annotation's rectangle an owner of their own: a <c>Link</c> element under the element they belong
    /// to, holding the link text and an object reference to the annotation (ISO 32000-2 §14.8.4.4.2, Table 368; ISO 14289-1 §7.18.5).
    /// </summary>
    private void AssignLinks(LayoutPage page, IReadOnlyList<TextGlyph> glyphs, int[] glyphOwners)
    {
        _pageLinks.Clear();
        int nodes = _pageNodes.Count;
        var numbers = new Dictionary<(int Link, int Parent), int>();
        IReadOnlyList<PdfAnnotation> annotations = page.Page.Annotations;
        for (int link = 0; link < annotations.Count; link++)
        {
            PdfAnnotation annotation = annotations[link];
            if (annotation.Kind != PdfAnnotationKind.Link || annotation.Reference is null)
            {
                continue;
            }

            PdfRectangle rect = annotation.Rect;
            for (int index = 0; index < glyphs.Count; index++)
            {
                int parent = glyphOwners[index];
                if (parent <= 0 || parent > nodes || (glyphs[index].Flags & TextGlyphFlags.Artifact) != 0)
                {
                    continue;
                }

                PdfRectangle box = glyphs[index].Quad.Bounds;
                double x = (box.Left + box.Right) / 2;
                double y = (box.Bottom + box.Top) / 2;
                if (x < rect.Left || x > rect.Right || y < rect.Bottom || y > rect.Top)
                {
                    continue;
                }

                if (!numbers.TryGetValue((link, parent), out int number))
                {
                    _pageLinks.Add((annotation, parent));
                    number = numbers[(link, parent)] = nodes + _pageLinks.Count;
                }

                glyphOwners[index] = number;
            }
        }

        foreach (IGrouping<PdfAnnotation, int> group in Enumerable.Range(0, glyphs.Count)
            .Where(index => glyphOwners[index] > nodes)
            .GroupBy(index => _pageLinks[glyphOwners[index] - nodes - 1].Link))
        {
            if (string.IsNullOrWhiteSpace(group.Key.Contents))
            {
                string text = string.Join(' ', string.Concat(group.Select(index => page.Text.GetGlyphText(glyphs[index]).ToString()))
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                if (text.Length > 0)
                {
                    group.Key.Contents = text;
                }
            }
        }
    }

    /// <summary>The <c>Link</c> element of a link annotation under a node's element; the first one created holds the object reference.</summary>
    private PdfStructureElement LinkElement(PdfAnnotation link, LayoutNode parent)
    {
        if (!_linkElements.TryGetValue(link.Dictionary, out Dictionary<LayoutNode, PdfStructureElement>? byParent))
        {
            _linkElements[link.Dictionary] = byParent = new(ReferenceEqualityComparer.Instance);
        }

        if (!byParent.TryGetValue(parent, out PdfStructureElement? element))
        {
            byParent[parent] = element = Ensure(parent).AddElement("Link");
            Reference(element, link);
        }

        return element;
    }

    /// <summary>
    /// Puts every annotation of a page not yet in the structure tree into it, after the page's content: links in <c>Link</c>, widgets
    /// in <c>Form</c>, the others (printer's marks excepted) in <c>Annot</c>, each through an object reference (ISO 32000-2 §14.8.4.4,
    /// Tables 368 and 370; ISO 14289-1 §7.18.1, §7.18.4, §7.18.5). Alternate descriptions are not invented.
    /// </summary>
    private void TagAnnotations(PdfPage page)
    {
        foreach (PdfAnnotation annotation in page.Annotations.ToArray())
        {
            if (annotation.Reference is null || annotation.Kind == PdfAnnotationKind.PrinterMark || _referenced.Contains(annotation.Dictionary))
            {
                continue;
            }

            string type = annotation.Kind switch
            {
                PdfAnnotationKind.Link => "Link",
                PdfAnnotationKind.Widget => "Form",
                _ => "Annot",
            };
            Reference(Ensure(_documentNode!).AddElement(type), annotation);
        }
    }

    /// <summary>Adds an object reference to an annotation, once; a <c>StructParent</c> left from an old structure tree is dropped first.</summary>
    private void Reference(PdfStructureElement element, PdfAnnotation annotation)
    {
        if (!_referenced.Add(annotation.Dictionary))
        {
            return;
        }

        annotation.Dictionary.Remove(StructureNames.StructParent);
        element.AddObjectReference(annotation.Reference!, annotation.Page);
    }

    /// <summary>Makes every annotation listed directly in a page's <c>Annots</c> array an indirect object, which an object reference needs (§14.7.5.3).</summary>
    private static void IndirectAnnotations(PdfDocument document, PdfPage page)
    {
        if (page.Dictionary.TryGetValue(Annots, out CosObject? annots) && document.Resolve(annots) is CosArray array)
        {
            for (int index = 0; index < array.Count; index++)
            {
                if (array[index] is CosDictionary annotation)
                {
                    array[index] = document.AddObject(annotation);
                }
            }
        }
    }

    private static CosReal Number(double value) => new CosReal(Math.Round(value, 3));

    /// <summary>Detaches an old structure tree for a forced run: the catalog entry and the parent-tree keys of pages, annotations and page XObjects.</summary>
    private static void Detach(PdfDocument document)
    {
        document.Catalog.Remove(StructureNames.StructTreeRoot);
        foreach (PdfPage page in document.Pages)
        {
            page.Dictionary.Remove(StructureNames.StructParents);
            if (page.Dictionary.TryGetValue(Annots, out CosObject? annots) && document.Resolve(annots) is CosArray array)
            {
                foreach (CosObject annotation in array)
                {
                    (document.Resolve(annotation) as CosDictionary)?.Remove(StructureNames.StructParent);
                }
            }

            if (page.Resources is { } resources && resources.TryGetValue(XObject, out CosObject? xobjects) && document.Resolve(xobjects) is CosDictionary named)
            {
                foreach (KeyValuePair<CosName, CosObject> entry in named)
                {
                    if (document.Resolve(entry.Value) is CosStream stream)
                    {
                        stream.Dictionary.Remove(StructureNames.StructParents);
                        stream.Dictionary.Remove(StructureNames.StructParent);
                    }
                }
            }
        }
    }

    private readonly record struct GlyphKey(uint Code, double X, double Y);

    private readonly record struct Unit(int First, int Last, int Owner, int Anchor, bool Glyphs, Piece? Piece);

    /// <summary>A glyph shown: the operator of the rewritten stream it belongs to, and where its code is in the shown string.</summary>
    private readonly record struct GlyphRecord(int Operator, bool PageLevel, int SourceIndex, int CodeLength);

    /// <summary>The glyphs shown with one character code at one origin, in content order, and how many are matched.</summary>
    private sealed class EventQueue
    {
        public List<int> Events { get; } = [];

        public int Next { get; set; }
    }

    /// <summary>One owner's run of a split show: its new <c>TJ</c>, and its opening once allocated.</summary>
    private sealed class Piece(int owner, int reading, byte[] show)
    {
        public int Owner => owner;

        public int Reading => reading;

        public byte[] Show => show;

        public byte[]? Opening { get; set; }
    }

    /// <summary>
    /// A page-level text-showing operator's strings and adjustments, and what its split form writes before the pieces: <c>T*</c>
    /// for <c>'</c>, the spacing and <c>T*</c> for <c>"</c> (§9.4.3, Table 107).
    /// </summary>
    private sealed class ShowRecord(CosObject[] elements, byte[] prefix)
    {
        public CosObject[] Elements => elements;

        public byte[] Prefix => prefix;

        public static ShowRecord Read(in ContentOperator op)
        {
            ContentOperand last = op.Operands[op.Operands.Count - 1];
            CosObject[] elements = op.Code == ContentOperatorCode.ShowTextAdjusted && last.ToCosObject() is CosArray array ? [.. array] : [last.ToCosObject()];
            string prefix = op.Code switch
            {
                ContentOperatorCode.NextLineShowText => "T*\n",
                ContentOperatorCode.NextLineShowTextWithSpacing when op.Operands.Count >= 3 => string.Create(
                    CultureInfo.InvariantCulture, $"{op.Operands[0].Number:R} Tw {op.Operands[1].Number:R} Tc T*\n"),
                _ => string.Empty,
            };
            return new ShowRecord(elements, Encoding.ASCII.GetBytes(prefix));
        }
    }

    /// <summary>
    /// The page's containers (<c>BT</c>/<c>ET</c> and marked content, matched) and how owner runs are wrapped without crossing them
    /// (§14.6.1): unmatched container operators and existing artifact sequences are barriers no run crosses or enters.
    /// </summary>
    private sealed class Nesting
    {
        private readonly int[] _partner;
        private readonly bool[] _barrier;

        public Nesting(int count, List<(int Operator, ContentOperatorCode Code)> containers, Dictionary<int, (bool Artifact, bool Mcid, byte[] Tag)> marked)
        {
            _partner = new int[count];
            Array.Fill(_partner, -1);
            _barrier = new bool[count];
            var open = new Stack<(int Operator, bool Text)>();
            foreach ((int op, ContentOperatorCode code) in containers)
            {
                if (op < 0 || op >= count)
                {
                    continue;
                }

                switch (code)
                {
                    case ContentOperatorCode.BeginText:
                        open.Push((op, true));
                        break;
                    case ContentOperatorCode.BeginMarkedContent or ContentOperatorCode.BeginMarkedContentWithProperties:
                        open.Push((op, false));
                        break;
                    default:
                        bool text = code == ContentOperatorCode.EndText;
                        if (open.Count > 0 && open.Peek().Text == text)
                        {
                            int start = open.Pop().Operator;
                            _partner[start] = op;
                            _partner[op] = start;
                            if (!text && marked.TryGetValue(start, out var info) && info.Artifact)
                            {
                                _barrier[start] = true;
                            }
                        }
                        else
                        {
                            _barrier[op] = true;
                        }

                        break;
                }
            }

            foreach ((int op, _) in open)
            {
                _barrier[op] = true;
                _partner[op] = -1;
            }
        }

        /// <summary>Reports each run to wrap in [<paramref name="start"/>, <paramref name="end"/>) as (first, last, key).</summary>
        public void Collect(int start, int end, int[] keys, Action<int, int, int> wrap)
        {
            int runKey = 0;
            int runFirst = -1;
            int runLast = -1;
            int index = start;
            while (index < end)
            {
                int itemEnd = index;
                int itemKey;
                bool enter = false;
                if (_partner[index] > index)
                {
                    itemEnd = _partner[index];
                    itemKey = _barrier[index] ? -1 : Summary(index + 1, itemEnd, keys);
                    enter = itemKey == -1 && !_barrier[index];
                }
                else
                {
                    itemKey = _barrier[index] ? -1 : keys[index];
                }

                if (itemKey > 0 && itemKey == runKey)
                {
                    runLast = itemEnd;
                }
                else if (itemKey != 0)
                {
                    if (runKey > 0)
                    {
                        wrap(runFirst, runLast, runKey);
                    }

                    runKey = Math.Max(itemKey, 0);
                    runFirst = index;
                    runLast = itemEnd;
                    if (enter)
                    {
                        Collect(index + 1, itemEnd, keys, wrap);
                    }
                }

                index = itemEnd + 1;
            }

            if (runKey > 0)
            {
                wrap(runFirst, runLast, runKey);
            }
        }

        /// <summary>The one key inside a container, 0 for none, -1 for several or a barrier.</summary>
        private int Summary(int start, int end, int[] keys)
        {
            int key = 0;
            for (int index = start; index < end; index++)
            {
                if (_barrier[index])
                {
                    return -1;
                }

                if (keys[index] != 0)
                {
                    if (key != 0 && keys[index] != key)
                    {
                        return -1;
                    }

                    key = keys[index];
                }
            }

            return key;
        }
    }
}
