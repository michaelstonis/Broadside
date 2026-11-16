using System.Diagnostics;
using System.Globalization;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Issue #362 with viewer capability 2: an image a display list keeps encoded is decoded when it is drawn, with the render's token,
/// so a decode cancelled part-way stops within 10 ms (the DCT decoder checks the token every MCU row), keeps nothing, and the next
/// request decodes the image again. The rasterizer hands its render's token to the decode.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class DeferredDecodeCancellationTests
{
    private static readonly TimeSpan Latency = TimeSpan.FromMilliseconds(10);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Cancelling_a_deferred_decode_stops_it_within_10_ms_and_keeps_nothing(int reduction)
    {
        // The 2550 x 3150 catalogue photograph: a decode takes tens of milliseconds.
        using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover(), new PdfOptions().WithImageCacheBudget(0).WithDisplayListCacheBudget(0));
        PdfPage page = document.Pages[0];
        TimeSpan full = DecodeTime(page, reduction);
        var latencies = new List<(TimeSpan At, TimeSpan Latency)>();
        for (int point = 1; point < 8; point++)
        {
            TimeSpan at = full * point / 8;
            TimeSpan best = TimeSpan.MaxValue;
            for (int attempt = 0; attempt < 5 && best > Latency; attempt++)
            {
                DisplayImage image = Image(page);
                (TimeSpan? latency, TimeSpan ran) = CancelAt(() => image.GetSamplesAt(reduction, Token.Value), at);
                if (latency is { } cancelled)
                {
                    if (cancelled < best)
                    {
                        best = cancelled;
                    }
                }
                else if (ran < full)
                {
                    // The decode finished before the cancel point: it runs faster now than when it was measured (the JIT keeps
                    // optimizing it), so place this and the later points within the faster decode instead of racing its completion.
                    full = ran;
                    at = full * point / 8;
                }

                // A cancelled decode is not a failure: the image decodes again on the next request.
                Assert.NotNull(image.GetSamplesAt(reduction, TestContext.Current.CancellationToken));
            }

            if (best != TimeSpan.MaxValue)
            {
                latencies.Add((at, best));
            }
        }

        Assert.True(latencies.Count >= 4, $"Only {latencies.Count} of 7 decodes were cancelled before they finished ({full.TotalMilliseconds:F0} ms).");
        Assert.All(latencies, entry => Assert.True(
            entry.Latency <= Latency,
            string.Create(CultureInfo.InvariantCulture, $"Cancelled {entry.At.TotalMilliseconds:F1} ms into a {full.TotalMilliseconds:F0} ms decode, it ran on for {entry.Latency.TotalMilliseconds:F1} ms.")));
    }

    private static readonly AsyncLocal<CancellationToken> Token = new();

    private static DisplayImage Image(PdfPage page)
    {
        var recorder = new RecordingBackend();
        page.CreateDisplayList().Replay(recorder);
        return Assert.Single(recorder.Images).Image;
    }

    /// <summary>Runs <paramref name="work"/> on its own thread and cancels it <paramref name="after"/> it starts; returns how long it ran on after the cancellation, or null when it finished first, and how long it ran.</summary>
    private static (TimeSpan? Latency, TimeSpan Ran) CancelAt(Action work, TimeSpan after)
    {
        using var cancellation = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        long cancelledAt = 0;
        TimeSpan? latency = null;
        TimeSpan ran = TimeSpan.Zero;
        var thread = new Thread(() =>
        {
            Token.Value = cancellation.Token;
            started.Set();
            long begin = Stopwatch.GetTimestamp();
            try
            {
                work();
            }
            catch (OperationCanceledException)
            {
                latency = Stopwatch.GetElapsedTime(Interlocked.Read(ref cancelledAt));
            }

            ran = Stopwatch.GetElapsedTime(begin);
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
        return (latency, ran);
    }

    /// <summary>
    /// The time the decode alone takes: the display list is built before the clock starts (building it takes longer than a reduced
    /// decode, so counting it put most cancel points after the decode had finished), and the fastest of three runs is kept so a
    /// first, unoptimized run does not stretch the cancel points past the end of the later decodes either.
    /// </summary>
    private static TimeSpan DecodeTime(PdfPage page, int reduction)
    {
        TimeSpan fastest = TimeSpan.MaxValue;
        for (int run = 0; run < 3; run++)
        {
            DisplayImage image = Image(page);
            long start = Stopwatch.GetTimestamp();
            Assert.NotNull(image.GetSamplesAt(reduction, TestContext.Current.CancellationToken));
            TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
            if (elapsed < fastest)
            {
                fastest = elapsed;
            }
        }

        return fastest;
    }
}
