---
title: "Versioning and support"
---

# Versioning and support

How Broadside's packages are numbered, what each kind of release promises, how long a 1.x release gets fixes, and where the
notes for each release come from.

## One version for every package

Every package built from this repository (`Broadside`, `Broadside.Rendering`, the rendering backends, the font packages,
`Broadside.Layout` and the viewers) carries the same version, taken from a git tag by [MinVer](https://github.com/adamralph/minver).
A tag `v1.2.3` produces packages `1.2.3`. Pushing a `v*` tag runs the release workflow, which builds, tests and packs every
package and publishes them to NuGet.org together, so a set of Broadside packages with the same version always came from the same
commit. A build from a commit without a tag gets a pre-release version above the last tag (`0.1.0-preview.0.<height>`), which is
never published.

## Kinds of release

| Kind | Tag | Example | What it promises |
|---|---|---|---|
| Preview | `v0.N.0-preview.M` | `v0.1.0-preview.10` | Nothing about compatibility: any preview may change or remove public API. Every change is listed in its notes, with the public API diff. |
| Release candidate | `v1.0.0-rc.N` | `v1.0.0-rc.1` | The API intended for 1.0. Only fixes, and changes that a fix forces, land between candidates. |
| Release | `vMAJOR.MINOR.PATCH` | `v1.0.0`, `v1.2.1` | [Semantic Versioning 2.0](https://semver.org): see below. |

Pre-release tags have a `-` suffix; their GitHub releases are marked as pre-releases and NuGet shows their packages as
pre-release.

## Semantic Versioning from 1.0

From 1.0 on, the version says what an upgrade can break:

- **Patch** (`1.2.0` to `1.2.1`): fixes only. No public API is added or removed.
- **Minor** (`1.2.1` to `1.3.0`): new features and new public API, plus fixes. Nothing public is removed or changed incompatibly.
- **Major** (`1.x` to `2.0.0`): the only kind of release that may break compatibility.

There are no breaking changes within 1.x. A breaking change is one that stops code built against an earlier 1.x version from
compiling or running as documented: removing or changing a public type or member (each package's public API is tracked in its
`PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`, and the build fails on an undeclared change), or changing documented
behavior. Reading untrusted files stays lenient as documented; a repair that records a new diagnostic is not a breaking change.

The viewer packages (`Broadside.Viewer` and its `Broadside.Viewer.*` shells) are not part of 1.0: they remain previews on their
own track until they reach their own 1.0, and the compatibility and support promises on this page do not cover them yet.

## 1.x support policy

- **Fixes land in the latest 1.x minor.** A bug fixed in 1.x ships in a patch of the newest minor release (or in the next minor).
  Upgrading within 1.x is the supported way to get fixes, and Semantic Versioning makes it safe.
- **Security fixes are backported to the previous minor for 6 months.** When a new minor release ships, the minor before it
  still receives security fixes, as patch releases, for six months from that date. Older minors receive none.
- **No breaking changes within 1.x**, as above.

How to report a vulnerability, and which versions are supported now, is in
[`SECURITY.md`](https://github.com/michaelstonis/Broadside/blob/main/SECURITY.md).

## Release notes and the changelog

Every release has notes generated from the history, not written by hand:

- Each pull request's title follows [Conventional Commits](https://www.conventionalcommits.org) (`type(scope): summary`, see
  [Contributing](contributing.md)), and that title is what lands on the target branch. A check fails pull requests whose title
  does not follow it.
- `tools/ReleaseNotes` reads the titles merged since the previous `v*` tag and groups them by type (features, fixes, performance,
  documentation and so on), then by scope. Breaking changes come first: titles with a `!` after the type, commits with a
  `BREAKING CHANGE:` footer, and any public API that was removed. The notes end with the public API diff: per package, the API
  entries added and removed, and a link to the full diff.
- The release workflow writes these notes into its job summary on every run, including dry runs, and uses them as the body of
  the GitHub release.
- [`CHANGELOG.md`](https://github.com/michaelstonis/Broadside/blob/main/CHANGELOG.md) accumulates the same notes, newest release
  first.

To see the notes for what is merged but not yet released, run from a clone with its tags:

```sh
dotnet run --project tools/ReleaseNotes
```

## Making a release

1. Create the release branch `release/<version>` from the integration branch and merge `main` into it.
2. Add the release's section to the changelog and commit it:
   `dotnet run --project tools/ReleaseNotes -- --version <version> --changelog CHANGELOG.md --output /dev/null`, then
   `git commit -s -am "chore(release): changelog for <version>"`.
3. Open a pull request into `main` titled `release: v<version>` with a short summary of the highlights. The release notes list
   the pull requests it brings in, not the release pull request itself.
4. After it is merged, the owner tags the merge commit (`git tag -a v<version> -m v<version>`) and pushes the tag. The release
   workflow publishes the packages and creates the GitHub release with the notes.

A dry run of the release workflow (Actions, Release, Run workflow, with `dry_run` checked) builds, tests and packs everything and
shows the notes since the last tag in its summary, without publishing.
