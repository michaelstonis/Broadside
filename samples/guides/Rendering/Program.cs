using System.Security.Cryptography;
using Broadside;
using Broadside.GoldenImages;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.Samples;

Sample.Start("Rendering");

#region render-page
using (PdfDocument document = PdfDocument.Open("paths-fill-rules.pdf"))
using (var rasterizer = new SoftwareRasterizer())
using (Bitmap bitmap = rasterizer.Render(document.Pages[0], new RenderOptions().WithDpi(144)))
{
    Console.WriteLine($"{bitmap.Width} x {bitmap.Height} pixels"); // premultiplied BGRA, rows top to bottom
    bitmap.SavePng("paths-fill-rules.png");
}
#endregion

// The page at 72 DPI, the resolution of the golden images, against its golden image (tests/Golden) within the rasterizer's threshold.
using (PdfDocument document = PdfDocument.Open("paths-fill-rules.pdf"))
using (var rasterizer = new SoftwareRasterizer())
using (Bitmap bitmap = rasterizer.Render(document.Pages[0]))
{
    GoldenCheck.Expect(GoldenThresholds.SoftwareRasterizer, "paths-fill-rules.pdf", 0, GoldenImage.FromBitmap(bitmap));
}

#region fonts
PdfOptions fontOptions = new PdfOptions()
    .UseStandard14Fonts()          // Broadside.Fonts.Standard14: Helvetica, Times, Courier, Symbol and ZapfDingbats
    .UsePredefinedCMaps()          // Broadside.Fonts.Cmaps: the predefined CJK character maps, such as 90ms-RKSJ-H
    .UseSystemFontResolver(null);  // no operating-system fonts, so the output does not depend on the machine
var fontEngine = new PdfEngine(fontOptions);

using (PdfDocument document = fontEngine.Open("text-cidcff-predefined-cmap.pdf"))
using (var rasterizer = new SoftwareRasterizer())
using (Bitmap bitmap = rasterizer.Render(document.Pages[0]))
{
    bitmap.SavePng("japanese.png");
}
#endregion

foreach (string file in new[] { "text-standard14.pdf", "text-cidcff-predefined-cmap.pdf" })
{
    using PdfDocument document = fontEngine.Open(file);
    using var rasterizer = new SoftwareRasterizer();
    using Bitmap bitmap = rasterizer.Render(document.Pages[0]);
    GoldenCheck.Expect(GoldenThresholds.SoftwareRasterizer, file, 0, GoldenImage.FromBitmap(bitmap));
}

using (PdfDocument document = PdfDocument.Open("paths-fill-rules.pdf"))
{
    PdfPage page = document.Pages[0];

    #region options
    RenderOptions options = new RenderOptions()
        .WithDpi(150)                       // or WithScale(150 / 72.0); the default is 72 DPI
        .WithRotation(90)                   // clockwise, added to the page's own rotation
        .WithTransparentBackground(true)    // no white paper: what nothing paints stays transparent
        .WithAntiAliasing(AntiAliasing.Paths | AntiAliasing.Images); // text in whole pixels

    PixelSize size = options.GetPixelSize(page); // width and height swapped by the quarter turn
    Console.WriteLine($"{page.CropBox.Width} x {page.CropBox.Height} points render to {size.Width} x {size.Height} pixels");
    #endregion

    Sample.Expect(size.Width == 625 && size.Height == 834, "the rotated pixel size at 150 DPI");
    using var rasterizer = new SoftwareRasterizer();
    using Bitmap transparent = rasterizer.Render(page, options);
    Sample.Expect(transparent.Pixels[3] == 0, "an unpainted corner is transparent");
}

using (PdfDocument document = PdfDocument.Open("paths-fill-rules.pdf"))
using (var rasterizer = new SoftwareRasterizer())
{
    PdfPage page = document.Pages[0];
    using Bitmap whole = rasterizer.Render(page, new RenderOptions().WithDpi(144));
    using var assembled = new Bitmap(whole.Width, whole.Height);
    int tiles = 0;

    #region tiles
    DisplayList list = page.CreateDisplayList(); // built once; the document's display-list cache keeps it for later calls too
    PdfRectangle crop = page.CropBox;
    const double tileSize = 128; // points

    for (double bottom = crop.Bottom; bottom < crop.Top; bottom += tileSize)
    {
        for (double left = crop.Left; left < crop.Right; left += tileSize)
        {
            var region = new PdfRectangle(left, bottom, Math.Min(left + tileSize, crop.Right), Math.Min(bottom + tileSize, crop.Top));
            RenderOptions tileOptions = new RenderOptions().WithDpi(144).WithRegion(region);
            PixelSize size = tileOptions.GetPixelSize(page);

            using var tile = new Bitmap(size.Width, size.Height);
            tile.Pixels.Fill(0xFF); // white paper: a display list draws over what the bitmap holds
            rasterizer.Render(list, tile, tileOptions.GetDeviceTransform(page));
            ShowTile(tile, tileOptions.GetPixelRectangle(page)); // where the tile sits in a render of the whole page
        }
    }
    #endregion

    Sample.Expect(tiles == 12, "twelve tiles");
    Sample.Expect(assembled.Pixels.SequenceEqual(whole.Pixels), "the tiles put together are the whole page's render, byte for byte");
    Console.WriteLine($"{tiles} tiles equal the whole page");

    void ShowTile(Bitmap tile, PixelRectangle at)
    {
        tiles++;
        for (int y = 0; y < tile.Height; y++)
        {
            tile.GetRow(y).CopyTo(assembled.GetRow(at.Y + y)[(at.X * 4)..]);
        }
    }
}

using (PdfDocument document = PdfDocument.Open("paths-fill-rules.pdf"))
using (var rasterizer = new SoftwareRasterizer())
{
    PdfPage page = document.Pages[0];
    var progress = new LastValue();

    #region cancellation
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30)); // or cancel it when the user scrolls away
    RenderOptions options = new RenderOptions()
        .WithCancellation(cancellation.Token)
        .WithProgress(progress); // an IProgress<double>, from 0 to 1, called on the rendering thread

    try
    {
        using Bitmap bitmap = rasterizer.Render(page, options);
        Console.WriteLine($"Rendered; progress {progress.Value:P0}");
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("The render was canceled"); // the rasterizer can render again at once
    }
    #endregion

    Sample.Expect(progress.Value == 1, "progress ends at 1");
    cancellation.Cancel();
    bool canceled = false;
    try
    {
        using Bitmap bitmap = rasterizer.Render(page, options);
    }
    catch (OperationCanceledException)
    {
        canceled = true;
    }

    Sample.Expect(canceled, "a canceled token stops the render");

    #region progressive
    PixelSize size = new RenderOptions().GetPixelSize(page);
    using var target = new Bitmap(size.Width, size.Height);
    using ProgressiveRender render = rasterizer.BeginRender(page, target, new RenderOptions());
    int slices = 1;
    while (!render.Continue(TimeSpan.FromMilliseconds(8))) // about half a frame at 60 Hz
    {
        slices++; // show render.Target so far, let the UI thread breathe, then continue
    }

    Console.WriteLine($"Drawn in {slices} slice(s)");
    #endregion

    using Bitmap direct = rasterizer.Render(page);
    Sample.Expect(target.Pixels.SequenceEqual(direct.Pixels), "a progressive render draws the same pixels");
}

using (var rasterizer = new SoftwareRasterizer())
{
    #region layers
    using PdfDocument document = PdfDocument.Open("annotations-appearance.pdf");
    PdfPage page = document.Pages[0];

    using Bitmap everything = rasterizer.Render(page); // DisplayLayers.All: content, then annotations, then form fields
    using Bitmap contentOnly = rasterizer.Render(page, new RenderOptions().WithLayers(DisplayLayers.Content));
    using Bitmap printed = rasterizer.Render(page, new RenderOptions().WithIntent(DisplayIntent.Print)); // annotations as printed

    DisplayList annotations = page.CreateDisplayList(DisplayLayers.Annotations); // one layer, to draw over cached content
    #endregion

    Sample.Expect(!everything.Pixels.SequenceEqual(contentOnly.Pixels), "the annotations layer draws something");
    Sample.Expect(annotations.OperationCount > 0, "the annotation layer's list");
}

using (var rasterizer = new SoftwareRasterizer())
{
    #region optional-content
    using PdfDocument document = PdfDocument.Open("optional-content.pdf");
    PdfOptionalContentProperties layers = document.OptionalContent!; // null when the file has no optional content
    PdfOptionalContentState states = layers.GetDefaultStates();
    foreach (PdfOptionalContentGroup group in layers.Groups)
    {
        Console.WriteLine($"Layer {group.Name}: {(states.IsOn(group) ? "on" : "off")}");
    }

    PdfOptionalContentGroup b = layers.Groups.First(group => group.Name == "B");
    using Bitmap withB = rasterizer.Render(document.Pages[0], new RenderOptions().WithOptionalContentState(states.With(b, on: true)));
    #endregion

    using Bitmap asOpened = rasterizer.Render(document.Pages[0]);
    Sample.Expect(!states.IsOn(b), "B is off by default");
    Sample.Expect(!withB.Pixels.SequenceEqual(asOpened.Pixels), "turning B on shows its content");
}

using (PdfDocument document = PdfDocument.Open("text-standard14.pdf", new PdfOptions().UseStandard14Fonts()))
{
    PdfPage page = document.Pages[0];
    var viewOptions = new RenderOptions().WithDpi(144);
    HitTestIndex glyphs = page.CreateHitTestIndex();
    Sample.Expect(glyphs.GlyphCount > 0, "the page has glyphs");
    PdfRectangle first = glyphs.GetGlyph(0).Quad.Bounds;
    PathPoint pixel = viewOptions.GetDeviceTransform(page).Transform((first.Left + first.Right) / 2, (first.Bottom + first.Top) / 2);
    (double clickX, double clickY) = (pixel.X, pixel.Y);
    string found = string.Empty;

    #region hit-test
    DisplayList list = page.CreateDisplayList(); // the list the viewer renders from
    HitTestIndex index = page.CreateHitTestIndex(list);

    // A click at pixel (clickX, clickY) of the page shown at 144 DPI, mapped back to page space.
    viewOptions.GetDeviceTransform(page).TryInvert(out Matrix toPage);
    PathPoint point = toPage.Transform(clickX, clickY);

    foreach (HitTestItem item in index.Find(point, HitTestKinds.Glyphs | HitTestKinds.Links))
    {
        if (item.Kind == HitTestKinds.Glyphs)
        {
            HitTestGlyph glyph = index.GetGlyph(item.Index);
            Console.WriteLine($"Glyph '{index.GetGlyphText(glyph)}' at {glyph.Quad.Bounds}");
            found += index.GetGlyphText(glyph).ToString();
        }
        else if (item.Kind == HitTestKinds.Links)
        {
            Console.WriteLine($"Link to {(index.Links[item.Index].Action as PdfUriAction)?.Uri}");
        }
    }

    // A drag selection: every glyph inside a rectangle of page space, in content order.
    var selection = new PdfRectangle(0, 0, page.CropBox.Right, page.CropBox.Top);
    var selected = new System.Text.StringBuilder();
    foreach (HitTestItem item in index.Find(selection, HitTestKinds.Glyphs))
    {
        selected.Append(index.GetGlyphText(index.GetGlyph(item.Index)));
    }
    #endregion

    Sample.Expect(found == "H", "the click is on the H");
    Sample.Expect(selected.ToString().Contains("Hello, Broadside", StringComparison.Ordinal), "the selection's text");
    Console.WriteLine($"Selected: {selected}");
}

using (PdfDocument document = PdfDocument.Open("annotations-link.pdf"))
{
    HitTestIndex index = document.Pages[0].CreateHitTestIndex();
    string? uri = null;
    foreach (HitTestItem item in index.Find(new PathPoint(100, 718), HitTestKinds.Links))
    {
        uri = (index.Links[item.Index].Action as PdfUriAction)?.Uri;
    }

    Sample.Expect(uri is not null, "the link under the point");
    Console.WriteLine($"Link to {uri}");
}

using (var rasterizer = new SoftwareRasterizer())
{
    using PdfDocument plain = PdfDocument.Open("icc-rgb-matrix.pdf");
    using Bitmap unmanaged = rasterizer.Render(plain.Pages[0]);

    #region color-management
    var managedEngine = new PdfEngine(new PdfOptions().UseIccColorManagement());
    using PdfDocument document = managedEngine.Open("icc-rgb-matrix.pdf");
    using Bitmap managed = rasterizer.Render(document.Pages[0]); // ICCBased colors through their embedded profiles
    #endregion

    Sample.Expect(!managed.Pixels.SequenceEqual(unmanaged.Pixels), "the profiles change the colors");

    #region print-preview
    using PdfDocument cmyk = managedEngine.Open("output-intent-cmyk.pdf"); // CMYK through the output intent's profile
    using Bitmap preview = rasterizer.Render(cmyk.Pages[0], new RenderOptions().WithOverprintPreview(true));
    #endregion

    #region dark-mode
    using Bitmap dark = rasterizer.Render(plain.Pages[0], new RenderOptions().WithColorFilter(ColorFilter.DarkMode));
    #endregion

    Sample.Expect(dark.Pixels[0] < 64 && unmanaged.Pixels[0] == 255, "white paper turns dark");
    Sample.Expect(preview.Width > 0, "the print preview");
}

using (PdfDocument document = PdfDocument.Open("paths-fill-rules.pdf"))
{
    PdfPage page = document.Pages[0];

    #region determinism
    RenderOptions options = new RenderOptions().WithDpi(96);
    using var rasterizer = new SoftwareRasterizer();
    using Bitmap bitmap = rasterizer.Render(page, options);
    string hash = Convert.ToHexString(SHA256.HashData(bitmap.Pixels)); // the same on Linux, Windows and macOS, x64 and Arm64
    Console.WriteLine($"SHA-256 of the pixels: {hash}");
    #endregion

    using var other = new SoftwareRasterizer();
    using Bitmap again = other.Render(page, options);
    Sample.Expect(Convert.ToHexString(SHA256.HashData(again.Pixels)) == hash, "the same bytes from another rasterizer");
}

{
    #region budgets
    PdfOptions budgets = new PdfOptions()
        .WithDisplayListCacheBudget(64L << 20)  // display lists kept for renders of the same page (default 32 MiB)
        .WithImageCacheBudget(128L << 20)       // decoded images (default 64 MiB)
        .WithGlyphCacheBudget(16L << 20)        // glyph outlines read from font programs (default 8 MiB)
        .WithContentCacheBudget(16L << 20)      // decoded content streams (default 32 MiB)
        .WithMaxImagePixels(100_000_000);       // a larger image is not decoded (default 2^28 pixels)

    using PdfDocument document = new PdfEngine(budgets).Open("paths-fill-rules.pdf");
    using var rasterizer = new SoftwareRasterizer { GlyphCacheBudget = 2L << 20 }; // its glyph coverage (default 4 MiB)
    using (Bitmap first = rasterizer.Render(document.Pages[0]))
    using (Bitmap second = rasterizer.Render(document.Pages[0], new RenderOptions().WithDpi(144)))
    {
        PdfCacheStatistics lists = document.CacheStatistics.DisplayLists;
        Console.WriteLine($"Display lists: {lists.Bytes:N0} of {lists.Budget:N0} bytes, {lists.Hits} hit(s), {lists.Misses} miss(es)");
    }
    #endregion

    Sample.Expect(document.CacheStatistics.DisplayLists.Hits >= 1, "the second render reuses the cached display list");
}

Console.WriteLine("OK");

/// <summary>Keeps the last progress value a render reported, on the rendering thread.</summary>
internal sealed class LastValue : IProgress<double>
{
    public double Value { get; private set; }

    public void Report(double value) => Value = value;
}
