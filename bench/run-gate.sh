#!/usr/bin/env bash
# Runs the benchmark regression gate's measurements (issue #139; bench/baselines/gate/README.md): the benchmarks of a base
# checkout and of a pull-request checkout, interleaved on this machine, then a rerun of whatever the rounds found slower.
# The verdict is `dotnet run --project tools/BenchGate -- compare --results <out> ...` afterwards.
#
#   bench/run-gate.sh --base <checkout> --head <checkout> --out <dir> [--methods <file>] [--rounds <n>]
#
#   --base     checkout of the commit to compare against (a git worktree); built here. Optional: without it only allocations are gated.
#   --head     checkout of the pull request; built here; its tools/BenchGate is the one used.
#   --out      results directory: <out>/round-<n>/{base,head}/ and <out>/confirm/{base,head}/ (BenchmarkDotNet --artifacts folders).
#   --methods  file of Namespace.Type.Method lines to run (BenchGate shard output); default every benchmark.
#   --rounds   interleaved rounds, default 2. Odd rounds run base first, even rounds head first, so drift of the machine
#              during the run weighs on both sides alike.
#
# Exit code: 0 when the pull request's benchmarks all ran (the base side may fail, e.g. a benchmark it does not have yet),
# 1 otherwise.
set -uo pipefail

base=""
head=""
out=""
methods=""
rounds=2
while [ $# -gt 0 ]; do
  case "$1" in
    --base) base="$2"; shift 2 ;;
    --head) head="$2"; shift 2 ;;
    --out) out="$2"; shift 2 ;;
    --methods) methods="$2"; shift 2 ;;
    --rounds) rounds="$2"; shift 2 ;;
    *) echo "unknown option $1" >&2; exit 2 ;;
  esac
done
if [ -z "${head}" ] || [ -z "${out}" ]; then
  echo "usage: $0 --base <checkout> --head <checkout> --out <dir> [--methods <file>] [--rounds <n>]" >&2
  exit 2
fi

head="$(cd "${head}" && pwd)"
mkdir -p "${out}"
out="$(cd "${out}" && pwd)"
if [ -n "${base}" ]; then
  base="$(cd "${base}" && pwd)"
fi

# The job: one process per case, 3 warm-up and 10 measured iterations of about 100 ms, allocations measured. Passed as plain
# BenchmarkDotNet options (not a --job name) so a base commit older than the gate runs the same job.
# The benchmark processes run without tiered compilation: with it, a process on a busy 4-core runner sometimes measures tier-0
# code for its whole run (4x slower, and more bytes allocated, because tier 0 does not stack-allocate), which made the same
# commit differ from itself by up to 4.7x in time and by up to 10 % in allocations. Fully optimized code from the first call
# removes that mode. DOTNET_PROCESSOR_COUNT=1 gives every ConcurrentDictionary a single lock: with several, a dictionary grows
# when one lock's share passes its budget, and which lock a key takes depends on HashCode's per-process random seed, so the
# same walk allocated a few KB more or less from one process to the next. One lock makes growth depend on the count alone, and
# the figures no longer depend on the machine's core count either.
bench_args=(--launchCount 1 --warmupCount 3 --iterationCount 10 --iterationTime 100 --memory --exporters json
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
    echo "::warning title=Benchmark gate::The base commit's benchmarks do not build; times are not compared, allocations still are."
  fi
  echo "::endgroup::"
fi

status=0

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
