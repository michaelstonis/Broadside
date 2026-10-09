namespace Broadside.IO;

/// <summary>
/// A seekable stream read in place: each read seeks and reads under a lock, since a <see cref="Stream"/> has one shared position and
/// is not safe for concurrent use. Windows are copies of the bytes read.
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.5.4: random access through the cross-reference table, so opening a large file through a stream reads its
/// structure and the objects used, not the whole stream. The stream's length is read once, when the source is created; the stream
/// must stay open and unchanged while the source is in use.
/// </remarks>
internal sealed class StreamSource : PdfSource
{
    private readonly Lock _gate = new();
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly long _start;
    private readonly long _length;

    /// <summary>Initializes a new instance of the <see cref="StreamSource"/> class over <paramref name="stream"/> from its current position.</summary>
    /// <param name="stream">The stream; seekable and readable.</param>
    /// <param name="ownsStream">Whether the source disposes the stream.</param>
    public StreamSource(Stream stream, bool ownsStream)
    {
        _stream = stream;
        _ownsStream = ownsStream;
        _start = stream.Position;
        _length = Math.Max(0, stream.Length - _start);
    }

    /// <inheritdoc/>
    public override long Length => _length;

    /// <inheritdoc/>
    public override int Read(long offset, Span<byte> destination)
    {
        ThrowIfDisposed();
        if (offset < 0 || offset >= _length)
        {
            return 0;
        }

        destination = destination[..(int)Math.Min(destination.Length, _length - offset)];
        lock (_gate)
        {
            ThrowIfDisposed();
            _stream.Position = _start + offset;
            int total = 0;
            while (total < destination.Length)
            {
                int read = _stream.Read(destination[total..]);
                if (read == 0)
                {
                    break;
                }

                total += read;
            }

            return total;
        }
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
        return window.AsMemory(0, Read(offset, window));
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_gate)
            {
                base.Dispose(disposing);
                if (_ownsStream)
                {
                    _stream.Dispose();
                }
            }

            return;
        }

        base.Dispose(disposing);
    }
}
