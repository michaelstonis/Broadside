using System.Collections.Concurrent;
using Broadside.Objects;
using Broadside.Tests.Hosting;
using Broadside.TestSupport;
using Microsoft.Extensions.Logging;

namespace Broadside.Tests.Document;

/// <summary>
/// Objects load on first use through the cross-reference table and are parsed once, whatever the number of threads reading them.
/// ISO 32000-2 §7.5.1 and §7.5.4: the cross-reference table "permits random access to indirect objects ... so that the entire PDF
/// file need not be read". The parse count is observed through the engine's trace log (event 2, <c>ObjectParsed</c>).
/// </summary>
[Collection(HeavyTestCollection.Name)]
public class LazyLoadingTests
{
    private const int PageCount = 40;

    [Fact]
    public void Opening_a_file_parses_the_catalog_and_nothing_else()
    {
        using var logs = new CapturingLoggerProvider();
        using ILoggerFactory loggerFactory = TraceLogging(logs);

        using PdfDocument document = PdfDocument.Open(ManyPages(), new PdfOptions().WithLoggerFactory(loggerFactory));

        Assert.Equal([(1, 0)], ParsedObjects(logs));
    }

    [Fact]
    public void Reading_the_pages_parses_the_page_tree_but_not_the_page_contents()
    {
        using var logs = new CapturingLoggerProvider();
        using ILoggerFactory loggerFactory = TraceLogging(logs);
        using PdfDocument document = PdfDocument.Open(ManyPages(), new PdfOptions().WithLoggerFactory(loggerFactory));

        Assert.Equal(PageCount, document.Pages.Count);

        // Catalog, page tree root, one page object per page: no content stream, no Length object.
        int[] expected = [1, 2, .. Enumerable.Range(0, PageCount).Select(page => 3 + (3 * page))];
        Assert.Equal(expected.Order(), ParsedObjects(logs).Select(parsed => parsed.Number).Order());
    }

    [Fact]
    public void Every_object_is_parsed_once_however_many_threads_read_it()
    {
        using var logs = new CapturingLoggerProvider();
        using ILoggerFactory loggerFactory = TraceLogging(logs);
        using PdfDocument document = PdfDocument.Open(ManyPages(), new PdfOptions().WithLoggerFactory(loggerFactory));
        int objectCount = 2 + (3 * PageCount);

        var seen = new ConcurrentDictionary<int, CosObject>();
        Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, iteration =>
        {
            foreach (int number in Shuffled(1, objectCount, iteration))
            {
                CosObject value = document.Resolve(new CosReference(number, 0));
                Assert.Same(seen.GetOrAdd(number, value), value);
                if (value is CosStream stream)
                {
                    Assert.Equal("q Q"u8.ToArray(), document.DecodeStream(stream).ToArray());
                }
            }
        });

        (int Number, int Generation)[] parsed = ParsedObjects(logs);
        Assert.Equal(Enumerable.Range(1, objectCount), parsed.Select(entry => entry.Number).Order());
        Assert.All(parsed, entry => Assert.Equal(0, entry.Generation));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Resolving_an_object_already_loaded_allocates_nothing()
    {
        using PdfDocument document = PdfDocument.Open(ManyPages());
        CosReference[] references = [.. Enumerable.Range(1, 2 + (3 * PageCount)).Select(number => new CosReference(number, 0))];

        Assert.Equal(0, Allocations.Measure(() => ResolveAll(document, references)));

        static void ResolveAll(PdfDocument document, CosReference[] references)
        {
            foreach (CosReference reference in references)
            {
                _ = document.Resolve(reference);
            }
        }
    }

    [Fact]
    public void An_object_stream_is_decoded_once_however_many_threads_read_its_members()
    {
        using var logs = new CapturingLoggerProvider();
        using ILoggerFactory loggerFactory = TraceLogging(logs);
        using PdfDocument document = PdfDocument.Open(Corpus.Path("object-stream.pdf"), new PdfOptions().WithLoggerFactory(loggerFactory));
        int size = (int)((CosInteger)document.Trailer[new CosName("Size")]).Value;

        Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, iteration =>
        {
            foreach (int number in Shuffled(1, size - 1, iteration))
            {
                _ = document.Resolve(new CosReference(number, 0));
            }
        });

        (int Number, int Generation)[] parsed = ParsedObjects(logs);
        Assert.Equal(parsed.Length, parsed.Distinct().Count());
        Assert.Single(logs.Entries, entry => entry.EventId.Name == "ObjectStreamDecoded");
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Two_streams_whose_Lengths_refer_to_each_other_read_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(MutualLengths());

        var first = Assert.IsType<CosStream>(document.Resolve(new CosReference(4, 0)));
        var second = Assert.IsType<CosStream>(document.Resolve(new CosReference(5, 0)));

        Assert.Equal("four"u8.ToArray(), first.EncodedData.ToArray());
        Assert.Equal("five"u8.ToArray(), second.EncodedData.ToArray());
        Assert.Contains("StreamLengthInvalid", document.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    [Fact]
    public async Task Two_threads_each_loading_a_stream_whose_Length_is_the_other_one_do_not_deadlock()
    {
        // Thread A holds stream 4 and needs 5; thread B holds 5 and needs 4. One of them must give up instead of waiting.
        for (int attempt = 0; attempt < 200; attempt++)
        {
            using PdfDocument document = PdfDocument.Open(MutualLengths());
            using var start = new Barrier(2);
            Task<CosObject>[] loads =
            [
                Task.Run(() => { start.SignalAndWait(); return document.Resolve(new CosReference(4, 0)); }),
                Task.Run(() => { start.SignalAndWait(); return document.Resolve(new CosReference(5, 0)); }),
            ];

            // A deadlock fails the test with a TimeoutException instead of hanging the run.
            CosObject[] results = await Task.WhenAll(loads).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            Assert.Equal("four"u8.ToArray(), Assert.IsType<CosStream>(results[0]).EncodedData.ToArray());
            Assert.Equal("five"u8.ToArray(), Assert.IsType<CosStream>(results[1]).EncodedData.ToArray());
            Assert.Same(results[0], document.Resolve(new CosReference(4, 0)));
            Assert.Same(results[1], document.Resolve(new CosReference(5, 0)));
        }
    }

    /// <summary>A one-page file plus objects 4 and 5, streams whose Length entries refer to each other.</summary>
    private static byte[] MutualLengths() => new TestPdf().Build(
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
        "<< /Length 5 0 R >>\nstream\nfour\nendstream",
        "<< /Length 4 0 R >>\nstream\nfive\nendstream");

    /// <summary>A catalog, a page tree root and <see cref="PageCount"/> pages, each with a content stream whose Length is indirect.</summary>
    internal static byte[] ManyPages()
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(' ', Enumerable.Range(0, PageCount).Select(page => $"{3 + (3 * page)} 0 R"))}] /Count {PageCount} >>",
        };
        for (int page = 0; page < PageCount; page++)
        {
            int number = 3 + (3 * page);
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Contents {number + 1} 0 R >>");
            objects.Add($"<< /Length {number + 2} 0 R >>\nstream\nq Q\nendstream");
            objects.Add("3");
        }

        return new TestPdf().Build([.. objects]);
    }

    /// <summary>The numbers <paramref name="start"/> to <paramref name="start"/> + <paramref name="count"/> - 1 in an order fixed by <paramref name="seed"/>.</summary>
    internal static int[] Shuffled(int start, int count, int seed)
    {
        int[] values = [.. Enumerable.Range(start, count)];
#pragma warning disable CA5394 // A reproducible test order, not security.
        new Random(seed).Shuffle(values);
#pragma warning restore CA5394
        return values;
    }

    internal static ILoggerFactory TraceLogging(CapturingLoggerProvider logs) =>
        LoggerFactory.Create(logging => logging.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));

    internal static (int Number, int Generation)[] ParsedObjects(CapturingLoggerProvider logs) =>
    [
        .. logs.Entries
            .Where(entry => entry.EventId.Name == "ObjectParsed")
            .Select(entry => ((int)entry.State["ObjectNumber"]!, (int)entry.State["Generation"]!)),
    ];
}
