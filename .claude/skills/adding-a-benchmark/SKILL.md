---
name: adding-a-benchmark
description: "Use when a PR touches a hot path (lexer, content interpreter, rasterizer, codecs) or adds any performance-sensitive code: add a BenchmarkDotNet class with MemoryDiagnoser, prove zero allocations, and run it in dry, short and full modes."
---

# Adding a benchmark

`CLAUDE.md` ("Code conventions") requires that hot paths allocate nothing per token, operator, glyph or scanline and that a BenchmarkDotNet benchmark with a `MemoryDiagnoser` assertion proves it. Benchmarks also become the baseline for the Phase 3I regression gate.

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
- Method names are stable identifiers; renaming one breaks its history in the regression gate.
- Per-item costs: set `[Benchmark(OperationsPerInvoke = n)]` when one call processes `n` tokens or scanlines, so the mean is per item.

## Asserting zero allocations

Two layers:

1. The benchmark. With `[MemoryDiagnoser]` the `Allocated` column must read `-` (0 B) for a hot path. Any number there is a regression, whatever the baseline says. Read it from the console table or from `BenchmarkDotNet.Artifacts/results/<Class>-report-github.md`.
2. A unit test next to the feature that fails the build on regressions, because BenchmarkDotNet only reports. After one warm-up call:

   ```csharp
   [Fact]
   public void Tokenizing_allocates_nothing()
   {
       byte[] bytes = Corpus.Bytes("text-standard14.pdf");
       RunHotPath(bytes);                                               // warm up: JIT, static init
       long before = GC.GetAllocatedBytesForCurrentThread();
       RunHotPath(bytes);
       Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
   }
   ```

   Put only the per-item work inside `RunHotPath`; one-time buffers (`ArrayPool<T>` rentals, the output list) are allowed per call, so size the input to make per-token allocations visible.

## Running

```sh
dotnet run -c Release --project bench/Broadside.Benchmarks -- --list flat                               # names
dotnet run -c Release --project bench/Broadside.Benchmarks -- --filter '*CorpusBenchmarks*' --job dry   # one in-process iteration, proves it runs (CI)
dotnet run -c Release --project bench/Broadside.Benchmarks -- --filter '*' --job short                  # 3 iterations, quick local signal
dotnet run -c Release --project bench/Broadside.Benchmarks -- --filter '*'                              # full Job.Default run for numbers you quote
dotnet run -c Release --project bench/Broadside.Benchmarks -- --filter '*' --job dry --exporters json   # adds <Class>-report-full-compressed.json
```

Results land in `BenchmarkDotNet.Artifacts/results/` (gitignored). `--job dry` numbers are meaningless; use them only to prove the benchmark executes. Quote `--job short` or full-run numbers in the PR's Verification section, with `Mean` and `Allocated`.

## The Phase 3I regression gate

Track 3I (`docs/plan/README.md`) adds a CI job that runs the suite and compares each benchmark's mean and allocated bytes against a baseline recorded from `main`, using the JSON exporter output above. What that gate needs from you now:

- stable class, method and `[Params]` names, since the comparison is keyed by them;
- `Allocated` already at `-` on hot paths, so the gate can treat any allocation as a failure rather than a percentage;
- one benchmark per operation, not one per test case, so the baseline stays small enough to run on every PR.

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
- [ ] `Allocated` reads `-` on hot paths; `GC.GetAllocatedBytesForCurrentThread` test added next to the feature
- [ ] Names stable; any rename called out in the PR body
