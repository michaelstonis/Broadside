# ImageIO image decoder in the CoreGraphics backend (#375)

`ImageIODecoder` puts Apple's ImageIO behind the platform image decoder seam (#372) for documents drawn with the CoreGraphics backend. This page records the measurements that decided what it decodes, and what it gains.

Measured 2026-11-10 on an Apple M4 Max Mac (macOS 27.2) while other builds ran on it (load average 20 to 60), so absolute times are high and noisy; the ratios between rows taken together are what count. The photographs are `CatalogueSamples`' (public domain): the 2550 x 3150 catalogue-class cover, baseline 4:4:4 at quality 90, and a 5280 x 3792 4:2:0 photograph. "Managed" is the managed DCT decoder with #362's reduced IDCTs. The research report (`docs/research/image-decode-scaled-and-native.md`, §5, measured on macOS 27.0.1) found ImageIO within 55 to 66 dB of libjpeg; on macOS 27.2 it is not, as below.

## Fidelity against the managed decoder

The bars are the seam's `Tolerance`: at full size max 24 levels, at least 50 dB and at most 1% of pixels more than 2 levels apart; reduced, at least 55 dB against the managed decode at the same reduction.

Full size, the DCT vectors (`tests/Broadside.Tests/Filters/Dct`, libjpeg-turbo's test image) and the generated photographs:

| Image | Result | Within |
|---|---|---|
| cover, 4:4:4, quality 90 | max 3, 55.1 dB, 0.003% | yes |
| `encoded-gray-q90`, `encoded-rgb-q90-444`, `encoded-rgb-q100-444` | max 1 to 3, 52.7 to 62.9 dB, at most 0.12% | yes |
| `sampling-444` (cjpeg's default quality 75) | max 17, 44.2 dB, 7.9% (`sips` gives the same) | no |
| `rgb` (quality 75, no transform) | max 17, 47.5 dB, 7.8% | no |
| cover, 4:2:0, quality 90 | max 52, 49.9 dB, 1.8% | no |
| 20 MP photograph, 4:2:0 | max 50, 50.3 dB, 1.6% | no |
| `sampling-420`, `encoded-rgb-q90-420`, `progressive`, `restart-blocks` (4:2:0) | 39.7 to 44.1 dB, 24 to 30% | no |
| `arithmetic-progressive-scans` (4:4:4, quality 75, arithmetic-coded) | max 1, 85.0 dB | yes |

Two things put ImageIO outside the bar, and both can be read from the header:
- **Subsampled chroma.** ImageIO interpolates it; the managed decoder replicates it, as Adobe's DCTDecode and `djpeg -nosmooth` do.
- **Coarse quantizers.** On Huffman-coded data ImageIO's IDCT drifts from libjpeg's in proportion to the quantizer's steps. Arithmetic-coded data of the same picture decodes almost exactly, so ImageIO evidently uses another decoder for it.

Over the real-world corpus (338 files with DCT images, the first 10 pages of each; `ImageIOMeasurements.Fidelity_by_coding_over_the_real_world_corpus`), of 341 images the core's rule offers:

| Images | Offered | Within the full-size bar | Median PSNR |
|---|---:|---:|---:|
| 4:2:0, 4:2:2 | 232 | 28 | 38.5 dB |
| gray, 4:4:4 | 109 | 95 | 57.5 dB |
| gray, 4:4:4, first component's quantizer mean step at most 12 | 87 | **87** | |
| gray, 4:4:4, mean step 15 or more | 22 | 8 | |

Every gray or 4:4:4 image whose quantizer has a mean step of at most 11.5 is within the bar (36 distinct images); the failures start at 15.2. A mean step of 12 is about IJG quality 90.

Reduced, against the managed decode at the same reduction:

| Image | ImageIO `kCGImageSourceSubsampleFactor` 1/2, 1/4, 1/8 | ImageIO at full size, averaged down, 1/2, 1/4, 1/8 |
|---|---|---|
| cover, 4:4:4 | 52.6, 46.5, 47.5 dB | 52.7, 53.5, 53.3 dB |
| cover, 4:2:0 | 47.8, 46.5, 47.1 dB | 50.7, 51.4, 51.9 dB |
| 20 MP photograph, 4:2:0 | 47.7, 45.3, 56.7 dB | 50.9, 51.6, 52.1 dB |
| DCT vectors | 26 to 47 dB | 33 to 54 dB |

Neither reaches 55 dB. `kCGImageSourceSubsampleFactor` returns floor(W / s) x floor(H / s) samples, against the ceiling the core's grid has (75 x 43 at 1/2 is 37 x 21, not 38 x 22).

## Time

`ImageIODecodeBenchmarks` (`--job short`), per decode; ImageIO includes the copy and repack into the core's samples:

| Image | Reduction | Managed | ImageIO | Ratio |
|---|---|---:|---:|---:|
| cover, 4:4:4 | full | 65.8 ms | 45.8 ms | 0.70 |
| cover, 4:4:4 | 1/2 | 43.8 ms | 25.0 ms | 0.57 |
| cover, 4:4:4 | 1/4 | 36.3 ms | 30.8 ms | 0.85 |
| cover, 4:4:4 | 1/8 | 31.3 ms | 26.3 ms | 0.84 |
| photograph, 4:2:0 | full | 142.1 ms | 99.7 ms | 0.70 |
| photograph, 4:2:0 | 1/2 | 93.2 ms | 43.4 ms | 0.47 |
| photograph, 4:2:0 | 1/4 | 76.3 ms | 33.3 ms | 0.44 |
| photograph, 4:2:0 | 1/8 | 68.2 ms | 52.2 ms | 0.77 |

A quieter run of the measurement test gave ImageIO 28.5 ms against 65.1 ms for the cover at full size (0.44), and 57.8 ms against 140.4 ms for the photograph. A full ImageIO decode averaged down to 1/2, 1/4 or 1/8 took 49 to 54 ms for the cover, slower than the managed reduced decode (31 to 44 ms).

First render of the cover through `CoreGraphicsRenderer` at 1 point per pixel (fresh document each time, so the render decodes the photograph; scaled decode off, so the full resolution is used), best of 5: managed 224 to 228 ms, ImageIO 202 to 213 ms. Converting and drawing 8 megapixels is most of the render; the decode is the part ImageIO shortens.

### iOS simulator

The same full-size decode in an iOS app on the iOS 26.5 simulator (iPhone, on the same Mac): the cover took 95.3 ms with ImageIO against 66.8 ms managed, a 1024 x 768 photograph 9.6 ms against 6.4 ms, so ImageIO was slower there. Its output differed from the Mac's (cover 57.6 dB; the 4:2:0 cover max 30, 53.7 dB), consistent with a software decoder in the simulator. iOS devices are unmeasured: the owner's device measurement decides whether to register the decoder there.

## Decisions

- **Full size only.** Reduced requests are never offered to ImageIO: its own reduction is 45 to 53 dB (below the 55 dB bar), and a full decode averaged down is both below the bar and slower than the managed reduced decode. The managed decoder serves every reduced request, exactly.
- **Gray and 4:4:4 only** (`CanDecode` refuses subsampled chroma, so those images are never offered and leave no diagnostic).
- **Quantizer mean step at most 12**: checked on the data; a coarser image decodes with the managed codec and records `ImageDecodedManaged` (Information) once, naming the step.
- **Opt-in.** `ImageIODecoder` is not registered by any package; register it with `PdfOptions.UsePlatformImageDecoder(ImageIODecoder.Shared)` and await `CoreGraphicsRenderer.PrefetchImagesAsync` before a list render.
- With these rules, verification mode over the real-world corpus records 87 `PlatformImageVerified` and no `PlatformImageMismatch`.

## Reproduce

```sh
dotnet build tests/Broadside.Rendering.CoreGraphics.Tests -c Release
# Fidelity and time per vector and reduction:
BROADSIDE_IMAGEIO_LOG=/tmp/imageio.txt artifacts/bin/Broadside.Rendering.CoreGraphics.Tests/release/Broadside.Rendering.CoreGraphics.Tests.app/Contents/MacOS/Broadside.Rendering.CoreGraphics.Tests --filter-class "*ImageIOMeasurements" --explicit only
# The corpus measurements read BROADSIDE_CORPUS_DIR and a list of files under it in BROADSIDE_IMAGEIO_FILES.
dotnet run -c Release --project bench/Broadside.Rendering.CoreGraphics.Benchmarks -- --filter '*ImageIODecode*' --job short
```
