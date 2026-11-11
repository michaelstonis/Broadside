---
name: adding-a-benchmark
description: "Use when a PR touches a hot path (lexer, content interpreter, rasterizer, codecs) or adds any performance-sensitive code, including a SIMD (Vector128) kernel: add a BenchmarkDotNet class with MemoryDiagnoser, prove zero allocations, run it in dry, short and full modes, and for a kernel prove bit-exactness, the hardware-intrinsics-off run and both browser builds."
---

# Adding a benchmark

`CLAUDE.md` ("Code conventions") requires that hot paths allocate nothing per token, operator, glyph or scanline and that a BenchmarkDotNet benchmark with a `MemoryDiagnoser` assertion proves it. Every benchmark is also gated in CI against a stored allocation baseline and against the base commit's times ("The regression gate" below).

## Where benchmarks live

`bench/Broadside.Benchmarks/`, namespace `Broadside.Benchmarks`, one class per hot path (`LexerBenchmarks`, `FlateBenchmarks`, `RasterizerBenchmarks`). The project:

- references `src/Broadside/Broadside.csproj`; add a `ProjectReference` to `src/Broadside.Rendering/Broadside.Rendering.csproj` when you benchmark the rasterizer;
- links `tests/Broadside.TestSupport/CorpusLocator.cs` as source, so `CorpusLocator.CorpusDirectory` and `CorpusLocator.CorpusFiles()` are available, but nothing from `Broadside.TestSupport` itself (it would drag xunit and Verify in);
- sees `internal` members of every `src/` package (`InternalsVisibleTo` in `src/Directory.Build.props`), so you can benchmark the internal implementation directly;
- builds optimized; BenchmarkDotNet refuses a Debug assembly.

`Program.cs` runs `BenchmarkSwitcher` with `BenchmarkConfig`, and exits 1 when any benchmark fails validation or execution; that exit code is the CI smoke signal.

## Conventions

- `[MemoryDiagnoser]` on every class. It adds the `Allocated` column.
- No job attributes (`[ShortRunJob]`, `[SimpleJob]`, `[DryJob]`) on the class. `BenchmarkConfig` supplies the job from `--job short` / `--job dry`; without `--job` it is `Job.Default`. A class-level job attribute would add a second job to every run.
- `[GlobalSetup]` loads the inputs once (corpus bytes via `CorpusLocator`), outside the measured body.
- The benchmark body is the hot path and nothing else, and returns a value derived from the work so the JIT cannot discard it.
- `[Params]` over corpus file names when the input matters; keep the values stable, they are part of the result key.
- Method names are stable identifiers; renaming one fails the regression gate until the baseline is updated.
- Per-item costs: set `[Benchmark(OperationsPerInvoke = n)]` when one call processes `n` tokens or scanlines, so the mean is per item.

## Asserting zero allocations

Two layers:

1. The benchmark. With `[MemoryDiagnoser]` the `Allocated` column must read `-` (0 B) for a hot path. Any number there is a regression, whatever the baseline says. Read it from the console table or from `BenchmarkDotNet.Artifacts/results/<Class>-report-github.md`.
2. A unit test next to the feature that fails the build on regressions, because BenchmarkDotNet only reports. Measure through `Broadside.TestSupport.Allocations.Measure` and put the class in the non-parallel `Heavy` collection:

   ```csharp
   [Collection(HeavyTestCollection.Name)]
   public class TokenizerAllocationTests
   {
       [Fact]
       public void Tokenizing_allocates_nothing()
       {
           byte[] bytes = Corpus.Bytes("text-standard14.pdf");

           long allocated = Allocations.Measure(() => RunHotPath(bytes));   // warm-up, then the cheapest of several calls

           Assert.Equal(0, allocated);
       }
   }
   ```

   Never measure one call after a fixed warm-up: under load that is flaky (issue #48), because the per-thread counter also sees unoptimized code still running while the background compiler is starved (tier 0 does not stack-allocate non-escaping objects), `ArrayPool<T>.Shared` arrays trimmed by another test's gen-2 collection, and first-use runtime work. `Measure` warms up, then takes the fewest bytes over several calls with a short pause between them, stopping at zero; a real per-item allocation shows on every call. The `Heavy` collection keeps parallel tests' collections and compilation away from the measurement. Put only the per-item work inside the delegate and create the delegate before the call; one-time buffers (`ArrayPool<T>` rentals, the output list) are allowed per call, so size the input to make per-token allocations visible.

## Running

```sh
dotnet run -c Release --project bench/Broadside.Benchmarks -- --list flat                               # names
dotnet run -c Release --project bench/Broadside.Benchmarks -- --filter '*CorpusBenchmarks*' --job dry   # one in-process iteration, proves it runs (CI)
dotnet run -c Release --project bench/Broadside.Benchmarks -- --filter '*' --job short                  # 3 iterations, quick local signal
dotnet run -c Release --project bench/Broadside.Benchmarks -- --filter '*'                              # full Job.Default run for numbers you quote
dotnet run -c Release --project bench/Broadside.Benchmarks -- --filter '*' --job dry --exporters json   # adds <Class>-report-full-compressed.json
```

Results land in `BenchmarkDotNet.Artifacts/results/` (gitignored). `--job dry` numbers are meaningless; use them only to prove the benchmark executes. Quote `--job short` or full-run numbers in the PR's Verification section, with `Mean` and `Allocated`.

## The regression gate

`.github/workflows/bench-gate.yml` (issues #139, #389; everything about it is in `bench/baselines/gate/README.md`) runs every benchmark of a pull request that touches `src/`, `bench/`, the corpus or the test support code, and fails when:

- the bytes one call of a case allocates (measured by `Broadside.Benchmarks --gate-allocations`, steady state, current thread) differ in any way from `bench/baselines/gate/allocations.json` (more or fewer), or a case is new, renamed or gone without the baseline saying so. **Every** such pull request, about 12 minutes;
- a case is at least 20 % slower than the same benchmark built from the base commit and run beside it on the same runner, overall, and at least 15 % slower in each of two interleaved rounds and in a rerun (the full rule and why are in the README). **Only** when the diff touches a hot path (`HOT_PATHS` in the workflow: `src/Broadside/` `IO`, `Parsing`, `Objects`, `Content`, `Filters`, `Images`, `Fonts`, `Graphics`, `Rendering`, `Text`; `Broadside.Rendering`, `Broadside.Rendering.Skia`, `Broadside.Fonts.Cmaps`, `Broadside.Fonts.Standard14`; SDK and package versions), when the pull request has the `perf` label, on pull requests into `main`, and nightly on the integration branch (a regression opens an issue). About 70 minutes.

What that means for you:

- **A hot-path change gets times automatically.** A change that can slow something outside those paths (the document model, caching, writing, the gate's own timing code in `bench/run-gate.sh` or `tools/BenchGate`) should carry the `perf` label: adding it re-runs the gate with times (`bench-gate-perf.yml`), and later pushes keep them. The `plan` job's summary says whether times ran and why.

- **A new benchmark or a new `[Params]` value, a rename, a removal, or an intended allocation change** fails the gate until the baseline is updated in the same PR. The failing run's summary prints the exact lines to change, and its `bench-gate-baseline` artifact is the whole file re-recorded on the CI runner; commit either. Do not record the file on your own machine: the figures differ by platform.
- **A benchmark with one case per corpus file** passes its `[ParamsSource]` values through `BenchmarkPartition.Select` and is added to the workflow's `SPLIT` list, so its cases spread over the gate's runners (`OpenAndWalkBenchmarks` does).
- **Keep class, method and `[Params]` names stable**: the gate keys every comparison by BenchmarkDotNet's full name.
- **Keep `Allocated` at `-` on hot paths**: the gate locks in whatever the baseline holds, so a zero recorded today is enforced from then on.
- **Only `[GlobalSetup]`/`[GlobalCleanup]` and parameters**: the gate's allocation mode runs each case itself and does not support `[Arguments]` or `[IterationSetup]`; a benchmark that needs them must extend `GateAllocations.cs`.
- **Run it locally** before pushing a hot-path change: `bench/run-gate.sh --base <worktree of the base> --head . --out artifacts/bench-gate --methods <file>` then `dotnet run --project tools/BenchGate -- compare --baseline bench/baselines/gate/allocations.json --results artifacts/bench-gate --subset true`.

## Worked example: `CorpusBenchmarks`

`bench/Broadside.Benchmarks/CorpusBenchmarks.cs` is the placeholder that proves the harness: `[MemoryDiagnoser]` on the class, `[GlobalSetup]` fills `_files` from `CorpusLocator.CorpusFiles()`, and `ReadAllCorpusFiles` reads every file and returns the byte total. A dry run prints:

```
| Method             | Mean     | Error | Allocated |
| ReadAllCorpusFiles | 7.612 ms |    NA |  22.13 KB |
```

`Allocated` is non-zero because `File.ReadAllBytes` allocates; that is fine for a placeholder and would be a failure for a lexer. A lexer benchmark that replaces it keeps the same shape: `[Params("text-standard14.pdf", "xref-stream.pdf")] public string File`, `[GlobalSetup]` reads the bytes once, and `[Benchmark] public int Tokenize()` walks every token and returns the count, with `Allocated` expected to read `-`.

## SIMD kernels

A PR that adds a `Vector128<T>` kernel (background: `docs/research/simd.md`) proves three things besides the benchmark above. The test-only `tests/Broadside.TestSupport/SimdExampleKernel.cs` and its tests in `tests/Broadside.Tests/Simd/SimdKernelAssertTests.cs` are the worked example.

**Shape.** An internal static class with a scalar method, a vector method (the `Vector128` loop, then the scalar method for the tail, run whether or not hardware acceleration is on, so tests can reach it), and a dispatcher that calls the vector method only when `Vector128.IsHardwareAccelerated`. Cross-platform `Vector128` APIs only, inlined by hand, and only operations that are fast in **every** build: the JIT and Native AOT (x64 and Arm64) and both browser builds (the interpreter and AOT). Kernels are written in a self-contained (BCL-only) source file so the browser benchmark can link it (`src/Broadside/Filters/Dct/JpegColor.Vector128.cs` is a production example).

**The operation subset** (measured per vector, #471; `docs/benchmarks/simd-kernels.md`):

- Loads and stores: `Vector128.LoadUnsafe(ref r, (nuint)i)` / `StoreUnsafe`, with `ref r = ref MemoryMarshal.GetReference(span)` and the bounds proved by slicing the spans to the row length before the loop and a loop condition that keeps every access inside it (say so in a comment). Not `Vector128.Create(span)` / `CopyTo(span)`: the interpreter runs them at about 3.4x the cost of `LoadUnsafe`, enough to halve a kernel's gain there.
- Shifts: the operators `>>>`, `>>` and `<<`. **Never the methods** `Vector128.ShiftRightLogical`, `ShiftRightArithmetic` or `ShiftLeft`: in the browser AOT build each costs about 8x the operator (they do not become a single WebAssembly SIMD shift there). That alone made #470's first example kernel 0.6x of scalar there.
- Fine everywhere: the arithmetic and bitwise operators (`+`, `-`, `*` in 16- and 32-bit lanes, `&`, `|`, `^`), `WidenLower`/`WidenUpper`, `Narrow` from 16-bit lanes, `Shuffle` with a constant mask (index 255 selects zero; `Vector128.Create` of byte literals in the loop), `Min`/`Max`, `ConditionalSelect`.
- Avoid: the tuple-returning `Widen`, `Narrow` from 32-bit lanes (12.7 ns in the interpreter; repack with the `& 0xFFFF` / `>>> 16` even-odd split or byte shuffles instead), `*Estimate`, `Sum`, `Dot`, anything on `Vector256`/`Vector512`, and helper methods inside the loop. Build a vector from a scalar with `Vector128.Create(x)` (a broadcast that writes the whole register), not `CreateScalarUnsafe`: on Arm64 the JIT inserts into one lane of the register's last value, which chains every iteration to the one before it (2.1x slower in #475). Write a `Shuffle`'s mask as a `Vector128.Create` of literals at the call: hoisted into a local outside the loop it cost #475 another 1.3x on the JIT.
- Lanes in `double`: `+`, `-`, `*` are fine, but a kernel that only spreads one item's four channels over two `Vector128<double>` saves little (#475, `BoxFilter`): the scalar `int`-to-`double` conversions are single instructions, and the vector path was 1.15x on Arm64 and slower than scalar in the browser AOT build.

**1. Bit-exactness.** A test next to the feature compares the vector method and the dispatcher with the scalar method through `Broadside.TestSupport.SimdKernelAssert`:

```csharp
[Trait("Category", SimdKernelAssert.Category)]
public sealed class YccKernelTests
{
    [Fact]
    public void The_vector_kernel_matches_the_scalar_kernel_on_every_pixel()
    {
        // All 2^24 (Y, Cb, Cr) triples, 3 output bytes each; the first chunk also at every length 0..70 and offset 0..15.
        SimdKernelAssert.ExhaustiveBytes<byte>(3, YccKernel.ToRgbScalar, YccKernel.ToRgbVector, outputsPerItem: 3);
        SimdKernelAssert.ExhaustiveBytes<byte>(3, YccKernel.ToRgbScalar, YccKernel.ToRgb, outputsPerItem: 3);
    }
}
```

- `tests/Broadside.Tests/Filters/Dct/JpegColorKernelTests.cs` is the real one for three input rows (an adapter splits the interleaved items into planes).
- `ExhaustiveBytes(width, ...)` enumerates every item of 1 to 3 input bytes. For larger domains (16-bit samples, two planes), call `Equal(scalar, vector, source, inputsPerItem, outputsPerItem)` with structured input (every value of one channel against a sweep of the other) or `SimdKernelAssert.RandomBytes(n)`; it also runs every short length and misalignment. Kernels with two input spans adapt with a lambda over an interleaved source.
- Either fails on the first differing element with the item's input, and on any store past the destination.
- Floating-point kernels are bit-exact only with each lane doing the scalar operation sequence (no fused multiply-add, no `Estimate` methods, no reordered sums); prove it with the goldens and the corpus pixel hashes as well.

**2. Hardware intrinsics off.** The `SimdKernel` category is what CI's `build-test (ubuntu-latest)` job runs a second time with `DOTNET_EnableHWIntrinsic=0` ("Test the SIMD kernels without hardware intrinsics"): the dispatcher must then take the scalar path and the vector method still has to match on the software fallback. A test without the trait is not rerun. Locally:

```sh
DOTNET_EnableHWIntrinsic=0 dotnet test --project tests/Broadside.Tests -c Release -- --filter-trait "Category=SimdKernel"
```

**3. Both browser builds.** Add a case to `Cases` in `samples/Broadside.Viewer.Blazor.Sample/KernelBenchmarks.cs` and link the kernel's source file in the sample's project file (next to `SimdExampleKernel.cs`; the sample sees no internals). The `blazor-wasm` job publishes the demo with AOT and without (`--no-aot`, the interpreter) on every run, and `KernelBenchmarkTests` runs every case in both in Chrome: it fails if the vector bytes differ from the scalar bytes and logs a line per build and case to the measurement log (the job summary and the `blazor-browser-measurements` artifact):

```
kernel aot example-multiply: scalar 4.327 ns/item, vector 1.574 ns/item, speed-up 2.75x, equal yes, accelerated true
kernel interpreter example-multiply: ...
```

Quote both lines in the PR. **A kernel must not be slower than scalar in either browser build** (owner rule, #471): a `speed-up` below 1 on the `aot` or the `interpreter` line does not ship as it is. Replace the slow operations (the subset above; time a suspect with a loop of that one operation in a `browser-wasm` console app run under Node, which needs only the `wasm-tools` workload), and if one build still loses, gate the vector path per platform (for example keep scalar under `OperatingSystem.IsBrowser()` in the losing build) and say why in the PR. Never ship a regression in one build for a gain in another. Record the JIT, Native AOT and both browser figures in the PR and in `docs/benchmarks/simd-kernels.md`.

## Checklist

- [ ] Class in `bench/Broadside.Benchmarks/`, `[MemoryDiagnoser]`, no job attributes
- [ ] Inputs loaded in `[GlobalSetup]`; body is only the hot path; result returned
- [ ] `--job dry` exits 0; `--job short` numbers quoted in the PR
- [ ] `Allocated` reads `-` on hot paths; an `Allocations.Measure` test in the `Heavy` collection added next to the feature
- [ ] Names stable; any rename called out in the PR body
- [ ] SIMD kernel: `SimdKernelAssert` test with the `SimdKernel` trait, a `KernelBenchmarks` case, both browser lines quoted in the PR with speed-up of at least 1 in each, JIT, Native AOT and browser figures in `docs/benchmarks/simd-kernels.md`
