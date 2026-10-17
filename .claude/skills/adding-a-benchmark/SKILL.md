---
name: adding-a-benchmark
description: "Use when a PR touches a hot path (lexer, content interpreter, rasterizer, codecs) or adds any performance-sensitive code: add a BenchmarkDotNet class with MemoryDiagnoser, prove zero allocations, and run it in dry, short and full modes."
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

`.github/workflows/bench-gate.yml` (issue #139; everything about it is in `bench/baselines/gate/README.md`) runs every benchmark of a pull request that touches `src/`, `bench/`, the corpus or the test support code, next to the same benchmarks built from its base commit on the same runner, and fails when:

- the bytes one call of a case allocates (measured by `Broadside.Benchmarks --gate-allocations`, steady state, current thread) differ in any way from `bench/baselines/gate/allocations.json` (more or fewer), or a case is new, renamed or gone without the baseline saying so;
- a case is at least 20 % slower than the base overall and at least 15 % slower in each of two interleaved rounds and in a rerun (the full rule and why are in the README).

What that means for you:

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

## Checklist

- [ ] Class in `bench/Broadside.Benchmarks/`, `[MemoryDiagnoser]`, no job attributes
- [ ] Inputs loaded in `[GlobalSetup]`; body is only the hot path; result returned
- [ ] `--job dry` exits 0; `--job short` numbers quoted in the PR
- [ ] `Allocated` reads `-` on hot paths; an `Allocations.Measure` test in the `Heavy` collection added next to the feature
- [ ] Names stable; any rename called out in the PR body
