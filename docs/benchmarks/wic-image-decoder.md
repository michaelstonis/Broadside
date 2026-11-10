# WIC image decoder: fidelity and decode time (#377)

Measured 2026-11-10 on the GitHub `windows-latest` runner (CI run 42843517692, `WicMeasurementTests`), Release build. Every JPEG of `tests/Broadside.Tests/Filters/Dct/` that the platform decoder rule allows (8-bit, 1 or 3 components, a height in the frame header, YCbCr when three components; arithmetic-coded vectors included to see whether WIC reads them), plus generated photographs (`CatalogueSamples.Jpeg`: 2550 x 3150 at 4:4:4 and 4:2:0, 1321 x 949 at 4:2:2 and 4:2:0; public domain), decoded through Windows Imaging Component two ways at full size and at 1/2, 1/4 and 1/8, each compared with the managed decoder at the reduction WIC delivered:

- **Planes**: `IWICPlanarBitmapSourceTransform.CopyPixels` into `8bppY`, `8bppCb` and `8bppCr` planes at the size `DoesSupportTransform` offers, chroma replicated and put through the managed decoder's integer YCbCr transform (`YCbCrConversion`).
- **Pixels**: `IWICBitmapSourceTransform.CopyPixels` in `24bppBGR` (or `8bppGray`) at the size `GetClosestSize` offers, repacked as RGB.

Times are the minimum of 3 runs for the large photographs, one run for the vectors.

Reproduce: the measurement runs in the Windows leg of CI (`dotnet test --project tests/Broadside.Rendering.Direct2D.Tests -- --filter-class "*WicMeasurementTests"`); it writes the full table to the job summary and to `artifacts/test-results/wic-jpeg-measurement.md` (uploaded with the test results).

## Fidelity against the managed decoder

| Path | Scaled | Worst max | Worst PSNR | Worst share over 2 levels |
|---|---|---:|---:|---:|
| Planes | no | 0 | identical | 0% |
| Planes | 1/2, 1/4, 1/8 | 0 | identical | 0% |
| Pixels | no | 34 | 39.9 dB | 31.0% |
| Pixels | 1/2, 1/4, 1/8 | 16 | 40.5 dB | 55.5% |

- **The planes are bit-identical everywhere**: every subsampling WIC has planes for (4:4:4, 4:2:2, 4:4:0, 4:2:0), baseline, extended, progressive, restart intervals, multiple scans, missing Huffman tables, and arithmetic coding (sequential and progressive), at every reduction. WIC's inverse DCT and its reduced IDCTs are libjpeg's (`-dct int`), and at a reduction its planes carry chroma at the resolution libjpeg's chroma rule gives.
- **WIC's own pixels are bit-identical without subsampling** (gray, 4:4:4, and 4:1:1, which WIC replicates) at every reduction, and faster than the planes.
- **WIC's pixels interpolate subsampled chroma** (libjpeg's "fancy" upsampling): at full size max 34 and up to 31% of pixels over 2 levels on the 227 x 149 vectors, max 29 and 1.1% on the 4:2:0 photograph. That is outside even the tolerance (max 24, 1%), so these pixels are never used.
- **No planes for 4:1:1**: `DoesSupportTransform` refuses them.
- **12-bit frames are rejected** ("The image is unrecognized", `WINCODEC_ERR_UNKNOWNIMAGEFORMAT`); the core never offers them anyway.

Decision, per the issue's bars: **Exact, on by default** (`WicImageDecoder.Fidelity`), at every reduction. A subsampled YCbCr image decodes through the planes; a gray or non-subsampled image through WIC's pixels (planes as a fallback); 4:1:1 and anything WIC rejects decode with the managed codec and an `ImageDecodedManaged` diagnostic. Arithmetic coding is declared (`PlatformImageFeatures.ArithmeticCoding`).

## Decode time, 8-megapixel photograph (2550 x 3150)

| Sampling | Reduction | Managed (ms) | WIC planes (ms) | WIC pixels (ms) | Used by default |
|---|---|---:|---:|---:|---|
| 4:4:4 | full | 125.5 | 82.9 | 56.8 | pixels: 2.2x faster |
| 4:4:4 | 1/2 | 74.3 | 45.7 | 38.6 | pixels: 1.9x |
| 4:4:4 | 1/4 | 57.3 | 35.9 | 34.0 | pixels: 1.7x |
| 4:4:4 | 1/8 | 45.7 | 28.8 | 28.4 | pixels: 1.6x |
| 4:2:0 | full | 106.9 | 71.7 | (48.4, inexact) | planes: 1.5x |
| 4:2:0 | 1/2 | 63.8 | 39.5 | 32.7 | planes: 1.6x |
| 4:2:0 | 1/4 | 48.2 | 29.9 | 27.9 | planes: 1.6x |
| 4:2:0 | 1/8 | 40.7 | 24.4 | 23.9 | planes: 1.7x |

The 1321 x 949 photographs: managed 17.0 to 18.6 ms at full size, WIC planes 11.4 to 12.9 ms.

- The planes cost about 25 ms more than WIC's pixels at full size: copying three planes and the managed replication and colour transform. It is still 1.5x faster than the managed decode, and the only exact path for subsampled chroma.
- `WicImageDecoderTests.WIC_decodes_the_8_megapixel_photograph_faster_than_the_managed_decoder` gates the managed-to-WIC ratio at full size (WIC must be faster, at 4:4:4 and 4:2:0). `WicImageDecodeBenchmarks.Managed` / `Wic` (`[MemoryDiagnoser]`, by sampling and scale) is dry-run in CI.
- The decode runs in the prefetch, before the replay: a `Direct2DRenderer` render awaits `PrefetchImagesAsync` first. Without a registered decoder the prefetch completes at once and allocates nothing (the existing `ImagePrefetchBenchmarks` gate is unchanged).

## Assembly size (#360)

The WIC surface added to the CsWin32 list (`CoCreateInstance`, `CoInitializeEx`, the factory, stream, decoder, frame and the two transform interfaces, and the GUIDs) and the decoder grow the Release `Broadside.Rendering.Direct2D.dll` from 1,631,744 to 1,653,760 bytes (+22 KB, 1.3%).
