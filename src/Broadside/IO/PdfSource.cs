namespace Broadside.IO;

/// <summary>The bytes of one PDF file, read by position.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.5.1: a file is read at random, through its cross-reference table, never serially. A source therefore has no
/// cursor: every read names its offset, so concurrent readers never interfere. Offsets are <see langword="long"/> everywhere.
/// </para>
/// <para>
/// Issue #37 ships the in-memory source only; paths and streams are copied into memory. Issue #45 adds file, memory-mapped and
/// windowed stream sources behind this same contract.
/// </para>
/// </remarks>
internal abstract class PdfSource : IDisposable
{
    private bool _disposed;

    /// <summary>Gets the length of the file in bytes.</summary>
    public abstract long Length { get; }

    /// <summary>Creates a source over bytes the caller promises not to change while the source is in use. The bytes are not copied.</summary>
    /// <param name="bytes">The file.</param>
    /// <returns>The source.</returns>
    public static PdfSource FromMemory(ReadOnlyMemory<byte> bytes) => new MemorySource(bytes);

    /// <summary>Creates a source over the file at <paramref name="path"/>.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The source.</returns>
    public static PdfSource FromFile(string path) => new MemorySource(File.ReadAllBytes(path));

    /// <summary>Creates a source over the bytes of <paramref name="stream"/> from its current position to its end.</summary>
    /// <param name="stream">The stream; read once, not disposed.</param>
    /// <returns>The source.</returns>
    public static PdfSource FromStream(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return new MemorySource(copy.ToArray());
    }

    /// <summary>Copies the bytes at <paramref name="offset"/> into <paramref name="destination"/>.</summary>
    /// <param name="offset">The offset of the first byte to copy.</param>
    /// <param name="destination">Where to copy to.</param>
    /// <returns>The number of bytes copied: fewer than the destination holds only at the end of the file.</returns>
    public abstract int Read(long offset, Span<byte> destination);

    /// <summary>
    /// Returns a contiguous view of the bytes from <paramref name="offset"/> on, for the span-based parsers. The view ends at the end
    /// of the file or, for a source that cannot map the whole file, at the end of the source's window; parsers treat its end as the
    /// end of input.
    /// </summary>
    /// <param name="offset">The offset of the first byte of the view.</param>
    /// <returns>The view, empty when <paramref name="offset"/> is at or past the end of the file.</returns>
    public abstract ReadOnlyMemory<byte> GetWindow(long offset);

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

        public override ReadOnlyMemory<byte> GetWindow(long offset)
        {
            ThrowIfDisposed();
            return offset < 0 || offset >= bytes.Length ? ReadOnlyMemory<byte>.Empty : bytes[(int)offset..];
        }
    }
}
