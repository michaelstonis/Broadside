using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>
/// A source trailer with entries that describe a cross-reference section rather than the document (issue #445, found by the
/// <c>save</c> fuzz target: a mutation of <c>encrypted-rc4-40-r3.pdf</c> whose damaged trailer read as <c>/Type /Page</c>). A save
/// writes its own <c>Type</c>, <c>W</c>, <c>Index</c> and stream entries (ISO 32000-2 §7.5.5, §7.5.8.2, Table 17) and never repeats
/// the source's: the trailer's <c>Type</c> had replaced the written cross-reference stream's <c>/XRef</c>, so the saved file's
/// cross-reference information did not read, and with its objects in encrypted object streams a scan found no catalog.
/// </summary>
public sealed class TrailerSectionKeySaveTests
{
    /// <summary>Trailer entries of a section, as a damaged file or a cross-reference stream source may carry them.</summary>
    public static TheoryData<string, PdfCrossReferenceLayout, bool> EntriesAndLayouts
    {
        get
        {
            string[] entries =
            [
                "/Type /Page",
                "/Type /XRef /W [1 1 1] /Index [5 1]",
                "/Filter /ASCIIHexDecode /DecodeParms << /Predictor 12 /Columns 3 >> /Length 3 /DL 9",
                "/F (external.dat) /FFilter /ASCIIHexDecode /FDecodeParms << >>",
            ];
            var data = new TheoryData<string, PdfCrossReferenceLayout, bool>();
            foreach (string entry in entries)
            {
                foreach (PdfCrossReferenceLayout layout in Enum.GetValues<PdfCrossReferenceLayout>())
                {
                    data.Add(entry, layout, false);
                    data.Add(entry, layout, true);
                }
            }

            return data;
        }
    }

    [Fact]
    public void The_fuzz_finding_saves_a_file_that_reopens()
    {
        // 941 bytes: the save target keeps the document's RC4 encryption and writes a cross-reference stream with object streams.
        byte[] file = File.ReadAllBytes(Path.Combine(Corpus.Directory, "..", "Broadside.Fuzz", "Seeds", "save", "trailer-type-overrides-cross-reference-stream.pdf"));
        using PdfDocument source = PdfDocument.Open(file);
        Assert.Equal(new CosName("Page"), source.Trailer[new CosName("Type")]);

        byte[] saved = Save(source, new PdfSaveOptions().WithCrossReferenceLayout(PdfCrossReferenceLayout.StreamWithObjectStreams).WithObjectNumbering(PdfObjectNumbering.Compact));

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Single(reopened.Pages);
        Assert.True(reopened.IsEncrypted);
        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Code != "EncryptionDeprecated");
        CosGraph.AssertIsomorphic(source, source.Trailer[new CosName("Root")], reopened, reopened.Trailer[new CosName("Root")]);
    }

    [Theory]
    [MemberData(nameof(EntriesAndLayouts))]
    public void Section_entries_of_the_source_trailer_are_not_repeated(string entries, PdfCrossReferenceLayout layout, bool incremental)
    {
        byte[] file = new TestPdf { TrailerEntries = entries }.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>");
        using PdfDocument source = PdfDocument.Open(file);
        source.Catalog[new CosName("Lang")] = new CosString("en"u8);

        byte[] saved = Save(source, new PdfSaveOptions().WithCrossReferenceLayout(layout).WithIncrementalUpdate(incremental));

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Empty(reopened.Diagnostics);
        Assert.Single(reopened.Pages);
        Assert.Equal(new CosString("en"u8), reopened.Catalog[new CosName("Lang")]);
        if (!incremental && layout == PdfCrossReferenceLayout.Table)
        {
            // A complete file has one trailer, which holds only the document's entries.
            Assert.All(reopened.Trailer, entry => Assert.Contains(entry.Key.Value, (string[])["Size", "Root", "Info", "ID"]));
        }
    }

    private static byte[] Save(PdfDocument document, PdfSaveOptions options)
    {
        using var output = new MemoryStream();
        document.Save(output, options);
        return output.ToArray();
    }
}
