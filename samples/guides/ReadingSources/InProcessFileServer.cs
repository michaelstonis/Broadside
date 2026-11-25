using System.Net;
using System.Net.Http.Headers;

/// <summary>
/// A web server inside the sample's process: an <see cref="HttpMessageHandler"/> that serves one file's bytes, answering a request
/// for a single byte range with <c>206 Partial Content</c>, as a web server or a storage service does. An <see cref="HttpClient"/> over
/// it never reaches the network. It counts the bytes it sends.
/// </summary>
internal sealed class InProcessFileServer(byte[] file) : HttpMessageHandler
{
    /// <summary>Gets the number of bytes sent so far.</summary>
    public long BytesServed { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(Send(request, cancellationToken));

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Range is not { Unit: "bytes", Ranges.Count: 1 } range)
        {
            BytesServed += file.Length;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(file) };
        }

        RangeItemHeaderValue item = range.Ranges.Single();
        long start = item.From ?? Math.Max(0, file.Length - item.To!.Value);
        long end = item.From is null ? file.Length : Math.Min(file.Length, (item.To ?? file.Length - 1) + 1);
        if (start >= end)
        {
            return new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable);
        }

        var content = new ByteArrayContent(file, (int)start, (int)(end - start));
        content.Headers.ContentRange = new ContentRangeHeaderValue(start, end - 1, file.Length);
        BytesServed += end - start;
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };
        response.Headers.AcceptRanges.Add("bytes");
        return response;
    }
}
