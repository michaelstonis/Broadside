#!/usr/bin/env bash
# The benchmark gate's `plan` (issue #389; bench/baselines/gate/README.md, "When it runs"): which commits a run measures and
# whether it compares times, and why. Run from the checkout the run measures (for a pull request, its merge commit).
#
# Reads the environment bench-gate.yml sets: EVENT (pull_request, schedule or workflow_dispatch), BASE_REF, PR, INPUT_BASE,
# INPUT_TIMING, HOT_PATHS, NIGHTLY_BRANCH, GITHUB_REPOSITORY; writes run, head, base, timing and reason to GITHUB_OUTPUT and a
# paragraph to GITHUB_STEP_SUMMARY (both default to stdout). For tests, BENCH_PLAN_LABELS (newline-separated) and
# BENCH_PLAN_CHANGED (newline-separated paths) replace the pull request's labels and diff, so neither gh nor a merge commit is
# needed. Exits non-zero on any error, which fails `plan` and with it the `gate` job.
set -euo pipefail

output="${GITHUB_OUTPUT:-/dev/stdout}"
summary="${GITHUB_STEP_SUMMARY:-/dev/stdout}"
head=$(git rev-parse HEAD)
run=true

case "${EVENT}" in
  pull_request)
    # The pull_request checkout is the merge commit; its first parent is the base branch as merged into.
    base=$(git rev-parse HEAD^1)
    if [ -n "${BENCH_PLAN_LABELS+set}" ]; then
      labels="${BENCH_PLAN_LABELS}"
    else
      labels=$(gh pr view "${PR}" --repo "${GITHUB_REPOSITORY}" --json labels --jq '.labels[].name')
    fi
    if [ -n "${BENCH_PLAN_CHANGED+set}" ]; then
      changed="${BENCH_PLAN_CHANGED}"
    else
      changed=$(git diff --name-only "${base}" "${head}")
    fi
    hot=$(grep -E "${HOT_PATHS}" <<< "${changed}" || true)
    if [ "${BASE_REF}" = main ]; then
      timing=true
      reason="the pull request is into main"
    elif grep -qx perf <<< "${labels}"; then
      timing=true
      reason="the pull request is labelled perf"
    elif [ -n "${hot}" ]; then
      count=$(grep -c . <<< "${hot}")
      shown=$(head -n 3 <<< "${hot}" | paste -sd ' ' -)
      if [ "${count}" -gt 3 ]; then
        shown="${shown} and $((count - 3)) more"
      fi
      timing=true
      reason="the pull request changes a hot path (${shown})"
    else
      timing=false
      reason="the pull request changes no hot path and is not labelled perf"
    fi
    ;;
  schedule)
    # Since the previous nightly that measured something (success or failure; a cancelled one measured nothing).
    since=$(gh run list --repo "${GITHUB_REPOSITORY}" --workflow bench-gate.yml --event schedule --status completed --limit 30 \
      --json createdAt,conclusion --jq '[.[] | select(.conclusion == "success" or .conclusion == "failure")][0].createdAt // empty')
    if [ -z "${since}" ]; then
      since=$(date -u -d '24 hours ago' +%Y-%m-%dT%H:%M:%SZ)
    fi
    base=$(git rev-list -1 --first-parent --before="${since}" HEAD)
    timing=true
    reason="nightly: ${NIGHTLY_BRANCH} at ${head:0:10} against ${base:0:10}, its tip at ${since}"
    if [ -z "${base}" ] || [ "${base}" = "${head}" ]; then
      run=false
      timing=false
      base="${head}"
      reason="nightly: nothing merged into ${NIGHTLY_BRANCH} since ${since}"
    fi
    ;;
  *)
    base=$(git rev-parse "origin/${INPUT_BASE}" 2>/dev/null || git rev-parse "${INPUT_BASE}")
    timing="${INPUT_TIMING:-true}"
    reason="run by hand against ${INPUT_BASE}"
    ;;
esac

{
  echo "run=${run}"
  echo "head=${head}"
  echo "base=${base}"
  echo "timing=${timing}"
  echo "reason=${reason}"
} >> "${output}"

if [ "${run}" = true ]; then allocations=checked; else allocations=skipped; fi
if [ "${timing}" = true ]; then times=compared; else times="not compared"; fi
{
  echo "## Benchmark gate plan"
  echo
  echo "Head \`${head:0:10}\`, base \`${base:0:10}\`. Allocations: ${allocations}. Times: ${times}, because ${reason}."
  if [ "${EVENT}" = pull_request ] && [ "${timing}" != true ]; then
    echo
    echo "To compare times on this pull request, add the \`perf\` label (bench-gate-perf.yml then re-runs this workflow)."
  fi
} >> "${summary}"
