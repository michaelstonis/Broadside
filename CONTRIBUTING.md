# Contributing to Broadside

Thank you for considering a contribution. This document describes how work is organized, what a pull request needs, and how it gets merged. Humans and AI agents follow the same flow; nothing here is different for either.

Before starting, read [CLAUDE.md](CLAUDE.md) (the rules), [CONTEXT.md](CONTEXT.md) (the vocabulary, use it exactly), [docs/adr](docs/adr) (the decisions, do not relitigate them) and [docs/plan/README.md](docs/plan/README.md) (the phases and tracks).

## How work is organized

- **Issues.** Every unit of work is a GitHub issue created from the [task template](.github/ISSUE_TEMPLATE/task.yml). It names the phase and track, the scope, the spec clauses it implements, the acceptance criteria and what blocks it. Bugs use the bug report template.
- **Labels.** Each issue carries a `phase:N` label, a `track:*` label where a track applies, one or more `area:*` labels, and `task` or `bug`. `in-progress` means someone has claimed it.
- **Milestones.** One milestone per phase, Phase 0 through Phase 7, plus `Post-v1` for earmarked work. The current milestone is the one with open issues and the lowest number. The status of each phase is in [docs/plan/README.md](docs/plan/README.md#status).
- **Project board.** Issues are tracked on the [Broadside project board](https://github.com/users/michaelstonis/projects/1). The board reflects issue state; update the issue, not the board.
- **Plan.** The issue lists per phase live in [docs/plan](docs/plan) and link to the GitHub issues.

## Picking up work

1. Find an open issue in the current milestone with no assignee whose "blocked by" issues are closed.
2. Self-assign it and add the `in-progress` label.
3. Create a branch (a git worktree is recommended so parallel work does not collide) named `<issue-number>-<slug>`, for example `42-cff-charstrings`, from `main`.
4. Read the spec clauses the issue lists before writing code. Cite them from `Specs/`, never from memory, when the two disagree.

## Commits

Commits and pull request titles follow [Conventional Commits](https://www.conventionalcommits.org): `type(scope): summary`, where `type` is one of `feat`, `fix`, `perf`, `refactor`, `docs`, `test`, `bench`, `build`, `ci`, `chore`, `style`, `revert` or `release` (release pull requests only), and `scope` is the area in lower case (`cos`, `filters`, `fonts`, `rendering`, and so on). A breaking change puts `!` before the colon (`refactor(api)!: rename PdfDocument.Load to Open`) or adds a `BREAKING CHANGE: <what breaks>` footer to the commit body. Examples:

```
feat(fonts): parse CFF charstrings
fix(cos): recover startxref when the offset is past end of file
docs: repository hygiene files (#9)
```

Keep commits focused. One PR per issue.

The **pull request title** is what lands on the target branch (the squash commit's title, or the body of GitHub's merge commit), and it is what the release notes list, so the **conventional title** check (`.github/workflows/pr-checks.yml`) fails a pull request whose title does not follow the format above; edit the title and the check runs again. Bot pull requests are exempt. Release notes are generated from these titles by `tools/ReleaseNotes`, grouped by type and scope, with breaking changes first; see the [versioning policy](https://michaelstonis.github.io/Broadside/articles/versioning/).

## Pull requests

Open a PR from your branch against `main` using the [pull request template](.github/PULL_REQUEST_TEMPLATE.md). The PR:

- starts its description with `Closes #<issue>`, so merging closes the issue;
- updates the [conformance map](docs/conformance) rows for every clause the change touches, in the same PR as the code; a row is not `done` without a linked test;
- updates `PublicAPI.Unshipped.txt` in every package whose public surface changed, says why in the description, and follows the [API review checklist](site/articles/api-review.md) (the tests in `tests/Broadside.ApiGuard.Tests` enforce its mechanical rules over every shipping package);
- adds tests: unit tests next to the feature, corpus tests driven by `tests/Corpus/`, rendering snapshots through Verify, fuzz targets for every parser and codec;
- carries `<remarks>ISO 32000-2 §x.y.z</remarks>` (or the relevant TS or reference) in the XML docs of every public type and member that implements a spec concept;
- adds any new diagnostic code to the public catalog in `src/Broadside/Diagnostics/`, in the class of its area (`FileStructureDiagnostics`, `FontDiagnostics`, `ImageDiagnostics`, ...; `DiagnosticCatalog` lists them all), with its default severity, clause and description, accepts the catalog snapshot of `DiagnosticCatalogTests` and regenerates the [diagnostic codes page](site/articles/diagnostics.md) (`dotnet run --project tools/DiagnosticCatalogPage -- site/articles/diagnostics.md --write`); a code is declared nowhere else, and `DiagnosticCatalogArchitectureTests` fails if one is. The page's stability policy applies from 1.0: a code is never renamed or reused, and a default severity change is a documented behavior change;
- includes a BenchmarkDotNet benchmark with a `MemoryDiagnoser` assertion when it touches a hot path (lexer, content interpreter, rasterizer, codecs);
- must have green CI before it can be merged.

## Review and merge policy

- **Phase 0 and Phase 1:** the author self-merges once CI is green and the checklist in the PR template is complete.
- **Phase 2 onward:** every PR needs a review from the repository owner before it is merged. Request it when CI is green.

Squash merges are the default; the squashed commit message follows Conventional Commits.

## Coding conventions

The short version: C# latest, file-scoped namespaces, `sealed` unless designed for inheritance, nullable enabled, warnings are errors, `Pdf` prefix for document-model types and `Cos` prefix for COS types, no per-token or per-glyph allocation on hot paths. The full list is in [CLAUDE.md](CLAUDE.md) under "Code conventions" and is enforced at build time by [.editorconfig](.editorconfig) (`EnforceCodeStyleInBuild` is on). Tests use xUnit v3.

If you think a convention or decision is wrong, say so in the PR or open an issue; do not silently work around it. Changing a term in `CONTEXT.md` or reversing an architecture decision requires stating why the existing decision was wrong.

## Humans and agents

AI agents working in this repository follow exactly the process above: the same issues, the same branch naming, the same PR checklist, the same review policy. A PR is judged on its content, not on who or what produced it. Agent-specific guidance lives in [CLAUDE.md](CLAUDE.md) and `.claude/skills/`.

## Questions

Use [GitHub Discussions](https://github.com/michaelstonis/Broadside/discussions) for questions and design conversations. Issues are for defined units of work and for bugs.
