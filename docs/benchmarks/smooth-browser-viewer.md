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
- **Item 4, AOT for hosts with a 16 MB file limit.** `publish-static.sh --hot-aot` compiles ahead of time only the methods a recorded
  viewing session ran (#547; before, every method of the core library, `Broadside.Rendering` and `System.Private.CoreLib`), optimized
  for size (`-Oz`). Everything else runs in the interpreter. See "AOT build size" below.

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
| | interpreter | phone, CPU/4 | 615 | 308 (#546: 259) | ≤ 50: not met (see below) |
| | AOT (rendering path) | desktop | | **42** | ≤ 50 ✓ |
| | AOT (rendering path) | phone, CPU/4 | | 123 (#546: 117) | ≤ 50: not met (see below) |
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
about 300 ms remained. They came from the page-model loads that follow a page's first tiles. Text selection built its hit-test index
and reading order in one step (`TextSelection.LoadPageAsync`), and the accessibility bridge extracted the page's layout in one step.
#546 slices those (next section); what is left on the phone profile is the display list those loads build first.

## Page-model loads in slices (#546)

In cooperative mode a page's selectable text and accessible tree are now built a step at a time, with a yield between steps:

- the hit-test index, from the page's display list (built in slices already), in a step of its own;
- the layout the reading order comes from, and the page's logical text, each extracted in slices of the page's top-level operators
  (`PdfPage.BeginLayoutExtraction` and `PdfPage.BeginTextExtraction`, which return the trees and text `ExtractLayout` and `ExtractText`
  return; `SlicedExtractionTests`), then the tree built from the text in a last slice;
- the reading order from that tree, and the selectable text from the order and the logical text, each in a step;
- the accessibility bridge's layout, in slices as above, and its nodes in a step of their own.

An edit of the document between two slices starts a load again (`SlicedPageModelTests`). The background mode (desktop and mobile
shells) still builds each in one call on a worker, through the same code.

Measured on 2026-11-17 on the same machine and harness (load average 5 to 17), `catalogue.pdf`, phone profile (390x844 @3x, CPU/4),
Chromium; two runs each, before on the `spec/33-broadside-v1` tip the branch started from:

| Build | Worker | Rendering the first view: max frame, before | After (two runs) | Viewer's own work, after | First view presented (ms), after |
|---|---|---:|---:|---:|---:|
| interpreter (`--no-aot`) | no | 301 | 259, 260 | 260, 255 | 7838, 7908 |
| interpreter (`--no-aot`) | yes | 292 | 275, 274 | 41, 34 | 4138, 4353 |
| AOT (rendering path, `--hot-aot`) | no | 132 | 117, 124 | 119, 118 | 5974, 6191 |
| AOT (rendering path, `--hot-aot`) | yes | 108 | 101, 106 | 33, 28 | 3952, 3477 |

The phone target (50 ms) is still not met, and slicing the page-model loads moved the maximum only a little, because the longest frame
was not theirs. A probe build that timed each step (not committed) found, on the phone profile:

- **The slices of the page-model loads are short.** With the interpreter the longest was 42 ms (one slice of a layout's text run),
  the hit-test index 15 ms, the reading order 20 ms, building the selectable text 30 ms and the accessible nodes 31 ms. With AOT the
  longest was 53 ms (a layout slice), then 38 ms (building the selectable text).
- **The longest frame is one slice of a display-list build.** With the worker, the page's thread builds the list text selection
  needs, and one slice of it took 247 ms (interpreter) and 95 ms (AOT): a top-level `Do` whose image is decoded when the list is
  built (the 720 x 720 Flate image with a soft mask of page 2). Without the worker the same decode runs in a tile's list build, in the
  viewer's own frame (262 ms interpreter, 132 ms AOT).

So the 50 ms phone target now waits on build-time image decodes (the first item of "What is still one step" below), not on text
selection or accessibility.

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

Chromium, AOT of the rendering path (`--hot-aot`, #365), after:

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

- **Build-time image decodes** other than Flate (LZW, CCITT, JBIG2, inline images), and any image painted inside a form XObject or
  pattern, or as an explicit mask: decoded inside the operator that paints them. Since #566 a top-level `Do` of a Flate image and its
  soft mask is decoded before the `Do`, a 16 KB inflate chunk or 16 predictor rows per step (the LZW filter itself is one step, its
  predictor rows are not); see "Build-time Flate images (#566)" below.
- **Font programs**: parsed on first use, inside a text operator.
- **The colour converter of an ICC space**: built on first use.
- **Reductions of images decoded at build**: the box average of a large Flate image drawn small.
- **Annotation and form-field layers**: built in the last slice of their list.
- **Text selection and accessibility, in parts** (#546): one top-level operator of a layout's or a text's run, the tree built from
  a layout's text, the hit-test index, the reading order from the tree, the selectable text, and the accessible nodes of a page.
- **One wavelet level of a large JPEG 2000 tile-component.**
- **A drawing operation other than an image**: a long glyph run, a large shading or pattern fill.

## Build-time Flate images (#566)

On page 2 of the catalogue a top-level `Do` paints a 720 x 720 RGB Flate image (PNG predictor 15) with a soft mask of its size. Its
decode (inflate, predictor rows, soft mask) used to run inside the `Do`, in one step of the list build. A build in slices now decodes it
before the `Do`: `DisplayListBuilder.PrepareImage` runs `PdfImage.LoadJob` for the image and then its soft mask, whose filter chain
(`StreamDecoder.ChainJob`) inflates 16 KB and undoes 16 predictor rows per step, and the run reads the `Do` again once they are done.
Samples, list, pixels and diagnostics equal the build in one step (`SlicedImageBuildTests`, `SlicedChainDecodeTests`, fuzz target
`filter-sliced`). The hot AOT profiles were recorded again so the new methods are compiled.

Natively (JIT, M4 Max), the longest step of a sliced build of page 2 with a 2 ms budget, documents opened afresh, after the first
run: 12.7 to 16.0 ms before, 2.4 to 3.1 ms after (page 11, the other such image: 12.4 to 16.1 ms before, 2.4 to 2.5 ms after).

Browser, 2026-11-17, same harness and machine (load average 8 to 11). Before is #546 (PR #564) on the `spec/33` tip; after adds this
change. Phone, CPU/4, catalogue:

| Build | Worker | Rendering the first view: max frame, over 50 ms | Viewer's own work: max frame | First view presented (ms) |
|---|---|---|---|---:|
| interpreter | no | 233, 9 → **110**, 11 | 231 → **94** | 6828 → 7014 |
| interpreter | yes | 242, 8 → **115**, 6 | 35 → 36 | 3882 → 3816 |
| hot AOT | no | 102, 5 → 93, 5 | 104 → **60** | 3344 → 3318 |
| hot AOT | yes | 132, 4 → 140, 3 | 26 → 41 | 2668 → 2677 |

- **The Flate step is gone.** With the interpreter the longest frame halves, with and without the worker; the viewer's own longest
  tile frame falls from 231 to 94 ms (interpreter) and 104 to 60 ms (hot AOT). Desktop rows are unchanged (25 to 42 ms).
- **What remains is not this decode.** With hot AOT the longest frame while the first view renders stays at 93 ms (no worker) and
  132 to 140 ms (worker, within the run-to-run noise noted above); with the worker the page's thread draws no content, so those
  frames come from the page model's remaining steps and the runtime (the other items of "What is still one step", garbage
  collection of a 290 to 590 MiB heap). The 50 ms target on the phone profile is not met yet.

## AOT build size (item 4, #547)

`dotnet.native.wasm` of the demo:

| Build | `dotnet.native.wasm` | Speed |
|---|---:|---|
| Interpreter (`--no-aot`) | 1.3 MB | interpreter |
| Full AOT (default) | 24.5 MB | AOT |
| AOT of `Broadside`, `Broadside.Rendering`, `Broadside.Viewer` and `System.Private.CoreLib`, emcc `-Oz` | 17.5 MiB (18,340,880 bytes) | AOT on the rendering path |
| `--hot-aot` before #547: AOT of `Broadside`, `Broadside.Rendering` and `System.Private.CoreLib`, emcc and bitcode `-Oz` | 16.6 MiB (17,389,847 bytes); 16.8 MiB (17,648,670 bytes) on the #547 base | AOT on the rendering path |
| **`--hot-aot` (#547): AOT of the profiled methods of every assembly, no shared generic instances module, emcc and bitcode `-Oz`** | **11.1 MiB (11,633,317 bytes)** | AOT on every path the session ran |

- **Under the cap.** 4.4 MB under a 16,000,000-byte cap, 4.9 MiB under a 16 MiB one. The `blazor-wasm` workflow publishes the build and
  fails when the module reaches 16,000,000 bytes. Hosts without a file-size cap already serve full AOT: the documentation site
  (`.github/workflows/docs.yml`) publishes the demo trimmed and fully AOT-compiled on every push to `main`.
- **How.** Mono's AOT compiler takes a profile (`WasmAotProfilePath`) and then compiles only the methods it lists (`profile-only`);
  every other method runs in the interpreter, so nothing stops working. The profiles are recorded from the demo itself:
  - `samples/Broadside.Viewer.Blazor.Sample/hot-aot.aotprofile`: the catalogue in the viewer on the page's thread (desktop and phone
    viewports, scrolling and zooming), every sample opened from the gallery, then every page of every sample rendered with the
    software rasterizer. About 25,400 methods, 8,300 of them in the core library and `Broadside.Rendering`.
  - `samples/Broadside.Viewer.Blazor.Sample/hot-aot-worker.aotprofile`: the catalogue with the render worker, which records the page's
    side of the worker protocol (about 14,600 methods).
  - `publish-static.sh --aot-profiler` publishes the recording build (the interpreter with Mono's AOT profiler), and the explicit
    browser test `AotProfileTests.Record_the_hot_aot_profile` drives the session and writes both files (`README.md` of the sample).
- **The memory helpers are always listed.** AOT-compiled code clears and copies blocks through `System.String`'s `memset`, `memcpy`,
  `bzero` and their aligned variants. The interpreter never calls them, so no recorded session lists them, and it cannot run them
  either: the first build compiled a caller without its helper and stopped at startup with `NIY encountered in method
  string:memset`. The recorder appends them to the profile.
- **No shared module of generic instances (`WasmDedup=false`).** By default the compiler collects the generic instances every
  assembly uses into one module. With a profile, that module is left half compiled: a compiled method that calls an instance the
  session did not record throws `NotImplementedException` (the demo's form panel did, in `PdfChoiceField.SelectedIndices`, which
  calls `Enumerable.ToArray<int>`). Without the shared module each assembly keeps the instances it uses, the call enters the
  interpreter as it should, and the module is 143 KB smaller.
- **A profile does not go stale all at once.** The compiler matches methods by assembly, type, name and signature, not by the
  assembly's identity: after an unrelated change to the core library the build compiled the same methods (its module within 13
  bytes of the size before). A method renamed or added since the recording runs in the interpreter until the profiles are recorded
  again.
- **Rebuilding.** The WebAssembly SDK recompiles an assembly only when the assembly is newer than its compiled output, so the demo's
  project deletes that output when a profile is newer than the last compilation.

Each lever on the #547 base (17,648,670 bytes), published locally:

| Lever | `dotnet.native.wasm` (bytes) | Taken |
|---|---:|---|
| `UseSystemResourceKeys=true` | 17,648,661 | no: already on in the trimmed build |
| `WasmEnableSIMD=false` | 16,136,667 | no: still over, and it costs the SIMD kernels of #470 |
| AOT profile, profiled methods of `Broadside`, `Broadside.Rendering` and `System.Private.CoreLib` only (first recording) | 7,911,648 | no: the viewer's own code stays interpreted |
| AOT profile, profiled methods of those plus the viewer, Blazor and the collections (first recording) | 9,650,648 | |
| AOT profile, profiled methods of every assembly (first recording) | 10,453,651 | |
| AOT profiles of both sessions, every assembly, memory helpers listed | 11,776,811 | no: the form panel fails (see above) |
| The same without the shared generic instances module (`WasmDedup=false`, final) | **11,633,317** | yes |
| Image codecs dropped through `PdfOptions.CreateWithoutImageCodecs()` (#544) | not measured | not needed, and the demo opens JPEG 2000, JBIG2 and CCITT samples |

Frame measurements of the AOT rows, same harness and machine, back to back (load average 7 to 9). Before is the `--hot-aot` build of
the #547 base, after is this build; a second run of the after build is in brackets:

| Device | Worker | Scroll: max, over 17 ms | Loading (first tile + 4 s): max, over 50 ms | Rendering the first view: max, over 50 ms | Viewer's own work: max | First tile (ms) | First view presented (ms) |
|---|---|---|---|---|---|---:|---:|
| desktop 1280x900 @2x | no | 33, 2 → 25, 3 [25, 1] | 150, 2 → 125, 2 [135, 1] | 42, 0 → 42, 0 [42, 0] | 20 → 23 [22] | 631 → 582 [581] | 1472 → **745** [766] |
| desktop 1280x900 @2x | yes | 17, 0 → 17, 0 [17, 0] | 150, 3 → 133, 1 [142, 1] | 50, 1 → 33, 0 [42, 0] | 11 → 13 [12] | 744 → 653 [669] | 1707 → **1297** [1316] |
| phone 390x844 @3x, CPU/4 | no | 41, 33 → 41, **5** [41, 6] | 624, 12 → 567, 8 [525, 8] | 135, 8 → 127, 5 [107, 5] | 122 → 121 [106] | 2198 → 1946 [1809] | 5692 → **3573** [3317] |
| phone 390x844 @3x, CPU/4 | yes | 17, 0 → 15, 0 [17, 1] | 625, 10 → 581, 7 [499, 6] | 125, 7 → 150, 5 [133, 4] | 29 → 25 [27] | 2091 → 1818 [1666] | 3431 → **2823** [2682] |

- **The AOT rows do not get worse.** Every count of long frames falls or stays within one frame of before, and the first view
  presents sooner everywhere: by 0.7 s on the desktop and 2.1 s on the phone profile. The phone's frames over 17 ms while scrolling
  fall from 33 to 5: Blazor, the viewer and the page's text layer now run compiled code where the old build interpreted them.
- **Within run-to-run noise:** the desktop "viewer's own work" (20 → 23 and 22 ms; an earlier run of the old build measured 15 ms)
  and the longest frame while the phone with the worker renders its first view (125 → 150 and 133 ms; an earlier run of the old
  build measured 149 ms), both with fewer frames over 50 ms than before.
- **Memory is unchanged** (413 MiB page; 240-248 + 166-199 MiB with the worker; 593 MiB phone; 288 + 286 MiB phone with the worker).
- **The demo's own browser tests pass on this build** in Chromium and WebKit (`DemoTests`, `SampleDocumentTests` and the rest of
  `Broadside.Viewer.Blazor.BrowserTests`, 73 passed). The old `--hot-aot` build timed out in `DemoTests` at the PDF/A conformance check,
  which it interpreted.

## #352 targets

#352 asked for concrete targets, measured as above (catalogue page, page's thread, no worker unless stated):

| # | Target | Now |
|---|---|---|
| 1 | Desktop: no frame over 50 ms while the first view renders, interpreter and AOT | 25 / 42 ms ✓ |
| 2 | Desktop: no frame over 50 ms while scrolling, AOT | 33 ms ✓ (interpreter: one frame of 67 ms) |
| 3 | Phone, CPU/4, AOT: no frame over 50 ms while scrolling | 42 ms ✓ |
| 4 | Phone, CPU/4, AOT: no frame over 50 ms while the first view renders | 117 to 124 ms (#546): a build-time image decode remains (see above) |
| 5 | Phone, CPU/4, interpreter: no frame over 100 ms while scrolling or rendering | 425 to 442 / 259 ms (#546): build-time image decodes remain |
| 6 | First view presented: desktop ≤ 2 s; phone (CPU/4) ≤ 6 s with AOT, ≤ 8 s with the interpreter | 1.6 s, 5.8 s, 7.9 s ✓ |
| 7 | AOT build that serves the rendering path at AOT speed from hosts with a 16 MiB file limit | 11.1 MiB (#547) ✓ |
