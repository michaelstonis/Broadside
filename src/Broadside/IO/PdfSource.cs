using System.IO.MemoryMappedFiles;
using Broadside.Objects;

namespace Broadside.IO;

/// <summary>The bytes of one PDF file, read by position.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.5.1: a file is read at random, through its cross-reference table, never serially; §7.5.4: the table "permits
/// random access to indirect objects ... so that the entire PDF file need not be read". A source therefore has no cursor: every read
/// names its offset, so concurrent readers never interfere. Offsets are <see langword="long"/> everywhere.
/// </para>
/// <para>
/// Implementations: memory (the caller's bytes, never copied), a memory-mapped file (a path, or a <see cref="FileStream"/>), a
/// seekable stream read in place under a lock, and a non-seekable stream copied when the document is opened (into memory, or into a
/// temporary file past the engine's buffer limit). Every implementation is safe for concurrent reads; none survives
/// <see cref="Dispose()"/>, after which reads throw <see cref="ObjectDisposedException"/>.
/// </para>
/// </remarks>
internal abstract class PdfSource : IDisposable
{
    /// <summary>
    /// The window <see cref="GetWindow(long)"/> asks a windowed source for: enough for any cross-reference section, trailer or
    /// object stream header a structure reader parses in one piece.
    /// </summary>
    public const int StructureWindow = 4 << 20;

    private const int CopyBufferLength = 81920;

    private volatile bool _disposed;

    /// <summary>Gets the length of the file in bytes.</summary>
    public abstract long Length { get; }

    /// <summary>Creates a source over bytes the caller promises not to change while the source is in use. The bytes are not copied.</summary>
    /// <param name="bytes">The file.</param>
    /// <returns>The source.</returns>
    public static PdfSource FromMemory(ReadOnlyMemory<byte> bytes) => new MemorySource(bytes);

    /// <summary>Creates a source over the file at <paramref name="path"/>, memory-mapped: nothing is read until it is used.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The source.</returns>
    public static PdfSource FromFile(string path)
    {
        FileStream? file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.RandomAccess);
        try
        {
            PdfSource source = FromOwnedFile(file);
            file = null;
            return source;
        }
        finally
        {
            file?.Dispose();
        }
    }

    /// <summary>
    /// Creates a source over the bytes of <paramref name="stream"/> from its current position to its end. A seekable stream is read
    /// in place, a <see cref="FileStream"/> memory-mapped; a non-seekable one is copied now.
    /// </summary>
    /// <param name="stream">The stream; not disposed, and it must stay open and unchanged while the source is in use.</param>
    /// <param name="bufferLimit">How many bytes of a non-seekable stream to copy into memory before using a temporary file.</param>
    /// <returns>The source.</returns>
    public static PdfSource FromStream(Stream stream, long bufferLimit)
    {
        if (stream.CanSeek)
        {
            return FromSeekableStream(stream);
        }

        bufferLimit = Math.Min(bufferLimit, Array.MaxLength);
        byte[] buffer = new byte[CopyBufferLength];
        var memory = new MemoryStream();
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            if (memory.Length + read > bufferLimit)
            {
                FileStream? file = CreateTemporaryFile(asynchronous: false);
                try
                {
                    memory.WriteTo(file);
                    file.Write(buffer, 0, read);
                    stream.CopyTo(file);
                    file.Flush();
                    file.Position = 0;
                    PdfSource source = FromOwnedFile(file);
                    file = null;
                    return source;
                }
                finally
                {
                    file?.Dispose();
                }
            }

            memory.Write(buffer, 0, read);
        }

        return new MemorySource(memory.GetBuffer().AsMemory(0, (int)memory.Length));
    }

    /// <summary>As <see cref="FromStream"/>, copying a non-seekable stream asynchronously; a seekable one is read in place later.</summary>
    /// <param name="stream">The stream; not disposed.</param>
    /// <param name="bufferLimit">How many bytes of a non-seekable stream to copy into memory before using a temporary file.</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <returns>The source.</returns>
    public static async Task<PdfSource> FromStreamAsync(Stream stream, long bufferLimit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (stream.CanSeek)
        {
            return FromSeekableStream(stream);
        }

        bufferLimit = Math.Min(bufferLimit, Array.MaxLength);
        byte[] buffer = new byte[CopyBufferLength];
        var memory = new MemoryStream();
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (memory.Length + read > bufferLimit)
            {
#pragma warning disable CA2000 // Disposed in the finally below unless the source took ownership; the analyzer cannot follow await.
                FileStream? file = CreateTemporaryFile(asynchronous: true);
#pragma warning restore CA2000
                try
                {
                    await file.WriteAsync(memory.GetBuffer().AsMemory(0, (int)memory.Length), cancellationToken).ConfigureAwait(false);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    await stream.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
                    await file.FlushAsync(cancellationToken).ConfigureAwait(false);
                    file.Position = 0;
                    PdfSource source = FromOwnedFile(file);
                    file = null;
                    return source;
                }
                finally
                {
                    if (file is not null)
                    {
                        await file.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }

            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return new MemorySource(memory.GetBuffer().AsMemory(0, (int)memory.Length));
    }

    /// <summary>Copies the bytes at <paramref name="offset"/> into <paramref name="destination"/>.</summary>
    /// <param name="offset">The offset of the first byte to copy.</param>
    /// <param name="destination">Where to copy to.</param>
    /// <returns>The number of bytes copied: fewer than the destination holds only at the end of the file.</returns>
    public abstract int Read(long offset, Span<byte> destination);

    /// <summary>
    /// Returns a contiguous view of the bytes from <paramref name="offset"/> on, for the span-based parsers. The view ends at the end
    /// of the file or, for a source that reads in windows, after at least <see cref="StructureWindow"/> bytes; parsers treat its end as
    /// the end of input.
    /// </summary>
    /// <param name="offset">The offset of the first byte of the view.</param>
    /// <returns>The view, empty when <paramref name="offset"/> is at or past the end of the file.</returns>
    public ReadOnlyMemory<byte> GetWindow(long offset) => GetWindow(offset, StructureWindow);

    /// <summary>
    /// Returns a contiguous view of the bytes from <paramref name="offset"/> on, at least <paramref name="minimumLength"/> long unless
    /// the file ends sooner. A source that holds or maps the whole file returns the rest of it (up to 1 GiB or more) whatever the
    /// minimum; a source that reads in windows reads about <paramref name="minimumLength"/> bytes.
    /// </summary>
    /// <param name="offset">The offset of the first byte of the view.</param>
    /// <param name="minimumLength">The least number of bytes the caller needs, when the file has them.</param>
    /// <returns>The view, empty when <paramref name="offset"/> is at or past the end of the file.</returns>
    public abstract ReadOnlyMemory<byte> GetWindow(long offset, int minimumLength);

    /// <summary>Creates the stream object for data at <paramref name="offset"/>, without copying the data out of the source.</summary>
    /// <param name="dictionary">The stream dictionary.</param>
    /// <param name="offset">The absolute offset of the first data byte.</param>
    /// <param name="length">The number of data bytes.</param>
    /// <returns>The stream, whose data is read from this source on each access.</returns>
    public virtual CosStream CreateStream(CosDictionary dictionary, long offset, int length) => new(dictionary, new SourceData(this, offset, length));

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases what the source holds.</summary>
    /// <param name="disposing"><see langword="true"/> when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing) => _disposed = true;

    /// <summary>Throws <see cref="ObjectDisposedException"/> once the source is disposed.</summary>
    protected void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>A file the source owns: mapped where the platform allows it, else read in place.</summary>
    private static PdfSource FromOwnedFile(FileStream file) => MappedFileSource.TryCreate(file, ownsFile: true) ?? new StreamSource(file, ownsStream: true);

    private static PdfSource FromSeekableStream(Stream stream) =>
        (stream is FileStream { CanRead: true } file ? MappedFileSource.TryCreate(file, ownsFile: false) : null) ?? new StreamSource(stream, ownsStream: false);

    /// <summary>A temporary file deleted when the source closes it.</summary>
    private static FileStream CreateTemporaryFile(bool asynchronous) => new(
        Path.Combine(Path.GetTempPath(), "broadside-" + Path.GetRandomFileName()),
        FileMode.CreateNew,
        FileAccess.ReadWrite,
        FileShare.None,
        CopyBufferLength,
        FileOptions.DeleteOnClose | (asynchronous ? FileOptions.Asynchronous : FileOptions.None));

    /// <summary>Stream data read from the source on each access, so a cached stream object holds no data.</summary>
    private sealed class SourceData(PdfSource source, long offset, int length) : DeferredStreamData
    {
        public override int Length => length;

        public override ReadOnlyMemory<byte> Read()
        {
            if (length == 0)
            {
                return ReadOnlyMemory<byte>.Empty;
            }

            byte[] data = GC.AllocateUninitializedArray<byte>(length);
            int read = source.Read(offset, data);
            return data.AsMemory(0, read);
        }
    }

    /// <summary>The caller's bytes: windows and stream data are slices of them.</summary>
    private sealed class MemorySource(ReadOnlyMemory<byte> bytes) : PdfSource
    {
        public override long Length => bytes.Length;

        public override int Read(long offset, Span<byte> destination)
        {
            ThrowIfDisposed();
            if (offset < 0 || offset >= bytes.Length)
            {
                return 0;
            }

            ReadOnlySpan<byte> available = bytes.Span[(int)offset..];
            int count = Math.Min(available.Length, destination.Length);
            available[..count].CopyTo(destination);
            return count;
        }

        public override ReadOnlyMemory<byte> GetWindow(long offset, int minimumLength)
        {
            ThrowIfDisposed();
            return offset < 0 || offset >= bytes.Length ? ReadOnlyMemory<byte>.Empty : bytes[(int)offset..];
        }

        // The caller keeps the bytes alive and unchanged, so a slice is as good as a copy and costs nothing.
        public override CosStream CreateStream(CosDictionary dictionary, long offset, int length) => new(dictionary, bytes.Slice((int)offset, length));
    }
}
