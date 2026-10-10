using System.Buffers;

namespace Broadside.Filters;

/// <summary>
/// A growable <see cref="IBufferWriter{T}"/> over arrays rented from <see cref="ArrayPool{T}.Shared"/>, with a length limit: the
/// buffer between the stages of a filter chain. Writing past the limit keeps the first <see cref="Limit"/> bytes and throws
/// <see cref="DecodedLengthExceededException"/>, which the pipeline turns into a diagnostic.
/// </summary>
internal sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
{
    private const int MinimumCapacity = 256;
    private byte[] _buffer;
    private int _written;

    /// <summary>Initializes a new instance of the <see cref="PooledBufferWriter"/> class.</summary>
    /// <param name="initialCapacity">The capacity to start with, such as the stream's <c>DL</c> hint.</param>
    /// <param name="limit">The most bytes the writer accepts.</param>
    public PooledBufferWriter(long initialCapacity, long limit)
    {
        Limit = (int)Math.Clamp(limit, 0, Array.MaxLength);
        _buffer = ArrayPool<byte>.Shared.Rent((int)Math.Clamp(initialCapacity, MinimumCapacity, Math.Max(MinimumCapacity, Math.Min(Limit, 1 << 24))));
    }

    /// <summary>Gets the most bytes the writer accepts.</summary>
    public int Limit { get; }

    /// <summary>Gets a value indicating whether a write went past <see cref="Limit"/>.</summary>
    public bool Exceeded { get; private set; }

    /// <summary>Gets the bytes written so far.</summary>
    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

    /// <summary>Gets the bytes written so far.</summary>
    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);

    /// <summary>Forgets what was written, keeping the buffer.</summary>
    public void Clear()
    {
        _written = 0;
        Exceeded = false;
    }

    /// <inheritdoc/>
    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > _buffer.Length - _written)
        {
            throw new InvalidOperationException("Cannot advance past the end of the buffer.");
        }

        if (count > Limit - _written)
        {
            _written = Limit;
            Exceeded = true;
            throw new DecodedLengthExceededException();
        }

        _written += count;
    }

    /// <inheritdoc/>
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Reserve(sizeHint);
        return _buffer.AsMemory(_written);
    }

    /// <inheritdoc/>
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Reserve(sizeHint);
        return _buffer.AsSpan(_written);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        byte[] buffer = _buffer;
        _buffer = [];
        _written = 0;
        if (buffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void Reserve(int sizeHint)
    {
        if (Exceeded)
        {
            throw new DecodedLengthExceededException();
        }

        int needed = Math.Max(sizeHint, 1);
        if (_buffer.Length - _written >= needed)
        {
            return;
        }

        long capacity = Math.Max((long)_buffer.Length * 2, (long)_written + needed);
        capacity = Math.Min(capacity, Math.Max((long)Limit + needed, _written + needed));
        if (capacity > Array.MaxLength)
        {
            capacity = Array.MaxLength;
        }

        byte[] larger = ArrayPool<byte>.Shared.Rent((int)capacity);
        _buffer.AsSpan(0, _written).CopyTo(larger);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = larger;
    }
}

/// <summary>Thrown by <see cref="PooledBufferWriter"/> when a filter writes past the decoded-length limit; never escapes the pipeline.</summary>
#pragma warning disable CA1064 // Internal control flow between the writer and the pipeline; never public.
internal sealed class DecodedLengthExceededException : Exception
#pragma warning restore CA1064
{
    /// <summary>Initializes a new instance of the <see cref="DecodedLengthExceededException"/> class.</summary>
    public DecodedLengthExceededException()
        : base("The decoded data exceeds the configured limit.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DecodedLengthExceededException"/> class.</summary>
    /// <param name="message">The message.</param>
    public DecodedLengthExceededException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DecodedLengthExceededException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public DecodedLengthExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
