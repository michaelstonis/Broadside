# Broadside

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/michaelstonis/Broadside/blob/main/LICENSE)
[![NuGet (prerelease)](https://img.shields.io/nuget/vpre/Broadside.svg?label=NuGet%20preview)](https://www.nuget.org/packages/Broadside)

Broadside is a fully managed .NET implementation of the PDF specification (ISO 32000-2 and its extensions). It reads, writes, edits and renders PDF files. Everything that interprets the PDF file itself (parsing, writing, fonts, image codecs, cryptography, content interpretation) is written in C# with no native dependency. Rendering goes through a display list that several backends consume: the managed software rasterizer, SkiaSharp, CoreGraphics, Android Canvas, Direct2D and WebGL 2. On top of that sit a framework-neutral viewer core and two viewer controls, one for Blazor WebAssembly and one for .NET MAUI. MIT licensed. The goal is to be the only PDF dependency a .NET application needs.

> **Broadside is in preview.** The first preview, `0.1.0-preview.1`, is on NuGet.org. The public API is not frozen yet (the v1 freeze is [#262](https://github.com/michaelstonis/Broadside/issues/262)), so expect breaking changes between previews. Read the [known issues](#known-issues-and-limitations) before you depend on it.

## Contents

- [Install](#install)
- [Packages](#packages)
- [Quick start](#quick-start)
- [What it does](#what-it-does)
- [Platform support](#platform-support)
- [Viewers and samples](#viewers-and-samples)
- [Status and roadmap](#status-and-roadmap)
- [Known issues and limitations](#known-issues-and-limitations)
- [Documentation](#documentation)
- [Building from source](#building-from-source)
- [Contributing](#contributing), [Sponsoring](#sponsoring), [License](#license)

## Install

Broadside targets `net10.0` only ([ADR 0002](https://github.com/michaelstonis/Broadside/blob/main/docs/adr/0002-target-net10-only.md)). Every package is a prerelease, so pass `--prerelease`:

```sh
dotnet add package Broadside --prerelease                    # read, write, edit; text extraction
dotnet add package Broadside.Rendering --prerelease          # software rasterizer and PNG output
dotnet add package Broadside.Fonts.Standard14 --prerelease   # glyphs for pages that use non-embedded Standard 14 fonts
dotnet add package Broadside.Layout --prerelease             # flow layout (FlowDocument, Paragraph)
```

Add the other packages from the table below as you need them.

## Packages

All 13 packages are published to NuGet.org at `0.1.0-preview.1`, with a matching [GitHub prerelease](https://github.com/michaelstonis/Broadside/releases/tag/v0.1.0-preview.1) that carries the packages and their symbol packages.

| Package | What it is | Target frameworks | Depends on |
|---|---|---|---|
| [`Broadside`](https://www.nuget.org/packages/Broadside) | The core: COS objects, the document model, filters, font program parsers, image codecs, the content interpreter, the display list and backend contract, text extraction, the layout tree, structure, forms, annotations, security handlers, signatures, canvas writing, diagnostics, dependency-injection registration | `net10.0` | `Microsoft.Extensions.*` abstractions, `System.Security.Cryptography.Pkcs` (no native code) |
| [`Broadside.Rendering`](https://www.nuget.org/packages/Broadside.Rendering) | The software rasterizer (the reference backend), the ICC colour engine, the PNG encoder | `net10.0` | core only |
| [`Broadside.Rendering.Skia`](https://www.nuget.org/packages/Broadside.Rendering.Skia) | SkiaSharp backend (CPU and GPU surfaces) | `net10.0` | SkiaSharp |
| [`Broadside.Rendering.CoreGraphics`](https://www.nuget.org/packages/Broadside.Rendering.CoreGraphics) | CoreGraphics (Quartz 2D) backend | `net10.0-macos`, `net10.0-ios`, `net10.0-maccatalyst` | Apple platform bindings |
| [`Broadside.Rendering.Android`](https://www.nuget.org/packages/Broadside.Rendering.Android) | Android Canvas backend | `net10.0-android` | Android bindings |
| [`Broadside.Rendering.Direct2D`](https://www.nuget.org/packages/Broadside.Rendering.Direct2D) | Direct2D and DirectWrite backend, bound at build time with CsWin32 | `net10.0-windows10.0.19041.0` | Windows |
| [`Broadside.Rendering.WebGL`](https://www.nuget.org/packages/Broadside.Rendering.WebGL) | WebGL 2 backend for the browser; what it cannot draw falls back to the software rasterizer, with a diagnostic | `net10.0` (browser only) | the browser; no third-party JavaScript |
| [`Broadside.Fonts.Standard14`](https://www.nuget.org/packages/Broadside.Fonts.Standard14) | Glyph data for the Standard 14 fonts (Liberation 2.1.5, Foxit Symbol and Dingbats); wire it in with `UseStandard14Fonts()` | `net10.0` | core only |
| [`Broadside.Fonts.Cmaps`](https://www.nuget.org/packages/Broadside.Fonts.Cmaps) | Adobe predefined CMaps and CID-to-Unicode tables; wire it in with `UsePredefinedCMaps()` | `net10.0` | core only |
| [`Broadside.Layout`](https://www.nuget.org/packages/Broadside.Layout) | Flow layout over the canvas API: paragraphs, lists, tables, headers and footers, broken into lines and pages, tagged by construction | `net10.0` | core only |
| [`Broadside.Viewer`](https://www.nuget.org/packages/Broadside.Viewer) | The framework-neutral viewer core: document session, page layout, tile scheduler, input model, text selection, annotation layer, accessibility bridge | `net10.0` | core and `Broadside.Rendering` |
| [`Broadside.Viewer.Blazor`](https://www.nuget.org/packages/Broadside.Viewer.Blazor) | Blazor WebAssembly viewer component (`PdfViewer`) | `net10.0` (browser only) | `Microsoft.AspNetCore.Components.Web` |
| [`Broadside.Viewer.Maui`](https://www.nuget.org/packages/Broadside.Viewer.Maui) | .NET MAUI viewer control (`PdfView`), drawn with SkiaSharp | `net10.0`, `net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst`; `net10.0-windows10.0.19041.0` from the next release ([#381](https://github.com/michaelstonis/Broadside/issues/381)) | `Microsoft.Maui.Controls`, `SkiaSharp.Views.Maui.Controls` |

The core and `Broadside.Rendering` depend only on packages Microsoft ships with no native component ([ADR 0001](https://github.com/michaelstonis/Broadside/blob/main/docs/adr/0001-fully-managed-core-with-extension-points.md)); every other backend depends on its platform library and nothing else. Every package except `Broadside.Viewer.Maui` is marked trimmable and AOT-compatible. MAUI and SkiaSharp's views are not annotated for trimming on every target.

## Quick start

This compiles and runs against the `0.1.0-preview.1` packages `Broadside`, `Broadside.Rendering`, `Broadside.Fonts.Standard14` and `Broadside.Layout`, in a `dotnet new console` project:

```csharp
using Broadside;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Layout.Flow;
using Broadside.Rendering;

// Write: a new document with one page of text.
using (PdfDocument created = PdfDocument.Create())
{
    PdfPage page = created.Pages[0]; // Create() starts with one empty US Letter page
    PdfCanvasFont helvetica = created.GetStandard14Font(Standard14Font.Helvetica);
    using (PdfCanvas canvas = page.CreateCanvas())
    {
        canvas.BeginText();
        canvas.SetFont(helvetica, 24);
        canvas.MoveText(72, 700);
        canvas.ShowText("Hello from Broadside");
        canvas.EndText();
    }

    created.Save("hello.pdf");
}

// Read: open a file (lenient by default), list what was repaired, extract text.
PdfOptions options = new PdfOptions().UseStandard14Fonts();
using PdfDocument document = PdfDocument.Open("hello.pdf", options);
foreach (Broadside.Diagnostics.Diagnostic diagnostic in document.Diagnostics)
{
    Console.WriteLine(diagnostic);
}

Console.WriteLine($"{document.Pages.Count} page(s)");
Console.WriteLine(document.Pages[0].ExtractText().Text);

// Render: page 1 to a PNG with the software rasterizer.
using var rasterizer = new SoftwareRasterizer();
using (Bitmap bitmap = rasterizer.Render(document.Pages[0], new RenderOptions().WithDpi(150)))
using (FileStream png = File.Create("page-1.png"))
{
    bitmap.SavePng(png);
}

// Flow layout: paragraphs broken into lines and flowed across pages.
using (PdfDocument report = PdfDocument.Create())
{
    report.Pages.RemoveAt(0); // the flow appends its own pages
    PdfCanvasFont body = report.GetStandard14Font(Standard14Font.TimesRoman);
    var flow = new FlowDocument(PageSetup.Letter);
    flow.Add(new Paragraph("Broadside lays this paragraph out in lines and pages.", new ParagraphStyle(body, 11)));
    FlowLayoutResult layout = flow.Render(report);
    Console.WriteLine($"{layout.Pages.Count} page(s) laid out");
    report.Save("report.pdf");
}
```

Reading is lenient by default: a damaged file is repaired and every repair is recorded as a `Diagnostic` on the document; `new PdfOptions().UseStrict()` makes the first deviation an error instead ([ADR 0005](https://github.com/michaelstonis/Broadside/blob/main/docs/adr/0005-lenient-reading-with-diagnostics.md)).

## What it does

- **Read.** Parsing with repair of damaged files (cross-reference reconstruction, `startxref` and stream `/Length` recovery), object streams, incremental-update chains, linearized files, and remote files read through HTTP range requests (`PdfDocument.OpenAsync(HttpClient, Uri)`). Decryption with the standard security handler (RC4, AES-128, AES-256, AES-GCM per ISO/TS 32003) and the public-key security handler.
- **Filters and codecs.** Flate, LZW, ASCIIHex, ASCII85, RunLength, predictors, DCT (JPEG), CCITT fax, JBIG2 and JPEG 2000, all managed.
- **Fonts.** TrueType, OpenType, CFF and CID-keyed CFF, Type 1 and Type 3, composite fonts with embedded or predefined CMaps, and a font resolver for fonts that are not embedded.
- **Render.** The content interpreter builds a display list that every backend consumes ([ADR 0006](https://github.com/michaelstonis/Broadside/blob/main/docs/adr/0006-display-list-rendering-architecture.md)). The software rasterizer covers paths, text, images, shadings, patterns and the transparency model. The ICC colour engine handles ICCBased colour spaces. Render options cover DPI or scale, rotation, a region, layers, intent, anti-aliasing, overprint preview, cancellation and progress.
- **Write.** The canvas API (`PdfPage.CreateCanvas()`), font embedding with subsetting, and the flow layout package. Output conformance: PDF/A-2b, -2u, -3b, -3u, -4 and -4f (`PdfSaveOptions.WithPdfAConformance`), PDF/UA-1 and -2 (`WithPdfUAConformance`), and Well-Tagged PDF. PDF/A-2b output is validated by veraPDF in CI.
- **Edit.** Incremental update or full rewrite, linearized output, page insertion, removal and reordering, `PdfDocument.Merge` and `Split`, annotations, form filling with regenerated appearances and flattening, and encryption on save.
- **Text and structure.** Text extraction with words, lines and reading order (`PdfPage.ExtractText()`), the layout tree (`PdfPage.ExtractLayout()`), the structure tree, and auto-tagging of untagged files (`PdfDocument.AutoTag()`).
- **Signatures.** CMS/PAdES signing through one signer interface (`PdfDocument.Sign`, `PdfCertificateSigner`; [ADR 0011](https://github.com/michaelstonis/Broadside/blob/main/docs/adr/0011-external-signer-contract.md)) and validation (`PdfDocument.ValidateSignatures`).
- **Out of scope** stays parse-and-preserve: JavaScript, XFA rendering, 3D, rich media and multimedia are read, kept and written back, never executed or rendered.

How much of each specification is implemented is in the [conformance map](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/README.md), summarized under [Documentation](#documentation).

## Platform support

| Platform | Read, write, edit and the software rasterizer | Native backend | Viewer | Exercised in CI |
|---|---|---|---|---|
| Windows | yes | Direct2D, Skia | MAUI from the next release ([#381](https://github.com/michaelstonis/Broadside/issues/381)) | build and tests; Direct2D golden images on a WARP device; Native AOT smoke |
| macOS | yes | CoreGraphics, Skia | none | build and tests; CoreGraphics golden images; Native AOT smoke |
| Linux | yes | Skia | none | build and tests; Native AOT smoke; corpus, fuzz and veraPDF jobs |
| iOS | yes | CoreGraphics, Skia | MAUI (Metal) | the MAUI sample compiles for the iOS simulator |
| Mac Catalyst | yes | CoreGraphics, Skia | MAUI (Metal) | the MAUI sample compiles |
| Android | yes | Android Canvas, Skia | MAUI (OpenGL ES) | the Android backend's golden images and a MAUI sample smoke run on an API 35 emulator |
| Browser (Blazor WebAssembly) | yes | WebGL 2 (opt-in) | Blazor | the demo's browser tests in Chrome, Firefox and WebKit, AOT and interpreted builds |

The CI jobs are described in [`.github/workflows/README.md`](https://github.com/michaelstonis/Broadside/blob/main/.github/workflows/README.md).

## Viewers and samples

Both viewer controls are shells over `Broadside.Viewer`, which owns the document session, page layout, tile scheduler, progressive zoom, input model, text selection, annotation layer and accessibility bridge. Uno, Avalonia, WPF and WinUI shells are planned (provisional issues [#222](https://github.com/michaelstonis/Broadside/issues/222) to [#225](https://github.com/michaelstonis/Broadside/issues/225)).

**Blazor WebAssembly** (`Broadside.Viewer.Blazor`). The `PdfViewer` component draws tiles into canvases and opens remote files through HTTP range requests, first page first. It has no native assets and no third-party JavaScript.

- The software rasterizer draws the tiles by default.
- WebGL 2 is opt-in: await `WebGLTileBackend.InitializeAsync()`, then pass `new ViewerOptions().UseBackend(WebGLTileBackend.Create)` ([ADR 0012](https://github.com/michaelstonis/Broadside/blob/main/docs/adr/0012-webgl2-backend-for-the-browser-viewer.md)). Thumbnails and other surfaces that need pixels read them back from the GPU on the integration branch ([#380](https://github.com/michaelstonis/Broadside/issues/380), not released yet).
- The render worker is opt-in too: a second .NET runtime in a JS Web Worker draws content tiles off the page's thread ([ADR 0013](https://github.com/michaelstonis/Broadside/blob/main/docs/adr/0013-tiles-render-off-the-ui-thread.md)). The library never starts it by itself. The app must serve it from `Main` (`RenderWorker.Serve`), start it (`RenderWorker.StartAsync`) and give each session a renderer (`ViewerOptions.UseRemoteRenderer`). It needs no cross-origin isolation and does not use .NET WebAssembly threads.

```razor
@using Broadside.Viewer.Blazor

<PdfViewer Source="@(new Uri("sample.pdf", UriKind.Relative))" Label="Sample document" Class="pdf-host" />
```

```css
/* wwwroot/css/app.css */
.pdf-host { height: 80vh; }
```

Give the viewer a size: its canvas and layers are absolutely positioned and its own stylesheet sets no height, so without one it collapses to zero height ([#393](https://github.com/michaelstonis/Broadside/issues/393) proposes a default).

**.NET MAUI** (`Broadside.Viewer.Maui`). The `PdfView` control draws with SkiaSharp. Tiles render off the UI thread ([#357](https://github.com/michaelstonis/Broadside/issues/357), ADR 0013) and are composed on the GPU: Metal on iOS and Mac Catalyst, OpenGL ES on Android, and Direct3D on Windows. Set `UseHardwareAcceleration` to false to compose on the CPU instead. Register it with `builder.UseBroadsideViewer()` in `MauiProgram`, then set `Source` to a file path or an http(s) address.

**Samples** (in [`samples/`](https://github.com/michaelstonis/Broadside/tree/main/samples)):

- [`Broadside.Viewer.Blazor.Sample`](https://github.com/michaelstonis/Broadside/blob/main/samples/Broadside.Viewer.Blazor.Sample/README.md) is a static Blazor WebAssembly app that shows the library end to end in the browser, phones included. It covers viewing, search, outline, thumbnails, annotations, forms, document information and tags, signatures, and creating PDF/A and PDF/UA documents with the layout package. It also has a renderer switch (software or WebGL) and the render worker. Run it with `dotnet run --project samples/Broadside.Viewer.Blazor.Sample`, or publish a trimmed, AOT-compiled static site with `samples/Broadside.Viewer.Blazor.Sample/publish-static.sh out/demo`.
- [`Broadside.Viewer.Maui.Sample`](https://github.com/michaelstonis/Broadside/blob/main/samples/Broadside.Viewer.Maui.Sample/README.md) is a MAUI app around `PdfView` with the same gallery, for Android, iOS and Mac Catalyst. It needs the `maui` workload. Run it with `dotnet build samples/Broadside.Viewer.Maui.Sample -f net10.0-maccatalyst -t:Run`, or with `-f net10.0-android` or `-f net10.0-ios`. It does less than the Blazor demo for now ([#358](https://github.com/michaelstonis/Broadside/issues/358)).

## Status and roadmap

The full v1 specification is [#33](https://github.com/michaelstonis/Broadside/issues/33) and the build plan is [`docs/plan/README.md`](https://github.com/michaelstonis/Broadside/blob/main/docs/plan/README.md). Each phase is a GitHub milestone. Ticket branches merge into the integration branch `spec/33-broadside-v1`, which reaches `main` in batches.

| Phase | Status |
|---|---|
| 0. Scaffold | done |
| 1. COS layer | done |
| 2. Model and codecs | done |
| 3. Rendering | done: display list, software rasterizer, ICC engine, Skia, CoreGraphics, Direct2D and Android backends, golden images, benchmarks |
| 4. Write and Edit | done: canvas, incremental update and edit, forms, flow layout, encryption on write |
| 5. Cross-cutting | done: text extraction, layout tree and auto-tagging, signatures, PDF/A and PDF/UA output, streaming sources |
| 6. Viewer | in progress. Done: viewer core, Blazor and MAUI shells. Open: search with background indexing ([#215](https://github.com/michaelstonis/Broadside/issues/215)), outline and thumbnails ([#216](https://github.com/michaelstonis/Broadside/issues/216)), form filling in the viewer ([#218](https://github.com/michaelstonis/Broadside/issues/218)), smart dark mode ([#219](https://github.com/michaelstonis/Broadside/issues/219)), and the Uno, Avalonia, WPF and WinUI shells. The Blazor demo implements search, outline, thumbnails and form filling itself, in the sample. |
| 7. Hardening and v1 release | in progress ([#231](https://github.com/michaelstonis/Broadside/issues/231)). The first preview is out. Open: coverage gaps, typed diagnostic codes, API tidy and freeze, guides, trimming and AOT verification, threat model, security review, and the comparison with PDFium and pdf.js. |
| Post-v1 | earmarked ([#232](https://github.com/michaelstonis/Broadside/issues/232)): JBIG2 extended templates, shaping for complex scripts |

"Done" means every task issue in the milestone is closed; the phase's parent issue stays open until spec #33 reaches `main`.

**On the integration branch since `0.1.0-preview.1`, not released yet:**

- Saves stay bounded on damaged files that declare huge object numbers ([#371](https://github.com/michaelstonis/Broadside/issues/371)).
- The MAUI package includes the Windows target ([#381](https://github.com/michaelstonis/Broadside/issues/381)).
- The WebGL backend reads thumbnails and other pixel surfaces back from the GPU ([#380](https://github.com/michaelstonis/Broadside/issues/380)).

## Known issues and limitations

From the [open issues](https://github.com/michaelstonis/Broadside/issues):

- **Damaged-file edits.** `AddObject`, and everything built on it (merge, split, form and annotation writers), throws on a file that already holds object number 2147483647 ([#387](https://github.com/michaelstonis/Broadside/issues/387)). Saving a damaged RC4-encrypted input can write a file that cannot be reopened ([#390](https://github.com/michaelstonis/Broadside/issues/390)). Both were found by fuzzing.
- **Large images are slow.** Images are decoded at full resolution even when drawn much smaller, and the managed DCT decoder blocks the browser's thread under the WebAssembly interpreter. The fixes are tracked in [#362](https://github.com/michaelstonis/Broadside/issues/362) (decode at the drawn resolution), [#365](https://github.com/michaelstonis/Broadside/issues/365) (smooth browser viewer without a second runtime), [#352](https://github.com/michaelstonis/Broadside/issues/352) (demo rendering), and [#372](https://github.com/michaelstonis/Broadside/issues/372) to [#377](https://github.com/michaelstonis/Broadside/issues/377) (platform image decoders).
- **Rendering speed.** Rendering is slower than PDFium on the pinned real-world pages: the geometric mean is 1.16x at 72 DPI and 1.70x at 150 DPI ([#336](https://github.com/michaelstonis/Broadside/issues/336), [baselines](https://github.com/michaelstonis/Broadside/blob/main/docs/benchmarks/phase-3-render-baselines.md)). Stroking is about 2x slower since the overlap fix ([#335](https://github.com/michaelstonis/Broadside/issues/335)). The software rasterizer has no glyph cache yet ([#370](https://github.com/michaelstonis/Broadside/issues/370)).
- **Signatures in the browser.** .NET on WebAssembly has no X.509 support, so signatures are listed but not validated there.
- **Not implemented yet.** Unencrypted wrapper documents ([#234](https://github.com/michaelstonis/Broadside/issues/234), [#235](https://github.com/michaelstonis/Broadside/issues/235)), order 3 sampled functions ([#236](https://github.com/michaelstonis/Broadside/issues/236)), and bounded memory on multi-gigabyte files ([#237](https://github.com/michaelstonis/Broadside/issues/237)).
- **Package size.** The core assembly measured 3.67 MB on 2026-10-19 ([#361](https://github.com/michaelstonis/Broadside/issues/361)) and the Direct2D backend 1.6 MB ([#360](https://github.com/michaelstonis/Broadside/issues/360)).

## Documentation

- **Site and API reference:** <https://michaelstonis.github.io/Broadside/>. Its sources are in [`site/`](https://github.com/michaelstonis/Broadside/tree/main/site); build and check it locally with `./check-docs.sh`.
- [`CONTEXT.md`](https://github.com/michaelstonis/Broadside/blob/main/CONTEXT.md): the vocabulary.
- [`docs/adr/`](https://github.com/michaelstonis/Broadside/tree/main/docs/adr): the architecture decisions and why.
- [`docs/plan/`](https://github.com/michaelstonis/Broadside/blob/main/docs/plan/README.md): phases, tracks and the issue lists.
- [`docs/conformance/`](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/README.md): what is implemented, clause by clause, checked in CI by `tools/ConformanceCheck`. In October 2026, of the 646 rows across 17 specifications, 356 are `done`, 115 `partial`, 128 `not started` and 47 `n/a`. For ISO 32000-2 alone, 272 of 433 rows are `done` and 68 `partial`. Most of the 66 `not started` rows there are introductions, out-of-scope features (multimedia, 3D, rich media), prepress (separations, trapping, OPI) and informative annexes. Run `dotnet run --project tools/ConformanceCheck -- --summary` for the current counts.
- [`docs/benchmarks/`](https://github.com/michaelstonis/Broadside/tree/main/docs/benchmarks): the [perf deep dive](https://github.com/michaelstonis/Broadside/blob/main/docs/benchmarks/perf-deep-dive.md), [WebGL against the software rasterizer](https://github.com/michaelstonis/Broadside/blob/main/docs/benchmarks/viewer-gpu-vs-software.md), [the render worker](https://github.com/michaelstonis/Broadside/blob/main/docs/benchmarks/viewer-render-worker.md), and the [Phase 2](https://github.com/michaelstonis/Broadside/blob/main/docs/benchmarks/phase-2-content-baseline.md) and [Phase 3](https://github.com/michaelstonis/Broadside/blob/main/docs/benchmarks/phase-3-render-baselines.md) baselines.
- [`docs/research/`](https://github.com/michaelstonis/Broadside/tree/main/docs/research): background reports, including the [PDF viewer landscape](https://github.com/michaelstonis/Broadside/blob/main/docs/research/pdf-viewer-landscape.md) and [scaled and native image decode](https://github.com/michaelstonis/Broadside/blob/main/docs/research/image-decode-scaled-and-native.md).
- [`CLAUDE.md`](https://github.com/michaelstonis/Broadside/blob/main/CLAUDE.md): the rules for agents working in this repository.

## Building from source

You need the .NET SDK pinned in `global.json`.

```sh
dotnet build Broadside.slnx        # everything the host can build
dotnet build Broadside.Core.slnf   # the net10.0 packages only; no workloads needed
dotnet test Broadside.Core.slnf
dotnet pack Broadside.Core.slnf -c Release   # packages land in artifacts/package/release/
```

`Broadside.Rendering.CoreGraphics` (macOS, iOS, Mac Catalyst) and `Broadside.Rendering.Android` need their .NET workloads. Each one decides at evaluation time whether the host can build it (by default: yes on macOS, no elsewhere). If it cannot, it drops out of the solution build as a no-op, so `Broadside.slnx` builds on a Linux or Windows machine without workloads. Override the decision with `-p:BroadsideBuildPlatformBackends=true|false`. `Broadside.Core.slnf` is a solution filter that leaves those two projects out entirely, for machines where the default is wrong or an IDE wants only loadable projects. `Broadside.Rendering.Direct2D` targets Windows but builds everywhere, so it is part of the core set.

`Specs/` is not committed. Place ISO 32000-2:2020, the ISO/TS 32001 through 32005 documents and the ISO 14289 (PDF/UA) documents there; the PDF Association offers free sponsored copies. Then download the free font, image and colour references listed in [`docs/specs-sources.md`](https://github.com/michaelstonis/Broadside/blob/main/docs/specs-sources.md) into `Specs/References/`.

## Contributing

People and AI agents contribute the same way: pick an issue, branch, and open a PR with a DCO sign-off. See [`CONTRIBUTING.md`](https://github.com/michaelstonis/Broadside/blob/main/CONTRIBUTING.md) for the issue, branch and review process. Conduct is covered by [`CODE_OF_CONDUCT.md`](https://github.com/michaelstonis/Broadside/blob/main/CODE_OF_CONDUCT.md). To report a vulnerability, follow [`SECURITY.md`](https://github.com/michaelstonis/Broadside/blob/main/SECURITY.md).

## Sponsoring

Broadside is free to use under the MIT license and will stay that way. Sponsorship funds the work and buys priority on bugs and features, never access to features. See [`SPONSORS.md`](https://github.com/michaelstonis/Broadside/blob/main/SPONSORS.md) for the tiers and [ADR 0008](https://github.com/michaelstonis/Broadside/blob/main/docs/adr/0008-mit-license-sponsorship-not-restriction.md) for the reasoning.

## License

[MIT](https://github.com/michaelstonis/Broadside/blob/main/LICENSE).
