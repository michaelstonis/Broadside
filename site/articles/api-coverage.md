---
title: "API coverage"
---

# API coverage

The [API reference](../api/readme.md) is generated from the XML documentation of every package under `src/`, built on a Linux runner. A package that targets several platforms is built for one target framework there, so this page says which one each package's reference shows and what it leaves out. The site's build check fails when a package has no reference, so this list stays complete.

| Package | Reference built from | What the reference shows |
| --- | --- | --- |
| `Broadside` | `net10.0` | The whole public API. |
| `Broadside.Rendering` | `net10.0` | The whole public API. |
| `Broadside.Rendering.Skia` | `net10.0` | The whole public API. SkiaSharp types link to Microsoft Learn. |
| `Broadside.Rendering.WebGL` | `net10.0` | The whole public API. It runs in the browser only. |
| `Broadside.Rendering.Direct2D` | `net10.0-windows10.0.19041.0` | The whole public API. The Windows targeting pack is a NuGet package, so it builds on Linux. |
| `Broadside.Rendering.Android` | `net10.0-android` | The whole public API. Android types link to Microsoft Learn. The site build installs the `android` workload. |
| `Broadside.Rendering.CoreGraphics` | Not generated | Its targets (`net10.0-macos`, `net10.0-ios`, `net10.0-maccatalyst`) need Apple workloads that only install on macOS. Its public API is listed [below](#broadsiderenderingcoregraphics). |
| `Broadside.Fonts.Standard14` | `net10.0` | The whole public API. |
| `Broadside.Fonts.Cmaps` | `net10.0` | The whole public API. |
| `Broadside.Layout` | `net10.0` | The whole public API. |
| `Broadside.Viewer` | `net10.0` | The whole public API. |
| `Broadside.Viewer.Blazor` | `net10.0` | The whole public API. It runs in the browser only. |
| `Broadside.Viewer.Maui` | `net10.0` | The whole public API. The package also targets `net10.0-ios`, `net10.0-android`, `net10.0-maccatalyst` and `net10.0-windows10.0.19041.0`. Its public API is the same on every target, and the platform handlers are internal. |

## Broadside.Rendering.CoreGraphics

Namespace `Broadside.Rendering.CoreGraphics`, for macOS, iOS and Mac Catalyst. The types in these signatures that come from other packages (`PdfPage`, `DisplayList`, `RenderOptions`, `Matrix`, `Diagnostic`) are in the API reference. `CGContext` and `CGBitmapContext` are Apple's Quartz 2D types from the .NET Apple platform bindings.

### CoreGraphicsRenderer

`public sealed class CoreGraphicsRenderer : IDisposable`

The CoreGraphics backend: it renders a page or a display list onto a Quartz 2D graphics context, natively anti-aliased. It consumes display lists only and puts every page on the same pixels as the software rasterizer, because it uses the same page mapping (`RenderOptions.GetDeviceTransform` and `RenderOptions.GetPixelSize`). TrueType and OpenType glyphs are filled by CoreGraphics' font engine; other glyphs, and every glyph when `RenderOptions.ForceOutlines` is set, are filled from the outlines in the display list. Mesh shadings (types 4 to 7) are rasterized by the backend. CoreGraphics' ColorDodge, ColorBurn and SoftLight blend modes differ from the PDF ones and are reported in `Diagnostics`. Transparency groups and soft masks are not drawn yet.

Reuse one instance for many renders: a warm replay allocates nothing per path, segment or dash. An instance renders one page at a time, so use one per thread to render in parallel.

| Member | Description |
| --- | --- |
| `CoreGraphicsRenderer()` | Initializes a renderer. |
| `void Render(PdfPage page, CGBitmapContext target, RenderOptions options)` | Renders the page into the top-left `RenderOptions.GetPixelSize` pixels of a bitmap context, on white. The context must be at least the page's pixel size (`ArgumentException` otherwise), with its user space still in pixels and the origin at the bottom left, as a new `CGBitmapContext` has it. Its graphics state is restored when the call returns. |
| `void Render(DisplayList list, CGContext context, in Matrix deviceTransform)` | Draws the list over what the context already holds, without a background, clipped to the context's current clip. `deviceTransform` maps the list's default user space to the context's current user space, for example `RenderOptions.GetDeviceTransform` for a flipped view whose user space is pixels with the origin at the top left. |
| `void Render(DisplayList list, CGContext context, in Matrix deviceTransform, bool forceOutlines)` | As above, and fills every glyph from its outline when `forceOutlines` is set. |
| `ValueTask PrefetchImagesAsync(DisplayList list, in Matrix deviceTransform, int width, int height, CancellationToken cancellationToken)` | Has the document's platform image decoders (such as `ImageIODecoder`) decode the images a list render of a `width` x `height` pixel area will draw, at the reductions this renderer will ask for. Await it before `Render(DisplayList, …)`; the render draws the same images without it, decoded by the managed codec. Completes at once without a registered decoder. |
| `IReadOnlyList<Diagnostic> Diagnostics { get; }` | What the last render could not draw exactly as the file asks, one diagnostic per kind. |
| `void Dispose()` | Ends the renderer's use. It renders no more after this. |

### ImageIODecoder

`public sealed class ImageIODecoder : IPlatformImageDecoder`

Apple's ImageIO JPEG decoder as a platform image decoder, for documents drawn with `CoreGraphicsRenderer`. It is not registered by default, because its samples are close to the managed decoder's but not identical: register it with `new PdfOptions().UsePlatformImageDecoder(ImageIODecoder.Shared)` and await `CoreGraphicsRenderer.PrefetchImagesAsync` before a list render. It decodes only what it decodes within the platform decoders' tolerance (measured on macOS 27.2): DCT images of gray or 4:4:4 samples, at their full resolution, whose quantizer is fine (about quality 90 and finer). Subsampled chroma, reduced requests, twelve-bit and CMYK data decode with the managed codec. See `docs/benchmarks/imageio-image-decoder.md` for the measurements.

| Member | Description |
| --- | --- |
| `static ImageIODecoder Shared { get; }` | The decoder to register. It keeps no state between decodes and serves every document and thread. |
| `const string DecoderName = "imageio"` | Its `Name`, for diagnostics and `PdfOptions.WithPlatformImageDecoder(name, false)`. |
| `PlatformImageFidelity Fidelity { get; }` | `Tolerance`. |
| `PlatformImageFeatures Features { get; }` | `ArithmeticCoding`. ImageIO applies no Exif orientation and converts no embedded profile, so neither segment is zero-filled. |
| `bool CanDecode(in PlatformImageRequest request)` | DCTDecode, 8-bit, 1 or 3 components that share one sampling, at reduction 0. |
| `ValueTask<DecodedImage?> DecodeAsync(ReadOnlyMemory<byte> encoded, PlatformImageRequest request, CancellationToken cancellationToken)` | Decodes synchronously on the calling thread. Throws `NotSupportedException` for a coarse quantizer, which the core records as `ImageDecodedManaged` before decoding the image with the managed codec. |
