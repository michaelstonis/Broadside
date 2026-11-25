using System.Buffers;
using System.Diagnostics;
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

    private OperandArena _arena = new();
    private PathBuilder _path = new();
    private readonly ContentContext _context;
    private List<ContentPart> _parts = [];
    private GraphicsState[] _states = ArrayPool<GraphicsState>.Shared.Rent(16);

    // Stacks outgrown during a run: State hands out references into the stack (ContentContext.State, held by processors across
    // nested runs that push states), so an outgrown stack goes back to the pool only when the run ends (issue #274).
    private readonly List<GraphicsState[]> _outgrownStates = [];
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

    // A page run in slices (issue #365): its content and where its next top-level operator starts.
    private ReadOnlyMemory<byte> _slicedContent;
    private int _slicedPosition;

    // Issue #584: whether a run in slices can continue (SliceIdle), is in a slice (SliceRunning), or has ended (SliceEnded).
    private const int SliceEnded = 0;
    private const int SliceIdle = 1;
    private const int SliceRunning = 2;
    private int _slice;

    // Progress of the top-level stream (ContentOptions.Progress): its length and the end of its last operator executed.
    private IProgress<double>? _progress;
    private int _progressLength;
    private int _progressPosition;

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
    internal static void RunBytes(ReadOnlySpan<byte> content, PdfDocument document, ContentProcessor processor, ContentOptions options) =>
        RunBytes(content, document, processor, options, resources: null);

    /// <summary>As <see cref="RunBytes(ReadOnlySpan{byte}, PdfDocument, ContentProcessor, ContentOptions)"/>, with names resolving in <paramref name="resources"/>.</summary>
    internal static void RunBytes(ReadOnlySpan<byte> content, PdfDocument document, ContentProcessor processor, ContentOptions options, CosDictionary? resources)
    {
        ContentInterpreter interpreter = Rent();
        try
        {
            interpreter.Begin(document, page: null, processor, options, document.Pages.Count > 0 ? document.Pages[0].Reference : null);
            interpreter._context.Resources = resources;
            interpreter._parts.Add(new ContentPart(0, null));
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
        // Issue #584: the interpreter leaves this thread's slot while it runs. A run in slices can end on another thread than the one
        // it began on (an await between slices); had the slot kept it, it would be cached on both threads once released, and two runs
        // on those threads would then share it. A run started from inside another run's callback finds the slot empty and gets an
        // interpreter of its own.
        ContentInterpreter interpreter = _cached ?? new ContentInterpreter();
        _cached = null;
        Debug.Assert(!interpreter._busy, "A cached interpreter is never running.");
        interpreter._busy = true;
        return interpreter;
    }

    private void RunPageCore(PdfPage page, PdfDocument document, ContentProcessor processor, ContentOptions options)
    {
        Begin(document, page, processor, options, page.Reference);
        Execute(ReadContents(page, document).Span);
        End();
    }

    /// <summary>
    /// Returns the page's decoded content, from the document's cache once a run has decoded it (issue #90); parts of an array are
    /// joined with a line feed between them (Table 31).
    /// </summary>
    private ReadOnlyMemory<byte> ReadContents(PdfPage page, PdfDocument document)
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
                _parts.Add(new ContentPart(0, entry as CosReference));
                return _context.GetContent(stream);
            case CosArray { Count: > 0 } array:
                return ReadParts(array);
            case CosArray:
                Report(ContentIssue.StreamInvalid, -1, "The page's Contents array is empty; the page has no content.");
                return ReadOnlyMemory<byte>.Empty;
            default:
                Report(ContentIssue.StreamInvalid, -1, "The page's Contents is neither a stream nor an array of streams; the page has no content.");
                return ReadOnlyMemory<byte>.Empty;
        }
    }

    private ReadOnlyMemory<byte> ReadParts(CosArray array)
    {
        ContentResourceCache.JoinedContent joined = _context.GetContent(array);
        foreach (string problem in joined.Problems)
        {
            Report(ContentIssue.StreamInvalid, -1, problem);
        }

        foreach (ContentPart part in joined.Parts)
        {
            _parts.Add(part);
        }

        if (_parts.Count == 0)
        {
            _parts.Add(new ContentPart(0, null));
        }

        return joined.Data;
    }

    /// <summary>Resets the interpreter for a top-level run of a page's content: initial state, empty stacks, the page's resources.</summary>
    private void Prepare(PdfDocument document, PdfPage? page, ContentProcessor processor, ContentOptions options, CosReference? fallbackReference)
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
        _glyphHeadPending = false;
        _maxSaveDepth = options.MaxSaveDepth;
        _printing = options.Printing;
        BeginNesting(document, page, options);
        _arena.Limit = options.MaxOperands;
        _path.Accumulate = (_events & (ContentEvents.Paths | ContentEvents.Clips)) != 0;
        _states[0] = GraphicsState.CreateInitial();
        _states[0].OutputIntent = document.GetOutputIntent(page);
        _maxNestingDepth = options.MaxNestingDepth;
        _frames.Clear();
        PushFrame(null, Matrix.Identity);
        _parts.Clear();
        _context.RunKind = ContentRunKind.Page;
        _context.Depth = 0;
        _context.Document = document;
        _context.Page = page;
        _context.Resources = page?.Resources;
        _context.StreamBaseMatrix = Matrix.Identity;
        _context.IsHidden = false;
        _context.ContentStream = null;
        _context.StructParents = page is null ? null : Annotations.AnnotationValues.ReadInteger(document, page.Dictionary.TryGetValue(StructParentsName, out CosObject? key) ? key : null);
        _context.CancellationToken = options.CancellationToken;
        _progress = options.Progress;
        _progressLength = 0;
        _progressPosition = 0;
    }

    /// <summary>Prepares a top-level run (<see cref="Prepare"/>) and starts it.</summary>
    private void Begin(PdfDocument document, PdfPage? page, ContentProcessor processor, ContentOptions options, CosReference? fallbackReference)
    {
        Prepare(document, page, processor, options, fallbackReference);
        Start();
    }

    /// <summary>Starts a prepared run: checks cancellation and reports <see cref="ContentProcessor.BeginRun"/>.</summary>
    private void Start()
    {
        _context.CancellationToken.ThrowIfCancellationRequested();
        _processor.BeginRun(_context);
    }

    private void End()
    {
        FinishStream();
        _processor.EndRun(_context);
        _progress?.Report(1);
    }

    /// <summary>
    /// At a cancellation point: checks the token, reports progress (whose receiver may cancel it), and checks again, so a cancel from
    /// the receiver stops the run before its next operator.
    /// </summary>
    private void CheckPoint()
    {
        CancellationToken token = _context.CancellationToken;
        token.ThrowIfCancellationRequested();
        if (_progress is { } progress)
        {
            progress.Report(_progressLength == 0 ? 0 : (double)_progressPosition / _progressLength);
            token.ThrowIfCancellationRequested();
        }
    }

    /// <summary>Doubles the graphics state stack; the outgrown one stays valid, and leaves the pool, until <see cref="Release"/>.</summary>
    private void GrowStates()
    {
        GraphicsState[] larger = ArrayPool<GraphicsState>.Shared.Rent(_states.Length * 2);
        _states.AsSpan(0, _depth + 1).CopyTo(larger);
        _outgrownStates.Add(_states);
        _states = larger;
    }

    private void Release()
    {
        EndNesting();
        foreach (GraphicsState[] outgrown in _outgrownStates)
        {
            ArrayPool<GraphicsState>.Shared.Return(outgrown, clearArray: true);
        }

        _outgrownStates.Clear();
        _arena.Trim();
        _path.Trim();
        Clips.Reset();
        _parts.Clear();
        _frames.Clear();
        _processor = null!;
        _diagnostics = null;
        _progress = null;
        _context.Document = null!;
        _context.Page = null;
        _context.Resources = null;
        _busy = false;
        _cached = this;
    }

    /// <summary>
    /// Starts a page's run in slices (issue #365): as <see cref="RunPage"/>, but the top-level operators are executed by
    /// <see cref="ContinuePageRun"/>, which stops at a deadline between two of them. The interpreter is the run's until
    /// <see cref="EndPageRun"/>.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="document">The document of the page.</param>
    /// <param name="processor">The processor.</param>
    /// <param name="options">Limits and cancellation.</param>
    /// <returns>The interpreter running the page.</returns>
    public static ContentInterpreter BeginPageRun(PdfPage page, PdfDocument document, ContentProcessor processor, ContentOptions options)
    {
        ContentInterpreter interpreter = Rent();
        try
        {
            interpreter.Begin(document, page, processor, options, page.Reference);
            interpreter._slicedContent = interpreter.ReadContents(page, document);
            interpreter._slicedPosition = 0;
            interpreter._slice = SliceIdle;
            return interpreter;
        }
        catch
        {
            interpreter.Release();
            throw;
        }
    }

    /// <summary>
    /// Executes the page's top-level operators from where the last call stopped until the end of its content or, after at least one
    /// operator, <see cref="Stopwatch"/> reaches <paramref name="deadline"/>. Once it returns <see langword="true"/> the run has ended
    /// (<see cref="ContentProcessor.EndRun"/>). An operator, with the form XObjects, patterns and glyphs it runs, is never split.
    /// </summary>
    /// <param name="deadline">The timestamp to stop at.</param>
    /// <returns><see langword="true"/> when the page's content is done.</returns>
    /// <remarks>
    /// ISO 32000-2 §7.8.2 and §8.4.2: between two top-level operators the run's whole state is the graphics state stack, the operand
    /// arena (empty there) and the position in the content, so the processor sees the calls of an unsliced run.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The run has ended (<see cref="EndPageRun"/>, or a slice that threw), its content is done, or another slice of it is running.
    /// </exception>
    public bool ContinuePageRun(long deadline)
    {
        // Issue #584: a run is continued by one caller at a time and never after it ended, when the interpreter may already run
        // another page.
        if (Interlocked.CompareExchange(ref _slice, SliceRunning, SliceIdle) != SliceIdle)
        {
            throw new InvalidOperationException("The page run has ended or another slice of it is running.");
        }

        bool done = false;
        try
        {
            if (!ExecuteRange(_slicedContent.Span, _slicedPosition, deadline, out int position))
            {
                _slicedPosition = position;
                return false;
            }

            _slicedContent = default;
            End();
            done = true;
            return true;
        }
        catch
        {
            done = true; // the caller ends a run whose slice threw
            throw;
        }
        finally
        {
            Volatile.Write(ref _slice, done ? SliceEnded : SliceIdle);
        }
    }

    /// <summary>Gives the interpreter of a run begun by <see cref="BeginPageRun"/> back, ended or abandoned.</summary>
    /// <exception cref="InvalidOperationException">A slice of the run is running, or the interpreter was already given back.</exception>
    public void EndPageRun()
    {
        if (!_busy)
        {
            throw new InvalidOperationException("The page run's interpreter was already given back.");
        }

        int state = Volatile.Read(ref _slice);
        while (true)
        {
            if (state == SliceRunning)
            {
                throw new InvalidOperationException("A slice of the page run is running; end the run once it returns.");
            }

            int seen = Interlocked.CompareExchange(ref _slice, SliceEnded, state);
            if (seen == state)
            {
                break;
            }

            state = seen;
        }

        _slicedContent = default;
        Release();
    }

    /// <summary>Reads and executes every operator of <paramref name="content"/>.</summary>
    private void Execute(ReadOnlySpan<byte> content) => ExecuteRange(content, 0, long.MaxValue, out _);

    /// <summary>
    /// Reads and executes the operators of <paramref name="content"/> from <paramref name="start"/> until its end or, after at least one
    /// operator, until <see cref="Stopwatch"/> reaches <paramref name="deadline"/>; returns whether the end was reached and, when not,
    /// where to resume.
    /// </summary>
    private bool ExecuteRange(ReadOnlySpan<byte> content, int start, long deadline, out int position)
    {
        position = content.Length;
        var reader = new ContentReader(content, _arena, start);
        if (start == 0)
        {
            _arena.Clear();
        }

        bool topLevel = _context.Depth == 0;
        if (topLevel)
        {
            _progressLength = content.Length;
        }

        int executed = 0;
        int resume = reader.Position;
        bool prepares = topLevel && deadline != long.MaxValue;
        while (reader.Next(out ReadOperator op))
        {
            if (prepares && op.Code == ContentOperatorCode.PaintXObject && reader.Issues == ReaderIssues.None && !_arena.Overflowed && !PrepareXObject(deadline))
            {
                // Issue #566: the image the Do paints is still decoding in slices; the next slice reads the Do again.
                _arena.Clear();
                position = resume;
                return false;
            }

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

            if (topLevel)
            {
                _progressPosition = op.End;
            }

            if (++_operatorCount % CancellationInterval == 0)
            {
                CheckPoint();
            }

            LocatePart(op.KeywordStart);
            if ((_events & ContentEvents.Operators) != 0)
            {
                ReportOperator(op, content);
            }

            Execute(op, content);
            _arena.Clear();
            if (deadline != long.MaxValue && (++executed & 7) == 0 && Stopwatch.GetTimestamp() >= deadline)
            {
                // Issue #365: a run in slices stops between two operators; the reader's issues so far were reported.
                if (reader.Issues != ReaderIssues.None)
                {
                    ReportReaderIssues(reader.Issues, reader.IssueOffset);
                }

                position = reader.Position;
                return false;
            }

            resume = reader.Position;
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

        return true;
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

    private void Execute(ReadOperator op, ReadOnlySpan<byte> content)
    {
        ContentOperatorCode code = op.Code;
        int offset = op.KeywordStart;
        bool glyphHead = TakeGlyphHead(code, offset);
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
            case OperatorCategory.TextObject or OperatorCategory.TextState or OperatorCategory.TextPositioning or OperatorCategory.TextShowing:
                ExecuteText(code, operands, offset);
                break;
            case OperatorCategory.Type3Font:
                ExecuteGlyphMetrics(code, offset, glyphHead);
                break;
            case OperatorCategory.Color:
                ExecuteColor(code, operands, offset);
                break;
            case OperatorCategory.Shading:
                ExecuteShading(code, operands, offset);
                break;
            case OperatorCategory.InlineImage or OperatorCategory.XObject:
                ExecuteXObject(code, operands, op, content, offset);
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

        CloseMarkedContent();

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

        if ((issues & ReaderIssues.InlineImageRepaired) != 0)
        {
            Report(ContentIssue.InlineImageInvalid, offset, "An inline image's data does not end where its L entry says, or ID is followed by CR LF, or no EI is followed by content; the end found is used.");
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
