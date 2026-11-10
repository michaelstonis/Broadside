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
| Browser interpreter, local | 4.268 | 0.627 | 6.81x |
| Browser AOT, local | 0.795 | 0.270 | 2.94x |
| Browser interpreter, local, #470's version | 4.194 | 1.511 | 2.78x |
| Browser AOT, local, #470's version | 0.929 | 1.482 | **0.63x** |

## YCbCr to RGB and YCCK to CMYK (`JpegColor`, #471)

| Build | YCbCr → RGB scalar | vector | speed-up | YCCK → CMYK scalar | vector | speed-up |
|---|---:|---:|---:|---:|---:|---:|
| JIT | 1.398 | 0.415 | 3.37x | 1.521 | 0.475 | 3.20x |
| Native AOT | 1.732 | 0.416 | 4.17x | 1.885 | 0.483 | 3.91x |
| Browser interpreter, local | 8.974 | 4.012 | 2.24x | 10.758 | 4.595 | 2.34x |
| Browser AOT, local | 3.309 | 0.715 | 4.63x | 3.758 | 0.785 | 4.79x |

BenchmarkDotNet, `JpegColorBenchmarks`, one 2,550-sample row, JIT, M4 Max (`Allocated` is `-` for all four):

| Method | Mean |
|---|---:|
| `YccToRgbScalar` | 3.742 µs |
| `YccToRgb` (vector) | 1.089 µs |
| `YcckToCmykScalar` | 3.835 µs |
| `YcckToCmyk` (vector) | 1.252 µs |
