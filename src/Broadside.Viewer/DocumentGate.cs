namespace Broadside.Viewer;

/// <summary>
/// Keeps a background session's document reads (page loads, display-list builds, page-model loads on workers) apart from its edits
/// (one thread, the UI thread): an edit cancels the reads in progress, waits until they have stopped, and the reads that arrive
/// meanwhile wait for it to end; a cancelled read starts again after the edit (the thread-safety contract: concurrent reads,
/// single-threaded mutation).
/// </summary>
/// <remarks>Spec #33 viewer capability 1 (#357). Replays of finished display lists read no document objects and do not take part.</remarks>
internal sealed class DocumentGate : IDisposable
{
    private readonly object _lock = new();
    private CancellationTokenSource _reads = new();
    private TaskCompletionSource? _editEnded;
    private int _readers;
    private int _edits;
    private int _editor;

    /// <summary>Runs <paramref name="work"/> on a worker as a read: after any edit in progress, and again if an edit cancels it.</summary>
    public async Task<T> ReadAsync<T>(Func<CancellationToken, T> work, CancellationToken token)
    {
        while (true)
        {
            CancellationToken edit = await EnterAsync(token).ConfigureAwait(false);
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, edit);
                CancellationToken both = linked.Token;
                return await Task.Run(() => work(both), both).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (edit.IsCancellationRequested && !token.IsCancellationRequested)
            {
                // An edit stopped the read: read again once it has ended.
            }
            finally
            {
                Exit();
            }
        }
    }

    /// <summary>Starts a short read on the calling thread when no edit is in progress (end it with <see cref="ExitRead"/>).</summary>
    public bool TryEnterRead()
    {
        lock (_lock)
        {
            if (_edits != 0)
            {
                return false;
            }

            _readers++;
            return true;
        }
    }

    /// <summary>Ends a read <see cref="TryEnterRead"/> started.</summary>
    public void ExitRead() => Exit();

    /// <summary>Starts an edit on the calling thread: cancels the reads in progress and blocks until they have stopped.</summary>
    public void BeginEdit()
    {
        CancellationTokenSource? cancel = null;
        int thread = Environment.CurrentManagedThreadId;
        lock (_lock)
        {
            if (_edits++ == 0)
            {
                _editor = thread;
                _editEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                cancel = _reads;
            }
            else if (_editor != thread)
            {
                _edits--;
                throw new InvalidOperationException("A document is edited on one thread at a time.");
            }
        }

        cancel?.Cancel();
        lock (_lock)
        {
            while (_readers > 0)
            {
                Monitor.Wait(_lock);
            }
        }
    }

    /// <summary>Ends the edit <see cref="BeginEdit"/> started; the waiting reads go on.</summary>
    public void EndEdit()
    {
        TaskCompletionSource? ended = null;
        lock (_lock)
        {
            if (_edits == 0)
            {
                return;
            }

            if (--_edits == 0)
            {
                ended = _editEnded;
                _editEnded = null;
                _reads.Dispose(); // cancelled, and every read that held it has stopped
                _reads = new CancellationTokenSource();
            }
        }

        ended?.TrySetResult();
    }

    /// <summary>Releases the reads' token once the session has stopped every read.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            _reads.Dispose();
        }
    }

    private async ValueTask<CancellationToken> EnterAsync(CancellationToken token)
    {
        while (true)
        {
            Task wait;
            lock (_lock)
            {
                if (_edits == 0)
                {
                    _readers++;
                    return _reads.Token;
                }

                wait = _editEnded!.Task;
            }

            await wait.WaitAsync(token).ConfigureAwait(false);
        }
    }

    private void Exit()
    {
        lock (_lock)
        {
            if (--_readers == 0)
            {
                Monitor.PulseAll(_lock);
            }
        }
    }
}
