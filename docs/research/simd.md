# SIMD in Broadside (research, 2026-11-09)

The owner asked whether Broadside can gain meaningful performance from .NET SIMD (`Vector<T>`, `Vector128/256/512<T>`, and the x86, Arm
and WebAssembly hardware intrinsics), and where. This report answers that for every target Broadside ships to: x64 and Arm64 desktop,
the MAUI shells on iOS, Android, Mac Catalyst and Windows, and the Blazor WebAssembly viewer.

Every claim about .NET or a platform links to the source that owns it: Microsoft Learn, the `dotnet/runtime` source at
`release/10.0`, or the WebAssembly specification. Claims I could not confirm at a primary source are marked **unverified**. The
measurements were made on a throwaway prototype in the session scratchpad (deleted). Nothing here changes the library.

## Summary

1. **Yes, but in a few places, and not where most PDF time goes.** Broadside has no explicit SIMD today. It gets some for free from the
   BCL (`SearchValues` in the lexers, `Span.Fill`, `IndexOfAnyExcept`, `BinaryPrimitives.ReverseEndianness`). The work that vectorizes
   well is the **per-pixel row work** after decoding: YCbCr to RGB, packing samples into BGRA, compositing a partly covered span, group
   and soft-mask arithmetic, and the integer IDCT. The work that dominates some pages does not vectorize: Huffman and MQ decoding
   (DCT, JPX tier-1, JBIG2, CCITT), the sparse cell rasterizer, Type 4 functions, and glyph flattening. Those need algorithmic fixes
   (scaled decode, a glyph cache, the tier-1 rewrite), which the [perf deep dive](../benchmarks/perf-deep-dive.md) already ranks.
2. **Measured on the prototype (Apple M4 Max, Arm64, local numbers), bit-exact with the scalar code:**
   - Partial-coverage span fill (`Compositor.Fill`): **6.4x** under the JIT and 6.5x under Native AOT.
   - YCbCr to RGB (`JpegColor.YccToRgb`): **2.3x** JIT, 3.0x Native AOT.
   - PNG Up predictor: **6.9x**.
   - All three are integer code. The YCbCr kernel and the fill were checked exhaustively against the scalar path: all 2^24 (Y, Cb, Cr)
     triples, and every coverage and channel value for the fill. Up is a plain byte add with wraparound in both versions.
3. **`Vector128<T>` is the only width that pays everywhere.**
   - Arm64 (Apple silicon, iOS, Android, Windows on Arm) and WebAssembly have 128-bit vectors only.
   - On Mono, the runtime of the .NET 10 MAUI shells, `Vector256` is never accelerated, and the Arm-specific `AdvSimd` intrinsics need
     LLVM. LLVM is on only for iOS Release builds. Android and Mac Catalyst get the portable `Vector128` API and nothing else.
   - `Vector256` and `Vector512` help only x64 desktop under the JIT. Native AOT on x64 defaults to SSE2 unless the app raises the
     instruction set.
4. **In the browser, SIMD is already switched on for the demo, but the interpreter is fragile.**
   - `WasmEnableSIMD` defaults to true in .NET 10, so the demo already requires WebAssembly SIMD (Safari 16.4+, Chrome 91+, Firefox 89+).
   - The AOT build (what the demo ships) compiles `Vector128` to native `v128` instructions.
   - The interpreter (`--no-aot` builds) maps only some `Vector128` methods to SIMD opcodes. Under it, my first YCbCr kernel was
     **2.6x slower** than scalar. Rewritten to avoid the operations the interpreter does not map, it became 1.4x faster with the
     jiterpreter on, and stayed slower than scalar with it off. The fill was 2.2x faster and the predictor 3.5x.
   - So browser gains need measuring per kernel, on both the AOT and the interpreter build.
5. **Recommendation:**
   - Add a small internal set of `Vector128` row kernels with a scalar fallback, in this order:
     1. image row conversion (YCbCr, BGRA packing and premultiplication, gray and CMYK expansion);
     2. compositing rows (partial-coverage fill, `Over`, `Mix`, knockout, soft-mask multiply);
     3. the islow IDCT in `int` lanes;
     4. the box filter's channel accumulation.
   - Keep each kernel bit-exact by construction, test each one exhaustively against the scalar path, and run the tests with
     `DOTNET_EnableHWIntrinsic=0`.
   - Do not add `Vector256`, `Vector512` or platform intrinsics until a benchmark shows they beat `Vector128` on a target that needs it.
   - Expected effect on whole pages: 10–30% on image-heavy and transparency-heavy pages, and nothing measurable on text pages. That is
     an estimate from the hotspot shares in `docs/benchmarks/`, not a measurement.

## What SIMD in .NET 10 offers

[Microsoft Learn: Use SIMD and hardware intrinsics in .NET](https://learn.microsoft.com/en-us/dotnet/standard/simd) is the reference.
The [`dotnet/runtime` vectorization guidelines](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/vectorization-guidelines.md)
now defer to it and add repository-specific notes. The points that matter here:

- **Layers.** The SIMD article lists five layers:
  - `System.Numerics` fixed-shape types (`Vector2/3/4`, `Matrix4x4`);
  - variable-width `Vector<T>`;
  - fixed-width `Vector64/128/256/512<T>` with cross-platform operations;
  - platform intrinsics in `System.Runtime.Intrinsics.X86`, `.Arm` and `.Wasm`;
  - `TensorPrimitives`, which ships in the separate `System.Numerics.Tensors` package.

  The fixed-width types are "the recommended starting point for new vectorized algorithms"
  ([SIMD article](https://learn.microsoft.com/en-us/dotnet/standard/simd#types-of-simd-support-in-net)).
- **`Vector128<T>` first.** "`Vector128<T>` is the common denominator across every platform that supports vectorization, so it's the best
  place to start." The best-practices list says to add wider widths and intrinsics "only for measured hot paths"
  ([SIMD article, best practices](https://learn.microsoft.com/en-us/dotnet/standard/simd#best-practices)). The portable operations
  "already lower to efficient instructions on each platform". For example, `(v & mask) == Zero` becomes `ptest` on x64
  ([platform-specific intrinsics](https://learn.microsoft.com/en-us/dotnet/standard/simd#platform-specific-hardware-intrinsics)).
- **Width on Arm.** `PreferredVectorBitWidth` "is a general knob, but only x86/x64 offers widths above 128 today"
  ([instruction-set knobs](https://learn.microsoft.com/en-us/dotnet/standard/simd#instruction-set-configuration-knobs)). On Arm64,
  `Vector256<T>.IsHardwareAccelerated` is false. This matched the local run, which printed `Vector256=False`.
- **`Vector<T>`** is fixed per process. Its default width can be narrower than the hardware's: "`Vector512<T>.IsHardwareAccelerated` can
  be `true` while `Vector<T>` stays 256-bit" ([knobs](https://learn.microsoft.com/en-us/dotnet/standard/simd#instruction-set-configuration-knobs)).
  It also gives up compile-time knowledge of the width, which shuffle-based kernels such as RGB interleaving need. Use the fixed widths.
- **`IsHardwareAccelerated` and `Count` are JIT-time constants.** Untaken branches are removed, so there is no cost to checking, and you
  should not cache them ([check for hardware acceleration](https://learn.microsoft.com/en-us/dotnet/standard/simd#check-for-hardware-acceleration)).
  Every operation has a software fallback. That makes correctness portable, but the fallback can be far slower than scalar code (see
  the WebAssembly interpreter below).
- **Remainders and bounds.** "Mishandling the remainder is a common source of bugs." The article recommends `Vector128.Create(span)`,
  `CopyTo`, or the `LoadUnsafe`/`StoreUnsafe` overloads that take a reference and an element offset, over pointers or `ref` arithmetic
  ([load and store safely](https://learn.microsoft.com/en-us/dotnet/standard/simd#load-and-store-vectors-safely)). Spans fit the
  project's safe-code style and keep hot loops allocation-free. The prototype allocated nothing (BenchmarkDotNet `MemoryDiagnoser`: `-`).
- **Floating point.** `MultiplyAddEstimate` "might return a result that was rounded as one ternary operation" on hardware with FMA, and
  might not elsewhere
  ([API reference](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.vector128.multiplyaddestimate?view=net-10.0)).
  The SIMD article says the `Estimate` and `Native` variants trade "some precision or ... an IEEE edge-case guarantee". Bit-exact
  kernels must use plain `*` and `+` (or `FusedMultiplyAdd` everywhere, which changes results against today's scalar code). They must
  also avoid `*Estimate` and `*Native`.
- **Testing knobs.** `DOTNET_EnableHWIntrinsic=0` turns all acceleration off, and `DOTNET_EnableAVX2=0` turns off `Vector256` on x64
  ([test all code paths](https://learn.microsoft.com/en-us/dotnet/standard/simd#test-all-code-paths)). These knobs "don't affect code
  that was already compiled ahead of time through ReadyToRun or Native AOT". The knob table on that page is .NET 11's; .NET 10's
  "baseline and AVX-512 knobs ... were reconfigured", so check names against the .NET 10 runtime before scripting them.

## What each target actually gets

The table covers .NET 10, the only target framework (ADR 0002). "Accelerated" means the runtime maps the API to vector instructions
instead of the software fallback.

| Target | Runtime and code generation (default) | `Vector128` | `Vector256` / `Vector512` | Platform intrinsics |
|---|---|---|---|---|
| x64 desktop, JIT | CoreCLR, RyuJIT | Yes (SSE2 minimum) | `Vector256` with AVX2, `Vector512` with AVX-512, decided at run time | `Sse*`, `Avx*`, `Avx512*` per CPU |
| x64, Native AOT | CoreCLR AOT | Yes | Only if the app sets `IlcInstructionSet` ([nativeaot docs](https://github.com/dotnet/runtime/blob/release/10.0/src/coreclr/nativeaot/docs/optimizing.md)); by default "the compiler targets the minimum instruction set", x86-64-v1 in .NET 10 ([hardware requirements](https://learn.microsoft.com/en-us/dotnet/core/compatibility/jit/11/minimum-hardware-requirements)) | Baseline plus opted-in sets |
| Arm64 desktop (macOS, Windows on Arm, Linux), JIT or Native AOT | CoreCLR | Yes (`AdvSimd` is in the minimum: Apple M1, armv8.0-a, same source) | No | `AdvSimd`, `Dp`, `Rdm`, `Crc32`, `Aes`, ... |
| iOS, Release | Mono full AOT **with LLVM** ([MAUI runtimes](https://learn.microsoft.com/en-us/dotnet/maui/deployment/runtimes-compilation?view=net-maui-10.0#what-net-maui-uses-by-default); `MtouchUseLlvm` "enabled for Release builds" on iOS, [build properties](https://learn.microsoft.com/en-us/dotnet/ios/building-apps/build-properties#mtouchusellvm)) | Yes | No ([`simd-intrinsics.c`](https://github.com/dotnet/runtime/blob/release/10.0/src/mono/mono/mini/simd-intrinsics.c): "Vector256/Vector512 ... return NULL") | `AdvSimd` (LLVM) |
| Mac Catalyst, Release | Mono full AOT, LLVM "never enabled by default" (same page) | Yes (Mono's own code generator handles 16-byte vectors on Arm64) | No | **No.** `AdvSimd` falls back: "Hardware intrinsics are LLVM-only" except groups marked JIT-supported (`ArmBase`, `Crc32`, `Dp`), same file |
| Android, Release | Mono profiled AOT plus Mono JIT, LLVM off by default ([`EnableLLVM`](https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-properties#enablellvm), [`AndroidEnableProfiledAot`](https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-properties#androidenableprofiledaot)) | Yes (`MONO_OPT_SIMD` is in Mono's [default optimizations](https://github.com/dotnet/runtime/blob/release/10.0/src/mono/mono/mini/driver.c)) | No | **No** (as Mac Catalyst) |
| Windows (MAUI) | CoreCLR JIT plus ReadyToRun | Yes | x64: as desktop JIT; Arm64: no | As desktop |
| Browser, AOT (`RunAOTCompilation`, what the demo ships) | Mono AOT to WebAssembly through LLVM | Yes, `v128` (`WasmEnableSIMD` defaults to on, below) | No (Mono returns no intrinsic for sizes other than 16 on WebAssembly, same file) | `PackedSimd` |
| Browser, interpreter (`--no-aot`, debug) | Mono interpreter plus jiterpreter | Reports accelerated. **A subset** of methods becomes SIMD opcodes; the rest runs the managed fallback | No | `PackedSimd` (subset) |

Notes on the table:

- **iOS and Mac Catalyst Debug** builds on Arm64 use "Mono + AOT + interpreter". **Android Debug** builds use the Mono JIT and
  interpreter ([MAUI runtimes](https://learn.microsoft.com/en-us/dotnet/maui/deployment/runtimes-compilation?view=net-maui-10.0#what-net-maui-uses-by-default)).
  Debug performance on device is not representative.
- **NativeAOT on iOS and Mac Catalyst** is opt-in (`PublishAot`, same page). It uses RyuJIT, so it gets `AdvSimd`.
- **CoreCLR on Android** is experimental in .NET 10 (same page).
- **.NET 11 changes the mobile picture.**
  - .NET 11 MAUI drops Mono for CoreCLR: ReadyToRun plus JIT on Android, and ReadyToRun plus the CoreCLR interpreter on iOS and Mac
    Catalyst (same page, `net-maui-11.0`).
  - The library stays `net10.0`, but apps that reference it can run on .NET 11.
  - **Unverified:** how much of `Vector128` the CoreCLR interpreter accelerates. ReadyToRun code would use the R2R target (Apple M1
    for Arm64), so precompiled `Vector128` code is fine.
- **WebAssembly SIMD** is fixed-width 128-bit: "The type v128 corresponds to a 128 bit vector of packed integer or floating-point data"
  ([WebAssembly core spec, vector types](https://webassembly.github.io/spec/core/syntax/types.html#vector-types)).

### The browser in detail

- **SIMD is on by default.** In the .NET 10 targets, `WasmEnableSIMD` defaults to `$(WasmEnableExceptionHandling)`, which defaults to
  `true` ([`BrowserWasmApp.targets`](https://github.com/dotnet/runtime/blob/release/10.0/src/mono/browser/build/BrowserWasmApp.targets),
  lines 4–5).
  - The same file links `libmono-wasm-simd.a` and passes `-msimd128` when SIMD is on.
  - The trimmer substitutes `Vector.IsHardwareAccelerated`, `Vector128.IsHardwareAccelerated` and `PackedSimd.IsSupported` with `true`
    ([`ILLink.Substitutions.WasmIntrinsics.xml`](https://github.com/dotnet/runtime/blob/release/10.0/src/mono/browser/build/ILLink.Substitutions.WasmIntrinsics.xml)).
    `Vector256` is not substituted.
  - Microsoft Learn documents the property as an opt-out: "To disable SIMD, for example when targeting old browsers ... set
    `<WasmEnableSIMD>` ... to `false`"
    ([Blazor WebAssembly runtime performance](https://learn.microsoft.com/en-us/aspnet/core/blazor/performance/webassembly-runtime-performance?view=aspnetcore-10.0#single-instruction-multiple-data-simd)).
  - The demo project does not set it, so the demo already depends on WebAssembly SIMD whether or not Broadside uses it.
- **Browser support** ([webassembly.org feature table](https://webassembly.org/features/), data file `features.json`):
  - Fixed-width SIMD: Chrome 91, Firefox 89, Safari 16.4.
  - Relaxed SIMD: Chrome 114 and Firefox 145; Safari only behind a flag.
  - **Unverified:** whether .NET 10 exposes relaxed SIMD at all. Do not plan on it.
- **The interpreter accelerates only part of the API** ([`transform-simd.c`](https://github.com/dotnet/runtime/blob/release/10.0/src/mono/mono/mini/interp/transform-simd.c)).
  - It maps `Vector128`, `Vector<T>` and `PackedSimd` methods to interpreter SIMD opcodes. Those opcodes are implemented with
    `wasm_simd128.h` in [`interp-simd.c`](https://github.com/dotnet/runtime/blob/release/10.0/src/mono/mono/mini/interp/interp-simd.c).
  - It returns `Vector128.IsHardwareAccelerated` as 1.
  - **Only methods in its tables are mapped:**
    - operators;
    - `Create` and `Load`/`Store`;
    - comparisons and `ConditionalSelect`;
    - shifts;
    - `Min`/`Max`;
    - `WidenLower`/`WidenUpper` and `Narrow`;
    - `Shuffle`;
    - a `PackedSimd` alias list.
  - Anything else (for example the tuple-returning `Widen`, `MultiplyAddEstimate`, `Sum`, `Dot`, anything on `Vector256`) runs the
    managed software fallback, one lane at a time.
  - The jiterpreter then compiles hot interpreter traces to WebAssembly, with `jiterpreter-simd-enabled` on by default
    ([`options-def.h`](https://github.com/dotnet/runtime/blob/release/10.0/src/mono/mono/utils/options-def.h)). The interpreter and
    jiterpreter SIMD work landed in [dotnet/runtime#82773](https://github.com/dotnet/runtime/pull/82773).
- **What that means, measured** (Node 22 and the .NET 10.0.14 `browser-wasm` runtime pack, interpreter build; nanoseconds per
  iteration of a 4096-step loop; local):

  | Operation | ns |
  |---|---:|
  | `Shuffle` (byte, constant mask) | 1.2 |
  | `int` multiply | 1.4 |
  | `Narrow(ushort → byte)` | 1.3 |
  | `WidenLower` | 1.6 |
  | `Min`/`Max` | 3.2 |
  | **`Narrow(uint → ushort)`** | **12.7** |
  | **`MultiplyAddEstimate(float)`** | **27.2** |

  One slow operation is enough to lose the whole gain. The first YCbCr kernel used the tuple-returning `Widen`, local helper
  functions and six `Narrow(uint → ushort)` per 16 pixels, and ran 2.6x slower than scalar. With `WidenLower`/`WidenUpper` and the
  code inlined by hand it was still 1.5x slower. Replacing the six narrows with byte shuffles made it 1.4x faster than scalar (table
  below). With the jiterpreter's traces turned off (`--no-jiterpreter-traces-enabled`), everything is about 10x slower. The fill
  and the predictor still win 1.8x each, but even the final YCbCr kernel is 1.4x slower than scalar (314 against 227 µs).
- **AOT build: not measured here.** It needs the `wasm-tools` workload, which this machine does not have. The CI `blazor-wasm` job
  builds AOT, and the kernels should be measured there before they are claimed for the browser. In the AOT build the LLVM back end
  emits `v128` instructions, so the ratios should be closer to native Arm64 than to the interpreter's. **Unverified.**
- **Threads stay off.** SIMD is independent of WebAssembly threads. Nothing here needs `WasmEnableThreads`, and the project rule
  against it stands.

## Current use of SIMD in Broadside

A search of `src/` on `spec/33-broadside-v1` at 3229c9b0 found no explicit vector code: no `Vector128`, `Vector256`, `Vector<T>`,
`System.Runtime.Intrinsics` or `IsHardwareAccelerated` anywhere. What exists:

- **`SearchValues<byte>`** in the lexers and parsers, which the BCL vectorizes:
  - `CosLexer`, `ContentReader`, `CosParser`, `CrossReferenceReader` and `FileHeader`;
  - `PostScriptLexer`, `PostScriptTokenizer` and `Type1Layout`.

  The lexer is already on the fast path, so there is nothing to add there.
- **Vectorized BCL span methods:**
  - `Span<T>.Fill` for opaque spans (`Compositor.Fill`, `SoftwareRasterizer`);
  - `IndexOfAnyExcept` for runs (`RasterClip`, `SoftMaskPlane`);
  - `BinaryPrimitives.ReverseEndianness(span, span)` for 16-bit samples (`ImageRows.Unpack`);
  - `CopyTo` and `SequenceEqual`.
- **Scalar bit operations** (`BitOperations.LeadingZeroCount`, `PopCount`, `Log2`) in CCITT, JBIG2, the JPEG encoder and OpenType
  positioning.
- **Fixed-point and table tricks** that already make the scalar code fast:
  - libjpeg's integer IDCT and range-limit table;
  - YCbCr lookup tables;
  - `Divide255` by shifts;
  - 8-bytes-per-lookup 1-bit unpacking (`ImageRows.Unpack1`).

## Where the time goes

From the existing measurements:

- **[Perf deep dive](../benchmarks/perf-deep-dive.md) (H1–H11).** It names these hotspots:
  - color conversion (`ManagedPipeline`, the CMYK characterization: about 25% of the pinned pages at 150 DPI before its fix);
  - group results conversion (32% inclusive);
  - mesh triangles (13%);
  - software text: flattening and cell sort, 49% + 17% of a text page's tiles;
  - JPX tier-1: 75% of `issue5567.pdf`'s 500 ms build;
  - Type 4 functions (8%).
- **[Scaled image decode research](image-decode-scaled-and-native.md).** On image pages, "decode is not where most of the time goes".
  Most goes to the per-pixel work after it: sample conversion, the color pipeline and the box filter. For the catalogue cover that was
  145–170 ms of drawing against 49 ms of decode (JIT), and 3.9 s of 4.3 s in the browser interpreter. Scaled decode (#362, #428)
  has since cut the pixel count. The per-pixel stages are the same.
- **[Sample documents baseline](../benchmarks/sample-documents-baseline.md).** Text pages render in 3–15 ms, and image pages in
  60–480 ms native and up to 14 s in the browser interpreter.

So SIMD can matter on image and transparency pages, through the per-pixel stages. On text pages the cost is in geometry and coverage,
where SIMD does not help.

## Measurements

The prototype copied three scalar kernels verbatim from Broadside and wrote `Vector128` versions with only cross-platform APIs:

- `JpegColor.YccToRgb`, in `int` lanes with byte-shuffle interleaving to RGB;
- the partial-coverage loop of `Compositor.Fill`, in `ushort` lanes, where `Divide255` is exact because every intermediate value is
  at most 65,407;
- the PNG Up predictor, a byte add.

**Correctness:** the YCbCr and fill kernels were byte-identical to the scalar code in every check:

- YCbCr: all 2^24 (Y, Cb, Cr) triples;
- fill: every coverage from 1 to 254, every source channel value and every destination byte;
- 200 random rows whose lengths are not multiples of 16.

The exhaustive checks ran under the JIT, under the JIT with `DOTNET_EnableHWIntrinsic=0` (the fallback path), and under Native AOT.
The interpreter ran the random rows only.

**Machine and method:**

- Apple M4 Max, macOS 27.2, .NET SDK 10.0.403, runtime 10.0.14.
- JIT: BenchmarkDotNet 0.15.8, `--job short`, `MemoryDiagnoser`.
- Native AOT and the WebAssembly interpreter: a Stopwatch harness, best of 7 runs after warm-up.
- WebAssembly: Node 22, the `browser-wasm` runtime pack built without AOT (interpreter plus jiterpreter, SIMD on by default).

**Inputs:** YCbCr is one 2,550-sample row (the catalogue cover's width), the fill one 256-pixel tile row, and Up one 8,160-byte row.

All numbers are local Apple Arm64 numbers.

| Kernel | JIT scalar | JIT `Vector128` | Native AOT scalar → `Vector128` | Wasm interpreter scalar → `Vector128` |
|---|---:|---:|---:|---:|
| YCbCr → RGB, 2,550 samples | 3.60 µs | 1.56 µs (**2.3x**) | 4.21 → 1.43 µs (**3.0x**) | 22.7 → 16.5 µs (**1.4x**); first version 59.9 µs (**0.38x**) |
| Fill, 256 px at coverage 100 | 384 ns | 60 ns (**6.4x**) | 370 → 57 ns (**6.5x**) | 3.40 → 1.55 µs (**2.2x**) |
| PNG Up, 8,160 bytes | 2.32 µs | 0.34 µs (**6.9x**) | 2.28 → 0.34 µs (**6.7x**) | 20.5 → 5.8 µs (**3.5x**) |

What the numbers say:

- **Native Arm64 gains are near the lane count for simple byte arithmetic.** Gains are smaller for kernels that widen to 32 bits and
  shuffle: YCbCr does about 40 vector operations per 16 pixels in `int` lanes. A 16-bit fixed-point formulation would be faster but
  needs care to stay exact. **Unverified:** whether libjpeg-turbo's NEON and SSE2 color converters are bit-exact with its C code.
- **The interpreter is 6–10x slower than the JIT even with the jiterpreter**, and SIMD recovers only part of that. The interpreter is a
  fallback build. AOT is the real fix for the browser, and the demo ships it.
- **The interpreter punishes unmapped operations.** Write kernels from the mapped subset: `WidenLower`/`WidenUpper` rather than
  `Widen`, no `*Estimate`, byte `Shuffle` rather than `Narrow` from 32-bit lanes. Measure each kernel there.
- **Native AOT matched or beat the JIT on these kernels.** That is the Arm64 Native AOT case. On x64, Native AOT without
  `IlcInstructionSet` would have SSE2 only, which is enough for `Vector128`.

## Candidate hot paths, ranked

Ranked by expected whole-page effect across the targets that matter (phone, browser AOT, desktop), then by effort and risk. Gains for
kernels I did not prototype are estimates from lane counts and the prototype, and are marked as such.

| # | Path (files) | What vectorizes | Kernel gain | Page-level effect | Effort | Risk | Targets |
|---|---|---|---|---|---|---|---|
| 1 | **Image row conversion**: `JpegColor.YccToRgb`/`YcckToCmyk` (`Filters/Dct`), the BGRA pack and premultiply loop in `ImageSource.Convert` (`Broadside.Rendering`), the 8-bit gray, RGB and CMYK row paths in `ManagedPipeline`/`DeviceConverter`, and the backends' `ImageConverter`s | Integer per-pixel arithmetic and byte interleaving | YCbCr 2.3–3.0x measured; pack and premultiply 3–6x (estimate, like the fill) | Every image pixel passes through these once per decode or resample. Large on photo pages, in the browser as well | S–M | Low: integer, exhaustively testable against the scalar code | All |
| 2 | **Compositing rows**: `Compositor.Fill` (partial coverage), `Over`, `Mix`, `KnockOut`, `RemoveBackdrop`, `Union`; soft-mask and clip-mask multiplication (`SoftwareRasterizer.SoftMasks`, `GroupLayer`, `RasterClip`) | `ushort`-lane `Divide255` arithmetic | Fill 6.4x measured; others 3–6x (estimate) | Translucent fills, groups, soft masks: GWG blend pages, the brochure. Antialiased glyph edges are 1–2 pixels long and gain nothing | M | Low: integer and exact | All (software rasterizer: Blazor, `Broadside.Rendering` users) |
| 3 | **Integer IDCT** (`InverseDct.Transform`, islow): the column pass and the row pass on four columns or rows at a time in `int` lanes, as in libjpeg-turbo's SIMD versions | 32-bit multiply and add, shifts, range-limit clamp | 2–3x on the IDCT (estimate) | Decode share of photo pages. The Huffman decode, which is serial, stays. **Unverified:** IDCT's share of decode time; profile `DctBenchmarks` first | M | Low to medium: the same integer operations, so exact by construction (32-bit wraparound matches), with `djpeg -dct int` as the existing oracle | All |
| 4 | **Box filter** (`ImagePainter.Average`, `Bilinear`, `SampleMask`): accumulate the four channels in two `Vector128<double>` (or one `Vector256<double>` on AVX) | Lane per channel, the same operation order as today | 1.5–2x on the sampler (estimate) | Image drawing at reduced size, every target using the software rasterizer | M | Medium: exact only if every lane does today's scalar operations in the same order with no fused multiply-add. Verify with goldens and the 982-file pixel hashes | All |
| 5 | **CMYK characterization and ICC matrix/TRC** (`CmykCharacterization.ToRgb`, `Icc/*`) | Across pixels (structure of arrays) in `double` lanes | About 2x Arm64 and Wasm, about 4x AVX2 (estimate) | CMYK pages (Ghent, the largest pinned pages). The run-of-one-color cache already removes much of the work | M | Medium: a large polynomial must keep the term order; bit-identical was already proven for the unrolled scalar version, and the same check applies | All |
| 6 | **JPX wavelet and MCT** (`JpxWavelet`, `JpxComposer`): the vertical lifting pass on 4 or 8 columns at once instead of gathering a column; RCT/ICT per pixel | `int` (5/3, RCT) and `float` (9/7, ICT) lanes | 2–4x on the wavelet (estimate) | Small: tier-1 is 75% of JPX time and serial. Do this after #352's tier-1 rewrite | M | Low (5/3) to medium (9/7 float order) | All |
| 7 | **PNG and TIFF predictors** (`Predictor`) | Up: byte add (6.9x measured); Sub, Average, Paeth and TIFF 2 carry a dependency along the row and gain little | Up 6.9x | Small: Flate dominates predicted streams | S | Low | All |
| 8 | **Group result conversion and mesh fills** (`SoftwareRasterizer.ConvertResult`, `MeshLayer.FillTriangle`) | Per-pixel color arithmetic | Unknown | Already reduced by run detection (H6, H7) | M | Medium | All |

Not worth SIMD (serial, gather-bound, or already vectorized):

- **Entropy decoders:** the Huffman (DCT), MQ (JPX tier-1, JBIG2) and CCITT/MMR decoders, plus LZW and RunLength. They are serial bit
  streams.
- **The sparse cell rasterizer** (`ScanlineRasterizer`): it scatters to sorted cells in `double`. Its spans end in `Compositor.Fill`,
  which is item 2.
- **8-bit transfer and ICC CLUT lookups** (`TransferPipeline`, `IccClut`): a 256-entry table gather. Arm's `TBL` covers 64 bytes per
  lookup and WebAssembly's `i8x16.swizzle` 16, so a full 256-entry lookup costs several shuffles per vector. Measure before trying.
- **Type 4 functions, glyph flattening, the lexers:** algorithmic work. The lexers already use `SearchValues`.
- **ASCIIHex and ASCII85:** rare in practice, and short.

## Recommendations

1. **One internal home for row kernels.**
   - Put the integer row kernels in an internal static class in `Broadside` (filters and color) and one in `Broadside.Rendering`
     (compositing). Each kernel is shaped like the SIMD article's example:

     ```csharp
     if (Vector128.IsHardwareAccelerated)
     {
         // loop on Vector128<T>
     }

     // scalar remainder
     ```

   - Use only cross-platform `Vector128` APIs from the interpreter-mapped subset: operators, `Create`/`CopyTo`,
     `WidenLower`/`WidenUpper`, `Narrow` from 16-bit lanes, `Shuffle` with constant masks, `Min`/`Max`, shifts and `ConditionalSelect`.
   - No `Vector256`, `Vector512`, `Vector<T>` or platform intrinsics until a benchmark shows a need on a target that has them. The x64
     desktop JIT is the only one that has them, and it is the fastest target already.
   - No new package: everything is in the BCL. `TensorPrimitives` would add `System.Numerics.Tensors`, a Microsoft managed package.
     The hard rule allows it, but it fits only floating-point span math, which is not where these kernels are.
2. **Bit-exactness is a requirement, not a hope.**
   - Integer kernels are exact by construction. Prove it with an exhaustive or near-exhaustive test against the scalar kernel, kept
     next to the feature. The prototype's tests ran in seconds natively.
   - Floating-point kernels must give each lane exactly the scalar operation sequence: lane per channel or lane per pixel, no fused
     multiply-add, no `Estimate` or `Native` methods, no reordered reductions. Prove it with the goldens and the 982-file pixel hash
     comparison already used in the perf work.
3. **Test both paths.**
   - Run the unit tests for these kernels a second time with `DOTNET_EnableHWIntrinsic=0`, as the SIMD article advises, and add lengths
     that are not multiples of 16.
   - CI's `blazor-wasm` job exercises the AOT browser build.
   - The fuzz targets already cover the decoders that call these kernels.
4. **Benchmark per target.**
   - Add BenchmarkDotNet benchmarks next to the existing ones (`ColorConversionBenchmarks`, `CompositingBenchmarks`, `DctBenchmarks`,
     `PngBenchmarks`), with `MemoryDiagnoser` proving zero allocation.
   - Report the browser AOT figure from the CI job and the interpreter figure from a `--no-aot` run, as `sample-documents-baseline.md`
     does. Keep a kernel only if it does not regress the interpreter.
5. **Do the algorithmic items first where they compete.** The glyph coverage cache, the JPX tier-1 rewrite and scaled decode each beat
   any SIMD gain on their pages. SIMD is the next step for the per-pixel stages they leave behind.

## Proposed follow-up issues (not filed)

1. **perf(images): vectorize YCbCr/YCCK to RGB/CMYK rows with `Vector128`.** Bit-exact with `JpegColor`, exhaustive test, benchmark
   in `DctBenchmarks`, interpreter-safe API subset.
2. **perf(rendering): vectorize BGRA packing and premultiplication in `ImageSource.Convert` and the backends' `ImageConverter`s.**
   Includes 8-bit gray and RGB expansion.
3. **perf(rendering): vectorize compositing rows.** Partial-coverage `Compositor.Fill`, `Over` for image spans, `Mix`, `KnockOut`,
   `RemoveBackdrop`, and soft-mask and clip-mask multiplication, in `ushort` lanes. Exhaustive tests.
4. **perf(filters): `Vector128` islow IDCT.** Columns and rows four at a time in `int` lanes, checked against `djpeg -dct int`. Profile
   the IDCT's share of decode first.
5. **perf(rendering): vectorize the box filter's channel accumulation (`ImagePainter`).** Lane per channel. Gate on goldens and the
   corpus pixel hashes.
6. **perf(colors): vectorize the CMYK characterization and ICC matrix/TRC across pixels.** Preserve the term order. Verify over 20 M
   colors as before.
7. **perf(jpx): vectorize the vertical wavelet pass and the RCT/ICT.** After #352's tier-1 rewrite.
8. **test(perf): run kernel tests with `DOTNET_EnableHWIntrinsic=0`, and add a browser-AOT kernel benchmark to the `blazor-wasm`
   job.** The shared test and benchmark infrastructure for items 1–7.
