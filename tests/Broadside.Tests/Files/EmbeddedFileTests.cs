using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Files;

/// <summary>The EmbeddedFiles name tree and the embedded files it lists. ISO 32000-2 §7.7.4, §7.11.3, §7.11.4.</summary>
public class EmbeddedFileTests
{
    [Fact]
    public void The_EmbeddedFiles_name_tree_lists_each_file_with_its_names_parameters_and_data()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("embedded-files.pdf"));

        IReadOnlyList<PdfEmbeddedFileEntry> files = document.EmbeddedFiles;

        Assert.Equal(["data.csv", "hello.txt"], files.Select(entry => entry.Name));
        PdfFileSpecification data = files[0].File;
        Assert.Equal("dätä.csv", data.FileName);
        Assert.Equal("Comma-separated data", data.Description);
        Assert.Equal("text/csv", data.EmbeddedFile!.Subtype);
        Assert.Equal("name,value\nalpha,1\nbeta,2\n"u8.ToArray(), data.EmbeddedFile.Decode().ToArray());
        Assert.Equal(data.EmbeddedFile.Decode().Length, data.EmbeddedFile.Parameters!.Size);
        PdfRelatedFile related = Assert.Single(data.RelatedFiles);
        Assert.Equal("data.schema", related.Name);
        Assert.Equal("name:text,value:int"u8.ToArray(), related.File.Decode().ToArray());

        PdfEmbeddedFile hello = files[1].File.EmbeddedFile!;
        Assert.Equal("hello.txt", files[1].File.FileName);
        Assert.Equal("text/plain", hello.MediaType);
        Assert.Equal("Hello, world!"u8.ToArray(), hello.Decode().ToArray());
        PdfEmbeddedFileParameters parameters = hello.Parameters!;
        Assert.Equal(13, parameters.Size);
        Assert.Equal(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero), parameters.CreationDate!.Value.Value);
        Assert.Equal(new DateTimeOffset(2024, 6, 7, 8, 9, 10, TimeSpan.FromHours(2)), parameters.ModificationDate!.Value.Value);
        Assert.Equal(PdfCheckSumStatus.Matches, hello.VerifyCheckSum());
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_name_dictionary_exposes_the_EmbeddedFiles_tree()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("embedded-files.pdf"));

        PdfNameTree tree = document.Names!.EmbeddedFiles!;

        Assert.True(tree.TryGetValue("hello.txt", out CosObject? value));
        Assert.Equal("hello.txt", document.GetFileSpecification(value)!.FileName);
    }

    [Fact]
    public void A_document_without_embedded_files_lists_none()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"));

        Assert.Empty(document.EmbeddedFiles);
    }

    [Fact]
    public void An_entry_that_is_not_a_file_specification_is_skipped_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(TestPdf.OnePage(
            "", "/Names << /EmbeddedFiles << /Names [(a.txt) 42 (b.txt) << /F (b.txt) >>] >> >>"));

        PdfEmbeddedFileEntry entry = Assert.Single(document.EmbeddedFiles);

        Assert.Equal("b.txt", entry.Name);
        Assert.Equal(["FileSpecificationInvalid"], document.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    [Fact]
    public void A_folder_prefix_in_the_key_gives_the_folder_id_and_the_bare_file_name()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("collection-portfolio.pdf"));

        IReadOnlyList<PdfEmbeddedFileEntry> files = document.EmbeddedFiles;

        Assert.Equal(["<1>report.txt", "notes.txt"], files.Select(entry => entry.Name));
        Assert.Equal([(int?)1, null], files.Select(entry => entry.FolderId));
        Assert.Equal(["report.txt", "notes.txt"], files.Select(entry => entry.FileName));
    }
}
