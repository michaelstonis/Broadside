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
    public void Cancelling_a_deferred_decode_stops_it_within_10_ms_and_keeps_nothing(int reduction)
    {
        // The 2550 x 3150 catalogue photograph: a decode takes tens of milliseconds.
        using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover(), new PdfOptions().WithImageCacheBudget(0).WithDisplayListCacheBudget(0));
        PdfPage page = document.Pages[0];
        TimeSpan full = Measure(() => Image(page).GetSamplesAt(reduction, TestContext.Current.CancellationToken));
        var latencies = new List<(TimeSpan At, TimeSpan Latency)>();
        for (int point = 1; point < 8; point++)
        {
            TimeSpan at = full * point / 8;
            TimeSpan best = TimeSpan.MaxValue;
            for (int attempt = 0; attempt < 5 && best > Latency; attempt++)
            {
                DisplayImage image = Image(page);
                if (CancelAt(() => image.GetSamplesAt(reduction, Token.Value), at) is { } latency && latency < best)
                {
                    best = latency;
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

    private static TimeSpan? CancelAt(Action work, TimeSpan after)
    {
        using var cancellation = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        long cancelledAt = 0;
        TimeSpan? latency = null;
        var thread = new Thread(() =>
        {
            Token.Value = cancellation.Token;
            started.Set();
            try
            {
                work();
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
}
