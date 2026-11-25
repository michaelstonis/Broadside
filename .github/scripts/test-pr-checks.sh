#!/usr/bin/env bash
# Tests the pull request checks of pr-checks.yml (issue #264) under the shell options GitHub Actions uses: check-pr-title.sh
# accepts Conventional Commit titles and rejects others. Run from the repository root:
# .github/scripts/test-pr-checks.sh. Exits 1 if any case fails.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
scratch=$(mktemp -d)
trap 'rm -rf "${scratch}"' EXIT
failures=0
cases=0

# expect <name> <expected exit code> <command...>
expect() {
  local name="$1" expected="$2" status=0
  shift 2
  "$@" > "${scratch}/log" 2>&1 || status=$?
  cases=$((cases + 1))
  if [ "${status}" -ne "${expected}" ]; then
    failures=$((failures + 1))
    echo "FAIL ${name}: exit ${status}, expected ${expected}"
    sed 's/^/    /' "${scratch}/log"
  else
    echo "ok   ${name}"
  fi
}

title() { bash -e -o pipefail "${root}/.github/scripts/check-pr-title.sh" "$1"; }
expect "title: type and scope" 0 title "feat(fonts): parse CFF charstrings (#42)"
expect "title: no scope" 0 title "docs: fix a typo"
expect "title: breaking" 0 title "refactor(api)!: rename PdfDocument.Load to Open"
expect "title: release" 0 title "release: v0.1.0-preview.11"
expect "title: hyphenated scope" 0 title "fix(viewer-maui): keep the panel open"
expect "title: plain sentence" 1 title "Update README"
expect "title: unknown type" 1 title "feature(fonts): parse CFF"
expect "title: upper-case type" 1 title "Fix(cos): recover startxref"
expect "title: no space after the colon" 1 title "fix:recover startxref"
expect "title: empty summary" 1 title "fix(cos): "
expect "title: GitHub's default for a branch" 1 title "264 versioning release notes"

echo "${cases} cases, ${failures} failed."
[ "${failures}" -eq 0 ]
