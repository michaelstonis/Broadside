using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.Tests.Writing;
using Broadside.TestSupport;

namespace Broadside.Tests.Files;

/// <summary>
/// Writing attachments: embedded files with full file specifications in the <c>EmbeddedFiles</c> name tree (ISO 32000-2 §7.7.4,
/// §7.11.3, §7.11.4) and associated files on the catalog, pages and annotations (§14.13), read back after a save.
/// </summary>
public sealed class AttachmentWritingTests
{
    private static readonly DateTimeOffset Created = new(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private static readonly DateTimeOffset Modified = new(2024, 6, 7, 8, 9, 10, TimeSpan.FromHours(2));

    [Fact]
    public void An_added_attachment_re_reads_with_its_bytes_and_parameters()
    {
        byte[] contents = "name,value\nalpha,1\n"u8.ToArray();
        using PdfDocument document = PdfDocument.Create();

        document.AddEmbeddedFile(
            "dätä.csv",
            contents,
            new PdfEmbeddedFileOptions()
                .WithMediaType("text/csv")
                .WithDescription("Comma-separated data")
                .WithCreationDate(Created)
                .WithModificationDate(Modified));

        byte[] file = Save(document);
        using PdfDocument reopened = PdfDocument.Open(file);
        PdfEmbeddedFileEntry entry = Assert.Single(reopened.EmbeddedFiles);
        Assert.Equal("dätä.csv", entry.Name);
        PdfFileSpecification specification = entry.File;
        Assert.Equal("dätä.csv", specification.FileName);
        Assert.Equal("Comma-separated data", specification.Description);
        PdfEmbeddedFile embedded = specification.EmbeddedFile!;
        Assert.Equal("text/csv", embedded.Subtype);
        Assert.Equal(contents, embedded.Decode().ToArray());
        PdfEmbeddedFileParameters parameters = embedded.Parameters!;
        Assert.Equal(contents.Length, parameters.Size);
        Assert.Equal(Created, parameters.CreationDate!.Value.Value);
        Assert.Equal(Modified, parameters.ModificationDate!.Value.Value);
        Assert.Equal(PdfCheckSumStatus.Matches, embedded.VerifyCheckSum());
        Assert.Empty(reopened.Diagnostics);
        AssertQpdfPasses(file);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Removing_an_attachment_from_embedded_files_pdf_updates_the_name_tree(bool incremental)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("embedded-files.pdf"));

        Assert.True(document.RemoveEmbeddedFile("hello.txt"));

        byte[] file = Save(document, new PdfSaveOptions().WithIncrementalUpdate(incremental));
        using PdfDocument reopened = PdfDocument.Open(file);
        Assert.Equal(["data.csv"], reopened.EmbeddedFiles.Select(entry => entry.Name));
        PdfNameTree tree = reopened.Names!.EmbeddedFiles!;
        Assert.False(tree.ContainsKey(new CosString("hello.txt"u8)));
        Assert.Equal(["data.csv"], tree.Select(entry => entry.Key.DecodeText()));
        Assert.Empty(reopened.Diagnostics);
        AssertQpdfPasses(file);
    }

    [Fact]
    public void A_removed_attachment_s_bytes_leave_the_file_when_the_save_drops_unreachable_objects()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("embedded-files.pdf"));
        document.RemoveEmbeddedFile("hello.txt");

        byte[] kept = Save(document, new PdfSaveOptions().WithStreamCompression(PdfStreamCompression.None));
        byte[] collected = Save(document, new PdfSaveOptions().WithStreamCompression(PdfStreamCompression.None).WithRemoveUnreachableObjects(true));

        Assert.True(kept.AsSpan().IndexOf("Hello, world!"u8) >= 0);
        Assert.True(collected.AsSpan().IndexOf("Hello, world!"u8) < 0);
        using PdfDocument reopened = PdfDocument.Open(collected);
        Assert.Equal(["data.csv"], reopened.EmbeddedFiles.Select(entry => entry.Name));
    }

    [Fact]
    public void Removing_a_name_the_tree_does_not_hold_changes_nothing()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("embedded-files.pdf"));
        using PdfDocument empty = PdfDocument.Create();

        Assert.False(document.RemoveEmbeddedFile("missing.txt"));
        Assert.False(empty.RemoveEmbeddedFile("hello.txt"));
        Assert.Equal(["data.csv", "hello.txt"], document.EmbeddedFiles.Select(entry => entry.Name));
    }

    [Fact]
    public void An_attachment_added_to_embedded_files_pdf_joins_the_tree_in_key_order_and_an_existing_name_is_replaced()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("embedded-files.pdf"));

        document.AddEmbeddedFile("chart.png", [0x89, 0x50, 0x4E, 0x47], new PdfEmbeddedFileOptions().WithMediaType("image/png"));
        document.AddEmbeddedFile("hello.txt", "Goodbye."u8);

        byte[] file = Save(document);
        using PdfDocument reopened = PdfDocument.Open(file);
        IReadOnlyList<PdfEmbeddedFileEntry> files = reopened.EmbeddedFiles;
        Assert.Equal(["chart.png", "data.csv", "hello.txt"], files.Select(entry => entry.Name));
        Assert.Equal("image/png", files[0].File.EmbeddedFile!.MediaType);
        Assert.Equal("Goodbye."u8.ToArray(), files[2].File.EmbeddedFile!.Decode().ToArray());
        Assert.Equal("application/octet-stream", files[2].File.EmbeddedFile!.Subtype);
        Assert.Empty(reopened.Diagnostics);
        AssertQpdfPasses(file);
    }

    [Fact]
    public void An_associated_file_with_relationship_Source_re_reads_on_the_page()
    {
        byte[] source = "<doc>source</doc>"u8.ToArray();
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"));
        PdfFileSpecification created = document.CreateEmbeddedFile(
            "source.xml",
            source,
            new PdfEmbeddedFileOptions().WithMediaType("application/xml").WithModificationDate(Modified).WithRelationship(PdfFileRelationship.Source));

        document.AddAssociatedFile(document.Pages[0].Dictionary, created);

        byte[] file = Save(document);
        using PdfDocument reopened = PdfDocument.Open(file);
        PdfFileSpecification associated = Assert.Single(reopened.Pages[0].AssociatedFiles);
        Assert.Equal(PdfFileRelationship.Source, associated.Relationship);
        Assert.Equal("source.xml", associated.FileName);
        Assert.Equal("application/xml", associated.EmbeddedFile!.Subtype);
        Assert.Equal(source, associated.EmbeddedFile.Decode().ToArray());
        Assert.Equal(Modified, associated.EmbeddedFile.Parameters!.ModificationDate!.Value.Value);
        PdfAssociatedFile found = Assert.Single(reopened.EnumerateAssociatedFiles());
        Assert.Equal((PdfAssociatedFileLocation.Page, 0), (found.Location, found.PageIndex));
        Assert.Empty(reopened.EmbeddedFiles);
        Assert.Empty(reopened.Diagnostics);
        AssertQpdfPasses(file);
    }

    [Fact]
    public void One_file_associated_with_the_catalog_and_an_annotation_and_listed_as_an_attachment_is_one_object()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-subtypes.pdf"));
        PdfFileSpecification created = document.CreateEmbeddedFile(
            "data.csv", "a,b\n"u8, new PdfEmbeddedFileOptions().WithMediaType("text/csv").WithModificationDate(Modified).WithRelationship(PdfFileRelationship.Data));
        CosDictionary annotation = document.Pages[0].Annotations[0].Dictionary;

        document.AddAssociatedFile(document.Catalog, created);
        document.AddAssociatedFile(annotation, created);
        document.AddAssociatedFile(annotation, created);
        document.AddEmbeddedFile("data.csv", created);

        byte[] file = Save(document, new PdfSaveOptions().WithIncrementalUpdate(true));
        using PdfDocument reopened = PdfDocument.Open(file);
        PdfFileSpecification catalogFile = Assert.Single(reopened.AssociatedFiles);
        PdfFileSpecification annotationFile = Assert.Single(reopened.ReadAssociatedFiles(reopened.Pages[0].Annotations[0].Dictionary));
        PdfEmbeddedFileEntry attachment = Assert.Single(reopened.EmbeddedFiles);
        Assert.NotNull(catalogFile.Reference);
        Assert.Equal(catalogFile.Reference, annotationFile.Reference);
        Assert.Same(catalogFile.Dictionary, attachment.File.Dictionary);
        Assert.Equal(PdfFileRelationship.Data, catalogFile.Relationship);
        Assert.Equal(
            [PdfAssociatedFileLocation.Catalog, PdfAssociatedFileLocation.Annotation],
            reopened.EnumerateAssociatedFiles().Where(associated => Equals(associated.File.Reference, catalogFile.Reference)).Select(associated => associated.Location));
        Assert.Empty(reopened.Diagnostics);
        AssertQpdfPasses(file);
    }

    [Fact]
    public void Removing_an_associated_file_removes_it_from_the_owner_and_an_emptied_array_with_it()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("associated-files.pdf"));
        CosDictionary page = document.Pages[0].Dictionary;
        PdfFileSpecification pageFile = Assert.Single(document.Pages[0].AssociatedFiles);
        PdfFileSpecification other = document.CreateEmbeddedFile("x.txt", "x"u8);

        Assert.False(document.RemoveAssociatedFile(page, other));
        Assert.False(document.RemoveAssociatedFile(document.Pages[0].Annotations[0].Dictionary, pageFile));
        Assert.True(document.RemoveAssociatedFile(page, pageFile));

        Assert.False(page.ContainsKey(new CosName("AF")));
        Assert.Empty(document.Pages[0].AssociatedFiles);
        Assert.Equal("source.txt", Assert.Single(document.AssociatedFiles).FileName);
    }

    [Fact]
    public void A_single_dictionary_AF_entry_becomes_an_array_when_a_file_is_added()
    {
        using PdfDocument document = PdfDocument.Open(TestPdf.OnePage(
            "", "/AF << /Type /Filespec /F (old.txt) /AFRelationship /Source >>"));
        PdfFileSpecification added = document.CreateEmbeddedFile("new.txt", "n"u8, new PdfEmbeddedFileOptions().WithModificationDate(Modified));

        document.AddAssociatedFile(document.Catalog, added);

        Assert.IsType<CosArray>(document.Catalog[new CosName("AF")]);
        Assert.Equal(["old.txt", "new.txt"], document.AssociatedFiles.Select(file => file.FileName));
    }

    [Fact]
    public void A_name_outside_PDFDocEncoding_is_written_exactly_in_UF_and_approximated_in_F()
    {
        using PdfDocument document = PdfDocument.Create();

        PdfFileSpecification created = document.CreateEmbeddedFile("報告/2024.txt", "r"u8);

        Assert.Equal("報告\\/2024.txt", created.FileName);
        Assert.Equal(["報告/2024.txt"], created.PathComponents);
        Assert.Equal("__\\/2024.txt", ((CosString)created.Dictionary![new CosName("F")]).DecodeText());
        Assert.Equal(PdfCheckSumStatus.Matches, created.EmbeddedFile!.VerifyCheckSum());
    }

    [Fact]
    public void Arguments_the_spec_does_not_allow_are_refused()
    {
        using PdfDocument document = PdfDocument.Create();
        using PdfDocument other = PdfDocument.Create();
        PdfFileSpecification foreign = other.CreateEmbeddedFile("a.txt", "a"u8);
        PdfFileSpecification text = document.GetFileSpecification(new CosString("a.txt"u8))!;

        Assert.Throws<ArgumentException>(() => document.CreateEmbeddedFile("", "a"u8));
        Assert.Throws<ArgumentException>(() => new PdfEmbeddedFileOptions().WithMediaType("text"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfEmbeddedFileOptions().WithRelationship(PdfFileRelationship.Other));
        Assert.Throws<ArgumentException>(() => document.AddEmbeddedFile("a.txt", foreign));
        Assert.Throws<ArgumentException>(() => document.AddEmbeddedFile("a.txt", text));
        Assert.Throws<ArgumentException>(() => document.AddAssociatedFile(document.Catalog, text));
        Assert.Throws<ArgumentException>(() => document.AddAssociatedFile(document.Catalog, foreign));
    }

    private static byte[] Save(PdfDocument document, PdfSaveOptions? options = null)
    {
        using var output = new MemoryStream();
        document.Save(output, options ?? new PdfSaveOptions());
        return output.ToArray();
    }

    private static void AssertQpdfPasses(byte[] file)
    {
        (int exitCode, string output) = ExternalTool.Run("qpdf", file, "--check");
        Assert.True(exitCode == 0, output);
    }
}
