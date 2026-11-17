using System.Globalization;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Broadside.Rendering;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace Broadside.Viewer.Avalonia.Tests;

/// <summary>
/// The acceptance walk over the rendering gate's pinned real-world pages (tests/Broadside.Rendering.Tests/CorpusGate/pinned-pages.txt):
/// each page opens in the viewer, its tiles reach the screen as the rasterizer drew them, and it scrolls and zooms to sharp tiles
/// again. Without the real-world corpus (BROADSIDE_CORPUS_DIR) hand-written files stand in; BROADSIDE_PINNED_REQUIRED=true (CI) makes
/// a missing pinned file a failure instead.
/// </summary>
public sealed class PinnedSubsetTests
{
    private static readonly string[] Fallback = ["text-standard14.pdf#0", "image-smask.pdf#0", "shading-type2-axial.pdf#0", "text-render-modes.pdf#0"];

    public static TheoryData<string> Pages()
    {
        var data = new TheoryData<string>();
        string list = Path.Combine(ViewerHarness.RepositoryRoot, "tests", "Broadside.Rendering.Tests", "CorpusGate", "pinned-pages.txt");
        bool required = string.Equals(Environment.GetEnvironmentVariable("BROADSIDE_PINNED_REQUIRED"), "true", StringComparison.OrdinalIgnoreCase);
        foreach (string line in File.ReadLines(list))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] fields = line.Split('\t');
            if (ViewerHarness.RealWorldFile(fields[0]) is null)
            {
                Assert.False(required, $"pinned file {fields[0]} is missing from the real-world corpus");
                continue;
            }

            foreach (string page in fields[1].Split(','))
            {
                data.Add($"{fields[0]}#{page}");
            }
        }

        if (data.Count == 0)
        {
            foreach (string page in Fallback)
            {
                if (File.Exists(ViewerHarness.CorpusFile(page[..page.IndexOf('#', StringComparison.Ordinal)])))
                {
                    data.Add(page);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public Task A_pinned_page_opens_scrolls_and_zooms_with_its_tiles_on_screen_as_rendered(string entry) => Ui.RunAsync(async () =>
    {
        int hash = entry.LastIndexOf('#');
        string file = entry[..hash];
        int page = int.Parse(entry[(hash + 1)..], CultureInfo.InvariantCulture);
        string path = ViewerHarness.RealWorldFile(file) ?? ViewerHarness.CorpusFile(file);

        // Overlays off: the comparison is of content tiles against the screen.
        using ViewerHarness viewer = await ViewerHarness.OpenAsync(path, showAnnotations: false);
        if (page > 0)
        {
            viewer.Viewer.GoToPage(page);
            await viewer.SettleAsync();
        }

        Assert.Equal(page, viewer.Viewer.State.PageIndex);
        AssertTilesOnScreen(viewer, page);

        // Scroll: three wheel notches down move the viewport (when the content is taller than the window).
        Viewport before = viewer.Viewport;
        bool scrollable = viewer.Layout.ContentHeight * before.Zoom > before.Height + 1;
        viewer.Window.MouseWheel(new Point(300, 400), new Vector(0, -3), RawInputModifiers.None);
        await viewer.SettleAsync();
        if (scrollable)
        {
            Assert.True(viewer.Viewport.Y > before.Y, $"{viewer.Viewport} after {before}");
        }

        // Zoom: the plus key zooms in at the centre; the settled frame is drawn by sharp tiles at the new scale.
        double zoom = viewer.Viewport.Zoom;
        viewer.Viewer.Focus();
        viewer.Key(Key.OemPlus);
        await viewer.SettleAsync();
        Assert.True(viewer.Viewport.Zoom > zoom * 1.2, $"zoom {viewer.Viewport.Zoom} after {zoom}");
        AssertTilesOnScreen(viewer, viewer.Viewer.State.PageIndex);
    });

    /// <summary>
    /// Every sharp content tile of a visible page is on screen where the frame puts it, with the pixels the rasterizer gave it (sampled,
    /// within a small tolerance for the edge of a destination that falls between pixels).
    /// </summary>
    internal static void AssertTilesOnScreen(ViewerHarness viewer, int page)
    {
        FramePixels frame = viewer.Capture();
        var parts = new FramePart[256];
        int count = Math.Min(viewer.Controller.Zoom!.GetFrame(parts), parts.Length);
        int tiles = 0;
        int samples = 0;
        int mismatches = 0;
        foreach (FramePart part in parts.AsSpan(0, count))
        {
            if (part.Kind != FramePartKind.Sharp || (part.Tile.Layers & DisplayLayers.Content) == 0)
            {
                continue;
            }

            Assert.True(viewer.Controller.Surface.TryGetImage(part.Tile, out AvaloniaBitmap? image));
            FramePixels tile = FramePixels.From(image!);
            tiles++;
            LayoutRectangle d = part.Destination;
            for (int y = 3; y < tile.Height - 3; y += 5)
            {
                int fy = (int)Math.Round(d.Y + y);
                if (fy < 0 || fy >= frame.Height)
                {
                    continue;
                }

                for (int x = 3; x < tile.Width - 3; x += 5)
                {
                    int fx = (int)Math.Round(d.X + x);
                    if (fx < 0 || fx >= frame.Width)
                    {
                        continue;
                    }

                    samples++;
                    mismatches += FramePixels.Difference(frame[fx, fy], tile[x, y]) > 24 ? 1 : 0;
                }
            }
        }

        Assert.True(tiles > 0, $"no sharp content tile on screen for page {page}");
        Assert.True(samples > 100, $"only {samples} samples");

        // Half-pixel destinations blur hard edges a little; anything else (a tile at the wrong place, a stale or blank tile) fails.
        Assert.True(mismatches <= samples / 50, $"{mismatches} of {samples} sampled pixels differ from their tiles");
    }
}
