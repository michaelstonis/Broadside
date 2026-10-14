using System.Text;
using Broadside.Content;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Tests.Rendering;

/// <summary>
/// Progress, cancellation and time-sliced replay of display lists (issue #97, viewer capability 2): building reports how much of the
/// page's content has run and stops when its token is cancelled; replay reports how many operations it has handed over, can stop
/// after a time budget and resume, and stops when its token is cancelled.
/// </summary>
public sealed class DisplayListProgressTests
{
    [Fact]
    public void Building_reports_progress_that_never_decreases_and_ends_at_completion()
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(ContentSamples.PathHeavy(256 * 1024)));
        var progress = new RecordingProgress();

        document.Pages[0].CreateDisplayList(new ContentOptions().WithProgress(progress));

        Assert.True(progress.Values.Count > 10, $"Only {progress.Values.Count} reports.");
        AssertNonDecreasing(progress.Values);
        Assert.All(progress.Values, value => Assert.InRange(value, 0, 1));
        Assert.Equal(1, progress.Values[^1]);
        Assert.Equal(1, progress.Values.Count(value => value == 1));
    }

    [Fact]
    public void Building_an_empty_page_reports_completion_once()
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage([]));
        var progress = new RecordingProgress();

        document.Pages[0].CreateDisplayList(new ContentOptions().WithProgress(progress));

        Assert.Equal([1.0], progress.Values);
    }

    [Fact]
    public void A_build_cancelled_from_its_progress_callback_stops_at_once_and_reports_no_completion()
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(ContentSamples.PathHeavy(256 * 1024)));
        using var cancellation = new CancellationTokenSource();
        var progress = new RecordingProgress(onReport: (count, _) =>
        {
            if (count == 3)
            {
                cancellation.Cancel();
            }
        });

        Assert.Throws<OperationCanceledException>(() => document.Pages[0].CreateDisplayList(
            new ContentOptions().WithCancellation(cancellation.Token).WithProgress(progress)));

        Assert.Equal(3, progress.Values.Count);
        Assert.True(progress.Values[^1] < 1);
    }

    [Fact]
    public void Replaying_reports_progress_that_never_decreases_and_ends_at_completion()
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(ContentSamples.PathHeavy(256 * 1024)));
        DisplayList list = document.Pages[0].CreateDisplayList();
        var progress = new RecordingProgress();
        var expected = new RecordingBackend();
        list.Replay(expected);
        var recorder = new RecordingBackend();

        list.Replay(recorder, CancellationToken.None, progress);

        Assert.Equal(expected.Operations, recorder.Operations);
        Assert.True(progress.Values.Count > 2, $"Only {progress.Values.Count} reports for {list.OperationCount} operations.");
        AssertNonDecreasing(progress.Values);
        Assert.Equal(1, progress.Values[^1]);
        Assert.Equal(1, progress.Values.Count(value => value == 1));
    }

    [Fact]
    public void A_replay_sliced_at_every_operation_hands_the_backend_the_same_calls_as_one_replay()
    {
        // A zero budget still replays one operation per slice, so every slice boundary of the list is exercised.
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("clipping-paths.pdf"));
        DisplayList list = document.Pages[0].CreateDisplayList();
        var expected = new RecordingBackend();
        list.Replay(expected);
        var recorder = new RecordingBackend();
        DisplayListReplay replay = list.BeginReplay(recorder, CancellationToken.None, progress: null);

        int slices = 0;
        int position = 0;
        while (!replay.Continue(TimeSpan.Zero))
        {
            slices++;
            Assert.Equal(position + 1, replay.Position);
            position = replay.Position;
            Assert.Equal((double)position / list.OperationCount, replay.Progress);
        }

        Assert.Equal(list.OperationCount - 1, slices);
        Assert.True(replay.IsCompleted);
        Assert.Equal(1, replay.Progress);
        Assert.Equal(expected.Operations, recorder.Operations);
        Assert.True(replay.Continue(TimeSpan.Zero));
        Assert.Equal(expected.Operations, recorder.Operations);
    }

    [Fact]
    public void A_region_replay_sliced_at_every_operation_hands_the_backend_the_same_calls_as_one_region_replay()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("clipping-paths.pdf"));
        DisplayList list = document.Pages[0].CreateDisplayList();
        var region = new PdfRectangle(0, 0, 150, 120);
        var expected = new RecordingBackend();
        list.Replay(expected, region);
        var recorder = new RecordingBackend();
        var progress = new RecordingProgress();
        DisplayListReplay replay = list.BeginReplay(recorder, region, CancellationToken.None, progress);

        while (!replay.Continue(TimeSpan.Zero))
        {
        }

        Assert.Equal(expected.Operations, recorder.Operations);
        AssertNonDecreasing(progress.Values);
        Assert.Equal(1, progress.Values[^1]);
    }

    [Fact]
    public void A_time_budget_stops_a_replay_that_the_next_slice_resumes_where_it_stopped()
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(ContentSamples.PathHeavy(256 * 1024)));
        DisplayList list = document.Pages[0].CreateDisplayList();
        var expected = new RecordingBackend();
        list.Replay(expected);
        var recorder = new RecordingBackend();
        var progress = new RecordingProgress();
        DisplayListReplay replay = list.BeginReplay(recorder, CancellationToken.None, progress);

        int slices = 1;
        while (!replay.Continue(TimeSpan.FromMilliseconds(1)))
        {
            Assert.Equal(replay.Progress, progress.Values[^1]);
            slices++;
        }

        Assert.True(slices > 1, "One millisecond replayed the whole list.");
        Assert.Equal(expected.Operations, recorder.Operations);
        AssertNonDecreasing(progress.Values);
        Assert.Equal(1, progress.Values[^1]);
        Assert.Equal(1, progress.Values.Count(value => value == 1));
    }

    [Fact]
    public void A_cancelled_replay_stops_before_its_next_operation()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("clipping-paths.pdf"));
        DisplayList list = document.Pages[0].CreateDisplayList();
        using var cancellation = new CancellationTokenSource();
        var recorder = new RecordingBackend();
        DisplayListReplay replay = list.BeginReplay(recorder, cancellation.Token, progress: null);
        Assert.False(replay.Continue(TimeSpan.Zero));
        Assert.False(replay.Continue(TimeSpan.Zero));

        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => replay.Continue(TimeSpan.Zero));
        Assert.Throws<OperationCanceledException>(() => list.Replay(new RecordingBackend(), cancellation.Token, progress: null));
        Assert.Equal(2, replay.Position);
        Assert.Equal(2, recorder.Operations.Count);
    }

    [Fact]
    public void An_empty_list_completes_in_one_slice_and_reports_completion()
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage([]));
        DisplayList list = document.Pages[0].CreateDisplayList();
        var progress = new RecordingProgress();

        DisplayListReplay replay = list.BeginReplay(new RecordingBackend(), CancellationToken.None, progress);

        Assert.Equal(0, list.OperationCount);
        Assert.True(replay.Continue(TimeSpan.Zero));
        Assert.Equal([1.0], progress.Values);
        Assert.Throws<ArgumentOutOfRangeException>(() => replay.Continue(TimeSpan.FromMilliseconds(-2)));
    }

    internal static void AssertNonDecreasing(IReadOnlyList<double> values)
    {
        for (int i = 1; i < values.Count; i++)
        {
            Assert.True(values[i] >= values[i - 1], $"Progress went from {values[i - 1]} back to {values[i]} at report {i}.");
        }
    }

    internal static byte[] Padding(int operators)
    {
        var content = new StringBuilder();
        for (int i = 0; i < operators; i++)
        {
            content.Append("q Q\n");
        }

        return Encoding.ASCII.GetBytes(content.ToString());
    }
}

/// <summary>An <see cref="IProgress{T}"/> that records every report synchronously, on the reporting thread.</summary>
/// <param name="onReport">Called after each report with the number of reports so far and the value.</param>
internal sealed class RecordingProgress(Action<int, double>? onReport = null) : IProgress<double>
{
    public List<double> Values { get; } = [];

    public void Report(double value)
    {
        Values.Add(value);
        onReport?.Invoke(Values.Count, value);
    }
}
