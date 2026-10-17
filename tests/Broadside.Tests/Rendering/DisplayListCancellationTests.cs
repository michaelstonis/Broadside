using System.Diagnostics;
using System.Globalization;
using System.Text;
using Broadside.Content;
using Broadside.Rendering;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Rendering;

/// <summary>
/// Cancelling a display-list build (issue #97, viewer capability 2): it stops within 10 ms of the request even inside a long image
/// decode, returns what it rented, and leaves nothing behind that changes the next build. Timing and allocation tests run alone in
/// the <c>Heavy</c> collection.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class DisplayListCancellationTests
{
    /// <summary>The longest a build may run on after its token is cancelled (the ticket's acceptance criterion).</summary>
    private static readonly TimeSpan Latency = TimeSpan.FromMilliseconds(10);

    [Theory]
    [InlineData("pdfjs/issue12841_reduced.pdf")] // One 5280 x 3792 baseline JPEG: the build is almost all DCT decoding.
    [InlineData("pdfjs/issue5567.pdf")] // One 1500 x 1125 JPEG 2000 image: code-blocks, wavelet, composition.
    [InlineData("pdfjs/bug1978317.pdf")] // About 430,000 operators: the interpreter's own checks.
    public void Cancelling_the_build_of_a_large_real_world_page_stops_within_10_ms(string file)
    {
        byte[] bytes = RealWorldFile(file);
        RecordingBackend expected = Record(bytes);

        // Cancel at evenly spaced moments through one uncancelled build (cold: content decoded afresh each time, as nothing
        // cancelled is cached). Scheduling on a busy machine can delay the cancelled thread itself, so each moment is tried up to five
        // times and its best latency counts: a decode that ignored the token would be slow every time.
        using PdfDocument document = PdfDocument.Open(bytes, RenderCacheTests.NoCaches());
        PdfPage page = document.Pages[0];
        CancelAt(page, TimeSpan.Zero); // The first cancellation through each code path pays for the runtime's exception setup.
        TimeSpan full = Measure(() =>
        {
            using PdfDocument fresh = PdfDocument.Open(bytes, RenderCacheTests.NoCaches());
            fresh.Pages[0].CreateDisplayList();
        });
        var latencies = new List<(TimeSpan At, TimeSpan Latency)>();
        for (int point = 0; point < 8; point++)
        {
            TimeSpan at = full * point / 8;
            TimeSpan best = TimeSpan.MaxValue;
            for (int attempt = 0; attempt < 5 && best > Latency; attempt++)
            {
                if (CancelAt(page, at) is { } latency && latency < best)
                {
                    best = latency;
                }
            }

            if (best != TimeSpan.MaxValue)
            {
                latencies.Add((at, best));
            }
        }

        Assert.True(latencies.Count >= 4, $"Only {latencies.Count} of 8 builds were cancelled before they finished ({full.TotalMilliseconds:F0} ms).");
        Assert.All(latencies, entry => Assert.True(
            entry.Latency <= Latency,
            string.Create(CultureInfo.InvariantCulture, $"Cancelled {entry.At.TotalMilliseconds:F1} ms into a {full.TotalMilliseconds:F0} ms build, it ran on for {entry.Latency.TotalMilliseconds:F1} ms.")));

        // Every cancelled build left the document as it was: a complete build now hands over exactly what a fresh document's does.
        var recorder = new RecordingBackend();
        page.CreateDisplayList().Replay(recorder);
        Assert.Equal(expected.Operations, recorder.Operations);
    }

    [Theory]
    [InlineData("pdfjs/22060_A1_01_Plans.pdf")] // An architectural plan: paths, clips, forms, text.
    [InlineData("pdfjs/bug1743245.pdf")]
    public void A_build_cancelled_at_any_progress_report_leaves_nothing_that_changes_the_next_build(string file)
    {
        byte[] bytes = RealWorldFile(file);
        RecordingBackend expected = Record(bytes);
        using PdfDocument document = PdfDocument.Open(bytes, RenderCacheTests.NoCaches());
        PdfPage page = document.Pages[0];
        var reports = new RecordingProgress();
        page.CreateDisplayList(new ContentOptions().WithProgress(reports));
        Assert.True(reports.Values.Count > 2, $"Only {reports.Values.Count} progress reports.");

        // A report before the first operator's check is the run's start; cancelling at report k stops the build there.
        for (int k = 1; k < reports.Values.Count; k++)
        {
            using var cancellation = new CancellationTokenSource();
            int cancelAt = k;
            var progress = new RecordingProgress(onReport: (count, _) =>
            {
                if (count == cancelAt)
                {
                    cancellation.Cancel();
                }
            });
            Assert.Throws<OperationCanceledException>(() => page.CreateDisplayList(
                new ContentOptions().WithCancellation(cancellation.Token).WithProgress(progress)));

            var recorder = new RecordingBackend();
            page.CreateDisplayList().Replay(recorder);
            Assert.Equal(expected.Operations, recorder.Operations);
        }

        // A token cancelled before the build starts stops it before anything runs.
        Assert.Throws<OperationCanceledException>(() => page.CreateDisplayList(new ContentOptions().WithCancellation(new CancellationToken(canceled: true))));
    }

    [Fact]
    public void A_build_cancelled_after_decoding_an_image_returns_the_images_pooled_buffers()
    {
        // A 2048 x 1024 gray image (2 MiB of samples, rented from the shared pool) painted first, then enough operators for the
        // first progress report, where the build is cancelled. Were the decoded image not returned, every build would rent a fresh
        // 2 MiB array; returned, the next build rents the same one again.
        const int width = 2048;
        const int height = 1024;
        var samples = new StringBuilder(width * height);
        for (int i = 0; i < width * height; i++)
        {
            samples.Append((char)(i % 251));
        }

        byte[] content = [.. Encoding.ASCII.GetBytes("q 200 0 0 100 0 0 cm /Im1 Do Q\n"), .. DisplayListProgressTests.Padding(4096)];
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(
            content,
            "/XObject << /Im1 5 0 R >>",
            ContentSamples.Stream(samples.ToString(), "/Type /XObject /Subtype /Image /Width 2048 /Height 1024 /ColorSpace /DeviceGray /BitsPerComponent 8")),
            RenderCacheTests.NoCaches()); // Every build decodes (issue #103's caches would hand back the image or the list).
        PdfPage page = document.Pages[0];
        Assert.Equal(4, page.CreateDisplayList().OperationCount); // save, transform, image, restore: the padding records nothing
        Action cancelledBuild = () =>
        {
            using var cancellation = new CancellationTokenSource();
            var options = new ContentOptions().WithCancellation(cancellation.Token).WithProgress(new CancellingProgress(cancellation));
            try
            {
                page.CreateDisplayList(options);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            Assert.Fail("The build was not cancelled.");
        };

        long allocated = Allocations.Measure(cancelledBuild, warmUpCalls: 5);

        Assert.InRange(allocated, 0, 256 * 1024);
    }

    /// <summary>Starts a build on its own thread, cancels it after <paramref name="after"/>, and returns how long it ran on.</summary>
    /// <returns>The time from the cancel to the build's exception; <see langword="null"/> when the build finished first.</returns>
    private static TimeSpan? CancelAt(PdfPage page, TimeSpan after)
    {
        using var cancellation = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        long cancelledAt = 0;
        TimeSpan? latency = null;
        var thread = new Thread(() =>
        {
            started.Set();
            try
            {
                page.CreateDisplayList(new ContentOptions().WithCancellation(cancellation.Token));
            }
            catch (OperationCanceledException)
            {
                latency = Stopwatch.GetElapsedTime(Interlocked.Read(ref cancelledAt));
            }
        });
        thread.Start();
        started.Wait();
        long start = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(start) < after)
        {
            Thread.SpinWait(64);
        }

        Interlocked.Exchange(ref cancelledAt, Stopwatch.GetTimestamp());
        cancellation.Cancel();
        thread.Join();
        return latency;
    }

    private static TimeSpan Measure(Action action)
    {
        long start = Stopwatch.GetTimestamp();
        action();
        return Stopwatch.GetElapsedTime(start);
    }

    private static RecordingBackend Record(byte[] bytes)
    {
        using PdfDocument document = PdfDocument.Open(bytes, RenderCacheTests.NoCaches());
        var recorder = new RecordingBackend();
        document.Pages[0].CreateDisplayList().Replay(recorder);
        return recorder;
    }

    private static byte[] RealWorldFile(string file)
    {
        if (!RealWorldCorpus.Files("pdfjs").Contains(file))
        {
            Assert.Skip($"{file} not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        return File.ReadAllBytes(RealWorldCorpus.PathOf(file));
    }

    /// <summary>Cancels its source at the first report.</summary>
    private sealed class CancellingProgress(CancellationTokenSource source) : IProgress<double>
    {
        public void Report(double value) => source.Cancel();
    }
}
