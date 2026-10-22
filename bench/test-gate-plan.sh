#!/usr/bin/env bash
# Tests bench/gate-plan.sh's pull-request decision (issue #389) under the shell options GitHub Actions uses (bash -e, pipefail):
# 0, 1, 3, 4 and 10 changed hot-path files (and a change outside them), with and without the `perf` label, into main and into
# the integration branch. Run from a checkout whose HEAD has a parent: bench/test-gate-plan.sh. Exits 1 on the first failure.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
# HOT_PATHS exactly as the workflow sets it (the line after `HOT_PATHS: >-`).
hot_paths=$(awk '/HOT_PATHS: >-/ { getline; sub(/^[ \t]+/, ""); print; exit }' "${root}/.github/workflows/bench-gate.yml")
[ -n "${hot_paths}" ] || { echo "HOT_PATHS not found in bench-gate.yml" >&2; exit 1; }

scratch=$(mktemp -d)
trap 'rm -rf "${scratch}"' EXIT
failures=0
cases=0

# check <count of hot files> <labels> <base ref> <expected timing> <expected reason fragment>
check() {
  local count="$1" labels="$2" base_ref="$3" expected="$4" fragment="$5" changed="docs/plan/README.md" i
  for ((i = 1; i <= count; i++)); do
    changed+=$'\n'"src/Broadside/Parsing/File${i}.cs"
  done
  : > "${scratch}/output"
  local status=0
  (cd "${root}" && EVENT=pull_request BASE_REF="${base_ref}" PR=0 HOT_PATHS="${hot_paths}" GITHUB_REPOSITORY=x/y \
    BENCH_PLAN_LABELS="${labels}" BENCH_PLAN_CHANGED="${changed}" GITHUB_OUTPUT="${scratch}/output" \
    GITHUB_STEP_SUMMARY=/dev/null bash -e -o pipefail bench/gate-plan.sh > /dev/null) || status=$?
  cases=$((cases + 1))
  local timing reason run
  timing=$(sed -n 's/^timing=//p' "${scratch}/output")
  reason=$(sed -n 's/^reason=//p' "${scratch}/output")
  run=$(sed -n 's/^run=//p' "${scratch}/output")
  if [ "${status}" -ne 0 ] || [ "${run}" != true ] || [ "${timing}" != "${expected}" ] || [[ "${reason}" != *"${fragment}"* ]]; then
    echo "FAIL: ${count} hot file(s), labels '${labels}', into ${base_ref}: exit ${status}, run '${run}', timing '${timing}', reason '${reason}'"
    failures=$((failures + 1))
  else
    echo "ok:   ${count} hot file(s), labels '${labels:-none}', into ${base_ref}: timing ${timing} (${reason})"
  fi
}

for base_ref in spec/33-broadside-v1 main; do
  for labels in "" "perf" $'in-progress\nperf' "in-progress"; do
    for count in 0 1 3 4 10; do
      if [ "${base_ref}" = main ]; then
        check "${count}" "${labels}" "${base_ref}" true "into main"
      elif grep -qx perf <<< "${labels}"; then
        check "${count}" "${labels}" "${base_ref}" true "labelled perf"
      elif [ "${count}" -eq 0 ]; then
        check "${count}" "${labels}" "${base_ref}" false "no hot path"
      elif [ "${count}" -gt 3 ]; then
        check "${count}" "${labels}" "${base_ref}" true "File3.cs and $((count - 3)) more)"
      else
        check "${count}" "${labels}" "${base_ref}" true "File${count}.cs)"
      fi
    done
  done
done

echo "${cases} case(s), ${failures} failure(s)"
[ "${failures}" -eq 0 ]
