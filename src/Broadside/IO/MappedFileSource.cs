using System.Buffers;
using System.IO.MemoryMappedFiles;

namespace Broadside.IO;

/// <summary>
/// A file mapped into memory: windows are views of the mapping, so parsing an object reads only the pages it touches and copies
/// nothing, whatever the size of the file.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.5.4: random access through the cross-reference table. The whole file is mapped once, read-only. A span cannot
/// exceed <see cref="int.MaxValue"/> bytes, so the mapping is handed out through overlapping segments that start every 1 GiB and
/// reach up to 2 GiB: a window from any offset has at least 1 GiB (or the rest of the file) in it.
/// </para>
/// <para>
/// Reads copy out of the mapping while holding a reference on it, so disposing the source while another thread reads makes that
/// read throw <see cref="ObjectDisposedException"/> or complete, never fault. Windows are not protected that way: disposing a
/// document while other threads still parse it is the caller's error. The file must not be truncated or rewritten while it is open.
/// </para>
/// </remarks>
internal sealed unsafe class MappedFileSource : PdfSource
{
    private const long SegmentStride = 1L << 30;

    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;
    private readonly FileStream? _ownedFile;
    private readonly byte* _start;
    private readonly long _length;
    private readonly Segment?[] _segments;

    private MappedFileSource(MemoryMappedFile file, MemoryMappedViewAccessor view, FileStream? ownedFile, long start, long length)
    {
        _file = file;
        _view = view;
        _ownedFile = ownedFile;
        _length = length;
        byte* pointer = null;
        view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
        _start = pointer + view.PointerOffset + start;
        _segments = new Segment?[(int)((length + SegmentStride - 1) / SegmentStride)];
    }

    /// <inheritdoc/>
    public override long Length => _length;

    /// <summary>Maps <paramref name="file"/> from its current position to its end.</summary>
    /// <param name="file">The file; it must be readable.</param>
    /// <param name="ownsFile">Whether the source disposes the file.</param>
    /// <returns>The source, or <see langword="null"/> when the platform or the file cannot be mapped.</returns>
    public static PdfSource? TryCreate(FileStream file, bool ownsFile)
    {
        if (OperatingSystem.IsBrowser() || OperatingSystem.IsWasi())
        {
            return null;
        }

        long start = file.Position;
        long length = file.Length - start;
        if (length <= 0)
        {
            if (ownsFile)
            {
                file.Dispose();
            }

            return FromMemory(ReadOnlyMemory<byte>.Empty);
        }

        MemoryMappedFile? mapping = null;
        try
        {
            mapping = MemoryMappedFile.CreateFromFile(file, mapName: null, capacity: 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: true);
            MemoryMappedViewAccessor view = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            return new MappedFileSource(mapping, view, ownsFile ? file : null, start, length);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or PlatformNotSupportedException)
        {
            mapping?.Dispose();
            return null;
        }
    }

    /// <inheritdoc/>
    public override int Read(long offset, Span<byte> destination)
    {
        ThrowIfDisposed();
        if (offset < 0 || offset >= _length)
        {
            return 0;
        }

        int count = (int)Math.Min(destination.Length, _length - offset);
        bool added = false;
        try
        {
            // Holds the mapping open across the copy: Dispose unmaps only after the last reader releases it.
            _view.SafeMemoryMappedViewHandle.DangerousAddRef(ref added);
            new ReadOnlySpan<byte>(_start + offset, count).CopyTo(destination);
        }
        finally
        {
            if (added)
            {
                _view.SafeMemoryMappedViewHandle.DangerousRelease();
            }
        }

        return count;
    }

    /// <inheritdoc/>
    public override ReadOnlyMemory<byte> GetWindow(long offset, int minimumLength)
    {
        ThrowIfDisposed();
        if (offset < 0 || offset >= _length)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        int index = (int)(offset / SegmentStride);
        Segment segment = Volatile.Read(ref _segments[index]) ?? CreateSegment(index);
        return segment.Memory[(int)(offset - (index * SegmentStride))..];
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _view.SafeMemoryMappedViewHandle.ReleasePointer();
            _view.Dispose();
            _file.Dispose();
            _ownedFile?.Dispose();
        }

        base.Dispose(disposing);
    }

    private Segment CreateSegment(int index)
    {
        long start = index * SegmentStride;
        var segment = new Segment(_start + start, (int)Math.Min(int.MaxValue, _length - start));
        return Interlocked.CompareExchange(ref _segments[index], segment, null) ?? segment;
    }

    /// <summary>Up to 2 GiB of the mapping as <see cref="Memory{T}"/>.</summary>
    private sealed class Segment(byte* pointer, int length) : MemoryManager<byte>
    {
        public override Span<byte> GetSpan() => new(pointer, length);

        public override MemoryHandle Pin(int elementIndex = 0) => new(pointer + elementIndex);

        public override void Unpin()
        {
        }

        protected override void Dispose(bool disposing)
        {
        }
    }
}
