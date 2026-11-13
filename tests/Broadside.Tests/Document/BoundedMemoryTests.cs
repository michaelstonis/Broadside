using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Runtime;
using System.Text;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>
/// Files with millions of objects and files of several gigabytes open, walk and save with memory bounded by what they hold, not by
/// their length (issue #237, spec story 7). ISO 32000-2 §7.5.4 (the cross-reference table "permits random access to indirect
/// objects ... so that the entire PDF file need not be read") and §7.5.8 (cross-reference streams).
/// </summary>
/// <remarks>
/// <para>
/// The bounds: a cross-reference entry costs at most <see cref="BytesPerEntry"/> bytes allocated while the file opens (16 bytes
/// stored per entry; the two hash maps it replaced, per section and merged, allocated several times that). A full save copies
/// unchanged stream data in windows of 1 MiB, so no single write is larger than <see cref="LargestWrite"/>. Opening, walking and
/// saving unchanged a file of more than 4 GiB read from a seekable stream grows the process's private memory
/// (<see cref="ProcessMemory.PrivateBytes"/>, sampled throughout) by less than <see cref="PrivateMemoryBound"/> at its peak.
/// </para>
/// <para>
/// The 4 GiB file is produced as it is read (<see cref="SyntheticPdfStream"/>), never stored, so the test needs neither disk nor
/// memory for it; it reads the file twice (open and save) and writes it once to a stream that keeps only its length, in about 4
/// seconds on a laptop (measured: 16.0 bytes allocated per entry; peak private memory growth 260 MiB, largest write 1 MiB), so it
/// runs with every test run.
/// </para>
/// </remarks>
[Collection(HeavyTestCollection.Name)]
public sealed class BoundedMemoryTests
{
    private const int BytesPerEntry = 24;
    private const int LargestWrite = (1 << 20) + (128 << 10);
    private const long PrivateMemoryBound = 512L << 20;

    [Fact]
    public void A_file_of_five_million_objects_opens_within_24_bytes_per_cross_reference_entry()
    {
        const int objects = 5_000_000;
        byte[] file = ManyObjects(objects);
        Action open = () =>
        {
            using PdfDocument document = PdfDocument.Open(file);
            _ = document.Pages.Count;
        };

        // Each open reads 100 MB of table: one warm-up call and two measured ones.
        long allocated = Allocations.Measure(open, warmUpCalls: 1, attempts: 2);

        TestContext.Current.TestOutputHelper?.WriteLine($"Opening allocated {allocated} bytes, {(double)allocated / objects:F2} per object.");
        Assert.True(allocated < (long)BytesPerEntry * objects, $"Opening allocated {allocated} bytes, {(double)allocated / objects:F1} per object.");
        using PdfDocument document = PdfDocument.Open(file);
        Assert.Single(document.Pages);
        Assert.Empty(document.Diagnostics);
        Assert.Equal(CosNull.Instance, document.Resolve(new CosReference(objects, 0)));
    }

    [Fact]
    public void A_full_save_writes_a_large_stream_in_bounded_windows_and_keeps_its_bytes()
    {
        const long contentLength = 9L << 20;
        using SyntheticPdfStream source = SyntheticPdfStream.WithPages(2, contentLength, filler: (byte)'x');
        using PdfDocument document = PdfDocument.Open(source);
        using var saved = new MeasuringStream(keep: true);

        document.Save(saved);

        Assert.True(saved.LargestWrite <= LargestWrite, $"The largest write was {saved.LargestWrite} bytes.");
        using PdfDocument reopened = PdfDocument.Open(saved.Kept);
        Assert.Empty(reopened.Diagnostics);
        Assert.Equal(2, reopened.Pages.Count);
        foreach (PdfPage page in reopened.Pages)
        {
            var contents = (CosStream)reopened.Resolve(page.Dictionary[new CosName("Contents")]);
            ReadOnlyMemory<byte> data = contents.EncodedData;
            Assert.Equal(contentLength, data.Length);
            int comment = data.Span.IndexOf((byte)'\n') + 1;
            Assert.True(data.Span.StartsWith("% page "u8));
            Assert.Equal(-1, data.Span[comment..].IndexOfAnyExcept((byte)'x'));
        }
    }

    [Fact]
    public void A_file_over_4_GiB_from_a_seekable_stream_opens_walks_and_saves_in_bounded_memory()
    {
        const int pages = 257;
        const long contentLength = 16L << 20;
        using SyntheticPdfStream source = SyntheticPdfStream.WithPages(pages, contentLength);
        Assert.True(source.Length > 4L << 30, $"The file is {source.Length} bytes.");
        using var saved = new MeasuringStream();

        Settle();
        long before = ProcessMemory.PrivateBytes() ?? throw new InvalidOperationException("The process's private memory cannot be read on this platform.");
        long peak = before;
        using var done = new ManualResetEventSlim();
        var sampler = new Thread(() =>
        {
            while (!done.Wait(10))
            {
                peak = Math.Max(peak, ProcessMemory.PrivateBytes()!.Value);
            }
        });
        sampler.Start();
        var watch = Stopwatch.StartNew();
        try
        {
            using PdfDocument document = PdfDocument.Open(source);
            Assert.Equal(pages, document.Pages.Count);
            foreach (PdfPage page in document.Pages)
            {
                Assert.Equal(new PdfRectangle(0, 0, 612, 792), page.MediaBox);
            }

            document.Save(saved);
            Assert.Empty(document.Diagnostics);
        }
        finally
        {
            done.Set();
            sampler.Join();
        }

        long growth = peak - before;
        TestContext.Current.TestOutputHelper?.WriteLine($"Peak private memory growth {growth >> 20} MiB, largest write {saved.LargestWrite} bytes, {saved.Length} bytes saved in {watch.Elapsed}.");
        Assert.True(growth < PrivateMemoryBound, $"The process's private memory grew by {growth} bytes at its peak ({watch.Elapsed}).");
        Assert.True(saved.Length > 4L << 30, $"The saved file is {saved.Length} bytes.");
        Assert.True(saved.LargestWrite <= LargestWrite, $"The largest write was {saved.LargestWrite} bytes.");
        Assert.True(saved.Tail.EndsWith("%%EOF\n"u8));
    }

    /// <summary>
    /// A file of <paramref name="objects"/> objects: a catalog, a page tree with one page, and null objects up to the last number,
    /// listed by one classic table subsection.
    /// </summary>
    private static byte[] ManyObjects(int objects)
    {
        var file = new MemoryStream(objects * 46);
        long[] offsets = new long[objects + 1];
        Span<byte> number = stackalloc byte[16];
        void Write(ReadOnlySpan<byte> bytes) => file.Write(bytes);

        Write("%PDF-1.7\n"u8);
        offsets[1] = file.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n"u8);
        offsets[2] = file.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n"u8);
        offsets[3] = file.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>\nendobj\n"u8);
        for (int index = 4; index <= objects; index++)
        {
            offsets[index] = file.Position;
            Utf8Formatter.TryFormat(index, number, out int written);
            Write(number[..written]);
            Write(" 0 obj\nnull\nendobj\n"u8);
        }

        long xref = file.Position;
        Write(Encoding.ASCII.GetBytes($"xref\n0 {objects + 1}\n0000000000 65535 f\r\n"));
        Span<byte> entry = stackalloc byte[20];
        " 00000 n\r\n"u8.CopyTo(entry[10..]);
        for (int index = 1; index <= objects; index++)
        {
            Utf8Formatter.TryFormat(offsets[index], entry, out _, new StandardFormat('D', 10));
            Write(entry);
        }

        Write(Encoding.ASCII.GetBytes($"trailer\n<< /Size {objects + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
        return file.ToArray();
    }

    /// <summary>Collects everything collectable and returns the freed memory to the operating system.</summary>
    private static void Settle()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }
}
