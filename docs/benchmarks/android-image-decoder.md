# Android image decoder: decode time, first paint and fidelity (#376)

Measured 2026-11-10 on the scratch Android emulator (Android 15, API 35, google_apis arm64-v8a, 4 GB, software GPU) on an Apple M-series Mac, with the Release device app (`tests/Broadside.Rendering.Android.Device`, `-e scaled 1`). "Managed" is the integration branch's managed DCT decoder with #362's scaled decode; "BitmapFactory" registers `AndroidImageDecoder` (`UseAndroidImageDecoder`) and awaits `AndroidCanvasBackend.PrefetchImagesAsync` before the render. The page is the generated catalogue-class cover: one 2550 x 3150 baseline DCT photograph over a letter page (`CatalogueSamples.Cover()`, public domain; never the owner's catalogue). Times in ms, medians of five runs after a warm-up. The emulator is CPU-starved (#357), so the ratios mean more than the absolute figures; an owner-device run is still to do.

Reproduce: `dotnet build tests/Broadside.Rendering.Android.Device -c Release`, then `ANDROID_SERIAL=<emulator> BROADSIDE_ANDROID_REQUIRED=1 dotnet test --project tests/Broadside.Rendering.Android.Tests -- --filter-class "*AndroidImageDecoderTests" --output detailed`; the `Decode_and_first_paint_times_against_the_managed_decoder` test prints every figure.

## Decode of the 2550 x 3150 photograph

| Chroma | Reduction | Managed | BitmapFactory | Speed-up |
|---|---:|---:|---:|---:|
| 4:4:4 | full | 183.8 | 30.2 | 6.1x |
| 4:4:4 | 1/2 | 108.2 | 21.8 | 5.0x |
| 4:4:4 | 1/4 | 87.2 | 20.5 | 4.3x |
| 4:4:4 | 1/8 | 68.9 | 18.3 | 3.8x |
| 4:2:0 | full | 146.0 | 27.8 (not used: outside the tolerance) | |
| 4:2:0 | 1/2 | 94.0 | 23.2 | 4.1x |
| 4:2:0 | 1/4 | 78.3 | 17.1 | 4.6x |
| 4:2:0 | 1/8 | 62.5 | 18.4 | 3.4x |

BitmapFactory's figure includes copying the JPEG into a Java array and reading the bitmap's pixels in place into the image's RGB samples (`AndroidBitmap_lockPixels`, no intermediate copy).

## First paint of the cover (fresh document: list, prefetch, render)

| Chroma | Width (px) | Reduction | Managed | BitmapFactory (prefetch + render) |
|---|---:|---:|---:|---:|
| 4:4:4 | 390 | 1/2 | 317 | 232 (23 + 209) |
| 4:2:0 | 390 | 1/2 | 309 | 269 (20 + 238) |
| 4:4:4 | 1080 | full | 1136 | 1043 (39 + 1007) |
| 4:2:0 | 1080 | full | 1109 | 1094 (managed: not taken at full size) |

## Reading the numbers

- BitmapFactory (libjpeg-turbo through Skia) decodes 3.4x to 6.1x faster than the managed decoder on the emulator, in line with the desktop measurement of the same engine through SkiaSharp (#374, research §5).
- A first paint gains the decode time only: at 390 pixels the decode is a quarter of the paint (25% faster with 4:4:4); at 1080 pixels converting and resampling the 8-megapixel photograph dominates and the gain is 8%. A 4:2:0 image at full size is not taken, so it paints as before.
- Memory: the decode makes one transient software bitmap of the delivered size (4 bytes per pixel), recycled as soon as its samples are read; the samples the document keeps are the same size as the managed decoder's.
- Not compared yet: SkiaSharp's decoder (#374) on the same device. The Android Canvas backend cannot use SkiaSharp (it depends only on the platform), so the decoder stays opt-in until an owner-device `FrameBench` run compares the two.

## Fidelity against the managed decoder

Each image decoded directly at each reduction and compared with the managed decode at the same reduction (the verification mode's measures: largest difference, PSNR, share of pixels more than 2 levels apart). "Edges" is the corpus's DCT test picture (ramp, dark disc, white bar: sharp chroma edges) at 45 x 29 with partial MCUs, and 203 x 131.

| Image | Full size | 1/2 | 1/4 | 1/8 | Taken |
|---|---|---|---|---|---|
| Photo 4:4:4, edges 4:4:4, gray | max 0 | max 0 | max 0 | max 0 | always |
| Photo 4:2:0 | max 30, 51.9 dB, 1.08% | max 0 | max 0 | max 0 | reduced only |
| Edges 4:2:0 (45 x 29, 203 x 131) | max 37 / 41, 33.9 / 41.1 dB | max 0 | max 0 | max 0 | reduced only |
| Photo 4:2:2 | max 23, 56.3 dB, 0.49% | max 16, 53.2 dB | max 13, 51.3 dB | max 0 | never (managed, with `ImageDecodedManaged`) |
| Edges 4:2:2 | max 12, 42.2 dB, 47.9% | 37.7 dB | 33.0 dB | max 0 | never |

- At full size libjpeg's "fancy" upsampling interpolates subsampled chroma where the managed decoder replicates it, the same difference Firefox shows (#373); the bars (max 24, 50 dB, 1% over 2 levels) fail, so `CanDecode` declines subsampled chroma at full size. Reduced, libjpeg's DCT scaling gives a 4:2:0 component a larger IDCT instead of upsampling, as the managed decoder does, and the two are bit-identical; 4:2:2 reduced is below 55 dB, so the decoder reads the frame's sampling factors and leaves anything but 4:2:0 to the managed decoder.
- ICC: a JPEG whose embedded profile swaps red and blue decodes unconverted with no `inPreferredColorSpace` (the bitmap is tagged with the profile, max 0) and converted with `inPreferredColorSpace` sRGB (max 169), so it is never set; the core zero-fills the profile's payload anyway (`ConvertsEmbeddedProfiles`), because Skia converts a profile it cannot tag a bitmap with to sRGB (AOSP `SkAndroidCodec::computeOutputColorSpace`). Exif orientation 6 is not applied (`BitmapFactory`; `ImageDecoder` would apply it and is never used).
- CMYK and YCCK (converted by Skia with an inversion assumed), 12-bit and arithmetic-coded data are never offered.
- Every image the decoder takes was bit-identical on this emulator; it declares `Tolerance` because Skia's JPEG decoder has changed across Android versions and only API 35 was measured.
