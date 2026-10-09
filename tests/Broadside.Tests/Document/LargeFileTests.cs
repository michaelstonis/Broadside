using System.Globalization;
using System.Text;
using Broadside.Objects;

namespace Broadside.Tests.Document;

/// <summary>
/// A file larger than 2 GiB opens and reads its last page with bounded memory: the reader keeps offsets as 64-bit values, maps the
/// file instead of reading it, and keeps stream data in the file. ISO 32000-2 §7.5.4 (the cross-reference table "permits random
/// access to indirect objects ... so that the entire PDF file need not be read"; ten-digit offsets reach 9 999 999 999).
/// </summary>
/// <remarks>
/// The bound: opening, walking all pages, resolving every content stream and decoding the last one grows the managed heap by less
/// than 64 MiB, while the file is 2.1 GiB. Measured with <see cref="GC.GetTotalMemory(bool)"/>, not the working set, which counts
/// the mapped (and reclaimable) pages of the file.
/// </remarks>
[Collection(HeavyTestCollection.Name)]
public class LargeFileTests
{
    private const int PageCount = 2100;
    private const int ContentLength = 1 << 20;
    private const long ManagedMemoryBound = 64L << 20;

    [Fact]
    public void A_file_over_2_GiB_opens_and_reads_its_last_page_in_bounded_memory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"broadside-large-{Guid.NewGuid():N}.pdf");
        try
        {
            long length = WriteLargeFile(path);
            Assert.True(length > (long)int.MaxValue + ContentLength, $"The file is {length} bytes.");

            ReadLastPage(() => PdfDocument.Open(path));

            // The same file through a seekable stream that is not a FileStream: read in place in windows, not mapped.
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096);
            using var stream = new BufferedStream(file, 64 * 1024);
            ReadLastPage(() => PdfDocument.Open(stream));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void ReadLastPage(Func<PdfDocument> open)
    {
        long before = GC.GetTotalMemory(forceFullCollection: true);
        using PdfDocument document = open();

        Assert.Equal(PageCount, document.Pages.Count);
        PdfPage last = document.Pages[PageCount - 1];
        Assert.Equal(new PdfRectangle(0, 0, 612, 792), last.MediaBox);

        // Every content stream object, 2 GiB of data in all, without reading the data.
        foreach (PdfPage page in document.Pages)
        {
            Assert.Equal(ContentLength, ((CosInteger)((CosStream)document.Resolve(page.Dictionary[new CosName("Contents")])).Dictionary[new CosName("Length")]).Value);
        }

        var contents = (CosStream)document.Resolve(last.Dictionary[new CosName("Contents")]);
        ReadOnlyMemory<byte> decoded = document.DecodeStream(contents);
        Assert.Equal(ContentLength, decoded.Length);
        Assert.True(decoded.Span.StartsWith(Encoding.ASCII.GetBytes($"q Q % page {PageCount - 1} ")));
        Assert.Empty(document.Diagnostics);

        decoded = default;
        long growth = GC.GetTotalMemory(forceFullCollection: true) - before;
        Assert.True(growth < ManagedMemoryBound, $"The managed heap grew by {growth} bytes.");
        GC.KeepAlive(document);
    }

    /// <summary>Writes the file one object at a time; returns its length.</summary>
    private static long WriteLargeFile(string path)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 1 << 20);
        var offsets = new long[(2 * PageCount) + 3];
        byte[] padding = new byte[ContentLength];
        padding.AsSpan().Fill((byte)'x');
        padding[^1] = (byte)'\n';

        Write(file, "%PDF-1.7\n%âãÏÓ\n");
        offsets[1] = file.Position;
        Write(file, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        offsets[2] = file.Position;
        var kids = new StringBuilder();
        for (int page = 0; page < PageCount; page++)
        {
            kids.Append(CultureInfo.InvariantCulture, $"{3 + (2 * page)} 0 R ");
        }

        Write(file, $"2 0 obj\n<< /Type /Pages /Kids [{kids}] /Count {PageCount} >>\nendobj\n");
        for (int page = 0; page < PageCount; page++)
        {
            int number = 3 + (2 * page);
            offsets[number] = file.Position;
            Write(file, $"{number} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Contents {number + 1} 0 R >>\nendobj\n");
            offsets[number + 1] = file.Position;
            string prefix = $"q Q % page {page} ";
            Write(file, $"{number + 1} 0 obj\n<< /Length {ContentLength} >>\nstream\n{prefix}");
            file.Write(padding, prefix.Length, ContentLength - prefix.Length);
            Write(file, "\nendstream\nendobj\n");
        }

        long xref = file.Position;
        int size = (2 * PageCount) + 3;
        var table = new StringBuilder();
        table.Append(CultureInfo.InvariantCulture, $"xref\n0 {size}\n0000000000 65535 f \n");
        for (int number = 1; number < size; number++)
        {
            table.Append(CultureInfo.InvariantCulture, $"{offsets[number]:D10} 00000 n \n");
        }

        table.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {size} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        Write(file, table.ToString());
        return file.Length;
    }

    private static void Write(FileStream file, string text) => file.Write(Encoding.Latin1.GetBytes(text));
}
