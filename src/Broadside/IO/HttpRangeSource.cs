using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace Broadside.IO;

/// <summary>
/// A remote file read through HTTP range requests (RFC 9110 §14) into a bounded cache of fixed-size blocks.
/// </summary>
/// <remarks>
/// <para>
/// Spec #33, viewer capability 5; ISO 32000-2 §7.5.1 and §7.5.4: a file is read at random through its cross-reference table, so a
/// viewer needs the tail (<c>startxref</c>, the table, the trailer) and the byte ranges of the objects it uses, not the whole file.
/// <see cref="OpenAsync"/> fetches the head first (Annex G, G.2: a linearized file's first page, its cross-reference section and
/// its hints are there, and a linearization parameter dictionary in the first 1024 bytes says whether the file is linearized); the
/// caller then fetches the tail or the rest of the first-page section with <see cref="FetchRangeAsync"/>. A server that answers
/// without a range gets its whole answer used as the file.
/// </para>
/// <para>
/// Reads are synchronous. A read whose blocks are cached copies them. A read that misses either blocks on the request,
/// or, inside <see cref="RunAsync{T}"/> and wherever synchronous fetching is off (always on the browser, which cannot block on
/// HTTP), throws <see cref="PdfRangeNotLoadedException"/>: <see cref="RunAsync{T}"/> catches it, fetches the range asynchronously
/// and runs the operation again, so an asynchronous prefetch is the same synchronous code run until nothing misses.
/// </para>
/// <para>
/// The cache keeps the most recently used blocks up to its limit; the blocks one fetch brings in are never evicted by that fetch,
/// so the operation that asked for them finds them when it runs again. Requests carry <c>If-Range</c> with the file's strong entity
/// tag, so a file that changes between requests fails with an <see cref="IOException"/> instead of mixing two versions.
/// </para>
/// </remarks>
internal sealed class HttpRangeSource : PdfSource
{
    /// <summary>
    /// The least number of bytes the first request asks for from the start of the file, and the least a caller fetches from the
    /// end of a file that is not linearized.
    /// </summary>
    public const int MinimumHeadLength = 64 * 1024;

    /// <summary>The most bytes one miss fetches beyond what the read asked for, guided by <see cref="AddObjectBoundaries"/>.</summary>
    private const long MaxExtentFetch = 8L << 20;

    /// <summary>The bytes past the next object's offset an object window includes: its object number, and more white-space than usual.</summary>
    private const int NextTokenAllowance = 64;

    /// <summary>How many times running the same operation may miss the same bytes after fetching them before it gives up.</summary>
    private const int MaxRepeatedMisses = 4;

    [ThreadStatic]
    private static int _withoutBlocking;

    private readonly Lock _gate = new();
    private readonly Dictionary<long, LinkedListNode<Block>> _blocks = [];
    private readonly LinkedList<Block> _recency = new();
    private readonly Dictionary<long, Task> _inFlight = [];
    private readonly HttpClient _client;
    private readonly Uri _uri;
    private readonly long _length;
    private readonly int _blockSize;
    private readonly int _maxBlocks;
    private readonly bool _synchronousFetch;
    private readonly EntityTagHeaderValue? _entityTag;
    private long[] _boundaries = [];
    private long _stamp;

    private HttpRangeSource(HttpClient client, Uri uri, long length, RangeSourceSettings settings, EntityTagHeaderValue? entityTag)
    {
        _client = client;
        _uri = uri;
        _length = length;
        _blockSize = settings.BlockSize;
        _maxBlocks = (int)Math.Clamp(settings.CacheLimit / settings.BlockSize, 1, int.MaxValue);
        _synchronousFetch = settings.SynchronousFetch;
        _entityTag = entityTag;
    }

    /// <inheritdoc/>
    public override long Length => _length;

    /// <inheritdoc/>
    public override bool IsLocal => false;

    /// <summary>
    /// Gets 16 KiB: enough for the first tokens of a cross-reference section after any usual white-space, without fetching 4 MiB
    /// around each older section of an incrementally updated file.
    /// </summary>
    public override int ProbeWindow => 16 * 1024;

    /// <summary>
    /// Gets a small first window: the object loader and the structure readers grow it while what they parse does not end inside,
    /// and every byte of a window is a byte fetched.
    /// </summary>
    public override int InitialWindow => 1024;

    /// <summary>
    /// Opens <paramref name="uri"/>: asks for the first bytes of the file and, when the server answers with a range, returns a range
    /// source holding them; otherwise returns a source over the whole answer.
    /// </summary>
    /// <param name="client">The client; not disposed.</param>
    /// <param name="uri">The file.</param>
    /// <param name="settings">Block size, cache limit and whether synchronous reads may block.</param>
    /// <param name="bufferLimit">How much of a whole-file answer is kept in memory before a temporary file is used.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The source.</returns>
    public static async Task<PdfSource> OpenAsync(HttpClient client, Uri uri, RangeSourceSettings settings, long bufferLimit, CancellationToken cancellationToken)
    {
        int head = HeadLength(settings);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Range = new RangeHeaderValue(0, head - 1);
        HttpResponseMessage? response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        try
        {
            if (response.StatusCode == HttpStatusCode.PartialContent
                && response.Content.Headers.ContentRange is { Unit: "bytes", HasRange: true, HasLength: true } range
                && range.From is { } from && range.To is { } to && range.Length is { } length
                && from >= 0 && to >= from && to < length)
            {
                byte[] data = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                if (data.LongLength != to - from + 1)
                {
                    throw new IOException(string.Create(CultureInfo.InvariantCulture, $"The server's answer to the first range request holds {data.LongLength} bytes; its Content-Range says {to - from + 1}."));
                }

                EntityTagHeaderValue? tag = response.Headers.ETag is { IsWeak: false } strong ? strong : null;
                var source = new HttpRangeSource(client, uri, length, settings, tag);
                source.Store(from, data);
                return source;
            }

            if (response.StatusCode is HttpStatusCode.PartialContent or HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                // A range without a usable Content-Range, or an empty file: ask for the whole file.
                response.Dispose();
                using var whole = new HttpRequestMessage(HttpMethod.Get, uri);
                response = await client.SendAsync(whole, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }

            // RFC 9110 §14.2: a server without range support ignores the Range header and sends the whole file.
            response.EnsureSuccessStatusCode();
            Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (body.ConfigureAwait(false))
            {
                return await CopyFromStreamAsync(body, bufferLimit, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            response.Dispose();
        }
    }

    /// <summary>Returns how many bytes the first request asks for: at least <see cref="MinimumHeadLength"/>, and a whole block.</summary>
    /// <param name="settings">The range options.</param>
    /// <returns>The length.</returns>
    public static int HeadLength(RangeSourceSettings settings) => Math.Max(MinimumHeadLength, settings.BlockSize);

    /// <summary>
    /// Records where objects, cross-reference sections and hinted byte ranges start or end (absolute offsets), so a miss inside an
    /// object fetches up to the next boundary in one request rather than growing a window request by request.
    /// </summary>
    /// <param name="boundaries">The offsets, in any order; they join the ones recorded before.</param>
    public override void AddObjectBoundaries(IEnumerable<long> boundaries)
    {
        lock (_gate)
        {
            long[] merged = [.. Volatile.Read(ref _boundaries).Concat(boundaries.Where(offset => offset >= 0 && offset < _length)).Distinct()];
            Array.Sort(merged);
            Volatile.Write(ref _boundaries, merged);
        }
    }

    /// <summary>Fetches the bytes from <paramref name="offset"/> on that are not cached, in as few requests as their gaps allow.</summary>
    /// <param name="offset">The absolute offset of the first byte.</param>
    /// <param name="length">The number of bytes.</param>
    /// <param name="cancellationToken">Cancels the requests.</param>
    /// <returns>A task that completes when the bytes are cached.</returns>
    public Task FetchRangeAsync(long offset, long length, CancellationToken cancellationToken)
    {
        long start = Math.Clamp(offset, 0, _length);
        return FetchAsync(start, Math.Min(length, _length - start), cancellationToken);
    }

    /// <summary>
    /// Returns the bytes up to the next object or cross-reference section, plus room for the token after <c>endobj</c>, which the
    /// object loader reads to know the object ended.
    /// </summary>
    /// <param name="offset">The absolute offset of the object's header.</param>
    /// <returns>The window length, or 0 before the cross-reference information is known.</returns>
    public override int ExpectedObjectWindow(long offset)
    {
        long[] boundaries = Volatile.Read(ref _boundaries);
        int next = Array.BinarySearch(boundaries, offset + 1);
        next = next < 0 ? ~next : next;
        return next < boundaries.Length ? (int)Math.Min(MaxExtentFetch, boundaries[next] - offset + NextTokenAllowance) : 0;
    }

    /// <summary>
    /// Runs <paramref name="operation"/> without blocking on the network: each time it misses, the bytes it missed are fetched
    /// asynchronously and it runs again, until it completes.
    /// </summary>
    /// <typeparam name="T">What the operation returns.</typeparam>
    /// <param name="operation">A synchronous read; it must be safe to run again after it failed on a miss.</param>
    /// <param name="cancellationToken">Cancels the fetches.</param>
    /// <returns>What the operation returned.</returns>
    public async Task<T> RunAsync<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        var misses = new MissTracker();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PdfRangeNotLoadedException? miss = TryRun(operation, out T result);
            if (miss is null)
            {
                return result;
            }

            misses.Check(miss);
            await FetchMissAsync(miss, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs <paramref name="operation"/> once without blocking on the network.
    /// </summary>
    /// <returns>The miss that stopped it, or <see langword="null"/> when it completed.</returns>
    public static PdfRangeNotLoadedException? TryRun<T>(Func<T> operation, out T result)
    {
        _withoutBlocking++;
        try
        {
            result = operation();
            return null;
        }
        catch (PdfRangeNotLoadedException miss)
        {
            result = default!;
            return miss;
        }
        finally
        {
            _withoutBlocking--;
        }
    }

    /// <summary>Fetches what <paramref name="miss"/> needed, up to the start of the next object.</summary>
    /// <param name="miss">The miss.</param>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    /// <returns>A task that completes when the fetch does.</returns>
    public Task FetchMissAsync(PdfRangeNotLoadedException miss, CancellationToken cancellationToken)
    {
        long end = Math.Min(_length, miss.Offset + miss.Length);
        long[] boundaries = Volatile.Read(ref _boundaries);
        int next = Array.BinarySearch(boundaries, miss.Offset + 1);
        next = next < 0 ? ~next : next;

        // Before the cross-reference information is read nothing is known about where objects end: fetch what was asked for.
        long objectEnd = next < boundaries.Length ? boundaries[next] : boundaries.Length > 0 ? _length : end;
        end = Math.Max(end, Math.Min(objectEnd, miss.Offset + MaxExtentFetch));
        return FetchAsync(miss.Offset, end - miss.Offset, cancellationToken);
    }

    /// <inheritdoc/>
    public override int Read(long offset, Span<byte> destination)
    {
        ThrowIfDisposed();
        if (offset < 0 || offset >= _length)
        {
            return 0;
        }

        destination = destination[..(int)Math.Min(destination.Length, _length - offset)];
        CopyOrFetch(offset, destination);
        return destination.Length;
    }

    /// <inheritdoc/>
    public override ReadOnlyMemory<byte> GetWindow(long offset, int minimumLength)
    {
        ThrowIfDisposed();
        if (offset < 0 || offset >= _length)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        byte[] window = GC.AllocateUninitializedArray<byte>((int)Math.Min(Math.Max(minimumLength, 1), _length - offset));
        CopyOrFetch(offset, window);
        return window;
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_gate)
            {
                _blocks.Clear();
                _recency.Clear();
            }
        }

        base.Dispose(disposing);
    }

    private void CopyOrFetch(long offset, Span<byte> destination)
    {
        for (int attempt = 0; !TryCopy(offset, destination); attempt++)
        {
            if (_withoutBlocking > 0 || !_synchronousFetch)
            {
                throw new PdfRangeNotLoadedException(offset, destination.Length);
            }

            if (attempt >= MaxRepeatedMisses)
            {
                throw CacheTooSmall(offset, destination.Length);
            }

            // Synchronous fetching blocks this thread on the request; it runs on the thread pool so that a caller's
            // synchronization context is never needed to complete it.
            var miss = new PdfRangeNotLoadedException(offset, destination.Length);
            Task.Run(() => FetchMissAsync(miss, CancellationToken.None)).GetAwaiter().GetResult();
        }
    }

    private IOException CacheTooSmall(long offset, long length) => new(string.Create(
        CultureInfo.InvariantCulture,
        $"Bytes {offset} to {offset + length - 1} of the remote file were fetched but did not stay in the range cache; raise PdfOptions.RangeCacheLimit (now {(long)_maxBlocks * _blockSize} bytes)."));

    private bool TryCopy(long offset, Span<byte> destination)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            long position = offset;
            int copied = 0;
            while (copied < destination.Length)
            {
                long index = position / _blockSize;
                if (!_blocks.TryGetValue(index, out LinkedListNode<Block>? node))
                {
                    return false;
                }

                Touch(node);
                int within = (int)(position - (index * _blockSize));
                int count = Math.Min(node.Value.Data.Length - within, destination.Length - copied);
                node.Value.Data.AsSpan(within, count).CopyTo(destination[copied..]);
                copied += count;
                position += count;
            }

            return true;
        }
    }

    /// <summary>Fetches the blocks covering <paramref name="length"/> bytes at <paramref name="offset"/> that are not cached.</summary>
    private async Task FetchAsync(long offset, long length, CancellationToken cancellationToken)
    {
        if (length <= 0)
        {
            return;
        }

        long first = offset / _blockSize;
        long last = (Math.Min(_length, offset + length) - 1) / _blockSize;
        var mine = new List<(long First, long Last, TaskCompletionSource Done)>();
        var others = new List<Task>();
        lock (_gate)
        {
            ThrowIfDisposed();
            for (long index = first; index <= last; index++)
            {
                if (_blocks.TryGetValue(index, out LinkedListNode<Block>? node))
                {
                    Touch(node);
                }
                else if (_inFlight.TryGetValue(index, out Task? pending))
                {
                    others.Add(pending);
                }
                else if (mine.Count > 0 && mine[^1].Last == index - 1)
                {
                    mine[^1] = mine[^1] with { Last = index };
                    _inFlight[index] = mine[^1].Done.Task;
                }
                else
                {
                    var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    mine.Add((index, index, done));
                    _inFlight[index] = done.Task;
                }
            }
        }

        Task[] fetches = [.. mine.Select(run => FetchRunAsync(run.First, run.Last, run.Done, cancellationToken))];
        await Task.WhenAll(fetches).ConfigureAwait(false);

        // A fetch another read started may fail (it was canceled, say); the operation that needed it misses again and fetches itself.
        foreach (Task other in others)
        {
            try
            {
                await other.ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Another caller's failure is that caller's; this one retries on its own request.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }
    }

    private async Task FetchRunAsync(long first, long last, TaskCompletionSource done, CancellationToken cancellationToken)
    {
        try
        {
            long start = first * _blockSize;
            long end = Math.Min(_length, (last + 1) * _blockSize);
            byte[] data = await RequestAsync(start, end, cancellationToken).ConfigureAwait(false);
            Store(start, data);
            done.SetResult();
        }
        catch (Exception exception)
        {
            done.SetException(exception);
            throw;
        }
        finally
        {
            lock (_gate)
            {
                for (long index = first; index <= last; index++)
                {
                    if (_inFlight.TryGetValue(index, out Task? task) && task == done.Task)
                    {
                        _inFlight.Remove(index);
                    }
                }
            }
        }
    }

    private async Task<byte[]> RequestAsync(long start, long end, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _uri);
        request.Headers.Range = new RangeHeaderValue(start, end - 1);
        if (_entityTag is not null)
        {
            request.Headers.IfRange = new RangeConditionHeaderValue(_entityTag);
        }

        using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode != HttpStatusCode.PartialContent
            || response.Content.Headers.ContentRange is not { Unit: "bytes", From: { } from, To: { } to, Length: { } length }
            || length != _length || from != start || to != end - 1)
        {
            throw new IOException(string.Create(
                CultureInfo.InvariantCulture,
                $"The server did not answer the request for bytes {start} to {end - 1} with that range of the same file ({(int)response.StatusCode}, Content-Range {response.Content.Headers.ContentRange}): the file changed, or the server stopped serving ranges."));
        }

        byte[] data = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return data.LongLength == end - start
            ? data
            : throw new IOException(string.Create(CultureInfo.InvariantCulture, $"The server's answer for bytes {start} to {end - 1} holds {data.LongLength} bytes."));
    }

    /// <summary>
    /// Caches each whole block (or the file's last, short block) in <paramref name="data"/>, which starts at <paramref name="start"/>,
    /// then evicts the least recently used blocks over the limit, never one this call cached: the read that asked for them must
    /// find them when it runs again.
    /// </summary>
    private void Store(long start, ReadOnlySpan<byte> data)
    {
        long end = start + data.Length;
        lock (_gate)
        {
            long firstStored = long.MaxValue;
            for (long index = (start + _blockSize - 1) / _blockSize; index * _blockSize < end; index++)
            {
                long blockStart = index * _blockSize;
                long blockEnd = Math.Min(_length, blockStart + _blockSize);
                if (blockEnd > end || _blocks.ContainsKey(index))
                {
                    continue;
                }

                byte[] block = data.Slice((int)(blockStart - start), (int)(blockEnd - blockStart)).ToArray();
                _blocks[index] = _recency.AddLast(new Block(index, block, _stamp));
                firstStored = Math.Min(firstStored, _stamp++);
            }

            while (_blocks.Count > _maxBlocks && _recency.First is { } oldest && oldest.Value.Stamp < firstStored)
            {
                _recency.RemoveFirst();
                _blocks.Remove(oldest.Value.Index);
            }
        }
    }

    private void Touch(LinkedListNode<Block> node)
    {
        _recency.Remove(node);
        _recency.AddLast(node);
    }

    /// <summary>One cached block: its index and bytes (shorter than the block size only at the end of the file).</summary>
    /// <param name="Index">The block's index: its offset divided by the block size.</param>
    /// <param name="Data">The bytes.</param>
    /// <param name="Stamp">When it was cached, in the order of caching.</param>
    private sealed record Block(long Index, byte[] Data, long Stamp);

    /// <summary>Stops an operation that keeps missing the same bytes after they were fetched: the cache cannot hold them.</summary>
    internal sealed class MissTracker
    {
        private long _offset = -1;
        private long _length;
        private int _repeats;

        public void Check(PdfRangeNotLoadedException miss)
        {
            if (miss.Offset == _offset && miss.Length == _length)
            {
                if (++_repeats >= MaxRepeatedMisses)
                {
                    throw new IOException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"Bytes {miss.Offset} to {miss.Offset + miss.Length - 1} of the remote file were fetched {_repeats} times without staying in the range cache; raise PdfOptions.RangeCacheLimit."));
                }

                return;
            }

            (_offset, _length, _repeats) = (miss.Offset, miss.Length, 0);
        }
    }
}

/// <summary>How a range source caches and fetches: the engine's range options.</summary>
/// <param name="BlockSize">The size of a cached block and the unit of a request, in bytes.</param>
/// <param name="CacheLimit">The most bytes of blocks the cache keeps.</param>
/// <param name="SynchronousFetch">Whether a synchronous read that misses may block on a request.</param>
internal readonly record struct RangeSourceSettings(int BlockSize, long CacheLimit, bool SynchronousFetch);
