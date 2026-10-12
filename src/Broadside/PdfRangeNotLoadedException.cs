using System.Globalization;

namespace Broadside;

/// <summary>
/// The exception thrown when a synchronous read of a remote document needs bytes that have not been fetched, and the document may
/// not fetch them synchronously.
/// </summary>
/// <remarks>
/// <para>
/// Spec #33, viewer capability 5 (range-request sources) and ADR 0010. Reading a document is synchronous; only opening it and
/// prefetching a page are asynchronous. A document opened with <see cref="PdfEngine.OpenAsync(System.Net.Http.HttpClient, Uri, CancellationToken)"/>
/// fetches the bytes a synchronous read misses by blocking on the request, except where
/// <see cref="PdfOptions.SynchronousRangeFetch"/> is off, which it always is on the browser platform: a browser cannot block on
/// HTTP. There, the read fails with this exception instead; await <see cref="PdfPage.PrefetchAsync"/> for the page first.
/// </para>
/// <para>
/// A read that fails this way leaves nothing cached: after the prefetch, the same read succeeds.
/// </para>
/// </remarks>
public sealed class PdfRangeNotLoadedException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="PdfRangeNotLoadedException"/> class.</summary>
    /// <param name="offset">The offset of the first byte the read needed.</param>
    /// <param name="length">The number of bytes the read needed.</param>
    public PdfRangeNotLoadedException(long offset, long length)
        : base(string.Create(
            CultureInfo.InvariantCulture,
            $"Bytes {offset} to {offset + length - 1} of the remote file have not been fetched, and this document may not fetch them synchronously (PdfOptions.SynchronousRangeFetch is off, as it always is on the browser, which cannot block on HTTP). Await {nameof(PdfPage)}.{nameof(PdfPage.PrefetchAsync)} for the page before reading it."))
    {
        Offset = offset;
        Length = length;
    }

    /// <summary>Initializes a new instance of the <see cref="PdfRangeNotLoadedException"/> class.</summary>
    public PdfRangeNotLoadedException()
        : this("A synchronous read needed bytes of the remote file that have not been fetched.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfRangeNotLoadedException"/> class.</summary>
    /// <param name="message">The message.</param>
    public PdfRangeNotLoadedException(string? message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfRangeNotLoadedException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public PdfRangeNotLoadedException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Gets the offset of the first byte the read needed.</summary>
    public long Offset { get; }

    /// <summary>Gets the number of bytes the read needed.</summary>
    public long Length { get; }
}
