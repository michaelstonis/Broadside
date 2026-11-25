#!/usr/bin/env bash
# Builds and runs the guide samples: every project under samples/guides/, each a console app whose code the site's guides include by
# region (tools/DocSnippets). A sample exits with 1 when a result is not what its guide says, and this script fails when any does.
# Every PDF file a sample writes is collected (Shared/Sample.cs, Sample.Finish) and checked:
#   - qpdf --check, every file but those encrypted for certificates, which qpdf cannot decrypt. Required on CI (the CI variable is set) except with --verapdf, whose job has no qpdf (build-test
#     runs the qpdf check); elsewhere skipped with a warning when qpdf is not installed.
#   - poppler's pdfsig, every file with signatures: each signature pdfsig can verify must be valid (it does not verify document
#     timestamps, sub-filter ETSI.RFC3161, which it lists as type "unknown"). Skipped with a warning when pdfsig is not installed.
#   - with --verapdf, veraPDF, every file whose name carries a profile: -pdfa2b, -pdfa2u, -pdfa3b, -pdfa3u, -pdfa4, -pdfua1 or
#     -pdfua2 (several where a file claims several). veraPDF must be on the PATH; CI's verapdf job runs this mode.
#
#   samples/guides/run-samples.sh                              build (Debug, warnings as errors), run and check every sample
#   samples/guides/run-samples.sh --build-only                 only build them (./check-docs.sh: an included sample must compile)
#   samples/guides/run-samples.sh -c Release --no-build        run the samples an earlier build produced (CI's build-test job)
#   samples/guides/run-samples.sh -c Release --verapdf         also validate the PDF/A and PDF/UA files with veraPDF (CI's verapdf job)
#
# Every project must be listed in Guides.slnf (built in one go) and in Broadside.slnx and Broadside.Core.slnf (built, formatted
# and planned by CI); the script checks the first two. Works with the bash 3.2 that ships with macOS.
set -euo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "${DIR}/../.." && pwd)"
configuration=Debug
build=true
run=true
verapdf=false
while [ $# -gt 0 ]; do
    case "$1" in
        -c|--configuration) configuration="$2"; shift 2 ;;
        --no-build) build=false; shift ;;
        --build-only) run=false; shift ;;
        --verapdf) verapdf=true; shift ;;
        *) echo "usage: $0 [-c <configuration>] [--no-build | --build-only] [--verapdf]" >&2; exit 2 ;;
    esac
done
if [ "${verapdf}" = true ] && ! command -v verapdf > /dev/null; then
    echo "run-samples: --verapdf needs verapdf on the PATH" >&2
    exit 2
fi

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
    output="$(mktemp -d)"
    trap 'rm -rf "${output}"' EXIT
    export BROADSIDE_SAMPLES_OUTPUT="${output}"
    for project in "${DIR}"/*/*.csproj; do
        if ! dotnet run --project "${project}" -c "${configuration}" --no-build; then
            echo "run-samples: $(basename "${project}" .csproj) FAILED" >&2
            failures=$((failures + 1))
        fi
    done

    if command -v qpdf > /dev/null; then
        checked=0
        for file in "${output}"/*/*.pdf; do
            [ -f "${file}" ] || continue
            # qpdf has no public-key security handler (Adobe.PubSec): such a file cannot be checked, only skipped.
            report="$(qpdf --check "${file}" 2>&1 || true)"
            if grep -q 'unsupported encryption filter' <<< "${report}"; then
                echo "run-samples: qpdf cannot decrypt ${file#"${output}/"} (public-key encryption), not checked"
                continue
            fi
            checked=$((checked + 1))
            if ! qpdf --check "${file}" > /dev/null 2>&1; then
                echo "run-samples: qpdf --check fails on ${file#"${output}/"}:" >&2
                qpdf --check "${file}" >&2 || true
                failures=$((failures + 1))
            fi
        done
        echo "run-samples: qpdf checked ${checked} file(s)"
    elif [ -n "${CI:-}" ] && [ "${verapdf}" = false ]; then
        echo "run-samples: qpdf is not installed" >&2
        failures=$((failures + 1))
    else
        echo "run-samples: warning: qpdf is not installed, so the samples' output is not checked" >&2
    fi

    if command -v pdfsig > /dev/null; then
        signed=0
        for file in "${output}"/*/*.pdf; do
            [ -f "${file}" ] || continue
            report="$(pdfsig "${file}" 2> /dev/null || true)"
            grep -q '^Signature #' <<< "${report}" || continue
            signed=$((signed + 1))
            # A verdict per signature whose type pdfsig knows; a document timestamp is type "unknown" and is not verified.
            bad="$(awk '/^Signature #/ { type = "" } /Signature Type:/ { type = $NF }
                /Signature Validation:/ && type != "unknown" && $0 !~ /Signature is Valid/ { print }' <<< "${report}")"
            if [ -n "${bad}" ]; then
                echo "run-samples: pdfsig does not verify ${file#"${output}/"}:" >&2
                echo "${report}" >&2
                failures=$((failures + 1))
            fi
        done
        echo "run-samples: pdfsig verified ${signed} signed file(s)"
    else
        echo "run-samples: warning: pdfsig (poppler-utils) is not installed, so the signed files are not verified" >&2
    fi

    if [ "${verapdf}" = true ]; then
        validated=0
        for file in "${output}"/*/*.pdf; do
            [ -f "${file}" ] || continue
            for profile in pdfa2b pdfa2u pdfa3b pdfa3u pdfa4 pdfua1 pdfua2; do
                case "$(basename "${file}")" in *-"${profile}"[-.]*) ;; *) continue ;; esac
                case "${profile}" in pdfa*) flavour="${profile#pdfa}" ;; *) flavour="ua${profile#pdfua}" ;; esac
                validated=$((validated + 1))
                if ! verapdf --flavour "${flavour}" --format xml "${file}" 2> /dev/null | grep -q 'isCompliant="true"'; then
                    echo "run-samples: veraPDF (${flavour}) fails ${file#"${output}/"}:" >&2
                    verapdf --flavour "${flavour}" --format text -v "${file}" >&2 || true
                    failures=$((failures + 1))
                fi
            done
        done
        if [ "${validated}" -eq 0 ]; then
            echo "run-samples: no sample wrote a file named for a PDF/A or PDF/UA profile" >&2
            failures=$((failures + 1))
        fi
        echo "run-samples: veraPDF validated ${validated} profile(s)"
    fi

    if [ "${failures}" -ne 0 ]; then
        echo "run-samples: ${failures} sample(s) or file(s) failed" >&2
        exit 1
    fi
    echo "run-samples: every guide sample ran"
fi
