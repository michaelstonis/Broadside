using Broadside.TestSupport;
using Microsoft.Maui.Controls;

namespace Broadside.Viewer.Maui.Tests;

/// <summary>The side panels (#636) bound to a real <see cref="PdfView"/> without a platform: the core models laid out with MAUI controls.</summary>
[Collection(HeavyTestCollection.Name)] // installs a process-wide dispatcher for its view: nothing runs beside it
public sealed class SidebarPanelTests
{
    [Fact]
    public void The_layers_panel_switches_a_layer_through_the_session() => UiThread.Run(async () =>
    {
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes("optional-content-pages.pdf"), Shell.Options());
        using PdfView view = await PdfToolbarTests.ShowAsync(session);
        var panel = new PdfLayersPanel { Viewer = view };
        await panel.Sidebar!.Loading;

        List<Switch> switches = [.. Find<Switch>(panel)];
        Assert.Equal(4, switches.Count); // Base, Notes, Draft (locked), Unused
        Assert.False(switches[2].IsEnabled);
        switches[0].IsToggled = false;

        Assert.NotNull(session.OptionalContentState);
        await PdfToolbarTests.SettleAsync(view);
        ((IButtonController)Find<Button>(panel).Single(b => b.AutomationId == "layers-reset")).SendClicked();
        Assert.Null(session.OptionalContentState);
        panel.Viewer = null;
        Assert.Null(panel.Sidebar);
    });

    [Fact]
    public void The_attachments_panel_hands_the_bytes_to_the_app() => UiThread.Run(async () =>
    {
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes("embedded-files.pdf"), Shell.Options());
        using PdfView view = await PdfToolbarTests.ShowAsync(session);
        var panel = new PdfAttachmentsPanel { Viewer = view };
        await panel.Sidebar!.Loading;
        var opened = new TaskCompletionSource<AttachmentOpeningEventArgs>();
        panel.Opening += (_, e) => opened.TrySetResult(e);

        Assert.Equal(2, panel.Count);
        ((IButtonController)Find<Button>(panel).First(b => b.AutomationId == "attachment-save")).SendClicked();

        AttachmentOpeningEventArgs args = await opened.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal((AttachmentAction.Save, "dätä.csv"), (args.Action, args.Item.FileName));
    });

    [Fact]
    public void The_sidebar_binds_every_panel_and_switches_tabs()
    {
        using var view = new PdfView();
        var sidebar = new PdfSidebar { Viewer = view };
        Assert.Same(view, sidebar.Layers.Viewer);
        Assert.Same(view, sidebar.Annotations.Viewer);
        Assert.Equal(0, sidebar.SelectedIndex);

        sidebar.SelectedIndex = 4;
        Assert.Equal(4, sidebar.SelectedIndex);
        Assert.Contains(sidebar.Layers, Find<ContentView>(sidebar));
        sidebar.Viewer = null;
        Assert.Null(sidebar.Attachments.Viewer);
    }

    private static IEnumerable<T> Find<T>(Element root)
        where T : Element =>
        ((Microsoft.Maui.IVisualTreeElement)root).GetVisualTreeDescendants().OfType<T>();
}
