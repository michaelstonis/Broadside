# Benchmark regression gate

The CI gate of spec story 52 (issue #139): every pull request that touches library, benchmark or corpus code runs every
benchmark of `bench/Broadside.Benchmarks` twice over, once built from the pull request and once from its base commit, on the
same runner, and fails when

- **an allocation changes**: a case's bytes allocated per operation differ in any way (more or fewer) from
  [`allocations.json`](allocations.json), or a case is new, renamed or gone without that file saying so; or
- **a case is measurably slower** than on the base commit (the rule is below).

The job summary lists each regression on its own row, with the exact baseline lines an intended change needs.

| Piece | Where |
|---|---|
| Workflow | [`.github/workflows/bench-gate.yml`](../../../.github/workflows/bench-gate.yml): four `measure` shards, then `gate` |
| Measuring | [`bench/run-gate.sh`](../../run-gate.sh): builds both commits, runs them interleaved, reruns what looks slower |
| Comparing | [`tools/BenchGate`](../../../tools/BenchGate/README.md): the allocation and time rules, the summary, `record`, `shard` |
| Baseline | [`allocations.json`](allocations.json): full name to bytes per operation, recorded on the CI runner |

The Phase 1 and Phase 2 baselines (`bench/baselines/phase1/`, `bench/baselines/phase2/`) stay as the human-readable reports of
those phases, measured on a developer machine with the real-world corpora. The gate covers the same benchmark classes and every
one added since, without the corpora (CI does not fetch them), so `OpenAndWalkBenchmarks` runs over `tests/Corpus/` only and
`ContentInterpretationBenchmarks` over its `tests/Corpus/` fallbacks.

## When it runs

On a pull request whose diff touches `src/**`, `bench/**`, `tests/Broadside.TestSupport/**`, `tests/Corpus/**`,
`tools/BenchGate/**`, `Directory.Build.props`, `Directory.Packages.props`, `global.json` or the workflow, and on the "Run
workflow" button (`gh workflow run bench-gate.yml --ref <branch> -f base=<branch, tag or commit>`), which is how a branch that has
no pull request yet gets a run and a re-recorded baseline. A newer push cancels the older run.

Each of four `measure` jobs (`ubuntu-24.04`, 4 vCPU) builds both commits and runs its share of the cases: methods dealt to the
lightest shard by their case count in the baseline, and `OpenAndWalkBenchmarks.OpenAndWalk`, which has a case per corpus file,
split over all four through `BROADSIDE_BENCH_PARTITION` (`bench/Broadside.Benchmarks/BenchmarkPartition.cs`). The `gate` job
compares everything at once, so a case no shard ran is caught. A run takes about 25 minutes of wall-clock time.

## How it measures

BenchmarkDotNet, one process per case: 3 warm-up and 10 measured iterations of about 100 ms each, `[MemoryDiagnoser]` figures,
JSON export. Two rounds per shard, interleaved so that drift of the runner during the job weighs on both sides alike: round 1
runs the base commit then the pull request, round 2 the pull request then the base. Two settings make the figures reproducible;
both were found by running the gate against an unchanged commit (A/A) on the CI runner:

- **`DOTNET_TieredCompilation=0`.** With tiering, a process on the 4-vCPU runner sometimes kept running tier-0 code through its
  whole measurement: the same commit was up to 4.7x slower than itself, and allocated more (tier 0 does not stack-allocate). Full
  optimization from the first call removes that mode. The gate therefore measures code without dynamic PGO; it compares two
  commits under the same conditions, so that is fine for a regression signal, but its absolute times are not those of a default
  run.
- **`DOTNET_PROCESSOR_COUNT=1`.** A `ConcurrentDictionary` with several locks grows when one lock's share passes its budget, and
  which lock a key lands on depends on `HashCode`'s per-process random seed (`CosReference` hashes through `HashCode.Combine`),
  so opening the same file allocated a few KB more or less from one process to the next. With one lock, growth depends on the
  count alone. It also makes the figures independent of the core count. Code that parallelizes would run on one core under the
  gate; nothing benchmarked does today.

Allocations still depend on the platform (the system fonts, and a few bytes per document between Linux and macOS), so the
baseline is recorded on the CI runner only.

## The allocation rule

Every case the pull request ran must have an entry in `allocations.json` with exactly the bytes per operation it measured, in
every round. Fewer bytes fail too, so an improvement is locked in by the PR that makes it rather than silently given back by the
next one. A baseline entry that no shard ran fails (a removed or renamed benchmark or `[Params]` value, or a case that crashed).
When the base commit's own figure already differs from the baseline, the summary says the baseline is stale (a notice).

## The time rule

For each case, with B the base commit's measured iterations and P the pull request's, pooled over both rounds, the case fails
only when all of these hold:

1. median(P) / median(B) is at least **1.20**;
2. in each round on its own, the ratio of medians is at least **1.10** (a slowdown in one round only is the runner, not the code);
3. the first quartile of P is above the third quartile of B (the middle halves do not overlap);
4. median(P) - median(B) is at least 1 ns (below that a ratio measures code alignment and timer resolution);
5. a fresh base/pull-request pair of that case, run after both rounds, is again at least 1.20 slower.

A case at 1.10 or more that does not fail is listed as a notice, and so is one at 1/1.20 or less (faster).

### Why these thresholds

TODO: fill in from the ten A/A runs.

## Updating the baseline

The baseline changes only in the pull request that changes the allocation, so a reviewer sees each change as a diff line.

1. Let the gate run on the pull request (or start it with the "Run workflow" button on your branch, `base` = the branch you will
   merge into). It fails, and its summary has a "Baseline update" block with the lines to change.
2. If every change there is intended, either apply those lines to `bench/baselines/gate/allocations.json` or download the run's
   `bench-gate-baseline` artifact (`gh run download <run id> -n bench-gate-baseline`) and replace the file with it: it is the
   whole file re-recorded from that run, sorted, one line per case.
3. Say why in the pull request description (for example "the walker now reads the page labels: +312 B per document").
4. Commit; the next run passes.

Never record the file on a developer machine: the figures differ by platform (see above). `tools/BenchGate record` exists for
CI and for experiments.

New benchmarks, new `[Params]` values and new `tests/Corpus/` files (each is an `OpenAndWalk` case) follow the same steps.
A renamed case is a removal plus an addition; call the rename out in the pull request.

## An intended slowdown

Times are compared with the base commit, never with stored figures, so there is nothing to re-record: once the pull request
merges, the slower code is the next base. An intended slowdown (a correctness fix that costs time, a feature on a hot path) is
accepted by the owner merging despite the failing gate, with the gate's numbers and the reason in the pull request description.
From Phase 2 on the owner reviews every merge anyway (`CLAUDE.md`). The gate is not a required status check, so a merge is not
blocked mechanically.

## Running it locally

```sh
git worktree add ../broadside-base origin/main                     # the commit to compare against
bench/run-gate.sh --base ../broadside-base --head . --out artifacts/bench-gate          # every case: about 25 min per round
bench/run-gate.sh --base ../broadside-base --head . --out artifacts/bench-gate --methods methods.txt   # only some methods
dotnet run --project tools/BenchGate -- compare --baseline bench/baselines/gate/allocations.json --results artifacts/bench-gate
```

`methods.txt` holds `Namespace.Type.Method` lines (`dotnet run -c Release --project bench/Broadside.Benchmarks -- --list flat`
prints them all). On a developer machine the time rule is meaningful (both commits run on the same machine), but the allocation
figures are this machine's, not the CI runner's: expect allocation findings on cases that touch the file system or fonts, and
use them only to compare before and after your own change.

## Validation

TODO: the ten A/A runs, the lexer allocation branch and the 30 % slowdown branch.
