---
name: picking-up-an-issue
description: "Use when starting any unit of work in Broadside: find an unblocked issue in the current milestone, claim it, branch in a worktree, implement, verify, commit, open the PR, and clean up."
---

# Picking up an issue

The flow from `CLAUDE.md` ("Picking up work") and `CONTRIBUTING.md`, made operational. Humans and agents follow the same steps. Pass `--repo michaelstonis/Broadside` to every `gh` call; it then works from any directory.

## 1. Find an unblocked issue

The current milestone is the lowest-numbered one that still has open issues.

```sh
gh api repos/michaelstonis/Broadside/milestones --jq '.[] | "\(.number) \(.title) open=\(.open_issues)"'
gh issue list --repo michaelstonis/Broadside --milestone "Phase 1: COS layer" --search "no:assignee" --state open
```

An issue is unblocked when every issue under its `### Blocked by` heading is closed (`none` means no blockers). List the blockers, then check each one:

```sh
gh issue list --repo michaelstonis/Broadside --milestone "Phase 1: COS layer" --search "no:assignee" --state open \
  --json number,title,body --jq '.[] | "\(.number) \(.title) | blocked by: \(.body | capture("### Blocked by\\n(?<b>[^\\n]*)").b)"'
gh issue view 2 --repo michaelstonis/Broadside --json state --jq .state      # CLOSED: the blocker is done
```

Take the lowest-numbered unblocked issue unless the caller named one.

## 2. Claim it

```sh
gh issue edit <n> --repo michaelstonis/Broadside --add-assignee "@me" --add-label in-progress
```

## 3. Worktree and branch

The branch is `<n>-<slug>`, slug from the title in kebab-case: issue 11 "[COS] Lexer" becomes `11-lexer`.

```sh
git fetch origin
git worktree add ../<n>-<slug> -b <n>-<slug> origin/main
cd ../<n>-<slug>
```

When the session already runs inside an isolated worktree (the path contains `.claude/worktrees/`), stay there and only create the branch with `git checkout -b <n>-<slug>`, after confirming `git log --oneline -1` matches `git log --oneline -1 origin/main`.

## 4. Read before writing

- The issue's `### Spec clauses`, opened from `Specs/` (local, gitignored), never from memory. Example: `pdftotext Specs/ISO_32000-2_sponsored_EC3.pdf - | grep -nE '^7\.2\.[0-9] '` shows that §7.2 is 7.2.1 General, 7.2.2 Representation, 7.2.3 Character set, 7.2.4 Comments, which is not what older editions say.
- The plan row the issue links (`docs/plan/phase-N-issues.md`), the research report for the area in `docs/research/`, and the current conformance rows in `docs/conformance/`.
- The skill for the task type, listed in `.claude/skills/README.md`.

## 5. Implement and verify

Every PR carries: code with `<remarks>ISO 32000-2 §x.y.z</remarks>` on spec-implementing public members, tests next to the feature, conformance rows (skill `updating-the-conformance-map`), `PublicAPI.Unshipped.txt` entries (the public API analyzer fails the build until they exist), a fuzz target for every parser or codec (skill `adding-a-fuzz-target`), and a benchmark on a hot path (skill `adding-a-benchmark`). Before pushing:

```sh
dotnet build Broadside.Core.slnf -warnaserror
dotnet test Broadside.Core.slnf
dotnet format Broadside.Core.slnf --verify-no-changes
dotnet run -c Release --project tests/Broadside.Fuzz -- --smoke <target> 60                 # if you added a target
dotnet run -c Release --project bench/Broadside.Benchmarks -- --filter '*' --job dry         # if you added a benchmark
```

`Broadside.slnx` also holds the Android and CoreGraphics backends, which need their workloads; build the full solution with `-p:BroadsideBuildPlatformBackends=false` on a machine without them.

## 6. Commit

Conventional Commits with the area as scope, the issue number in the subject, and two trailers at the end: the DCO sign-off `CONTRIBUTING.md` requires and the co-author line. Some sandboxes refuse any command whose text contains `git commit -s` or `.github`, so write the message to a scratch file and pass it with `-F`; the `Signed-off-by` line written by hand is exactly what `-s` appends.

```sh
cat > /tmp/commit-msg.txt <<'MSG'
feat(cos): parse COS objects from bytes (#36)

Tokenizes the character set, delimiters, numbers, names, strings and
comments of ISO 32000-2 clause 7.2 without allocating per token.

Signed-off-by: Your Name <you@example.com>
Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
MSG
git add -A && git -c commit.gpgsign=false commit -F /tmp/commit-msg.txt
```

## 7. Push and open the PR

```sh
git push -u origin <n>-<slug>
gh pr create --repo michaelstonis/Broadside --title "<commit subject>" --body-file /tmp/pr-body.md
```

The body follows `.github/PULL_REQUEST_TEMPLATE.md`: first line `Closes #<n>`; Summary; the Deliverables checklist; Public API changes (what and why, or "none"); Verification with the commands you ran and their output; last line `🤖 Generated with [Claude Code](https://claude.com/claude-code)`. An additive change to `CLAUDE.md` or `CONTEXT.md` states in the body why it is justified.

## 8. Merge policy

- Phase 0 and 1: `gh pr checks <pr> --repo michaelstonis/Broadside --watch`, then `gh pr merge <pr> --repo michaelstonis/Broadside --squash --delete-branch` once green and the checklist is complete.
- Phase 2 onward: `gh pr edit <pr> --repo michaelstonis/Broadside --add-reviewer michaelstonis` and stop. Never merge.

## 9. Rebasing when main moved

`Broadside.slnx` conflicts whenever two PRs add a project to the same folder. Keep both `<Project>` lines.

```sh
git fetch origin && git rebase origin/main
# resolve Broadside.slnx by keeping both lines, then:
git add Broadside.slnx && GIT_EDITOR=true git rebase --continue
dotnet build Broadside.Core.slnf && git push --force-with-lease
```

## 10. Clean up

```sh
gh issue edit <n> --repo michaelstonis/Broadside --remove-label in-progress   # closing via "Closes #n" does not remove labels
git worktree remove ../<n>-<slug> && git branch -D <n>-<slug>                 # from the main checkout, after the merge
```

## Checklist

- [ ] Issue is in the current milestone, unassigned, and every blocker is closed
- [ ] Self-assigned, `in-progress` label added
- [ ] Branch `<n>-<slug>` from `origin/main` in a worktree
- [ ] Spec clauses read from `Specs/`; plan row, research report and conformance rows read
- [ ] Code, tests, conformance rows, `PublicAPI.Unshipped.txt`, fuzz target and benchmark as the issue requires
- [ ] `dotnet build -warnaserror`, `dotnet test`, `dotnet format --verify-no-changes` clean
- [ ] Conventional Commit with `Signed-off-by` and `Co-Authored-By` trailers
- [ ] PR body starts with `Closes #<n>` and ends with the Claude Code footer
- [ ] Phase 0–1: self-merged when green; Phase 2+: owner review requested, not merged
- [ ] `in-progress` removed, worktree and branch deleted
