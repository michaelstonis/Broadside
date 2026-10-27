# Scaled image decode on the native backends (#428)

#362 made the software rasterizer decode a large image at the resolution it is drawn (a power-of-two reduction that keeps k = 2 samples per device pixel by default). #428 brings it to the native backends. This page records the measurements, one section per backend as each lands.

The page is the generated catalogue-class page: one 2550 x 3150 baseline 4:4:4 DCT photograph in an ICCBased (sRGB) space over a letter page (`CatalogueSamples.Cover()`, public domain; never the owner's catalogue).

## Skia: the MAUI viewer (iOS simulator)

Measured 2026-10-25 with the MAUI sample's photo bench (`PhotoBench.cs`), Release build for the iOS simulator (iPhone, 402 x 874 points at 3 pixels per point, iOS 26.5) on an Apple M4 Max host. "Before" is the integration branch at 0cd2290e (the Skia backend converts every image at its own resolution); "after" is this branch with the default `SkiaTileBackend()` (scaled decode, k = 2). One launch per row, so each starts from a fresh process; two launches each, both shown.

- **Tile total**: the sum of every tile's draw time (tiles draw concurrently, so it is CPU work, not wall time).
- **Idle**: from opening the document until the view had drawn everything it shows.
- **Peak RSS**: the app's resident memory while the viewer drew, sampled from the host every 250 ms (iOS does not report it to the app). Encoding the photograph before the viewer opens leaves the process at about 780 MB in every run.

The view is narrowed to show the page smaller: at full width the page is 1180 pixels wide, so 2550 samples give 2.2 per pixel and k = 2 keeps the full resolution. This is #362's note that a phone at 3 pixels per point does not reduce the cover at fit width.

| View width | Page width (px) | Reduction | Tiles | Tile total (ms), before | after | Longest tile (ms), before | after | Idle (ms), before | after | Peak RSS (MB), before | after | Decoded-image cache (MB), before | after |
|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 402 pt (full) | 1180 | none (k = 2) | 31 | 9199, 10559 | 6815, 8666 | 758, 795 | 566, 696 | 1176, 1290 | 992, 1107 | 3565, 3780 | 3742, 3738 | 34 | 36 |
| 201 pt (half) | 590 | 1/2 | 10 | 3967, 3855 | 1373, 1313 | 419, 418 | 159, 145 | 474, 497 | 254, 221 | 1911, 1880 | 1237, 1238 | 34 | 12 |
| 100 pt (quarter) | 294 | 1/4 | 5 | 1659, 1624 | 287, 313 | 335, 329 | 58, 63 | 364, 355 | 108, 161 | 1166, 1175 | 829, 825 | 34 | 4 |

With k = 1, which the owner may choose (`ViewerOptions.WithImageSamplesPerPixel(1)`), a full-width view reduces to 1/2. Tile total 2062 ms (from 9199 to 10559), longest tile 182 ms, idle 760 ms, peak RSS 1991 MB (from about 3.7 GB). A half-width view reduces to 1/4: 670 ms, 76 ms, 124 ms, 983 MB.

Reading the numbers:
- **When a reduction applies, each tile costs 2.7x (1/2) to 5.5x (1/4) less, and the page is idle 2x to 3x sooner.** Peak memory falls by 0.65 GB and 0.35 GB, and the decoded samples kept fall by 3x and 9x.
- **Without a reduction nothing changes.** The full-width rows differ only by noise between launches (the two "before" launches differ by 1.4 s).
- The full-resolution rows peak at 3.7 GB. Each pooled tile backend converts the photograph itself (one Skia image cache per backend), so several 32 MB conversions and their scratch arrays are alive at once. Scaled decode shrinks each of them, but the duplication is separate work (#432, measured below).

Reproduce: build the sample (`dotnet build samples/Broadside.Viewer.Maui.Sample -f net10.0-ios -c Release -p:RuntimeIdentifier=iossimulator-arm64`; add `-p:ValidateXcodeVersion=false` when Xcode is newer than the workload), install it on a simulator, and launch it with `SIMCTL_CHILD_BROADSIDE_PHOTO=1`. Optional: `SIMCTL_CHILD_BROADSIDE_PHOTO_WIDTH=0.5` (share of the window's width), `SIMCTL_CHILD_BROADSIDE_PHOTO_K=1`, `SIMCTL_CHILD_BROADSIDE_PHOTO_FULL=1` (scaled decode off). Run it with `xcrun simctl launch --console-pty`; it prints `BROADSIDE_PHOTO ready …`, then `BROADSIDE_PHOTO fit …`, and quits.

## One conversion per session: the pooled tile backends share converted images (#432)

Measured 2026-10-27, same page, sample, simulator (iPhone, 402 x 874 points at 3 pixels per point) and method as above, Release builds of the integration branch at efa152c3 ("before") and of #432's branch ("after"), both with the default `SkiaTileBackend()` (scaled decode). One launch per row; two launches each, both shown. "Managed" is the managed heap after a full collection once the view was idle.

| View width | k | Tiles | Tile total (ms), before | after | Longest tile (ms), before | after | Idle (ms), before | after | Peak RSS (MB), before | after | Managed (MB), before | after |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 402 pt (full) | 2 | 31 | 14829, 11529 | 5561, 5754 | 1052, 874 | 401, 401 | 1115, 926 | 482, 480 | 3562, 3712 | 826, 819 | 4096, 4061 | 112, 111 |
| 402 pt (full) | 1 | 31 | 4467, 3223 | 2406, 2059 | 327, 251 | 180, 155 | 397, 321 | 258, 223 | 2117, 2124 | 748, 753 | 2084, 2086 | 53, 57 |
| 201 pt (half) | 2 | 10 | 1364, 1465 | 1283, 1289 | 147, 164 | 134, 132 | 195, 196 | 162, 163 | 1234, 1180 | 768, 772 | 653, 645 | 60, 62 |

What changed:
- **Every pooled backend converted the photograph itself.** The session draws up to one tile per core but one at once (15 on this host), each with its own `SkiaBackend` and its own image cache, so each made its own 32 MB BGRA conversion and its own resampling to the device. Now the backends of a session share one cache per document (`SkiaBackend.ShareConvertedImages`, on in `SkiaTileBackend`; `SoftwareRasterizer.ShareConvertedImages`, on in `SoftwareTileBackend`): the first backend to miss an image converts it under that image's lock while the others wait, then all draw the same conversion, leased while drawn. The kept pixels stay within one `ImageCacheBudget` for the whole pool, the two largest over it outside it, as before; a finer conversion still serves a coarser request.
- **The resampling's scratch held a row of sums per sample row of the image.** Resampling the 2550 x 3150 photograph to 1180 pixels kept 5 doubles per output column for every one of its 3150 rows: 150 MB, rented from the shared array pool, which rounds it to 256 MB and keeps it per thread afterwards. The 2 to 4 GB managed heap left once the view was idle is consistent with one such array kept per worker thread (both changes landed together, so the split is inferred, not measured). The resampling now works in bands of at most 64 sample rows (about 3 MB here), with the same pixels.
- **The peak is now about the process before the viewer opened plus what one page needs** (encoding the photograph before the viewer opens leaves the process at about 780 MB). The idle time halves at full width; tile total falls further because the tiles no longer convert the same image at once (time waiting for the one conversion still counts in each tile).

The Blazor viewer draws on the page's thread one tile at a time (a cooperative session), so its software rasterizers rarely converted the same image twice; its memory does not change. Interpreter build, Playwright Chromium, render worker off, `ScrollFrameTests` with the same page: page WebAssembly plus JS heap 304 MiB before, 293 MiB after (desktop 1280 x 900 at 2x); 294 and 293 MiB (phone 390 x 844 at 3x, CPU/4). With the worker on, the page holds 178 to 184 MiB and the worker 139 to 140 MiB, before and after.

Reproduce the browser figures: write the page with `CatalogueSamples.Cover()`, publish the sample with `publish-static.sh --no-aot`, then run `ScrollFrameTests` as described in `browser-image-decoder.md` with `BROADSIDE_FRAME_BENCH_DOCUMENTS` set to the page.

## CoreGraphics (macOS)

The CoreGraphics backend has no tile backend or viewer shell yet, so it was measured on macOS directly: `CoreGraphicsRenderer` list renders of the same page into a `CGBitmapContext` of the page's pixel size, Release build of the CoreGraphics test app, macOS 27.0.1 on an Apple M4 Max, 2026-10-27. "Before" is `ScaledImageDecode` off, which draws exactly what the integration branch drew (the hashes of 116 page renders of the scaled-image pages, the cover and 20 corpus image files at four scales are identical before and after); "after" is `ScaledImageDecode = true` with the default k = 2 unless shown. Median of 7 runs, each with a fresh document and renderer.

- **First render**: the first render of the page's list, which decodes the photograph (at the reduction asked for) and converts it to a CGImage.
- **Allocated**: managed bytes allocated by that first render (the decoded samples and the converted pixels).
- **Kept**: the converted pixels the renderer keeps afterwards (the CGImage's pixels and its resampling to the device).

| Page width (px) | Reduction | First render (ms), before | after | Allocated (MB), before | after | Kept (MB), before | after | Pixels converted, before | after |
|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 1180 | none (k = 2) | 299 | 282 | 69.7 | 69.7 | 37.5 | 37.5 | 8,032,500 | 8,032,500 |
| 1180 | 1/2 (k = 1) | 299 | 122 | 69.7 | 22.7 | 37.5 | 14.5 | 8,032,500 | 2,008,125 |
| 590 | 1/2 | 249 | 97 | 64.4 | 17.4 | 32.4 | 9.4 | 8,032,500 | 2,008,125 |
| 390 | 1/2 | 236 | 95 | 63.4 | 16.5 | 31.4 | 8.4 | 8,032,500 | 2,008,125 |
| 294 | 1/4 | 229 | 52 | 63.1 | 4.4 | 31.1 | 2.3 | 8,032,500 | 502,744 |
| 150 | 1/8 | 221 | 36 | 62.8 | 1.1 | 30.8 | 0.6 | 8,032,500 | 125,686 |

Reading the numbers:
- **When a reduction applies, the first render is 2.4x (1/2) to 6x (1/8) faster, and the renderer allocates and keeps 4x to 50x less.** A warm render (the conversion kept) takes under 1 ms either way, so the gain is in opening and zooming.
- **Without a reduction nothing changes** (the first row; the difference is noise).
- The CoreGraphics backend has no pooled tile backends, so #432's shared cache does not apply; the converted image is kept per `CoreGraphicsRenderer`, keyed by the reduction delivered (a finer conversion serves a coarser request, so zooming out converts nothing again).

Reproduce: a throwaway xUnit fact in `tests/Broadside.Rendering.CoreGraphics.Tests` that opens `CatalogueSamples.Cover()`, builds the page's display list and renders it with `new CoreGraphicsRenderer { ScaledImageDecode = …, ImageSamplesPerPixel = … }` into a flipped `CGBitmapContext`, timing the first `Render` and reading `GC.GetTotalAllocatedBytes(true)` around it; run with `dotnet test --project tests/Broadside.Rendering.CoreGraphics.Tests -c Release`.

## Direct2D (Windows)

Not measured: Direct2D needs Windows, and no Windows machine was available for this part (#428 part 5). The MAUI sample on Windows draws with Skia, not Direct2D, and Direct2D has no tile backend, so there is no viewer to measure it in yet. The only figures available come from CI.

The Windows CI job (GitHub-hosted `windows-latest` runner, WARP software device, Release build) prints the time of one list render of the catalogue cover with a new `Direct2DRenderer` (`Direct2DScaledImageDecodeTests`). Each render decodes the DCT photograph, converts it and draws the page once. The reduced render runs first, so the full one does not benefit from a warm decode. These are single runs on a shared runner: they show the size of the effect, not a benchmark.

| Page width (px) | Reduction (k = 2) | Render (ms), full | reduced | Converted pixels kept (MB), full | reduced |
|---:|---|---:|---:|---:|---:|
| 390 | 1/2 | 583 | 205 | 31.4 | 8.4 |
| 300 | 1/4 | 607 | 97 | 31.1 | 2.4 |
| 150 | 1/8 | 578 | 63 | 30.8 | 0.6 |

"Kept" is the renderer's image cache: the converted BGRA pixels plus their resampling to the device.
