using Broadside.IO;
using Broadside.Objects;
using Broadside.Parsing;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>
/// Hint table counts and lengths are untrusted (ISO 32000-2 Annex F, F.4.1 to F.4.3; issue #568): a page offset or shared object
/// hint table entry that gives a page or group more objects than bytes, or an <c>N</c> larger than the file, makes the tables invalid
/// with a diagnostic, and a hinted range never sizes the object search by its count alone.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class HintTableBoundsTests
{
    private const long AllocationBound = 64L << 20;

    private static byte[] Reproducer =>
        File.ReadAllBytes(Path.Combine(Corpus.Directory, "..", "Broadside.Fuzz", "Seeds", "first-page-first", "hint-object-count-exceeds-file.pdf"));

    [Fact]
    public void A_remote_read_of_hints_that_give_a_page_two_billion_objects_allocates_in_proportion_to_the_file()
    {
        // A mutation of linearized.pdf (1,627 bytes) whose page offset hint table's least object count (item 1) is 0x7F000002:
        // page 2's objects were searched for in a table of 2.1 billion offsets, 11 to 14 GB.
        byte[] file = Reproducer;

        using var source = new RemoteLikeSource(PdfSource.FromMemory(file));
        long before = GC.GetAllocatedBytesForCurrentThread();
        using PdfDocument document = PdfDocument.Read(source, EngineConfiguration.From(new PdfOptions()));
        int pages = document.Pages.Count;
        foreach (PdfPage page in document.Pages)
        {
            _ = page.MediaBox;
        }

        for (int number = 1; number < 13; number++)
        {
            _ = document.Resolve(new CosReference(number, 0));
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < AllocationBound, $"The read allocated {allocated:N0} bytes.");
        Assert.Equal(2, pages);
        Assert.Contains(
            document.Diagnostics,
            diagnostic => diagnostic.Code == "LinearizationHintsInvalid"
                && diagnostic.Message.Contains("gives a page more objects than bytes", StringComparison.Ordinal));
    }

    [Fact]
    public void A_local_read_of_the_same_file_has_no_hints_and_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(Reproducer);

        Assert.True(document.IsLinearized);
        Assert.Null(document.Linearization!.Hints);
        Assert.Equal(2, document.Pages.Count);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "LinearizationHintsInvalid");
    }

    [Theory]
    [InlineData(2L, 1L)]
    [InlineData(0x7F000002L, 184L)]
    [InlineData(1L, 0L)]
    public void A_page_with_more_objects_than_bytes_makes_the_page_offset_table_invalid(long objects, long length)
    {
        byte[] data = PageOffsetTable(objects, location: 0, length);
        var layout = new HintTableLayout(HeaderOffset: 0, HintOffset: 2000, HintLength: 0, FirstPageObjectNumber: 1, PageCount: 1, FileLength: 1000);

        PdfLinearizationHints? hints = HintTableReader.Parse(data, PageOffsetTableLength, layout, out string? error);

        Assert.Null(hints);
        Assert.Equal("The page offset hint table gives a page more objects than bytes.", error);
    }

    [Theory]
    [InlineData(3L, 900L, 100L)] // inside the file
    [InlineData(3L, 990L, 20L)] // ends past the file: the object search reads only the bytes there are
    public void A_page_with_no_more_objects_than_bytes_is_read(long objects, long location, long length)
    {
        byte[] data = PageOffsetTable(objects, location, length);
        var layout = new HintTableLayout(HeaderOffset: 0, HintOffset: 2000, HintLength: 0, FirstPageObjectNumber: 1, PageCount: 1, FileLength: 1000);

        PdfLinearizationHints? hints = HintTableReader.Parse(data, PageOffsetTableLength, layout, out string? error);

        Assert.Null(error);
        PdfPageHint page = Assert.Single(hints!.Pages);
        Assert.Equal((1, (int)objects, location, length), (page.FirstObjectNumber, page.ObjectCount, page.Offset, page.Length));
    }

    [Fact]
    public void N_larger_than_the_file_makes_the_tables_invalid()
    {
        byte[] data = PageOffsetTable(objects: 1, location: 0, length: 10);
        var layout = new HintTableLayout(HeaderOffset: 0, HintOffset: 2000, HintLength: 0, FirstPageObjectNumber: 1, PageCount: 11, FileLength: 10);

        Assert.Null(HintTableReader.Parse(data, PageOffsetTableLength, layout, out string? error));
        Assert.Equal("The linearization parameter dictionary's N entry is too large for a hint table.", error);
    }

    [Fact]
    public void A_hinted_group_whose_count_exceeds_its_bytes_is_searched_in_proportion_to_its_bytes()
    {
        // The locator takes hints as given; a count the reader did not bound still sizes the search by the range's bytes.
        byte[] file = "1 0 obj\nnull\nendobj\n2 0 obj\nnull\nendobj\n"u8.ToArray();
        using PdfSource source = PdfSource.FromMemory(file);
        var hints = new PdfLinearizationHints([], [new PdfSharedObjectHint(1, int.MaxValue - 1, 0, file.Length)]);
        var locator = new HintObjectLocator(source, 0, () => hints);

        long before = GC.GetAllocatedBytesForCurrentThread();
        bool first = locator.TryLocate(1, out XrefEntry one);
        bool second = locator.TryLocate(2, out XrefEntry two);
        bool far = locator.TryLocate(1_000_000, out _);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < AllocationBound, $"The search allocated {allocated:N0} bytes.");
        Assert.True(first);
        Assert.Equal(0, one.Offset);
        Assert.True(second);
        Assert.Equal(20, two.Offset);
        Assert.False(far);
    }

    /// <summary>The page offset hint table header's length (Table F.3): 32-bit items 1, 2, 4, 6 and 8, 16-bit items 3, 5, 7 and 9 to 13.</summary>
    private const int PageOffsetTableLength = (5 * 4) + (8 * 2);

    /// <summary>
    /// A page offset hint table (Table F.3) whose pages each have <paramref name="objects"/> objects and <paramref name="length"/>
    /// bytes, the first at <paramref name="location"/>, every per-page item 0 bits wide, then a shared object table (Table F.5) with
    /// no groups.
    /// </summary>
    private static byte[] PageOffsetTable(long objects, long location, long length)
    {
        var bytes = new List<byte>();
        Add32(bytes, objects); // 1: least objects in a page
        Add32(bytes, location); // 2: location of the first page's page object
        bytes.AddRange([0, 0]); // 3: bits for the object count difference
        Add32(bytes, length); // 4: least page length
        bytes.AddRange([0, 0]); // 5: bits for the length difference
        Add32(bytes, 0); // 6
        bytes.AddRange([0, 0]); // 7
        Add32(bytes, 0); // 8
        bytes.AddRange([0, 0]); // 9
        bytes.AddRange([0, 0]); // 10: bits for the shared object count
        bytes.AddRange([0, 0]); // 11
        bytes.AddRange([0, 0]); // 12
        bytes.AddRange([0, 4]); // 13: denominator

        // Shared object hint table header (Table F.5): no groups.
        Add32(bytes, 0);
        Add32(bytes, 0);
        Add32(bytes, 0);
        Add32(bytes, 0);
        bytes.AddRange([0, 0]);
        Add32(bytes, 0);
        bytes.AddRange([0, 0]);
        return [.. bytes];

        static void Add32(List<byte> bytes, long value) =>
            bytes.AddRange([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
    }

    /// <summary>A source over another that says it is not local, so the document reads it as it reads a remote file.</summary>
    private sealed class RemoteLikeSource(PdfSource inner) : PdfSource
    {
        public override long Length => inner.Length;

        public override bool IsLocal => false;

        public override int Read(long offset, Span<byte> destination) => inner.Read(offset, destination);

        public override ReadOnlyMemory<byte> GetWindow(long offset, int minimumLength) => inner.GetWindow(offset, minimumLength);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
