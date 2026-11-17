#!/usr/bin/env bash
# Runs the benchmark regression gate's measurements (issue #139; bench/baselines/gate/README.md): the allocation figures of the
# pull request (and of the base commit, to spot a stale baseline), then the benchmarks of both commits interleaved on this
# machine, then a rerun of whatever the rounds found slower. The verdict is
# `dotnet run --project tools/BenchGate -- compare --baseline bench/baselines/gate/allocations.json --results <out>` afterwards.
#
#   bench/run-gate.sh --head <checkout> --out <dir> [--base <checkout>] [--methods <file>] [--rounds <n>] [--confirm <n>]
#                     [--measure <what>] [--avx512 on|off]
#
#   --head     checkout of the pull request; built here; its tools/BenchGate is the one used.
#   --base     checkout of the commit to compare against (a git worktree); built here. Without it only allocations are measured.
#   --out      results directory: <out>/machine.json, <out>/allocations/{base,head}/, <out>/round-<n>/{base,head}/,
#              <out>/confirm/{base,head}/ and, with --confirm 2 or more, <out>/confirm-<n>/{base,head}/.
#   --methods  file of Namespace.Type.Method lines to run (BenchGate shard output); default every benchmark.
#   --rounds   interleaved rounds, default 2. Odd rounds run base first, even rounds head first, so drift of the machine
#              during the run weighs on both sides alike.
#   --confirm  reruns of the cases the rounds find slower, default 1. Rerun 1 runs base first, rerun 2 head first, and so on.
#   --measure  all (default): everything above. allocations: the pull request's allocation figures only, no base build and no
#              timing (CI's `allocations` jobs, on every pull request). times: the timing rounds and the rerun, plus the base
#              commit's allocation figures for the stale-baseline notice, but not the pull request's (CI's `measure` jobs, which
#              run beside the `allocations` jobs only when times are compared; bench/baselines/gate/README.md, "When it runs").
#   --avx512   on (default): the runtime's default instruction sets. off: AVX-512 disabled in every measuring process
#              (DOTNET_EnableAVX512F=0, DOTNET_EnableAVX512=0), so the JIT has 16 vector registers instead of 32 and a
#              register-pressure regression shows on any runner (issue #539). Times only: no allocation figures are measured.
#
# Every mode first writes <out>/machine.json (BenchGate machine): the runner's processor and whether the measuring processes use
# AVX-512; on GitHub Actions it also adds that line to the job summary.
#
# Exit code: 0 when the pull request's benchmarks all ran (the base side may fail, e.g. a benchmark it does not have yet),
# 1 otherwise.
set -uo pipefail

base=""
head=""
out=""
methods=""
rounds=2
confirm=1
measure=all
avx512=on
while [ $# -gt 0 ]; do
  case "$1" in
    --base) base="$2"; shift 2 ;;
    --head) head="$2"; shift 2 ;;
    --out) out="$2"; shift 2 ;;
    --methods) methods="$2"; shift 2 ;;
    --rounds) rounds="$2"; shift 2 ;;
    --confirm) confirm="$2"; shift 2 ;;
    --measure) measure="$2"; shift 2 ;;
    --avx512) avx512="$2"; shift 2 ;;
    *) echo "unknown option $1" >&2; exit 2 ;;
  esac
done
if [ -z "${head}" ] || [ -z "${out}" ]; then
  echo "usage: $0 --head <checkout> --out <dir> [--base <checkout>] [--methods <file>] [--rounds <n>] [--confirm <n>] [--measure all|allocations|times] [--avx512 on|off]" >&2
  exit 2
fi
case "${measure}" in
  all|times) ;;
  allocations) base="" ;; # the pull request's figures against the stored baseline: the base commit is not needed
  *) echo "--measure takes all, allocations or times, not '${measure}'" >&2; exit 2 ;;
esac
case "${avx512}" in
  on) ;;
  off)
    if [ "${measure}" = allocations ]; then
      echo "--avx512 off times the benchmarks; it does not go with --measure allocations" >&2
      exit 2
    fi
    ;;
  *) echo "--avx512 takes on or off, not '${avx512}'" >&2; exit 2 ;;
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
# Issue #539: a regression that keeps more doubles live than 16 vector registers hold (#335's ResolvePieces) spills only where
# the JIT has no AVX-512 and its 32 registers, so whether the gate saw it depended on the runner's processor. The AVX-512-off
# pass disables it in the runtime (both knobs, whichever one the runtime reads; BenchGate machine checks it took), which
# reproduces the spill on a processor that has AVX-512 and changes nothing on one that does not.
if [ "${avx512}" = off ]; then
  runtime_env+=(DOTNET_EnableAVX512F=0 DOTNET_EnableAVX512=0)
fi

# The timing job: 3 warm-up and 10 measured iterations of about 100 ms, in process, one process per benchmark class (issue
# #337). Plain BenchmarkDotNet options (not a --job name) so a base commit older than the gate runs the same job. Out of process,
# BenchmarkDotNet rebuilt the whole project graph (rendering, fonts, Skia, the viewer) into its generated project without the
# compiler server on every invocation: six minutes on the 4-vCPU runner, six times per shard, a third of a measuring job. In
# process it runs the build made here, so the runtime settings go on the process itself (runtime_env) rather than through
# --envVars, which the in-process toolchain refuses. A class still gets a fresh process, so no case runs after another class's
# code was compiled or its caches filled, and the base and the pull request run each class in the same order.
bench_args=(--inProcess --launchCount 1 --warmupCount 3 --iterationCount 10 --iterationTime 100 --exporters json)

echo "::group::Build the pull request's benchmarks and gate tool"
dotnet build "${head}/bench/Broadside.Benchmarks" -c Release -warnaserror || exit 1
dotnet build "${head}/tools/BenchGate" -c Release -warnaserror || exit 1
echo "::endgroup::"

# The runner's processor and what the measuring processes see of AVX-512, under the same runtime settings. It fails when AVX-512
# was to be disabled and the runtime still uses it, rather than report a pass that measured nothing new.
machine_label="This job"
[ "${avx512}" = off ] && machine_label="This job (AVX-512 disabled pass)"
machine_args=(--out "${out}/machine.json" --label "${machine_label}")
if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  machine_args+=(--summary "${GITHUB_STEP_SUMMARY}")
fi
env "${runtime_env[@]}" dotnet run -c Release --project "${head}/tools/BenchGate" --no-build -- machine "${machine_args[@]}" || exit 1

# The methods to run, one Namespace.Type.Method per line: the --methods file, or every benchmark of the pull request (the timing
# runs go class by class, so they need the names rather than '*').
if [ -z "${methods}" ]; then
  methods="${out}/methods.txt"
  (cd "${head}" && dotnet artifacts/bin/Broadside.Benchmarks/release/Broadside.Benchmarks.dll --list flat) > "${methods}" || exit 1
fi
filters=()
while IFS= read -r line; do
  [ -n "${line// /}" ] && filters+=("${line}")
done < "${methods}"
if [ "${#filters[@]}" -eq 0 ]; then
  echo "No benchmark methods to run."
  exit 0
fi

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

# run <checkout> <side> <folder> <filter...>: one process per benchmark class, each writing its report to
# <out>/<folder>/<side>/<class>/ (BenchmarkDotNet names a report after its class, so two classes never share a folder). A filter
# is Namespace.Type.Method, or a full name with its parameters (the rerun's suspects); its class is what precedes the method.
run() {
  local checkout="$1" side="$2" folder="$3"
  shift 3
  local classes class filter name code
  local selected=()
  classes="$(for filter in "$@"; do name="${filter%%(*}"; echo "${name%.*}"; done | awk '!seen[$0]++')"
  echo "::group::${folder}/${side}"
  while IFS= read -r class; do
    [ -n "${class}" ] || continue
    selected=()
    for filter in "$@"; do
      name="${filter%%(*}"
      [ "${name%.*}" = "${class}" ] && selected+=("${filter}")
    done
    (cd "${checkout}" && env "${runtime_env[@]}" dotnet artifacts/bin/Broadside.Benchmarks/release/Broadside.Benchmarks.dll \
      --filter "${selected[@]}" "${bench_args[@]}" --artifacts "${out}/${folder}/${side}/${class##*.}")
    code=$?
    if [ "${code}" -ne 0 ]; then
      if [ "${side}" = "head" ]; then
        echo "::error title=Benchmark gate::A pull-request benchmark of ${class##*.} failed in ${folder} (exit ${code}); see the log."
        status=1
      else
        echo "::warning title=Benchmark gate::A base-commit benchmark of ${class##*.} failed in ${folder} (exit ${code}); it is not compared in time."
      fi
    fi
  done <<< "${classes}"
  echo "::endgroup::"
}

# The AVX-512-off pass measures times only: allocations do not depend on the instruction set, and the default pass has them.
if [ "${measure}" != times ] && [ "${avx512}" = on ]; then
  allocations "${head}" head
fi
if [ "${base_built}" = true ] && [ "${avx512}" = on ]; then
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
    for pass in $(seq 1 "${confirm}"); do
      folder=confirm
      [ "${pass}" -gt 1 ] && folder="confirm-${pass}"
      if [ $((pass % 2)) -eq 1 ]; then
        run "${base}" base "${folder}" "${again[@]}"
        run "${head}" head "${folder}" "${again[@]}"
      else
        run "${head}" head "${folder}" "${again[@]}"
        run "${base}" base "${folder}" "${again[@]}"
      fi
    done
  fi
fi

exit "${status}"
