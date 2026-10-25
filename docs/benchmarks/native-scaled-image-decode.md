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
- The full-resolution rows peak at 3.7 GB. Each pooled tile backend converts the photograph itself (one Skia image cache per backend), so several 32 MB conversions and their scratch arrays are alive at once. Scaled decode shrinks each of them, but the duplication is separate work (#432).

Reproduce: build the sample (`dotnet build samples/Broadside.Viewer.Maui.Sample -f net10.0-ios -c Release -p:RuntimeIdentifier=iossimulator-arm64`; add `-p:ValidateXcodeVersion=false` when Xcode is newer than the workload), install it on a simulator, and launch it with `SIMCTL_CHILD_BROADSIDE_PHOTO=1`. Optional: `SIMCTL_CHILD_BROADSIDE_PHOTO_WIDTH=0.5` (share of the window's width), `SIMCTL_CHILD_BROADSIDE_PHOTO_K=1`, `SIMCTL_CHILD_BROADSIDE_PHOTO_FULL=1` (scaled decode off). Run it with `xcrun simctl launch --console-pty`; it prints `BROADSIDE_PHOTO ready …`, then `BROADSIDE_PHOTO fit …`, and quits.
