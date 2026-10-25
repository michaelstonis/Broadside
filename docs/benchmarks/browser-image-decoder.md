# Browser image decoder: first tile and worst frame (#373)

Measured 2026-10-25 on an Apple M-series Mac (macOS 27), Playwright Chromium (headless), with `ScrollFrameTests` on a generated catalogue-class page: one 2550 x 3150 baseline 4:4:4 DCT photograph in an ICCBased (sRGB) space over a letter page (`CatalogueSamples.Cover()`, public domain; never the owner's catalogue). "Before" is the integration branch with the platform decoder seam and no decoder registered (managed DCT decode, #362's scaled decode); "after" registers `BrowserImageDecoder` (WebCodecs `ImageDecoder`). Times in ms; one run each, so frame maxima are indicative.

Reproduce: write the page with `CatalogueSamples.Cover()`, publish the sample (`publish-static.sh`, with and without `--no-aot`), then `BROADSIDE_CORPUS_DIR=<corpus> BROADSIDE_FRAME_BENCH_LABEL=<label> BROADSIDE_BLAZOR_APP=<published folder> BROADSIDE_FRAME_BENCH_DOCUMENTS=<page.pdf> dotnet test --project tests/Broadside.Viewer.Blazor.BrowserTests -- --filter-class "*ScrollFrameTests" --explicit only`.

## Page's thread (no render worker)

| Build | Device | First tile, before | First tile, after | Worst frame while loading, before | after | Worst frame while scrolling, before | after |
|---|---|---:|---:|---:|---:|---:|---:|
| AOT | desktop 1280x900 @2x | 1157 | 660 | 492 | 400 | 17 | 10 |
| AOT | phone 390x844 @3x, CPU/4 | 4028 | 1870 | 2032 | 1651 | 15 | 17 |
| Interpreter | desktop 1280x900 @2x | 5391 | 801 | 4367 | 3833 | 19 | 34 |
| Interpreter | phone 390x844 @3x, CPU/4 | 21967 | 3035 | 17865 | 1418 | 17 | 291 |

## Render worker

| Build | Device | First tile, before | after | Worst frame while loading, before | after |
|---|---|---:|---:|---:|---:|
| AOT | desktop | 755 | 740 | 131 | 133 |
| AOT | phone | 1786 | 1778 | 576 | 567 |
| Interpreter | desktop | 1140 | 924 | 125 | 125 |
| Interpreter | phone | 2057 | 1887 | 582 | 591 |

## Reading the numbers

- The decode leaves the page's thread: the first tile comes 1.7x (AOT) to 7x (interpreter) sooner, because the managed decode of the 8-megapixel photograph (72 ms AOT, 364 ms interpreter, more under the phone's CPU/4) no longer runs on it.
- At these viewports k = 2 keeps the photograph at full resolution (2550 samples over 2560 and 1170 device pixels), so `ImageDecoder` returns the YCbCr planes, bit-identical to the managed decoder. The rest of the worst frames is converting and painting 8 megapixels, which a faster decoder does not change; #362's reductions do, when the page is drawn smaller.
- With the interpreter, the "before" runs were still drawing during the 4 s loading window, so their scroll frames had little left to do; "after" draws tiles while scrolling, and converting the photograph for a tile (one operation, not sliced) shows as the 34 ms and 291 ms scroll frames.
- In the render worker the page's thread already did no decoding; the worker decodes with `ImageDecoder` too, which saves little against its total.
- Memory is unchanged (page WebAssembly plus JS heap 320 MiB AOT, 293 MiB interpreter): a tile waiting for its images holds no rasterizer, so the tiles still share one converted copy of the photograph.

## Fidelity in the browsers (verification mode)

| Browser | Path | Full size | Reduced |
|---|---|---|---|
| Chromium | `ImageDecoder` YCbCr planes | 4:4:4 and 4:2:0: max 0 (bit-identical) | 1/2: max 0 |
| WebKit 26 | `createImageBitmap` + read back (render worker; page only on request) | 4:4:4: max 3, 57.6 dB, 0.024% over 2 levels | not used (full decode) |
| WebKit 26 | same, 4:2:0 | max 27, 51.6 dB, 1.03% over 2 levels: outside the tolerance, so subsampled images decode managed | |
| Firefox | `ImageDecoder` | in CI only (Playwright Firefox does not launch on this Mac) | |
