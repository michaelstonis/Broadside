# BenchGate

The benchmark regression gate's comparer (issue #139). What the gate checks, its thresholds, how CI runs it and how to update
the baseline are in [bench/baselines/gate/README.md](../../bench/baselines/gate/README.md); this file is only the tool's own notes.

```sh
dotnet run --project tools/BenchGate -- compare --baseline bench/baselines/gate/allocations.json --results artifacts/bench-gate
dotnet run --project tools/BenchGate -- record --results artifacts/bench-gate --out bench/baselines/gate/allocations.json
dotnet run --project tools/BenchGate -- suspects --results artifacts/bench-gate --out suspects.txt
dotnet run --project tools/BenchGate -- shard --list methods.txt --baseline bench/baselines/gate/allocations.json --count 4 --index 0 --out shard.txt
```

| Command | Does |
|---|---|
| `compare` | The verdict. Exit 0 when nothing fails, 1 on a regression; one `::error` annotation per regression, the Markdown summary on stdout and appended to `--summary <file>` (CI passes `$GITHUB_STEP_SUMMARY`). `--record <file>` also writes the re-recorded baseline. `--base-name` labels the base commit in the summary; `--fail-ratio` overrides the time threshold for experiments. |
| `record` | Writes the pull request side's allocations as a baseline file (sorted, one line per case). |
| `suspects` | Writes the full names of the cases the rounds find slower (BenchmarkDotNet `--filter` matches a full name with its parameters), for `bench/run-gate.sh`'s confirmation rerun. |
| `shard` | Splits the methods of `--list flat` over `--count` runners, balanced by the baseline's case count per method. |

Exit code 2 is a bad command line or an unreadable input.

Results layout (what `bench/run-gate.sh --out <dir>` writes; shard folders above it are merged):

```
<dir>/[<shard>/]round-<n>/{base,head}/results/*-report-full-compressed.json
<dir>/[<shard>/]confirm/{base,head}/results/*-report-full-compressed.json
```

## Layout

- `GateResults.cs`: reads BenchmarkDotNet's full JSON reports (`Workload`/`Result` iterations, `BytesAllocatedPerOperation`) into rounds.
- `AllocationBaseline.cs`: the baseline file.
- `BenchmarkGate.cs`: the allocation and time rules, sharding and suspects; `GateOptions.cs` holds the thresholds.
- `GateReport.cs`: findings and the job summary.
- `Program.cs`: argument parsing and console output only.

`tests/BenchGate.Tests` drives the rules with synthetic reports and reads a real one from `bench/baselines/phase1/`, so
`dotnet test Broadside.Core.slnf` covers it.
