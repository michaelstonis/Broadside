# Getting from the release candidate to 1.0.0

There is no fixed soak period and no formal sign-off. The owner tags `v1.0.0` when the release candidate has had enough real-world use and no release-blocking issue (below) is open. Issue [#276](https://github.com/michaelstonis/Broadside/issues/276) tracks the candidate, and the [gate checklist](v1.0.0-rc.1-gate.md) lists the runs on its commit.

Every package ships at 1.0.0 together, including the viewer packages (`Broadside.Viewer` and its six controls).

## What would hold 1.0.0 back

These issues are a reason to ship another candidate (`v1.0.0-rc.2`, and so on) instead of 1.0.0:

1. **A crash on reading.** Opening, rendering or extracting text from a file, including a damaged one, throws an unhandled exception outside strict mode, hangs, or exceeds the documented resource limits.
2. **Data loss on save.** A save writes a file that does not open, or changes content the caller did not edit.
3. **A security issue** that [`SECURITY.md`](../../SECURITY.md) puts in scope.
4. **A broken install.** A Broadside package fails to restore, build or run in a fresh project on Linux, Windows or macOS.
5. **A public API defect that needs a breaking change.** After 1.0.0 a break needs 2.0, so it is cheaper to fix it in a candidate.

Anything else goes to the Post-v1 milestone or a later 1.0.x release. If it limits what 1.0 does, it is added to the [known limitations](../known-limitations.md).

## How the owner tags v1.0.0

1. Create `release/1.0.0` from the integration branch and merge `main` into it.
2. Add the changelog section and commit it: run `dotnet run --project tools/ReleaseNotes -- --version 1.0.0 --changelog CHANGELOG.md --output /dev/null`, then `git commit -am "chore(release): changelog for 1.0.0"`.
3. Move the package validation baseline to the last candidate (below). Move any `PublicAPI.Unshipped.txt` entries that a fix added into `PublicAPI.Shipped.txt`.
4. Open the pull request into `main` titled `release: v1.0.0`, and run the release workflow's dry run on the branch: `gh workflow run release.yml --ref release/1.0.0 -f dry_run=true`.
5. Merge the pull request, then tag the merge commit and push the tag: `git tag -a v1.0.0 -m v1.0.0` and `git push origin v1.0.0`. The tag push publishes to NuGet.org, so only the owner pushes tags.
6. Run the [install check](v1.0.0-rc.1-notes.md#install-check) with `--version 1.0.0`.

## Moving the package validation baseline

`PackageValidationBaselineVersion` in `src/Directory.Build.props` names the last version on NuGet.org, which is now `0.1.0-preview.10`. The breaks the v1 API freeze made deliberately against that preview are recorded in each affected package's `CompatibilitySuppressions.xml`.

- **In the `release: v1.0.0` pull request**, set the baseline to the last published candidate (`1.0.0-rc.N`). Then delete every `src/*/CompatibilitySuppressions.xml` file. Also remove `ApiCompatPermitUnnecessarySuppressions` from `src/Broadside.Viewer.Maui/Broadside.Viewer.Maui.csproj`.
- **In the first release after 1.0.0**, set the baseline to `1.0.0`. From then on, any break fails `dotnet pack`.
