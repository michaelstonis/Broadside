using System.Buffers;
using System.Runtime.InteropServices;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Content;

/// <summary>What a nested content stream needs to run inside the current run: a tiling pattern's cell (issue #79), and forms,
/// Type 3 glyphs and soft masks as they come.</summary>
internal readonly struct NestedRun
{
    /// <summary>Gets what kind of stream it is.</summary>
    public required ContentRunKind Kind { get; init; }

    /// <summary>Gets the stream object, which may not run inside itself (the recursion guard compares instances).</summary>
    public required CosObject Identity { get; init; }

    /// <summary>Gets the stream's reference, for diagnostics and <see cref="ContentContext.CurrentStream"/>.</summary>
    public CosReference? Reference { get; init; }

    /// <summary>Gets the decoded content.</summary>
    public required ReadOnlyMemory<byte> Content { get; init; }

    /// <summary>Gets the resources names resolve against.</summary>
    public CosDictionary? Resources { get; init; }

    /// <summary>Gets the state the stream starts with, its CTM already the stream's base matrix.</summary>
    public required GraphicsState InitialState { get; init; }

    /// <summary>Gets a rectangle, in the stream's space, intersected with the clip before the content runs (a cell's or form's BBox).</summary>
    public PdfRectangle? ClipBox { get; init; }

    /// <summary>Gets the processor the nested run's events go to.</summary>
    public required ContentProcessor Processor { get; init; }

    /// <summary>Gets a value indicating whether colour operators are ignored inside (inherited by every stream run from it).</summary>
    public bool IgnoresColorOperators { get; init; }

    /// <summary>Gets the <c>StructParents</c> of the stream (a form's), which its marked-content identifiers resolve through (§14.7.5.4).</summary>
    public int? StructParents { get; init; }
}

/// <summary>Why a nested run did not run.</summary>
internal enum NestedRunResult
{
    Ran,
    Recursive,
    TooDeep,
}

/// <summary>Nested runs: a content stream run from inside another, sharing the state stack and clip arena (§8.7.3.1, §8.10.1).</summary>
/// <remarks>
/// ISO 32000-2 §8.7.3.1 (a pattern cell is painted with q, the parent stream's initial state with the pattern matrix, the cell, Q),
/// §8.10.1 (forms the same way, issue #56). The run gets operand and path buffers of its own, so the outer operator's operands and
/// path survive a run started from a processor callback; its text object, marked-content sequences and text clip are its own (an
/// outer text object is set aside and resumes after it, §8.10.1, §14.6); its <c>q</c>/<c>Q</c> cannot pop below its floor; one depth budget
/// (<see cref="ContentOptions.MaxNestingDepth"/>) and one visited set of streams guard every kind of nesting.
/// </remarks>
internal sealed partial class ContentInterpreter
{
    private readonly List<RunFrame> _frames = [];
    private readonly List<NestedBuffers> _spareBuffers = [];
    private int _maxNestingDepth = ContentOptions.DefaultMaxNestingDepth;

    /// <summary>Gets the start state of the innermost running stream whose base matrix is <paramref name="baseMatrix"/>.</summary>
    /// <remarks>
    /// A pattern colour captures the base matrix of the stream that selected it (§8.7.2); the cell starts from that stream's initial
    /// graphics state (§8.7.3.1 step b). Falls back to the running stream.
    /// </remarks>
    internal ref readonly GraphicsState StartStateFor(Matrix baseMatrix)
    {
        for (int i = _frames.Count - 1; i >= 0; i--)
        {
            if (_frames[i].BaseMatrix == baseMatrix)
            {
                return ref CollectionsMarshal.AsSpan(_frames)[i].StartState;
            }
        }

        return ref CollectionsMarshal.AsSpan(_frames)[^1].StartState;
    }

    /// <summary>Runs a nested content stream; see <see cref="NestedRun"/>.</summary>
    internal NestedRunResult RunNested(in NestedRun run)
    {
        if (CanNest(run.Identity) is not NestedRunResult.Ran and var refused)
        {
            return refused;
        }

        _context.CancellationToken.ThrowIfCancellationRequested();
        int depth = _context.Depth;
        if (_spareBuffers.Count <= depth)
        {
            _spareBuffers.Add(new NestedBuffers());
        }

        NestedBuffers buffers = _spareBuffers[depth];
        var saved = new SavedRun(this);
        bool pushed = false;
        try
        {
            _processor = run.Processor;
            _events = run.Processor.Events;
            buffers.Arena.Limit = _arena.Limit;
            _arena = buffers.Arena;
            _path = buffers.Path;
            _path.Accumulate = (_events & (ContentEvents.Paths | ContentEvents.Clips)) != 0;
            _parts = buffers.Parts;
            _parts.Clear();
            _parts.Add((0, run.Reference));
            _part = 0;
            _reported = 0;
            _inText = false;
            _pendingClip = null;
            _compatibilityDepth = 0;
            _ignoredSaves = 0;
            IgnoresColorOperators |= run.IgnoresColorOperators;
            _context.RunKind = run.Kind;
            _context.Depth = depth + 1;
            _context.Resources = run.Resources;
            _context.StreamBaseMatrix = run.InitialState.Ctm;
            _context.ContentStream = run.Identity as CosStream;
            _context.StructParents = run.StructParents;
            _textMatricesValid = false;
            _textClipStart = _clipGlyphCount;
            _pendingAdjustment = 0;
            _markedFloor = _markedCount;
            PushNestedState(run.InitialState);
            pushed = true;
            _floor = _depth;
            if (run.ClipBox is { } box)
            {
                IntersectBox(box);
            }

            _frames.Add(new RunFrame(run.Identity, run.InitialState.Ctm, State));
            Execute(run.Content.Span);
            FinishStream();
        }
        finally
        {
            if (_frames.Count > 1 && ReferenceEquals(_frames[^1].Identity, run.Identity))
            {
                _frames.RemoveAt(_frames.Count - 1);
            }

            _path.Reset();
            _arena.Clear();
            _floor = saved.Floor;
            _depth = saved.Depth;
            if (pushed && (_events & ContentEvents.StateStack) != 0)
            {
                _processor.RestoreState(_context);
            }

            saved.Restore(this);
        }

        return NestedRunResult.Ran;
    }

    /// <summary>
    /// Returns whether a stream could run nested here: <see cref="NestedRunResult.Ran"/>, or why not (it is already running, or the
    /// depth budget is spent). Lets a caller decide before it reports the stream's begin event.
    /// </summary>
    internal NestedRunResult CanNest(CosObject identity)
    {
        if (_context.Depth >= _maxNestingDepth)
        {
            return NestedRunResult.TooDeep;
        }

        foreach (RunFrame frame in _frames)
        {
            if (ReferenceEquals(frame.Identity, identity))
            {
                return NestedRunResult.Recursive;
            }
        }

        return NestedRunResult.Ran;
    }

    /// <summary>Pushes <paramref name="state"/> above the current state, with no depth limit (§8.4.2 implicit save).</summary>
    private void PushNestedState(in GraphicsState state)
    {
        if (_depth + 1 == _states.Length)
        {
            GraphicsState[] larger = ArrayPool<GraphicsState>.Shared.Rent(_states.Length * 2);
            _states.AsSpan(0, _depth + 1).CopyTo(larger);
            ArrayPool<GraphicsState>.Shared.Return(_states, clearArray: true);
            _states = larger;
        }

        _states[++_depth] = state;
        if ((_events & ContentEvents.StateStack) != 0)
        {
            _processor.SaveState(_context);
        }
    }

    /// <summary>Intersects the clip with a rectangle in the current user space, as a 5-verb path, reporting it.</summary>
    private void IntersectBox(PdfRectangle box)
    {
        if ((_events & ContentEvents.Clips) == 0)
        {
            return;
        }

        bool accumulate = _path.Accumulate;
        _path.Accumulate = true;
        _path.Rectangle(box.Left, box.Bottom, box.Width, box.Height);
        int parent = State.ClipHandle;
        int handle = Clips.Add(parent, ClipKind.Rectangle, FillRule.NonZero, _path.View, State.Ctm);
        State.ClipHandle = handle;
        var clipEvent = new ClipEvent
        {
            Handle = handle,
            ParentHandle = parent,
            Kind = ClipKind.Rectangle,
            Rule = FillRule.NonZero,
            Path = _path.View,
            Ctm = State.Ctm,
        };
        _processor.IntersectClip(clipEvent, _context);
        _path.Reset();
        _path.Accumulate = accumulate;
    }

    /// <summary>A running stream: what it is, the base matrix patterns selected in it are relative to, and its initial state.</summary>
    private struct RunFrame(CosObject? identity, Matrix baseMatrix, in GraphicsState startState)
    {
        public readonly CosObject? Identity = identity;

        public readonly Matrix BaseMatrix = baseMatrix;

        public GraphicsState StartState = startState;
    }

    /// <summary>The operand, path and part buffers of one nesting level, allocated once per interpreter and reused.</summary>
    private sealed class NestedBuffers
    {
        public OperandArena Arena { get; } = new();

        public PathBuilder Path { get; } = new();

        public List<(int Start, CosReference? Reference)> Parts { get; } = [];
    }

    /// <summary>The outer run's interpreter state, restored when a nested run ends.</summary>
    private readonly struct SavedRun
    {
        private readonly ContentProcessor _processor;
        private readonly ContentEvents _events;
        private readonly OperandArena _arena;
        private readonly PathBuilder _path;
        private readonly List<(int Start, CosReference? Reference)> _parts;
        private readonly int _part;
        private readonly ulong _reported;
        private readonly bool _inText;
        private readonly FillRule? _pendingClip;
        private readonly int _compatibilityDepth;
        private readonly int _ignoredSaves;
        private readonly bool _ignoresColorOperators;
        private readonly ContentRunKind _kind;
        private readonly int _contextDepth;
        private readonly CosDictionary? _resources;
        private readonly Matrix _baseMatrix;
        private readonly CosStream? _contentStream;
        private readonly int? _structParents;
        private readonly Matrix _textMatrix;
        private readonly Matrix _lineMatrix;
        private readonly bool _textMatricesValid;
        private readonly int _textClipStart;
        private readonly double _pendingAdjustment;
        private readonly int _markedFloor;

        public SavedRun(ContentInterpreter interpreter)
        {
            _contentStream = interpreter._context.ContentStream;
            _structParents = interpreter._context.StructParents;
            _textMatrix = interpreter._textMatrix;
            _lineMatrix = interpreter._lineMatrix;
            _textMatricesValid = interpreter._textMatricesValid;
            _textClipStart = interpreter._textClipStart;
            _pendingAdjustment = interpreter._pendingAdjustment;
            _markedFloor = interpreter._markedFloor;
            _processor = interpreter._processor;
            _events = interpreter._events;
            _arena = interpreter._arena;
            _path = interpreter._path;
            _parts = interpreter._parts;
            _part = interpreter._part;
            _reported = interpreter._reported;
            _inText = interpreter._inText;
            _pendingClip = interpreter._pendingClip;
            _compatibilityDepth = interpreter._compatibilityDepth;
            _ignoredSaves = interpreter._ignoredSaves;
            _ignoresColorOperators = interpreter.IgnoresColorOperators;
            _kind = interpreter._context.RunKind;
            _contextDepth = interpreter._context.Depth;
            _resources = interpreter._context.Resources;
            _baseMatrix = interpreter._context.StreamBaseMatrix;
            Floor = interpreter._floor;
            Depth = interpreter._depth;
        }

        public int Floor { get; }

        public int Depth { get; }

        public void Restore(ContentInterpreter interpreter)
        {
            interpreter._processor = _processor;
            interpreter._events = _events;
            interpreter._arena = _arena;
            interpreter._path = _path;
            interpreter._parts = _parts;
            interpreter._part = _part;
            interpreter._reported = _reported;
            interpreter._inText = _inText;
            interpreter._pendingClip = _pendingClip;
            interpreter._compatibilityDepth = _compatibilityDepth;
            interpreter._ignoredSaves = _ignoredSaves;
            interpreter.IgnoresColorOperators = _ignoresColorOperators;
            interpreter._context.RunKind = _kind;
            interpreter._context.Depth = _contextDepth;
            interpreter._context.Resources = _resources;
            interpreter._context.StreamBaseMatrix = _baseMatrix;
            interpreter._context.ContentStream = _contentStream;
            interpreter._context.StructParents = _structParents;
            interpreter._textMatrix = _textMatrix;
            interpreter._lineMatrix = _lineMatrix;
            interpreter._textMatricesValid = _textMatricesValid;
            interpreter._textClipStart = _textClipStart;
            interpreter._pendingAdjustment = _pendingAdjustment;
            interpreter._markedFloor = _markedFloor;
        }
    }
}
