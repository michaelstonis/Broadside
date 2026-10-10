using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Files;

/// <summary>Associated files at every location ISO 32000-2 §14.13 and PDF 2.0 Application Note 002 list.</summary>
public sealed class AssociatedFileTests
{
    [Fact]
    public void Enumeration_finds_each_associated_file_with_its_location()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("associated-files.pdf"));

        var found = document.EnumerateAssociatedFiles().Select(file => (file.Location, file.File.FileName, file.File.Relationship)).ToList();

        Assert.Equal(
            [
                (PdfAssociatedFileLocation.Catalog, "source.txt", PdfFileRelationship.Source),
                (PdfAssociatedFileLocation.MetadataStream, "schema.xsd", PdfFileRelationship.Schema),
                (PdfAssociatedFileLocation.StructureTreeRoot, "tree.txt", PdfFileRelationship.Alternative),
                (PdfAssociatedFileLocation.StructureElement, "element.txt", PdfFileRelationship.Alternative),
                (PdfAssociatedFileLocation.DocumentPart, "part.txt", PdfFileRelationship.Source),
                (PdfAssociatedFileLocation.Page, "page-data.csv", PdfFileRelationship.Data),
                (PdfAssociatedFileLocation.FormXObject, "equation.mml", PdfFileRelationship.Supplement),
                (PdfAssociatedFileLocation.ImageXObject, "image-data.csv", PdfFileRelationship.Data),
                (PdfAssociatedFileLocation.MarkedContent, "marked.csv", PdfFileRelationship.Data),
                (PdfAssociatedFileLocation.MarkedContent, "marked-legacy.txt", PdfFileRelationship.Supplement),
                (PdfAssociatedFileLocation.Annotation, "annotation.txt", PdfFileRelationship.Unspecified),
            ],
            found);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Each_associated_file_knows_its_owner_and_page()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("associated-files.pdf"));

        List<PdfAssociatedFile> files = [.. document.EnumerateAssociatedFiles()];

        PdfAssociatedFile form = files.Single(file => file.Location == PdfAssociatedFileLocation.FormXObject);
        Assert.Equal(new CosReference(11, 0), form.OwnerReference);
        Assert.IsType<CosStream>(form.Owner);
        Assert.Equal(0, form.PageIndex);
        Assert.Equal("application/mathml+xml", form.File.EmbeddedFile!.Subtype);
        Assert.Null(files.Single(file => file.Location == PdfAssociatedFileLocation.Catalog).PageIndex);
        Assert.Equal("MF1", files.First(file => file.Location == PdfAssociatedFileLocation.MarkedContent).PropertyName);
    }

    [Fact]
    public void A_deep_scan_also_finds_associated_files_in_unlisted_places()
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>",
            "<< /Type /Custom /AF [5 0 R] >>",
            "<< /Type /Filespec /F (hidden.txt) /AFRelationship /Data >>");
        using PdfDocument document = PdfDocument.Open(file);

        Assert.Empty(document.EnumerateAssociatedFiles());
        PdfAssociatedFile found = Assert.Single(document.EnumerateAssociatedFiles(deep: true));
        Assert.Equal(PdfAssociatedFileLocation.Other, found.Location);
        Assert.Equal(new CosReference(4, 0), found.OwnerReference);
        Assert.Equal("hidden.txt", found.File.FileName);
    }

    [Fact]
    public void Catalog_and_page_accessors_read_AF_directly()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("associated-files.pdf"));

        Assert.Equal("source.txt", Assert.Single(document.AssociatedFiles).FileName);
        Assert.Equal("page-data.csv", Assert.Single(document.Pages[0].AssociatedFiles).FileName);
        var annotation = (CosDictionary)document.Resolve(new CosReference(5, 0));
        Assert.Equal("annotation.txt", Assert.Single(document.ReadAssociatedFiles(annotation)).FileName);
    }

    [Fact]
    public void A_marked_content_property_list_gives_its_files_from_MCAF_or_a_bare_array()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("associated-files.pdf"));
        var properties = (CosDictionary)document.Resolve(document.Pages[0].Resources![new CosName("Properties")]);

        Assert.Equal("marked.csv", Assert.Single(document.ReadMarkedContentAssociatedFiles(properties[new CosName("MF1")])).FileName);
        Assert.Equal("marked-legacy.txt", Assert.Single(document.ReadMarkedContentAssociatedFiles(properties[new CosName("MF2")])).FileName);
    }

    [Theory]
    [MemberData(nameof(MalformedAssociatedFiles))]
    public void A_malformed_AF_entry_is_repaired_with_a_diagnostic(string pageEntries, string[] extraObjects, string expectedCode, int expectedFiles)
    {
        using PdfDocument document = PdfDocument.Open(new TestPdf().Build(
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] {pageEntries} >>",
            .. extraObjects,
        ]));

        Assert.Equal(expectedFiles, document.Pages[0].AssociatedFiles.Count);
        Assert.Contains(expectedCode, document.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    public static TheoryData<string, string[], string, int> MalformedAssociatedFiles => new()
    {
        { "/AF 4 0 R", ["<< /Type /Filespec /F (single.txt) >>"], "AssociatedFilesInvalid", 1 },
        { "/AF [(plain.txt)]", [], "AssociatedFilesInvalid", 1 },
        { "/AF [42]", [], "AssociatedFilesInvalid", 0 },
        { "/AF [4 0 R]", ["<< /Type /Filespec /F (a.txt) /EF << /F 5 0 R >> >>", "<< /Params << /ModDate (D:2024) >> /Length 1 >>\nstream\nx\nendstream"], "EmbeddedFileSubtypeMissing", 1 },
        { "/AF [4 0 R]", ["<< /Type /Filespec /F (a.txt) /EF << /F 5 0 R >> >>", "<< /Subtype /text#2Fplain /Params << /Size 1 >> /Length 1 >>\nstream\nx\nendstream"], "EmbeddedFileParamsInvalid", 1 },
    };

    [Fact]
    public void Strict_mode_throws_for_an_AF_entry_that_is_not_an_array()
    {
        using PdfDocument document = PdfDocument.Open(
            TestPdf.OnePage("/MediaBox [0 0 612 792] /AF << /F (x.txt) >>"),
            new PdfOptions().UseStrict());

        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => document.Pages[0].AssociatedFiles);
        Assert.Equal("AssociatedFilesInvalid", exception.Diagnostic.Code);
    }

    [Fact]
    public void Reading_associated_files_leaves_every_object_clean()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("associated-files.pdf"));

        foreach (PdfAssociatedFile file in document.EnumerateAssociatedFiles(deep: true))
        {
            _ = (file.File.FileName, file.File.EmbeddedFile?.Parameters?.ModificationDate);
        }

        for (int number = 1; number <= 32; number++)
        {
            Assert.False(document.Resolve(new CosReference(number, 0)).IsDirty);
        }
    }
}
