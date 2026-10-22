# Browser viewer: the render worker (#357)

What the opt-in render worker (`RenderWorker`) costs and what it buys. By default the Blazor viewer draws every tile on the page's thread. An app can opt in to a second .NET runtime in a Web Worker, which renders content tiles and hands them back as `ImageBitmap`s. The library never starts the worker by itself. To use it, an app does all three of these:

- serves it from `Main` (`RenderWorker.Serve`);
- starts it (`RenderWorker.StartAsync`);
- gives each session a renderer (`ViewerOptions.UseRemoteRenderer(RenderWorker.Open(...))`).

The demo opts in; `?worker=0` turns the worker off.

## Setup

- 2026-10-19, MacBook Pro M4 Max, macOS 27, Playwright 1.58 headless Chromium. AOT build (`publish-static.sh`), Release, served without COOP/COEP (as GitHub Pages serves it).
- Harness: `tests/Broadside.Viewer.Blazor.BrowserTests/ScrollFrameTests.cs` (explicit), run as
  `BROADSIDE_CORPUS_DIR=<corpus> BROADSIDE_FRAME_BENCH_LABEL=aot BROADSIDE_BLAZOR_APP=<published folder> dotnet test --project tests/Broadside.Viewer.Blazor.BrowserTests -- --filter-class "*ScrollFrameTests" --explicit only`.
- What each run does:
  - opens the document through `?file=` (range requests) in a fresh context, once with the worker and once with `&worker=0`;
  - waits 4 s, then zooms in on issue5567 (3 Ctrl+wheel steps);
  - scrolls with 60 wheel steps, 16 ms apart (120 px on the desktop, 60 px on the phone);
  - records every `requestAnimationFrame` interval during the scroll, and separately for 1.5 s after it.
- Devices: desktop 1280x900 at 2x, and a "phone" at 390x844 and 3x with Chromium's CPU throttling at 4x. Headless Chromium paces `requestAnimationFrame` at 120 Hz here. An idle frame is therefore 8.3 ms, and "over 17 ms" means at least one missed frame.
- **Worker boot** runs from creating the worker until its runtime is ready and served (`globalThis.broadsideRenderWorker.bootMs`). The page has already downloaded and compiled the framework files, so this is instantiation, not download.
- **First tile** runs from navigation to the first tile on the canvas, so it includes the worker's boot.
- **Memory** is the size of each runtime's WebAssembly memory (the managed heap, which grows and never shrinks), plus the page's JS heap (`performance.memory`). Script cannot see a worker's JS heap; it holds the transferred bitmaps only briefly.
- Documents: TraceMonkey (14 text pages); pdf.js issue5567 (one large JPX image, zoomed in); issue12841_reduced (the heavy page from #211, with a 768 ms display-list build).

## Results (AOT)

| Device | Document | Worker | Frames while scrolling | Mean | p95 | Max | Over 17 ms | Settling 1.5 s after: max, over 17 ms | Worker boot (ms) | First tile (ms) | Page wasm+JS / worker wasm (MiB) |
|---|---|---|---|---|---|---|---|---|---|---|---|
| desktop 1280x900 @2x | tracemonkey.pdf | no | 237 | 8.5 | 8.9 | 25 | 1 | 9, 0 | - | 577 | 206 / - |
| desktop 1280x900 @2x | tracemonkey.pdf | yes | 240 | 8.4 | 9.0 | 17 | 0 | 18, 1 | 109 | 647 | 206 / 67 |
| desktop 1280x900 @2x | issue5567.pdf | no | 241 | 8.4 | 9.1 | 17 | 1 | 9, 0 | - | 1085 | 297 / - |
| desktop 1280x900 @2x | issue5567.pdf | yes | 241 | 8.4 | 9.1 | 17 | 0 | 9, 0 | 98 | 1401 | 288 / 138 |
| desktop 1280x900 @2x | issue12841_reduced.pdf | no | 241 | 177.1 | 475.0 | 484 | 130 | 475, 4 | - | 2329 | 2003 / - |
| desktop 1280x900 @2x | issue12841_reduced.pdf | yes | 235 | 8.3 | 9.9 | 10 | 0 | 10, 0 | 102 | 1903 | 361 / 620 |
| phone 390x844 @3x, CPU/4 | tracemonkey.pdf | no | 247 | 9.6 | 15.7 | 109 | 9 | 10, 0 | - | 2177 | 206 / - |
| phone 390x844 @3x, CPU/4 | tracemonkey.pdf | yes | 239 | 8.4 | 10.0 | 16 | 0 | 118, 3 | 98 | 1678 | 206 / 67 |
| phone 390x844 @3x, CPU/4 | issue5567.pdf | no | 240 | 8.4 | 9.2 | 18 | 1 | 9, 0 | - | 4447 | 288 / - |
| phone 390x844 @3x, CPU/4 | issue5567.pdf | yes | 240 | 8.4 | 9.2 | 18 | 1 | 9, 0 | 104 | 4407 | 288 / 138 |
| phone 390x844 @3x, CPU/4 | issue12841_reduced.pdf | no | 235 | 259.6 | 2008.2 | 2026 | 44 | 9, 0 | - | 9312 | 1298 / - |
| phone 390x844 @3x, CPU/4 | issue12841_reduced.pdf | yes | 237 | 8.3 | 9.2 | 9 | 0 | 9, 0 | 109 | 3237 | 361 / 620 |

## Reading it

- **Cost:**
  - The worker boots in about 100 ms once the page has loaded the framework.
  - It adds 67 MiB (TraceMonkey) to 138 MiB (issue5567) of WebAssembly memory for its copy of the document and its caches.
  - On issue12841 its memory reaches 620 MiB, because that page builds a very large display list. In exchange, the page's own heap stays at 361 MiB instead of growing to 1.3–2 GiB.
  - On light documents the first tile comes only slightly later (+70 ms on TraceMonkey on the desktop). On the heavy page it comes sooner, because the page no longer builds the list itself.
- **Benefit:**
  - With the worker, no document had more than one frame over 17 ms while scrolling, on either device.
  - Without it, the heavy page is unusable: 130 long frames up to 484 ms on the desktop, and 44 up to 2 s on the phone.
  - Without it, TraceMonkey on the phone drops 9 frames (up to 109 ms).
  - issue5567 is smooth either way at this zoom, now that #352 made image draws cheaper.
- **What still runs on the page's thread with the worker:** text selection and the accessibility layout of the pages in view. With the worker, the viewer starts them only once the view has been still for 300 ms. They appear in the "settling" column: up to 118 ms on the phone after TraceMonkey's scroll stops.
- **The default path** (no worker) is to be made smooth on its own by follow-up work: scaled image decode (#362), browser-native image decode and sliceable decode. The worker then stays an option for the heaviest documents, not a requirement.
