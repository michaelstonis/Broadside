#!/usr/bin/env bash
# The pull request title check of pr-checks.yml (issue #264): the title becomes the squash commit's title (or GitHub's merge
# commit body) and a line of the release notes (tools/ReleaseNotes), so it must follow Conventional Commits:
# `type(scope)!: summary`, where the scope and the `!` (a breaking change) are optional.
# Usage: .github/scripts/check-pr-title.sh "<title>". Exits 1 with an error annotation when the title does not match.
set -euo pipefail

title="${1-}"
types='feat|fix|perf|refactor|docs|test|bench|build|ci|chore|style|revert|release'
pattern="^(${types})(\([a-z0-9][a-z0-9._/-]*\))?!?: [^[:space:]].*$"

if [[ "${title}" =~ ${pattern} ]]; then
  echo "Conventional title: ${title}"
  exit 0
fi

echo "::error title=Pull request title::'${title}' does not follow Conventional Commits. Use 'type(scope): summary' (scope optional, '!' before the colon for a breaking change), with type one of: ${types//|/, }. Example: 'fix(cos): recover startxref past the end of the file (#42)'. See CONTRIBUTING.md."
exit 1
