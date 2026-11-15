using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Viewer.Wpf.Tests;

/// <summary>The WPF control itself on an STA thread: its drawing and its UI Automation peers (#222's acceptance: page text in reading order).</summary>
public sealed class PdfViewTests
{
    [Fact]
    public void The_automation_peers_expose_each_loaded_page_with_its_text_in_reading_order() => Sta.Run(async () =>
    {
        await using DocumentSession session = DocumentSession.Open(Shell.CorpusFile("tagged-structure.pdf"), Shell.Options());
        using PdfView view = await ShowAsync(session);

        AutomationPeer peer = UIElementAutomationPeer.CreatePeerForElement(view);
        Assert.Equal(AutomationControlType.Document, peer.GetAutomationControlType());
        AutomationPeer page = Assert.Single(peer.GetChildren(), child => child.GetName() == "Page 1");
        Assert.Equal("page", page.GetLocalizedControlType());
        Assert.Equal("Page1", page.GetAutomationId());
        var value = (IValueProvider)page.GetPattern(PatternInterface.Value);
        Assert.Equal(ReadingOrderTests.TaggedStructureText, value.Value);
        Assert.True(value.IsReadOnly);

        // The tree under the page mirrors the structure in reading order: a section holding a level-1 heading, a paragraph, a list and a table.
        AutomationPeer section = Assert.Single(page.GetChildren());
        List<AutomationPeer> blocks = section.GetChildren();
        Assert.Equal("heading level 1", blocks[0].GetLocalizedControlType());
        Assert.Equal("Broadside", blocks[0].GetName());
        Assert.Equal(AutomationControlType.Text, blocks[1].GetAutomationControlType());
        Assert.Equal("A paragraph with a link.", blocks[1].GetName());
        Assert.Equal(AutomationControlType.List, blocks[2].GetAutomationControlType());
        Assert.Equal(AutomationControlType.Table, blocks[3].GetAutomationControlType());
        Assert.Equal(["Name", "Value"], blocks[3].GetChildren()[0].GetChildren().Select(cell => cell.GetName()));

        // Peers are kept while their page stays loaded, so a client's references stay valid across frames.
        view.RunFrame();
        Assert.Same(page, Assert.Single(peer.GetChildren(), child => child.GetName() == "Page 1"));
    });

    [Fact]
    public void The_scroll_pattern_scrolls_the_document_and_reports_where_it_is() => Sta.Run(async () =>
    {
        await using DocumentSession session = DocumentSession.Open(ManyPagesDocument.Create(6), Shell.Options());
        using PdfView view = await ShowAsync(session);
        var scroll = (IScrollProvider)UIElementAutomationPeer.CreatePeerForElement(view).GetPattern(PatternInterface.Scroll);
        Assert.True(scroll.VerticallyScrollable);
        Assert.False(scroll.HorizontallyScrollable);
        Assert.Equal(0, scroll.VerticalScrollPercent);

        scroll.SetScrollPercent(ScrollPatternIdentifiers.NoScroll, 50);
        await SettleAsync(view);
        Assert.Equal(50, scroll.VerticalScrollPercent, 3);

        scroll.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement);
        await SettleAsync(view);
        Assert.True(scroll.VerticalScrollPercent > 50);
        Assert.True(view.State.PageIndex >= 3);
    });

    [Fact]
    public void The_view_draws_the_page_on_its_background_and_dark_mode_darkens_both() => Sta.Run(async () =>
    {
        await using DocumentSession session = DocumentSession.Open(ManyPagesDocument.Create(2), Shell.Options());
        using PdfView view = await ShowAsync(session);

        Color[] light = Sample(view);
        Assert.Equal(FrameRenderer.Background, light[0]); // the corner is outside the page
        Assert.Equal(Colors.White, light[1]); // the page's top margin is paper

        view.ColorFilter = ColorFilter.DarkMode;
        await SettleAsync(view);
        Color[] dark = Sample(view);
        Assert.Equal(FrameRenderer.DarkBackground, dark[0]);
        Assert.Equal(Colors.Black, dark[1]);
    });

    private static async Task<PdfView> ShowAsync(DocumentSession session)
    {
        var view = new PdfView();
        view.Measure(new Size(400, 600));
        view.Arrange(new Rect(0, 0, 400, 600));
        view.UpdateLayout();
        view.Session = session;
        await SettleAsync(view);
        Assert.True(view.State.PageCount > 0);
        return view;
    }

    private static async Task SettleAsync(PdfView view)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int quiet = 0;
        while (watch.Elapsed < TimeSpan.FromMinutes(1))
        {
            await Sta.YieldAsync();
            bool more = view.RunFrame();
            bool loading = view.State.IsLoading || view.State.PageCount == 0 || view.Controller.IsLoadingPages;
            quiet = more || loading ? 0 : quiet + 1;
            if (quiet >= 3)
            {
                view.UpdateLayout();
                return;
            }
        }

        Assert.Fail("The view did not settle.");
    }

    // Renders the view into a bitmap and reads the top-left corner and a point in the first page's top margin.
    private static Color[] Sample(PdfView view)
    {
        var bitmap = new RenderTargetBitmap(400, 600, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        LayoutRectangle page = view.Controller.Zoom!.Layout.PageToViewport(view.Controller.Input!.Viewport, 0, view.Controller.Zoom.Layout.GetPageMetrics(0).CropBox);
        return [Pixel(bitmap, 1, 1), Pixel(bitmap, (int)(page.X + (page.Width / 2)), (int)(page.Y + 6))];

        static Color Pixel(BitmapSource bitmap, int x, int y)
        {
            var pixel = new byte[4];
            bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
            return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
        }
    }
}
