using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace Broadside.Tests.Document;

/// <summary>One request a <see cref="RangeServer"/> answered.</summary>
/// <param name="Range">The <c>Range</c> header as sent, or <see langword="null"/> for a request without one.</param>
/// <param name="Status">The status answered.</param>
/// <param name="Start">The offset of the first byte answered.</param>
/// <param name="Length">The number of bytes answered.</param>
internal sealed record ServedRequest(string? Range, HttpStatusCode Status, long Start, long Length);

/// <summary>
/// An HTTP server in the test process: a message handler serving one file's bytes, with single byte ranges (RFC 9110 §14) when it
/// supports them, recording every request. No socket is opened, so tests never touch the network.
/// </summary>
/// <param name="file">The file served.</param>
/// <param name="supportsRanges">
/// Whether ranges are served (<c>206</c> with <c>Content-Range</c>, <c>Accept-Ranges: bytes</c>); otherwise every request is answered
/// with the whole file and <c>200</c>, as a server without range support does.
/// </param>
internal sealed class RangeServer(byte[] file, bool supportsRanges = true) : HttpMessageHandler
{
    private volatile bool _supportsRanges = supportsRanges;

    private readonly Lock _gate = new();
    private readonly List<ServedRequest> _requests = [];
    private readonly bool[] _served = new bool[file.Length];

    /// <summary>Gets the requests answered so far, in order.</summary>
    public IReadOnlyList<ServedRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>Gets the number of distinct bytes of the file sent so far.</summary>
    public long DistinctBytesServed
    {
        get
        {
            lock (_gate)
            {
                return _served.Count(static served => served);
            }
        }
    }

    /// <summary>Gets the total number of bytes sent so far, counting repeats.</summary>
    public long BytesServed
    {
        get
        {
            lock (_gate)
            {
                return _requests.Sum(static request => request.Length);
            }
        }
    }

    /// <summary>Returns whether any byte from <paramref name="start"/> up to <paramref name="end"/> has been sent.</summary>
    public bool AnyServed(long start, long end)
    {
        lock (_gate)
        {
            for (long index = start; index < end; index++)
            {
                if (_served[index])
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>From now on, answers every request with the whole file, as a server reconfigured mid-session would.</summary>
    public void StopServingRanges() => _supportsRanges = false;

    /// <summary>Creates a client over this server; the caller disposes it, which leaves the server alive.</summary>
    public HttpClient CreateClient() => new(this, disposeHandler: false);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? range = request.Headers.Range?.ToString();
        HttpResponseMessage response;
        if (_supportsRanges && request.Headers.Range is { Unit: "bytes", Ranges.Count: 1 } header)
        {
            RangeItemHeaderValue item = header.Ranges.Single();
            long start;
            long end;
            if (item.From is null)
            {
                start = Math.Max(0, file.Length - item.To!.Value);
                end = file.Length;
            }
            else
            {
                start = item.From.Value;
                end = item.To is { } to ? Math.Min(file.Length, to + 1) : file.Length;
            }

            if (start >= file.Length || start >= end)
            {
                response = new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable);
                response.Content = new ByteArrayContent([]);
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(file.Length);
                Record(range, response.StatusCode, 0, 0);
                return Task.FromResult(response);
            }

            response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(file, (int)start, (int)(end - start)),
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, end - 1, file.Length);
            response.Headers.AcceptRanges.Add("bytes");
            Record(range, response.StatusCode, start, end - start);
            return Task.FromResult(response);
        }

        response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(file) };
        Record(range, response.StatusCode, 0, file.Length);
        return Task.FromResult(response);
    }

    private void Record(string? range, HttpStatusCode status, long start, long length)
    {
        lock (_gate)
        {
            _requests.Add(new ServedRequest(range, status, start, length));
            Array.Fill(_served, true, (int)start, (int)length);
        }
    }

    /// <summary>Formats the request log for an assertion message.</summary>
    public override string ToString() => string.Join(
        "; ",
        Requests.Select(static request => string.Create(CultureInfo.InvariantCulture, $"{request.Range ?? "(none)"} -> {(int)request.Status} {request.Start}+{request.Length}")));
}
