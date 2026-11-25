#!/usr/bin/env bash
# Proves the v1 API freeze holds: a deliberate breaking change to a shipping package must fail the build.
#
#   ./check-api-freeze.sh      exit 0 when the breaking change fails the build as it should, 1 when it builds
#
# Every shipping package's public API is recorded in PublicAPI.Shipped.txt and its PublicAPI.Unshipped.txt holds no entries
# (Broadside.ApiGuard.Tests checks both). The script renames one shipped public method of Broadside.Fonts.Cmaps, the smallest
# shipping package, builds it, and restores the source whatever happens. The build must fail with RS0017 (a shipped member is
# gone) and RS0016 (a public member nobody declared), which the PublicApiAnalyzers report and the build treats as errors.
# Works with macOS bash 3.2.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="${ROOT}/src/Broadside.Fonts.Cmaps/Broadside.Fonts.Cmaps.csproj"
SOURCE="$(grep -l 'public static PdfOptions UsePredefinedCMaps(' "${ROOT}"/src/Broadside.Fonts.Cmaps/*.cs)"
BACKUP="$(mktemp)"
LOG="$(mktemp)"

cp "${SOURCE}" "${BACKUP}"
trap 'cp "${BACKUP}" "${SOURCE}"; rm -f "${BACKUP}" "${LOG}"' EXIT

# The breaking change: a shipped method renamed, as a careless refactoring would.
sed 's/public static PdfOptions UsePredefinedCMaps(/public static PdfOptions UsePredefinedCMapsRenamed(/' "${BACKUP}" > "${SOURCE}"

if dotnet build "${PROJECT}" -nologo -v:q -p:UseSharedCompilation=false -nodeReuse:false > "${LOG}" 2>&1; then
    echo "FAIL: renaming a shipped public method of Broadside.Fonts.Cmaps built; the API freeze is not enforced." >&2
    exit 1
fi

if ! grep -q 'RS0017' "${LOG}"; then
    echo "FAIL: the build failed, but not with RS0017 (a shipped member removed):" >&2
    grep -E 'error' "${LOG}" | sort -u >&2 || true
    exit 1
fi

echo "OK: the breaking change failed the build:"
grep -E 'error RS00(16|17)' "${LOG}" | sed -E 's/^.*(error RS00(16|17)[^[]*).*$/  \1/' | sort -u
