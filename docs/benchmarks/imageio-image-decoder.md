# ImageIO image decoder in the CoreGraphics backend (#375)

`ImageIODecoder` puts Apple's ImageIO behind the platform image decoder seam (#372) for documents drawn with the CoreGraphics backend. This page records the measurements that decided what it decodes, and what it gains: JPEG (#375), then JPEG 2000 (#482, the last section: ImageIO decodes it faithfully and slower than the managed decoder, so it is not used).

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

First render of the cover through `CoreGraphicsBackend` at 1 point per pixel (fresh document each time, so the render decodes the photograph; scaled decode off, so the full resolution is used), best of 5: managed 224 to 228 ms, ImageIO 202 to 213 ms. Converting and drawing 8 megapixels is most of the render; the decode is the part ImageIO shortens.

### iOS simulator

The same full-size decode in an iOS app on the iOS 26.5 simulator (iPhone, on the same Mac): the cover took 95.3 ms with ImageIO against 66.8 ms managed, a 1024 x 768 photograph 9.6 ms against 6.4 ms, so ImageIO was slower there. Its output differed from the Mac's (cover 57.6 dB; the 4:2:0 cover max 30, 53.7 dB), consistent with a software decoder in the simulator. iOS devices are unmeasured: the owner's device measurement decides whether to register the decoder there.

## Decisions

- **Full size only.** Reduced requests are never offered to ImageIO: its own reduction is 45 to 53 dB (below the 55 dB bar), and a full decode averaged down is both below the bar and slower than the managed reduced decode. The managed decoder serves every reduced request, exactly.
- **Gray and 4:4:4 only** (`CanDecode` refuses subsampled chroma, so those images are never offered and leave no diagnostic).
- **Quantizer mean step at most 12**: checked on the data; a coarser image decodes with the managed codec and records `ImageDecodedManaged` (Information) once, naming the step.
- **Opt-in.** `ImageIODecoder` is not registered by any package; register it with `PdfOptions.UsePlatformImageDecoder(ImageIODecoder.Shared)` and await `CoreGraphicsBackend.PrefetchImagesAsync` before a list render.
- With these rules, verification mode over the real-world corpus records 87 `PlatformImageVerified` and no `PlatformImageMismatch`.

## JPEG 2000 (#482)

ImageIO decodes JPEG 2000 (`public.jpeg-2000`): JP2 files and bare codestreams, with `kCGImageSourceSubsampleFactor` 2, 4 and 8 giving the codestream's resolution levels dropped. This section records whether that serves JPXDecode images, measured 2026-11-14 on the same Mac (macOS 27.2) while other builds ran on it (load average 50 to 350), so the absolute times are high and noisy; the ratios are steady between runs. "Managed" is `JpxDecodeFilter` at the same reduction, timed cold (a fresh document, only the decode timed, best of 3; the first few decodes in the process also pay the managed decoder's JIT); "ImageIO" includes the no-copy `CFData`, the decode, the provider copy and the repack into the core's samples.

The images are every JPX image the core offers (the rule of #482, below) in the JPX files of the real-world corpora (found with `pdfimages -list`: 27 of 5743 files), `tests/Corpus/jpx-*.pdf`, and the 28 OpenJPEG-encoded vectors of `JpxSamples` that are 8-bit gray or RGB (23 lossless, 5 lossy).

### Fidelity against the managed decoder

The bars are the seam's `Tolerance` (full size: max 24, 50 dB, 1% over 2 levels; reduced: 55 dB). ImageIO is within them everywhere it decodes, and exact on reversible (5/3) codestreams:

| Image | Coding | Full size | 1/2, 1/4, 1/8 with the subsample factor |
|---|---|---|---|
| S2.pdf, 6 images, about 340 x 400 | 9/7, 5 levels, 5 layers, 256 x 256 tiles | max 1 to 2, 57.5 to 58.0 dB | max 1 to 2, 57.7 to 60.1 dB |
| issue5481.pdf, 1090 x 725 | 9/7, 1 level, 10 layers, precincts | max 1, 59.5 dB | 65.6 dB |
| issue5549.pdf, 1090 x 725 | 9/7, 5 levels, 10 layers, precincts | max 2, 56.1 dB | 56.3, 57.4, 58.3 dB |
| 123.pdf, 1269 x 1643 | 9/7, 5 levels, 6 layers, tiles | max 2, 57.2 dB | 56.9, 57.8, 58.2 dB |
| issue5475.pdf, 512 x 512 gray | 9/7, 1 level | max 1, 62.3 dB | 68.1 dB |
| issue5567.pdf, 1500 x 1125, bare codestream | 5/3, 5 levels | exact | exact |
| veraPDF 6-2-8-3, 640 x 480 (4 files) | 5/3, 5 levels | exact | exact |
| jp2k-resetprob.pdf, `jpx-lossless.pdf`, `jpx-subsampled.pdf` | 5/3 | exact | exact |
| the 28 OpenJPEG vectors (every progression, tiles, precincts, POC, ROI, every code-block style, tile-parts) | 5/3 and 9/7 | lossless exact, lossy max 1, at least 57.4 dB | the same, at every level the codestream has |

Other findings:
- **Sizes.** ImageIO delivers ⌈W / 2^d⌉ x ⌈H / 2^d⌉ samples for a JPEG 2000 reduction (1090 x 725 at 1/8 is 137 x 91), the core's grid, so no replicated edge is needed as it is for JPEG. It stops at a factor of 8 (a request for 1/16 delivers 1/8).
- **Averaging is not a reduction.** A full ImageIO decode box-averaged down is 16 to 34 dB from the resolution-level decode (the codestream's low-pass filter is not a box); only the subsample factor serves a reduction.
- **What ImageIO refuses**: `6-2-8-3-t03` (a 4-byte ICC profile box), `-t04` (enumerated color space 19), `-t05`, and the 212-megapixel `issue19517.pdf` (after 5.4 s; the managed decoder refuses that one at its limit too). The managed decoder decodes the first three.
- **The boxes the rule refuses** (`ImageIOJpxMeasurements.ImageIO_and_the_JP2_boxes_the_shared_rule_refuses`, on synthetic JP2 files made from the 64 x 48 OpenJPEG vector): ImageIO reads some of them as the managed decoder does and some not, so the rule keeps them all managed, the safe side. It could admit sYCC and channel definitions if ever needed.

| JP2 content (a 64 x 48 RGB codestream, or gray with a palette) | ImageIO against the managed decoder |
|---|---|
| enumerated sRGB (accepted by the rule) | exact |
| enumerated sYCC (18) | max 1, 52.9 dB: ImageIO converts it to RGB as the managed decoder does |
| enumerated CIELab (14) | max 68, 33.4 dB, 53% of pixels over 2 levels |
| a channel definition box naming the colors in reverse order | exact: ImageIO honors it |
| an ICC profile box (a 128-byte header-only profile) | ImageIO refuses the file, the managed decoder decodes it |
| a palette and component mapping box (a gray codestream, 256 entries of 3 columns) | ImageIO refuses the file, the managed decoder decodes it |
| a fragmented codestream (two fragments, in order or not) | ImageIO refuses the file, the managed decoder decodes it |

### Time

Per decode, managed against ImageIO (ms; the load made every number larger than on a quiet Mac):

| Image | Full | 1/2 | 1/4 | 1/8 |
|---|---|---|---|---|
| S2.pdf, 341 x 392 (the first three in the process, JIT cold) | 12.1 / 6.8 | 6.2 / 3.1 | 3.3 / 1.9 | 1.6 / 0.9 |
| S2.pdf, 344 x 413 (warm) | 2.2 / 6.9 | 1.5 / 3.5 | 0.9 / 2.2 | 0.4 / 1.0 |
| issue5481.pdf, 1090 x 725 | 87 / 273 | 39 / 141 | | |
| issue5549.pdf, 1090 x 725 | 54 / 416 | 37 / 300 | 12 / 40 | 3.9 / 39 |
| 123.pdf, 1269 x 1643 | 17.9 / 81.6 | 8.0 / 29.8 | 4.0 / 13.9 | 2.2 / 6.9 |
| issue5567.pdf, 1500 x 1125, lossless | 74 / 924 | 38 / 387 | 20 / 56 | 5.6 / 14.2 |
| issue5475.pdf, 512 x 512 gray | 5.1 / 40.5 | 5.3 / 12.4 | | |
| veraPDF 6-2-8-3-t01-pass-b.pdf, 640 x 480 | 4.0 / 14.1 | 3.4 / 6.1 | 2.1 / 3.0 | 0.6 / 1.1 |

ImageIO is 2.4 to 12.6 times slower than the managed decoder at full size (issue5567: 12.6) and 1.4 to 10 times slower at every reduction. The one case it wins is the first three decodes in the process, where the managed decoder is still compiling. A run at a load of 50 to 100 and one at 200 and above gave the same ratios.

### The rule and the decision

- **The shared rule (#482).** `PlatformImageEligibility` offers a JPX image to a platform decoder when it is 8-bit unsigned gray or RGB with as many components as channels, all on the image grid, no `SMaskInData` opacity, not `Indexed`, no palette, component mapping or channel definition box, no color specification but enumerated sRGB or gray, and the codestream in one piece. The request's `Reduction` is the resolution levels dropped, capped by the codestream's decomposition levels (`ImageHeader.MaxReduction`).
- **Fragmented files stay managed.** A file that stores its codestream in a fragment table (ISO/IEC 15444-2) is handed over as the file, and a platform decoder has no reason to join the pieces; the rule refuses it. (The sweep's reassembly, `JpxCodestreamReader.Locate`'s `assembled`, is what lets the managed decoder read it.)
- **`ImageIODecoder` does not take JPX.** The bars are met, but ImageIO is slower than the managed decoder on every image measured, so offering JPX would slow every render that has such an image. `CanDecode` refuses it, `DecodeAsync` and `Decode` still decode a JPX request (with `kCGImageSourceAllowableTypes` restricted to `public.jpeg-2000`), and `ImageIOJpxTests` holds the fidelity measured above, so taking JPX later (a faster ImageIO, a measurement on iOS devices) is a change to `CanDecode`. The issue can close on this evidence.

## Reproduce

```sh
dotnet build tests/Broadside.Rendering.CoreGraphics.Tests -c Release
# JPEG 2000 (#482): fidelity and time over the JPX files listed in BROADSIDE_IMAGEIO_FILES (paths under BROADSIDE_CORPUS_DIR or absolute),
# over the OpenJPEG vectors, and the boxes the rule refuses (add --filter-method "*Vectors*" or "*boxes*" for one):
BROADSIDE_IMAGEIO_LOG=/tmp/imageio-jpx.txt artifacts/bin/Broadside.Rendering.CoreGraphics.Tests/release/Broadside.Rendering.CoreGraphics.Tests.app/Contents/MacOS/Broadside.Rendering.CoreGraphics.Tests --filter-class "*ImageIOJpxMeasurements" --explicit only
# Fidelity and time per vector and reduction:
BROADSIDE_IMAGEIO_LOG=/tmp/imageio.txt artifacts/bin/Broadside.Rendering.CoreGraphics.Tests/release/Broadside.Rendering.CoreGraphics.Tests.app/Contents/MacOS/Broadside.Rendering.CoreGraphics.Tests --filter-class "*ImageIOMeasurements" --explicit only
# The corpus measurements read BROADSIDE_CORPUS_DIR and a list of files under it in BROADSIDE_IMAGEIO_FILES.
dotnet run -c Release --project bench/Broadside.Rendering.CoreGraphics.Benchmarks -- --filter '*ImageIODecode*' --job short
```
