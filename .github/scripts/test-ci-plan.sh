#!/usr/bin/env bash
# Tests .github/scripts/ci-plan.sh (issue #587) under the shell options GitHub Actions uses (bash -e, pipefail), with the diff and
# dotnet-affected's answer given: what runs everything, and what a docs-only, a viewer-only, a WPF-only, a core-library and a
# fuzz-only change run. Run from the repository root: .github/scripts/test-ci-plan.sh. Exits 1 if any case fails.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
scratch=$(mktemp -d)
trap 'rm -rf "${scratch}"' EXIT
failures=0
cases=0

# check <name> <event> <base ref> <head ref> <changed paths> <affected projects or FAIL> <expected key=value...>
check() {
  local name="$1" event="$2" base_ref="$3" head_ref="$4" changed="$5" affected="$6" status=0 expectation key value actual
  shift 6
  : > "${scratch}/output"
  (cd "${root}" && EVENT="${event}" BASE_REF="${base_ref}" HEAD_REF="${head_ref}" CI_PLAN_CHANGED="${changed}" \
    CI_PLAN_AFFECTED="${affected}" GITHUB_OUTPUT="${scratch}/output" GITHUB_STEP_SUMMARY=/dev/null \
    bash -e -o pipefail .github/scripts/ci-plan.sh > /dev/null) || status=$?
  cases=$((cases + 1))
  local problems=""
  [ "${status}" -eq 0 ] || problems+=" exit ${status};"
  for expectation in "$@"; do
    key="${expectation%%=*}"
    value="${expectation#*=}"
    actual=$(sed -n "s/^${key}=//p" "${scratch}/output")
    if [[ "${actual}" != ${value} ]]; then
      problems+=" ${key} is '${actual}', not '${value}';"
    fi
  done
  if [ -n "${problems}" ]; then
    echo "FAIL: ${name}:${problems}"
    failures=$((failures + 1))
  else
    echo "ok:   ${name}"
  fi
}

all_jobs=(aot size_probes verapdf conformance format fuzz bench blazor uno maui winui avalonia)
everything=(full=true linux=all windows=all macos=all format_projects=all)
for job in "${all_jobs[@]}"; do everything+=("${job}=true"); done
nothing=(full=false linux= windows= macos= format_projects=)
for job in "${all_jobs[@]}"; do nothing+=("${job}=false"); done

check "push to main" push "" "" "" "" "${everything[@]}" "reason=*push*"
check "nightly" schedule "" "" "" "" "${everything[@]}"
check "run workflow" workflow_dispatch "" "" "" "" "${everything[@]}"
check "pull request into main" pull_request main feature "docs/a.md" "" "${everything[@]}" "reason=*into main*"
check "release branch" pull_request spec/33-broadside-v1 release/0.1.0 "docs/a.md" "" "${everything[@]}" "reason=*release*"
check "workflow change" pull_request spec/33-broadside-v1 x $'docs/a.md\n.github/workflows/ci.yml' "" "${everything[@]}" "reason=*ci.yml*"
check "global.json" pull_request spec/33-broadside-v1 x "global.json" "" "${everything[@]}"
check "solution" pull_request spec/33-broadside-v1 x "Broadside.slnx" "" "${everything[@]}"
check "solution filter" pull_request spec/33-broadside-v1 x "Broadside.Core.slnf" "" "${everything[@]}"
check "solution props" pull_request spec/33-broadside-v1 x "Directory.Solution.props" "" "${everything[@]}"
check "corpus file" pull_request spec/33-broadside-v1 x "tests/Corpus/text-standard14.pdf" "" "${everything[@]}"
check "golden image" pull_request spec/33-broadside-v1 x "tests/Golden/paths.page0.png" "" "${everything[@]}"
check "dotnet-affected fails" pull_request spec/33-broadside-v1 x "src/Broadside/A.cs" FAIL "${everything[@]}" "reason=*failed*"

check "docs only" pull_request spec/33-broadside-v1 x "docs/plan/README.md" "" "${nothing[@]}"
check "conformance map" pull_request spec/33-broadside-v1 x "docs/conformance/iso-32000-2.md" "" full=false linux= conformance=true format=false fuzz=false

viewer=$'src/Broadside.Viewer/Broadside.Viewer.csproj
src/Broadside.Viewer.Wpf/Broadside.Viewer.Wpf.csproj
tests/Broadside.Viewer.Wpf.Tests/Broadside.Viewer.Wpf.Tests.csproj
samples/Broadside.Viewer.Wpf.Sample/Broadside.Viewer.Wpf.Sample.csproj
src/Broadside.Viewer.Avalonia/Broadside.Viewer.Avalonia.csproj
tests/Broadside.Viewer.Avalonia.Tests/Broadside.Viewer.Avalonia.Tests.csproj
samples/Broadside.Viewer.Avalonia.Sample/Broadside.Viewer.Avalonia.Sample.csproj
src/Broadside.Viewer.Blazor/Broadside.Viewer.Blazor.csproj
samples/Broadside.Viewer.Blazor.Sample/Broadside.Viewer.Blazor.Sample.csproj
tests/Broadside.Viewer.Blazor.BrowserTests/Broadside.Viewer.Blazor.BrowserTests.csproj
src/Broadside.Viewer.Maui/Broadside.Viewer.Maui.csproj
samples/Broadside.Viewer.Maui.Sample/Broadside.Viewer.Maui.Sample.csproj
src/Broadside.Viewer.Uno/Broadside.Viewer.Uno.csproj
samples/Broadside.Viewer.Uno.Sample/Broadside.Viewer.Uno.Sample.csproj
src/Broadside.Viewer.WinUI/Broadside.Viewer.WinUI.csproj
samples/Broadside.Viewer.WinUI.Sample/Broadside.Viewer.WinUI.Sample.csproj
tests/Broadside.Viewer.WinUI.UITests/Broadside.Viewer.WinUI.UITests.csproj
tests/Broadside.Viewer.Tests/Broadside.Viewer.Tests.csproj
bench/Broadside.Benchmarks/Broadside.Benchmarks.csproj'
check "viewer only" pull_request spec/33-broadside-v1 x "src/Broadside.Viewer/Tiles.cs" "${viewer}" full=false \
  "linux=*src/Broadside.Viewer/Broadside.Viewer.csproj tests/Broadside.Viewer.Tests/Broadside.Viewer.Tests.csproj" \
  "windows=samples/Broadside.Viewer.Wpf.Sample/Broadside.Viewer.Wpf.Sample.csproj src/Broadside.Viewer.Wpf/Broadside.Viewer.Wpf.csproj tests/Broadside.Viewer.Wpf.Tests/Broadside.Viewer.Wpf.Tests.csproj" \
  macos= "format_projects=*src/Broadside.Viewer/Broadside.Viewer.csproj*" \
  aot=false size_probes=false verapdf=false conformance=false format=true fuzz=false bench=true blazor=true uno=true maui=true winui=true avalonia=true

check "wpf only" pull_request spec/33-broadside-v1 x "src/Broadside.Viewer.Wpf/Control.cs" $'src/Broadside.Viewer.Wpf/Broadside.Viewer.Wpf.csproj
tests/Broadside.Viewer.Wpf.Tests/Broadside.Viewer.Wpf.Tests.csproj
samples/Broadside.Viewer.Wpf.Sample/Broadside.Viewer.Wpf.Sample.csproj' full=false "linux=src/Broadside.Viewer.Wpf/Broadside.Viewer.Wpf.csproj" \
  "windows=samples/Broadside.Viewer.Wpf.Sample/Broadside.Viewer.Wpf.Sample.csproj src/Broadside.Viewer.Wpf/Broadside.Viewer.Wpf.csproj tests/Broadside.Viewer.Wpf.Tests/Broadside.Viewer.Wpf.Tests.csproj" \
  macos= format=true fuzz=false blazor=false uno=false maui=false winui=false avalonia=false aot=false

core=$(cd "${root}" && sed -n 's/^ *"\([^"]*\.csproj\)",\{0,1\}$/\1/p' Broadside.Core.slnf)
core+=$'\ntests/Broadside.Fuzz/Broadside.Fuzz.csproj
tests/Broadside.Rendering.CoreGraphics.Tests/Broadside.Rendering.CoreGraphics.Tests.csproj
src/Broadside.Rendering.CoreGraphics/Broadside.Rendering.CoreGraphics.csproj
tests/Broadside.Rendering.Direct2D.Tests/Broadside.Rendering.Direct2D.Tests.csproj
samples/Broadside.Viewer.Maui.Sample/Broadside.Viewer.Maui.Sample.csproj
samples/Broadside.Viewer.Uno.Sample/Broadside.Viewer.Uno.Sample.csproj
samples/Broadside.Viewer.WinUI.Sample/Broadside.Viewer.WinUI.Sample.csproj
samples/Broadside.Viewer.Blazor.Sample/Broadside.Viewer.Blazor.Sample.csproj
tests/Broadside.Viewer.Avalonia.Tests/Broadside.Viewer.Avalonia.Tests.csproj
bench/Broadside.Benchmarks/Broadside.Benchmarks.csproj'
check "core library" pull_request spec/33-broadside-v1 x "src/Broadside/Parsing/Lexer.cs" "${core}" full=false \
  "linux=*src/Broadside/Broadside.csproj *tests/Broadside.Tests/Broadside.Tests.csproj*" \
  "windows=*tests/Broadside.Rendering.Direct2D.Tests/Broadside.Rendering.Direct2D.Tests.csproj*tests/Broadside.Rendering.Tests/Broadside.Rendering.Tests.csproj*" \
  "macos=*src/Broadside.Rendering.CoreGraphics/Broadside.Rendering.CoreGraphics.csproj*" \
  aot=true size_probes=true verapdf=true conformance=true format=true fuzz=true bench=true blazor=true uno=true maui=true winui=true avalonia=true

check "fuzz harness only" pull_request spec/33-broadside-v1 x "tests/Broadside.Fuzz/PngTarget.cs" "tests/Broadside.Fuzz/Broadside.Fuzz.csproj" \
  full=false linux= windows= macos= format_projects=tests/Broadside.Fuzz/Broadside.Fuzz.csproj format=true fuzz=true aot=false

check "size probe script" pull_request spec/33-broadside-v1 x "bench/size-probes.sh" "" full=false size_probes=true linux=

# The guide samples write PDF/A and PDF/UA files that the verapdf job validates (#270).
check "guide sample" pull_request spec/33-broadside-v1 x "samples/guides/Writing/Program.cs" "samples/guides/Writing/Writing.csproj" \
  full=false linux=samples/guides/Writing/Writing.csproj verapdf=true aot=false fuzz=false
check "guide samples script" pull_request spec/33-broadside-v1 x "samples/guides/run-samples.sh" "" full=false linux= verapdf=true

echo "${cases} case(s), ${failures} failure(s)"
[ "${failures}" -eq 0 ]
