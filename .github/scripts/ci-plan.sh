#!/usr/bin/env bash
# The `plan` of ci.yml (issue #587; .github/workflows/README.md, "What a pull request skips"): whether this run builds and tests
# everything or only what a pull request's change can affect, and then which jobs run and what each operating system builds.
# Run from the checkout CI tests (for a pull request, its merge commit, whose first parent is the base branch as merged into).
#
# Everything runs on pushes, the nightly schedule, the "Run workflow" button, pull requests into main or from a release/* branch,
# pull requests that touch what decides how everything builds (FULL_PATHS: global.json, solutions and filters, the
# solution-level props, the CI workflows and their scripts) or the hand-written corpus and golden images (read at run time by
# most test projects, which dotnet-affected cannot see), and whenever dotnet-affected fails. Otherwise dotnet-affected
# (DOTNET_AFFECTED, the tool's path) lists the projects the merge commit's diff affects through project references, Central
# Package Management, Directory.Build.props/targets and .editorconfig, and each job runs only if one of its projects is listed.
#
# Reads EVENT, BASE_REF and HEAD_REF from the environment; writes the outputs ci.yml's jobs read to GITHUB_OUTPUT and a table
# to GITHUB_STEP_SUMMARY (both default to stdout). For tests (.github/scripts/test-ci-plan.sh), CI_PLAN_CHANGED
# (newline-separated paths) replaces the diff and CI_PLAN_AFFECTED (newline-separated project paths, or FAIL) replaces the
# tool, so neither a merge commit nor dotnet-affected is needed.
set -euo pipefail

output="${GITHUB_OUTPUT:-/dev/stdout}"
summary="${GITHUB_STEP_SUMMARY:-/dev/stdout}"
root=$(pwd)

# A change to one of these runs everything (extended regular expression over repository-relative paths).
FULL_PATHS='^(global\.json|nuget\.config|Directory\.Solution\.props|[^/]*\.slnx?|[^/]*\.slnf|\.github/(workflows|scripts|actions)/.*|tests/(Corpus|Golden)/.*)$'

# What each operating system builds on a narrowed pull request, besides the core filter on Linux. Linux: Broadside.Core.slnf
# plus the projects nothing else builds. Windows and macOS: their platform backends with tests and benchmarks, the WPF viewer,
# and Broadside.Rendering.Tests for the render-hash test (RenderHashTests: every corpus page renders to the same bytes twice).
linux_extra=(
  samples/Broadside.BuilderSamples/Broadside.BuilderSamples.csproj
  bench/Broadside.SampleTimings/Broadside.SampleTimings.csproj
  samples/documents/generator/Broadside.SampleDocuments.csproj
  tools/BenchGate/BenchGate.csproj
  tools/ConformanceCheck/ConformanceCheck.csproj
  tools/CorpusFetcher/CorpusFetcher.csproj
  tools/DocSnippets/DocSnippets.csproj
)
windows_set=(
  src/Broadside.Rendering.Direct2D/Broadside.Rendering.Direct2D.csproj
  tests/Broadside.Rendering.Direct2D.Tests/Broadside.Rendering.Direct2D.Tests.csproj
  bench/Broadside.Rendering.Direct2D.Benchmarks/Broadside.Rendering.Direct2D.Benchmarks.csproj
  src/Broadside.Viewer.Wpf/Broadside.Viewer.Wpf.csproj
  tests/Broadside.Viewer.Wpf.Tests/Broadside.Viewer.Wpf.Tests.csproj
  samples/Broadside.Viewer.Wpf.Sample/Broadside.Viewer.Wpf.Sample.csproj
  tests/Broadside.Rendering.Tests/Broadside.Rendering.Tests.csproj
)
macos_set=(
  src/Broadside.Rendering.CoreGraphics/Broadside.Rendering.CoreGraphics.csproj
  tests/Broadside.Rendering.CoreGraphics.Tests/Broadside.Rendering.CoreGraphics.Tests.csproj
  bench/Broadside.Rendering.CoreGraphics.Benchmarks/Broadside.Rendering.CoreGraphics.Benchmarks.csproj
  src/Broadside.Rendering.Android/Broadside.Rendering.Android.csproj
  tests/Broadside.Rendering.Android.Device/Broadside.Rendering.Android.Device.csproj
  tests/Broadside.Rendering.Android.Tests/Broadside.Rendering.Android.Tests.csproj
  tests/Broadside.Rendering.Tests/Broadside.Rendering.Tests.csproj
)
# The projects `format` checks besides Broadside.Core.slnf (they are outside it).
format_extra=(
  tests/Broadside.Fuzz/Broadside.Fuzz.csproj
  bench/Broadside.Benchmarks/Broadside.Benchmarks.csproj
  tests/Broadside.Rendering.Direct2D.Tests/Broadside.Rendering.Direct2D.Tests.csproj
  bench/Broadside.Rendering.Direct2D.Benchmarks/Broadside.Rendering.Direct2D.Benchmarks.csproj
  tests/Broadside.Viewer.Wpf.Tests/Broadside.Viewer.Wpf.Tests.csproj
  samples/Broadside.Viewer.Wpf.Sample/Broadside.Viewer.Wpf.Sample.csproj
  samples/Broadside.BuilderSamples/Broadside.BuilderSamples.csproj
  tests/Broadside.Viewer.WinUI.UITests/Broadside.Viewer.WinUI.UITests.csproj
)
core=()
while IFS= read -r project; do core+=("${project}"); done < <(sed -n 's/^ *"\([^"]*\.csproj\)",\{0,1\}$/\1/p' Broadside.Core.slnf)
[ "${#core[@]}" -gt 0 ] || { echo "No projects read from Broadside.Core.slnf" >&2; exit 1; }

# The jobs that run only when one of their projects is affected or one of their paths changed (written without associative
# arrays, so that the script and its test also run in macOS's bash 3.2).
jobs=(aot size_probes verapdf conformance format fuzz bench blazor uno maui winui avalonia)
# job_projects <job>: the projects that make the job run (extended regular expression over repository-relative paths).
job_projects() {
  case "$1" in
    aot) echo '^tests/Broadside\.Rendering\.AotSmoke/' ;;
    size_probes) echo '^(bench/SizeProbes/|tools/BenchGate/)' ;;
    verapdf) echo '^(tests/Broadside\.Tests|tests/Broadside\.Fonts\.Standard14\.Tests|tests/Broadside\.Layout\.Tests|tools/CorpusFetcher|samples/guides)/' ;;
    conformance) echo '^(src/(Broadside|Broadside\.Fonts\.Cmaps|Broadside\.Fonts\.Standard14|Broadside\.Layout|Broadside\.Rendering)|tools/ConformanceCheck)/' ;;
    fuzz) echo '^tests/Broadside\.Fuzz/' ;;
    bench) echo '^bench/(Broadside\.Benchmarks|Broadside\.Benchmarks\.Realistic)/' ;;
    blazor) echo '^(samples/Broadside\.Viewer\.Blazor\.Sample|tests/Broadside\.Viewer\.Blazor\.BrowserTests)/' ;;
    uno) echo '^(samples/Broadside\.Viewer\.Uno\.Sample|tests/Broadside\.Viewer\.Uno\.UITests)/' ;;
    maui) echo '^(samples/Broadside\.Viewer\.Maui\.Sample|tools/ApkAotCheck)/' ;;
    winui) echo '^(src/Broadside\.Viewer\.WinUI|samples/Broadside\.Viewer\.WinUI\.Sample|tests/Broadside\.Viewer\.WinUI\.UITests)/' ;;
    avalonia) echo '^(samples/Broadside\.Viewer\.Avalonia\.Sample|tests/Broadside\.Viewer\.Avalonia\.Tests|tools/CorpusFetcher)/' ;;
    *) echo '^$' ;;
  esac
}
# job_paths <job>: other changed paths that make the job run.
job_paths() {
  case "$1" in
    size_probes) echo '^bench/(size-probes\.sh|baselines/gate/sizes\.json)$' ;;
    verapdf) echo '^samples/guides/run-samples\.sh$' ;;
    conformance) echo '^(docs/conformance/|site/articles/conformance\.md$)' ;;
    avalonia) echo '^tests/Broadside\.Rendering\.Tests/CorpusGate/pinned-pages\.txt$' ;;
    *) echo '' ;;
  esac
}

full=false
reason=""
changed=""
affected=""
case "${EVENT}" in
  pull_request)
    if [ "${BASE_REF}" = main ]; then
      full=true
      reason="the pull request is into main"
    elif [[ "${HEAD_REF}" == release/* ]]; then
      full=true
      reason="the pull request is from a release branch"
    else
      if [ -n "${CI_PLAN_CHANGED+set}" ]; then
        changed="${CI_PLAN_CHANGED}"
      else
        changed=$(git diff --name-only HEAD^1 HEAD)
      fi
      forcing=$(grep -E "${FULL_PATHS}" <<< "${changed}" || true)
      if [ -n "${forcing}" ]; then
        full=true
        reason="the pull request changes $(head -n 3 <<< "${forcing}" | paste -sd ' ' -)"
      elif [ -n "${CI_PLAN_AFFECTED+set}" ]; then
        if [ "${CI_PLAN_AFFECTED}" = FAIL ]; then
          full=true
          reason="dotnet-affected failed"
        else
          affected="${CI_PLAN_AFFECTED}"
        fi
      else
        # Every project in the repository, samples outside Broadside.slnx included, against the base branch as merged into.
        scratch=$(mktemp -d)
        status=0
        "${DOTNET_AFFECTED}" --from HEAD^1 --uncommitted none --format text --output-dir "${scratch}" > "${scratch}/log" 2>&1 || status=$?
        if [ "${status}" -eq 0 ]; then
          affected=$(sed "s#^${root}/##" "${scratch}/affected.txt")
        elif [ "${status}" -ne 166 ]; then # 166: no project is affected
          cat "${scratch}/log" >&2
          echo "::warning title=ci plan::dotnet-affected exited ${status}; running everything"
          full=true
          reason="dotnet-affected failed (exit ${status})"
        fi
        rm -rf "${scratch}"
      fi
    fi
    ;;
  *)
    full=true
    reason="the run is a ${EVENT}"
    ;;
esac
affected=$(grep -v '^$' <<< "${affected}" | sort -u || true)

# in_list <path> <array...>: whether the path is one of the array's elements.
in_list() {
  local needle="$1" item
  shift
  for item in "$@"; do
    [ "${item}" = "${needle}" ] && return 0
  done
  return 1
}

# The projects each operating system builds (space-separated; empty skips its build-test leg) and the job switches.
linux=()
windows=()
macos=()
formats=()
# run_<job>: whether the job runs.
if [ "${full}" = true ]; then
  for job in "${jobs[@]}"; do eval "run_${job}=true"; done
  linux_list=all
  windows_list=all
  macos_list=all
  format_list=all
else
  while IFS= read -r project; do
    [ -n "${project}" ] || continue
    if in_list "${project}" "${core[@]}" "${linux_extra[@]}"; then linux+=("${project}"); fi
    if in_list "${project}" "${windows_set[@]}"; then windows+=("${project}"); fi
    if in_list "${project}" "${macos_set[@]}"; then macos+=("${project}"); fi
    if in_list "${project}" "${core[@]}" "${format_extra[@]}"; then formats+=("${project}"); fi
  done <<< "${affected}"
  for job in "${jobs[@]}"; do
    value=false
    if [ -n "${affected}" ] && grep -qE "$(job_projects "${job}")" <<< "${affected}"; then value=true; fi
    paths=$(job_paths "${job}")
    if [ -n "${paths}" ] && grep -qE "${paths}" <<< "${changed}"; then value=true; fi
    eval "run_${job}=${value}"
  done
  run_format=false
  [ "${#formats[@]}" -gt 0 ] && run_format=true
  linux_list="${linux[*]:-}"
  windows_list="${windows[*]:-}"
  macos_list="${macos[*]:-}"
  format_list="${formats[*]:-}"
  count=$(grep -c . <<< "${affected}" || true)
  if [ "${count}" -eq 0 ]; then
    reason="the pull request changes no project"
  else
    reason="the pull request affects ${count} project(s)"
  fi
fi

{
  echo "full=${full}"
  echo "reason=${reason}"
  echo "linux=${linux_list}"
  echo "windows=${windows_list}"
  echo "macos=${macos_list}"
  echo "format_projects=${format_list}"
  for job in "${jobs[@]}"; do eval "echo \"${job}=\${run_${job}}\""; done
} >> "${output}"

{
  echo "## CI plan"
  echo
  if [ "${full}" = true ]; then
    echo "Everything builds and every test runs on every operating system: ${reason}."
  else
    echo "Narrowed to what the change affects: ${reason} (dotnet-affected against the base branch)."
    echo
    echo "| Job | Runs |"
    echo "|---|---|"
    echo "| build-test (ubuntu) | $([ -n "${linux_list}" ] && echo "builds and tests ${#linux[@]} project(s); \`Category=Exhaustive\` sweeps run as samples" || echo "no") |"
    echo "| build-test (windows) | $([ -n "${windows_list}" ] && echo "${windows_list}" || echo "no") |"
    echo "| build-test (macos) | $([ -n "${macos_list}" ] && echo "${macos_list}" || echo "no") |"
    for job in "${jobs[@]}"; do eval "echo \"| ${job} | \${run_${job}} |\""; done
    if [ -n "${affected}" ]; then
      echo
      echo "<details><summary>Affected projects</summary>"
      echo
      sed 's/^/- /' <<< "${affected}"
      echo
      echo "</details>"
    fi
  fi
} >> "${summary}"
