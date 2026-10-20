namespace Broadside.Viewer;

/// <summary>
/// One load per page at a time (#357): callers that ask for a page while its load runs await the same load instead of building the
/// page's model again on another worker. A caller that stops waiting leaves the load to the others; the session's closing stops it.
/// </summary>
/// <typeparam name="T">What a load produces.</typeparam>
internal sealed class PageLoads<T>
{
    private readonly Lock _lock = new();
    private readonly Dictionary<int, Task<T>> _running = [];

    public Task<T> LoadAsync(int pageIndex, Func<Task<T>> load, CancellationToken cancellationToken)
    {
        TaskCompletionSource<T>? owner = null;
        Task<T>? running;
        lock (_lock)
        {
            if (!_running.TryGetValue(pageIndex, out running))
            {
                owner = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
                running = owner.Task;
                _running.Add(pageIndex, running);
            }
        }

        if (owner is not null)
        {
            _ = RunAsync(pageIndex, load, owner);
        }

        return running.WaitAsync(cancellationToken);
    }

    private async Task RunAsync(int pageIndex, Func<Task<T>> load, TaskCompletionSource<T> owner)
    {
        try
        {
            owner.TrySetResult(await load().ConfigureAwait(false));
        }
        catch (OperationCanceledException exception)
        {
            owner.TrySetCanceled(exception.CancellationToken);
        }
#pragma warning disable CA1031 // every waiter hears the failure through the shared task
        catch (Exception exception)
#pragma warning restore CA1031
        {
            owner.TrySetException(exception);
        }
        finally
        {
            lock (_lock)
            {
                _running.Remove(pageIndex);
            }
        }
    }
}
