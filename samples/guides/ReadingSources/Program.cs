using Broadside;
using Broadside.Samples;

Sample.Start("Reading: sources");

OpenFromPath();
OpenFromStream();
OpenFromBytes();
await OpenAsync();
OpenWithOneEngine();
ReadALargeFile();
Console.WriteLine("OK");

static void OpenFromPath()
{
    #region open-path
    using PdfDocument document = PdfDocument.Open("page-tree-inherited.pdf");
    Console.WriteLine($"{document.Pages.Count} pages");
    #endregion
    Sample.Expect(document.Pages.Count == 2, "two pages from a path");
}

static void OpenFromStream()
{
    #region open-stream
    using FileStream stream = File.OpenRead("page-tree-inherited.pdf");
    using PdfDocument document = PdfDocument.Open(stream); // keep the stream open until the document is disposed
    Console.WriteLine(document.Pages[1].ExtractText().Text);
    #endregion
    Sample.Expect(document.Pages[1].ExtractText().Text.Contains("Page 2", StringComparison.Ordinal), "page 2 from a stream");
}

static void OpenFromBytes()
{
    #region open-bytes
    byte[] bytes = File.ReadAllBytes("page-tree-inherited.pdf");
    using PdfDocument document = PdfDocument.Open(bytes); // the bytes are not copied: leave them unchanged
    Console.WriteLine(document.Pages[0].ExtractText().Text);
    #endregion
    Sample.Expect(document.Pages[0].ExtractText().Text.Contains("Page 1", StringComparison.Ordinal), "page 1 from bytes");
}

static async Task OpenAsync()
{
    #region open-async
    await using FileStream stream = File.OpenRead("page-tree-inherited.pdf");
    using PdfDocument document = await PdfDocument.OpenAsync(stream);
    #endregion
    Sample.Expect(document.Pages.Count == 2, "two pages, opened asynchronously");
}

static void OpenWithOneEngine()
{
    #region engine
    var engine = new PdfEngine(new PdfOptions().UseStrict());
    foreach (string path in new[] { "page-tree-inherited.pdf", "linearized.pdf" })
    {
        using PdfDocument document = engine.Open(path);
        Console.WriteLine($"{path}: {document.Pages.Count} pages, linearized: {document.IsLinearized}");
    }
    #endregion
}

static void ReadALargeFile()
{
    #region large-files
    PdfOptions options = new PdfOptions()
        .WithStreamBufferLimit(16L << 20)    // a non-seekable stream over 16 MiB is copied to a temporary file
        .WithMaxDecodedLength(256L << 20)    // no single stream decodes to more than 256 MiB
        .WithImageCacheBudget(16L << 20);    // decoded images kept for rendering stay under 16 MiB

    using PdfDocument document = PdfDocument.Open("linearized.pdf", options);
    foreach (PdfPage page in document.Pages)
    {
        Console.WriteLine(page.ExtractText().Text);
        document.TrimCaches(4L << 20); // keep at most 4 MiB of render caches between pages
    }

    Console.WriteLine($"The render caches hold {document.CachedBytes} bytes");
    #endregion
    Sample.Expect(document.CachedBytes <= 4L << 20, "caches trimmed");
}
