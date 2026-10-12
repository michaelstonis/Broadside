#!/usr/bin/env bash
# Fuzzes one target under libFuzzer for a fixed time (README.md, "Real fuzzing"; .github/workflows/fuzz.yml runs it per target).
#
#   tests/Broadside.Fuzz/run-libfuzzer.sh <target> <seconds> [pdf-directory...]
#
# Before: the harness is built in Release (dotnet build -c Release tests/Broadside.Fuzz), the driver is built
# (tests/Broadside.Fuzz/build-libfuzzer-dotnet.sh) and the sharpfuzz tool is on PATH
# (dotnet tool install --global SharpFuzz.CommandLine --version 2.3.0). Linux only. The script fuzzes a copy of the build in
# artifacts/fuzz/instrumented/ whose Broadside.dll it instruments; seeds and replays run on the plain build, because
# instrumented code writes coverage into libFuzzer's shared memory and faults when started without the driver.
#
# Work directory artifacts/fuzz/<target>/: seeds/ (rewritten each run by --seeds from tests/Corpus and the PDF directories),
# corpus/ (what libFuzzer found; keep it between runs, CI caches it, and it is minimized with -merge=1 after each run),
# findings/ (crash-*, timeout-*, oom-* inputs, each with a .txt replay of what the harness reports), logs/ (one log per worker)
# and stats.json (time, workers, executions, corpus size, findings). Exit code 0 when nothing was found, 1 otherwise.
#
# Environment: FUZZ_WORKERS (parallel libFuzzer workers, default the processor count), LIBFUZZER_DOTNET (the driver, default
# artifacts/fuzz/libfuzzer-dotnet), DOTNET_GCHeapHardLimit (managed heap cap per worker, default 3 GiB, so a decompression
# bomb surfaces as an OutOfMemoryException finding instead of the machine swapping).
set -euo pipefail

if [ "$#" -lt 2 ]; then
  echo "usage: $0 <target> <seconds> [pdf-directory...]" >&2
  exit 2
fi

target="$1"
seconds="$2"
shift 2
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
build="${root}/artifacts/bin/Broadside.Fuzz/release"
harness="${build}/Broadside.Fuzz.dll"
instrumented="${root}/artifacts/fuzz/instrumented"
driver="${LIBFUZZER_DOTNET:-${root}/artifacts/fuzz/libfuzzer-dotnet}"
workers="${FUZZ_WORKERS:-$(nproc)}"
work="${root}/artifacts/fuzz/${target}"
max_len=262144

mkdir -p "${work}/corpus" "${work}/findings" "${work}/logs"
rm -rf "${work}/seeds"
dotnet "${harness}" --seeds "${target}" "${work}/seeds" "$@"

# Instrument a copy of the build (once per build: the copy is redone when the build is newer).
if [ ! -f "${instrumented}/Broadside.dll" ] || [ "${build}/Broadside.dll" -nt "${instrumented}/Broadside.dll" ]; then
  rm -rf "${instrumented}"
  mkdir -p "${instrumented}"
  cp -R "${build}/." "${instrumented}/"
  sharpfuzz "${instrumented}/Broadside.dll"
fi

export BROADSIDE_FUZZ_TARGET="${target}"
export DOTNET_GCHeapHardLimit="${DOTNET_GCHeapHardLimit:-0xC0000000}"
fuzz_options=(
  --target_path=dotnet
  "--target_arg=${instrumented}/Broadside.Fuzz.dll"
  -timeout=10
  -rss_limit_mb=4096
  "-max_len=${max_len}"
  "-dict=${root}/tests/Broadside.Fuzz/pdf.dict"
)

# -jobs writes fuzz-<n>.log into the current directory.
started=${SECONDS}
set +e
(cd "${work}/logs" && "${driver}" "${fuzz_options[@]}" \
  "-max_total_time=${seconds}" \
  "-artifact_prefix=${work}/findings/" \
  "-jobs=${workers}" "-workers=${workers}" \
  -print_final_stats=1 \
  "${work}/corpus" "${work}/seeds")
fuzz_status=$?
set -e
elapsed=$((SECONDS - started))

# Keep the corpus small for the cache: only inputs that add coverage. On failure the unminimized corpus stays.
rm -rf "${work}/corpus-min"
mkdir -p "${work}/corpus-min"
if (cd "${work}/logs" && timeout 1800 "${driver}" "${fuzz_options[@]}" -merge=1 "${work}/corpus-min" "${work}/corpus" > merge.log 2>&1); then
  rm -rf "${work}/corpus"
  mv "${work}/corpus-min" "${work}/corpus"
else
  echo "corpus merge failed; keeping the unminimized corpus (see ${work}/logs/merge.log)"
  rm -rf "${work}/corpus-min"
fi

# Executions per worker: the final statistics, or the last status line of a worker that stopped on a finding.
executions=0
for log in "${work}"/logs/fuzz-*.log; do
  [ -f "${log}" ] || continue
  count=$(sed -n 's/^stat::number_of_executed_units: *\([0-9]*\)$/\1/p' "${log}" | tail -1)
  if [ -z "${count}" ]; then
    count=$(sed -n 's/^#\([0-9][0-9]*\)[[:space:]].*/\1/p' "${log}" | tail -1)
  fi
  executions=$((executions + ${count:-0}))
done
corpus_files=$(find "${work}/corpus" -type f | wc -l | tr -d ' ')
findings=()
while IFS= read -r -d '' finding; do
  findings+=("$(basename "${finding}")")
  # Replay each finding so the artifact carries the exception and stack trace, not just the input.
  timeout 120 dotnet "${harness}" --run "${target}" "${finding}" > "${finding}.txt" 2>&1 || true
done < <(find "${work}/findings" -type f \( -name 'crash-*' -o -name 'timeout-*' -o -name 'oom-*' -o -name 'leak-*' \) ! -name '*.txt' -print0 | sort -z)

jq -n \
  --arg target "${target}" \
  --argjson seconds "${elapsed}" \
  --argjson workers "${workers}" \
  --argjson executions "${executions}" \
  --argjson corpus "${corpus_files}" \
  --argjson status "${fuzz_status}" \
  --args '{target: $target, wallSeconds: $seconds, workers: $workers, cpuHours: (($seconds * $workers) / 3600 * 100 | round / 100), executions: $executions, corpusFiles: $corpus, libFuzzerExitCode: $status, findings: $ARGS.positional}' \
  "${findings[@]}" > "${work}/stats.json"
cat "${work}/stats.json"

if [ "${#findings[@]}" -gt 0 ]; then
  echo "::error::fuzz target ${target}: ${#findings[@]} finding(s); replay with: dotnet run -c Release --project tests/Broadside.Fuzz -- --run ${target} <file>"
  exit 1
fi

if [ "${fuzz_status}" -ne 0 ]; then
  echo "::error::libFuzzer exited with ${fuzz_status} for ${target} without leaving a finding; see artifacts/fuzz/${target}/logs"
  exit 1
fi
