using System.Globalization;
using Broadside.Content;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Text.Editing;

/// <summary>
/// Rewrites the text-showing operators of a page's or a form's own content that hold the glyphs of matches to replace (issue #641):
/// each such operator becomes a <c>TJ</c> of its other glyphs' codes, with the replacement where the match's first glyph was and the
/// match's other glyphs dropped. Forms painted where matches lie are copied and the copy rewritten by an editor of their own.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.4.3 (Table 107): a number in a <c>TJ</c> array moves the next glyph by −n/1000 of the font size, scaled horizontally,
/// so a dropped glyph becomes a number equal to its advance and the replacement is followed by a number equal to its own advance
/// backwards, and later glyphs keep their places (<see cref="PdfTextFit.KeepPosition"/>). <see cref="PdfTextFit.Compress"/> shows a
/// wider replacement with <c>Tz</c> (§9.3.4) lowered, at least to half the scaling; <see cref="PdfTextFit.ShiftLine"/> leaves the
/// numbers out, so later glyphs of the operator move, and carries the difference to the later operators of the text object that start
/// on the same line after a text positioning operator (§9.4.2), along y for vertical writing. A replacement character the original font
/// cannot show is shown in a fallback font: <c>/Fn size Tf</c>, the run, then the original <c>Tf</c> again, in the same text object, so
/// the rendering mode (clipping modes 4 to 7 included) and the other text state apply to it unchanged (§9.3).
/// </para>
/// <para>
/// §14.6.2 and §14.9.4: the <c>ActualText</c>, <c>Alt</c> and <c>E</c> of a marked-content sequence holding a replaced match are
/// updated, the sequence's operator rewritten with an inline property list. Operators without a match's glyph are copied as they are:
/// with the editor reused, a page with no match in it allocates nothing per glyph or operator.
/// </para>
/// </remarks>
internal sealed class TextEditEditor : ContentEditor
{
    private static readonly CosName ActualText = new("ActualText");
    private static readonly CosName Alt = new("Alt");
    private static readonly CosName Expansion = new("E");
    private static readonly CosName XObject = new("XObject");
    private static readonly CosName FontKey = new("Font");

    private readonly TextEditSession _session;
    private readonly ContentWriter _writer = new();
    private readonly OperandArena _arena = new();
    private readonly Dictionary<PdfFont, (double Descent, double Ascent)> _extents = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, CosName> _fontNames = new(ReferenceEqualityComparer.Instance);
    private readonly List<int> _anchors = [];
    private readonly HashSet<CosName> _privateKinds = [];
    private Matrix _toPage = Matrix.Identity;
    private CosDictionary _owner = null!;
    private CosDictionary? _resources;
    private CosDictionary? _privateResources;

    private GlyphRecord[] _glyphs = new GlyphRecord[64];
    private int _glyphCount;
    private int _textOperator = -1;
    private ContentOperatorCode _textCode;
    private bool _textHit;
    private bool _hasGlyph;
    private TextEditState _state;
    private Matrix _firstMatrix;
    private PathPoint _firstOrigin;

    private PathPoint _shift;
    private PathPoint _lineOrigin;
    private PathPoint _lineDirection;
    private double _lineTolerance;
    private bool _hasLine;
    private bool _shiftPending;

    private MarkedEntry[] _marked = new MarkedEntry[8];
    private int _markedCount;

    // The TJ being written: whether its array and a hexadecimal string are open, and the number waiting to be written.
    private bool _arrayOpen;
    private bool _stringOpen;
    private double _number;
    private bool _hasNumber;

    public TextEditEditor(TextEditSession session) => _session = session;

    public override ContentEvents Events =>
        ContentEvents.Operators | ContentEvents.Glyphs | ContentEvents.Forms | ContentEvents.MarkedContent | ContentEvents.HiddenContent;

    /// <summary>Gets a value indicating whether the last run changed a match's glyphs.</summary>
    public bool ChangedGlyphs { get; private set; }

    /// <summary>Prepares the next run.</summary>
    /// <param name="owner">The dictionary whose <c>Resources</c> the run's names resolve in: the page's, or the form stream's.</param>
    /// <param name="resources">The resources the run uses (a page's may be inherited).</param>
    /// <param name="toPage">The matrix from the run's space to page space: the identity for a page, the CTMs of the <c>Do</c>s that lead to a form.</param>
    public void Configure(CosDictionary owner, CosDictionary? resources, Matrix? toPage = null)
    {
        _toPage = toPage ?? Matrix.Identity;
        _owner = owner;
        _resources = resources;
        _privateResources = null;
        _privateKinds.Clear();
        _fontNames.Clear();
        ChangedGlyphs = false;
    }

    public override void BeginRun(ContentContext context)
    {
        _glyphCount = 0;
        _textOperator = -1;
        _textHit = false;
        _markedCount = 0;
        ResetLine();
    }

    public override void EndRun(ContentContext context)
    {
        Flush();
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
            Flush();
        }

        switch (op.Code)
        {
            case ContentOperatorCode.BeginText or ContentOperatorCode.EndText:
                ResetLine();
                break;
            case ContentOperatorCode.MoveText or ContentOperatorCode.MoveTextSetLeading or ContentOperatorCode.SetTextMatrix or ContentOperatorCode.NextLine:
                _shiftPending |= _hasLine;
                break;
            case ContentOperatorCode.ShowText or ContentOperatorCode.ShowTextAdjusted or ContentOperatorCode.NextLineShowText
                or ContentOperatorCode.NextLineShowTextWithSpacing:
                _shiftPending |= _hasLine && op.Code is ContentOperatorCode.NextLineShowText or ContentOperatorCode.NextLineShowTextWithSpacing;
                _textOperator = index;
                _textCode = op.Code;
                _glyphCount = 0;
                _textHit = false;
                _hasGlyph = false;
                break;
        }
    }

    public override void ShowGlyph(in GlyphEvent glyph, ContentContext context)
    {
        if (context.Depth != 0 || _textOperator < 0)
        {
            return;
        }

        ref readonly GraphicsState state = ref context.State;
        Matrix toPage = glyph.TextMatrix * glyph.Ctm * _toPage;
        if (!_hasGlyph)
        {
            _hasGlyph = true;
            _state = new TextEditState(glyph.Font, glyph.FontDictionary, state.FontSize, state.HorizontalScaling, state.CharacterSpacing, state.WordSpacing, glyph.IsVertical);
            _firstMatrix = toPage;
            _firstOrigin = toPage.Transform(0, 0);
        }

        int match = -1;
        bool anchor = false;
        double adjustment = 0;
        if (glyph.Font is { } font && _session.HasTargets)
        {
            if (!_extents.TryGetValue(font, out (double Descent, double Ascent) extent))
            {
                extent = TextExtractor.ReadExtent(font);
                _extents[font] = extent;
            }

            if (_session.TryTake(GlyphMatching.Key(glyph.CharacterCode, toPage.Transform(0, extent.Descent)), out match))
            {
                TextEditMatch entry = _session.Match(match);
                anchor = !entry.Anchored;
                entry.Anchored = true;
                double size = state.FontSize;
                double scaling = state.HorizontalScaling;
                adjustment = glyph.IsVertical
                    ? (size == 0 ? 0 : -glyph.AdvanceY * 1000 / size)
                    : (size * scaling == 0 ? 0 : -glyph.AdvanceX * 1000 / (size * scaling));
                _textHit = true;
                MarkTouched(match);
            }
        }

        if (_glyphCount == _glyphs.Length)
        {
            Array.Resize(ref _glyphs, _glyphs.Length * 2);
        }

        _glyphs[_glyphCount++] = new GlyphRecord(glyph.SourceIndex, glyph.CodeLength, match, anchor, adjustment, glyph.Adjustment);
    }

    public override ContentVisit BeginForm(in FormEvent form, ContentContext context)
    {
        if (context.Depth != 0 || !_session.HasTargets || form.Form is not { } view)
        {
            return ContentVisit.Skip;
        }

        Matrix outer = context.State.Ctm;
        if (form.HasBoundingBox)
        {
            PdfRectangle box = form.BoundingBox;
            PdfRectangle bounds = Quad.FromRectangle(form.Matrix * outer * _toPage, box.Left, box.Bottom, box.Right, box.Top).Bounds;
            PdfRectangle targets = _session.TargetBounds;
            if (bounds.Right < targets.Left || bounds.Left > targets.Right || bounds.Top < targets.Bottom || bounds.Bottom > targets.Top)
            {
                return ContentVisit.Skip;
            }
        }

        if (_session.RewriteFormCopy(view, outer * _toPage) is { } copy)
        {
            CosName name = AddPrivate(XObject, "Te", copy);
            _writer.Clear();
            _writer.Name(name).Operator("Do");
            Replace(CurrentOperator, _writer.Written);
            ChangedGlyphs = true;
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

        _marked[_markedCount++] = new MarkedEntry(CurrentOperator, replacement, replacement is null ? null : new CosName(mc.Tag.ToArray()));
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

    private void ResetLine()
    {
        _shift = default;
        _hasLine = false;
        _shiftPending = false;
    }

    private void MarkTouched(int match)
    {
        for (int index = 0; index < _markedCount; index++)
        {
            ref MarkedEntry entry = ref _marked[index];
            if (entry.Replacement is not null)
            {
                entry.Matches ??= [];
                if (!entry.Matches.Contains(match))
                {
                    entry.Matches.Add(match);
                }
            }
        }
    }

    /// <summary>Rewrites a marked-content sequence's properties whose replacement text described replaced matches (§14.9.4).</summary>
    private void CloseMarked()
    {
        MarkedEntry entry = _marked[--_markedCount];
        if (entry.Matches is null || entry.Replacement is not { } properties || !_session.Options.UpdateReplacementText)
        {
            return;
        }

        List<TextEditMatch> replaced = [.. entry.Matches.Select(_session.Match).Where(static match => match.Replaced)];
        if (replaced.Count == 0)
        {
            return;
        }

        var copy = new CosDictionary();
        foreach (KeyValuePair<CosName, CosObject> pair in properties)
        {
            copy[pair.Key] = pair.Value;
        }

        if (!_session.UpdateReplacementText(copy, replaced, null))
        {
            return;
        }

        _writer.Clear();
        _writer.Name(entry.Tag!);
        CosWriter.Write(copy, _writer);
        _writer.Write((byte)' ');
        _writer.Operator("BDC");
        Replace(entry.Operator, _writer.Written);
    }

    /// <summary>
    /// Rewrites the text-showing operator that just ran when it holds a match's glyph, or when the line it starts on was shifted by an
    /// earlier replacement (<see cref="PdfTextFit.ShiftLine"/>).
    /// </summary>
    private void Flush()
    {
        int index = _textOperator;
        _textOperator = -1;
        if (index < 0 || !_hasGlyph)
        {
            return;
        }

        double lead = 0;
        if (_shiftPending && _textCode is ContentOperatorCode.ShowText or ContentOperatorCode.ShowTextAdjusted && OnShiftedLine() && _firstMatrix.TryInvert(out Matrix inverse))
        {
            PathPoint shift = inverse.TransformVector(_shift.X, _shift.Y);
            lead = -(_state.Vertical ? shift.Y : shift.X) * 1000;
            _shiftPending = false;
        }

        if (!_textHit && lead == 0)
        {
            return;
        }

        _textHit = false;
        _anchors.Clear();
        bool effective = false;
        for (int glyph = 0; glyph < _glyphCount; glyph++)
        {
            GlyphRecord record = _glyphs[glyph];
            if (record.Match < 0)
            {
                continue;
            }

            TextEditMatch match = _session.Match(record.Match);
            if (record.Anchor)
            {
                match.Plan = _session.Plan(match, _state);
                match.Planned = true;
                if (match.Plan is not null)
                {
                    _anchors.Add(record.Match);
                }
            }

            effective |= match.Effective;
        }

        if (!effective && lead == 0)
        {
            return;
        }

        if (!Rebuild(index, lead, out double delta))
        {
            return;
        }

        foreach (int anchor in _anchors)
        {
            _session.Replaced(_session.Match(anchor));
        }

        ChangedGlyphs |= effective;
        if (_session.Options.Fit == PdfTextFit.ShiftLine && delta != 0)
        {
            PathPoint vector = _state.Vertical ? _firstMatrix.TransformVector(0, delta / 1000) : _firstMatrix.TransformVector(delta / 1000, 0);
            if (!_hasLine)
            {
                PathPoint direction = _state.Vertical ? _firstMatrix.TransformVector(0, 1) : _firstMatrix.TransformVector(1, 0);
                double length = Math.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y));
                PathPoint across = _state.Vertical ? _firstMatrix.TransformVector(1, 0) : _firstMatrix.TransformVector(0, 1);
                if (length > 0)
                {
                    _hasLine = true;
                    _lineOrigin = _firstOrigin;
                    _lineDirection = new PathPoint(direction.X / length, direction.Y / length);
                    _lineTolerance = 0.3 * Math.Sqrt((across.X * across.X) + (across.Y * across.Y));
                }
            }

            _shift = new PathPoint(_shift.X + vector.X, _shift.Y + vector.Y);
        }
    }

    /// <summary>Whether the operator's first glyph lies on the shifted line, after where the shift began.</summary>
    private bool OnShiftedLine()
    {
        if (!_hasLine || (_shift.X == 0 && _shift.Y == 0))
        {
            return false;
        }

        double dx = _firstOrigin.X - _lineOrigin.X, dy = _firstOrigin.Y - _lineOrigin.Y;
        double along = (dx * _lineDirection.X) + (dy * _lineDirection.Y);
        double across = Math.Abs((dx * _lineDirection.Y) - (dy * _lineDirection.X));
        return along > 0 && across <= _lineTolerance;
    }

    /// <summary>
    /// Writes the operator again as a <c>TJ</c> (after the line move and spacing of <c>'</c> and <c>"</c>), the replacements in place
    /// of the matches; <paramref name="delta"/> is how far, in <c>TJ</c> units, later glyphs moved (<see cref="PdfTextFit.ShiftLine"/>).
    /// </summary>
    private bool Rebuild(int index, double lead, out double delta)
    {
        delta = 0;
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
        _arrayOpen = _stringOpen = _hasNumber = false;
        _number = 0;
        switch (_textCode)
        {
            case ContentOperatorCode.NextLineShowText:
                _writer.Operator("T*");
                break;
            case ContentOperatorCode.NextLineShowTextWithSpacing when operands.Count >= 3:
                _writer.Number(operands[operands.Count - 3].Number).Operator("Tw");
                _writer.Number(operands[operands.Count - 2].Number).Operator("Tc");
                _writer.Operator("T*");
                break;
            case ContentOperatorCode.NextLineShowTextWithSpacing:
                return false;
        }

        if (lead != 0)
        {
            Number(lead);
        }

        var cursor = new Cursor();
        if (operands.Count == 0)
        {
            return false;
        }

        ContentOperand last = operands[operands.Count - 1];
        if (_textCode == ContentOperatorCode.ShowTextAdjusted)
        {
            if (last.Kind != ContentOperandKind.Array)
            {
                return false;
            }

            foreach (ContentOperand item in last.Items)
            {
                if (item.Kind == ContentOperandKind.String)
                {
                    if (!AddString(item.Bytes, ref cursor))
                    {
                        return false;
                    }
                }
                else if (item.IsNumber)
                {
                    cursor.Held += item.Number;
                    cursor.HasHeld = true;
                }
            }
        }
        else if (last.Kind != ContentOperandKind.String || !AddString(last.Bytes, ref cursor))
        {
            return false;
        }

        if (cursor.Next != _glyphCount)
        {
            return false;
        }

        if (cursor.HasHeld)
        {
            Number(cursor.Held);
        }

        EndArray();
        delta = cursor.Delta;
        Replace(index, _writer.Written);
        return true;
    }

    private bool AddString(ReadOnlySpan<byte> text, ref Cursor cursor)
    {
        bool shift = _session.Options.Fit == PdfTextFit.ShiftLine;
        int position = 0;
        while (position < text.Length)
        {
            if (cursor.Next >= _glyphCount)
            {
                return false;
            }

            int at = cursor.Next++;
            GlyphRecord glyph = _glyphs[at];
            if (glyph.SourceIndex != position || glyph.CodeLength <= 0 || position + glyph.CodeLength > text.Length)
            {
                return false;
            }

            bool changed = glyph.Match >= 0 && _session.Match(glyph.Match).Effective;
            if (cursor.HasHeld)
            {
                // Under ShiftLine a number between two glyphs of one match is part of the match's width and goes with it.
                if (shift && changed && cursor.LastMatch == glyph.Match)
                {
                    cursor.Delta += cursor.Held;
                }
                else
                {
                    Number(cursor.Held);
                }

                cursor.Held = 0;
                cursor.HasHeld = false;
            }

            if (!changed)
            {
                Code(text.Slice(position, glyph.CodeLength));
            }
            else
            {
                if (glyph.Anchor)
                {
                    WriteReplacement(glyph.Match, at, ref cursor);
                }

                if (shift)
                {
                    cursor.Delta += glyph.Adjustment;
                }
                else
                {
                    Number(glyph.Adjustment);
                }
            }

            cursor.LastMatch = changed ? glyph.Match : -1;
            position += glyph.CodeLength;
        }

        return true;
    }

    /// <summary>Writes a match's replacement at its first glyph, then, unless later glyphs move, a number back to where it started.</summary>
    private void WriteReplacement(int index, int at, ref Cursor cursor)
    {
        TextEditMatch match = _session.Match(index);
        TextEditPlan plan = match.Plan!;
        double factor = 1;
        if (_session.Options.Fit == PdfTextFit.Compress && plan.Units > 0)
        {
            double old = OldUnits(index, at);
            if (old > 0 && plan.Units > old)
            {
                factor = Math.Max(0.5, old / plan.Units);
            }
        }

        if (factor != 1)
        {
            EndArray();
            _writer.Number(_state.Scaling * 100 * factor).Operator("Tz");
        }

        // A run in another font ends the TJ: Tf may not stand inside its array. The original font's runs stay in the TJ they are in.
        PdfCanvasFont? current = null;
        foreach (TextEditRun run in plan.Runs)
        {
            if (!ReferenceEquals(run.Fallback, current))
            {
                EndArray();
                _writer.Name(run.Fallback is { } fallback ? FallbackName(fallback) : OriginalName()).Number(_state.Size).Operator("Tf");
                current = run.Fallback;
            }

            Code(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(run.Codes));
        }

        if (current is not null)
        {
            EndArray();
            _writer.Name(OriginalName()).Number(_state.Size).Operator("Tf");
        }

        if (factor != 1)
        {
            EndArray();
            _writer.Number(_state.Scaling * 100).Operator("Tz");
        }

        if (_session.Options.Fit == PdfTextFit.ShiftLine)
        {
            cursor.Delta += plan.Units;
        }
        else
        {
            Number(plan.Units * factor);
        }
    }

    /// <summary>The width, in <c>TJ</c> units, of a match's glyphs in this operator from its first glyph: their advances and the numbers between them.</summary>
    private double OldUnits(int match, int from)
    {
        double units = 0;
        for (int glyph = from; glyph < _glyphCount; glyph++)
        {
            GlyphRecord record = _glyphs[glyph];
            if (record.Match != match)
            {
                continue;
            }

            units -= record.Adjustment;
            if (glyph > from && _glyphs[glyph - 1].Match == match)
            {
                units -= record.Before;
            }
        }

        return units;
    }

    /// <summary>The name the run's resources give the original font, added (from the font's reference or dictionary) when the font was set by a graphics state.</summary>
    private CosName OriginalName()
    {
        object key = (object?)_state.FontDictionary ?? _state.Font!;
        if (_fontNames.TryGetValue(key, out CosName? known))
        {
            return known;
        }

        if (_session.Document.Resolve(_resources is not null && _resources.TryGetValue(FontKey, out CosObject? fonts) ? fonts : null) is CosDictionary dictionary)
        {
            foreach (KeyValuePair<CosName, CosObject> pair in dictionary)
            {
                if (ReferenceEquals(_session.Document.Resolve(pair.Value), _state.FontDictionary))
                {
                    _fontNames[key] = pair.Key;
                    return pair.Key;
                }
            }
        }

        CosObject value = _state.Font?.Reference is { } reference ? reference : _state.FontDictionary!;
        CosName name = AddPrivate(FontKey, "TeF", value);
        _fontNames[key] = name;
        return name;
    }

    private CosName FallbackName(PdfCanvasFont font)
    {
        if (!_fontNames.TryGetValue(font, out CosName? name))
        {
            name = AddPrivate(FontKey, "TeF", font.Reference);
            _fontNames[font] = name;
        }

        return name;
    }

    /// <summary>
    /// Names <paramref name="value"/> in the run's resources of a kind (<c>Font</c>, <c>XObject</c>): the resources and the kind's
    /// dictionary are copied first, once per run, so other pages and forms that share them keep theirs.
    /// </summary>
    private CosName AddPrivate(CosName kind, string prefix, CosObject value)
    {
        if (_privateResources is null)
        {
            _privateResources = Shallow(_resources);
            _owner[KnownNames.Resources] = _privateResources;
            _resources = _privateResources;
            _privateKinds.Clear();
        }

        if (!_privateKinds.Contains(kind) || _privateResources[kind] is not CosDictionary target)
        {
            target = Shallow(_session.Document.Resolve(_privateResources.TryGetValue(kind, out CosObject? existing) ? existing : null) as CosDictionary);
            _privateResources[kind] = target;
            _privateKinds.Add(kind);
        }

        return Name(target, prefix, value);
    }

    private static CosName Name(CosDictionary dictionary, string prefix, CosObject value)
    {
        for (int number = 1; ; number++)
        {
            var name = new CosName(prefix + number.ToString(CultureInfo.InvariantCulture));
            if (!dictionary.ContainsKey(name))
            {
                dictionary[name] = value;
                return name;
            }
        }
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

    private void BeginArray()
    {
        if (!_arrayOpen)
        {
            _writer.BeginArray();
            _arrayOpen = true;
        }
    }

    private void Code(ReadOnlySpan<byte> code)
    {
        BeginArray();
        FlushNumber();
        if (!_stringOpen)
        {
            _writer.Write((byte)'<');
            _stringOpen = true;
        }

        foreach (byte value in code)
        {
            _writer.Write(Hex(value >> 4));
            _writer.Write(Hex(value & 0xF));
        }
    }

    private void Number(double value)
    {
        BeginArray();
        CloseString();
        _number += value;
        _hasNumber = true;
    }

    private void EndArray()
    {
        if (!_arrayOpen)
        {
            return;
        }

        CloseString();
        FlushNumber();
        _writer.EndArray().Operator("TJ");
        _arrayOpen = false;
    }

    private void CloseString()
    {
        if (_stringOpen)
        {
            _writer.Write((byte)'>');
            _stringOpen = false;
        }
    }

    private void FlushNumber()
    {
        if (_hasNumber)
        {
            CloseString();
            if (Math.Round(_number, 4) != 0)
            {
                _writer.Number(_number);
            }

            _number = 0;
            _hasNumber = false;
        }
    }

    private static byte Hex(int nibble) => (byte)(nibble < 10 ? '0' + nibble : 'A' + nibble - 10);

    /// <param name="SourceIndex">The offset of the glyph's code in its string.</param>
    /// <param name="CodeLength">The number of bytes of its code.</param>
    /// <param name="Match">The match it belongs to, or -1.</param>
    /// <param name="Anchor">Whether it is the first glyph of its match the content shows.</param>
    /// <param name="Adjustment">For a match's glyph, the <c>TJ</c> number that moves as far as its advance.</param>
    /// <param name="Before">The <c>TJ</c> number right before it.</param>
    private readonly record struct GlyphRecord(int SourceIndex, int CodeLength, int Match, bool Anchor, double Adjustment, double Before);

    private record struct MarkedEntry(int Operator, CosDictionary? Replacement, CosName? Tag)
    {
        public List<int>? Matches { get; set; }
    }

    /// <summary>Where the rebuild of a text-showing operator is: the next glyph record, the numbers not yet written, the last match written, how far later glyphs moved.</summary>
    private struct Cursor
    {
        public int Next;
        public double Held;
        public bool HasHeld;
        public int LastMatch;
        public double Delta;

        public Cursor() => LastMatch = -1;
    }
}
