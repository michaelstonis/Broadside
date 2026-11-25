# General-availability criteria for 1.0.0

What has to be true before the owner tags `v1.0.0`, after the release candidate `v1.0.0-rc.1` is on NuGet.org. Issue [#276](https://github.com/michaelstonis/Broadside/issues/276) is the release-candidate gate; the [gate checklist](v1.0.0-rc.1-gate.md) lists the run that proves each gate on the candidate's commit. The [versioning policy](../../site/articles/versioning.md) says what a release candidate promises: the API intended for 1.0, with only fixes, and changes a fix forces, between candidates.

Every package ships at 1.0.0 together, the viewer packages (`Broadside.Viewer` and its six controls) included.

## Soak period

**14 days (owner to confirm)** from the day `v1.0.0-rc.1` is published on NuGet.org and its install check passes on Linux, Windows and macOS.

- The soak passes when it ends with no open release-blocking issue (below).
- When a release-blocking issue is found, it is fixed on the integration branch, the fix ships as the next candidate (`v1.0.0-rc.2`, and so on) through the same gate, and the soak starts again from that candidate's publication. The owner may shorten the restarted period for a narrow fix and records that decision on #276.
- Issues that are not release-blocking do not stop the soak. They go to the Post-v1 milestone, or into a 1.0.x patch, and those that limit what 1.0 does join the [known limitations](../known-limitations.md).

## What is release-blocking

An issue blocks 1.0.0 when it is one of these, in any package, the viewers included:

1. **A crash or memory corruption on reading.** Opening, walking, rendering or extracting text from a file, damaged or crafted ones included, throws an unhandled exception outside strict mode, hangs, recurses until the stack overflows, exceeds the documented resource limits, or corrupts memory.
2. **Data loss on save.** Saving, incrementally or in full, writes a file that does not open, or loses or changes content the caller did not edit.
3. **A security issue.** Anything [`SECURITY.md`](../../SECURITY.md) puts in scope, reported privately or found by the team.
4. **A broken install on any operating system.** `dotnet add package` of any Broadside package fails to restore, build or run in a fresh project on Linux, Windows or macOS (and, for the platform packages, on the platforms they target).
5. **A public API defect that needs a breaking change to fix.** After 1.0.0 a break needs 2.0, so a misnamed or misshaped public member found during the soak is fixed in the next candidate, not lived with.

Anything else (a rendering difference, a missing feature, a slow page, a documentation error) is not release-blocking.

## How the owner tags v1.0.0

When the soak ends with no open release-blocking issue:

1. Create `release/1.0.0` from the integration branch and merge `main` into it.
2. Add the changelog section and commit it:
   `dotnet run --project tools/ReleaseNotes -- --version 1.0.0 --changelog CHANGELOG.md --output /dev/null`, then
   `git commit -am "chore(release): changelog for 1.0.0"`.
3. Move the package validation baseline to the last candidate (below), and move any `PublicAPI.Unshipped.txt` entries a fix added into `PublicAPI.Shipped.txt`.
4. Open the pull request into `main` titled `release: v1.0.0`; every workflow it triggers must be green. Run the release workflow's dry run on the branch (`gh workflow run release.yml --ref release/1.0.0 -f dry_run=true`) and check its summaries, as the [release checklist](../../.github/workflows/README.md#release-checklist) says.
5. After the owner merges it, the owner tags the merge commit and pushes the tag: `git tag -a v1.0.0 -m v1.0.0`, `git push origin v1.0.0`. The release workflow publishes every package and creates the GitHub release.
6. Run the install check of the [release notes](v1.0.0-rc.1-notes.md#install-check) with `--version 1.0.0` on Linux, Windows and macOS.

Only the owner pushes tags: pushing a `v*` tag publishes to NuGet.org.

## Moving the package validation baseline to 1.0.0

`PackageValidationBaselineVersion` in `src/Directory.Build.props` names the last version on NuGet.org (now `0.1.0-preview.10`). The deliberate breaks of the v1 API freeze against that preview are recorded in each affected package's `CompatibilitySuppressions.xml` (12 files under `src/`).

- **In the `release: v1.0.0` pull request:** set the baseline to the last published candidate (`1.0.0-rc.N`) and delete every `src/*/CompatibilitySuppressions.xml`. The candidate already contains those breaks, so nothing is left for them to excuse. If a fix between candidates forced a break, that is a release-blocking API defect (above), and its suppression is regenerated against the candidate and deleted with the next baseline move.
- **In the first release pull request after 1.0.0 is on NuGet.org** (1.0.1 or 1.1.0): set the baseline to `1.0.0` and delete any suppression file that remains. From then on `dotnet pack` fails on any break against 1.0.0, and no suppression file may be committed: a breaking change needs 2.0.

## Sign-off

The owner signs off these criteria (the soak length in particular) on #276 before `v1.0.0-rc.1` is tagged.
