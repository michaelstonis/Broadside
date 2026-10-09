# Workflows

## `ci.yml`: every pull request and every push to `main`

| Job | Runs on | What it does |
|---|---|---|
| `build-test` | ubuntu, windows, macos | `dotnet build Broadside.slnx -c Release -warnaserror`, then `dotnet test Broadside.Core.slnf` with TRX reports uploaded as `test-results-<os>`. On Linux and Windows the CoreGraphics and Android backends drop out through the `BroadsideBuildPlatformBackends` gate; macOS installs the `macos ios maccatalyst android` workloads first and builds them. Linux also packs the core packages and uploads them as `packages`. |
| `api-surface` | ubuntu (PRs only) | Writes the diff of every `src/**/PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` against the base branch to the job summary. Never fails; the PublicApiAnalyzers already fail the build on an undeclared API change. |
| `conformance-map` | ubuntu | `dotnet run --project tools/ConformanceCheck -- --root . --summary` into the job summary; fails on a `done` row without a test. Its steps are skipped, with a note in the summary, until `tools/ConformanceCheck` lands (issue #7). |
| `fuzz-smoke` | ubuntu | `--list`s the fuzz targets and runs `--smoke <target> 60` for each; uploads `artifacts/fuzz/**` as `fuzz-findings` on failure. |
| `bench-dry` | ubuntu | `dotnet run --project bench/Broadside.Benchmarks -- --filter '*' --job dry`: one iteration of every benchmark, no measurement. |

A newer push to the same PR or branch cancels the older run. NuGet packages are cached per OS, keyed on `Directory.Packages.props` and every `.csproj`. `DiffEngine_Disabled=true` keeps Verify from launching a diff tool.

## `release.yml`: tags `v*` and the "Run workflow" button

1. `build-linux`: builds, tests and packs the core packages (`Broadside.Core.slnf`).
2. `build-macos`: installs the workloads, builds and tests the full solution, packs it, and uploads only `Broadside.Rendering.CoreGraphics` and `Broadside.Rendering.Android`, so that every package comes from exactly one build.
3. `publish`: merges the two sets into the `packages` artifact. On a `v*` tag that is not a dry run it logs in to NuGet.org with trusted publishing, runs `dotnet nuget push --skip-duplicate`, and creates a GitHub release with generated notes and the packages attached (marked pre-release when the tag has a `-` suffix, e.g. `v0.1.0-preview.1`).

MinVer derives the package version from the tag (`v1.2.3` → `1.2.3`); a dispatch run on a branch produces `0.1.0-preview.0.<height>`.

### Release dry run

Actions tab → **Release** → **Run workflow** → pick the branch or tag → leave **dry_run** checked → **Run workflow**. Or from a terminal:

```sh
gh workflow run release.yml --ref main -f dry_run=true
gh run watch
```

The dry run builds, tests and packs on both runners and uploads the merged `packages` artifact, but pushes nothing and creates no release. It needs no secrets. Setting `dry_run` to false on a tag ref performs a real release; on a branch ref it still publishes nothing, because the push and release steps also require a `v*` tag.

### Trusted publishing (one-time setup by the owner)

The repository holds no NuGet API key. `NuGet/login` exchanges the job's GitHub OIDC token for a short-lived API key, which only works once nuget.org knows the workflow. Before the first real release:

1. Sign in to nuget.org as `michaelstonis` → profile → **Trusted Publishing** → **Add**.
2. Repository owner `michaelstonis`, repository `Broadside`, workflow file `release.yml`, environment empty.
3. Push a tag: `git tag v0.1.0-preview.1 && git push origin v0.1.0-preview.1`.

Until that registration exists, only dry runs succeed; the login step of a real release fails with an authorization error and nothing is pushed.
