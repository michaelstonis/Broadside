# JPEG 2000 tier-1 decoding (#255)

Tier-1 (ITU-T T.800 Annex D: the significance propagation, magnitude refinement and cleanup passes driven by the MQ decoder) was
almost all of the time a page with a large JPEG 2000 image takes to build (#352; the [perf deep dive](perf-deep-dive.md), H9). This
records where the time went, what changed in `JpxCodeBlockDecoder` and `MqDecoder`, and the effect per build. The vertical wavelet
pass and the component transforms are #477.

Machine: Apple M4 Max (Arm64, macOS 27), .NET 10.0.13/14. Base is `spec/33-broadside-v1` at 4765bfe8.

## Where the time went

`sample` on a Native AOT build decoding `pdfjs/issue5567.pdf` (1500 x 1125 RGB, lossless 5/3, 5.06 million samples) in a loop,
self time by function:

| Stage | Base | Head |
|---|---:|---:|
| Tier-1 coding passes (significance, refinement, cleanup, with the context and sign helpers) | 76.2% | 78.3% (MQ inlined) |
| MQ decoder (RENORMD and BYTEIN calls) | 10.5% | inlined in the passes |
| Reconstruction of the quantization indices (`WriteOutput`) | 4.2% | 4.4% |
| Inverse wavelet (`Inverse53`, `Inverse1D`, `Interleave`) | 7.1% | 13.5% |
| Component transform and plane store (`ApplyComponentTransform`, `JpxComposer`, `JpxPlane.Write`) | 1.9% | 3.6% |

The image takes 21.5 million MQ decisions (4.25 per sample): 9.0 million in significance passes, 4.5 million in refinement passes
and 7.9 million in cleanup passes, over 17.4 million stripe-column visits.

Tier-1 per sample on that image (Native AOT, the passes' and reconstruction's share of the whole decode): **87 ns before, 41.5 ns
after**. Whole decode through `JpxDecodeFilter.DecodeImage`, best of runs over at least 3 s:

| Image | Samples | JIT base | JIT head | Native AOT base | Native AOT head |
|---|---:|---:|---:|---:|---:|
| `pdfjs/issue5567.pdf` | 5,062,500 | 463.2 ms (91.5 ns/sample) | 238.3 ms (47.1) | 486.4 ms (96.1) | 253.9 ms (50.1) |
| `pdfjs/issue5481.pdf` | 2,370,750 | 182.8 ms (77.1) | 100.7 ms (42.5) | 182.9 ms (77.1) | 103.9 ms (43.8) |
| `pdfjs/issue5549.pdf` | 2,370,750 | 185.8 ms (78.4) | 100.2 ms (42.3) | | |
| `pdfjs/issue5475.pdf` | 262,144 | 30.5 ms (116.5) | 16.1 ms (61.4) | | |
| `pdfjs/issue19326.pdf` (16-bit) | 185,687 | 12.5 ms (67.5) | 6.4 ms (34.3) | | |

For comparison, `opj_decompress -threads 1` (OpenJPEG 2.5.4, Homebrew) reports 235 ms of decode time for issue5567.

`JpxTier1Benchmarks` (BenchmarkDotNet, `--job short`, JIT) times tier-1 alone on 64 x 64 code-blocks of pseudo-random bytes, 28
passes each: decisions close to even, the hard case. Per sample: `Default` 123.2 ns before, **75.8 ns** after; `BypassCausal`
97.0 ns before, **56.3 ns** after; `Allocated` `-` in both.

## What changed

- **State per stripe column.** One `uint` per column of a stripe (four samples), with a border of words around the block. Each word
  holds the significance of the 3 x 6 neighborhood its samples see (rows -1 to 4, columns -1 to +1), the signs of its own column's
  rows -1 to 4, and the refined and coded-in-this-plane bits of its four samples. A sample that becomes significant updates its own
  word and the words of its neighbors, so a zero-coding context is `ZeroCoding[orientation][9 bits of one word]` (Table D.1), a sign
  context one lookup on eight bits (Tables D.2, D.3), and a refinement context two bits (Table D.4). A column with nothing to code is
  skipped on one test, and the cleanup pass's run-length condition is one test. Vertically causal contexts (D.7) come from not
  propagating a stripe's first row to the stripe above. Magnitudes are kept in stripe-column order next to it.
- **No bounds checks in the passes.** The state, the magnitudes and the contexts are reached through `ref` arithmetic; `Decode`
  sizes both arrays for the block and its border first (and rejects a block larger than Table A.18 allows, which the codestream
  reader never makes).
- **MQ registers in locals.** `MqDecoder.Decode(ref byte, ReadOnlySpan<byte>, ref uint, ref uint, ref int, ref int)` keeps A, C, CT
  and the read position in the pass's locals, with BYTEIN inlined, so nothing but the context byte touches memory. Before, every
  decision read and wrote the decoder's fields and called RENORMD and BYTEIN out of line, which forced the pass's live values into
  callee-saved registers and the stack.
- **A mostly branch-free DECODE.** Tier-1 decisions are often close to even, so the LPS/MPS and conditional-exchange branches
  mispredicted. One table word per context byte holds Qe and both next states; the decision, A, C and the new context come from the
  borrows of 32-bit subtractions. The one outcome skewed contexts mostly take (MPS sub-interval, no renormalization) keeps a branch:
  fully branch-free was 6% faster on issue5567 but up to 30% slower on images with skewed contexts. RENORMD shifts by the leading
  zeros of A at once, with BYTEIN wherever CT runs out in between.
- **Rows unrolled.** Full stripes run the four rows with constant shifts; the refinement pass walks the set bits of "significant and
  not coded in this plane" (rows are independent there) with the Table D.4 context and the +/- step computed without branches.
- **Reconstruction by stripe column.** One state word and four magnitudes per column, no per-sample bounds checks or float/int branch.

Context bytes stay in the existing `(I << 1) | MPS` encoding (shared with JBIG2). Holding the whole table word in each context instead
saved one dependent load and measured 3.5% faster on issue5567, but its `uint[19]` changes what each decode allocates, which the
allocation gate locks for the existing JPX benchmarks.

## Identical output

- Every JPEG 2000 image of the fetched corpora (`RealWorldJpxTests`, 38 streams of pdf.js, PDFium and veraPDF files against
  `jpx-corpus-references.txt`) and every OpenJPEG vector (`JpxDecodeFilterTests`, `JpxScaledDecodeTests`) decodes to the same samples.
- SHA-256 of the samples (and alpha) of every corpus stream at reductions 0, 1 and 2, every `JpxSamples` vector and the J.11 worked
  example, the `tests/Corpus` JPX files, `samples/documents/pdf/scan-moon-memo-jpx.pdf` and the `filter-jpx` fuzz seed: 217 hashes,
  identical before and after.
- The whole-render tables below hash every rendered page: one hash per document across all eight runs (four builds, base and head).
- `MqDecoderTests` checks the register path against DECODE decision by decision (random, skewed and marker-heavy streams, every state
  with either MPS).

## Whole render

First render of every page at 96 DPI with the software rasterizer, each pass opening the document afresh (so the image is decoded),
best pass. JIT and Native AOT: 6 passes, two interleaved runs (the better). Browser: a `browser-wasm` console app under Node 22
(`wasm-tools` workload), interpreter 4 passes, AOT (`RunAOTCompilation`) 5 passes in two interleaved runs.

| Document | JIT base | JIT head | Change | Native AOT base | Native AOT head | Change |
|---|---:|---:|---:|---:|---:|---:|
| `pdfjs/issue5567.pdf` | 487.2 | 257.0 | **-47%** | 502.8 | 271.4 | **-46%** |
| `pdfjs/issue5481.pdf` | 388.7 | 229.7 | **-41%** | 398.5 | 235.8 | **-41%** |
| `pdfjs/issue5549.pdf` | 197.9 | 110.7 | **-44%** | 201.9 | 114.6 | **-43%** |
| `pdfium-tests/fx/action/123.pdf` | 80.9 | 73.8 | -9% | 91.3 | 84.7 | -7% |
| `samples/documents/pdf/scan-moon-memo-jpx.pdf` | 731.0 | 585.2 | **-20%** | 817.3 | 680.2 | **-17%** |

| Document | Browser interpreter base | head | Change | Browser AOT base | head | Change |
|---|---:|---:|---:|---:|---:|---:|
| `pdfjs/issue5567.pdf` | 3,711.1 | 2,394.7 | **-35%** | 581.1 | 296.4 | **-49%** |
| `pdfjs/issue5481.pdf` | 3,731.1 | 2,459.1 | **-34%** | 502.4 | 295.8 | **-41%** |
| `pdfjs/issue5549.pdf` | 1,575.8 | 1,122.4 | **-29%** | 254.5 | 145.0 | **-43%** |
| `pdfium-tests/fx/action/123.pdf` | 1,209.8 | 1,213.1 | 0% | 167.4 | 160.2 | -4% |
| `samples/documents/pdf/scan-moon-memo-jpx.pdf` | 6,744.5 | 6,483.4 | -4% | 1,388.1 | 1,192.4 | **-14%** |

All times in ms. 123.pdf and the moon memo spend most of their time outside tier-1 (a light lossy image; the rest of the page and
the other decode stages of two 8.5-megapixel scans).

## Reproducing

`JpxTier1Benchmarks` and `JpxBenchmarks` in `bench/Broadside.Benchmarks`. The decode and render harnesses were throwaway console
apps over `JpxDecodeFilter.DecodeImage` and `SoftwareRasterizer.Render`, the same source built for JIT, Native AOT
(`PublishAot`) and `browser-wasm` (with and without `RunAOTCompilation`), with the PDFs embedded as resources for the browser build.
