using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Broadside.TestSupport;

namespace Broadside.Viewer.Wpf.Tests;

/// <summary>The side panels (#636) bound to a real <see cref="PdfView"/> on an STA thread: the core's sidebar model laid out with WPF controls.</summary>
public sealed class SidebarPanelTests
{
    [Fact]
    public void The_layers_panel_hides_a_layer_in_the_view_and_reset_shows_it_again() => Sta.Run(async () =>
    {
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes("optional-content-pages.pdf"), Shell.Options());
        using PdfView view = await PdfToolbarTests.ShowAsync(session);
        using var panel = new PdfLayersPanel { Viewer = view };
        await panel.Sidebar!.Loading;
        List<CheckBox> boxes = [.. Find<CheckBox>(panel, "layer")];
        Assert.Equal(4, boxes.Count);
        Assert.False(boxes[2].IsEnabled); // Draft is locked

        boxes[0].IsChecked = false;
        await PdfToolbarTests.SettleAsync(view);
        Assert.NotNull(session.OptionalContentState);

        Find<Button>(panel, "layers-reset").Single().RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Null(session.OptionalContentState);
    });

    [Fact]
    public void The_thumbnails_panel_shows_a_page_on_click() => Sta.Run(async () =>
    {
        await using DocumentSession session = DocumentSession.Open(ManyPagesDocument.Create(4), Shell.Options());
        using PdfView view = await PdfToolbarTests.ShowAsync(session);
        using var panel = new PdfThumbnailsPanel { Viewer = view };
        await panel.Sidebar!.Loading;
        Assert.Equal(4, panel.PageCount);

        Find<Button>(panel, "thumbnail").ElementAt(2).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await PdfToolbarTests.SettleAsync(view);
        Assert.Equal(2, view.State.PageIndex);
    });

    [Fact]
    public void The_sidebar_hands_an_attachment_to_the_app() => Sta.Run(async () =>
    {
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes("embedded-files.pdf"), Shell.Options());
        using PdfView view = await PdfToolbarTests.ShowAsync(session);
        using var sidebar = new PdfSidebar { Viewer = view, SelectedPanel = SidebarPanels.Attachments };
        await sidebar.Sidebar!.Loading;
        var opened = new TaskCompletionSource<AttachmentOpeningEventArgs>();
        sidebar.AttachmentOpening += (_, e) => opened.TrySetResult(e);

        Find<Button>(sidebar, "attachment-save").First().RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        AttachmentOpeningEventArgs args = await opened.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal((AttachmentAction.Save, "dätä.csv"), (args.Action, args.Item.FileName));
    });

    private static IEnumerable<T> Find<T>(DependencyObject root, string automationId)
        where T : DependencyObject
    {
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject element)
            {
                continue;
            }

            if (element is T match && AutomationProperties.GetAutomationId(element) == automationId)
            {
                yield return match;
            }

            foreach (T inner in Find<T>(element, automationId))
            {
                yield return inner;
            }
        }
    }
}
