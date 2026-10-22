#!/usr/bin/env bash
# Fails when user-facing text references architecture decision records. Decision records stay in
# docs/adr/ for maintainers; the docs, READMEs (which are also the NuGet package READMEs), the site,
# the GitHub templates and the XML doc comments that become the API reference state the substance
# in plain words instead of citing them.
#
#   ./check-adr-refs.sh        list every offending line and exit 1, or exit 0
#
# Scope: top-level *.md (except CLAUDE.md and CONTEXT.md, which are agent and maintainer
# instructions), site/**, docs/** (except docs/adr/**), every README, the issue and pull request
# templates, and /// lines in src/**/*.cs. .claude/** is out of scope.
# Match: the case-sensitive token ADR or ADRs, or a path segment adr/ (docs/adr/, ../adr/).
# Words that merely contain "adr" (quadratic, Adrian) do not match.
# Called by check-docs.sh; runs in CI (.github/workflows/docs.yml). Works with macOS bash 3.2.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "${ROOT}"

PATTERN='(^|[^A-Za-z0-9_])(ADRs?([^A-Za-z0-9_]|$)|adr/)'

doc_files() {
    git ls-files -z \
        | tr '\0' '\n' \
        | grep -v -E '^(\.claude/|docs/adr/|CLAUDE\.md$|CONTEXT\.md$)' \
        | grep -E '^[^/]+\.md$|^site/|^docs/|(^|/)README[^/]*$|^\.github/ISSUE_TEMPLATE/|^\.github/PULL_REQUEST_TEMPLATE' \
        || true
}

hits="$(mktemp)"
trap 'rm -f "${hits}"' EXIT

doc_files | while IFS= read -r f; do
    [ -f "${f}" ] || continue
    grep -I -n -H -E "${PATTERN}" "${f}" || true
done >> "${hits}"

git ls-files -z -- 'src/*.cs' \
    | tr '\0' '\n' \
    | while IFS= read -r f; do
        [ -f "${f}" ] || continue
        grep -n -H -E "^[[:space:]]*///.*${PATTERN}" "${f}" || true
    done >> "${hits}"

if [ -s "${hits}" ]; then
    echo "check-adr-refs: user-facing text references ADRs; state the decision in plain words instead:" >&2
    cat "${hits}" >&2
    echo "check-adr-refs: FAILED with $(wc -l < "${hits}" | tr -d ' ') reference(s)" >&2
    exit 1
fi
echo "check-adr-refs: OK, no ADR references in docs, READMEs, site pages, templates or XML doc comments"
