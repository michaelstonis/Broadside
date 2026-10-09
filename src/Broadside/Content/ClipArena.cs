using System.Buffers;
using Broadside.Graphics;

namespace Broadside.Content;

/// <summary>
/// The clip nodes of a top-level run and of every nested run inside it. A graphics state refers to its clip by a handle, so
/// <c>q</c> and <c>Q</c> save and restore clips by copying an integer; nodes are never removed during the run.
/// </summary>
/// <remarks>ISO 32000-2 §8.5.4: each node intersects its parent with a region; handle 0 is the run's initial clipping path.</remarks>
internal sealed class ClipArena
{
    private Node[] _nodes = ArrayPool<Node>.Shared.Rent(16);
    private PathVerb[] _verbs = ArrayPool<PathVerb>.Shared.Rent(64);
    private PathPoint[] _points = ArrayPool<PathPoint>.Shared.Rent(128);
    private int _nodeCount;
    private int _verbCount;
    private int _pointCount;

    /// <summary>Forgets every node, at the end of a top-level run, and gives oversized buffers back to the pool.</summary>
    public void Reset()
    {
        _nodeCount = 0;
        _verbCount = 0;
        _pointCount = 0;
        if (_points.Length > 1 << 16)
        {
            ArrayPool<Node>.Shared.Return(_nodes);
            ArrayPool<PathVerb>.Shared.Return(_verbs);
            ArrayPool<PathPoint>.Shared.Return(_points);
            _nodes = ArrayPool<Node>.Shared.Rent(16);
            _verbs = ArrayPool<PathVerb>.Shared.Rent(64);
            _points = ArrayPool<PathPoint>.Shared.Rent(128);
        }
    }

    /// <summary>Adds a node intersecting <paramref name="parent"/> with a copy of <paramref name="path"/>; returns its handle.</summary>
    public int Add(int parent, ClipKind kind, FillRule rule, PathView path, Matrix ctm)
    {
        if (_nodeCount == _nodes.Length)
        {
            Grow(ref _nodes, _nodeCount, _nodeCount + 1);
        }

        if (_verbCount + path.Verbs.Length > _verbs.Length)
        {
            Grow(ref _verbs, _verbCount, _verbCount + path.Verbs.Length);
        }

        if (_pointCount + path.Points.Length > _points.Length)
        {
            Grow(ref _points, _pointCount, _pointCount + path.Points.Length);
        }

        path.Verbs.CopyTo(_verbs.AsSpan(_verbCount));
        path.Points.CopyTo(_points.AsSpan(_pointCount));
        _nodes[_nodeCount++] = new Node(parent, kind, rule, _verbCount, path.Verbs.Length, _pointCount, path.Points.Length, ctm);
        _verbCount += path.Verbs.Length;
        _pointCount += path.Points.Length;
        return _nodeCount;
    }

    /// <summary>Returns the node a handle refers to; handle 0, and any handle not issued in this run, is the initial clip.</summary>
    public ClipView Get(int handle)
    {
        if (handle <= 0 || handle > _nodeCount)
        {
            return new ClipView { Kind = ClipKind.Initial, Ctm = Matrix.Identity };
        }

        ref readonly Node node = ref _nodes[handle - 1];
        return new ClipView
        {
            Handle = handle,
            ParentHandle = node.Parent,
            Kind = node.Kind,
            Rule = node.Rule,
            Path = new PathView(_verbs.AsSpan(node.VerbStart, node.VerbCount), _points.AsSpan(node.PointStart, node.PointCount)),
            Ctm = node.Ctm,
        };
    }

    private static void Grow<T>(ref T[] array, int used, int minimum)
    {
        T[] larger = ArrayPool<T>.Shared.Rent(Math.Max(minimum, array.Length * 2));
        array.AsSpan(0, used).CopyTo(larger);
        ArrayPool<T>.Shared.Return(array);
        array = larger;
    }

    private readonly record struct Node(int Parent, ClipKind Kind, FillRule Rule, int VerbStart, int VerbCount, int PointStart, int PointCount, Matrix Ctm);
}
