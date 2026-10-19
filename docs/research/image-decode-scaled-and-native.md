# Scaled image decode and native image decoders (research, 2026-10-19)

The owner asked for fact-finding before any implementation. This report covers two questions:

- How much is saved by decoding an image only at the resolution it is drawn at (#362), and how to do it without losing fidelity?
- Where can a platform's native or hardware decoder do the work, and how would it plug in without breaking ADR 0001 (no native dependency in the core)?

Every number in this report was measured on this branch's code, or on a throwaway prototype of it, unless it says otherwise. The prototypes lived in the session scratchpad and are deleted. Nothing here changes the library.

Issues that come out of this report:
- **#362**: updated (scaled decode).
- **#372**: platform image decoder seam, ADR 0015.
- **#373**: browser.
- **#374**: SkiaSharp.
- **#375**: Apple ImageIO.
- **#376**: Android.
- **#377**: Windows WIC.

## Summary

1. **On image pages, decode is not where most of the time goes.** What costs most is everything done per decoded pixel afterwards: sample conversion, the colour pipeline, and the rasterizer's box filter.
   - The owner's 2550×3150 catalogue cover (DCT, ICCBased) takes 49 ms to decode on an M4 (JIT).
   - Drawing that page at phone width takes another 145–170 ms.
   - In the browser, under the .NET interpreter, the same page takes **4.3 s** (0.39 s decode, 3.9 s render).
   - **Decoding at a reduced size cuts both parts.** At 1/2, 1/4 and 1/8 the page takes 1.5 s, 0.76 s and 0.20 s (interpreter), and 175, 96 and 30 ms (WebAssembly AOT).
2. **Scaled DCT decode should port libjpeg-turbo's reduced IDCT (`jidctred.c`) and its chroma rule (`jdmaster.c`).**
   - That output is within 55–57 dB of "decode fully, then box-average". The textbook N-point IDCT of the low coefficients reaches only 46–49 dB.
   - It gives a byte-exact test oracle in `djpeg -scale M/8 -dct int -nosmooth`.
   - For progressive JPEGs at 1/8 whose every component reduces to one sample per block, the AC scans can be skipped, which is **8× faster** with identical output.
3. **Choose the reduction so the decoded image still has at least k samples per device pixel. k = 2 is proposed (an owner decision).**
   - Page-level differences against a full decode at k ≈ 2–3 are 36–54 dB.
   - That is smaller than the difference between our full-decode output and poppler's (26–37 dB).
4. **JPX: dropping resolution levels is the large lever.**
   - OpenJPEG on `issue5567.pdf`: 340 ms full, 129 ms (−1 level), 63 ms (−2), 45 ms (−3).
   - Our managed decoder takes 475 ms full, so it is only 1.4× OpenJPEG's single-thread time.
5. **Native decoders measured on this Mac, for the 8-megapixel cover:**
   - WebCodecs `ImageDecoder` in Chrome 155: 15.5 ms, off the page's thread.
   - SkiaSharp `SKCodec`: 14.9 ms.
   - ImageIO: 18.2 ms.
   - Managed: 49 ms (JIT), 72 ms (WebAssembly AOT), 364 ms (WebAssembly interpreter).
   - **Chrome's `ImageDecoder` returns the YCbCr planes for a full-size decode.** Replicating chroma and applying our integer YCbCr→RGB transform gives **bit-identical** output to the managed decoder on all eight files tried, the 8-megapixel cover included.
6. **Fidelity, per decoder:**
   - Skia equals libjpeg-turbo exactly. That is bit-identical to us for 4:4:4 and differs at chroma edges for 4:2:0 (fancy upsampling: at most 23 levels, 0.48% of pixels over 2 levels).
   - ImageIO is never bit-exact: at most 3 levels at 4:4:4, and its 1/4 scaled output is at 46 dB.
   - No native decoder tried returns usable raw CMYK. Skia, Chrome and WebKit convert CMYK to RGB. ImageIO returns CMYK that differs from ours (35 dB).
   - Chrome cannot decode 12-bit or arithmetic-coded JPEGs, nor one corpus file (`issue12841_reduced.pdf`) that we decode. A managed fallback is required.
7. **Recommendation, in order:**
   1. Scaled decode in the managed codecs (#362). It helps every platform and is the precondition for the rest.
   2. The browser native path (#373) behind a new asynchronous seam (#372, ADR 0015). It is the only place native decode is a large win, 5–23× and off the thread.
   3. SkiaSharp `SKCodec` for the MAUI/Skia shells (#374), about 3×.
   4. ImageIO, Android and WIC (#375, #376, #377) only if device measurements beat the Skia path.

## Method

- **Machine:** Apple M4 Max, macOS 27.0.1 (26A434), .NET SDK 10.0.402 (runtime 10.0.12). The machine was shared with other agents (load average 14–31), so times are the minimum of 3–9 runs and indicative to 10–20%. Each comparison ran back to back.
- **Code:** `spec/33-broadside-v1` at 3368226e, copied into the scratchpad. A prototype scale switch was added to `JpegDecoder`, with three reduced-IDCT variants (below) and a fix that keeps the painted geometry of a reduced image exact.
- **Native references:**
  - libjpeg-turbo 3.2.0 (`djpeg`; times include about 3 ms of process start).
  - OpenJPEG 2.5.4 (`opj_decompress -threads 1`).
  - SkiaSharp 4.153.1 (the repository's pinned version).
  - ImageIO through a Swift program (`CGImageSourceCreateImageAtIndex`, raw bytes from `CGDataProvider`).
  - Google Chrome 155.0.8059.85 (headless, through Playwright 1.58).
  - Playwright WebKit 26.0 (build 2248). **This is not Safari.** It is WebKit on macOS with ImageIO underneath, so Safari and iOS numbers are unmeasured.
  - Playwright Firefox (build 1509) does not launch headless on this Mac (known; #338). **Every Firefox statement is from source and is unmeasured.**
- **.NET WebAssembly:** a small `Microsoft.NET.Sdk.WebAssembly` app over the same prototype code. Published with and without `RunAOTCompilation` (the wasm-tools 10.0.113 workload) and run in Chrome 155.
- **Files:**

  | File | Contents |
  |---|---|
  | The owner's catalogue | Local only, never committed. Page 1 is a 2550×3150 baseline 4:4:4 DCT image in an ICCBased space. |
  | `issue12841_reduced.pdf` (pdf.js corpus) | 5280×3792 baseline 4:2:0 DCT with a restart interval. libjpeg warns "Invalid SOS parameters for sequential JPEG". |
  | `issue5567.pdf` | 1500×1125 JPX: raw codestream, 5 decomposition levels, 1 layer, LRCP, reversible. |
  | `cmykjpeg.pdf` | 200×150 Adobe CMYK, APP14 transform 0. |
  | Feature files | Generated with libjpeg-turbo `cjpeg` from a 1320×948 decode of the photo: 4:2:0, 4:4:4, progressive, arithmetic-coded, 12-bit, EXIF orientation 6, and an embedded ROMM RGB ICC profile. |

- **Fidelity measures:** maximum absolute difference per channel; mean absolute difference; PSNR over RGB; and the share of pixels whose largest channel difference exceeds 2 and 8 levels.
  - "Page" comparisons render page 1 with the software rasterizer at a device width and compare against the same page rendered with a full decode.

## 1. Where the time goes on an image page

Page 1 of the owner's catalogue, a fresh document for each run. "Build" is display-list build, which includes the image decode. "Render" is the software rasterizer, which includes sample conversion and painting. The table shows the 390-pixel-wide case.

| Runtime | Decode scale | Build | Render | Total |
|---|---|---:|---:|---:|
| .NET JIT (M4) | 1/1 | 51 ms | 145 ms | 196 ms |
| | 1/2 | 53 ms * | 38 ms | 91 ms |
| | 1/4 | 48 ms * | 12 ms | 60 ms |
| | 1/8 | 17 ms | 3 ms | 20 ms |
| WebAssembly AOT, Chrome 155 | 1/1 | 78 ms | 374 ms | 451 ms |
| | 1/2 | 76 ms * | 99 ms | 175 ms |
| | 1/4 | 67 ms * | 29 ms | 96 ms |
| | 1/8 | 21 ms | 9 ms | 30 ms |
| WebAssembly interpreter, Chrome 155 | 1/1 | 387 ms | 3,929 ms | 4,316 ms |
| | 1/2 | 444 ms * | 1,090 ms | 1,534 ms |
| | 1/4 | 405 ms * | 353 ms | 758 ms |
| | 1/8 | 119 ms | 78 ms | 197 ms |

\* The 1/2 and 1/4 prototype runs the full 8×8 IDCT and then averages, so its decode is no cheaper than a full decode (see §2.2). A port of `jidctred.c` would bring these builds down. The estimate from the entropy-decode floor below is about 25–32 ms on the JIT.

The cost splits as follows (8-megapixel cover, JIT):

| Part | Cost |
|---|---:|
| Entropy decode alone, no IDCT or colour (prototype switch) | 15.6 ms |
| Full decode | 48.7 ms |
| Everything after decode, at 390 px | about 145 ms |

That last cost scales with the decoded pixel count, not with the device size: 145 ms at 300 px, 150 ms at 800 px.

The 20-megapixel photo behaves the same way. On the JIT the total is 370 ms at 1/1 against 125 ms at 1/4 (390 px wide, where 1/4 still gives k ≥ 2). Its decode is 147 ms, of which entropy decode is 70 ms.

The issue's "frames up to 2.3 s" on the published demo is this render column under the interpreter. **The biggest lever is to stop producing pixels that are thrown away. A faster decoder comes second.**

## 2. Scaled DCT decode

### 2.1 What libjpeg-turbo does

The facts below are from libjpeg-turbo `jddctmgr.c`, `jidctred.c`, `jdmaster.c` and `libjpeg.txt` (main branch, checked 2026-10-19).

- **Scales:** `scale_num/scale_denom` selects M/8 for M = 1 to 16.
- **IDCT routines:**
  - 1/8 uses `jpeg_idct_1x1`: DC / 8, with no IDCT at all.
  - 1/4 and 1/2 use `jpeg_idct_2x2` and `jpeg_idct_4x4` in `jidctred.c`, with NEON/SSE versions.
  - Other M use the scaled `jidctint.c` routines.
- **Chroma rule:** when IDCT scaling is compiled in, `jdmaster.c` gives each subsampled component a larger IDCT instead of upsampling it. For 4:2:0 at 1/2, luma uses 4×4 and chroma uses the full 8×8, and nothing is upsampled.
- **Precision and arithmetic coding:** both decode in the scaled path. Arithmetic coding and SIMD are 8-bit only.
- **Partial decode:** `jpeg_crop_scanline` decodes a horizontal span. `jpeg_skip_scanlines` skips rows but costs up to as much as reading them.

Precedent in other PDF engines:
- **PDFium:** `CPDF_DIB` computes the levels to skip from the size the caller needs. It passes `scale_denom = 1 << min(skip, 3)` to libjpeg and `cp_reduce` to OpenJPEG.
- **pdf.js:** reduces JPX through OpenJPEG's `opj_set_decoded_resolution_factor`, but only to fit memory limits. In August 2026 it began downscaling large JPEGs with WebCodecs `desiredWidth`/`desiredHeight`.

### 2.2 Three ways to compute a reduced block, measured

The prototype computed the reduced IDCT three ways:

- **A, textbook:** the n-point IDCT of the low n×n coefficients.
- **B, averaged basis:** each 8-point basis function averaged over s×s pixels, in float. This is what `jidctred.c` computes in integers.
- **C, IDCT then average:** the existing integer 8×8 IDCT, then an s×s average.

1/8 is the DC term in all three, and it equals libjpeg-turbo exactly.

Image-level accuracy:

| Image, scale | A vs libjpeg-turbo | A vs full + box | B vs libjpeg-turbo | B vs full + box | libjpeg-turbo vs full + box |
|---|---|---|---|---|---|
| cover 4:4:4, 1/2 | max 24, 48.4 dB | 48.0 dB | max 3, 63.0 dB | 55.1 dB | max 7, 55.6 dB |
| cover 4:4:4, 1/4 | max 18, 46.3 dB | 46.2 dB | max 3, 62.1 dB | 56.2 dB | max 24, 56.9 dB |
| cover, 1/8 | identical | 55.5 dB | identical | 55.5 dB | 55.5 dB |
| photo 4:2:0, 1/2 | max 23, 49.3 dB | 49.1 dB | max 1, 66.6 dB | 56.5 dB | 56.5 dB |
| photo 4:2:0, 1/4 | max 15, 49.3 dB | 49.1 dB | max 3, 61.7 dB | 55.9 dB | 56.5 dB |
| photo 4:2:0, 1/8 | max 7, 56.2 dB | 54.4 dB | max 2, 61.3 dB | 54.6 dB | 54.9 dB |

Full-size decodes equal `djpeg -dct int -nosmooth` exactly, as the decoder's documentation says.

**Conclusions:**
- Method A must not be used: it rings at edges, with up to 24 levels of difference.
- `jidctred.c` is effectively "8-point IDCT, then average".
- Porting it, under the same IJG notice as `jidctint.c` today, gives:
  - output within rounding of the exact box average;
  - an exact oracle (`djpeg -scale 1/s -dct int -nosmooth`);
  - less arithmetic than a full IDCT.

Decode time, JIT (minimum of 7–9 runs):

| Image | Full | 1/2 (C) | 1/4 (C) | 1/8 | 1/2 (B, float) | Entropy only |
|---|---:|---:|---:|---:|---:|---:|
| 2550×3150 4:4:4 | 48.7 ms | 51.9 ms | 48.5 ms | 16.5 ms | 96.6 ms | 15.6 ms |
| 5280×3792 4:2:0 | 146.5 ms | 123.9 ms | 121.7 ms | 85.8 ms | 152.6 ms | 70.2 ms |
| libjpeg-turbo, 2550×3150 | 19.1 ms | 15.4 ms | 13.5 ms | 12.5 ms | | |
| libjpeg-turbo, 5280×3792 | 56.0 ms | 48.8 ms | 44.7 ms | 44.0 ms | | |

The libjpeg-turbo rows are minus 3 ms of process start.

Reading the table:
- Huffman decoding sets the floor (about 16 ms and 70 ms here). No scale goes below it.
- At 1/8 the decode is about 3× cheaper; at 1/2 and 1/4 it gains roughly 1.5–2× once ported.
- **Most of the gain is downstream (§1).** For example, the 20-megapixel photo decoded at 1/1 is 60 MB of samples, close to the 64 MB default image-cache budget. At 1/4 it is 3.75 MB.

### 2.3 Progressive JPEG: stop after the DC scans at 1/8

The prototype skips the progressive AC scans (T.81 Annex G: Ss > 0) when decoding at 1/8. The test files are the same images re-encoded progressive by `jpegtran` (lossless).

| Image | Full | 1/8, all scans | 1/8, DC scans only | Difference |
|---|---:|---:|---:|---|
| cover 4:4:4 | 78.6 ms | 48.3 ms | 9.6 ms | identical |
| photo 4:2:0 | 250.7 ms | 190.4 ms | 27.8 ms | max 38 (chroma) |

At 1/8, 4:2:0 chroma uses a 2×2 IDCT (the chroma rule above), and that needs odd AC coefficients. So the shortcut is exact only when every component's scaled block is 1×1: gray, 4:4:4, and CMYK without subsampling.

Two options for subsampled images:
- **Default:** keep all scans.
- **Opt-in:** decode chroma at 1×1 as well and replicate it. Not libjpeg-exact, so it needs a tolerance.

### 2.4 Geometry: a reduced image does not have W/s samples

libjpeg reports ceil(W/s) samples. ImageIO reports floor(W/s). The last sample covers only part of the image, so painting ceil(W/s) samples over the unit square shifts every sample by up to half a reduced sample.

On the cover at 1/4 and 390 px this cost 4 dB (36.0 dB before the fix, 40.3 dB after). The fix maps the reduced image onto [0, ceil(W/s)·s/W] of the unit square and clips at 1.

**The design must carry the source size and the reduction with the reduced samples. Never infer them from the sample counts.**

### 2.5 Page-level fidelity and choosing the reduction

The table compares a page rendered from a reduced decode with the same page rendered from a full decode (software rasterizer). Here k is the reduced image's samples per device pixel on its tighter axis.

| Page and width | Scale | k | PSNR | > 2 levels | > 8 levels | Total time (JIT) |
|---|---|---:|---:|---:|---:|---:|
| cover, 300 px | 1/2 | 4.3 | 53.9 dB | 0.62% | 0.000% | 92 ms (from 193) |
| cover, 300 px | 1/4 | 2.1 | 41.6 dB | 5.4% | 0.71% | 60 ms |
| cover, 390 px | 1/2 | 3.3 | 53.4 dB | 0.66% | 0.001% | 91 ms (from 196) |
| cover, 390 px | 1/4 | 1.6 | 40.3 dB | 6.5% | 1.0% | 60 ms |
| cover, 390 px | 1/8 | 0.8 | 29.6 dB | 17.3% | 7.7% | 20 ms |
| cover, 600 px | 1/2 | 2.1 | 50.0 dB | 2.0% | 0.07% | 97 ms |
| cover, 800 px | 1/2 | 1.6 | 48.3 dB | 2.8% | 0.20% | 101 ms |
| cover, 1206 px (phone 3×) | 1/2 | 1.06 | 45.4 dB | 4.0% | 0.52% | 111 ms (from 214) |
| photo (rotated 90°), 390 px | 1/4 | 2.4 | 36.5 dB | 18.1% | 3.2% | 125 ms (from 370) |
| photo, 390 px | 1/2 | 4.9 | 49.4 dB | 1.6% | 0.08% | 145 ms |
| photo, 660 px | 1/2 | 2.9 | 39.1 dB | 16.1% | 1.6% | 141 ms (from 365) |

What the residual is:
- A reduced image is a slightly blurrier resample. Device pixels whose footprint cuts through a reduced sample average that sample's whole area.
- On the photo's fine wallpaper texture this shows as a faint periodic pattern, plus differences of up to 75 levels on the high-contrast frame edges.
- None of it is visible side by side at these sizes (inspected).

A reference point for the scale of these differences, comparing our full-decode page with poppler 26.10 (`pdftoppm`, same size):

| Page | Ours vs poppler |
|---|---|
| photo, 660 px | 36.6 dB |
| photo, 1206 px | 36.0 dB |
| cover, 390 px | 26.5 dB |
| cover, 1206 px | 29.6 dB |

Using a reduced decode with k ≥ 2 moves these by 0 dB (cover at 390 px, 1/2) to 1.7 dB (photo at 660 px, 1/2). **The scaled decode changes the page less than the choice of renderer does.**

Proposed rule:
- Use the largest power-of-two s ≤ 8 for DCT (≤ 2^levels for JPX) such that the reduced image keeps **k ≥ 2** samples per device pixel in every direction.
  - With A the linear part of image space → device space (`CTM × device` applied to `diag(1/W, 1/H)`), that is s ≤ 1 / (k · σmax(A)).
  - σmax, the larger singular value, handles rotation and skew conservatively.
  - The region (tile) does not matter: the reduction depends on scale alone.
- k = 1 is the owner's alternative (decision 1 below). It roughly halves the time again at 35–45 dB.
- `/Interpolate` (§8.9.5.3) only affects enlargement, so it does not change the choice. Enlargement never happens when k ≥ 1.
- **Never reduce for print intent or exports** (decision 2): a `RenderOptions` switch, on by default for viewers.

### 2.6 Cache and zoom

- **Decoded samples are cached per (image stream, reduction)** in the document's image cache. The reductions are charged against `ImageCacheBudget` like today's full decodes.
- **Zoom-in:** asks for the next reduction up and decodes it from the encoded bytes. The coarser entry stays as the progressive-zoom placeholder (#212) until the budget evicts it.
- **Zoom-out:** uses any cached finer reduction (down-filtering is always correct). It does not decode again.
- **Masks:** an SMask or Mask has its own size (§11.6.5.2, §8.9.6.3). It is reduced by its own rule; today's code already resamples masks from their own resolution. A soft mask of the image's size is "folded" only when both have the same reduction.

## 3. JPX: resolution levels and layers

T.800 §B.5: resolution level r of a tile-component with N_L decomposition levels is the sub-band of level N_L − r. Its size is ceil(x / 2^(N_L − r)) on the reference grid.

Dropping the top d levels therefore:
- gives the image at 1/2^d;
- skips tier-1 (EBCOT, Annex D) for every code-block of the dropped subbands;
- skips the inverse DWT (Annex F) for the dropped levels.

Packet headers of the dropped levels must still be read in progressions that interleave them (LRCP, RLCP): the next packet's position depends on their lengths. RPCL and the position-major orders put them last.

Measured with OpenJPEG 2.5.4 (`-r d -threads 1`, including process start and PPM write) on `issue5567.pdf` (1500×1125, 5 levels, 1 layer):

| Levels dropped | Size | Time |
|---|---|---:|
| 0 | 1500×1125 | 340 ms |
| 1 | 750×563 | 129 ms (2.6×) |
| 2 | 375×282 | 63 ms (5.4×) |
| 3 | 188×141 | 45 ms (7.6×) |

Our managed decoder takes 475 ms full on the JIT (§H9 of `docs/benchmarks/perf-deep-dive.md` puts tier-1 at 75% of it). A managed reduced decode should gain in proportion, because tier-1 work is proportional to the samples in the kept subbands. The JPX prototype was not built; these OpenJPEG ratios are the estimate.

Quality layers (`cp_layer`) trade fidelity for time inside the kept levels. Layers are encoder-defined, so they are not a resolution choice. They are useful only as an explicit "draft" option and are out of scope.

The JPX tier-1 rewrite (perf deep dive item 1, #352) and this work compound.

## 4. Flate, raw and other filters

Flate, LZW, RunLength, CCITT and JBIG2 must decode every byte, so a reduction cannot save decode time. It can still save everything downstream (§1) and the cache memory, by box-filtering rows as they come out of the decoder.

Where averaging is allowed:
- Components that average linearly:
  - DeviceGray, DeviceRGB, DeviceCMYK;
  - ICCBased with a default `/Decode`;
  - Separation/DeviceN tints.
  These are averaged in the source space before conversion, as the DCT path already does in YCbCr.
- **Indexed images are never averaged** (§8.6.6.3: values are indices).
- **1-bit images and stencil masks** become 8-bit coverage: a 600-dpi scan shown at 100 dpi becomes 6×6 averages. That changes the image's bits per component, so `DisplayImage` must say so.
- **Colour-key masks** (§8.9.6.4) compare raw values, so they are applied before the reduction and travel as an opacity plane.

The gain is the downstream cost, so this matters most for large scans (CCITT/JBIG2 pages) on phones. It is the third part of #362.

## 5. Native decoders, per platform

### 5.1 Availability and APIs

Sources were checked 2026-10-19; the citations list is at the end. "Measured" means on this Mac.

| Platform | API | Scaled decode | Off the calling thread | Raw samples without ICC or EXIF effects | CMYK / YCCK | 12-bit, arithmetic | JPEG 2000 |
|---|---|---|---|---|---|---|---|
| Chrome / Edge | WebCodecs `ImageDecoder` (94+; page and dedicated worker) | `desiredWidth/Height`: libjpeg DCT scaling to M/8. Honoured for the non-MCU-aligned 2550×3150 cover (measured). | Yes (measured: gaps ≤ 1.8 ms during a 15 ms decode) | Full size gives **I444/I420 planes**, before colour conversion and orientation (measured). Scaled gives BGRX. | Converted to RGB, Adobe inversion assumed (source; BGRX measured) | Neither (source, measured) | No (`isTypeSupported('image/jp2')` false, measured) |
| Chrome / Edge | `createImageBitmap(blob, {colorSpaceConversion:'none'})` | No: full decode, then resample (source; time measured) | Yes (worker pool, source; measured) | `'none'` honoured (measured). EXIF orientation applied by default. `'none'` is still not supported. | as above | Neither | No |
| Firefox 133+ | `ImageDecoder` and `createImageBitmap` | DCT scaling when MCU-aligned and an output size is set (pref `image.jpeg.dct-scaling`) | `createImageBitmap(Blob)` decodes synchronously on the main thread, even from a worker (source). **Unmeasured.** | `FLAG_DECODE_NO_COLORSPACE_CONVERSION` (source) | `image/x-jpeg-pdf` decodes non-inverted CMYK (source) | Neither (source) | No |
| Safari / WebKit | `createImageBitmap` only. `ImageDecoder` is in Technology Preview, not shipped. | No (25 ms at full and at 1/4, measured) | Resolves in 25–39 ms with ≤ 7 ms gaps, but drawing the bitmap into a canvas took **60 ms (8 MP) and 141 ms (20 MP)** on the calling thread (measured). Consistent with decode deferred to first draw (inference). | **`colorSpaceConversion:'none'` ignored**: the ROMM-tagged file was converted, 24.9 dB (measured). EXIF applied. | Converted to RGB (measured) | Both decode (ImageIO underneath, measured) | **Removed in Safari 18** (WebKit 273978@main; measured failing in WebKit 26) |
| Apple | ImageIO `CGImageSourceCreateImageAtIndex` + `kCGImageSourceSubsampleFactor` 2/4/8 (iOS 9, macOS 10.11; JPEG, HEIF, TIFF, PNG) | Yes; floor(W/s) sizes (measured) | Synchronous; call it on a worker. No documented hardware key. | Yes: `CGDataProvider` bytes are unconverted, and the image is tagged with its profile (measured, ROMM file). Orientation only with `kCGImageSourceCreateThumbnailWithTransform`. | Returns CMYK samples, but they differ from ours by 35 dB (measured) | Both decode. The 12-bit file came back as 16 bpc and sniffed as `public.heic` (measured). | Yes, still in ImageIO (iOS 18.3.1 per Apple forums; `sips` on macOS 27) |
| Android | `BitmapFactory.inSampleSize` (any API); `ImageDecoder.setTargetSize/SampleSize` (API 28) | Yes, libjpeg-turbo through Skia (source) | Synchronous; worker | `BitmapFactory` does not orient. `ImageDecoder` **applies EXIF orientation** and tags the embedded profile without converting (AOSP source). | `SkJpegCodec` converts to RGB, inversion assumed (source) | Arithmetic yes (AOSP libjpeg-turbo 2.1.5.1); 12-bit no (source) | No |
| Windows | WIC JPEG decoder + `IWICBitmapSourceTransform` (power-of-two DCT scaling); `IWICPlanarBitmapSourceTransform` (YCbCr planes, Windows 8.1+) | Yes (docs) | Synchronous; MTA-capable codecs (docs) | Decoder does not colour-convert; orientation is metadata only (docs, inference) | `32bppCMYK` pixel format; inversion handling undocumented | 12-bit no (no 16-bit JPEG format); arithmetic undocumented | No codec in the box |
| SkiaSharp 4.153.1 | `SKCodec.GetScaledDimensions` + `GetPixels` | Yes, libjpeg-turbo 1/8–8/8 (measured identical to `djpeg -scale`) | Synchronous; worker | Null destination colour space means no conversion (measured, ROMM file). `EncodedOrigin` reported, not applied (measured). | Converted to RGB, inversion assumed (measured) | Arithmetic yes; 12-bit fails (measured) | No |

### 5.2 Time and fidelity, measured

Full-size decode of the 2550×3150 cover. Times are the minimum of 5 runs. "Bit-exact" means against the managed decoder.

| Decoder | Time | vs managed | Notes |
|---|---:|---|---|
| Managed, .NET JIT | 48.7 ms | — | |
| Managed, WebAssembly AOT | 71.8 ms | — | Blocks the thread |
| Managed, WebAssembly interpreter | 363.5 ms | — | Blocks the thread |
| Chrome `ImageDecoder`, planes + 3.0 ms copy + our YCbCr→RGB | 15.5 ms | **identical** (all 8 files, incl. 4:2:0, progressive, EXIF, ICC-tagged) | |
| Chrome `ImageDecoder`, `copyTo` RGBA | 15.5 + 13.1 ms | max 1, 73.8 dB | |
| Chrome `createImageBitmap` + canvas readback + heap copy | 20.9 + 10.6 + 2.1 ms | identical for 4:4:4; 4:2:0 max 23, 56.5 dB | |
| WebKit 26 `createImageBitmap` + readback + heap copy | 39 + 60 + 5 ms | max 3, 60.6 dB | Readback on the calling thread |
| SkiaSharp `SKCodec` | 14.9 ms | identical for 4:4:4; photo (4:2:0) max 11, 65.9 dB; feature files max 23, 56.5 dB | Equals libjpeg-turbo exactly |
| ImageIO | 18.2 ms | max 3, 65.8 dB; 4:2:0 max 11–23, 55.7–57.6 dB | Not bit-exact even at 4:4:4 |
| libjpeg-turbo `djpeg` (reference) | about 19 ms | identical with `-nosmooth` | |

Reduced decodes:

| Decoder | 1/2 | 1/4 | 1/8 | Fidelity at 1/4 vs the managed 1/4 |
|---|---:|---:|---:|---|
| Chrome `ImageDecoder` `desiredWidth` | — | 11.0 ms | 10.2 ms | max 3, 62.1 dB (libjpeg-turbo's) |
| Chrome `createImageBitmap` `resizeWidth`, low / high | — | 20.1 / 25.9 ms | — | **32.1 / 33.1 dB**: full decode, then a poor resample |
| SkiaSharp `SKCodec` | 11.7 ms | 10.3 ms | 9.6 ms | max 3, 62.1 dB |
| ImageIO `SubsampleFactor` | 10.8 ms | 10.4 ms | 8.4 ms | **max 20, 46.2 dB**; photo 48.6 dB (looks like method A) |
| Managed, WebAssembly AOT (prototype C) | 72.6 ms | 63.6 ms | 19.1 ms | — |
| Managed, WebAssembly interpreter (prototype C) | 420 ms | 390 ms | 104 ms | — |

20-megapixel photo, full size:

| Decoder | Time | Notes |
|---|---:|---|
| Managed (JIT) | 146.5 ms | |
| Skia | 52.2 ms | |
| ImageIO | 72.5 ms | |
| WebKit `createImageBitmap` | 86–96 ms | Readback 31–141 ms |
| Chrome | — | **Rejects the file**, as `createImageBitmap` and `ImageDecoder` alike. We, Skia, ImageIO and WebKit decode it. |

### 5.3 What breaks faithfulness, and the rules that follow

ISO 32000-2 §8.9.5 makes the image dictionary's `/ColorSpace` and `/Decode` authoritative. §7.4.8 Table 13 makes `/ColorTransform` and the APP14 transform decide YCbCr. Native decoders know none of this. Measured hazards:

| Hazard | Rule that follows |
|---|---|
| **Embedded ICC profile.** WebKit ignores `colorSpaceConversion:'none'` (24.9 dB). Skia and Chrome honour a null destination or `'none'`. | Strip APP2 `ICC_PROFILE` segments (zero-fill or rewrite the marker) before handing the bytes to WebKit/Safari. Elsewhere, request no conversion. |
| **EXIF orientation.** `createImageBitmap` (Chrome and WebKit) and Android's `ImageDecoder` rotate. `imageOrientation:'none'` is still not supported. Planes, Skia, ImageIO, `BitmapFactory` and WIC do not rotate. | Zero-fill the APP1 Exif payload for the APIs that rotate, as pdf.js does since its bug 1942064. |
| **CMYK / YCCK / Adobe inversion.** Every native path converts to RGB with an inversion assumption, or (ImageIO) differs. | 4-component images stay managed. |
| **`/ColorTransform 0` with three components and no APP14.** libjpeg-based decoders still apply YCbCr, but §7.4.8 says not to. | Ineligible. |
| **12-bit, arithmetic, lossless, hierarchical.** | Managed unless measured supported on that path. Arithmetic is fine on Skia and ImageIO. |
| **SOF size differs from the dictionary's, or height 0 with DNL.** | Managed. The pdf.js rule. |
| **Fancy upsampling** (Skia, Chrome RGB paths, ImageIO, Android). Up to 23 levels at chroma edges of 4:2:0 images, 0.5% of pixels. | Tolerance tests (§6.5). Chrome planes avoid it entirely. |
| **Decode failure** (Chrome on `issue12841_reduced.pdf`). | Fall back to managed and record an Information diagnostic (ADR 0005: never silent). |

### 5.4 Round trip into .NET

**Browser, Chrome:**
- `VideoFrame.copyTo` into an `ArrayBuffer` costs 2.6–3.0 ms for 8-megapixel I444 planes.
- RGBA conversion through `copyTo({format:'RGBA'})` costs 11–13 ms.
- `copyTo` takes any `BufferSource`, so it can write straight into the WebAssembly heap. That needs a pinned pooled array, and the view re-created if memory grows during the await.
- A `Uint8Array.set` copy of 32 MB took 2–5 ms (Chrome) and 3–10 ms (WebKit).

**Browser, WebKit:** `drawImage` + `getImageData` on an `OffscreenCanvas` is the only way back, and it is the expensive step (60–141 ms).

**Avoiding the round trip:** an `ImageBitmap` can go straight to a canvas `drawImage` or a WebGL `texImage2D` with no readback (spec). That is "mode B" below. It is only correct when our colour pipeline would be an identity for the image.

**Native platforms (Skia, ImageIO, WIC, Android):** pixels land in memory we pass in, or are one `memcpy` away (`CGDataProviderCopyData`, `Bitmap.copyPixelsToBuffer` for software bitmaps only; `HARDWARE` bitmaps cannot be read back).

## 6. Design proposal

### 6.1 Display lists hold images that decode on demand (ADR 0014, in #362)

Today `DisplayListBuilder.InternImage` decodes each image fully at build time, and `DisplayImage` holds the samples (ADR 0006: "decoded images"). A reduction depends on the device transform, which a device-independent list does not know.

Proposal:
- `DisplayImage` holds a reference to an internal `ImageSource` record with:
  - the image's header facts and dictionary values;
  - the encoded bytes after the filters that precede the image codec, which the list already owns;
  - a per-reduction sample cache in `DocumentCaches.Images`, keyed by (stream, reduction).
- Backends call `image.GetSamples(int reduction)` (or `GetSamples(in Matrix imageToDevice)`, with the rule in §2.5 in one place).
  - The call returns a `DecodedImage` plus the source size, so geometry stays exact (§2.4).
  - It decodes at most once per (image, reduction) across threads. A per-entry once-gate honours the concurrent-replay contract of #357.
- **The build still reads the header** (`IImageFilter.TryReadHeader`) and applies the size limits, so a list stays complete and safe. Decode diagnostics move from list build to first replay. Document that.
- **Invariant:** a backend still never reads the file. The encoded bytes are in memory, and ADR 0010's prefetch has already fetched them.
- `IImageFilter` gains an optional reduction:
  - `ImageFilterContext.Reduction` (log2 requested).
  - `DecodedImage.Reduction` (log2 delivered: a codec may deliver less).
  - Codecs that do not support it ignore it.
  This extends an existing extension point; ADR 0001 needs no new one.

### 6.2 The platform decoder seam (ADR 0015, #372)

Browser decoders are asynchronous, and the core is synchronous (ADR 0010). Following ADR 0010's pattern, add one asynchronous boundary instead of making decode asynchronous:

```csharp
// Broadside core: the extension point; managed default = none registered.
public interface IPlatformImageDecoder
{
    // Cheap and synchronous: header facts, dictionary values, requested reduction.
    bool CanDecode(in PlatformImageRequest request);
    ValueTask<DecodedImage?> DecodeAsync(ReadOnlyMemory<byte> encoded, PlatformImageRequest request, CancellationToken cancellationToken);
}
// PdfOptions.UsePlatformImageDecoder(IPlatformImageDecoder)
// DisplayList.PrefetchImagesAsync(in Matrix deviceTransform, PixelRectangle? region, CancellationToken)
```

How it is used:
- **Before replaying a tile**, the tile scheduler awaits `PrefetchImagesAsync` for the list's images that the tile reaches and that lack a cached reduction. Eligible ones go to the platform decoder; the rest decode managed.
- **Then the replay runs synchronously** and finds every image in the cache. A cache miss during replay, such as a caller that did not prefetch, decodes managed. It never blocks on the platform decoder, so the browser cannot deadlock.
- **In MAUI/desktop**, the background tile job (#357) awaits the prefetch on the thread pool.
- **In the browser** it awaits in the cooperative loop: the native decode runs off the thread while the frame loop continues. Or the render worker (ADR 0013) does the same in its own runtime.

Other parts of the seam:
- **Eligibility** (§5.3) lives in the core as one shared predicate over `JpegInfo` (already in `Broadside.Images`) plus the dictionary. A platform decoder can only narrow it.
- **Fallback:** a platform failure decodes managed and records `ImageDecodedManaged` (Information) with the platform's reason, once per image.
- **Diagnostics when native and managed differ:** a debug/test option, `PdfOptions.VerifyPlatformImageDecoder`, decodes both and records `PlatformImageMismatch` with the measured PSNR. The browser and Skia test suites run with it on.
- **Mode B (later, browser only):** the platform decoder may return a native handle (an `ImageBitmap`) instead of samples. Only when the colour conversion is an identity (DeviceRGB/Gray or an sRGB-equivalent ICC, default `/Decode`, no masks, no transfer or overprint), so the WebGL backend or canvas surface uses it directly. This saves the readback entirely and is the only good path on Safari. It is a follow-up in #373, behind the same seam.

### 6.3 Why not just register a native `IImageFilter`?

`PdfOptions.UseFilter` already lets a platform package replace `DCTDecode` synchronously (ADR 0001). For the synchronous platforms (Skia, ImageIO, WIC, Android) that would work today. It has three costs:
- It cannot serve the browser's asynchronous APIs.
- It gives no shared eligibility or fallback, so each package re-implements both.
- It would replace the managed decoder for byte-level `Decode` callers, such as text extraction or export, who need exact §8.9.3 samples.

The seam keeps `IImageFilter` as the managed, exact contract and adds the platform path beside it, for rendering only.

### 6.4 Tests, benchmarks and the fidelity bar

Tests:
- **Managed scaled decode:** byte-exact against `djpeg -scale 1/s -dct int -nosmooth` over the DCT corpus. CI installs libjpeg-turbo as it does qpdf. JPX against `opj_decompress -r d` within the existing JPX tolerance. A geometry test at non-divisible sizes.
- **Page level:** snapshot tests at k = 2 against a full decode, with PSNR ≥ 35 dB and ≤ 4% of pixels over 8 levels (the worst measured is 36.5 dB and 3.2% at k = 2.4 on the photo's fine texture; owner to confirm, decision 1). Goldens at 1:1 stay untouched, because k ≥ 2 never reduces at 1:1.
- **Native:**
  - Chrome planes: bit-exact.
  - Skia and Chrome RGB: max ≤ 24, PSNR ≥ 50 dB, ≤ 1% of pixels over 2 levels at full size; PSNR ≥ 55 dB against managed at 1/2–1/8.
  - ImageIO and Android: ≥ 45 dB scaled, pending decision 3.
  - Feature files: each hazard in §5.3 falls back.

Benchmarks (`[MemoryDiagnoser]`, allocation-free per row and MCU):
- `DctBenchmarks.Decode(scale)`;
- `JpxBenchmarks.Decode(levels)`;
- `PhoneTileBenchmarks` image pages at 390 and 1206 px;
- the browser `ScrollFrameTests` on the catalogue-class page (#365's ≤ 50 ms frame target).

## 7. Recommendations and owner decisions

| Platform | Recommendation | Why |
|---|---|---|
| All | **#362 first**: scaled DCT (port `jidctred.c` and the chroma rule, DC-only progressive), JPX level discard, Flate row reduction, on-demand display images (ADR 0014) | 2–20× on image pages everywhere; precondition for the rest |
| Browser (Blazor, page and render worker) | **Yes, high priority** (#373). `ImageDecoder` planes at full size (bit-exact), `desiredWidth` when reduced. `createImageBitmap` fallback on Safari, preferably in the render worker or as mode B. | 15.5 ms off-thread vs 72 ms (AOT) / 364 ms (interpreter) on-thread |
| MAUI and the Skia backend (Android, iOS, Catalyst, Windows) | **Yes, medium priority** (#374). `SKCodec` scaled decode in `Broadside.Rendering.Skia`. | 3.3× full, equal to libjpeg-turbo; native dependency already allowed there |
| Apple, CoreGraphics backend | **Measure on a device first** (#375). ImageIO is 2.7× but not bit-exact, and its 1/4 output is 46 dB. | Only matters if the CoreGraphics backend is used without Skia |
| Android Canvas backend | **Measure on a device first** (#376). `BitmapFactory.inSampleSize`, never `ImageDecoder` (orientation). | Same libjpeg-turbo as Skia |
| Windows, Direct2D backend | **Measure on CI first** (#377). The WIC planar path may be bit-exact. | Unmeasured |

Decisions for the owner:
1. **Reduction threshold k.**
   - k = 2 (proposed): 36–54 dB against a full decode, and about 2–3× faster pages.
   - k = 1: 35–48 dB, and about 2× faster again.
2. **Viewer-only.** Scaled decode on by default in viewers (`DocumentSession`), off for `Render(page)` callers, print intent and exports.
3. **Native decoders that are not bit-exact** (Skia's fancy upsampling, ImageIO): on by default within the tolerances in §6.4, or opt-in?
   - Proposed: the Chrome planes path on by default (bit-exact), the others opt-in until their device numbers are in.
4. **Progressive DC-only shortcut for subsampled images** (chroma replicated, not libjpeg-exact). Proposed: no; exact cases only.
5. **Safari.** Ship the `createImageBitmap` path only in the render worker and as mode B, never as main-thread readback (60 ms for 8 MP on the page's thread).

## Citations

All checked 2026-10-19. Platform and version in brackets.

- ISO 32000-2:2020 §7.4.8 (DCTDecode, Table 13), §7.4.9 (JPXDecode), §8.6.6.3 (Indexed), §8.9.4–§8.9.5.3 (image space, dictionaries, Decode, Interpolate), §8.9.6.3–§8.9.6.4 (masks), §11.6.5.2 (soft-mask images), from `Specs/ISO_32000-2_sponsored_EC3.pdf`.
- ITU-T T.81 (ISO/IEC 10918-1) §A.3.3 (IDCT), Annex G (progressive; spectral selection); `Specs/References/T-REC-T.81-199209_w3c.pdf`.
- ITU-T T.800 (2019) (ISO/IEC 15444-1) §B.5 (resolution levels), §B.8–§B.12 (layers, packets, progression), Annex D (tier-1), Annex F (inverse DWT); `Specs/References/T-REC-T.800-201906.pdf`.
- Adobe Technical Note #5116 (APP14); `Specs/References/5116.DCT_Filter.pdf`.
- libjpeg-turbo, main branch: `src/jddctmgr.c`, `src/jidctred.c`, `src/jdmaster.c`, `doc/libjpeg.txt` (github.com/libjpeg-turbo/libjpeg-turbo) [3.2.0 binaries used locally].
- OpenJPEG `src/bin/jp2/opj_decompress.c`, `src/lib/openjp2/openjpeg.h` (`cp_reduce`, `cp_layer`) [2.5.4 locally].
- WHATWG HTML, ImageBitmap and canvas sections (html.spec.whatwg.org/multipage/imagebitmap-and-animations.html, …/canvas.html).
- W3C WebCodecs editors' draft and the TR Working Draft of 2026-10-07 (w3.org/TR/webcodecs/).
- MDN browser-compat-data: `api/_globals/createImageBitmap.json`, `api/ImageDecoder.json`, `api/VideoFrame.json`.
- Chromium, HEAD: `third_party/blink/renderer/modules/canvas/imagebitmap/image_bitmap_factories.cc`, `core/imagebitmap/image_bitmap.cc`, `platform/image-decoders/jpeg/jpeg_image_decoder.cc`, `modules/webcodecs/image_decoder_core.cc`, `image_decoder_external.cc`, `common/mime_util/mime_util.cc`, `deps/libjpeg_turbo` `jconfig.h` and `BUILD.gn` [Chrome 155 measured].
- WebKit, main: `Source/WebCore/html/ImageBitmap.cpp`, `ImageBitmapOptions.h`; bug 178758 (JPEG 2000 removed, 273978@main, 2024-02-02); Safari 18 release notes [Playwright WebKit 26.0 measured].
- Gecko, main: `dom/canvas/ImageBitmap.cpp`, `image/imgTools.cpp`, `image/decoders/nsJPEGDecoder.cpp`, `image/DecoderFactory.cpp`, `media/libjpeg/jconfig.h`, `modules/libpref/init/StaticPrefList.yaml`; Firefox 133 release notes [unmeasured].
- Apple ImageIO documentation: `kCGImageSourceSubsampleFactor`, `kCGImageSourceThumbnailMaxPixelSize`, `kCGImageSourceCreateThumbnailWithTransform`, `kCGImageSourceShouldCacheImmediately`, `CGImageSourceSetAllowableTypes`; Apple Developer Forums thread 776802 (JPEG 2000 on iOS 18.3.1) [macOS 27.0.1 measured].
- Android: `ImageDecoder`, `BitmapFactory.Options`, `Bitmap.Config` reference; AOSP `libs/hwui/jni/ImageDecoder.cpp`, `libs/hwui/hwui/ImageDecoder.cpp`, `libs/hwui/jni/BitmapFactory.cpp`, `external/libjpeg-turbo` [unmeasured].
- Microsoft Learn: `IWICBitmapSourceTransform::CopyPixels`, "JPEG YCbCr support", "WIC native pixel formats", `IWICJpegFrameDecode`, "Native WIC codecs", WIC colour management [unmeasured].
- Skia, main: `src/codec/SkJpegCodec.cpp`, `include/codec/SkCodec.h`, `third_party/libjpeg-turbo/BUILD.gn`; SkiaSharp API reference (learn.microsoft.com, `view=skiasharp`) [SkiaSharp 4.153.1 measured].
- PDFium: `core/fpdfapi/page/cpdf_dib.cpp`, `core/fxcodec/jpx/cjpx_decoder.cpp`.
- pdf.js master after v6.4.299: `src/core/jpeg_stream.js`, `src/core/jpg.js` (`canUseImageDecoder`), `src/core/image.js`, `src/core/jpx.js`, `src/core/image_resizer.js`; commits b6c4f0b69e, 65eedfb0fc, 7e66cd3994, 9be0233cc1.
- poppler `pdftoppm` 26.10.0 (local reference renders).
