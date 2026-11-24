#!/usr/bin/env bash
# The Developer Certificate of Origin check of pr-checks.yml (issue #264): every commit a pull request adds (base..head, merge
# commits left out) must carry a `Signed-off-by:` trailer with its author's email address, which `git commit -s` writes.
# Commits authored by GitHub Apps (`...[bot]@users.noreply.github.com`) are exempt. Usage, from the repository root:
# .github/scripts/check-dco.sh <base> <head>. Exits 1 with one error annotation per commit without a matching sign-off.
set -euo pipefail

base="${1:?usage: check-dco.sh <base> <head>}"
head="${2:?usage: check-dco.sh <base> <head>}"
summary="${GITHUB_STEP_SUMMARY:-/dev/null}"

checked=0
unsigned=0
lower() { tr '[:upper:]' '[:lower:]'; }
commits=$(git rev-list --no-merges --reverse "${base}..${head}")
for sha in ${commits}; do
  email=$(git log -1 --format=%ae "${sha}" | lower)
  subject=$(git log -1 --format=%s "${sha}")
  if [[ "${email}" == *"[bot]@users.noreply.github.com" ]]; then
    echo "skip ${sha:0:12} (bot author): ${subject}"
    continue
  fi
  checked=$((checked + 1))
  signoffs=$(git log -1 --format='%(trailers:key=Signed-off-by,valueonly)' "${sha}" | lower)
  if grep -qF "<${email}>" <<< "${signoffs}"; then
    echo "ok   ${sha:0:12}: ${subject}"
  else
    unsigned=$((unsigned + 1))
    echo "::error title=DCO sign-off missing::${sha:0:12} '${subject}' has no 'Signed-off-by:' line for its author <${email}>. Sign off with 'git commit -s'; fix existing commits with 'git rebase --signoff <base>' and force-push. See CONTRIBUTING.md."
  fi
done

if [ "${unsigned}" -gt 0 ]; then
  echo "**DCO**: ${unsigned} of ${checked} commit(s) have no sign-off by their author." >> "${summary}"
  exit 1
fi
echo "DCO: all ${checked} commit(s) signed off by their authors."
echo "**DCO**: all ${checked} commit(s) signed off by their authors." >> "${summary}"
