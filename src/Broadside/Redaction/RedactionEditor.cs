using Broadside.Content;
using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Redaction;

/// <summary>
/// Removes what a page's or a form's own content draws under redaction areas: glyphs from text-showing operators (kept glyphs keep
/// their positions), images, subpaths (rectangles cut exactly), the parts of shadings, and the replacement text of the marked content
/// that held them. Forms painted over an area are copied and the copy rewritten by an editor of their own.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.5.6.23 (Table 195). §9.4.3 (Table 107): a removed glyph becomes a <c>TJ</c> adjustment equal to its advance, so
/// later glyphs do not move; §9.4.4 for the advance; §8.5.2 and §8.5.3 for paths; §8.9.5 and §8.9.7 for images; §8.7.4.2 and
/// §8.5.4 for shadings and clipping; §8.10.1 for forms; §14.6.2 and §14.9.4 for marked content and replacement text.
/// </para>
/// <para>
/// The areas are given in the run's space: default user space for a page, the space the form's run starts in (the form matrix
/// concatenated with the CTM at its <c>Do</c>) for a form. A page with no content under an area makes no edit and, with the editor
/// reused, allocates nothing per glyph or operator.
/// </para>
/// </remarks>
internal sealed class RedactionEditor : ContentEditor
{
    private static readonly CosName ActualText = new("ActualText");
    private static readonly CosName Alt = new("Alt");
    private static readonly CosName Expansion = new("E");
    private static readonly CosName XObject = new("XObject");

    private readonly RedactionSession _session;
    private readonly ContentWriter _writer = new();
    private readonly OperandArena _arena = new();
    private readonly Dictionary<PdfFont, (double Descent, double Ascent)> _extents = new(ReferenceEqualityComparer.Instance);
    private readonly List<int> _mcids = [];
    private readonly List<CosName> _dropped = [];
    private Quad[] _areas = [];
    private int _areaCount;
    private PdfRectangle _union;
    private CosDictionary _owner = null!;
    private CosDictionary? _resources;
    private CosDictionary? _privateXObjects;

    private GlyphRecord[] _glyphs = new GlyphRecord[64];
    private int _glyphCount;
    private int _textOperator = -1;
    private ContentOperatorCode _textCode;
    private bool _textHit;
    private Matrix _textStart;
    private bool _textVertical;

    private int _pathStart = -1;
    private bool _afterPaint;
    private bool _pathInterrupted;

    private MarkedEntry[] _marked = new MarkedEntry[8];
    private int _markedCount;

    public RedactionEditor(RedactionSession session) => _session = session;

    public override ContentEvents Events =>
        ContentEvents.Operators | ContentEvents.Paths | ContentEvents.Glyphs | ContentEvents.Images | ContentEvents.Shadings
        | ContentEvents.Forms | ContentEvents.MarkedContent | ContentEvents.HiddenContent;

    /// <summary>Gets the MCIDs of the marked content whose content the last run removed something from.</summary>
    public IReadOnlyList<int> RemovedMcids => _mcids;

    /// <summary>Gets a value indicating whether the last run removed anything.</summary>
    public bool RemovedAny { get; private set; }

    /// <summary>Prepares the next run.</summary>
    /// <param name="areas">The areas in the run's space, each a convex quadrilateral ordered counter-clockwise.</param>
    /// <param name="owner">The dictionary whose <c>Resources</c> the run's names resolve in: the page's, or the form stream's.</param>
    /// <param name="resources">The resources the run uses (a page's may be inherited).</param>
    public void Configure(ReadOnlySpan<Quad> areas, CosDictionary owner, CosDictionary? resources)
    {
        if (_areas.Length < areas.Length)
        {
            _areas = new Quad[areas.Length];
        }

        areas.CopyTo(_areas);
        _areaCount = areas.Length;
        double left = double.PositiveInfinity, bottom = double.PositiveInfinity, right = double.NegativeInfinity, top = double.NegativeInfinity;
        foreach (Quad area in areas)
        {
            PdfRectangle bounds = area.Bounds;
            left = Math.Min(left, bounds.Left);
            bottom = Math.Min(bottom, bounds.Bottom);
            right = Math.Max(right, bounds.Right);
            top = Math.Max(top, bounds.Top);
        }

        _union = areas.IsEmpty ? default : new PdfRectangle(left, bottom, right, top);
        _owner = owner;
        _resources = resources;
        _privateXObjects = null;
        _mcids.Clear();
        _dropped.Clear();
        RemovedAny = false;
    }

    public override void BeginRun(ContentContext context)
    {
        _glyphCount = 0;
        _textOperator = -1;
        _textHit = false;
        _pathStart = -1;
        _pathInterrupted = false;
        _afterPaint = false;
        _markedCount = 0;
    }

    public override void EndRun(ContentContext context)
    {
        FlushText(context);
        while (_markedCount > 0)
        {
            CloseMarked();
        }
    }

    public override void VisitOperator(in ContentOperator op, ContentContext context)
    {
        if (context.Depth != 0)
        {
            return;
        }

        int index = CurrentOperator;
        if (_textOperator >= 0 && _textOperator != index)
        {
            FlushText(context);
        }

        if (_afterPaint)
        {
            _pathStart = -1;
            _pathInterrupted = false;
            _afterPaint = false;
        }

        switch (op.Code)
        {
            case ContentOperatorCode.MoveTo or ContentOperatorCode.LineTo or ContentOperatorCode.CurveTo
                or ContentOperatorCode.CurveToInitialPointReplicated or ContentOperatorCode.CurveToFinalPointReplicated
                or ContentOperatorCode.ClosePath or ContentOperatorCode.Rectangle:
                if (_pathStart < 0)
                {
                    _pathStart = index;
                }

                break;
            case ContentOperatorCode.Stroke or ContentOperatorCode.CloseAndStroke or ContentOperatorCode.Fill or ContentOperatorCode.FillObsolete
                or ContentOperatorCode.FillEvenOdd or ContentOperatorCode.FillAndStroke or ContentOperatorCode.FillEvenOddAndStroke
                or ContentOperatorCode.CloseFillAndStroke or ContentOperatorCode.CloseFillEvenOddAndStroke or ContentOperatorCode.EndPath:
                _afterPaint = true;
                break;
            case ContentOperatorCode.ShowText or ContentOperatorCode.ShowTextAdjusted or ContentOperatorCode.NextLineShowText
                or ContentOperatorCode.NextLineShowTextWithSpacing:
                _textOperator = index;
                _textCode = op.Code;
                _textStart = context.TextMatrix;
                _textVertical = false;
                _glyphCount = 0;
                _textHit = false;
                _pathInterrupted |= _pathStart >= 0;
                break;
            case ContentOperatorCode.Clip or ContentOperatorCode.ClipEvenOdd:
                break;
            default:
                // A path object is its construction operators, a clip and a painting operator with nothing between (§8.2, Figure 9);
                // a path interrupted by anything else is not removed by operator: it is ended with n and what is left of it painted.
                _pathInterrupted |= _pathStart >= 0;
                break;
        }
    }

    public override void ShowGlyph(in GlyphEvent glyph, ContentContext context)
    {
        if (context.Depth != 0 || _textOperator < 0)
        {
            return;
        }

        bool hit = false;
        if (_areaCount > 0 && glyph.Font is { } font)
        {
            if (!_extents.TryGetValue(font, out (double Descent, double Ascent) extent))
            {
                extent = Text.TextExtractor.ReadExtent(font);
                _extents[font] = extent;
            }

            double width = glyph.HorizontalDisplacement;
            Quad quad = Quad.FromRectangle(glyph.TextMatrix * glyph.Ctm, Math.Min(0, width), extent.Descent, Math.Max(0, width), extent.Ascent);
            hit = HitsArea(quad);
        }

        _textVertical = glyph.IsVertical;
        double adjustment = 0;
        if (hit)
        {
            double size = context.State.FontSize;
            double scaling = context.State.HorizontalScaling;
            if (glyph.IsVertical)
            {
                adjustment = size == 0 ? 0 : -glyph.AdvanceY * 1000 / size;
            }
            else
            {
                adjustment = size * scaling == 0 ? 0 : -glyph.AdvanceX * 1000 / (size * scaling);
            }

            _textHit = true;
            _session.GlyphsRemoved++;
            MarkRemoved();
        }

        if (_glyphCount == _glyphs.Length)
        {
            Array.Resize(ref _glyphs, _glyphs.Length * 2);
        }

        _glyphs[_glyphCount++] = new GlyphRecord(glyph.SourceIndex, glyph.CodeLength, hit, adjustment);
    }

    public override void PaintPath(in PathEvent path, ContentContext context)
    {
        if (context.Depth != 0 || path.Paint == PathPaint.None || _areaCount == 0 || path.Path.IsEmpty)
        {
            return;
        }

        Matrix ctm = context.State.Ctm;
        PdfRectangle bounds = path.Path.Bounds;
        double margin = path.Paint != PathPaint.Fill ? Math.Max(context.State.LineWidth, 0) / 2 : 0;
        Quad device = Quad.FromRectangle(ctm, bounds.Left - margin, bounds.Bottom - margin, bounds.Right + margin, bounds.Top + margin);
        if (!RedactionGeometry.Overlaps(device.Bounds, _union) || !ctm.TryInvert(out Matrix inverse))
        {
            return;
        }

        Span<PdfRectangle> boxes = _areaCount <= 16 ? stackalloc PdfRectangle[_areaCount] : new PdfRectangle[_areaCount];
        for (int index = 0; index < _areaCount; index++)
        {
            boxes[index] = RedactionGeometry.Transform(_areas[index], inverse).Bounds;
        }

        var cutter = new PathCutter(path.Path, path.Paint, path.FillRule, boxes, margin, _writer);
        if (!cutter.Cut(out int removed))
        {
            return;
        }

        _session.PathsRemoved += removed;
        MarkRemoved();
        int paint = CurrentOperator;
        if (path.PendingClip is null && _pathStart >= 0 && !_pathInterrupted)
        {
            for (int index = _pathStart; index < paint; index++)
            {
                if (GetOperator(index).Code is not (ContentOperatorCode.Clip or ContentOperatorCode.ClipEvenOdd))
                {
                    Remove(index);
                }
            }

            Replace(paint, _writer.Written);
        }
        else
        {
            // The clip keeps the path it was given (a clip shows nothing itself); the paint becomes n, then the cut path is painted.
            Span<byte> end = [(byte)'n', (byte)'\n'];
            byte[] text = new byte[end.Length + _writer.Length];
            end.CopyTo(text);
            _writer.Written.CopyTo(text.AsSpan(end.Length));
            Replace(paint, text);
        }
    }

    public override void PaintImage(in ImageEvent image, ContentContext context)
    {
        if (context.Depth != 0 || _areaCount == 0)
        {
            return;
        }

        Quad quad = Quad.FromRectangle(image.Ctm, 0, 0, 1, 1);
        if (!HitsArea(quad))
        {
            return;
        }

        if (!InsideArea(quad) && _session.Options.ImageMode == PdfRedactionImageMode.BlankPixels)
        {
            _session.Report(
                RedactionDiagnostics.RedactionImageRemovedWhole,
                "An image that a redaction area covers in part is removed whole: blanking only its covered samples is not implemented yet (issue #624).",
                image.Reference);
        }

        Remove(CurrentOperator);
        Drop(image.ResourceName);
        _session.ImagesChanged++;
        MarkRemoved();
    }

    public override void PaintShading(in ShadingEvent shading, ContentContext context)
    {
        if (context.Depth != 0 || _areaCount == 0 || !shading.Ctm.TryInvert(out Matrix inverse))
        {
            return;
        }

        // §8.7.4.2: sh fills the current clip. Each area is excluded by an even-odd clip of a large rectangle and the area, so the
        // clips intersect to everything outside every area.
        _writer.Clear();
        _writer.Operator("q");
        Quad outer = RedactionGeometry.Transform(Quad.FromRectangle(Matrix.Identity, -1e6, -1e6, 1e6, 1e6), inverse);
        for (int index = 0; index < _areaCount; index++)
        {
            WritePolygon(outer);
            WritePolygon(RedactionGeometry.Transform(_areas[index], inverse));
            _writer.Operator("W*").Operator("n");
        }

        Wrap(CurrentOperator, CurrentOperator, _writer.Written, "Q"u8);
        MarkRemoved();
    }

    public override ContentVisit BeginForm(in FormEvent form, ContentContext context)
    {
        if (context.Depth != 0 || _areaCount == 0 || form.Form is not { } view)
        {
            return ContentVisit.Skip;
        }

        Matrix outer = context.State.Ctm;
        if (form.HasBoundingBox)
        {
            PdfRectangle box = form.BoundingBox;
            Quad quad = Quad.FromRectangle(form.Matrix * outer, box.Left, box.Bottom, box.Right, box.Top);
            if (!HitsArea(quad))
            {
                return ContentVisit.Skip;
            }

            if (InsideArea(quad))
            {
                Remove(CurrentOperator);
                Drop(form.ResourceName);
                MarkRemoved();
                return ContentVisit.Skip;
            }
        }

        if (_session.FormDepth >= _session.ContentOptions.MaxNestingDepth)
        {
            _session.Report(
                RedactionDiagnostics.RedactionFormRemovedWhole,
                "A form XObject painted over a redaction area is nested too deeply to rewrite; the Do that paints it is removed.",
                form.Reference);
            Remove(CurrentOperator);
            Drop(form.ResourceName);
            MarkRemoved();
            return ContentVisit.Skip;
        }

        if (!outer.TryInvert(out Matrix inverse))
        {
            return ContentVisit.Skip;
        }

        Span<Quad> areas = _areaCount <= 16 ? stackalloc Quad[_areaCount] : new Quad[_areaCount];
        for (int index = 0; index < _areaCount; index++)
        {
            areas[index] = RedactionGeometry.Hull(RedactionGeometry.Transform(_areas[index], inverse));
        }

        if (_session.RewriteFormCopy(view, areas) is { } copy)
        {
            CosName name = AddPrivateXObject(copy);
            _writer.Clear();
            _writer.Name(name).Operator("Do");
            Replace(CurrentOperator, _writer.Written);
            Drop(form.ResourceName);
            MarkRemoved();
        }

        return ContentVisit.Skip;
    }

    public override void BeginMarkedContent(in MarkedContentEvent mc, ContentContext context)
    {
        if (context.Depth != 0)
        {
            return;
        }

        CosDictionary? replacement = null;
        if (mc.Properties is { } named)
        {
            if (named.ContainsKey(ActualText) || named.ContainsKey(Alt) || named.ContainsKey(Expansion))
            {
                replacement = named;
            }
        }
        else if (mc.InlineProperties.Kind == ContentOperandKind.Dictionary && HasReplacement(mc.InlineProperties))
        {
            replacement = mc.InlineProperties.ToCosObject() as CosDictionary;
        }

        if (_markedCount == _marked.Length)
        {
            Array.Resize(ref _marked, _marked.Length * 2);
        }

        _marked[_markedCount++] = new MarkedEntry(CurrentOperator, mc.Mcid, replacement, replacement is null ? null : new CosName(mc.Tag.ToArray()));
    }

    public override void EndMarkedContent(in MarkedContentEvent mc, ContentContext context)
    {
        if (context.Depth == 0 && _markedCount > 0)
        {
            CloseMarked();
        }
    }

    private static bool HasReplacement(ContentOperand dictionary)
    {
        int position = 0;
        foreach (ContentOperand item in dictionary.Items)
        {
            if (position++ % 2 == 0 && (item.IsName("ActualText"u8) || item.IsName("Alt"u8) || item.IsName("E"u8)))
            {
                return true;
            }
        }

        return false;
    }

    private void CloseMarked()
    {
        MarkedEntry entry = _marked[--_markedCount];
        if (!entry.Removed || entry.Replacement is not { } properties)
        {
            return;
        }

        // §14.9.4: the replacement text of content that was removed would still say what it was; the sequence keeps its other entries.
        var kept = new CosDictionary();
        foreach (KeyValuePair<CosName, CosObject> pair in properties)
        {
            if (!pair.Key.Equals(ActualText) && !pair.Key.Equals(Alt) && !pair.Key.Equals(Expansion))
            {
                kept[pair.Key] = pair.Value;
            }
        }

        _writer.Clear();
        _writer.Name(entry.Tag!);
        CosWriter.Write(kept, _writer);
        _writer.Write((byte)' ');
        _writer.Operator("BDC");
        Replace(entry.Operator, _writer.Written);
    }

    private void Drop(ReadOnlySpan<byte> name)
    {
        if (!name.IsEmpty)
        {
            _dropped.Add(new CosName(name.ToArray()));
        }
    }

    private void MarkRemoved()
    {
        RemovedAny = true;
        for (int index = 0; index < _markedCount; index++)
        {
            ref MarkedEntry entry = ref _marked[index];
            if (!entry.Removed)
            {
                entry.Removed = true;
                if (entry.Mcid is int mcid)
                {
                    _mcids.Add(mcid);
                }
            }
        }
    }

    private bool HitsArea(in Quad quad)
    {
        if (!RedactionGeometry.Overlaps(quad.Bounds, _union) && !Degenerate(quad))
        {
            return false;
        }

        for (int index = 0; index < _areaCount; index++)
        {
            if (RedactionGeometry.Intersects(_areas[index], quad))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A zero-width or zero-height box (a glyph with no advance) is tested by the separating axis test alone.</summary>
    private static bool Degenerate(in Quad quad)
    {
        PdfRectangle bounds = quad.Bounds;
        return bounds.Width == 0 || bounds.Height == 0;
    }

    private bool InsideArea(in Quad quad)
    {
        for (int index = 0; index < _areaCount; index++)
        {
            if (RedactionGeometry.Contains(_areas[index], quad))
            {
                return true;
            }
        }

        return false;
    }

    private void WritePolygon(in Quad quad)
    {
        _writer.Number(quad.BottomLeft.X).Number(quad.BottomLeft.Y).Operator("m");
        _writer.Number(quad.BottomRight.X).Number(quad.BottomRight.Y).Operator("l");
        _writer.Number(quad.TopRight.X).Number(quad.TopRight.Y).Operator("l");
        _writer.Number(quad.TopLeft.X).Number(quad.TopLeft.Y).Operator("l");
        _writer.Operator("h");
    }

    /// <summary>
    /// Names <paramref name="reference"/> in the run's XObject resources: the resources and their XObject dictionary are copied first,
    /// once per run, so other pages and forms that share them keep naming the original.
    /// </summary>
    private CosName AddPrivateXObject(CosReference reference)
    {
        CosDictionary xobjects = PrivateXObjects();
        for (int number = 1; ; number++)
        {
            var name = new CosName("Rd" + number.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!xobjects.ContainsKey(name))
            {
                xobjects[name] = reference;
                return name;
            }
        }
    }

    /// <summary>
    /// Removes from the run's XObject resources the names whose <c>Do</c> the run removed or replaced and that the rewritten content no
    /// longer paints, so the image or form they name (still holding what was under an area) becomes unreachable and a full save drops
    /// it. The resources are copied first, as for <see cref="AddPrivateXObject"/>.
    /// </summary>
    /// <param name="content">The rewritten content.</param>
    public void PruneXObjects(ReadOnlySpan<byte> content)
    {
        if (_dropped.Count == 0)
        {
            return;
        }

        var used = new HashSet<CosName>();
        _arena.Clear();
        var reader = new ContentReader(content, _arena);
        while (reader.Next(out ReadOperator op))
        {
            ContentOperands operands = _arena.Operands;
            if (op.Code == ContentOperatorCode.PaintXObject && operands.Count > 0 && operands[operands.Count - 1].Kind == ContentOperandKind.Name)
            {
                used.Add(new CosName(operands[operands.Count - 1].Bytes.ToArray()));
            }

            _arena.Clear();
        }

        foreach (CosName name in _dropped)
        {
            if (!used.Contains(name))
            {
                PrivateXObjects().Remove(name);
            }
        }
    }

    private CosDictionary PrivateXObjects()
    {
        if (_privateXObjects is null)
        {
            PdfDocument document = _session.Document;
            CosDictionary resources = Shallow(_resources);
            CosDictionary xobjects = Shallow(document.Resolve(resources.TryGetValue(XObject, out CosObject? value) ? value : null) as CosDictionary);
            resources[XObject] = xobjects;
            _owner[KnownNames.Resources] = resources;
            _resources = resources;
            _privateXObjects = xobjects;
        }

        return _privateXObjects;
    }

    private static CosDictionary Shallow(CosDictionary? source)
    {
        var copy = new CosDictionary();
        if (source is not null)
        {
            foreach (KeyValuePair<CosName, CosObject> pair in source)
            {
                copy[pair.Key] = pair.Value;
            }
        }

        return copy;
    }

    /// <summary>
    /// Replaces the text-showing operator that just ran, when glyphs of it were under an area, with a <c>TJ</c> of the other glyphs'
    /// codes in which each removed glyph is an adjustment of its advance (Table 107: a number moves the next glyph by −n/1000 of the
    /// font size, scaled horizontally). <c>'</c> and <c>"</c> keep their line move and spacing.
    /// </summary>
    private void FlushText(ContentContext context)
    {
        int index = _textOperator;
        _textOperator = -1;
        if (index < 0 || !_textHit)
        {
            return;
        }

        _textHit = false;
        if (!RebuildText(index))
        {
            _session.Report(
                RedactionDiagnostics.RedactionTextRemovedWhole,
                "A text-showing operator with glyphs under a redaction area could not be rebuilt without them; all its glyphs are removed.",
                null);
            ReplaceWithAdvance(index, context);
        }
    }

    /// <summary>
    /// Replaces a text-showing operator by one that shows nothing and moves as far as it did (a <c>TJ</c> of one adjustment, after the
    /// line move of <c>'</c> and <c>"</c>), so the glyphs after it stay where they were: the move is read from the text matrix the
    /// operator started from and the one it left.
    /// </summary>
    private void ReplaceWithAdvance(int index, ContentContext context)
    {
        ref readonly GraphicsState state = ref context.State;
        _writer.Clear();
        Matrix start = _textStart;
        if (_textCode is ContentOperatorCode.NextLineShowText or ContentOperatorCode.NextLineShowTextWithSpacing)
        {
            if (_textCode == ContentOperatorCode.NextLineShowTextWithSpacing)
            {
                _writer.Number(state.WordSpacing).Operator("Tw").Number(state.CharacterSpacing).Operator("Tc");
            }

            _writer.Operator("T*");
            start = context.TextLineMatrix;
        }

        double adjustment = 0;
        if (start.TryInvert(out Matrix inverse))
        {
            Matrix move = context.TextMatrix * inverse;
            double size = state.FontSize;
            double scaling = state.HorizontalScaling;
            adjustment = _textVertical
                ? (size == 0 ? 0 : -move.F * 1000 / size)
                : (size * scaling == 0 ? 0 : -move.E * 1000 / (size * scaling));
        }

        _writer.Write((byte)'[');
        _writer.Number(adjustment);
        _writer.Write((byte)']');
        _writer.Write((byte)' ');
        _writer.Operator("TJ");
        Replace(index, _writer.Written);
    }

    private bool RebuildText(int index)
    {
        ContentOperatorRange range = GetOperator(index);
        ReadOnlySpan<byte> source = Content.Span.Slice(range.Offset, range.Length);
        _arena.Clear();
        var reader = new ContentReader(source, _arena);
        if (!reader.Next(out ReadOperator read) || read.Code != _textCode)
        {
            return false;
        }

        ContentOperands operands = _arena.Operands;
        _writer.Clear();
        var builder = new AdjustedTextBuilder(_writer);
        int glyph = 0;
        switch (_textCode)
        {
            case ContentOperatorCode.ShowText when operands.Count >= 1:
                if (!builder.AddString(operands[operands.Count - 1].Bytes, _glyphs.AsSpan(0, _glyphCount), ref glyph))
                {
                    return false;
                }

                break;
            case ContentOperatorCode.NextLineShowText when operands.Count >= 1:
                _writer.Operator("T*");
                if (!builder.AddString(operands[operands.Count - 1].Bytes, _glyphs.AsSpan(0, _glyphCount), ref glyph))
                {
                    return false;
                }

                break;
            case ContentOperatorCode.NextLineShowTextWithSpacing when operands.Count >= 3:
                _writer.Number(operands[operands.Count - 3].Number).Operator("Tw");
                _writer.Number(operands[operands.Count - 2].Number).Operator("Tc");
                _writer.Operator("T*");
                if (!builder.AddString(operands[operands.Count - 1].Bytes, _glyphs.AsSpan(0, _glyphCount), ref glyph))
                {
                    return false;
                }

                break;
            case ContentOperatorCode.ShowTextAdjusted when operands.Count >= 1 && operands[operands.Count - 1].Kind == ContentOperandKind.Array:
                foreach (ContentOperand item in operands[operands.Count - 1].Items)
                {
                    if (item.Kind == ContentOperandKind.String)
                    {
                        if (!builder.AddString(item.Bytes, _glyphs.AsSpan(0, _glyphCount), ref glyph))
                        {
                            return false;
                        }
                    }
                    else if (item.IsNumber)
                    {
                        builder.AddNumber(item.Number);
                    }
                }

                break;
            default:
                return false;
        }

        if (glyph != _glyphCount)
        {
            return false;
        }

        builder.End();
        Replace(index, _writer.Written);
        return true;
    }

    /// <param name="SourceIndex">The offset of the glyph's code in its string.</param>
    /// <param name="CodeLength">The number of bytes of its code.</param>
    /// <param name="Removed">Whether it is under an area.</param>
    /// <param name="Adjustment">For a removed glyph, the <c>TJ</c> number that moves as far as its advance.</param>
    internal readonly record struct GlyphRecord(int SourceIndex, int CodeLength, bool Removed, double Adjustment);

    private record struct MarkedEntry(int Operator, int? Mcid, CosDictionary? Replacement, CosName? Tag)
    {
        public bool Removed { get; set; }
    }

    /// <summary>Writes the <c>[…] TJ</c> that replaces a text-showing operator: codes as hexadecimal strings, adjustments merged.</summary>
    private ref struct AdjustedTextBuilder(ContentWriter writer)
    {
        private bool _started;
        private bool _stringOpen;
        private double _pending;
        private bool _hasPending;

        public bool AddString(ReadOnlySpan<byte> text, ReadOnlySpan<GlyphRecord> glyphs, ref int next)
        {
            Start();
            int position = 0;
            while (position < text.Length)
            {
                if (next >= glyphs.Length)
                {
                    return false;
                }

                GlyphRecord glyph = glyphs[next++];
                if (glyph.SourceIndex != position || glyph.CodeLength <= 0 || position + glyph.CodeLength > text.Length)
                {
                    return false;
                }

                if (glyph.Removed)
                {
                    AddNumber(glyph.Adjustment);
                }
                else
                {
                    AddCode(text.Slice(position, glyph.CodeLength));
                }

                position += glyph.CodeLength;
            }

            return true;
        }

        public void AddNumber(double value)
        {
            Start();
            CloseString();
            _pending += value;
            _hasPending = true;
        }

        public void End()
        {
            Start();
            CloseString();
            FlushNumber();
            writer.Write((byte)']');
            writer.Write((byte)' ');
            writer.Operator("TJ");
        }

        private void AddCode(ReadOnlySpan<byte> code)
        {
            FlushNumber();
            if (!_stringOpen)
            {
                writer.Write((byte)'<');
                _stringOpen = true;
            }

            foreach (byte value in code)
            {
                writer.Write(Hex(value >> 4));
                writer.Write(Hex(value & 0xF));
            }
        }

        private static byte Hex(int nibble) => (byte)(nibble < 10 ? '0' + nibble : 'A' + nibble - 10);

        private void Start()
        {
            if (!_started)
            {
                writer.Write((byte)'[');
                _started = true;
            }
        }

        private void CloseString()
        {
            if (_stringOpen)
            {
                writer.Write((byte)'>');
                _stringOpen = false;
            }
        }

        private void FlushNumber()
        {
            if (_hasPending)
            {
                CloseString();
                writer.Number(_pending);
                _pending = 0;
                _hasPending = false;
            }
        }
    }
}
