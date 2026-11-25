using System.Net;
using System.Net.Http.Headers;
using Broadside.Diagnostics;
using Broadside.Security;
using Broadside.TestSupport;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>range-response</c> target (issue #274): a remote file opened through HTTP range requests, against a server whose
/// answers the input controls. The first four bytes are control bytes and the rest is the file; a whole PDF file (the smoke
/// seeds) is served as it is by an honest server that answers the first request with at most <c>64 x (1 + length % 256)</c>
/// bytes, so the later range requests run on small files too. Control byte 0 caps the first answer the same way, byte 1 picks
/// the range block size (512 to 8 KiB), and bytes 2 and 3 plan the faults: request <c>n</c> is answered wrongly when bit
/// <c>n % 8</c> of byte 2 is set, in the way <c>(byte 3 + n) % 8</c> picks (the whole file with <c>200</c>; a shifted or
/// mislabeled <c>Content-Range</c>; a short body; <c>416</c>; <c>500</c>; no <c>Content-Range</c>; corrupted bytes). The document
/// is opened with <see cref="PdfDocument.OpenAsync(HttpClient, Uri, PdfOptions, CancellationToken)"/> and walked with
/// <see cref="DocumentWalker"/>, which fetches what each read misses. A <see cref="DiagnosticException"/>, an
/// <see cref="IOException"/>, an <see cref="HttpRequestException"/> and the password and encryption exceptions are the documented
/// outcomes; anything else is a finding. With an honest server nothing may fail after the open, and a document that is not linearized
/// must open remotely exactly when it opens from memory, with the same pages.
/// </summary>
/// <remarks>Spec #33, viewer capability 5; RFC 9110 §14 (range requests), §15.3.7 (206), §15.5.17 (416); ISO 32000-2 §7.5, Annex F.</remarks>
internal static class RangeResponseTarget
{
    private const int ControlLength = 4;

    private static readonly Uri FileUri = new("https://fuzz.test/file.pdf");

    public static void Target(ReadOnlySpan<byte> data)
    {
        byte[] control;
        byte[] file;
        if (data.StartsWith("%PDF-"u8))
        {
            control = [(byte)data.Length, 0, 0, 0];
            file = data.ToArray();
        }
        else if (data.Length > ControlLength)
        {
            control = data[..ControlLength].ToArray();
            file = data[ControlLength..].ToArray();
        }
        else
        {
            return;
        }

        bool honest = control[2] == 0;
        var options = new PdfOptions().WithRangeBlockSize(512 * (1 + (control[1] % 16)));
        using var server = new FaultyServer(file, firstAnswer: 64 * (1 + control[0]), faults: control[2], kinds: control[3]);
        using var client = new HttpClient(server, disposeHandler: false);
        PdfDocument document;
        try
        {
            // The open completes here, before the client and server are disposed; the server answers synchronously.
#pragma warning disable CA2025
            document = PdfDocument.OpenAsync(client, FileUri, options).GetAwaiter().GetResult();
#pragma warning restore CA2025
        }
        catch (Exception exception) when (exception is DiagnosticException or PdfPasswordException or PdfEncryptionNotSupportedException or PdfCertificateException
            || (!honest && exception is IOException or HttpRequestException))
        {
            if (honest && exception is DiagnosticException)
            {
                using PdfDocument? local = FuzzTargets.OpenOrNull(file);
                if (local is { IsLinearized: false })
                {
                    throw new InvalidOperationException("An honest server's file failed to open remotely but opens from memory.", exception);
                }
            }

            return;
        }

        using (document)
        {
            if (honest)
            {
                using PdfDocument? local = FuzzTargets.OpenOrNull(file);
                // A linearized file is read remotely through its first-page section and hint tables, which may disagree with a
                // damaged page tree read whole; only a file without them must read the same both ways.
                if (!(local?.IsLinearized ?? document.IsLinearized) && local?.Pages.Count != document.Pages.Count)
                {
                    throw new InvalidOperationException($"An honest server's file has {document.Pages.Count} pages remotely and {local?.Pages.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "no document"} from memory.");
                }

                _ = DocumentWalker.Walk(document);
                return;
            }

            try
            {
                _ = DocumentWalker.Walk(document);
            }
            catch (Exception exception) when (exception is IOException or HttpRequestException)
            {
                // A server that changes its answers fails the read that needed them; that is the documented outcome.
            }
        }
    }

    /// <summary>A server in the process: serves one file with single byte ranges and answers the planned requests wrongly.</summary>
    private sealed class FaultyServer(byte[] file, int firstAnswer, byte faults, byte kinds) : HttpMessageHandler
    {
        private int _requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int number = Interlocked.Increment(ref _requests) - 1;
            long length = file.LongLength;
            RangeItemHeaderValue? range = request.Headers.Range is { Unit: "bytes", Ranges.Count: 1 } header ? header.Ranges.First() : null;
            if (range is null || length == 0)
            {
                return Task.FromResult(Whole());
            }

            long from = range.From ?? Math.Max(0, length - range.To!.Value);
            long to = Math.Min(range.From is null ? length - 1 : range.To ?? length - 1, length - 1);
            if (number == 0)
            {
                to = Math.Min(to, from + firstAnswer - 1);
            }

            if (from >= length || to < from)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable));
            }

            byte[] body = file.AsSpan((int)from, (int)(to - from + 1)).ToArray();
            long labelFrom = from;
            long labelLength = length;
            bool label = true;
            if ((faults >> (number % 8) & 1) != 0)
            {
                switch ((kinds + number) % 8)
                {
                    case 0:
                        return Task.FromResult(Whole());
                    case 1:
                        labelFrom = from + 1;
                        break;
                    case 2:
                        labelLength = length + 1 + kinds;
                        break;
                    case 3:
                        body = body[..^1];
                        break;
                    case 4:
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable));
                    case 5:
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
                    case 6:
                        label = false;
                        break;
                    default:
                        for (int index = 0; index < body.Length; index += 1 + kinds)
                        {
                            body[index] ^= 0x5A;
                        }

                        break;
                }
            }

            var content = new ByteArrayContent(body);
            if (label)
            {
                content.Headers.ContentRange = new ContentRangeHeaderValue(labelFrom, labelFrom + (to - from), labelLength);
            }

            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };
            response.Headers.AcceptRanges.Add("bytes");
            return Task.FromResult(response);
        }

        private HttpResponseMessage Whole() => new(HttpStatusCode.OK) { Content = new ByteArrayContent(file) };
    }
}
