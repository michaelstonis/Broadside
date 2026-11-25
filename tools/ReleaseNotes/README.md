# ReleaseNotes

Writes the release notes for the commits between the previous `v*` tag and a ref, from their Conventional Commit titles (issue #264). `release.yml` runs it on every run and uses its output as the GitHub release body; the release branch runs it with `--changelog` to add the same section to `CHANGELOG.md`. The policy it serves is `site/articles/versioning.md`.

```sh
dotnet run --project tools/ReleaseNotes                       # notes for HEAD since the last tag, to standard output
dotnet run --project tools/ReleaseNotes -- --to v0.1.0-preview.10
dotnet run --project tools/ReleaseNotes -- --version 0.1.0-preview.11 --changelog CHANGELOG.md --output /dev/null
```

| Option | Default |
|---|---|
| `--root <dir>` | the current directory |
| `--from <ref>` | the highest `v*` tag (SemVer order) below the version tagged on `--to`, reachable or not |
| `--to <ref>` | `HEAD` |
| `--version <version>` | the `v*` tag on `--to` without its `v`, else `Unreleased` |
| `--repository <owner/name>` | `michaelstonis/Broadside` (links) |
| `--output <file>` | standard output |
| `--changelog <file>` | none; adds the section, replacing one for the same version or `Unreleased` |

## What it reads

The first-parent history from `--to`, leaving out everything `--from` reaches:

- A commit with a Conventional title (`type(scope)!: summary`) is one entry. `chore(release)`, `release:` and `merge:` commits are left out.
- A merge commit whose pull request title (GitHub writes it as the merge commit's body, under `Merge pull request #N from ...`) is Conventional is one entry, linked to the pull request. It is breaking if the title has `!` or any commit of the merged branch has `!` or a `BREAKING CHANGE:` footer.
- Any other merge (`merge: #N ...`, a `release:` pull request, `Merge main into ...`) is replaced by the commits it brought in, read the same way, so a release pull request into `main` lists the pull requests merged into the integration branch.
- A commit whose title is not Conventional goes under "Other changes".

Entries are grouped by type (Features, Fixes, Performance, Refactoring, Documentation, Tests, Benchmarks, Build and CI, Reverts, Chores, Other changes), then sorted by scope, oldest first within a scope. Breaking changes are listed first, with the footer's text when there is one. `#N` references become links.

The public API diff compares every package's `src/<package>/PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` at both refs as sets (unshipped `*REMOVED*` entries removed): a table of added and removed counts, the entries themselves (up to 200 per package and direction) and a link to the full diff. Removed entries are also called out as breaking changes.

Tests: `tests/ReleaseNotes.Tests` builds small git repositories (tags, pull request merges, release merges, API files) and checks the notes.
