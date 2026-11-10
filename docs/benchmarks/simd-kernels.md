# SIMD kernels: speed in every build

One section per `Vector128` kernel: its scalar and vector times under the JIT, Native AOT and both browser builds (the WebAssembly interpreter and the AOT build). A kernel ships only if its vector path is not slower than its scalar path in any of them (adding-a-benchmark skill, "SIMD kernels"). Background: `docs/research/simd.md`.

How each column is measured:

- **JIT** and **Native AOT**: a Stopwatch harness over the kernel source files (best of 7 samples of at least 20 ms after warm-up, 65,536 items, random bytes; the same loop as the browser's `KernelBenchmarks`), on an Apple M4 Max (Arm64, macOS 27), .NET 10.0.4xx. The BenchmarkDotNet figures for a 2,550-sample row are `JpegColorBenchmarks` (`bench/Broadside.Benchmarks/DctBenchmarks.cs`).
- **Browser, CI**: the `kernel aot|interpreter` lines of the `blazor-wasm` job (`KernelBenchmarkTests`, headless Chrome on the hosted Linux x64 runner).
- **Browser, local**: the same kernels in a `browser-wasm` console app run under Node 22 (`wasm-tools` workload, .NET 10.0.13 runtime pack), published with and without `RunAOTCompilation`, on the M4 Max.

Times are ns per item (a pixel for the colour transforms); speed-up is scalar over vector.

## Compositing rows (`CompositingRows`, #473)

Source-over of a row at one coverage (`OverRow`: a transparency group compositing onto its parent) and an opaque color over a run at
partial coverage (`FillRow`: an anti-aliased or translucent fill), in 16-bit lanes. Items are pixels. `over-row` and `fill-row` are
the browser benchmark's cases: random bytes, the backdrop copied into the destination before each call (both paths pay the copy),
`OverRow` at full coverage and `FillRow` at coverage 100. `over-row, c = 200` is source-over at partial coverage, without the copy.

| Build | Over, full coverage: scalar | vector | speed-up | Over, c = 200: scalar | vector | speed-up | Fill: scalar | vector | speed-up |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| JIT | 5.582 | 0.397 | 14.05x | 8.426 | 0.610 | 13.82x | 1.579 | 0.277 | 5.70x |
| JIT, premultiplied pixels | 2.682 | 0.374 | 7.17x | | | | | | |
| Native AOT | 3.287 | 0.383 | 8.59x | 3.195 | 0.604 | 5.29x | 1.507 | 0.279 | 5.40x |
| Browser interpreter, CI | 162.506 | 8.392 | 19.36x | | | | 61.226 | 6.866 | 8.92x |
| Browser AOT, CI | 7.582 | 2.050 | 3.70x | | | | 4.768 | 1.526 | 3.13x |
| Browser interpreter, local | 55.241 | 3.506 | 15.76x | 63.257 | 6.941 | 9.11x | 14.323 | 2.295 | 6.24x |
| Browser AOT, local | 3.000 | 0.857 | 3.50x | 2.883 | 2.062 | 1.40x | 1.696 | 0.783 | 2.17x |

- The JIT's scalar full-coverage time on random bytes (5.6 ns) is mostly mispredicted branches: random pixels are rarely
  premultiplied. On premultiplied pixels, a third of them transparent (`over-row-premul` locally), it is 2.7 ns, and the vector path
  is the same 0.37 ns.
- The first version used one formula for every coverage, with the wrap of `s × c + d × keep` caught by a compare: 41 operations per
  four pixels, 1.45x scalar in the browser AOT build. At full coverage s × 255 / 255 is s exactly and the clamped result equals
  s + d × (255 − a) / 255 (checked for every s and every product), so that loop is 22 operations with a saturating byte add: 3.5x. At
  partial coverage `Min(s × c, 65,152 − d × keep) + d × keep` replaces the compare.

BenchmarkDotNet, `CompositingRowBenchmarks`, JIT, M4 Max, `--job short --inProcess` (`Allocated` is `-` for all four):

| Method | Mean |
|---|---:|
| `OverRowScalar` (794 pixels) | 2,069.7 ns |
| `OverRow` (vector) | 295.8 ns |
| `FillRowScalar` (256 pixels) | 387.2 ns |
| `FillRow` (vector) | 59.4 ns |

### Whole-page effect

What the compositing rows cost before #473, sampled (macOS `sample`, 1 kHz, Native AOT, 96 DPI, every page rendered in a loop):
transparency-group compositing (`GroupSink.Composite` and the `Compositor.Over` it calls) was 51% of the showcase's render time and
partial-coverage `Compositor.Fill` 3%. On the image pages they are under 2%: the brochure, the newsletter and the catalogue spend
their time in color conversion (`ColorPipeline`, `IccAlternateConverter`, `CmykCharacterization`), the box filter
(`ImagePainter.Average`) and JPEG decoding.

Per document, ms per pass of every page at 96 DPI, best of the passes in 5 s, base and head interleaved twice (the better run of
each), M4 Max:

| Document | JIT base | JIT head | Change | Native AOT base | Native AOT head | Change |
|---|---:|---:|---:|---:|---:|---:|
| `showcase.pdf` | 32.8 | 18.2 | **-44%** | 38.4 | 22.8 | **-41%** |
| `showcase.pdf`, page 1 | 18.3 | 6.7 | **-64%** | 21.5 | 8.8 | **-59%** |
| `brochure.pdf` | 183.4 | 183.2 | 0% | 209.7 | 207.1 | -1% |
| `newsletter.pdf` | 282.6 | 278.8 | -1% | 331.1 | 322.5 | -3% |
| `catalogue.pdf` | 1182.0 | 1191.6 | +1% (noise) | 1245.4 | 1214.3 | -2% |

In the browser, the showcase rendered in a `browser-wasm` console app under Node 22 (every page six times, the best of the last
five; base and head twice each, M4 Max). The CI sample-document table cannot show it: its runners differ by tens of percent between
runs.

| Build | Base, all pages | Head, all pages | Change | Base, page 1 | Head, page 1 | Change |
|---|---:|---:|---:|---:|---:|---:|
| Browser interpreter | 952.2 | 566.7 | **-40%** | 573.9 | 257.3 | **-55%** |
| Browser AOT | 90.6 | 79.4 | **-12%** | 37.0 | 27.8 | **-25%** |

Pixels each path composited per render of the samples (counted with a temporary probe, two passes): the showcase's first page sends
about 3.6 million pixels a pass through group source-over at full coverage and 0.25 million through partial-coverage fills; the
catalogue sends 7.4 million through image source-over (`ImagePainter`), where sampling the image costs far more than compositing it
(`Compositor.Over` was 0.9% of its samples). Not vectorized yet, each under 1% of every sample measured: source-over for image spans
(`ImagePainter` composites while it samples, so it needs a sampled row first), `Mix` (non-isolated groups in Normal), `KnockOut`,
`RemoveBackdrop` and `Union` (knockout and alpha-tracking groups, absent from the samples), and soft-mask and clip-mask multiplication
(`RasterClip`, `SoftMaskPlane`, under 0.3%).
