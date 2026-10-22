# Browser viewer: WebGL 2 against the software rasterizer (#353)

What GPU rendering changes in the Blazor WebAssembly viewer. The WebGL backend (`Broadside.Rendering.WebGL`) is compared with the default managed software rasterizer in the demo app, AOT and non-AOT builds, on the documents from #211/#352 that motivated the question.

## Setup

- 2026-10-17, MacBook Pro M4 Max, macOS 27, Playwright 1.58 headless Chromium (build 1208) with `--enable-gpu --ignore-gpu-blocklist --use-angle=metal`: WebGL 2 ran on the **real GPU** (`ANGLE Metal Renderer: Apple M4 Max`, 4x MSAA, max texture 16384), not SwiftShader. CI's Linux runner gets SwiftShader (software WebGL); its numbers would not mean anything, so CI only checks correctness.
- Builds: `publish-static.sh` (AOT) and `publish-static.sh --no-aot` (IL interpreter, what the hosted demo uses), Release.
- Harness: `tests/Broadside.Viewer.Blazor.BrowserTests/GpuBenchmarkTests.cs` (explicit tests), run as
  `BROADSIDE_CORPUS_DIR=<corpus> BROADSIDE_GPU_BENCH_LABEL=aot BROADSIDE_BLAZOR_APP=<published folder> dotnet test --project tests/Broadside.Viewer.Blazor.BrowserTests -- --filter-class "*GpuBenchmarkTests" --explicit only`.
  The demo opens the document through `?file=` (with `&renderer=webgl` for WebGL) in a fresh context; tile times come from the demo's timing tile backend (`DemoTileBackend`), the first sharp page from the scheduler's events (`PdfViewer.TileObserver`: the first near/ahead tile start, which the scheduler allows only once every visible tile is resident), frame times are `requestAnimationFrame` intervals while 40 wheel steps scroll (160 px each, 16 ms apart) and 3 Ctrl+wheel steps zoom in, each until 1.5 s after the gesture.
- Devices: desktop 1280x900 at 2x; "phone": 390x844 at 3x with Chromium's CPU throttling at 4x (a mid-range phone CPU; the GPU is still the M4's, so this flatters WebGL somewhat).
- Headless Chromium paces `requestAnimationFrame` at 120 Hz here, so an idle frame is 8.3 ms.

## Results

**First sharp page** is from the open request (download, parse, display list, tiles) to the visible tiles at full resolution. **Tiles** are per-tile render time on the main thread (`open`: until the first sharp page; `scroll`: during the scroll). **Fallback tiles** are tiles the software rasterizer drew whole or in part (a transparency group) because WebGL could not.

### AOT

| Device | Document | Renderer | First sharp page (ms) | Scroll frames mean / p95 / max (ms), >50 ms | Zoom frames mean / p95 / max (ms), >50 ms | Tiles mean / p95 / max (ms) | Fallback tiles | WASM heap / JS heap / textures (MiB) |
|---|---|---|---|---|---|---|---|---|
| desktop | tracemonkey | software | 255 | 8.5 / 9.2 / 33, 0 | 8.4 / 9.2 / 17, 0 | open 2.5 / 27.6 / 28; scroll 1.9 / 4.2 / 25 | 0 | 96 / 69 / 0 |
| desktop | tracemonkey | webgl | **196** | 8.5 / 9.2 / 42, 0 | 8.3 / 9.2 / 9, 0 | open 0.4 / 0.5 / 9; scroll **0.9** / 1.9 / 12 | 0 | 96 / 69 / 0.3 |
| desktop | issue5567 (JPX 1500x1125) | software | 993 | 8.3 / 9.1 / 9, 0 | 8.4 / 9.1 / 34, 0 | open 1.7 / 1.1 / 104 | 0 | 138 / 78 / 0 |
| desktop | issue5567 | webgl | **836** | 8.3 / 9.1 / 9, 0 | 8.4 / 9.1 / 34, 0 | open 0.7 / 0.1 / 52 | 0 | 138 / 78 / 8.6 |
| desktop | personwithdog | software | 198 | 8.3 / 9.2 / 9, 0 | 10.0 / 17.3 / 34, 0 | open 3.2 / 37.8 / 38 | 0 | 67 / 78 / 0 |
| desktop | personwithdog | webgl | 194 | 8.3 / 9.2 / 9, 0 | 9.8 / 16.9 / 50, 0 | open 4.3 / 41.3 / 41 | 94 (shading) | 67 / 65 / 0 |
| phone | tracemonkey | software | 1823 | 12.7 / 25.3 / 275, 8 | 14.6 / 25.5 / 150, 2 | open 32.9 / 136 / 136; scroll 18.0 / 41.6 / 66 | 0 | 96 / 69 / 0 |
| phone | tracemonkey | webgl | **1445** | 10.3 / **15.9** / 83, 9 | **9.3 / 16.7 / 25, 0** | open 15.0 / 50.8 / 51; scroll **6.7** / 17.5 / 39 | 0 | 138 / 69 / 0.3 |
| phone | issue5567 | software | 3969 | 8.3 / 9.2 / 9, 0 | 8.5 / 9.2 / 17, 0 | open 85.8 / 452 / 452 | 0 | 138 / 78 / 0 |
| phone | issue5567 | webgl | **3472** | 8.3 / 9.2 / 9, 0 | 8.4 / 9.2 / 26, 0 | open 10.3 / 1.7 / 210 | 0 | 138 / 78 / 8.6 |
| phone | personwithdog | software | 1162 | 8.3 / 9.2 / 9, 0 | 22.9 / 74.6 / 133, 13 | open 89.6 / 196 / 196 | 0 | 67 / 65 / 0 |
| phone | personwithdog | webgl | 893 | 8.3 / 9.2 / 9, 0 | 22.1 / 75.7 / 133, 13 | open 88.4 / 196 / 196 | 31 (shading) | 67 / 78 / 0 |

### Non-AOT (interpreter; the hosted demo)

| Device | Document | Renderer | First sharp page (ms) | Scroll frames mean / p95 / max (ms), >50 ms | Zoom frames mean / p95 / max (ms), >50 ms | Tiles mean / p95 / max (ms) | Fallback tiles | WASM heap / JS heap / textures (MiB) |
|---|---|---|---|---|---|---|---|---|
| desktop | tracemonkey | software | 567 | 9.9 / 16.7 / 83, 3 | 9.6 / 16.7 / 17, 0 | open 4.5 / 6.0 / 92; scroll 8.5 / 17.9 / 141 | 0 | 96 / 65 / 0 |
| desktop | tracemonkey | webgl | **402** | 9.2 / **9.2** / 83, 3 | 9.1 / 16.6 / 17, 0 | open 1.7 / 2.0 / 41; scroll **5.1** / 10.8 / 61 | 0 | 96 / 65 / 0.3 |
| desktop | issue5567 | software | 3909 | 8.3 / 9.0 / 9, 0 | 9.6 / 16.9 / 26, 0 | open 85.9 / 966 / 966 | 0 | 138 / 78 / 0 |
| desktop | issue5567 | webgl | **3280** | 8.3 / 9.0 / 9, 0 | 8.4 / 9.1 / 25, 0 | open 6.0 / 0.2 / 481 | 0 | 115 / 78 / 8.6 |
| desktop | personwithdog | software | 569 | 24.2 / 124.6 / 250, 51 | 34.8 / 141.6 / 192, 42 | open 28.5 / 296 / 296; scroll 73.4 / 201 / 253 | 0 | 67 / 65 / 0 |
| desktop | personwithdog | webgl | 509 | 22.8 / 108.8 / 250, 48 | 29.9 / 133.4 / 183, 32 | open 27.2 / 277 / 277; scroll 68.9 / 184 / 255 | 66 (shading) | 67 / 65 / 0 |
| phone | tracemonkey | software | 3670 | 28.2 / 75.0 / 517, 19 | 35.3 / 66.4 / 475, 12 | open 361 / 675 / 675; scroll 118 / 358 / 395 | 0 | 96 / 65 / 0 |
| phone | tracemonkey | webgl | **2653** | 23.6 / **41.8** / 433, **10** | 22.6 / **41.7** / 484, **2** | open **57** / 298 / 298; scroll **34** / 94 / 137 | 0 | 115 / 65 / 0.3 |
| phone | issue5567 | software | 16200 | 8.7 / 9.2 / 67, 2 | 38.5 / 183.4 / 192, 28 | open 2144 / 4090 / 4090 | 0 | 138 / 73 / 0 |
| phone | issue5567 | webgl | **13879** | 8.3 / 9.2 / 9, 0 | **8.3 / 9.2 / 9, 0** | open **97** / 1.0 / 2033 | 0 | 115 / 73 / 8.6 |
| phone | personwithdog | software | 2648 | 18.6 / 9.2 / 1300, 5 | 56.8 / 334 / 675, 10 | open 841 / 1354 / 1354 | 0 | 55 / 65 / 0 |
| phone | personwithdog | webgl | 2387 | 18.9 / 9.3 / 1325, 6 | 57.2 / 333 / 692, 20 | open 829 / 1333 / 1333 | 15 (shading) | 55 / 65 / 0 |

### Whole pages outside the viewer (pinned set, 144 DPI, median of 3 after a warm-up)

WebGL here includes reading the pixels back into a `Bitmap` (a fixed ~3-5 ms per page) and converting and uploading every image (each call reopens the file, so no texture is reused); the viewer avoids both.

| Page | AOT software / WebGL (ms) | Non-AOT software / WebGL (ms) | WebGL fell back to the rasterizer for |
|---|---|---|---|
| tracemonkey.pdf#0 | 38 / **14** | 170 / **54** | - |
| tracemonkey.pdf#1 | 52 / **20** | 301 / **84** | - |
| ccitt_EndOfBlock_false.pdf#0 | 5 / 6 | 71 / **21** | - |
| PDF 2.0 image with BPC.pdf#0 | 4 / 5 | 24 / 17 | - |
| cmykjpeg, calgray, devicen, bug_jpx, jbig2_symbol_offset, annotation-highlight, page level output intent, issue3584 | 0-5 / 1-11 | 1-48 / 1-50 | - (small pages: the read-back and upload dominate; WebGL is slower by a few ms) |
| Ghent_PDF-Output-Test-V50_CMYK_X4.pdf#0 | 439 / 467 | 4606 / 4981 | groups, shadings |
| alphatrans.pdf#0 | 9 / 24 | 111 / 165 | groups, shadings |
| personwithdog, function_based_shading, issue2948, issue6296 | same | same | shadings (whole page) |
| GWG160 (blend modes), smask_alpha_oob (soft mask), tiling-pattern-large-steps | same | same | blend mode, soft mask, tiling pattern |

9 of the 21 pinned pages fall back to the software rasterizer for at least one tile today, almost all for shadings.

## What the numbers say

- **Text and vector pages (TraceMonkey):** tiles are 2-3.5x cheaper (AOT desktop scroll 1.9 to 0.9 ms; non-AOT phone 118 to 34 ms) and the first sharp page arrives 20-30 % sooner (non-AOT phone 3.7 s to 2.7 s). On a fast desktop AOT build frames were already smooth with the rasterizer, so there is nothing visible to gain there; on the throttled phone, non-AOT, frames over 50 ms during scrolling halve (19 to 10) and the p95 frame drops from 75 to 42 ms; zooming goes from 12 long frames to 2.
- **Image pages (issue5567, #352's complaint):** the big win. The software rasterizer redraws (and resamples) the whole 1500x1125 image in every tile, in one operation slicing cannot split (#211); WebGL converts it once into a mipmapped texture and every tile and zoom level reuses it. Phone non-AOT zoom: mean frame 38.5 to 8.3 ms and 28 frames over 50 ms to none; tile time 2144 to 97 ms. The first sharp page improves only 15 %, because decoding the JPX (not drawing) dominates it.
- **Pages the GPU path does not draw yet (personwithdog: a shading):** no change, since the rasterizer draws those tiles; nothing gets worse either.
- **Memory:** about the same WASM heap (the GPU path keeps flattened glyphs, ~0.3 MiB of glyph data for TraceMonkey, and textures on the GPU side: 8.6 MiB for issue5567's image with mipmaps). The heaps vary by run more than by renderer.
- **Fidelity:** WebGL anti-aliases with 4x multisampling where the rasterizer computes exact coverage, so single edge pixels of small text differ by up to 0.55 (the `webgl` rows of `GoldenThresholds`; flat areas match within the default). At 2x and 3x device pixel ratios the difference is hard to see; at 1x small text looks slightly heavier.

## Recommendation

Keep the software rasterizer as the default and ship WebGL as an option (done: `ViewerOptions.UseBackend(WebGLTileBackend.Create)`, the demo's View panel, `?renderer=webgl`). Turn it on by default for the hosted demo and phones once shadings (axial and radial in a fragment shader) and the separable blend modes are native, since those are the fallbacks that leave pages unaccelerated; the image and text gains above already justify it for image-heavy documents on phones. The bigger lever for the hosted demo remains the AOT build (3-5x on every tile, either renderer); WebGL adds 2-3x on top for text and an order of magnitude for zooming image pages.
