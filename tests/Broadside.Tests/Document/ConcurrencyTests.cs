using System.Collections.Concurrent;
using System.Security.Cryptography;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>Every source a document can be opened from, for the concurrency theory.</summary>
public enum ConcurrentSource
{
    Bytes,
    Path,
    FileStream,
    SeekableStream,
    NonSeekableStream,
}

/// <summary>
/// The concurrency contract of <see cref="PdfDocument"/> (CLAUDE.md, spec #33 "Thread safety"): while nobody mutates a document, any
/// number of threads may read it at once, and they see exactly what one thread reading alone sees. Every page and every object of
/// every corpus file is read from many threads in parallel, repeatedly and in random order, through every kind of source.
/// ISO 32000-2 §7.5.4 (random access to indirect objects through the cross-reference table).
/// </summary>
[Collection(HeavyTestCollection.Name)]
public class ConcurrencyTests
{
    private const int Repetitions = 50;

    /// <summary>The owner password of the encrypted corpus files, so the password-protected ones open too (ignored elsewhere).</summary>
    private static readonly PdfOptions Options = new PdfOptions().WithPassword("owner");

    public static TheoryData<string, ConcurrentSource> FilesBySource
    {
        get
        {
            var data = new TheoryData<string, ConcurrentSource>();
            foreach (string fileName in Corpus.AllFileNames)
            {
                foreach (ConcurrentSource source in Enum.GetValues<ConcurrentSource>())
                {
                    data.Add(fileName, source);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(FilesBySource))]
    public void Every_page_and_object_reads_the_same_from_many_threads_as_from_one(string fileName, ConcurrentSource source)
    {
        Reading expected;
        try
        {
            using PdfDocument alone = PdfDocument.Open(Corpus.Bytes(fileName), Options);
            expected = Reading.Of(alone, References(alone));
        }
        catch (DiagnosticException unreadable)
        {
            // A file the reader cannot open yet (cross-reference reconstruction is issue #41) fails the same way from every source.
            DiagnosticException exception = Assert.Throws<DiagnosticException>(() => Open(fileName, source).Dispose());
            Assert.Equal(unreadable.Diagnostic.Code, exception.Diagnostic.Code);
            return;
        }

        using PdfDocument document = Open(fileName, source);
        var instances = new ConcurrentDictionary<CosReference, CosObject>();
        Parallel.For(0, Repetitions, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, repetition =>
        {
            foreach (int index in LazyLoadingTests.Shuffled(0, expected.References.Length, repetition))
            {
                CosReference reference = expected.References[index];
                CosObject value = document.Resolve(reference);
                Assert.Same(instances.GetOrAdd(reference, value), value);
                Assert.Equal(expected.Objects[index], Reading.Describe(document, value));
            }

            Assert.Equal(expected.Pages, Reading.DescribePages(document));
        });

        Assert.Equal(expected.Diagnostics, Reading.DistinctDiagnostics(document));
    }

    /// <summary>Every object reachable from the trailer, plus objects 1 to Size - 1 with generation 0, in a fixed order.</summary>
    private static CosReference[] References(PdfDocument document)
    {
        var found = new HashSet<CosReference>();
        var pending = new Stack<CosObject>([document.Trailer]);
        while (pending.TryPop(out CosObject? value))
        {
            switch (value)
            {
                case CosReference reference when found.Add(reference):
                    pending.Push(document.Resolve(reference));
                    break;
                case CosArray array:
                    foreach (CosObject item in array)
                    {
                        pending.Push(item);
                    }

                    break;
                case CosDictionary dictionary:
                    foreach (KeyValuePair<CosName, CosObject> entry in dictionary)
                    {
                        pending.Push(entry.Value);
                    }

                    break;
                case CosStream stream:
                    pending.Push(stream.Dictionary);
                    break;
            }
        }

        long size = document.Trailer.TryGetValue(new CosName("Size"), out CosObject? sizeEntry) && sizeEntry is CosInteger integer ? integer.Value : 0;
        for (int number = 1; number < Math.Min(size, 10_000); number++)
        {
            found.Add(new CosReference(number, 0));
        }

        return [.. found.OrderBy(reference => reference.ObjectNumber).ThenBy(reference => reference.Generation)];
    }

    private static PdfDocument Open(string fileName, ConcurrentSource source) => source switch
    {
        ConcurrentSource.Bytes => PdfDocument.Open(Corpus.Bytes(fileName), Options),
        ConcurrentSource.Path => PdfDocument.Open(Corpus.Path(fileName), Options),
        ConcurrentSource.FileStream => OpenFileStream(fileName),
        ConcurrentSource.SeekableStream => PdfDocument.Open(new ProbeStream(Corpus.Bytes(fileName)), Options),
        _ => PdfDocument.Open(new ProbeStream(Corpus.Bytes(fileName), seekable: false), Options),
    };

    /// <summary>Opens through a <see cref="FileStream"/> the test then closes: the mapping outlives the caller's handle.</summary>
    private static PdfDocument OpenFileStream(string fileName)
    {
        using FileStream stream = Corpus.Open(fileName);
        return PdfDocument.Open(stream, Options);
    }

    /// <summary>What one complete read of a document saw, as values.</summary>
    private sealed record Reading(CosReference[] References, string[] Objects, string Pages, string[] Diagnostics)
    {
        public static Reading Of(PdfDocument document, CosReference[] references)
        {
            string[] objects = [.. references.Select(reference => Describe(document, document.Resolve(reference)))];
            return new Reading(references, objects, DescribePages(document), DistinctDiagnostics(document));
        }

        /// <summary>The object's canonical syntax, and for a stream the hash of its decoded data.</summary>
        public static string Describe(PdfDocument document, CosObject value) => value is CosStream stream
            ? $"{value} decoded {Convert.ToHexString(SHA256.HashData(document.DecodeStream(stream).Span))}"
            : value.ToString();

        public static string DescribePages(PdfDocument document) => string.Join('\n', document.Pages.Select(static page =>
            $"{page.Reference} media {page.MediaBox} crop {page.CropBox} bleed {page.BleedBox} trim {page.TrimBox} art {page.ArtBox} "
            + $"rotate {page.Rotation} unit {page.UserUnit} resources {page.Resources}"));

        // Decoding is not cached, so a filter diagnostic repeats with each decode; what must match is the set of deviations.
        public static string[] DistinctDiagnostics(PdfDocument document) =>
            [.. document.Diagnostics.Select(static diagnostic => diagnostic.ToString()).Distinct().Order(StringComparer.Ordinal)];
    }
}
