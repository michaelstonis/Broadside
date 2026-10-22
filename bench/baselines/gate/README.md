# Benchmark regression gate

The CI gate of spec story 52 (issue #139): every pull request that touches library, benchmark or corpus code measures every
benchmark case of `bench/Broadside.Benchmarks` and fails when

- **an allocation changes**: the bytes one call of a case allocates differ in any way (more or fewer) from
  [`allocations.json`](allocations.json), or a case is new, renamed or gone without that file saying so (every such pull
  request); or
- **a case is measurably slower** than on the base commit, built and run side by side on the same runner (the rule is below; on
  hot-path and `perf`-labelled pull requests, pull requests into `main` and nightly, see "When it runs").

The job summary lists each regression on its own row (and as an `::error` annotation), with the exact baseline lines an intended
allocation change needs.

| Piece | Where |
|---|---|
| Workflow | [`.github/workflows/bench-gate.yml`](../../../.github/workflows/bench-gate.yml): `plan`, four `allocations` shards, four `measure` shards when times are compared, then `gate`; [`bench-gate-perf.yml`](../../../.github/workflows/bench-gate-perf.yml) re-runs it when a pull request is labelled `perf` |
| Measuring | [`bench/run-gate.sh`](../../run-gate.sh): builds both commits, measures allocations, runs the timings interleaved, reruns what looks slower; `--measure allocations` or `times` does one part |
| Allocation figures | [`GateAllocations.cs`](../../Broadside.Benchmarks/GateAllocations.cs): `Broadside.Benchmarks --gate-allocations` |
| Comparing | [`tools/BenchGate`](../../../tools/BenchGate/README.md): the allocation and time rules, the summary, `record`, `shard` |
| Baseline | [`allocations.json`](allocations.json): full name to bytes per call, recorded on the CI runner |

The Phase 1 and Phase 2 baselines (`bench/baselines/phase1/`, `bench/baselines/phase2/`) stay as the human-readable reports of
those phases, measured with BenchmarkDotNet's default job on a developer machine with the real-world corpora. The gate covers the
same benchmark classes and every one added since, without the corpora (CI does not fetch them): `OpenAndWalkBenchmarks` runs
over `tests/Corpus/` only and `ContentInterpretationBenchmarks` over its `tests/Corpus/` fallbacks. Phase 3's render benchmarks
(D51) join automatically when they land: every class in the assembly is gated.

## When it runs

The gate has two parts (issue #389): allocations on every pull request it watches, times only where they matter. Comparing times
needs the base commit built and both commits run in interleaved rounds, about an hour of wall-clock time; checking allocations
needs one short run of the pull request, about 12 minutes.

**Allocations** are checked on every pull request whose diff touches `src/**`, `bench/**`, `tests/Broadside.TestSupport/**`,
`tests/Corpus/**`, `tools/BenchGate/**`, `Directory.Build.props`, `Directory.Packages.props`, `global.json` or the workflow, and
on every run listed below. Same baseline file, same exactness as ever.

**Times** are compared as well (the `measure` jobs run) when:

| Trigger | Base the times are compared with |
|---|---|
| The pull request's diff touches a hot path: `src/Broadside/` `IO`, `Parsing`, `Objects`, `Content`, `Filters`, `Images`, `Fonts`, `Graphics`, `Rendering` or `Text`; `src/Broadside.Rendering`, `src/Broadside.Rendering.Skia`, `src/Broadside.Fonts.Cmaps`, `src/Broadside.Fonts.Standard14`; `global.json`, `Directory.Build.props`, `Directory.Packages.props` (`HOT_PATHS` in the workflow) | the pull request's base |
| The pull request has the `perf` label | the pull request's base |
| The pull request is into `main` (the integration branch's merge) | `main` |
| Nightly, 02:17 UTC, on the integration branch (`NIGHTLY_BRANCH`, `spec/33-broadside-v1`) | the branch's tip when the previous nightly ran: each night measures what was merged that day. Nothing merged, no run. |
| The "Run workflow" button (`gh workflow run bench-gate.yml --ref <branch> -f base=<branch, tag or commit>`; `-f timing=false` for allocations only) | `base` |

The `plan` job decides, and its job summary (and the `gate` summary's last line) says whether times were compared and why.

**Requesting times on a pull request**: add the `perf` label. `bench-gate-perf.yml` then re-runs the pull request's gate (it
cancels a run in progress first), whose `plan` job reads the labels the pull request has at that moment; every later push keeps
comparing times while the label stays. Without the helper (say, the label was there before the pull request touched a watched
path), re-run the workflow from the Actions tab or push. Use it for a change that can slow a path outside `HOT_PATHS` (the
document model, caching, writing) or a change to the gate's own timing machinery (`bench/run-gate.sh`, `tools/BenchGate`).

**A nightly regression** (or a nightly whose shards fail) opens an issue titled "Nightly benchmark gate failed on
spec/33-broadside-v1" with the summary and the range of commits, or comments on that issue while it is open. Close it when the
regression is fixed or accepted. The schedule runs from `main`'s copy of the workflow (GitHub runs schedules from the default
branch only), so it starts once this workflow reaches `main`.

Each of four `allocations` jobs (`ubuntu-24.04`, 4 vCPU) builds the pull request and measures the allocation figures of its share
of the cases (`bench/run-gate.sh --measure allocations`): methods dealt to the lightest shard by their case count in the baseline,
and `OpenAndWalkBenchmarks.OpenAndWalk`, which has a case per corpus file, split over all four through `BROADSIDE_BENCH_PARTITION`
([`BenchmarkPartition.cs`](../../Broadside.Benchmarks/BenchmarkPartition.cs)). They take about 10 minutes, most of it the build.
When times are compared, four `measure` jobs, sharded the same way, build both commits and run the timing rounds and the rerun
(`--measure times`, which also measures the base commit's allocation figures for the stale-baseline notice); they take 45 to 70
minutes. The `gate` job compares everything at once, so a case no shard ran is caught. A newer push cancels the older run.

## How it measures

**Allocations** come from `Broadside.Benchmarks --gate-allocations`, not from BenchmarkDotNet's `Allocated` column. For each case
it does what BenchmarkDotNet would (parameters, `[GlobalSetup]`, `[GlobalCleanup]`), warms the method up, then records the fewest
bytes the current thread allocated in one call over ten calls during which no garbage collection ran (a 256 MB generation-0
budget makes those the rule). That is the method's steady-state cost per call, the same measurement as the unit tests'
`Allocations.Measure` (#48). BenchmarkDotNet's figure is not repeatable enough to compare exactly: it divides one iteration's
allocations, all threads included, by an operation count its pilot picks from timing, so a one-off cost per iteration (a cache
rebuilt after its forced collection) moved it by up to a few hundred bytes between runs of the same commit. The figure is
bytes per call of the benchmark method: for a method with `OperationsPerInvoke = n` it is n times BenchmarkDotNet's per-operation
figure.

**Times** come from BenchmarkDotNet, one process per case: 3 warm-up and 10 measured iterations of about 100 ms each, JSON export.
Two rounds per shard, interleaved so that drift of the runner during the job weighs on both sides alike: round 1 runs the base
commit then the pull request, round 2 the pull request then the base. A case the rounds find slower is rerun once for both
commits after the rounds.

Both run with two runtime settings, each found by running the gate against an unchanged commit on the CI runner:

- **`DOTNET_TieredCompilation=0`.** With tiering, a benchmark process on the 4-vCPU runner sometimes kept running tier-0 code
  through its whole measurement: the same commit measured up to 4.7x slower than itself, and allocated more (tier 0 does not
  stack-allocate). Fully optimized code from the first call removes that mode. The gate therefore measures code without dynamic
  PGO; it compares two commits under the same conditions, which is what a regression signal needs, but its absolute times are not
  those of a default run.
- **`DOTNET_PROCESSOR_COUNT=1`.** A `ConcurrentDictionary` with several locks grows when one lock's share passes its budget, and
  which lock a key lands on depends on `HashCode`'s per-process random seed (`CosReference` hashes through `HashCode.Combine`),
  so opening the same file allocated a few KB more or less from one process to the next. With one lock, growth depends on the
  count alone, and the figures no longer depend on the core count. Code that parallelizes runs on one core under the gate;
  nothing benchmarked does today.

Allocations still depend on the platform (the system fonts the font benchmarks find, and a few bytes per document between Linux
and macOS), so the baseline is recorded on the CI runner only.

## The allocation rule

Every case the pull request ran must have an entry in `allocations.json`, and its figure must equal it. Fewer bytes fail too, so
an improvement is locked in by the pull request that makes it rather than silently given back by the next one. A baseline entry
that no shard ran fails (a removed or renamed benchmark or `[Params]` value, or a case that crashed). When the base commit's own
figure already differs from the baseline, the summary says the baseline is stale (a notice, not a failure).

### No ranges

Every entry is one exact figure; the file refuses anything else. In the first validation runs two cases did vary between
processes: `StructureTreeBenchmarks.BuildPageMcidIndex` with 1,000 and 10,000 marked-content items (a 0.4 % spread), because the
object cache's `ConcurrentDictionary` grew its lock array by a hash with a per-process seed. #288 made every per-document
concurrent table a `DeterministicTable` (one fixed lock), and those cases now measure the same in every process. A case whose
figures differ between processes fails the gate ("varies between processes") whatever the baseline says: fix its determinism in
the library (a new per-document concurrent table must come from `DeterministicTable.Create`), do not widen the baseline.

## The time rule

For each case, with B the base commit's measured iterations and P the pull request's, pooled over both rounds, the case fails
only when all of these hold:

1. median(P) / median(B) is at least **1.20**;
2. in each round on its own, the ratio of the medians is at least **1.15** (a slowdown in one round only is the runner, not the
   code);
3. the first quartile of P is above the third quartile of B (the middle halves do not overlap);
4. median(P) - median(B) is at least 1 ns (below that a ratio measures code alignment and timer resolution);
5. the rerun of that case, after both rounds, again has a ratio of at least **1.15**.

A case at 1.10 or more that does not fail is listed as a notice ("slower, within tolerance"), and so is one at 1/1.20 or less
("faster"). The full table of ratios is folded at the end of the summary.

### Why these thresholds

Measured over ten runs of the gate against an unchanged commit on the CI runner (`noise-01` to `noise-10` below), plus one more
(`aa-4`): 3,795 case-runs.

- Run-to-run noise in one round: the ratio of medians is 1.000 at the median, 0.886 to 1.136 for 98 % of case-rounds, and has a
  heavy tail: 34 case-runs had a round at 1.20 or more, the largest 8.7x (`OpenAndWalk(annotations-subtypes.pdf)`, round 2 of `noise-06`, while
  its round 1 read 0.85). Those spikes come from a noisy stretch of the shared runner and hit one round, never both: no case
  reached 1.20 in both rounds, 2 reached 1.15 in both (pooled 1.18 each), and with the pooled 1.20 condition none was left.
  A one-round rule would have failed most runs; a pooled-only rule at 1.20 would have flagged 13 case-runs.
- An earlier version of the rule (pooled 1.20, each round 1.10, rerun 1.20) failed an unchanged case in the first 30 %
  slowdown run: `OpenAndWalk(dct-cmyk.pdf)`, rounds 1.12 and 2.17, rerun 1.32. Requiring 1.15 in every round rejects that pattern while a real
  30 % slowdown still clears it: its rounds read 1.32 to 1.41.
- A true 30 % slowdown shows about 1.30 in each round. It escapes only if noise lowers a round or the rerun below 1.15 (noise
  below 0.885, about 1 % of case-rounds) or the pooled ratio below 1.20, so it is caught in about 96 % of runs; 40 % or more is
  caught essentially always. Under 15 % is not the gate's business: it is within what the runner itself moves.
- The time a run takes was the other constraint: two rounds plus a targeted rerun kept a run near 25 minutes on four shards
  when this rule was set. More rounds would tighten the estimate, at about 8 minutes each. With Phase 3's benchmarks a shard
  had grown to 45 to 70 minutes by October 2026, which is why times now run only where they matter (#389).

## Updating the baseline

The baseline changes only in the pull request that changes the allocation, so a reviewer sees each change as a diff line.

1. Let the gate run on the pull request (or start it with the "Run workflow" button on your branch, `base` = the branch you will
   merge into). It fails, and its summary has a "Baseline update" block with the lines to change.
2. If every change there is intended, either apply those lines to `bench/baselines/gate/allocations.json` or download the run's
   `bench-gate-baseline` artifact (`gh run download <run id> -n bench-gate-baseline`) and replace the file with it: it is the
   whole file re-recorded from that run, sorted, one line per case.
3. Say why in the pull request description (for example "the walker now reads the page labels: +312 B per document").
4. Commit; the next run passes.

Never record the file on a developer machine: the figures differ by platform (see above). New benchmarks, new `[Params]`
values and new `tests/Corpus/` files (each is an `OpenAndWalk` case) follow the same steps. A renamed case is a removal plus an
addition; call the rename out in the pull request. A new benchmark whose `[ParamsSource]` grows with the corpus passes its values
through `BenchmarkPartition.Select` and joins the workflow's `SPLIT` list.

## An intended slowdown

Times are compared with the base commit, never with stored figures, so there is nothing to re-record: once the pull request
merges, the slower code is the next base. An intended slowdown (a correctness fix that costs time, a feature on a hot path) is
accepted by the owner merging despite the failing gate, with the gate's numbers and the reason in the pull request description.
From Phase 2 on the owner reviews every merge anyway (`CLAUDE.md`). The gate is not a required status check, so it does not block
a merge mechanically. A slowdown that reached the integration branch without a timing run (a pull request outside `HOT_PATHS`
without the `perf` label) is caught by the next nightly, which opens an issue; accept it by closing that issue with the reason.

## Running it locally

```sh
git worktree add ../broadside-base origin/main                     # the commit to compare against
bench/run-gate.sh --base ../broadside-base --head . --out artifacts/bench-gate --methods methods.txt
dotnet run --project tools/BenchGate -- compare --baseline bench/baselines/gate/allocations.json \
  --results artifacts/bench-gate --subset true
```

`methods.txt` holds `Namespace.Type.Method` lines (`dotnet run -c Release --project bench/Broadside.Benchmarks -- --list flat`
prints them all); without `--methods` every case runs (about 25 minutes per round on an idle machine). `--measure allocations`
measures only the pull request's allocation figures, as every pull request's CI does (no `--base` needed). `--subset true` stops
the comparison from reporting the baseline cases you did not run. On a developer machine the time rule is meaningful (both
commits run on the same machine), but the allocation figures are this machine's: expect allocation findings on the cases that
read system fonts and a few bytes per document on macOS, and use them only to compare before and after your own change.

## Validation

Runs of the workflow on throwaway branches with a temporary push trigger (the "Run workflow" button needs the workflow on `main`);
every run compares a commit with its parent, and the branches were deleted afterwards. The verdicts are those of the final rule
and baseline, recomputed from each run's reports.

| Run | What changed against the base | Verdict |
|---|---|---|
| [`noise-01`](https://github.com/michaelstonis/Broadside/actions/runs/38239240151) to [`noise-10`](https://github.com/michaelstonis/Broadside/actions/runs/38239242135), [`aa-4`](https://github.com/michaelstonis/Broadside/actions/runs/38236740285) | nothing (the workflow file only) | all passed; 1 to 9 "within tolerance" notices each, no allocation finding |
| [`lexer-alloc`](https://github.com/michaelstonis/Broadside/actions/runs/38239222457) | `CosLexer.Next` allocates a 1-byte array per token | failed: 218 allocation changes (`LexerBenchmarks.Tokenize(empty-page.pdf)`: 2,464 B per call, baseline 0 B; every case that lexes), and 4 cases slower |
| [`slow-30b`](https://github.com/michaelstonis/Broadside/actions/runs/38242385985) | `LexerBenchmarks.Tokenize` spins for 30 % of its own time | failed: exactly the two `Tokenize` cases, 1.35x (rounds 1.34, 1.41; rerun 1.34) and 1.32x (rounds 1.32, 1.32; rerun 1.30); nothing else |

After merging the integration branch (which had added 56 cases: display list, rasterizer, text extraction, encoders and more,
and changed others, e.g. +32 B per document open), the baseline was re-recorded from
[`record-1`](https://github.com/michaelstonis/Broadside/actions/runs/38245757517): 401 cases, base and pull-request figures equal
for all but the two ranged ones, no slower case.

**Current baseline**: recorded by [run 38951269081](https://github.com/michaelstonis/Broadside/actions/runs/38951269081) (pull
request #331, the gate merged with the Phase 3 integration branch at `c123b691`), from its `bench-gate-baseline` artifact,
unedited: 560 cases, every one exact. Against `record-1` it adds 159 cases (Phase 3's rendering, text, Skia, hit-test,
real-world render and `OpenAndWalk` cases of new corpus files) and changes 217, reviewed by class: about +1.6 KB per document
open in every open path (`DocumentOpen`, `LazyLoading`, `Repair`, `OpenAndWalk`, `ContentInterpretation`, save; the Phase 3
document model, including #288's fixed-capacity tables), +8 B per CFF parse, `CorpusBenchmarks.ReadAllCorpusFiles` +522 KB
(more corpus files), `DisplayListBenchmarks.Build(path-heavy-256kb)` +163 KB (display-list regions and extents),
`BuildPageMcidIndex` now exact (71,752 / 711,984 / 6,946,480 B), and `TextExtractionBenchmarks.ExtractText` -6.4 MB (Phase 3
text extraction rework). That run's timings were not compared (BenchmarkDotNet's build hit its 120 s limit, fixed since with
`--buildTimeout`); the run that validated this baseline in full is linked from #331.

The first runs (`aa-1` to `aa-3`, before the runtime settings and the allocation mode above) are where the 4.7x tier-0 mode,
the per-process allocation spread and the BenchmarkDotNet allocation drift were found.
