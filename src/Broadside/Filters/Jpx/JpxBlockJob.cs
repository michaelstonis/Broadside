using System.Buffers;
using System.Runtime.ExceptionServices;

namespace Broadside.Filters.Jpx;

/// <summary>One code-block of a tile-component to decode, with what its decode found.</summary>
internal struct JpxBlockWork
{
    /// <summary>The first of the code-block's chunks in the tile decoder's chunk list.</summary>
    public int FirstChunk;

    /// <summary>The code-block's compressed length, all chunks together.</summary>
    public int DataLength;

    /// <summary>Where the code-block's top-left sample is in the component buffer.</summary>
    public int Origin;

    /// <summary>The magnitude bit-planes of its sub-band with the region-of-interest shift, for the diagnostic.</summary>
    public int Planes;

    /// <summary>0 to decode; 1 when the sub-band has too many bit-planes; 2 when the code-block claims too many missing bit-planes.</summary>
    public int Status;

    /// <summary>Whether the segmentation symbols, where present, were right (the decoder's result).</summary>
    public bool Symbols;

    /// <summary>The code-block's coding parameters.</summary>
    public JpxBlockCoding Coding;
}

/// <summary>
/// Decodes the code-blocks of one tile-component on the calling thread and on thread-pool helpers (ITU-T T.800 Annex D: each
/// code-block is coded independently, and each writes only its own samples of the component buffer).
/// </summary>
/// <remarks>
/// <para>
/// The caller takes part and helpers join as the thread pool runs them: every thread takes the next code-block from a shared counter
/// until none is left. A helper the pool has not started when the caller runs out of code-blocks finds the job closed and returns
/// at once, so a busy pool (several tiles rendering at once) costs nothing: the caller decodes everything itself and waits only for
/// helpers already decoding. Each helper borrows a <see cref="JpxCodeBlockDecoder"/> and its join buffers from a pool, and each
/// thread keeps its job and work list, so a decode allocates nothing per code-block and, once warm, nothing per call.
/// </para>
/// <para>
/// The samples do not depend on which thread decodes a code-block. Diagnostics are reported by the caller afterwards in code-block
/// order, as the serial decode reports them. Never used in browser builds, which have no threads.
/// </para>
/// </remarks>
internal sealed unsafe class JpxBlockJob : IThreadPoolWorkItem
{
    /// <summary>Code-block samples one thread should have at least, so a small image stays on one thread (about 3 ms of tier-1).</summary>
    internal const long DefaultSamplesPerThread = 1 << 16;

    /// <summary>The most threads one tile-component is decoded on, the caller included.</summary>
    internal static readonly int MaxThreads = Math.Min(Environment.ProcessorCount, 8);

    [ThreadStatic]
    private static long _samplesPerThreadOverride;

    [ThreadStatic]
    private static JpxBlockJob? _job;

    private static readonly Scratch?[] ScratchPool = new Scratch?[16];
    private static int _scratchCount;

    private readonly object _gate = new();
    private JpxBlockWork[] _work = [];
    private int _count;
    private int _next;
    private int _outstanding;
    private int _active;
    private bool _closed;
    private byte* _data;
    private int _dataLength;
    private JpxChunk[] _chunks = [];
    private int[] _buffer = [];
    private int _stride;
    private CancellationToken _token;
    private ExceptionDispatchInfo? _error;

    /// <summary>Gets or sets, for tests, an override of <see cref="DefaultSamplesPerThread"/> on the calling thread (when positive).</summary>
    internal static long SamplesPerThreadOverride
    {
        get => _samplesPerThreadOverride;
        set => _samplesPerThreadOverride = value;
    }

    /// <summary>Gets the code-block samples one thread should have at least.</summary>
    public static long SamplesPerThread => _samplesPerThreadOverride > 0 ? _samplesPerThreadOverride : DefaultSamplesPerThread;

    /// <summary>Gets whether code-blocks may be decoded on several threads: not in browser builds, and only with several cores.</summary>
    public static bool Enabled => !OperatingSystem.IsBrowser() && !OperatingSystem.IsWasi() && MaxThreads > 1;

    /// <summary>Gets the work list to fill, at least <paramref name="capacity"/> long.</summary>
    public Span<JpxBlockWork> Work(int capacity)
    {
        if (_work.Length < capacity)
        {
            if (_work.Length > 0)
            {
                ArrayPool<JpxBlockWork>.Shared.Return(_work);
            }

            _work = ArrayPool<JpxBlockWork>.Shared.Rent(capacity);
        }

        return _work;
    }

    /// <summary>Gets the calling thread's job, or a new one while its job still has helpers queued from an earlier decode.</summary>
    public static JpxBlockJob ForThread()
    {
        JpxBlockJob? job = _job;
        if (job is not null)
        {
            lock (job._gate)
            {
                if (job._outstanding == 0)
                {
                    return job;
                }
            }
        }

        return _job = new JpxBlockJob();
    }

    /// <summary>
    /// Decodes the first <paramref name="count"/> entries of the work list on <paramref name="threads"/> threads, the caller one of
    /// them, and returns when every code-block is decoded or the decode is cancelled (the caller then checks the token).
    /// </summary>
    public void Run(
        int count,
        int threads,
        ReadOnlySpan<byte> data,
        JpxChunk[] chunks,
        int[] buffer,
        int stride,
        JpxCodeBlockDecoder decoder,
        ref byte[] joined,
        ref JpxSegment[] segments,
        CancellationToken token)
    {
        fixed (byte* pointer = data)
        {
            _count = count;
            _next = 0;
            _active = 0;
            _closed = false;
            _error = null;
            _data = pointer;
            _dataLength = data.Length;
            _chunks = chunks;
            _buffer = buffer;
            _stride = stride;
            _token = token;
            _outstanding = threads - 1;
            for (int i = 1; i < threads; i++)
            {
                ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
            }

            try
            {
                DecodeAll(decoder, ref joined, ref segments);
            }
            finally
            {
                lock (_gate)
                {
                    _closed = true;
                    while (_active > 0)
                    {
                        Monitor.Wait(_gate);
                    }

                    _data = null;
                    _chunks = [];
                    _buffer = [];
                }
            }
        }

        _error?.Throw();
    }

    /// <summary>A helper: decodes code-blocks until none is left, unless the caller already finished.</summary>
    void IThreadPoolWorkItem.Execute()
    {
        lock (_gate)
        {
            _outstanding--;
            if (_closed)
            {
                return;
            }

            _active++;
        }

#pragma warning disable CA2000 // The scratch goes back to the pool, which disposes what it cannot keep.
        Scratch scratch = RentScratch();
#pragma warning restore CA2000
        try
        {
            DecodeAll(scratch.Decoder, ref scratch.Joined, ref scratch.Segments);
        }
#pragma warning disable CA1031 // Rethrown on the calling thread by Run.
        catch (Exception e)
        {
            Interlocked.CompareExchange(ref _error, ExceptionDispatchInfo.Capture(e), null);
            Volatile.Write(ref _next, int.MaxValue / 2);
        }
#pragma warning restore CA1031
        finally
        {
            ReturnScratch(scratch);
            lock (_gate)
            {
                if (--_active == 0)
                {
                    Monitor.PulseAll(_gate);
                }
            }
        }
    }

    private void DecodeAll(JpxCodeBlockDecoder decoder, ref byte[] joined, ref JpxSegment[] segments)
    {
        var data = new ReadOnlySpan<byte>(_data, _dataLength);
        while (!_token.IsCancellationRequested)
        {
            int index = Interlocked.Increment(ref _next) - 1;
            if (index >= _count)
            {
                return;
            }

            ref JpxBlockWork work = ref _work[index];
            if (work.Status != 0)
            {
                continue;
            }

            ReadOnlySpan<byte> bytes = JpxTileDecoder.Join(_chunks, work.FirstChunk, work.DataLength, data, ref joined, ref segments, out int segmentCount);
            work.Symbols = decoder.Decode(bytes, segments.AsSpan(0, segmentCount), work.Coding, _buffer.AsSpan(work.Origin), _stride);
        }
    }

    private static Scratch RentScratch()
    {
        lock (ScratchPool)
        {
            if (_scratchCount > 0)
            {
                Scratch scratch = ScratchPool[--_scratchCount]!;
                ScratchPool[_scratchCount] = null;
                return scratch;
            }
        }

        return new Scratch();
    }

    private static void ReturnScratch(Scratch scratch)
    {
        lock (ScratchPool)
        {
            if (_scratchCount < ScratchPool.Length)
            {
                ScratchPool[_scratchCount++] = scratch;
                return;
            }
        }

        scratch.Dispose();
    }

    /// <summary>A helper thread's code-block decoder and join buffers, pooled across decodes.</summary>
    private sealed class Scratch : IDisposable
    {
        public readonly JpxCodeBlockDecoder Decoder = new();
        public byte[] Joined = ArrayPool<byte>.Shared.Rent(256);
        public JpxSegment[] Segments = ArrayPool<JpxSegment>.Shared.Rent(16);

        public void Dispose()
        {
            Decoder.Dispose();
            ArrayPool<byte>.Shared.Return(Joined);
            ArrayPool<JpxSegment>.Shared.Return(Segments);
        }
    }
}
