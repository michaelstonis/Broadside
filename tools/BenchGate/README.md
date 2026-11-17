# BenchGate

The benchmark regression gate's comparer (issue #139). What the gate checks, its thresholds, how CI runs it and how to update
the baseline are in [bench/baselines/gate/README.md](../../bench/baselines/gate/README.md); this file is only the tool's own notes.

```sh
dotnet run --project tools/BenchGate -- compare --baseline bench/baselines/gate/allocations.json --results artifacts/bench-gate [--subset true]
dotnet run --project tools/BenchGate -- record --results artifacts/bench-gate --out new.json --baseline bench/baselines/gate/allocations.json
dotnet run --project tools/BenchGate -- suspects --results artifacts/bench-gate --out suspects.txt
dotnet run --project tools/BenchGate -- shard --list methods.txt --baseline bench/baselines/gate/allocations.json --count 4 --index 0 --out shard.txt \
  --split Broadside.Benchmarks.OpenAndWalkBenchmarks.OpenAndWalk
dotnet run --project tools/BenchGate -- sizes --baseline bench/baselines/gate/sizes.json --results artifacts/size-probes/sizes.json [--record bench/baselines/gate/sizes.json]
dotnet run --project tools/BenchGate -- machine --out artifacts/bench-gate/machine.json [--summary "$GITHUB_STEP_SUMMARY"] [--label "This job"]
```

| Command | Does |
|---|---|
| `compare` | The verdict. Exit 0 when nothing fails, 1 on a regression; one `::error` annotation per regression, the Markdown summary on stdout and appended to `--summary <file>` (CI passes `$GITHUB_STEP_SUMMARY`). `--record <file>` also writes the re-recorded baseline. `--base-name` labels the base commit in the summary; `--fail-ratio` overrides the time threshold for experiments; `--subset true` (local runs of a few methods) does not report baseline cases that did not run. |
| `record` | Writes the pull request side's allocation figures as a baseline file (sorted, one line per case). |
| `suspects` | Writes the full names of the cases the rounds find slower (BenchmarkDotNet `--filter` matches a full name with its parameters), for `bench/run-gate.sh`'s confirmation rerun. |
| `sizes` | The Native AOT size probes' verdict (issue #361): compares `bench/size-probes.sh`'s sizes with the baseline, per runtime identifier. Exit 1 when a probe grows by more than `--tolerance` (default 0.01), is new or is missing; a probe that shrinks past it passes with a request to re-record. `--summary` and `--record <file>` as for `compare` (recording replaces the measured runtimes' sizes and keeps the others). |
| `machine` | Writes `machine.json` (issue #539): the processor's model, whether it has AVX-512 (`/proc/cpuinfo` on Linux), whether this process uses it (`Avx512F.IsSupported`, so run it under the benchmarks' runtime settings) and whether `DOTNET_EnableAVX512F=0` or `DOTNET_EnableAVX512=0` disabled it. `--summary` appends one line for people. Exit 1 when AVX-512 was disabled and the runtime still uses it. |
| `shard` | Splits the methods of `--list flat` over `--count` runners, balanced by the baseline's case count per method; the `--split` methods go to every runner (they partition their own cases). |

Exit code 2 is a bad command line or an unreadable input.

Results layout (what `bench/run-gate.sh --out <dir>` writes; shard folders above it are merged):

```
<dir>/[<shard>/]machine.json                                    (BenchGate machine: processor, AVX-512, timing pass)
<dir>/[<shard>/]round-<n>/{base,head}/results/*-report-full-compressed.json
<dir>/[<shard>/]confirm/{base,head}/results/*-report-full-compressed.json
<dir>/[<shard>/]confirm-<n>/{base,head}/results/*-report-full-compressed.json   (second and later reruns)
<dir>/[<shard>/]allocations/{base,head}/allocations.json        (Broadside.Benchmarks --gate-allocations)
```

A base iteration is paired only with the pull request's iterations from the same shard folder, so a time ratio never mixes two
runners. A shard folder whose `machine.json` says AVX-512 was disabled belongs to the AVX-512-off pass, whose rounds and reruns
are compared separately from the default pass's.

## Layout

- `GateResults.cs`: reads BenchmarkDotNet's full JSON reports (`Workload`/`Result` iterations) into rounds, and the allocation files.
- `AllocationBaseline.cs`: the baseline file (one exact number of bytes per case; ranges are refused).
- `BenchmarkGate.cs`: the allocation and time rules, sharding and suspects; `GateOptions.cs` holds the thresholds.
- `GateReport.cs`: findings and the job summary.
- `SizeGate.cs`: the size probes' baseline file, rule and summary.
- `MachineInfo.cs`: the runner's processor and AVX-512 state, and the timing pass it puts a shard in.
- `Program.cs`: argument parsing and console output only.

`tests/BenchGate.Tests` drives the rules with synthetic reports and reads a real one from `bench/baselines/phase1/`, so
`dotnet test Broadside.Core.slnf` covers it.
