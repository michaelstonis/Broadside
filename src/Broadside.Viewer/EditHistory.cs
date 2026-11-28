using Broadside.Diagnostics;

namespace Broadside.Viewer;

/// <summary>
/// The undo and redo history of a session's edits: the annotation and form field edits an <see cref="AnnotationLayer"/> (and the
/// <see cref="FormFilling"/> over it) makes while its <see cref="AnnotationLayer.History"/> is set, and an app's own edits recorded with
/// <see cref="Record"/>. Each shell maps Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z (Cmd on Apple platforms) to <see cref="Undo"/> and
/// <see cref="Redo"/>, and a <see cref="ViewerToolbar"/> offers them as <see cref="ViewerCommandIds.EditUndo"/> and
/// <see cref="ViewerCommandIds.EditRedo"/>.
/// </summary>
/// <remarks>
/// <para>
/// Not a spec feature; the built-in steps snapshot the annotation dictionaries (ISO 32000-2 §12.5.2) and field dictionaries
/// (§12.7.4.1) an edit changed. A snapshot is the containers' entries, with direct dictionaries and arrays deep-copied and indirect
/// references kept as references: undoing writes the old entries back into the same live dictionaries, so the <c>Pdf*</c> views over
/// them stay valid, streams such as appearances are never copied, and the appearance an edit replaced comes back as it was.
/// </para>
/// <para>
/// Like the document, the history is single-threaded: record, undo and redo on the UI thread. <see cref="Undo"/> and
/// <see cref="Redo"/> run the step inside <see cref="DocumentSession.BeginEdit"/>, which holds the session's worker reads, then the
/// layer re-reads the pages the step touched and redraws only the overlay tiles it returned.
/// </para>
/// <para>
/// The history knows only the edits it is told of. An edit made around the layer while the history is set (an app calling
/// <see cref="PdfPage.AddTextAnnotation(PdfRectangle, string, Annotations.PdfAnnotationOptions?)"/> directly) is not undoable; record
/// it as an app-defined step if it should be. If such an edit changed what a recorded step restores, undoing that step fails: per
/// the library's leniency rule <see cref="Undo"/> then returns <see langword="false"/>, reports a <see cref="Diagnostic"/> through
/// <see cref="PdfDocument.ReportDiagnostic"/>, drops the step and clears the redo stack; it does not throw (except in strict mode,
/// where reporting a warning throws). A step whose page is no longer in the document is dropped, with every older step. Closing the
/// session clears the history.
/// </para>
/// </remarks>
public sealed class EditHistory
{
    /// <summary>The code of the diagnostic reported when a step could not be undone or redone.</summary>
    internal const string RestoreFailedCode = "ViewerEditHistoryRestoreFailed";

    private readonly List<IEditStep> _undo = [];
    private readonly List<IEditStep> _redo = [];
    private readonly int _limit;
    private readonly TimeSpan _mergeWindow;
    private readonly TimeProvider _time;
    private List<IEditStep>? _group;
    private string? _groupName;
    private int _groupDepth;
    private long _lastRecorded;
    private bool _restoring;

    // The saved state is the one with _savedTop on top of the undo stack (null: an empty stack), unless it can no longer be reached.
    private IEditStep? _savedTop;
    private bool _savedLost;

    /// <summary>Initializes a new instance of the <see cref="EditHistory"/> class for a session's document.</summary>
    /// <param name="session">The session whose edits the history records.</param>
    /// <param name="options">The step limit and merge window, or <see langword="null"/> for the defaults.</param>
    public EditHistory(DocumentSession session, EditHistoryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        Session = session;
        options ??= new EditHistoryOptions();
        _limit = options.Limit;
        _mergeWindow = options.MergeWindow;
        _time = options.TimeProvider;
    }

    /// <summary>Raised after a step was recorded, undone or redone, or the history was cleared or marked saved: update the Undo and Redo commands.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised before a step is undone or redone (a <see cref="FormFilling"/> abandons the edit it has not committed).</summary>
    internal event Action? Restoring;

    /// <summary>Raised after a step was undone or redone, with what to draw again (an <see cref="AnnotationLayer"/> listens).</summary>
    internal event Action<IReadOnlyList<PageInvalidation>>? Restored;

    /// <summary>Gets the session.</summary>
    public DocumentSession Session { get; }

    /// <summary>Gets a value indicating whether there is a step to undo.</summary>
    public bool CanUndo => _undo.Count > 0 && _group is null && !Session.IsClosed;

    /// <summary>Gets a value indicating whether there is a step to redo.</summary>
    public bool CanRedo => _redo.Count > 0 && _group is null && !Session.IsClosed;

    /// <summary>Gets the name of the step <see cref="Undo"/> reverts, such as "Move annotation", for a menu; <see langword="null"/> when none.</summary>
    public string? UndoName => CanUndo ? _undo[^1].Name : null;

    /// <summary>Gets the name of the step <see cref="Redo"/> applies again; <see langword="null"/> when none.</summary>
    public string? RedoName => CanRedo ? _redo[^1].Name : null;

    /// <summary>Gets a value indicating whether the document differs from its state at the last <see cref="MarkSaved"/> (or at creation).</summary>
    public bool IsModified => _savedLost || !ReferenceEquals(Top, _savedTop);

    /// <summary>Gets how many steps can be undone.</summary>
    internal int UndoCount => _undo.Count;

    /// <summary>Gets how many steps can be redone.</summary>
    internal int RedoCount => _redo.Count;

    /// <summary>Gets whether a step is being undone or redone: the layer records nothing then.</summary>
    internal bool IsRestoring => _restoring;

    private IEditStep? Top => _undo.Count > 0 ? _undo[^1] : null;

    /// <summary>Reverts the latest step.</summary>
    /// <returns>
    /// Whether a step was undone: <see langword="false"/> when there was none, a group is open, the session is closed, or the step could
    /// not be applied (see the remarks of <see cref="EditHistory"/>).
    /// </returns>
    public bool Undo() => Apply(undo: true);

    /// <summary>Applies the latest undone step again.</summary>
    /// <returns>Whether a step was redone (<see langword="false"/> in the same cases as <see cref="Undo"/>).</returns>
    public bool Redo() => Apply(undo: false);

    /// <summary>
    /// Starts a group: every step recorded until the returned scope is disposed is undone and redone as one, such as one drag, one set
    /// of ink strokes or one page drop. Groups nest; only the outermost one makes a step, named by it. An empty group records nothing.
    /// </summary>
    /// <param name="name">The step's name, for a menu.</param>
    /// <returns>The scope; dispose it to end the group.</returns>
    public IDisposable BeginGroup(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_groupDepth++ == 0)
        {
            _group = [];
            _groupName = name;
        }

        return new GroupScope(this);
    }

    /// <summary>
    /// Records an edit the app made itself, already applied to the document, so <see cref="Undo"/> can revert it. Inside a group it joins
    /// the group. The redo stack is cleared, and the oldest step is dropped once there are more than <see cref="EditHistoryOptions.Limit"/>.
    /// </summary>
    /// <param name="step">The step.</param>
    public void Record(IEditStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (_restoring || Session.IsClosed)
        {
            return;
        }

        if (_group is { } group)
        {
            group.Add(step);
            return;
        }

        Push(step);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Marks the current state as saved: <see cref="IsModified"/> is <see langword="false"/> until a step is recorded, undone or redone.</summary>
    public void MarkSaved()
    {
        _savedTop = Top;
        _savedLost = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Forgets every step; the document stays as it is, and <see cref="IsModified"/> keeps its value.</summary>
    public void Clear()
    {
        bool modified = IsModified;
        _undo.Clear();
        _redo.Clear();
        _savedTop = null;
        _savedLost = modified;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Push(IEditStep step)
    {
        if (_redo.Count > 0)
        {
            _redo.Clear();
        }

        long now = _time.GetTimestamp();
        if (step is StateEditStep { MergeKey: { } key } next
            && Top is StateEditStep { MergeKey: { } topKey } top
            && ReferenceEquals(key, topKey)
            && string.Equals(top.Name, next.Name, StringComparison.Ordinal)
            && !ReferenceEquals(top, _savedTop)
            && _mergeWindow > TimeSpan.Zero
            && _time.GetElapsedTime(_lastRecorded, now) <= _mergeWindow)
        {
            top.Merge(next);
            _lastRecorded = now;
            return;
        }

        _undo.Add(step);
        _lastRecorded = now;
        if (_undo.Count > _limit)
        {
            DropOldest(_undo.Count - _limit);
        }
    }

    private void DropOldest(int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (ReferenceEquals(_undo[i], _savedTop) || _savedTop is null)
            {
                _savedLost = true; // the saved state lay at or below a dropped step
            }
        }

        _undo.RemoveRange(0, count);
    }

    private bool Apply(bool undo)
    {
        if (Session.IsClosed)
        {
            if (_undo.Count > 0 || _redo.Count > 0)
            {
                Clear();
            }

            return false;
        }

        List<IEditStep> from = undo ? _undo : _redo;
        if (from.Count == 0 || _group is not null || _restoring)
        {
            return false;
        }

        Restoring?.Invoke();
        if (from.Count == 0)
        {
            return false;
        }

        bool modified = IsModified;
        IEditStep step = from[^1];
        from.RemoveAt(from.Count - 1);
        PdfDocument document = Session.Document;
        IReadOnlyList<PageInvalidation> invalidations;
        _restoring = true;
        try
        {
            using (Session.BeginEdit())
            {
                invalidations = undo ? step.Undo(document) : step.Redo(document);
            }
        }
        catch (EditStepPageRemovedException)
        {
            // The page went (a page organizer removed it): this step and every older one are dropped (#638).
            _restoring = false;
            from.Clear();
            Unapplied(modified);
            return false;
        }
#pragma warning disable CA1031 // Leniency: a step that cannot be restored is reported, never thrown into the shell's key handler
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _restoring = false;
            _redo.Clear();
            Unapplied(modified);
            document.ReportDiagnostic(new Diagnostic(
                RestoreFailedCode,
                DiagnosticSeverity.Warning,
                $"\"{step.Name}\" could not be {(undo ? "undone" : "redone")} and was dropped from the edit history: {exception.Message}"));
            return false;
        }
        finally
        {
            _restoring = false;
        }

        (undo ? _redo : _undo).Add(step);
        Restored?.Invoke(invalidations);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    // A step was dropped without being applied: the document is as it was, so it is the saved state exactly when it was before.
    private void Unapplied(bool modified)
    {
        _savedTop = Top;
        _savedLost = modified;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void EndGroup()
    {
        if (--_groupDepth > 0)
        {
            return;
        }

        List<IEditStep> steps = _group!;
        string name = _groupName!;
        _group = null;
        _groupName = null;
        if (steps.Count > 0 && !Session.IsClosed)
        {
            Push(new GroupEditStep(name, steps));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class GroupScope(EditHistory history) : IDisposable
    {
        private EditHistory? _history = history;

        public void Dispose() => Interlocked.Exchange(ref _history, null)?.EndGroup();
    }
}
