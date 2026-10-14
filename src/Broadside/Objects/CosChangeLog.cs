namespace Broadside.Objects;

/// <summary>
/// A change set that can be reverted: every change made on the current thread while it records (<see cref="Record"/>) to a
/// <see cref="CosDictionary"/>, a <see cref="CosArray"/> or a <see cref="CosStream"/>'s data through their public API, and every
/// indirect object a document gains or loses, is undone by <see cref="Revert"/>, together with the undo actions added to it
/// (<see cref="Add"/>, <see cref="OnRevert"/>). Signing uses it to leave a document exactly as it was when the signer or the
/// time-stamp authority fails after the save inside signing changed it (issue #306).
/// </summary>
/// <remarks>
/// <para>
/// A container is recorded once, on its first change while recording: its entries or elements (in order), or its data, and whether
/// it was dirty. Reverting puts them back as they were, marks the container changed for the caches that record its
/// <c>Version</c>, and so the document's COS graph is the one before the first recorded change. The log is not a lock: it records
/// only what this thread changes, which is every change to the document since mutation is single-threaded (ADR 0004).
/// </para>
/// <para>
/// Reverting runs the undo actions and restores the containers in the reverse order they were recorded in. An object number a
/// reverted addition took stays taken.
/// </para>
/// </remarks>
internal sealed class CosChangeLog
{
    /// <summary>How many threads are recording: zero lets every change skip the thread-local lookup.</summary>
    private static int s_recording;

    [ThreadStatic]
    private static CosChangeLog? t_current;

    private readonly List<Action> _undo = [];
    private readonly HashSet<CosObject> _recorded = new(ReferenceEqualityComparer.Instance);

    /// <summary>Gets the log recording on this thread, or <see langword="null"/>.</summary>
    public static CosChangeLog? Current => Volatile.Read(ref s_recording) == 0 ? null : t_current;

    /// <summary>Records every change this thread makes until the returned scope is disposed; scopes nest.</summary>
    /// <returns>The scope; disposing it stops recording into this log.</returns>
    public Recording Record()
    {
        CosChangeLog? previous = t_current;
        t_current = this;
        Interlocked.Increment(ref s_recording);
        return new Recording(previous);
    }

    /// <summary>Adds an action that undoes a change the containers do not record (state outside the COS graph).</summary>
    public void Add(Action undo) => _undo.Add(undo);

    /// <summary>Adds <paramref name="undo"/> to the log recording on this thread, when there is one.</summary>
    public static void OnRevert(Action undo) => Current?.Add(undo);

    /// <summary>Undoes every change recorded, latest first, and empties the log.</summary>
    public void Revert()
    {
        for (int index = _undo.Count - 1; index >= 0; index--)
        {
            _undo[index]();
        }

        _undo.Clear();
        _recorded.Clear();
    }

    /// <summary>Called by a container before it changes: records how it is, the first time while a log records.</summary>
    internal static void Changing(CosObject container)
    {
        if (Current is { } log && log._recorded.Add(container))
        {
            log._undo.Add(container.CaptureState());
        }
    }

    /// <summary>The scope of <see cref="Record"/>.</summary>
    internal readonly struct Recording(CosChangeLog? previous) : IDisposable
    {
        public void Dispose()
        {
            t_current = previous;
            Interlocked.Decrement(ref s_recording);
        }
    }
}
