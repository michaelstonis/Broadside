namespace Broadside.Tests.Document;

/// <summary>
/// A stream over bytes that is not a <see cref="MemoryStream"/> or <see cref="FileStream"/> (so the reader cannot take a shortcut),
/// optionally not seekable or not readable synchronously, counting the bytes read through it.
/// </summary>
internal sealed class ProbeStream(byte[] bytes, bool seekable = true, bool synchronous = true) : Stream
{
    private long _position;
    private long _bytesRead;

    public long BytesRead => Interlocked.Read(ref _bytesRead);

    public bool IsDisposed { get; private set; }

    public override bool CanRead => !IsDisposed;

    public override bool CanSeek => seekable && !IsDisposed;

    public override bool CanWrite => false;

    public override long Length => seekable ? bytes.Length : throw new NotSupportedException();

    public override long Position
    {
        get => seekable ? _position : throw new NotSupportedException();
        set => _position = seekable ? value : throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (!synchronous)
        {
            throw new InvalidOperationException("This stream can only be read asynchronously.");
        }

        return ReadCore(buffer);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(ReadCore(buffer.Span));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Task.FromResult(ReadCore(buffer.AsSpan(offset, count)));

    public override long Seek(long offset, SeekOrigin origin)
    {
        if (!seekable)
        {
            throw new NotSupportedException();
        }

        _position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => bytes.Length + offset,
        };
        return _position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }

    private int ReadCore(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        int count = (int)Math.Clamp(bytes.Length - _position, 0, buffer.Length);
        bytes.AsSpan((int)_position, count).CopyTo(buffer);
        _position += count;
        Interlocked.Add(ref _bytesRead, count);
        return count;
    }
}
