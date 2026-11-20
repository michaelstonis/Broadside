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
| Workflow | [`.github/workflows/bench-gate.yml`](../../../.github/workflows/bench-gate.yml): `plan`, four `allocations` shards, eight `measure` shards and six `measure-avx512-off` shards when times are compared, then `gate`; [`bench-gate-perf.yml`](../../../.github/workflows/bench-gate-perf.yml) re-runs it when a pull request is labelled `perf` |
| Measuring | [`bench/run-gate.sh`](../../run-gate.sh): records the runner's processor, builds both commits, measures allocations, runs the timings interleaved, reruns what looks slower; `--measure allocations` or `times` does one part, `--avx512 off` times with AVX-512 disabled |
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
needs the base commit built and both commits run in interleaved rounds, about half an hour of wall-clock time; checking allocations
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
comparing times while the label stays. It works for a fork's pull request too (`pull_request_target`, metadata only; see the
workflow's header). Without the helper (say, the label was there before the pull request touched a watched
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
When times are compared, eight `measure` jobs (`MEASURE_SHARDS`), sharded the same way, build both commits and run the timing
rounds and the rerun (`--measure times`, which also measures the base commit's allocation figures for the stale-baseline notice).
Until #337 four of them took 45 to 70 minutes, a third of it BenchmarkDotNet rebuilding the project graph on each of its six
invocations; in process and over eight shards each should take about half an hour. Beside them, six `measure-avx512-off` jobs
time every case once more with AVX-512 disabled (see "AVX-512" below). The `gate` job compares everything at once, so a case no
shard ran is caught. A newer push cancels the older run.

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

**Times** come from BenchmarkDotNet, in process (`--inProcess`, the build `run-gate.sh` made) and one process per benchmark class:
3 warm-up and 10 measured iterations of about 100 ms each, JSON export. Until #337 every case ran out of process, which made
BenchmarkDotNet rebuild the whole project graph into its generated project on every invocation (six minutes on the runner) and
measured nothing that a process per class does not: the base and the pull request still run each class in a fresh process, in
the same order, under the same runtime settings.
Two rounds per shard, interleaved so that drift of the runner during the job weighs on both sides alike: round 1 runs the base
commit then the pull request, round 2 the pull request then the base. A case the rounds find slower is rerun once for both
commits after the rounds, in three processes per commit (`--processes`, alternating which commit goes first), and every timing
process draws a random placement of its heap data and compiled code (see "Identical code measured slower" below).

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

## AVX-512

Issue #539. #335 made Type 3 rendering 1.4x to 1.8x slower, and the gate passed it on most runs: `ResolvePieces` kept eight
`double` locals live, more than the 16 vector registers of a processor without AVX-512 hold, so they spilled to the stack. With
AVX-512 the JIT has 32 vector registers and nothing spilled. GitHub's `ubuntu-24.04` runners are a mix: the AMD EPYC 7763 has no
AVX-512; the EPYC 9V74 and 9V45 (Zen 4) and the Xeon Platinum 6975P-C, 8573C and 8370C have it, though one hypervisor was seen
hiding it. Whether the gate saw a register-pressure regression was luck of the draw.

Two things make it deterministic:

- **Every job records its runner.** `bench/run-gate.sh` first runs `BenchGate machine`, under the same runtime settings as the
  benchmarks, and writes `<out>/machine.json`: the processor's model name (`model name` in `/proc/cpuinfo`), whether the
  processor reports AVX-512 Foundation (`avx512f` in its flags; a hypervisor that hides it hides it from .NET too), whether .NET
  used AVX-512 in the measuring processes (`Avx512F.IsSupported`) and whether the gate disabled it. The line goes into each job's
  summary, the file into each job's artifact, and the `gate` summary has a folded "Runners" table of every job. A time finding
  names the processor its case was measured on and the AVX-512 state, e.g. "on INTEL XEON PLATINUM 8573C, AVX-512 on".
- **Every case is also timed without AVX-512.** When times are compared, six `measure-avx512-off` jobs run
  `bench/run-gate.sh --avx512 off`, which sets `DOTNET_EnableAVX512F=0` and `DOTNET_EnableAVX512=0` on every measuring process
  of both commits. On a processor with AVX-512 that gives the JIT the 16 registers of one without it (setting these on an
  AVX-512 runner reproduces #335's spill); on a processor without it, it is that processor's ordinary code. Either way the pass
  measures what a processor without AVX-512 runs, whatever runner it got. If the runtime ignores the setting (`BenchGate
  machine` sees `Avx512F.IsSupported` still true), the job fails rather than report a pass that measured nothing new.

The pass is compared on its own (its rounds and reruns never pool with the default pass's) by the same time rule, and its
findings say so: "slower with AVX-512 disabled" in the summary, `Benchmark regression with AVX-512 disabled` as the annotation
title. A case slower only there is a register-pressure regression (or another one that only a processor without AVX-512 shows);
it fails the gate like any other. It measures times only: allocations do not depend on the instruction set, and the
`allocations` jobs have them.

**Its budget: one round instead of two, rerun twice, over six shards.** In run
[43943204488](https://github.com/michaelstonis/Broadside/actions/runs/43943204488) the eight `measure` jobs took 21 to 34
minutes: about 5 minutes building the pull request, 4 building the base commit, 4 measuring the base commit's allocations and
about 10 minutes per round (both commits) over an eighth of the cases. A second job per shard doing the same two rounds would
have doubled the runner time for a second opinion on the same code. The AVX-512-off jobs skip the allocations and run one round
over a sixth of the cases, about 13 minutes, so with the builds each takes about 23 to 25 minutes and they finish within the
`measure` jobs, and the gate's wall-clock time should not grow (the eighteen timing jobs and four allocation jobs fit GitHub's
20 concurrent jobs for a public repository, though another workflow of the same push can queue a few). With one round,
condition 2 of the time rule is the round itself; what stops one noisy stretch of the runner from failing a case is the rerun,
so a slower case is rerun twice, base first and then pull request first, and both reruns must reach 1.15. In the noise runs
(below) 34 of about 7,600 case-rounds (0.45 %) reached 1.20, and 98 % of case-rounds read between 0.886 and 1.136, so about 1 %
reach 1.15; a noisy round followed by two noisy reruns is then about as rare as the default pass's two noisy rounds and one
noisy rerun. A real register spill like #335's (1.4x to 1.8x) clears every condition.

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

### Cases whose allocations legitimately vary

A benchmark may opt out of the exact allocation comparison with `[BenchmarkCategory(GateAllocations.NoAllocationGate)]`
(the string `"no-allocation-gate"`). It stays a normal BenchmarkDotNet benchmark and **its time is still compared** against the base
commit by the time rule below. What changes: `--gate-allocations` does not measure it (it lists the name under `NotGated` in its
output file), the gate neither wants a baseline entry for it nor reports it as missing, and `allocations.json` has no key for it.

Allowed only when the figure cannot be made exact, and the reason goes in the benchmark's XML doc:

- **Parallel work**: the allocations of one call depend on how the thread pool schedules it (per-thread caches, how many workers
  start), so they differ by megabytes between runs. The gate measures the calling thread only, which is not the cost either.
- **Multi-gigabyte, collection-sensitive cases**: a call that allocates hundreds of megabytes or more cannot finish without garbage
  collections, and a collection inside the call clears weakly held caches (`ConditionalWeakTable`) halfway through it, so the figure
  moves by bytes to a megabyte with where the collection falls. The gate's "no collection during the call" filter cannot find a
  clean call for these, even with the 256 MB generation-0 budget.

Not allowed: a case that is small and merely not yet deterministic (fix the library, see "No ranges"), or one that is simply
inconvenient to re-record. A deterministic case, however large, stays gated. Keep the small deterministic twin of an opted-out case
in the gate (for the document builder, `Hello` and `HelloFlow`) so the allocation behavior it shares is still locked.

## The time rule

For each case, with B the base commit's measured iterations and P the pull request's, pooled over both rounds, the case fails
only when all of these hold:

1. median(P) / median(B) is at least **1.20**;
2. in each round on its own, the ratio of the medians is at least **1.15** (a slowdown in one round only is the runner, not the
   code);
3. the first quartile of P is above the third quartile of B (the middle halves do not overlap);
4. median(P) - median(B) is at least 1 ns (below that a ratio measures code alignment and timer resolution);
5. the rerun of that case, after both rounds, again has a ratio of at least **1.15** (in the AVX-512-off pass, which runs one
   round, both of its reruns must). The rerun measures each commit in three processes, and its ratio is the pull request's
   fastest process over the base's slowest (each process's median iteration): every pull-request process must be 1.15x slower
   than every base process.

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
  had grown to 45 to 70 minutes by October 2026, which is why times now run only where they matter (#389), and why they now
  run in process over eight shards (#337).

## Identical code measured slower

Issue #554. The gate reported `PixelPackingBenchmarks.GrayToRgb` 1.34x slower (both rounds and the rerun) on pull requests that
did not touch it, `ColorConversionBenchmarks.ConvertBytes(Rgb)` 1.24x, `SecurityBenchmarks.ManagedAesCbcDecrypt` 1.21x and
`DisplayListBenchmarks.Replay` about 1.4x with AVX-512 disabled, and #544's `Replay` read 0.70x and 1.39x with byte-identical
machine code (#574).

**The cause: where a process's heap data and compiled code land.** Some kernels run at two or three distinct speeds depending
on the placement of their data relative to their compiled code: `GrayToRgb` at about 103 or 138 ns on the Xeon 6975P-C (1.1x
apart on AMD), `Replay(path-heavy-256kb)` with AVX-512 off at about 283 or 400 us on Intel, `ConvertBytes(Rgb)` at three speeds
on the Xeon 6975P-C and 8370C. On the exact pair of commits PR #575's gate failed on (a tooling-only change; run
[44204853687](https://github.com/michaelstonis/Broadside/actions/runs/44204853687)), with the same change applied to both builds:

| Change at startup, both commits | `GrayToRgb` rounds, Xeon 6975P-C |
|---|---|
| None | 0.76, 0.75, 0.77 |
| An 8-byte array allocated first | 0.76, 0.76, 0.79 |
| A 16-, 32- or 64-byte array | 1.30, 1.29, 1.32 |
| A 512-byte or 4 KB array | 0.76, 0.76, 0.76 |
| 1 to 8 small methods compiled first | 0.75 |
| 16 to 128 methods compiled first | 1.35 to 1.37 |

The placement is fixed by everything a build compiles and allocates before the benchmark (string lengths of paths included: a
rerun folder named `<class>-p2` instead of `<class>` flipped it too), so two builds of identical code can differ by 1.3x in
every round and every rerun, in either direction. That is what the earlier A/A runs missed: with the same commit on both sides
the placement differed only through the checkout paths, and they showed the speeds as noise between processes (`GrayToRgb`
rounds from 0.73 to 1.37 on the Xeon 6975P-C whether the two checkouts had paths of equal length or not, with base and pull
request in either role, with `DOTNET_JitAlignLoops=0`; run
[44161704319](https://github.com/michaelstonis/Broadside/actions/runs/44161704319)). `DOTNET_JitAlignLoops=0` turns #544's
`Replay` around (1.00x with AVX-512 off, 0.72x with it on); `DOTNET_JitAlignLoopAdaptive=0`, `DOTNET_JitAlignLoopBoundary=64` and
`DOTNET_EnableWriteXorExecute=0` change nothing (run [44186578218](https://github.com/michaelstonis/Broadside/actions/runs/44186578218)).
No runtime setting makes it neutral.

**What the gate does about it**, two parts:

1. **Every timing process draws its placement at random.** `run-gate.sh` sets `BROADSIDE_BENCH_LAYOUT=random` on the measuring
   processes, and the benchmark host ([`LayoutJitter.cs`](../../Broadside.Benchmarks/LayoutJitter.cs)) then allocates a live
   array of 0 to 4 KB and compiles 0 to 63 small methods before BenchmarkDotNet starts. A layout difference between two builds
   becomes a difference between processes, which both commits share. `BROADSIDE_BENCH_LAYOUT=heap:<n>,code:<n>` reproduces one
   placement; set it empty to turn the jitter off.
2. **The rerun measures each commit in three processes** (`--processes`, default 3, alternating which commit goes first) and
   compares the pull request's **fastest** process with the base's **slowest**, each by its median iteration. A regression slows
   every process; a slow draw does not.

The rounds stay one process per commit, so one of them may still read 1.3x; the rerun is what decides. Run
[44209934033](https://github.com/michaelstonis/Broadside/actions/runs/44209934033) ran the whole gate (both AVX-512 passes,
every case's rerun forced) on PR #575's pair and on #544's pair, eight jobs (Xeon 6975P-C and EPYC 7763), with and without the
jitter:

| | Reruns | Reruns at 1.15 or more: one process per commit | Pull request's fastest over base's slowest of three |
|---|---|---|---|
| Fixed placement | 528 | 26 | 20 (the split is the same in every process) |
| Jitter (the gate) | 528 | 18 | **0** |

The A/A runs of run [44177420713](https://github.com/michaelstonis/Broadside/actions/runs/44177420713) (648 case reruns on the
Xeon 6975P-C and Platinum 8370C and the EPYC 7763, 9V45 and 9V74) agree: one process per commit reached 1.15 fourteen times
(`ManagedAesCbcDecrypt` 1.33 on the EPYC 7763, `ConvertBytes(Rgb)` 1.29), the fastest-over-slowest rule never (largest 1.04).

The cost: in a case whose speed varies by more than 1.15 between placements, a slowdown smaller than that spread can hide behind
it. Scaling the pull request's processes of those A/A reruns by 1.3, every rerun of the cases without such speeds still
confirms, while `ConvertBytes(Rgb)` misses 23 of 54, `GrayToRgb` 13 of 54, `ManagedAesCbcDecrypt` 5 and `PackOpaque` 3; at 1.4
it misses 23 of 648 in all. A regression smaller than a case's own placement spread was never a reliable signal there. The
rerun takes three times as long as before, but only for the few cases the rounds flagged.

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

## Size probes

Issue #361 gates the shipped size of the core the same way: [`sizes.json`](sizes.json) holds the size, in bytes, of each Native AOT
size probe's executable, by runtime identifier. A probe is a tiny app in [`bench/SizeProbes`](../../SizeProbes) that calls only the
public API one scenario needs, so the trimmer keeps only what that scenario reaches:

| Probe | Does |
|---|---|
| `empty` | Nothing: the runtime's floor, which every other probe includes |
| `open-metadata` | Opens a file and reads its page count and document information |
| `open-metadata-no-image-codecs` | The same, with options from `PdfOptions.CreateWithoutImageCodecs()` (issue #544): no JPX, JBIG2, DCT or CCITT decoder |
| `extract-text` | Opens a file and extracts the text of every page |
| `render` | Opens a file and renders its first page with the software rasterizer |
| `fill-form` | Opens a form, sets a text field (regenerating its appearance) and saves |
| `save` | Opens a file, sets its title and saves |
| `sign` | Opens a file and signs it with a certificate |

[`bench/size-probes.sh`](../../size-probes.sh) publishes each probe for the current runtime (`dotnet publish -c Release
--use-current-runtime`; trimmer and AOT warnings are errors), runs it over a corpus file (a probe exits non-zero when its result is
wrong, so a trim that removed something the scenario needs fails too) and writes the sizes; `BenchGate sizes` compares them with the
baseline. A probe fails when it grows by more than 1% (`--tolerance`), when it is not in the baseline, or when a baselined probe of
the measured runtime was not measured. A probe that shrinks by more than 1% passes, with a request to re-record so the saving is
kept. The tolerance absorbs what a linker or runtime patch moves; a real feature costs far more.

The CI job `size-probes` (`ci.yml`, every pull request that affects the core) runs on macOS, Linux and Windows, and each leg
compares the probes of its own runtime identifier (`osx-arm64`, `linux-x64`, `win-x64`; issue #272): Native AOT cannot
cross-compile between operating systems, so each runtime's sizes are recorded on that runtime. A developer Mac measures exactly
what the macOS leg does. To record an intended change, run the probes and record (or download the leg's `size-probes-<os>` artifact
and record from it; recording merges the runtime identifiers it measured into the file), and say in the pull request what the bytes
bought:

```sh
bench/size-probes.sh --out artifacts/size-probes/sizes.json
dotnet run --project tools/BenchGate -- sizes --baseline bench/baselines/gate/sizes.json \
  --results artifacts/size-probes/sizes.json --record bench/baselines/gate/sizes.json
```

What the probes measured, where the bytes go and what an app can switch off is in
[`docs/benchmarks/core-size.md`](../../../docs/benchmarks/core-size.md).

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
