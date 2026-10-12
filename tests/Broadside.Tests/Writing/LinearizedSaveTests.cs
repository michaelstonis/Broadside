using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>A full rewrite laid out as a linearized file (ISO 32000-2 Annex F, F.3 and F.4; issue #228).</summary>
public sealed class LinearizedSaveTests
{
    private static readonly CosName Contents = new("Contents");

    [Fact]
    public void A_one_page_document_saved_linearized_passes_qpdf_and_reopens_as_linearized()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("empty-page.pdf"));

        byte[] saved = Save(source, Linearized());

        AssertQpdfAcceptsLinearization(saved);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.True(reopened.IsLinearized);
        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Severity > DiagnosticSeverity.Information);
    }

    public static TheoryData<string, PdfCrossReferenceLayout> FilesAndLayouts
    {
        get
        {
            var data = new TheoryData<string, PdfCrossReferenceLayout>();
            foreach (string file in Corpus.WellFormedFileNames.Where(name => !name.StartsWith("encrypted-", StringComparison.Ordinal)))
            {
                foreach (PdfCrossReferenceLayout layout in Enum.GetValues<PdfCrossReferenceLayout>())
                {
                    data.Add(file, layout);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(FilesAndLayouts))]
    public void Every_well_formed_file_saved_linearized_passes_qpdf_and_reopens_with_hints_matching_its_layout(string fileName, PdfCrossReferenceLayout layout)
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path(fileName));

        byte[] saved = Save(source, Linearized(layout));

        AssertQpdfAcceptsLinearization(saved);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.True(reopened.IsLinearized);
        Assert.Single(reopened.Revisions);
        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Severity > DiagnosticSeverity.Information);
        AssertSamePages(source, reopened);
        AssertHintsMatchLayout(reopened, saved);
    }

    [Fact]
    public void Pages_that_share_objects_the_first_page_does_not_use_list_them_in_the_hints()
    {
        // Page 1 uses font 3; pages 2 and 3 share font 4 (part 8); page 3 also has an image only it uses (part 7).
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [5 0 R 7 0 R 9 0 R] /Count 3 /MediaBox [0 0 200 200] >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>",
            "<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 3 0 R >> >> /Contents 6 0 R >>",
            "<< /Length 32 >>\nstream\nBT /F1 12 Tf 10 10 Td (1) Tj ET\nendstream",
            "<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 4 0 R >> >> /Contents 8 0 R >>",
            "<< /Length 32 >>\nstream\nBT /F1 12 Tf 10 10 Td (2) Tj ET\nendstream",
            "<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 4 0 R >> /XObject << /Im 11 0 R >> >> /Contents 10 0 R >>",
            "<< /Length 32 >>\nstream\nBT /F1 12 Tf 10 10 Td (3) Tj ET\nendstream",
            "<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Length 1 >>\nstream\n\u0080\nendstream");
        using PdfDocument source = PdfDocument.Open(file);

        byte[] saved = Save(source, Linearized());

        AssertQpdfAcceptsLinearization(saved);
        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfLinearizationHints hints = reopened.Linearization!.Hints!;
        Assert.Equal([3, 2, 3], hints.Pages.Select(page => page.ObjectCount));
        Assert.Empty(hints.Pages[0].SharedObjects);
        int courier = ((CosReference)((CosDictionary)reopened.Pages[1].Resources![new CosName("Font")])[new CosName("F1")]).ObjectNumber;
        int shared = Assert.Single(hints.Pages[1].SharedObjects);
        Assert.Equal([shared], hints.Pages[2].SharedObjects);
        Assert.Equal(courier, hints.SharedObjects[shared].FirstObjectNumber);
        Assert.Equal(hints.Pages[0].ObjectCount + 1, hints.SharedObjects.Count);
        AssertHintsMatchLayout(reopened, saved);
    }

    [Fact]
    public void Inherited_page_attributes_are_written_on_each_page_and_the_document_is_not_changed()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("page-tree-inherited.pdf"));
        CosDictionary root = (CosDictionary)source.Resolve(source.Catalog[new CosName("Pages")]);
        string[] keysBefore = [.. root.Keys.Select(key => key.Value)];
        Assert.False(source.Pages[0].Dictionary.ContainsKey(new CosName("MediaBox")));

        using PdfDocument reopened = PdfDocument.Open(Save(source, Linearized()));

        Assert.All(reopened.Pages, page =>
        {
            Assert.True(page.Dictionary.ContainsKey(new CosName("MediaBox")));
            Assert.True(page.Dictionary.ContainsKey(new CosName("Resources")));
        });
        var writtenRoot = (CosDictionary)reopened.Resolve(reopened.Catalog[new CosName("Pages")]);
        Assert.DoesNotContain(writtenRoot.Keys, key => key.Value is "MediaBox" or "Resources" or "CropBox" or "Rotate");
        Assert.Equal(keysBefore, root.Keys.Select(key => key.Value));
        Assert.False(source.Pages[0].Dictionary.ContainsKey(new CosName("MediaBox")));
        Assert.False(root.IsDirty);
    }

    [Fact]
    public void An_incrementally_updated_file_saved_linearized_is_rewritten_as_one_revision()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("incremental-update.pdf"));
        Assert.True(source.Revisions.Count > 1);

        byte[] saved = Save(source, Linearized());

        AssertQpdfAcceptsLinearization(saved);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.True(reopened.IsLinearized);
        Assert.Single(reopened.Revisions);
        CosGraph.AssertIsomorphic(source, reopened);
    }

    [Fact]
    public void An_incremental_update_of_a_linearized_file_leaves_it_unlinearized_with_an_information_diagnostic()
    {
        using PdfDocument source = PdfDocument.Open(LinearizedCorpusFile("page-tree-inherited.pdf"));
        Assert.True(source.IsLinearized);
        source.Pages[0].Dictionary[new CosName("Rotate")] = new CosInteger(90);

        byte[] updated = Save(source, new PdfSaveOptions().WithIncrementalUpdate(true));

        Diagnostic dropped = Assert.Single(source.Diagnostics, diagnostic => diagnostic.Code == "LinearizationDropped");
        Assert.Equal(DiagnosticSeverity.Information, dropped.Severity);
        Assert.False(source.IsLinearized);
        using PdfDocument reopened = PdfDocument.Open(updated);
        Assert.False(reopened.IsLinearized);
        Assert.Equal(90, reopened.Pages[0].Rotation);
        Assert.Equal(2, reopened.Revisions.Count);
    }

    [Fact]
    public void An_incremental_update_of_a_linearized_file_in_strict_mode_is_not_refused()
    {
        byte[] linearized = LinearizedCorpusFile("empty-page.pdf");
        using PdfDocument source = PdfDocument.Open(linearized, new PdfOptions().UseStrict());

        byte[] updated = Save(source, new PdfSaveOptions().WithIncrementalUpdate(true));

        Assert.True(updated.Length > linearized.Length);
    }

    [Fact]
    public void Linearizing_an_incremental_update_is_refused_before_anything_is_written()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("empty-page.pdf"));
        using var stream = new MemoryStream();

        Assert.Throws<ArgumentException>(() => source.Save(stream, Linearized().WithIncrementalUpdate(true)));

        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void A_document_without_pages_cannot_be_linearized()
    {
        byte[] file = new TestPdf().Build("<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [] /Count 0 >>");
        using PdfDocument source = PdfDocument.Open(file);
        using var stream = new MemoryStream();

        Assert.Throws<NotSupportedException>(() => source.Save(stream, Linearized()));

        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void Saving_linearized_twice_writes_the_same_bytes_and_a_compressed_hint_stream_reads_the_same_hints()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("text-truetype-embedded.pdf"));

        byte[] first = Save(source, Linearized());
        byte[] second = Save(source, Linearized());
        byte[] uncompressed = Save(source, Linearized().WithStreamCompression(PdfStreamCompression.None));

        Assert.Equal(first, second);
        using PdfDocument compressedHints = PdfDocument.Open(first);
        using PdfDocument plainHints = PdfDocument.Open(uncompressed);
        CosStream hintStream = HintStream(compressedHints, first);
        Assert.Equal(new CosName("FlateDecode"), hintStream.Dictionary[new CosName("Filter")]);
        Assert.False(HintStream(plainHints, uncompressed).Dictionary.ContainsKey(new CosName("Filter")));
        Assert.Equal(
            plainHints.Linearization!.Hints!.Pages.Select(page => (page.ObjectCount, page.Length)),
            compressedHints.Linearization!.Hints!.Pages.Select(page => (page.ObjectCount, page.Length)));
    }

    private static CosStream HintStream(PdfDocument document, byte[] file)
    {
        var hints = (CosArray)document.Linearization!.Dictionary[new CosName("H")];
        long offset = ((CosInteger)hints[0]).Value;
        int number = ObjectOffsets(file).Single(pair => pair.Value == offset).Key;
        return (CosStream)document.Resolve(new CosReference(number, 0));
    }

    [Fact]
    public async Task A_remote_linearized_file_renders_page_one_from_its_first_page_range_alone()
    {
        const int pages = 20;
        byte[] file = PaddedPages(pages);
        using PdfDocument local = PdfDocument.Open(file);
        byte[] linearized = Save(local, Linearized().WithStreamCompression(PdfStreamCompression.None));
        long firstPageEnd;
        using (PdfDocument check = PdfDocument.Open(linearized))
        {
            firstPageEnd = ((CosInteger)check.Linearization!.Dictionary[new CosName("E")]).Value;
        }

        using var server = new RangeServer(linearized);
        using HttpClient client = server.CreateClient();
        using PdfDocument document = await PdfDocument.OpenAsync(client, new Uri("http://broadside.test/linearized.pdf"), TestContext.Current.CancellationToken);
        Assert.True(document.IsLinearized);
        int opened = server.Requests.Count;

        PdfPage first = document.Pages[0];
        await first.PrefetchAsync(TestContext.Current.CancellationToken);
        var backend = new RecordingBackend();
        first.CreateDisplayList().Replay(backend);
        Assert.NotEmpty(backend.Operations);
        Assert.Contains("Page 1", first.ExtractText().Text, StringComparison.Ordinal);

        // Everything page one needed after the open came from the first-page byte range (parts 1 to 6, up to E).
        Assert.True(server.Requests.Count > opened, $"Page one's content was fetched by the open: {server}");
        Assert.All(server.Requests.Skip(opened), request => Assert.True(request.Start + request.Length <= firstPageEnd, $"{request} lies past E = {firstPageEnd}: {server}"));

        // No other page's content was sent (outside the tail the open reads first).
        long tailStart = server.Requests[0].Start;
        string text = Encoding.Latin1.GetString(linearized);
        for (int page = 2; page <= pages; page++)
        {
            long content = text.IndexOf(string.Create(CultureInfo.InvariantCulture, $"(Page {page})"), StringComparison.Ordinal);
            long middle = content + (PagePadding / 2);
            if (middle + 1024 <= tailStart)
            {
                Assert.False(server.AnyServed(middle, middle + 1024), $"Page {page}'s content was fetched: {server}");
            }
        }
    }

    private const int PagePadding = 40 * 1024;

    /// <summary>A file of <paramref name="count"/> pages sharing a font, each content stream padded with comments to 40 KiB.</summary>
    private static byte[] PaddedPages(int count)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [" + string.Join(' ', Enumerable.Range(0, count).Select(static page => $"{4 + (2 * page)} 0 R")) + $"] /Count {count} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        };
        for (int page = 0; page < count; page++)
        {
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents {5 + (2 * page)} 0 R >>");
            var content = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"0 0 10 10 re f BT /F1 12 Tf 72 720 Td (Page {page + 1}) Tj ET\n"));
            while (content.Length < PagePadding)
            {
                content.Append('%').Append('x', 78).Append('\n');
            }

            objects.Add(string.Create(CultureInfo.InvariantCulture, $"<< /Length {content.Length} >>\nstream\n{content}\nendstream"));
        }

        return new TestPdf().Build([.. objects]);
    }

    private static byte[] LinearizedCorpusFile(string fileName)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName));
        return Save(document, Linearized());
    }

    private static PdfSaveOptions Linearized(PdfCrossReferenceLayout layout = PdfCrossReferenceLayout.Table) =>
        new PdfSaveOptions().WithLinearize(true).WithCrossReferenceLayout(layout);

    private static byte[] Save(PdfDocument document, PdfSaveOptions options)
    {
        using var stream = new MemoryStream();
        document.Save(stream, options);
        return stream.ToArray();
    }

    /// <summary>The pages read the same: boxes, rotation, and graphs isomorphic from their resources and contents (inherited or not).</summary>
    private static void AssertSamePages(PdfDocument expected, PdfDocument actual)
    {
        Assert.Equal(expected.Pages.Count, actual.Pages.Count);
        for (int index = 0; index < expected.Pages.Count; index++)
        {
            PdfPage before = expected.Pages[index];
            PdfPage after = actual.Pages[index];
            Assert.Equal(before.MediaBox, after.MediaBox);
            Assert.Equal(before.CropBox, after.CropBox);
            Assert.Equal(before.Rotation, after.Rotation);
            CosGraph.AssertIsomorphic(expected, (CosObject?)before.Resources ?? CosNull.Instance, actual, (CosObject?)after.Resources ?? CosNull.Instance);
            CosGraph.AssertIsomorphic(expected, before.Dictionary.GetValueOrDefault(Contents) ?? CosNull.Instance, actual, after.Dictionary.GetValueOrDefault(Contents) ?? CosNull.Instance);
        }
    }

    /// <summary>
    /// The hint tables the reader decodes agree with where the objects are, found by scanning the file for <c>N 0 obj</c>: each page
    /// starts at its page object and ends where the next begins (the first at <c>E</c>), and each shared group starts at its object.
    /// </summary>
    private static void AssertHintsMatchLayout(PdfDocument document, byte[] file)
    {
        Dictionary<int, long> offsets = ObjectOffsets(file);
        PdfLinearization linearization = document.Linearization!;
        PdfLinearizationHints? hints = linearization.Hints;
        Assert.NotNull(hints);
        Assert.Equal(document.Pages.Count, linearization.PageCount);
        Assert.Equal(document.Pages.Count, hints.Pages.Count);
        Assert.Equal(document.Pages[0].Reference!.ObjectNumber, linearization.FirstPageObjectNumber);
        long firstPageEnd = ((CosInteger)linearization.Dictionary[new CosName("E")]).Value;
        for (int index = 0; index < hints.Pages.Count; index++)
        {
            PdfPageHint page = hints.Pages[index];
            int number = document.Pages[index].Reference!.ObjectNumber;
            Assert.Equal(number, page.FirstObjectNumber);
            Assert.Equal(offsets[number], page.Offset);
            long end = index == 0 ? firstPageEnd : index + 1 < hints.Pages.Count ? hints.Pages[index + 1].Offset : page.Offset + page.Length;
            Assert.Equal(end, page.Offset + page.Length);
            Assert.Empty(hints.Pages[0].SharedObjects);
            Assert.All(page.SharedObjects, shared => Assert.InRange(shared, 0, hints.SharedObjects.Count - 1));
        }

        Assert.All(hints.SharedObjects, group => Assert.Equal(offsets[group.FirstObjectNumber], group.Offset));
    }

    /// <summary>The offset of each <c>N 0 obj</c> at the start of a line, the first definition of each number.</summary>
    private static Dictionary<int, long> ObjectOffsets(byte[] file)
    {
        var offsets = new Dictionary<int, long>();
        string text = Encoding.Latin1.GetString(file);
        foreach (Match match in Regex.Matches(text, @"(?<=[\r\n])(\d+) 0 obj\b"))
        {
            offsets.TryAdd(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), match.Index);
        }

        return offsets;
    }

    /// <summary>
    /// qpdf's structural check and its linearization check (which recomputes the parts and hint tables from the file) find no error
    /// and nothing wrong with the linearization. A warning qpdf gives the source file too (name-tree-deep.pdf's keys are in byte
    /// order, which qpdf's comparison disagrees with) is allowed.
    /// </summary>
    private static void AssertQpdfAcceptsLinearization(byte[] file)
    {
        (int exitCode, string output) = ExternalTool.Run("qpdf", file, "--check");
        Assert.True(exitCode is 0 or 3 && output.Contains("File is linearized", StringComparison.Ordinal) && !output.Contains("ERROR", StringComparison.Ordinal), output);
        (exitCode, output) = ExternalTool.Run("qpdf", file, "--check-linearization");
        Assert.True(exitCode is 0 or 3 && output.Contains("no linearization errors", StringComparison.Ordinal), output);
        Assert.DoesNotContain(
            output.Split('\n'),
            line => line.Contains("WARNING", StringComparison.Ordinal) && !line.Contains("Name/Number tree", StringComparison.Ordinal));
    }
}
