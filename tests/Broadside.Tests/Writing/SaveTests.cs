using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>Saving an opened document as a complete new file. ISO 32000-2 §7.5, §7.3.8, §7.3.10.</summary>
public sealed class SaveTests
{
    private static readonly PdfCrossReferenceLayout[] Layouts = Enum.GetValues<PdfCrossReferenceLayout>();

    /// <summary>
    /// Broken files whose damage is in a document-model structure read lazily (a name tree, an outline, a pattern, mesh data, a Type 3 glyph), not in the file structure:
    /// they open without a diagnostic and save byte for byte, damage included, so qpdf reports it again.
    /// </summary>
    private static readonly HashSet<string> DamagedInTheDocumentModel = new(StringComparer.Ordinal) { "name-tree-broken.pdf", "outline-broken.pdf", "annotations-malformed.pdf", "text-type1-pfb.pdf", "text-type1-hex-eexec.pdf", "text-type1-bad-lengths.pdf", "pattern-recursive.pdf", "shading-mesh-truncated.pdf", "text-type3-recursive.pdf", "ccitt-g3-damaged.pdf", "ccitt-g4-truncated.pdf" };

    /// <summary>
    /// Well-formed files qpdf 12.4.2 warns about because it compares name tree keys as decoded text, not byte by byte as ISO 32000-2
    /// §7.9.6 requires (tests/Corpus/README.md).
    /// </summary>
    private static readonly HashSet<string> QpdfComparesNameTreeKeysAsText = new(StringComparer.Ordinal) { "name-tree-deep.pdf" };

    /// <summary>Every unencrypted well-formed corpus file with every layout (encrypted files cannot be saved yet).</summary>
    public static TheoryData<string, PdfCrossReferenceLayout> FilesAndLayouts
    {
        get
        {
            var data = new TheoryData<string, PdfCrossReferenceLayout>();
            foreach (string file in Corpus.WellFormedFileNames.Where(name => !name.StartsWith("encrypted-", StringComparison.Ordinal)))
            {
                foreach (PdfCrossReferenceLayout layout in Layouts)
                {
                    data.Add(file, layout);
                }
            }

            return data;
        }
    }

    /// <summary>Every unencrypted deliberately broken corpus file with every layout (encrypted files cannot be saved yet).</summary>
    public static TheoryData<string, PdfCrossReferenceLayout> BrokenFilesAndLayouts
    {
        get
        {
            var data = new TheoryData<string, PdfCrossReferenceLayout>();
            foreach (string file in Corpus.MalformedFileNames.Where(name => !name.StartsWith("encrypted-", StringComparison.Ordinal) && !DamagedInTheDocumentModel.Contains(name)))
            {
                foreach (PdfCrossReferenceLayout layout in Layouts)
                {
                    data.Add(file, layout);
                }
            }

            return data;
        }
    }

    [Fact]
    public void A_saved_file_reopens_with_the_same_pages_and_no_diagnostics()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"));
        using var output = new MemoryStream();

        document.Save(output);

        using PdfDocument reopened = PdfDocument.Open(output.ToArray());
        Assert.Equal(new PdfRectangle(0, 0, 612, 792), Assert.Single(reopened.Pages).MediaBox);
        Assert.Empty(reopened.Diagnostics);
    }

    [Theory]
    [MemberData(nameof(FilesAndLayouts))]
    public void A_well_formed_file_saved_in_any_layout_reopens_with_the_same_objects_and_no_diagnostics(string fileName, PdfCrossReferenceLayout layout)
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path(fileName));

        using PdfDocument saved = PdfDocument.Open(Save(source, layout));

        Assert.Equal(source.Pages.Count, saved.Pages.Count);
        for (int index = 0; index < source.Pages.Count; index++)
        {
            Assert.Equal(source.Pages[index].MediaBox, saved.Pages[index].MediaBox);
            Assert.Equal(source.Pages[index].CropBox, saved.Pages[index].CropBox);
            Assert.Equal(source.Pages[index].Rotation, saved.Pages[index].Rotation);
        }

        AssertSameObjectGraph(source, saved);
        Assert.DoesNotContain(saved.Diagnostics, diagnostic => diagnostic.Severity > Broadside.Diagnostics.DiagnosticSeverity.Information);
    }

    [Theory]
    [MemberData(nameof(FilesAndLayouts))]
    public void A_well_formed_file_saved_in_any_layout_passes_qpdf_check(string fileName, PdfCrossReferenceLayout layout)
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path(fileName));

        (int exitCode, string output) = ExternalTool.Run("qpdf", Save(source, layout), "--check");

        if (QpdfComparesNameTreeKeysAsText.Contains(fileName))
        {
            // qpdf 12 compares name-tree keys as decoded text and warns (exit code 3); qpdf 11 compares
            // bytes as ISO 32000-2 §7.9.6 requires and passes cleanly (exit code 0). Accept either, and
            // when there are warnings every one must be that text-order complaint.
            Assert.True(exitCode is 0 or 3, output);
            Assert.All(
                output.Split('\n').Where(line => line.StartsWith("WARNING", StringComparison.Ordinal)),
                line => Assert.Contains("keys are not sorted", line, StringComparison.Ordinal));
            return;
        }

        Assert.True(exitCode == 0, output);
    }

    [Theory]
    [MemberData(nameof(FilesAndLayouts))]
    public void Objects_that_were_not_changed_keep_their_source_bytes_in_any_layout(string fileName, PdfCrossReferenceLayout layout)
    {
        byte[] file = Corpus.Bytes(fileName);
        using PdfDocument source = PdfDocument.Open(file);

        byte[] saved = Save(source, layout);

        Dictionary<(int Number, int Generation), byte[]> before = ObjectBodies.Read(file);
        Dictionary<(int Number, int Generation), byte[]> after = ObjectBodies.Read(saved);
        foreach (((int number, int generation), byte[] bytes) in before)
        {
            if (after.TryGetValue((number, generation), out byte[]? written))
            {
                Assert.True(bytes.AsSpan().SequenceEqual(written), $"Object {number} {generation} changed bytes.");
            }
            else
            {
                Assert.True(IsDroppedOnSave(source, new CosReference(number, generation)), $"Object {number} {generation} was not written.");
            }
        }
    }

    [Fact]
    public void Bytes_a_canonical_writer_would_change_are_kept_in_every_layout()
    {
        // (en\055US) is "en-US" with an octal escape, and the dictionary has no spaces: re-serializing would change both (§7.3.4.2).
        const string catalog = "<</Type/Catalog/Pages 2 0 R/Lang(en\\055US)>>";
        byte[] file = new Document.TestPdf().Build(
            catalog,
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612.0 792.00] >>");
        using PdfDocument source = PdfDocument.Open(file);

        foreach (PdfCrossReferenceLayout layout in Layouts)
        {
            Dictionary<(int Number, int Generation), byte[]> written = ObjectBodies.Read(Save(source, layout));
            Assert.Equal(catalog, System.Text.Encoding.Latin1.GetString(written[(1, 0)]));
            Assert.Equal("<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612.0 792.00] >>", System.Text.Encoding.Latin1.GetString(written[(3, 0)]));
        }
    }

    [Fact]
    public void A_changed_object_is_written_with_its_new_value_and_the_others_keep_their_bytes()
    {
        byte[] file = Corpus.Bytes("page-tree-inherited.pdf");
        using PdfDocument source = PdfDocument.Open(file);
        PdfPage page = source.Pages[0];
        page.Dictionary[new CosName("MediaBox")] = new CosArray([new CosInteger(0), new CosInteger(0), new CosInteger(100), new CosInteger(200)]);

        byte[] saved = Save(source, PdfCrossReferenceLayout.Table);

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(new PdfRectangle(0, 0, 100, 200), reopened.Pages[0].MediaBox);
        Assert.Equal(source.Pages[1].MediaBox, reopened.Pages[1].MediaBox);
        Assert.Empty(reopened.Diagnostics);
        Dictionary<(int Number, int Generation), byte[]> before = ObjectBodies.Read(file);
        Dictionary<(int Number, int Generation), byte[]> after = ObjectBodies.Read(saved);
        (int, int) changed = (page.Reference!.ObjectNumber, page.Reference.Generation);
        Assert.False(before[changed].AsSpan().SequenceEqual(after[changed]));
        Assert.All(before.Keys.Where(key => key != changed), key => Assert.True(before[key].AsSpan().SequenceEqual(after[key])));
    }

    [Theory]
    [MemberData(nameof(BrokenFilesAndLayouts))]
    public void A_repaired_file_is_saved_with_a_new_cross_reference_section_and_reopens_without_warnings_or_errors(string fileName, PdfCrossReferenceLayout layout)
    {
        // The broken corpus files: wrong xref offsets, missing endobj, wrong stream Length, no xref, wrong startxref (#41 repairs
        // them on open). A save writes what was read, repaired objects serialized, under a cross-reference section of its own.
        using PdfDocument source = PdfDocument.Open(Corpus.Path(fileName));

        byte[] saved = Save(source, layout);

        Assert.Contains(source.Diagnostics, diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(source.Pages.Count, reopened.Pages.Count);
        AssertSameObjectGraph(source, reopened);
        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning);
        Assert.Single(reopened.Revisions);
        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--check");
        Assert.True(exitCode == 0, output);
    }

    [Fact]
    public void Saving_an_updated_file_collapses_its_revisions_into_one_with_the_newest_objects()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("incremental-update.pdf"));

        byte[] saved = Save(source, PdfCrossReferenceLayout.Table);

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Single(reopened.Revisions);
        Assert.Equal(new PdfRectangle(0, 0, 595, 842), Assert.Single(reopened.Pages).MediaBox);
        Assert.Equal(1, CountOf(saved, "\nxref\n"u8));
        Assert.Equal(1, CountOf(saved, "%%EOF"u8));
        Assert.False(reopened.Trailer.ContainsKey(new CosName("Prev")));
    }

    [Theory]
    [InlineData(PdfCrossReferenceLayout.Table)]
    [InlineData(PdfCrossReferenceLayout.Stream)]
    [InlineData(PdfCrossReferenceLayout.StreamWithObjectStreams)]
    public void The_information_dictionary_of_a_hybrid_file_survives_from_its_object_stream(PdfCrossReferenceLayout layout)
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("hybrid-xref.pdf"));

        using PdfDocument reopened = PdfDocument.Open(Save(source, layout));

        var info = Assert.IsType<CosDictionary>(reopened.Resolve(reopened.Trailer[new CosName("Info")]));
        Assert.Equal("hybrid", Assert.IsType<CosString>(info[new CosName("Title")]).DecodeText());
        Assert.False(reopened.Trailer.ContainsKey(new CosName("XRefStm")));
    }

    [Fact]
    public void A_linearized_file_is_saved_without_its_linearization_dictionary_and_hint_stream()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("linearized.pdf"));
        Assert.True(source.IsLinearized);

        using PdfDocument reopened = PdfDocument.Open(Save(source, PdfCrossReferenceLayout.Table));

        Assert.Null(reopened.Linearization);
        Assert.False(reopened.IsLinearized);
        Assert.Equal(CosNull.Instance, reopened.Resolve(source.Linearization!.Reference));
        Assert.Equal(CosNull.Instance, reopened.Resolve(new CosReference(7, 0)));
        Assert.Equal(2, reopened.Pages.Count);
    }

    [Fact]
    public void A_linearization_dictionary_the_page_tree_refers_to_is_kept()
    {
        // A damaged file (found by the save fuzz target): the page tree root's kid is the linearization dictionary, object 5.
        byte[] file = Corpus.Bytes("linearized.pdf");
        int kids = file.AsSpan().IndexOf("/Kids [ 4 0 R ]"u8);
        file[kids + "/Kids [ ".Length] = (byte)'5';
        using PdfDocument source = PdfDocument.Open(file);

        using PdfDocument reopened = PdfDocument.Open(Save(source, PdfCrossReferenceLayout.Table));

        Assert.Equal(source.Pages.Count, reopened.Pages.Count);
        Assert.True(CosObject.DeepEquals(source.Resolve(new CosReference(5, 0)), reopened.Resolve(new CosReference(5, 0))));
    }

    [Fact]
    public void A_document_with_object_numbers_beyond_the_limit_is_not_saved()
    {
        // A classic table lists every number up to the largest; renumbering is not supported yet.
        byte[] file = Document.TestPdf.AppendUpdate(
            Document.TestPdf.OnePage(string.Empty),
            "/Size 9000001 /Root 1 0 R",
            (9_000_000, 0, "<< /Unused true >>"));
        using PdfDocument source = PdfDocument.Open(file);
        using var output = new MemoryStream();

        Assert.Throws<NotSupportedException>(() => source.Save(output));
        Assert.Equal(0, output.Length);
    }

    // Found by libFuzzer (issue #48): an 11 KB file whose one object is numbered 6,600,016 saved to a 132 MB file (a classic table
    // lists every number up to the largest), which then took 2.3 GB to open again. Saving a numbering that sparse needs
    // renumbering, so it is refused like numbers above the limit; a large but dense numbering still saves.
    [Fact]
    public void A_document_whose_numbering_is_far_sparser_than_its_objects_is_not_saved()
    {
        byte[] file = Document.TestPdf.AppendUpdate(
            Document.TestPdf.OnePage(string.Empty),
            "/Size 2000001 /Root 1 0 R",
            (2_000_000, 0, "<< /Unused true >>"));
        using PdfDocument source = PdfDocument.Open(file);
        using var output = new MemoryStream();

        NotSupportedException error = Assert.Throws<NotSupportedException>(() => source.Save(output));
        Assert.Contains("renumbering", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void A_document_with_a_sparse_numbering_below_a_million_is_saved()
    {
        byte[] file = Document.TestPdf.AppendUpdate(
            Document.TestPdf.OnePage(string.Empty),
            "/Size 1000001 /Root 1 0 R",
            (1_000_000, 0, "<< /Unused true >>"));
        using PdfDocument source = PdfDocument.Open(file);
        using var output = new MemoryStream();

        source.Save(output);

        using PdfDocument saved = PdfDocument.Open(output.ToArray());
        Assert.Single(saved.Pages);
    }

    [Fact]
    public void An_encrypted_document_is_not_saved()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("encrypted-rc4-128.pdf"));
        using var output = new MemoryStream();

        Assert.Throws<NotSupportedException>(() => source.Save(output));
        Assert.Equal(0, output.Length);
    }

    [Theory]
    [InlineData(PdfCrossReferenceLayout.Table)]
    [InlineData(PdfCrossReferenceLayout.Stream)]
    [InlineData(PdfCrossReferenceLayout.StreamWithObjectStreams)]
    public async Task SaveAsync_writes_the_same_bytes_as_Save(PdfCrossReferenceLayout layout)
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("object-stream.pdf"));
        var options = new PdfSaveOptions().WithCrossReferenceLayout(layout);
        using var synchronous = new MemoryStream();
        using var asynchronous = new MemoryStream();

#pragma warning disable CA1849 // The synchronous path is what the asynchronous one is compared with.
        source.Save(synchronous, options);
#pragma warning restore CA1849
        await source.SaveAsync(asynchronous, options, TestContext.Current.CancellationToken);

        Assert.True(synchronous.ToArray().AsSpan().SequenceEqual(asynchronous.ToArray()));
    }

    [Fact]
    public void Saving_to_a_path_writes_the_same_bytes_as_saving_to_a_stream()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("flate-stream.pdf"));
        string path = Path.Combine(Path.GetTempPath(), $"broadside-{Guid.NewGuid():N}.pdf");
        try
        {
            source.Save(path);

            Assert.Equal(Save(source, PdfCrossReferenceLayout.Table), File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Saving_twice_writes_the_same_bytes_and_keeps_the_first_file_identifier()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("linearized.pdf"));
        var sourceId = (CosArray)source.Trailer[new CosName("ID")];

        byte[] first = Save(source, PdfCrossReferenceLayout.Table);
        byte[] second = Save(source, PdfCrossReferenceLayout.Table);

        Assert.Equal(first, second);
        using PdfDocument reopened = PdfDocument.Open(first);
        var id = Assert.IsType<CosArray>(reopened.Trailer[new CosName("ID")]);
        Assert.Equal(sourceId[0], id[0]);
        Assert.Equal(16, Assert.IsType<CosString>(id[1]).Bytes.Length);
    }

    [Fact]
    public void A_file_without_identifiers_gets_two_equal_ones_when_saved()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("empty-page.pdf"));
        Assert.False(source.Trailer.ContainsKey(new CosName("ID")));

        using PdfDocument reopened = PdfDocument.Open(Save(source, PdfCrossReferenceLayout.Stream));

        var id = Assert.IsType<CosArray>(reopened.Trailer[new CosName("ID")]);
        Assert.Equal(2, id.Count);
        Assert.Equal(id[0], id[1]);
        Assert.Equal(16, Assert.IsType<CosString>(id[0]).Bytes.Length);
    }

    [Theory]
    [InlineData("%PDF-1.4", PdfCrossReferenceLayout.Table, "1.4")]
    [InlineData("%PDF-1.4", PdfCrossReferenceLayout.Stream, "1.5")]
    [InlineData("%PDF-1.4", PdfCrossReferenceLayout.StreamWithObjectStreams, "1.5")]
    [InlineData("%PDF-2.0", PdfCrossReferenceLayout.Stream, "2.0")]
    public void The_header_keeps_the_version_unless_the_layout_needs_a_later_one(string header, PdfCrossReferenceLayout layout, string expected)
    {
        using PdfDocument source = PdfDocument.Open(Document.TestPdf.OnePage(string.Empty, header: header));

        byte[] saved = Save(source, layout);

        Assert.StartsWith($"%PDF-{expected}\n%", System.Text.Encoding.Latin1.GetString(saved, 0, 10), StringComparison.Ordinal);
        Assert.True(saved[10] >= 128 && saved[11] >= 128 && saved[12] >= 128 && saved[13] >= 128, "The second line marks the file as binary (§7.5.2).");
    }

    [Theory]
    [InlineData(PdfCrossReferenceLayout.Table, "xref\n0 ")]
    [InlineData(PdfCrossReferenceLayout.Stream, "")]
    public void Startxref_gives_the_offset_of_the_cross_reference_information(PdfCrossReferenceLayout layout, string expectedStart)
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("empty-page.pdf"));

        byte[] saved = Save(source, layout);

        string text = System.Text.Encoding.Latin1.GetString(saved);
        int startxref = text.LastIndexOf("startxref\n", StringComparison.Ordinal);
        int offset = int.Parse(text.AsSpan(startxref + 10, text.IndexOf('\n', startxref + 10) - startxref - 10), System.Globalization.CultureInfo.InvariantCulture);
        Assert.StartsWith(expectedStart, text[offset..], StringComparison.Ordinal);
        if (layout == PdfCrossReferenceLayout.Stream)
        {
            Assert.Matches(@"^[0-9]+ 0 obj\n<< /Type /XRef ", text[offset..]);
        }
        else
        {
            // One subsection from 0; every entry is exactly 20 bytes (§7.5.4).
            string[] lines = text[(offset + 5)..text.IndexOf("trailer", offset, StringComparison.Ordinal)].Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.All(lines.Skip(1), line => Assert.Equal(19, line.Length));
            Assert.StartsWith("0000000000 65535 f", lines[1], StringComparison.Ordinal);
        }

        Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);
    }

    private static int CountOf(byte[] file, ReadOnlySpan<byte> value)
    {
        int count = 0;
        ReadOnlySpan<byte> rest = file;
        for (int at = rest.IndexOf(value); at >= 0; at = rest.IndexOf(value))
        {
            count++;
            rest = rest[(at + value.Length)..];
        }

        return count;
    }

    /// <summary>Saves through the public API into memory.</summary>
    private static byte[] Save(PdfDocument document, PdfCrossReferenceLayout layout)
    {
        using var output = new MemoryStream();
        document.Save(output, new PdfSaveOptions().WithCrossReferenceLayout(layout));
        return output.ToArray();
    }

    /// <summary>
    /// The objects a full save does not write: old cross-reference and object streams, the linearization dictionary and hint streams
    /// (Annex F), and objects an update deleted.
    /// </summary>
    private static bool IsDroppedOnSave(PdfDocument source, CosReference reference)
    {
        CosObject value = source.Resolve(reference);
        if (value is CosNull || reference.Equals(source.Linearization?.Reference))
        {
            return true;
        }

        return value is CosStream stream
            && ((stream.Dictionary.TryGetValue(new CosName("Type"), out CosObject? type) && (type.Equals(new CosName("XRef")) || type.Equals(new CosName("ObjStm"))))
                || (source.Linearization is not null && stream.Dictionary.ContainsKey(new CosName("S"))));
    }

    /// <summary>Walks every object reachable from the trailer of both documents and compares them structurally.</summary>
    private static void AssertSameObjectGraph(PdfDocument expected, PdfDocument actual)
    {
        var pending = new Queue<CosObject>();
        var seen = new HashSet<CosReference>();
        foreach (string key in (string[])["Root", "Info"])
        {
            if (expected.Trailer.TryGetValue(new CosName(key), out CosObject? value))
            {
                Assert.True(actual.Trailer.TryGetValue(new CosName(key), out CosObject? other), $"The trailer lost {key}.");
                Assert.True(CosObject.DeepEquals(value, other), $"The trailer's {key} changed.");
                pending.Enqueue(value);
            }
        }

        while (pending.TryDequeue(out CosObject? value))
        {
            switch (value)
            {
                case CosReference reference when seen.Add(reference):
                    CosObject left = expected.Resolve(reference);
                    Assert.True(CosObject.DeepEquals(left, actual.Resolve(reference)), $"Object {reference} differs.");
                    pending.Enqueue(left);
                    break;
                case CosArray array:
                    array.ToList().ForEach(pending.Enqueue);
                    break;
                case CosDictionary dictionary:
                    dictionary.Values.ToList().ForEach(pending.Enqueue);
                    break;
                case CosStream stream:
                    pending.Enqueue(stream.Dictionary);
                    break;
                default:
                    break;
            }
        }
    }
}
