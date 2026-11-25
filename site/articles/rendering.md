---
title: "Rendering"
---

# Rendering

This guide turns pages into pixels: the rendering backends and how to choose one, the render options and their defaults, tiles drawn from a cached display list, cancellation and progress, layers and optional content, hit-testing, color management, the fonts and character-map packages, what the software rasterizer guarantees about its output, and the memory a document and a rasterizer keep.

The code comes from compiled samples in [`samples/guides`](https://github.com/michaelstonis/Broadside/tree/main/samples/guides). `Rendering` (the software rasterizer) and `RenderingSkia` are console apps that CI builds and runs on every change. The CoreGraphics, Direct2D and Android samples need their platform, so they live in `samples/guides/RenderingBackends` and run inside each backend's tests, in the CI job that already runs on that platform. Every sample renders a page of the repository's test corpus and compares it with the page's reviewed golden image, within the threshold set for that backend, so each snippet here compiles and draws what the text says.

## Backends

Every backend draws the same thing: a page's display list, the drawing operations the library builds from the page's content, annotations and form fields. They differ in where they run and what they draw into.

| Backend | Package | Runs on | Draws into |
|---|---|---|---|
| Software rasterizer (`SoftwareRasterizer`) | `Broadside.Rendering` | Everywhere .NET runs, including the browser | `Bitmap`: premultiplied BGRA in managed or your own memory |
| SkiaSharp (`SkiaBackend`) | `Broadside.Rendering.Skia` | Wherever SkiaSharp has a native library: Windows, macOS, Linux, iOS, Android | `SKBitmap` or any `SKCanvas` |
| CoreGraphics (`CoreGraphicsRenderer`) | `Broadside.Rendering.CoreGraphics` | macOS, iOS, Mac Catalyst | `CGBitmapContext` or any `CGContext` |
| Direct2D (`Direct2DRenderer`) | `Broadside.Rendering.Direct2D` | Windows | `Bitmap`, through a Direct3D 11 device or WARP |
| Android Canvas (`AndroidCanvasBackend`) | `Broadside.Rendering.Android` | Android | `Android.Graphics.Bitmap` or any `Canvas` |

The software rasterizer is fully managed and has no native dependency. It is the reference: its renders are the golden images the other backends are tested against, and it gives the same bytes on every platform (see [Determinism](#determinism)). The platform backends draw with the operating system's graphics library, which is faster on that platform and draws straight into the surfaces its UI toolkit uses. What a platform library cannot draw (some blend modes, soft masks and shadings, depending on the backend) is drawn by the software rasterizer and composited in, and the backend's `Diagnostics` list says so. `Broadside.Rendering.WebGL` draws on the GPU in the browser for the Blazor viewer and is not covered here.

Each platform backend also comes with a platform image decoder (`SkiaImageDecoder`, `ImageIODecoder`, `WicImageDecoder`, `AndroidImageDecoder`) that a viewer registers with `PdfOptions.UsePlatformImageDecoder`. It decodes the images a viewer prefetches for a display list (`PrefetchImagesAsync`) with the platform's codecs, within a small tolerance of the managed ones.

All of them take the same `RenderOptions`, so everything below applies to every backend unless it says otherwise.

## Render a page

Open the document, create a rasterizer and render a page. The bitmap's buffer comes from a pool; dispose the bitmap to return it.

<!-- snippet: samples/guides/Rendering/Program.cs#render-page -->
```csharp
using (PdfDocument document = PdfDocument.Open("paths-fill-rules.pdf"))
using (var rasterizer = new SoftwareRasterizer())
using (Bitmap bitmap = rasterizer.Render(document.Pages[0], new RenderOptions().WithDpi(144)))
{
    Console.WriteLine($"{bitmap.Width} x {bitmap.Height} pixels"); // premultiplied BGRA, rows top to bottom
    bitmap.SavePng("paths-fill-rules.png");
}
```
<!-- endsnippet -->

A rasterizer keeps working buffers and a glyph cache between renders, so reuse one rather than creating one per page. It renders one page at a time: give each thread its own. A document is safe to render from several threads at once while nobody changes it.

## Fonts and character maps

A PDF file may use a font without embedding it, most often one of the 14 standard fonts (Helvetica, Times, Courier, Symbol and ZapfDingbats). Two optional packages make such pages render the same everywhere:

- `Broadside.Fonts.Standard14` carries metric-compatible fonts for the standard 14 (Liberation Sans, Serif and Mono, and Foxit's Symbol and Dingbats). Without it, the library looks for the fonts on the operating system and substitutes the nearest one it finds.
- `Broadside.Fonts.Cmaps` carries the predefined character maps of Chinese, Japanese and Korean text, such as `90ms-RKSJ-H` or `UniGB-UTF16-H`, which files name instead of embedding.

<!-- snippet: samples/guides/Rendering/Program.cs#fonts -->
```csharp
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
```
<!-- endsnippet -->

Without `UseSystemFontResolver(null)`, a font that is neither embedded nor in a package comes from the operating system's font folders, so the same file can render differently on two machines. Font resolvers you register with `UseFontResolver` are asked first, in order.

## Render options

`RenderOptions` is a mutable options object: set its properties or chain its `With*` methods. A new instance has these defaults:

| Option | Default | What it does |
|---|---|---|
| `Scale` / `WithScale` | `1` | Device pixels per point (1/72 inch, before the page's `UserUnit`). |
| `Dpi` / `WithDpi` | `72` (`RenderOptions.DefaultDpi`) | The same setting as `Scale`, in dots per inch: `Dpi` is `Scale` × 72. |
| `Rotation` / `WithRotation` | `0` | Degrees clockwise, a multiple of 90, added to the page's own `/Rotate`. |
| `Region` / `WithRegion` | `null`, the whole page | The part of the page to render, in page coordinates; see [Tiles](#tiles-from-a-cached-display-list). |
| `Layers` / `WithLayers` | `DisplayLayers.All` | Which layers to draw: `Content`, `Annotations`, `FormFields`. |
| `Intent` / `WithIntent` | `DisplayIntent.View` | `View` or `Print`: decides which annotations are drawn, which optional content is shown and which alternate images are painted. |
| `OptionalContentState` / `WithOptionalContentState` | `null`, the document's default configuration | Which optional content groups (layers) are on. |
| `CancellationToken` / `WithCancellation` | `CancellationToken.None` | Cancels the render. |
| `Progress` / `WithProgress` | `null`, no reports | Receives progress from 0 to 1. |
| `AntiAliasing` / `WithAntiAliasing` | `AntiAliasing.All` | Which kinds of painting are anti-aliased: `Paths`, `Text`, `Images`. |
| `TransparentBackground` / `WithTransparentBackground` | `false`, white paper | Starts from transparent black, so unpainted pixels stay transparent. |
| `ForceOutlines` / `WithForceOutlines` | `false` | Makes a platform backend draw glyphs from their outlines instead of its own font engine, so its text matches the software rasterizer's. The software rasterizer always draws outlines. |
| `OverprintPreview` / `WithOverprintPreview` | `false` | Shows overprinting and spot colors as a print preview does. |
| `ColorFilter` / `WithColorFilter` | `ColorFilter.None` | `DarkMode` darkens the page for display, leaving images as they are. For display only. |
| `ScaledImageDecode` / `WithScaledImageDecode` | `false` | Decodes large images at the resolution they are drawn at, for viewers showing a page small. For display only. |
| `ImageSamplesPerPixel` / `WithImageSamplesPerPixel` | `2` (`RenderOptions.DefaultImageSamplesPerPixel`) | With `ScaledImageDecode`, the fewest decoded samples a reduced image keeps per device pixel. |
| `GlyphPositioning` / `WithGlyphPositioning` | `GlyphPositioning.Quantized` | The software rasterizer places glyphs at the nearest quarter pixel so its glyph cache reuses more of them; `Exact` uses the exact position. Other backends ignore it. |

The page's crop box, `/Rotate` and `UserUnit` always apply. `GetPixelSize` returns the size of the bitmap a render needs, and `GetDeviceTransform` the transform from page coordinates to its pixels (origin at the top left, y down), which any backend can use:

<!-- snippet: samples/guides/Rendering/Program.cs#options -->
```csharp
RenderOptions options = new RenderOptions()
    .WithDpi(150)                       // or WithScale(150 / 72.0); the default is 72 DPI
    .WithRotation(90)                   // clockwise, added to the page's own rotation
    .WithTransparentBackground(true)    // no white paper: what nothing paints stays transparent
    .WithAntiAliasing(AntiAliasing.Paths | AntiAliasing.Images); // text in whole pixels

PixelSize size = options.GetPixelSize(page); // width and height swapped by the quarter turn
Console.WriteLine($"{page.CropBox.Width} x {page.CropBox.Height} points render to {size.Width} x {size.Height} pixels");
```
<!-- endsnippet -->

The options object is read during a render, so do not change one that a render on another thread is using.

## Tiles from a cached display list

Rendering a page has two steps: building the page's display list from its content, then drawing the list. A viewer that zooms, scrolls or splits the page into tiles builds the list once and draws it many times. Give each tile a `Region` and draw the list into a bitmap of the region's size, using the region's device transform:

<!-- snippet: samples/guides/Rendering/Program.cs#tiles -->
```csharp
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
```
<!-- endsnippet -->

Each tile's pixels are exactly the pixels at the same place in a render of the whole page, which `GetPixelRectangle` locates, so the tiles join without seams. Drawing a list into a bitmap does not paint a background: the tile above starts white. Paints entirely outside the region are skipped, so a small region of a complex page draws quickly at any zoom.

You need not keep the list yourself. A document keeps the display lists it builds, up to `PdfOptions.DisplayListCacheBudget` bytes, so `CreateDisplayList` and every page render reuse a page's list until the document changes or the list is evicted (see [Memory budgets](#memory-budgets)). Each platform backend has the same `Render(DisplayList, ...)` overload for its own surface.

## Cancellation and progress

A render checks its cancellation token while it builds the display list and before every drawing operation. A canceled render throws `OperationCanceledException`, and the rasterizer is ready for the next render at once. Progress goes from 0 to 0.5 while the list is built and from 0.5 to 1 while it is drawn, never decreases, and is reported on the rendering thread.

<!-- snippet: samples/guides/Rendering/Program.cs#cancellation -->
```csharp
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
```
<!-- endsnippet -->

A UI thread can also render in time slices: `BeginRender` starts a render and each `Continue` draws until its time budget is used, so a large page never blocks the thread for more than a frame. The result is the same bitmap as a single render.

<!-- snippet: samples/guides/Rendering/Program.cs#progressive -->
```csharp
PixelSize size = new RenderOptions().GetPixelSize(page);
using var target = new Bitmap(size.Width, size.Height);
using ProgressiveRender render = rasterizer.BeginRender(page, target, new RenderOptions());
int slices = 1;
while (!render.Continue(TimeSpan.FromMilliseconds(8))) // about half a frame at 60 Hz
{
    slices++; // show render.Target so far, let the UI thread breathe, then continue
}

Console.WriteLine($"Drawn in {slices} slice(s)");
```
<!-- endsnippet -->

## Layers and optional content

A page render draws three layers in order: the page content, the annotations and the form fields. Render any combination with `Layers`, or build one layer's display list to redraw annotations over a cached render of the content. `Intent` chooses between what a viewer shows and what a printer prints: annotations marked `NoView` or not marked `Print`, for instance, appear in one and not the other.

<!-- snippet: samples/guides/Rendering/Program.cs#layers -->
```csharp
using PdfDocument document = PdfDocument.Open("annotations-appearance.pdf");
PdfPage page = document.Pages[0];

using Bitmap everything = rasterizer.Render(page); // DisplayLayers.All: content, then annotations, then form fields
using Bitmap contentOnly = rasterizer.Render(page, new RenderOptions().WithLayers(DisplayLayers.Content));
using Bitmap printed = rasterizer.Render(page, new RenderOptions().WithIntent(DisplayIntent.Print)); // annotations as printed

DisplayList annotations = page.CreateDisplayList(DisplayLayers.Annotations); // one layer, to draw over cached content
```
<!-- endsnippet -->

Optional content (what PDF viewers call layers) is a set of groups that a document turns on and off. A render shows the states of the document's default configuration unless you pass others:

<!-- snippet: samples/guides/Rendering/Program.cs#optional-content -->
```csharp
using PdfDocument document = PdfDocument.Open("optional-content.pdf");
PdfOptionalContentProperties layers = document.OptionalContent!; // null when the file has no optional content
PdfOptionalContentState states = layers.GetDefaultStates();
foreach (PdfOptionalContentGroup group in layers.Groups)
{
    Console.WriteLine($"Layer {group.Name}: {(states.IsOn(group) ? "on" : "off")}");
}

PdfOptionalContentGroup b = layers.Groups.First(group => group.Name == "B");
using Bitmap withB = rasterizer.Render(document.Pages[0], new RenderOptions().WithOptionalContentState(states.With(b, on: true)));
```
<!-- endsnippet -->

`PdfOptionalContentProperties.GetStates` computes the states of the document's other configurations. The states also decide which annotations and alternate images are shown.

## Hit-testing

A hit-test index answers what is under a point or inside a rectangle of the page: glyphs (with their text), images, filled and stroked paths, and links. Build it from the display list you render from, so the two always agree. Points are in page coordinates; invert the device transform to map a pixel of the view back to the page.

<!-- snippet: samples/guides/Rendering/Program.cs#hit-test -->
```csharp
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
```
<!-- endsnippet -->

Matches come by kind and then in content order, and enumerating them allocates nothing. A link match carries its annotation, destination and action.

## Color management

By default, colors in ICC-based color spaces are converted through their alternate device spaces. `UseIccColorManagement` converts them through their embedded ICC profiles instead, with the managed ICC engine in `Broadside.Rendering`. It also converts device colors through a document's output intent profile, as a print-oriented viewer does.

<!-- snippet: samples/guides/Rendering/Program.cs#color-management -->
```csharp
var managedEngine = new PdfEngine(new PdfOptions().UseIccColorManagement());
using PdfDocument document = managedEngine.Open("icc-rgb-matrix.pdf");
using Bitmap managed = rasterizer.Render(document.Pages[0]); // ICCBased colors through their embedded profiles
```
<!-- endsnippet -->

To change its settings, create the engine yourself: `new IccColorManagement().WithOutputIntents(false)` ignores output intents, `WithDefaultBlackPointCompensation(false)` turns off black point compensation where the content leaves it to the reader, and `WithRgbProfile` sets the profile of the display. Pass it to `PdfOptions.UseColorManagement`.

Overprint preview paints the page into the four process colorants and one per spot color, applies the overprint settings, and converts the result for display, as a prepress preview does:

<!-- snippet: samples/guides/Rendering/Program.cs#print-preview -->
```csharp
using PdfDocument cmyk = managedEngine.Open("output-intent-cmyk.pdf"); // CMYK through the output intent's profile
using Bitmap preview = rasterizer.Render(cmyk.Pages[0], new RenderOptions().WithOverprintPreview(true));
```
<!-- endsnippet -->

The dark mode color filter turns white paper black and dark text light while leaving photographs alone. It runs over the finished pixels and the display list does not depend on it, so a viewer switches it on and off by drawing its cached lists again. Leave it off for printing and export.

<!-- snippet: samples/guides/Rendering/Program.cs#dark-mode -->
```csharp
using Bitmap dark = rasterizer.Render(plain.Pages[0], new RenderOptions().WithColorFilter(ColorFilter.DarkMode));
```
<!-- endsnippet -->

## Determinism

The software rasterizer gives the same bytes for the same package version, page and options on every platform .NET runs natively: Linux, Windows and macOS, on x64 and Arm64, with or without hardware vector instructions. Its arithmetic avoids everything that can round differently from one machine to another, such as fused multiply-add, results that depend on the vector width, elementary functions from the platform's C library, and sums whose order depends on threads. CI renders every page of the test corpus on Linux x64, Windows x64, macOS Arm64 and with hardware intrinsics off, and compares the hashes.

<!-- snippet: samples/guides/Rendering/Program.cs#determinism -->
```csharp
RenderOptions options = new RenderOptions().WithDpi(96);
using var rasterizer = new SoftwareRasterizer();
using Bitmap bitmap = rasterizer.Render(page, options);
string hash = Convert.ToHexString(SHA256.HashData(bitmap.Pixels)); // the same on Linux, Windows and macOS, x64 and Arm64
Console.WriteLine($"SHA-256 of the pixels: {hash}");
```
<!-- endsnippet -->

This makes the rasterizer suitable for snapshot tests and for caching renders by hash. WebAssembly is not yet covered: in the browser, the elementary functions come from the runtime's own library. The platform backends anti-alias with platform code, so they match the rasterizer within the golden-image threshold, not bit for bit, whether or not `ForceOutlines` is set.

## Memory budgets

A document keeps what it decodes so that the next render of a page is faster, and drops the least recently used entries beyond a budget. Each budget is a `PdfOptions` setting; zero keeps nothing.

| Setting | Default | What it keeps or limits |
|---|---|---|
| `DisplayListCacheBudget` | 32 MiB | Display lists, charged with the images they hold, so another zoom or tile reuses the page's list. |
| `ImageCacheBudget` | 64 MiB | Decoded images, their masks and color tables. |
| `GlyphCacheBudget` | 8 MiB | Glyph outlines read from font programs. |
| `ContentCacheBudget` | 32 MiB | Decoded content streams of pages, forms, patterns and Type 3 glyphs. |
| `RangeCacheLimit` | 64 MiB | For a file read over HTTP range requests, the bytes fetched. |
| `MaxImagePixels` | 2<sup>28</sup> pixels | An image larger than this is not decoded, with a diagnostic, before any memory is taken. |
| `MaxMeshVertices` | 2<sup>22</sup> vertices | The rest of a larger mesh shading is dropped, with a diagnostic. |

`SoftwareRasterizer.GlyphCacheBudget` (4 MiB by default) is the rasterizer's own cache of glyph coverage, so that a glyph shown again is not scan converted again. The output never depends on any of these budgets. `PdfDocument.CacheStatistics` reports each cache's bytes, peak, hits, misses and evictions:

<!-- snippet: samples/guides/Rendering/Program.cs#budgets -->
```csharp
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
```
<!-- endsnippet -->

A display list you hold keeps its images alive after they leave the image cache. Bitmaps from `Render` are pooled: dispose them, or render into a `Bitmap` over memory you own.

## SkiaSharp

`SkiaBackend` renders into a new `SKBitmap` or onto any `SKCanvas`, such as the surface of a SkiaSharp view. SkiaSharp brings its native library for Windows and Apple platforms; on Linux, add `SkiaSharp.NativeAssets.Linux` (or `.NoDependencies`).

<!-- snippet: samples/guides/RenderingSkia/Program.cs#skia -->
```csharp
using PdfDocument document = PdfDocument.Open("paths-fill-rules.pdf");
PdfPage page = document.Pages[0];
using var backend = new SkiaBackend();

// A new SKBitmap of the page's pixel size, on white.
using SKBitmap bitmap = backend.Render(page, new RenderOptions());

// Or onto a canvas you already have, such as a view's surface: the page fills the canvas's top-left GetPixelSize pixels.
RenderOptions options = new RenderOptions().WithDpi(144);
PixelSize size = options.GetPixelSize(page);
using var surface = new SKBitmap(size.Width, size.Height);
using (var canvas = new SKCanvas(surface))
{
    backend.Render(page, canvas, options);
}

foreach (Broadside.Diagnostics.Diagnostic diagnostic in backend.Diagnostics)
{
    Console.WriteLine($"Drawn another way: {diagnostic}"); // what Skia could not draw itself, drawn by the software rasterizer
}
```
<!-- endsnippet -->

## CoreGraphics

`CoreGraphicsRenderer` draws into a `CGBitmapContext` (or any `CGContext`, through its display-list overload) on macOS, iOS and Mac Catalyst:

<!-- snippet: samples/guides/RenderingBackends/CoreGraphicsSample.cs#coregraphics -->
```csharp
using PdfDocument document = PdfDocument.Open(path);
PdfPage page = document.Pages[0];
var options = new RenderOptions(); // 72 DPI

// A bitmap context over your own memory: premultiplied BGRA (Apple's native layout) in sRGB, the page's pixel size.
PixelSize size = options.GetPixelSize(page);
int stride = size.Width * 4;
byte[] pixels = new byte[stride * size.Height];
using CGColorSpace srgb = CGColorSpace.CreateSrgb()!;
using var context = new CGBitmapContext(
    pixels, size.Width, size.Height, 8, stride, srgb, CGBitmapFlags.PremultipliedFirst | CGBitmapFlags.ByteOrder32Little);

using var renderer = new CoreGraphicsRenderer();
renderer.Render(page, context, options); // on white, like every page render
context.Flush();
```
<!-- endsnippet -->

## Direct2D

`Direct2DRenderer` draws through Direct2D on a Direct3D 11 device into a `Bitmap`. `Direct2DDriver.Hardware`, the default, uses the graphics card and falls back to WARP, Windows' software Direct3D, on a machine without one. `Warp` always uses WARP, which draws the same pixels on every machine:

<!-- snippet: samples/guides/RenderingBackends/Direct2DSample.cs#direct2d -->
```csharp
using PdfDocument document = PdfDocument.Open(path);
using var renderer = new Direct2DRenderer(driver); // Direct2DDriver.Hardware, or Warp for the same pixels on every machine
Bitmap bitmap = renderer.Render(document.Pages[0], new RenderOptions()); // premultiplied BGRA, on white
```
<!-- endsnippet -->

## Android

`AndroidCanvasBackend` renders into a new `Android.Graphics.Bitmap` or onto any `Canvas`, such as a view's in `OnDraw`:

<!-- snippet: samples/guides/RenderingBackends/AndroidSample.cs#android -->
```csharp
using PdfDocument document = PdfDocument.Open(pdf);
PdfPage page = document.Pages[0];
using var backend = new AndroidCanvasBackend();

// A new ARGB_8888 bitmap of the page's pixel size, on white.
Android.Graphics.Bitmap bitmap = backend.Render(page, new RenderOptions());

// Or onto a canvas you already have, such as a view's in OnDraw: the page fills its top-left GetPixelSize pixels.
RenderOptions options = new RenderOptions().WithDpi(144);
PixelSize size = options.GetPixelSize(page);
using var surface = Android.Graphics.Bitmap.CreateBitmap(size.Width, size.Height, Android.Graphics.Bitmap.Config.Argb8888!);
using (var canvas = new Android.Graphics.Canvas(surface))
{
    backend.Render(page, canvas, options);
}
```
<!-- endsnippet -->
