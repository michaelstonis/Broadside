using System.Text;
using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

public sealed class DocumentAttachmentsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Embedded_files_list_in_tree_order_with_their_parameters_and_extract()
    {
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes("embedded-files.pdf"));
        DocumentAttachments attachments = await DocumentAttachments.ReadAsync(session, Token);

        Assert.True(attachments.IsComplete);
        Assert.Equal(["dätä.csv", "hello.txt"], attachments.Items.Select(i => i.FileName));
        AttachmentItem csv = attachments.Items[0], hello = attachments.Items[1];
        Assert.Equal(("Comma-separated data", "text/csv", (long?)26, (int?)null), (csv.Description, csv.MediaType, csv.Size, csv.PageIndex));
        Assert.Equal((PdfCheckSumStatus.Matches, "text/plain", (long?)13), (hello.CheckSum, hello.MediaType, hello.Size));
        Assert.True(hello.IsEmbedded);
        Assert.NotNull(hello.Specification);

        Assert.Equal("Hello, world!", Encoding.ASCII.GetString((await attachments.ExtractAsync(hello, Token)).Span));
        Assert.Equal("name,value\nalpha,1\nbeta,2\n", Encoding.ASCII.GetString((await attachments.ExtractAsync(csv, Token)).Span));
    }

    [Fact]
    public async Task File_attachment_annotations_list_their_page_and_with_a_layer_their_target()
    {
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes("annotations-subtypes.pdf"));
        var layer = new AnnotationLayer(session);

        AttachmentItem plain = Assert.Single((await DocumentAttachments.ReadAsync(session, Token)).Items);
        Assert.Equal(("a.txt", (int?)0, (AnnotationTarget?)null), (plain.FileName, plain.PageIndex, plain.Annotation));

        DocumentAttachments attachments = await DocumentAttachments.ReadAsync(layer, Token);
        AttachmentItem item = Assert.Single(attachments.Items);
        AnnotationTarget target = Assert.NotNull(item.Annotation);
        Assert.IsType<Annotations.PdfFileAttachmentAnnotation>(layer.GetAnnotation(target));
        Assert.Equal("attached", Encoding.ASCII.GetString((await attachments.ExtractAsync(item, Token)).Span));
    }

    [Fact]
    public async Task A_bad_checksum_and_a_missing_stream_still_list_with_their_status()
    {
        await using DocumentSession session = DocumentSession.Open(PanelSamples.Attachments());
        DocumentAttachments attachments = await DocumentAttachments.ReadAsync(session, Token);

        Assert.Equal(
            [("bad.txt", PdfCheckSumStatus.DoesNotMatch, true), ("good.txt", PdfCheckSumStatus.Matches, true), ("missing.txt", PdfCheckSumStatus.Absent, false), ("clip.txt", PdfCheckSumStatus.Absent, true)],
            attachments.Items.Select(i => (i.FileName, i.CheckSum, i.IsEmbedded)));
        Assert.Equal("A good file", attachments.Items[1].Description);
        Assert.True((await attachments.ExtractAsync(attachments.Items[2], Token)).IsEmpty);
        Assert.Equal("bad data", Encoding.ASCII.GetString((await attachments.ExtractAsync(attachments.Items[0], Token)).Span));
        Assert.Contains(session.Document.Diagnostics, d => d.Code.Contains("EmbeddedFileMissing", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_document_without_attachments_lists_none()
    {
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes("optional-content.pdf"));
        DocumentAttachments attachments = await DocumentAttachments.ReadAsync(session, Token);
        Assert.True(attachments.IsEmpty);
    }

    [Fact]
    public void The_opening_event_carries_the_bytes_and_the_action()
    {
        var item = new AttachmentItem("a.txt", null, null, 3, null, null, PdfCheckSumStatus.Absent);
        var args = new AttachmentOpeningEventArgs(item, new byte[] { 1, 2, 3 }, AttachmentAction.Save);
        Assert.Equal((item, 3, AttachmentAction.Save), (args.Item, args.Data.Length, args.Action));
    }
}
