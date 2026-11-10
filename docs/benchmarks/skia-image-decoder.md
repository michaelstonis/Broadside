# SkiaSharp's JPEG decoder behind the platform image decoder seam (#374)

`SkiaImageDecoder` (`Broadside.Rendering.Skia`) decodes DCT images with SkiaSharp's `SKCodec`, which wraps libjpeg-turbo, before a viewer's Skia tiles draw them. It scales in the DCT to the reduction the tile asks for. The MAUI `PdfView` registers it for the engine it builds; `PdfOptions.UseSkiaImageDecoder()` registers it anywhere else. This page records its fidelity and speed against the managed decoder.

## Fidelity: bit-identical where it is used

Measured with `PdfOptions.VerifyPlatformImageDecoder`, which decodes every image both ways and compares them. libjpeg-turbo's integer IDCTs (full size and reduced) and its YCbCr transform are the ones the managed decoder ports, so the samples are identical except in one case. libjpeg-turbo upsamples 2:1 chroma by interpolation ("fancy" upsampling), while the managed decoder replicates it, as Adobe's DCTDecode does. That happens for 4:2:0 at full size, and for 4:2:2 and 4:4:0 down to 1/4. At 1/2 and below, libjpeg-turbo decodes 4:2:0 chroma with a larger IDCT at the output's resolution, and at 1/8 it never interpolates.

The two decoders differ when the chroma is interpolated:

| Image | Reduction | Max | PSNR | Pixels over 2 levels |
|---|---|---:|---:|---:|
| `testorig.jpg` (227 x 149, 4:2:0) | 1/1 | 34 | 39.9 dB | 31% |
| `sampling-420.jpg` (75 x 43) | 1/1 | 12 | 43.4 dB | 29% |
| `sampling-422.jpg` | 1/1, 1/2, 1/4 | 7, 7, 9 | 46.3, 42.7, 40.5 dB | 15%, 36%, 56% |
| `sampling-440.jpg` | 1/1, 1/2, 1/4 | 7, 9, 9 | 45.9, 42.1, 40.9 dB | 16%, 36%, 43% |
| Generated photograph (1320 x 948, 4:2:0) | 1/1 | 30 | 51.9 dB | 1.075% |

Each of these misses the issue's tolerance bar (max 24, 50 dB, 1% over 2 levels at full size; 55 dB reduced). The research's "max 23, 56.5 dB" for the feature files did not hold on these vectors. So the decoder never takes those cases. The request now carries the chroma ratios (`PlatformImageRequest.ChromaHorizontalSubsampling` and `ChromaVerticalSubsampling`), and `CanDecode` follows libjpeg-turbo's own rule (`jdmaster.c`, `jdsample.c`) to decline them.

Everything it does take is bit-identical:
- **Committed DCT vectors:** all 27 eligible vectors at 1/1, 1/2, 1/4 and 1/8. These cover baseline, extended, progressive and arithmetic coding, restarts, and 4:4:4, 4:2:2, 4:4:0, 4:2:0, 4:1:1 and gray.
- **Generated photographs:** 4:4:4 at every reduction, 4:2:0 from 1/2 down, and 4:2:2 at 1/8.
- **Fetched corpora:** the 338 files that contain DCTDecode, at six device scales and up to five pages each. That made 1601 Skia decodes: all max 0, with no mismatch and no fallback.

The decoder therefore declares `PlatformImageFidelity.Exact`, and the MAUI viewer turns it on by default. The Skia golden pages render byte for byte the same with it on and off (`SkiaImageDecoderTests.Skia_renders_with_the_decoder_on_equal_those_with_it_off`).

Other measured behavior:
- **Color space:** a null destination color space means no conversion. A JPEG tagged with a gamma 1.8 profile came back unconverted.
- **Orientation:** Skia reports Exif orientation 6 but does not apply it.
- **Coding:** arithmetic coding decodes.
- **Left to the managed codec:** 12-bit data, and CMYK and YCCK, are never offered.
- **Rejected data:** data that Skia stops short on (a truncated scan) decodes with the managed codec and records `ImageDecodedManaged`.

SkiaSharp 4.153.1 binds no YUV planes API. `libSkiaSharp` exports no `sk_codec_*yuva*` function, and the managed `SKCodec` has no planes member. So a full-size 4:2:0 image cannot be read as planes and replicated, as the browser decoder does; it stays managed. Skia's JPEG codec also has no RGBX output: `Rgb888x` returns `InvalidConversion`. The decoder therefore reads RGBA and drops the alpha byte.

## Decode time (macOS)

BenchmarkDotNet `--job short`, Release, macOS 27, Apple M4 Max, 2026-11-10. The photograph is `CatalogueSamples.Jpeg()`, 2550 x 3150, baseline, quality 90. "Managed" is `DctScaledBenchmarks`; "Skia" is `SkiaImageDecodeBenchmarks`. Skia at 4:2:0 1/1 is a bound only, because the viewer does not offer that case.

| Sampling | Scale | Managed | Skia | Speed-up |
|---|---|---:|---:|---:|
| 4:4:4 | 1/1 | 64.6 ms | 22.1 ms | 2.9x |
| 4:4:4 | 1/2 | 42.1 ms | 17.9 ms | 2.3x |
| 4:4:4 | 1/4 | 35.9 ms | 17.0 ms | 2.1x |
| 4:4:4 | 1/8 | 30.0 ms | 15.8 ms | 1.9x |
| 4:2:0 | 1/1 | 58.7 ms | (19.6 ms) | (not used) |
| 4:2:0 | 1/2 | 37.8 ms | 15.4 ms | 2.5x |
| 4:2:0 | 1/4 | 31.5 ms | 14.3 ms | 2.2x |
| 4:2:0 | 1/8 | 27.4 ms | 14.6 ms | 1.9x |

The managed decode allocates 288 B per call and Skia's 443 B (the codec's wrappers). Neither allocates per row: the band of RGBA rows and the samples are pooled, and `SkiaImageDecoderTests.A_decode_allocates_per_image_not_per_row` asserts that a 64-row and a 1024-row decode allocate the same.

## First tile (macOS)

`SkiaFirstTileBenchmarks`: a fresh document of the generated page (`CatalogueSamples.Cover()`, the photograph over a letter page in an ICCBased sRGB space) is opened, its display list built, the tile's images prefetched, and the top-left 256-pixel tile drawn with the Skia backend (scaled decode on, k = 2). Same machine and job.

| Page width | Sampling | Reduction | Managed | Skia |
|---:|---|---|---:|---:|
| 1206 px | 4:4:4 | 1/1 | 248.8 ms | 205.9 ms |
| 1206 px | 4:2:0 | 1/1 (not offered) | 240.8 ms | 269.4 ms, ±23.5 ms (noise: the same managed path) |
| 390 px | 4:4:4 | 1/4 | 85.0 ms | 58.3 ms |
| 390 px | 4:2:0 | 1/4 | 82.1 ms | 57.0 ms |

The first tile's remaining time is the image's conversion and resampling to the device, not the decode.

The issue named the first tile of `issue12841_reduced.pdf` at 1206 px as the gate. That file is a 5280 x 3792 baseline 4:2:0 DCT image in the pdf.js corpus, which CI does not fetch, so it was measured with a throwaway harness: median of 8 runs, each a fresh document. The page draws the image about nine times wider than the page, mostly clipped, so neither width asks for a reduction. The 4:2:0 image is therefore left to the managed decoder at full size (no verification diagnostic at either width), and the decoder changes nothing:

| Width | Managed | Skia |
|---:|---:|---:|
| 1206 px | 175 ms | 179 ms |
| 390 px | 365 ms | 369 ms |

Drawn small enough to reduce, Skia decodes it exactly at 1/8 (verified, max 0).

## MAUI viewer (iOS simulator)

Measured 2026-11-10 with the MAUI sample's photo bench (`PhotoBench.cs`), Release build for the iOS simulator (iPhone, 402 x 874 points at 3 pixels per point) on an Apple M4 Max host, same method as `native-scaled-image-decode.md`. Every row uses the same build with scaled decode on (k = 2). "Before" is `BROADSIDE_PHOTO_DECODER=managed`, which decodes as the integration branch did; "after" uses SkiaSharp's decoder. One launch per measurement, two launches each, both shown.

- **Tile total** is the tiles' own draw time. With Skia, the decode moves out of the tiles into the prefetch.
- **Prefetch** is the longest prefetch: the Skia decode, run once on the thread pool while the tiles that need it wait.
- **Idle** is the time from opening the document until the view had drawn everything it shows.

| Photograph | View width | Reduction | Tile total (ms), before | after | Prefetch (ms), after | Idle (ms), before | after | Peak RSS (MB), before | after |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|
| 4:4:4 | 402 pt (full) | none | 5809, 5853 | 4520, 4386 | 36, 38 | 491, 474 | 435, 401 | 811, 814 | 824, 819 |
| 4:4:4 | 201 pt (half) | 1/2 | 1238, 1274 | 860, 838 | 29, 28 | 187, 188 | 174, 165 | 760, 759 | 761, 764 |
| 4:2:0 | 402 pt (full) | none (not offered) | 5739, 5705 | 5647, 5523 | — | 490, 474 | 464, 456 | 817, 812 | 819, 814 |
| 4:2:0 | 201 pt (half) | 1/2 | 1435, 1376 | 795, 781 | 27, 28 | 198, 197 | 174, 153 | 762, 766 | 758, 763 |

What the numbers say:
- **Where Skia decodes, the page is idle 8% to 20% sooner, and the tiles' own work falls by 25% to 45%.** The decode is a small share of a page: the photograph's conversion to BGRA and its resampling to the device cost more than the decode, on either path.
- **Memory does not change.** The decoded samples are the same size on either path.
- **A full-width 4:2:0 view is unchanged.** At full resolution its chroma would be interpolated, so the image is left to the managed decoder.

A first build ran the decode synchronously inside `DecodeAsync`. That build was slower than managed at half width (idle 231 to 322 ms against 173 to 191 ms), and it kept more managed memory afterwards. A decode that has completed by the time `DecodeAsync` returns is never in flight for other tiles to join, so every tile that reached the image decoded it. Measured in a test: eight concurrent prefetches made eight decodes. The decoder now runs the decode through `Task.Run`, and `SkiaImageDecoderTests.Tiles_that_reach_the_image_at_once_share_one_decode` holds it to one. The ImageIO and Android decoders have the same exposure; #484 tracks fixing it in the core.

Android and Windows devices are unmeasured. The fidelity tests run on every CI leg that runs `Broadside.Rendering.Skia.Tests` (macOS, Linux and Windows), because a SkiaSharp update that changed libjpeg-turbo would show there first.

Reproduce: build the sample as in `native-scaled-image-decode.md`, then launch it with `SIMCTL_CHILD_BROADSIDE_PHOTO=1`. For the "before" rows, add `SIMCTL_CHILD_BROADSIDE_PHOTO_DECODER=managed`. `SIMCTL_CHILD_BROADSIDE_PHOTO_SAMPLING=420` switches the photograph to 4:2:0, and `SIMCTL_CHILD_BROADSIDE_PHOTO_WIDTH=0.5` gives the half-width view.
