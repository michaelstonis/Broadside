using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

public sealed class ViewerSidebarTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_model_is_read_for_the_open_document_and_again_for_the_next()
    {
        await using DocumentSession first = DocumentSession.Open(Corpus.Bytes("optional-content-pages.pdf"));
        await using DocumentSession second = DocumentSession.Open(Corpus.Bytes("embedded-files.pdf"));
        var viewer = new RecordingViewer(null);
        using var sidebar = new ViewerSidebar(viewer);
        var changes = new List<SidebarChangedEventArgs>();
        sidebar.Changed += (_, e) => changes.Add(e);
        Assert.Null(sidebar.Session);

        using var search = new DocumentSearch(first);
        viewer.Open(first, search, new AnnotationLayer(first));
        await sidebar.Loading;

        Assert.Same(first, sidebar.Session);
        Assert.Equal(SidebarChange.Document, changes[0].Change);
        Assert.NotNull(sidebar.Outline);
        Assert.NotNull(sidebar.SearchResults);
        Assert.Equal(2, sidebar.Layers?.Roots.Count);
        Assert.True(sidebar.Attachments?.IsEmpty);
        Assert.Equal(1, sidebar.Annotations?.Items.Count); // the Square on page 3
        Assert.Equal("1", sidebar.GetPageLabel(0));

        viewer.Open(second);
        await sidebar.Loading;
        Assert.Null(sidebar.SearchResults);
        Assert.Null(sidebar.Annotations);
        Assert.True(sidebar.Layers?.IsEmpty);
        Assert.Equal(2, sidebar.Attachments?.Items.Count);

        sidebar.Dispose();
        Assert.False(viewer.HasViewChangedHandlers);
    }

    [Fact]
    public async Task Only_the_chosen_panels_are_read()
    {
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes("optional-content-pages.pdf"));
        var viewer = new RecordingViewer(session);
        using var sidebar = new ViewerSidebar(viewer, SidebarPanels.Layers);
        await sidebar.Loading;

        Assert.NotNull(sidebar.Layers);
        Assert.Null(sidebar.Outline);
        Assert.Null(sidebar.Attachments);
    }

    [Fact]
    public async Task Thumbnails_are_drawn_nearest_the_page_in_view_and_again_after_a_change_of_layers()
    {
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes("optional-content-pages.pdf"));
        var viewer = new RecordingViewer(session);
        using var sidebar = new ViewerSidebar(viewer, SidebarPanels.Thumbnails | SidebarPanels.Layers, new ThumbnailOptions().WithSize(40));
        await sidebar.Loading;
        var drawn = new List<int>();
        sidebar.Changed += (_, e) =>
        {
            if (e.Change == SidebarChange.Thumbnail)
            {
                drawn.Add(e.PageIndex);
            }
        };
        viewer.GoToPage(2);

        await sidebar.RenderThumbnailsAsync(0, 3, Token);

        Assert.Equal([2, 1, 0], drawn);
        SidebarThumbnail thumbnail = Assert.IsType<SidebarThumbnail>(sidebar.GetThumbnail(0));
        Assert.Equal(40, Math.Max(thumbnail.Width, thumbnail.Height));
        Assert.Equal(thumbnail.Width * thumbnail.Height * 4, thumbnail.Pixels.Length);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], thumbnail.EncodePng()[..4]);

        Assert.True(sidebar.SetLayerVisible(sidebar.Layers!.Roots[0], false));
        Assert.Null(sidebar.GetThumbnail(0));
        sidebar.ResetLayers();
        Assert.Null(session.OptionalContentState);
    }

    [Fact]
    public async Task Navigation_goes_through_the_viewer()
    {
        await using DocumentSession session = DocumentSession.Open(PanelSamples.Comments());
        var layer = new AnnotationLayer(session);
        var viewer = new RecordingViewer(session, annotations: layer);
        using var sidebar = new ViewerSidebar(viewer);
        await sidebar.Loading;

        sidebar.GoToPage(2);
        Assert.Equal(2, viewer.PageIndex);
        sidebar.GoToPage(7); // out of range: ignored
        Assert.Equal(2, viewer.PageIndex);

        sidebar.Reveal(sidebar.Annotations!.Items[2]);
        Assert.Equal((1, new PdfRectangle(100, 100, 200, 200)), viewer.Revealed);
    }

    [Fact]
    public async Task The_outline_opens_and_closes_entries_and_navigates()
    {
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes("outline-full.pdf"));
        var viewer = new RecordingViewer(session);
        using var sidebar = new ViewerSidebar(viewer, SidebarPanels.Outline);
        await sidebar.Loading;
        DocumentOutline outline = Assert.IsType<DocumentOutline>(sidebar.Outline);
        Assert.False(outline.IsEmpty);

        OutlineEntry parent = outline.Entries.First(e => e.Children.Count > 0);
        bool open = sidebar.IsOpen(parent);
        int rows = sidebar.GetVisibleOutline().Count;
        sidebar.Toggle(parent);
        Assert.NotEqual(open, sidebar.IsOpen(parent));
        Assert.NotEqual(rows, sidebar.GetVisibleOutline().Count);

        OutlineEntry navigable = outline.Entries.First(e => e.Target is not null);
        Assert.True(sidebar.GoTo(navigable));
        Assert.Equal(navigable.Target, viewer.Target);
    }

    [Fact]
    public async Task Opening_an_attachment_raises_the_event_with_its_bytes_and_never_for_a_missing_stream()
    {
        await using DocumentSession session = DocumentSession.Open(PanelSamples.Attachments());
        var viewer = new RecordingViewer(session);
        using var sidebar = new ViewerSidebar(viewer, SidebarPanels.Attachments);
        await sidebar.Loading;
        var opened = new List<AttachmentOpeningEventArgs>();
        sidebar.AttachmentOpening += (_, e) => opened.Add(e);
        IReadOnlyList<AttachmentItem> items = sidebar.Attachments!.Items;

        Assert.True(await sidebar.OpenAttachmentAsync(items[1], AttachmentAction.Save, Token));
        Assert.False(await sidebar.OpenAttachmentAsync(items[2], AttachmentAction.Open, Token));

        AttachmentOpeningEventArgs args = Assert.Single(opened);
        Assert.Equal(("good.txt", AttachmentAction.Save, "good data"), (args.Item.FileName, args.Action, System.Text.Encoding.ASCII.GetString(args.Data.Span)));
        Assert.True(sidebar.Reveal(items[3])); // the annotation's page
        Assert.False(sidebar.Reveal(items[0]));
    }
}
