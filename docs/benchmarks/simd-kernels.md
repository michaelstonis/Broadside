# SIMD kernels: speed in every build

One section per `Vector128` kernel: its scalar and vector times under the JIT, Native AOT and both browser builds (the WebAssembly interpreter and the AOT build). A kernel ships only if its vector path is not slower than its scalar path in any of them (adding-a-benchmark skill, "SIMD kernels"). Background: `docs/research/simd.md`.

How each column is measured:

- **JIT** and **Native AOT**: a Stopwatch harness over the kernel source files (best of 7 samples of at least 20 ms after warm-up, 65,536 items, random bytes; the same loop as the browser's `KernelBenchmarks`), on an Apple M4 Max (Arm64, macOS 27), .NET 10.0.4xx. The BenchmarkDotNet figures for a 2,550-sample row are `JpegColorBenchmarks` (`bench/Broadside.Benchmarks/DctBenchmarks.cs`).
- **Browser, CI**: the `kernel aot|interpreter` lines of the `blazor-wasm` job (`KernelBenchmarkTests`, headless Chrome on the hosted Linux x64 runner).
- **Browser, local**: the same kernels in a `browser-wasm` console app run under Node 22 (`wasm-tools` workload, .NET 10.0.13 runtime pack), published with and without `RunAOTCompilation`, on the M4 Max.

Times are ns per item (a pixel for the colour transforms); speed-up is scalar over vector.

## Per-operation cost in the browser (#471)

Why #470's example kernel was 0.56x of scalar in the AOT build. One loop of 4,096 `LoadUnsafe`, one operation, `StoreUnsafe` per call, ns per 16-byte vector, Node 22, local:

| Operation | AOT | Interpreter |
|---|---:|---:|
| `LoadUnsafe` + `StoreUnsafe` (copy) | 0.42 | 1.81 |
| `Vector128.Create(span)` + `CopyTo(span)` (copy) | 0.76 | 6.22 |
| `&`, `\|`, `^`, `+`, `*` (16- and 32-bit lanes) | 0.37–0.42 | 2.4–2.5 |
| `WidenLower` / `WidenUpper` | 0.33 / 0.43 | 2.40 / 2.23 |
| `Narrow` (16 → 8 bits, two loads) | 0.20 | 1.50 |
| `Shuffle` (byte, constant mask) | 0.41 | 2.46 |
| `Min(Max(...))` (16-bit) | 0.40 | 2.53 |
| `>>>`, `>>`, `<<` operators | 0.90–0.95 | 2.48–2.53 |
| **`Vector128.ShiftRightLogical`, `ShiftRightArithmetic`, `ShiftLeft` methods** | **3.30–3.44** | 2.41–2.54 |

- In the AOT build the shift **methods** cost about 8x any other operation and 3.6x the shift operators: they do not become a single WebAssembly SIMD shift there. The example kernel does six per 16 items, which alone made it slower than scalar. With the operators it is 2.9x faster than scalar there (below).
- `Create(span)`/`CopyTo(span)` cost the interpreter 3.4x `LoadUnsafe`/`StoreUnsafe` (and the AOT build 1.8x). With span loads the YCbCr kernel was 1.56x scalar in the interpreter; with `LoadUnsafe` it is 2.27x.
- `Narrow` from 16-bit lanes and `MemoryMarshal.Cast` were suspected and are not the cause: replacing either changed nothing in the AOT build (every load and narrow variant of the example measured 0.62–0.65x until the shifts changed).

## Example kernel (`SimdExampleKernel.Multiply`, test-only, #470)

| Build | Scalar | Vector | Speed-up |
|---|---:|---:|---:|
| JIT | 0.535 | 0.066 | 8.08x |
| Native AOT | 0.536 | 0.064 | 8.42x |
| Browser interpreter, CI | 10.681 | 1.287 | 8.30x |
| Browser AOT, CI | 1.836 | 0.513 | 3.58x |
| Browser AOT, CI, #470's version | | | **0.56x** |
| Browser interpreter, local | 4.268 | 0.627 | 6.81x |
| Browser AOT, local | 0.795 | 0.270 | 2.94x |
| Browser interpreter, local, #470's version | 4.194 | 1.511 | 2.78x |
| Browser AOT, local, #470's version | 0.929 | 1.482 | **0.63x** |

## YCbCr to RGB and YCCK to CMYK (`JpegColor`, #471)

| Build | YCbCr → RGB scalar | vector | speed-up | YCCK → CMYK scalar | vector | speed-up |
|---|---:|---:|---:|---:|---:|---:|
| JIT | 1.398 | 0.415 | 3.37x | 1.521 | 0.475 | 3.20x |
| Native AOT | 1.732 | 0.416 | 4.17x | 1.885 | 0.483 | 3.91x |
| Browser interpreter, CI | 22.221 | 5.054 | 4.40x | 26.512 | 6.104 | 4.34x |
| Browser AOT, CI | 11.730 | 2.480 | 4.73x | 14.687 | 2.861 | 5.13x |
| Browser interpreter, local | 8.974 | 4.012 | 2.24x | 10.758 | 4.595 | 2.34x |
| Browser AOT, local | 3.309 | 0.715 | 4.63x | 3.758 | 0.785 | 4.79x |

BenchmarkDotNet, `JpegColorBenchmarks`, one 2,550-sample row, JIT, M4 Max (`Allocated` is `-` for all four):

| Method | Mean |
|---|---:|
| `YccToRgbScalar` | 3.742 µs |
| `YccToRgb` (vector) | 1.089 µs |
| `YcckToCmykScalar` | 3.835 µs |
| `YcckToCmyk` (vector) | 1.252 µs |

`DctBenchmarks.Decode`, the whole decode, before (integration branch) and after, `--job short --inProcess`, JIT, M4 Max (`Allocated` `-` on both sides):

| Vector | Base | Head | Change |
|---|---:|---:|---:|
| testorig (4:2:0) | 178.3 µs | 143.8 µs | -19% |
| sampling-444 | 25.5 µs | 22.1 µs | -13% |
| restart-blocks | 179.8 µs | 143.8 µs | -20% |
| multiscan | 20.0 µs | 16.2 µs | -19% |
| progressive | 243.1 µs | 208.3 µs | -14% |
| arithmetic-progressive | 462.0 µs | 421.9 µs | -9% |
| ycck | 27.9 µs | 24.4 µs | -12% |
| gray-2x2 (no transform) | 9.30 µs | 9.26 µs | 0% |

## Pixel packing and gray expansion (`PixelPacking`, `GrayExpansion`, #472)

Packing a converted RGB row into BGRA (or RGBA, WebGL), opaque or premultiplied by its folded opacity, as the software rasterizer's
`ImageSource` and every backend's `ImageConverter` do (`ImageRows.PackBgra`/`PackRgba`); and expanding 8-bit gray to RGB, the byte
path of a one-component space whose table holds the grays. ns per pixel (gray: per sample):

| Build | BGRA opaque scalar | vector | speed-up | BGRA premultiplied scalar | vector | speed-up | RGBA premultiplied scalar | vector | speed-up | Gray → RGB scalar | vector | speed-up |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| JIT | 0.771 | 0.056 | 13.69x | 1.557 | 0.302 | 5.15x | 1.295 | 0.310 | 4.18x | 0.524 | 0.040 | 13.12x |
| Native AOT | 1.032 | 0.062 | 16.60x | 1.576 | 0.311 | 5.08x | 1.326 | 0.311 | 4.26x | 0.528 | 0.038 | 13.74x |
| Browser interpreter, CI | 13.447 | 0.954 | 14.10x | 18.406 | 3.338 | 5.51x | 22.602 | 5.054 | 4.47x | 8.178 | 0.358 | 22.87x |
| Browser AOT, CI | 4.053 | 0.215 | 18.89x | 7.844 | 1.907 | 4.11x | 7.534 | 1.812 | 4.16x | 1.758 | 0.060 | 29.50x |
| Browser interpreter, local | 7.636 | 0.621 | 12.29x | 12.101 | 3.819 | 3.17x | 12.118 | 3.748 | 3.23x | 3.232 | 0.320 | 10.11x |
| Browser AOT, local | 1.604 | 0.107 | 14.94x | 2.154 | 0.754 | 2.86x | 2.162 | 0.755 | 2.86x | 0.962 | 0.088 | 10.89x |

The gray scalar column is the replicating loop; the code before #472 copied three bytes per sample from the table with
`Slice`/`CopyTo`, slower still (`GrayToRgbTable` below).

BenchmarkDotNet, `PixelPackingBenchmarks`, one 2,550-pixel row, `--job short`, JIT, M4 Max (`Allocated` is `-` for all seven):

| Method | Mean |
|---|---:|
| `PackOpaqueScalar` | 2.372 µs |
| `PackOpaque` (vector) | 0.179 µs |
| `PackPremultipliedScalar` | 3.516 µs |
| `PackPremultiplied` (vector) | 0.814 µs |
| `GrayToRgbTable` (before #472) | 1.886 µs |
| `GrayToRgbScalar` | 1.024 µs |
| `GrayToRgb` (vector) | 0.128 µs |

Whole page, base (integration branch) and head alternated twice in-process, means of the two runs, M4 Max:

- `ScaledImageBenchmarks.Page` (the catalogue cover built, decoded, converted and rendered cold):
  - 390 pixels, full decode: 209.4 → 192.5 ms (-8%).
  - 390 pixels, scaled decode: 81.7 → 78.1 ms (-4%).
  - 1206 pixels, full decode: 231.2 → 210.8 ms (-9%).
  - 1206 pixels, scaled decode: 229.0 → 211.0 ms (-8%).
- The tile and first-tile benchmarks replay a list whose image is already converted, so they do not change: `ScaledImageBenchmarks.Replay`, `PhoneTileBenchmarks`, `FirstPaintBenchmarks`.

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

## Box filter (`BoxFilter`, #475)

The weighted average of the samples under a device pixel's footprint when an image is drawn smaller than its resolution
(`ImagePainter`): four `double` sums, one per channel. The vector path keeps a lane per channel (blue and green in one
`Vector128<double>`, red and alpha in another), each lane doing the scalar operations in their order, so it is bit-exact. Items are
device pixels; the footprint is n × n random samples with fractional weights on its edge rows and columns (`box-filter-4x4` is the
browser benchmark's case).

| Build | 4 × 4: scalar | vector | speed-up | 2 × 2: scalar | vector | speed-up | 7 × 7: scalar | vector | speed-up |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| JIT | 17.067 | 14.566 | 1.17x | 6.454 | 5.904 | 1.09x | 47.715 | 41.885 | 1.14x |
| Native AOT | 18.137 | 15.727 | 1.15x | 7.442 | 7.209 | 1.03x | 47.582 | 42.244 | 1.13x |
| Browser interpreter, CI | 552.367 | 666.809 | **0.83x** | | | | | | |
| Browser AOT, CI | 96.893 | 105.286 | **0.92x** | | | | | | |
| Browser interpreter, local | 254.890 | 258.315 | **0.99x** | 115.582 | 130.976 | **0.88x** | 603.177 | 586.424 | 1.03x |
| Browser AOT, local | 25.837 | 30.260 | **0.85x** | 11.294 | 12.458 | **0.91x** | 64.403 | 77.965 | **0.83x** |

- **Gated off in the browser.** The vector path is slower than scalar in both browser builds (on CI; locally the interpreter is even), so
  `BoxFilter.Average` takes it only where `Vector128` is accelerated and `!OperatingSystem.IsBrowser()`. The `box-filter-4x4` case
  of the browser benchmark times `AverageVector` itself, so its CI lines show the speed-up below 1 that the gate is for.
- **Why so little.** Each sum depends on the one before it, and the scalar loop's `int`-to-`double` conversions are single
  instructions, so spreading one pixel's four channels over two vectors saves operations (about 13 vector operations a sample against
  15 scalar floating-point ones on Arm64) but not latency. Interleaving two pixels' footprints (four sums in flight) measured no
  faster on the JIT, and was dropped. The research report's estimate of 1.5–2x for this kernel does not hold.
- Getting the vector path to 1.15x took two fixes on the JIT: building the sample with `Vector128.Create(ulong)` (a broadcast)
  instead of `CreateScalarUnsafe` (an insert into one lane of the register's previous value, which chained every sample to the one
  before it: 2.1x slower), and writing the shuffle masks as literals at the call (hoisted, 1.3x slower). The exact conversion takes
  no instruction of its own: the sample is OR'd under the top bytes of 2^52, and one shuffle puts each channel under those bytes,
  so one subtraction of 2^52 leaves it as a `double`.

Most of the gain is the scalar restructuring around the kernel: the footprint's columns, rows and weights are now worked out once per
pixel (rows once per span when the image is upright) instead of once per sample, through `Axis.Index` and `Axis.Weight`, and
switched on the axis's kind once. The same values, so the same bits. The sampler per device pixel, before (`ImagePainter.Average`
as it was) and after (the footprint, then `BoxFilter`), 4 × 4 footprint, ns per pixel:

| Build | Before | After, scalar | After, vector |
|---|---:|---:|---:|
| JIT | 42.4 | 29.8 (1.42x) | 27.0 (1.61x) |
| Native AOT | 49.1 | 29.2 (1.69x) | 28.1 (1.72x) |
| Browser interpreter, local | 1,495 | 692 (2.16x) | |
| Browser AOT, local | 45.5 | 46.6 (0.98x) | |

In the browser AOT build the old loop was already about as fast (the compiler inlined `Index` and `Weight`); working the rows out once
per span is what takes it below the old time on whole pages. 7 × 7: 1.25x there, 2.2x on the JIT.

BenchmarkDotNet, `BoxFilterBenchmarks` (794 device pixels, 4 × 4 footprints), JIT, M4 Max, `--job short` (`Allocated` is `-` for
both):

| Method | Mean |
|---|---:|
| `AverageScalar` | 13.34 µs |
| `Average` (vector) | 12.31 µs (1.08x) |

On Linux x64 (the bench gate's runner, JIT, base and head side by side), the image replays: `ScaledImageBenchmarks.Replay` 65.7 →
27.3 ms at 390 pixels wide and 158.3 → 93.5 ms at 1,206 (full decode), `ScaledScanBenchmarks.Replay` (scan) 214.4 → 81.5 ms,
`PhoneTileBenchmarks.Tiles` (image-photo, software) 160.8 → 94.8 ms.

### Whole-page effect

Where the time went before #475, sampled (macOS `sample`, 1 kHz, Native AOT, 96 DPI, every page rendered in a loop): the box filter
(`ImagePainter.Average`, `Axis.Weight`, `Axis.Index`, `Axis.Create`, `Channel`) was 24% of the catalogue's render time, 20% of the
newsletter's and 19% of the brochure's. After it (`BoxFilter.AverageVector`, `Axis.Fill`, `Axis.Create`, `Channel` and the rest of
`ImagePainter`'s share), 15%, 10% and 10% of the shorter times. The rest is color conversion and JPEG decoding.

Per document, ms per pass of every page at 96 DPI, best of the passes in 5 s, base and head interleaved twice (the better run of
each), M4 Max:

| Document | JIT base | JIT head | Change | Native AOT base | Native AOT head | Change |
|---|---:|---:|---:|---:|---:|---:|
| `brochure.pdf` | 181.9 | 156.2 | **-14%** | 198.9 | 179.4 | **-10%** |
| `newsletter.pdf` | 279.1 | 240.2 | **-14%** | 313.4 | 278.8 | **-11%** |
| `catalogue.pdf` | 1095.4 | 975.2 | **-11%** | 1122.2 | 994.0 | **-11%** |

In the browser: the same documents in a `browser-wasm` console app under Node 22 (every page, 3 or 4 passes, the best after the
first; the best of three runs of the base and the run of the final head, interleaved, M4 Max). Both builds take the scalar kernel, so this is the restructured sampler alone.

| Document | Interpreter base | head | Change | AOT base | head | Change |
|---|---:|---:|---:|---:|---:|---:|
| `brochure.pdf` | 10,085.9 | 8,592.3 | **-15%** | 652.2 | 641.7 | -2% |
| `newsletter.pdf` | 15,928.1 | 13,418.6 | **-16%** | 1046.8 | 1032.3 | -1% |
| `catalogue.pdf` | 60,486.7 | 49,941.8 | **-17%** | 3506.2 | 3470.1 | -1% |

Pixels: the goldens and snapshots (`Broadside.Rendering.Tests`, 1,442 tests) pass, and page 0 of every pdf.js corpus file (989
files, 976 render) at 72 DPI hashes (SHA-256 of the pixels) the same before and after.

## CMYK characterization (`CmykCharacterization`, #476)

DeviceCMYK to RGB through the SWOP polynomial (1 plus 34 monomials per channel, `double`), the default CMYK conversion. The vector
path takes four colors per iteration in two pairs of `Vector128<double>` lanes, each lane doing the scalar operations in their
order (the same clip, the monomials multiplied left to right, each sum started at 0 and added term after term), so it is bit-exact.
Items are colors: four random 8-bit components as `b / 255`, out three `float` channels (`cmyk-to-rgb` is the browser benchmark's
case).

| Build | Scalar | Vector | Speed-up |
|---|---:|---:|---:|
| JIT | 20.246 | 12.835 | 1.58x |
| Native AOT | 20.157 | 9.885 | 2.04x |
| Browser interpreter, CI | 617.981 | 694.275 | **0.89x** |
| Browser AOT, CI | 146.484 | 52.643 | 2.78x |
| Browser interpreter, local | 400.816 | 278.801 | 1.44x |
| Browser AOT, local | 47.814 | 17.687 | 2.70x |

- **Gated off in the browser.** The interpreter build runs the vector path slower than scalar on CI (Chrome on Linux x64), though
  faster under Node on the M4 Max; the AOT build runs it 2.7–2.8x faster, but nothing at run time tells the two builds apart. So
  `CmykCharacterization.ToRgbRow` takes it only where `Vector128` is accelerated and `!OperatingSystem.IsBrowser()`, as the box
  filter does. The `cmyk-to-rgb` case of the browser benchmark times `ToRgbRowVector` itself.
- **Two pairs in flight.** Each channel's sum is a chain of 34 dependent adds that must keep its order. One pair of colors per
  iteration with every monomial held at once measured 1.08x on the JIT; computing each monomial where it is used and running two
  pairs side by side (six chains) took it to 1.32x, and reading the coefficients as prebuilt broadcast vectors
  (`Weights`, one load each) instead of `Vector128.Create(Red[j])` to 1.58x.
- **Run detection kept.** `DeviceConverter` still copies a color equal to the one before it (flat areas, a group's backdrop); each
  stretch of colors that differ from their predecessor converts as one row.

BenchmarkDotNet, `CmykCharacterizationBenchmarks` (794 colors), JIT, M4 Max, `--job short` (`Allocated` is `-` for both):

| Method | Mean |
|---|---:|
| `ToRgbRowScalar` | 14.66 µs |
| `ToRgbRow` (vector) | 7.34 µs (2.00x) |

### Scalar changes in the same pipeline

Profiling first (macOS `sample`, 1 kHz, Native AOT, 96 DPI, every page in a loop) showed where color conversion time went before
#476: in the catalogue (CMYK JPEGs) the characterization and its callers (`ToRgb`, `Clamp01`, `ManagedPipeline`,
`DeviceConverter`, `FromRgb`) were 33% of the samples; in the brochure and the newsletter, 73% went to RGB images in an ICCBased
space converting through their DeviceRGB alternate (`IccAlternateConverter`, `ManagedPipeline`, `ColorPipeline.Convert`,
`Clamp01`, `DeviceConverter`, `FromRgb`, `ToBytes`): every byte through `float`, two clips and back, to come out as the same byte.
The showcase spent under 1% in color conversion. The ICC engine's matrix/TRC (`Broadside.Rendering`'s `IccTransform`) is not on
these paths: it runs only with `UseIccColorManagement`, so it is not vectorized here.

- **ICCBased with a device alternate copies its samples.** `ManagedPipeline` already copied 8-bit DeviceRGB and DeviceCMYK samples
  when the target is the same model; it now does the same for an ICCBased space whose alternate is such a device space and whose
  ranges (and the alternate's) are 0..1. The bytes are the ones the conversion gave (each byte maps to `b / 255`, which the clips
  leave alone, and back); a narrower `/Range` still converts.
- **`Clamp01` inlines.** Two comparisons instead of `Math.Min` (the same bits once the value is positive and not NaN); it was not
  inlined in Native AOT and ran as a call several times per pixel.
- **No modulo per sample.** The range lookups in `ColorPipeline.Convert`, `ManagedPipeline.Convert` and
  `IccAlternateConverter.Convert` walk each component's samples with a stride instead of indexing `_ranges[i % inputs]`.

After #476 the characterization (`ToRgbRowVector`, `ConvertCharacterizedRows`) is 15% of the catalogue's shorter time, and color
conversion is 1% of the brochure's.

### Whole-page effect

Per document, ms per pass of every page at 96 DPI, best of the passes in 6 to 8 s, base (the integration branch before #472) and head (before merging it), M4 Max:

| Document | JIT base | JIT head | Change | Native AOT base | Native AOT head | Change |
|---|---:|---:|---:|---:|---:|---:|
| `brochure.pdf` | 155.3 | 51.8 | **-67%** | 184.0 | 51.3 | **-72%** |
| `newsletter.pdf` | 233.0 | 77.0 | **-67%** | 290.2 | 76.3 | **-74%** |
| `catalogue.pdf` | 979.2 | 861.4 | **-12%** | 994.5 | 805.6 | **-19%** |
| `color-guide.pdf` | 8.9 | 8.6 | -3% | 12.5 | 11.0 | **-12%** |

In the browser: the same documents in a `browser-wasm` console app under Node 22 (every page, 3 or 4 passes, the best after the
first; M4 Max), base the integration branch after #472 and head this change merged onto it. Both builds take the scalar kernel (the
gate above), so the change is the scalar work alone.

| Document | Interpreter base | head | Change | AOT base | head | Change |
|---|---:|---:|---:|---:|---:|---:|
| `brochure.pdf` | 5,161.0 | 1,284.8 | **-75%** | 372.1 | 94.6 | **-75%** |
| `newsletter.pdf` | 7,868.0 | 1,569.5 | **-80%** | 597.2 | 143.0 | **-76%** |
| `catalogue.pdf` | 29,320.6 | 16,507.4 | **-44%** | 1,800.3 | 1,578.5 | **-12%** |

Pixels: the goldens and snapshots (`Broadside.Rendering.Tests`, 1,442 tests) pass, and page 0 of 1,228 files (the pdf.js corpus,
the GWG output suite, the PDF 2.0 examples and the sample documents; 1,113 render) at 72 DPI hashes (SHA-256 of the pixels) the
same before and after. The kernel tests compare 20 million 8-bit colors and the clip's special values (NaN, ±0, denormals, values
outside 0..1, infinities) bit for bit, with hardware intrinsics on and off.
