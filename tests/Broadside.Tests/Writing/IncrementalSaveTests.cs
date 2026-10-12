using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>Saving a changed document as an incremental update appended to its file. ISO 32000-2 §7.5.6.</summary>
public sealed class IncrementalSaveTests
{
    private static readonly CosName PageLayout = new("PageLayout");
    private static readonly CosName Rotate = new("Rotate");

    /// <summary>Saves that do not date the modification (#162), for tests that count exactly the objects an update writes.</summary>
    private static PdfSaveOptions Undated => new PdfSaveOptions().WithUpdateModificationDate(false);

    public static TheoryData<string> UpdatedForms => new() { "incremental-update.pdf", "xref-stream.pdf", "hybrid-xref.pdf" };

    [Theory]
    [MemberData(nameof(UpdatedForms))]
    public void A_changed_document_saved_incrementally_keeps_every_original_byte_and_reopens_with_the_change(string fileName)
    {
        byte[] file = Corpus.Bytes(fileName);
        using PdfDocument document = PdfDocument.Open(file);
        int revisions = document.Revisions.Count;
        document.Catalog[PageLayout] = new CosName("TwoColumnLeft");
        document.Pages[0].Dictionary[Rotate] = new CosInteger(90);

        byte[] saved = SaveIncrementally(document);

        Assert.True(saved.AsSpan(0, file.Length).SequenceEqual(file), "The original bytes changed.");
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(PdfPageLayout.TwoColumnLeft, reopened.PageLayout);
        Assert.Equal(90, reopened.Pages[0].Rotation);
        Assert.Equal(revisions + 1, reopened.Revisions.Count);
        Assert.Equal(saved.Length, reopened.Revisions[^1].Length);
        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning);
        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--check");
        Assert.True(exitCode == 0, output);
    }

    /// <summary>Every unencrypted well-formed corpus file (an encrypted document cannot be updated yet).</summary>
    public static TheoryData<string> WellFormedFiles => [.. Corpus.WellFormedFileNames.Where(name => !name.StartsWith("encrypted-", StringComparison.Ordinal))];

    [Theory]
    [MemberData(nameof(WellFormedFiles))]
    public void Every_well_formed_file_updated_incrementally_keeps_its_bytes_reopens_cleanly_and_passes_qpdf_check(string fileName)
    {
        byte[] file = Corpus.Bytes(fileName);
        using PdfDocument document = PdfDocument.Open(file);
        Diagnostic[] before = [.. document.Diagnostics.Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)];
        document.Catalog[new CosName("Lang")] = new CosString("en-GB"u8);

        byte[] saved = SaveIncrementally(document);

        Assert.True(saved.AsSpan(0, file.Length).SequenceEqual(file), "The original bytes changed.");
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal("en-GB", reopened.Language);
        Assert.Equal(document.Pages.Count, reopened.Pages.Count);
        Assert.Equal(before.Length, reopened.Diagnostics.Count(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning));
        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--check");
        Assert.True(exitCode == 0 || (exitCode == 3 && fileName == "name-tree-deep.pdf"), output);
    }

    [Theory]
    [MemberData(nameof(UpdatedForms))]
    public void A_second_incremental_save_appends_only_the_second_change(string fileName)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(fileName));
        document.Catalog[PageLayout] = new CosName("SinglePage");
        byte[] first = SaveIncrementally(document, Undated);
        PdfPage page = document.Pages[0];
        page.Dictionary[Rotate] = new CosInteger(270);

        byte[] second = SaveIncrementally(document, Undated);

        Assert.True(second.AsSpan(0, first.Length).SequenceEqual(first), "The first update's bytes changed.");
        List<(int Number, int Generation)> defined = ObjectsDefinedIn(second.AsSpan(first.Length));
        if (fileName == "xref-stream.pdf")
        {
            // The update's own cross-reference stream is an object too (§7.5.8).
            Assert.Equal(2, defined.Count);
            defined.RemoveAt(1);
        }

        Assert.Equal([(page.Reference!.ObjectNumber, page.Reference.Generation)], defined);
        using PdfDocument reopened = PdfDocument.Open(second);
        Assert.Equal(PdfPageLayout.SinglePage, reopened.PageLayout);
        Assert.Equal(270, reopened.Pages[0].Rotation);
        Assert.Equal(document.Revisions.Select(revision => revision.Length), reopened.Revisions.Select(revision => revision.Length));
        (int exitCode, string output) = ExternalTool.Run("qpdf", second, "--check");
        Assert.True(exitCode == 0, output);
    }

    [Fact]
    public void An_incremental_save_of_an_unchanged_document_appends_no_object()
    {
        byte[] file = Corpus.Bytes("incremental-update.pdf");
        using PdfDocument document = PdfDocument.Open(file);
        _ = document.Pages[0].MediaBox;

        byte[] saved = SaveIncrementally(document);

        Assert.Empty(ObjectsDefinedIn(saved.AsSpan(file.Length)));
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Single(reopened.Pages);
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void Removed_objects_get_free_entries_with_their_generations_raised_by_one_linked_from_object_0()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("incremental-update.pdf"));
        CosReference page = document.Pages[0].Reference!;
        CosReference added = document.AddObject(new CosDictionary { [new CosName("Title")] = new CosString("Removed"u8) });
        SaveIncrementally(document, Undated);
        int before = (int)document.Revisions[^1].Length;
        Assert.True(page.ObjectNumber < added.ObjectNumber);
        Assert.True(document.RemoveObject(added));
        Assert.False(document.RemoveObject(added));
        Assert.True(document.RemoveObject(page));

        byte[] saved = SaveIncrementally(document, Undated);

        // H.7.4: object 0 heads the free list, which runs through the freed numbers in order and back to 0.
        Dictionary<int, (int Type, long Second, int Third)> entries = NewestSection(saved);
        Assert.Equal((0, (long)page.ObjectNumber, 65535), entries[0]);
        Assert.Equal((0, (long)added.ObjectNumber, page.Generation + 1), entries[page.ObjectNumber]);
        Assert.Equal((0, 0L, added.Generation + 1), entries[added.ObjectNumber]);
        Assert.Equal(3, entries.Count);
        Assert.True(before < saved.Length);
        Assert.Same(CosNull.Instance, document.Resolve(added));
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Same(CosNull.Instance, reopened.Resolve(page));
        Assert.Same(CosNull.Instance, reopened.Resolve(added));
    }

    [Theory]
    [MemberData(nameof(UpdatedForms))]
    public void A_removed_object_reads_as_null_after_an_incremental_save_in_every_form(string fileName)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(fileName));
        CosReference info = document.AddObject(new CosDictionary());
        SaveIncrementally(document);
        Assert.True(document.RemoveObject(info));
        Assert.Same(CosNull.Instance, document.Resolve(info));

        byte[] saved = SaveIncrementally(document);

        Assert.Same(CosNull.Instance, document.Resolve(info));
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Same(CosNull.Instance, reopened.Resolve(info));
        Assert.Equal(document.Pages.Count, reopened.Pages.Count);
        Dictionary<int, (int Type, long Second, int Third)> entries = NewestSection(saved);
        Assert.Equal((0, (long)info.ObjectNumber, 65535), entries[0]);
        Assert.Equal((0, 0L, 1), entries[info.ObjectNumber]);
        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--check");
        Assert.True(exitCode == 0, output);
    }

    [Theory]
    [MemberData(nameof(UpdatedForms))]
    public void An_added_stream_is_written_compressed_and_reads_back_through_the_reference_that_names_it(string fileName)
    {
        byte[] file = Corpus.Bytes(fileName);
        byte[] content = System.Text.Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("0 0 m 100 100 l S\n", 200)));
        using PdfDocument document = PdfDocument.Open(file);
        CosReference stream = document.AddObject(new CosStream(new CosDictionary(), content));
        document.Pages[0].Dictionary[new CosName("Contents")] = stream;

        byte[] saved = SaveIncrementally(document);

        using PdfDocument reopened = PdfDocument.Open(saved);
        var written = (CosStream)reopened.Resolve(reopened.Pages[0].Dictionary[new CosName("Contents")]);
        Assert.Equal(new CosName("FlateDecode"), written.Dictionary[new CosName("Filter")]);
        Assert.Equal(content, reopened.DecodeStream(written).ToArray());
        Assert.Contains((stream.ObjectNumber, 0), ObjectsDefinedIn(saved.AsSpan(file.Length)));
        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--check");
        Assert.True(exitCode == 0, output);
    }

    [Fact]
    public void After_an_incremental_save_the_document_reads_from_the_updated_file_and_a_full_save_keeps_the_change()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("xref-stream.pdf"));
        document.Catalog[PageLayout] = new CosName("OneColumn");
        Assert.True(document.Catalog.IsDirty);

        byte[] updated = SaveIncrementally(document);

        Assert.False(document.Catalog.IsDirty);
        Assert.Equal(updated.Length, document.Revisions[^1].Length);
        using var output = new MemoryStream();
        document.Save(output);
        using PdfDocument rewritten = PdfDocument.Open(output.ToArray());
        Assert.Equal(PdfPageLayout.OneColumn, rewritten.PageLayout);
        Assert.Single(rewritten.Revisions);
        Assert.Empty(rewritten.Diagnostics);
    }

    [Fact]
    public void An_incremental_save_of_a_linearized_file_leaves_it_no_longer_linearized()
    {
        byte[] file = Corpus.Bytes("linearized.pdf");
        using PdfDocument document = PdfDocument.Open(file);
        Assert.True(document.IsLinearized);
        document.Pages[0].Dictionary[Rotate] = new CosInteger(90);

        byte[] saved = SaveIncrementally(document);

        Assert.False(document.IsLinearized);
        Assert.True(saved.AsSpan(0, file.Length).SequenceEqual(file));
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.False(reopened.IsLinearized);
        Assert.NotNull(reopened.Linearization);
        Assert.Equal(90, reopened.Pages[0].Rotation);
        Assert.Equal(document.Pages.Count, reopened.Pages.Count);
        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--check");
        Assert.True(exitCode == 0, output);
    }

    [Fact]
    public void An_update_to_a_file_whose_cross_reference_information_was_rebuilt_lists_every_object()
    {
        byte[] file = Corpus.Bytes("no-xref.pdf");
        using PdfDocument document = PdfDocument.Open(file);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning);
        document.Pages[0].Dictionary[Rotate] = new CosInteger(90);

        byte[] saved = SaveIncrementally(document);

        Assert.True(saved.AsSpan(0, file.Length).SequenceEqual(file));
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Empty(reopened.Diagnostics);
        Assert.Equal(90, reopened.Pages[0].Rotation);
        Assert.Single(reopened.Revisions);
        Assert.Single(document.Revisions);
        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--check");
        Assert.True(exitCode == 0, output);
    }

    [Fact]
    public void An_incremental_save_of_an_encrypted_document_is_refused_before_anything_is_written()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("encrypted-aes-256.pdf"));
        using var output = new MemoryStream();

        NotSupportedException error = Assert.Throws<NotSupportedException>(() => document.Save(output, new PdfSaveOptions().WithIncrementalUpdate(true)));

        Assert.Contains("encrypted", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task An_incremental_save_writes_the_same_bytes_to_a_stream_asynchronously_and_to_a_path()
    {
        byte[] file = Corpus.Bytes("hybrid-xref.pdf");
        using PdfDocument first = PdfDocument.Open(file);
        using PdfDocument second = PdfDocument.Open(file);
        first.Catalog[PageLayout] = new CosName("SinglePage");
        second.Catalog[PageLayout] = new CosName("SinglePage");
        byte[] synchronous = SaveIncrementally(first);
        using var output = new MemoryStream();

        await second.SaveAsync(output, new PdfSaveOptions().WithIncrementalUpdate(true), TestContext.Current.CancellationToken);

        Assert.Equal(synchronous, output.ToArray());
        Assert.Equal(first.Revisions.Count, second.Revisions.Count);
        string path = Path.Combine(Path.GetTempPath(), $"broadside-{Guid.NewGuid():N}.pdf");
        try
        {
            using PdfDocument third = PdfDocument.Open(file);
            third.Catalog[PageLayout] = new CosName("SinglePage");
            third.Save(path, new PdfSaveOptions().WithIncrementalUpdate(true));
            Assert.Equal(synchronous, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            Assert.False(third.Catalog.IsDirty);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_full_save_writes_added_objects_and_frees_removed_ones()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("page-tree-inherited.pdf"));
        CosReference removed = document.Pages[1].Reference!;
        CosReference added = document.AddObject(new CosDictionary { [new CosName("Marker")] = new CosInteger(7) });
        document.Catalog[new CosName("Marker")] = added;
        Assert.True(document.RemoveObject(removed));
        using var output = new MemoryStream();

        document.Save(output);

        using PdfDocument saved = PdfDocument.Open(output.ToArray());
        Assert.Equal(new CosInteger(7), ((CosDictionary)saved.Resolve(saved.Catalog[new CosName("Marker")]))[new CosName("Marker")]);
        Assert.Same(CosNull.Instance, saved.Resolve(removed));
        Assert.DoesNotContain((removed.ObjectNumber, removed.Generation), ObjectBodies.Read(output.ToArray()).Keys);
        Assert.Equal(0, NewestSection(output.ToArray())[removed.ObjectNumber].Type);
        Assert.Equal(removed.Generation + 1, NewestSection(output.ToArray())[removed.ObjectNumber].Third);
    }

    /// <summary>
    /// Reads the newest cross-reference section of <paramref name="file"/> (the one <c>startxref</c> names) as rows of Table 18 (ISO
    /// 32000-2 §7.5.8.3): type 0 free (next free number, generation), 1 in use (offset, generation). A classic table is read as text
    /// (§7.5.4); a stream's data is decoded through the library's filters and its rows read here, by <c>W</c> and <c>Index</c>.
    /// </summary>
    private static Dictionary<int, (int Type, long Second, int Third)> NewestSection(byte[] file)
    {
        string text = System.Text.Encoding.Latin1.GetString(file);
        System.Text.RegularExpressions.Match startxref = System.Text.RegularExpressions.Regex.Match(text, @"startxref\s+(\d+)\s+%%EOF\s*$");
        Assert.True(startxref.Success);
        int offset = int.Parse(startxref.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var entries = new Dictionary<int, (int Type, long Second, int Third)>();
        if (text.AsSpan(offset).StartsWith("xref", StringComparison.Ordinal))
        {
            string[] lines = text[(offset + 4)..text.IndexOf("trailer", offset, StringComparison.Ordinal)].Split('\n', StringSplitOptions.RemoveEmptyEntries);
            int number = 0;
            foreach (string line in lines.Select(line => line.TrimEnd('\r')))
            {
                string[] fields = line.Split(' ');
                if (fields.Length == 2)
                {
                    number = int.Parse(fields[0], System.Globalization.CultureInfo.InvariantCulture);
                    continue;
                }

                entries[number++] = (fields[2] == "n" ? 1 : 0, long.Parse(fields[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture));
            }

            return entries;
        }

        System.Text.RegularExpressions.Match header = System.Text.RegularExpressions.Regex.Match(text[offset..], @"^(\d+) (\d+) obj");
        using PdfDocument document = PdfDocument.Open(file);
        var stream = (CosStream)document.Resolve(new CosReference(int.Parse(header.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), int.Parse(header.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)));
        byte[] data = document.DecodeStream(stream).ToArray();
        int[] widths = [.. ((CosArray)stream.Dictionary[new CosName("W")]).Select(width => (int)((CosInteger)width).Value)];
        long[] index = stream.Dictionary.TryGetValue(new CosName("Index"), out CosObject? value)
            ? [.. ((CosArray)value).Select(item => ((CosInteger)item).Value)]
            : [0, ((CosInteger)stream.Dictionary[new CosName("Size")]).Value];
        int at = 0;
        for (int pair = 0; pair < index.Length; pair += 2)
        {
            for (long number = index[pair]; number < index[pair] + index[pair + 1]; number++)
            {
                long[] fields = new long[3];
                for (int field = 0; field < 3; field++)
                {
                    for (int @byte = 0; @byte < widths[field]; @byte++)
                    {
                        fields[field] = (fields[field] << 8) | data[at++];
                    }
                }

                entries[(int)number] = ((int)fields[0], fields[1], (int)fields[2]);
            }
        }

        return entries;
    }

    /// <summary>The object numbers and generations defined (<c>N G obj</c> at a line start) in <paramref name="bytes"/>, in order.</summary>
    private static List<(int Number, int Generation)> ObjectsDefinedIn(ReadOnlySpan<byte> bytes) =>
        [.. System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(bytes), @"(?m)^(\d+) (\d+) obj\b")
            .Select(match => (int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)))];

    private static byte[] SaveIncrementally(PdfDocument document, PdfSaveOptions? options = null)
    {
        using var output = new MemoryStream();
        document.Save(output, (options ?? new PdfSaveOptions()).WithIncrementalUpdate(true));
        return output.ToArray();
    }
}
