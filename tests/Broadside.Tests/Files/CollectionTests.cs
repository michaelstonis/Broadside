using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Files;

/// <summary>Portable collections (portfolios): schema, sort, colours, split, folders and collection items. ISO 32000-2 §12.3.5, §7.11.6.</summary>
public sealed class CollectionTests
{
    private static PdfDocument OpenCorpus() => PdfDocument.Open(Corpus.Path("collection-portfolio.pdf"));

    [Fact]
    public void The_collection_exposes_view_initial_document_and_schema_fields()
    {
        using PdfDocument document = OpenCorpus();

        PdfCollection collection = document.Collection!;

        Assert.Equal(PdfCollectionView.Details, collection.View);
        Assert.Equal("notes.txt", collection.InitialDocument);
        Assert.Equal(["name", "date", "pages", "fname", "size"], collection.Schema.Select(field => field.Key));
        Assert.Equal(
            [PdfCollectionFieldType.Text, PdfCollectionFieldType.Date, PdfCollectionFieldType.Number, PdfCollectionFieldType.FileName, PdfCollectionFieldType.Size],
            collection.Schema.Select(field => field.Type));
        Assert.Equal(["Name", "Date", "Pages", "File", "Size"], collection.Schema.Select(field => field.Name));
        Assert.Equal([0, 1, 2, 3, 4], collection.Schema.Select(field => field.Order ?? -1));
        Assert.Equal([true, true, false, true, true], collection.Schema.Select(field => field.IsVisible));
        Assert.Equal([false, false, false, true, false], collection.Schema.Select(field => field.IsEditable));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Sort_pads_its_ascending_flags_with_true()
    {
        using PdfDocument document = OpenCorpus();

        PdfCollectionSort sort = document.Collection!.Sort!;

        Assert.Equal(["date", "name"], sort.Keys);
        Assert.Equal([false, true], sort.Ascending);
    }

    [Fact]
    public void Colors_and_split_are_typed()
    {
        using PdfDocument document = OpenCorpus();
        PdfCollection collection = document.Collection!;

        Assert.Equal(new PdfRgbColor(1, 1, 1), collection.Colors!.Background);
        Assert.Equal(new PdfRgbColor(0.9, 0.9, 0.9), collection.Colors.CardBackground);
        Assert.Equal(new PdfRgbColor(0, 0, 0), collection.Colors.CardBorder);
        Assert.Equal(new PdfRgbColor(0.5, 0.5, 0.5), collection.Colors.SecondaryText);
        Assert.Equal(PdfCollectionSplitDirection.Vertical, collection.Split.Direction);
        Assert.Equal(30, collection.Split.Position);
    }

    [Fact]
    public void Folders_form_a_tree_and_files_are_placed_by_their_key_prefix()
    {
        using PdfDocument document = OpenCorpus();
        PdfCollection collection = document.Collection!;

        PdfCollectionFolder root = collection.RootFolder!;

        Assert.Equal(0, root.Id);
        Assert.Equal("Portfolio", root.Name);
        Assert.Null(root.Parent);
        Assert.Equal([new PdfCollectionIdRange(2, 10)], root.FreeIds);
        PdfCollectionFolder reports = Assert.Single(root.Children);
        Assert.Equal((1, "Reports", "Quarterly reports"), (reports.Id, reports.Name, reports.Description));
        Assert.Same(root, reports.Parent);
        Assert.Same(reports.Dictionary, collection.FindFolder(1)!.Dictionary);
        IReadOnlyList<PdfEmbeddedFileEntry> files = document.EmbeddedFiles;
        Assert.Same(reports.Dictionary, collection.GetFolder(files[0])!.Dictionary);
        Assert.Same(root.Dictionary, collection.GetFolder(files[1])!.Dictionary);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Collection_items_give_each_files_values_with_subitem_prefixes()
    {
        using PdfDocument document = OpenCorpus();
        IReadOnlyList<PdfEmbeddedFileEntry> files = document.EmbeddedFiles;

        PdfCollectionItem report = files[0].File.CollectionItem!;

        Assert.Equal(["name", "date", "pages"], report.Keys);
        Assert.Equal("Quarterly report", report.GetValue("name")!.Text);
        Assert.Equal("Q1: ", report.GetValue("name")!.Prefix);
        Assert.Equal(new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero), report.GetValue("date")!.Date!.Value.Value);
        Assert.Equal(3, report.GetValue("pages")!.Number);
        Assert.Null(report.GetValue("size"));
        Assert.Equal("Notes", files[1].File.CollectionItem!.GetValue("name")!.Text);
    }

    [Fact]
    public void A_document_without_a_collection_has_none()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("embedded-files.pdf"));

        Assert.Null(document.Collection);
    }

    [Fact]
    public void A_navigator_view_without_a_navigator_reads_as_details_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(TestPdf.OnePage("", "/Collection << /Type /Collection /View /C >>"));

        PdfCollection collection = document.Collection!;

        Assert.Equal(PdfCollectionView.Details, collection.View);
        Assert.Equal(PdfCollectionSplitDirection.Horizontal, collection.Split.Direction);
        Assert.Null(collection.Split.Position);
        Assert.Equal(["CollectionInvalid"], document.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    [Fact]
    public void Split_defaults_follow_the_view()
    {
        using PdfDocument tile = PdfDocument.Open(TestPdf.OnePage("", "/Collection << /View /T >>"));
        using PdfDocument hidden = PdfDocument.Open(TestPdf.OnePage("", "/Collection << /View /H >>"));

        Assert.Equal(PdfCollectionSplitDirection.Vertical, tile.Collection!.Split.Direction);
        Assert.Equal(PdfCollectionSplitDirection.None, hidden.Collection!.Split.Direction);
    }

    [Fact]
    public void A_folder_cycle_and_a_duplicate_id_are_cut_with_diagnostics()
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R /Collection << /Folders 4 0 R >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>",
            "<< /Type /Folder /ID 0 /Name (Root) /Child 5 0 R >>",
            "<< /Type /Folder /ID 1 /Name (A) /Parent 4 0 R /Next 6 0 R /Child 4 0 R >>",
            "<< /Type /Folder /ID 1 /Name (B) /Parent 4 0 R /Next 5 0 R >>");
        using PdfDocument document = PdfDocument.Open(file);

        PdfCollectionFolder root = document.Collection!.RootFolder!;

        Assert.Equal(["A", "B"], root.Children.Select(folder => folder.Name));
        Assert.Empty(root.Children[0].Children);
        Assert.Equal("A", document.Collection.FindFolder(1)!.Name);
        Assert.Equal(["CollectionFolderCycle", "CollectionFolderIdDuplicate"], document.Diagnostics.Select(diagnostic => diagnostic.Code).Distinct().Order());
    }

    [Fact]
    public void A_schema_field_order_written_as_a_whole_real_reads_as_that_integer_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(TestPdf.OnePage("/MediaBox [0 0 612 792]", "/Collection << /Schema << /a << /Subtype /S /N (A) /O 2.0 >> >> >>"));

        PdfCollectionField field = Assert.Single(document.Collection!.Schema);

        Assert.Equal(2, field.Order);
        Assert.Equal(["CollectionInvalid"], document.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    [Theory]
    [InlineData("/Type /Folder", -1, "")]
    [InlineData("/Type /Folder /ID -3 /Name (Root)", -1, "Root")]
    [InlineData("/Type /Folder /ID (zero) /Name (Root)", -1, "Root")]
    [InlineData("/Type /Folder /ID 2.0 /Name /Root", 2, "Root")]
    public void A_missing_or_invalid_folder_id_or_name_is_repaired_with_a_diagnostic(string entries, int id, string name)
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R /Collection << /Folders 4 0 R >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>",
            $"<< {entries} >>");
        using PdfDocument document = PdfDocument.Open(file);
        PdfCollectionFolder root = document.Collection!.RootFolder!;

        (int Id, string Name) read = (root.Id, root.Name);

        Assert.Equal((id, name), read);
        Assert.Equal(["CollectionFolderInvalid 4"], document.Diagnostics.Select(diagnostic => $"{diagnostic.Code} {diagnostic.ObjectReference?.ObjectNumber}").Distinct());
    }
}
