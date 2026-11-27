using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Viewer.Uno.Tests;

/// <summary>
/// The toolbar model bound to this shell's controller (#632) through the calls its <c>PdfView</c> forwards for
/// <see cref="IDocumentViewer"/>: page and zoom round trips. The control itself needs the platform; this project compiles the shell's
/// platform-free sources only.
/// </summary>
public sealed class PdfToolbarTests
{
    [Fact]
    public void The_page_commands_and_the_page_box_move_the_view() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Corpus.Bytes("page-labels.pdf"));
        var viewer = new ControllerViewer(shell);
        using var toolbar = new ViewerToolbar(viewer);
        var page = (ViewerPageCommand)toolbar.Find(ViewerCommandIds.PageNumber)!;
        for (int i = 0; i < 500 && page.Text != "i"; i++)
        {
            await SettleAsync(shell, viewer); // the labels are read in the background
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Equal(("i", 12), (page.Text, page.PageCount));

        ((ViewerButtonCommand)toolbar.Find(ViewerCommandIds.PageNext)!).Execute();
        await SettleAsync(shell, viewer);
        Assert.Equal(1, shell.Controller.State.PageIndex);
        Assert.Equal("ii", page.Text);

        Assert.True(page.TrySubmit("V"));
        await SettleAsync(shell, viewer);
        Assert.Equal(6, shell.Controller.State.PageIndex);
        Assert.False(page.TrySubmit("99"));
        await SettleAsync(shell, viewer);
        Assert.Equal(6, shell.Controller.State.PageIndex);
        Assert.NotNull(page.Error);

        ((ViewerButtonCommand)toolbar.Find(ViewerCommandIds.PagePrevious)!).Execute();
        await SettleAsync(shell, viewer);
        Assert.Equal(5, shell.Controller.State.PageIndex);
    });

    [Fact]
    public void The_zoom_commands_zoom_the_view_and_the_picker_shows_the_zoom() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(ManyPagesDocument.Create(2));
        var viewer = new ControllerViewer(shell);
        using var toolbar = new ViewerToolbar(viewer, new ViewerToolbarOptions().WithZoomPresets(1, 2));
        var picker = (ViewerChoiceCommand)toolbar.Find(ViewerCommandIds.ZoomMode)!;
        double start = shell.Controller.State.Zoom;

        ((ViewerButtonCommand)toolbar.Find(ViewerCommandIds.ZoomIn)!).Execute();
        await SettleAsync(shell, viewer);
        Assert.Equal(start * 1.25, shell.Controller.State.Zoom, 3);

        picker.Selected = 4; // 200 %
        await SettleAsync(shell, viewer);
        Assert.Equal(2, shell.Controller.State.Zoom, 3);
        Assert.Equal((4, "200%"), (picker.Selected, picker.Text));

        picker.Selected = 2; // actual size
        await SettleAsync(shell, viewer);
        Assert.Equal(1, shell.Controller.State.Zoom, 3);

        ((ViewerToggleCommand)toolbar.Find(ViewerCommandIds.DarkMode)!).IsChecked = true;
        await SettleAsync(shell, viewer);
        Assert.Equal(ColorFilter.DarkMode, shell.Controller.Session!.ColorFilter);
    });

    private static async Task SettleAsync(Shell shell, ControllerViewer viewer)
    {
        await shell.SettleAsync();
        viewer.Raise();
    }

    /// <summary>What the shell's <c>PdfView</c> does for <see cref="IDocumentViewer"/>, over the controller the test hosts.</summary>
    private sealed class ControllerViewer(Shell shell) : IDocumentViewer
    {
        private ColorFilter? _colorFilter;

        public event EventHandler? ViewChanged;

        public DocumentSession? Session => shell.Controller.Session;

        public int PageIndex => shell.Controller.State.PageIndex;

        public int PageCount => shell.Controller.State.PageCount;

        public double Zoom => shell.Controller.State.Zoom;

        public TextSelection? Selection => shell.Controller.Selection;

        public DocumentSearch? Search => shell.Controller.Search;

        public AnnotationLayer? Annotations => shell.Controller.Annotations;

        public PageLabelTable? PageLabels => shell.Controller.PageLabels;

        public PageLayoutOptions? LayoutOptions { get; private set; }

        public ColorFilter? ColorFilter
        {
            get => _colorFilter;
            set
            {
                _colorFilter = value;
                shell.Controller.SetColorFilter(value);
                Raise();
            }
        }

        public void Raise() => ViewChanged?.Invoke(this, EventArgs.Empty);

        public void GoToPage(int pageIndex) => shell.Controller.GoToPage(pageIndex);

        public void NextPage() => shell.Controller.NextPage();

        public void PreviousPage() => shell.Controller.PreviousPage();

        public void GoTo(ViewTarget target) => shell.Controller.GoTo(target);

        public void ZoomTo(ZoomMode mode) => shell.Controller.ZoomTo(mode);

        public void ZoomBy(double factor) => shell.Controller.ZoomBy(factor);

        public void SetLayout(PageLayoutOptions options)
        {
            LayoutOptions = options;
            shell.Controller.SetLayoutOptions(options);
            Raise();
        }

        public void Reveal(int pageIndex, PdfRectangle area) => shell.Controller.Reveal(pageIndex, area);
    }
}
