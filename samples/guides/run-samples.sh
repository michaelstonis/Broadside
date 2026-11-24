#!/usr/bin/env bash
# Builds and runs the guide samples: every project under samples/guides/, each a console app whose code the site's guides include by
# region (tools/DocSnippets). A sample exits with 1 when a result is not what its guide says, and this script fails when any does.
#
#   samples/guides/run-samples.sh                              build (Debug, warnings as errors) and run every sample
#   samples/guides/run-samples.sh --build-only                 only build them (./check-docs.sh: an included sample must compile)
#   samples/guides/run-samples.sh -c Release --no-build        run the samples an earlier build produced (CI's build-test job)
#
# Every project must be listed in Guides.slnf (built in one go) and in Broadside.slnx and Broadside.Core.slnf (built, formatted
# and planned by CI); the script checks the first two. Works with the bash 3.2 that ships with macOS.
set -euo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "${DIR}/../.." && pwd)"
configuration=Debug
build=true
run=true
while [ $# -gt 0 ]; do
    case "$1" in
        -c|--configuration) configuration="$2"; shift 2 ;;
        --no-build) build=false; shift ;;
        --build-only) run=false; shift ;;
        *) echo "usage: $0 [-c <configuration>] [--no-build | --build-only]" >&2; exit 2 ;;
    esac
done

failures=0
for project in "${DIR}"/*/*.csproj; do
    relative="${project#"${ROOT}/"}"
    for list in "${DIR}/Guides.slnf" "${ROOT}/Broadside.slnx" "${ROOT}/Broadside.Core.slnf"; do
        if ! grep -q "${relative}" "${list}"; then
            echo "run-samples: ${relative} is not listed in ${list#"${ROOT}/"}" >&2
            failures=$((failures + 1))
        fi
    done
done
[ "${failures}" -eq 0 ] || exit 1

if [ "${build}" = true ]; then
    dotnet build "${DIR}/Guides.slnf" -c "${configuration}" -warnaserror --nologo
fi

if [ "${run}" = true ]; then
    for project in "${DIR}"/*/*.csproj; do
        if ! dotnet run --project "${project}" -c "${configuration}" --no-build; then
            echo "run-samples: $(basename "${project}" .csproj) FAILED" >&2
            failures=$((failures + 1))
        fi
    done
    if [ "${failures}" -ne 0 ]; then
        echo "run-samples: ${failures} sample(s) failed" >&2
        exit 1
    fi
    echo "run-samples: every guide sample ran"
fi
