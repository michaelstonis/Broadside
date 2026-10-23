#!/usr/bin/env bash
# Runs the benchmark regression gate's measurements (issue #139; bench/baselines/gate/README.md): the allocation figures of the
# pull request (and of the base commit, to spot a stale baseline), then the benchmarks of both commits interleaved on this
# machine, then a rerun of whatever the rounds found slower. The verdict is
# `dotnet run --project tools/BenchGate -- compare --baseline bench/baselines/gate/allocations.json --results <out>` afterwards.
#
#   bench/run-gate.sh --head <checkout> --out <dir> [--base <checkout>] [--methods <file>] [--rounds <n>] [--measure <what>]
#
#   --head     checkout of the pull request; built here; its tools/BenchGate is the one used.
#   --base     checkout of the commit to compare against (a git worktree); built here. Without it only allocations are measured.
#   --out      results directory: <out>/allocations/{base,head}/, <out>/round-<n>/{base,head}/ and <out>/confirm/{base,head}/.
#   --methods  file of Namespace.Type.Method lines to run (BenchGate shard output); default every benchmark.
#   --rounds   interleaved rounds, default 2. Odd rounds run base first, even rounds head first, so drift of the machine
#              during the run weighs on both sides alike.
#   --measure  all (default): everything above. allocations: the pull request's allocation figures only, no base build and no
#              timing (CI's `allocations` jobs, on every pull request). times: the timing rounds and the rerun, plus the base
#              commit's allocation figures for the stale-baseline notice, but not the pull request's (CI's `measure` jobs, which
#              run beside the `allocations` jobs only when times are compared; bench/baselines/gate/README.md, "When it runs").
#
# Exit code: 0 when the pull request's benchmarks all ran (the base side may fail, e.g. a benchmark it does not have yet),
# 1 otherwise.
set -uo pipefail

base=""
head=""
out=""
methods=""
rounds=2
measure=all
while [ $# -gt 0 ]; do
  case "$1" in
    --base) base="$2"; shift 2 ;;
    --head) head="$2"; shift 2 ;;
    --out) out="$2"; shift 2 ;;
    --methods) methods="$2"; shift 2 ;;
    --rounds) rounds="$2"; shift 2 ;;
    --measure) measure="$2"; shift 2 ;;
    *) echo "unknown option $1" >&2; exit 2 ;;
  esac
done
if [ -z "${head}" ] || [ -z "${out}" ]; then
  echo "usage: $0 --head <checkout> --out <dir> [--base <checkout>] [--methods <file>] [--rounds <n>] [--measure all|allocations|times]" >&2
  exit 2
fi
case "${measure}" in
  all|times) ;;
  allocations) base="" ;; # the pull request's figures against the stored baseline: the base commit is not needed
  *) echo "--measure takes all, allocations or times, not '${measure}'" >&2; exit 2 ;;
esac

head="$(cd "${head}" && pwd)"
mkdir -p "${out}"
out="$(cd "${out}" && pwd)"
if [ -n "${base}" ]; then
  base="$(cd "${base}" && pwd)"
fi

# Both measurements run without tiered compilation: with it, a process on a busy 4-core runner sometimes ran tier-0 code through
# its whole measurement (the same commit was up to 4.7x slower than itself, and allocated more, because tier 0 does not
# stack-allocate). DOTNET_PROCESSOR_COUNT=1 gives every ConcurrentDictionary a single lock: with several, a dictionary grows when
# one lock's share passes its budget, and which lock a key takes depends on HashCode's per-process random seed, so the same walk
# allocated a few KB more or less from one process to the next. bench/baselines/gate/README.md has the measurements.
runtime_env=(DOTNET_TieredCompilation=0 DOTNET_PROCESSOR_COUNT=1)

# The timing job: one process per case, 3 warm-up and 10 measured iterations of about 100 ms. Plain BenchmarkDotNet options
# (not a --job name) so a base commit older than the gate runs the same job. BenchmarkDotNet builds its generated project, and
# with it the whole project graph without the compiler server, once per invocation: since Phase 3 (rendering, fonts, Skia) that
# takes more than its default 120 s limit on the 4-vCPU runner, so the limit is 15 minutes.
bench_args=(--launchCount 1 --warmupCount 3 --iterationCount 10 --iterationTime 100 --exporters json --buildTimeout 900
  --envVars DOTNET_TieredCompilation:0 DOTNET_PROCESSOR_COUNT:1)

filters=('*')
if [ -n "${methods}" ]; then
  filters=()
  while IFS= read -r line; do
    [ -n "${line// /}" ] && filters+=("${line}")
  done < "${methods}"
  if [ "${#filters[@]}" -eq 0 ]; then
    echo "No benchmark methods to run."
    exit 0
  fi
fi

echo "::group::Build the pull request's benchmarks and gate tool"
dotnet build "${head}/bench/Broadside.Benchmarks" -c Release -warnaserror || exit 1
dotnet build "${head}/tools/BenchGate" -c Release -warnaserror || exit 1
echo "::endgroup::"

base_built=false
if [ -n "${base}" ]; then
  echo "::group::Build the base commit's benchmarks"
  if dotnet build "${base}/bench/Broadside.Benchmarks" -c Release; then
    base_built=true
  else
    echo "::warning title=Benchmark gate::The base commit's benchmarks do not build; times are not compared."
  fi
  echo "::endgroup::"
fi

status=0

# allocations <checkout> <side>: Broadside.Benchmarks --gate-allocations (bench/Broadside.Benchmarks/GateAllocations.cs), with a
# 256 MB generation-0 budget so that most measured calls see no collection.
allocations() {
  local checkout="$1" side="$2"
  echo "::group::allocations/${side}"
  (cd "${checkout}" && env "${runtime_env[@]}" DOTNET_GCgen0size=0x10000000 \
    dotnet artifacts/bin/Broadside.Benchmarks/release/Broadside.Benchmarks.dll \
    --gate-allocations "${out}/allocations/${side}/allocations.json" --filter "${filters[@]}")
  local code=$?
  echo "::endgroup::"
  if [ "${code}" -ne 0 ]; then
    if [ "${side}" = "head" ]; then
      echo "::error title=Benchmark gate::The pull request's allocation measurement failed (exit ${code}); see the log."
      status=1
    else
      echo "::notice title=Benchmark gate::The base commit's allocation measurement failed (exit ${code}); it may predate it. The stale-baseline check is skipped."
    fi
  fi
}

# run <checkout> <side> <folder> <filter...>
run() {
  local checkout="$1" side="$2" folder="$3"
  shift 3
  echo "::group::${folder}/${side}"
  (cd "${checkout}" && dotnet run -c Release --project bench/Broadside.Benchmarks --no-build -- \
    --filter "$@" "${bench_args[@]}" --artifacts "${out}/${folder}/${side}")
  local code=$?
  echo "::endgroup::"
  if [ "${code}" -ne 0 ]; then
    if [ "${side}" = "head" ]; then
      echo "::error title=Benchmark gate::A pull-request benchmark failed in ${folder} (exit ${code}); see the log."
      status=1
    else
      echo "::warning title=Benchmark gate::A base-commit benchmark failed in ${folder} (exit ${code}); it is not compared in time."
    fi
  fi
}

if [ "${measure}" != times ]; then
  allocations "${head}" head
fi
if [ "${base_built}" = true ]; then
  allocations "${base}" base
fi
if [ "${measure}" = allocations ]; then
  exit "${status}"
fi

for round in $(seq 1 "${rounds}"); do
  if [ "${base_built}" = true ] && [ $((round % 2)) -eq 1 ]; then
    run "${base}" base "round-${round}" "${filters[@]}"
  fi
  run "${head}" head "round-${round}" "${filters[@]}"
  if [ "${base_built}" = true ] && [ $((round % 2)) -eq 0 ]; then
    run "${base}" base "round-${round}" "${filters[@]}"
  fi
done

if [ "${base_built}" = true ]; then
  suspects="${out}/suspects.txt"
  dotnet run -c Release --project "${head}/tools/BenchGate" --no-build -- suspects --results "${out}" --out "${suspects}" || exit 1
  if [ -s "${suspects}" ]; then
    again=()
    while IFS= read -r line; do
      [ -n "${line}" ] && again+=("${line}")
    done < "${suspects}"
    run "${base}" base confirm "${again[@]}"
    run "${head}" head confirm "${again[@]}"
  fi
fi

exit "${status}"
