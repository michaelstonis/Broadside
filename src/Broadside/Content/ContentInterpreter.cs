using System.Buffers;
using System.Globalization;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Content;

/// <summary>
/// The one content interpreter: reads a content stream, runs its operators through one graphics-state machine and reports the
/// result to a <see cref="ContentProcessor"/>. Single-threaded; instances are reused per thread, so a run allocates nothing per
/// operator once its buffers are warm.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.8.2 (content streams, Table 33 compatibility sections), §8.2 (graphics objects, Figure 9), §8.4 (graphics
/// state), §8.5 (paths and clipping). Dispatch is split by operator category into partial files (<c>.State</c>, <c>.Path</c>,
/// <c>.Text</c>, <c>.Color</c>, <c>.Shading</c>, <c>.XObject</c>, <c>.MarkedContent</c>) so that later issues each edit their own.
/// </para>
/// <para>
/// Lenient: malformed content is repaired as described on each operator and recorded once per kind of deviation; nothing throws
/// except in strict mode, where the document's diagnostic sink throws the first deviation of severity Warning or above.
/// </para>
/// </remarks>
internal sealed partial class ContentInterpreter
{
    /// <summary>The options a page run uses when the caller passes none.</summary>
    internal static readonly ContentOptions DefaultOptions = new();

    private const int CancellationInterval = 1024;

    [ThreadStatic]
    private static ContentInterpreter? _cached;

    private readonly OperandArena _arena = new();
    private readonly PathBuilder _path = new();
    private readonly ContentContext _context;
    private readonly List<(int Start, CosReference? Reference)> _parts = [];
    private GraphicsState[] _states = ArrayPool<GraphicsState>.Shared.Rent(16);
    private ContentProcessor _processor = null!;
    private ContentEvents _events;
    private DiagnosticSink? _diagnostics;
    private CosReference? _fallbackReference;
    private ulong _reported;
    private int _depth;
    private int _floor;
    private int _maxSaveDepth;
    private int _ignoredSaves;
    private int _compatibilityDepth;
    private int _operatorCount;
    private int _part;
    private bool _inText;
    private FillRule? _pendingClip;
    private bool _busy;

    private ContentInterpreter() => _context = new ContentContext(this);

    /// <summary>Gets the current graphics state.</summary>
    public ref GraphicsState State => ref _states[_depth];

    /// <summary>Gets the number of saved states above the run's initial state.</summary>
    public int StateDepth => _depth;

    /// <summary>Gets the clip nodes of the run.</summary>
    public ClipArena Clips { get; } = new();

    /// <summary>Gets the content stream the current operator is in.</summary>
    public CosReference? CurrentStream => _parts.Count > 0 ? _parts[_part].Reference : null;

    /// <summary>Gets the index of the part the current operator is in.</summary>
    public int CurrentPartIndex => _part;

    /// <summary>Runs a page's content (§7.7.3.3, Table 31): its <c>Contents</c> stream, or its array of streams read as one.</summary>
    /// <param name="page">The page.</param>
    /// <param name="document">The document of the page.</param>
    /// <param name="processor">The processor.</param>
    /// <param name="options">Limits and cancellation.</param>
    public static void RunPage(PdfPage page, PdfDocument document, ContentProcessor processor, ContentOptions options)
    {
        ContentInterpreter interpreter = Rent();
        try
        {
            interpreter.RunPageCore(page, document, processor, options);
        }
        finally
        {
            interpreter.Release();
        }
    }

    /// <summary>
    /// Runs decoded content bytes as the top-level stream of a page run; for benchmarks and fuzzing, which have content without a
    /// file around it.
    /// </summary>
    internal static void RunBytes(ReadOnlySpan<byte> content, PdfDocument document, ContentProcessor processor, ContentOptions options)
    {
        ContentInterpreter interpreter = Rent();
        try
        {
            interpreter.Begin(document, page: null, processor, options, document.Pages.Count > 0 ? document.Pages[0].Reference : null);
            interpreter._parts.Add((0, null));
            interpreter.Execute(content);
            interpreter.End();
        }
        finally
        {
            interpreter.Release();
        }
    }

    private static ContentInterpreter Rent()
    {
        ContentInterpreter? interpreter = _cached;
        if (interpreter is null || interpreter._busy)
        {
            // A run started from inside another run's callback on this thread gets an interpreter of its own.
            interpreter = new ContentInterpreter();
        }

        interpreter._busy = true;
        return interpreter;
    }

    private void RunPageCore(PdfPage page, PdfDocument document, ContentProcessor processor, ContentOptions options)
    {
        Begin(document, page, processor, options, page.Reference);
        byte[]? rented = null;
        try
        {
            ReadOnlyMemory<byte> content = ReadContents(page, document, ref rented);
            Execute(content.Span);
            End();
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    /// <summary>Decodes the page's content; parts of an array are joined with a line feed between them (Table 31).</summary>
    private ReadOnlyMemory<byte> ReadContents(PdfPage page, PdfDocument document, ref byte[]? rented)
    {
        if (!page.Dictionary.TryGetValue(KnownNames.Contents, out CosObject? entry))
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        switch (document.Resolve(entry))
        {
            case CosNull:
                return ReadOnlyMemory<byte>.Empty;
            case CosStream stream:
                _parts.Add((0, entry as CosReference));
                return document.DecodeStream(stream);
            case CosArray { Count: > 0 } array:
                return ReadParts(array, document, ref rented);
            case CosArray:
                Report(ContentIssue.StreamInvalid, -1, "The page's Contents array is empty; the page has no content.");
                return ReadOnlyMemory<byte>.Empty;
            default:
                Report(ContentIssue.StreamInvalid, -1, "The page's Contents is neither a stream nor an array of streams; the page has no content.");
                return ReadOnlyMemory<byte>.Empty;
        }
    }

    private ReadOnlyMemory<byte> ReadParts(CosArray array, PdfDocument document, ref byte[]? rented)
    {
        var decoded = new ReadOnlyMemory<byte>[array.Count];
        int total = 0;
        for (int index = 0; index < array.Count; index++)
        {
            if (document.Resolve(array[index]) is CosStream stream)
            {
                decoded[index] = document.DecodeStream(stream);
                total += decoded[index].Length + 1;
            }
            else
            {
                Report(ContentIssue.StreamInvalid, -1, $"Element {index} of the page's Contents array is not a stream; skipped.");
            }
        }

        rented = ArrayPool<byte>.Shared.Rent(Math.Max(1, total));
        int position = 0;
        for (int index = 0; index < array.Count; index++)
        {
            if (document.Resolve(array[index]) is CosStream)
            {
                _parts.Add((position, array[index] as CosReference));
                decoded[index].Span.CopyTo(rented.AsSpan(position));
                position += decoded[index].Length;
                rented[position++] = (byte)'\n';
            }
        }

        if (_parts.Count == 0)
        {
            _parts.Add((0, null));
        }

        return rented.AsMemory(0, position);
    }

    private void Begin(PdfDocument document, PdfPage? page, ContentProcessor processor, ContentOptions options, CosReference? fallbackReference)
    {
        _processor = processor;
        _events = processor.Events;
        _diagnostics = document.DiagnosticSink;
        _fallbackReference = fallbackReference;
        _reported = 0;
        _depth = 0;
        _floor = 0;
        _ignoredSaves = 0;
        _compatibilityDepth = 0;
        _operatorCount = 0;
        _part = 0;
        _inText = false;
        _pendingClip = null;
        IgnoresColorOperators = false;
        _maxSaveDepth = options.MaxSaveDepth;
        _arena.Limit = options.MaxOperands;
        _path.Accumulate = (_events & (ContentEvents.Paths | ContentEvents.Clips)) != 0;
        _states[0] = GraphicsState.CreateInitial();
        _parts.Clear();
        _context.RunKind = ContentRunKind.Page;
        _context.Depth = 0;
        _context.Document = document;
        _context.Page = page;
        _context.Resources = page?.Resources;
        _context.StreamBaseMatrix = Matrix.Identity;
        _context.IsHidden = false;
        _context.CancellationToken = options.CancellationToken;
        options.CancellationToken.ThrowIfCancellationRequested();
        processor.BeginRun(_context);
    }

    private void End()
    {
        FinishStream();
        _processor.EndRun(_context);
    }

    private void Release()
    {
        _arena.Trim();
        _path.Trim();
        Clips.Reset();
        _parts.Clear();
        _processor = null!;
        _diagnostics = null;
        _context.Document = null!;
        _context.Page = null;
        _context.Resources = null;
        _busy = false;
        _cached = this;
    }

    /// <summary>Reads and executes every operator of <paramref name="content"/>.</summary>
    private void Execute(ReadOnlySpan<byte> content)
    {
        var reader = new ContentReader(content, _arena);
        _arena.Clear();
        while (reader.Next(out ReadOperator op))
        {
            if (reader.Issues != ReaderIssues.None)
            {
                ReportReaderIssues(reader.Issues, reader.IssueOffset);
                reader.ClearIssues();
            }

            if (_arena.Overflowed)
            {
                _arena.Overflowed = false;
                Report(ContentIssue.OperandOverflow, op.KeywordStart, "More operands than the limit were written before an operator; the oldest were dropped.");
            }

            if (++_operatorCount % CancellationInterval == 0)
            {
                _context.CancellationToken.ThrowIfCancellationRequested();
            }

            LocatePart(op.KeywordStart);
            if ((_events & ContentEvents.Operators) != 0)
            {
                ReportOperator(op, content);
            }

            Execute(op);
            _arena.Clear();
        }

        if (reader.Issues != ReaderIssues.None)
        {
            ReportReaderIssues(reader.Issues, reader.IssueOffset);
        }

        if (_arena.Count > 0 || _arena.OpenDepth > 0)
        {
            Report(ContentIssue.OperandCount, content.Length, "Operands are left over at the end of the content stream; ignored.");
            _arena.Clear();
        }
    }

    private void ReportOperator(ReadOperator op, ReadOnlySpan<byte> content)
    {
        int partStart = _parts.Count > 0 ? _parts[_part].Start : 0;
        var report = new ContentOperator
        {
            Code = op.Code,
            Keyword = content.Slice(op.KeywordStart, op.KeywordLength),
            Operands = _arena.Operands,
            Data = content.Slice(op.DataStart, op.DataLength),
            Stream = CurrentStream,
            PartIndex = _part,
            Offset = op.Start - partStart,
            Length = op.End - op.Start,
            KeywordOffset = op.KeywordStart - partStart,
        };
        _processor.VisitOperator(report, _context);
    }

    private void Execute(ReadOperator op)
    {
        ContentOperatorCode code = op.Code;
        int offset = op.KeywordStart;
        if (code == ContentOperatorCode.Unknown)
        {
            if (_compatibilityDepth == 0)
            {
                Report(ContentIssue.UnknownOperator, offset, "A keyword that is not an operator, outside a compatibility section (BX/EX); it and its operands are ignored.");
            }

            return;
        }

        OperatorCategory category = OperatorTable.Category(code);
        CheckContext(category, offset);
        if (!TakeOperands(code, offset, out ContentOperands operands))
        {
            return;
        }

        switch (category)
        {
            case OperatorCategory.GeneralGraphicsState or OperatorCategory.SpecialGraphicsState:
                ExecuteGraphicsState(code, operands, offset);
                break;
            case OperatorCategory.PathConstruction:
                ExecutePathConstruction(code, operands, offset);
                break;
            case OperatorCategory.PathPainting:
                ExecutePathPainting(code, offset);
                break;
            case OperatorCategory.ClippingPath:
                ExecuteClip(code, offset);
                break;
            case OperatorCategory.TextObject or OperatorCategory.TextState or OperatorCategory.TextPositioning or OperatorCategory.TextShowing
                or OperatorCategory.Type3Font:
                ExecuteText(code, operands, offset);
                break;
            case OperatorCategory.Color:
                ExecuteColor(code, operands, offset);
                break;
            case OperatorCategory.Shading:
                ExecuteShading(code, operands, offset);
                break;
            case OperatorCategory.InlineImage or OperatorCategory.XObject:
                ExecuteXObject(code, operands, op, offset);
                break;
            case OperatorCategory.MarkedContent:
                ExecuteMarkedContent(code, operands, offset);
                break;
            case OperatorCategory.Compatibility:
                ExecuteCompatibility(code, offset);
                break;
        }
    }

    /// <summary>
    /// Checks the operands against the operator's signature: too few, or of the wrong kind, skips the operator; too many uses the
    /// last ones (they are the ones that "immediately precede" it, §7.8.2).
    /// </summary>
    private bool TakeOperands(ContentOperatorCode code, int offset, out ContentOperands operands)
    {
        OperandSignature signature = OperatorTable.Signature(code);
        int count = _arena.Count;
        operands = _arena.Operands;
        if (signature.IsVariable)
        {
            if (count == 0)
            {
                Report(ContentIssue.OperandCount, offset, "A colour operator has no operands; skipped.");
                return false;
            }

            for (int index = 0; index < count; index++)
            {
                ContentOperand operand = operands[index];
                bool trailingName = index == count - 1 && signature.Kinds.Length > 1 && operand.Kind == ContentOperandKind.Name;
                if (!operand.IsNumber && !trailingName)
                {
                    Report(ContentIssue.OperandType, offset, "A colour operator's operands must be numbers (and, for SCN and scn, a final name); skipped.");
                    return false;
                }
            }

            return true;
        }

        int expected = signature.Kinds.Length;
        if (count < expected)
        {
            Report(ContentIssue.OperandCount, offset, "An operator has fewer operands than it takes; skipped.");
            return false;
        }

        if (count > expected)
        {
            Report(ContentIssue.OperandCount, offset, "An operator has more operands than it takes; the last ones are used and the others ignored.");
            operands = _arena.Last(expected);
        }

        for (int index = 0; index < expected; index++)
        {
            ContentOperandKind kind = operands[index].Kind;
            bool matches = signature.Kinds[index] switch
            {
                (byte)'n' => kind is ContentOperandKind.Integer or ContentOperandKind.Real,
                (byte)'N' => kind == ContentOperandKind.Name,
                (byte)'s' => kind == ContentOperandKind.String,
                (byte)'a' => kind == ContentOperandKind.Array,
                (byte)'d' => kind == ContentOperandKind.Dictionary,
                _ => kind is ContentOperandKind.Dictionary or ContentOperandKind.Name,
            };
            if (!matches)
            {
                Report(ContentIssue.OperandType, offset, "An operand is not of the type the operator takes; the operator is skipped.");
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Figure 9: inside a path object only path construction, clipping and painting operators may appear; inside a text object,
    /// general graphics state, colour, text and marked-content operators; text positioning and showing only inside a text object.
    /// A violation is applied anyway and recorded at Information severity (real files do it, e.g. <c>m l 2 w S</c>).
    /// </summary>
    private void CheckContext(OperatorCategory category, int offset)
    {
        bool allowed;
        if (_path.IsOpen)
        {
            allowed = category is OperatorCategory.PathConstruction or OperatorCategory.PathPainting or OperatorCategory.ClippingPath
                or OperatorCategory.Compatibility;
        }
        else if (_inText)
        {
            allowed = category is OperatorCategory.GeneralGraphicsState or OperatorCategory.Color or OperatorCategory.TextState
                or OperatorCategory.TextPositioning or OperatorCategory.TextShowing or OperatorCategory.TextObject
                or OperatorCategory.MarkedContent or OperatorCategory.Compatibility or OperatorCategory.PathPainting
                or OperatorCategory.ClippingPath;
        }
        else
        {
            allowed = category is not (OperatorCategory.TextPositioning or OperatorCategory.TextShowing);
        }

        if (!allowed)
        {
            Report(
                ContentIssue.OperatorOutOfContext,
                offset,
                "An operator appears where Figure 9 does not allow it (a graphics state change inside a path object, or text outside a text object); it is applied anyway.");
        }
    }

    private void ExecuteCompatibility(ContentOperatorCode code, int offset)
    {
        if (code == ContentOperatorCode.BeginCompatibility)
        {
            _compatibilityDepth++;
        }
        else if (_compatibilityDepth > 0)
        {
            _compatibilityDepth--;
        }
        else
        {
            Report(ContentIssue.CompatibilityUnbalanced, offset, "EX without a matching BX; ignored.");
        }
    }

    /// <summary>
    /// Ends the running stream: leftover state that a well-formed stream would have closed is closed, with diagnostics: an
    /// unpainted path is discarded, an open text object ends, an open compatibility section closes, and unbalanced <c>q</c>
    /// operators are restored down to the stream's floor (§8.4.2).
    /// </summary>
    private void FinishStream()
    {
        if (_path.IsOpen)
        {
            Report(ContentIssue.PathNotPainted, -1, "The content stream ends inside a path object that was never painted; the path is discarded.");
            _path.Reset();
            _pendingClip = null;
        }

        if (_inText)
        {
            Report(ContentIssue.TextObjectUnbalanced, -1, "The content stream ends inside a text object (BT without ET); it is ended there.");
            EndTextObject();
        }

        if (_compatibilityDepth > 0)
        {
            Report(ContentIssue.CompatibilityUnbalanced, -1, "The content stream ends inside a compatibility section (BX without EX).");
            _compatibilityDepth = 0;
        }

        if (_depth > _floor)
        {
            Report(ContentIssue.UnbalancedSave, -1, "The content stream ends with graphics states saved by q that no Q restored; they are restored there.");
            while (_depth > _floor)
            {
                PopState();
            }
        }

        _ignoredSaves = 0;
    }

    /// <summary>Moves the current part forward to the one containing <paramref name="offset"/> (offsets only grow).</summary>
    private void LocatePart(int offset)
    {
        while (_part + 1 < _parts.Count && _parts[_part + 1].Start <= offset)
        {
            _part++;
        }
    }

    private void ReportReaderIssues(ReaderIssues issues, int offset)
    {
        if ((issues & ReaderIssues.GluedTokens) != 0)
        {
            Report(ContentIssue.GluedTokens, offset, "Operators and operands are written without white-space between them (such as q1 or 0cm); they are read as separate tokens.");
        }

        if ((issues & ReaderIssues.SyntaxInvalid) != 0)
        {
            Report(ContentIssue.SyntaxInvalid, offset, "A malformed token (a stray delimiter, an unbalanced ] or >>, a malformed number, string or name) was skipped or repaired.");
        }

        if ((issues & ReaderIssues.ContainerUnclosed) != 0)
        {
            Report(ContentIssue.SyntaxInvalid, offset, "An array or dictionary operand is not closed before its operator; it is closed there.");
        }

        if ((issues & ReaderIssues.InlineImageInvalid) != 0)
        {
            Report(ContentIssue.InlineImageInvalid, offset, "An inline image lacks ID or EI, or its dictionary holds something that is not a value; the image ends there.");
        }
    }

    /// <summary>
    /// Records a deviation once per kind per run, on the content stream's object (the page's when the stream is direct), with the
    /// part and the offset in the decoded data in the message. Strict mode throws through the document's sink.
    /// </summary>
    private void Report(ContentIssue issue, int offset, string message)
    {
        ulong bit = 1UL << (int)issue;
        if ((_reported & bit) != 0 || _diagnostics is null)
        {
            return;
        }

        _reported |= bit;
        if (offset >= 0)
        {
            LocatePart(offset);
        }

        int partStart = _parts.Count > 0 ? _parts[_part].Start : 0;
        string location = offset < 0
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $" (content stream part {_part}, decoded offset {offset - partStart})");
        _diagnostics.ReportOnce(ContentIssues.Code(issue), ContentIssues.Severity(issue), message + location, offset: null, CurrentStream ?? _fallbackReference);
    }
}
