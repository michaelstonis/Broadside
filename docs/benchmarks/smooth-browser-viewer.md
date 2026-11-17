# Browser viewer without a second runtime: sliced work and a smaller AOT build (#365, #352)

Issue #365 makes the default browser viewer (one .NET runtime, no WebAssembly threads, no render worker) smooth on its own. Items 1
and 2 (decoding images at the drawn resolution, #362; the browser's own JPEG decoder, #372 and #373) were merged before. This page
measures items 3 and 4 and sets #352's targets from the measurements.

- **Item 3, sliceable long operations.** A cooperative frame no longer runs a whole long operation at once:
  - a display list builds a few top-level operators at a time (`PdfPage.BeginDisplayList`);
  - an image decodes one DCT MCU row, or one JPEG 2000 code-block or wavelet level, at a time (`DisplayImage.ContinueDecode`);
  - a tile render first decodes the images it draws and converts them to pixels, a row at a time, before its first operation;
  - a large image is painted a band of device rows at a time.

  Each step checks the slice's deadline and resumes where it stopped. Output is byte-identical to the unsliced path:
  `SlicedImageDecodeTests` covers every DCT and JPEG 2000 layout, and `SlicedPreparationTests` covers samples, lists and pixels.
- **Item 4, AOT for hosts with a 16 MB file limit.** `publish-static.sh --hot-aot` compiles ahead of time only the rendering path: the
  core library, `Broadside.Rendering` and `System.Private.CoreLib`, optimized for size (`-Oz`). Everything else runs in the
  interpreter.

## Setup

- Date and machine: 2026-11-14, MacBook Pro M4 Max, macOS 27, Playwright's headless Chromium and WebKit 26. The machine was shared
  with other builds (load average 13 to 40), so single runs vary by a few tens of percent.
- Harness: `tests/Broadside.Viewer.Blazor.BrowserTests/ScrollFrameTests.cs` (explicit), on `samples/documents/pdf/catalogue.pdf` at
  the fitted zoom. Its cover is a 2049 x 2050 4:2:0 baseline JPEG over the whole page.
  - Run it with `BROADSIDE_CORPUS_DIR=<corpus> BROADSIDE_FRAME_BENCH_LABEL=<label> BROADSIDE_BLAZOR_APP=<published folder> BROADSIDE_FRAME_BENCH_DOCUMENTS=$PWD/samples/documents/pdf/catalogue.pdf`.
  - Add `BROADSIDE_FRAME_BENCH_BROWSER=webkit` for WebKit. WebKit has no CPU throttling and no mobile wheel, so its "phone" is the
    phone viewport at full speed.
- New columns (this ticket):
  - **Rendering the first view** is every `requestAnimationFrame` interval from the viewer's first frame until the last tile of the
    first view arrives (no tile for 3 s afterwards). Render-ahead tiles are included.
  - **Viewer's own work** is the longest frame the viewer measured around its own tile scheduler and drawing.
  - **First view presented** is the time from navigation to that last tile.
- Before: the `sweep/7b` base, with the platform image decoder (`ImageDecoder` in Chromium; WebKit decodes JPEG with the managed
  codec on the page's thread). After: this branch.

## Measures (#365)

| Measure | Build | Device | Before | After | Target |
|---|---|---|---:|---:|---|
| Max frame while rendering the catalogue page, page's thread (ms) | interpreter | desktop | 68 | **25** | ≤ 50 ✓ |
| | interpreter | phone, CPU/4 | 615 | 308 | ≤ 50: not met (see below) |
| | AOT (rendering path) | desktop | | **42** | ≤ 50 ✓ |
| | AOT (rendering path) | phone, CPU/4 | | 123 | ≤ 50: not met (see below) |
| | interpreter, WebKit | desktop | 126 | **63** | ≤ 50: close |
| Longest frame of the viewer's own tile work (ms) | interpreter | desktop | 62 | **18** | |
| | interpreter | phone, CPU/4 | 609 | 264 | |
| | AOT (rendering path) | phone, CPU/4 | | 122 | |
| Max frame while scrolling (ms) | interpreter | phone, CPU/4 | 1008 | **442** | |
| | interpreter, WebKit | phone viewport | 1182 | **149** | |
| | AOT (rendering path) | phone, CPU/4 | | **42** | |
| Time to fully present the first view, page 1 (ms) | interpreter | desktop | 1609 | 1629 | ≤ 2000 |
| | interpreter | phone, CPU/4 | 7175 | 7925 | ≤ 8000 |
| | AOT (rendering path) | desktop | | 1557 | ≤ 2000 |
| | AOT (rendering path) | phone, CPU/4 | | 5800 | ≤ 6000 |
| Page's WebAssembly + JS heap, no worker (MiB) | interpreter | desktop / phone | 413 / 612 | 441 / 495 | recorded |
| Page + worker WebAssembly, worker path (MiB) | interpreter | desktop / phone | 240 + 166 / 240 + 239 | same | recorded |

The worker path stays opt-in (`ViewerOptions.UseRemoteRenderer`). On the phone profile it moves the tile work off the page's thread:

| Measure (phone, CPU/4) | No worker | Worker |
|---|---:|---:|
| Viewer's own work while rendering: max frame (ms), interpreter | 264 | 34 |
| Max frame while rendering (ms), interpreter | 308 | 302 |
| First view presented (ms), interpreter | 7925 | 4327 |
| First view presented (ms), AOT (rendering path) | 5800 | 3472 |
| Memory: page + worker (MiB) | 495 | 240 + 239 |

On the phone the remaining long frames are no longer tile work. With the worker, the page's thread renders no content, yet frames of
about 300 ms remain. They come from the page-model loads that follow a page's first tiles. Text selection builds its hit-test index
and reading order in one step (`TextSelection.LoadPageAsync`), and the accessibility bridge extracts the page's layout in one step.
Both are content runs outside this ticket's list (JPEG, JPEG 2000, colour conversion, list build). Their display list is now built in
slices too (`DocumentSession.GetDisplayListAsync` yields between the slices of any layers' build), but the index, the reading order and
the layout analysis are still one step each. Slicing them, or deferring them to idle time, is what is left of the 50 ms target on the
phone profile.

## Raw tables

Chromium, interpreter (`--no-aot`), before:

| Device | Worker | Scroll: max, over 17 ms | Loading (first tile + 4 s): max, over 50 ms | Rendering the first view: max, over 50 ms | Viewer's own work: max | First tile (ms) | First view presented (ms) | Page wasm+JS / worker wasm (MiB) |
|---|---|---|---|---|---|---:|---:|---|
| desktop 1280x900 @2x | no | 117, 5 | 117, 2 | 68, 1 | 62 | 661 | 1609 | 413 / - |
| desktop 1280x900 @2x | yes | 17, 0 | 125, 2 | 67, 1 | 9 | 699 | 2278 | 240 / 166 |
| phone 390x844 @3x, CPU/4 | no | 1008, 64 | 615, 18 | 615, 14 | 609 | 2710 | 7175 | 612 / - |
| phone 390x844 @3x, CPU/4 | yes | 34, 1 | 560, 13 | 543, 9 | 40 | 2102 | 4811 | 240 / 239 |

Chromium, interpreter, after:

| Device | Worker | Scroll: max, over 17 ms | Loading (first tile + 4 s): max, over 50 ms | Rendering the first view: max, over 50 ms | Viewer's own work: max | First tile (ms) | First view presented (ms) | Page wasm+JS / worker wasm (MiB) |
|---|---|---|---|---|---|---:|---:|---|
| desktop 1280x900 @2x | no | 67, 4 | 125, 1 | 25, 0 | 18 | 624 | 1629 | 441 / - |
| desktop 1280x900 @2x | yes | 18, 1 | 132, 2 | 75, 1 | 10 | 721 | 2487 | 240 / 166 |
| phone 390x844 @3x, CPU/4 | no | 442, 17 | 600, 17 | 308, 11 | 264 | 2321 | 7925 | 495 / - |
| phone 390x844 @3x, CPU/4 | yes | 35, 1 | 566, 16 | 302, 12 | 34 | 1969 | 4327 | 240 / 239 |

Chromium, AOT of the rendering path (`--hot-aot`), after:

| Device | Worker | Scroll: max, over 17 ms | Loading (first tile + 4 s): max, over 50 ms | Rendering the first view: max, over 50 ms | Viewer's own work: max | First tile (ms) | First view presented (ms) | Page wasm+JS / worker wasm (MiB) |
|---|---|---|---|---|---|---:|---:|---|
| desktop 1280x900 @2x | no | 33, 2 | 167, 2 | 42, 0 | 20 | 660 | 1557 | 413 / - |
| desktop 1280x900 @2x | yes | 17, 1 | 152, 2 | 50, 0 | 11 | 902 | 1881 | 248 / 166 |
| phone 390x844 @3x, CPU/4 | no | 42, 37 | 650, 11 | 123, 7 | 122 | 2198 | 5800 | 593 / - |
| phone 390x844 @3x, CPU/4 | yes | 33, 1 | 649, 11 | 158, 7 | 29 | 2134 | 3472 | 288 / 286 |

WebKit 26, interpreter. Here JPEG decodes with the managed codec on the page's thread, which is where sliced decoding matters most:

| Viewport | Worker | Before: scroll max, over 17 ms | After | Before: rendering max, over 50 ms | After | Before: first view (ms) | After |
|---|---|---|---|---|---|---:|---:|
| desktop 1280x900 @2x | no | 118, 40 | 121, 24 | 126, 3 | 63, 2 | 2100 | 2768 |
| desktop 1280x900 @2x | yes | 19, 16 | 19, 20 | 59, 1 | 97, 1 | 2167 | 2137 |
| phone 390x844 @3x | no | 1182, 56 | 149, 26 | 133, 6 | 137, 2 | 1920 | 2767 |
| phone 390x844 @3x | yes | 19, 27 | 19, 23 | 171, 3 | 220, 3 | 2233 | 2216 |

The "Loading" column counts every frame from navigation on, so it includes booting the runtime and opening the document (up to about
650 ms on the phone profile). It is kept for comparison with `viewer-render-worker.md` and `browser-image-decoder.md`. The rendering
columns start at the viewer's first frame.

## Natively (the same cooperative scheduler under the JIT)

The development probe drove a cooperative `TileScheduler` over every page of `catalogue.pdf` with an 8 ms frame budget, at a 390 x 844
viewport. Before, the longest frame was 250 to 315 ms, almost all of it one tile slice drawing the cover photograph: decode,
conversion and paint in one operation. After, the longest frame was about 65 ms. That frame is a single build step: a top-level `Do`
whose image is decoded when the list is built (a 720 x 720 Flate image with a soft mask, pages 2 and 11) or whose font program is
parsed the first time it is used. Slices otherwise end within a row of their budget. The browser interpreter is 15 to 30 times
slower than the JIT, which is why those steps still show on the CPU/4 profile.

## What is still one step

- **Build-time image decodes** (Flate, LZW, CCITT, JBIG2, inline images): decoded inside the `Do` operator that paints them.
- **Font programs**: parsed on first use, inside a text operator.
- **The colour converter of an ICC space**: built on first use.
- **Reductions of images decoded at build**: the box average of a large Flate image drawn small.
- **Annotation and form-field layers**: built in the last slice of their list.
- **Text selection and accessibility**: the hit-test index, reading order and layout analysis of a page (see above).
- **One wavelet level of a large JPEG 2000 tile-component.**
- **A drawing operation other than an image**: a long glyph run, a large shading or pattern fill.

## AOT build size (item 4)

`dotnet.native.wasm` of the demo:

| Build | `dotnet.native.wasm` | Speed |
|---|---:|---|
| Interpreter (`--no-aot`) | 1.3 MB | interpreter |
| Full AOT (default) | 24.5 MB | AOT |
| AOT of `Broadside`, `Broadside.Rendering`, `Broadside.Viewer` and `System.Private.CoreLib`, emcc `-Oz` | 17.5 MiB (18,340,880 bytes) | AOT on the rendering path |
| `--hot-aot`: AOT of `Broadside`, `Broadside.Rendering` and `System.Private.CoreLib`, emcc and bitcode `-Oz` | **16.6 MiB (17,389,847 bytes)** | AOT on the rendering path (measured above) |

- **Still over the cap.** The `--hot-aot` build misses a 16 MiB per-file cap by 0.6 MiB, and a 16,000,000-byte cap by 1.4 MB.
- **`System.Private.CoreLib` must stay compiled.** Interpreting it while compiling the core fails to link: the AOT code needs CoreLib's
  allocation and vtable wrappers.
- **The core library is most of what remains.** Its 2.9 MB of IL reaches the PDF/A converter, signature validation and the writers.
  These are the remaining levers, in order:
  1. #361's pay-for-play trimming, which removes code the viewer never reaches from the core's AOT image.
  2. A profiled AOT build (`WasmAotProfilePath`, Mono's AOT profiler), which compiles only the methods a recorded session ran.
  3. Dropping SIMD (`WasmEnableSIMD=false`) for that build only, which costs the kernels of #470.
- **Hosts without a file-size cap already serve full AOT.** The documentation site (`.github/workflows/docs.yml`) publishes the demo
  trimmed and fully AOT-compiled on every push to `main`, so GitHub Pages hosts an AOT build. The other half of item 4 is done there.

## #352 targets

#352 asked for concrete targets, measured as above (catalogue page, page's thread, no worker unless stated):

| # | Target | Now |
|---|---|---|
| 1 | Desktop: no frame over 50 ms while the first view renders, interpreter and AOT | 25 / 42 ms ✓ |
| 2 | Desktop: no frame over 50 ms while scrolling, AOT | 33 ms ✓ (interpreter: one frame of 67 ms) |
| 3 | Phone, CPU/4, AOT: no frame over 50 ms while scrolling | 42 ms ✓ |
| 4 | Phone, CPU/4, AOT: no frame over 50 ms while the first view renders | 123 ms: page-model loads remain (see above) |
| 5 | Phone, CPU/4, interpreter: no frame over 100 ms while scrolling or rendering | 442 / 308 ms: page-model loads remain |
| 6 | First view presented: desktop ≤ 2 s; phone (CPU/4) ≤ 6 s with AOT, ≤ 8 s with the interpreter | 1.6 s, 5.8 s, 7.9 s ✓ |
| 7 | AOT build that serves the rendering path at AOT speed from hosts with a 16 MiB file limit | 16.6 MiB: 0.6 MiB over |
