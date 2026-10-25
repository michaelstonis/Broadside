using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using Broadside.Graphics;
using Broadside.Rendering;

namespace Broadside.Viewer.Blazor.Sample;

/// <summary>
/// The browser image decoder checked against the managed decoder in the browser itself (#373, for the browser tests): a page's images
/// are prefetched through <see cref="BrowserImageDecoder"/> with the verification mode on, which records how far the browser's samples
/// are from the managed decoder's.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class ImageDecodeChecks
{
    /// <summary>
    /// Prefetches page 1 of the document at <paramref name="address"/> drawn at <paramref name="scale"/> and returns, one per line, the
    /// decoder this browser uses, the milliseconds the prefetch took, then <c>code|message</c> for each diagnostic.
    /// </summary>
    /// <param name="address">The document.</param>
    /// <param name="scale">The scale the page is drawn at (1 is 72 dpi).</param>
    /// <param name="samplesPerPixel">The backend's samples per device pixel, or 0 for images at their own resolution.</param>
    /// <param name="bitmapOnPage">Whether to use <c>createImageBitmap</c> on the page instead of the defaults (WebKit's tolerance check).</param>
    /// <returns>The report.</returns>
    [JSExport]
    public static async Task<string> VerifyAsync(string address, double scale, double samplesPerPixel, bool bitmapOnPage)
    {
        using var http = new HttpClient();
        byte[] bytes = await http.GetByteArrayAsync(new Uri(address)).ConfigureAwait(true);
        await BrowserImageDecoder.InitializeAsync().ConfigureAwait(true);
        BrowserImageDecoder decoder = bitmapOnPage
            ? new BrowserImageDecoder(new BrowserImageDecoderOptions().WithWebCodecs(false).WithImageBitmapOnPage(true))
            : BrowserImageDecoder.Shared;
        using PdfDocument document = new PdfEngine(new PdfOptions().UseStandard14Fonts().UsePlatformImageDecoder(decoder).WithPlatformImageVerification(true)).Open(bytes);
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        Matrix device = new RenderOptions().WithScale(scale).GetDeviceTransform(page);
        long start = Stopwatch.GetTimestamp();
        await list.PrefetchImagesAsync(device, null, samplesPerPixel, CancellationToken.None).ConfigureAwait(true);
        double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var report = new StringBuilder();
        report.Append(decoder.Description).Append('\n');
        report.Append(milliseconds.ToString("0.0", CultureInfo.InvariantCulture)).Append('\n');
        foreach (Diagnostics.Diagnostic diagnostic in document.Diagnostics)
        {
            report.Append(diagnostic.Code).Append('|').Append(diagnostic.Message.Replace('\n', ' ')).Append('\n');
        }

        return report.ToString();
    }
}
