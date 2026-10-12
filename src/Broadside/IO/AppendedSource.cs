using Broadside.Objects;

namespace Broadside.IO;

/// <summary>
/// A file that an incremental update was appended to: the bytes of the file the document was read from, followed by the update the
/// document wrote, held in memory. What a document reads once an incremental save has written its last byte.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.5.6: an update is appended and leaves the original bytes intact, so the original source still serves every offset
/// before the update, and objects already loaded from it (stream data read on access included) stay valid. A later update is
/// appended to the same in-memory part, so the source never nests.
/// </para>
/// <para>
/// A window that starts in the original and needs bytes of the update is copied into one array; no structure crosses the boundary in
/// a well-formed file, so that only happens to a parse looking past the original's end (the search for the last <c>startxref</c>).
/// </para>
/// </remarks>
internal sealed class AppendedSource : PdfSource
{
    private readonly PdfSource _original;
    private readonly long _originalLength;
    private readonly ReadOnlyMemory<byte> _appended;

    private AppendedSource(PdfSource original, ReadOnlyMemory<byte> appended)
    {
        _original = original;
        _originalLength = original.Length;
        _appended = appended;
    }

    /// <inheritdoc/>
    public override long Length => _originalLength + _appended.Length;

    /// <inheritdoc/>
    public override int InitialWindow => _original.InitialWindow;

    /// <summary>Returns <paramref name="source"/> with <paramref name="update"/> appended; the result owns <paramref name="source"/>.</summary>
    /// <param name="source">The file the update was appended to.</param>
    /// <param name="update">The update's bytes, which nobody changes afterwards.</param>
    /// <returns>The source of the updated file.</returns>
    public static AppendedSource Append(PdfSource source, ReadOnlyMemory<byte> update)
    {
        if (source is not AppendedSource appended)
        {
            return new AppendedSource(source, update);
        }

        byte[] joined = new byte[appended._appended.Length + update.Length];
        appended._appended.CopyTo(joined);
        update.CopyTo(joined.AsMemory(appended._appended.Length));
        return new AppendedSource(appended._original, joined);
    }

    /// <inheritdoc/>
    public override int Read(long offset, Span<byte> destination)
    {
        ThrowIfDisposed();
        if (offset < 0 || offset >= Length)
        {
            return 0;
        }

        int read = 0;
        if (offset < _originalLength)
        {
            int wanted = (int)Math.Min(destination.Length, _originalLength - offset);
            read = _original.Read(offset, destination[..wanted]);
            if (read < wanted)
            {
                return read;
            }
        }

        long start = offset + read - _originalLength;
        if (read < destination.Length && start < _appended.Length)
        {
            ReadOnlySpan<byte> rest = _appended.Span[(int)start..];
            int count = Math.Min(rest.Length, destination.Length - read);
            rest[..count].CopyTo(destination[read..]);
            read += count;
        }

        return read;
    }

    /// <inheritdoc/>
    public override ReadOnlyMemory<byte> GetWindow(long offset, int minimumLength)
    {
        ThrowIfDisposed();
        if (offset < 0 || offset >= Length)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        if (offset >= _originalLength)
        {
            return _appended[(int)(offset - _originalLength)..];
        }

        if (offset + minimumLength <= _originalLength)
        {
            return _original.GetWindow(offset, minimumLength);
        }

        // The window crosses into the update: copy the rest of the file, less than the minimum of the original and the update.
        byte[] window = GC.AllocateUninitializedArray<byte>((int)Math.Min(Array.MaxLength, Length - offset));
        return window.AsMemory(0, Read(offset, window));
    }

    /// <inheritdoc/>
    public override CosStream CreateStream(CosDictionary dictionary, long offset, int length)
    {
        if (offset + length <= _originalLength)
        {
            return _original.CreateStream(dictionary, offset, length);
        }

        return offset >= _originalLength
            ? new CosStream(dictionary, _appended.Slice((int)(offset - _originalLength), length))
            : base.CreateStream(dictionary, offset, length);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _original.Dispose();
        }

        base.Dispose(disposing);
    }
}
